from pathlib import Path

path = Path('src/SewerScan.Infrastructure/Parsers/SewerProjectParser.cs')
text = path.read_text(encoding='utf-8-sig')

old = '''                    var dx = Math.Abs(pair.X - anchor.X);\n                    var dy = Math.Abs(pair.Y - anchor.Y);\n                    if (pair.Horizontal)\n                    {\n                        if (dx > 85 || dy > 18)\n                            continue;\n                    }\n                    else\n                    {\n                        if (dx > 32 || dy > 58)\n                            continue;\n                    }'''

new = '''                    var dx = Math.Abs(pair.X - anchor.X);\n                    var dy = Math.Abs(pair.Y - anchor.Y);\n\n                    // 4.2.4 Batorego: tiled OCR often returns each rendered level two to four\n                    // times. That repetition is useful confidence evidence. In the real PZT the\n                    // repeated level stack can be displaced 70-90 px from the D/S label, so the\n                    // old 32 px vertical-pair gate discarded otherwise unambiguous 62,25/60,58\n                    // pairs. Only widen the ownership window when BOTH levels are independently\n                    // repeated near the pair; ordinary one-off numbers keep the strict gate.\n                    var repeatedGround = numeric.Count(n =>\n                        Math.Abs(n.Value - pair.Ground) < 0.001 &&\n                        Math.Abs(n.X - pair.X) <= 105 &&\n                        Math.Abs(n.Y - pair.Y) <= 85) >= 2;\n                    var repeatedInvert = numeric.Count(n =>\n                        Math.Abs(n.Value - pair.Invert) < 0.001 &&\n                        Math.Abs(n.X - pair.X) <= 105 &&\n                        Math.Abs(n.Y - pair.Y) <= 85) >= 2;\n                    var strongRepeatedPair = repeatedGround && repeatedInvert;\n\n                    if (pair.Horizontal)\n                    {\n                        if (dx > 85 || dy > 18)\n                            continue;\n                    }\n                    else\n                    {\n                        var maxDx = strongRepeatedPair ? 95 : 32;\n                        var maxDy = strongRepeatedPair ? 75 : 58;\n                        if (dx > maxDx || dy > maxDy)\n                            continue;\n                    }'''

if old not in text:
    if 'var strongRepeatedPair = repeatedGround && repeatedInvert;' in text:
        print('4.2.4 repeated-level ownership patch already applied')
    else:
        raise SystemExit('BuildPztElevationAssignments ownership block not found')
else:
    text = text.replace(old, new, 1)
    path.write_text(text, encoding='utf-8')
    print('Applied 4.2.4 repeated-level ownership patch')
