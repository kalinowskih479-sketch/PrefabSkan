using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SewerScan.Application.DTO;
using SewerScan.Application.Interfaces;

namespace SewerScan.Application.Services
{
    /// <summary>
    /// Coordinates text extraction and parsing to produce parsed project model.
    /// </summary>
    public class PdfAnalyzer : IPdfAnalyzer
    {
        private readonly ITextExtractor _extractor;
        private readonly IProjectParser _parser;

        public PdfAnalyzer(ITextExtractor extractor, IProjectParser parser)
        {
            _extractor = extractor;
            _parser = parser;
        }

        public async Task<ParsedProject> AnalyzeAsync(string filePath)
        {
            var pages = await _extractor.ExtractAsync(filePath).ConfigureAwait(false);

            // PrefabScan 4.2.2: inject an explicit filename-derived hint BEFORE parsing.
            // In 3.6 the type was corrected only after ParseAsync, so spatial parsing had already
            // used the wrong geometry rules. The marker is internal and is ignored as engineering data.
            var fileClass = ClassifyDrawingFromFileName(filePath);
            if (!string.IsNullOrWhiteSpace(fileClass) && pages.Count > 0)
                pages[0].Text = $"[[PREFABSCAN_DRAWING:{fileClass}]] " + (pages[0].Text ?? string.Empty);

            var project = await _parser.ParseAsync(pages).ConfigureAwait(false);

            // Filename authority remains the final guard as well.
            // Technical drawing filenames are usually much more reliable than OCR for deciding
            // whether a sheet is PZT or a longitudinal profile. OCR can see the word "profil"
            // inside notes on a PZT and previously reclassified the whole set incorrectly.
            if (!string.IsNullOrWhiteSpace(fileClass))
                project.DrawingType = fileClass;

            project.Diagnostics = project.SourceFile ?? string.Empty;
            project.Diagnostics += Environment.NewLine + $"[4.1 DocumentClass] {Path.GetFileName(filePath)} => {project.DrawingType}";
            project.SourceFile = filePath;

            var sourceName = Path.GetFileName(filePath);
            if (!project.SourceDocuments.Contains(sourceName, StringComparer.OrdinalIgnoreCase))
                project.SourceDocuments.Add(sourceName);

            foreach (var manhole in project.Manholes)
            {
                manhole.SourceDocument = sourceName;
                if (!manhole.SourceDocuments.Contains(sourceName, StringComparer.OrdinalIgnoreCase))
                    manhole.SourceDocuments.Add(sourceName);
            }

            foreach (var inlet in project.Inlets)
            {
                inlet.SourceDocument = sourceName;
                if (!inlet.SourceDocuments.Contains(sourceName, StringComparer.OrdinalIgnoreCase))
                    inlet.SourceDocuments.Add(sourceName);
            }

            foreach (var pipe in project.Pipes)
                pipe.SourceDocument = sourceName;

            return project;
        }
        private static string? ClassifyDrawingFromFileName(string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
            var normalized = name.ToUpperInvariant();

            if (normalized.Contains("PZT") ||
                normalized.Contains("ZAGOSPODAROWANIA TERENU") ||
                normalized.Contains("PLAN SYTUACYJ"))
                return "PZT";

            if (normalized.Contains("PROFIL") ||
                normalized.Contains("PROFILE") ||
                normalized.Contains("PODŁUŻ") ||
                normalized.Contains("PODLUZ"))
                return "PROFIL";

            return null;
        }

    }
}
