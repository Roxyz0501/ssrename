using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SSRename
{
    public sealed class SSRenamePlugin : IActPluginV1
    {
        private Label actStatus;
        private SettingsControl control;
        private PluginSettings settings;
        private readonly object settingsSync = new object();
        private readonly object updateTaskSync = new object();
        private readonly HashSet<Task> updateTasks = new HashSet<Task>();
        private GameDataService gameData;
        private FfxivContextProvider contextProvider;
        private ScreenshotWatcher screenshotWatcher;
        private System.Windows.Forms.Timer contextTimer;
        private TabPage pluginPage;
        private string settingsPath;
        private volatile ScreenshotContext latestContext;
        private GitHubUpdateClient updateClient;
        private UpdatePackageService updatePackageService;
        private CancellationTokenSource updateCancellation;
        private SettingsSaveCoordinator saveCoordinator;
        private SemVersion currentVersion;
        private volatile bool updateAvailable;
        private string lastSettingsSaveError;
        private int shuttingDown;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            actStatus = pluginStatusText;
            pluginPage = pluginScreenSpace;
            settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker", "Config", "SSRename.config.xml");
            settings = PluginSettings.Load(settingsPath);
            var persistedSettings = settings.Clone();
            if (settings.LanguageWasMissing) settings.Language = Localization.DetectOsLanguage();
            else settings.Language = Localization.Normalize(settings.Language);
            saveCoordinator = SettingsSaveCoordinator.ForFile(settingsPath, persistedSettings, SnapshotSettings);
            saveCoordinator.SaveFailed += OnSettingsSaveFailed;
            saveCoordinator.Start();

            pluginScreenSpace.Text = Localization.T(settings.Language, "title");
            gameData = new GameDataService(settings.Language);
            contextProvider = new FfxivContextProvider(gameData);
            control = new SettingsControl(settings);
            control.SettingsChanged += OnSettingsChanged;
            control.WatchFolderSettingsCommitted += OnWatchFolderSettingsCommitted;
            control.CheckUpdatesRequested += OnCheckUpdatesRequested;
            control.InstallUpdateRequested += OnInstallUpdateRequested;
            control.SkipUpdateRequested += OnSkipUpdateRequested;
            pluginScreenSpace.Controls.Add(control);

            updateClient = new GitHubUpdateClient();
            updatePackageService = new UpdatePackageService();
            updateCancellation = new CancellationTokenSource();
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            SemVersion.TryParse(assemblyVersion.Major + "." + assemblyVersion.Minor + "." + assemblyVersion.Build, out currentVersion);

            screenshotWatcher = new ScreenshotWatcher();
            screenshotWatcher.StatusChanged += OnWatcherStatus;
            screenshotWatcher.FileRenamed += OnFileRenamed;
            ActGlobals.oFormActMain.OnLogLineRead += OnLogLineRead;
            contextTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            contextTimer.Tick += OnContextTick;
            OnContextTick(null, EventArgs.Empty);
            contextTimer.Start();
            RestartWatcher();
            actStatus.Text = "SSRename: " + Localization.T(settings.Language, "started");
            if (settings.CheckUpdatesOnStartup)
                StartUpdateTask(delegate { return CheckForUpdatesAsync(false); });
        }

        public void DeInitPlugin()
        {
            Interlocked.Exchange(ref shuttingDown, 1);
            if (control != null)
            {
                control.DisableBackgroundUi();
                control.SettingsChanged -= OnSettingsChanged;
                control.WatchFolderSettingsCommitted -= OnWatchFolderSettingsCommitted;
                control.CheckUpdatesRequested -= OnCheckUpdatesRequested;
                control.InstallUpdateRequested -= OnInstallUpdateRequested;
                control.SkipUpdateRequested -= OnSkipUpdateRequested;
            }
            if (contextTimer != null)
            {
                contextTimer.Stop();
                contextTimer.Tick -= OnContextTick;
                contextTimer.Dispose();
                contextTimer = null;
            }
            if (ActGlobals.oFormActMain != null)
                ActGlobals.oFormActMain.OnLogLineRead -= OnLogLineRead;
            if (screenshotWatcher != null)
            {
                screenshotWatcher.StatusChanged -= OnWatcherStatus;
                screenshotWatcher.FileRenamed -= OnFileRenamed;
                screenshotWatcher.Dispose();
                screenshotWatcher = null;
            }

            Task[] pending;
            lock (updateTaskSync)
            {
                if (updateCancellation != null) updateCancellation.Cancel();
                pending = new Task[updateTasks.Count];
                updateTasks.CopyTo(pending);
            }
            try { Task.WhenAll(pending).GetAwaiter().GetResult(); }
            catch (Exception) { /* Each task reports its own failure and shutdown must continue. */ }
            lock (updateTaskSync)
            {
                if (updateCancellation != null) { updateCancellation.Dispose(); updateCancellation = null; }
            }
            if (updateClient != null) { updateClient.Dispose(); updateClient = null; }
            if (saveCoordinator != null)
            {
                var saved = saveCoordinator.StopAndFlush();
                saveCoordinator.SaveFailed -= OnSettingsSaveFailed;
                saveCoordinator.Dispose();
                saveCoordinator = null;
                if (actStatus != null)
                    actStatus.Text = "SSRename: " + (saved
                        ? Localization.T(settings == null ? Localization.English : settings.Language, "stopped")
                        : Localization.T(settings == null ? Localization.English : settings.Language, "settings_save_failed", lastSettingsSaveError ?? "final save failed"));
            }
            else if (actStatus != null)
                actStatus.Text = "SSRename: " + Localization.T(settings == null ? Localization.English : settings.Language, "stopped");
        }

        private PluginSettings SnapshotSettings()
        {
            lock (settingsSync) return settings == null ? null : settings.Clone();
        }

        private void OnSettingsSaveFailed(Exception exception)
        {
            Interlocked.Exchange(ref lastSettingsSaveError, exception.GetBaseException().Message);
            if (Volatile.Read(ref shuttingDown) != 0) return;
            PostUi(delegate
            {
                var language = SnapshotSettings();
                if (actStatus != null && language != null)
                    actStatus.Text = "SSRename: " + Localization.T(language.Language, "settings_save_failed", exception.GetBaseException().Message);
            });
        }

        private void OnContextTick(object sender, EventArgs e)
        {
            if (Volatile.Read(ref shuttingDown) != 0 || contextProvider == null) return;
            var snapshot = SnapshotSettings();
            var status = contextProvider.Read(snapshot.Language);
            latestContext = status.Context == null ? null : status.Context.Clone();
            if (control != null) control.SetContext(status);
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            if (Volatile.Read(ref shuttingDown) != 0 || control == null) return;
            PluginSettings previous;
            PluginSettings current;
            lock (settingsSync)
            {
                previous = settings.Clone();
                control.ApplyTo(settings);
                current = settings.Clone();
            }
            if (pluginPage != null) pluginPage.Text = Localization.T(current.Language, "title");
            if (previous.RenameEnabled != current.RenameEnabled)
                RestartWatcher();
            else
                screenshotWatcher.UpdateSettings(current);
        }

        private void OnWatchFolderSettingsCommitted(object sender, EventArgs e)
        {
            if (Volatile.Read(ref shuttingDown) == 0) RestartWatcher();
        }

        private void RestartWatcher()
        {
            if (Volatile.Read(ref shuttingDown) != 0 || screenshotWatcher == null) return;
            var snapshot = SnapshotSettings();
            screenshotWatcher.Restart(snapshot, delegate
            {
                var value = latestContext;
                return value == null ? null : value.Clone();
            });
        }

        private void OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            if (Volatile.Read(ref shuttingDown) != 0 || isImport || logInfo == null || logInfo.detectedType != 1 || string.IsNullOrWhiteSpace(logInfo.originalLogLine)) return;
            var fields = logInfo.originalLogLine.Split('|');
            if (fields.Length > 3 && contextProvider != null) contextProvider.SetZoneName(fields[3]);
        }

        private void OnWatcherStatus(string message, bool isError)
        {
            if (Volatile.Read(ref shuttingDown) != 0) return;
            var snapshot = SnapshotSettings();
            if (control != null) control.SetWatcherStatus(message, isError);
            PostUi(delegate
            {
                if (actStatus != null)
                    actStatus.Text = "SSRename: " + message + (updateAvailable ? "  |  " + Localization.T(snapshot.Language, "update_available") : string.Empty);
            });
        }

        private void OnFileRenamed(string oldPath, string newPath)
        {
            if (Volatile.Read(ref shuttingDown) == 0 && control != null) control.AddHistory(oldPath, newPath);
        }

        private void PostUi(Action action)
        {
            if (Volatile.Read(ref shuttingDown) != 0 || actStatus == null || actStatus.IsDisposed) return;
            try
            {
                if (actStatus.InvokeRequired)
                    actStatus.BeginInvoke((Action)delegate { if (Volatile.Read(ref shuttingDown) == 0 && !actStatus.IsDisposed) action(); });
                else if (Volatile.Read(ref shuttingDown) == 0) action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void OnCheckUpdatesRequested(object sender, EventArgs e)
        {
            StartUpdateTask(delegate { return CheckForUpdatesAsync(true); });
        }

        private void StartUpdateTask(Func<Task> operation)
        {
            lock (updateTaskSync)
            {
                if (Volatile.Read(ref shuttingDown) != 0 || updateCancellation == null) return;
                var task = Task.Run(operation);
                updateTasks.Add(task);
                task.ContinueWith(completed =>
                {
                    lock (updateTaskSync) updateTasks.Remove(completed);
                    if (completed.IsFaulted) { var ignored = completed.Exception; }
                }, TaskScheduler.Default);
            }
        }

        private async Task CheckForUpdatesAsync(bool manual)
        {
            var client = updateClient;
            var cancellation = updateCancellation;
            if (client == null || cancellation == null || currentVersion == null) return;
            var initial = new UpdateUiState { Checking = true, CurrentVersion = currentVersion.ToString() };
            PostUi(delegate { if (control != null) control.SetUpdateState(initial); });
            var snapshot = SnapshotSettings();
            UpdateCheckResult result;
            try { result = await client.CheckAsync(currentVersion, manual ? null : snapshot.SkippedVersion, cancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            if (Volatile.Read(ref shuttingDown) != 0 || cancellation.IsCancellationRequested) return;
            var checkedAt = result.CheckedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            lock (settingsSync) settings.LastUpdateCheckUtc = checkedAt;
            snapshot = SnapshotSettings();
            var state = new UpdateUiState
            {
                CurrentVersion = currentVersion.ToString(),
                LatestVersion = result.Release == null || result.Release.Version == null ? null : result.Release.Version.ToString(),
                LastChecked = checkedAt,
                Notes = result.Release == null ? null : Summarize(result.Release.Body),
                Release = result.Release
            };
            switch (result.Status)
            {
                case UpdateCheckStatus.NotConfigured: state.MessageKey = "update_source_missing"; break;
                case UpdateCheckStatus.UpToDate: state.MessageKey = "update_none"; updateAvailable = false; break;
                case UpdateCheckStatus.Available: state.MessageKey = "update_available"; state.CanInstall = true; state.CanSkip = true; updateAvailable = true; break;
                case UpdateCheckStatus.Skipped: state.MessageKey = "update_skipped"; state.MessageArgs = new object[] { state.LatestVersion }; break;
                default: state.MessageKey = "update_check_failed"; state.MessageArgs = new object[] { result.Error }; state.IsError = true; break;
            }
            PostUi(delegate
            {
                if (control != null) control.SetUpdateState(state);
                if (state.MessageKey == "update_available" && actStatus != null)
                    actStatus.Text = "SSRename: " + Localization.T(snapshot.Language, "update_available") + "  " + currentVersion + " → " + state.LatestVersion;
            });
        }

        private void OnInstallUpdateRequested(ReleaseInfo release)
        {
            if (release == null || release.Version == null) return;
            StartUpdateTask(delegate { return PrepareUpdateAsync(release); });
        }

        private async Task PrepareUpdateAsync(ReleaseInfo release)
        {
            var cancellation = updateCancellation;
            var language = SnapshotSettings().Language;
            PostUi(delegate
            {
                if (control != null) control.SetUpdateState(new UpdateUiState { Checking = true, CurrentVersion = currentVersion.ToString(), LatestVersion = release.Version.ToString(), Notes = Summarize(release.Body), MessageKey = "update_download", Release = release });
            });
            try
            {
                var stagedPath = await updatePackageService.PrepareAsync(release, cancellation.Token).ConfigureAwait(false);
                if (Volatile.Read(ref shuttingDown) != 0 || cancellation.IsCancellationRequested) return;
                PostUi(delegate
                {
                    if (control != null) control.SetUpdateState(new UpdateUiState { CurrentVersion = currentVersion.ToString(), LatestVersion = release.Version.ToString(), Notes = Summarize(release.Body), MessageKey = "update_ready_manual", MessageArgs = new object[] { stagedPath }, Release = release });
                    if (actStatus != null) actStatus.Text = "SSRename: " + Localization.T(language, "update_ready_manual", stagedPath);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (Volatile.Read(ref shuttingDown) == 0)
                    PostUi(delegate { if (control != null) control.SetUpdateState(new UpdateUiState { CurrentVersion = currentVersion.ToString(), LatestVersion = release.Version.ToString(), Notes = Summarize(release.Body), MessageKey = "update_failed", MessageArgs = new object[] { ex.GetBaseException().Message }, IsError = true, CanInstall = true, CanSkip = true, Release = release }); });
            }
        }

        private void OnSkipUpdateRequested(ReleaseInfo release)
        {
            if (release == null || release.Version == null || Volatile.Read(ref shuttingDown) != 0) return;
            lock (settingsSync) settings.SkippedVersion = release.Version.ToString();
            updateAvailable = false;
            var snapshot = SnapshotSettings();
            if (control != null) control.SetUpdateState(new UpdateUiState { CurrentVersion = currentVersion.ToString(), LatestVersion = release.Version.ToString(), Notes = Summarize(release.Body), MessageKey = "update_skipped", MessageArgs = new object[] { release.Version.ToString() }, Release = release });
        }

        private static string Summarize(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;
            var value = body.Trim(); return value.Length <= 1600 ? value : value.Substring(0, 1600) + "…";
        }
    }
}
