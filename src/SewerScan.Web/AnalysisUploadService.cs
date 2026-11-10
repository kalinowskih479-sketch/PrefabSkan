using SewerScan.Application.DTO;
using SewerScan.Application.Interfaces;
using SewerScan.Application.Services;

namespace SewerScan.Web;

/// <summary>
/// Validates PDFs, preserves filename-based PZT/profile classification and cleans
/// up temporary copies even when individual files cannot be parsed.
/// </summary>
internal sealed class AnalysisUploadService
{
    private const long MaximumPdfSize = 25L * 1024 * 1024;
    private const long MaximumTotalSize = 80L * 1024 * 1024;
    private const int MaximumFiles = 10;

    public string? Validate(IFormFileCollection files)
    {
        if (files.Count == 0)
            return "Wybierz co najmniej jeden plik PDF.";
        if (files.Count > MaximumFiles)
            return $"Maksymalnie {MaximumFiles} plików PDF w jednym zestawie.";
        if (files.Sum(file => file.Length) > MaximumTotalSize)
            return "Łączny rozmiar plików nie może przekroczyć 80 MB.";

        foreach (var file in files)
        {
            if (file.Length <= 0 || file.Length > MaximumPdfSize)
                return "Każdy plik PDF musi mieć od 1 bajtu do 25 MB.";

            if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
                return "Dozwolone są wyłącznie pliki z rozszerzeniem .pdf.";
        }
        return null;
    }

    public async Task<AnalysisOutcome> AnalyzeAsync(
        IFormFileCollection files, IPdfAnalyzer analyzer, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PrefabScanWeb", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var projects = new List<ParsedProject>();
        var warnings = new List<string>();

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = SafeFileName(file.FileName);
                var path = Path.Combine(directory, $"{Guid.NewGuid():N}_{name}");

                try
                {
                    await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await file.CopyToAsync(target, cancellationToken);

                    // Extension and Content-Type can be forged. Reject files without
                    // the basic PDF header before starting PdfPig/PDFium/Tesseract.
                    var header = new byte[5];
                    await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (await source.ReadAsync(header, cancellationToken) != header.Length ||
                            !header.AsSpan().SequenceEqual("%PDF-"u8))
                        {
                            warnings.Add($"{name}: plik nie zawiera prawidłowego nagłówka PDF.");
                            continue;
                        }
                    }

                    // The analyzer currently calls synchronous CPU-intensive PDF
                    // extraction, so execute it on a worker thread.
                    projects.Add(await Task.Run(() => analyzer.AnalyzeAsync(path)));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    warnings.Add($"{name}: błąd odczytu ({ex.GetType().Name}).");
                }
            }
        }
        finally
        {
            // Files supplied by the user are never persisted beyond this request.
            if (Directory.Exists(directory))
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { /* best-effort cleanup; never mask parser errors */ }
                catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
            }
        }

        return new AnalysisOutcome(projects.Count > 0 ? ProjectMerger.Merge(projects) : null, warnings);
    }

    private static string SafeFileName(string untrusted)
    {
        // Normalize separators before taking a basename; the hosting OS may
        // differ from the OS used by the sender of the multipart request.
        var basename = Path.GetFileName(untrusted.Replace('\\', '/'));
        var cleaned = new string(basename
            .Where(c => c >= ' ' && !Path.GetInvalidFileNameChars().Contains(c))
            .ToArray());
        cleaned = cleaned.Trim(' ', '.');
        if (cleaned.Length > 120)
            cleaned = cleaned[..116] + ".pdf";
        return string.IsNullOrWhiteSpace(cleaned) ? "dokument.pdf" : cleaned;
    }
}

internal sealed record AnalysisOutcome(ParsedProject? Project, IReadOnlyList<string> Warnings);
