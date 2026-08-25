from pathlib import Path

path = Path('src/SewerScan.Infrastructure/Parsers/SewerProjectParser.cs')
text = path.read_text(encoding='utf-8-sig')
old = '''            var best = bands
                .Select(b => new
                {
                    Band = b,
                    Distinct = b.Select(a => a.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Width = b.Count > 1 ? b.Max(a => a.X) - b.Min(a => a.X) : 0.0
                })
                .OrderByDescending(x => x.Distinct)
                .ThenByDescending(x => x.Width)
                .First();
'''
new = '''            // 4.2.3: the densest OCR band is not necessarily the profile node row.
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
                        var n = Regex.Match(a.Identifier, @"^(?:D|S)(?<n>\\d+)$", RegexOptions.IgnoreCase);
                        if (n.Success && int.TryParse(n.Groups["n"].Value, out var number) && number >= 30) syntaxPenalty += 5;
                    }

                    var score = engineeringColumns * 24.0 + dnColumns * 14.0 + distinct * 2.0 - syntaxPenalty;
                    return new { Band = b, Distinct = distinct, Width = width, EngineeringColumns = engineeringColumns, DnColumns = dnColumns, Score = score };
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.EngineeringColumns)
                .ThenByDescending(x => x.DnColumns)
                .ThenByDescending(x => x.Distinct)
                .ThenByDescending(x => x.Width)
                .First();

            debug.AppendLine($"4.2.3 profile node-band score: score={best.Score:0.0}, engineering={best.EngineeringColumns}, dn={best.DnColumns}, ids={best.Distinct}.");
'''
if old not in text:
    if '4.2.3 profile node-band score' in text:
        print('already patched')
        raise SystemExit(0)
    raise SystemExit('target block not found')
path.write_text(text.replace(old, new), encoding='utf-8')
print('patched SewerProjectParser.cs')
