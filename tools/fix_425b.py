from pathlib import Path

path = Path('src/SewerScan.Infrastructure/Parsers/SewerProjectParser.cs')
text = path.read_text(encoding='utf-8-sig')
old = '''                    Token = CleanSpatialToken(item.Text),\n                    X = item.X + item.Width / 2.0,'''
new = '''                    // Node indices such as 11 are legitimate. Do not run CAD duplicate-glyph\n                    // normalization here, because it would collapse "11" to "1".\n                    Token = Regex.Replace((item.Text ?? string.Empty).Trim(), @"^[^0-9]+|[^0-9]+$", string.Empty),\n                    X = item.X + item.Width / 2.0,'''
if new in text:
    print('already patched')
elif old in text:
    text = text.replace(old, new, 1)
    path.write_text(text, encoding='utf-8')
    print('patched repeated profile node digits')
else:
    raise SystemExit('target profile numeric token block not found')
