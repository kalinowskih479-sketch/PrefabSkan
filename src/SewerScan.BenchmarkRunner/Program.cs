using System.Text;
using System.Text.Json;
using SewerScan.Application.DTO;
using SewerScan.Application.Services;
using SewerScan.Infrastructure.Parsers;
using SewerScan.Infrastructure.Pdf;

var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
var stormOnly = args.Any(a => a.Equals("--storm-only", StringComparison.OrdinalIgnoreCase));
var diagnosticOnly = args.Any(a => a.Equals("--diagnostic-only", StringComparison.OrdinalIgnoreCase));
var referenceDir = positional.Length > 0 ? Path.GetFullPath(positional[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Reference", "Batorego"));
var outputDir = positional.Length > 1 ? Path.GetFullPath(positional[1]) : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "batorego"));
Directory.CreateDirectory(outputDir);

var required = stormOnly
    ? new[] { "Rys I_8 profil kanalizacja deszczowa.pdf" }
    : new[] { "Rys I_1 PZT.pdf", "Rys I_4 profil kanalizacja sanitarna.pdf", "Rys I_8 profil kanalizacja deszczowa.pdf" };

var files = required.Select(name => Path.Combine(referenceDir, name)).ToArray();
var missing = files.Where(path => !File.Exists(path)).ToArray();
if (missing.Length > 0)
{
    Console.Error.WriteLine("Missing Batorego reference files:");
    foreach (var file in missing) Console.Error.WriteLine(" - " + file);
    return 3;
}

try
{
    var analyzer = new PdfAnalyzer(new PdfTextExtractor(), new SewerProjectParser());
    var projects = new List<ParsedProject>();
    foreach (var file in files)
    {
        Console.WriteLine($"[Batorego] Analysing {Path.GetFileName(file)}");
        projects.Add(await analyzer.AnalyzeAsync(file));
    }

    var merged = ProjectMerger.Merge(projects);
    var score = BatoregoBenchmark.GetScore(merged);
    var compact = BatoregoBenchmark.BuildCompactSummary(merged);
    var detail = BatoregoBenchmark.BuildCompactDetail(merged);
    var report = BuildDiagnostics(merged, compact, detail, stormOnly);
    var scoreObject = new { timestampUtc = DateTime.UtcNow, mode = stormOnly ? "storm-only" : "full", found = score.Found, expected = 15, dn = score.Dn, height = score.Height, type = score.Type, crown = score.Crown, extras = score.Extras, compact, detail };

    await File.WriteAllTextAsync(Path.Combine(outputDir, "Batorego_LATEST.txt"), report, Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(outputDir, "Batorego_SCORE.json"), JsonSerializer.Serialize(scoreObject, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(outputDir, "Batorego_SUMMARY.md"), $"## PrefabScan — Batorego E2E\n\nMode: **{(stormOnly ? "storm-only" : "full")}**\n\n**{compact}**\n\n{detail}\n", Encoding.UTF8);

    Console.WriteLine(compact);
    Console.WriteLine(detail);
    Console.WriteLine($"Diagnostics: {Path.Combine(outputDir, "Batorego_LATEST.txt")}");

    if (diagnosticOnly) return 0;
    var regression = !stormOnly && (score.Found < 12 || score.Extras > 8);
    if (regression)
    {
        Console.Error.WriteLine($"REGRESSION: baseline requires Found >= 12 and Extras <= 8; got Found={score.Found}, Extras={score.Extras}.");
        return 2;
    }
    return 0;
}
catch (Exception ex)
{
    await File.WriteAllTextAsync(Path.Combine(outputDir, "Batorego_FATAL.txt"), ex.ToString(), Encoding.UTF8);
    Console.Error.WriteLine(ex);
    return 1;
}

static string BuildDiagnostics(ParsedProject project, string compact, string detail, bool stormOnly)
{
    var sb = new StringBuilder();
    sb.AppendLine("PREFABSCAN HEADLESS E2E — TORUŃ, BATOREGO");
    sb.AppendLine($"Timestamp UTC: {DateTime.UtcNow:O}");
    sb.AppendLine($"Mode: {(stormOnly ? "storm-only" : "full")}");
    sb.AppendLine($"Typ zestawu: {project.DrawingType}");
    sb.AppendLine($"Dokumenty: {string.Join(" | ", project.SourceDocuments)}");
    sb.AppendLine($"Studnie: {project.Manholes.Count} | Wpusty: {project.Inlets.Count} | Rury: {project.Pipes.Count}");
    sb.AppendLine(); sb.AppendLine(compact); sb.AppendLine(detail); sb.AppendLine();
    sb.AppendLine(BatoregoBenchmark.BuildReport(project));
    sb.AppendLine(); sb.AppendLine("SUROWA DIAGNOSTYKA PARSERÓW:"); sb.AppendLine(project.Diagnostics);
    return sb.ToString();
}
