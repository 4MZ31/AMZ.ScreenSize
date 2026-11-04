using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AMZ.ScreenSize
{
    public sealed class DisplayScan
    {
        public string WindowsDisplay, ModelId, Description, Capabilities, CapabilitiesError, Status, Error;
        public string Connector, DevicePath;
        public byte Command;
        public Dictionary<int,int> Modes = new Dictionary<int,int>();
        public bool? ScalingAdvertised;
        public string ScalingValuesAdvertised;
        public List<FeatureRead> Reads = new List<FeatureRead>();
    }

    public sealed class FeatureRead
    {
        public string Code, Error;
        public uint? Current, Maximum;
    }

    internal static partial class DdcTransport
    {
        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetCapabilitiesStringLength(IntPtr handle, out uint length);
        [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr handle, StringBuilder text, uint length);

        private static string LastError()
        {
            return "0x" + Marshal.GetLastWin32Error().ToString("X8");
        }

        private static FeatureRead Probe(IntPtr handle, byte code)
        {
            FeatureRead result = new FeatureRead { Code = code.ToString("X2") };
            uint type, current, maximum;
            if (GetVCPFeatureAndVCPFeatureReply(handle, code, out type, out current, out maximum))
            {
                result.Current = current;
                result.Maximum = maximum;
            }
            else result.Error = LastError();
            return result;
        }

        internal static void ReadScalingDeclaration(DisplayScan result)
        {
            if (string.IsNullOrEmpty(result.Capabilities)) return;
            Match group = Regex.Match(result.Capabilities, @"\bvcp\s*\(", RegexOptions.IgnoreCase);
            if (!group.Success) return;
            // Some devices concatenate hex pairs. Skip complete value lists of other features.
            int offset = group.Index + group.Length;
            Regex feature = new Regex(@"\G\s*([a-fA-F0-9]{2})(?:\s*\(([^()]*)\))?");
            while (offset < result.Capabilities.Length)
            {
                Match item = feature.Match(result.Capabilities, offset);
                if (!item.Success)
                {
                    if (result.Capabilities.Substring(offset).TrimStart().StartsWith(")"))
                        result.ScalingAdvertised = false;
                    return;
                }
                if (item.Groups[1].Value == "86")
                {
                    result.ScalingAdvertised = true;
                    result.ScalingValuesAdvertised = item.Groups[2].Success ? item.Groups[2].Value : null;
                    return;
                }
                offset += item.Length;
            }
        }

        private static void Inspect(IntPtr handle, DisplayScan result)
        {
            uint length;
            if (!GetCapabilitiesStringLength(handle, out length)) result.CapabilitiesError = LastError();
            else if (length == 0 || length > 65536) result.CapabilitiesError = "Invalid capabilities length";
            else
            {
                StringBuilder buffer = new StringBuilder((int)length);
                if (CapabilitiesRequestAndCapabilitiesReply(handle, buffer, length)) result.Capabilities = buffer.ToString();
                else result.CapabilitiesError = LastError();
            }
            ReadScalingDeclaration(result);
            result.Reads.Add(Probe(handle, 0xDF));
            FeatureRead scaling = Probe(handle, 0x86);
            result.Reads.Add(scaling);
            FeatureRead vendor = null;
            if (result.ModelId == SamsungMonitor.HardwareId || result.ModelId == "SAM7476" || result.ModelId == "SAM06A3")
            {
                vendor = Probe(handle, 0xDB);
                result.Reads.Add(vendor);
            }
            result.Status = Classify(result.ModelId, scaling.Current, result.ScalingAdvertised, vendor == null ? null : vendor.Current);
            DisplayModes.Resolve(result, scaling.Current, vendor == null ? null : vendor.Current);
        }

        internal static string Classify(string model, uint? standard, bool? advertised, uint? vendor)
        {
            if (model == SamsungMonitor.HardwareId && vendor.HasValue && vendor.Value < ScreenPreset.All.Count)
                return "known-g4-readable";
            if ((model == "SAM7476" && vendor <= 9) || (model == "SAM06A3" && (vendor == 0 || vendor == 4)) ||
                (model == "ACI28A7" && (standard == 2 || standard == 12))) return "documented-readable";
            if (model == "ACR0692") return "undetermined";
            if (standard >= 1 && standard <= 255) return "scaling-read-candidate";
            return advertised == true ? "scaling-advertised-unconfirmed" : "undetermined";
        }

        private static void ScanLogical(IntPtr logical, Dictionary<string,List<VideoTarget>> topology, List<DisplayScan> results)
        {
            Info info = new Info { Size = Marshal.SizeOf(typeof(Info)) };
            if (!GetMonitorInfo(logical, ref info)) throw new Win32Exception();
            List<VideoTarget> targets;
            if (!topology.TryGetValue(info.Device, out targets))
            {
                results.Add(new DisplayScan { WindowsDisplay = info.Device, Status = "unidentified-path" });
                return;
            }
            foreach (VideoTarget target in targets)
            {
                DisplayScan result = new DisplayScan { WindowsDisplay = info.Device, ModelId = target.Model,
                    DevicePath = target.DevicePath, Description = string.IsNullOrWhiteSpace(target.Name) ? target.Model : target.Name, Connector = target.Connector, Status = "undetermined" };
                results.Add(result);
                if (!target.Eligible) { result.Status = "excluded-connector"; continue; }
                if (targets.Count != 1) { result.Status = "ambiguous-path"; continue; }
                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(logical, out count))
                { result.Error = LastError(); continue; }
                if (count != 1) { result.Status = "ambiguous-path"; continue; }
                Physical[] physical = new Physical[count];
                if (!GetPhysicalMonitorsFromHMONITOR(logical, count, physical))
                { result.Error = LastError(); continue; }
                try
                {
                    lock (BusLock)
                    {
                        List<VideoTarget> latest;
                        if (string.IsNullOrEmpty(target.DevicePath) || !VideoTopology.Read().TryGetValue(info.Device, out latest) ||
                            latest.Count != 1 || !latest[0].Eligible || !string.Equals(latest[0].DevicePath, target.DevicePath, StringComparison.OrdinalIgnoreCase))
                        { result.Status = "unidentified-path"; continue; }
                        Inspect(physical[0].Handle, result);
                    }
                }
                finally { DestroyPhysicalMonitors(count, physical); }
            }
        }

        internal static List<DisplayScan> Scan()
        {
            var topology = VideoTopology.Read();
            List<DisplayScan> results = new List<DisplayScan>();
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr h, IntPtr dc, ref Rect rect, IntPtr data)
            {
                try { ScanLogical(h, topology, results); }
                catch (Exception error) { results.Add(new DisplayScan { Status = "undetermined", Error = error.Message }); }
                return true;
            }, IntPtr.Zero)) throw new Win32Exception();
            return results;
        }
    }
}
