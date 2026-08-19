using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SSRename
{
    internal sealed class ScreenshotWatcher : IDisposable
    {
        private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".bmp" };
        private readonly object sync = new object();
        private readonly HashSet<string> processing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private FileSystemWatcher watcher;
        private PluginSettings settings;
        private Func<ScreenshotContext> contextProvider;

        public event Action<string, bool> StatusChanged;
        public event Action<string, string> FileRenamed;

        public void Restart(PluginSettings newSettings, Func<ScreenshotContext> newContextProvider)
        {
            Stop();
            settings = newSettings;
            contextProvider = newContextProvider;
            if (!settings.RenameEnabled)
            {
                Report(Localization.T(settings.Language, "watch_paused"), false);
                return;
            }
            if (string.IsNullOrWhiteSpace(settings.ScreenshotFolder) || !Directory.Exists(settings.ScreenshotFolder))
            {
                Report(Localization.T(settings.Language, "watch_folder_missing"), true);
                return;
            }
            var destination = settings.EffectiveDestinationFolder;
            if (string.IsNullOrWhiteSpace(destination))
            {
                Report(Localization.T(settings.Language, "destination_required"), true);
                return;
            }
            try
            {
                Directory.CreateDirectory(destination);
                watcher = new FileSystemWatcher(settings.ScreenshotFolder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
                };
                watcher.Created += OnCreated;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;
                Report(Localization.T(settings.Language, "watching", settings.ScreenshotFolder), false);
            }
            catch (Exception ex)
            {
                Stop();
                Report(Localization.T(settings.Language, "watch_start_failed", ex.GetBaseException().Message), true);
            }
        }

        public void Stop()
        {
            if (watcher == null)
                return;
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnCreated;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
            watcher = null;
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (!Extensions.Contains(Path.GetExtension(e.FullPath)))
                return;
            lock (sync)
            {
                if (!processing.Add(e.FullPath))
                    return;
            }
            ThreadPool.QueueUserWorkItem(_ => ProcessFile(e.FullPath));
        }

        private void ProcessFile(string sourcePath)
        {
            try
            {
                if (!WaitUntilReady(sourcePath, TimeSpan.FromSeconds(12)))
                    throw new IOException(Localization.T(settings.Language, "write_timeout"));
                var context = contextProvider == null ? null : contextProvider();
                if (context == null || string.IsNullOrWhiteSpace(context.CharacterName) || string.IsNullOrWhiteSpace(context.WorldName))
                    throw new InvalidOperationException(Localization.T(settings.Language, "context_missing"));
                var fileName = FileNameService.BuildFileName(sourcePath, settings.FileNameTemplate, context, settings.Language);
                var destination = FileNameService.FindAvailablePath(settings.EffectiveDestinationFolder, fileName, sourcePath, settings.Language);
                if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    File.Move(sourcePath, destination);
                if (FileRenamed != null)
                    FileRenamed(sourcePath, destination);
                Report(Localization.T(settings.Language, "rename_done", Path.GetFileName(destination)), false);
            }
            catch (Exception ex)
            {
                Report(Localization.T(settings.Language, "rename_failed", Path.GetFileName(sourcePath), ex.GetBaseException().Message), true);
            }
            finally
            {
                lock (sync) processing.Remove(sourcePath);
            }
        }

        private static bool WaitUntilReady(string path, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
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
                Thread.Sleep(150);
            }
            return false;
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Report(Localization.T(settings.Language, "watch_error", e.GetException().Message), true);
        }

        private void Report(string message, bool isError)
        {
            if (StatusChanged != null)
                StatusChanged(message, isError);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
