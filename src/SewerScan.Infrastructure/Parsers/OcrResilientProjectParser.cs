using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SewerScan.Application.DTO;
using SewerScan.Application.Interfaces;
using SewerScan.Application.Models;

namespace SewerScan.Infrastructure.Parsers;

public sealed class OcrResilientProjectParser : IProjectParser
{
    private readonly SewerProjectParser _inner = new();
    private static readonly Regex BlockRegex = new(
        @"\b(?<id>(?:D|S)\s*\d{1,2})\b(?<body>.{0,160}?)(?=\b(?:D|S)\s*\d{1,2}\b|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex DnRegex = new(@"\bDN\s*(?<dn>800|1000|1200|1500|1800|2000|2500|3000)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeightRegex = new(@"\bH\s*[:=]?\s*(?<h>\d{1,2}[,.]\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ConcreteRegex = new(@"\bbeton\p{L}*\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CrownRegex = new(@"\bw[lł]az\s+(?:zeliwny|żeliwny)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<ParsedProject> ParseAsync(IReadOnlyList<PageText> pages)
    {
        var result = await _inner.ParseAsync(pages);
        foreach (var page in pages)
            EnrichFromOcrText(result, page);
        return result;
    }

    private static void EnrichFromOcrText(ParsedProject result, PageText page)
    {
        var text = string.Join(" ", new[] { page.Text, page.RawText, page.OrderedText }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(text)) return;

        // Only accept ambiguous OCR substitutions when a strong technical row follows.
        text = Regex.Replace(text, @"\bD/\s+(?=DN\s*1200\b)", "D7 ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b57\s+(?=DN\s*1200\b)", "S7 ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?<=H\s*[=:]?\s*\d{1,2}[,.]\d?)/(?=\d?\b)", "7", RegexOptions.IgnoreCase);

        foreach (Match block in BlockRegex.Matches(text))
        {
            var id = Regex.Replace(block.Groups["id"].Value, @"\s+", string.Empty).ToUpperInvariant();
            var body = block.Groups["body"].Value;
            var manhole = result.Manholes.FirstOrDefault(m => string.Equals(m.Identifier, id, StringComparison.OrdinalIgnoreCase));
            if (manhole == null)
            {
                manhole = new ParsedManhole { Identifier = id, Page = page.PageNumber, RawText = block.Value };
                result.Manholes.Add(manhole);
            }

            var dn = DnRegex.Match(body);
            if (!manhole.DiameterMm.HasValue && dn.Success && int.TryParse(dn.Groups["dn"].Value, out var diameter))
                manhole.DiameterMm = diameter;

            var height = HeightRegex.Match(body);
            if (!manhole.HeightM.HasValue && height.Success && double.TryParse(height.Groups["h"].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h))
                manhole.HeightM = h;

            if (string.IsNullOrWhiteSpace(manhole.Type) && ConcreteRegex.IsMatch(body))
                manhole.Type = "betonowa";

            var crown = CrownRegex.Match(body);
            if (string.IsNullOrWhiteSpace(manhole.Crown) && crown.Success)
                manhole.Crown = "właz żeliwny";
        }
    }
}
