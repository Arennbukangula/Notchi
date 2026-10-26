namespace WindowsNotch.Conversion
{
    public sealed record ConversionJobFile(
        string InputPath,
        string OutputPath,
        string TempOutputPath,
        string Target);

    public sealed record ConversionResultFile(
        bool Success,
        string? OutputPath,
        string? Error,
        List<string> Warnings,
        bool IsScannedPdf);
}
