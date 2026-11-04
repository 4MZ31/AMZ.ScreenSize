using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Linq;

namespace AMZ.ScreenSize
{
    public interface IAppActions
    {
        void ApplyManual(int value);
        void SaveProfileTarget(string id, int value);
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
    }

    public sealed class MainForm : Form
    {
        private readonly IAppActions actions;
        private readonly ListBox presets;
        private readonly ScreenPreview preview;
        private readonly Label selected, current, status, target;
        private readonly Button apply, save, refresh;
        private readonly CheckBox automatic, startup;
        private readonly CheckBox autoUpdates;
        private readonly Button checkUpdates;
        private readonly Label updateStatus;
        private readonly ComboBox profileCombo;
        private readonly Button manageProfiles;
        private string profilesSignature;
        private bool rendering, busy;
        private AppSnapshot last;
        public bool AllowExit { get; set; }
        private readonly Font itemFont = new Font("Segoe UI", 11, FontStyle.Regular);
        private readonly Font ratioFont = new Font("Segoe UI", 9, FontStyle.Regular);
        private static readonly Color Ink = Color.FromArgb(29, 43, 53);
        private static readonly Color Muted = Color.FromArgb(96, 109, 120);

        private static Label TextLabel(string text, float size, bool bold)
        {
            return new Label { Text = text, AutoSize = false, Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft };
        }
        private static Button ActionButton(string text, bool primary)
        {
            Button button = new Button { Text = text, AutoSize = false, Width = 190, Height = 38,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0),
                BackColor = primary ? Ink : Color.White, ForeColor = primary ? Color.White : Ink };
            button.FlatAppearance.BorderColor = primary ? Ink : Color.FromArgb(210, 217, 222);
            if (primary) button.EnabledChanged += delegate { button.BackColor = button.Enabled ? Ink : Color.FromArgb(217, 223, 227); };
            return button;
        }

        public MainForm(IAppActions actions, Icon icon)
        {
            this.actions = actions;
            Text = "AMZ.ScreenSize"; Icon = icon;
            Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(245, 247, 249);
            ForeColor = Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1000, 750);
            MinimumSize = new Size(960, 780);
            StartPosition = FormStartPosition.CenterScreen;

            Panel header = new Panel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(28, 12, 28, 10), BackColor = Color.White };
            Label title = TextLabel("AMZ.ScreenSize", 22, true);
            title.Dock = DockStyle.Top; title.Height = 40;
            Label subtitle = TextLabel("Samsung Odyssey G4  ·  Taille de l’image", 10, false);
            subtitle.ForeColor = Muted;
            header.Controls.Add(subtitle); header.Controls.Add(title);
            TableLayoutPanel updates = new TableLayoutPanel { Dock = DockStyle.Right, Width = 244, RowCount = 3, ColumnCount = 1, Margin = new Padding(0) };
            updates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            updates.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            updates.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            updates.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            autoUpdates = new CheckBox { Text = "Mises à jour automatiques", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f), Checked = actions.AutoUpdatesEnabled };
            autoUpdates.CheckedChanged += delegate { if (!rendering) actions.SetAutoUpdates(autoUpdates.Checked); };
            checkUpdates = ActionButton("Vérifier les mises à jour", false); checkUpdates.Dock = DockStyle.Fill; checkUpdates.Font = new Font("Segoe UI", 8.5f);
            checkUpdates.Click += delegate { actions.CheckForUpdates(); };
            updateStatus = TextLabel("Version " + Program.Version, 8, false); updateStatus.ForeColor = Muted;
            updates.Controls.Add(autoUpdates, 0, 0); updates.Controls.Add(checkUpdates, 0, 1); updates.Controls.Add(updateStatus, 0, 2);
            header.Controls.Add(updates);

            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 20, 28, 14), ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = new Padding(0, 0, 24, 0) };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            left.Controls.Add(TextLabel("Formats prédéfinis", 12, true), 0, 0);
            presets = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 44, BorderStyle = BorderStyle.None, IntegralHeight = false,
                BackColor = Color.White, Margin = new Padding(0, 8, 0, 8), AccessibleName = "Formats prédéfinis" };
            foreach (ScreenPreset preset in ScreenPreset.All) presets.Items.Add(preset);
            presets.DrawItem += DrawPreset;
            presets.SelectedIndexChanged += delegate { if (presets.SelectedIndex >= 0) UpdateSelection(); };
            left.Controls.Add(presets, 0, 1);
            Label note = TextLabel("Ces formats changent la zone d’affichage du moniteur. La résolution Windows reste identique.", 9, false);
            note.ForeColor = Muted; note.TextAlign = ContentAlignment.TopLeft; note.Padding = new Padding(0, 10, 0, 0);
            left.Controls.Add(note, 0, 2);

            TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 194));
            preview = new ScreenPreview { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), BackColor = Color.White };
            right.Controls.Add(preview, 0, 0);
            selected = TextLabel("23\u2033 Wide (16:9)", 13, true);
            right.Controls.Add(selected, 0, 1);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            apply = ActionButton("Appliquer maintenant", true);
            save = ActionButton("Utiliser pour le profil", false);
            apply.Click += delegate { actions.ApplyManual(presets.SelectedIndex); };
            save.Click += delegate { GameProfile profile = profileCombo.SelectedItem as GameProfile; if (profile != null) actions.SaveProfileTarget(profile.Id, presets.SelectedIndex); };
            buttons.Controls.Add(apply); buttons.Controls.Add(save); right.Controls.Add(buttons, 0, 2);

            TableLayoutPanel automation = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 8), BackColor = Color.White,
                RowCount = 5, ColumnCount = 1, Margin = new Padding(0, 4, 0, 0) };
            automation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            automation.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            automation.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            automation.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            automation.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            automation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Panel profileHeader = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            manageProfiles = ActionButton("Gérer / détecter…", false); manageProfiles.Dock = DockStyle.Right; manageProfiles.Width = 154; manageProfiles.Font = new Font("Segoe UI", 9);
            manageProfiles.Click += delegate { actions.EditProfiles(); };
            profileHeader.Controls.Add(TextLabel("Profils de jeux", 11, true)); profileHeader.Controls.Add(manageProfiles);
            automation.Controls.Add(profileHeader, 0, 0);
            automatic = new CheckBox { Text = "Activer les profils automatiques", Dock = DockStyle.Fill, AutoSize = false, Cursor = Cursors.Hand };
            automatic.CheckedChanged += delegate { if (!rendering) actions.SetEnabled(automatic.Checked); };
            automation.Controls.Add(automatic, 0, 1);
            profileCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Profil de jeu" };
            profileCombo.SelectedIndexChanged += delegate { if (!rendering) UpdateButtons(); };
            automation.Controls.Add(profileCombo, 0, 2);
            target = TextLabel("Priorité au premier jeu détecté", 9, false); target.ForeColor = Muted;
            automation.Controls.Add(target, 0, 3);
            Label restore = TextLabel("Le format d’origine revient après le dernier jeu géré.", 9, false);
            restore.ForeColor = Muted;
            automation.Controls.Add(restore, 0, 4);
            right.Controls.Add(automation, 0, 3);
            body.Controls.Add(left, 0, 0); body.Controls.Add(right, 1, 0);

            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 88, BackColor = Color.White,
                Padding = new Padding(28, 8, 28, 8), ColumnCount = 3, RowCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            current = TextLabel("Format actuel : connexion…", 10, true); footer.Controls.Add(current, 0, 0);
            refresh = ActionButton("Actualiser", false); refresh.Dock = DockStyle.Fill; refresh.Height = 30;
            refresh.Click += delegate { actions.RefreshMonitor(); }; footer.Controls.Add(refresh, 1, 0);
            Button log = ActionButton("Journal", false); log.Dock = DockStyle.Fill; log.Height = 30;
            log.Click += delegate { actions.OpenLog(); }; footer.Controls.Add(log, 2, 0);
            status = TextLabel("Connexion au moniteur…", 8.5f, false); status.ForeColor = Muted; footer.Controls.Add(status, 0, 1);
            startup = new CheckBox { Text = "Lancer avec Windows", Dock = DockStyle.Fill, AutoSize = false, Font = new Font("Segoe UI", 8.5f), Checked = actions.StartupEnabled };
            startup.CheckedChanged += delegate { if (!rendering) actions.SetStartup(startup.Checked); };
            footer.Controls.Add(startup, 1, 1); footer.SetColumnSpan(startup, 2);
            Controls.Add(body); Controls.Add(footer); Controls.Add(header);
            presets.SelectedIndex = 7;
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (!AllowExit && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; actions.HideWindow(); }
            };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) actions.HideWindow(); };
        }

        private void DrawPreset(object sender, DrawItemEventArgs args)
        {
            if (args.Index < 0) return;
            ScreenPreset preset = ScreenPreset.All[args.Index];
            bool chosen = (args.State & DrawItemState.Selected) != 0;
            Color background = chosen ? Ink : Color.White;
            Color foreground = chosen ? Color.White : Ink;
            using (Brush brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
            Rectangle name = new Rectangle(args.Bounds.X + 14, args.Bounds.Y, args.Bounds.Width - 100, args.Bounds.Height);
            Rectangle ratio = new Rectangle(args.Bounds.Right - 102, args.Bounds.Y, 88, args.Bounds.Height);
            TextRenderer.DrawText(args.Graphics, preset.Name, itemFont, name, foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(args.Graphics, preset.Value < 2 ? (preset.Value == 0 ? "Source" : "Plein écran") : preset.Ratio,
                ratioFont, ratio, chosen ? Color.FromArgb(188, 217, 222) : Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
        }
        private void UpdateSelection()
        {
            ScreenPreset preset = ScreenPreset.All[presets.SelectedIndex];
            preview.Preset = preset; selected.Text = preset.Label;
            UpdateButtons();
        }
        private void UpdateButtons()
        {
            bool hasSelection = presets.SelectedIndex >= 0;
            apply.Enabled = !busy && hasSelection && (last == null || (!last.Settings.RestorePending && !(last.GameRunning && last.Settings.Enabled)));
            GameProfile profile = profileCombo == null ? null : profileCombo.SelectedItem as GameProfile;
            save.Enabled = !busy && hasSelection && profile != null && presets.SelectedIndex != profile.TargetMode;
        }
        public void Render(AppSnapshot snapshot, bool working)
        {
            last = snapshot; busy = working; rendering = true;
            try
            {
                if (snapshot.Settings.Profiles == null) snapshot.Settings.Validate();
                string signature = string.Join("|", snapshot.Settings.Profiles.Select(profile => profile.Id + ":" + profile.Name + ":" + profile.TargetMode + ":" + profile.Enabled));
                if (signature != profilesSignature)
                {
                    GameProfile previous = profileCombo.SelectedItem as GameProfile;
                    string selectedId = previous == null ? null : previous.Id;
                    profileCombo.BeginUpdate();
                    try
                    {
                        profileCombo.Items.Clear();
                        foreach (GameProfile profile in snapshot.Settings.Profiles) profileCombo.Items.Add(profile.Copy());
                        int index = snapshot.Settings.Profiles.FindIndex(profile => profile.Id == selectedId);
                        profileCombo.SelectedIndex = index >= 0 ? index : profileCombo.Items.Count > 0 ? 0 : -1;
                    }
                    finally { profileCombo.EndUpdate(); }
                    profilesSignature = signature;
                }
                profileCombo.Enabled = !working && profileCombo.Items.Count > 0; manageProfiles.Enabled = !working;
                automatic.Checked = snapshot.Settings.Enabled;
                automatic.Enabled = !working;
                startup.Checked = actions.StartupEnabled; startup.Enabled = !working;
                autoUpdates.Checked = snapshot.Settings.AutoUpdate; autoUpdates.Enabled = !working;
                target.Text = snapshot.ActiveProfileName == null ? "Priorité au premier jeu détecté" : "Jeu détecté : " + snapshot.ActiveProfileName;
                current.Text = "Format actuel : " + (snapshot.CurrentMode >= 0 ? ScreenPreset.Describe(snapshot.CurrentMode) : "indisponible");
                current.ForeColor = snapshot.Connected ? Ink : Color.FromArgb(163, 79, 38);
                status.Text = working ? "Changement en cours…" : snapshot.Status;
                refresh.Enabled = !working;
                UpdateButtons();
            }
            finally { rendering = false; }
        }
        public void SelectPreset(int value)
        { if (ScreenPreset.IsSelectable(value)) presets.SelectedIndex = value; }
        public void SetUpdateStatus(string text, bool ready, bool checking)
        {
            updateStatus.Text = text;
            checkUpdates.Text = ready ? "Installer la mise à jour" : "Vérifier les mises à jour";
            checkUpdates.Enabled = !checking;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { itemFont.Dispose(); ratioFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    public sealed class ScreenPreview : Control
    {
        private ScreenPreset preset = ScreenPreset.All[7];
        public ScreenPreset Preset { get { return preset; } set { preset = value; Invalidate(); } }
        public ScreenPreview() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            Graphics graphics = args.Graphics; graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float width = Math.Max(40, Math.Min(ClientSize.Width - 54, (ClientSize.Height - 58) * 16f / 9f));
            float height = width * 9f / 16f;
            RectangleF frame = new RectangleF((ClientSize.Width - width) / 2, Math.Max(12, (ClientSize.Height - height - 34) / 2), width, height);
            using (Brush border = new SolidBrush(Color.FromArgb(29, 43, 53))) graphics.FillRectangle(border, frame);
            RectangleF screen = RectangleF.Inflate(frame, -7, -7);
            graphics.FillRectangle(Brushes.Black, screen);
            double nativeWidth = 27 * 16 / Math.Sqrt(337), nativeHeight = 27 * 9 / Math.Sqrt(337);
            double physicalHeight = preset.Inches / Math.Sqrt(preset.Aspect * preset.Aspect + 1);
            double physicalWidth = physicalHeight * preset.Aspect;
            float activeWidth = screen.Width * (float)(physicalWidth / nativeWidth);
            float activeHeight = screen.Height * (float)(physicalHeight / nativeHeight);
            RectangleF active = new RectangleF(screen.X + (screen.Width - activeWidth) / 2, screen.Y + (screen.Height - activeHeight) / 2, activeWidth, activeHeight);
            using (Brush fill = new SolidBrush(Color.FromArgb(46, 107, 120))) graphics.FillRectangle(fill, active);
            using (Pen grid = new Pen(Color.FromArgb(62, 128, 140), 1))
            {
                for (int i = 1; i < 4; i++)
                {
                    graphics.DrawLine(grid, active.X + active.Width * i / 4, active.Top, active.X + active.Width * i / 4, active.Bottom);
                    graphics.DrawLine(grid, active.Left, active.Y + active.Height * i / 4, active.Right, active.Y + active.Height * i / 4);
                }
            }
            using (Font font = new Font("Segoe UI", 16, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString(preset.Value < 2 ? preset.Name : preset.Name.Replace(" Wide", "") + "\n" + preset.Ratio, font, Brushes.White, active, format);
            using (Pen stand = new Pen(Color.FromArgb(29, 43, 53), 5))
            {
                graphics.DrawLine(stand, frame.X + frame.Width / 2, frame.Bottom, frame.X + frame.Width / 2, frame.Bottom + 10);
                graphics.DrawLine(stand, frame.X + frame.Width / 2 - 24, frame.Bottom + 11, frame.X + frame.Width / 2 + 24, frame.Bottom + 11);
            }
            using (Font font = new Font("Segoe UI", 8.5f))
                TextRenderer.DrawText(graphics, "Aperçu indicatif · Le rendu dépend du signal vidéo", font,
                    new Rectangle(0, ClientSize.Height - 22, ClientSize.Width, 20), Color.FromArgb(96, 109, 120), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
