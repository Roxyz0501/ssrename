using Advanced_Combat_Tracker;
using System;
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
        private SemVersion currentVersion;
        private bool updateAvailable;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            actStatus = pluginStatusText;
            pluginPage = pluginScreenSpace;
            settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Advanced Combat Tracker", "Config", "SSRename.config.xml");
            settings = PluginSettings.Load(settingsPath);
            if (settings.LanguageWasMissing)
                settings.Language = Localization.DetectOsLanguage();
            else
                settings.Language = Localization.Normalize(settings.Language);
            pluginScreenSpace.Text = Localization.T(settings.Language, "title");
            SaveSettings();
            gameData = new GameDataService(settings.Language);
            contextProvider = new FfxivContextProvider(gameData);
            control = new SettingsControl(settings);
            control.SettingsChanged += OnSettingsChanged;
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
                CheckForUpdatesAsync(false);
        }

        public void DeInitPlugin()
        {
            if (ActGlobals.oFormActMain != null)
                ActGlobals.oFormActMain.OnLogLineRead -= OnLogLineRead;
            if (contextTimer != null)
            {
                contextTimer.Stop();
                contextTimer.Tick -= OnContextTick;
                contextTimer.Dispose();
                contextTimer = null;
            }
            if (screenshotWatcher != null)
            {
                screenshotWatcher.StatusChanged -= OnWatcherStatus;
                screenshotWatcher.FileRenamed -= OnFileRenamed;
                screenshotWatcher.Dispose();
                screenshotWatcher = null;
            }
            if (control != null)
            {
                control.SettingsChanged -= OnSettingsChanged;
                control.CheckUpdatesRequested -= OnCheckUpdatesRequested;
                control.InstallUpdateRequested -= OnInstallUpdateRequested;
                control.SkipUpdateRequested -= OnSkipUpdateRequested;
            }
            if (updateCancellation != null) { updateCancellation.Cancel(); updateCancellation.Dispose(); updateCancellation = null; }
            if (updateClient != null) { updateClient.Dispose(); updateClient = null; }
            SaveSettings();
            if (actStatus != null)
                actStatus.Text = "SSRename: " + Localization.T(settings.Language, "stopped");
        }

        private void OnContextTick(object sender, EventArgs e)
        {
            var status = contextProvider.Read(settings.Language);
            latestContext = status.Context == null ? null : status.Context.Clone();
            control.SetContext(status);
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            control.ApplyTo(settings);
            if (pluginPage != null) pluginPage.Text = Localization.T(settings.Language, "title");
            SaveSettings();
            RestartWatcher();
        }

        private void RestartWatcher()
        {
            screenshotWatcher.Restart(settings, delegate
            {
                var value = latestContext;
                return value == null ? null : value.Clone();
            });
        }

        private void OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            if (isImport || logInfo == null || logInfo.detectedType != 1 || string.IsNullOrWhiteSpace(logInfo.originalLogLine))
                return;
            var fields = logInfo.originalLogLine.Split('|');
            if (fields.Length > 3)
                contextProvider.SetZoneName(fields[3]);
        }

        private void OnWatcherStatus(string message, bool isError)
        {
            control.SetWatcherStatus(message, isError);
            if (actStatus != null)
            {
                Action update = delegate
                {
                    actStatus.Text = "SSRename: " + message + (updateAvailable ? "  |  " + Localization.T(settings.Language, "update_available") : string.Empty);
                };
                if (actStatus.InvokeRequired) actStatus.BeginInvoke(update); else update();
            }
        }

        private void OnFileRenamed(string oldPath, string newPath)
        {
            control.AddHistory(oldPath, newPath);
        }

        private void SaveSettings()
        {
            try
            {
                if (settings != null)
                    settings.Save(settingsPath);
            }
            catch (Exception ex)
            {
                if (actStatus != null)
                    actStatus.Text = "SSRename: " + Localization.T(settings == null ? Localization.English : settings.Language, "settings_save_failed", ex.Message);
            }
        }

        private void OnCheckUpdatesRequested(object sender, EventArgs e)
        {
            CheckForUpdatesAsync(true);
        }

        private async void CheckForUpdatesAsync(bool manual)
        {
            if (updateClient == null || updateCancellation == null) return;
            control.SetUpdateState(new UpdateUiState { Checking = true, CurrentVersion = currentVersion.ToString() });
            var result = await updateClient.CheckAsync(currentVersion, manual ? null : settings.SkippedVersion, updateCancellation.Token);
            if (updateCancellation == null || updateCancellation.IsCancellationRequested) return;
            settings.LastUpdateCheckUtc = result.CheckedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            SaveSettings();
            var state = new UpdateUiState
            {
                CurrentVersion = currentVersion.ToString(),
                LatestVersion = result.Release == null || result.Release.Version == null ? null : result.Release.Version.ToString(),
                LastChecked = settings.LastUpdateCheckUtc,
                Notes = result.Release == null ? null : Summarize(result.Release.Body),
                Release = result.Release
            };
            switch (result.Status)
            {
                case UpdateCheckStatus.NotConfigured:
                    state.MessageKey = "update_source_missing";
                    break;
                case UpdateCheckStatus.UpToDate:
                    state.MessageKey = "update_none";
                    updateAvailable = false;
                    break;
                case UpdateCheckStatus.Available:
                    state.MessageKey = "update_available"; state.CanInstall = true; state.CanSkip = true;
                    updateAvailable = true;
                    if (actStatus != null) actStatus.Text = "SSRename: " + Localization.T(settings.Language, "update_available") + "  " + currentVersion + " → " + state.LatestVersion;
                    break;
                case UpdateCheckStatus.Skipped:
                    state.MessageKey = "update_skipped"; state.MessageArgs = new object[] { state.LatestVersion };
                    break;
                default:
                    state.MessageKey = "update_check_failed"; state.MessageArgs = new object[] { result.Error }; state.IsError = true;
                    break;
            }
            control.SetUpdateState(state);
        }

        private async void OnInstallUpdateRequested(ReleaseInfo release)
        {
            if (release == null || updateCancellation == null) return;
            control.SetUpdateState(new UpdateUiState { Checking = true, CurrentVersion = currentVersion.ToString(), LatestVersion = release.Version.ToString(), Notes = Summarize(release.Body), MessageKey = "update_download", Release = release });
            try
            {
                await updatePackageService.PrepareAndScheduleAsync(release, updateCancellation.Token);
                control.SetUpdateState(new UpdateUiState { CurrentVersion = currentVersion.ToString(), LatestVersion = release.Version.ToString(), Notes = Summarize(release.Body), MessageKey = "update_ready", Release = release });
                if (actStatus != null) actStatus.Text = "SSRename: " + Localization.T(settings.Language, "update_ready");
            }
            catch (Exception ex)
            {
                control.SetUpdateState(new UpdateUiState { CurrentVersion = currentVersion.ToString(), LatestVersion = release.Version.ToString(), Notes = Summarize(release.Body), MessageKey = "update_failed", MessageArgs = new object[] { ex.GetBaseException().Message }, IsError = true, CanInstall = true, CanSkip = true, Release = release });
            }
        }

        private void OnSkipUpdateRequested(ReleaseInfo release)
        {
            if (release == null || release.Version == null) return;
            settings.SkippedVersion = release.Version.ToString(); SaveSettings(); updateAvailable = false;
            control.SetUpdateState(new UpdateUiState { CurrentVersion = currentVersion.ToString(), LatestVersion = release.Version.ToString(), Notes = Summarize(release.Body), MessageKey = "update_skipped", MessageArgs = new object[] { release.Version.ToString() }, Release = release });
        }

        private static string Summarize(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;
            var value = body.Trim(); return value.Length <= 1600 ? value : value.Substring(0, 1600) + "…";
        }
    }
}
