from pathlib import Path

path = Path('src/SewerScan.Infrastructure/Parsers/SewerProjectParser.cs')
text = path.read_text(encoding='utf-8-sig')

old_call = '''            var anchors = FindSpatialManholeAnchors(usable, result.DrawingType);\n\n            if (string.Equals(result.DrawingType, "PROFIL", StringComparison.OrdinalIgnoreCase))\n                anchors = SelectProfileTableAnchors(anchors, usable, debug);'''
new_call = '''            var anchors = FindSpatialManholeAnchors(usable, result.DrawingType);\n\n            if (string.Equals(result.DrawingType, "PROFIL", StringComparison.OrdinalIgnoreCase))\n            {\n                anchors = RecoverProfileNumericOnlyAnchors(anchors, usable, page.Text ?? string.Empty, debug);\n                anchors = SelectProfileTableAnchors(anchors, usable, debug);\n            }'''
if old_call not in text:
    raise SystemExit('profile anchor call block not found')
text = text.replace(old_call, new_call, 1)

marker = '''        private static List<SpatialManholeAnchor> SelectProfileTableAnchors(\n'''
if marker not in text:
    raise SystemExit('SelectProfileTableAnchors marker not found')

method = r'''        private static List<SpatialManholeAnchor> RecoverProfileNumericOnlyAnchors(
            IReadOnlyList<SpatialManholeAnchor> existing,
            IReadOnlyList<TextItem> items,
            string pageText,
            StringBuilder debug)
        {
            var output = existing.ToList();
            var prefix = Regex.IsMatch(pageText ?? string.Empty, @"KANALIZACJI\s+DESZCZ|DESZCZOW", RegexOptions.IgnoreCase)
                ? "D"
                : Regex.IsMatch(pageText ?? string.Empty, @"KANALIZACJI\s+SANITAR|SANITARN", RegexOptions.IgnoreCase)
                    ? "S"
                    : string.Empty;

            if (string.IsNullOrWhiteSpace(prefix))
                return output;

            // Do not invent a second identity row when OCR already recovered a usable family row.
            var familyAnchors = output.Count(a => Regex.IsMatch(a.Identifier, "^" + prefix + @"\d", RegexOptions.IgnoreCase));
            if (familyAnchors >= 3)
                return output;

            var heights = items.Where(i => i.Height > 0).Select(i => i.Height).OrderBy(h => h).ToList();
            var typicalHeight = heights.Count > 0 ? heights[heights.Count / 2] : 10.0;
            var yTolerance = Math.Max(9.0, Math.Min(32.0, typicalHeight * 2.2));

            var bare = items
                .Select((item, index) => new
                {
                    Item = item,
                    Index = index,
                    Token = CleanSpatialToken(item.Text),
                    X = item.X + item.Width / 2.0,
                    Y = item.Y - item.Height / 2.0
                })
                .Where(x => Regex.IsMatch(x.Token, @"^\d{1,2}$"))
                .Select(x => new
                {
                    x.Item,
                    x.Index,
                    x.Token,
                    x.X,
                    x.Y,
                    Number = int.TryParse(x.Token, out var n) ? n : -1
                })
                .Where(x => x.Number >= 1 && x.Number <= 99)
                .ToList();

            if (bare.Count < 3)
                return output;

            var bands = new List<List<dynamic>>();
            foreach (var n in bare.OrderBy(x => x.Y))
            {
                var band = bands
                    .Where(b => Math.Abs(b.Average(x => (double)x.Y) - n.Y) <= yTolerance)
                    .OrderBy(b => Math.Abs(b.Average(x => (double)x.Y) - n.Y))
                    .FirstOrDefault();
                if (band == null)
                {
                    band = new List<dynamic>();
                    bands.Add(band);
                }
                band.Add(n);
            }

            var scored = bands
                .Select(b =>
                {
                    var row = b
                        .GroupBy(x => (int)x.Number)
                        .Select(g => g.OrderBy(x => (double)x.X).First())
                        .OrderBy(x => (double)x.X)
                        .ToList();
                    var distinct = row.Count;
                    var span = distinct > 1 ? (double)row.Last().X - (double)row.First().X : 0.0;
                    var sequentialLinks = 0;
                    for (var i = 1; i < row.Count; i++)
                    {
                        var delta = Math.Abs((int)row[i].Number - (int)row[i - 1].Number);
                        if (delta == 1) sequentialLinks++;
                    }

                    var engineeringColumns = row.Count(x => items.Count(i =>
                    {
                        if (!TryParseElevation(i.Text ?? string.Empty, out _)) return false;
                        var cx = i.X + i.Width / 2.0;
                        var cy = i.Y - i.Height / 2.0;
                        return Math.Abs(cx - (double)x.X) <= 75 && Math.Abs(cy - (double)x.Y) >= 35 && Math.Abs(cy - (double)x.Y) <= 360;
                    }) >= 2);

                    var score = distinct * 5.0 + sequentialLinks * 24.0 + engineeringColumns * 20.0 + Math.Min(20.0, span / 80.0);
                    return new { Row = row, Distinct = distinct, Span = span, SequentialLinks = sequentialLinks, EngineeringColumns = engineeringColumns, Score = score };
                })
                .Where(x => x.Distinct >= 3 && x.SequentialLinks >= 2 && x.EngineeringColumns >= 2 && x.Span >= 120)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (scored == null)
            {
                debug.AppendLine($"4.2.5 profile numeric-ID recovery: no credible {prefix} row.");
                return output;
            }

            var added = 0;
            foreach (var n in scored.Row)
            {
                var identifier = prefix + ((int)n.Number).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (output.Any(a => string.Equals(a.Identifier, identifier, StringComparison.OrdinalIgnoreCase)))
                    continue;

                output.Add(new SpatialManholeAnchor
                {
                    Identifier = identifier,
                    X = (double)n.X,
                    Y = (double)n.Y,
                    SourceIndex = (int)n.Index
                });
                added++;
            }

            debug.AppendLine($"4.2.5 profile numeric-ID recovery: family={prefix}, rowIds={scored.Distinct}, sequential={scored.SequentialLinks}, engineering={scored.EngineeringColumns}, added={added}.");
            return output;
        }

'''

text = text.replace(marker, method + marker, 1)
path.write_text(text, encoding='utf-8')
print('patched profile numeric-only ID recovery for 4.2.5')
