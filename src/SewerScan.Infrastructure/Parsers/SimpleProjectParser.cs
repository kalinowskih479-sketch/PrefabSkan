using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SewerScan.Application.DTO;
using SewerScan.Application.Interfaces;
using SewerScan.Application.Models;

namespace SewerScan.Infrastructure.Parsers
{
    /// <summary>
    /// A simple PDF-to-project parser that looks for tokens and simple patterns.
    /// </summary>
    public class SimpleProjectParser : IProjectParser
    {
        private static readonly string[] Materials = new[] { "PVC", "PP", "PE", "Concrete" };
        private static readonly string[] Tokens = new[] { "KD", "KS", "D", "Wp", "DN" };

        public Task<ParsedProject> ParseAsync(IReadOnlyList<PageText> pages)
        {
            var result = new ParsedProject();

            foreach (var page in pages)
            {
                // split lines and attempt to find tokens
                var lines = page.Text?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                foreach (var line in lines)
                {
                    var l = line.Trim();
                    if (string.IsNullOrEmpty(l)) continue;

                    // Check for manhole token KD or KS
                    if (Regex.IsMatch(l, "\\b(KD|KS)\\b", RegexOptions.IgnoreCase))
                    {
                        result.Manholes.Add(new ParsedManhole { Page = page.PageNumber, RawText = l });
                        continue;
                    }

                    // Check for inlet token Wp
                    if (Regex.IsMatch(l, "\\b(Wp)\\b", RegexOptions.IgnoreCase))
                    {
                        result.Inlets.Add(new ParsedInlet { Page = page.PageNumber, RawText = l });
                        continue;
                    }

                    // Check for pipe markers (D, DN and material)
                    if (Tokens.Any(t => Regex.IsMatch(l, "\\b" + Regex.Escape(t) + "\\b", RegexOptions.IgnoreCase)) || Materials.Any(m => l.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Pipes.Add(new ParsedPipe { Page = page.PageNumber, RawText = l });
                        continue;
                    }
                }
            }

            return Task.FromResult(result);
        }
    }
}
