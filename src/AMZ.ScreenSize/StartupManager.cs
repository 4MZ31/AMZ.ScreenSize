using System;
using Microsoft.Win32;

namespace AMZ.ScreenSize
{
    public static class StartupManager
    {
        private const string KeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string Name = "AMZ.ScreenSize";
        public static bool Enabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                    return key != null && key.GetValue(Name) != null;
            }
        }
        public static void SetEnabled(bool enabled, string executable)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (enabled) key.SetValue(Name, "\"" + executable + "\" --background", RegistryValueKind.String);
                else key.DeleteValue(Name, false);
            }
        }
    }
}
