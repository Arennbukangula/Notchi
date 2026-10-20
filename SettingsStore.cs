using System;
using System.IO;
using System.Text.Json;

namespace WindowsNotch
{
    public static class SettingsStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        public static string SettingsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Notchi", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AppSettings();

                return JsonSerializer.Deserialize<AppSettings>(
                           File.ReadAllText(SettingsPath), SerializerOptions)
                       ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static bool Save(AppSettings settings)
        {
            try
            {
                string? directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temporaryPath = SettingsPath + ".tmp";
                File.WriteAllText(temporaryPath,
                    JsonSerializer.Serialize(settings, SerializerOptions));
                File.Move(temporaryPath, SettingsPath, overwrite: true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
