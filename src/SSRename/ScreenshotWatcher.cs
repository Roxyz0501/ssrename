using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SSRename
{
    internal sealed class ScreenshotWatcher : IDisposable
    {
        private sealed class WorkItem
        {
            public string SourcePath;
            public long Generation;
            public PluginSettings Settings;
            public Func<ScreenshotContext> ContextProvider;
            public CancellationTokenSource Cancellation;
        }

        private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".bmp" };
        private const int MaximumConcurrentJobs = 4;
        private readonly object sync = new object();
        private readonly object lifecycleSync = new object();
        private readonly HashSet<string> processing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<string, TimeSpan, CancellationToken, bool> readyWaiter;
        private FileSystemWatcher watcher;
        private PluginSettings settings;
        private Func<ScreenshotContext> contextProvider;
        private CancellationTokenSource generationCancellation;
        private long generation;
        private int activeJobs;
        private bool accepting;
        private bool disposed;

        public event Action<string, bool> StatusChanged;
        public event Action<string, string> FileRenamed;

        public ScreenshotWatcher()
            : this(WaitUntilReady)
        {
        }

        internal ScreenshotWatcher(Func<string, TimeSpan, CancellationToken, bool> readyWaiter)
        {
            this.readyWaiter = readyWaiter ?? throw new ArgumentNullException("readyWaiter");
        }

        internal int ActiveJobCount
        {
            get { lock (sync) return activeJobs; }
        }

        internal void NotifyCreatedForTests(string sourcePath)
        {
            FileSystemWatcher active;
            lock (sync) active = watcher;
            if (active != null)
                OnCreated(active, new FileSystemEventArgs(WatcherChangeTypes.Created, Path.GetDirectoryName(sourcePath), Path.GetFileName(sourcePath)));
        }

        public void Restart(PluginSettings newSettings, Func<ScreenshotContext> newContextProvider)
        {
            if (newSettings == null) throw new ArgumentNullException("newSettings");
            lock (lifecycleSync)
            {
                if (disposed) throw new ObjectDisposedException("ScreenshotWatcher");
                StopCore();

                long currentGeneration;
                PluginSettings snapshot = newSettings.Clone();
                lock (sync)
                {
                    currentGeneration = ++generation;
                    settings = snapshot;
                    contextProvider = newContextProvider;
                    generationCancellation = new CancellationTokenSource();
                }

                if (!snapshot.RenameEnabled)
                {
                    Report(currentGeneration, Localization.T(snapshot.Language, "watch_paused"), false, false);
                    return;
                }
                if (string.IsNullOrWhiteSpace(snapshot.ScreenshotFolder) || !Directory.Exists(snapshot.ScreenshotFolder))
                {
                    Report(currentGeneration, Localization.T(snapshot.Language, "watch_folder_missing"), true, false);
                    return;
                }
                if (string.IsNullOrWhiteSpace(snapshot.EffectiveDestinationFolder))
                {
                    Report(currentGeneration, Localization.T(snapshot.Language, "destination_required"), true, false);
                    return;
                }

                FileSystemWatcher created = null;
                try
                {
                    Directory.CreateDirectory(snapshot.EffectiveDestinationFolder);
                    created = new FileSystemWatcher(snapshot.ScreenshotFolder)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
                    };
                    created.Created += OnCreated;
                    created.Error += OnWatcherError;
                    lock (sync)
                    {
                        if (currentGeneration != generation) return;
                        watcher = created;
                        accepting = true;
                    }
                    created.EnableRaisingEvents = true;
                    Report(currentGeneration, Localization.T(snapshot.Language, "watching", snapshot.ScreenshotFolder), false, true);
                }
                catch (Exception ex)
                {
                    if (created != null)
                    {
                        created.EnableRaisingEvents = false;
                        created.Created -= OnCreated;
                        created.Error -= OnWatcherError;
                        created.Dispose();
                    }
                    StopCore();
                    Report(currentGeneration, Localization.T(snapshot.Language, "watch_start_failed", ex.GetBaseException().Message), true, false);
                }
            }
        }

        public void UpdateSettings(PluginSettings newSettings)
        {
            if (newSettings == null) throw new ArgumentNullException("newSettings");
            lock (sync)
            {
                if (disposed || settings == null) return;
                settings = newSettings.Clone();
            }
        }

        public void Stop()
        {
            lock (lifecycleSync)
                StopCore();
        }

        private void StopCore()
        {
            FileSystemWatcher oldWatcher;
            CancellationTokenSource oldCancellation;
            lock (sync)
            {
                accepting = false;
                oldWatcher = watcher;
                watcher = null;
                oldCancellation = generationCancellation;
                if (oldCancellation != null)
                    oldCancellation.Cancel();
            }

            if (oldWatcher != null)
            {
                oldWatcher.EnableRaisingEvents = false;
                oldWatcher.Created -= OnCreated;
                oldWatcher.Error -= OnWatcherError;
                oldWatcher.Dispose();
            }

            lock (sync)
            {
                while (activeJobs > 0)
                    Monitor.Wait(sync);
                if (ReferenceEquals(generationCancellation, oldCancellation))
                {
                    generationCancellation = null;
                    settings = null;
                    contextProvider = null;
                    processing.Clear();
                }
            }
            if (oldCancellation != null)
                oldCancellation.Dispose();
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (!Extensions.Contains(Path.GetExtension(e.FullPath)))
                return;

            WorkItem work = null;
            string rejection = null;
            lock (sync)
            {
                if (disposed || !accepting || !ReferenceEquals(sender, watcher) || settings == null || !settings.RenameEnabled || generationCancellation == null)
                    return;
                if (processing.Contains(e.FullPath)) return;
                if (activeJobs >= MaximumConcurrentJobs)
                    rejection = Localization.T(settings.Language, "watch_queue_full");
                else
                {
                    processing.Add(e.FullPath);
                    activeJobs++;
                    work = new WorkItem
                    {
                        SourcePath = e.FullPath,
                        Generation = generation,
                        Settings = settings.Clone(),
                        ContextProvider = contextProvider,
                        Cancellation = generationCancellation
                    };
                }
            }

            if (work == null)
            {
                if (rejection != null) ReportForCurrent(rejection, true);
                return;
            }

            if (!ThreadPool.QueueUserWorkItem(ProcessFile, work))
            {
                CompleteWork(work);
                Report(work.Generation, Localization.T(work.Settings.Language, "watch_queue_full"), true, true);
            }
        }

        private void ProcessFile(object state)
        {
            var work = (WorkItem)state;
            try
            {
                var token = work.Cancellation.Token;
                if (!readyWaiter(work.SourcePath, TimeSpan.FromSeconds(12), token))
                {
                    token.ThrowIfCancellationRequested();
                    throw new IOException(Localization.T(work.Settings.Language, "write_timeout"));
                }
                token.ThrowIfCancellationRequested();
                var context = work.ContextProvider == null ? null : work.ContextProvider();
                token.ThrowIfCancellationRequested();
                if (context == null || string.IsNullOrWhiteSpace(context.CharacterName) || string.IsNullOrWhiteSpace(context.WorldName))
                    throw new InvalidOperationException(Localization.T(work.Settings.Language, "context_missing"));

                var fileName = FileNameService.BuildFileName(work.SourcePath, work.Settings.FileNameTemplate, context, work.Settings.Language);
                var destinationFolder = work.Settings.EffectiveDestinationFolder;
                if (string.IsNullOrWhiteSpace(destinationFolder))
                    throw new InvalidOperationException(Localization.T(work.Settings.Language, "destination_required"));
                var destination = FileNameService.FindAvailablePath(destinationFolder, fileName, work.SourcePath, work.Settings.Language);
                var samePath = string.Equals(Path.GetFullPath(work.SourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);

                lock (sync)
                {
                    if (!IsCurrentWorkLocked(work) || token.IsCancellationRequested)
                        return;
                    Directory.CreateDirectory(destinationFolder);
                    if (!samePath)
                        File.Move(work.SourcePath, destination);
                }

                if (!IsCurrentWork(work)) return;
                if (FileRenamed != null && !samePath)
                {
                    try { FileRenamed(work.SourcePath, destination); }
                    catch { /* UI observers must not turn a successful move into a reported processing failure. */ }
                }
                Report(work.Generation, Localization.T(work.Settings.Language, "rename_done", Path.GetFileName(destination)), false, true);
            }
            catch (OperationCanceledException)
            {
                // A stopped generation leaves the original screenshot untouched.
            }
            catch (Exception ex)
            {
                if (!work.Cancellation.IsCancellationRequested)
                    Report(work.Generation, Localization.T(work.Settings.Language, "rename_failed", Path.GetFileName(work.SourcePath), ex.GetBaseException().Message), true, true);
            }
            finally
            {
                CompleteWork(work);
            }
        }

        private bool IsCurrentWork(WorkItem work)
        {
            lock (sync) return IsCurrentWorkLocked(work);
        }

        private bool IsCurrentWorkLocked(WorkItem work)
        {
            return accepting && !disposed && generation == work.Generation &&
                ReferenceEquals(generationCancellation, work.Cancellation) && !work.Cancellation.IsCancellationRequested;
        }

        private void CompleteWork(WorkItem work)
        {
            lock (sync)
            {
                processing.Remove(work.SourcePath);
                activeJobs--;
                Monitor.PulseAll(sync);
            }
        }

        private static bool WaitUntilReady(string path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(path))
                    {
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                            if (stream.Length > 0) return true;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (cancellationToken.WaitHandle.WaitOne(150))
                    cancellationToken.ThrowIfCancellationRequested();
            }
            return false;
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            PluginSettings snapshot;
            long currentGeneration;
            lock (sync)
            {
                if (!accepting || disposed || !ReferenceEquals(sender, watcher) || settings == null) return;
                snapshot = settings.Clone();
                currentGeneration = generation;
            }
            Report(currentGeneration, Localization.T(snapshot.Language, "watch_error", e.GetException().Message), true, true);
        }

        private void ReportForCurrent(string message, bool isError)
        {
            long currentGeneration;
            lock (sync) currentGeneration = generation;
            Report(currentGeneration, message, isError, true);
        }

        private void Report(long reportGeneration, string message, bool isError, bool requireAccepting)
        {
            Action<string, bool> handler;
            lock (sync)
            {
                if (disposed || reportGeneration != generation || (requireAccepting && !accepting)) return;
                handler = StatusChanged;
            }
            if (handler != null)
            {
                try { handler(message, isError); }
                catch { /* Watcher lifecycle is independent from UI subscriber failures. */ }
            }
        }

        public void Dispose()
        {
            lock (lifecycleSync)
            {
                if (disposed) return;
                StopCore();
                lock (sync) disposed = true;
            }
        }
    }
}
