using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AMZ.ScreenSize
{
    public sealed class GameDiscoveryForm : Form
    {
        private readonly ListView games;
        private readonly TextBox filter;
        private readonly ComboBox format;
        private readonly Label status;
        private readonly Button import, refresh;
        private readonly HashSet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<InstalledGame> detected = new List<InstalledGame>();
        private Task<List<InstalledGame>> initial;
        private bool updating, scanning;
        public List<GameProfile> Result { get; private set; }
        public GameDiscoveryForm(IEnumerable<GameProfile> profiles, int defaultMode, Icon icon, Task<List<InstalledGame>> detection)
        {
            initial = detection;
            foreach (GameProfile profile in profiles)
                foreach (string process in profile.Processes ?? new string[0])
                    try { existing.Add(GameProfile.NormalizeProcess(process)); } catch (IOException) { }
            Text = "Jeux installés · AMZ.ScreenSize"; Icon = icon;
            Font = UiTheme.Body();
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(1000, 660); MinimumSize = new Size(960, 620);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), RowCount = 7, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            root.Controls.Add(UiTheme.Label("Ajouter des jeux installés", 24), 0, 0);
            root.Controls.Add(UiTheme.Label("Sélectionnez vos jeux, puis choisissez leur format de départ.", 10.5f, "secondary"), 0, 1);
            status = UiTheme.Label("Détection des jeux installés…", 9.5f, "muted");
            root.Controls.Add(status, 0, 4);
            TableLayoutPanel search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            search.RowCount = 1; search.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148)); search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
            search.Controls.Add(UiTheme.Label("Rechercher un jeu"), 0, 0);
            filter = new TextBox { AccessibleName = "Rechercher un jeu installé" }; filter.TextChanged += delegate { Populate(); };
            refresh = UiTheme.Button("Actualiser"); refresh.Dock = DockStyle.Fill; refresh.Margin = new Padding(12, 0, 0, 12); refresh.Click += delegate { StartScan(null); };
            Control filterShell = UiTheme.Input(filter); filterShell.Margin = new Padding(0, 0, 0, 12);
            search.Controls.Add(filterShell, 1, 0); search.Controls.Add(refresh, 2, 0); root.Controls.Add(search, 0, 2);
            games = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, ShowItemToolTips = true,
                BorderStyle = BorderStyle.None, OwnerDraw = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, AccessibleName = "Jeux installés détectés", Margin = new Padding(0) };
            games.Columns.Add("Jeu", 324); games.Columns.Add("Plateforme", 168); games.Columns.Add("Exécutable", 412);
            ImageList rowHeight = new ImageList { ImageSize = new Size(1, 40) }; games.SmallImageList = rowHeight;
            FormClosed += delegate { rowHeight.Dispose(); };
            games.DrawColumnHeader += DrawHeader;
            games.DrawItem += delegate { };
            games.DrawSubItem += DrawCell;
            games.Resize += delegate { if (games.Columns.Count == 3) games.Columns[2].Width = Math.Max(160, games.ClientSize.Width - games.Columns[0].Width - games.Columns[1].Width); };
            games.ItemCheck += delegate(object sender, ItemCheckEventArgs args)
            {
                if (updating) return;
                InstalledGame game = (InstalledGame)games.Items[args.Index].Tag;
                if (string.IsNullOrEmpty(game.ProcessName) || existing.Contains(game.ProcessName)) { args.NewValue = CheckState.Unchecked; return; }
                if (args.NewValue == CheckState.Checked) chosen.Add(Key(game)); else chosen.Remove(Key(game));
                UpdateImport(); games.Invalidate();
            };
            games.DoubleClick += delegate { ChooseExecutable(); };
            root.Controls.Add(games, 0, 3);
            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            Button executable = UiTheme.Button("Choisir un .exe"); executable.Width = 160; executable.Margin = new Padding(0, 0, 20, 0); executable.Click += delegate { ChooseExecutable(); };
            options.Controls.Add(executable);
            options.Controls.Add(new Label { Text = "Format des nouveaux profils", Width = 224, Height = 40, TextAlign = ContentAlignment.MiddleLeft });
            format = UiTheme.Formats("Format des jeux détectés"); format.Dock = DockStyle.None; format.Width = 252;
            format.SelectedIndex = ScreenPreset.IsSelectable(defaultMode) ? defaultMode : 7; options.Controls.Add(format); root.Controls.Add(options, 0, 5);
            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 0) };
            Button cancel = UiTheme.Button("Annuler"); cancel.Width = 112; cancel.DialogResult = DialogResult.Cancel;
            import = UiTheme.Button("Ajouter la sélection", true); import.Width = 240; import.Margin = new Padding(0, 0, 12, 0); import.Enabled = false;
            import.Click += delegate
            {
                Result = detected.Where(game => chosen.Contains(Key(game)) && !string.IsNullOrEmpty(game.ProcessName) && !existing.Contains(game.ProcessName))
                    .GroupBy(game => game.ProcessName, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                    .Select(game => new GameProfile { Name = game.Name, Processes = new[] { game.ProcessName }, TargetMode = format.SelectedIndex }).ToList();
                if (Result.Count > 0) { DialogResult = DialogResult.OK; Close(); }
            };
            bottom.Controls.Add(cancel); bottom.Controls.Add(import); root.Controls.Add(bottom, 0, 6); Controls.Add(root);
            CancelButton = cancel; AcceptButton = import;
            UiTheme.Apply(this); HandleCreated += delegate { UiTheme.Caption(Handle); };
            Shown += delegate { StartScan(initial); initial = null; };
        }
        private static string Key(InstalledGame game) { return game.InstallDirectory; }
        private void UpdateImport()
        { import.Enabled = chosen.Count > 0 && !scanning; import.Text = chosen.Count > 0 ? "Ajouter " + chosen.Count + (chosen.Count == 1 ? " profil" : " profils") : "Ajouter la sélection"; }
        private void DrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (Brush b = new SolidBrush(UiTheme.Raised)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, Rectangle.Inflate(e.Bounds, -12, 0), UiTheme.Secondary, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        private void DrawCell(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item.Selected; float s = e.Graphics.DpiX / 96;
            using (Brush b = new SolidBrush(selected ? UiTheme.Selection : UiTheme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            Rectangle text = Rectangle.Inflate(e.Bounds, -(int)(12 * s), 0);
            if (e.ColumnIndex == 0)
            {
                Rectangle box = new Rectangle(e.Bounds.X + (int)(12 * s), e.Bounds.Y + (e.Bounds.Height - (int)(16 * s)) / 2, (int)(16 * s), (int)(16 * s));
                using (Pen p = new Pen(e.Item.Checked ? UiTheme.Accent : UiTheme.Muted, 1.5f * s))
                {
                    e.Graphics.DrawRectangle(p, box);
                    if (e.Item.Checked) e.Graphics.DrawLines(p, new[] { new Point(box.Left + 3, box.Top + box.Height / 2), new Point(box.Left + box.Width / 2 - 1, box.Bottom - 4), new Point(box.Right - 3, box.Top + 4) });
                }
                text.X += (int)(28 * s); text.Width -= (int)(28 * s);
            }
            InstalledGame game = (InstalledGame)e.Item.Tag;
            Color ink = existing.Contains(game.ProcessName) ? UiTheme.Muted : e.ColumnIndex == 0 ? UiTheme.Text : UiTheme.Secondary;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, text, ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (e.ColumnIndex == 0 && e.Item.Focused && games.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), UiTheme.Accent, UiTheme.Surface);
        }
        private void StartScan(Task<List<InstalledGame>> pending)
        {
            if (scanning) return;
            scanning = true; refresh.Enabled = false; import.Enabled = false; status.Text = "Détection Steam, Epic Games, GOG et Xbox…";
            Task<List<InstalledGame>> task = pending ?? Task.Factory.StartNew(delegate { return GameDiscovery.Discover(GameDiscovery.SystemOptions()); });
            task.ContinueWith(delegate(Task<List<InstalledGame>> result)
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        scanning = false; refresh.Enabled = true;
                        if (result.IsFaulted) { status.Text = "La détection a échoué : " + result.Exception.GetBaseException().Message; return; }
                        detected = result.Result;
                        chosen.IntersectWith(detected.Where(game => !string.IsNullOrEmpty(game.ProcessName) && !existing.Contains(game.ProcessName)).Select(Key));
                        Populate();
                        status.Text = detected.Count + " jeux trouvés · " + detected.Count(game => !string.IsNullOrEmpty(game.ProcessName)) + " exécutables identifiés · cochez les jeux à ajouter";
                    });
                }
                catch (InvalidOperationException) { }
            });
        }
        private void Populate()
        {
            updating = true; games.BeginUpdate();
            try
            {
                games.Items.Clear();
                foreach (InstalledGame game in detected.Where(game => game.Name.IndexOf(filter.Text, StringComparison.CurrentCultureIgnoreCase) >= 0 || game.Platform.IndexOf(filter.Text, StringComparison.CurrentCultureIgnoreCase) >= 0))
                {
                    bool already = existing.Contains(game.ProcessName);
                    ListViewItem item = new ListViewItem(game.Name) { Tag = game, Checked = !already && chosen.Contains(Key(game)), ToolTipText = game.InstallDirectory };
                    item.SubItems.Add(game.Platform);
                    item.SubItems.Add(already ? game.ProcessName + ".exe · profil existant" : string.IsNullOrEmpty(game.ProcessName) ? "À choisir avec le bouton .exe" : game.ProcessName + ".exe");
                    if (already) item.ForeColor = Color.Gray;
                    games.Items.Add(item);
                }
            }
            finally { games.EndUpdate(); updating = false; }
            UpdateImport();
        }
        private void ChooseExecutable()
        {
            if (games.SelectedItems.Count != 1) return;
            InstalledGame game = (InstalledGame)games.SelectedItems[0].Tag;
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "Choisir le processus de " + game.Name, Filter = "Applications (*.exe)|*.exe", InitialDirectory = game.InstallDirectory, CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) { game.Executable = dialog.FileName; Populate(); }
        }
    }
}
