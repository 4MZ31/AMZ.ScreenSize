using System;
using System.Collections.Generic;

namespace AMZ.ScreenSize
{
    public sealed class ScreenPreset
    {
        public int Value { get; private set; }
        public string Name { get; private set; }
        public string Ratio { get; private set; }
        public double Inches { get; private set; }
        public double Aspect { get; private set; }
        private ScreenPreset(int value, string name, string ratio, double inches, double aspect)
        { Value = value; Name = name; Ratio = ratio; Inches = inches; Aspect = aspect; }

        // G40B PC-mode options. Do not infer support from the VCP maximum (which is 9).
        public static readonly IList<ScreenPreset> All = Array.AsReadOnly(new[] {
            new ScreenPreset(0, "Auto", "Format de la source", 27, 16.0 / 9),
            new ScreenPreset(1, "Wide", "Plein écran", 27, 16.0 / 9),
            new ScreenPreset(2, "17\u2033", "4:3", 17, 4.0 / 3),
            new ScreenPreset(3, "19\u2033", "4:3", 19, 4.0 / 3),
            new ScreenPreset(4, "19\u2033 Wide", "16:10", 19, 16.0 / 10),
            new ScreenPreset(5, "21,5\u2033 Wide", "16:9", 21.5, 16.0 / 9),
            new ScreenPreset(6, "22\u2033 Wide", "16:10", 22, 16.0 / 10),
            new ScreenPreset(7, "23\u2033 Wide", "16:9", 23, 16.0 / 9)
        });
        private static readonly Dictionary<int,ScreenPreset> extra = BuildExtra();
        private static Dictionary<int,ScreenPreset> BuildExtra()
        {
            var items = new Dictionary<int,ScreenPreset>();
            items[27] = new ScreenPreset(27, "27″ Wide", "16:9", 27, 16.0/9);
            items[29] = new ScreenPreset(29, "29″ Wide", "21:9", 29, 21.0/9);
            string[] names = { "Taille originale", "Ajuster en conservant le ratio", "Ajuster en hauteur", "Ajuster en largeur",
                "Étirement vertical", "Étirement horizontal", "Expansion horizontale", "Zoom sur les deux axes", "Compression", "Expansion non linéaire" };
            for (int i = 0; i < names.Length; i++) items[101+i] = new ScreenPreset(101+i, names[i], "", 0, 1);
            items[112] = new ScreenPreset(112, "Surbalayage", "", 0, 1);
            return items;
        }
        public static ScreenPreset Find(int value)
        {
            if (value >= 0 && value < All.Count) return All[value];
            ScreenPreset preset;
            return extra.TryGetValue(value, out preset) ? preset : null;
        }
        public string Label { get { return Value < 2 || Ratio.Length == 0 ? Name : Name + " (" + Ratio + ")"; } }
        public override string ToString() { return Label; }
        public static bool IsSelectable(int value) { return Find(value) != null; }
        public static string Describe(int value)
        { return IsSelectable(value) ? Find(value).Label : "Format " + value; }
    }
}
