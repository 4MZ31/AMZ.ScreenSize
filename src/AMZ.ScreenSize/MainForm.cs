using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AMZ.ScreenSize
{
    public interface IDisplayActions
    {
        void RequestDiscovery();
        void SelectDisplays(List<string> paths);
    }
    public interface IAppActions
    {
        void ApplyManual(int value);
        void SaveProfileTarget(string id, int value);
        void SaveProfiles(List<GameProfile> profiles);
        void EditProfiles();
        void SetEnabled(bool enabled);
        void RefreshMonitor();
        bool StartupEnabled { get; }
        void SetStartup(bool enabled);
        void OpenLog();
        void HideWindow();
        bool AutoUpdatesEnabled { get; }
        void SetAutoUpdates(bool enabled);
        void CheckForUpdates();
        void SetDarkMode(bool dark);
    }

    public sealed class MainForm : Form
    {
        private readonly IAppActions actions;
        private readonly Control[] pages;
        private readonly UiButton[] navigation;
        private readonly ListBox presets;
        private readonly ScreenPreview preview;
        private readonly Label selected, current, status, deviceStatus, activity, updateStatus, feedback;
        private readonly UiButton apply, assign, checkUpdates, dark, light, refresh;
        private readonly UiSwitch automatic, startup, autoUpdates;
        private readonly ProfilesPanel profiles;
        private readonly DetectedDisplaysPanel inventory;
        private UiButton displayPicker;
        private int wantedFormat = 7;
        private string formatsSignature;
        private int ChosenMode { get { var preset = presets.SelectedItem as ScreenPreset; return preset == null ? -1 : preset.Value; } }
        private AppSnapshot last;
        private bool rendering, busy;
        private readonly Timer feedbackTimer = new Timer { Interval = 8000 };
        private readonly ToolTip hints = new ToolTip { AutoPopDelay = 10000 };
        public bool AllowExit { get; set; }
        public bool HasUnsavedProfiles { get { return profiles.Dirty || profiles.PendingSelection; } }
        public bool EditingProfiles { get { return profiles.InteractionActive; } }

        public MainForm(IAppActions actions, Icon icon)
        {
            this.actions = actions;
            Text = "AMZ.ScreenSize"; Icon = icon; Font = UiTheme.Body(); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1160, 760); MinimumSize = new Size(1080, 736); StartPosition = FormStartPosition.CenterScreen;
            Panel sidebar = new Panel { Name = "Sidebar", Dock = DockStyle.Left, Width = 232, Padding = new Padding(16, 28, 16, 20), Tag = "sidebar" };
            Label brand = UiTheme.Label("AMZ.ScreenSize", 16); brand.Font = UiTheme.Medium(16); brand.Dock = DockStyle.Top; brand.Height = 40;
            Label tagline = UiTheme.Label("Votre écran, à votre mesure.", 9.5f, "muted"); tagline.Dock = DockStyle.Top; tagline.Height = 32;
            Panel nav = new Panel { Dock = DockStyle.Top, Height = 196, Tag = "sidebar" };
            navigation = new UiButton[4];
            string[] titles = { "Écran", "Profils de jeux", "Réglages", "Moniteurs" }, symbols = { "screen", "profiles", "settings", "screen" };
            for (int i = 0; i < navigation.Length; i++)
            {
                int page = i; UiButton button = UiTheme.Button(titles[i]); button.Quiet = true; button.Symbol = symbols[i];
                button.SetBounds(0, 28 + i * 40, 200, 36); button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                button.Click += delegate { Navigate(page); }; navigation[i] = button; nav.Controls.Add(button);
            }
            Panel device = new Panel { Dock = DockStyle.Bottom, Height = 124, Tag = "sidebar" };
            Label model = UiTheme.Label("Moniteurs sélectionnés"); model.Dock = DockStyle.Top; model.Height = 28;
            deviceStatus = UiTheme.Label("Connexion en cours", 9.5f, "muted"); deviceStatus.Dock = DockStyle.Top; deviceStatus.Height = 24;
            Label version = UiTheme.Label("AMZ.ScreenSize  /  " + Program.Version, 9.5f, "muted"); version.Dock = DockStyle.Bottom; version.Height = 32;
            device.Controls.Add(deviceStatus); device.Controls.Add(model); device.Controls.Add(version);
            sidebar.Controls.Add(nav); sidebar.Controls.Add(tagline); sidebar.Controls.Add(brand); sidebar.Controls.Add(device);
            Panel content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(32, 28, 32, 24) };

            TableLayoutPanel screen = UiTheme.Table(1, 48, 36, 28, 0, 72, 64);
            screen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Panel heading = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            displayPicker = UiTheme.Button("Choisir les écrans ▾"); displayPicker.Dock = DockStyle.Right; displayPicker.Width = 280;
            displayPicker.Click += PickDisplays;
            heading.Controls.Add(UiTheme.Label("Format de l’écran", 24)); heading.Controls.Add(displayPicker);
            screen.Controls.Add(heading, 0, 0);
            screen.Controls.Add(UiTheme.Label("Ajustez la zone d’affichage de votre moniteur.", 10.5f, "secondary"), 0, 1);
            TableLayoutPanel chooser = UiTheme.Table(2, 0); chooser.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 252)); chooser.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            presets = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 44, BorderStyle = BorderStyle.None,
                IntegralHeight = false, Margin = new Padding(0, 0, 24, 0), AccessibleName = "Formats de l’écran" };
            foreach (ScreenPreset preset in ScreenPreset.All) presets.Items.Add(preset);
            presets.DrawItem += DrawPreset;
            presets.SelectedIndexChanged += delegate { if (presets.SelectedIndex >= 0) { preview.Preset = (ScreenPreset)presets.SelectedItem; wantedFormat = ChosenMode; selected.Text = preview.Preset.Label; UpdateButtons(); } };
            Panel previewArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 12), Tag = "surface", Margin = new Padding(0) };
            Label previewLabel = UiTheme.Label("Zone d’affichage", 10.5f, "secondary"); previewLabel.Dock = DockStyle.Top; previewLabel.Height = 32;
            selected = UiTheme.Label("23″ Wide (16:9)", 17); selected.Dock = DockStyle.Bottom; selected.Height = 40; selected.TextAlign = ContentAlignment.MiddleCenter;
            Label caption = UiTheme.Label("Aperçu indicatif · dalle de référence 27″", 9.5f, "muted"); caption.Dock = DockStyle.Bottom; caption.Height = 28; caption.TextAlign = ContentAlignment.MiddleCenter;
            preview = new ScreenPreview { Dock = DockStyle.Fill, Tag = "surface" };
            previewArea.Controls.Add(preview); previewArea.Controls.Add(previewLabel); previewArea.Controls.Add(selected); previewArea.Controls.Add(caption);
            chooser.Controls.Add(presets, 0, 0); chooser.Controls.Add(previewArea, 1, 0); screen.Controls.Add(chooser, 0, 3);
            TableLayoutPanel commands = UiTheme.Table(2, 0); commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); commands.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 308)); commands.Padding = new Padding(0, 16, 0, 12);
            Label note = UiTheme.Label("La résolution Windows reste identique.", 9.5f, "muted");
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            assign = UiTheme.Button("Attribuer à un jeu"); assign.Width = 172; assign.Margin = new Padding(0, 0, 12, 0); assign.Click += AssignPreset;
            apply = UiTheme.Button("Appliquer", true); apply.Width = 124; apply.Click += delegate { actions.ApplyManual(ChosenMode); };
            buttons.Controls.Add(assign); buttons.Controls.Add(apply); commands.Controls.Add(note, 0, 0); commands.Controls.Add(buttons, 1, 0); screen.Controls.Add(commands, 0, 4);
            Panel readout = new Panel { Dock = DockStyle.Fill, Tag = "surface", Padding = new Padding(16, 8, 16, 8), Margin = new Padding(0) };
            current = UiTheme.Label("Format actuel : connexion…"); current.Dock = DockStyle.Top; current.Height = 24;
            activity = UiTheme.Label("Les profils appliquent leur format au lancement d’un jeu.", 9.5f, "muted"); activity.Dock = DockStyle.Fill;
            readout.Controls.Add(activity); readout.Controls.Add(current); screen.Controls.Add(readout, 0, 5);

            TableLayoutPanel profilePage = UiTheme.Table(1, 48, 40, 56, 0); profilePage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            profilePage.Controls.Add(UiTheme.Label("Profils de jeux", 24), 0, 0);
            profilePage.Controls.Add(UiTheme.Label("Un format par jeu. Le précédent revient à la fermeture du dernier jeu.", 10.5f, "secondary"), 0, 1);
            automatic = new UiSwitch { Text = "Changer automatiquement le format", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16), AccessibleName = "Activer les profils automatiques" };
            automatic.CheckedChanged += delegate { if (!rendering) actions.SetEnabled(automatic.Checked); }; profilePage.Controls.Add(automatic, 0, 2);
            profiles = new ProfilesPanel(new GameProfile[0], icon, null);
            profiles.SaveRequested += delegate { actions.SaveProfiles(profiles.ValidatedDraft()); };
            profilePage.Controls.Add(profiles, 0, 3);

            TableLayoutPanel settings = UiTheme.Table(1, 48, 40, 160, 184, 140, 0); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settings.Controls.Add(UiTheme.Label("Réglages", 24), 0, 0);
            settings.Controls.Add(UiTheme.Label("Un utilitaire discret, configuré pour votre quotidien.", 10.5f, "secondary"), 0, 1);
            Panel appearance = SettingSection("Apparence", "Choisissez le thème de l’application.");
            FlowLayoutPanel themes = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, WrapContents = false, Margin = new Padding(0) };
            dark = UiTheme.Button("Sombre"); dark.Width = 128; dark.Margin = new Padding(0, 0, 12, 0); dark.Click += delegate { actions.SetDarkMode(true); };
            light = UiTheme.Button("Clair"); light.Width = 128; light.Click += delegate { actions.SetDarkMode(false); }; themes.Controls.Add(dark); themes.Controls.Add(light); appearance.Controls.Add(themes);
            TableLayoutPanel preferences = UiTheme.Table(2, 0); preferences.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); preferences.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            appearance.Margin = new Padding(0, 0, 12, 12); preferences.Controls.Add(appearance, 0, 0);
            Panel launch = SettingSection("Au démarrage", "Reste actif dans les icônes masquées.");
            startup = new UiSwitch { Text = "Lancer avec Windows", Dock = DockStyle.Bottom, Height = 40, Checked = actions.StartupEnabled };
            startup.CheckedChanged += delegate { if (!rendering) actions.SetStartup(startup.Checked); }; launch.Controls.Add(startup); preferences.Controls.Add(launch, 1, 0); settings.Controls.Add(preferences, 0, 2);
            Panel updates = SettingSection("Mises à jour", "Les nouvelles versions s’installent après la fermeture des jeux gérés.");
            autoUpdates = new UiSwitch { Text = "Télécharger et installer automatiquement", Dock = DockStyle.Bottom, Height = 40, Checked = actions.AutoUpdatesEnabled };
            autoUpdates.CheckedChanged += delegate { if (!rendering) actions.SetAutoUpdates(autoUpdates.Checked); };
            Panel updateRow = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            checkUpdates = UiTheme.Button("Vérifier les mises à jour"); checkUpdates.Width = 228; checkUpdates.Dock = DockStyle.Right; checkUpdates.Click += delegate { actions.CheckForUpdates(); };
            updateStatus = UiTheme.Label("Version " + Program.Version, 9.5f, "muted"); updateStatus.Dock = DockStyle.Fill;
            updateRow.Controls.Add(updateStatus); updateRow.Controls.Add(checkUpdates); updates.Controls.Add(autoUpdates); updates.Controls.Add(updateRow); settings.Controls.Add(updates, 0, 3);
            Panel support = SettingSection("Moniteur et diagnostic", "Relisez le format actuel ou consultez le journal d’activité.");
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, WrapContents = false };
            refresh = UiTheme.Button("Actualiser le moniteur"); refresh.Width = 204; refresh.Margin = new Padding(0, 0, 12, 0); refresh.Click += delegate { actions.RefreshMonitor(); };
            UiButton log = UiTheme.Button("Ouvrir le journal"); log.Width = 172; log.Click += delegate { actions.OpenLog(); }; tools.Controls.Add(refresh); tools.Controls.Add(log); support.Controls.Add(tools); settings.Controls.Add(support, 0, 4);
            inventory = new DetectedDisplaysPanel(RequestDiscovery);
            pages = new Control[] { screen, profilePage, settings, inventory }; foreach (Control page in pages) { page.Dock = DockStyle.Fill; content.Controls.Add(page); }
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(32, 0, 32, 0) };
            status = UiTheme.Label("Connexion au moniteur…", 9.5f, "muted"); bottom.Controls.Add(status);
            feedback = UiTheme.Label("", 9.5f); feedback.Visible = false; bottom.Controls.Add(feedback); feedback.BringToFront();
            feedbackTimer.Tick += delegate { feedback.Visible = false; feedbackTimer.Stop(); };
            hints.SetToolTip(assign, "Enregistrer le format sélectionné pour un profil de jeu.");
            Panel workspace = new Panel { Dock = DockStyle.Fill };
            workspace.Controls.Add(content); workspace.Controls.Add(bottom);
            Controls.Add(workspace); Controls.Add(sidebar);
            presets.SelectedIndex = 7; Navigate(0); UiTheme.Apply(this);
            HandleCreated += delegate { UiTheme.Caption(Handle); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!AllowExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; actions.HideWindow(); } };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) actions.HideWindow(); };
        }
        private static Panel SettingSection(string title, string description)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 12), Margin = new Padding(0, 0, 0, 12), Tag = "surface" };
            Label heading = UiTheme.Label(title, 11.5f); heading.Font = UiTheme.Medium(11.5f); heading.Dock = DockStyle.Top; heading.Height = 24;
            Label detail = UiTheme.Label(description, 9.5f, "secondary"); detail.Dock = DockStyle.Top; detail.Height = 28;
            panel.Controls.Add(detail); panel.Controls.Add(heading); return panel;
        }
        public void Navigate(int index)
        {
            if (index < 0 || index >= pages.Length) return;
            for (int i = 0; i < pages.Length; i++) { pages[i].Visible = i == index; navigation[i].Selected = i == index; navigation[i].Invalidate(); }
            pages[index].BringToFront(); feedback.BringToFront();
            if (index == 1) profiles.PrepareDetection();
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x007E && inventory != null && IsHandleCreated && !IsDisposed)
                RequestDiscovery();
        }
        private void DrawPreset(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            ScreenPreset preset = (ScreenPreset)presets.Items[e.Index]; bool chosen = (e.State & DrawItemState.Selected) != 0;
            using (Brush brush = new SolidBrush(chosen ? UiTheme.Selection : UiTheme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            float s = e.Graphics.DpiX / 96;
            UiIcon.Draw(e.Graphics, "crop", new RectangleF(e.Bounds.X + 12 * s, e.Bounds.Y + (e.Bounds.Height - 18 * s) / 2, 18 * s, 18 * s), chosen ? UiTheme.Accent : UiTheme.Muted);
            TextRenderer.DrawText(e.Graphics, preset.Name, Font, new Rectangle(e.Bounds.X + (int)(42 * s), e.Bounds.Y, e.Bounds.Width - (int)(104 * s), e.Bounds.Height), chosen ? UiTheme.Accent : UiTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (Font mono = new Font("Consolas", 9.5f)) TextRenderer.DrawText(e.Graphics, preset.Value < 2 ? "" : preset.Ratio, mono,
                new Rectangle(e.Bounds.Right - (int)(62 * s), e.Bounds.Y, (int)(50 * s), e.Bounds.Height), UiTheme.Secondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }
        private void AssignPreset(object sender, EventArgs e)
        {
            if (last == null || last.Settings.Profiles.Count == 0) { Navigate(1); return; }
            ContextMenuStrip menu = new ContextMenuStrip { Renderer = new UiMenuRenderer(), Font = UiTheme.Body() };
            foreach (GameProfile profile in last.Settings.Profiles)
            {
                string id = profile.Id;
                menu.Items.Add(profile.Name, null, delegate { actions.SaveProfileTarget(id, ChosenMode); });
            }
            menu.Closed += delegate { BeginInvoke((Action)delegate { menu.Dispose(); }); }; menu.Show(assign, new Point(0, assign.Height));
        }
        private void UpdateButtons()
        {
            apply.Enabled = !busy && presets.SelectedIndex >= 0 && last != null && last.Connected && !last.Settings.RestorePending && !(last.GameRunning && last.Settings.Enabled);
            apply.Text = last != null && ChosenMode >= 0 && last.CurrentMode == ChosenMode ? "Appliqué" : "Appliquer";
            if (last != null && ChosenMode >= 0 && last.CurrentMode == ChosenMode) apply.Enabled = false;
            assign.Enabled = !busy && !HasUnsavedProfiles && presets.SelectedIndex >= 0;
            hints.SetToolTip(apply, last != null && (last.Settings.RestorePending || (last.GameRunning && last.Settings.Enabled)) ? "Un profil gère l’écran. Mettez les profils en pause pour appliquer un format manuel." : "Appliquer le format sélectionné au moniteur.");
        }
        public void Render(AppSnapshot snapshot, bool working)
        {
            last = snapshot; busy = working; rendering = true;
            try
            {
                snapshot.Settings.Validate();
                RenderDisplays(snapshot, working);
                if (UiTheme.Dark != snapshot.Settings.DarkMode) { UiTheme.Dark = snapshot.Settings.DarkMode; UiTheme.Apply(this); }
                dark.Selected = snapshot.Settings.DarkMode; light.Selected = !snapshot.Settings.DarkMode; dark.Invalidate(); light.Invalidate();
                automatic.Checked = snapshot.Settings.Enabled; automatic.Enabled = !working;
                startup.Checked = actions.StartupEnabled; startup.Enabled = !working;
                autoUpdates.Checked = snapshot.Settings.AutoUpdate; autoUpdates.Enabled = !working;
                dark.Enabled = light.Enabled = refresh.Enabled = !working;
                current.Text = "Format actuel  ·  " + (snapshot.CurrentMode >= 0 ? ScreenPreset.Describe(snapshot.CurrentMode) : "indisponible");
                activity.Text = snapshot.ActiveProfileName != null ? "Profil actif : " + snapshot.ActiveProfileName + " · restauration à la fermeture" : snapshot.Settings.Enabled ? "Profils automatiques actifs · en attente d’un jeu" : "Profils automatiques en pause · le format reste manuel";
                deviceStatus.Text = snapshot.Connected ? "Connecté · DDC/CI" : "Moniteur indisponible";
                deviceStatus.ForeColor = snapshot.Connected ? UiTheme.Accent : UiTheme.Muted;
                status.Text = working ? "Opération en cours…" : snapshot.Status;
                profiles.Render(snapshot.Settings.Profiles, working); UpdateButtons();
            }
            finally { rendering = false; }
        }
        public void SelectPreset(int value)
        {
            wantedFormat = value;
            var preset = ScreenPreset.Find(value);
            presets.SelectedIndex = preset == null ? -1 : presets.Items.IndexOf(preset);
        }

        private void RequestDiscovery()
        { var action = actions as IDisplayActions; if (action != null) action.RequestDiscovery(); }

        private void RenderDisplays(AppSnapshot snapshot, bool working)
        {
            if (snapshot.Displays == null) return;
            inventory.Render(snapshot.Displays);
            var paths = snapshot.Settings.DisplaySelection ?? new List<string>();
            var group = MonitorFleet.Selected(snapshot.Displays, paths);
            int[] common = MonitorFleet.Common(snapshot.Displays, paths);
            string signature = string.Join(",", common);
            if (formatsSignature != signature)
            {
                int restore = wantedFormat;
                presets.BeginUpdate();
                try
                {
                    presets.Items.Clear();
                    foreach (int value in common) presets.Items.Add(ScreenPreset.Find(value));
                    if (common.Length != 0) SelectPreset(common.Contains(restore) ? restore : common[0]);
                    else { preview.Preset = null; selected.Text = paths.Count == 0 ? "Sélectionnez un écran" : "Aucun format commun disponible"; }
                }
                finally { presets.EndUpdate(); }
                profiles.SetFormats(common);
                formatsSignature = signature;
            }
            displayPicker.Enabled = !working && !snapshot.Settings.RestorePending;
            displayPicker.Text = group.Count == 1 && group[0] != null ? group[0].Description + " ▾" :
                paths.Count > 0 ? paths.Count + " écran(s) sélectionné(s) ▾" : "Choisir les écrans ▾";
        }

        private void PickDisplays(object sender, EventArgs e)
        {
            var action = actions as IDisplayActions;
            if (action == null || last == null || last.Displays == null) return;
            var paths = last.Settings.DisplaySelection ?? new List<string>();
            var menu = new ContextMenuStrip { Renderer = new UiMenuRenderer() };
            var choices = new List<ToolStripMenuItem>();
            foreach (DisplayScan display in last.Displays.Where(d => d.Modes.Count > 0 && !string.IsNullOrEmpty(d.DevicePath)))
            {
                var item = new ToolStripMenuItem(display.Description + " · " + display.Connector + " · " + display.WindowsDisplay)
                { CheckOnClick = true, Checked = paths.Contains(display.DevicePath, StringComparer.OrdinalIgnoreCase), Tag = display.DevicePath };
                choices.Add(item); menu.Items.Add(item);
            }
            if (choices.Count == 0) menu.Items.Add(new ToolStripMenuItem("Aucun écran compatible détecté") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            bool done = false;
            menu.Items.Add("Valider la sélection", null, delegate {
                done = true; action.SelectDisplays(choices.Where(c => c.Checked).Select(c => (string)c.Tag).ToList()); menu.Close();
            });
            menu.Closing += delegate(object s, ToolStripDropDownClosingEventArgs args) {
                if (!done && args.CloseReason == ToolStripDropDownCloseReason.ItemClicked) args.Cancel = true;
            };
            menu.Closed += delegate { if (IsHandleCreated) BeginInvoke((Action)delegate { menu.Dispose(); }); };
            menu.Show(displayPicker, new Point(0, displayPicker.Height));
        }
        public void ShowFeedback(string text, bool error)
        { feedback.Text = text; hints.SetToolTip(feedback, text); feedback.ForeColor = error ? UiTheme.Danger : UiTheme.Accent; feedback.Visible = true; feedback.BringToFront(); feedbackTimer.Stop(); feedbackTimer.Start(); }
        protected override void Dispose(bool disposing)
        { if (disposing) { feedbackTimer.Dispose(); hints.Dispose(); } base.Dispose(disposing); }
        public void SetUpdateStatus(string text, bool ready, bool checking)
        {
            updateStatus.Text = text;
            checkUpdates.Text = ready ? "Installer la mise à jour" : "Vérifier les mises à jour"; checkUpdates.Enabled = !checking;
        }
    }

    public sealed class ScreenPreview : Control
    {
        private ScreenPreset preset = ScreenPreset.All[7];
        public ScreenPreset Preset { get { return preset; } set { preset = value; Invalidate(); } }
        public ScreenPreview() { DoubleBuffered = true; ResizeRedraw = true; AccessibleName = "Aperçu de la zone d’affichage"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; float s = g.DpiX / 96;
            if (preset == null || preset.Inches == 0 || preset.Inches > 27)
            {
                TextRenderer.DrawText(g, preset == null ? "Aucun aperçu" : preset.Label, Font, ClientRectangle, UiTheme.Secondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            float w = Math.Max(40, Math.Min(Width - 24 * s, (Height - 64 * s) * 16 / 9f)), h = w * 9 / 16;
            RectangleF frame = new RectangleF((Width - w) / 2, Math.Max(8 * s, (Height - h - 24 * s) / 2), w, h);
            using (GraphicsPath path = UiTheme.Rounded(frame, 8 * s)) using (Brush b = new SolidBrush(UiTheme.Border)) g.FillPath(b, path);
            RectangleF screen = RectangleF.Inflate(frame, -5 * s, -5 * s); using (Brush b = new SolidBrush(UiTheme.Hex(0x090B0C))) g.FillRectangle(b, screen);
            double nativeW = 27 * 16 / Math.Sqrt(337), nativeH = 27 * 9 / Math.Sqrt(337);
            double ph = preset.Inches / Math.Sqrt(preset.Aspect * preset.Aspect + 1), pw = ph * preset.Aspect;
            float aw = screen.Width * (float)(pw / nativeW), ah = screen.Height * (float)(ph / nativeH);
            RectangleF active = new RectangleF(screen.X + (screen.Width - aw) / 2, screen.Y + (screen.Height - ah) / 2, aw, ah);
            using (Brush b = new SolidBrush(UiTheme.Hex(0x1D3931))) g.FillRectangle(b, active);
            using (Pen p = new Pen(UiTheme.Hex(0x73C8B1), 1.5f * s))
            {
                float c = 12 * s; RectangleF r = RectangleF.Inflate(active, -6 * s, -6 * s);
                g.DrawLines(p, new[] { new PointF(r.Left, r.Top + c), new PointF(r.Left, r.Top), new PointF(r.Left + c, r.Top) });
                g.DrawLines(p, new[] { new PointF(r.Right - c, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Top + c) });
                g.DrawLines(p, new[] { new PointF(r.Left, r.Bottom - c), new PointF(r.Left, r.Bottom), new PointF(r.Left + c, r.Bottom) });
                g.DrawLines(p, new[] { new PointF(r.Right - c, r.Bottom), new PointF(r.Right, r.Bottom), new PointF(r.Right, r.Bottom - c) });
            }
            using (Font f = UiTheme.Body(26)) using (Brush b = new SolidBrush(UiTheme.Hex(0xD8EDE6))) using (StringFormat align = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(preset.Value < 2 ? preset.Name : preset.Name.Replace(" Wide", ""), f, b, active, align);
            using (Pen stand = new Pen(UiTheme.Border, 4 * s)) { g.DrawLine(stand, frame.X + w / 2, frame.Bottom + 2 * s, frame.X + w / 2, frame.Bottom + 20 * s); g.DrawLine(stand, frame.X + w / 2 - 28 * s, frame.Bottom + 20 * s, frame.X + w / 2 + 28 * s, frame.Bottom + 20 * s); }
        }
    }
}
