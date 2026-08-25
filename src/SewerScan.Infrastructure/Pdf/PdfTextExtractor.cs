using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Docnet.Core;
using Docnet.Core.Models;
using SewerScan.Application.Interfaces;
using SewerScan.Application.Models;
using SewerScan.Infrastructure.OCR;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SewerScan.Infrastructure.Pdf
{
    /// <summary>
    /// Multi-engine PDF extraction for PrefabScan 3.3.
    /// Engine 1: PdfPig. Engine 2: PDFium via Docnet.Core. If PdfPig opens a CAD PDF
    /// but returns an empty page, PDFium is tried automatically. PDFium also exposes
    /// character bounding boxes, so spatial parsing remains available.
    /// </summary>
    public class PdfTextExtractor : ITextExtractor
    {
        private static readonly Regex CandidateRegex = new(
            @"\b(?:KD|KS|WP)\s*[-.:/]?\s*\d{1,3}(?:[./-]\d+)*\b|\bD\s*[-.:/]?\s*\d{1,3}(?:[./-]\d+)*\b|\bSO\b|\b(?:DN|Ø|ø)\s*\d{2,4}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Task<IReadOnlyList<PageText>> ExtractAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));

            var pigPages = TryExtractWithPdfPig(filePath, out var pigFailure);
            var pigUseful = pigPages.Count > 0 && pigPages.Any(IsUseful);

            if (pigUseful)
                return Task.FromResult((IReadOnlyList<PageText>)pigPages);

            Debug.WriteLine($"[PdfTextExtractor] PdfPig returned no useful text for '{filePath}'. Trying PDFium/Docnet.");

            var pdfiumPages = TryExtractWithPdfium(filePath, out var pdfiumFailure);
            var pdfiumUseful = pdfiumPages.Count > 0 && pdfiumPages.Any(IsUseful);

            if (pdfiumUseful)
            {
                // Preserve the fact that PdfPig failed/was empty in every page diagnostic.
                foreach (var page in pdfiumPages)
                {
                    page.ExtractionDiagnostics =
                        "FALLBACK: PdfPig nie zwrócił użytecznej warstwy tekstowej. Użyto PDFium/Docnet.\r\n" +
                        (string.IsNullOrWhiteSpace(pigFailure) ? string.Empty : $"PdfPig: {pigFailure}\r\n") +
                        page.ExtractionDiagnostics;
                }
                return Task.FromResult((IReadOnlyList<PageText>)pdfiumPages);
            }

            // Engine 3: OCR. This is intentionally used only when both text engines report
            // an empty layer. Engineering drawings are rendered through PDFium and OCR is run
            // on overlapping tiles, preserving word coordinates for the spatial parser.
            Debug.WriteLine($"[PdfTextExtractor] Text engines empty for '{filePath}'. Starting tiled OCR fallback.");
            var ocrPages = OcrFallbackExtractor.Extract(filePath, out var ocrFailure);
            var ocrUseful = ocrPages.Count > 0 && ocrPages.Any(IsUseful);
            if (ocrUseful)
            {
                foreach (var page in ocrPages)
                {
                    page.ExtractionDiagnostics =
                        "FALLBACK_3: PdfPig i PDFium nie znalazły tekstu. Uruchomiono OCR kafelkowy.\r\n" +
                        (string.IsNullOrWhiteSpace(pigFailure) ? string.Empty : $"PdfPig: {pigFailure}\r\n") +
                        (string.IsNullOrWhiteSpace(pdfiumFailure) ? string.Empty : $"PDFium: {pdfiumFailure}\r\n") +
                        page.ExtractionDiagnostics;
                }
                return Task.FromResult((IReadOnlyList<PageText>)ocrPages);
            }

            // All three engines failed. Return diagnostics instead of pretending the parser
            // can analyse an empty document.
            var diagnosticPages = pdfiumPages.Count > 0 ? pdfiumPages : pigPages;
            if (diagnosticPages.Count > 0)
            {
                foreach (var page in diagnosticPages)
                {
                    page.ExtractionDiagnostics =
                        "OCR_FAILED: PdfPig, PDFium i OCR nie znalazły użytecznych danych.\r\n" +
                        (string.IsNullOrWhiteSpace(pigFailure) ? string.Empty : $"PdfPig: {pigFailure}\r\n") +
                        (string.IsNullOrWhiteSpace(pdfiumFailure) ? string.Empty : $"PDFium: {pdfiumFailure}\r\n") +
                        (string.IsNullOrWhiteSpace(ocrFailure) ? string.Empty : $"OCR: {ocrFailure}\r\n") +
                        page.ExtractionDiagnostics;
                }
                return Task.FromResult((IReadOnlyList<PageText>)diagnosticPages);
            }

            throw new System.IO.InvalidDataException(
                $"Nie można odczytać pliku PDF '{System.IO.Path.GetFileName(filePath)}' żadnym silnikiem. " +
                $"PdfPig: {pigFailure ?? "brak danych"}. PDFium: {pdfiumFailure ?? "brak danych"}. OCR: {ocrFailure ?? "brak danych"}.");
        }

        private static List<PageText> TryExtractWithPdfPig(string filePath, out string? failure)
        {
            failure = null;
            var pages = new List<PageText>();
            PdfDocument? pdf = null;
            string openMode = "strict";

            try
            {
                try
                {
                    pdf = PdfDocument.Open(filePath);
                }
                catch (Exception strictException)
                {
                    Debug.WriteLine($"[PdfTextExtractor] Strict PdfPig open failed: {strictException.Message}");
                    pdf = PdfDocument.Open(filePath, new ParsingOptions { UseLenientParsing = true });
                    openMode = "lenient";
                }

                using (pdf)
                foreach (var page in pdf.GetPages())
                {
                    var pt = new PageText
                    {
                        PageNumber = (int)page.Number,
                        ExtractionEngine = $"PdfPig/{openMode}"
                    };

                    try { pt.RawText = page.Text ?? string.Empty; }
                    catch (Exception ex) { Debug.WriteLine(ex.Message); }

                    try { pt.OrderedText = ContentOrderTextExtractor.GetText(page) ?? string.Empty; }
                    catch (Exception ex) { Debug.WriteLine(ex.Message); }

                    pt.Text = ChoosePrimaryText(pt.RawText, pt.OrderedText);

                    var allWords = new List<string>();
                    int coordinateWords = 0;
                    int wordReadFailures = 0;
                    try
                    {
                        foreach (var word in page.GetWords())
                        {
                            allWords.Add(word.Text ?? string.Empty);
                            double x = 0, y = 0, w = 0, h = 0;
                            try
                            {
                                var wt = word.GetType();
                                var bboxProp = wt.GetProperty("BoundingBox") ?? wt.GetProperty("Bbox");
                                var bbox = bboxProp?.GetValue(word);
                                if (bbox != null)
                                {
                                    var bt = bbox.GetType();
                                    var leftProp = bt.GetProperty("Left") ?? bt.GetProperty("X1");
                                    var rightProp = bt.GetProperty("Right") ?? bt.GetProperty("X2");
                                    var topProp = bt.GetProperty("Top") ?? bt.GetProperty("Y2");
                                    var bottomProp = bt.GetProperty("Bottom") ?? bt.GetProperty("Y1");

                                    if (leftProp != null && rightProp != null && topProp != null && bottomProp != null &&
                                        IsNumericProperty(leftProp) && IsNumericProperty(rightProp) && IsNumericProperty(topProp) && IsNumericProperty(bottomProp))
                                    {
                                        var left = Convert.ToDouble(leftProp.GetValue(bbox));
                                        var right = Convert.ToDouble(rightProp.GetValue(bbox));
                                        var top = Convert.ToDouble(topProp.GetValue(bbox));
                                        var bottom = Convert.ToDouble(bottomProp.GetValue(bbox));
                                        x = left; y = top; w = Math.Abs(right - left); h = Math.Abs(top - bottom);
                                    }
                                    else
                                    {
                                        var bl = bt.GetProperty("BottomLeft")?.GetValue(bbox);
                                        var tr = bt.GetProperty("TopRight")?.GetValue(bbox);
                                        if (bl != null && tr != null)
                                        {
                                            x = ReadPoint(bl, "X");
                                            var bottom = ReadPoint(bl, "Y");
                                            var right = ReadPoint(tr, "X");
                                            y = ReadPoint(tr, "Y");
                                            w = Math.Abs(right - x); h = Math.Abs(y - bottom);
                                        }
                                    }
                                }
                            }
                            catch { wordReadFailures++; }

                            if (Math.Abs(x) > .001 || Math.Abs(y) > .001 || w > .001 || h > .001)
                                coordinateWords++;

                            pt.Items.Add(new TextItem { Text = word.Text ?? string.Empty, X = x, Y = y, Width = w, Height = h });
                        }
                    }
                    catch (Exception wordsException)
                    {
                        wordReadFailures++;
                        Debug.WriteLine($"[PdfTextExtractor] PdfPig GetWords failed: {wordsException}");
                    }

                    var wordsText = string.Join(" ", allWords.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (string.IsNullOrWhiteSpace(pt.Text) && !string.IsNullOrWhiteSpace(wordsText))
                        pt.Text = wordsText;

                    pt.ExtractionDiagnostics = BuildDiagnostics(
                        filePath, pt.ExtractionEngine, pt, allWords, coordinateWords, wordReadFailures, wordsText);
                    pages.Add(pt);
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
                Debug.WriteLine($"[PdfTextExtractor] PdfPig failed for '{filePath}': {ex}");
            }

            return pages;
        }

        private static List<PageText> TryExtractWithPdfium(string filePath, out string? failure)
        {
            failure = null;
            var pages = new List<PageText>();

            try
            {
                // Docnet/PDFium is independent from PdfPig and uses Chromium's PDF engine.
                // 4096x4096 keeps sufficiently precise coordinates for engineering drawings.
                using var docReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(4096, 4096));
                var pageCount = docReader.GetPageCount();

                for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    using var pageReader = docReader.GetPageReader(pageIndex);
                    var pt = new PageText
                    {
                        PageNumber = pageIndex + 1,
                        ExtractionEngine = "PDFium/Docnet"
                    };

                    string pdfiumText = string.Empty;
                    try { pdfiumText = pageReader.GetText() ?? string.Empty; }
                    catch (Exception ex) { Debug.WriteLine($"[PdfTextExtractor] PDFium GetText: {ex.Message}"); }

                    var characters = new List<Docnet.Core.Models.Character>();
                    try { characters = pageReader.GetCharacters().ToList(); }
                    catch (Exception ex) { Debug.WriteLine($"[PdfTextExtractor] PDFium GetCharacters: {ex.Message}"); }

                    var spatialWords = BuildPdfiumWords(characters);
                    var spatialText = BuildSpatialReadingText(spatialWords);

                    pt.RawText = pdfiumText;
                    pt.OrderedText = spatialText;
                    pt.Text = ChoosePrimaryText(pdfiumText, spatialText);
                    if (string.IsNullOrWhiteSpace(pt.Text) && spatialWords.Count > 0)
                        pt.Text = string.Join(" ", spatialWords.Select(w => w.Text));

                    foreach (var word in spatialWords)
                        pt.Items.Add(word);

                    var wordStrings = spatialWords.Select(w => w.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    var wordsText = string.Join(" ", wordStrings);
                    pt.ExtractionDiagnostics = BuildDiagnostics(
                        filePath,
                        pt.ExtractionEngine,
                        pt,
                        wordStrings,
                        spatialWords.Count,
                        0,
                        wordsText,
                        extra: $"PDFium chars: {characters.Count}; page px: {pageReader.GetPageWidth()}x{pageReader.GetPageHeight()}");
                    pages.Add(pt);
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
                Debug.WriteLine($"[PdfTextExtractor] PDFium failed for '{filePath}': {ex}");
            }

            return pages;
        }

        private static List<TextItem> BuildPdfiumWords(IReadOnlyList<Docnet.Core.Models.Character> chars)
        {
            if (chars.Count == 0) return new List<TextItem>();

            // First try content-order grouping. Spaces and control characters become word separators.
            var contentWords = new List<TextItem>();
            var current = new StringBuilder();
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;

            void Flush()
            {
                if (current.Length == 0) return;
                contentWords.Add(new TextItem
                {
                    Text = current.ToString(),
                    X = left == int.MaxValue ? 0 : left,
                    Y = top == int.MaxValue ? 0 : top,
                    Width = right > left ? right - left : 0,
                    Height = bottom > top ? bottom - top : 0
                });
                current.Clear();
                left = top = int.MaxValue;
                right = bottom = int.MinValue;
            }

            foreach (var ch in chars)
            {
                if (char.IsWhiteSpace(ch.Char) || char.IsControl(ch.Char))
                {
                    Flush();
                    continue;
                }

                current.Append(ch.Char);
                left = Math.Min(left, ch.Box.Left);
                top = Math.Min(top, ch.Box.Top);
                right = Math.Max(right, ch.Box.Right);
                bottom = Math.Max(bottom, ch.Box.Bottom);
            }
            Flush();

            // CAD exports sometimes expose no actual space characters. If grouping produced too few
            // giant tokens, rebuild spatially by lines and gaps.
            if (contentWords.Count >= 3 && contentWords.All(w => w.Text.Length < 120))
                return contentWords;

            return BuildSpatialWordsFromCharacters(chars);
        }

        private sealed class PdfiumGlyph
        {
            public char Char { get; init; }
            public int Left { get; init; }
            public int Top { get; init; }
            public int Right { get; init; }
            public int Bottom { get; init; }
            public double CenterY { get; init; }
            public int Height { get; init; }
            public int Width { get; init; }
        }

        private static List<TextItem> BuildSpatialWordsFromCharacters(IReadOnlyList<Docnet.Core.Models.Character> chars)
        {
            var glyphs = chars
                .Where(c => !char.IsControl(c.Char) && !char.IsWhiteSpace(c.Char))
                .Select(c => new PdfiumGlyph
                {
                    Char = c.Char,
                    Left = c.Box.Left,
                    Top = c.Box.Top,
                    Right = c.Box.Right,
                    Bottom = c.Box.Bottom,
                    CenterY = (c.Box.Top + c.Box.Bottom) / 2.0,
                    Height = Math.Max(1, c.Box.Bottom - c.Box.Top),
                    Width = Math.Max(1, c.Box.Right - c.Box.Left)
                })
                .ToList();

            if (glyphs.Count == 0) return new List<TextItem>();

            var medianHeight = Median(glyphs.Select(g => (double)g.Height));
            var lineTolerance = Math.Max(3.0, medianHeight * 0.65);
            var lines = new List<List<PdfiumGlyph>>();

            foreach (var glyph in glyphs.OrderBy(g => g.CenterY).ThenBy(g => g.Left))
            {
                List<PdfiumGlyph>? best = null;
                double bestDelta = double.MaxValue;
                foreach (var line in lines)
                {
                    var center = line.Average(x => x.CenterY);
                    var delta = Math.Abs(center - glyph.CenterY);
                    if (delta <= lineTolerance && delta < bestDelta)
                    {
                        best = line;
                        bestDelta = delta;
                    }
                }
                if (best == null)
                {
                    best = new List<PdfiumGlyph>();
                    lines.Add(best);
                }
                best.Add(glyph);
            }

            var words = new List<TextItem>();
            foreach (var line in lines.OrderBy(l => l.Average(x => x.CenterY)))
            {
                var ordered = line.OrderBy(x => (int)x.Left).ToList();
                var medianWidth = Median(ordered.Select(x => (double)x.Width));
                var gapThreshold = Math.Max(4.1, medianWidth * 1.35);

                var sb = new StringBuilder();
                int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
                PdfiumGlyph? previous = null;

                void FlushSpatial()
                {
                    if (sb.Length == 0) return;
                    words.Add(new TextItem
                    {
                        Text = sb.ToString(),
                        X = l,
                        Y = t,
                        Width = Math.Max(0, r - l),
                        Height = Math.Max(0, b - t)
                    });
                    sb.Clear();
                    l = t = int.MaxValue;
                    r = b = int.MinValue;
                }

                foreach (var glyph in ordered)
                {
                    if (previous != null && glyph.Left - previous.Right > gapThreshold)
                        FlushSpatial();

                    sb.Append(glyph.Char);
                    l = Math.Min(l, glyph.Left);
                    t = Math.Min(t, glyph.Top);
                    r = Math.Max(r, glyph.Right);
                    b = Math.Max(b, glyph.Bottom);
                    previous = glyph;
                }
                FlushSpatial();
            }

            return words;
        }

        private static string BuildSpatialReadingText(IReadOnlyList<TextItem> words)
        {
            if (words.Count == 0) return string.Empty;
            var medianH = Median(words.Select(w => Math.Max(1.0, w.Height)));
            var tolerance = Math.Max(3.0, medianH * 0.8);
            var lines = new List<List<TextItem>>();

            foreach (var word in words.OrderBy(w => w.Y).ThenBy(w => w.X))
            {
                var centerY = word.Y + word.Height / 2.0;
                var line = lines.FirstOrDefault(l => Math.Abs(l.Average(x => x.Y + x.Height / 2.0) - centerY) <= tolerance);
                if (line == null)
                {
                    line = new List<TextItem>();
                    lines.Add(line);
                }
                line.Add(word);
            }

            return string.Join(Environment.NewLine,
                lines.OrderBy(l => l.Average(w => w.Y))
                    .Select(l => string.Join(" ", l.OrderBy(w => w.X).Select(w => w.Text))));
        }

        private static double Median(IEnumerable<double> values)
        {
            var data = values.OrderBy(x => x).ToArray();
            if (data.Length == 0) return 1;
            var mid = data.Length / 2;
            return data.Length % 2 == 0 ? (data[mid - 1] + data[mid]) / 2.0 : data[mid];
        }

        private static bool IsUseful(PageText page)
        {
            if (CandidateRegex.IsMatch(page.Text ?? string.Empty)) return true;
            if ((page.Text?.Length ?? 0) >= 20) return true;
            return page.Items.Count >= 3;
        }

        private static bool IsNumericProperty(System.Reflection.PropertyInfo property)
        {
            var t = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            return t == typeof(double) || t == typeof(float) || t == typeof(decimal) || t == typeof(int) || t == typeof(long);
        }

        private static double ReadPoint(object point, string property)
        {
            var p = point.GetType().GetProperty(property);
            return p == null ? 0 : Convert.ToDouble(p.GetValue(point));
        }

        private static string ChoosePrimaryText(string raw, string ordered)
        {
            if (string.IsNullOrWhiteSpace(raw)) return ordered ?? string.Empty;
            if (string.IsNullOrWhiteSpace(ordered)) return raw;

            var rawCandidates = CandidateRegex.Matches(raw).Count;
            var orderedCandidates = CandidateRegex.Matches(ordered).Count;
            if (rawCandidates != orderedCandidates)
                return rawCandidates > orderedCandidates ? raw : ordered;

            return ordered.Length >= raw.Length * 0.35 ? ordered : raw;
        }

        private static string BuildDiagnostics(string filePath, string engine, PageText pt,
            IReadOnlyCollection<string> words, int coordinateWords, int failures, string wordsText, string? extra = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== PDF: {System.IO.Path.GetFileName(filePath)} | strona {pt.PageNumber} ===");
            sb.AppendLine($"Silnik: {engine}");
            if (!string.IsNullOrWhiteSpace(extra)) sb.AppendLine(extra);
            sb.AppendLine($"RawText: {pt.RawText.Length} znaków");
            sb.AppendLine($"OrderedText: {pt.OrderedText.Length} znaków");
            sb.AppendLine($"PrimaryText: {pt.Text.Length} znaków");
            sb.AppendLine($"Słowa: {words.Count}; słowa ze współrzędnymi: {coordinateWords}; błędy słów: {failures}");
            sb.AppendLine($"Kandydaci RAW: {CandidateRegex.Matches(pt.RawText).Count}; ORDERED: {CandidateRegex.Matches(pt.OrderedText).Count}; PRIMARY: {CandidateRegex.Matches(pt.Text).Count}");
            sb.AppendLine("Kandydaci PRIMARY: " + string.Join(" | ", CandidateRegex.Matches(pt.Text).Cast<Match>().Select(m => m.Value).Take(100)));
            sb.AppendLine("Pierwsze słowa: " + Clip(wordsText, 1400));
            sb.AppendLine("--- RAW SAMPLE ---");
            sb.AppendLine(Clip(NormalizeForLog(pt.RawText), 2200));
            sb.AppendLine("--- ORDERED SAMPLE ---");
            sb.AppendLine(Clip(NormalizeForLog(pt.OrderedText), 2200));
            sb.AppendLine("--- PRIMARY SAMPLE ---");
            sb.AppendLine(Clip(NormalizeForLog(pt.Text), 2200));
            return sb.ToString();
        }

        private static string NormalizeForLog(string value) => (value ?? string.Empty).Replace("\0", "");
        private static string Clip(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";
    }
}
