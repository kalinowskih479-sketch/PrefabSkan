from pathlib import Path


def replace_once(text: str, old: str, new: str) -> str:
    if old in text:
        return text.replace(old, new, 1)
    return text


parser = Path("src/SewerScan.Infrastructure/Parsers/SewerProjectParser.cs")
s = parser.read_text(encoding="utf-8-sig")

# Batch 1: nullable merges, OCR fallback counting and local profile context.
replacements = [
    (
        "if (match.DiameterMm == 0 && mh.DiameterMm > 0)\n                        match.DiameterMm = mh.DiameterMm;",
        "if (!match.DiameterMm.HasValue && mh.DiameterMm.HasValue && mh.DiameterMm.Value > 0)\n                        match.DiameterMm = mh.DiameterMm;",
    ),
    (
        "if (match.HeightM == 0 && mh.HeightM > 0)\n                        match.HeightM = mh.HeightM;",
        "if (!match.HeightM.HasValue && mh.HeightM.HasValue && mh.HeightM.Value > 0)\n                        match.HeightM = mh.HeightM;",
    ),
    (
        "if (existing.DiameterMm == 0 && diam > 0)\n                        existing.DiameterMm = diam;",
        "if (!existing.DiameterMm.HasValue && diam > 0)\n                        existing.DiameterMm = diam;",
    ),
    (
        'if (existing.HeightM == 0 && heightMatch.Success && heightMatch.Groups["h"]?.Success == true)',
        'if (!existing.HeightM.HasValue && heightMatch.Success && heightMatch.Groups["h"]?.Success == true)',
    ),
]
for old, new in replacements:
    s = replace_once(s, old, new)

old = """                var repeatedOcrId = identifierOccurrences.TryGetValue(id, out var occurrenceCount) && occurrenceCount >= 2;
                if (isOcr && !directElevations.ContainsKey(id) && !repeatedOcrId)
                {
                    debug.AppendLine($\"OCR text fallback rejected {id}: no spatial anchor/elevation pair and occurrences={occurrenceCount}.\");
                    continue;
                }"""
new = """                var acceptedSpatialAnchor = spatialAnchors.Any(a =>
                    string.Equals(a.Identifier, id, StringComparison.OrdinalIgnoreCase));
                var repeatedTextId = identifierOccurrences.TryGetValue(id, out var occurrenceCount) && occurrenceCount >= 2;
                if (isOcr && !directElevations.ContainsKey(id) && !acceptedSpatialAnchor && !repeatedTextId)
                {
                    debug.AppendLine($\"OCR text fallback rejected {id}: no accepted spatial anchor/elevation pair and occurrences={occurrenceCount}.\");
                    continue;
                }"""
s = replace_once(s, old, new)

old = """                foreach (var a in FindSpatialManholeAnchors(page.Items, result.DrawingType))
                {
                    identifiers.Add(a.Identifier);
                    CountIdentifier(a.Identifier);
                }"""
new = """                foreach (var a in FindSpatialManholeAnchors(page.Items, result.DrawingType))
                    identifiers.Add(a.Identifier);"""
s = replace_once(s, old, new)

old = """                var left = index <= 0
                    ? anchor.X - Math.Max(45, pageWidth * 0.04)
                    : (ordered[index - 1].X + anchor.X) / 2.0;
                var right = index < 0 || index == ordered.Count - 1
                    ? anchor.X + Math.Max(45, pageWidth * 0.04)
                    : (anchor.X + ordered[index + 1].X) / 2.0;"""
new = """                var edgeHalfWidth = ordered.Count == 1
                    ? Math.Max(140, pageWidth * 0.65)
                    : Math.Max(45, pageWidth * 0.04);
                var left = index <= 0
                    ? anchor.X - edgeHalfWidth
                    : (ordered[index - 1].X + anchor.X) / 2.0;
                var right = index < 0 || index == ordered.Count - 1
                    ? anchor.X + edgeHalfWidth
                    : (anchor.X + ordered[index + 1].X) / 2.0;"""
s = replace_once(s, old, new)

# Batch 2: an OCR page with real word geometry but no accepted structure anchor must not
# resurrect D60/D00-like text fragments through the legacy text-only parser.
old = """                var spatialManholesParsed = ParseSpatialManholes(page, result, debug);
                var spatialInletsParsed = ParseSpatialInlets(page, result, debug);

                foreach (var rawLine in lines)"""
new = """                var spatialManholesParsed = ParseSpatialManholes(page, result, debug);
                var spatialInletsParsed = ParseSpatialInlets(page, result, debug);
                var isOcrPage = (page.ExtractionEngine ?? string.Empty).StartsWith(\"OCR/\", StringComparison.OrdinalIgnoreCase);
                var hasUsableSpatialWords = page.Items != null && page.Items.Any(i =>
                    !string.IsNullOrWhiteSpace(i.Text) &&
                    (Math.Abs(i.X) > 0.001 || Math.Abs(i.Y) > 0.001 || i.Width > 0.001 || i.Height > 0.001));

                foreach (var rawLine in lines)"""
s = replace_once(s, old, new)

old = """                    if (!spatialManholesParsed)
                    {
                        ParseManholes(line, page.PageNumber, result, debug);
                        ParseDescriptiveManholes(
                            line,
                            page.PageNumber,
                            result,
                            debug);
                    }"""
new = """                    if (!spatialManholesParsed && !(isOcrPage && hasUsableSpatialWords))
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
                        debug.AppendLine(\"OCR legacy text fallback suppressed: coordinate words exist but no credible manhole anchor was found.\");
                    }"""
s = replace_once(s, old, new)

# Batch 2: duplicated CAD glyphs append reconstructed elevations after the original item stream.
# In that case geometry owns the elevation pair; content-stream order can otherwise assign D7's
# pair to D6/1 simply because the synthetic 126.47 has a later item index.
old = """            var usable = page.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Text))
                .Where(i => Math.Abs(i.X) > 0.001 || Math.Abs(i.Y) > 0.001 || i.Width > 0.001 || i.Height > 0.001)
                .ToList();

            // AutoCAD/plotter PDFs frequently expose duplicated glyphs (e.g. DD + 66//11)
            // and split elevations (1 + 2 + 6,47).  Add synthetic numeric words without
            // discarding the original geometry so downstream matching can use either form.
            usable.AddRange(BuildSyntheticCadElevationItems(usable));"""
new = """            var usable = page.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Text))
                .Where(i => Math.Abs(i.X) > 0.001 || Math.Abs(i.Y) > 0.001 || i.Width > 0.001 || i.Height > 0.001)
                .ToList();
            var hasDuplicatedCadGlyphs = usable.Any(i =>
                !string.Equals(NormalizeCadDuplicatedGlyphs(i.Text), (i.Text ?? string.Empty).Trim(), StringComparison.Ordinal));

            // AutoCAD/plotter PDFs frequently expose duplicated glyphs (e.g. DD + 66//11)
            // and split elevations (1 + 2 + 6,47).  Add synthetic numeric words without
            // discarding the original geometry so downstream matching can use either form.
            usable.AddRange(BuildSyntheticCadElevationItems(usable));"""
s = replace_once(s, old, new)

old = """                else if (directTextElevations.TryGetValue(anchor.Identifier, out var directPair))
                    assignedElevations = directPair;
                else if (orderedPztElevations.TryGetValue(anchor.Identifier, out var orderedPair))
                    assignedElevations = orderedPair;
                else if (spatialPztElevations.TryGetValue(anchor.Identifier, out var spatialPair))
                    assignedElevations = spatialPair;"""
new = """                else if (directTextElevations.TryGetValue(anchor.Identifier, out var directPair))
                    assignedElevations = directPair;
                else if (pztLike && hasDuplicatedCadGlyphs && spatialPztElevations.TryGetValue(anchor.Identifier, out var cadSpatialPair))
                    assignedElevations = cadSpatialPair;
                else if (orderedPztElevations.TryGetValue(anchor.Identifier, out var orderedPair))
                    assignedElevations = orderedPair;
                else if (spatialPztElevations.TryGetValue(anchor.Identifier, out var spatialPair))
                    assignedElevations = spatialPair;"""
s = replace_once(s, old, new)

parser.write_text(s, encoding="utf-8")

# Keep the corrected regression expectations consistent with the source data.
tests = Path("tests/SewerScan.Application.Tests/SewerProjectParserTests.cs")
t = tests.read_text(encoding="utf-8-sig")
t = replace_once(t, "Assert.Equal(134.15, d61.GroundElevationM);", "Assert.Equal(133.55, d61.GroundElevationM);")
t = replace_once(t, "Assert.Equal(134.15, d7.GroundElevationM);", "Assert.Equal(133.65, d7.GroundElevationM);")
tests.write_text(t, encoding="utf-8")
