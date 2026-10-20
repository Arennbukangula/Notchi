using System;
using System.IO;

namespace WindowsNotch
{
    public static class CrashLogger
    {
        private const long MaxLogBytes = 1_000_000;

        private static readonly object Gate = new();

        public static string LogPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Notchi", "notchi.log");

        public static void Log(string source, Exception exception)
        {
            try
            {
                lock (Gate)
                {
                    string? directory = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var info = new FileInfo(LogPath);
                    if (info.Exists && info.Length > MaxLogBytes)
                    {
                        File.Move(LogPath, LogPath + ".old", overwrite: true);
                    }

                    string entry =
                        $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {source}{Environment.NewLine}" +
                        $"{exception}{Environment.NewLine}{Environment.NewLine}";

                    File.AppendAllText(LogPath, entry);
                }
            }
            catch
            {
                // Logging must never take down the app.
            }
        }
    }
}
