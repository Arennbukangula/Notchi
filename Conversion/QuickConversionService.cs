using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsNotch.Conversion
{
    public sealed record QuickConversionProgress(
        int Index,
        int Total,
        string FileName,
        ConversionTarget Target);

    public sealed record QuickConversionOutcome(
        string InputPath,
        string? OutputPath,
        string? Error,
        bool Success);

    public sealed class QuickConversionService
    {
        public async Task<IReadOnlyList<QuickConversionOutcome>> ConvertAsync(
            IEnumerable<string> paths,
            IProgress<QuickConversionProgress>? progress,
            CancellationToken cancellationToken)
        {
            var inputs = new List<string>();

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                string fullPath;

                try
                {
                    fullPath = Path.GetFullPath(path);
                }
                catch
                {
                    continue;
                }

                if (!File.Exists(fullPath)) continue;
                if (!ConversionFormats.TryGetTarget(fullPath, out _)) continue;

                if (inputs.Any(existing =>
                        string.Equals(existing, fullPath,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                inputs.Add(fullPath);
            }

            var outcomes = new List<QuickConversionOutcome>(inputs.Count);
            if (inputs.Count == 0) return outcomes;

            string downloadsDirectory = ConversionPaths.GetDownloadsDirectory();
            Directory.CreateDirectory(downloadsDirectory);

            for (int index = 0; index < inputs.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string inputPath = inputs[index];

                if (!ConversionFormats.TryGetTarget(inputPath, out var target))
                {
                    outcomes.Add(new QuickConversionOutcome(
                        inputPath,
                        null,
                        "Unsupported conversion.",
                        false));
                    continue;
                }

                progress?.Report(new QuickConversionProgress(
                    index + 1,
                    inputs.Count,
                    Path.GetFileName(inputPath),
                    target));

                if (!ConversionPaths.TryCreateOutputPath(
                        inputPath,
                        target,
                        downloadsDirectory,
                        out string outputPath))
                {
                    outcomes.Add(new QuickConversionOutcome(
                        inputPath,
                        null,
                        "Unable to create the output path.",
                        false));
                    continue;
                }

                try
                {
                    var client = new ConversionClient();
                    var result = await client.ConvertAsync(
                        inputPath,
                        target,
                        outputPath,
                        cancellationToken);

                    if (result.Cancelled)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    bool success = result.Success &&
                        !string.IsNullOrWhiteSpace(result.OutputPath);

                    outcomes.Add(new QuickConversionOutcome(
                        inputPath,
                        success ? result.OutputPath : null,
                        result.Error,
                        success));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    CrashLogger.Log("QuickConversionService", exception);
                    outcomes.Add(new QuickConversionOutcome(
                        inputPath,
                        null,
                        exception.Message,
                        false));
                }
            }

            return outcomes;
        }
    }
}
