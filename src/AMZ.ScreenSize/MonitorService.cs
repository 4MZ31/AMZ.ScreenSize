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

    public sealed class SamsungMonitor : IMonitor
    {
        public const string HardwareId = "SAM727E";
        public const byte SizeCode = 0xDB;
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
        private struct Device
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
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
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string name, uint index, ref Device device, uint flags);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] Physical[] physical);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint maximum);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
        [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitors(uint count, Physical[] physical);

        private static T WithMonitor<T>(Func<IntPtr, T> operation)
        {
            IntPtr found = IntPtr.Zero;
            int matches = 0;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr handle, IntPtr dc, ref Rect rect, IntPtr data)
                {
                    Info info = new Info(); info.Size = Marshal.SizeOf(typeof(Info));
                    if (!GetMonitorInfo(handle, ref info)) return true;
                    for (uint index = 0; ; index++)
                    {
                        Device device = new Device(); device.Size = Marshal.SizeOf(typeof(Device));
                        if (!EnumDisplayDevices(info.Device, index, ref device, 0)) break;
                        if (device.Id != null && device.Id.IndexOf("MONITOR\\" + HardwareId + "\\", StringComparison.OrdinalIgnoreCase) >= 0)
                        { found = handle; matches++; break; }
                    }
                    return true;
                }, IntPtr.Zero);
            if (matches == 0) throw new IOException("Samsung Odyssey G4 introuvable. Vérifiez le câble et la mise en veille.");
            if (matches > 1) throw new IOException("Plusieurs écrans SAM727E détectés. Débranchez les écrans identiques supplémentaires.");
            uint count;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(found, out count) || count != 1)
                throw new IOException("Impossible d’identifier le moniteur physique Samsung.");
            Physical[] physical = new Physical[count];
            if (!GetPhysicalMonitorsFromHMONITOR(found, count, physical)) throw NativeError("Connexion au moniteur");
            try { return operation(physical[0].Handle); }
            finally { DestroyPhysicalMonitors(count, physical); }
        }
        private static IOException NativeError(string operation)
        { return new IOException(operation + " : erreur DDC/CI 0x" + Marshal.GetLastWin32Error().ToString("X8") + "."); }

        public int ReadMode()
        {
            return WithMonitor<int>(delegate(IntPtr handle)
            {
                uint type, current, maximum;
                if (!GetVCPFeatureAndVCPFeatureReply(handle, SizeCode, out type, out current, out maximum))
                    throw NativeError("Lecture du format");
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
                if (!SetVCPFeature(handle, SizeCode, (uint)value)) throw NativeError("Changement de format");
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
