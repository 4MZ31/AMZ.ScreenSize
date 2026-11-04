using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AMZ.ScreenSize
{
    public sealed class SavedDisplay
    {
        public string Path;
        public byte Command;
        public int Raw;
    }

    public class MonitorFleet
    {
        public virtual List<DisplayScan> Discover() { return DdcTransport.Scan(); }

        public virtual int Read(string path, byte code)
        { return DdcTransport.OnDevice(path, h => DdcTransport.Read(h, code)); }

        public virtual void Write(string path, byte code, int raw)
        {
            if (Read(path, code) == raw) return;
            DdcTransport.OnDevice(path, h => { DdcTransport.Write(h, code, (uint)raw); return true; });
            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(700);
                try { if (Read(path, code) == raw) return; } catch (IOException) { }
            }
            throw new IOException("Le moniteur n’a pas confirmé le format demandé.");
        }

        public static List<DisplayScan> Selected(List<DisplayScan> all, List<string> paths)
        {
            if (paths == null) return new List<DisplayScan>();
            return paths.Select(path => all.FirstOrDefault(d => string.Equals(d.DevicePath, path, StringComparison.OrdinalIgnoreCase) && d.Modes.Count > 0)).ToList();
        }

        public static int[] Common(List<DisplayScan> all, List<string> paths)
        {
            var selected = Selected(all, paths);
            if (selected.Count == 0 || selected.Any(d => d == null)) return new int[0];
            return selected[0].Modes.Keys.Where(k => selected.All(d => d.Modes.ContainsKey(k))).OrderBy(k => k).ToArray();
        }

        public List<SavedDisplay> Capture(List<DisplayScan> all, List<string> paths, int format)
        {
            if (!Common(all, paths).Contains(format)) throw new IOException("Choisissez un format disponible sur tous les moniteurs sélectionnés.");
            var originals = new List<SavedDisplay>();
            foreach (DisplayScan display in Selected(all, paths))
            {
                int raw = Read(display.DevicePath, display.Command);
                if (!display.Modes.ContainsValue(raw)) throw new IOException("Le format actuel de ce moniteur est inconnu.");
                originals.Add(new SavedDisplay { Path = display.DevicePath, Command = display.Command, Raw = raw });
            }
            return originals;
        }

        public void Apply(List<DisplayScan> all, List<string> paths, int format)
        {
            if (!Common(all, paths).Contains(format)) throw new IOException("Format indisponible pour cette sélection.");
            foreach (DisplayScan display in Selected(all, paths))
                Write(display.DevicePath, display.Command, display.Modes[format]);
        }

    }
}
