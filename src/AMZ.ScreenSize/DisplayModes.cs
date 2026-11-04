using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AMZ.ScreenSize
{
    public static class DisplayModes
    {
        public static bool IsVideoConnector(int technology)
        { return technology == 5 || technology == 10; }

        public static void Resolve(DisplayScan display, uint? standard, uint? proprietary)
        {
            var modes = new Dictionary<int,int>();
            byte code = 0xDB;
            uint? current = proprietary;
            switch (display.ModelId)
            {
                case "SAM727E":
                case "SAM7476":
                    for (int i=0; i<8; i++) modes.Add(i,i);
                    if (display.ModelId == "SAM7476") { modes.Add(27,8); modes.Add(29,9); }
                    break;
                case "SAM06A3":
                    modes.Add(0,0); modes.Add(1,4);
                    break;
                case "ACI28A7":
                    code = 0x86; current = standard;
                    modes.Add(1,2); modes.Add(112,12);
                    break;
                case "ACR0692": break; // 86 is sharpness on this model.
                default:
                    code = 0x86; current = standard;
                    if (display.ScalingAdvertised == true && display.ScalingValuesAdvertised != null)
                    {
                        string hex = Regex.Replace(display.ScalingValuesAdvertised, @"\s", "");
                        if (Regex.IsMatch(hex, @"\A(?:[0-9a-fA-F]{2})+\z"))
                            for (int i=0; i<hex.Length; i+=2)
                            {
                                int raw = int.Parse(hex.Substring(i,2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                if (raw >= 1 && raw <= 10) modes[100+raw] = raw;
                            }
                    }
                    break;
            }
            display.Command = code;
            display.Modes = modes.Count >= 2 && current.HasValue && modes.ContainsValue((int)current.Value) ? modes : new Dictionary<int,int>();
            if (display.Modes.Count > 0 && display.Status == "scaling-read-candidate") display.Status = "standard-readable";
        }
    }
}
