using System.Threading.Tasks;

namespace SewerScan.Application.Interfaces
{
    /// <summary>
    /// Service that analyzes PDF files and returns parsed project model.
    /// </summary>
    public interface IPdfAnalyzer
    {
        /// <summary>
        /// Analyze PDF at given path and return parsed project.
        /// </summary>
        Task<DTO.ParsedProject> AnalyzeAsync(string filePath);
    }
}
