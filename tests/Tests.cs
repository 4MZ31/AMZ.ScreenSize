using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using AMZ.ScreenSize;

internal static class Tests
{
    private sealed class FakeMonitor : IMonitor
    {
        public int Mode = 1, Writes;
        public bool FailSet, FailRead;
        public int ReadMode() { if (FailRead) throw new IOException("read disconnected"); return Mode; }
        public void SetMode(int value)
        { if (FailSet) throw new IOException("write disconnected"); Writes++; Mode = value; }
    }
    private sealed class MemoryStore
    {
        public bool Fail;
        public AppSettings Persisted;
        public void Save(AppSettings settings)
        { if (Fail) throw new IOException("disk full"); Persisted = settings.Copy(); }
    }
    private sealed class PreviewActions : IAppActions
    {
        public bool StartupEnabled { get { return true; } }
        public void ApplyManual(int value) { }
        public void SaveTarget(int value) { }
        public void SaveProfileTarget(string id, int value) { }
        public void EditProfiles() { }
        public void SetEnabled(bool value) { }
        public void RefreshMonitor() { }
        public void SetStartup(bool value) { }
        public void OpenLog() { }
        public void HideWindow() { }
        public bool AutoUpdatesEnabled { get { return true; } }
        public void SetAutoUpdates(bool enabled) { }
        public void CheckForUpdates() { }
        public void SaveProfiles(List<GameProfile> profiles) { }
        public void SetDarkMode(bool dark) { }
    }
    private static int checks;
    private static void Assert(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS " + name); }
    private static void Throws(Action action, string name)
    {
        bool threw = false;
        try { action(); } catch (IOException) { threw = true; } catch (InvalidOperationException) { threw = true; }
        Assert(threw, name);
    }
    private static AutomationController Make(FakeMonitor monitor, MemoryStore store, AppSettings settings)
    { return new AutomationController(monitor, settings, store.Save, delegate(string text) { }); }

    private static void Offline()
    {
        Assert(ScreenPreset.All.Count == 8 && ScreenPreset.All[2].Ratio == "4:3" && ScreenPreset.All[7].Value == 7, "G40B preset catalog");
        Assert(!ScreenPreset.IsSelectable(8) && !ScreenPreset.IsSelectable(9) && !ScreenPreset.IsSelectable(-1), "unsupported G9-only sizes excluded");
        FakeMonitor monitor = new FakeMonitor(); MemoryStore store = new MemoryStore();
        AutomationController app = Make(monitor, store, new AppSettings());
        app.Tick(false); Assert(monitor.Writes == 0, "idle does not write to screen");
        app.Tick(true); Assert(monitor.Mode == 7 && store.Persisted.OriginalMode == 1 && store.Persisted.RestorePending, "game start persists baseline then applies target");
        int writes = monitor.Writes; app.Tick(true);
        Assert(monitor.Writes == writes, "no repeated DDC writes while game remains open");
        app.SetTarget(2); Assert(monitor.Mode == 2 && store.Persisted.OriginalMode == 1, "change live target preserves initial baseline");
        app.SetEnabled(false); Assert(monitor.Mode == 1 && !store.Persisted.RestorePending, "disable while playing restores original");
        app.SetEnabled(true); Assert(monitor.Mode == 2, "reenable while playing uses saved target");
        Throws(delegate { app.ApplyManual(3); }, "manual write blocked during managed game");
        app.Tick(false); Assert(monitor.Mode == 1, "game exit restores original");
        app.ApplyManual(5); Assert(monitor.Mode == 5 && store.Persisted.SelectedMode == 5, "manual apply remains after idle tick");
        app.Tick(false); Assert(monitor.Mode == 5, "manual mode is not automatically undone");
        app.Tick(true); app.Close(); Assert(monitor.Mode == 5 && app.Snapshot().Settings.Enabled, "quit restores original and keeps enabled preference");
        monitor.Mode = 7;
        app = Make(monitor, store, new AppSettings()); app.Tick(true); app.Tick(false);
        Assert(monitor.Mode == 7, "original 23-inch mode is preserved");
        app = Make(monitor, store, new AppSettings { RestorePending = true, OriginalMode = 0 }); app.Tick(false);
        Assert(monitor.Mode == 0 && !app.Snapshot().Settings.RestorePending, "interrupted session recovers saved baseline");
        monitor.Mode = 1; monitor.FailRead = true; app = Make(monitor, store, new AppSettings());
        Throws(delegate { app.Tick(true); }, "disconnected monitor reports failure");
        Assert(!app.Snapshot().Settings.RestorePending && monitor.Mode == 1, "failed baseline read cannot mutate screen");
        monitor.FailRead = false; store.Fail = true;
        Throws(delegate { app.Tick(true); }, "failed settings write prevents switching");
        Assert(!app.Snapshot().Settings.RestorePending && monitor.Mode == 1, "capture persistence rollback");
        store.Fail = false; monitor.FailSet = true;
        Throws(delegate { app.Tick(true); }, "DDC set failure propagates");
        Assert(app.Snapshot().Settings.RestorePending, "failed set keeps recovery record");
        monitor.FailSet = false; app.Tick(true); Assert(monitor.Mode == 7, "mode change retries after reconnect");
        monitor.FailSet = true; Throws(app.Close, "restore failure propagates");
        Assert(app.Snapshot().Settings.RestorePending, "failed restore keeps recovery record");
        monitor.FailSet = false; store.Fail = true; Throws(app.Close, "restore persistence failure propagates");
        Assert(monitor.Mode == 1 && app.Snapshot().Settings.RestorePending, "restored hardware retains durable recovery intent if saving fails");
        store.Fail = false; app.Tick(true); Assert(monitor.Mode == 7, "new game reapplies after interrupted restore save");
        app.Close();
        string directory = Path.Combine(Path.GetTempPath(), "AMZ.ScreenSize.tests." + Guid.NewGuid().ToString("N"));
        SettingsStore disk = new SettingsStore(directory);
        try
        {
            AppSettings saved = new AppSettings { Enabled = false, TargetMode = 4, SelectedMode = 2, RestorePending = true, OriginalMode = 0 };
            disk.Save(saved); disk.Save(saved); AppSettings loaded = disk.Load();
            Assert(!loaded.Enabled && loaded.TargetMode == 4 && loaded.SelectedMode == 2 && loaded.RestorePending && loaded.OriginalMode == 0, "atomic disk settings round trip");
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"Enabled\":false}");
            loaded = disk.Load(); Assert(loaded.TargetMode == 7 && loaded.SelectedMode == 7 && !loaded.Enabled, "missing settings fields get safe defaults");
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"TargetMode\":9}");
            Throws(delegate { disk.Load(); }, "unsupported target rejected on load");
        }
        finally { File.Delete(Path.Combine(directory, "settings.json")); Directory.Delete(directory); }
    }
    private static void Hardware()
    {
        SamsungMonitor monitor = new SamsungMonitor(); int original = monitor.ReadMode();
        Console.WriteLine("HARDWARE original=" + original);
        try
        {
            foreach (ScreenPreset preset in ScreenPreset.All)
            {
                monitor.SetMode(preset.Value);
                Assert(monitor.ReadMode() == preset.Value, "hardware readback " + preset.Label);
            }
        }
        finally { monitor.SetMode(original); Console.WriteLine("HARDWARE restored=" + monitor.ReadMode()); }
    }
    private static GameProfile Profile(string id, int mode)
    { return new GameProfile { Id = id, Name = "Game " + id, Processes = new[] { id + ".exe" }, TargetMode = mode }; }
    private static void Profiles()
    {
        AppSettings legacy = new AppSettings { TargetMode = 4, Enabled = false, AutoUpdate = false, RestorePending = true, OriginalMode = 0 };
        legacy.Validate();
        Assert(legacy.Profiles.Count == 1 && legacy.Profiles[0].TargetMode == 4 && legacy.Profiles[0].Processes.Contains("cs2") && !legacy.Enabled && !legacy.AutoUpdate && legacy.RestorePending && legacy.OriginalMode == 0, "1.0 settings migrate Counter-Strike without losing recovery state");
        AppSettings copy = legacy.Copy(); copy.Profiles[0].Processes[0] = "changed";
        Assert(legacy.Profiles[0].Processes[0] == "cs2", "profile arrays are deeply copied");
        GameProfile alpha = Profile("alpha", 2), beta = Profile("beta", 5);
        FakeMonitor monitor = new FakeMonitor(); MemoryStore store = new MemoryStore();
        AutomationController app = Make(monitor, store, new AppSettings { Profiles = new List<GameProfile> { alpha, beta } });
        app.Tick(new[] { "ALPHA" });
        Assert(monitor.Mode == 2 && app.Snapshot().ActiveProfileId == "alpha", "first custom game applies its own format case-insensitively");
        app.Tick(new[] { "beta", "alpha" });
        Assert(monitor.Mode == 2 && app.Snapshot().ActiveProfileId == "alpha", "second game cannot steal priority");
        app.Tick(new[] { "beta" });
        Assert(monitor.Mode == 5 && app.Snapshot().Settings.OriginalMode == 1 && app.Snapshot().Settings.RestorePending, "handoff uses second game's format without replacing original baseline");
        app.Tick(new string[0]); Assert(monitor.Mode == 1 && !app.Snapshot().Settings.RestorePending, "last game exit restores initial format");
        app.Tick(new[] { "beta", "alpha" }); Assert(app.Snapshot().ActiveProfileId == "alpha", "simultaneous startup uses profile order deterministically");
        app.Close(); app.Tick(new[] { "beta" }); app.Tick(new[] { "alpha", "beta" });
        Assert(app.Snapshot().ActiveProfileId == "beta" && monitor.Mode == 5, "earlier detected game stays active independent of profile order");
        GameProfile disabledBeta = beta.Copy(); disabledBeta.Enabled = false;
        app.SetProfiles(new[] { alpha, disabledBeta });
        Assert(monitor.Mode == 2 && app.Snapshot().ActiveProfileId == "alpha", "disabling active profile hands control to another running game");
        app.SetProfiles(new[] { disabledBeta }); Assert(monitor.Mode == 1 && !app.Snapshot().Settings.RestorePending, "removing last active profile restores initial mode");
        app.SetProfiles(new GameProfile[0]); Assert(app.Snapshot().Settings.Profiles.Count == 0 && !app.Snapshot().GameRunning, "empty profile list does not recreate Counter-Strike");
        GameProfile duplicate = Profile("other", 3); duplicate.Processes = new[] { " ALPHA.EXE " };
        Throws(delegate { app.SetProfiles(new[] { alpha, duplicate }); }, "ambiguous process assignment rejected");
        Assert(app.Snapshot().Settings.Profiles.Count == 0, "invalid profile edits preserve existing settings");
        GameProfile invalid = Profile("bad", 7); invalid.Processes = new[] { "C:\\Games\\bad.exe" };
        Throws(delegate { app.SetProfiles(new[] { invalid }); }, "process fields cannot contain a file path");
        app.SetProfiles(new[] { alpha, beta }); app.Tick(new[] { "alpha" });
        GameProfile renamed = alpha.Copy(); renamed.Name = "Renamed game"; renamed.TargetMode = 6;
        app.SetProfiles(new[] { renamed, beta }); Assert(monitor.Mode == 6 && app.Snapshot().ActiveProfileName == "Renamed game" && app.Snapshot().Settings.OriginalMode == 1, "editing active game preserves baseline and applies changed format");
        store.Fail = true;
        Throws(delegate { app.SetProfiles(new[] { beta }); }, "failed profile save does not change live profile set");
        Assert(app.Snapshot().Settings.Profiles.Count == 2 && monitor.Mode == 6, "profile persistence failure retains current format and profiles");
        store.Fail = false; monitor.FailSet = true;
        Throws(delegate { app.Tick(new[] { "beta" }); }, "failed game handoff preserves recovery record");
        monitor.FailSet = false; app.Tick(new[] { "beta" }); app.Close();
        Assert(monitor.Mode == 1 && !app.Snapshot().Settings.RestorePending, "handoff retry and final restore succeed");
        app.Tick(new[] { "gamma" }); app.SetProfiles(new[] { Profile("gamma", 3) });
        Assert(monitor.Mode == 3, "adding a profile for an already running game takes effect immediately");
        AutomationController recovered = Make(monitor, store, app.Snapshot().Settings); recovered.Tick(new string[0]);
        Assert(monitor.Mode == 1, "multi-game crash recovery restores saved initial mode");
        GameProfile aliases = Profile("aliases", 4); aliases.Processes = new[] { "one.exe", "two.EXE", "ONE" };
        app = Make(monitor, store, new AppSettings { Profiles = new List<GameProfile> { aliases } });
        app.Tick(new[] { "one", "two" }); app.Tick(new[] { "two" });
        Assert(monitor.Mode == 4 && app.Snapshot().GameRunning && app.Snapshot().Settings.Profiles[0].Processes.Length == 2, "process aliases remain active until their last process closes");
        app.Close();
    }
    private static void WriteFixture(string path, string content)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content); }
    private static void DiscoverGames()
    {
        string root = Path.Combine(Path.GetTempPath(), "AMZ.ScreenSize.discovery." + Guid.NewGuid().ToString("N"));
        string steam = Path.Combine(root, "Steam"), library = Path.Combine(root, "OtherLibrary"), epic = Path.Combine(root, "EpicManifests"), xbox = Path.Combine(root, "XboxGames");
        string alpha = Path.Combine(steam, "steamapps", "common", "Alpha"), beta = Path.Combine(library, "steamapps", "common", "Beta"), epicGame = Path.Combine(root, "EpicGame");
        try
        {
            WriteFixture(Path.Combine(alpha, "alpha.exe"), "fixture");
            WriteFixture(Path.Combine(beta, "beta.exe"), "fixture");
            WriteFixture(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "// fixture\n\"libraryfolders\" { \"0\" { \"path\" \"" + steam.Replace("\\", "\\\\") + "\" } \"1\" \"" + library.Replace("\\", "\\\\") + "\" }");
            WriteFixture(Path.Combine(steam, "steamapps", "appmanifest_1.acf"), "\"AppState\" { \"name\" \"Étoile Alpha\" \"installdir\" \"Alpha\" }");
            WriteFixture(Path.Combine(library, "steamapps", "appmanifest_2.acf"), "\"AppState\" { \"name\" \"Beta\" \"installdir\" \"Beta\" }");
            WriteFixture(Path.Combine(steam, "steamapps", "appmanifest_3.acf"), "\"AppState\" { \"name\" \"Missing game\" \"installdir\" \"Absent\" }");
            WriteFixture(Path.Combine(steam, "steamapps", "appmanifest_4.acf"), "\"AppState\" { \"name\" \"Traversal\" \"installdir\" \"..\\..\\..\\EpicGame\" }");
            WriteFixture(Path.Combine(epicGame, "Bootstrapper.exe"), "fixture");
            string shipping = Path.Combine(epicGame, "Game", "Binaries", "Win64", "Epic-Win64-Shipping.exe"); WriteFixture(shipping, "fixture");
            Directory.CreateDirectory(epic);
            EpicManifest epicManifest = new EpicManifest { DisplayName = "Epic Fixture", InstallLocation = epicGame, LaunchExecutable = "Bootstrapper.exe" };
            using (FileStream stream = File.Create(Path.Combine(epic, "fixture.item"))) new DataContractJsonSerializer(typeof(EpicManifest)).WriteObject(stream, epicManifest);
            epicManifest.DisplayName = "Duplicate Alpha"; epicManifest.InstallLocation = alpha;
            using (FileStream stream = File.Create(Path.Combine(epic, "duplicate.item"))) new DataContractJsonSerializer(typeof(EpicManifest)).WriteObject(stream, epicManifest);
            WriteFixture(Path.Combine(epic, "broken.item"), "not-json");
            string xboxContent = Path.Combine(xbox, "XboxFixture", "Content");
            WriteFixture(Path.Combine(xboxContent, "xboxgame.exe"), "fixture");
            WriteFixture(Path.Combine(xboxContent, "MicrosoftGame.config"), "<Game><ShellVisuals DefaultDisplayName=\"Xbox Fixture\"/><ExecutableList><Executable Name=\"xboxgame.exe\"/></ExecutableList></Game>");
            DiscoveryOptions options = new DiscoveryOptions { IncludeRegistry = false }; options.SteamRoots.Add(steam); options.EpicManifestDirectories.Add(epic); options.XboxRoots.Add(xbox);
            List<InstalledGame> games = GameDiscovery.Discover(options);
            Assert(games.Count == 4, "discovery reads Steam, legacy secondary library, Epic and Xbox without duplicates");
            Assert(games.Any(game => game.Name == "Étoile Alpha" && game.ProcessName == "alpha"), "Steam Unicode metadata and executable resolution");
            Assert(games.Any(game => game.Name == "Beta" && game.Platform == "Steam"), "Steam secondary library discovery");
            Assert(games.Any(game => game.Name == "Epic Fixture" && game.Executable == shipping), "Epic real shipping process preferred over bootstrapper");
            Assert(games.Any(game => game.Name == "Xbox Fixture" && game.ProcessName == "xboxgame"), "Xbox MicrosoftGame.config detection");
            Assert(!games.Any(game => game.Name == "Traversal" || game.Name == "Missing game"), "missing installs and Steam path traversal ignored");
            Assert(GameDiscovery.FindExecutable(alpha, "Alpha", Path.Combine(root, "outside.exe")) == Path.Combine(alpha, "alpha.exe"), "executable hints cannot escape install directory");
            WriteFixture(Path.Combine(alpha, "icon.ico"), "fixture");
            Assert(GameDiscovery.FindExecutable(alpha, "Alpha", "icon.ico") == Path.Combine(alpha, "alpha.exe"), "registry icon hints cannot be mistaken for game executables");
            WriteFixture(Path.Combine(alpha, "bpreport.exe"), "fixture");
            Assert(GameDiscovery.FindExecutable(alpha, "Unrelated title", null) == Path.Combine(alpha, "alpha.exe"), "crash reporting utilities excluded");
            string counter = Path.Combine(root, "CounterStrike");
            WriteFixture(Path.Combine(counter, "csgo.exe"), "fixture"); WriteFixture(Path.Combine(counter, "game", "bin", "win64", "cs2.exe"), "fixture");
            Assert(GameDiscovery.FindExecutable(counter, "Counter-Strike 2", null).EndsWith("cs2.exe"), "current Counter-Strike executable preferred over leftover CS GO");
            string leftover = Path.Combine(steam, "steamapps", "common", "Leftover"); Directory.CreateDirectory(Path.Combine(leftover, "SaveData"));
            WriteFixture(Path.Combine(steam, "steamapps", "appmanifest_5.acf"), "\"AppState\" { \"name\" \"Uninstalled leftover\" \"installdir\" \"Leftover\" }");
            Assert(!GameDiscovery.Discover(options).Any(game => game.Name == "Uninstalled leftover"), "uninstalled game save folders ignored");
            Throws(delegate { ValveKeyValues.Parse("\"unclosed"); }, "invalid Steam manifest rejected");
        }
        finally
        {
            string full = Path.GetFullPath(root), temp = Path.GetFullPath(Path.GetTempPath());
            if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("AMZ.ScreenSize.discovery.", StringComparison.Ordinal)) Directory.Delete(full, true);
        }
    }
    private static void Updates()
    {
        Assert(UpdateManager.ParseVersion("v1.2.3").ToString(3) == "1.2.3", "stable update version parsing");
        Throws(delegate { UpdateManager.ParseVersion("v1.2.3/../other"); }, "unsafe release tag rejected");
        string hash = new string('a', 64);
        Assert(UpdateManager.ParseChecksum(hash.ToUpperInvariant() + "  AMZ.ScreenSize.exe\r\n") == hash, "SHA-256 manifest parsing");
        Throws(delegate { UpdateManager.ParseChecksum(hash + "  other.exe"); }, "checksum for a different asset rejected");
        string executable = typeof(Program).Assembly.Location;
        string actual = UpdateManager.HashFile(executable);
        UpdateManager.VerifyDownload(executable, actual, new Version(Program.Version));
        Assert(actual.Length == 64, "download checksum and assembly identity verified");
        Throws(delegate { UpdateManager.VerifyDownload(executable, new string('0', 64), new Version(Program.Version)); }, "tampered download rejected");
        Throws(delegate { UpdateManager.VerifyDownload(executable, actual, new Version(99, 0, 0)); }, "incorrect executable version rejected");
        Assert(!UpdateManager.CanAutoApply(true, true, false, false) && !UpdateManager.CanAutoApply(true, false, true, false) && UpdateManager.CanAutoApply(true, false, false, false), "updates deferred during game and pending restore");
        FakeMonitor monitor = new FakeMonitor(); MemoryStore store = new MemoryStore();
        AutomationController app = Make(monitor, store, new AppSettings()); app.SetAutoUpdate(false);
        Assert(!store.Persisted.AutoUpdate && monitor.Writes == 0, "automatic update preference persists without monitor changes");
        Assert(UpdateManager.QuoteArgument("C:\\Screens With Spaces\\") == "\"C:\\Screens With Spaces\\\\\"", "updater paths with spaces and trailing slash quoted correctly");
    }
    private static T Field<T>(object target, string name) where T : class
    { return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
    private static double Luminance(Color c)
    {
        double[] channels = { c.R / 255.0, c.G / 255.0, c.B / 255.0 };
        for (int i = 0; i < channels.Length; i++) channels[i] = channels[i] <= 0.04045 ? channels[i] / 12.92 : Math.Pow((channels[i] + 0.055) / 1.055, 2.4);
        return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
    }
    private static double Contrast(Color a, Color b)
    { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
    private static void PaintRegression(Control control, Color parentColor)
    {
        using (Panel parent = new Panel { BackColor = parentColor })
        using (Bitmap bitmap = new Bitmap(200, 44))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            parent.Controls.Add(control); control.Size = bitmap.Size; control.Text = "Test";
            graphics.Clear(Color.Magenta);
            control.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { new PaintEventArgs(graphics, control.ClientRectangle) });
            Assert(bitmap.GetPixel(0, 0).ToArgb() == parentColor.ToArgb() && bitmap.GetPixel(199, 43).ToArgb() == parentColor.ToArgb(), control.GetType().Name + " paints corners without native black or stale buffer");
            if (control is UiSwitch) Assert(bitmap.GetPixel(80, 20).ToArgb() == parentColor.ToArgb(), "switch repaints its entire text background");
        }
    }
    private static void Interface()
    {
        using (MemoryStream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}")))
        {
            AppSettings migrated = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream);
            Assert(migrated.DarkMode, "legacy preferences default to dark without resetting other settings");
        }
        FakeMonitor monitor = new FakeMonitor(); MemoryStore store = new MemoryStore();
        AutomationController controller = Make(monitor, store, new AppSettings()); controller.SetDarkMode(false);
        Assert(!store.Persisted.DarkMode && monitor.Writes == 0, "theme persists independently of monitor and automation");
        using (MemoryStream stream = new MemoryStream())
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings)); serializer.WriteObject(stream, store.Persisted); stream.Position = 0;
            Assert(!((AppSettings)serializer.ReadObject(stream)).DarkMode, "explicit light theme survives serialization");
        }
        foreach (bool dark in new[] { true, false })
        {
            UiTheme.Dark = dark;
            Assert(new[] { UiTheme.Text, UiTheme.Secondary, UiTheme.Muted }.All(c => Contrast(c, UiTheme.Surface) >= 4.5 && Contrast(c, UiTheme.Canvas) >= 4.5), "text contrast across " + (dark ? "dark" : "light") + " surfaces");
            Assert(Contrast(UiTheme.Accent, UiTheme.Selection) >= 4.5, "selection contrast in " + (dark ? "dark" : "light") + " theme");
            PaintRegression(new UiButton(), UiTheme.Sidebar);
            PaintRegression(new UiSwitch(), UiTheme.Surface);
        }
        UiTheme.Dark = true;
        using (Form host = new Form { ClientSize = new Size(900, 520) })
        using (ProfilesPanel panel = new ProfilesPanel(new[] { GameProfile.CounterStrike(7) }, SystemIcons.Application, null))
        {
            host.Controls.Add(panel); host.Show(); Application.DoEvents();
            TextBox name = Field<TextBox>(panel, "name"); UiButton save = Field<UiButton>(panel, "save");
            name.Text = "My custom name"; panel.Render(new[] { GameProfile.CounterStrike(7) }, false);
            Assert(panel.Dirty && name.Text == "My custom name", "periodic snapshots preserve unsaved profile edits");
            List<GameProfile> valid = panel.ValidatedDraft(); save.PerformClick(); panel.Render(new[] { GameProfile.CounterStrike(7) }, false);
            Assert(panel.Dirty, "unacknowledged or failed save preserves draft");
            panel.Render(valid, false); Assert(!panel.Dirty, "successful persisted snapshot acknowledges profile save");
            name.Text = ""; save.PerformClick();
            Assert(panel.Dirty && Field<Label>(panel, "message").Text.Length > 0, "invalid profile gets inline validation without closing editor");
            Field<UiButton>(panel, "cancel").PerformClick(); Assert(!panel.Dirty && name.Text == "My custom name", "cancel returns to latest committed profiles");
            name.Text = "Unsaved"; valid[0].Name = "Externally saved"; panel.Render(valid, false);
            Field<UiButton>(panel, "cancel").PerformClick(); Assert(name.Text == "Externally saved", "cancel respects newer committed state while draft is open");
            host.Close();
        }
        using (MainForm form = new MainForm(new PreviewActions(), SystemIcons.Application))
        {
            AppSnapshot snapshot = new AppSnapshot { Settings = new AppSettings(), CurrentMode = 1, Connected = true, Status = "Ready" };
            form.Render(snapshot, false); form.Navigate(2); form.Show(); Application.DoEvents();
            Assert(Field<UiButton>(form, "dark").Selected, "default theme is visible in settings");
            Control sidebar = form.Controls.Find("Sidebar", true).Single();
            Assert(sidebar.Top == 0 && sidebar.Bottom == form.ClientSize.Height, "sidebar extends to the bottom independently of status bar");
            UiSwitch startup = Field<UiSwitch>(form, "startup");
            Assert(startup.Parent.Controls.OfType<Label>().All(label => label.Bottom <= startup.Top), "startup switch cannot overlap heading or description");
            snapshot.Settings.DarkMode = false; form.Render(snapshot, false);
            Assert(!UiTheme.Dark && form.BackColor == UiTheme.Canvas && Field<UiButton>(form, "light").Selected, "live theme change updates native window and selected choice");
            form.SelectPreset(7); Assert(Field<UiButton>(form, "apply").Enabled, "manual format application remains available when idle");
            snapshot.GameRunning = true; snapshot.Settings.RestorePending = true; form.Render(snapshot, false);
            Assert(!Field<UiButton>(form, "apply").Enabled, "manual format remains blocked during managed game");
            ProfilesPanel panel = Field<ProfilesPanel>(form, "profiles");
            typeof(ProfilesPanel).GetField("detection", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(panel, Task.FromResult(new List<InstalledGame>()));
            form.Size = form.MinimumSize; form.Navigate(1); Application.DoEvents();
            UiSwitch profileSwitch = Field<UiSwitch>(panel, "enabled"); ComboBox profileFormat = Field<ComboBox>(panel, "format");
            Console.WriteLine("Minimum editor: window=" + form.ClientSize + " format=" + profileFormat.Bounds + " switch=" + profileSwitch.Bounds + " editor=" + profileSwitch.Parent.ClientSize);
            Assert(profileSwitch.Top >= profileFormat.Bottom && profileSwitch.Bottom <= profileSwitch.Parent.ClientSize.Height, "profile switch stays below format and inside editor at minimum window size");
            form.AllowExit = true; form.Close();
        }
        UiTheme.Dark = true;
    }
    private static void InlineGames()
    {
        List<InstalledGame> games = new List<InstalledGame> {
            new InstalledGame { Name = "Counter-Strike installé", Platform = "Steam", InstallDirectory = "C:\\Games\\CS", Executable = "C:\\Games\\CS\\CS2.exe" },
            new InstalledGame { Name = "Nouveau jeu", Platform = "Steam", InstallDirectory = "C:\\Games\\New", Executable = "C:\\Games\\New\\newgame.exe" },
            new InstalledGame { Name = "Autre jeu", Platform = "Epic", InstallDirectory = "C:\\Games\\Other", Executable = "C:\\Games\\Other\\other.exe" },
            new InstalledGame { Name = "Jeu à identifier", Platform = "GOG", InstallDirectory = "C:\\Games\\Unknown" }
        };
        List<GameChoice> catalog = GameCatalog.Build(new[] { GameProfile.CounterStrike(7) }, games);
        Assert(catalog.Count == 4 && catalog.Count(c => c.Profile != null) == 1, "catalog merges configured process aliases with installed games without duplicates");
        Assert(catalog.Count(c => c.Matches("epic")) == 1 && catalog.Count(c => c.Matches("CS2")) == 1, "catalog search covers platform and executable aliases");
        Assert(GameCatalog.Create(catalog, 2).Count == 2, "bulk creation skips existing profiles and unresolved executables");
        using (Form host = new Form { ClientSize = new Size(900, 520) })
        using (ProfilesPanel panel = new ProfilesPanel(new[] { GameProfile.CounterStrike(7) }, SystemIcons.Application, Task.FromResult(games)))
        {
            host.Controls.Add(panel); host.Show(); panel.PrepareDetection(); Application.DoEvents();
            ListView list = Field<UiGameList>(panel, "list");
            Func<string, ListViewItem> find = process => list.Items.Cast<ListViewItem>().Single(i => ((GameChoice)i.Tag).Installation != null && ((GameChoice)i.Tag).Installation.ProcessName == process);
            find("newgame").Selected = true; Application.DoEvents();
            ComboBox format = Field<ComboBox>(panel, "format"); format.SelectedIndex = 2;
            UiButton save = Field<UiButton>(panel, "save");
            Assert(save.Text == "Créer le profil" && save.Enabled && Application.OpenForms.Count == 1, "installed game selection offers creation directly in the existing page");
            int requests = 0; panel.SaveRequested += delegate { requests++; };
            save.PerformClick(); List<GameProfile> result = panel.ValidatedDraft();
            Assert(requests == 1 && result.Single(p => p.Processes.Contains("newgame")).TargetMode == 2, "one inline action creates and saves selected game with its chosen format");
            panel.Render(result, false);
            find("other").Checked = true;
            Assert(panel.InteractionActive, "checked games defer automatic installation while selecting");
            Field<UiButton>(panel, "cancel").PerformClick();
            Assert(list.Items.Cast<ListViewItem>().Where(i => ((GameChoice)i.Tag).Profile == null).All(i => !i.Checked), "cancel clears pending game selection without changing committed profiles");
            ListViewItem configured = list.Items.Cast<ListViewItem>().Single(i => ((GameChoice)i.Tag).Profile != null && ((GameChoice)i.Tag).Profile.Processes.Contains("newgame"));
            configured.Selected = true; Field<UiButton>(panel, "remove").PerformClick();
            find("newgame").Selected = true; save.PerformClick(); result = panel.ValidatedDraft();
            Assert(result.Count == 1 && result[0].Id == GameProfile.CounterStrikeId, "saving deletion cannot recreate an automatically selected installed game");
            panel.Render(result, false);
            find("newgame").Checked = true; find("other").Checked = true; format.SelectedIndex = 5;
            Assert(save.Text == "Créer 2 profils", "inline bulk selection displays exact creation count");
            save.PerformClick(); result = panel.ValidatedDraft();
            Assert(result.Count == 3 && result.Where(p => p.Id != GameProfile.CounterStrikeId).All(p => p.TargetMode == 5), "inline bulk creation keeps common chosen format and existing profile");
            host.Close();
        }
    }
    private static void Screenshot(string path, string kind)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        List<GameProfile> examples = new List<GameProfile> { GameProfile.CounterStrike(7), new GameProfile { Name = "Jeu d’exemple", Processes = new[] { "example" }, TargetMode = 2 } };
        Task<List<InstalledGame>> games = Task.FromResult(new List<InstalledGame> {
            new InstalledGame { Name = "Jeu d’exemple", Platform = "Steam", InstallDirectory = "C:\\Games\\Example", Executable = "C:\\Games\\Example\\example.exe" },
            new InstalledGame { Name = "Autre jeu", Platform = "Epic Games", InstallDirectory = "C:\\Games\\Other", Executable = "C:\\Games\\Other\\other.exe" }
        });
        UiTheme.Dark = !kind.Contains("light");
        Form selectedForm = new MainForm(new PreviewActions(), SystemIcons.Application);
        using (Form form = selectedForm)
        {
            MainForm main = form as MainForm;
            if (main != null)
            {
                main.Render(new AppSnapshot { Settings = new AppSettings { Profiles = examples, DarkMode = UiTheme.Dark }, CurrentMode = 1, Connected = true, Status = "Prêt · en attente d’un jeu" }, false);
                if (kind.StartsWith("profiles") || kind == "discovery")
                {
                    ProfilesPanel panel = Field<ProfilesPanel>(main, "profiles");
                    typeof(ProfilesPanel).GetField("detection", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(panel, games);
                    main.Navigate(1);
                    if (kind == "discovery") Field<UiGameList>(panel, "list").Items.Cast<ListViewItem>().First(i => ((GameChoice)i.Tag).Profile == null).Selected = true;
                }
                if (kind.Contains("settings")) main.Navigate(2);
                if (kind.Contains("minimum")) main.Size = main.MinimumSize;
            }
            form.Show(); Application.DoEvents();
            for (int frame = 0; frame < 20; frame++) { System.Threading.Thread.Sleep(10); Application.DoEvents(); }
            using (Bitmap bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                using (Bitmap whole = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(whole, new Rectangle(Point.Empty, whole.Size));
                    Rectangle client = new Rectangle(form.PointToScreen(Point.Empty).X - form.Left, form.PointToScreen(Point.Empty).Y - form.Top, form.ClientSize.Width, form.ClientSize.Height);
                    using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.DrawImage(whole, new Rectangle(Point.Empty, bitmap.Size), client, GraphicsUnit.Pixel);
                }
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Dispose();
        }
        Console.WriteLine("Screenshot saved: " + path);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--screenshot") { Screenshot(args[1], "main"); return 0; }
            if (args.Length == 2 && args[0] == "--profiles-screenshot") { Screenshot(args[1], "profiles"); return 0; }
            if (args.Length == 2 && args[0] == "--discovery-screenshot") { Screenshot(args[1], "discovery"); return 0; }
            if (args.Length == 2 && args[0] == "--settings-screenshot") { Screenshot(args[1], "settings"); return 0; }
            if (args.Length == 2 && args[0] == "--light-screenshot") { Screenshot(args[1], "light"); return 0; }
            if (args.Length == 2 && args[0] == "--settings-light-screenshot") { Screenshot(args[1], "settings-light"); return 0; }
            if (args.Length == 2 && args[0] == "--minimum-screenshot") { Screenshot(args[1], "minimum"); return 0; }
            if (args.Length == 2 && args[0] == "--profiles-minimum-screenshot") { Screenshot(args[1], "profiles-minimum"); return 0; }
            if (args.Length == 2 && args[0] == "--settings-minimum-screenshot") { Screenshot(args[1], "settings-minimum"); return 0; }
            if (args.Length == 1 && args[0] == "--discover")
            {
                List<InstalledGame> games = GameDiscovery.Discover(GameDiscovery.SystemOptions());
                foreach (InstalledGame game in games) Console.WriteLine(game.Platform + " | " + game.Name + " | " + game.ProcessName);
                Console.WriteLine("DETECTED " + games.Count + " games; " + games.Count(game => !string.IsNullOrEmpty(game.ProcessName)) + " executables"); return 0;
            }
            Offline(); Updates(); Profiles(); DiscoverGames(); Interface(); InlineGames();
            if (Array.IndexOf(args, "--hardware") >= 0) Hardware();
            Console.WriteLine("PASS: " + checks + " checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
