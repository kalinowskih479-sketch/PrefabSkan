using System.Collections.Generic;
using System.Threading.Tasks;

namespace SewerScan.Application.Interfaces
{
    /// <summary>
    /// Parses extracted page texts into domain-specific parsed project model.
    /// </summary>
    public interface IProjectParser
    {
        Task<DTO.ParsedProject> ParseAsync(IReadOnlyList<Models.PageText> pages);
    }
}
