using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace AMZ.ScreenSize
{
    public sealed class AppSnapshot
    {
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
        private readonly Action<AppSettings> save;
        private readonly Action<string> log;
        private AppSettings state;
        private int appliedMode = -1, currentMode = -1;
        private bool connected, gameRunning;
        private string[] runningProcesses = new string[0];
        private readonly List<string> runningOrder = new List<string>();
        private GameProfile activeProfile;
        private string status = "Connexion au moniteur…";
        public AutomationController(IMonitor monitor, AppSettings state, Action<AppSettings> save, Action<string> log)
        { state.Validate(); this.monitor = monitor; this.state = state.Copy(); this.save = save; this.log = log; }
        public AppSnapshot Snapshot()
        { return new AppSnapshot { Settings = state.Copy(), CurrentMode = currentMode, Connected = connected, GameRunning = gameRunning, Status = status,
            ActiveProfileId = activeProfile == null ? null : activeProfile.Id, ActiveProfileName = activeProfile == null ? null : activeProfile.Name }; }
        private void Persist(AppSettings next) { save(next); state = next; }
        private void SetMode(int value)
        {
            monitor.SetMode(value);
            currentMode = value; connected = true;
        }
        public void Refresh()
        {
            try { currentMode = monitor.ReadMode(); connected = true; UpdateStatus(); }
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
            SetMode(value);
            AppSettings next = state.Copy(); next.SelectedMode = value; Persist(next);
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
                if (gameRunning && state.Enabled)
                {
                    if (!state.RestorePending)
                    {
                        int original = monitor.ReadMode();
                        AppSettings next = state.Copy(); next.OriginalMode = original; next.RestorePending = true;
                        Persist(next); // Must succeed before any change to the physical screen.
                        log("CAPTURE original=" + original);
                    }
                    if (appliedMode != activeProfile.TargetMode)
                    {
                        SetMode(activeProfile.TargetMode); appliedMode = activeProfile.TargetMode;
                        log("APPLIED mode=" + appliedMode + " profile=" + activeProfile.Id);
                    }
                }
                else if (state.RestorePending)
                {
                    SetMode(state.OriginalMode);
                    AppSettings next = state.Copy(); next.RestorePending = false;
                    appliedMode = -1;
                    Persist(next);
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
