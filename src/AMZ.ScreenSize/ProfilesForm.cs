using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AMZ.ScreenSize
{
    // Standalone host used by diagnostics and previews; the app uses the same editor inline.
    public sealed class ProfilesForm : Form
    {
        public List<GameProfile> Result { get; private set; }
        public ProfilesForm(IEnumerable<GameProfile> profiles, Icon icon, Task<List<InstalledGame>> preloadedGames = null)
        {
            Text = "Profils de jeux · AMZ.ScreenSize"; Icon = icon; Font = UiTheme.Body();
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(900, 660); MinimumSize = new Size(880, 680);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; Padding = new Padding(28);
            Label title = UiTheme.Label("Profils de jeux", 24); title.Dock = DockStyle.Top; title.Height = 64;
            ProfilesPanel editor = new ProfilesPanel(profiles, icon, preloadedGames);
            editor.SaveRequested += delegate { Result = editor.ValidatedDraft(); DialogResult = DialogResult.OK; Close(); };
            Controls.Add(editor); Controls.Add(title); UiTheme.Apply(this);
            HandleCreated += delegate { UiTheme.Caption(Handle); };
        }
    }
}
