using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

namespace AMZ.ScreenSize
{
    public sealed class TrayApplication : ApplicationContext, IAppActions, IDisplayActions
    {
        public const string MutexName = "Local\\AMZ.ScreenSize-v1";
        public const string ShowName = "Local\\AMZ.ScreenSize-v1-Show";
        public const string QuitName = "Local\\AMZ.ScreenSize-v1-Quit";
        private readonly SettingsStore store;
        private readonly AutomationController controller;
        private readonly MainForm window;
        private readonly Control dispatcher;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem enabledMenu, statusMenu, formatsMenu;
        private readonly System.Windows.Forms.Timer timer;
        private readonly EventWaitHandle showEvent, quitEvent;
        private readonly Icon activeIcon, inactiveIcon;
        private readonly string[] processNames;
        private string profilesMenuSignature;
        private bool visualBusy;
        private bool discoverRequested;
        private string topologySignature;
        private DateTime nextDetection = DateTime.MinValue;
        private readonly Queue<Action> pendingOperations = new Queue<Action>();
        private readonly UpdateManager updater;
        private UpdatePlan pendingUpdate;
        private bool checkingUpdates, installUpdate, manualInstallRequested;
        private string updateMessage = "Version " + Program.Version;
        private DateTime nextAutoCheck = DateTime.UtcNow.AddSeconds(8), nextInstallAttempt = DateTime.MinValue;
        private bool busy, quitting, allowedClose, updatingMenu, backgroundTipShown;
        private DateTime nextAttempt = DateTime.MinValue, nextRead = DateTime.MinValue;

        public TrayApplication(string directory, bool background, string[] processNames)
        {
            this.processNames = processNames;
            store = new SettingsStore(directory);
            controller = new AutomationController(new SamsungMonitor(), store.Load(), store.Save, store.Log, new MonitorFleet());
            updater = new UpdateManager(directory, store.Log);
            pendingUpdate = updater.ReadReady();
            if (pendingUpdate != null) updateMessage = "Version " + pendingUpdate.Version + " prête";
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
            quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);
            dispatcher = new Control(); IntPtr unused = dispatcher.Handle;
            activeIcon = MakeIcon(true); inactiveIcon = MakeIcon(false);
            UiTheme.Dark = controller.Snapshot().Settings.DarkMode;
            ContextMenuStrip menu = new ContextMenuStrip { Renderer = new UiMenuRenderer(), Font = UiTheme.Body() };
            menu.Items.Add("Ouvrir AMZ.ScreenSize", null, delegate { ShowWindow(); });
            statusMenu = new ToolStripMenuItem("Connexion…") { Enabled = false }; menu.Items.Add(statusMenu);
            menu.Items.Add(new ToolStripSeparator());
            enabledMenu = new ToolStripMenuItem("Activer les profils de jeux") { CheckOnClick = true };
            enabledMenu.CheckedChanged += delegate { if (!updatingMenu) SetEnabled(enabledMenu.Checked); };
            menu.Items.Add(enabledMenu);
            formatsMenu = new ToolStripMenuItem("Profils de jeux");
            menu.Items.Add(formatsMenu);
            menu.Items.Add("Vérifier les mises à jour", null, delegate { CheckForUpdates(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter et restaurer l’écran", null, delegate { RequestQuit(); });
            tray = new NotifyIcon { ContextMenuStrip = menu, Icon = activeIcon, Text = "AMZ.ScreenSize", Visible = true };
            tray.DoubleClick += delegate { ShowWindow(); };
            window = new MainForm(this, activeIcon);
            window.SelectPreset(controller.Snapshot().Settings.SelectedMode);
            window.FormClosing += delegate(object sender, FormClosingEventArgs args)
            { if (allowedClose) args.Cancel = false; };
            timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += TimerTick;
            timer.Start();
            Render();
            store.Log("START version=" + Program.Version + " profiles=" + controller.Snapshot().Settings.Profiles.Count);
            if (!background) ShowWindow();
            RunOperation(delegate { controller.Discover(); controller.Tick(RunningProcesses()); controller.Refresh(); }, false);
        }

        private static Icon MakeIcon(bool active)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Brush ink = new SolidBrush(Color.FromArgb(29, 43, 53)))
            using (Brush accent = new SolidBrush(active ? Color.FromArgb(67, 175, 148) : Color.FromArgb(130, 137, 145)))
            {
                graphics.Clear(Color.Transparent);
                graphics.FillRectangle(ink, 1, 4, 30, 23);
                graphics.FillRectangle(accent, 5, 8, 22, 15);
                graphics.FillRectangle(ink, 14, 27, 4, 3);
                graphics.FillRectangle(ink, 9, 30, 14, 2);
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        private string[] RunningProcesses()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in Process.GetProcesses())
            {
                try { names.Add(process.ProcessName); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                finally { process.Dispose(); }
            }
            // Preserve the optional 1.0 testing override without changing stored profiles.
            if (processNames != null && processNames.Any(names.Contains)) { names.Add("cs2"); names.Add("csgo"); }
            return names.ToArray();
        }
        private bool GameRunning()
        {
            HashSet<string> running = new HashSet<string>(RunningProcesses(), StringComparer.OrdinalIgnoreCase);
            return controller.Snapshot().Settings.Profiles.Any(profile => profile.Processes.Any(running.Contains));
        }
        private void TimerTick(object sender, EventArgs args)
        {
            if (showEvent.WaitOne(0)) ShowWindow();
            if (quitEvent.WaitOne(0)) { RequestQuit(); return; }
            AppSnapshot snapshot = controller.Snapshot();
            if (!quitting && !checkingUpdates && snapshot.Settings.AutoUpdate && DateTime.UtcNow >= nextAutoCheck && updater.CheckDue)
                CheckForUpdates();
            if (pendingUpdate != null && !quitting && DateTime.UtcNow >= nextInstallAttempt && (manualInstallRequested || updater.InstallRetryDue) &&
                UpdateManager.CanAutoApply(snapshot.Settings.AutoUpdate || manualInstallRequested, GameRunning(), snapshot.Settings.RestorePending, busy || window.EditingProfiles || pendingOperations.Count > 0))
            { installUpdate = true; RequestQuit(); return; }
            if (busy || quitting || DateTime.UtcNow < nextAttempt) return;
            bool refresh = DateTime.UtcNow >= nextRead;
            bool scan = discoverRequested;
            discoverRequested = false;
            RunOperation(delegate
            {
                if (scan || refresh)
                {
                    string topology = string.Join("|", VideoTopology.Read().SelectMany(pair => pair.Value.Select(t => pair.Key + t.DevicePath + t.Technology)).OrderBy(v => v));
                    if (scan || topology != topologySignature || DateTime.UtcNow >= nextDetection)
                    {
                        controller.Discover(); topologySignature = topology;
                        nextDetection = DateTime.UtcNow.AddMinutes(3);
                    }
                }
                controller.Tick(RunningProcesses());
                if (refresh) { controller.Refresh(); nextRead = DateTime.UtcNow.AddSeconds(15); }
            }, false, true);
        }
        private void RunOperation(Action operation, bool shutdown, bool quiet = false)
        {
            if (busy)
            {
                if (!quiet && !shutdown && !quitting) { pendingOperations.Enqueue(operation); visualBusy = true; Render(); }
                return;
            }
            busy = true; visualBusy = !quiet; Render();
            Task.Factory.StartNew(delegate
            {
                Exception failure = null;
                try { operation(); }
                catch (Exception error) { failure = error; controller.RecordError(error); }
                dispatcher.BeginInvoke((Action)delegate
                {
                    busy = false; visualBusy = false;
                    if (failure != null)
                    {
                        nextAttempt = DateTime.UtcNow.AddSeconds(10);
                        if (shutdown)
                        {
                            installUpdate = false; manualInstallRequested = false;
                            nextInstallAttempt = DateTime.UtcNow.AddMinutes(10);
                            quitting = false; timer.Start();
                            tray.ShowBalloonTip(5000, "AMZ.ScreenSize", "Restauration impossible pour le moment. L’application reste ouverte pour réessayer.", ToolTipIcon.Warning);
                        }
                    }
                    else nextAttempt = DateTime.MinValue;
                    Render();
                    if (!quiet && !shutdown) window.ShowFeedback(failure == null ? "Modifications appliquées" : failure.Message, failure != null);
                    if (shutdown && failure == null)
                    {
                        if (installUpdate)
                        {
                            try { UpdateManager.BeginApply(pendingUpdate, store.DirectoryPath, !window.Visible); }
                            catch (Exception error)
                            {
                                store.Log("UPDATE_LAUNCH_FAILED " + error.ToString());
                                installUpdate = false; manualInstallRequested = false; quitting = false;
                                nextInstallAttempt = DateTime.UtcNow.AddHours(1);
                                updateMessage = "Installation impossible · voir Journal";
                                timer.Start(); Render(); return;
                            }
                        }
                        store.Log("STOP restored=true");
                        allowedClose = true; window.AllowExit = true; tray.Visible = false; window.Close(); ExitThread(); return;
                    }
                    if (quitting && !shutdown) QueueShutdown();
                    else if (!quitting && pendingOperations.Count > 0) RunOperation(pendingOperations.Dequeue(), false);
                });
            });
        }
        private void Render()
        {
            AppSnapshot snapshot = controller.Snapshot();
            window.Render(snapshot, visualBusy || quitting);
            window.SetUpdateStatus(updateMessage, pendingUpdate != null, checkingUpdates || visualBusy || quitting);
            updatingMenu = true;
            try
            {
                enabledMenu.Checked = snapshot.Settings.Enabled;
                enabledMenu.Enabled = !busy && !quitting;
                formatsMenu.Enabled = !busy && !quitting && !window.HasUnsavedProfiles;
                int[] common = MonitorFleet.Common(snapshot.Displays, snapshot.Settings.DisplaySelection);
                string signature = string.Join(",", common) + string.Join("|", snapshot.Settings.Profiles.Select(profile => profile.Id + ":" + profile.Name + ":" + profile.TargetMode + ":" + profile.Enabled));
                if (signature != profilesMenuSignature)
                {
                    foreach (ToolStripItem item in formatsMenu.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
                    formatsMenu.DropDownItems.Clear();
                    formatsMenu.DropDownItems.Add("Gérer / détecter les profils…", null, delegate { EditProfiles(); });
                    foreach (GameProfile profile in snapshot.Settings.Profiles)
                    {
                        string id = profile.Id;
                        ToolStripMenuItem group = new ToolStripMenuItem(profile.Name + (profile.Enabled ? "" : " · désactivé"));
                        foreach (ScreenPreset preset in common.Select(ScreenPreset.Find))
                        {
                            int value = preset.Value;
                            ToolStripMenuItem item = new ToolStripMenuItem(preset.Label) { Checked = value == profile.TargetMode };
                            item.Click += delegate { SaveProfileTarget(id, value); };
                            group.DropDownItems.Add(item);
                        }
                        formatsMenu.DropDownItems.Add(group);
                    }
                    profilesMenuSignature = signature;
                }
                tray.Icon = snapshot.Settings.Enabled ? activeIcon : inactiveIcon;
                string tooltip = "AMZ.ScreenSize · " + (snapshot.Settings.Enabled ? snapshot.ActiveProfileName ?? "profils actifs" : "automatisme désactivé");
                tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 60) + "…" : tooltip;
                statusMenu.Text = snapshot.CurrentMode >= 0 ? "Écran : " + ScreenPreset.Describe(snapshot.CurrentMode) : "Écran indisponible";
            }
            finally { updatingMenu = false; }
        }
        public void RequestDiscovery() { discoverRequested = true; }
        public void SelectDisplays(List<string> paths) { RunOperation(delegate { controller.SelectDisplays(paths); }, false); }
        public void ApplyManual(int value) { nextAttempt = DateTime.MinValue; RunOperation(delegate { controller.ApplyManual(value); }, false); }
        public void SaveTarget(int value) { nextAttempt = DateTime.MinValue; RunOperation(delegate { controller.SetTarget(value); }, false); }
        public void SaveProfileTarget(string id, int value) { nextAttempt = DateTime.MinValue; RunOperation(delegate { controller.SetProfileTarget(id, value); }, false); }
        public void EditProfiles()
        {
            ShowWindow(); window.Navigate(1);
        }
        public void SaveProfiles(List<GameProfile> profiles)
        { RunOperation(delegate { controller.SetProfiles(profiles); }, false); }
        public void SetDarkMode(bool dark)
        { RunOperation(delegate { controller.SetDarkMode(dark); }, false); }
        public void SetEnabled(bool enabled) { nextAttempt = DateTime.MinValue; RunOperation(delegate { controller.SetEnabled(enabled); }, false); }
        public void RefreshMonitor() { nextAttempt = DateTime.MinValue; RunOperation(controller.Refresh, false); }
        public bool AutoUpdatesEnabled { get { return controller.Snapshot().Settings.AutoUpdate; } }
        public void SetAutoUpdates(bool enabled)
        { RunOperation(delegate { controller.SetAutoUpdate(enabled); }, false); }
        public void CheckForUpdates()
        {
            if (checkingUpdates || quitting) return;
            if (pendingUpdate != null)
            {
                manualInstallRequested = true; nextInstallAttempt = DateTime.MinValue;
                updateMessage = GameRunning() ? "Installation après la partie" : "Installation programmée…";
                Render(); return;
            }
            checkingUpdates = true; updateMessage = "Recherche en cours…"; Render();
            Task.Factory.StartNew(delegate
            {
                UpdatePlan plan = null; Exception failure = null;
                try { plan = updater.Check(); }
                catch (Exception error) { failure = error; store.Log("UPDATE_CHECK_FAILED " + error.Message); }
                if (quitting || dispatcher.IsDisposed) return;
                try
                {
                    dispatcher.BeginInvoke((Action)delegate
                    {
                        checkingUpdates = false; nextAutoCheck = DateTime.UtcNow.AddDays(1);
                        if (failure != null) updateMessage = "Vérification impossible · voir Journal";
                        else if (plan == null) updateMessage = "Aucune nouvelle version disponible";
                        else
                        {
                            pendingUpdate = plan;
                            updateMessage = "Version " + plan.Version + " prête";
                            tray.ShowBalloonTip(4000, "AMZ.ScreenSize", "La version " + plan.Version + " est prête. L’installation automatique attend la fermeture des jeux gérés.", ToolTipIcon.Info);
                        }
                        Render();
                    });
                }
                catch (InvalidOperationException) { /* Application is closing. */ }
            });
        }
        public bool StartupEnabled { get { return StartupManager.Enabled; } }
        public void SetStartup(bool enabled)
        {
            try { StartupManager.SetEnabled(enabled, Application.ExecutablePath); store.Log("STARTUP enabled=" + enabled); }
            catch (Exception error) { MessageBox.Show(window, error.Message, "Lancement avec Windows", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            Render();
        }
        public void OpenLog()
        {
            if (!File.Exists(store.LogPath)) store.Log("Journal ouvert");
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + store.LogPath + "\"") { UseShellExecute = true });
        }
        private void ShowWindow()
        { window.Show(); window.WindowState = FormWindowState.Normal; window.Activate(); }
        public void HideWindow()
        {
            window.Hide();
            if (!backgroundTipShown)
            {
                backgroundTipShown = true;
                tray.ShowBalloonTip(3000, "AMZ.ScreenSize", "L’application reste active dans les icônes masquées. Double-cliquez sur son icône pour rouvrir l’interface.", ToolTipIcon.Info);
            }
        }
        private void RequestQuit()
        {
            if (quitting) return;
            if (window.HasUnsavedProfiles && !installUpdate && MessageBox.Show(window, "Les profils contiennent des modifications non enregistrées. Quitter sans les enregistrer ?", "Quitter AMZ.ScreenSize", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            pendingOperations.Clear();
            quitting = true; timer.Stop(); Render();
            if (!busy) QueueShutdown();
        }
        private void QueueShutdown() { RunOperation(controller.Close, true); }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose(); tray.Dispose(); window.Dispose(); dispatcher.Dispose();
                showEvent.Dispose(); quitEvent.Dispose(); activeIcon.Dispose(); inactiveIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
