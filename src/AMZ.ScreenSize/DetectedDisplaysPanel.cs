using System;
using System.Text;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AMZ.ScreenSize
{
    internal sealed class DetectedDisplaysPanel : UserControl
    {
        private readonly TextBox results;
        private readonly UiButton refresh;
        private List<DisplayScan> previous;

        internal DetectedDisplaysPanel(Action scan)
        {
            Dock = DockStyle.Fill;
            var layout = UiTheme.Table(1, 48, 52, 52, 0);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(UiTheme.Label("Moniteurs détectés", 24), 0, 0);
            layout.Controls.Add(UiTheme.Label("Détection des formats via HDMI et DisplayPort.", 10.5f, "secondary"), 0, 1);
            refresh = UiTheme.Button("Actualiser la liste");
            refresh.Click += delegate { scan(); };
            layout.Controls.Add(refresh, 0, 2);
            results = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical, AccessibleName = "Résultats de détection des moniteurs", Text = "Détection en attente…" };
            layout.Controls.Add(results, 0, 3);
            Controls.Add(layout);
        }

        internal void Render(List<DisplayScan> displays)
        {
            if (displays == null || previous == displays) return;
            previous = displays;
            var report = new StringBuilder();
            foreach (DisplayScan display in displays)
            {
                report.AppendLine((display.Description ?? display.ModelId ?? display.WindowsDisplay ?? "Moniteur") + " · " + (display.Connector ?? "Connexion inconnue"));
                report.AppendLine(Describe(display.Status));
                report.AppendLine();
            }
            results.Text = displays.Count == 0 ? "Aucun moniteur détecté." : report.ToString();
        }

        private static string Describe(string state)
        {
            switch (state)
            {
                case "known-g4-readable": return "Format lisible · modèle validé pour les profils";
                case "standard-readable": return "Formats DDC/CI annoncés et format actuel relu";
                case "documented-readable": return "Commande documentée pour ce modèle · lecture réussie";
                case "scaling-read-candidate": return "Commande de mise à l’échelle lisible · formats à confirmer";
                case "scaling-advertised-unconfirmed": return "Mise à l’échelle annoncée · lecture à confirmer";
                case "excluded-connector": return "Connexion hors HDMI / DisplayPort externe";
                case "ambiguous-path": return "Chemin vidéo partagé · identification ambiguë";
                case "unidentified-path": return "Connexion vidéo indéterminée";
                default: return "Commande de format non identifiée";
            }
        }
    }
}
