using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AMZ.ScreenSize
{
    public sealed class GameChoice
    {
        public GameProfile Profile;
        public InstalledGame Installation;
        public string Key { get { return Profile != null ? "profile:" + Profile.Id : "install:" + Installation.InstallDirectory; } }
        public string Name { get { return Profile != null ? Profile.Name : Installation.Name; } }
        public string Detail
        {
            get
            {
                if (Profile != null) return ScreenPreset.Describe(Profile.TargetMode) + (Profile.Enabled ? " · profil" : " · en pause");
                return Installation.Platform + (string.IsNullOrEmpty(Installation.ProcessName) ? " · exécutable à choisir" : " · sans profil");
            }
        }
        public bool Matches(string search)
        {
            string processes = Profile != null ? string.Join(";", Profile.Processes) : Installation.ProcessName;
            return (Name + " " + Detail + " " + processes).IndexOf(search.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
        }
    }
    public static class GameCatalog
    {
        public static List<GameChoice> Build(IEnumerable<GameProfile> profiles, IEnumerable<InstalledGame> installations)
        {
            List<GameChoice> result = profiles.Select(p => new GameChoice { Profile = p }).ToList();
            HashSet<string> claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameProfile profile in profiles)
                foreach (string alias in profile.Processes)
                    try { claimed.Add(GameProfile.NormalizeProcess(alias)); } catch (IOException) { }
            foreach (InstalledGame installation in installations)
            {
                if (!string.IsNullOrEmpty(installation.ProcessName) && claimed.Contains(installation.ProcessName)) continue;
                result.Add(new GameChoice { Installation = installation });
            }
            return result.OrderByDescending(c => c.Profile != null).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        public static List<GameProfile> Create(IEnumerable<GameChoice> choices, int mode)
        {
            return choices.Where(c => c.Profile == null && !string.IsNullOrEmpty(c.Installation.ProcessName))
                .GroupBy(c => c.Installation.ProcessName, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .Select(c => new GameProfile { Name = c.Name, Processes = new[] { c.Installation.ProcessName }, TargetMode = mode }).ToList();
        }
    }
}
