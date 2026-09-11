using System.Linq;
using SewerScan.Application.DTO;

namespace SewerScan.Application.Services;

public static class AnalysisResultSummary
{
    public static string BuildCompact(ParsedProject project)
    {
        var complete = project.Manholes.Count(m => m.CompletenessPercent >= 70);
        return $"WYNIK ANALIZY: {project.DrawingType} | dokumenty {project.SourceDocuments.Count} | " +
               $"studnie {project.Manholes.Count} (≥70%: {complete}) | " +
               $"wpusty {project.Inlets.Count} | rury {project.Pipes.Count}";
    }
}
