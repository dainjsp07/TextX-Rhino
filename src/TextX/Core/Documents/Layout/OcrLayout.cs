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
        public static List<DocLine> BuildLines(OcrResult ocr, DocPage page, float scale, float minConfidence = 30f)
        {
            var lines = new List<DocLine>();
            foreach (var group in ocr.Words.Where(w => w.Confidence >= minConfidence || w.Text.Length > 2).GroupBy(w => w.Line))
            {
                var words = group.OrderBy(w => w.Left).ToList();
                if (words.Count == 0) continue;
                // split a line at wide gaps (separate labels on a drawing)
                var part = new List<OcrWord> { words[0] };
                for (int i = 1; i < words.Count; i++)
                {
                    int h = Math.Max(1, Math.Max(words[i].Bottom - words[i].Top, words[i - 1].Bottom - words[i - 1].Top));
                    if (words[i].Left - words[i - 1].Right > h * 2.2)
                    {
                        lines.Add(MakeLine(part, page, scale));
                        part = new List<OcrWord>();
                    }
                    part.Add(words[i]);
                }
                lines.Add(MakeLine(part, page, scale));
            }
            // top to bottom, then left to right (the order Tesseract reports blocks can jump around)
            return lines.OrderByDescending(l => Math.Round(l.Baseline / Math.Max(2f, l.FontSize * 0.5f))).ThenBy(l => l.Box.L).ToList();
        }

        private static DocLine MakeLine(List<OcrWord> words, DocPage page, float scale)
        {
            bool rtl = words.Count(w => RtlText.ContainsRtl(w.Text)) * 2 > words.Count;
            var ordered = rtl ? words.OrderByDescending(w => w.Right).ToList() : words;
            var sb = new StringBuilder();
            foreach (var w in ordered)
            {
                if (sb.Length > 0 && !(LayoutAnalyzer.IsCjk(sb[sb.Length - 1]) && w.Text.Length > 0 && LayoutAnalyzer.IsCjk(w.Text[0]))) sb.Append(' ');
                sb.Append(w.Text);
            }
            int left = words.Min(w => w.Left), right = words.Max(w => w.Right);
            int top = words.Min(w => w.Top), bottom = words.Max(w => w.Bottom);
            // median word height is a steadier estimate than the line box (ascenders, descenders)
            float heightPx = LayoutAnalyzer.Median(words.Select(w => (float)(w.Bottom - w.Top)));
            float x1, y1, x2, y2;
            page.DisplayToFrame(left / scale, top / scale, out x1, out y1);
            page.DisplayToFrame(right / scale, bottom / scale, out x2, out y2);
            var box = new Box(x1, y1, x2, y2);
            // word boxes are tight around the ink: about 0.74 of the font size for typical words
            float size = Math.Max(4f, heightPx / scale * 1.35f);
            var line = new DocLine
            {
                Text = sb.ToString(),
                Box = box,
                FontSize = size,
                Rtl = rtl,
                Color = 0xFF000000,
                FontName = string.Empty,
            };
            // baseline ~ 20% of the text height above the bottom of the box
            line.Baseline = box.B + box.H * 0.2f;
            line.OriginX = box.L;
            line.OriginY = line.Baseline;
            line.Advance = box.W;
            return line;
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
