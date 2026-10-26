using System;
using System.IO;

namespace WindowsNotch.Conversion
{
    public static class ConversionPaths
    {
        public static bool TryCreateOutputPath(
            string inputPath,
            ConversionTarget target,
            out string outputPath)
        {
            outputPath = string.Empty;

            try
            {
                if (!File.Exists(inputPath)) return false;

                string directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
                string baseName = Path.GetFileNameWithoutExtension(inputPath);
                string extension = ConversionFormats.GetExtension(target);

                string candidate = Path.Combine(directory, $"{baseName} (converted){extension}");
                int index = 2;

                while (File.Exists(candidate))
                {
                    candidate = Path.Combine(
                        directory,
                        $"{baseName} (converted {index}){extension}");
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
    }
}
