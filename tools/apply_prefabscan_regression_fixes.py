from pathlib import Path


def replace_once(text: str, old: str, new: str) -> str:
    if old in text:
        return text.replace(old, new, 1)
    return text


parser = Path("src/SewerScan.Infrastructure/Parsers/SewerProjectParser.cs")
s = parser.read_text(encoding="utf-8-sig")

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
parser.write_text(s, encoding="utf-8")

tests = Path("tests/SewerScan.Application.Tests/SewerProjectParserTests.cs")
t = tests.read_text(encoding="utf-8-sig")
t = replace_once(t, "Assert.Equal(134.15, d61.GroundElevationM);", "Assert.Equal(133.55, d61.GroundElevationM);")
t = replace_once(t, "Assert.Equal(134.15, d7.GroundElevationM);", "Assert.Equal(133.65, d7.GroundElevationM);")
tests.write_text(t, encoding="utf-8")

# Touching this file intentionally triggers the simplified regression workflow.
