using System;
using System.Collections.Generic;
using TextX.Core.Documents.Imaging;
using TextX.Core.Documents.Native;

namespace TextX.Core.Documents.Export
{
    /// <summary>
    /// Crops regions (figures, drawings) out of rendered pages as PNG, for documents written in other
    /// formats. Keeps the PDF open and the last rendered page, so consecutive images of one page are cheap.
    /// </summary>
    public sealed class PageImages : IDisposable
    {
        private readonly PdfFile _doc;
        private readonly float _scale;
        private int _page = -1;
        private RenderedImage _image;

        /// <param name="dpi">Resolution of the crops (150 is sharp in Word documents and keeps files small).</param>
        public PageImages(string pdfPath, float dpi = 150f)
        {
            _doc = PdfFile.Open(pdfPath);
            _scale = dpi / 72f;
        }

        /// <summary>The region of the page as PNG, or null when it is empty or outside the page.</summary>
        public byte[] Crop(DocPage page, Box box)
        {
            if (page == null || box.W <= 1 || box.H <= 1) return null;
            if (_page != page.Index)
            {
                using (var p = _doc.LoadPage(page.Index)) _image = p.Render(_scale);
                _page = page.Index;
            }
            float left, top, width, height;
            page.DisplayRect(box, out left, out top, out width, out height);
            int x0 = Clamp((int)Math.Floor(left * _scale), _image.Width), y0 = Clamp((int)Math.Floor(top * _scale), _image.Height);
            int x1 = Clamp((int)Math.Ceiling((left + width) * _scale), _image.Width), y1 = Clamp((int)Math.Ceiling((top + height) * _scale), _image.Height);
            int w = x1 - x0, h = y1 - y0;
            if (w < 2 || h < 2) return null;
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(_image.Pixels, ((y0 + y) * _image.Width + x0) * 4, px, y * w * 4, w * 4);
            // a picture filling a large sheet is shrunk: Word shows it at most a page wide
            int factor = (int)Math.Ceiling(Math.Max(w, h) / (double)MaxSide);
            if (factor > 1) px = Shrink(px, ref w, ref h, factor);
            return PngWriter.FromBgra(px, w, h);
        }

        /// <summary>Longest side of a crop in pixels.</summary>
        public int MaxSide = 1800;

        /// <summary>Averages blocks of factor x factor pixels (BGRA).</summary>
        internal static byte[] Shrink(byte[] px, ref int w, ref int h, int factor)
        {
            int nw = Math.Max(1, w / factor), nh = Math.Max(1, h / factor);
            var result = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int b = 0, g = 0, r = 0, a = 0, n = 0;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        int sy = y * factor + dy;
                        if (sy >= h) break;
                        for (int dx = 0; dx < factor; dx++)
                        {
                            int sx = x * factor + dx;
                            if (sx >= w) break;
                            int i = (sy * w + sx) * 4;
                            b += px[i]; g += px[i + 1]; r += px[i + 2]; a += px[i + 3]; n++;
                        }
                    }
                    int o = (y * nw + x) * 4;
                    result[o] = (byte)(b / n); result[o + 1] = (byte)(g / n); result[o + 2] = (byte)(r / n); result[o + 3] = (byte)(a / n);
                }
            w = nw; h = nh;
            return result;
        }

        private static int Clamp(int v, int max)
        {
            return v < 0 ? 0 : v > max ? max : v;
        }

        public void Dispose()
        {
            _image = null;
            _doc.Dispose();
        }
    }
}
