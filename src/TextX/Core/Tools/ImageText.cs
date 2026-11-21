using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TextX.Core.Documents.Layout;
using TextX.Core.Documents.Ocr;

namespace TextX.Core.Tools
{
    /// <summary>A picture to read: grey pixels (0 = black) with its size.</summary>
    public sealed class GrayImage
    {
        public GrayImage(byte[] pixels, int width, int height)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
        }

        public byte[] Pixels { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>From 32-bit BGRA pixels (rows of width*4 bytes, or <paramref name="stride"/>). Transparent pixels count as white.</summary>
        public static GrayImage FromBgra(byte[] bgra, int width, int height, int stride = 0)
        {
            if (stride <= 0) stride = width * 4;
            var g = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int p = row + x * 4;
                    int a = bgra[p + 3];
                    int l = (bgra[p] * 29 + bgra[p + 1] * 150 + bgra[p + 2] * 77) >> 8;
                    g[y * width + x] = (byte)((l * a + 255 * (255 - a)) / 255);
                }
            }
            return new GrayImage(g, width, height);
        }

        /// <summary>Bilinear resize (screenshots are enlarged so their small text reads reliably).</summary>
        public GrayImage Scale(double factor)
        {
            int w = Math.Max(1, (int)Math.Round(Width * factor)), h = Math.Max(1, (int)Math.Round(Height * factor));
            var dst = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                double sy = Math.Max(0, Math.Min(Height - 1, (y + 0.5) / factor - 0.5));
                int y0 = (int)sy, y1 = Math.Min(Height - 1, y0 + 1);
                double fy = sy - y0;
                for (int x = 0; x < w; x++)
                {
                    double sx = Math.Max(0, Math.Min(Width - 1, (x + 0.5) / factor - 0.5));
                    int x0 = (int)sx, x1 = Math.Min(Width - 1, x0 + 1);
                    double fx = sx - x0;
                    double top = Pixels[y0 * Width + x0] * (1 - fx) + Pixels[y0 * Width + x1] * fx;
                    double bottom = Pixels[y1 * Width + x0] * (1 - fx) + Pixels[y1 * Width + x1] * fx;
                    dst[y * w + x] = (byte)Math.Round(top * (1 - fy) + bottom * fy);
                }
            }
            return new GrayImage(dst, w, h);
        }

        /// <summary>Light text on a dark background (dark mode screenshots) is turned into dark on light.</summary>
        public GrayImage DarkOnLight()
        {
            long sum = 0;
            int step = Math.Max(1, Pixels.Length / 20000);
            int n = 0;
            for (int i = 0; i < Pixels.Length; i += step) { sum += Pixels[i]; n++; }
            if (n == 0 || sum / n >= 110) return this;
            var inv = new byte[Pixels.Length];
            for (int i = 0; i < inv.Length; i++) inv[i] = (byte)(255 - Pixels[i]);
            return new GrayImage(inv, Width, Height);
        }
    }

    public sealed class ImageTextResult
    {
        public string Text = string.Empty;
        public string Languages = string.Empty;
        public float Confidence;
        public int Words;
    }

    /// <summary>Text from pictures and screenshots (OCR with Tesseract, on this computer).</summary>
    public static class ImageText
    {
        /// <param name="language">Text X language code of the text, or null / "auto" to detect the alphabet.</param>
        public static async Task<ImageTextResult> ReadAsync(GrayImage image, string language, IProgress<string> status, CancellationToken ct)
        {
            if (image == null || image.Width < 8 || image.Height < 8) return new ImageTextResult();
            var img = image.DarkOnLight();
            // Tesseract reads best when capital letters are 20-40 px tall: screenshots are enlarged
            if (img.Width * img.Height < 4000000 && Math.Max(img.Width, img.Height) < 2600) img = img.Scale(img.Width < 900 ? 3.0 : 2.0);
            int dpi = 300;
            string langs = string.IsNullOrEmpty(language) || language == Languages.AutoDetect ? null : OcrLanguages.ForLanguage(language);
            if (langs == null)
            {
                await OcrEngine.EnsureDataAsync("osd", Progress(status), ct).ConfigureAwait(false);
                if (status != null) status.Report("Detecting the alphabet...");
                ScriptDetection script = null;
                try { script = await Task.Run(() => OcrEngine.DetectScript(img.Pixels, img.Width, img.Height, dpi), ct).ConfigureAwait(false); }
                catch (Exception) { script = null; }
                langs = OcrLanguages.ForScript(script == null ? null : script.Script);
            }
            await OcrEngine.EnsureDataAsync(langs, Progress(status), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (status != null) status.Report("Reading the text...");
            var ocr = await Task.Run(() => OcrEngine.Recognize(img.Pixels, img.Width, img.Height, dpi, langs, false), ct).ConfigureAwait(false);
            return new ImageTextResult { Text = ToPlainText(ocr), Languages = langs, Confidence = ocr.MeanConfidence, Words = ocr.Words.Count };
        }

        private static IProgress<string> Progress(IProgress<string> status)
        {
            if (status == null) return null;
            return new Progress<string>(l => status.Report("Downloading text recognition data (" + l + ")..."));
        }

        /// <summary>Words as lines and paragraphs: a wider gap between lines starts a new paragraph.</summary>
        public static string ToPlainText(OcrResult ocr)
        {
            var lines = ocr.Words.Where(w => !string.IsNullOrWhiteSpace(w.Text) && (w.Confidence >= 25 || w.Text.Length > 2))
                .GroupBy(w => new { w.Block, w.Paragraph, w.Line })
                .Select(g =>
                {
                    var words = g.OrderBy(w => w.Left).ToList();
                    bool rtl = words.Count(w => Text.RtlText.ContainsRtl(w.Text)) * 2 > words.Count;
                    if (rtl) words = g.OrderByDescending(w => w.Right).ToList();
                    var sb = new StringBuilder();
                    foreach (var w in words)
                    {
                        if (sb.Length > 0 && !(LayoutAnalyzer.IsCjk(sb[sb.Length - 1]) && LayoutAnalyzer.IsCjk(w.Text[0]))) sb.Append(' ');
                        sb.Append(w.Text);
                    }
                    return new
                    {
                        Text = sb.ToString(),
                        Top = words.Min(w => w.Top),
                        Bottom = words.Max(w => w.Bottom),
                        Height = LayoutAnalyzer.Median(words.Select(w => (float)(w.Bottom - w.Top))),
                        Para = g.Key.Block * 10000 + g.Key.Paragraph,
                    };
                })
                .OrderBy(l => l.Top).ToList();
            var result = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    var prev = lines[i - 1];
                    float gap = lines[i].Top - prev.Bottom;
                    bool newParagraph = lines[i].Para != prev.Para || gap > Math.Max(prev.Height, lines[i].Height) * 0.9f;
                    result.Append(newParagraph ? "\n\n" : "\n");
                }
                result.Append(lines[i].Text);
            }
            return result.ToString();
        }
    }
}
