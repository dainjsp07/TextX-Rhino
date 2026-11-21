using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TextX.Core.Documents.Fonts;
using TextX.Core.Documents.Layout;
using TextX.Core.Documents.Native;

namespace TextX.Core.Documents.Export
{
    /// <summary>
    /// Builds a new PDF from text lines and filled rectangles (reports, schedules). Pages are laid out
    /// first; on saving, each font is embedded once with only the characters used.
    /// Coordinates are points from the bottom-left corner of the page.
    /// </summary>
    public sealed class PdfComposer
    {
        internal sealed class Op
        {
            public FittedLine Line;
            public float Size, X, Y, W, H;
            public uint Color;
        }

        public sealed class Page
        {
            public float Width, Height;
            internal readonly List<Op> Ops = new List<Op>();
        }

        private readonly List<Page> _pages = new List<Page>();

        public PdfComposer(FontCatalog fonts)
        {
            Fonts = fonts;
        }

        public FontCatalog Fonts { get; private set; }
        public int PageCount { get { return _pages.Count; } }

        public Page AddPage(float width, float height)
        {
            var p = new Page { Width = width, Height = height };
            _pages.Add(p);
            return p;
        }

        public void Rect(Page page, float x, float y, float w, float h, uint argb)
        {
            page.Ops.Add(new Op { X = x, Y = y, W = w, H = h, Color = argb });
        }

        /// <summary>Draws a line already laid out by <see cref="TextFitter"/> with its left end at x and baseline at y.</summary>
        public void Line(Page page, FittedLine line, float size, float x, float y, uint argb)
        {
            if (line == null || line.Runs.Count == 0) return;
            page.Ops.Add(new Op { Line = line, Size = size, X = x, Y = y, Color = argb });
        }

        /// <summary>
        /// Wraps text into a column of the given width (never shrinking it) and returns the lines,
        /// for measuring before drawing.
        /// </summary>
        public List<FittedLine> Wrap(string text, float width, float size, bool bold = false, string lang = "en")
        {
            if (string.IsNullOrEmpty(text)) return new List<FittedLine>();
            var primary = Fonts.Primary(FontKind.Sans, bold, false, lang);
            if (primary == null) throw new InvalidOperationException("No usable font was found on this computer.");
            bool rtl = TextX.Core.Text.RtlText.ContainsRtl(text);
            var fit = TextFitter.Fit(TranslatedPdfWriter.NormalizeForPdf(text), Math.Max(4f, width), 100000f, size, 1.2f, false, rtl, primary, Fonts.FallbackFaces(lang), 1f);
            return fit.Lines;
        }

        public void Save(string path)
        {
            var temp = path + ".partial";
            using (var doc = PdfFile.CreateNew())
            {
                var used = new Dictionary<FontFace, HashSet<int>>();
                foreach (var op in _pages.SelectMany(p => p.Ops).Where(o => o.Line != null))
                    foreach (var run in op.Line.Runs)
                    {
                        HashSet<int> set;
                        if (!used.TryGetValue(run.Face, out set)) used[run.Face] = set = new HashSet<int>();
                        for (int i = 0; i < run.Text.Length; i++)
                        {
                            int cp = run.Text[i];
                            if (char.IsHighSurrogate(run.Text[i]) && i + 1 < run.Text.Length) { cp = char.ConvertToUtf32(run.Text[i], run.Text[i + 1]); i++; }
                            set.Add(cp);
                        }
                    }
                var fonts = new Dictionary<FontFace, IntPtr>();
                foreach (var kv in used) fonts[kv.Key] = doc.LoadFont(kv.Key.Key + "#subset", kv.Key.Font.Subset(kv.Value));
                foreach (var page in _pages)
                {
                    using (var p = doc.AddPage(page.Width, page.Height))
                    {
                        foreach (var op in page.Ops)
                        {
                            if (op.Line == null) { p.AddRectangle(op.X, op.Y, op.W, op.H, op.Color); continue; }
                            float along = 0;
                            foreach (var run in op.Line.Runs)
                            {
                                if (run.Text.Trim().Length > 0) p.AddText(fonts[run.Face], op.Size, run.Text, op.X + along, op.Y, op.Color);
                                along += run.Width;
                            }
                        }
                        p.CommitChanges();
                    }
                }
                doc.Save(temp);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
    }
}
