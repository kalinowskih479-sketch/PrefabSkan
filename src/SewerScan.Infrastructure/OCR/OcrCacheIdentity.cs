namespace SewerScan.Infrastructure.OCR;

internal static class OcrCacheIdentity
{
    internal const int SchemaVersion = 1;
    internal const string AlgorithmVersion = "4.2.7";
    internal const string Identity = "ocr-schema-1_algorithm-4.2.7";

    internal static string CreatePath(string cacheRoot, string pdfHash) =>
        Path.Combine(cacheRoot, $"{Identity}_{pdfHash}.json");
}
