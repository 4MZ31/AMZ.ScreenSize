using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace AMZ.ScreenSize
{
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public bool Enabled = true;
        [DataMember] public bool AutoUpdate = true;
        [DataMember] public bool DarkMode = true;
        [DataMember] public int TargetMode = 7;
        [DataMember] public int SelectedMode = 7;
        [DataMember] public bool RestorePending;
        [DataMember] public int OriginalMode = 1;
        [DataMember] public List<GameProfile> Profiles;
        [OnDeserializing] private void Defaults(StreamingContext context)
        { Enabled = true; AutoUpdate = true; DarkMode = true; TargetMode = 7; SelectedMode = 7; OriginalMode = 1; }
        public AppSettings Copy()
        {
            AppSettings value = (AppSettings)MemberwiseClone();
            value.Profiles = Profiles == null ? null : Profiles.Select(profile => profile.Copy()).ToList();
            return value;
        }
        public void Validate()
        {
            if (!ScreenPreset.IsSelectable(TargetMode) || !ScreenPreset.IsSelectable(SelectedMode) || OriginalMode < 0 || OriginalMode > 9)
                throw new IOException("Les préférences contiennent un format non reconnu.");
            // Missing field means a 1.0 settings file. An explicitly empty list stays empty.
            if (Profiles == null) Profiles = new List<GameProfile> { GameProfile.CounterStrike(TargetMode) };
            Profiles = GameProfile.ValidateAndCopy(Profiles);
        }
    }

    public sealed class SettingsStore
    {
        public string DirectoryPath { get; private set; }
        public string LogPath { get { return Path.Combine(DirectoryPath, "activity.log"); } }
        public SettingsStore(string directory)
        { DirectoryPath = directory; Directory.CreateDirectory(directory); }
        public AppSettings Load()
        {
            string path = Path.Combine(DirectoryPath, "settings.json");
            if (!File.Exists(path)) return new AppSettings();
            using (FileStream stream = File.OpenRead(path))
            {
                AppSettings settings = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream);
                settings.Validate();
                return settings;
            }
        }
        public void Save(AppSettings settings)
        {
            settings.Validate();
            string path = Path.Combine(DirectoryPath, "settings.json"), temporary = path + ".tmp";
            using (FileStream stream = File.Create(temporary))
            {
                new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(stream, settings);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        public void Log(string text)
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024)
                {
                    string previous = LogPath + ".previous";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(LogPath, previous);
                }
                File.AppendAllText(LogPath, DateTimeOffset.Now.ToString("o") + " " + text + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
