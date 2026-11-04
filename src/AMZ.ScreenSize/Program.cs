using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;

namespace AMZ.ScreenSize
{
    [DataContract]
    public sealed class Diagnostic
    {
        [DataMember] public string Version;
        [DataMember] public string HardwareId;
        [DataMember] public string Vcp;
        [DataMember] public int Mode;
        [DataMember] public string Label;
        [DataMember] public string Error;
    }
    public static class Program
    {
        public static string Version { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }
        private static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return null;
            if (index + 1 >= args.Length) throw new ArgumentException("Valeur manquante pour " + name);
            return args[index + 1];
        }
        public static bool Signal(string name)
        {
            try { using (EventWaitHandle handle = EventWaitHandle.OpenExisting(name)) handle.Set(); return true; }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }
        [STAThread]
        public static int Main(string[] args)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMZ.ScreenSize");
            try
            {
                if (args.Length == 2 && args[0] == "--apply-update") return UpdateManager.ApplyAndRelaunch(args[1]);
                directory = Argument(args, "--state-dir") ?? directory;
                string diagnosticPath = Argument(args, "--diagnostic");
                if (diagnosticPath != null)
                {
                    Diagnostic diagnostic = new Diagnostic { Version = Version, HardwareId = SamsungMonitor.HardwareId, Vcp = "0xDB", Mode = -1 };
                    try { diagnostic.Mode = new SamsungMonitor().ReadMode(); diagnostic.Label = ScreenPreset.Describe(diagnostic.Mode); }
                    catch (Exception error) { diagnostic.Error = error.Message; }
                    using (FileStream output = File.Create(diagnosticPath)) new DataContractJsonSerializer(typeof(Diagnostic)).WriteObject(output, diagnostic);
                    return diagnostic.Error == null ? 0 : 1;
                }
                if (Array.IndexOf(args, "--quit") >= 0) return Signal(TrayApplication.QuitName) ? 0 : 2;
                bool created;
                using (Mutex mutex = new Mutex(true, TrayApplication.MutexName, out created))
                {
                    if (!created) { Signal(TrayApplication.ShowName); return 0; }
                    try
                    {
                        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs eventArgs)
                        {
                            new SettingsStore(directory).Log("UI_ERROR " + eventArgs.Exception.ToString());
                            MessageBox.Show(eventArgs.Exception.Message, "AMZ.ScreenSize", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        };
                        string processes = Argument(args, "--processes");
                        string[] names = processes == null ? null : processes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        using (TrayApplication app = new TrayApplication(directory, Array.IndexOf(args, "--background") >= 0, names)) Application.Run(app);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception error)
            {
                try { new SettingsStore(directory).Log("STARTUP_ERROR " + error.ToString()); } catch { }
                MessageBox.Show(error.Message, "AMZ.ScreenSize", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
