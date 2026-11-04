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
        private readonly ListBox list;
        private readonly TextBox name, processes;
        private readonly ComboBox format;
        private readonly UiSwitch enabled;
        private readonly UiButton save, cancel, remove, browse, add, discover;
        private readonly Label message, empty;
        private readonly Panel fields;
        private readonly Icon icon;
        private Task<List<InstalledGame>> detection;
        private List<GameProfile> committed;
        private string savedSignature;
        private bool loading, working;
        public bool Dirty { get; private set; }
        public bool DiscoveryOpen { get; private set; }
        public event EventHandler SaveRequested;
        public ProfilesPanel(IEnumerable<GameProfile> initial, Icon icon, Task<List<InstalledGame>> preloaded)
        {
            this.icon = icon; detection = preloaded;
            Dock = DockStyle.Fill; Margin = new Padding(0); Font = UiTheme.Body();
            TableLayoutPanel root = UiTheme.Table(1, 44, 16, 0, 36, 44); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            discover = UiTheme.Button("Détecter les jeux"); discover.Width = 180; discover.Symbol = "search"; discover.Margin = new Padding(0, 0, 12, 0); discover.Click += Discover;
            add = UiTheme.Button("Ajouter un jeu"); add.Width = 172; add.Symbol = "plus"; add.Click += delegate
            {
                list.Items.Add(new GameProfile { Name = "Nouveau jeu" }); list.SelectedIndex = list.Items.Count - 1; Changed(); name.Focus(); name.SelectAll();
            };
            toolbar.Controls.Add(discover); toolbar.Controls.Add(add); root.Controls.Add(toolbar, 0, 0);
            TableLayoutPanel editor = UiTheme.Table(2, 0); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 252)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 64, IntegralHeight = false, BorderStyle = BorderStyle.None, Margin = new Padding(0, 0, 24, 0), AccessibleName = "Profils de jeux enregistrés" };
            list.DrawItem += DrawProfile; list.SelectedIndexChanged += delegate { ShowSelection(); }; editor.Controls.Add(list, 0, 0);
            fields = new Panel { Dock = DockStyle.Fill, Tag = "surface", Padding = new Padding(20, 12, 20, 12), Margin = new Padding(0) };
            TableLayoutPanel form = UiTheme.Table(1, 24, 40, 28, 40, 40, 28, 40, 40, 0); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            form.Controls.Add(UiTheme.Label("Nom du jeu"), 0, 0);
            name = new TextBox { MaxLength = 80, AccessibleName = "Nom du jeu" }; form.Controls.Add(UiTheme.Input(name), 0, 1);
            form.Controls.Add(UiTheme.Label("Exécutables du jeu"), 0, 2);
            TableLayoutPanel processRow = UiTheme.Table(2, 0); processRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); processRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            processes = new TextBox { AccessibleName = "Exécutables à détecter, séparés par un point-virgule" };
            Control input = UiTheme.Input(processes); input.Margin = new Padding(0, 0, 12, 4); processRow.Controls.Add(input, 0, 0);
            browse = UiTheme.Button("Choisir .exe"); browse.Dock = DockStyle.Fill; browse.Margin = new Padding(0, 0, 0, 4);
            browse.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog { Title = "Choisir l’exécutable du jeu", Filter = "Applications (*.exe)|*.exe", CheckFileExists = true })
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK) processes.Text = Path.GetFileName(dialog.FileName);
            };
            processRow.Controls.Add(browse, 1, 0); form.Controls.Add(processRow, 0, 3);
            Label help = UiTheme.Label("Plusieurs exécutables ? Séparez-les par « ; ».\nChoisissez le jeu plutôt que son lanceur.", 9.5f, "muted"); form.Controls.Add(help, 0, 4);
            form.Controls.Add(UiTheme.Label("Format à appliquer"), 0, 5); format = UiTheme.Formats("Format du profil"); form.Controls.Add(format, 0, 6);
            enabled = new UiSwitch { Text = "Activer ce profil", Dock = DockStyle.Fill, AccessibleName = "Activer ce profil" }; form.Controls.Add(enabled, 0, 7);
            remove = UiTheme.Button("Supprimer le profil"); remove.Quiet = true; remove.Destructive = true; remove.Dock = DockStyle.Left; remove.Width = 184;
            remove.Click += delegate
            {
                int index = list.SelectedIndex; if (index < 0) return;
                list.Items.RemoveAt(index); if (list.Items.Count > 0) list.SelectedIndex = Math.Min(index, list.Items.Count - 1); else ShowSelection(); Changed();
            };
            fields.Controls.Add(form);
            empty = UiTheme.Label("Aucun profil\n\nDétectez vos jeux installés ou ajoutez un exécutable pour commencer.", 12, "secondary"); empty.TextAlign = ContentAlignment.MiddleCenter; fields.Controls.Add(empty);
            editor.Controls.Add(fields, 1, 0); root.Controls.Add(editor, 0, 2);
            message = UiTheme.Label("Les changements restent en brouillon jusqu’à l’enregistrement.", 9.5f, "muted"); root.Controls.Add(message, 0, 3);
            Panel footer = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 272, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
            save = UiTheme.Button("Enregistrer", true); save.Width = 148; save.Click += delegate
            {
                try
                {
                    List<GameProfile> valid = ValidatedDraft(); savedSignature = Signature(valid);
                    if (SaveRequested != null) SaveRequested(this, EventArgs.Empty);
                }
                catch (IOException error) { message.Text = error.Message; message.ForeColor = UiTheme.Danger; }
            };
            cancel = UiTheme.Button("Annuler"); cancel.Width = 112; cancel.Margin = new Padding(0, 0, 12, 0); cancel.Click += delegate { Replace(committed); };
            bottom.Controls.Add(save); bottom.Controls.Add(cancel); footer.Controls.Add(bottom); footer.Controls.Add(remove); root.Controls.Add(footer, 0, 4); Controls.Add(root);
            name.TextChanged += CaptureSelection; processes.TextChanged += CaptureSelection; format.SelectedIndexChanged += CaptureSelection; enabled.CheckedChanged += CaptureSelection;
            committed = initial.Select(p => p.Copy()).ToList(); Replace(committed); UiTheme.Apply(this);
        }
        private static string Signature(IEnumerable<GameProfile> profiles)
        { return string.Join("|", profiles.Select(p => p.Id + ":" + p.Name + ":" + p.TargetMode + ":" + p.Enabled + ":" + string.Join(";", p.Processes))); }
        public List<GameProfile> ValidatedDraft() { return GameProfile.ValidateAndCopy(list.Items.Cast<GameProfile>()); }
        public void Render(IEnumerable<GameProfile> values, bool busy)
        {
            working = busy; List<GameProfile> next = values.Select(p => p.Copy()).ToList();
            string signature = Signature(next);
            if ((!Dirty && signature != Signature(committed)) || (savedSignature != null && signature == savedSignature)) { committed = next; Replace(next); }
            committed = next;
            UpdateButtons();
        }
        private void Replace(IEnumerable<GameProfile> values)
        {
            GameProfile selected = list.SelectedItem as GameProfile; string id = selected == null ? null : selected.Id;
            loading = true; list.BeginUpdate();
            try
            {
                list.Items.Clear(); foreach (GameProfile profile in values) list.Items.Add(profile.Copy());
                int index = list.Items.Cast<GameProfile>().ToList().FindIndex(p => p.Id == id); list.SelectedIndex = index >= 0 ? index : list.Items.Count > 0 ? 0 : -1;
            }
            finally { list.EndUpdate(); loading = false; }
            Dirty = false; savedSignature = null; ShowSelection(); message.Text = "Le premier jeu détecté garde la priorité jusqu’à sa fermeture."; message.ForeColor = UiTheme.Muted; UpdateButtons();
        }
        private void ShowSelection()
        {
            loading = true;
            try
            {
                GameProfile profile = list.SelectedItem as GameProfile; bool exists = profile != null;
                name.Text = exists ? profile.Name : "";
                processes.Text = exists ? string.Join(" ; ", profile.Processes.Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : p + ".exe")) : "";
                format.SelectedIndex = exists ? profile.TargetMode : -1; enabled.Checked = exists && profile.Enabled;
                empty.Visible = !exists; if (!exists) empty.BringToFront(); else empty.SendToBack();
            }
            finally { loading = false; }
            UpdateButtons();
        }
        private void CaptureSelection(object sender, EventArgs args)
        {
            if (loading) return; GameProfile profile = list.SelectedItem as GameProfile; if (profile == null) return;
            profile.Name = name.Text; profile.Processes = processes.Text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            profile.TargetMode = format.SelectedIndex; profile.Enabled = enabled.Checked; Changed();
        }
        private void Changed() { Dirty = true; savedSignature = null; message.Text = "Modifications non enregistrées"; message.ForeColor = UiTheme.Accent; list.Invalidate(); UpdateButtons(); }
        private void UpdateButtons()
        {
            bool exists = list.SelectedIndex >= 0;
            name.Enabled = processes.Enabled = format.Enabled = enabled.Enabled = browse.Enabled = remove.Enabled = exists && !working;
            save.Enabled = cancel.Enabled = Dirty && !working; add.Enabled = discover.Enabled = !working;
        }
        private void DrawProfile(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return; GameProfile profile = (GameProfile)list.Items[e.Index]; bool chosen = (e.State & DrawItemState.Selected) != 0; float s = e.Graphics.DpiX / 96;
            using (Brush b = new SolidBrush(chosen ? UiTheme.Selection : UiTheme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, profile.Name, Font, new Rectangle(e.Bounds.X + (int)(16 * s), e.Bounds.Y + (int)(10 * s), e.Bounds.Width - (int)(32 * s), (int)(24 * s)), chosen ? UiTheme.Accent : UiTheme.Text, TextFormatFlags.EndEllipsis);
            using (Font small = UiTheme.Body(9.5f)) TextRenderer.DrawText(e.Graphics, ScreenPreset.Describe(profile.TargetMode) + (profile.Enabled ? "" : " · en pause"), small,
                new Rectangle(e.Bounds.X + (int)(16 * s), e.Bounds.Y + (int)(34 * s), e.Bounds.Width - (int)(32 * s), (int)(22 * s)), UiTheme.Secondary, TextFormatFlags.EndEllipsis);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }
        private void Discover(object sender, EventArgs e)
        {
            DiscoveryOpen = true;
            try
            {
                using (GameDiscoveryForm dialog = new GameDiscoveryForm(list.Items.Cast<GameProfile>(), format.SelectedIndex, icon, detection))
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    {
                        foreach (GameProfile profile in dialog.Result) list.Items.Add(profile);
                        if (list.Items.Count > 0) list.SelectedIndex = list.Items.Count - 1; Changed();
                    }
                detection = null;
            }
            finally { DiscoveryOpen = false; }
        }
        public void PrepareDetection()
        {
            if (detection != null) return;
            detection = Task.Factory.StartNew(delegate { return GameDiscovery.Discover(GameDiscovery.SystemOptions()); });
            detection.ContinueWith(delegate(Task<List<InstalledGame>> result)
            {
                if (result.IsFaulted) { Exception observed = result.Exception; return; }
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)delegate { if (!IsDisposed) discover.Text = "Jeux installés (" + result.Result.Count + ")"; }); }
                catch (InvalidOperationException) { }
            });
        }
    }
}
