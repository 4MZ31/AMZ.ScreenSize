using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AMZ.ScreenSize
{
    public interface IMonitor
    {
        int ReadMode();
        void SetMode(int value);
    }

    internal static partial class DdcTransport
    {
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Info
        {
            public int Size;
            public Rect Bounds, Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Physical
        {
            public IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        }
        private delegate bool Callback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, Callback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref Info info);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] Physical[] physical);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint maximum);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
        [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitors(uint count, Physical[] physical);

        internal static T WithMonitor<T>(string hardwareId, Func<IntPtr, T> operation)
        {
            lock (BusLock) return WithTarget(hardwareId, false, operation);
        }

        internal static readonly object BusLock = new object();
        internal static T OnDevice<T>(string path, Func<IntPtr,T> operation)
        {
            lock (BusLock) return WithTarget(path, true, operation);
        }

        private static T WithTarget<T>(string key, bool byPath, Func<IntPtr,T> operation)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new IOException("Identité du moniteur manquante.");
            var topology = VideoTopology.Read();
            Func<VideoTarget,bool> match = t => t.Eligible && string.Equals(byPath ? t.DevicePath : t.Model, key, StringComparison.OrdinalIgnoreCase);
            int routes = 0;
            foreach (var group in topology.Values) foreach (var target in group) if (match(target)) routes++;
            if (routes != 1) throw new IOException("Moniteur débranché ou identification ambiguë.");
            IntPtr found = IntPtr.Zero;
            int matches = 0;
            string sourceName = null;
            bool complete = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr handle, IntPtr dc, ref Rect rect, IntPtr data)
                {
                    Info info = new Info(); info.Size = Marshal.SizeOf(typeof(Info));
                    if (!GetMonitorInfo(handle, ref info)) return true;
                    System.Collections.Generic.List<VideoTarget> targets;
                    if (topology.TryGetValue(info.Device, out targets) && targets.Count == 1 &&
                        match(targets[0]))
                    { found = handle; sourceName = info.Device; matches++; }
                    return true;
                }, IntPtr.Zero);
            if (!complete || matches == 0) throw new IOException("Chemin vidéo indisponible.");
            if (matches > 1) throw new IOException("Plusieurs chemins vidéo correspondent à ce moniteur.");
            uint count;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(found, out count) || count != 1)
                throw new IOException("Impossible d’identifier le moniteur physique.");
            Physical[] physical = new Physical[count];
            if (!GetPhysicalMonitorsFromHMONITOR(found, count, physical)) throw NativeError("Connexion au moniteur");
            try
            {
                System.Collections.Generic.List<VideoTarget> current;
                if (!VideoTopology.Read().TryGetValue(sourceName, out current) || current.Count != 1 || !match(current[0]))
                    throw new IOException("Le branchement a changé.");
                return operation(physical[0].Handle);
            }
            finally { DestroyPhysicalMonitors(count, physical); }
        }
        private static IOException NativeError(string operation)
        { return new IOException(operation + " : erreur DDC/CI 0x" + Marshal.GetLastWin32Error().ToString("X8") + "."); }

        internal static int Read(IntPtr handle, byte code)
        {
            uint type, current, maximum;
            if (!GetVCPFeatureAndVCPFeatureReply(handle, code, out type, out current, out maximum))
                throw NativeError("Lecture du format");
            return checked((int)current);
        }
        internal static void Write(IntPtr handle, byte code, uint value)
        {
            if (!SetVCPFeature(handle, code, value)) throw NativeError("Changement de format");
        }
    }

    public sealed class SamsungMonitor : IMonitor
    {
        public const string HardwareId = "SAM727E";
        public const byte SizeCode = 0xDB;
        private static T WithMonitor<T>(Func<IntPtr, T> operation)
        { return DdcTransport.WithMonitor(HardwareId, operation); }

        public int ReadMode()
        {
            return WithMonitor<int>(delegate(IntPtr handle)
            {
                int current = DdcTransport.Read(handle, SizeCode);
                if (current > 9) throw new IOException("Valeur de format inattendue : " + current + ".");
                return (int)current;
            });
        }
        public void SetMode(int value)
        {
            if (value < 0 || value > 9) throw new ArgumentOutOfRangeException("value");
            if (ReadMode() == value) return;
            WithMonitor<bool>(delegate(IntPtr handle)
            {
                DdcTransport.Write(handle, SizeCode, (uint)value);
                return true;
            });
            Exception last = null;
            // Display scaling can invalidate physical-monitor handles: reopen on every read.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Thread.Sleep(700);
                try
                {
                    if (ReadMode() == value) return;
                    last = new IOException("L’écran n’a pas confirmé le format demandé. Vérifiez Adaptive-Sync et Ultrawide Game View dans son menu.");
                }
                catch (Exception error) { last = error; }
            }
            throw new IOException("Le changement n’a pas pu être vérifié.", last);
        }
    }
}
