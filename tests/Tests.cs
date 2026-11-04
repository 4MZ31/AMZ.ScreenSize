using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
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
        public void SetEnabled(bool value) { }
        public void RefreshMonitor() { }
        public void SetStartup(bool value) { }
        public void OpenLog() { }
        public void HideWindow() { }
        public bool AutoUpdatesEnabled { get { return true; } }
        public void SetAutoUpdates(bool enabled) { }
        public void CheckForUpdates() { }
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
    private static void Screenshot(string path)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (MainForm form = new MainForm(new PreviewActions(), SystemIcons.Application))
        {
            form.Render(new AppSnapshot { Settings = new AppSettings(), CurrentMode = 1, Connected = true, Status = "Prêt · en attente de Counter-Strike" }, false);
            form.Show(); Application.DoEvents();
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
            if (args.Length == 2 && args[0] == "--screenshot") { Screenshot(args[1]); return 0; }
            Offline(); Updates();
            if (Array.IndexOf(args, "--hardware") >= 0) Hardware();
            Console.WriteLine("PASS: " + checks + " checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
