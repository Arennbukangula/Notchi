using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace WindowsNotch
{
    public static class StartupService
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";

        private const string ValueName = "Notchi";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string value &&
                       !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (key == null) return false;

                if (enabled)
                {
                    string? executablePath = GetExecutablePath();
                    if (string.IsNullOrWhiteSpace(executablePath)) return false;

                    key.SetValue(ValueName, $"\"{executablePath}\"",
                        RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool RefreshPathIfEnabled()
        {
            return !IsEnabled() || SetEnabled(true);
        }

        private static string? GetExecutablePath()
        {
            string? path = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(path) ||
                !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                path = Process.GetCurrentProcess().MainModule?.FileName;
            }

            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
    }
}
