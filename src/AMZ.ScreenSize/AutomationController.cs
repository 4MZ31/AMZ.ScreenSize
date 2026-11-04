using System;
using System.IO;

namespace AMZ.ScreenSize
{
    public sealed class AppSnapshot
    {
        public AppSettings Settings;
        public int CurrentMode;
        public bool Connected, GameRunning;
        public string Status;
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
        private string status = "Connexion au moniteur…";
        public AutomationController(IMonitor monitor, AppSettings state, Action<AppSettings> save, Action<string> log)
        { state.Validate(); this.monitor = monitor; this.state = state.Copy(); this.save = save; this.log = log; }
        public AppSnapshot Snapshot()
        { return new AppSnapshot { Settings = state.Copy(), CurrentMode = currentMode, Connected = connected, GameRunning = gameRunning, Status = status }; }
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
        { AppSettings next = state.Copy(); next.Enabled = enabled; Persist(next); log("AUTOMATION enabled=" + enabled); Tick(gameRunning); }
        public void SetTarget(int value)
        {
            if (!ScreenPreset.IsSelectable(value)) throw new ArgumentOutOfRangeException("value");
            AppSettings next = state.Copy(); next.TargetMode = value; next.SelectedMode = value;
            Persist(next); log("PROFILE target=" + value); Tick(gameRunning);
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
            gameRunning = running;
            try
            {
                if (running && state.Enabled)
                {
                    if (!state.RestorePending)
                    {
                        int original = monitor.ReadMode();
                        AppSettings next = state.Copy(); next.OriginalMode = original; next.RestorePending = true;
                        Persist(next); // Must succeed before any change to the physical screen.
                        log("CAPTURE original=" + original);
                    }
                    if (appliedMode != state.TargetMode)
                    {
                        SetMode(state.TargetMode); appliedMode = state.TargetMode;
                        log("APPLIED mode=" + appliedMode + " game_running=true");
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
                "Counter-Strike ouvert · " + ScreenPreset.Describe(state.TargetMode) : "Prêt · en attente de Counter-Strike";
        }
        public void RecordError(Exception error)
        { connected = false; status = error.Message; log("ERROR " + error.ToString()); }
    }
}
