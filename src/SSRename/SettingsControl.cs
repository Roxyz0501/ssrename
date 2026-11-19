using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SSRename
{
    internal sealed class SettingsControl : UserControl
    {
        private readonly PluginSettings settings;
        private CheckBox enabledCheck, sameDestinationCheck;
        private TextBox sourceText, destinationText, templateText;
        private Button destinationBrowseButton;
        private Roxyz.Act.UI.PluginTabs tabs;
        private Roxyz.Act.UI.PluginLanguagePage languagePage;
        private Roxyz.Act.UI.PluginUpdatePage updates;
        private string hostedLanguage;
        internal string Language { get { return hostedLanguage ?? settings.Language; } }
        private Label previewLabel, connectionValue, characterValue, placeValue, weatherValue, etValue, watcherValue;
        private ListBox historyList;
        private bool building;
        private bool uiInactive;
        private ContextStatus lastContext;
        private string lastWatcherMessage;
        private bool lastWatcherError;
        private UpdateUiState updateState;
        private readonly List<string> history = new List<string>();

        public event EventHandler SettingsChanged;
        public event EventHandler WatchFolderSettingsCommitted;
        public event EventHandler CheckUpdatesRequested;
        public event Action<ReleaseInfo> InstallUpdateRequested;
        public event Action<ReleaseInfo> SkipUpdateRequested;

        public SettingsControl(PluginSettings settings)
        {
            this.settings = settings;
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(244, 247, 251);
            Font = new Font("Yu Gothic UI", 9f);
            BuildUi();
        }

        public void ApplyTo(PluginSettings target)
        {
            if (enabledCheck == null) return;
            target.RenameEnabled = enabledCheck.Checked;
            target.ScreenshotFolder = sourceText.Text.Trim();
            target.UseScreenshotFolderAsDestination = sameDestinationCheck.Checked;
            target.DestinationFolder = destinationText.Text.Trim();
            target.FileNameTemplate = templateText.Text.Trim();
            if (updates != null) target.CheckUpdatesOnStartup = updates.CheckOnStartup;
            if (hostedLanguage == null && languagePage != null) target.Language = languagePage.LanguageCode;
        }

        public void SetContext(ContextStatus status) { lastContext = status; SafeUi(ApplyContext); }
        public void SetWatcherStatus(string message, bool isError)
        {
            lastWatcherMessage = message; lastWatcherError = isError;
            SafeUi(delegate { if (watcherValue != null) { watcherValue.Text = message; watcherValue.ForeColor = isError ? Color.FromArgb(176, 65, 56) : Color.FromArgb(17, 108, 75); } });
        }
        public void AddHistory(string oldPath, string newPath)
        {
            SafeUi(delegate { history.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + Path.GetFileName(oldPath) + "  →  " + Path.GetFileName(newPath)); while (history.Count > 50) history.RemoveAt(history.Count - 1); RefreshHistory(); });
        }
        public void SetUpdateState(UpdateUiState state) { updateState = state; SafeUi(ApplyUpdateState); }
        private string T(string key, params object[] args) { return Localization.T(Language, key, args); }

        private void BuildUi()
        {
            building = true; SuspendLayout();
            int selected = tabs == null ? 0 : tabs.SelectedIndex;
            while (Controls.Count > 0) Controls[0].Dispose();
            updates = null; languagePage = null;
            Controls.Clear();
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); Controls.Add(root);
            root.Controls.Add(BuildHeader(), 0, 0);
            var support = NewPage(T("tab_support"));
            tabs = new Roxyz.Act.UI.PluginTabs(support);
            tabs.SetLanguageFont(Language == Localization.ChineseSimplified ? "Microsoft YaHei UI" : Language == Localization.Korean ? "Malgun Gothic" : Language == Localization.Japanese ? "Yu Gothic UI" : "Segoe UI");
            root.Controls.Add(tabs, 0, 1);
            var basic = NewPage(T("tab_basic")); var names = NewPage(T("tab_filename")); var status = NewPage(T("tab_status"));
            foreach (var page in new[] { basic, names, status }) tabs.AddContentPage(page);
            BuildBasicPage(basic); BuildNamePage(names); BuildStatusPage(status);
            if (hostedLanguage == null)
            {
                languagePage = new Roxyz.Act.UI.PluginLanguagePage(Language);
                languagePage.LanguageChanged += delegate { RaiseSettingsChanged(); BuildUi(); };
                updates = new Roxyz.Act.UI.PluginUpdatePage("ssrename", Assembly.GetExecutingAssembly().GetName().Version.ToString(3), settings.CheckUpdatesOnStartup, Language);
                updates.CheckRequested += delegate { CheckUpdatesRequested?.Invoke(this, EventArgs.Empty); };
                updates.InstallRequested += delegate { if (updateState?.Release != null) InstallUpdateRequested?.Invoke(updateState.Release); };
                updates.LaterRequested += delegate { if (updateState?.Release != null) SkipUpdateRequested?.Invoke(updateState.Release); };
                updates.StartupChanged += delegate { RaiseSettingsChanged(); };
                tabs.AddContentPage(languagePage); tabs.AddContentPage(updates); BuildSupportPage(support); tabs.AddContentPage(support);
            }
            else support.Dispose();
            tabs.SelectedIndex = Math.Max(0, Math.Min(selected, tabs.TabCount - 1));
            building = false; UpdateDestinationState(); UpdatePreview(); ApplyContext(); RefreshHistory(); ApplyUpdateState(); ResumeLayout(true);
        }

        private void BuildBasicPage(TabPage page)
        {
            var table = NewTable(); page.Controls.Add(table);
            enabledCheck = new CheckBox { Text = T("enable_rename"), AutoSize = true, Checked = settings.RenameEnabled }; AddWideRow(table, enabledCheck);
            sourceText = new TextBox { Text = settings.ScreenshotFolder, Dock = DockStyle.Fill }; AddRow(table, T("watch_folder"), MakePathEditor(sourceText, T("browse"), BrowseSource));
            sameDestinationCheck = new CheckBox { Text = T("same_destination"), AutoSize = true, Checked = settings.UseScreenshotFolderAsDestination }; AddRow(table, T("destination"), sameDestinationCheck);
            destinationText = new TextBox { Text = settings.DestinationFolder, Dock = DockStyle.Fill }; destinationBrowseButton = NewButton(T("browse"), BrowseDestination); AddRow(table, T("destination_changed"), MakePathEditor(destinationText, destinationBrowseButton));
            AddRow(table, T("folder_actions"), NewButton(T("open_destination"), OpenDestination)); AddWideRow(table, NewNote(T("basic_note")));
            enabledCheck.CheckedChanged += delegate { RaiseSettingsChanged(); }; sameDestinationCheck.CheckedChanged += delegate { UpdateDestinationState(); RaiseSettingsChanged(); };
            sourceText.TextChanged += delegate { RaiseSettingsChanged(); };
            sourceText.Leave += delegate { if (building || uiInactive) return; RaiseSettingsChanged(); if (WatchFolderSettingsCommitted != null) WatchFolderSettingsCommitted(this, EventArgs.Empty); };
            destinationText.TextChanged += delegate { RaiseSettingsChanged(); };
            destinationText.Leave += delegate { RaiseSettingsChanged(); };
        }

        private void BuildNamePage(TabPage page)
        {
            var table = NewTable(); page.Controls.Add(table); templateText = new TextBox { Text = settings.FileNameTemplate, Dock = DockStyle.Fill }; AddRow(table, T("template"), templateText); AddWideRow(table, NewNote(T("template_note")));
            previewLabel = new Label { AutoSize = true, ForeColor = Color.FromArgb(45, 58, 77), Padding = new Padding(4, 8, 4, 8) }; AddRow(table, T("preview"), previewLabel);
            AddRow(table, T("template_actions"), NewButton(T("reset_template"), delegate { templateText.Text = "ffxiv_{timestamp}_{character}@{world}_{place}_{weather}_{et}"; RaiseSettingsChanged(); }));
            templateText.TextChanged += delegate { UpdatePreview(); RaiseSettingsChanged(); };
        }

        private void BuildStatusPage(TabPage page)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) }; root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250f)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); page.Controls.Add(root);
            var table = NewTable(); root.Controls.Add(table, 0, 0); connectionValue = NewValue(T("waiting_connection")); characterValue = NewValue("—"); placeValue = NewValue("—"); weatherValue = NewValue("—"); etValue = NewValue("—"); watcherValue = NewValue(T("initializing"));
            AddRow(table, T("connection"), connectionValue); AddRow(table, T("character"), characterValue); AddRow(table, T("place"), placeValue); AddRow(table, T("weather"), weatherValue); AddRow(table, T("et"), etValue); AddRow(table, T("file_watch"), watcherValue);
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) }; panel.Controls.Add(new Label { Text = T("history"), Dock = DockStyle.Top, Height = 25, ForeColor = Color.FromArgb(45, 58, 77) }); historyList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Yu Gothic UI", 8.5f) }; panel.Controls.Add(historyList); historyList.BringToFront(); root.Controls.Add(panel, 0, 1);
        }

        private void BuildSupportPage(TabPage page)
        {
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, RowCount = 7, Padding = new Padding(28, 24, 28, 24), BackColor = Color.White }; content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); page.Controls.Add(content);
            content.Controls.Add(Heading(T("support_heading")), 0, 0); content.Controls.Add(new Panel { Height = 3, Width = 520, BackColor = Color.FromArgb(218, 145, 52), Margin = new Padding(0, 0, 0, 18) }, 0, 1); content.Controls.Add(SupportText(T("support_intro"), false), 0, 2);
            var voluntary = SupportText(T("support_voluntary"), true); voluntary.BackColor = Color.FromArgb(255, 248, 232); voluntary.Padding = new Padding(12, 10, 12, 10); content.Controls.Add(voluntary, 0, 3);
            var status = NewNote(T("support_click_only")); status.Margin = new Padding(0, 10, 0, 0); var button = NewAccentButton(T("support_button")); button.Width = 360; button.Height = 50; button.Font = new Font("Yu Gothic UI Semibold", 11f, FontStyle.Bold);
            button.Click += delegate { string error; if (ExternalLinkService.TryOpenSupportPage(Language, out error)) { status.Text = T("support_opened"); status.ForeColor = Color.FromArgb(17, 108, 75); } else { status.Text = error; status.ForeColor = Color.FromArgb(176, 65, 56); } };
            content.Controls.Add(button, 0, 4); content.Controls.Add(new Label { Text = ExternalLinkService.SupportUrl, AutoSize = true, Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(127, 82, 28), Margin = new Padding(2, 4, 0, 0) }, 0, 5); content.Controls.Add(status, 0, 6);
        }

        internal void SetHostedLanguage(string code)
        {
            var next = string.IsNullOrEmpty(code) ? null : Localization.Normalize(code);
            if (hostedLanguage == next) return;
            hostedLanguage = next;
            lastWatcherMessage = null;
            BuildUi();
        }

        private void ApplyContext()
        {
            if (connectionValue == null) return; connectionValue.Text = lastContext != null && lastContext.Connected ? T("connected") : (lastContext == null ? T("waiting_connection") : lastContext.Error ?? T("waiting_connection")); connectionValue.ForeColor = lastContext != null && lastContext.Connected ? Color.FromArgb(17, 108, 75) : Color.FromArgb(176, 65, 56);
            var context = lastContext == null ? null : lastContext.Context; characterValue.Text = context == null ? "—" : (context.CharacterName ?? "—") + (string.IsNullOrWhiteSpace(context.WorldName) ? "" : " @ " + context.WorldName); placeValue.Text = context == null ? "—" : context.PlaceName ?? "—"; weatherValue.Text = context == null ? "—" : context.WeatherName ?? "—"; etValue.Text = context == null ? "—" : context.EorzeaTime ?? "—";
            if (!string.IsNullOrWhiteSpace(lastWatcherMessage)) { watcherValue.Text = lastWatcherMessage; watcherValue.ForeColor = lastWatcherError ? Color.FromArgb(176, 65, 56) : Color.FromArgb(17, 108, 75); }
        }

        private void ApplyUpdateState()
        {
            if (updates == null) return;
            var s = updateState;
            var mode = s == null ? Roxyz.Act.UI.UpdatePageState.Ready :
                s.MessageKey == "update_download" ? Roxyz.Act.UI.UpdatePageState.Preparing :
                s.Checking ? Roxyz.Act.UI.UpdatePageState.Checking :
                s.MessageKey == "update_ready" ? Roxyz.Act.UI.UpdatePageState.Prepared :
                s.CanInstall ? (s.IsError ? Roxyz.Act.UI.UpdatePageState.PrepareFailed : Roxyz.Act.UI.UpdatePageState.Available) : Roxyz.Act.UI.UpdatePageState.Ready;
            var message = s == null ? "" : T(s.MessageKey ?? "checking", s.MessageArgs ?? new object[0]);
            if (s != null && s.CanInstall)
                message += Environment.NewLine + s.CurrentVersion + " → " + s.LatestVersion + Environment.NewLine +
                    (s.MessageKey == "update_available_rate_limited" ? T("update_notes_rate_limited") : s.Notes);
            updates.ShowState(mode, message);
        }

        private void RefreshHistory() { if (historyList == null) return; historyList.Items.Clear(); foreach (var item in history) historyList.Items.Add(item); }
        private Panel BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.White }; panel.Controls.Add(new Label { Text = T("title"), Font = new Font("Yu Gothic UI Semibold", 17f, FontStyle.Bold), ForeColor = Color.FromArgb(34, 44, 61), AutoSize = true, Location = new Point(18, 9) }); panel.Controls.Add(new Label { Text = T("subtitle"), Font = new Font("Yu Gothic UI", 8.5f), ForeColor = Color.FromArgb(103, 116, 137), AutoSize = true, Location = new Point(20, 43) }); panel.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 2, BackColor = Color.FromArgb(202, 165, 87) }); return panel;
        }

        private static TabPage NewPage(string text) { return new TabPage(text) { BackColor = Color.White, Padding = new Padding(4) }; }
        private static TableLayoutPanel NewTable() { var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(8), BackColor = Color.White }; t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235f)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); return t; }
        private static void AddRow(TableLayoutPanel t, string label, Control control) { var row = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(4, 10, 8, 8), ForeColor = Color.FromArgb(45, 58, 77) }, 0, row); control.Margin = new Padding(4, 6, 4, 6); t.Controls.Add(control, 1, row); }
        private static void AddWideRow(TableLayoutPanel t, Control control) { var row = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); control.Margin = new Padding(4, 8, 4, 8); t.Controls.Add(control, 0, row); t.SetColumnSpan(control, 2); }
        private static Label NewNote(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(73, 78, 88), MaximumSize = new Size(780, 0) }; }
        private static Label NewValue(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(45, 58, 77), Padding = new Padding(0, 3, 0, 3) }; }
        private static Label Heading(string text) { return new Label { Text = text, AutoSize = true, Font = new Font("Yu Gothic UI Semibold", 17f, FontStyle.Bold), ForeColor = Color.FromArgb(166, 83, 15), Margin = new Padding(0, 0, 0, 8) }; }
        private static Label UpdateHeading(string text) { return new Label { Text = text, AutoSize = true, Font = new Font("Yu Gothic UI Semibold", 15f, FontStyle.Bold), ForeColor = Color.FromArgb(38, 53, 73), Margin = new Padding(0, 0, 0, 8) }; }
        private static Label SupportText(string text, bool bold) { return new Label { Text = text, AutoSize = true, MaximumSize = new Size(760, 0), Font = new Font(bold ? "Yu Gothic UI Semibold" : "Yu Gothic UI", 10f, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = bold ? Color.FromArgb(87, 67, 42) : Color.FromArgb(45, 58, 77), Margin = new Padding(0, 0, 0, bold ? 20 : 10) }; }
        private Control MakePathEditor(TextBox box, string text, EventHandler action) { return MakePathEditor(box, NewButton(text, action)); }
        private static Control MakePathEditor(TextBox box, Button button) { var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty }; panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); panel.Controls.Add(box, 0, 0); panel.Controls.Add(button, 1, 0); return panel; }
        private static Button NewButton(string text, EventHandler action) { var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(63, 76, 95), Font = new Font("Yu Gothic UI Semibold", 8.5f, FontStyle.Bold), Cursor = Cursors.Hand, Padding = new Padding(9, 3, 9, 3) }; b.FlatAppearance.BorderColor = Color.FromArgb(171, 181, 196); b.Click += action; return b; }
        private static Button NewAccentButton(string text) { var b = new Button { Text = text, Width = 150, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(202, 101, 20), ForeColor = Color.White, Font = new Font("Yu Gothic UI Semibold", 9f, FontStyle.Bold), Cursor = Cursors.Hand }; b.FlatAppearance.BorderColor = Color.FromArgb(164, 79, 12); b.FlatAppearance.MouseOverBackColor = Color.FromArgb(224, 126, 31); b.FlatAppearance.MouseDownBackColor = Color.FromArgb(174, 83, 13); return b; }
        private void BrowseSource(object s, EventArgs e) { BrowseInto(sourceText); }
        private void BrowseDestination(object s, EventArgs e) { BrowseInto(destinationText); }
        private void BrowseInto(TextBox target) { using (var d = new FolderBrowserDialog { Description = T("folder_dialog"), SelectedPath = Directory.Exists(target.Text) ? target.Text : string.Empty }) if (d.ShowDialog(this) == DialogResult.OK) { target.Text = d.SelectedPath; RaiseSettingsChanged(); } }
        private void OpenDestination(object s, EventArgs e) { var path = sameDestinationCheck.Checked ? sourceText.Text.Trim() : destinationText.Text.Trim(); try { if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); else MessageBox.Show(this, T("destination_missing"), "SSRename", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch (Exception ex) { MessageBox.Show(this, T("destination_missing") + "\r\n" + ex.Message, "SSRename", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        private void UpdateDestinationState() { if (destinationText != null) { destinationText.Enabled = !sameDestinationCheck.Checked; destinationBrowseButton.Enabled = !sameDestinationCheck.Checked; } }
        private void UpdatePreview() { if (previewLabel == null) return; var value = string.IsNullOrWhiteSpace(templateText.Text) ? "ffxiv_{timestamp}_{character}@{world}_{place}_{weather}_{et}" : templateText.Text; previewLabel.Text = value.Replace("{character}", "Taro Yamada").Replace("{world}", "Chocobo").Replace("{place}", "Limsa Lominsa").Replace("{timestamp}", "20260731_142530_123").Replace("{weather}", "Clear Skies").Replace("{et}", "ET1842") + ".png"; }
        internal void DisableBackgroundUi() { uiInactive = true; }
        private void RaiseSettingsChanged() { if (!building && !uiInactive && SettingsChanged != null) SettingsChanged(this, EventArgs.Empty); }
        private void SafeUi(Action action)
        {
            if (uiInactive || IsDisposed) return;
            Action guarded = delegate { if (!uiInactive && !IsDisposed) action(); };
            try { if (InvokeRequired) BeginInvoke(guarded); else guarded(); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}
