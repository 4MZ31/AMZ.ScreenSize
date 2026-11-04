using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace AMZ.ScreenSize
{
    public sealed class InstalledGame
    {
        public string Name, Platform, InstallDirectory, Executable;
        public string ProcessName { get { return string.IsNullOrEmpty(Executable) ? "" : Path.GetFileNameWithoutExtension(Executable); } }
    }
    public sealed class DiscoveryOptions
    {
        public readonly List<string> SteamRoots = new List<string>();
        public readonly List<string> EpicManifestDirectories = new List<string>();
        public readonly List<string> XboxRoots = new List<string>();
        public bool IncludeRegistry = true;
    }
    [DataContract]
    public sealed class EpicManifest
    {
        [DataMember] public string DisplayName;
        [DataMember] public string InstallLocation;
        [DataMember] public string LaunchExecutable;
        [DataMember] public bool bIsIncompleteInstall;
    }

    public static class ValveKeyValues
    {
        public static Dictionary<string, object> Parse(string source)
        {
            List<string> tokens = new List<string>();
            for (int index = 0; index < source.Length; )
            {
                char current = source[index];
                if (char.IsWhiteSpace(current)) { index++; continue; }
                if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
                { while (index < source.Length && source[index] != '\n') index++; continue; }
                if (current == '{' || current == '}') { tokens.Add(current.ToString()); index++; continue; }
                StringBuilder token = new StringBuilder();
                if (current == '"')
                {
                    index++; bool closed = false;
                    while (index < source.Length)
                    {
                        current = source[index++];
                        if (current == '"') { closed = true; break; }
                        if (current == '\\' && index < source.Length && (source[index] == '\\' || source[index] == '"')) current = source[index++];
                        token.Append(current);
                    }
                    if (!closed) throw new IOException("Fichier Steam incomplet.");
                }
                else while (index < source.Length && !char.IsWhiteSpace(source[index]) && source[index] != '{' && source[index] != '}') token.Append(source[index++]);
                tokens.Add(token.ToString());
                if (tokens.Count > 100000) throw new IOException("Fichier Steam trop volumineux.");
            }
            int position = 0; return Read(tokens, ref position, 0);
        }
        private static Dictionary<string, object> Read(List<string> tokens, ref int position, int depth)
        {
            if (depth > 24) throw new IOException("Structure Steam trop profonde.");
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (position < tokens.Count)
            {
                string key = tokens[position++];
                if (key == "}") return result;
                if (position >= tokens.Count) throw new IOException("Fichier Steam incomplet.");
                string value = tokens[position++];
                result[key] = value == "{" ? (object)Read(tokens, ref position, depth + 1) : value;
            }
            return result;
        }
        public static Dictionary<string, object> Section(Dictionary<string, object> source, string key)
        { object value; return source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null; }
        public static string Text(Dictionary<string, object> source, string key)
        { object value; return source != null && source.TryGetValue(key, out value) ? value as string : null; }
    }

    public static class GameDiscovery
    {
        private static readonly string[] IgnoredDirectories = { "engine", "redist", "_commonredist", "directx", "easyanticheat", "easyanticheat_eos", "battleye", "crashreportclient", "cef", "webview", "node_modules", "dotnet", "support", "installers" };
        private static bool IsAuxiliary(string name)
        { return Regex.IsMatch(name, @"(?i)(^unins|^setup|^install|^steam$|^steamservice$|crash|report|downloader|updater|^update|launcher|bootstrap|anticheat|easyanti|battleye|^vc_redist|^dxsetup|^dotnet|helper|_eac|_be$|^cef|^werfault|^vvis$|^vrad$)"); }
        private static string SmallText(string path)
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Manifest trop volumineux.");
            return File.ReadAllText(path);
        }
        private static string RegistryText(RegistryKey root, string path, string value)
        {
            try { using (RegistryKey key = root.OpenSubKey(path)) return key == null ? null : key.GetValue(value) as string; }
            catch (System.Security.SecurityException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        public static DiscoveryOptions SystemOptions()
        {
            DiscoveryOptions options = new DiscoveryOptions();
            string steam = RegistryText(Registry.CurrentUser, "Software\\Valve\\Steam", "SteamPath");
            if (!string.IsNullOrWhiteSpace(steam)) options.SteamRoots.Add(steam);
            options.SteamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            options.EpicManifestDirectories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests"));
            foreach (DriveInfo drive in DriveInfo.GetDrives())
                try { if (drive.DriveType == DriveType.Fixed && drive.IsReady) options.XboxRoots.Add(Path.Combine(drive.RootDirectory.FullName, "XboxGames")); }
                catch (IOException) { }
            return options;
        }
        private static IEnumerable<string> Files(string directory, string pattern)
        {
            try { return Directory.Exists(directory) ? Directory.GetFiles(directory, pattern) : new string[0]; }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }
        private static IEnumerable<string> Directories(string directory)
        {
            try { return Directory.Exists(directory) ? Directory.GetDirectories(directory) : new string[0]; }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }
        public static string FindExecutable(string root, string gameName, string hint)
        {
            List<string> candidates = new List<string>();
            string allowed = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(hint))
            {
                string hinted = Path.GetFullPath(Path.Combine(root, hint.Replace('/', Path.DirectorySeparatorChar)));
                if (hinted.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(hinted).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(hinted)) candidates.Add(hinted);
            }
            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0)); int visited = 0;
            while (queue.Count > 0 && visited++ < 250 && candidates.Count < 120)
            {
                KeyValuePair<string, int> directory = queue.Dequeue();
                candidates.AddRange(Files(directory.Key, "*.exe").Where(file => !IsAuxiliary(Path.GetFileNameWithoutExtension(file))).Take(120 - candidates.Count));
                if (directory.Value >= 4) continue;
                foreach (string child in Directories(directory.Key))
                {
                    if (IgnoredDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) continue;
                    try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) queue.Enqueue(new KeyValuePair<string, int>(child, directory.Value + 1)); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            string cleanName = Regex.Replace(gameName ?? "", @"[^\p{L}\p{N}]", "").ToLowerInvariant();
            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(file => !IsAuxiliary(Path.GetFileNameWithoutExtension(file)))
                .OrderByDescending(file =>
                {
                    string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    int score = name.Contains("shipping") ? 120 : 0;
                    if (name == "cs2") score += 260;
                    if (name == "csgo" || name == "gta5" || name == "gta5_enhanced") score += 180;
                    string clean = Regex.Replace(name, @"[^\p{L}\p{N}]", "");
                    if (cleanName.Length > 2 && (clean == cleanName || clean.Contains(cleanName))) score += 80;
                    if (name.Contains("win64") || name.Contains("x64")) score += 10;
                    if (name.Contains("-min-cpu")) score -= 10;
                    if (Path.GetDirectoryName(file).Equals(root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) score += 12;
                    if (!string.IsNullOrEmpty(hint) && file.Equals(Path.Combine(root, hint.Replace('/', Path.DirectorySeparatorChar)), StringComparison.OrdinalIgnoreCase)) score += 300;
                    return score;
                }).ThenBy(file => file, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? "";
        }
        private static void Add(List<InstalledGame> games, string name, string platform, string directory, string hint)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
            if (Regex.IsMatch(name, @"(?i)(redistribut|dedicated server|steamworks|epic games launcher|rockstar games launcher|ubisoft connect|battle\.net|^ea app$|gog galaxy|^steam$)")) return;
            string full = Path.GetFullPath(directory).TrimEnd('\\', '/');
            if (games.Any(game => game.InstallDirectory.Equals(full, StringComparison.OrdinalIgnoreCase))) return;
            string executable = FindExecutable(full, name, hint);
            if (executable.Length == 0 && !Files(full, "*").Any() && !Directories(full).Any(child => !Regex.IsMatch(Path.GetFileName(child), @"(?i)^(savedata|saves|savegames)$"))) return;
            games.Add(new InstalledGame { Name = name.Trim(), Platform = platform, InstallDirectory = full, Executable = executable });
        }
        public static List<InstalledGame> Discover(DiscoveryOptions options)
        {
            List<InstalledGame> games = new List<InstalledGame>();
            HashSet<string> steamRoots = new HashSet<string>(options.SteamRoots.Where(Directory.Exists), StringComparer.OrdinalIgnoreCase);
            foreach (string root in steamRoots.ToArray())
            {
                foreach (string libraryFile in Files(Path.Combine(root, "steamapps"), "libraryfolders.vdf"))
                {
                    try
                    {
                        Dictionary<string, object> libraries = ValveKeyValues.Section(ValveKeyValues.Parse(SmallText(libraryFile)), "libraryfolders");
                        if (libraries == null) continue;
                        foreach (object value in libraries.Values)
                        {
                            string path = value as string ?? ValveKeyValues.Text(value as Dictionary<string, object>, "path");
                            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) steamRoots.Add(path);
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            foreach (string root in steamRoots)
                foreach (string manifest in Files(Path.Combine(root, "steamapps"), "appmanifest_*.acf"))
                    try
                    {
                        Dictionary<string, object> game = ValveKeyValues.Section(ValveKeyValues.Parse(SmallText(manifest)), "AppState");
                        string name = ValveKeyValues.Text(game, "name"), relative = ValveKeyValues.Text(game, "installdir");
                        if (!string.IsNullOrEmpty(relative))
                        {
                            string common = Path.GetFullPath(Path.Combine(root, "steamapps", "common")) + Path.DirectorySeparatorChar;
                            string installed = Path.GetFullPath(Path.Combine(common, relative));
                            if (installed.StartsWith(common, StringComparison.OrdinalIgnoreCase)) Add(games, name, "Steam", installed, null);
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (ArgumentException) { }
            foreach (string directory in options.EpicManifestDirectories)
                foreach (string manifest in Files(directory, "*.item"))
                    try
                    {
                        using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(SmallText(manifest))))
                        {
                            EpicManifest game = (EpicManifest)new DataContractJsonSerializer(typeof(EpicManifest)).ReadObject(input);
                            if (!game.bIsIncompleteInstall) Add(games, game.DisplayName, "Epic Games", game.InstallLocation, game.LaunchExecutable);
                        }
                    }
                    catch (SerializationException) { }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (ArgumentException) { }
            foreach (string root in options.XboxRoots)
                foreach (string directory in Directories(root))
                {
                    string content = Path.Combine(directory, "Content"), config = Path.Combine(content, "MicrosoftGame.config");
                    if (!File.Exists(config)) { content = directory; config = Path.Combine(content, "MicrosoftGame.config"); }
                    if (!File.Exists(config)) continue;
                    try
                    {
                        XmlDocument document = new XmlDocument { XmlResolver = null };
                        using (XmlReader reader = XmlReader.Create(new StringReader(SmallText(config)), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
                        XmlNode visuals = document.SelectSingleNode("//*[local-name()='ShellVisuals']");
                        string name = visuals != null && visuals.Attributes["DefaultDisplayName"] != null ? visuals.Attributes["DefaultDisplayName"].Value : Path.GetFileName(directory);
                        if (name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) || name.StartsWith("@")) name = Path.GetFileName(directory);
                        XmlNode executable = document.SelectSingleNode("//*[local-name()='Executable'][@Name]");
                        Add(games, name, "Xbox / Microsoft Store", content, executable == null ? null : executable.Attributes["Name"].Value);
                    }
                    catch (XmlException) { }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            if (options.IncludeRegistry) RegistryGames(games);
            return games.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        private static void RegistryGames(List<InstalledGame> games)
        {
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                        {
                            using (RegistryKey gog = root.OpenSubKey("Software\\GOG.com\\Games"))
                                if (gog != null) foreach (string id in gog.GetSubKeyNames())
                                    using (RegistryKey game = gog.OpenSubKey(id)) if (game != null)
                                        Add(games, (game.GetValue("gameName") ?? game.GetValue("GAMENAME")) as string, "GOG", game.GetValue("path") as string, game.GetValue("exe") as string);
                            using (RegistryKey uninstall = root.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall"))
                                if (uninstall != null) foreach (string id in uninstall.GetSubKeyNames())
                                    using (RegistryKey game = uninstall.OpenSubKey(id))
                                    {
                                        if (game == null) continue;
                                        string publisher = game.GetValue("Publisher") as string ?? "";
                                        if (!id.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(publisher, @"(?i)(GOG|Ubisoft|Blizzard|Electronic Arts|Rockstar Games|Riot Games|Bethesda|CD PROJEKT|Bandai Namco|Square Enix|Valve|2K Games|Activision|Bohemia Interactive|Mojang|Xbox Game Studios|Microsoft Studios)")) continue;
                                        string platform = id.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase) ? "Steam" : publisher;
                                        string icon = game.GetValue("DisplayIcon") as string;
                                        if (icon != null) icon = icon.Trim('"').Split(',')[0].Trim('"');
                                        Add(games, game.GetValue("DisplayName") as string, platform, game.GetValue("InstallLocation") as string, icon);
                                    }
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (System.Security.SecurityException) { }
                    catch (IOException) { }
                    catch (ArgumentException) { }
        }
    }
}
