using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsNotch.Conversion
{
    public sealed record ConversionResult(
        bool Success,
        string? OutputPath,
        string? Error,
        IReadOnlyList<string> Warnings,
        bool IsScannedPdf,
        bool Cancelled,
        bool TimedOut);

    public sealed class ConversionClient
    {
        private static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(10);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public async Task<ConversionResult> ConvertAsync(
            string inputPath,
            ConversionTarget target,
            string outputPath,
            CancellationToken cancellationToken)
        {
            string jobDirectory = Path.Combine(
                Path.GetTempPath(),
                "Notchi",
                "convert");
            string jobPath = Path.Combine(
                jobDirectory,
                Guid.NewGuid().ToString("N") + ".job.json");
            string resultPath = jobPath + ".result.json";
            string tempPath = ConversionPaths.CreateTempOutputPath(outputPath, target);

            try
            {
                Directory.CreateDirectory(jobDirectory);
                CleanupOldJobs(jobDirectory);

                var job = new ConversionJobFile(
                    inputPath,
                    outputPath,
                    tempPath,
                    target.ToString());

                await File.WriteAllTextAsync(
                    jobPath,
                    JsonSerializer.Serialize(job, JsonOptions),
                    cancellationToken);

                ProcessStartInfo startInfo = CreateStartInfo(jobPath);

                using var process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                if (!process.Start())
                {
                    return Failure("The converter could not be started.");
                }

                using var timeoutCts = new CancellationTokenSource(ConversionTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCts.Token);

                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    Kill(process);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return new ConversionResult(
                            false,
                            null,
                            "Cancelled.",
                            Array.Empty<string>(),
                            false,
                            true,
                            false);
                    }

                    return new ConversionResult(
                        false,
                        null,
                        "Conversion timed out.",
                        Array.Empty<string>(),
                        false,
                        false,
                        true);
                }

                ConversionResultFile? result = null;

                try
                {
                    if (File.Exists(resultPath))
                    {
                        result = JsonSerializer.Deserialize<ConversionResultFile>(
                            await File.ReadAllTextAsync(resultPath, CancellationToken.None),
                            JsonOptions);
                    }
                }
                catch (Exception exception)
                {
                    CrashLogger.Log("ConversionClient.ReadResult", exception);
                }

                if (result == null)
                {
                    return Failure(
                        $"The converter exited with code {process.ExitCode}.");
                }

                if (result.Success &&
                    (string.IsNullOrWhiteSpace(result.OutputPath) ||
                     !File.Exists(result.OutputPath)))
                {
                    return Failure("The converter did not produce an output file.");
                }

                return new ConversionResult(
                    result.Success,
                    result.OutputPath,
                    result.Error,
                    result.Warnings ?? new List<string>(),
                    result.IsScannedPdf,
                    false,
                    false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new ConversionResult(
                    false,
                    null,
                    "Cancelled.",
                    Array.Empty<string>(),
                    false,
                    true,
                    false);
            }
            catch (Exception exception)
            {
                CrashLogger.Log("ConversionClient", exception);
                return Failure(exception.Message);
            }
            finally
            {
                TryDelete(jobPath);
                TryDelete(resultPath);
                TryDelete(tempPath);
            }
        }

        private static ProcessStartInfo CreateStartInfo(string jobPath)
        {
            string processPath =
                Environment.ProcessPath ??
                Process.GetCurrentProcess().MainModule?.FileName ??
                throw new InvalidOperationException(
                    "The Notchi executable path is unavailable.");

            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            string processName = Path.GetFileNameWithoutExtension(processPath);

            if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                string entryAssembly =
                    Assembly.GetEntryAssembly()?.Location ??
                    throw new InvalidOperationException(
                        "The Notchi assembly path is unavailable.");

                startInfo.FileName = processPath;
                startInfo.ArgumentList.Add(entryAssembly);
            }
            else
            {
                startInfo.FileName = processPath;
            }

            startInfo.ArgumentList.Add("--convert");
            startInfo.ArgumentList.Add(jobPath);
            return startInfo;
        }

        private static void CleanupOldJobs(string jobDirectory)
        {
            try
            {
                DateTime cutoff = DateTime.UtcNow.AddDays(-1);

                foreach (string path in Directory
                             .EnumerateFiles(jobDirectory, "*.json")
                             .Where(path =>
                                 File.GetLastWriteTimeUtc(path) < cutoff))
                {
                    TryDelete(path);
                }
            }
            catch
            {
            }
        }

        private static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch
            {
            }
        }

        private static ConversionResult Failure(string error)
        {
            return new ConversionResult(
                false,
                null,
                error,
                Array.Empty<string>(),
                false,
                false,
                false);
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
