using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TextX.Core.Documents.Ocr;
using TextX.Core.Text;

namespace TextX.Core.Documents.Layout
{
    /// <summary>Turns OCR words (pixels of a page render) into page lines in PDF user space.</summary>
    public static class OcrLayout
    {
        /// <param name="scale">Pixels per point of the image that was read.</param>
        /// <param name="image">The page image that was read: when given, each line's ink colour and weight (bold) are measured.</param>
        public static List<DocLine> BuildLines(OcrResult ocr, DocPage page, float scale, float minConfidence = 30f, RenderedImageInfo image = null, bool drawing = false)
        {
            var lines = new List<DocLine>();
            foreach (var w in ocr.Words) w.Text = StripMarks(w.Text);
            if (drawing)
            {
                foreach (var group in ocr.Words.Where(w => w.Text.Length > 0).GroupBy(w => w.Line))
                    lines.AddRange(DrawingLines(group.OrderBy(w => w.Left).ToList(), page, scale, image));
                return lines.OrderByDescending(l => Math.Round(l.Baseline / Math.Max(2f, l.FontSize * 0.5f))).ThenBy(l => l.Box.L).ToList();
            }
            foreach (var group in ocr.Words.Where(w => w.Text.Length > 0 && (w.Confidence >= minConfidence || w.Text.Length > 2)).GroupBy(w => w.Line))
            {
                var words = group.OrderBy(w => w.Left).ToList();
                if (words.Count == 0) continue;
                if (words.Count == 0) continue;
                // split a line at wide gaps (separate labels on a drawing)
                var part = new List<OcrWord> { words[0] };
                for (int i = 1; i < words.Count; i++)
                {
                    int h = Math.Max(1, Math.Max(words[i].Bottom - words[i].Top, words[i - 1].Bottom - words[i - 1].Top));
                    if (words[i].Left - words[i - 1].Right > h * 2.2)
                    {
                        lines.Add(MakeLine(part, page, scale, image));
                        part = new List<OcrWord>();
                    }
                    part.Add(words[i]);
                }
                lines.Add(MakeLine(part, page, scale, image));
            }
            // top to bottom, then left to right (the order Tesseract reports blocks can jump around)
            return lines.OrderByDescending(l => Math.Round(l.Baseline / Math.Max(2f, l.FontSize * 0.5f))).ThenBy(l => l.Box.L).ToList();
        }

        /// <summary>Removes the direction marks Tesseract puts around left-to-right words in right-to-left lines.</summary>
        internal static string StripMarks(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
                if (!(c == '\u200E' || c == '\u200F' || c == '\u061C' || (c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069'))) sb.Append(c);
            return sb.ToString().Trim();
        }

        /// <summary>
        /// On a drawing, OCR also "reads" hatching, arrows and line work. Keeps words with letters or
        /// digits that the engine is fairly sure of; in a right-to-left line, unsure Latin words are
        /// misread letters rather than real English.
        /// </summary>
        internal static List<OcrWord> DrawingWords(List<OcrWord> words)
        {
            bool rtlLine = words.Count(w => RtlText.ContainsRtl(w.Text)) * 2 >= words.Count && words.Any(w => RtlText.ContainsRtl(w.Text));
            return words.Where(w =>
            {
                int letters = w.Text.Count(char.IsLetter), digits = w.Text.Count(char.IsDigit);
                if (letters + digits == 0) return false;
                if (letters + digits == 1 && w.Confidence < 85) return false;
                // a Hebrew word read with some doubt is still worth translating: dropping it leaves it untranslated
                bool rtlWord = RtlText.ContainsRtl(w.Text);
                if (rtlWord && w.Text.Count(c => RtlText.ContainsRtl(c.ToString())) >= 2 && w.Confidence >= 10) return true;
                if (w.Confidence < 55) return false;
                bool latinOnly = letters > 0 && digits == 0 && !RtlText.ContainsRtl(w.Text);
                if (rtlLine && latinOnly && w.Confidence < 85) return false;
                if (latinOnly && letters <= 3 && w.Confidence < 75) return false;
                return true;
            }).ToList();
        }

        /// <summary>
        /// The labels of one OCR line on a drawing. A word left out (unsure, or English misread in a Hebrew
        /// line) still holds its place: the line is not split at the hole it leaves, and its box still covers
        /// it, so no untranslated Hebrew is left showing between the translated words.
        /// </summary>
        private static List<DocLine> DrawingLines(List<OcrWord> words, DocPage page, float scale, RenderedImageInfo image)
        {
            var result = new List<DocLine>();
            var keep = new HashSet<OcrWord>(DrawingWords(words));
            if (keep.Count == 0) return result;
            float keptHeight = LayoutAnalyzer.Median(keep.Select(w => (float)Math.Max(1, w.Bottom - w.Top)));
            Func<OcrWord, bool> holdsPlace = w =>
            {
                if (keep.Contains(w)) return true;
                int h = w.Bottom - w.Top;
                return w.Text.Count(char.IsLetterOrDigit) >= 2 && h >= keptHeight * 0.5f && h <= keptHeight * 1.8f;
            };
            var placed = words.Where(holdsPlace).ToList();
            var part = new List<OcrWord>();
            Action flush = () =>
            {
                var kept = part.Where(keep.Contains).ToList();
                if (kept.Count > 0) result.Add(MakeLine(kept, page, scale, image, part));
                part = new List<OcrWord>();
            };
            for (int i = 0; i < placed.Count; i++)
            {
                if (part.Count > 0)
                {
                    var prev = part[part.Count - 1];
                    int h = Math.Max(1, Math.Max(placed[i].Bottom - placed[i].Top, prev.Bottom - prev.Top));
                    if (placed[i].Left - prev.Right > h * 2.2) flush();
                }
                part.Add(placed[i]);
            }
            flush();
            return result;
        }

        private static DocLine MakeLine(List<OcrWord> words, DocPage page, float scale, RenderedImageInfo image, List<OcrWord> area = null)
        {
            bool rtl = words.Count(w => RtlText.ContainsRtl(w.Text)) * 2 > words.Count;
            var ordered = rtl ? words.OrderByDescending(w => w.Right).ToList() : words;
            var sb = new StringBuilder();
            foreach (var w in ordered)
            {
                if (sb.Length > 0 && !(LayoutAnalyzer.IsCjk(sb[sb.Length - 1]) && w.Text.Length > 0 && LayoutAnalyzer.IsCjk(w.Text[0]))) sb.Append(' ');
                sb.Append(w.Text);
            }
            // the box spans the words read and, on a drawing, those left out between them (to be covered too)
            var span = area != null && area.Count > 0 ? area : words;
            int left = span.Min(w => w.Left), right = span.Max(w => w.Right);
            int top = words.Min(w => w.Top), bottom = words.Max(w => w.Bottom);
            float x1, y1, x2, y2;
            page.DisplayToFrame(left / scale, top / scale, out x1, out y1);
            page.DisplayToFrame(right / scale, bottom / scale, out x2, out y2);
            var box = new Box(x1, y1, x2, y2);
            // A word box is tight around the ink, so its height depends on the letters: "acme" is only
            // the x-height, "Type" spans capital to descender. Each word's letters say which part of the
            // em its box covers, giving its font size and where its baseline sits.
            var sizes = new List<float>();
            var baselines = new List<float>(); // pixels from the top of the page image
            foreach (var w in words)
            {
                float above, below;
                InkExtent(w.Text, out above, out below);
                float h = w.Bottom - w.Top;
                if (h <= 0 || above + below <= 0) continue;
                float em = h / (above + below);
                sizes.Add(em);
                baselines.Add(w.Bottom - below * em);
            }
            float sizePx = sizes.Count > 0 ? LayoutAnalyzer.Median(sizes) : LayoutAnalyzer.Median(words.Select(w => (float)(w.Bottom - w.Top))) * 1.35f;
            float baselinePx = baselines.Count > 0 ? LayoutAnalyzer.Median(baselines) : bottom - (bottom - top) * 0.2f;
            float bx, by;
            page.DisplayToFrame(left / scale, baselinePx / scale, out bx, out by);
            var line = new DocLine
            {
                Text = sb.ToString(),
                Box = box,
                FontSize = Math.Max(4f, sizePx / scale),
                Rtl = rtl,
                Color = 0xFF000000,
                FontName = string.Empty,
            };
            line.Baseline = by;
            if (image != null)
            {
                uint ink;
                bool bold;
                if (InkStyle(image, left, top, right, bottom, sizePx, out ink, out bold)) { line.Color = ink; line.Bold = bold; }
            }
            line.OriginX = box.L;
            line.OriginY = line.Baseline;
            line.Advance = box.W;
            return line;
        }

        /// <summary>
        /// The colour of the text in a region of the page image (the strongest-contrast pixels, so
        /// anti-aliased edges do not wash it out) and whether it is bold, from the typical width of
        /// its strokes: about 0.09 of the font size for regular text, 0.14 or more for bold.
        /// </summary>
        internal static bool InkStyle(RenderedImageInfo img, int left, int top, int right, int bottom, float emPx, out uint ink, out bool bold)
        {
            ink = 0xFF000000; bold = false;
            left = Math.Max(0, left); top = Math.Max(0, top);
            right = Math.Min(img.Width - 1, right); bottom = Math.Min(img.Height - 1, bottom);
            if (right - left < 3 || bottom - top < 3 || emPx < 6) return false;
            // background: the lighter (or, for light text, darker) majority of the box
            var lumas = new List<int>();
            int step = Math.Max(1, (right - left) * (bottom - top) / 40000);
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x += step)
                    lumas.Add(LumaAt(img, x, y));
            lumas.Sort();
            int bg = lumas[lumas.Count * 3 / 4];
            int darkest = lumas[0], lightest = lumas[lumas.Count - 1];
            bool lightText = bg - darkest < lightest - lumas[lumas.Count / 4] && lumas[lumas.Count / 4] < 110;
            if (lightText) bg = lumas[lumas.Count / 4];
            int threshold = Math.Max(60, Math.Abs((lightText ? lightest : darkest) - bg) / 2);
            Func<int, bool> isInk = l => lightText ? l - bg > threshold : bg - l > threshold;

            var reds = new List<int>(); var greens = new List<int>(); var blues = new List<int>();
            var runs = new List<int>();
            for (int y = top; y <= bottom; y++)
            {
                int run = 0;
                for (int x = left; x <= right + 1; x++)
                {
                    bool on = x <= right && isInk(LumaAt(img, x, y));
                    if (on)
                    {
                        run++;
                        int p = (y * img.Width + x) * 4;
                        int l = LumaAt(img, x, y);
                        if (Math.Abs(l - bg) > threshold * 1.5) { blues.Add(img.Pixels[p]); greens.Add(img.Pixels[p + 1]); reds.Add(img.Pixels[p + 2]); }
                    }
                    else if (run > 0) { runs.Add(run); run = 0; }
                }
            }
            if (runs.Count < 8) return false;
            if (reds.Count > 0)
            {
                reds.Sort(); greens.Sort(); blues.Sort();
                ink = 0xFF000000u | (uint)(reds[reds.Count / 2] << 16) | (uint)(greens[greens.Count / 2] << 8) | (uint)blues[blues.Count / 2];
            }
            runs.Sort();
            float stem = runs[runs.Count / 2];
            bold = stem / emPx >= 0.125f;
            return true;
        }

        private static int LumaAt(RenderedImageInfo img, int x, int y)
        {
            int p = (y * img.Width + x) * 4;
            return (img.Pixels[p + 2] * 77 + img.Pixels[p + 1] * 150 + img.Pixels[p] * 29) >> 8;
        }

        /// <summary>
        /// How far a word's ink reaches above and below the baseline, as shares of the font size,
        /// judged from its letters (typical proportions of text fonts).
        /// </summary>
        internal static void InkExtent(string text, out float above, out float below)
        {
            above = 0; below = 0;
            bool any = false;
            foreach (var ch in text ?? string.Empty)
            {
                float a, b;
                if (!CharExtent(ch, out a, out b)) continue;
                any = true;
                if (a > above) above = a;
                if (b > below) below = b;
            }
            if (!any) { above = 0.72f; below = 0.05f; }
        }

        private static bool CharExtent(char c, out float above, out float below)
        {
            above = 0; below = 0;
            if (char.IsWhiteSpace(c)) return false;
            if (LayoutAnalyzer.IsCjk(c) || (c >= 0xAC00 && c <= 0xD7AF)) { above = 0.84f; below = 0.1f; return true; }
            if (c >= 0x0590 && c <= 0x06FF) { above = 0.7f; below = 0.25f; return true; } // Hebrew, Arabic
            if ("gjpqy\u00E7\u00FF\u0105\u0119\u015F\u0163".IndexOf(c) >= 0) { above = c == 'j' ? 0.72f : 0.52f; below = 0.21f; if (c == '\u00FF') above = 0.72f; return true; }
            if (c == ',' || c == ';') { above = 0.1f; below = 0.15f; return true; }
            if (c == '.' || c == '_' ) { above = 0.1f; below = 0f; return true; }
            if (c == '-' || c == '\u2013' || c == '\u2014' || c == '=' || c == '~' || c == '*' || c == '"' || c == '\'' || c == '\u2019' || c == '\u201C' || c == '\u201D' || c == '\u00B0') return false; // floating marks
            if ("()[]{}|/\\".IndexOf(c) >= 0) { above = 0.73f; below = 0.21f; return true; }
            if ("Q".IndexOf(c) >= 0) { above = 0.72f; below = 0.05f; return true; }
            if (char.IsUpper(c))
            {
                above = c > 0x7F && "\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u00C8\u00C9\u00CA\u00CB\u00CC\u00CD\u00CE\u00CF\u00D2\u00D3\u00D4\u00D5\u00D6\u00D9\u00DA\u00DB\u00DC\u00D1\u0152".IndexOf(c) >= 0 ? 0.9f : 0.72f;
                if (c == '\u00C7') below = 0.21f;
                return true;
            }
            if (char.IsDigit(c)) { above = 0.72f; return true; }
            if ("bdfhklt\u00DF".IndexOf(c) >= 0) { above = c == 't' ? 0.66f : 0.73f; return true; }
            if (c == 'i') { above = 0.72f; return true; }
            if ("\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u00E8\u00E9\u00EA\u00EB\u00EC\u00ED\u00EE\u00EF\u00F2\u00F3\u00F4\u00F5\u00F6\u00F9\u00FA\u00FB\u00FC\u00F1\u0153".IndexOf(c) >= 0) { above = 0.74f; return true; }
            if (char.IsLower(c)) { above = 0.52f; return true; }
            if (char.IsLetter(c)) { above = 0.72f; below = 0.05f; return true; }   // other scripts: average
            if ("%&@#$\u20AC\u00A3?!".IndexOf(c) >= 0) { above = 0.72f; below = c == '@' ? 0.2f : 0.03f; return true; }
            return false;
        }

        /// <summary>
        /// The colour behind a block on a rendered page: the median of the pixels just outside the
        /// box. Used to cover scanned text before the translation is written over it.
        /// </summary>
        public static uint BackgroundAround(RenderedImageInfo img, DocPage page, Box box, float scale)
        {
            float l, t, w, h;
            page.DisplayRect(box, out l, out t, out w, out h);
            int x0 = (int)(l * scale) - 3, y0 = (int)(t * scale) - 3, x1 = (int)((l + w) * scale) + 3, y1 = (int)((t + h) * scale) + 3;
            var samples = new List<int>();
            for (int x = x0; x <= x1; x += Math.Max(1, (x1 - x0) / 40))
            {
                Sample(img, x, y0, samples);
                Sample(img, x, y1, samples);
            }
            for (int y = y0; y <= y1; y += Math.Max(1, (y1 - y0) / 20))
            {
                Sample(img, x0, y, samples);
                Sample(img, x1, y, samples);
            }
            if (samples.Count == 0) return 0xFFFFFFFF;
            samples.Sort((a, b) => Luma(a).CompareTo(Luma(b)));
            int m = samples[samples.Count / 2];
            return 0xFF000000u | (uint)m;
        }

        private static int Luma(int rgb) { return ((rgb >> 16) & 255) * 77 + ((rgb >> 8) & 255) * 150 + (rgb & 255) * 29; }

        private static void Sample(RenderedImageInfo img, int x, int y, List<int> samples)
        {
            if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) return;
            int p = (y * img.Width + x) * 4;
            samples.Add((img.Pixels[p + 2] << 16) | (img.Pixels[p + 1] << 8) | img.Pixels[p]);
        }
    }

    /// <summary>The pixels of a page render (BGRA, top row first).</summary>
    public sealed class RenderedImageInfo
    {
        public int Width, Height;
        public byte[] Pixels;
    }
}
