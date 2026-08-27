from pathlib import Path

path = Path('src/SewerScan.Infrastructure/Parsers/SewerProjectParser.cs')
text = path.read_text(encoding='utf-8-sig')

old = '''                .Where(x => x.Distinct >= 3 && x.SequentialLinks >= 2 && x.EngineeringColumns >= 2 && x.Span >= 120)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (scored == null)
            {
                debug.AppendLine($"4.2.5 profile numeric-ID recovery: no credible {prefix} row.");
                return output;
            }
'''

new = '''                // 4.2.6: Batorego's storm profile loses the D prefixes and also has too
                // little clean elevation OCR to satisfy the 4.2.5 engineering-column gate.
                // A long, monotonic consecutive run (e.g. 8,9,10,11,12) across a wide row is
                // itself strong table-structure evidence. Keep the old engineering-supported
                // path, but additionally accept >=5 IDs with >=4 consecutive links.
                .Where(x => x.Span >= 120 && (
                    (x.Distinct >= 3 && x.SequentialLinks >= 2 && x.EngineeringColumns >= 2) ||
                    (x.Distinct >= 5 && x.SequentialLinks >= 4)))
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (scored == null)
            {
                var bestObserved = bands
                    .Select(b => b
                        .GroupBy(x => (int)x.Number)
                        .Select(g => g.OrderBy(x => (double)x.X).First())
                        .OrderBy(x => (double)x.X)
                        .ToList())
                    .OrderByDescending(r => r.Count)
                    .FirstOrDefault();
                var observed = bestObserved == null ? "-" : string.Join(",", bestObserved.Select(x => ((int)x.Number).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                debug.AppendLine($"4.2.6 profile numeric-ID recovery: no credible {prefix} row; best observed=[{observed}].");
                return output;
            }
'''

if old not in text:
    if '4.2.6 profile numeric-ID recovery' in text:
        print('already patched')
        raise SystemExit(0)
    raise SystemExit('4.2.6 target block not found')

text = text.replace(old, new)
text = text.replace('debug.AppendLine($"4.2.5 profile numeric-ID recovery: family={prefix}, rowIds={scored.Distinct}, sequential={scored.SequentialLinks}, engineering={scored.EngineeringColumns}, added={added}.");',
                    'debug.AppendLine($"4.2.6 profile numeric-ID recovery: family={prefix}, rowIds={scored.Distinct}, sequential={scored.SequentialLinks}, engineering={scored.EngineeringColumns}, added={added}.");')
path.write_text(text, encoding='utf-8')
print('patched 4.2.6 sequential profile row recovery')
