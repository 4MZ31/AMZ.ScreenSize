using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace AMZ.ScreenSize
{
    public sealed class AppSnapshot
    {
        public List<DisplayScan> Displays;
        public AppSettings Settings;
        public int CurrentMode;
        public bool Connected, GameRunning;
        public string Status;
        public string ActiveProfileId, ActiveProfileName;
    }

    // Called by one serialized worker. All DDC operations stay off the UI thread.
    public sealed class AutomationController
    {
        private readonly IMonitor monitor;
        private readonly MonitorFleet fleet;
        private List<DisplayScan> displays = new List<DisplayScan>();
        private readonly Action<AppSettings> save;
        private readonly Action<string> log;
        private AppSettings state;
        private int appliedMode = -1, currentMode = -1;
        private bool connected, gameRunning;
        private string[] runningProcesses = new string[0];
        private readonly List<string> runningOrder = new List<string>();
        private GameProfile activeProfile;
        private string status = "Connexion au moniteur…";
        public AutomationController(IMonitor monitor, AppSettings state, Action<AppSettings> save, Action<string> log, MonitorFleet fleet = null)
        { this.fleet = fleet; state.Validate(); this.monitor = monitor; this.state = state.Copy(); this.save = save; this.log = log; }
        public AppSnapshot Snapshot()
        { return new AppSnapshot { Displays = fleet == null ? null : displays, Settings = state.Copy(), CurrentMode = currentMode, Connected = connected, GameRunning = gameRunning, Status = status,
            ActiveProfileId = activeProfile == null ? null : activeProfile.Id, ActiveProfileName = activeProfile == null ? null : activeProfile.Name }; }
        private void Persist(AppSettings next) { save(next); state = next; }
        private void SetMode(int value)
        {
            if (fleet == null) monitor.SetMode(value);
            else fleet.Apply(displays, state.DisplaySelection, value);
            currentMode = value; connected = true;
        }
        public void Discover()
        {
            if (fleet == null) return;
            displays = new List<DisplayScan>();
            displays = fleet.Discover();
            appliedMode = -1;
            if (state.DisplaySelection == null && !state.RestorePending)
            {
                var legacy = displays.Where(d => d.ModelId == SamsungMonitor.HardwareId && (d.Connector == "HDMI" || d.Connector == "DisplayPort")).ToList();
                if (legacy.Count == 1 && legacy[0].Modes.Count > 0 && !string.IsNullOrEmpty(legacy[0].DevicePath)) SelectDisplays(new List<string> { legacy[0].DevicePath });
            }
        }

        public void SelectDisplays(List<string> paths)
        {
            if (state.RestorePending) throw new IOException("Attendez la restauration du format avant de changer de moniteurs.");
            var next = state.Copy();
            next.DisplaySelection = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (MonitorFleet.Selected(displays, next.DisplaySelection).Any(d => d == null)) throw new IOException("Moniteur indisponible.");
            Persist(next); appliedMode = currentMode = -1; connected = false;
            Refresh();
        }

        private void Capture(int desired, bool manual)
        {
            var next = state.Copy();
            if (fleet == null) next.OriginalMode = monitor.ReadMode();
            else next.DisplayOriginals = fleet.Capture(displays, state.DisplaySelection, desired);
            next.ManualRecovery = manual;
            next.RestorePending = true;
            Persist(next);
        }

        private void Restore()
        {
            appliedMode = -1;
            if (state.DisplayOriginals == null)
            {
                monitor.SetMode(state.OriginalMode); // Also handles pending 2.0.x recovery on the original G4.
                currentMode = state.OriginalMode;
                var next = state.Copy();
                next.RestorePending = false; next.ManualRecovery = false;
                Persist(next);
                return;
            }
            if (!state.ManualRecovery)
            {
                var restoring = state.Copy(); restoring.ManualRecovery = true; Persist(restoring);
            }
            var errors = new List<string>();
            foreach (var saved in state.DisplayOriginals.ToArray())
            {
                try
                {
                    fleet.Write(saved.Path, saved.Command, saved.Raw);
                    var next = state.Copy();
                    next.DisplayOriginals.RemoveAll(d => d.Path == saved.Path);
                    if (next.DisplayOriginals.Count == 0)
                    {
                        next.DisplayOriginals = null; next.RestorePending = false; next.ManualRecovery = false;
                    }
                    Persist(next);
                }
                catch (Exception error) { errors.Add(error.Message); }
            }
            currentMode = -1;
            if (errors.Count != 0) throw new IOException("Restauration en attente. " + string.Join(" ", errors));
        }

        public void Refresh()
        {
            try
            {
                if (fleet == null) currentMode = monitor.ReadMode();
                else
                {
                    var selected = MonitorFleet.Selected(displays, state.DisplaySelection);
                    if (selected.Count == 0 || selected.Any(d => d == null))
                    { currentMode = -1; connected = false; status = "Choisissez des moniteurs connectés."; return; }
                    var values = selected.Select(d => {
                        int raw = fleet.Read(d.DevicePath, d.Command);
                        return d.Modes.Where(pair => pair.Value == raw).Select(pair => pair.Key).DefaultIfEmpty(-1).First();
                    }).Distinct().ToList();
                    currentMode = values.Count == 1 ? values[0] : -1;
                }
                connected = true; UpdateStatus();
            }
            catch { connected = false; throw; }
        }
        public void SetEnabled(bool enabled)
        { AppSettings next = state.Copy(); next.Enabled = enabled; Persist(next); log("AUTOMATION enabled=" + enabled); Tick(runningProcesses); }
        public void SetAutoUpdate(bool enabled)
        { AppSettings next = state.Copy(); next.AutoUpdate = enabled; Persist(next); log("AUTO_UPDATE enabled=" + enabled); }
        public void SetDarkMode(bool dark)
        { AppSettings next = state.Copy(); next.DarkMode = dark; Persist(next); log("THEME dark=" + dark); }
        public void SetTarget(int value)
        {
            if (!ScreenPreset.IsSelectable(value)) throw new ArgumentOutOfRangeException("value");
            SetProfileTarget(GameProfile.CounterStrikeId, value);
        }
        public void SetProfileTarget(string id, int value)
        {
            if (!ScreenPreset.IsSelectable(value)) throw new ArgumentOutOfRangeException("value");
            AppSettings next = state.Copy();
            GameProfile profile = next.Profiles.FirstOrDefault(item => item.Id == id);
            if (profile == null) throw new IOException("Ce profil n’existe plus.");
            profile.TargetMode = value; next.SelectedMode = value;
            if (id == GameProfile.CounterStrikeId) next.TargetMode = value;
            Persist(next); log("PROFILE id=" + id + " target=" + value); Tick(runningProcesses);
        }
        public void SetProfiles(IEnumerable<GameProfile> profiles)
        {
            AppSettings next = state.Copy(); next.Profiles = GameProfile.ValidateAndCopy(profiles);
            GameProfile cs = next.Profiles.FirstOrDefault(profile => profile.Id == GameProfile.CounterStrikeId);
            if (cs != null) next.TargetMode = cs.TargetMode;
            Persist(next); log("PROFILES_SAVED count=" + next.Profiles.Count); Tick(runningProcesses);
        }
        public void ApplyManual(int value)
        {
            if (!ScreenPreset.IsSelectable(value)) throw new ArgumentOutOfRangeException("value");
            if (state.RestorePending || (state.Enabled && gameRunning))
                throw new InvalidOperationException("Désactivez l’automatisme avant un changement manuel pendant la partie.");
            if (fleet != null) Capture(value, true);
            try
            {
                SetMode(value);
                AppSettings next = state.Copy(); next.SelectedMode = value;
                next.ManualRecovery = next.RestorePending = false; next.DisplayOriginals = null; Persist(next);
            }
            catch { if (fleet != null && state.RestorePending) Restore(); throw; }
            log("MANUAL mode=" + value); UpdateStatus();
        }
        public void Tick(bool running)
        {
            GameProfile cs = state.Profiles.FirstOrDefault(profile => profile.Id == GameProfile.CounterStrikeId);
            Tick(running && cs != null ? cs.Processes : new string[0]);
        }
        public void Tick(IEnumerable<string> processes)
        {
            runningProcesses = processes == null ? new string[0] : processes.ToArray();
            HashSet<string> names = new HashSet<string>(runningProcesses, StringComparer.OrdinalIgnoreCase);
            List<GameProfile> matches = state.Profiles.Where(profile => profile.Enabled && profile.Processes.Any(names.Contains)).ToList();
            runningOrder.RemoveAll(id => !matches.Any(profile => profile.Id == id));
            foreach (GameProfile profile in matches) if (!runningOrder.Contains(profile.Id)) runningOrder.Add(profile.Id);
            if (activeProfile == null || !matches.Any(profile => profile.Id == activeProfile.Id))
                activeProfile = runningOrder.Count == 0 ? null : matches.First(profile => profile.Id == runningOrder[0]);
            else activeProfile = matches.First(profile => profile.Id == activeProfile.Id);
            gameRunning = activeProfile != null;
            try
            {
                if (state.ManualRecovery || (fleet != null && state.RestorePending && state.DisplayOriginals == null)) Restore();
                if (gameRunning && state.Enabled)
                {
                    if (!state.RestorePending)
                    {
                        Capture(activeProfile.TargetMode, false);
                    }
                    if (appliedMode != activeProfile.TargetMode)
                    {
                        appliedMode = -1;
                        try { SetMode(activeProfile.TargetMode); }
                        catch { if (fleet != null) Restore(); throw; }
                        appliedMode = activeProfile.TargetMode;
                        log("APPLIED mode=" + appliedMode + " profile=" + activeProfile.Id);
                    }
                }
                else if (state.RestorePending)
                {
                    Restore();
                    log("RESTORED mode=" + currentMode);
                }
                UpdateStatus();
            }
            catch { connected = false; throw; }
        }
        public void Close() { Tick(false); }
        private void UpdateStatus()
        {
            status = !state.Enabled ? "Automatisme désactivé" : gameRunning ?
                activeProfile.Name + " ouvert · " + ScreenPreset.Describe(activeProfile.TargetMode) : "Prêt · en attente d’un jeu";
        }
        public void RecordError(Exception error)
        { connected = false; status = error.Message; log("ERROR " + error.ToString()); }
    }
}
