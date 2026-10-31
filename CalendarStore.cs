using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WindowsNotch
{
    public static class CalendarStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        public static string NotesPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Notchi", "calendar_notes.json");

        public static Dictionary<string, string> Load()
        {
            try
            {
                if (!File.Exists(NotesPath)) return new Dictionary<string, string>();

                return JsonSerializer.Deserialize<Dictionary<string, string>>(
                           File.ReadAllText(NotesPath), SerializerOptions)
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        public static bool Save(Dictionary<string, string> notes)
        {
            try
            {
                string? directory = Path.GetDirectoryName(NotesPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temporaryPath = NotesPath + ".tmp";
                File.WriteAllText(temporaryPath,
                    JsonSerializer.Serialize(notes, SerializerOptions));
                File.Move(temporaryPath, NotesPath, overwrite: true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
