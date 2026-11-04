using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AMZ.ScreenSize
{
    public sealed class ProfilesPanel : UserControl
    {
        private readonly UiGameList list;
        private readonly TextBox name, processes, search;
        private readonly ComboBox format;
        private readonly UiSwitch enabled;
        private readonly UiButton save, cancel, remove, browse, add, refresh;
        private readonly Label message, empty, help, catalogStatus;
        private readonly Panel fields;
        private readonly Timer scanTimer = new Timer { Interval = 200 };
        private readonly ImageList rowImages = new ImageList();
        private Task<List<InstalledGame>> detection, observed;
        private List<InstalledGame> installations = new List<InstalledGame>();
        private readonly HashSet<string> chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<GameProfile> committed, draft;
        private string savedSignature;
        private bool loading, populating, working, scanning;
        private int creationMode = 7;
        public bool Dirty { get; private set; }
        public bool PendingSelection { get { return chosen.Count > 0; } }
        public bool DiscoveryOpen { get { return false; } }
        public bool InteractionActive { get { return Dirty || chosen.Count > 0 || ContainsFocus; } }
        public event EventHandler SaveRequested;

        public ProfilesPanel(IEnumerable<GameProfile> initial, Icon icon, Task<List<InstalledGame>> preloaded)
        {
            detection = preloaded;
            Dock = DockStyle.Fill; Margin = new Padding(0); Font = UiTheme.Body();
            TableLayoutPanel root = UiTheme.Table(1, 44, 16, 0, 36, 44); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            TableLayoutPanel toolbar = UiTheme.Table(4, 0);
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            toolbar.Controls.Add(UiTheme.Label("Rechercher"), 0, 0);
            search = new TextBox { AccessibleName = "Rechercher un jeu, un profil ou un exécutable" };
            Control searchShell = UiTheme.Input(search); searchShell.Margin = new Padding(0, 0, 12, 4); toolbar.Controls.Add(searchShell, 1, 0);
            refresh = UiTheme.Button("Actualiser"); refresh.Dock = DockStyle.Fill; refresh.Margin = new Padding(0, 0, 12, 4);
            refresh.Click += delegate { detection = null; PrepareDetection(); }; toolbar.Controls.Add(refresh, 2, 0);
            add = UiTheme.Button("Ajouter un jeu"); add.Symbol = "plus"; add.Dock = DockStyle.Fill; add.Margin = new Padding(0, 0, 0, 4);
            add.Click += delegate
            {
                GameProfile profile = new GameProfile { Name = "Nouveau jeu", TargetMode = creationMode, Processes = new string[0] };
                draft.Add(profile); search.Text = ""; Changed(); Populate("profile:" + profile.Id); name.Focus(); name.SelectAll();
            };
            toolbar.Controls.Add(add, 3, 0); root.Controls.Add(toolbar, 0, 0);
            TableLayoutPanel editor = UiTheme.Table(2, 0); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            TableLayoutPanel catalog = UiTheme.Table(1, 0, 32); catalog.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); catalog.Margin = new Padding(0, 0, 24, 0);
            list = new UiGameList { Dock = DockStyle.Fill, View = View.Details, HeaderStyle = ColumnHeaderStyle.None, CheckBoxes = true,
                OwnerDraw = true, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None,
                AccessibleName = "Jeux et profils. Cochez les jeux sans profil pour les ajouter ensemble.", Margin = new Padding(0) };
            rowImages.ImageSize = new Size(1, 60); list.SmallImageList = rowImages; list.Columns.Add("Jeux", 252);
            list.Resize += delegate { list.Columns[0].Width = Math.Max(80, list.ClientSize.Width); };
            list.DrawItem += delegate { }; list.DrawSubItem += DrawGame;
            list.SelectedIndexChanged += delegate { if (!populating) ShowSelection(); };
            list.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (populating) return;
                GameChoice choice = (GameChoice)list.Items[e.Index].Tag;
                if (choice.Profile != null) { e.NewValue = CheckState.Checked; return; }
                if (string.IsNullOrEmpty(choice.Installation.ProcessName)) { e.NewValue = CheckState.Unchecked; return; }
                if (e.NewValue == CheckState.Checked) chosen.Add(choice.Key); else chosen.Remove(choice.Key);
                UpdateButtons();
            };
            list.ItemChecked += delegate(object sender, ItemCheckedEventArgs e)
            {
                if (!populating && e.Item.Checked && ((GameChoice)e.Item.Tag).Profile == null) e.Item.Selected = true;
                list.Invalidate();
            };
            catalog.Controls.Add(list, 0, 0);
            catalogStatus = UiTheme.Label("Jeux installés et profils", 9.5f, "muted"); catalog.Controls.Add(catalogStatus, 0, 1);
            editor.Controls.Add(catalog, 0, 0);
            fields = new Panel { Dock = DockStyle.Fill, Tag = "surface", Padding = new Padding(20, 12, 20, 12), Margin = new Padding(0), AutoScroll = true };
            TableLayoutPanel form = UiTheme.Table(1, 24, 40, 28, 40, 40, 24, 40, 52, 0);
            form.Dock = DockStyle.Top; form.AutoSize = true; form.AutoSizeMode = AutoSizeMode.GrowAndShrink; form.MinimumSize = new Size(0, 288);
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            form.Controls.Add(UiTheme.Label("Nom du jeu"), 0, 0);
            name = new TextBox { MaxLength = 80, AccessibleName = "Nom du jeu" }; form.Controls.Add(UiTheme.Input(name), 0, 1);
            form.Controls.Add(UiTheme.Label("Exécutables du jeu"), 0, 2);
            TableLayoutPanel processRow = UiTheme.Table(2, 0); processRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); processRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            processes = new TextBox { AccessibleName = "Exécutables à détecter, séparés par un point-virgule" };
            Control input = UiTheme.Input(processes); input.Margin = new Padding(0, 0, 12, 4); processRow.Controls.Add(input, 0, 0);
            browse = UiTheme.Button("Choisir .exe"); browse.Dock = DockStyle.Fill; browse.Margin = new Padding(0, 0, 0, 4);
            browse.Click += Browse; processRow.Controls.Add(browse, 1, 0); form.Controls.Add(processRow, 0, 3);
            help = UiTheme.Label("", 9.5f, "muted"); form.Controls.Add(help, 0, 4);
            form.Controls.Add(UiTheme.Label("Format à appliquer"), 0, 5); format = UiTheme.Formats("Format du profil"); form.Controls.Add(format, 0, 6);
            enabled = new UiSwitch { Text = "Activer ce profil", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4), AccessibleName = "Activer ce profil" }; form.Controls.Add(enabled, 0, 7);
            fields.Controls.Add(form);
            empty = UiTheme.Label("Choisissez un jeu\n\nVos jeux installés et vos profils sont réunis dans la liste de gauche.", 12, "secondary");
            empty.TextAlign = ContentAlignment.MiddleCenter; fields.Controls.Add(empty); editor.Controls.Add(fields, 1, 0); root.Controls.Add(editor, 0, 2);
            message = UiTheme.Label("", 9.5f, "muted"); root.Controls.Add(message, 0, 3);
            Panel footer = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            remove = UiTheme.Button("Supprimer le profil"); remove.Quiet = true; remove.Destructive = true; remove.Dock = DockStyle.Left; remove.Width = 184;
            remove.Click += delegate
            {
                GameChoice choice = Selection; if (choice == null || choice.Profile == null) return;
                draft.Remove(choice.Profile); Changed(); Populate(null);
            };
            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 336, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
            save = UiTheme.Button("Enregistrer", true); save.Width = 212; save.Click += Save;
            cancel = UiTheme.Button("Annuler"); cancel.Width = 112; cancel.Margin = new Padding(0, 0, 12, 0); cancel.Click += delegate { Replace(committed); };
            bottom.Controls.Add(save); bottom.Controls.Add(cancel); footer.Controls.Add(bottom); footer.Controls.Add(remove); root.Controls.Add(footer, 0, 4); Controls.Add(root);
            name.TextChanged += CaptureSelection; processes.TextChanged += CaptureSelection; format.SelectedIndexChanged += CaptureSelection; enabled.CheckedChanged += CaptureSelection;
            search.TextChanged += delegate { if (!populating) Populate(null); };
            scanTimer.Tick += delegate { CompleteDetection(); };
            committed = initial.Select(p => p.Copy()).ToList(); Replace(committed); UiTheme.Apply(this);
        }
        private GameChoice Selection { get { return list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as GameChoice : null; } }
        private static string Signature(IEnumerable<GameProfile> profiles)
        { return string.Join("|", profiles.Select(p => p.Id + ":" + p.Name + ":" + p.TargetMode + ":" + p.Enabled + ":" + string.Join(";", p.Processes))); }
        public List<GameProfile> ValidatedDraft() { return GameProfile.ValidateAndCopy(draft); }
        public void Render(IEnumerable<GameProfile> values, bool busy)
        {
            working = busy; List<GameProfile> next = values.Select(p => p.Copy()).ToList(); string signature = Signature(next);
            if ((!Dirty && signature != Signature(committed)) || (savedSignature != null && signature == savedSignature)) Replace(next);
            committed = next; UpdateButtons();
        }
        private void Replace(IEnumerable<GameProfile> values)
        {
            draft = values.Select(p => p.Copy()).ToList(); Dirty = false; savedSignature = null; chosen.Clear();
            Populate(null); message.Text = "Sélectionnez un jeu, choisissez son format, puis enregistrez."; message.ForeColor = UiTheme.Muted; UpdateButtons();
        }
        private void Populate(string selectedKey)
        {
            GameChoice previous = Selection; if (selectedKey == null && previous != null) selectedKey = previous.Key;
            List<GameChoice> choices = GameCatalog.Build(draft, installations);
            chosen.IntersectWith(choices.Where(c => c.Profile == null && !string.IsNullOrEmpty(c.Installation.ProcessName)).Select(c => c.Key));
            populating = true; list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (GameChoice choice in choices.Where(c => c.Matches(search.Text)))
                    list.Items.Add(new ListViewItem(choice.Name) { Tag = choice, Checked = choice.Profile != null || chosen.Contains(choice.Key) });
                ListViewItem selected = list.Items.Cast<ListViewItem>().FirstOrDefault(i => ((GameChoice)i.Tag).Key == selectedKey);
                if (selected == null && list.Items.Count > 0) selected = list.Items[0];
                if (selected != null) { selected.Selected = true; selected.EnsureVisible(); }
            }
            finally { list.EndUpdate(); populating = false; }
            catalogStatus.Text = scanning ? "Recherche des jeux installés…" : list.Items.Count + " jeux · " + draft.Count + " profils";
            GameChoice current = Selection;
            if (previous != null && current != null && current.Profile != null && ReferenceEquals(previous.Profile, current.Profile)) UpdateButtons();
            else ShowSelection();
        }
        private int FormatId { get { var p = format.SelectedItem as ScreenPreset; return p == null ? -1 : p.Value; } }

        public void SetFormats(int[] values)
        {
            loading = true;
            try
            {
                format.Items.Clear();
                foreach (int id in values) format.Items.Add(ScreenPreset.Find(id));
                if (!values.Contains(creationMode)) creationMode = values.Contains(7) ? 7 : values.Length == 0 ? -1 : values[0];
            }
            finally { loading = false; }
            ShowSelection();
        }

        private void ShowSelection()
        {
            loading = true;
            try
            {
                GameChoice choice = Selection; GameProfile profile = choice == null ? null : choice.Profile;
                name.Text = choice == null ? "" : choice.Name;
                processes.Text = profile != null ? string.Join(" ; ", profile.Processes.Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : p + ".exe")) :
                    choice != null && !string.IsNullOrEmpty(choice.Installation.ProcessName) ? choice.Installation.ProcessName + ".exe" : "";
                format.SelectedIndex = format.Items.IndexOf(ScreenPreset.Find(profile != null ? profile.TargetMode : creationMode) ?? ScreenPreset.All[0]); enabled.Checked = profile == null || profile.Enabled;
                help.Text = choice == null ? "" : profile != null ? "Plusieurs exécutables ? Séparez-les par « ; ».\nChoisissez le jeu plutôt que son lanceur." :
                    string.IsNullOrEmpty(choice.Installation.ProcessName) ? "Exécutable introuvable : choisissez le fichier .exe du jeu." :
                    choice.Installation.Platform + " · choisissez le format du nouveau profil.\nCochez plusieurs jeux pour les ajouter ensemble.";
                if (profile != null && format.SelectedIndex < 0) help.Text = "Le format enregistré est indisponible. Choisissez un format commun pour ce jeu.";
                empty.Visible = choice == null; if (empty.Visible) empty.BringToFront(); else empty.SendToBack();
            }
            finally { loading = false; }
            UpdateButtons();
        }
        private void CaptureSelection(object sender, EventArgs args)
        {
            if (loading) return; GameChoice choice = Selection;
            if (choice == null || choice.Profile == null)
            {
                if (FormatId >= 0) creationMode = FormatId;
                UpdateButtons(); return;
            }
            choice.Profile.Name = name.Text; choice.Profile.Processes = processes.Text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (FormatId >= 0) choice.Profile.TargetMode = FormatId;
            choice.Profile.Enabled = enabled.Checked;
            list.SelectedItems[0].Text = name.Text; Changed();
        }
        private void Changed() { Dirty = true; savedSignature = null; message.Text = "Modifications non enregistrées"; message.ForeColor = UiTheme.Accent; list.Invalidate(); UpdateButtons(); }
        private List<GameChoice> PendingChoices()
        {
            List<GameChoice> choices = GameCatalog.Build(draft, installations).Where(c => c.Profile == null && chosen.Contains(c.Key)).ToList();
            GameChoice selected = Selection;
            if (choices.Count == 0 && !Dirty && selected != null && selected.Profile == null) choices.Add(selected);
            return choices;
        }
        private void Save(object sender, EventArgs args)
        {
            try
            {
                List<GameProfile> additions = creationMode >= 0 ? GameCatalog.Create(PendingChoices(), creationMode) : new List<GameProfile>();
                if (additions.Count > 0)
                {
                    draft.AddRange(additions); Dirty = true; chosen.Clear(); Populate("profile:" + additions[0].Id);
                }
                List<GameProfile> valid = ValidatedDraft(); savedSignature = Signature(valid);
                if (SaveRequested != null) SaveRequested(this, EventArgs.Empty);
            }
            catch (IOException error) { message.Text = error.Message; message.ForeColor = UiTheme.Danger; UpdateButtons(); }
        }
        private void UpdateButtons()
        {
            GameChoice choice = Selection; bool exists = choice != null, configured = exists && choice.Profile != null;
            name.Enabled = processes.Enabled = exists && !working; name.ReadOnly = processes.ReadOnly = !configured;
            format.Enabled = enabled.Enabled = browse.Enabled = exists && !working; enabled.Visible = configured;
            remove.Enabled = configured && !working;
            add.Enabled = !working && creationMode >= 0; search.Enabled = list.Enabled = !working; refresh.Enabled = !working && !scanning;
            int count = creationMode < 0 ? 0 : PendingChoices().Count(c => !string.IsNullOrEmpty(c.Installation.ProcessName));
            save.Text = count > 0 ? count == 1 ? "Créer le profil" : "Créer " + count + " profils" : "Enregistrer";
            save.Enabled = !working && (Dirty || count > 0); cancel.Enabled = !working && (Dirty || chosen.Count > 0);
        }
        private void Browse(object sender, EventArgs e)
        {
            GameChoice choice = Selection; if (choice == null) return;
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "Choisir l’exécutable du jeu", Filter = "Applications (*.exe)|*.exe", CheckFileExists = true,
                InitialDirectory = choice.Installation == null ? "" : choice.Installation.InstallDirectory })
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    if (choice.Profile != null) processes.Text = Path.GetFileName(dialog.FileName);
                    else { choice.Installation.Executable = dialog.FileName; string key = choice.Key; Populate(key); }
                }
        }
        private void DrawGame(object sender, DrawListViewSubItemEventArgs e)
        {
            GameChoice choice = (GameChoice)e.Item.Tag; float s = e.Graphics.DpiX / 96; bool selected = e.Item.Selected;
            using (Brush brush = new SolidBrush(selected ? UiTheme.Selection : UiTheme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            int size = (int)(16 * s), x = e.Bounds.X + (int)(4 * s), y = e.Bounds.Y + (e.Bounds.Height - size) / 2;
            Color checkColor = choice.Profile != null ? UiTheme.Muted : UiTheme.Accent;
            using (Pen pen = new Pen(e.Item.Checked ? checkColor : UiTheme.Muted, 1.4f * s))
            {
                if (choice.Profile == null) e.Graphics.DrawRectangle(pen, x, y, size, size);
                if (e.Item.Checked) e.Graphics.DrawLines(pen, new[] { new Point(x + (int)(3 * s), y + size / 2), new Point(x + (int)(7 * s), y + size - (int)(4 * s)), new Point(x + size - (int)(2 * s), y + (int)(4 * s)) });
            }
            int textX = e.Bounds.X + (int)(32 * s), lineHeight = Font.Height + (int)(4 * s);
            Rectangle first = new Rectangle(textX, e.Bounds.Y + (int)(8 * s), e.Bounds.Right - textX - (int)(8 * s), lineHeight);
            TextRenderer.DrawText(e.Graphics, choice.Name, Font, first, selected ? UiTheme.Accent : UiTheme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            using (Font small = UiTheme.Body(9.5f)) TextRenderer.DrawText(e.Graphics, choice.Detail, small, new Rectangle(first.X, first.Bottom, first.Width, lineHeight),
                UiTheme.Secondary, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            if (e.Item.Focused && list.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -1, -1), UiTheme.Accent, UiTheme.Surface);
        }
        public void PrepareDetection()
        {
            if (detection == null) detection = Task.Factory.StartNew(delegate { return GameDiscovery.Discover(GameDiscovery.SystemOptions()); });
            if (ReferenceEquals(observed, detection)) return;
            scanning = true; refresh.Enabled = false; catalogStatus.Text = "Recherche des jeux installés…"; scanTimer.Start(); CompleteDetection();
        }
        private void CompleteDetection()
        {
            if (detection == null || !detection.IsCompleted || ReferenceEquals(observed, detection)) return;
            observed = detection; scanning = false; scanTimer.Stop();
            if (detection.IsFaulted) { message.Text = "Détection impossible : " + detection.Exception.GetBaseException().Message; message.ForeColor = UiTheme.Danger; }
            else if (!detection.IsCanceled) { installations = detection.Result; Populate(null); }
            UpdateButtons();
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { scanTimer.Dispose(); rowImages.Dispose(); } base.Dispose(disposing); }
    }
    public sealed class UiGameList : ListView
    {
        public UiGameList() { DoubleBuffered = true; }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ResizeRows(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ResizeRows(); }
        private void ResizeRows()
        { if (SmallImageList != null) SmallImageList.ImageSize = new Size(1, Math.Min(256, Math.Max(60, Font.Height * 3 + 8))); }
    }
}
