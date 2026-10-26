using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WindowsNotch.Conversion
{
    public static class ConversionCli
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static int Run(string jobPath)
        {
            try
            {
                ConversionJobFile? job = JsonSerializer.Deserialize<ConversionJobFile>(
                    File.ReadAllText(jobPath),
                    JsonOptions);

                if (job == null)
                {
                    return WriteFailure(jobPath, "Invalid conversion job.");
                }

                if (!Enum.TryParse<ConversionTarget>(job.Target, true, out var target) ||
                    !ConversionFormats.IsCompatible(job.InputPath, target))
                {
                    return WriteFailure(jobPath, "Unsupported conversion.");
                }

                if (!File.Exists(job.InputPath))
                {
                    return WriteFailure(jobPath, "Input file was not found.");
                }

                string inputPath = Path.GetFullPath(job.InputPath);
                string outputPath = Path.GetFullPath(job.OutputPath);
                string tempPath = Path.GetFullPath(job.TempOutputPath);

                if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(inputPath, tempPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(outputPath, tempPath, StringComparison.OrdinalIgnoreCase))
                {
                    return WriteFailure(jobPath, "Invalid conversion paths.");
                }

                string? outputDirectory = Path.GetDirectoryName(outputPath);
                string? tempDirectory = Path.GetDirectoryName(tempPath);

                if (string.IsNullOrEmpty(outputDirectory) ||
                    string.IsNullOrEmpty(tempDirectory) ||
                    !string.Equals(outputDirectory, tempDirectory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return WriteFailure(jobPath, "Invalid conversion output directory.");
                }

                if (File.Exists(outputPath))
                {
                    return WriteFailure(jobPath, "Output file already exists.");
                }

                Directory.CreateDirectory(outputDirectory);

                var normalizedJob = job with
                {
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    TempOutputPath = tempPath
                };

                ConversionResultFile result = OfficeImoConverter.Convert(
                    normalizedJob,
                    CancellationToken.None);

                if (result.Success)
                {
                    try
                    {
                        File.Move(tempPath, outputPath, overwrite: false);
                        result = result with { OutputPath = outputPath };
                    }
                    catch (Exception exception)
                    {
                        TryDelete(tempPath);
                        result = Failure(exception.Message);
                    }
                }
                else
                {
                    TryDelete(tempPath);
                }

                WriteResult(jobPath, result);
                return result.Success ? 0 : 2;
            }
            catch (Exception exception)
            {
                CrashLogger.Log("ConversionCli", exception);
                WriteResult(jobPath, Failure(exception.Message));
                return 2;
            }
        }

        private static int WriteFailure(string jobPath, string error)
        {
            WriteResult(jobPath, Failure(error));
            return 2;
        }

        private static ConversionResultFile Failure(string error)
        {
            return new ConversionResultFile(
                false,
                null,
                error,
                new List<string>(),
                false);
        }

        private static void WriteResult(string jobPath, ConversionResultFile result)
        {
            try
            {
                string resultPath = jobPath + ".result.json";
                File.WriteAllText(
                    resultPath,
                    JsonSerializer.Serialize(result, JsonOptions));
            }
            catch (Exception exception)
            {
                CrashLogger.Log("ConversionCli.WriteResult", exception);
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
    }
}
