using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AMZ.ScreenSize
{
    internal sealed class VideoTarget
    {
        internal string Name, Model, DevicePath;
        internal int Technology;
        internal bool Eligible { get { return DisplayModes.IsVideoConnector(Technology); } }
        internal string Connector { get { return Technology == 5 ? "HDMI" : Technology == 10 ? "DisplayPort" : "Other (" + Technology + ")"; } }
    }

    internal static class VideoTopology
    {
        [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] private struct Source { public Luid Adapter; public uint Id, Mode, Status; }
        [StructLayout(LayoutKind.Sequential)] private struct Target
        {
            public Luid Adapter;
            public uint Id, Mode;
            public int Technology;
            public uint Rotation, Scaling, Numerator, Denominator, ScanOrder;
            public int Available;
            public uint Status;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Path { public Source Source; public Target Target; public uint Flags; }
        [StructLayout(LayoutKind.Explicit, Size = 64)] private struct Mode { [FieldOffset(0)] public uint Type; }
        [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct SourceName
        {
            public Header Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Name;
        }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct TargetName
        {
            public Header Header;
            public uint Flags, Technology;
            public ushort Manufacturer, Product;
            public uint ConnectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DevicePath;
        }
        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint count, [Out] Path[] paths, ref uint modeCount, [Out] Mode[] modes, IntPtr topology);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SourceName data);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TargetName data);

        internal static Dictionary<string,List<VideoTarget>> Read()
        {
            const uint active = 2;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint paths, modes;
                int rc = GetDisplayConfigBufferSizes(active, out paths, out modes);
                if (rc != 0) throw new Win32Exception(rc);
                Path[] buffer = new Path[paths];
                rc = QueryDisplayConfig(active, ref paths, buffer, ref modes, new Mode[modes], IntPtr.Zero);
                if (rc == 122) continue;
                if (rc != 0) throw new Win32Exception(rc);
                var map = new Dictionary<string,List<VideoTarget>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < paths; i++)
                {
                    Path path = buffer[i];
                    var source = new SourceName();
                    source.Header = new Header { Type=1, Size=(uint)Marshal.SizeOf(typeof(SourceName)), Adapter=path.Source.Adapter, Id=path.Source.Id };
                    rc = DisplayConfigGetDeviceInfo(ref source);
                    if (rc != 0) throw new Win32Exception(rc);
                    var name = new TargetName();
                    name.Header = new Header { Type=2, Size=(uint)Marshal.SizeOf(typeof(TargetName)), Adapter=path.Target.Adapter, Id=path.Target.Id };
                    bool identified = DisplayConfigGetDeviceInfo(ref name) == 0;
                    Match model = Regex.Match(name.DevicePath ?? "", @"DISPLAY#([A-Z0-9]+)#", RegexOptions.IgnoreCase);
                    var target = new VideoTarget { Technology=path.Target.Technology, Name=identified ? name.Name : null,
                        Model=model.Success ? model.Groups[1].Value.ToUpperInvariant() : null, DevicePath=identified ? name.DevicePath : null };
                    if (!map.ContainsKey(source.Name)) map.Add(source.Name, new List<VideoTarget>());
                    map[source.Name].Add(target);
                }
                return map;
            }
            throw new Win32Exception(122);
        }
    }
}
