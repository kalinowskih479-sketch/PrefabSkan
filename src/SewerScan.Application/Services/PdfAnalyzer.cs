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
            var project = await _parser.ParseAsync(pages).ConfigureAwait(false);
            project.SourceFile = filePath;
            return project;
        }
    }
}
