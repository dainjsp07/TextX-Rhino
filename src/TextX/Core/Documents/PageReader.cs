using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TextX.Core.Documents.Layout;
using TextX.Core.Documents.Native;
using TextX.Core.Documents.Ocr;

namespace TextX.Core.Documents
{
    public sealed class ReadOptions
    {
        /// <summary>Language of the document ("auto" to detect), used to pick OCR language data.</summary>
        public string SourceLang = Languages.AutoDetect;
        /// <summary>Read image-only pages with OCR (needs the OCR engine).</summary>
        public bool AllowOcr = true;
        /// <summary>OCR resolution for normal pages; large sheets are read at a lower resolution to limit memory.</summary>
        public int OcrDpi = 300;
        /// <summary>Find text anywhere on the page (drawings) rather than in columns.</summary>
        public bool SparseOcr;
        /// <summary>Reports what is happening ("Downloading OCR data heb", "Reading page 3 with OCR").</summary>
        public IProgress<string> Status;
    }

    /// <summary>Reads one page of a PDF into a <see cref="DocPage"/>: text layer, or OCR for scanned pages.</summary>
    public static class PageReader
    {
        private const long MaxOcrPixels = 25L * 1000 * 1000;

        public static async Task<DocPage> ReadAsync(PdfFile file, int index, ReadOptions options, CancellationToken ct)
        {
            options = options ?? new ReadOptions();
            DocPage page;
            List<PdfChar> chars;
            bool scanned, ocrLayer;
            RenderedImage background = null;
            using (var p = file.LoadPage(index))
            {
                page = new DocPage { Index = index, Width = p.Width, Height = p.Height, Rotation = p.Rotation, OriginX = p.OriginX, OriginY = p.OriginY };
                chars = p.GetChars();
                var objects = p.GetObjects();
                float pageArea = Math.Max(1f, page.Width * page.Height);
                float maxImage = 0;
                foreach (var o in objects.Where(o => o.IsImage))
                {
                    var box = FrameBox(page, o.Left, o.Bottom, o.Right, o.Top);
                    var clipped = new Box(Math.Max(0, box.L), Math.Max(0, box.B), Math.Min(page.Width, box.R), Math.Min(page.Height, box.T));
                    if (clipped.W <= 0 || clipped.H <= 0) continue;
                    float area = clipped.W * clipped.H;
                    maxImage = Math.Max(maxImage, area / pageArea);
                    if (area / pageArea >= 0.005f) page.Images.Add(clipped);
                }
                var letters = chars.Where(c => !c.Generated && c.Code > 32 && c.Code < 0x10FFFF && (char.IsLetterOrDigit(Safe(c.Code)) || c.Code > 0x2E80)).ToList();
                int invisible = 0;
                if (maxImage > 0.35f && letters.Count > 0)
                {
                    var objectsOfText = letters.Where(c => c.TextObject != IntPtr.Zero).GroupBy(c => c.TextObject).ToList();
                    foreach (var g in objectsOfText) if (PdfPage.IsInvisibleText(g.Key)) invisible += g.Count();
                }
                scanned = letters.Count < 12 && maxImage > 0.35f;
                ocrLayer = !scanned && maxImage > 0.35f && invisible > letters.Count * 0.6;
                if (scanned && options.AllowOcr && OcrEngine.IsAvailable)
                {
                    float dpi = options.OcrDpi;
                    double inches2 = page.Width / 72.0 * page.Height / 72.0;
                    if (inches2 * dpi * dpi > MaxOcrPixels) dpi = (float)Math.Sqrt(MaxOcrPixels / inches2);
                    background = p.Render(dpi / 72f);
                }
                else if (ocrLayer)
                {
                    background = p.Render(1f);
                }
            }

            page.Scanned = scanned;
            page.HasOcrLayer = ocrLayer;
            if (!scanned)
            {
                var frameChars = chars.Select(c => ToFrame(c, page)).ToList();
                var lines = LayoutAnalyzer.BuildLines(frameChars);
                page.Blocks = LayoutAnalyzer.BuildBlocks(page, lines);
                if (ocrLayer && background != null)
                {
                    foreach (var b in page.Blocks)
                    {
                        b.FromOcr = true;
                        b.Background = OcrLayout.BackgroundAround(Info(background), page, b.Box, 1f);
                    }
                }
            }
            else if (background != null)
            {
                float scale = background.Width / page.Width;
                int dpi = (int)Math.Round(scale * 72);
                var gray = background.ToGray();
                string languages = OcrLanguages.ForLanguage(options.SourceLang);
                if (languages == null)
                {
                    await OcrEngine.EnsureDataAsync("osd", Report(options, "Downloading OCR data"), ct).ConfigureAwait(false);
                    if (options.Status != null) options.Status.Report("Detecting the script of page " + (index + 1));
                    ScriptDetection script = null;
                    try { script = OcrEngine.DetectScript(gray, background.Width, background.Height, dpi); }
                    catch (Exception) { script = null; }
                    languages = OcrLanguages.ForScript(script == null ? null : script.Script);
                }
                await OcrEngine.EnsureDataAsync(languages, Report(options, "Downloading OCR data"), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (options.Status != null) options.Status.Report("Reading page " + (index + 1) + " with OCR");
                var ocr = await Task.Run(() => OcrEngine.Recognize(gray, background.Width, background.Height, dpi, languages, options.SparseOcr), ct).ConfigureAwait(false);
                var lines = OcrLayout.BuildLines(ocr, page, scale);
                page.Blocks = LayoutAnalyzer.BuildBlocks(page, lines);
                var info = Info(background);
                foreach (var b in page.Blocks)
                {
                    b.FromOcr = true;
                    b.Background = OcrLayout.BackgroundAround(info, page, b.Box, scale);
                }
                page.OcrConfidence = ocr.MeanConfidence;
                page.OcrLanguages = languages;
            }
            page.Extracted = true;
            return page;
        }

        private static IProgress<string> Report(ReadOptions o, string prefix)
        {
            if (o.Status == null) return null;
            return new Progress<string>(lang => o.Status.Report(prefix + " (" + lang + ")..."));
        }

        private static RenderedImageInfo Info(RenderedImage img)
        {
            return new RenderedImageInfo { Width = img.Width, Height = img.Height, Pixels = img.Pixels };
        }

        private static char Safe(int code) { return code < 0xD800 ? (char)code : 'x'; }

        private static Box FrameBox(DocPage page, float l, float b, float r, float t)
        {
            float x1, y1, x2, y2;
            page.UserToFrame(l, b, out x1, out y1);
            page.UserToFrame(r, t, out x2, out y2);
            return new Box(x1, y1, x2, y2);
        }

        /// <summary>A character in page-frame coordinates (rotation removed).</summary>
        public static PdfChar ToFrame(PdfChar c, DocPage page)
        {
            if (page.Rotation == 0 && page.OriginX == 0 && page.OriginY == 0) return c;
            var box = FrameBox(page, c.Left, c.Bottom, c.Right, c.Top);
            c.Left = box.L; c.Bottom = box.B; c.Right = box.R; c.Top = box.T;
            float ox, oy;
            page.UserToFrame(c.OriginX, c.OriginY, out ox, out oy);
            c.OriginX = ox; c.OriginY = oy;
            c.Angle -= page.FrameToUserAngle;
            return c;
        }
    }
}
