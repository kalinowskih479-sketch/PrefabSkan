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
    /// Parser that recognises typical sewer project tokens and extracts identifiers, DN and material.
    /// </summary>
    public class SewerProjectParser : IProjectParser
    {
        private static readonly Regex ManholeRegex = new(@"\b(?<token>KD|KS)\b[:\s-]*(?<id>[A-Za-z0-9\-/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex InletRegex = new(@"\b(?<token)Wp\b[:\s-]*(?<id>[A-Za-z0-9\-/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DnRegex = new(@"\b(?<token>DN|D)\b[:=\s]*(?<value>[0-9]{1,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MaterialRegex = new(@"\b(?<mat>PVC|PP|PE|Concrete)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Task<ParsedProject> ParseAsync(IReadOnlyList<PageText> pages)
        {
            if (pages == null) throw new ArgumentNullException(nameof(pages));

            var result = new ParsedProject();

            foreach (var page in pages)
            {
                result.SourceFile ??= string.Empty;

                var lines = (page.Text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    // manhole
                    var m = ManholeRegex.Match(line);
                    if (m.Success)
                    {
                        var id = m.Groups["id"].Value;
                        result.Manholes.Add(new ParsedManhole { Page = page.PageNumber, RawText = line, Identifier = id });
                        // continue to also detect DN/material in same line
                    }

                    // inlet
                    var iw = InletRegex.Match(line);
                    if (iw.Success)
                    {
                        var id = iw.Groups["id"].Value;
                        result.Inlets.Add(new ParsedInlet { Page = page.PageNumber, RawText = line, Identifier = id });
                    }

                    // DN
                    var dn = DnRegex.Match(line);
                    // material
                    var mat = MaterialRegex.Match(line);

                    if (dn.Success || mat.Success)
                    {
                        var parsed = new ParsedPipe { Page = page.PageNumber, RawText = line };
                        if (dn.Success && int.TryParse(dn.Groups["value"].Value, out var value)) parsed.DiameterMm = value;
                        if (mat.Success) parsed.Material = mat.Groups["mat"].Value;
                        result.Pipes.Add(parsed);
                    }
                }
            }

            return Task.FromResult(result);
        }
    }
}
