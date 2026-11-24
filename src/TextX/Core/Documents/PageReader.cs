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
        /// <summary>Read drawings whose words were exported as outlines (no real text) with OCR.</summary>
        public bool ReadOutlinedText = true;
        /// <summary>Language seen elsewhere in the document (auto mode): picks the OCR data when a page has no text to go by.</summary>
        public string LanguageHint;
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
            bool scanned, ocrLayer, outlined;
            RenderedImage background = null;
            PageGeometry geometry;
            using (var p = file.LoadPage(index))
            {
                page = new DocPage { Index = index, Width = p.Width, Height = p.Height, Rotation = p.Rotation, OriginX = p.OriginX, OriginY = p.OriginY };
                chars = p.GetChars();
                var objects = p.GetObjects();
                // ruled lines keep neighbouring cells apart when the lines of text are put together
                geometry = PageGeometry.From(page, objects);
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
                outlined = !scanned && !ocrLayer && options.ReadOutlinedText && LooksOutlined(letters, objects);
                bool ocr = (scanned || outlined) && options.AllowOcr && OcrEngine.IsAvailable;
                if (ocr)
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
                if (!ocr) outlined = false;
            }

            page.Scanned = scanned;
            page.HasOcrLayer = ocrLayer;
            if (!scanned)
            {
                var frameChars = chars.Select(c => ToFrame(c, page)).ToList();
                var lines = LayoutAnalyzer.BuildLines(frameChars, geometry);
                if (outlined && background != null)
                {
                    // a drawing whose words were turned into outlines (CAD export): read them with OCR,
                    // except where the page has real text
                    if (options.Status != null) options.Status.Report("Reading the drawing text of page " + (index + 1) + " with OCR");
                    var info = Info(background);
                    page.Blocks = LayoutAnalyzer.BuildBlocks(page, lines, 0, geometry);
                    var ocrLines = await OcrLinesAsync(page, background, info, options, true, ct, true).ConfigureAwait(false);
                    ocrLines = ocrLines.Where(o => !lines.Any(l => Overlaps(l.Box, o.Box))).ToList();
                    page.Blocks = LayoutAnalyzer.BuildBlocks(page, lines.Concat(ocrLines).ToList(), 0, geometry);
                    float scale = background.Width / page.Width;
                    foreach (var b in page.Blocks.Where(b => b.Lines.All(l => l.Chars.Count == 0)))
                    {
                        b.FromOcr = true;
                        b.Background = OcrLayout.BackgroundAround(info, page, b.Box, scale);
                    }
                    page.OutlinedText = page.Blocks.Any(b => b.FromOcr);
                }
                else
                {
                    page.Blocks = LayoutAnalyzer.BuildBlocks(page, lines, 0, geometry);
                }
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
                if (options.Status != null) options.Status.Report("Reading page " + (index + 1) + " with OCR");
                var info = Info(background);
                var lines = await OcrLinesAsync(page, background, info, options, options.SparseOcr, ct).ConfigureAwait(false);
                float scale = background.Width / page.Width;
                page.Blocks = LayoutAnalyzer.BuildBlocks(page, lines);
                foreach (var b in page.Blocks)
                {
                    b.FromOcr = true;
                    b.Background = OcrLayout.BackgroundAround(info, page, b.Box, scale);
                }
            }
            page.Extracted = true;
            page.LayoutVersion = DocPage.CurrentLayout;
            return page;
        }

        /// <summary>
        /// A drawing with (almost) no real words but thousands of small shapes: CAD programs often export
        /// their text as outlines, which only OCR can read.
        /// </summary>
        internal static bool LooksOutlined(List<PdfChar> letters, List<PdfObjectInfo> objects)
        {
            int words = letters.Count(c => char.IsLetter(Safe(c.Code)));
            if (words >= 80) return false;
            int glyphLike = objects.Count(o => o.IsPath && o.Width > 0.8f && o.Height > 0.8f && o.Width < 12f && o.Height < 12f);
            return glyphLike >= 600;
        }

        private static bool Overlaps(Box a, Box b)
        {
            float w = Math.Min(a.R, b.R) - Math.Max(a.L, b.L), h = Math.Min(a.T, b.T) - Math.Max(a.B, b.B);
            if (w <= 0 || h <= 0) return false;
            return w * h > 0.3f * Math.Max(1f, Math.Min(a.W * a.H, b.W * b.H));
        }

        /// <summary>OCR of a rendered page (language detected from the script when not given) as page lines.</summary>
        private static async Task<List<DocLine>> OcrLinesAsync(DocPage page, RenderedImage background, RenderedImageInfo info, ReadOptions options, bool sparse, CancellationToken ct, bool drawing = false)
        {
            float scale = background.Width / page.Width;
            int dpi = (int)Math.Round(scale * 72);
            var gray = background.ToGray();
            string languages = OcrLanguages.ForLanguage(options.SourceLang);
            // auto: the page's own real text, then the rest of the document (and its file name), then the script
            // seen on the image. A drawing's real text is often only its English title block while the drawing's
            // own words (outlines) are Hebrew: real text decides only when it is in a script other than Latin.
            if (languages == null) languages = OcrLanguages.ForLanguage(LanguageGuess.FromScript(string.Join(" ", page.Blocks.Where(b => !b.FromOcr).Select(b => b.Source))));
            if (languages == null) languages = OcrLanguages.ForLanguage(options.LanguageHint);
            if (languages == null)
            {
                await OcrEngine.EnsureDataAsync("osd", Report(options, "Downloading OCR data"), ct).ConfigureAwait(false);
                if (options.Status != null) options.Status.Report("Detecting the script of page " + (page.Index + 1));
                ScriptDetection script = null;
                try { script = OcrEngine.DetectScript(gray, background.Width, background.Height, dpi); }
                catch (Exception) { script = null; }
                // line drawings mislead script detection: trust it when it is sure, or when it sees a script other
                // than Latin at all (its model also reads English, so a wrong guess costs little; reading Hebrew
                // with the Latin model gives nonsense like "PTNINW" and nothing gets translated)
                bool sure = script != null && (!sparse || script.ScriptConfidence >= 1.5f);
                bool otherScript = script != null && !string.IsNullOrEmpty(script.Script) && script.Script != "Latin" && script.ScriptConfidence >= 0.3f;
                if (options.Status != null && script != null) options.Status.Report("Script " + script.Script + " (" + script.ScriptConfidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")");
                languages = OcrLanguages.ForScript(sure || otherScript ? script.Script : null);
            }
            await OcrEngine.EnsureDataAsync(languages, Report(options, "Downloading OCR data"), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var ocr = await Task.Run(() => OcrEngine.Recognize(gray, background.Width, background.Height, dpi, languages, sparse), ct).ConfigureAwait(false);
            page.OcrConfidence = ocr.MeanConfidence;
            page.OcrLanguages = languages;
            return OcrLayout.BuildLines(ocr, page, scale, 30f, info, drawing);
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
            c.Angle = Pdfium.NormalizeAngle(c.Angle - page.FrameToUserAngle);
            return c;
        }

        /// <summary>
        /// A page read again by a newer version keeps what was done on it: every paragraph whose text is
        /// unchanged keeps its translation, the user's edits and remarks.
        /// </summary>
        public static void CarryOver(DocPage old, DocPage fresh)
        {
            if (old == null || fresh == null || old.Blocks == null || old.Blocks.Count == 0 || ReferenceEquals(old, fresh)) return;
            var done = new Dictionary<string, Queue<DocBlock>>(StringComparer.Ordinal);
            foreach (var b in old.Blocks.OrderBy(b => b.Order))
            {
                if (b.State == BlockState.Pending || string.IsNullOrEmpty(b.Source)) continue;
                Queue<DocBlock> q;
                if (!done.TryGetValue(b.Source, out q)) done[b.Source] = q = new Queue<DocBlock>();
                q.Enqueue(b);
            }
            foreach (var b in fresh.Blocks.OrderBy(b => b.Order))
            {
                Queue<DocBlock> q;
                if (b.Source == null || !done.TryGetValue(b.Source, out q) || q.Count == 0) continue;
                var o = q.Dequeue();
                b.Translation = o.Translation;
                b.State = o.State;
                b.Note = o.Note;
            }
        }
    }
}
