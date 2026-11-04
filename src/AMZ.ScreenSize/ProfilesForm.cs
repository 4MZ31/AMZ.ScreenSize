using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace AMZ.ScreenSize
{
    public sealed class ProfilesForm : Form
    {
        private readonly ListBox list;
        private readonly TextBox name, processes;
        private readonly ComboBox format;
        private readonly CheckBox enabled;
        private readonly Button remove, browse;
        private bool loading;
        private Task<List<InstalledGame>> detection;
        public List<GameProfile> Result { get; private set; }
        public ProfilesForm(IEnumerable<GameProfile> profiles, Icon icon, Task<List<InstalledGame>> preloadedGames = null)
        {
            Text = "Profils de jeux · AMZ.ScreenSize"; Icon = icon;
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 249);
            ForeColor = Color.FromArgb(29, 43, 53);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(850, 480);
            MinimumSize = new Size(820, 520); StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 18, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Button discover = new Button { Text = "Jeux installés…", Dock = DockStyle.Fill };
            discover.Click += delegate
            {
                using (GameDiscoveryForm dialog = new GameDiscoveryForm(list.Items.Cast<GameProfile>(), format.SelectedIndex, Icon, detection))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        foreach (GameProfile profile in dialog.Result) list.Items.Add(profile);
                        if (list.Items.Count > 0) list.SelectedIndex = list.Items.Count - 1;
                    }
            };
            left.Controls.Add(discover, 0, 0);
            list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 56,
                BorderStyle = BorderStyle.None, BackColor = Color.White, AccessibleName = "Profils de jeux" };
            foreach (GameProfile profile in profiles) list.Items.Add(profile.Copy());
            list.DrawItem += delegate(object sender, DrawItemEventArgs args)
            {
                if (args.Index < 0) return;
                GameProfile profile = (GameProfile)list.Items[args.Index];
                bool selected = (args.State & DrawItemState.Selected) != 0;
                Color ink = Color.FromArgb(29, 43, 53);
                using (Brush background = new SolidBrush(selected ? ink : Color.White)) args.Graphics.FillRectangle(background, args.Bounds);
                TextRenderer.DrawText(args.Graphics, profile.Name, Font, new Rectangle(args.Bounds.X + 10, args.Bounds.Y + 6, args.Bounds.Width - 20, 22), selected ? Color.White : ink, TextFormatFlags.EndEllipsis);
                using (Font small = new Font("Segoe UI", 8.5f)) TextRenderer.DrawText(args.Graphics,
                    ScreenPreset.Describe(profile.TargetMode) + (profile.Enabled ? "" : " · désactivé"), small,
                    new Rectangle(args.Bounds.X + 10, args.Bounds.Y + 29, args.Bounds.Width - 20, 20), selected ? Color.FromArgb(188, 217, 222) : Color.DimGray, TextFormatFlags.EndEllipsis);
            };
            list.SelectedIndexChanged += delegate { ShowSelection(); };
            left.Controls.Add(list, 0, 1);
            FlowLayoutPanel listButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            Button add = new Button { Text = "Ajouter un jeu", Width = 132, Height = 32 };
            remove = new Button { Text = "Supprimer", Width = 118, Height = 32 };
            add.Click += delegate
            {
                list.Items.Add(new GameProfile { Name = "Nouveau jeu" }); list.SelectedIndex = list.Items.Count - 1; name.Focus(); name.SelectAll();
            };
            remove.Click += delegate
            {
                int selected = list.SelectedIndex;
                if (selected < 0) return;
                list.Items.RemoveAt(selected);
                if (list.Items.Count > 0) list.SelectedIndex = Math.Min(selected, list.Items.Count - 1);
                else ShowSelection();
            };
            listButtons.Controls.Add(add); listButtons.Controls.Add(remove); left.Controls.Add(listButtons, 0, 2);
            root.Controls.Add(left, 0, 0);

            TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Margin = new Padding(0) };
            foreach (int height in new[] { 28, 36, 28, 36, 48, 28, 36, 34 }) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fields.Controls.Add(Label("Nom du jeu"), 0, 0);
            name = new TextBox { Dock = DockStyle.Fill, MaxLength = 80, AccessibleName = "Nom du jeu" }; fields.Controls.Add(name, 0, 1);
            fields.Controls.Add(Label("Processus à détecter"), 0, 2);
            TableLayoutPanel processRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            processRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); processRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            processes = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Processus à détecter" };
            browse = new Button { Text = "Choisir .exe…", Dock = DockStyle.Fill };
            browse.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog { Title = "Choisir l’exécutable du jeu", Filter = "Applications (*.exe)|*.exe", CheckFileExists = true, Multiselect = false })
                    if (dialog.ShowDialog(this) == DialogResult.OK) processes.Text = Path.GetFileName(dialog.FileName);
            };
            processRow.Controls.Add(processes, 0, 0); processRow.Controls.Add(browse, 1, 0); fields.Controls.Add(processRow, 0, 3);
            Label explanation = Label("Exemple : game.exe ; game-win64.exe\nChoisissez le jeu, sans son lanceur Steam ou Epic."); explanation.Font = new Font("Segoe UI", 8.5f); explanation.ForeColor = Color.DimGray;
            fields.Controls.Add(explanation, 0, 4);
            fields.Controls.Add(Label("Format d’écran pour ce jeu"), 0, 5);
            format = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Format du profil" };
            foreach (ScreenPreset preset in ScreenPreset.All) format.Items.Add(preset);
            fields.Controls.Add(format, 0, 6);
            enabled = new CheckBox { Text = "Activer ce profil", Dock = DockStyle.Fill }; fields.Controls.Add(enabled, 0, 7);
            Label priority = Label("Le premier jeu détecté garde la priorité. Le format d’origine revient après la fermeture du dernier jeu géré.");
            priority.Font = new Font("Segoe UI", 9); priority.ForeColor = Color.DimGray; priority.TextAlign = ContentAlignment.TopLeft;
            fields.Controls.Add(priority, 0, 8); root.Controls.Add(fields, 1, 0);
            name.TextChanged += delegate { CaptureSelection(); };
            processes.TextChanged += delegate { CaptureSelection(); };
            format.SelectedIndexChanged += delegate { CaptureSelection(); };
            enabled.CheckedChanged += delegate { CaptureSelection(); };
            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 14, 0, 0) };
            Button cancel = new Button { Text = "Annuler", Width = 110, Height = 34, DialogResult = DialogResult.Cancel };
            Button save = new Button { Text = "Enregistrer les profils", Width = 190, Height = 34 };
            save.Click += delegate
            {
                try { Result = GameProfile.ValidateAndCopy(list.Items.Cast<GameProfile>()); DialogResult = DialogResult.OK; Close(); }
                catch (IOException error) { MessageBox.Show(this, error.Message, "Vérifier les profils", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            bottom.Controls.Add(cancel); bottom.Controls.Add(save); root.Controls.Add(bottom, 0, 1); root.SetColumnSpan(bottom, 2);
            Controls.Add(root); AcceptButton = save; CancelButton = cancel;
            Shown += delegate
            {
                detection = preloadedGames ?? Task.Factory.StartNew(delegate { return GameDiscovery.Discover(GameDiscovery.SystemOptions()); });
                detection.ContinueWith(delegate(Task<List<InstalledGame>> result)
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    try { BeginInvoke((Action)delegate { if (!result.IsFaulted) discover.Text = "Jeux installés (" + result.Result.Count + ")…"; }); }
                    catch (InvalidOperationException) { }
                });
            };
            if (list.Items.Count > 0) list.SelectedIndex = 0; else ShowSelection();
        }
        private static Label Label(string text)
        { return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }; }
        private void ShowSelection()
        {
            loading = true;
            try
            {
                GameProfile profile = list.SelectedItem as GameProfile;
                bool exists = profile != null;
                name.Enabled = processes.Enabled = format.Enabled = enabled.Enabled = browse.Enabled = remove.Enabled = exists;
                name.Text = exists ? profile.Name : "";
                processes.Text = exists ? string.Join(" ; ", profile.Processes.Select(value => value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value : value + ".exe")) : "";
                format.SelectedIndex = exists ? profile.TargetMode : -1; enabled.Checked = exists && profile.Enabled;
            }
            finally { loading = false; }
        }
        private void CaptureSelection()
        {
            if (loading) return;
            GameProfile profile = list.SelectedItem as GameProfile;
            if (profile == null) return;
            profile.Name = name.Text; profile.Processes = processes.Text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            profile.TargetMode = format.SelectedIndex; profile.Enabled = enabled.Checked; list.Invalidate();
        }
    }
}
