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
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 249);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(940, 560); MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), RowCount = 5, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            status = new Label { Text = "Détection des jeux installés…", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            root.Controls.Add(status, 0, 0);
            TableLayoutPanel search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132)); search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            search.Controls.Add(new Label { Text = "Rechercher un jeu", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            filter = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Rechercher un jeu installé" }; filter.TextChanged += delegate { Populate(); };
            refresh = new Button { Text = "Actualiser", Dock = DockStyle.Fill }; refresh.Click += delegate { StartScan(null); };
            search.Controls.Add(filter, 1, 0); search.Controls.Add(refresh, 2, 0); root.Controls.Add(search, 0, 1);
            games = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, ShowItemToolTips = true, AccessibleName = "Jeux installés détectés" };
            games.Columns.Add("Jeu", 300); games.Columns.Add("Plateforme", 170); games.Columns.Add("Processus du jeu", 350);
            games.ItemCheck += delegate(object sender, ItemCheckEventArgs args)
            {
                if (updating) return;
                InstalledGame game = (InstalledGame)games.Items[args.Index].Tag;
                if (string.IsNullOrEmpty(game.ProcessName) || existing.Contains(game.ProcessName)) { args.NewValue = CheckState.Unchecked; return; }
                if (args.NewValue == CheckState.Checked) chosen.Add(Key(game)); else chosen.Remove(Key(game));
                import.Enabled = chosen.Count > 0 && !scanning;
            };
            games.DoubleClick += delegate { ChooseExecutable(); };
            root.Controls.Add(games, 0, 2);
            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            Button executable = new Button { Text = "Choisir un .exe…", Width = 158, Height = 30 }; executable.Click += delegate { ChooseExecutable(); };
            options.Controls.Add(executable);
            options.Controls.Add(new Label { Text = "Format des profils ajoutés", Width = 198, Height = 30, TextAlign = ContentAlignment.MiddleRight });
            format = new ComboBox { Width = 242, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Format des jeux détectés" };
            foreach (ScreenPreset preset in ScreenPreset.All) format.Items.Add(preset);
            format.SelectedIndex = ScreenPreset.IsSelectable(defaultMode) ? defaultMode : 7; options.Controls.Add(format); root.Controls.Add(options, 0, 3);
            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 0) };
            Button cancel = new Button { Text = "Annuler", Width = 110, Height = 32, DialogResult = DialogResult.Cancel };
            import = new Button { Text = "Créer les profils sélectionnés", Width = 245, Height = 32, Enabled = false };
            import.Click += delegate
            {
                Result = detected.Where(game => chosen.Contains(Key(game)) && !string.IsNullOrEmpty(game.ProcessName) && !existing.Contains(game.ProcessName))
                    .GroupBy(game => game.ProcessName, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                    .Select(game => new GameProfile { Name = game.Name, Processes = new[] { game.ProcessName }, TargetMode = format.SelectedIndex }).ToList();
                if (Result.Count > 0) { DialogResult = DialogResult.OK; Close(); }
            };
            bottom.Controls.Add(cancel); bottom.Controls.Add(import); root.Controls.Add(bottom, 0, 4); Controls.Add(root);
            CancelButton = cancel; AcceptButton = import;
            Shown += delegate { StartScan(initial); initial = null; };
        }
        private static string Key(InstalledGame game) { return game.InstallDirectory; }
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
                        detected = result.Result; Populate();
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
            import.Enabled = chosen.Count > 0 && !scanning;
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
