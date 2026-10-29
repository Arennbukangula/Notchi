using System;
using System.IO;
using System.Runtime.InteropServices;

namespace WindowsNotch.Conversion
{
    public static class ConversionPaths
    {
        private static readonly Guid DownloadsFolderId =
            new("374DE290-123F-4565-9164-39C4925E467B");

        public static bool TryCreateOutputPath(
            string inputPath,
            ConversionTarget target,
            out string outputPath)
        {
            return TryCreateOutputPath(inputPath, target, null, out outputPath);
        }

        public static bool TryCreateOutputPath(
            string inputPath,
            ConversionTarget target,
            string? destinationDirectory,
            out string outputPath)
        {
            outputPath = string.Empty;

            try
            {
                if (!File.Exists(inputPath)) return false;

                string directory = string.IsNullOrWhiteSpace(destinationDirectory)
                    ? Path.GetDirectoryName(inputPath) ?? string.Empty
                    : destinationDirectory;
                string baseName = Path.GetFileNameWithoutExtension(inputPath);
                string extension = ConversionFormats.GetExtension(target);

                string candidate = Path.Combine(directory, $"{baseName}{extension}");
                int index = 2;

                while (File.Exists(candidate))
                {
                    candidate = Path.Combine(
                        directory,
                        $"{baseName} ({index}){extension}");
                    index++;
                }

                outputPath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string CreateTempOutputPath(string outputPath, ConversionTarget target)
        {
            string extension = ConversionFormats.GetExtension(target);
            return $"{outputPath}.{Guid.NewGuid():N}.part{extension}";
        }

        public static string GetDownloadsDirectory()
        {
            IntPtr pathPtr = IntPtr.Zero;

            try
            {
                var folderId = DownloadsFolderId;
                if (SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out pathPtr) == 0 &&
                    pathPtr != IntPtr.Zero)
                {
                    string? path = Marshal.PtrToStringUni(pathPtr);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // Fallback ke lokasi default di bawah
            }
            finally
            {
                if (pathPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pathPtr);
                }
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(
            ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
    }
}
