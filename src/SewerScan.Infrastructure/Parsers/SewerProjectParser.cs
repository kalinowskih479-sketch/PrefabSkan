using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private static readonly Regex ManholeRegex = new(
            @"\b(?:(?<token>KD|KS)\s*[-.:]?\s*(?<number>\d{1,3}(?:[./-]\d+)*)|(?<token>D|S)\s*[-.:]?\s*(?<number>\d{1,3}(?:[./-]\d+)*)|(?<special>SO))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InletRegex = new(
            @"\bWP\s*(?<id>\d{1,3}(?:[./-]\d+)*)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DnRegex = new(
            @"\b(?<token>DN|D)\b[:=\s]*(?<value>[0-9]{1,4})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MaterialRegex = new(
            @"\b(?<mat>PE-HD|PEHD|HDPE|PVC|PP|PE|Concrete|Beton|Żelbet)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Studnia / studzienka z opcjonalną średnicą
        private static readonly Regex StudniaRegex = new(
            @"\b(studnia|studzienka|studni[aą])\b(?:[^\d\r\n]{0,30}[Øø]?\s*(?<diam>\d{2,4}))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SeparatorRegex = new(
            @"\b(separator|osadnik|osadniki?)\b(?:[^\d\r\n]{0,20}[Øø]?\s*(?<diam>\d{2,4}))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PipeMaterialDiameterRegex = new(
            @"\b(?<mat>PE-HD|PEHD|HDPE|PVC|PP|PE)\b[^\d\r\n]{0,6}[Øø]?\s*(?<diam>\d{2,4})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Lokalne zwarte zapisy np. PVC200, PE110, P1PVC200
        private static readonly Regex LocalPipeCompactRegex = new(
            @"(?<prefix>P\d+)?(?<mat>PE-HD|PEHD|HDPE|PVC|PP|PE)\s*[Øø]?\s*(?<diam>\d{2,4})(?:\b|(?=i=|%))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Przykład: PE szereg SDR 11 PN16
        private static readonly Regex PeSeriesRegex = new(
            @"(?<mat>PE)\s+szereg\s+SDR\s*(?<sdr>\d{1,3})(?:\s+PN\s*(?<pn>\d{1,3}))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Task<ParsedProject> ParseAsync(IReadOnlyList<PageText> pages)
        {
            if (pages == null)
                throw new ArgumentNullException(nameof(pages));

            var result = new ParsedProject
            {
                DrawingType = DetectDrawingType(pages)
            };

            foreach (var page in pages)
            {
                result.SourceFile ??= string.Empty;

                // PrefabScan parses the primary extraction plus both alternative text streams.
                // This prevents a change in PdfPig reading order from reducing a drawing to zero objects.
                var raw = string.Join("\n", new[] { page.Text, page.RawText, page.OrderedText }
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.Ordinal));

                var rawLines = raw.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);

                var lines = rawLines
                    .Select(NormalizeExtractedText)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();

                var debug = new StringBuilder();

                // Prefer spatial parsing whenever PdfPig supplied word coordinates.
                // This prevents one manhole from absorbing pipe labels belonging to
                // other manholes elsewhere on the same drawing.
                var spatialManholesParsed = ParseSpatialManholes(page, result, debug);
                var spatialInletsParsed = ParseSpatialInlets(page, result, debug);
                var isOcrPage = (page.ExtractionEngine ?? string.Empty).StartsWith("OCR/", StringComparison.OrdinalIgnoreCase);
                var hasUsableSpatialWords = page.Items != null && page.Items.Any(i =>
                    !string.IsNullOrWhiteSpace(i.Text) &&
                    (Math.Abs(i.X) > 0.001 || Math.Abs(i.Y) > 0.001 || i.Width > 0.001 || i.Height > 0.001));

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();

                    if (string.IsNullOrEmpty(line))
                        continue;

                    ParsePipesByDiameter(line, page.PageNumber, result, debug);
                    ParseMaterialDiameterPipes(line, page.PageNumber, result, debug);
                    ParseLocalPipes(line, page.PageNumber, result, debug);
                    ParseMaterialOnlyPipes(line, page.PageNumber, result, debug);

                    // Legacy text-only mode remains as a fallback for PDFs without
                    // usable coordinates or for OCR-only pages.
                    if (!spatialManholesParsed && !(isOcrPage && hasUsableSpatialWords))
                    {
                        ParseManholes(line, page.PageNumber, result, debug);
                        ParseDescriptiveManholes(
                            line,
                            page.PageNumber,
                            result,
                            debug);
                    }
                    else if (!spatialManholesParsed && isOcrPage)
                    {
                        debug.AppendLine("OCR legacy text fallback suppressed: coordinate words exist but no credible manhole anchor was found.");
                    }

                    if (!spatialInletsParsed)
                        ParseInlets(line, page.PageNumber, result, debug);

                    ParseSeparators(
                        line,
                        page.PageNumber,
                        result,
                        debug);
                }

                result.SourceFile +=
                    "\n[ExtractionDiagnostic Page " + page.PageNumber + "]\n" +
                    (page.ExtractionDiagnostics ?? string.Empty) +
                    "\n[ParserDebug Page " + page.PageNumber + "]\n" +
                    debug;
            }
            // OCR cleanup: normalize identifiers such as S08 -> S8 and remove very high,
            // unsupported labels that are typical OCR artefacts (e.g. S95).
            foreach (var mh in result.Manholes)
            {
                mh.Identifier = NormalizeParsedIdentifier(mh.Identifier);
                if (mh.GroundElevationM.HasValue && mh.InvertElevationM.HasValue && !mh.HeightM.HasValue)
                    mh.HeightM = Math.Round(mh.GroundElevationM.Value - mh.InvertElevationM.Value, 2);
                mh.Confidence = DetermineConfidence(mh);
            }
            // Vision Pipeline 3.0: never delete an OCR candidate globally at this stage.
            // A weak candidate is retained for cross-drawing corroboration by ProjectMerger;
            // questionable labels are marked in ValidationIssues instead of silently disappearing.
            foreach (var mh in result.Manholes)
            {
                if (IsUnsupportedOcrIdentifier(mh, pages))
                {
                    mh.ValidationIssues = string.IsNullOrWhiteSpace(mh.ValidationIssues)
                        ? "kandydat OCR do weryfikacji"
                        : mh.ValidationIssues + "; kandydat OCR do weryfikacji";
                    mh.Confidence = "niska";
                }
            }

            // Final merge pass: combine duplicate manholes on the same page by identifier or raw text
            var merged = new List<ParsedManhole>();
            foreach (var mh in result.Manholes)
            {
                var match = merged.FirstOrDefault(m =>
                    m.Page == mh.Page && (
                        (!string.IsNullOrWhiteSpace(m.Identifier) && !string.IsNullOrWhiteSpace(mh.Identifier) && string.Equals(m.Identifier, mh.Identifier, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(m.RawText) && !string.IsNullOrWhiteSpace(mh.RawText) && string.Equals(m.RawText, mh.RawText, StringComparison.OrdinalIgnoreCase))
                    ));

                if (match == null)
                {
                    merged.Add(mh);
                }
                else
                {
                    // merge structured fields: prefer existing values, fill missing from mh
                    if (string.IsNullOrWhiteSpace(match.Type) && !string.IsNullOrWhiteSpace(mh.Type))
                        match.Type = mh.Type;

                    if (!match.DiameterMm.HasValue && mh.DiameterMm.HasValue && mh.DiameterMm.Value > 0)
                        match.DiameterMm = mh.DiameterMm;

                    if (!match.HeightM.HasValue && mh.HeightM.HasValue && mh.HeightM.Value > 0)
                        match.HeightM = mh.HeightM;

                    if (!match.GroundElevationM.HasValue && mh.GroundElevationM.HasValue)
                        match.GroundElevationM = mh.GroundElevationM;

                    if (!match.InvertElevationM.HasValue && mh.InvertElevationM.HasValue)
                        match.InvertElevationM = mh.InvertElevationM;

                    if (string.IsNullOrWhiteSpace(match.Crown) && !string.IsNullOrWhiteSpace(mh.Crown))
                        match.Crown = mh.Crown;

                    if (ConfidenceRank(mh.Confidence) > ConfidenceRank(match.Confidence))
                        match.Confidence = mh.Confidence;

                    // merge transitions sums
                    foreach (var t in mh.Transitions)
                    {
                        var ex = match.Transitions.FirstOrDefault(tt => string.Equals(tt.Material, t.Material, StringComparison.OrdinalIgnoreCase) && tt.DiameterMm == t.DiameterMm);
                        if (ex != null)
                            ex.Quantity += t.Quantity;
                        else
                            match.Transitions.Add(new ManholeTransition { Material = t.Material, DiameterMm = t.DiameterMm, Quantity = t.Quantity });
                    }

                    // append rawtext if different
                    if (!string.Equals(match.RawText, mh.RawText, StringComparison.OrdinalIgnoreCase))
                        match.RawText = string.IsNullOrWhiteSpace(match.RawText) ? mh.RawText : (match.RawText + " | " + mh.RawText);
                }
            }

            result.Manholes.Clear();
            result.Manholes.AddRange(merged);

            return Task.FromResult(result);
        }

        private sealed class SpatialManholeAnchor
        {
            public string Identifier { get; init; } = string.Empty;
            public double X { get; init; }
            public double Y { get; init; }
            public int SourceIndex { get; init; } = -1;
        }

        private sealed class ElevationCandidate
        {
            public double Value { get; init; }
            public double Distance { get; init; }
            public double Dx { get; init; }
            public double Dy { get; init; }
        }

        private sealed class ProfileNumericPoint
        {
            public double Value { get; init; }
            public double X { get; init; }
            public double Y { get; init; }
            public double Height { get; init; }
        }

        private sealed class ProfileNumericBand
        {
            public double Y { get; set; }
            public List<ProfileNumericPoint> Points { get; } = new();
        }

        private sealed class ProfileColumnAssignment
        {
            public string Identifier { get; init; } = string.Empty;
            public double X { get; init; }
            public double? Ground { get; set; }
            public double? Invert { get; set; }
            public double? Depth { get; set; }
            public int? Diameter { get; set; }
            public int Evidence { get; set; }
        }

        private static readonly Regex ExactSpatialManholeRegex = new(
            @"^(?:(?<token>KD|KS)(?<number>\d+(?:[./-]\d+)*)|(?<token>D|S)(?<number>\d{1,3}(?:[./-]\d+)*)|(?<special>SO))$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ExactSpatialInletRegex = new(
            @"^WP(?<number>\d{1,3}(?:[./-]\d+)*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SpatialManholeTypeRegex = new(
            @"\b(kinetow(?:a|e|y)?|osadnik(?:ow(?:a|e|y)?)?|rozpr[eę]żn(?:a|e|y)?|czyszczak(?:ow(?:a|e|y)?)?|tłocz(?:ny|na|ne)?)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SpatialHeightRegex = new(
            @"\b(?:wys(?:okość)?\.?(?:\s*całk(?:owita)?\.?)?|H)\s*[:=]?\s*(?<h>\d{1,2}(?:[,.]\d{1,3})?)\s*m\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SpatialCrownRegex = new(
            @"\b(właz\s+[^,;]{1,45}|pokrywa\s+[^,;]{1,45}|pokrycie\s+[^,;]{1,45}|zwieńczenie\s+[^,;]{1,55}|klasa\s+[A-Za-z0-9]+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SpatialTransitionRegex = new(
            @"(?<![A-Za-z0-9])(?<mat>PE-HD|PEHD|HDPE|PVC|PP|PE|Żelbet|Beton)\s*(?:DN|D|Ø|ø)?\s*(?<diam>\d{2,4})(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string DetectDrawingType(IReadOnlyList<PageText> pages)
        {
            var text = string.Join(" ", pages.Select(p => p.Text ?? string.Empty));

            // 4.1: explicit per-file hint supplied by PdfAnalyzer has absolute priority.
            var hint = Regex.Match(text, @"\[\[PREFABSCAN_DRAWING:(?<type>PZT|PROFIL)\]\]", RegexOptions.IgnoreCase);
            if (hint.Success)
                return hint.Groups["type"].Value.ToUpperInvariant();

            if (Regex.IsMatch(text, @"RZĘDNE\s+DNA\s+PRZEWODU|ZAGŁ[EĘ]BIENIE\s+DNA|PROFILE?\s+PODŁU|\bPROFIL\b.{0,80}KANALIZACJI|KANALIZACJI.{0,80}\bPROFIL\b", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                return "PROFIL";

            if (Regex.IsMatch(text, @"PLAN\s+SYTUACYJNO|PROJEKT\s+ZAGOSPODAROWANIA\s+TERENU|\bPZT\b|\bPYT\b", RegexOptions.IgnoreCase))
                return "PZT";

            return "NIEZNANY";
        }

        private static bool ParseSpatialManholes(PageText page, ParsedProject result, StringBuilder debug)
        {
            if (page.Items == null || page.Items.Count == 0)
                return false;

            var usable = page.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Text))
                .Where(i => Math.Abs(i.X) > 0.001 || Math.Abs(i.Y) > 0.001 || i.Width > 0.001 || i.Height > 0.001)
                .ToList();
            var hasDuplicatedCadGlyphs = usable.Any(i =>
                !string.Equals(NormalizeCadDuplicatedGlyphs(i.Text), (i.Text ?? string.Empty).Trim(), StringComparison.Ordinal));

            // AutoCAD/plotter PDFs frequently expose duplicated glyphs (e.g. DD + 66//11)
            // and split elevations (1 + 2 + 6,47).  Add synthetic numeric words without
            // discarding the original geometry so downstream matching can use either form.
            usable.AddRange(BuildSyntheticCadElevationItems(usable));

            if (usable.Count == 0)
            {
                debug.AppendLine("Spatial mode unavailable: no usable word coordinates.");
                return false;
            }

            var anchors = FindSpatialManholeAnchors(usable, result.DrawingType);

            if (string.Equals(result.DrawingType, "PROFIL", StringComparison.OrdinalIgnoreCase))
                anchors = SelectProfileTableAnchors(anchors, usable, page.Text ?? string.Empty, debug);
            else
                anchors = anchors
                    .GroupBy(a => a.Identifier, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g
                        .OrderByDescending(a => VisionEvidenceScore(a, usable, result.DrawingType))
                        .First())
                    .ToList();

            var preliminaryIsOcr = (page.ExtractionEngine ?? string.Empty).StartsWith("OCR/", StringComparison.OrdinalIgnoreCase);
            if (preliminaryIsOcr && string.Equals(result.DrawingType, "PZT", StringComparison.OrdinalIgnoreCase))
            {
                var before = anchors.Count;
                anchors = anchors
                    .Where(a => IsCrediblePztOcrAnchor(a, usable))
                    .ToList();
                debug.AppendLine($"4.1 PZT OCR anchor filter: {before} -> {anchors.Count}.");
            }

            if (anchors.Count == 0)
            {
                debug.AppendLine("Spatial mode: no reliable manhole anchors found.");
                return false;
            }

            var minX = usable.Min(i => i.X);
            var maxX = usable.Max(i => i.X + Math.Max(0, i.Width));
            var minY = usable.Min(i => i.Y - Math.Max(0, i.Height));
            var maxY = usable.Max(i => i.Y);
            var pageWidth = Math.Max(1, maxX - minX);
            var pageHeight = Math.Max(1, maxY - minY);

            var isOcr = (page.ExtractionEngine ?? string.Empty).StartsWith("OCR/", StringComparison.OrdinalIgnoreCase);
            var pztLike = string.Equals(result.DrawingType, "PZT", StringComparison.OrdinalIgnoreCase) ||
                          (isOcr && !string.Equals(result.DrawingType, "PROFIL", StringComparison.OrdinalIgnoreCase));

            // Vision Pipeline 3.0: OCR candidates are scored, not deleted.
            // A weak D/S token may still be confirmed by another drawing (PZT/profile/detail).
            if (isOcr)
            {
                foreach (var candidate in anchors)
                    debug.AppendLine($"Vision candidate {candidate.Identifier}: evidence={VisionEvidenceScore(candidate, usable, result.DrawingType)}");
            }

            var directTextElevations = FindDirectTextElevationAssignments(page.Text);
            var orderedPztElevations = pztLike
                ? BuildOrderedPztElevationAssignments(anchors, usable)
                : new Dictionary<string, (double Ground, double Invert)>(StringComparer.OrdinalIgnoreCase);
            var spatialPztElevations = pztLike
                ? BuildPztElevationAssignments(anchors, usable)
                : new Dictionary<string, (double Ground, double Invert)>(StringComparer.OrdinalIgnoreCase);

            // 4.1 profile parser: identify repeated horizontal numeric bands in the profile table
            // and then read ground/invert/DN from the same X-column as each structure.
            var profileColumnElevations = string.Equals(result.DrawingType, "PROFIL", StringComparison.OrdinalIgnoreCase)
                ? BuildProfileColumnElevationAssignments(anchors, usable, debug)
                : new Dictionary<string, (double Ground, double Invert)>(StringComparer.OrdinalIgnoreCase);
            var profileColumnDiameters = string.Equals(result.DrawingType, "PROFIL", StringComparison.OrdinalIgnoreCase)
                ? BuildProfileColumnDiameterAssignments(anchors, usable, debug)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 4.1 geometry-first reconciliation: build a second, stricter whole-profile model
            // and let it override the older heuristic only where the row/column evidence is strong.
            var profileGeometry = string.Equals(result.DrawingType, "PROFIL", StringComparison.OrdinalIgnoreCase)
                ? BuildProfileGeometryAssignments(anchors, usable, pageWidth, pageHeight, debug)
                : new Dictionary<string, ProfileColumnAssignment>(StringComparer.OrdinalIgnoreCase);

            foreach (var anchor in anchors)
            {
                var localItems = GetLocalItems(anchor, anchors, usable, result.DrawingType, pageWidth, pageHeight);
                var context = NormalizeExtractedText(string.Join(" ", localItems.Select(i => i.Text)));
                if (string.IsNullOrWhiteSpace(context))
                    context = anchor.Identifier;

                (double Ground, double Invert)? assignedElevations = null;
                if (profileGeometry.TryGetValue(anchor.Identifier, out var geometryAssignment) &&
                    geometryAssignment.Ground.HasValue && geometryAssignment.Invert.HasValue &&
                    geometryAssignment.Evidence >= 3)
                    assignedElevations = (geometryAssignment.Ground.Value, geometryAssignment.Invert.Value);
                else if (profileColumnElevations.TryGetValue(anchor.Identifier, out var profilePair))
                    assignedElevations = profilePair;
                else if (directTextElevations.TryGetValue(anchor.Identifier, out var directPair))
                    assignedElevations = directPair;
                else if (pztLike && hasDuplicatedCadGlyphs && spatialPztElevations.TryGetValue(anchor.Identifier, out var cadSpatialPair))
                    assignedElevations = cadSpatialPair;
                else if (orderedPztElevations.TryGetValue(anchor.Identifier, out var orderedPair))
                    assignedElevations = orderedPair;
                else if (spatialPztElevations.TryGetValue(anchor.Identifier, out var spatialPair))
                    assignedElevations = spatialPair;

                int? assignedDiameter =
                    profileGeometry.TryGetValue(anchor.Identifier, out var geometryDn) &&
                    geometryDn.Diameter.HasValue && geometryDn.Evidence >= 2
                        ? geometryDn.Diameter
                        : profileColumnDiameters.TryGetValue(anchor.Identifier, out var profileDn)
                            ? profileDn
                            : null;

                double? assignedDepth =
                    profileGeometry.TryGetValue(anchor.Identifier, out var geometryDepth) &&
                    geometryDepth.Depth.HasValue && geometryDepth.Evidence >= 4
                        ? geometryDepth.Depth
                        : null;

                var parsed = BuildSpatialManhole(anchor, page.PageNumber, context, localItems, result.DrawingType, assignedElevations, assignedDiameter, assignedDepth);
                var visionScore = VisionEvidenceScore(anchor, usable, result.DrawingType);
                if (isOcr && visionScore <= 1 && !parsed.GroundElevationM.HasValue && !parsed.DiameterMm.HasValue && parsed.Transitions.Count == 0)
                {
                    parsed.ValidationIssues = "słaby kandydat OCR — wymaga potwierdzenia na drugim rysunku";
                    parsed.Confidence = "niska";
                }
                result.Manholes.Add(parsed);

                debug.AppendLine(
                    $"Spatial manhole {anchor.Identifier}: visionScore={visionScore}, localWords={localItems.Count}, DN={parsed.DiameterMm?.ToString() ?? "?"}, H={parsed.HeightM?.ToString("0.00") ?? "?"}, transitions={parsed.Transitions.Count}, confidence={parsed.Confidence}");
                debug.AppendLine($"  context: {context}");
            }

            AddMissingTextOnlyManholes(page, result, anchors, debug);

            debug.AppendLine($"Spatial mode active: {anchors.Count} reliable manhole anchor(s), drawing={result.DrawingType}.");
            return true;
        }

        private static List<SpatialManholeAnchor> SelectProfileTableAnchors(
            IReadOnlyList<SpatialManholeAnchor> candidates,
            IReadOnlyList<TextItem> items,
            string pageText,
            StringBuilder debug)
        {
            if (candidates.Count == 0)
                return new List<SpatialManholeAnchor>();

            // The "Węzeł" row of a longitudinal profile contains many D/S identifiers on one
            // horizontal baseline. Upper schematic callouts are sparse and irregular. Use the
            // densest identifier Y-band as the primary set of table columns.
            var heights = items.Where(i => i.Height > 0).Select(i => i.Height).OrderBy(h => h).ToList();
            var typicalHeight = heights.Count > 0 ? heights[heights.Count / 2] : 10.0;
            var yTolerance = Math.Max(12.0, Math.Min(55.0, typicalHeight * 3.5));

            var bands = new List<List<SpatialManholeAnchor>>();
            foreach (var anchor in candidates.OrderBy(a => a.Y))
            {
                var band = bands
                    .Where(b => Math.Abs(b.Average(a => a.Y) - anchor.Y) <= yTolerance)
                    .OrderBy(b => Math.Abs(b.Average(a => a.Y) - anchor.Y))
                    .FirstOrDefault();

                if (band == null)
                {
                    band = new List<SpatialManholeAnchor>();
                    bands.Add(band);
                }
                band.Add(anchor);
            }

            // 4.2.4: use the network family as engineering evidence too. On Polish sewer
            // drawings the deszczowa profile is normally D/KD and sanitarna is S/KS.
            // OCR may still hallucinate a numerically stronger band from the opposite network.
            var preferredFamily = Regex.IsMatch(pageText ?? string.Empty, @"KANALIZACJI\s+DESZCZ|DESZCZOW", RegexOptions.IgnoreCase)
                ? "D"
                : Regex.IsMatch(pageText ?? string.Empty, @"KANALIZACJI\s+SANITAR|SANITARN", RegexOptions.IgnoreCase)
                    ? "S"
                    : string.Empty;

            // 4.2.3: the densest OCR band is not necessarily the profile node row.
            // Batorego produced a denser garbage band (S79, S13, S06/09, S61.16...)
            // than the actual structure row. Score each band by engineering-table support:
            // repeated elevation values and standard manhole DN values in the same X columns.
            var best = bands
                .Select(b =>
                {
                    var distinct = b.Select(a => a.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    var width = b.Count > 1 ? b.Max(a => a.X) - b.Min(a => a.X) : 0.0;
                    var engineeringColumns = 0;
                    var dnColumns = 0;
                    var syntaxPenalty = 0;

                    foreach (var a in b.GroupBy(x => x.Identifier, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                    {
                        var verticalNumbers = items.Count(i =>
                        {
                            if (!TryParseElevation(i.Text ?? string.Empty, out _)) return false;
                            var cx = i.X + i.Width / 2.0;
                            var cy = i.Y - i.Height / 2.0;
                            return Math.Abs(cx - a.X) <= 70 && Math.Abs(cy - a.Y) >= 35;
                        });
                        if (verticalNumbers >= 2) engineeringColumns++;

                        var hasDn = items.Any(i =>
                        {
                            var token = NormalizeExtractedText(i.Text ?? string.Empty).Replace(" ", string.Empty);
                            if (!Regex.IsMatch(token, @"^(?:DN|D|Ø|ø)?(?:800|1000|1200|1500|1800|2000|2500|3000)$", RegexOptions.IgnoreCase))
                                return false;
                            var cx = i.X + i.Width / 2.0;
                            return Math.Abs(cx - a.X) <= 75;
                        });
                        if (hasDn) dnColumns++;

                        if (Regex.IsMatch(a.Identifier, @"[./-]", RegexOptions.IgnoreCase)) syntaxPenalty += 4;
                        var n = Regex.Match(a.Identifier, @"^(?:D|S)(?<n>\d+)$", RegexOptions.IgnoreCase);
                        if (n.Success && int.TryParse(n.Groups["n"].Value, out var number) && number >= 30) syntaxPenalty += 5;
                    }

                    var uniqueAnchors = b.GroupBy(x => x.Identifier, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                    var familyMatches = string.IsNullOrWhiteSpace(preferredFamily)
                        ? 0
                        : uniqueAnchors.Count(a => preferredFamily == "D"
                            ? Regex.IsMatch(a.Identifier, @"^(?:D|KD)\d", RegexOptions.IgnoreCase)
                            : Regex.IsMatch(a.Identifier, @"^(?:S|KS)\d", RegexOptions.IgnoreCase));
                    var familyMismatches = string.IsNullOrWhiteSpace(preferredFamily)
                        ? 0
                        : uniqueAnchors.Count - familyMatches;

                    var score = engineeringColumns * 24.0 + dnColumns * 14.0 + distinct * 2.0 - syntaxPenalty
                                + familyMatches * 28.0 - familyMismatches * 18.0;
                    return new { Band = b, Distinct = distinct, Width = width, EngineeringColumns = engineeringColumns, DnColumns = dnColumns, FamilyMatches = familyMatches, FamilyMismatches = familyMismatches, Score = score };
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.EngineeringColumns)
                .ThenByDescending(x => x.DnColumns)
                .ThenByDescending(x => x.Distinct)
                .ThenByDescending(x => x.Width)
                .First();

            debug.AppendLine($"4.2.4 profile node-band score: score={best.Score:0.0}, family={preferredFamily}, familyMatch={best.FamilyMatches}, familyMismatch={best.FamilyMismatches}, engineering={best.EngineeringColumns}, dn={best.DnColumns}, ids={best.Distinct}.");

            var selected = best.Band
                .GroupBy(a => a.Identifier, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(a => Math.Abs(a.Y - best.Band.Average(x => x.Y))).First())
                .OrderBy(a => a.X)
                .ToList();

            // If the densest band is too small, fall back to the previous evidence-based strategy.
            if (selected.Count < 3)
            {
                selected = candidates
                    .GroupBy(a => a.Identifier, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(a => VisionEvidenceScore(a, items, "PROFIL")).First())
                    .ToList();
                debug.AppendLine($"4.1 profile node row: fallback, denseBand={best.Distinct}.");
            }
            else
            {
                debug.AppendLine($"4.1 profile node row: selected {selected.Count} identifiers, Y~{best.Band.Average(a => a.Y):0.0}, tol={yTolerance:0.0}.");
            }

            return selected;
        }

        private static List<SpatialManholeAnchor> FindSpatialManholeAnchors(IReadOnlyList<TextItem> usable, string drawingType)
        {
            var anchors = new List<SpatialManholeAnchor>();

            // 1) Normal case: PdfPig returned the whole identifier as one word, e.g. D7 or D1.4.
            for (var itemIndex = 0; itemIndex < usable.Count; itemIndex++)
            {
                var item = usable[itemIndex];
                var token = CleanSpatialToken(item.Text);
                var match = ExactSpatialManholeRegex.Match(token);
                if (!match.Success)
                    continue;

                var identifier = BuildSpatialIdentifier(match);
                if (!IsLikelySpatialManholeIdentifier(identifier))
                    continue;

                anchors.Add(new SpatialManholeAnchor
                {
                    Identifier = identifier,
                    X = item.X + item.Width / 2.0,
                    Y = item.Y - item.Height / 2.0,
                    SourceIndex = itemIndex
                });
            }

            // 2) CAD PDFs often split labels into two words, e.g. "D" + "6/1" or "D" + "16".
            // Reconstruct only very close, same-line pairs so ordinary drawing text is not promoted to a manhole.
            var prefixes = usable
                .Select((item, index) => new { Item = item, Index = index })
                .Where(x => Regex.IsMatch(CleanSpatialToken(x.Item.Text), @"^(?:D|S|KD|KS)$", RegexOptions.IgnoreCase))
                .ToList();

            foreach (var prefixEntry in prefixes)
            {
                var prefixItem = prefixEntry.Item;
                var prefix = CleanSpatialToken(prefixItem.Text).ToUpperInvariant();
                var prefixCx = prefixItem.X + prefixItem.Width / 2.0;
                var prefixCy = prefixItem.Y - prefixItem.Height / 2.0;
                var rightEdge = prefixItem.X + Math.Max(0, prefixItem.Width);

                var suffix = usable
                    .Select((item, index) => new { Item = item, Index = index })
                    .Where(x => !ReferenceEquals(x.Item, prefixItem))
                    .Select(x => new
                    {
                        x.Item,
                        x.Index,
                        Token = CleanSpatialToken(x.Item.Text),
                        Cx = x.Item.X + x.Item.Width / 2.0,
                        Cy = x.Item.Y - x.Item.Height / 2.0
                    })
                    .Where(x => Regex.IsMatch(x.Token, @"^\d{1,3}(?:[./-]\d+)*$"))
                    .Where(x => x.Item.X >= prefixItem.X - 12)
                    .Where(x => x.Item.X - rightEdge <= (string.Equals(drawingType, "PZT", StringComparison.OrdinalIgnoreCase) ? 90 : 52))
                    .Where(x => Math.Abs(x.Cy - prefixCy) <= Math.Max(
                        string.Equals(drawingType, "PZT", StringComparison.OrdinalIgnoreCase) ? 30 : 22,
                        Math.Max(prefixItem.Height, x.Item.Height) * (string.Equals(drawingType, "PZT", StringComparison.OrdinalIgnoreCase) ? 3.0 : 2.4)))
                    // Geometry is more reliable than content-stream order in CAD PDFs.
                    .Where(x => Math.Abs(x.Index - prefixEntry.Index) <= (string.Equals(drawingType, "PZT", StringComparison.OrdinalIgnoreCase) ? 240 : 120) || Math.Abs(x.Cy - prefixCy) <= 8)
                    .OrderBy(x => Math.Abs(x.Cy - prefixCy) * 6.0 + Math.Abs(x.Cx - prefixCx) + Math.Min(40, Math.Abs(x.Index - prefixEntry.Index)))
                    .FirstOrDefault();

                if (suffix == null)
                    continue;

                var identifier = prefix + suffix.Token;
                if (!IsLikelySpatialManholeIdentifier(identifier))
                    continue;

                anchors.Add(new SpatialManholeAnchor
                {
                    Identifier = identifier,
                    X = (prefixCx + suffix.Cx) / 2.0,
                    Y = (prefixCy + suffix.Cy) / 2.0,
                    SourceIndex = Math.Min(prefixEntry.Index, suffix.Index)
                });
            }

            return anchors;
        }

        private static Dictionary<string, (double Ground, double Invert)> FindDirectTextElevationAssignments(string? rawText)
        {
            var result = new Dictionary<string, (double Ground, double Invert)>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(rawText))
                return result;

            // Strong PZT evidence: many CAD exports preserve a logical sequence such as
            // "D6/1 133,55 126,47" even when the rendered words are positioned separately.
            var normalized = rawText.Replace('\r', ' ').Replace('\n', ' ');
            var regex = new Regex(
                @"\b(?<id>(?:(?:KD|KS|D|S)\s*\d{1,3}(?:[./-]\d+)*|SO))\b\s*(?<a>\d{2,3}[,.]\d{2})\s+(?<b>\d{2,3}[,.]\d{2})",
                RegexOptions.IgnoreCase);

            foreach (Match match in regex.Matches(normalized))
            {
                var id = Regex.Replace(match.Groups["id"].Value, @"\s+", string.Empty).ToUpperInvariant();
                if (!IsLikelySpatialManholeIdentifier(id))
                    continue;

                if (!TryParseElevation(match.Groups["a"].Value, out var a) ||
                    !TryParseElevation(match.Groups["b"].Value, out var b))
                    continue;

                var diff = Math.Abs(a - b);
                if (diff < 0.20 || diff > 15.0)
                    continue;

                result[id] = (Math.Max(a, b), Math.Min(a, b));
            }

            return result;
        }

        private static Dictionary<string, (double Ground, double Invert)> BuildOrderedPztElevationAssignments(
            IReadOnlyList<SpatialManholeAnchor> anchors,
            IReadOnlyList<TextItem> items)
        {
            var result = new Dictionary<string, (double Ground, double Invert)>(StringComparer.OrdinalIgnoreCase);
            var usedItemIndices = new HashSet<int>();

            var numeric = items
                .Select((item, index) => new
                {
                    Index = index,
                    Raw = (item.Text ?? string.Empty).Trim(),
                    X = item.X + item.Width / 2.0,
                    Y = item.Y - item.Height / 2.0
                })
                .Where(x => TryParseElevation(x.Raw, out _))
                .Select(x => new
                {
                    x.Index,
                    x.X,
                    x.Y,
                    Value = ParseElevationUnchecked(x.Raw)
                })
                .ToList();

            foreach (var anchor in anchors.OrderBy(a => a.SourceIndex < 0 ? int.MaxValue : a.SourceIndex))
            {
                if (anchor.SourceIndex < 0)
                    continue;

                var nearby = numeric
                    .Where(n => !usedItemIndices.Contains(n.Index))
                    .Where(n => Math.Abs(n.Index - anchor.SourceIndex) <= 10)
                    .Where(n => Math.Abs(n.X - anchor.X) <= 120 && Math.Abs(n.Y - anchor.Y) <= 85)
                    .ToList();

                var candidates = new List<(int A, int B, double Ground, double Invert, double Score)>();
                for (var i = 0; i < nearby.Count; i++)
                {
                    for (var j = i + 1; j < nearby.Count; j++)
                    {
                        var a = nearby[i];
                        var b = nearby[j];
                        var diff = Math.Abs(a.Value - b.Value);
                        if (diff < 0.20 || diff > 15.0)
                            continue;

                        var pairDx = Math.Abs(a.X - b.X);
                        var pairDy = Math.Abs(a.Y - b.Y);
                        var coherentPair = (pairDy <= 14 && pairDx <= 85) || (pairDx <= 20 && pairDy <= 42);
                        if (!coherentPair)
                            continue;

                        var midX = (a.X + b.X) / 2.0;
                        var midY = (a.Y + b.Y) / 2.0;
                        var anchorDx = Math.Abs(midX - anchor.X);
                        var anchorDy = Math.Abs(midY - anchor.Y);

                        // CAD text blocks normally keep identifier and its two elevations
                        // adjacent in the content stream. Prioritise that relationship heavily.
                        var indexScore = Math.Abs(a.Index - anchor.SourceIndex) + Math.Abs(b.Index - anchor.SourceIndex);
                        var score = indexScore * 18.0 + anchorDx + anchorDy * 3.0 + pairDx * 0.20 + pairDy * 0.60;
                        candidates.Add((a.Index, b.Index, Math.Max(a.Value, b.Value), Math.Min(a.Value, b.Value), score));
                    }
                }

                var best = candidates.OrderBy(c => c.Score).FirstOrDefault();
                if (best.Score <= 0 || best.Score > 520)
                    continue;

                result[anchor.Identifier] = (best.Ground, best.Invert);
                usedItemIndices.Add(best.A);
                usedItemIndices.Add(best.B);
            }

            return result;
        }

        private static double ParseElevationUnchecked(string raw)
        {
            double.TryParse(
                (raw ?? string.Empty).Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value);
            return value;
        }

        private sealed class PztElevationPair
        {
            public int IndexA { get; init; }
            public int IndexB { get; init; }
            public double Ground { get; init; }
            public double Invert { get; init; }
            public double X { get; init; }
            public double Y { get; init; }
            public bool Horizontal { get; init; }
        }

        private static Dictionary<string, (double Ground, double Invert)> BuildPztElevationAssignments(
            IReadOnlyList<SpatialManholeAnchor> anchors,
            IReadOnlyList<TextItem> items)
        {
            var assignments = new Dictionary<string, (double Ground, double Invert)>(StringComparer.OrdinalIgnoreCase);

            var numeric = items
                .Select((item, index) => new
                {
                    Item = item,
                    Index = index,
                    Raw = (item.Text ?? string.Empty).Trim().Replace(',', '.'),
                    X = item.X + item.Width / 2.0,
                    Y = item.Y - item.Height / 2.0
                })
                .Where(x => TryParseElevation(x.Raw, out _))
                .Select(x => new
                {
                    x.Item,
                    x.Index,
                    x.X,
                    x.Y,
                    Value = TryParseElevation(x.Raw, out var v) ? v : double.NaN
                })
                .Where(x => !double.IsNaN(x.Value))
                .ToList();

            var pairs = new List<PztElevationPair>();
            for (var i = 0; i < numeric.Count; i++)
            {
                for (var j = i + 1; j < numeric.Count; j++)
                {
                    var a = numeric[i];
                    var b = numeric[j];
                    var valueDiff = Math.Abs(a.Value - b.Value);
                    if (valueDiff < 0.20 || valueDiff > 15.0)
                        continue;

                    var dx = Math.Abs(a.X - b.X);
                    var dy = Math.Abs(a.Y - b.Y);
                    var verticalStack = dx <= 18 && dy <= 34;
                    var horizontalRow = dy <= 12 && dx <= 70;
                    if (!verticalStack && !horizontalRow)
                        continue;

                    pairs.Add(new PztElevationPair
                    {
                        IndexA = a.Index,
                        IndexB = b.Index,
                        Ground = Math.Max(a.Value, b.Value),
                        Invert = Math.Min(a.Value, b.Value),
                        X = (a.X + b.X) / 2.0,
                        Y = (a.Y + b.Y) / 2.0,
                        Horizontal = horizontalRow
                    });
                }
            }

            // Build all plausible anchor-pair edges, then claim pairs globally. This prevents
            // the same elevations from being copied to two neighbouring manholes.
            var edges = new List<(SpatialManholeAnchor Anchor, int PairIndex, double Score)>();
            for (var p = 0; p < pairs.Count; p++)
            {
                var pair = pairs[p];
                foreach (var anchor in anchors)
                {
                    var dx = Math.Abs(pair.X - anchor.X);
                    var dy = Math.Abs(pair.Y - anchor.Y);

                    // 4.2.4 Batorego: tiled OCR often returns each rendered level two to four
                    // times. That repetition is useful confidence evidence. In the real PZT the
                    // repeated level stack can be displaced 70-90 px from the D/S label, so the
                    // old 32 px vertical-pair gate discarded otherwise unambiguous 62,25/60,58
                    // pairs. Only widen the ownership window when BOTH levels are independently
                    // repeated near the pair; ordinary one-off numbers keep the strict gate.
                    var repeatedGround = numeric.Count(n =>
                        Math.Abs(n.Value - pair.Ground) < 0.001 &&
                        Math.Abs(n.X - pair.X) <= 105 &&
                        Math.Abs(n.Y - pair.Y) <= 85) >= 2;
                    var repeatedInvert = numeric.Count(n =>
                        Math.Abs(n.Value - pair.Invert) < 0.001 &&
                        Math.Abs(n.X - pair.X) <= 105 &&
                        Math.Abs(n.Y - pair.Y) <= 85) >= 2;
                    var strongRepeatedPair = repeatedGround && repeatedInvert;

                    if (pair.Horizontal)
                    {
                        if (dx > 85 || dy > 18)
                            continue;
                    }
                    else
                    {
                        var maxDx = strongRepeatedPair ? 95 : 32;
                        var maxDy = strongRepeatedPair ? 75 : 58;
                        if (dx > maxDx || dy > maxDy)
                            continue;
                    }

                    // Last-resort spatial matching only. Ordered text-block matching has
                    // already had first refusal, so keep this deliberately conservative.
                    var score = dx * 1.5 + dy * 5.0;
                    if (pair.Horizontal)
                        score -= 6.0;

                    edges.Add((anchor, p, score));
                }
            }

            var usedAnchors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedNumericItems = new HashSet<int>();
            foreach (var edge in edges.OrderBy(e => e.Score))
            {
                if (edge.Score > 260)
                    continue;
                if (usedAnchors.Contains(edge.Anchor.Identifier))
                    continue;

                var pair = pairs[edge.PairIndex];
                if (usedNumericItems.Contains(pair.IndexA) || usedNumericItems.Contains(pair.IndexB))
                    continue;

                assignments[edge.Anchor.Identifier] = (pair.Ground, pair.Invert);
                usedAnchors.Add(edge.Anchor.Identifier);
                usedNumericItems.Add(pair.IndexA);
                usedNumericItems.Add(pair.IndexB);
            }

            return assignments;
        }

        private static bool TryParseElevation(string raw, out double value)
        {
            var normalized = NormalizeCadDuplicatedGlyphs(raw ?? string.Empty)
                .Trim()
                .Replace(']', '1')
                .Replace('|', '1')
                .Replace('I', '1')
                .Replace('l', '1')
                .Replace(',', '.');

            // OCR occasionally leaves punctuation around an otherwise valid level.
            normalized = Regex.Replace(normalized, @"^[^0-9]+|[^0-9.]+$", string.Empty);
            value = 0;
            if (!Regex.IsMatch(normalized, @"^\d{2,3}\.\d{2}$"))
                return false;

            if (!double.TryParse(
                    normalized,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value))
                return false;

            return value >= 20 && value <= 300;
        }

        private static void AddMissingTextOnlyManholes(
            PageText page,
            ParsedProject result,
            IReadOnlyList<SpatialManholeAnchor> spatialAnchors,
            StringBuilder debug)
        {
            if (string.IsNullOrWhiteSpace(page.Text))
                return;

            var directElevations = FindDirectTextElevationAssignments(page.Text);
            var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var identifierOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            void CountIdentifier(string id)
            {
                if (string.IsNullOrWhiteSpace(id))
                    return;
                identifierOccurrences[id] = identifierOccurrences.TryGetValue(id, out var n) ? n + 1 : 1;
            }

            foreach (Match match in ManholeRegex.Matches(page.Text))
            {
                var id = BuildManholeIdentifier(match);
                if (!string.IsNullOrWhiteSpace(id) && IsLikelySpatialManholeIdentifier(id))
                {
                    identifiers.Add(id);
                    CountIdentifier(id);
                }
            }

            // Some CAD exporters omit/reorder labels in Page.Text while still exposing them
            // as word items. Scan the item stream as a second source, including split labels.
            if (page.Items != null && page.Items.Count > 0)
            {
                var itemStream = string.Join(" ", page.Items.Select(i => i.Text ?? string.Empty));
                foreach (Match match in ManholeRegex.Matches(itemStream))
                {
                    var id = BuildManholeIdentifier(match);
                    if (!string.IsNullOrWhiteSpace(id) && IsLikelySpatialManholeIdentifier(id))
                        identifiers.Add(id);
                }

                foreach (var a in FindSpatialManholeAnchors(page.Items, result.DrawingType))
                    identifiers.Add(a.Identifier);
            }

            var isOcr = (page.ExtractionEngine ?? string.Empty).StartsWith("OCR/", StringComparison.OrdinalIgnoreCase);
            foreach (var id in identifiers)
            {
                if (result.Manholes.Any(m => m.Page == page.PageNumber && string.Equals(m.Identifier, id, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // OCR text streams may turn a diameter/elevation fragment into D60/D00.
                // Without a spatial anchor, retain an OCR-only identifier only when the same
                // text sequence also carries a plausible elevation pair.
                var acceptedSpatialAnchor = spatialAnchors.Any(a =>
                    string.Equals(a.Identifier, id, StringComparison.OrdinalIgnoreCase));
                var repeatedTextId = identifierOccurrences.TryGetValue(id, out var occurrenceCount) && occurrenceCount >= 2;
                if (isOcr && !directElevations.ContainsKey(id) && !acceptedSpatialAnchor && !repeatedTextId)
                {
                    debug.AppendLine($"OCR text fallback rejected {id}: no accepted spatial anchor/elevation pair and occurrences={occurrenceCount}.");
                    continue;
                }

                var parsed = new ParsedManhole
                {
                    Page = page.PageNumber,
                    Identifier = id,
                    RawText = "Oznaczenie znalezione w warstwie tekstowej PDF; brak pewnej kotwicy przestrzennej.",
                    Confidence = "niska"
                };

                if (directElevations.TryGetValue(id, out var pair))
                {
                    parsed.GroundElevationM = pair.Ground;
                    parsed.InvertElevationM = pair.Invert;
                    parsed.HeightM = Math.Round(pair.Ground - pair.Invert, 2);
                    parsed.Confidence = "średnia";
                }

                result.Manholes.Add(parsed);
                debug.AppendLine($"Text fallback manhole {id}: no spatial anchor, confidence={parsed.Confidence}.");
            }
        }

        private static bool ParseSpatialInlets(PageText page, ParsedProject result, StringBuilder debug)
        {
            if (page.Items == null || page.Items.Count == 0)
                return false;

            var found = 0;
            foreach (var item in page.Items.Where(i => !string.IsNullOrWhiteSpace(i.Text)))
            {
                var token = CleanSpatialToken(item.Text);
                var match = ExactSpatialInletRegex.Match(token);
                if (!match.Success)
                    continue;

                var identifier = "WP" + match.Groups["number"].Value;
                if (result.Inlets.Any(i => i.Page == page.PageNumber && string.Equals(i.Identifier, identifier, StringComparison.OrdinalIgnoreCase)))
                    continue;

                result.Inlets.Add(new ParsedInlet
                {
                    Page = page.PageNumber,
                    Identifier = identifier,
                    RawText = item.Text,
                    Confidence = "wysoka"
                });
                found++;
            }

            if (found > 0)
                debug.AppendLine($"Spatial inlets: {found} WP anchor(s).");

            return found > 0;
        }

        private static List<TextItem> GetLocalItems(
            SpatialManholeAnchor anchor,
            IReadOnlyList<SpatialManholeAnchor> anchors,
            IReadOnlyList<TextItem> items,
            string drawingType,
            double pageWidth,
            double pageHeight)
        {
            if (string.Equals(drawingType, "PROFIL", StringComparison.OrdinalIgnoreCase))
            {
                var ordered = anchors.OrderBy(a => a.X).ToList();
                var index = ordered.FindIndex(a => ReferenceEquals(a, anchor) ||
                    (a.Identifier == anchor.Identifier && Math.Abs(a.X - anchor.X) < 0.01 && Math.Abs(a.Y - anchor.Y) < 0.01));

                var edgeHalfWidth = ordered.Count == 1
                    ? Math.Max(140, pageWidth * 0.65)
                    : Math.Max(45, pageWidth * 0.04);
                var left = index <= 0
                    ? anchor.X - edgeHalfWidth
                    : (ordered[index - 1].X + anchor.X) / 2.0;
                var right = index < 0 || index == ordered.Count - 1
                    ? anchor.X + edgeHalfWidth
                    : (anchor.X + ordered[index + 1].X) / 2.0;

                var halfY = Math.Max(180, pageHeight * 0.32);
                return items
                    .Where(i =>
                    {
                        var cx = i.X + i.Width / 2.0;
                        var cy = i.Y - i.Height / 2.0;
                        return cx >= left && cx <= right && Math.Abs(cy - anchor.Y) <= halfY;
                    })
                    .OrderBy(i => Distance(anchor, i))
                    .ToList();
            }

            // PZT/unknown: build a local "vision cell" around the manhole.  An OCR word is
            // normally owned by the nearest manhole anchor; this prevents D7 from stealing D8
            // elevations/transitions while still keeping a generous inspection window.
            var halfX = Math.Max(130, Math.Min(360, pageWidth * 0.12));
            var halfYpzt = Math.Max(110, Math.Min(260, pageHeight * 0.12));
            return items
                .Where(i =>
                {
                    var cx = i.X + i.Width / 2.0;
                    var cy = i.Y - i.Height / 2.0;
                    if (Math.Abs(cx - anchor.X) > halfX || Math.Abs(cy - anchor.Y) > halfYpzt)
                        return false;

                    var ownDistance = Distance(anchor, i);
                    var nearestOther = anchors
                        .Where(a => !string.Equals(a.Identifier, anchor.Identifier, StringComparison.OrdinalIgnoreCase) || Math.Abs(a.X-anchor.X) > 0.1 || Math.Abs(a.Y-anchor.Y) > 0.1)
                        .Select(a => Distance(a, i))
                        .DefaultIfEmpty(double.MaxValue)
                        .Min();

                    // Allow a small overlap because labels and pipe descriptions can lie between two structures.
                    return ownDistance <= nearestOther * 1.18 || ownDistance <= 48;
                })
                .OrderBy(i => Distance(anchor, i))
                .ToList();
        }

        private static string BuildSpatialIdentifier(Match match)
        {
            if (match.Groups["special"].Success)
                return match.Groups["special"].Value.ToUpperInvariant();

            var token = match.Groups["token"].Value.ToUpperInvariant();
            var number = NormalizeIdentifierNumber(match.Groups["number"].Value);
            return token + number;
        }

        private static string NormalizeIdentifierNumber(string number)
        {
            if (string.IsNullOrWhiteSpace(number))
                return string.Empty;

            var parts = Regex.Split(number.Trim(), @"([./-])");
            for (var i = 0; i < parts.Length; i += 2)
            {
                if (int.TryParse(parts[i], out var value))
                    parts[i] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return string.Concat(parts);
        }

        private static bool IsLikelySpatialManholeIdentifier(string identifier)
        {
            if (string.Equals(identifier, "SO", StringComparison.OrdinalIgnoreCase))
                return true;

            // Existing pipes such as "ist. ks200" and "ist. ks160" are common on drawings.
            // KS/KD manhole numbering is normally a short index, not a pipe diameter.
            var match = Regex.Match(identifier, @"^(?<token>KD|KS|D|S)(?<n>\d+)", RegexOptions.IgnoreCase);
            if (!match.Success || !int.TryParse(match.Groups["n"].Value, out var n))
                return false;

            var token = match.Groups["token"].Value.ToUpperInvariant();
            if ((token == "KS" || token == "KD") && n >= 100)
                return false;
            if ((token == "D" || token == "S") && n >= 100)
                return false;

            // Vision 3.1: reject classic OCR contamination such as S3/2025.
            // Real drawing suffixes used by the projects handled here are short branch/index
            // components (D6/1, D1.4), not years, elevations or pipe sizes.
            var tail = identifier.Substring(match.Length);
            if (!string.IsNullOrEmpty(tail))
            {
                if (!Regex.IsMatch(tail, @"^(?:[./-]\d{1,2}){1,2}$"))
                    return false;

                // 3.3 hardening: OCR frequently turns an elevation such as 159.93 into D59.93.
                // Branch/index suffixes in the supported sewer drawings are short indices (D6/1,
                // D1.4).  A suffix >= 20 is therefore treated as numeric contamination rather than
                // a structure identifier.  Plain identifiers such as D20 remain valid.
                foreach (Match suffix in Regex.Matches(tail, @"[./-](?<n>\d{1,2})"))
                {
                    if (int.TryParse(suffix.Groups["n"].Value, out var suffixNumber) && suffixNumber >= 20)
                        return false;
                }
            }

            return true;
        }

        private static string NormalizeCadDuplicatedGlyphs(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var value = text.Trim();
            if (value.Length < 2 || value.Length % 2 != 0)
                return value;

            var sb = new StringBuilder(value.Length / 2);
            for (var i = 0; i < value.Length; i += 2)
            {
                if (value[i] != value[i + 1])
                    return value;
                sb.Append(value[i]);
            }

            return sb.ToString();
        }

        private static List<TextItem> BuildSyntheticCadElevationItems(IReadOnlyList<TextItem> items)
        {
            var synthetic = new List<TextItem>();
            var candidates = items
                .Where(i => !string.IsNullOrWhiteSpace(i.Text))
                .Where(i => Regex.IsMatch(NormalizeCadDuplicatedGlyphs(i.Text), @"^[0-9,.\]|Il]+$"))
                .Select(i => new
                {
                    Item = i,
                    Text = NormalizeCadDuplicatedGlyphs(i.Text),
                    Left = i.X,
                    Right = i.X + Math.Max(0, i.Width),
                    Cy = i.Y - i.Height / 2.0
                })
                .ToList();

            foreach (var seed in candidates)
            {
                var line = candidates
                    .Where(x => Math.Abs(x.Cy - seed.Cy) <= Math.Max(2.5, Math.Min(6.0, seed.Item.Height * 0.45)))
                    .Where(x => x.Left >= seed.Left - 0.1 && x.Left <= seed.Left + 55)
                    .OrderBy(x => x.Left)
                    .ToList();

                for (var start = 0; start < line.Count; start++)
                {
                    var text = string.Empty;
                    var left = line[start].Left;
                    var right = line[start].Right;
                    var minY = line[start].Item.Y - line[start].Item.Height;
                    var maxY = line[start].Item.Y;

                    for (var end = start; end < Math.Min(line.Count, start + 5); end++)
                    {
                        var part = line[end];
                        if (end > start && part.Left - right > 4.1)
                            break;

                        text += part.Text;
                        right = Math.Max(right, part.Right);
                        minY = Math.Min(minY, part.Item.Y - part.Item.Height);
                        maxY = Math.Max(maxY, part.Item.Y);

                        if (!TryParseElevation(text, out _))
                            continue;

                        synthetic.Add(new TextItem
                        {
                            Text = text,
                            X = left,
                            Y = maxY,
                            Width = Math.Max(1, right - left),
                            Height = Math.Max(1, maxY - minY)
                        });
                    }
                }
            }

            return synthetic
                .GroupBy(i => $"{i.Text}|{Math.Round(i.X, 1)}|{Math.Round(i.Y, 1)}")
                .Select(g => g.First())
                .ToList();
        }

        private static string CleanSpatialToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var value = text.Trim()
                .Trim(',', ';', ':', '(', ')', '[', ']', '{', '}')
                .Replace(" ", string.Empty);

            return NormalizeCadDuplicatedGlyphs(value);
        }

        private static int SpatialNeighborhoodScore(SpatialManholeAnchor anchor, IReadOnlyList<TextItem> items)
        {
            var score = 0;
            foreach (var item in items)
            {
                if (Distance(anchor, item) > 320)
                    continue;

                var t = item.Text ?? string.Empty;
                if (Regex.IsMatch(t, @"stud|DN|Ø|PVC|PP|PE|właz|rzęd|kinet|osad|\d{2,3}[,.]\d{2}", RegexOptions.IgnoreCase))
                    score++;
            }
            return score;
        }

        /// <summary>
        /// Scores an OCR/CAD identifier the way an engineer inspects a drawing: an identifier
        /// becomes credible when nearby geometry also contains elevations, a standard manhole DN,
        /// pipe/material labels, or explicit manhole terminology.  The score is deliberately not
        /// used as a hard delete gate; ProjectMerger can corroborate a weak candidate on another PDF.
        /// </summary>

        private static bool IsCrediblePztOcrAnchor(SpatialManholeAnchor anchor, IReadOnlyList<TextItem> items)
        {
            var score = VisionEvidenceScore(anchor, items, "PZT");
            var id = anchor.Identifier ?? string.Empty;

            // A normal short D/S label with at least one nearby engineering clue is retained.
            var numeric = Regex.Match(id, @"^(?<p>D|S)(?<n>\d+)$", RegexOptions.IgnoreCase);
            if (numeric.Success && int.TryParse(numeric.Groups["n"].Value, out var n))
            {
                // High-number OCR fragments are especially often born from DN/elevation text.
                if (n >= 30)
                    return score >= 4;
                return score >= 1;
            }

            // Branch identifiers require more evidence because OCR frequently turns decimals
            // and station values into slash/dot suffixes.
            if (Regex.IsMatch(id, @"^(?:D|S)\d+[./-]\d+$", RegexOptions.IgnoreCase))
                return score >= 3;

            // "SO" is allowed only with strong local engineering evidence.
            if (string.Equals(id, "SO", StringComparison.OrdinalIgnoreCase))
                return score >= 4;

            return score >= 2;
        }

        private static int VisionEvidenceScore(SpatialManholeAnchor anchor, IReadOnlyList<TextItem> items, string drawingType)
        {
            var nearby = items.Where(i => Distance(anchor, i) <= (string.Equals(drawingType, "PROFIL", StringComparison.OrdinalIgnoreCase) ? 420 : 300)).ToList();
            var score = 0;

            var elevations = nearby
                .Select(i => TryParseElevation(i.Text ?? string.Empty, out var v) ? (double?)v : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            if (elevations.Count >= 2 && elevations.Any(a => elevations.Any(b => a > b && a - b >= 0.20 && a - b <= 15.0)))
                score += 4;
            else if (elevations.Count == 1)
                score += 1;

            if (nearby.Any(i => Regex.IsMatch(NormalizeExtractedText(i.Text ?? string.Empty).Replace(" ", string.Empty), @"^(?:DN|Ø|ø)?(?:800|1000|1200|1500|1800|2000|2500|3000)$", RegexOptions.IgnoreCase)))
                score += 3;

            if (nearby.Any(i => Regex.IsMatch(i.Text ?? string.Empty, @"studnia|studzienka|kinet|osadnik|rewizyj|rozpr[eę]ż", RegexOptions.IgnoreCase)))
                score += 3;

            if (nearby.Any(i => Regex.IsMatch(i.Text ?? string.Empty, @"PVC|PYC|PP|PE|HDPE|PE-HD|DN\s*\d|Ø\s*\d", RegexOptions.IgnoreCase)))
                score += 2;

            if (nearby.Any(i => Regex.IsMatch(i.Text ?? string.Empty, @"właz|pokrywa|zwieńczenie|D400|C250", RegexOptions.IgnoreCase)))
                score += 1;

            return score;
        }

        private static double Distance(SpatialManholeAnchor anchor, TextItem item)
        {
            var cx = item.X + item.Width / 2.0;
            var cy = item.Y - item.Height / 2.0;
            var dx = cx - anchor.X;
            var dy = cy - anchor.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static ParsedManhole BuildSpatialManhole(
            SpatialManholeAnchor anchor,
            int pageNumber,
            string context,
            IReadOnlyList<TextItem> localItems,
            string drawingType,
            (double Ground, double Invert)? assignedElevations = null,
            int? assignedDiameter = null,
            double? assignedDepth = null)
        {
            var parsed = new ParsedManhole
            {
                Page = pageNumber,
                Identifier = anchor.Identifier,
                RawText = context
            };

            var typeMatch = SpatialManholeTypeRegex.Match(context);
            if (typeMatch.Success)
                parsed.Type = NormalizeManholeType(typeMatch.Value);

            parsed.DiameterMm = assignedDiameter ?? FindDirectSpatialManholeDiameter(anchor, localItems, drawingType);
            if (!parsed.DiameterMm.HasValue && Regex.IsMatch(context, @"\b(studnia|studzienka)\b", RegexOptions.IgnoreCase))
                parsed.DiameterMm = FindLikelyManholeDiameter(context);

            // 4.1: for quotation-ready manhole height use the profile's "Zagłębienie dna"
            // minus the standard maximum 0.15 m reserved for cover/regulation above the prefab body.
            // The raw ground/invert elevations are preserved separately.
            if (assignedDepth.HasValue && assignedDepth.Value >= 0.50 && assignedDepth.Value <= 10.0)
                parsed.HeightM = Math.Round(Math.Max(0.20, assignedDepth.Value - 0.15), 2);

            var heightMatch = SpatialHeightRegex.Match(context);
            if (!parsed.HeightM.HasValue && heightMatch.Success)
            {
                var rawHeight = heightMatch.Groups["h"].Value.Replace(',', '.');
                if (double.TryParse(rawHeight, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var explicitHeight))
                    parsed.HeightM = explicitHeight;
            }

            if (!parsed.HeightM.HasValue)
            {
                var elevations = assignedElevations ?? FindSpatialElevationPair(anchor, localItems, drawingType);
                if (elevations.HasValue)
                {
                    parsed.GroundElevationM = elevations.Value.Ground;
                    parsed.InvertElevationM = elevations.Value.Invert;
                    if (!parsed.HeightM.HasValue)
                        parsed.HeightM = Math.Round(elevations.Value.Ground - elevations.Value.Invert, 2);
                }
            }

            var crownMatch = SpatialCrownRegex.Match(context);
            if (crownMatch.Success)
                parsed.Crown = crownMatch.Value.Trim();

            // 4.1 quotation derivation from a validated gravity-profile table.
            // If a structure has a profile depth but no explicit "osadnik" description, the
            // through-flow profile is treated as kinetowa. For DN1200 quotation geometry,
            // shallow bodies use a relieving plate/ring; taller bodies use a cone.
            if (string.Equals(drawingType, "PROFIL", StringComparison.OrdinalIgnoreCase) && assignedDepth.HasValue)
            {
                if (string.IsNullOrWhiteSpace(parsed.Type) &&
                    !Regex.IsMatch(context, @"osadnik|osadnikow", RegexOptions.IgnoreCase))
                    parsed.Type = "kinetowa";

                if (string.IsNullOrWhiteSpace(parsed.Crown) && parsed.HeightM.HasValue)
                    parsed.Crown = parsed.HeightM.Value <= 1.10
                        ? "pierścień+płyta_odc."
                        : "zwężka";
            }

            var transitions = new Dictionary<(string Material, int Diameter), int>();
            foreach (Match transition in SpatialTransitionRegex.Matches(context))
            {
                if (!int.TryParse(transition.Groups["diam"].Value, out var diameter) || diameter < 20)
                    continue;

                var material = NormalizeMaterial(transition.Groups["mat"].Value);
                if (material is "BETON" or "ŻELBET")
                {
                    var lookBehindStart = Math.Max(0, transition.Index - 28);
                    var lookBehind = context.Substring(lookBehindStart, transition.Index - lookBehindStart);
                    if (Regex.IsMatch(lookBehind, @"studni(?:a|e|y|ą)|studzienk(?:a|i)", RegexOptions.IgnoreCase))
                        continue;
                }

                // A transition must be a realistic pipe diameter, never the manhole DN itself.
                if (parsed.DiameterMm.HasValue && diameter == parsed.DiameterMm.Value && diameter >= 800)
                    continue;

                var key = (material, diameter);
                transitions[key] = transitions.TryGetValue(key, out var count) ? count + 1 : 1;
            }

            // OCR often separates material and diameter into neighbouring words (e.g. "PVC" + "160")
            // or misreads PVC as PYC/P¥C. Recover those local connections spatially.
            foreach (var pair in FindSpatialTransitionPairs(anchor, localItems, parsed.DiameterMm))
            {
                var key = (pair.Material, pair.Diameter);
                transitions[key] = Math.Max(transitions.TryGetValue(key, out var count) ? count : 0, pair.Quantity);
            }

            foreach (var pair in transitions.OrderBy(t => t.Key.Material).ThenBy(t => t.Key.Diameter))
            {
                parsed.Transitions.Add(new ManholeTransition
                {
                    Material = pair.Key.Material,
                    DiameterMm = pair.Key.Diameter,
                    Quantity = pair.Value
                });
            }

            parsed.Confidence = DetermineConfidence(parsed);
            return parsed;
        }

        private static int? FindDirectSpatialManholeDiameter(SpatialManholeAnchor anchor, IReadOnlyList<TextItem> localItems, string drawingType)
        {
            // On PZT, nearby Ø800/Ø630 etc. are usually pipe diameters, not manhole DN.
            // Only profiles/details are allowed to provide DN from local geometry.
            if (string.Equals(drawingType, "PZT", StringComparison.OrdinalIgnoreCase))
                return null;

            var candidates = new List<(int Diameter, double Distance)>();
            foreach (var item in localItems)
            {
                var token = NormalizeExtractedText(item.Text ?? string.Empty).Replace(" ", string.Empty);
                var match = Regex.Match(token, @"^(?:DN|D|Ø|ø)(?<d>\d{3,4})$", RegexOptions.IgnoreCase);
                if (!match.Success || !int.TryParse(match.Groups["d"].Value, out var diameter))
                    continue;
                if (!IsPlausibleManholeDiameter(diameter))
                    continue;

                var distance = Distance(anchor, item);
                if (distance <= 95)
                    candidates.Add((diameter, distance));
            }

            // OCR frequently splits "Ø1200" / "DN1200" into two words. Pair a marker
            // with the nearest standard diameter on the same visual line.
            var markers = localItems.Where(i => Regex.IsMatch((i.Text ?? string.Empty).Trim(), @"^(?:DN|D|Ø|ø)$", RegexOptions.IgnoreCase)).ToList();
            foreach (var marker in markers)
            {
                var mcx = marker.X + marker.Width / 2.0;
                var mcy = marker.Y - marker.Height / 2.0;
                foreach (var number in localItems)
                {
                    var raw = Regex.Replace(number.Text ?? string.Empty, @"[^0-9]", string.Empty);
                    if (!int.TryParse(raw, out var diameter) || !IsPlausibleManholeDiameter(diameter))
                        continue;
                    var ncx = number.X + number.Width / 2.0;
                    var ncy = number.Y - number.Height / 2.0;
                    var dx = Math.Abs(ncx - mcx);
                    var dy = Math.Abs(ncy - mcy);
                    if (dx <= 85 && dy <= Math.Max(22, Math.Max(marker.Height, number.Height) * 1.8))
                    {
                        var d = Math.Sqrt((ncx-anchor.X)*(ncx-anchor.X)+(ncy-anchor.Y)*(ncy-anchor.Y));
                        if (d <= 120) candidates.Add((diameter, d + dx * 0.15));
                    }
                }
            }

            if (candidates.Count == 0 && string.Equals(drawingType, "PROFIL", StringComparison.OrdinalIgnoreCase))
            {
                // Vision fallback: OCR may lose the Ø/DN glyph while preserving the standard
                // manhole number next to the structure label (e.g. D7 1200). Accept only standard
                // prefab diameters on nearly the same baseline and very close to the anchor.
                foreach (var number in localItems)
                {
                    var raw = Regex.Replace(number.Text ?? string.Empty, @"[^0-9]", string.Empty);
                    if (!int.TryParse(raw, out var diameter) || !IsPlausibleManholeDiameter(diameter))
                        continue;
                    var ncx = number.X + number.Width / 2.0;
                    var ncy = number.Y - number.Height / 2.0;
                    var dx = Math.Abs(ncx - anchor.X);
                    var dy = Math.Abs(ncy - anchor.Y);
                    if (dx <= 115 && dy <= 34)
                        candidates.Add((diameter, Math.Sqrt(dx * dx + dy * dy) + 18));
                }
            }

            return candidates.OrderBy(c => c.Distance).Select(c => (int?)c.Diameter).FirstOrDefault();
        }

        private static IEnumerable<(string Material, int Diameter, int Quantity)> FindSpatialTransitionPairs(
            SpatialManholeAnchor anchor, IReadOnlyList<TextItem> localItems, int? manholeDiameter)
        {
            var found = new Dictionary<(string Material, int Diameter), HashSet<string>>();
            for (var i = 0; i < localItems.Count; i++)
            {
                var raw = NormalizeExtractedText(localItems[i].Text ?? string.Empty);
                var compact = Regex.Match(raw.Replace(" ", string.Empty), @"^(?<mat>PVC|PYC|P¥C|PP|PE|PEHD|HDPE|PE-HD)(?:DN|D|Ø|ø)?(?<d>\d{2,4})$", RegexOptions.IgnoreCase);
                if (compact.Success && int.TryParse(compact.Groups["d"].Value, out var cd))
                {
                    Add(compact.Groups["mat"].Value, cd, localItems[i]);
                    continue;
                }

                var matMatch = Regex.Match(raw, @"^(?<mat>PVC|PYC|P¥C|PP|PE|PEHD|HDPE|PE-HD)$", RegexOptions.IgnoreCase);
                if (!matMatch.Success) continue;

                var a = localItems[i];
                var acx = a.X + a.Width / 2.0;
                var acy = a.Y - a.Height / 2.0;
                var number = localItems
                    .Select(x => new { Item=x, Text=NormalizeExtractedText(x.Text ?? string.Empty) })
                    .Select(x => new { x.Item, Match=Regex.Match(x.Text.Replace(" ", string.Empty), @"^(?:DN|D|Ø|ø)?(?<d>\d{2,4})$", RegexOptions.IgnoreCase) })
                    .Where(x => x.Match.Success)
                    .Select(x => new { x.Item, Diameter=int.Parse(x.Match.Groups["d"].Value), Cx=x.Item.X+x.Item.Width/2.0, Cy=x.Item.Y-x.Item.Height/2.0 })
                    .Where(x => x.Diameter >= 40 && x.Diameter <= 2000)
                    .Where(x => Math.Abs(x.Cy-acy) <= Math.Max(28, Math.Max(a.Height,x.Item.Height)*2.2))
                    .Where(x => Math.Abs(x.Cx-acx) <= 115)
                    .OrderBy(x => Math.Abs(x.Cx-acx)+Math.Abs(x.Cy-acy)*3)
                    .FirstOrDefault();
                if (number != null) Add(matMatch.Groups["mat"].Value, number.Diameter, number.Item);
            }

            foreach (var kv in found)
                yield return (kv.Key.Material, kv.Key.Diameter, Math.Max(1, kv.Value.Count));

            void Add(string materialRaw, int diameter, TextItem evidence)
            {
                if (diameter < 40 || diameter > 2000) return;
                if (manholeDiameter.HasValue && diameter == manholeDiameter.Value && diameter >= 800) return;
                var material = materialRaw.ToUpperInvariant().Replace("PYC", "PVC").Replace("P¥C", "PVC");
                material = NormalizeMaterial(material);
                var key=(material,diameter);
                if (!found.TryGetValue(key, out var set)) found[key]=set=new HashSet<string>();
                set.Add($"{Math.Round(evidence.X,0)}:{Math.Round(evidence.Y,0)}");
            }
        }



        private static Dictionary<string, ProfileColumnAssignment> BuildProfileGeometryAssignments(
            IReadOnlyList<SpatialManholeAnchor> anchors,
            IReadOnlyList<TextItem> items,
            double pageWidth,
            double pageHeight,
            StringBuilder debug)
        {
            var result = anchors
                .GroupBy(a => a.Identifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => new ProfileColumnAssignment
                    {
                        Identifier = g.Key,
                        X = g.OrderByDescending(a => VisionEvidenceScore(a, items, "PROFIL")).First().X
                    },
                    StringComparer.OrdinalIgnoreCase);

            if (result.Count < 2 || items.Count == 0)
                return result;

            var anchorList = result.Values.OrderBy(a => a.X).ToList();
            var gaps = anchorList.Zip(anchorList.Skip(1), (a, b) => b.X - a.X)
                .Where(g => g > 2)
                .OrderBy(g => g)
                .ToList();
            var medianGap = gaps.Count > 0 ? gaps[gaps.Count / 2] : Math.Max(50.0, pageWidth / Math.Max(3, anchorList.Count));
            var xTolerance = Math.Max(15.0, Math.Min(65.0, medianGap * 0.36));

            // Elevation candidates: two decimals, plausible civil-engineering level range.
            var elevationPoints = new List<ProfileNumericPoint>();
            foreach (var item in items)
            {
                var raw = (item.Text ?? string.Empty).Trim();
                if (!Regex.IsMatch(raw, @"^\d{2,3}[,.]\d{2}$"))
                    continue;
                if (!TryParseElevation(raw, out var value) || value < 20 || value > 250)
                    continue;

                elevationPoints.Add(new ProfileNumericPoint
                {
                    Value = value,
                    X = item.X + item.Width / 2.0,
                    Y = item.Y - item.Height / 2.0,
                    Height = Math.Max(1.0, item.Height)
                });
            }

            if (elevationPoints.Count >= 4)
            {
                var medianTextHeight = elevationPoints.Select(p => p.Height).OrderBy(h => h).ElementAt(elevationPoints.Count / 2);
                var bandTol = Math.Max(3.5, Math.Min(15.0, medianTextHeight * 0.9));
                var bands = ClusterProfileBands(elevationPoints, bandTol)
                    .Where(b => b.Points.Count >= 2)
                    .ToList();

                // 4.2.2: depth is NOT an elevation. In 4.1 the triple resolver searched the
                // elevation bands (20..250) for values such as 1.56, therefore it could never
                // succeed. Build a separate depth-band model from decimal values 0.45..8.00.
                var depthPoints = new List<ProfileNumericPoint>();
                foreach (var item in items)
                {
                    var raw = (item.Text ?? string.Empty).Trim();
                    if (!Regex.IsMatch(raw, @"^\d{1,2}[,.]\d{2}$"))
                        continue;
                    if (!double.TryParse(raw.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0.45 || value > 8.0)
                        continue;
                    depthPoints.Add(new ProfileNumericPoint
                    {
                        Value = value,
                        X = item.X + item.Width / 2.0,
                        Y = item.Y - item.Height / 2.0,
                        Height = Math.Max(1.0, item.Height)
                    });
                }
                var depthBands = ClusterProfileBands(depthPoints, bandTol)
                    .Where(b => b.Points.Count >= 2)
                    .ToList();

                // Geometry table resolver: Ground - Invert ~= Depth in the same X columns.
                (ProfileNumericBand GroundBand, ProfileNumericBand InvertBand, ProfileNumericBand DepthBand, double Score, int Coverage)? tripleBest = null;

                foreach (var groundBand in bands)
                foreach (var invertBand in bands)
                {
                    if (ReferenceEquals(groundBand, invertBand))
                        continue;

                    foreach (var depthBand in depthBands)
                    {

                        var hits = 0;
                        var residualSum = 0.0;
                        var dxSum = 0.0;
                        var depthValues = new List<double>();

                        foreach (var column in anchorList)
                        {
                            var g = groundBand.Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                            var inv = invertBand.Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                            var dep = depthBand.Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                            if (g == null || inv == null || dep == null)
                                continue;

                            var gx = Math.Abs(g.X - column.X);
                            var ix = Math.Abs(inv.X - column.X);
                            var dx = Math.Abs(dep.X - column.X);
                            if (gx > xTolerance || ix > xTolerance || dx > xTolerance)
                                continue;

                            var calculated = g.Value - inv.Value;
                            if (calculated < 0.45 || calculated > 8.0)
                                continue;
                            if (dep.Value < 0.45 || dep.Value > 8.0)
                                continue;

                            var residual = Math.Abs(calculated - dep.Value);
                            if (residual > 0.28)
                                continue;

                            hits++;
                            residualSum += residual;
                            dxSum += gx + ix + dx;
                            depthValues.Add(dep.Value);
                        }

                        if (hits < Math.Max(2, Math.Min(4, anchorList.Count / 3)))
                            continue;

                        var meanResidual = residualSum / hits;
                        var meanDx = dxSum / hits;
                        var score = hits * 70.0 - meanResidual * 180.0 - meanDx * 0.5;

                        if (!tripleBest.HasValue || score > tripleBest.Value.Score)
                            tripleBest = (groundBand, invertBand, depthBand, score, hits);
                    }
                }

                if (tripleBest.HasValue)
                {
                    foreach (var column in anchorList)
                    {
                        var g = tripleBest.Value.GroundBand.Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                        var inv = tripleBest.Value.InvertBand.Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                        var dep = tripleBest.Value.DepthBand.Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                        if (g == null || inv == null || dep == null)
                            continue;

                        if (Math.Abs(g.X - column.X) > xTolerance ||
                            Math.Abs(inv.X - column.X) > xTolerance ||
                            Math.Abs(dep.X - column.X) > xTolerance)
                            continue;

                        var calculated = g.Value - inv.Value;
                        var residual = Math.Abs(calculated - dep.Value);
                        if (calculated < 0.45 || calculated > 8.0 ||
                            dep.Value < 0.45 || dep.Value > 8.0 ||
                            residual > 0.28)
                            continue;

                        column.Ground = g.Value;
                        column.Invert = inv.Value;
                        column.Depth = dep.Value;
                        column.Evidence += 4;
                    }

                    debug.AppendLine(
                        $"4.2.2 PROFILE TABLE: groundY={tripleBest.Value.GroundBand.Y:0.0}, invertY={tripleBest.Value.InvertBand.Y:0.0}, depthY={tripleBest.Value.DepthBand.Y:0.0}, " +
                        $"coverage={tripleBest.Value.Coverage}/{anchorList.Count}, assignments={anchorList.Count(c => c.Depth.HasValue)}.");
                }

                (ProfileNumericBand GroundBand, ProfileNumericBand InvertBand, double Score, int Coverage)? best = null;

                for (var i = 0; i < bands.Count; i++)
                {
                    for (var j = 0; j < bands.Count; j++)
                    {
                        if (i == j)
                            continue;

                        var pairs = new List<(double X, double Ground, double Invert, double Dx)>();
                        foreach (var column in anchorList)
                        {
                            var a = bands[i].Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                            var b = bands[j].Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                            if (a == null || b == null)
                                continue;

                            var dax = Math.Abs(a.X - column.X);
                            var dbx = Math.Abs(b.X - column.X);
                            if (dax > xTolerance || dbx > xTolerance)
                                continue;

                            // Ground must be above invert and the depth must resemble a sewer structure.
                            var h = a.Value - b.Value;
                            if (h < 0.45 || h > 6.50)
                                continue;

                            pairs.Add((column.X, a.Value, b.Value, dax + dbx));
                        }

                        if (pairs.Count < Math.Max(2, Math.Min(4, anchorList.Count / 3)))
                            continue;

                        pairs = pairs.OrderBy(x => x.X).ToList();
                        var groundRoughness = 0.0;
                        var invertRoughness = 0.0;
                        for (var k = 1; k < pairs.Count; k++)
                        {
                            groundRoughness += Math.Abs(pairs[k].Ground - pairs[k - 1].Ground);
                            invertRoughness += Math.Abs(pairs[k].Invert - pairs[k - 1].Invert);
                        }

                        // A true table row should cover many structure columns and vary smoothly.
                        // Huge jumps are typical when OCR rows from unrelated labels are mixed.
                        var meanDx = pairs.Average(x => x.Dx);
                        var smoothPenalty = groundRoughness * 3.2 + invertRoughness * 2.0;
                        // OCR/PdfPig/PDFium may expose opposite Y-axis directions.
                        // Ground/invert identity is established by numeric value, not screen Y.
                        var score =
                            pairs.Count * 35.0
                            - meanDx * 0.8
                            - smoothPenalty;

                        if (!best.HasValue || score > best.Value.Score)
                            best = (bands[i], bands[j], score, pairs.Count);
                    }
                }

                if (best.HasValue && !result.Values.Any(c => c.Depth.HasValue))
                {
                    foreach (var column in anchorList)
                    {
                        var ground = best.Value.GroundBand.Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                        var invert = best.Value.InvertBand.Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                        if (ground == null || invert == null)
                            continue;
                        if (Math.Abs(ground.X - column.X) > xTolerance || Math.Abs(invert.X - column.X) > xTolerance)
                            continue;

                        var height = ground.Value - invert.Value;
                        if (height < 0.45 || height > 6.50)
                            continue;

                        column.Ground = ground.Value;
                        column.Invert = invert.Value;
                        column.Evidence += 3;
                    }

                    debug.AppendLine(
                        $"4.1 geometry elevations: rows Y={best.Value.GroundBand.Y:0.0}/{best.Value.InvertBand.Y:0.0}, " +
                        $"coverage={best.Value.Coverage}/{anchorList.Count}, assignments={anchorList.Count(c => c.Ground.HasValue)}, xTol={xTolerance:0.0}.");
                }
                else
                {
                    debug.AppendLine($"4.1 geometry elevations: no stable two-row model; bands={bands.Count}.");
                }
            }

            // Diameter row: standard manhole diameters repeated along a common horizontal band.
            var diameterPoints = new List<ProfileNumericPoint>();
            foreach (var item in items)
            {
                var raw = NormalizeExtractedText(item.Text ?? string.Empty).Replace(" ", string.Empty);
                var match = Regex.Match(raw, @"^(?:DN|D|Ø|ø)?(?<d>\d{3,4})$", RegexOptions.IgnoreCase);
                if (!match.Success || !int.TryParse(match.Groups["d"].Value, out var dn) || !IsPlausibleManholeDiameter(dn))
                    continue;

                diameterPoints.Add(new ProfileNumericPoint
                {
                    Value = dn,
                    X = item.X + item.Width / 2.0,
                    Y = item.Y - item.Height / 2.0,
                    Height = Math.Max(1.0, item.Height)
                });
            }

            if (diameterPoints.Count >= 2)
            {
                var medH = diameterPoints.Select(p => p.Height).OrderBy(h => h).ElementAt(diameterPoints.Count / 2);
                var dnBands = ClusterProfileBands(diameterPoints, Math.Max(4.1, Math.Min(18.0, medH * 1.1)));

                ProfileNumericBand? bestDnBand = null;
                var bestDnScore = double.MinValue;
                var bestDnHits = 0;

                foreach (var band in dnBands)
                {
                    var hits = new List<ProfileNumericPoint>();
                    foreach (var column in anchorList)
                    {
                        var nearest = band.Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                        if (nearest != null && Math.Abs(nearest.X - column.X) <= xTolerance)
                            hits.Add(nearest);
                    }

                    if (hits.Count < Math.Max(2, Math.Min(4, anchorList.Count / 3)))
                        continue;

                    // Repeated identical DN is strong evidence; mixed pipe sizes are penalized.
                    var dominant = hits.GroupBy(h => (int)Math.Round(h.Value))
                        .OrderByDescending(g => g.Count())
                        .First();
                    var dominance = dominant.Count() / (double)hits.Count;
                    var score = hits.Count * 20.0 + dominance * 40.0;

                    if (score > bestDnScore)
                    {
                        bestDnScore = score;
                        bestDnBand = band;
                        bestDnHits = hits.Count;
                    }
                }

                if (bestDnBand != null)
                {
                    foreach (var column in anchorList)
                    {
                        var nearest = bestDnBand.Points.OrderBy(pt => Math.Abs(pt.X - column.X)).FirstOrDefault();
                        if (nearest == null || Math.Abs(nearest.X - column.X) > xTolerance)
                            continue;
                        column.Diameter = (int)Math.Round(nearest.Value);
                        column.Evidence += 2;
                    }

                    debug.AppendLine(
                        $"4.1 geometry DN: row Y={bestDnBand.Y:0.0}, hits={bestDnHits}/{anchorList.Count}, assignments={anchorList.Count(c => c.Diameter.HasValue)}.");
                }
                else
                {
                    debug.AppendLine("4.1 geometry DN: no stable repeated DN row.");
                }
            }

            // 4.1 vertical-callout DN rescue. Profile drawings often write "Studnia ... 1.2m"
            // vertically above the node rather than placing DN in a horizontal table row.
            foreach (var column in anchorList.Where(c => !c.Diameter.HasValue))
            {
                var columnItems = items
                    .Where(i => Math.Abs((i.X + i.Width / 2.0) - column.X) <= Math.Max(28.0, xTolerance))
                    .OrderBy(i => Math.Abs((i.X + i.Width / 2.0) - column.X))
                    .ToList();

                var standardCandidates = new List<int>();
                foreach (var item in columnItems)
                {
                    var token = NormalizeExtractedText(item.Text ?? string.Empty).Replace(" ", string.Empty);
                    var direct = Regex.Match(token, @"^(?:DN|D|Ø|ø)?(?<d>800|1000|1200|1500|1800|2000|2500|3000)$", RegexOptions.IgnoreCase);
                    if (direct.Success && int.TryParse(direct.Groups["d"].Value, out var dn))
                        standardCandidates.Add(dn);

                    // OCR frequently drops the trailing "m" from vertical manhole callouts (e.g. 1,2 m).
                    var metres = Regex.Match(token, @"^(?<m>[0123](?:[,.]\d{1,2}))m?$", RegexOptions.IgnoreCase);
                    if (metres.Success &&
                        double.TryParse(metres.Groups["m"].Value.Replace(',', '.'),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var metresValue))
                    {
                        var mm = (int)Math.Round(metresValue * 1000.0 / 50.0) * 50;
                        if (IsPlausibleManholeDiameter(mm))
                            standardCandidates.Add(mm);
                    }
                }

                if (standardCandidates.Count > 0)
                {
                    var selectedDn = standardCandidates
                        .GroupBy(x => x)
                        .OrderByDescending(g => g.Count())
                        .ThenBy(g => Math.Abs(g.Key - 1200))
                        .First().Key;
                    column.Diameter = selectedDn;
                    column.Evidence += 2;
                }
            }

            debug.AppendLine($"4.1 vertical DN rescue: assignments={anchorList.Count(c => c.Diameter.HasValue)}/{anchorList.Count}.");
            return result;
        }

        private static Dictionary<string, (double Ground, double Invert)> BuildProfileColumnElevationAssignments(
            IReadOnlyList<SpatialManholeAnchor> anchors,
            IReadOnlyList<TextItem> items,
            StringBuilder debug)
        {
            var result = new Dictionary<string, (double Ground, double Invert)>(StringComparer.OrdinalIgnoreCase);
            if (anchors.Count < 2 || items.Count == 0)
                return result;

            // Only CAD-style elevations with exactly two decimal places are candidates for row bands.
            // This excludes station numbers, pipe DN, slopes and most dimensions.
            var points = items
                .Where(i => Regex.IsMatch((i.Text ?? string.Empty).Trim(), @"^\d{2,3}[,.]\d{2}$"))
                .Select(i =>
                {
                    TryParseElevation(i.Text ?? string.Empty, out var value);
                    return new ProfileNumericPoint
                    {
                        Value = value,
                        X = i.X + i.Width / 2.0,
                        Y = i.Y - i.Height / 2.0,
                        Height = Math.Max(1.0, i.Height)
                    };
                })
                .Where(p => p.Value >= 20 && p.Value <= 250)
                .ToList();

            if (points.Count < 4)
                return result;

            var typicalHeight = points.OrderBy(p => p.Height).ElementAt(points.Count / 2).Height;
            var yTolerance = Math.Max(4.1, Math.Min(14.1, typicalHeight * 0.9));
            var bands = ClusterProfileBands(points, yTolerance)
                .Where(b => b.Points.Count >= Math.Max(2, Math.Min(5, anchors.Count / 4)))
                .ToList();

            if (bands.Count < 2)
            {
                debug.AppendLine($"4.1 profile rows: insufficient numeric bands ({bands.Count}).");
                return result;
            }

            // Derive X tolerance from anchor spacing instead of a fixed pixel constant.
            var sortedX = anchors.Select(a => a.X).OrderBy(x => x).ToList();
            var gaps = sortedX.Zip(sortedX.Skip(1), (a, b) => b - a).Where(g => g > 1).OrderBy(g => g).ToList();
            var medianGap = gaps.Count > 0 ? gaps[gaps.Count / 2] : 80.0;
            var xTolerance = Math.Max(16.0, Math.Min(55.0, medianGap * 0.34));

            (ProfileNumericBand A, ProfileNumericBand B, int Hits, double Alignment, double MeanHeight)? best = null;

            for (var i = 0; i < bands.Count; i++)
            {
                for (var j = i + 1; j < bands.Count; j++)
                {
                    var heights = new List<double>();
                    var alignment = 0.0;
                    var hits = 0;

                    foreach (var anchor in anchors)
                    {
                        var a = bands[i].Points.OrderBy(p => Math.Abs(p.X - anchor.X)).FirstOrDefault();
                        var b = bands[j].Points.OrderBy(p => Math.Abs(p.X - anchor.X)).FirstOrDefault();
                        if (a == null || b == null)
                            continue;

                        var dax = Math.Abs(a.X - anchor.X);
                        var dbx = Math.Abs(b.X - anchor.X);
                        if (dax > xTolerance || dbx > xTolerance)
                            continue;

                        var h = Math.Abs(a.Value - b.Value);
                        if (h < 0.35 || h > 8.0)
                            continue;

                        hits++;
                        heights.Add(h);
                        alignment += dax + dbx;
                    }

                    if (hits < 2)
                        continue;

                    var meanHeight = heights.Average();
                    // Prefer the pair that explains most structure columns, then best X alignment.
                    if (!best.HasValue ||
                        hits > best.Value.Hits ||
                        (hits == best.Value.Hits && alignment < best.Value.Alignment))
                    {
                        best = (bands[i], bands[j], hits, alignment, meanHeight);
                    }
                }
            }

            if (!best.HasValue)
            {
                debug.AppendLine("4.1 profile rows: no coherent ground/invert band pair.");
                return result;
            }

            foreach (var anchor in anchors)
            {
                var a = best.Value.A.Points.OrderBy(p => Math.Abs(p.X - anchor.X)).FirstOrDefault();
                var b = best.Value.B.Points.OrderBy(p => Math.Abs(p.X - anchor.X)).FirstOrDefault();
                if (a == null || b == null)
                    continue;
                if (Math.Abs(a.X - anchor.X) > xTolerance || Math.Abs(b.X - anchor.X) > xTolerance)
                    continue;

                var h = Math.Abs(a.Value - b.Value);
                if (h < 0.35 || h > 8.0)
                    continue;

                result[anchor.Identifier] = (Math.Max(a.Value, b.Value), Math.Min(a.Value, b.Value));
            }

            debug.AppendLine(
                $"4.1 profile rows: bands={bands.Count}, selected Y={best.Value.A.Y:0.0}/{best.Value.B.Y:0.0}, " +
                $"columnHits={best.Value.Hits}/{anchors.Count}, assignments={result.Count}, meanH={best.Value.MeanHeight:0.00}, xTol={xTolerance:0.0}.");
            return result;
        }

        private static Dictionary<string, int> BuildProfileColumnDiameterAssignments(
            IReadOnlyList<SpatialManholeAnchor> anchors,
            IReadOnlyList<TextItem> items,
            StringBuilder debug)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (anchors.Count < 2)
                return result;

            var points = new List<ProfileNumericPoint>();
            foreach (var item in items)
            {
                var raw = NormalizeExtractedText(item.Text ?? string.Empty).Replace(" ", string.Empty);
                var match = Regex.Match(raw, @"^(?:DN|D|Ø|ø)?(?<d>\d{3,4})$", RegexOptions.IgnoreCase);
                if (!match.Success || !int.TryParse(match.Groups["d"].Value, out var diameter))
                    continue;
                if (!IsPlausibleManholeDiameter(diameter))
                    continue;

                points.Add(new ProfileNumericPoint
                {
                    Value = diameter,
                    X = item.X + item.Width / 2.0,
                    Y = item.Y - item.Height / 2.0,
                    Height = Math.Max(1.0, item.Height)
                });
            }

            if (points.Count < 2)
                return result;

            var typicalHeight = points.OrderBy(p => p.Height).ElementAt(points.Count / 2).Height;
            var bands = ClusterProfileBands(points, Math.Max(5.0, Math.Min(16.0, typicalHeight)))
                .OrderByDescending(b => b.Points.Count)
                .ToList();

            var sortedX = anchors.Select(a => a.X).OrderBy(x => x).ToList();
            var gaps = sortedX.Zip(sortedX.Skip(1), (a, b) => b - a).Where(g => g > 1).OrderBy(g => g).ToList();
            var medianGap = gaps.Count > 0 ? gaps[gaps.Count / 2] : 80.0;
            var xTolerance = Math.Max(16.0, Math.Min(55.0, medianGap * 0.34));

            ProfileNumericBand? bestBand = null;
            int bestHits = 0;
            foreach (var band in bands)
            {
                var hits = anchors.Count(anchor =>
                {
                    var nearest = band.Points.OrderBy(p => Math.Abs(p.X - anchor.X)).FirstOrDefault();
                    return nearest != null && Math.Abs(nearest.X - anchor.X) <= xTolerance;
                });

                if (hits > bestHits)
                {
                    bestHits = hits;
                    bestBand = band;
                }
            }

            // One or two accidental Ø1000 labels must not define a project-wide DN row.
            if (bestBand == null || bestHits < Math.Max(3, Math.Min(6, anchors.Count / 3)))
            {
                debug.AppendLine($"4.1 profile DN row: no repeated manhole-DN band (bestHits={bestHits}).");
                return result;
            }

            foreach (var anchor in anchors)
            {
                var nearest = bestBand.Points.OrderBy(p => Math.Abs(p.X - anchor.X)).FirstOrDefault();
                if (nearest == null || Math.Abs(nearest.X - anchor.X) > xTolerance)
                    continue;
                result[anchor.Identifier] = (int)Math.Round(nearest.Value);
            }

            debug.AppendLine($"4.1 profile DN row: Y={bestBand.Y:0.0}, hits={bestHits}/{anchors.Count}, assignments={result.Count}.");
            return result;
        }

        private static List<ProfileNumericBand> ClusterProfileBands(
            IReadOnlyList<ProfileNumericPoint> points,
            double tolerance)
        {
            var bands = new List<ProfileNumericBand>();
            foreach (var point in points.OrderBy(p => p.Y))
            {
                var band = bands
                    .Where(b => Math.Abs(b.Y - point.Y) <= tolerance)
                    .OrderBy(b => Math.Abs(b.Y - point.Y))
                    .FirstOrDefault();

                if (band == null)
                {
                    band = new ProfileNumericBand { Y = point.Y };
                    bands.Add(band);
                }

                band.Points.Add(point);
                band.Y = band.Points.Average(p => p.Y);
            }
            return bands;
        }

        private static (double Ground, double Invert)? FindSpatialElevationPair(
            SpatialManholeAnchor anchor,
            IReadOnlyList<TextItem> localItems,
            string drawingType)
        {
            var candidates = new List<ElevationCandidate>();
            foreach (var item in localItems)
            {
                var raw = (item.Text ?? string.Empty).Trim();
                if (!TryParseElevation(raw, out var value))
                    continue;

                var cx = item.X + item.Width / 2.0;
                var cy = item.Y - item.Height / 2.0;
                var dx = cx - anchor.X;
                var dy = cy - anchor.Y;

                if (string.Equals(drawingType, "PROFIL", StringComparison.OrdinalIgnoreCase))
                {
                    // Profile tables place ground/invert values in the same vertical column as the manhole.
                    if (Math.Abs(dx) > 18 || Math.Abs(dy) > 105)
                        continue;
                }
                else
                {
                    // PZT labels usually keep the two elevations next to the D/WP label.
                    if (Math.Abs(dx) > 42 || Math.Abs(dy) > 55)
                        continue;
                }

                candidates.Add(new ElevationCandidate
                {
                    Value = value,
                    Dx = dx,
                    Dy = dy,
                    Distance = Math.Sqrt(dx * dx + dy * dy)
                });
            }

            var nearest = candidates
                .OrderBy(c => c.Distance)
                .ThenBy(c => Math.Abs(c.Dx))
                .Take(4)
                .ToList();

            if (nearest.Count < 2)
                return null;

            (ElevationCandidate A, ElevationCandidate B, double Score)? best = null;
            for (var i = 0; i < nearest.Count; i++)
            {
                for (var j = i + 1; j < nearest.Count; j++)
                {
                    var diff = Math.Abs(nearest[i].Value - nearest[j].Value);
                    if (diff < 0.20 || diff > 15.0)
                        continue;

                    var alignmentPenalty = string.Equals(drawingType, "PROFIL", StringComparison.OrdinalIgnoreCase)
                        ? Math.Abs(nearest[i].Dx - nearest[j].Dx) * 3.0
                        // PZT labels may put the two elevations vertically OR horizontally.
                        // Reward whichever alignment is stronger instead of assuming one layout.
                        : Math.Min(Math.Abs(nearest[i].Dx - nearest[j].Dx), Math.Abs(nearest[i].Dy - nearest[j].Dy)) * 2.0;
                    var score = nearest[i].Distance + nearest[j].Distance + alignmentPenalty;

                    if (!best.HasValue || score < best.Value.Score)
                        best = (nearest[i], nearest[j], score);
                }
            }

            if (!best.HasValue)
                return null;

            var ground = Math.Max(best.Value.A.Value, best.Value.B.Value);
            var invert = Math.Min(best.Value.A.Value, best.Value.B.Value);
            return (ground, invert);
        }

        private static string DetermineConfidence(ParsedManhole manhole)
        {
            var evidence = 0;
            if (manhole.DiameterMm.HasValue) evidence += 2;
            if (manhole.GroundElevationM.HasValue && manhole.InvertElevationM.HasValue && manhole.HeightM.HasValue) evidence += 3;
            else if (manhole.HeightM.HasValue) evidence++;
            if (!string.IsNullOrWhiteSpace(manhole.Type)) evidence += 2;
            if (!string.IsNullOrWhiteSpace(manhole.Crown)) evidence++;
            if (manhole.Transitions.Count > 0) evidence += 2;

            return evidence >= 5 ? "wysoka" : evidence >= 2 ? "średnia" : "niska";
        }

        private static string? NormalizeParsedIdentifier(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return identifier;
            if (string.Equals(identifier, "SO", StringComparison.OrdinalIgnoreCase)) return "SO";
            var m = Regex.Match(identifier.Trim(), @"^(?<p>KD|KS|D|S)(?<n>\d{1,3}(?:[./-]\d+)*)$", RegexOptions.IgnoreCase);
            if (!m.Success) return identifier.Trim().ToUpperInvariant();
            return m.Groups["p"].Value.ToUpperInvariant() + NormalizeIdentifierNumber(m.Groups["n"].Value);
        }

        private static bool IsUnsupportedOcrIdentifier(ParsedManhole mh, IReadOnlyList<PageText> pages)
        {
            if (string.IsNullOrWhiteSpace(mh.Identifier) || string.Equals(mh.Identifier, "SO", StringComparison.OrdinalIgnoreCase))
                return false;
            var page = pages.FirstOrDefault(p => p.PageNumber == mh.Page);
            var isOcr = (page?.ExtractionEngine ?? string.Empty).StartsWith("OCR/", StringComparison.OrdinalIgnoreCase);
            if (!isOcr) return false;
            var m = Regex.Match(mh.Identifier, @"^(?:D|S)(?<n>\d+)$", RegexOptions.IgnoreCase);
            if (!m.Success || !int.TryParse(m.Groups["n"].Value, out var n) || n < 80) return false;
            var hasEvidence = mh.DiameterMm.HasValue || mh.GroundElevationM.HasValue || mh.InvertElevationM.HasValue ||
                              mh.HeightM.HasValue || !string.IsNullOrWhiteSpace(mh.Type) || !string.IsNullOrWhiteSpace(mh.Crown) || mh.Transitions.Count > 0;
            return !hasEvidence;
        }

        private static int ConfidenceRank(string? confidence)
        {
            return confidence?.ToLowerInvariant() switch
            {
                "wysoka" => 3,
                "średnia" => 2,
                _ => 1
            };
        }

        private static string NormalizeManholeType(string value)
        {
            var lower = value.ToLowerInvariant();
            if (lower.Contains("kinet")) return "kinetowa";
            if (lower.Contains("osadnik")) return "osadnikowa";
            if (lower.Contains("rozpr")) return "rozprężna";
            if (lower.Contains("czyszcz")) return "czyszczakowa";
            if (lower.Contains("tłocz")) return "tłoczna";
            return value.Trim();
        }

        private static string NormalizeMaterial(string value)
        {
            var upper = value.Trim().ToUpperInvariant();
            return upper switch
            {
                "PEHD" => "PE-HD",
                "HDPE" => "PE-HD",
                _ => upper
            };
        }

        private static int? FindLikelyManholeDiameter(string context)
        {
            // Strongest evidence: diameter written close to the word "studnia".
            var direct = Regex.Match(
                context,
                @"\b(?:studnia|studzienka)\b.{0,80}?(?:DN|D|Ø|ø)\s*(?<d>\d{3,4})\b",
                RegexOptions.IgnoreCase);

            if (direct.Success && int.TryParse(direct.Groups["d"].Value, out var directDiameter) && IsPlausibleManholeDiameter(directDiameter))
                return directDiameter;

            // Otherwise prefer standard prefabricated manhole diameters and ignore
            // odd pipe ODs such as 1013 mm that caused the v0.4 false result.
            var candidates = Regex.Matches(context, @"\b(?:DN|D|Ø|ø)\s*(?<d>\d{3,4})\b", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(m => int.TryParse(m.Groups["d"].Value, out var d) ? d : 0)
                .Where(IsPlausibleManholeDiameter)
                .ToList();

            if (candidates.Count == 0)
                return null;

            var preferred = new[] { 1000, 1200, 1500, 2000, 2500, 3000, 800 };
            foreach (var standard in preferred)
            {
                if (candidates.Contains(standard))
                    return standard;
            }

            return candidates.OrderByDescending(x => x).First();
        }

        private static bool IsPlausibleManholeDiameter(int diameter)
        {
            return diameter is >= 800 and <= 4000 && diameter % 50 == 0;
        }

        private static string BuildManholeIdentifier(Match match)
        {
            if (!match.Success)
                return string.Empty;

            if (match.Groups["special"].Success)
                return match.Groups["special"].Value.Trim().ToUpperInvariant();

            var token = match.Groups["token"].Value.Trim().ToUpperInvariant();
            var number = NormalizeIdentifierNumber(match.Groups["number"].Value.Trim());
            return string.IsNullOrWhiteSpace(number) ? token : token + number;
        }

        private static string NormalizeExtractedText(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var s = input;

            // Zamiana nowych linii na spacje
            s = s.Replace('\r', ' ')
                 .Replace('\n', ' ');

            // np. 0,0Studnia -> 0,0 Studnia
            s = Regex.Replace(
                s,
                @"(?<=\d[0-9,.])(?=[A-Za-zĄąĆćĘęŁłŃńÓóŚśŹźŻżÅÄÖØåäöø])",
                " ");

            // np. 1200Studnia -> 1200 Studnia
            s = Regex.Replace(
                s,
                @"(?<=\d)(?=[A-Za-zĄąĆćĘęŁłŃńÓóŚśŹźŻżÅÄÖØåäöø])",
                " ");

            // np. PVC200 -> PVC 200
            s = Regex.Replace(
                s,
                @"(?<=[A-Za-zĄąĆćĘęŁłŃńÓóŚśŹźŻżÅÄÖØåäöø])(?=\d)",
                " ");

            // Dodanie spacji przed typowymi tokenami
            s = Regex.Replace(
                s,
                @"(?<!\s)(?=\b(PE-HD|PEHD|HDPE|PVC|PP|PE|Studnia|Studzienka|Separator|Osadnik|Wp|KD|KS)\b)",
                " ",
                RegexOptions.IgnoreCase);

            // Typowe pomyłki OCR w oznaczeniach rur technicznych.
            s = Regex.Replace(s, @"\bP[¥YV]C(?=\s*\d)", "PVC", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bPVCI(?=\s*\d)", "PVC", RegexOptions.IgnoreCase);

            // Wielokrotne białe znaki -> jedna spacja
            s = Regex.Replace(
                s,
                @"[\u00A0\s]+",
                " ");

            // studniaø1200 -> studnia ø1200
            s = Regex.Replace(
                s,
                @"(?<=[A-Za-z])(?=[Øø])",
                " ");

            return s.Trim();
        }

        private static void ParseManholes(
            string line,
            int pageNumber,
            ParsedProject result,
            StringBuilder debug)
        {
            foreach (Match m in ManholeRegex.Matches(line))
            {
                var id = BuildManholeIdentifier(m);
                if (!IsLikelySpatialManholeIdentifier(id))
                    continue;

                // broader dedupe: avoid adding if an existing manhole on same page has same identifier
                // or its RawText already contains the identifier
                var candidateId = id?.Trim() ?? string.Empty;
                var exists = result.Manholes.Any(h =>
                    h.Page == pageNumber &&
                    !string.IsNullOrWhiteSpace(h.Identifier) &&
                    !string.IsNullOrWhiteSpace(candidateId) &&
                    string.Equals(h.Identifier.Trim(), candidateId, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    result.Manholes.Add(
                        new ParsedManhole
                        {
                            Page = pageNumber,
                            RawText = m.Value,
                            Identifier = id
                        });

                    // diagnostics
                    Console.WriteLine($"[Parser] Added manhole token: Page={pageNumber} Id={id} Raw='{m.Value}'");
                    debug.AppendLine($"Manhole token matched: {m.Value}");
                }
            }
        }

        private static void ParseInlets(
            string line,
            int pageNumber,
            ParsedProject result,
            StringBuilder debug)
        {
            foreach (Match iw in InletRegex.Matches(line))
            {
                var id = "WP" + iw.Groups["id"].Value;

                if (!result.Inlets.Any(i =>
                    i.Page == pageNumber &&
                    string.Equals(
                        i.Identifier,
                        id,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    result.Inlets.Add(
                        new ParsedInlet
                        {
                            Page = pageNumber,
                            RawText = iw.Value,
                            Identifier = id,
                            Confidence = "średnia"
                        });

                    debug.AppendLine(
                        $"Inlet token matched: {iw.Value}");
                }
            }
        }

        private static void ParsePipesByDiameter(
            string line,
            int pageNumber,
            ParsedProject result,
            StringBuilder debug)
        {
            foreach (Match dn in DnRegex.Matches(line))
            {
                var parsed = new ParsedPipe
                {
                    Page = pageNumber,
                    RawText = dn.Value
                };

                if (int.TryParse(
                    dn.Groups["value"].Value,
                    out var value))
                {
                    var token = dn.Groups["token"].Value;
                    // OCR frequently reads a manhole label "D 12" as a pipe diameter.
                    // Keep Dxxx as a diameter only in the realistic pipe range; DNxxx remains explicit.
                    if (string.Equals(token, "D", StringComparison.OrdinalIgnoreCase) && value < 100)
                        continue;
                    parsed.DiameterMm = value;
                }

                var mat = MaterialRegex.Match(line);

                if (mat.Success)
                    parsed.Material = mat.Groups["mat"].Value;

                if (!result.Pipes.Any(p =>
                    p.Page == pageNumber &&
                    p.DiameterMm == parsed.DiameterMm &&
                    string.Equals(
                        p.Material ?? string.Empty,
                        parsed.Material ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    result.Pipes.Add(parsed);

                    debug.AppendLine(
                        $"DN pipe matched: {dn.Value} mat={parsed.Material}");
                }
            }
        }

        private static void ParseMaterialDiameterPipes(
            string line,
            int pageNumber,
            ParsedProject result,
            StringBuilder debug)
        {
            foreach (Match m in PipeMaterialDiameterRegex.Matches(line))
            {
                var mat = m.Groups["mat"].Value;

                var diam = 0;

                if (m.Groups["diam"].Success)
                    int.TryParse(
                        m.Groups["diam"].Value,
                        out diam);

                if (!result.Pipes.Any(p =>
                    p.Page == pageNumber &&
                    p.DiameterMm == diam &&
                    string.Equals(
                        p.Material ?? string.Empty,
                        mat ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    var parsed = new ParsedPipe
                    {
                        Page = pageNumber,
                        RawText = m.Value,
                        Material = mat
                    };

                    if (diam > 0)
                        parsed.DiameterMm = diam;

                    result.Pipes.Add(parsed);

                    debug.AppendLine(
                        $"Material+diam pipe matched: {m.Value}");
                }
            }
        }

        private static void ParseMaterialOnlyPipes(
            string line,
            int pageNumber,
            ParsedProject result,
            StringBuilder debug)
        {
            foreach (Match mat in MaterialRegex.Matches(line))
            {
                var matName = mat.Groups["mat"].Value ?? string.Empty;
                var rawText = mat.Value ?? string.Empty;

                // If there's already a pipe on this page with the same material and a known diameter, skip adding material-only entry.
                var hasMaterialWithDiameter = result.Pipes.Any(p =>
                    p.Page == pageNumber &&
                    string.Equals(p.Material ?? string.Empty, matName, StringComparison.OrdinalIgnoreCase) &&
                    p.DiameterMm > 0);

                // Preserve existing dedupe by RawText: don't add if an identical RawText+Material exists.
                var hasSameRaw = result.Pipes.Any(p =>
                    p.Page == pageNumber &&
                    string.Equals(p.Material ?? string.Empty, matName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.RawText ?? string.Empty, rawText, StringComparison.OrdinalIgnoreCase));

                if (!hasSameRaw && !hasMaterialWithDiameter)
                {
                    result.Pipes.Add(
                        new ParsedPipe
                        {
                            Page = pageNumber,
                            RawText = rawText,
                            Material = matName
                        });

                    debug.AppendLine(
                        $"Material-only pipe matched: {rawText}");
                }
            }
        }

        private static void ParseLocalPipes(
            string line,
            int pageNumber,
            ParsedProject result,
            StringBuilder debug)
        {
            foreach (Match s in PeSeriesRegex.Matches(line))
            {
                var mat = s.Groups["mat"].Value;
                var raw = s.Value;

                if (!result.Pipes.Any(p =>
                    p.Page == pageNumber &&
                    string.Equals(
                        p.RawText ?? string.Empty,
                        raw ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    var parsed = new ParsedPipe
                    {
                        Page = pageNumber,
                        RawText = raw,
                        Material = mat
                    };

                    result.Pipes.Add(parsed);

                    debug.AppendLine(
                        $"PE series matched: {raw}");
                }
            }

            foreach (Match m in LocalPipeCompactRegex.Matches(line))
            {
                var mat = m.Groups["mat"].Value;

                var diam = 0;

                if (m.Groups["diam"].Success)
                {
                    int.TryParse(
                        m.Groups["diam"].Value,
                        out diam);
                }

                var raw = m.Value;

                if (diam <= 0)
                    continue;

                // liczby poniżej 20 mm ignorujemy
                if (diam < 20)
                    continue;

                if (!result.Pipes.Any(p =>
                    p.Page == pageNumber &&
                    p.DiameterMm == diam &&
                    string.Equals(
                        p.Material ?? string.Empty,
                        mat ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        p.RawText ?? string.Empty,
                        raw ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    var parsed = new ParsedPipe
                    {
                        Page = pageNumber,
                        RawText = raw,
                        Material = mat,
                        DiameterMm = diam
                    };

                    result.Pipes.Add(parsed);

                    debug.AppendLine(
                        $"Local pipe matched: {raw} mat={mat} diam={diam}");
                }
            }
        }

        private static void ParseDescriptiveManholes(
            string line,
            int pageNumber,
            ParsedProject result,
            StringBuilder debug)
        {
            var manholeTypeRegex = new Regex(
                @"\b(kinetow(?:a|e|y)?|osadnik(?:ow(?:a|e|y)?)?|rozpr[eę]żn(?:a|e|y)?|czyszczak(?:ow(?:a|e|y)?)?|tłocz(?:ny|na|ne)?)\b",
                RegexOptions.IgnoreCase);

            // Obsługuje m.in.:
            // wys. całk. 4,5 m
            // wys całk 4.5 m
            // wysokość całkowita 4,5 m
            // wysokość 4,5 m
            // H=4,5 m
            var heightRegex = new Regex(
                @"\b(?:wys(?:okość)?\.?(?:\s*całk(?:owita)?\.?)?|H)\s*[:=]?\s*(?<h>\d{1,2}(?:[,.]\d{1,3})?)\s*m\b",
                RegexOptions.IgnoreCase);

            var crownRegex = new Regex(
                @"\b(właz\s+[^\s,;]+(?:\s+[^\s,;]+)?|pokrycie\s+[^\s,;]+(?:\s+[^\s,;]+)?|zwieńczenie\s+[^\s,;]+(?:\s+[^\s,;]+)?|klasa\s+[A-Za-z0-9]+)\b",
                RegexOptions.IgnoreCase);

            var transitionLocalRegex = new Regex(
                @"\b(?<mat>PE-HD|PEHD|HDPE|PVC|PP|PE|Żelbet|Beton)\b[^\dA-Za-z]{0,6}?(?:DN|Ø|ø)?\s*(?<diam>\d{2,4})\b",
                RegexOptions.IgnoreCase);

            foreach (Match s in StudniaRegex.Matches(line))
            {
                var rawMatch =
                    s.Value?.Trim() ??
                    string.Empty;

                // Średnica studni
                int diam = 0;

                var diamMatch = Regex.Match(
                    rawMatch,
                    @"\b(?:DN|Ø|ø)?\s*(\d{3,4})\b",
                    RegexOptions.IgnoreCase);

                if (diamMatch.Success)
                {
                    int.TryParse(
                        diamMatch.Groups[1].Value,
                        out diam);
                }
                else if (s.Groups["diam"].Success)
                {
                    int.TryParse(
                        s.Groups["diam"].Value,
                        out diam);
                }

                // Typ studni
                var typeMatch =
                    manholeTypeRegex.Match(line);

                var type =
                    typeMatch.Success
                        ? typeMatch.Value.Trim()
                        : "BRAK DANYCH";

                // Wysokość
                var heightMatch =
                    heightRegex.Match(line);

                var heightStr =
                    "BRAK DANYCH";

                if (heightMatch.Success &&
                    heightMatch.Groups["h"].Success)
                {
                    heightStr =
                        heightMatch.Groups["h"].Value +
                        " m";
                }

                // Zwieńczenie
                var crownMatch =
                    crownRegex.Match(line);

                var crown =
                    crownMatch.Success
                        ? crownMatch.Value.Trim()
                        : "BRAK DANYCH";

                // Przejścia szczelne / materiał + średnica
                var transitions =
                    new Dictionary<string, int>(
                        StringComparer.OrdinalIgnoreCase);

                foreach (Match t in transitionLocalRegex.Matches(line))
                {
                    var mat =
                        t.Groups["mat"]
                         .Value
                         .ToUpperInvariant();

                    if (!int.TryParse(
                        t.Groups["diam"].Value,
                        out var d))
                    {
                        continue;
                    }

                    if (d < 20)
                        continue;

                    var key =
                        $"{mat} DN{d}";

                    if (transitions.ContainsKey(key))
                        transitions[key]++;
                    else
                        transitions[key] = 1;
                }

                var transitionsList =
                    transitions
                        .Select(kv =>
                            $"{kv.Key} × {kv.Value}")
                        .ToArray();

                var transitionsStr =
                    transitionsList.Length > 0
                        ? string.Join(
                            " | ",
                            transitionsList)
                        : "BRAK DANYCH";

                // Identyfikator
                var id = rawMatch;

                var manholeIdMatch =
                    ManholeRegex.Match(line);

                if (manholeIdMatch.Success)
                {
                    id = BuildManholeIdentifier(manholeIdMatch);
                }

                var diameterStr =
                    diam > 0
                        ? $"DN{diam}"
                        : "BRAK DANYCH";

                var composed =
                    $"Name:{id};" +
                    $"Type:{type};" +
                    $"Diameter:{diameterStr};" +
                    $"Height:{heightStr};" +
                    $"Crown:{crown};" +
                    $"Transitions:{transitionsStr};" +
                    $"RawDesc:{rawMatch}";
                // Try to find existing manhole by identifier on same page
                ParsedManhole existing = null;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    existing = result.Manholes.FirstOrDefault(h =>
                        h.Page == pageNumber &&
                        !string.IsNullOrWhiteSpace(h.Identifier) &&
                        string.Equals(h.Identifier, id, StringComparison.OrdinalIgnoreCase));
                }

                if (existing != null)
                {
                    // update structured fields only when richer info is available
                    if (string.IsNullOrWhiteSpace(existing.Type))
                    {
                        existing.Type = !string.Equals(type, "BRAK DANYCH", StringComparison.OrdinalIgnoreCase) ? type : existing.Type;
                    }

                    if (!existing.DiameterMm.HasValue && diam > 0)
                        existing.DiameterMm = diam;

                    if (!existing.HeightM.HasValue && heightMatch.Success && heightMatch.Groups["h"]?.Success == true)
                    {
                        var htxt = heightMatch.Groups["h"].Value.Replace(',', '.');
                        if (double.TryParse(htxt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hv))
                        {
                            existing.HeightM = hv;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(existing.Crown) && !string.Equals(crown, "BRAK DANYCH", StringComparison.OrdinalIgnoreCase))
                        existing.Crown = crown;

                    // merge transitions counts into existing.Transitions
                    foreach (var kv in transitions)
                    {
                        var parts = kv.Key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var matPart = parts.Length > 0 ? parts[0] : string.Empty;
                        int d = 0;
                        if (parts.Length > 1 && parts[1].StartsWith("DN", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(parts[1].Substring(2), out d);
                        }

                        var found = existing.Transitions.FirstOrDefault(t =>
                            string.Equals(t.Material, matPart, StringComparison.OrdinalIgnoreCase) &&
                            t.DiameterMm == d);
                        if (found != null)
                        {
                            found.Quantity += kv.Value;
                        }
                        else
                        {
                            existing.Transitions.Add(new ManholeTransition { Material = matPart, DiameterMm = d, Quantity = kv.Value });
                        }
                    }

                    debug.AppendLine($"Descriptive manhole updated: {rawMatch} -> merged into existing id={id}");
                }
                else
                {
                    var parsed = new ParsedManhole
                    {
                        Page = pageNumber,
                        RawText = composed,
                        Identifier = id
                    };

                    // populate new structured fields when available
                    parsed.Type = !string.Equals(type, "BRAK DANYCH", StringComparison.OrdinalIgnoreCase) ? type : null;

                    if (diam > 0)
                        parsed.DiameterMm = diam;

                    // parse height value (convert comma to dot) and set HeightM
                    if (heightMatch.Success && heightMatch.Groups["h"]?.Success == true)
                    {
                        var htxt = heightMatch.Groups["h"].Value.Replace(',', '.');
                        if (double.TryParse(htxt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hv))
                        {
                            parsed.HeightM = hv;
                        }
                    }

                    parsed.Crown = !string.Equals(crown, "BRAK DANYCH", StringComparison.OrdinalIgnoreCase) ? crown : null;

                    // fill transitions list (add to new parsed)
                    foreach (var kv in transitions)
                    {
                        var parts = kv.Key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var matPart = parts.Length > 0 ? parts[0] : string.Empty;
                        int d = 0;
                        if (parts.Length > 1 && parts[1].StartsWith("DN", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(parts[1].Substring(2), out d);
                        }
                        parsed.Transitions.Add(new ManholeTransition { Material = matPart, DiameterMm = d, Quantity = kv.Value });
                    }

                    result.Manholes.Add(parsed);

                    debug.AppendLine(
                        $"Descriptive manhole matched: {rawMatch} -> {composed}");
                }
            }
        }

        private static void ParseSeparators(
            string line,
            int pageNumber,
            ParsedProject result,
            StringBuilder debug)
        {
            foreach (Match s in SeparatorRegex.Matches(line))
            {
                var diam = 0;

                if (s.Groups["diam"].Success)
                {
                    int.TryParse(
                        s.Groups["diam"].Value,
                        out diam);
                }

                var raw = s.Value;

                if (!result.Pipes.Any(p =>
                    p.Page == pageNumber &&
                    string.Equals(
                        p.RawText ?? string.Empty,
                        raw ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    var parsed = new ParsedPipe
                    {
                        Page = pageNumber,
                        RawText = raw
                    };

                    if (diam > 0)
                        parsed.DiameterMm = diam;

                    result.Pipes.Add(parsed);

                    debug.AppendLine(
                        $"Separator matched: {s.Value}");
                }
            }
        }
    }
}
