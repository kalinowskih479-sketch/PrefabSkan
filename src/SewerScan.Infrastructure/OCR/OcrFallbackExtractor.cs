using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docnet.Core;
using Docnet.Core.Models;
using ImageMagick;
using SewerScan.Application.Models;
using Tesseract;

namespace SewerScan.Infrastructure.OCR;

/// <summary>
/// OCR fallback for raster/CAD-image PDFs. The extractor renders pages through PDFium,
/// splits large engineering drawings into overlapping tiles, OCRs them with Tesseract,
/// preserves word coordinates, de-duplicates overlap results and caches successful OCR.
/// </summary>
internal static class OcrFallbackExtractor
{
    private const string OcrVersion = "prefabscan-ocr-4.1-profile";
    private const int RenderMaxDimension = 6200;
    private const int TileSize = 1700;
    private const int TileOverlap = 180;
    private const double BaseScale = 2.35;
    private const float NormalConfidenceFloor = 28f;

    private static readonly Regex CandidateRegex = new(
        @"^(?:(?:D|S|KD|KS)\s*\d{1,3}(?:[./-]\d+)*|WP\s*\d{1,3}(?:[./-]\d+)*|SO|(?:DN|Ø|ø)\s*\d{2,4})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumericRegex = new(
        @"^\d{2,3}[,.]\d{1,3}$",
        RegexOptions.Compiled);

    private sealed class CacheDocument
    {
        public string Version { get; set; } = OcrVersion;
        public List<CachePage> Pages { get; set; } = new();
    }

    private sealed class CachePage
    {
        public int PageNumber { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Diagnostics { get; set; } = string.Empty;
        public List<CacheItem> Items { get; set; } = new();
    }

    private sealed class CacheItem
    {
        public string Text { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed record OcrWord(string Text, double X, double Y, double Width, double Height, float Confidence, int Rotation);

    public static List<PageText> Extract(string filePath, out string? failure)
    {
        failure = null;
        try
        {
            if (TryLoadCache(filePath, out var cached))
                return cached;

            var tessdata = OcrModelStore.Resolve(AppContext.BaseDirectory);
            const string languages = "pol+eng";

            using var engine = new TesseractEngine(tessdata, languages, EngineMode.Default);
            engine.SetVariable("preserve_interword_spaces", "1");
            engine.SetVariable("user_defined_dpi", "300");

            using var docReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(RenderMaxDimension, RenderMaxDimension));
            var pages = new List<PageText>();

            for (var pageIndex = 0; pageIndex < docReader.GetPageCount(); pageIndex++)
            {
                using var reader = docReader.GetPageReader(pageIndex);
                var width = reader.GetPageWidth();
                var height = reader.GetPageHeight();
                var rawImage = reader.GetImage();

                if (rawImage == null || rawImage.Length == 0 || width <= 0 || height <= 0)
                {
                    pages.Add(new PageText
                    {
                        PageNumber = pageIndex + 1,
                        ExtractionEngine = "OCR/Tesseract",
                        ExtractionDiagnostics = $"OCR: PDFium nie zwrócił obrazu strony {pageIndex + 1}."
                    });
                    continue;
                }

                // Docnet/PDFium returns a 32-bit BGRA framebuffer. Converting it to a
                // standard BMP first avoids ImageMagick PixelReadSettings API differences
                // between package versions and keeps channel ordering explicit.
                var bmpBytes = CreateBmpFromBgra(rawImage, width, height);
                using var source = new MagickImage(bmpBytes);
                source.ColorSpace = ColorSpace.sRGB;

                var ocrScale = height < 1500 ? 3.15 : BaseScale;
                var words = OcrPage(engine, source, width, height, ocrScale, out var tileCount, out var rotationPasses);
                var deduped = Deduplicate(words);
                var items = deduped.Select(w => new TextItem
                {
                    Text = w.Text,
                    X = w.X,
                    Y = w.Y,
                    Width = w.Width,
                    Height = w.Height
                }).ToList();
                var text = BuildSpatialReadingText(items);
                var candidates = deduped.Count(w => IsImportantToken(w.Text));
                var avgConfidence = deduped.Count == 0 ? 0 : deduped.Average(w => w.Confidence);

                var pt = new PageText
                {
                    PageNumber = pageIndex + 1,
                    ExtractionEngine = "OCR/Tesseract tiled",
                    RawText = text,
                    OrderedText = text,
                    Text = text,
                    ExtractionDiagnostics =
                        $"OCR_FALLBACK: rasterowa strona przeanalizowana przez Tesseract.\r\n" +
                        $"Silnik: OCR/Tesseract tiled; języki: {languages}\r\n" +
                        $"Render: {width}x{height}px; skala OCR: {ocrScale:0.00}x; kafle: {tileCount}; przebiegi orientacji: {rotationPasses}\r\n" +
                        $"OCR słowa: {deduped.Count}; kandydaci techniczni: {candidates}; średnia pewność: {avgConfidence:0.0}\r\n" +
                        $"Cache: {GetCachePath(filePath)}\r\n" +
                        $"Pierwsze tokeny: {string.Join(" | ", deduped.Take(100).Select(w => w.Text))}"
                };
                pt.Items.AddRange(items);
                pages.Add(pt);
            }

            if (pages.Any(p => p.Items.Count > 0 || !string.IsNullOrWhiteSpace(p.Text)))
                SaveCache(filePath, pages);

            return pages;
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
            Debug.WriteLine($"[OcrFallbackExtractor] {ex}");
            return new List<PageText>();
        }
    }

    private static List<OcrWord> OcrPage(TesseractEngine engine, MagickImage source, int pageWidth, int pageHeight, double scale,
        out int tileCount, out int rotationPasses)
    {
        var words = new List<OcrWord>();
        tileCount = 0;
        rotationPasses = 0;

        foreach (var tile in EnumerateTiles(pageWidth, pageHeight))
        {
            tileCount++;
            using var crop = (MagickImage)source.Clone();
            crop.Crop(new MagickGeometry(tile.X, tile.Y, tile.Width, tile.Height));
            crop.RePage();

            PrepareForOcr(crop, scale);
            words.AddRange(RunTile(engine, crop, tile.X, tile.Y, tile.Width, tile.Height, 0, scale, PageSegMode.SparseText));
            rotationPasses++;
        }

        // Technical drawings frequently contain vertical labels. Only pay the cost of orientation
        // passes when the first pass did not find a healthy set of engineering identifiers.
        var firstPassCandidates = words.Count(w => IsImportantToken(w.Text));
        var profileShapedPage = pageWidth >= pageHeight * 2.2;
        if (firstPassCandidates < 4 || profileShapedPage)
        {
            foreach (var tile in EnumerateTiles(pageWidth, pageHeight))
            {
                using var crop = (MagickImage)source.Clone();
                crop.Crop(new MagickGeometry(tile.X, tile.Y, tile.Width, tile.Height));
                crop.RePage();
                PrepareForOcr(crop, scale);

                using var cw = (MagickImage)crop.Clone();
                cw.Rotate(90);
                words.AddRange(RunTile(engine, cw, tile.X, tile.Y, tile.Width, tile.Height, 90, scale, PageSegMode.SparseText));
                rotationPasses++;

                using var ccw = (MagickImage)crop.Clone();
                ccw.Rotate(270);
                words.AddRange(RunTile(engine, ccw, tile.X, tile.Y, tile.Width, tile.Height, 270, scale, PageSegMode.SparseText));
                rotationPasses++;
            }
        }

        // 2.2 rescue pass: engineering plots often use very small CAD fonts.  When the
        // sparse pass sees too few identifiers, run a second segmentation strategy on the
        // original orientation.  Results are merged geometrically by Deduplicate(), so this
        // increases recall without multiplying the same label.
        if (words.Count(w => IsImportantToken(w.Text)) < 10)
        {
            foreach (var tile in EnumerateTiles(pageWidth, pageHeight))
            {
                using var crop = (MagickImage)source.Clone();
                crop.Crop(new MagickGeometry(tile.X, tile.Y, tile.Width, tile.Height));
                crop.RePage();
                var rescueScale = Math.Max(2.6, scale);
                PrepareForOcr(crop, rescueScale);
                words.AddRange(RunTile(engine, crop, tile.X, tile.Y, tile.Width, tile.Height, 0, rescueScale, PageSegMode.Auto));
                rotationPasses++;
            }
        }

        // 4.1 profile-table OCR: on wide/short longitudinal profiles the engineering table
        // occupies the lower part of the sheet and many labels are rotated. Re-read overlapping
        // full-width horizontal strips with multiple segmentation modes. Unlike anchor-focused
        // OCR this works even when the structure identifiers themselves were missed initially.
        if (profileShapedPage)
        {
            var stripHeight = Math.Max(220, pageHeight / 2);
            var stripStarts = new[] { Math.Max(0, pageHeight / 3), Math.Max(0, pageHeight - stripHeight) }
                .Distinct()
                .ToList();

            foreach (var sy in stripStarts)
            {
                var sh = Math.Min(stripHeight, pageHeight - sy);
                using var strip = (MagickImage)source.Clone();
                strip.Crop(new MagickGeometry(0, sy, pageWidth, sh));
                strip.RePage();
                PrepareForOcr(strip, 3.8);

                words.AddRange(RunTile(engine, strip, 0, sy, pageWidth, sh, 0, 3.8, PageSegMode.SparseText));
                rotationPasses++;

                using var stripAuto = (MagickImage)strip.Clone();
                words.AddRange(RunTile(engine, stripAuto, 0, sy, pageWidth, sh, 0, 3.8, PageSegMode.Auto));
                rotationPasses++;

                using var stripCw = (MagickImage)strip.Clone();
                stripCw.Rotate(90);
                words.AddRange(RunTile(engine, stripCw, 0, sy, pageWidth, sh, 90, 3.8, PageSegMode.SparseText));
                rotationPasses++;

                using var stripCcw = (MagickImage)strip.Clone();
                stripCcw.Rotate(270);
                words.AddRange(RunTile(engine, stripCcw, 0, sy, pageWidth, sh, 270, 3.8, PageSegMode.SparseText));
                rotationPasses++;
            }
        }

        // Vision 3.1: second-look pass. This mirrors manual drawing inspection: after the
        // global scan finds an engineering label, zoom into its neighbourhood and OCR that
        // small region again at much higher scale. This recovers DN/elevations/material labels
        // that are too small to survive whole-sheet OCR.
        // Vision 3.3: geometry-first second look.  Do not trust the raw OCR stream alone:
        // deduplicate the global pass first, then re-read three differently sized regions around
        // each credible structure label.  Small core crops recover DN/elevations, while the wide
        // profile cell captures pipe material and crown descriptions that can sit far above/below
        // the identifier on engineering profiles.
        var anchors = Deduplicate(words)
            .Where(w => w.Confidence >= 24f)
            .Where(w => Regex.IsMatch(Regex.Replace(w.Text ?? string.Empty, @"\s+", string.Empty),
                @"^(?:(?:D|S|KD|KS)\d{1,3}(?:[./-]\d{1,2})?|SO|WP\d{1,3})$", RegexOptions.IgnoreCase))
            .OrderByDescending(w => w.Confidence)
            .Take(32)
            .ToList();

        // 3.4 performance hardening: the former implementation executed three very large,
        // heavily enlarged OCR passes for every anchor. On a profile with ~24 structures this
        // could mean 70+ expensive Tesseract runs. We now use one compact pass per anchor and
        // only a conditional rescue pass when the compact crop returns almost no technical data.
        // A hard pass/time budget prevents a difficult sheet from monopolising the UI for minutes.
        var secondLookPasses = 0;
        var secondLookTimer = Stopwatch.StartNew();
        const int maxSecondLookPasses = 38;
        var secondLookBudget = TimeSpan.FromSeconds(75);

        foreach (var anchor in anchors)
        {
            if (secondLookPasses >= maxSecondLookPasses || secondLookTimer.Elapsed > secondLookBudget)
                break;

            var cx = anchor.X + anchor.Width / 2.0;
            var cy = anchor.Y + anchor.Height / 2.0;

            var compact = RunFocus(780, 620, 3.25, PageSegMode.SparseText);
            words.AddRange(compact);

            var useful = compact.Count(w => IsImportantToken(w.Text) || NumericRegex.IsMatch((w.Text ?? string.Empty).Trim()));
            if (useful < 2 && secondLookPasses < maxSecondLookPasses && secondLookTimer.Elapsed <= secondLookBudget)
            {
                var rescue = RunFocus(1050, 1050, 2.75, PageSegMode.Auto);
                words.AddRange(rescue);
            }

            List<OcrWord> RunFocus(int requestedW, int requestedH, double focusScale, PageSegMode mode)
            {
                var focusW = Math.Min(requestedW, pageWidth);
                var focusH = Math.Min(requestedH, pageHeight);
                var fx = Math.Max(0, Math.Min(pageWidth - focusW, (int)Math.Round(cx - focusW / 2.0)));
                var fy = Math.Max(0, Math.Min(pageHeight - focusH, (int)Math.Round(cy - focusH / 2.0)));

                using var focus = (MagickImage)source.Clone();
                focus.Crop(new MagickGeometry(fx, fy, focusW, focusH));
                focus.RePage();
                PrepareForOcr(focus, focusScale);
                secondLookPasses++;
                return RunTile(engine, focus, fx, fy, focusW, focusH, 0, focusScale, mode).ToList();
            }
        }

        rotationPasses += secondLookPasses;
        return words;
    }

    private static byte[] CreateBmpFromBgra(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Nieprawidłowy rozmiar obrazu PDFium.");

        var stride = checked(width * 4);
        var expected = checked(stride * height);
        if (bgra.Length < expected)
            throw new InvalidDataException($"Bufor PDFium jest za krótki: {bgra.Length} B, oczekiwano co najmniej {expected} B.");

        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        var pixelOffset = fileHeaderSize + infoHeaderSize;
        var fileSize = checked(pixelOffset + expected);
        var bmp = new byte[fileSize];

        // BITMAPFILEHEADER
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32Le(bmp, 2, fileSize);
        WriteInt32Le(bmp, 10, pixelOffset);

        // BITMAPINFOHEADER. Negative height means top-down rows, matching PDFium's buffer.
        WriteInt32Le(bmp, 14, infoHeaderSize);
        WriteInt32Le(bmp, 18, width);
        WriteInt32Le(bmp, 22, -height);
        WriteInt16Le(bmp, 26, 1);
        WriteInt16Le(bmp, 28, 32);
        WriteInt32Le(bmp, 30, 0); // BI_RGB
        WriteInt32Le(bmp, 34, expected);

        Buffer.BlockCopy(bgra, 0, bmp, pixelOffset, expected);
        return bmp;
    }

    private static void WriteInt16Le(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32Le(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void PrepareForOcr(MagickImage image, double scale)
    {
        image.ColorSpace = ColorSpace.Gray;
        image.AutoLevel();
        image.ContrastStretch(new Percentage(0.5), new Percentage(0.5));
        image.Sharpen(0, 0.8);
        image.FilterType = FilterType.Lanczos;
        image.Resize(new Percentage(scale * 100.0));
        image.Format = MagickFormat.Png;
    }

    private static IEnumerable<(int X, int Y, int Width, int Height)> EnumerateTiles(int width, int height)
    {
        if (width <= TileSize && height <= TileSize)
        {
            yield return (0, 0, width, height);
            yield break;
        }

        var step = TileSize - TileOverlap;
        for (var y = 0; y < height; y += step)
        {
            var h = Math.Min(TileSize, height - y);
            var yy = h < TileSize ? Math.Max(0, height - TileSize) : y;
            h = Math.Min(TileSize, height - yy);

            for (var x = 0; x < width; x += step)
            {
                var w = Math.Min(TileSize, width - x);
                var xx = w < TileSize ? Math.Max(0, width - TileSize) : x;
                w = Math.Min(TileSize, width - xx);
                yield return (xx, yy, w, h);
                if (xx + w >= width) break;
            }
            if (yy + h >= height) break;
        }
    }

    private static IEnumerable<OcrWord> RunTile(TesseractEngine engine, MagickImage tile, int offsetX, int offsetY,
        int originalTileWidth, int originalTileHeight, int rotation, double scale, PageSegMode segmentationMode)
    {
        using var ms = new MemoryStream();
        tile.Write(ms, MagickFormat.Png);
        using var pix = Pix.LoadFromMemory(ms.ToArray());
        using var page = engine.Process(pix, segmentationMode);
        using var iter = page.GetIterator();
        iter.Begin();

        do
        {
            var text = iter.GetText(PageIteratorLevel.Word)?.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            var confidence = iter.GetConfidence(PageIteratorLevel.Word);
            if (confidence < NormalConfidenceFloor && !IsImportantToken(text) && !NumericRegex.IsMatch(text))
                continue;
            if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var box))
                continue;

            var x = box.X1 / scale;
            var y = box.Y1 / scale;
            var w = Math.Max(1, box.Width / scale);
            var h = Math.Max(1, box.Height / scale);

            double ox, oy, ow, oh;
            if (rotation == 90)
            {
                ox = y;
                oy = originalTileHeight - (x + w);
                ow = h;
                oh = w;
            }
            else if (rotation == 270)
            {
                ox = originalTileWidth - (y + h);
                oy = x;
                ow = h;
                oh = w;
            }
            else
            {
                ox = x; oy = y; ow = w; oh = h;
            }

            if (ox < -5 || oy < -5 || ox > originalTileWidth + 5 || oy > originalTileHeight + 5)
                continue;

            yield return new OcrWord(NormalizeOcrToken(text), offsetX + Math.Max(0, ox), offsetY + Math.Max(0, oy), ow, oh, confidence, rotation);
        }
        while (iter.Next(PageIteratorLevel.Word));
    }

    private static string NormalizeOcrToken(string text)
    {
        var t = text.Trim()
            .Replace('—', '-')
            .Replace('–', '-')
            .Replace('O', 'O');

        // Common OCR confusions only in identifier-like tokens.
        if (Regex.IsMatch(t, @"^(?:[DKSWP]{1,2})[Il|]\d", RegexOptions.IgnoreCase))
            t = Regex.Replace(t, @"([DKSWP]{1,2})[Il|](?=\d)", "$11", RegexOptions.IgnoreCase);

        // Common OCR confusions in pipe material labels on narrow profile drawings.
        t = Regex.Replace(t, @"^P[¥YV]C(?=\d)", "PVC", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"^PVCI(?=\d)", "PVC", RegexOptions.IgnoreCase);
        return t;
    }

    private static bool IsImportantToken(string text)
    {
        var compact = Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
        return CandidateRegex.IsMatch(compact);
    }

    private static List<OcrWord> Deduplicate(IEnumerable<OcrWord> input)
    {
        var result = new List<OcrWord>();
        foreach (var word in input
                     .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                     .OrderByDescending(w => IsImportantToken(w.Text))
                     .ThenByDescending(w => w.Confidence))
        {
            var normalized = NormalizeForDedup(word.Text);
            var duplicate = result.FirstOrDefault(x =>
                NormalizeForDedup(x.Text) == normalized &&
                Math.Abs((x.X + x.Width / 2) - (word.X + word.Width / 2)) <= Math.Max(16, word.Width * .55) &&
                Math.Abs((x.Y + x.Height / 2) - (word.Y + word.Height / 2)) <= Math.Max(12, word.Height * .75));
            if (duplicate == null)
                result.Add(word);
        }
        return result.OrderBy(w => w.Y).ThenBy(w => w.X).ToList();
    }

    private static string NormalizeForDedup(string text) => Regex.Replace(text.ToUpperInvariant(), @"[^A-Z0-9ĄĆĘŁŃÓŚŹŻØ./,-]", string.Empty);

    private static string BuildSpatialReadingText(IReadOnlyList<TextItem> words)
    {
        if (words.Count == 0) return string.Empty;
        var medianH = Median(words.Select(w => Math.Max(1.0, w.Height)));
        var tolerance = Math.Max(8.0, medianH * 1.1);
        var lines = new List<List<TextItem>>();

        foreach (var word in words.OrderBy(w => w.Y).ThenBy(w => w.X))
        {
            var cy = word.Y + word.Height / 2.0;
            var line = lines.FirstOrDefault(l => Math.Abs(l.Average(x => x.Y + x.Height / 2.0) - cy) <= tolerance);
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

    private static string GetCachePath(string filePath)
    {
        var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrefabScan", "ocr-cache");
        Directory.CreateDirectory(cacheRoot);
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = Convert.ToHexString(sha.ComputeHash(stream));
        return Path.Combine(cacheRoot, $"{OcrVersion}_{hash}.json");
    }

    private static bool TryLoadCache(string filePath, out List<PageText> pages)
    {
        pages = new List<PageText>();
        try
        {
            var path = GetCachePath(filePath);
            if (!File.Exists(path)) return false;
            var doc = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(path));
            if (doc == null || doc.Version != OcrVersion || doc.Pages.Count == 0) return false;
            pages = doc.Pages.Select(p =>
            {
                var pt = new PageText
                {
                    PageNumber = p.PageNumber,
                    Text = p.Text,
                    RawText = p.Text,
                    OrderedText = p.Text,
                    ExtractionEngine = "OCR/Tesseract cache",
                    ExtractionDiagnostics = "OCR_CACHE_HIT: wynik OCR odczytano z lokalnego cache.\r\n" + p.Diagnostics
                };
                pt.Items.AddRange(p.Items.Select(i => new TextItem { Text = i.Text, X = i.X, Y = i.Y, Width = i.Width, Height = i.Height }));
                return pt;
            }).ToList();
            return true;
        }
        catch { return false; }
    }

    private static void SaveCache(string filePath, IReadOnlyList<PageText> pages)
    {
        try
        {
            var doc = new CacheDocument
            {
                Pages = pages.Select(p => new CachePage
                {
                    PageNumber = p.PageNumber,
                    Text = p.Text,
                    Diagnostics = p.ExtractionDiagnostics,
                    Items = p.Items.Select(i => new CacheItem { Text = i.Text, X = i.X, Y = i.Y, Width = i.Width, Height = i.Height }).ToList()
                }).ToList()
            };
            File.WriteAllText(GetCachePath(filePath), JsonSerializer.Serialize(doc));
        }
        catch (Exception ex) { Debug.WriteLine($"OCR cache save failed: {ex.Message}"); }
    }
}
