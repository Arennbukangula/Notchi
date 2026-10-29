using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OfficeIMO;
using OfficeIMO.Pdf;
using OfficeIMO.Word;
using OfficeIMO.Word.LegacyDoc;
using OfficeIMO.Word.Pdf;

namespace WindowsNotch.Conversion
{
    public static class OfficeImoConverter
    {
        public static ConversionResultFile Convert(
            ConversionJobFile job,
            CancellationToken cancellationToken)
        {
            try
            {
                return job.Target switch
                {
                    nameof(ConversionTarget.Pdf) => ConversionFormats.IsLegacyDoc(job.InputPath)
                        ? LegacyDocToPdf(job, cancellationToken)
                        : DocxToPdf(job, cancellationToken),
                    nameof(ConversionTarget.Docx) => PdfToDocx(job, cancellationToken),
                    _ => Failure("Unsupported conversion target.")
                };
            }
            catch (Exception exception)
            {
                CrashLogger.Log("OfficeImoConverter.Convert", exception);
                return Failure(exception.Message);
            }
        }

        private static ConversionResultFile DocxToPdf(
            ConversionJobFile job,
            CancellationToken cancellationToken)
        {
            string? sanitizedPath = null;

            try
            {
                string sanitizedDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "Notchi",
                    "convert");
                Directory.CreateDirectory(sanitizedDirectory);

                string candidate = Path.Combine(
                    sanitizedDirectory,
                    Guid.NewGuid().ToString("N") + ".docx");

                string loadPath = job.InputPath;

                if (DocxSanitizer.TryCreateSanitizedCopy(
                        job.InputPath,
                        candidate,
                        out bool changed))
                {
                    if (changed)
                    {
                        sanitizedPath = candidate;
                        loadPath = candidate;
                    }
                    else
                    {
                        TryDelete(candidate);
                    }
                }

                using WordDocument word = WordDocument.Load(loadPath);

                PdfSaveResult result = word.SaveAsPdfResult(
                    job.TempOutputPath,
                    new WordToPdfOptions(),
                    cancellationToken);

                if (!result.Succeeded)
                {
                    if (result.Exception != null)
                    {
                        CrashLogger.Log("OfficeImoConverter.DocxToPdf", result.Exception);
                    }

                    return Failure(
                        result.Exception?.Message ?? "Word to PDF conversion failed.",
                        ToMessages(result.Warnings));
                }

                return new ConversionResultFile(
                    true,
                    job.TempOutputPath,
                    null,
                    ToMessages(result.Warnings),
                    false);
            }
            finally
            {
                if (sanitizedPath != null)
                {
                    TryDelete(sanitizedPath);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static ConversionResultFile LegacyDocToPdf(
            ConversionJobFile job,
            CancellationToken cancellationToken)
        {
            using FileStream input = File.OpenRead(job.InputPath);

            PdfDocumentConversionResult conversion =
                LegacyDocPdfConverter.ToPdfDocumentResult(
                    input,
                    new WordToPdfOptions(),
                    new LegacyDocImportOptions
                    {
                        ReportUnsupportedContent = true
                    },
                    OfficeConversionLossPolicy.Allow,
                    cancellationToken);

            PdfSaveResult save = conversion.SaveResult(
                job.TempOutputPath,
                cancellationToken);

            if (!save.Succeeded)
            {
                List<string> failureWarnings = ToMessages(conversion.Warnings)
                    .Concat(ToMessages(save.Warnings))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                return Failure(
                    save.Exception?.Message ?? "Legacy DOC to PDF conversion failed.",
                    failureWarnings);
            }

            List<string> warnings = ToMessages(conversion.Warnings)
                .Concat(ToMessages(save.Warnings))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (conversion.HasLoss || save.HasLoss)
            {
                warnings.Insert(
                    0,
                    "legacy-doc-loss: Legacy DOC content may have been omitted or changed.");
            }

            return new ConversionResultFile(
                true,
                job.TempOutputPath,
                null,
                warnings,
                false);
        }

        private static ConversionResultFile PdfToDocx(
            ConversionJobFile job,
            CancellationToken cancellationToken)
        {
            PdfDocument pdf = PdfDocument.Load(
                job.InputPath,
                PdfLoadOptions.Default,
                cancellationToken);

            PdfDocumentReadResult read = pdf.Read(
                new PdfReadOptions
                {
                    IncludeImages = true,
                    IncludeFormWidgets = true
                },
                cancellationToken);

            if (string.IsNullOrWhiteSpace(read.Text))
            {
                return new ConversionResultFile(
                    false,
                    null,
                    "Scanned PDF has no text layer.",
                    new List<string>(),
                    true);
            }

            var import = read.ToWordDocumentResult(
                new PdfToWordOptions(),
                cancellationToken);

            using WordDocument word = import.Value;
            word.Save(job.TempOutputPath);

            return new ConversionResultFile(
                true,
                job.TempOutputPath,
                null,
                ToMessages(import.Report?.Warnings),
                false);
        }

        private static List<string> ToMessages(
            IEnumerable<PdfConversionWarning>? warnings)
        {
            return warnings?
                .Select(warning => $"{warning.Code}: {warning.Message}")
                .ToList() ?? new List<string>();
        }

        private static ConversionResultFile Failure(
            string error,
            List<string>? warnings = null)
        {
            return new ConversionResultFile(
                false,
                null,
                error,
                warnings ?? new List<string>(),
                false);
        }
    }
}
