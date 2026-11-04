using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AMZ.ScreenSize
{
    [DataContract]
    public sealed class ReleaseMetadata
    {
        [DataMember(Name = "tag_name")] public string Tag;
        [DataMember(Name = "draft")] public bool Draft;
        [DataMember(Name = "prerelease")] public bool Prerelease;
    }
    [DataContract]
    public sealed class UpdatePlan
    {
        [DataMember] public string Version;
        [DataMember] public string Source;
        [DataMember] public string Hash;
    }
    [DataContract]
    public sealed class ApplyRequest
    {
        [DataMember] public int ParentId;
        [DataMember] public string Source, Target, Hash, Version, StateDirectory;
        [DataMember] public bool Background;
    }

    public sealed class UpdateManager
    {
        private const string Api = "https://api.github.com/repos/4MZ31/AMZ.ScreenSize/releases/latest";
        private const string Releases = "https://github.com/4MZ31/AMZ.ScreenSize/releases/download/";
        private readonly string directory, readyPath, stampPath;
        private readonly Action<string> log;
        public UpdateManager(string stateDirectory, Action<string> log)
        {
            directory = Path.Combine(stateDirectory, "updates");
            Directory.CreateDirectory(directory);
            readyPath = Path.Combine(directory, "ready.json"); stampPath = Path.Combine(directory, "last-check.txt");
            this.log = log;
        }
        public bool CheckDue
        { get { return !File.Exists(stampPath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(stampPath) >= TimeSpan.FromDays(1); } }
        public bool InstallRetryDue
        {
            get
            {
                string failure = Path.Combine(directory, "last-install-failure.txt");
                return !File.Exists(failure) || DateTime.UtcNow - File.GetLastWriteTimeUtc(failure) >= TimeSpan.FromHours(1);
            }
        }
        public static Version ParseVersion(string tag)
        {
            if (tag == null || !Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$")) throw new IOException("Version de release non reconnue.");
            Version version;
            if (!Version.TryParse(tag.Substring(1), out version)) throw new IOException("Version de release invalide.");
            return version;
        }
        public static string ParseChecksum(string text)
        {
            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = Regex.Match(line, @"^([a-fA-F0-9]{64})\s+\*?AMZ\.ScreenSize\.exe\s*$");
                if (match.Success) return match.Groups[1].Value.ToLowerInvariant();
            }
            throw new IOException("Empreinte de l’exécutable absente de SHA256SUMS.txt.");
        }
        public static string HashFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static void VerifyDownload(string path, string expectedHash, Version expectedVersion)
        {
            if (!Regex.IsMatch(expectedHash ?? "", @"^[a-fA-F0-9]{64}$") || !string.Equals(HashFile(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("L’empreinte du téléchargement est incorrecte. Mise à jour refusée.");
            AssemblyName assembly = AssemblyName.GetAssemblyName(path);
            if (assembly.Name != "AMZ.ScreenSize" || assembly.Version.ToString(3) != expectedVersion.ToString(3))
                throw new IOException("Le fichier téléchargé ne correspond pas à l’application et à la version attendues.");
        }
        public UpdatePlan ReadReady()
        {
            if (!File.Exists(readyPath)) return null;
            try
            {
                UpdatePlan plan;
                using (FileStream input = File.OpenRead(readyPath)) plan = (UpdatePlan)new DataContractJsonSerializer(typeof(UpdatePlan)).ReadObject(input);
                Version version = ParseVersion("v" + plan.Version);
                string allowed = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(plan.Source).StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || version <= new Version(Program.Version)) return null;
                VerifyDownload(plan.Source, plan.Hash, version); return plan;
            }
            catch (Exception error) { log("UPDATE_CACHE_IGNORED " + error.Message); return null; }
        }
        private static void Download(string url, string path, long limit)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "AMZ.ScreenSize/" + Program.Version;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 15000; request.ReadWriteTimeout = 20000; request.AllowAutoRedirect = true;
            using (WebResponse response = request.GetResponse())
            {
                if (response.ResponseUri.Scheme != Uri.UriSchemeHttps) throw new IOException("Redirection non sécurisée refusée.");
                if (response.ContentLength > limit) throw new IOException("Téléchargement trop volumineux.");
                using (Stream input = response.GetResponseStream())
                using (FileStream output = File.Create(path))
                {
                    byte[] buffer = new byte[8192]; long total = 0; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        total += count;
                        if (total > limit) throw new IOException("Téléchargement trop volumineux.");
                        output.Write(buffer, 0, count);
                    }
                    output.Flush(true);
                }
            }
        }
        public UpdatePlan Check()
        {
            File.WriteAllText(stampPath, DateTime.UtcNow.ToString("o"));
            string metadataPath = Path.Combine(directory, "latest.json");
            try { Download(Api, metadataPath, 1024 * 1024); }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.NotFound) { response.Dispose(); return null; }
                if (response != null) response.Dispose();
                throw;
            }
            ReleaseMetadata metadata;
            using (FileStream input = File.OpenRead(metadataPath)) metadata = (ReleaseMetadata)new DataContractJsonSerializer(typeof(ReleaseMetadata)).ReadObject(input);
            if (metadata.Draft || metadata.Prerelease) return null;
            Version version = ParseVersion(metadata.Tag);
            if (version <= new Version(Program.Version)) return null;
            UpdatePlan cached = ReadReady();
            if (cached != null && cached.Version == version.ToString(3)) return cached;
            string stage = Path.Combine(directory, metadata.Tag); Directory.CreateDirectory(stage);
            string hashes = Path.Combine(stage, "SHA256SUMS.txt"), temporary = Path.Combine(stage, "AMZ.ScreenSize.download"), executable = Path.Combine(stage, "AMZ.ScreenSize.exe");
            Download(Releases + metadata.Tag + "/SHA256SUMS.txt", hashes, 32768);
            string expected = ParseChecksum(File.ReadAllText(hashes, Encoding.UTF8));
            Download(Releases + metadata.Tag + "/AMZ.ScreenSize.exe", temporary, 20 * 1024 * 1024);
            VerifyDownload(temporary, expected, version);
            File.Copy(temporary, executable, true); File.Delete(temporary);
            UpdatePlan plan = new UpdatePlan { Version = version.ToString(3), Source = executable, Hash = expected };
            string tempPlan = readyPath + ".tmp";
            using (FileStream output = File.Create(tempPlan)) new DataContractJsonSerializer(typeof(UpdatePlan)).WriteObject(output, plan);
            if (File.Exists(readyPath)) File.Replace(tempPlan, readyPath, null); else File.Move(tempPlan, readyPath);
            log("UPDATE_READY version=" + plan.Version); return plan;
        }
        public static bool CanAutoApply(bool enabled, bool gameRunning, bool restorePending, bool busy)
        { return enabled && !gameRunning && !restorePending && !busy; }
        public static string QuoteArgument(string value)
        {
            StringBuilder output = new StringBuilder("\""); int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') { output.Append('\\', slashes * 2 + 1); output.Append('"'); }
                else { output.Append('\\', slashes); output.Append(character); }
                slashes = 0;
            }
            output.Append('\\', slashes * 2); output.Append('"'); return output.ToString();
        }
        public static void BeginApply(UpdatePlan plan, string stateDirectory, bool background)
        {
            VerifyDownload(plan.Source, plan.Hash, ParseVersion("v" + plan.Version));
            string target = Assembly.GetExecutingAssembly().Location;
            string helperDirectory = Path.Combine(stateDirectory, "updater"); Directory.CreateDirectory(helperDirectory);
            string helper = Path.Combine(helperDirectory, "AMZ.ScreenSize.Updater.exe");
            File.Copy(target, helper, true);
            ApplyRequest request = new ApplyRequest { ParentId = Process.GetCurrentProcess().Id, Source = plan.Source,
                Target = target, Hash = plan.Hash, Version = plan.Version, StateDirectory = stateDirectory, Background = background };
            string payload;
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(ApplyRequest)).WriteObject(stream, request);
                payload = Convert.ToBase64String(stream.ToArray());
            }
            Process.Start(new ProcessStartInfo(helper, "--apply-update " + payload) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        public static int ApplyAndRelaunch(string payload)
        {
            ApplyRequest request;
            using (MemoryStream input = new MemoryStream(Convert.FromBase64String(payload)))
                request = (ApplyRequest)new DataContractJsonSerializer(typeof(ApplyRequest)).ReadObject(input);
            SettingsStore store = new SettingsStore(request.StateDirectory);
            try
            {
                Version incoming = ParseVersion("v" + request.Version);
                VerifyDownload(request.Source, request.Hash, incoming);
                string target = Path.GetFullPath(request.Target);
                if (!string.Equals(Path.GetFileName(target), "AMZ.ScreenSize.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Cible de mise à jour invalide.");
                AssemblyName previous = AssemblyName.GetAssemblyName(target);
                if (previous.Name != "AMZ.ScreenSize" || incoming <= new Version(previous.Version.ToString(3))) throw new IOException("La mise à jour ne serait pas plus récente.");
                try
                {
                    using (Process parent = Process.GetProcessById(request.ParentId))
                    {
                        if (!string.Equals(parent.MainModule.FileName, target, StringComparison.OrdinalIgnoreCase)) throw new IOException("Le processus parent ne correspond pas à l’application.");
                        if (!parent.WaitForExit(60000)) throw new IOException("L’application ne s’est pas arrêtée à temps.");
                    }
                }
                catch (ArgumentException) { /* Parent already exited. */ }
                string temporary = target + ".update.tmp";
                File.Copy(request.Source, temporary, true);
                VerifyDownload(temporary, request.Hash, incoming);
                File.Replace(temporary, target, target + ".previous", true);
                File.Delete(Path.Combine(request.StateDirectory, "updates", "ready.json"));
                File.Delete(Path.Combine(request.StateDirectory, "updates", "last-install-failure.txt"));
                store.Log("UPDATE_INSTALLED version=" + request.Version);
            }
            catch (Exception error)
            {
                store.Log("UPDATE_INSTALL_FAILED " + error.ToString());
                // Persist a cooldown so a failed replacement cannot cause a restart loop.
                try
                {
                    string updates = Path.Combine(request.StateDirectory, "updates"); Directory.CreateDirectory(updates);
                    File.WriteAllText(Path.Combine(updates, "last-install-failure.txt"), DateTime.UtcNow.ToString("o"));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            try
            {
                Process.Start(new ProcessStartInfo(request.Target, "--state-dir " + QuoteArgument(request.StateDirectory) + (request.Background ? " --background" : "")) { UseShellExecute = false });
                return 0;
            }
            catch (Exception error) { store.Log("UPDATE_RESTART_FAILED " + error.ToString()); return 1; }
        }
    }
}
