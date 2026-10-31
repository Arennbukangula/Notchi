using System;
using System.IO;

namespace WindowsNotch.Conversion
{
    public enum ConversionTarget
    {
        Pdf,
        Docx
    }

    public static class ConversionFormats
    {
        public const string PdfExtension = ".pdf";
        public const string DocxExtension = ".docx";
        public const string DocExtension = ".doc";

        public static bool TryGetTarget(string inputPath, out ConversionTarget target)
        {
            target = default;
            if (string.IsNullOrWhiteSpace(inputPath)) return false;

            string extension = Path.GetExtension(inputPath);

            if (extension.Equals(PdfExtension, StringComparison.OrdinalIgnoreCase))
            {
                target = ConversionTarget.Docx;
                return true;
            }

            if (extension.Equals(DocxExtension, StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(DocExtension, StringComparison.OrdinalIgnoreCase))
            {
                target = ConversionTarget.Pdf;
                return true;
            }

            return false;
        }

        public static bool IsLegacyDoc(string inputPath)
        {
            return Path.GetExtension(inputPath)
                .Equals(DocExtension, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCompatible(string inputPath, ConversionTarget target)
        {
            return TryGetTarget(inputPath, out var expected) && expected == target;
        }

        public static string GetExtension(ConversionTarget target)
        {
            return target == ConversionTarget.Pdf ? PdfExtension : DocxExtension;
        }

        public static string GetDisplayName(ConversionTarget target)
        {
            return target == ConversionTarget.Pdf ? "PDF" : "DOCX";
        }
    }
}
