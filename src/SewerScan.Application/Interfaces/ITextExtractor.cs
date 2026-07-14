using System.Collections.Generic;
using System.Threading.Tasks;

namespace SewerScan.Application.Interfaces
{
    /// <summary>
    /// Extracts text and positions from a PDF file.
    /// </summary>
    public interface ITextExtractor
    {
        /// <summary>
        /// Extract pages with text and items from a PDF file.
        /// </summary>
        Task<IReadOnlyList<Models.PageText>> ExtractAsync(string filePath);
    }
}
