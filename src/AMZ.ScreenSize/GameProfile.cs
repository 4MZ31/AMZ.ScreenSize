using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace AMZ.ScreenSize
{
    [DataContract]
    public sealed class GameProfile
    {
        public const string CounterStrikeId = "counter-strike";
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name = "";
        [DataMember] public string[] Processes = new string[0];
        [DataMember] public int TargetMode = 7;
        [DataMember] public bool Enabled = true;
        [OnDeserializing] private void Defaults(StreamingContext context)
        { Id = Guid.NewGuid().ToString("N"); Name = ""; Processes = new string[0]; TargetMode = 7; Enabled = true; }
        public GameProfile Copy()
        {
            GameProfile value = (GameProfile)MemberwiseClone();
            value.Processes = Processes == null ? null : (string[])Processes.Clone();
            return value;
        }
        public static GameProfile CounterStrike(int mode)
        { return new GameProfile { Id = CounterStrikeId, Name = "Counter-Strike", Processes = new[] { "cs2", "csgo" }, TargetMode = mode }; }
        public static string NormalizeProcess(string name)
        {
            string value = (name ?? "").Trim();
            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 4);
            if (value.Length == 0 || value.Length > 180 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.IndexOf(';') >= 0 || value.IndexOf(',') >= 0)
                throw new IOException("Indiquez un nom de processus, par exemple game.exe, sans chemin de dossier.");
            return value;
        }
        public static List<GameProfile> ValidateAndCopy(IEnumerable<GameProfile> profiles)
        {
            if (profiles == null) throw new IOException("La liste des profils est absente.");
            List<GameProfile> result = new List<GameProfile>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameProfile source in profiles)
            {
                if (source == null) throw new IOException("Un profil est invalide.");
                GameProfile profile = source.Copy(); profile.Name = (profile.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 80 || !ids.Add(profile.Id)) throw new IOException("Identifiant de profil invalide ou dupliqué.");
                if (profile.Name.Length == 0 || profile.Name.Length > 80) throw new IOException("Chaque profil doit avoir un nom de 1 à 80 caractères.");
                if (!ScreenPreset.IsSelectable(profile.TargetMode)) throw new IOException("Le format du profil " + profile.Name + " est invalide.");
                if (profile.Processes == null || profile.Processes.Length == 0 || profile.Processes.Length > 16) throw new IOException("Ajoutez au moins un processus au profil " + profile.Name + ".");
                profile.Processes = profile.Processes.Select(NormalizeProcess).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (string process in profile.Processes)
                {
                    string previous;
                    if (owners.TryGetValue(process, out previous)) throw new IOException("Le processus " + process + ".exe est déjà utilisé par le profil " + previous + ".");
                    owners.Add(process, profile.Name);
                }
                result.Add(profile);
                if (result.Count > 64) throw new IOException("La limite est de 64 profils.");
            }
            return result;
        }
        public override string ToString()
        { return Name + " · " + ScreenPreset.Describe(TargetMode) + (Enabled ? "" : " · désactivé"); }
    }
}
