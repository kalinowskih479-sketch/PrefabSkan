from pathlib import Path

path = Path('src/SewerScan.Infrastructure/Parsers/SewerProjectParser.cs')
text = path.read_text(encoding='utf-8-sig')

old_call = 'anchors = SelectProfileTableAnchors(anchors, usable, debug);'
new_call = 'anchors = SelectProfileTableAnchors(anchors, usable, page.Text ?? string.Empty, debug);'
if old_call not in text:
    raise SystemExit('SelectProfileTableAnchors call not found')
text = text.replace(old_call, new_call, 1)

old_sig = '''        private static List<SpatialManholeAnchor> SelectProfileTableAnchors(\n            IReadOnlyList<SpatialManholeAnchor> candidates,\n            IReadOnlyList<TextItem> items,\n            StringBuilder debug)'''
new_sig = '''        private static List<SpatialManholeAnchor> SelectProfileTableAnchors(\n            IReadOnlyList<SpatialManholeAnchor> candidates,\n            IReadOnlyList<TextItem> items,\n            string pageText,\n            StringBuilder debug)'''
if old_sig not in text:
    raise SystemExit('SelectProfileTableAnchors signature not found')
text = text.replace(old_sig, new_sig, 1)

old_before_best = '''            // 4.2.3: the densest OCR band is not necessarily the profile node row.\n            // Batorego produced a denser garbage band (S79, S13, S06/09, S61.16...)\n            // than the actual structure row. Score each band by engineering-table support:\n            // repeated elevation values and standard manhole DN values in the same X columns.\n            var best = bands'''
new_before_best = '''            // 4.2.4: use the network family as engineering evidence too. On Polish sewer\n            // drawings the deszczowa profile is normally D/KD and sanitarna is S/KS.\n            // OCR may still hallucinate a numerically stronger band from the opposite network.\n            var preferredFamily = Regex.IsMatch(pageText ?? string.Empty, @"KANALIZACJI\\s+DESZCZ|DESZCZOW", RegexOptions.IgnoreCase)\n                ? "D"\n                : Regex.IsMatch(pageText ?? string.Empty, @"KANALIZACJI\\s+SANITAR|SANITARN", RegexOptions.IgnoreCase)\n                    ? "S"\n                    : string.Empty;\n\n            // 4.2.3: the densest OCR band is not necessarily the profile node row.\n            // Batorego produced a denser garbage band (S79, S13, S06/09, S61.16...)\n            // than the actual structure row. Score each band by engineering-table support:\n            // repeated elevation values and standard manhole DN values in the same X columns.\n            var best = bands'''
if old_before_best not in text:
    raise SystemExit('profile score preamble not found')
text = text.replace(old_before_best, new_before_best, 1)

old_score = '''                    var score = engineeringColumns * 24.0 + dnColumns * 14.0 + distinct * 2.0 - syntaxPenalty;\n                    return new { Band = b, Distinct = distinct, Width = width, EngineeringColumns = engineeringColumns, DnColumns = dnColumns, Score = score };'''
new_score = '''                    var uniqueAnchors = b.GroupBy(x => x.Identifier, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();\n                    var familyMatches = string.IsNullOrWhiteSpace(preferredFamily)\n                        ? 0\n                        : uniqueAnchors.Count(a => preferredFamily == "D"\n                            ? Regex.IsMatch(a.Identifier, @"^(?:D|KD)\\d", RegexOptions.IgnoreCase)\n                            : Regex.IsMatch(a.Identifier, @"^(?:S|KS)\\d", RegexOptions.IgnoreCase));\n                    var familyMismatches = string.IsNullOrWhiteSpace(preferredFamily)\n                        ? 0\n                        : uniqueAnchors.Count - familyMatches;\n\n                    var score = engineeringColumns * 24.0 + dnColumns * 14.0 + distinct * 2.0 - syntaxPenalty\n                                + familyMatches * 28.0 - familyMismatches * 18.0;\n                    return new { Band = b, Distinct = distinct, Width = width, EngineeringColumns = engineeringColumns, DnColumns = dnColumns, FamilyMatches = familyMatches, FamilyMismatches = familyMismatches, Score = score };'''
if old_score not in text:
    raise SystemExit('profile score expression not found')
text = text.replace(old_score, new_score, 1)

old_debug = 'debug.AppendLine($"4.2.3 profile node-band score: score={best.Score:0.0}, engineering={best.EngineeringColumns}, dn={best.DnColumns}, ids={best.Distinct}.");'
new_debug = 'debug.AppendLine($"4.2.4 profile node-band score: score={best.Score:0.0}, family={preferredFamily}, familyMatch={best.FamilyMatches}, familyMismatch={best.FamilyMismatches}, engineering={best.EngineeringColumns}, dn={best.DnColumns}, ids={best.Distinct}.");'
if old_debug not in text:
    raise SystemExit('profile score debug line not found')
text = text.replace(old_debug, new_debug, 1)

path.write_text(text, encoding='utf-8')
print('Patched SewerProjectParser.cs for 4.2.4 family-aware profile selection')
