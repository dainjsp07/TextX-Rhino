using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TextX.Core.Documents.Fonts;
using TextX.Core.Documents.Layout;
using TextX.Core.Documents.Native;

namespace TextX.Core.Documents.Export
{
    public sealed class PdfWriteOptions
    {
        public FontCatalog Fonts;
        /// <summary>Text never shrinks below this share of its original size (it may then overflow its box).</summary>
        public float MinScale = 0.55f;
        public IProgress<int> PagesDone;
    }

    public sealed class PageWriteReport
    {
        public int Page;
        public int Written;
        public int Covered;
        public int Shrunk;
        public int Overflowing;
    }

    /// <summary>
    /// Writes a translated PDF: the original file with only the text replaced. Text objects of
    /// translated blocks are removed and the translation is drawn in the same place, size, style and
    /// colour, wrapped and reduced as needed. Images, drawings, tables and page layout are untouched.
    /// Scanned text (OCR) cannot be removed from the image, so it is covered with its background colour.
    /// </summary>
    public static class TranslatedPdfWriter
    {
        public static List<PageWriteReport> Write(DocProject project, string outputPath, PdfWriteOptions options, CancellationToken ct)
        {
            var reports = new List<PageWriteReport>();
            var temp = outputPath + ".partial";
            using (var doc = PdfFile.Open(project.PdfPath))
            {
                int done = 0;
                foreach (var page in project.Pages)
                {
                    ct.ThrowIfCancellationRequested();
                    if (page.Extracted)
                    {
                        using (var p = doc.LoadPage(page.Index)) reports.Add(Apply(doc, p, page, project, options));
                    }
                    done++;
                    if (options.PagesDone != null) options.PagesDone.Report(done);
                }
                doc.Save(temp);
            }
            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(temp, outputPath);
            return reports;
        }

        /// <summary>Renders one page as it will look translated (nothing is saved).</summary>
        public static RenderedImage RenderPage(DocProject project, int pageIndex, float scale, PdfWriteOptions options, out PageWriteReport report)
        {
            using (var doc = PdfFile.Open(project.PdfPath))
            using (var p = doc.LoadPage(pageIndex))
            {
                var page = project.Pages[pageIndex];
                report = page.Extracted ? Apply(doc, p, page, project, options) : null;
                return p.Render(scale);
            }
        }

        /// <summary>Renders one page of the original.</summary>
        public static RenderedImage RenderOriginal(string pdfPath, int pageIndex, float scale)
        {
            using (var doc = PdfFile.Open(pdfPath))
            using (var p = doc.LoadPage(pageIndex))
                return p.Render(scale);
        }

        private static bool Changed(DocBlock b)
        {
            return (b.State == BlockState.Translated || b.State == BlockState.Edited) && b.Translation != null &&
                   !string.Equals(b.Translation.Trim(), b.Source.Trim(), StringComparison.Ordinal);
        }

        internal static PageWriteReport Apply(PdfFile doc, PdfPage p, DocPage page, DocProject project, PdfWriteOptions options)
        {
            var report = new PageWriteReport { Page = page.Index };
            var write = new HashSet<DocBlock>(page.Blocks.Where(Changed));
            if (write.Count == 0) return report;

            var chars = p.GetChars();
            var objects = p.GetObjects();
            var objectByHandle = new Dictionary<IntPtr, PdfObjectInfo>();
            foreach (var o in objects) if (o.IsText && !objectByHandle.ContainsKey(o.Handle)) objectByHandle[o.Handle] = o;
            Func<DocBlock, IEnumerable<IntPtr>> handlesOf = b => b.CharIndices.Where(i => i >= 0 && i < chars.Count)
                .Select(i => chars[i].TextObject).Where(h => h != IntPtr.Zero).Distinct();

            // text objects to remove; blocks sharing one of them are rewritten too so nothing disappears
            var remove = new HashSet<IntPtr>();
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var b in write.Where(b => !b.FromOcr).ToList())
                    foreach (var h in handlesOf(b)) remove.Add(h);
                foreach (var b in page.Blocks.Where(b => !b.FromOcr && !write.Contains(b)))
                {
                    if (handlesOf(b).Any(remove.Contains)) { write.Add(b); grew = true; }
                }
            }

            var cover = new HashSet<DocBlock>(write.Where(b => b.FromOcr));
            foreach (var h in remove)
            {
                PdfObjectInfo info;
                bool removed = objectByHandle.TryGetValue(h, out info) && p.RemoveObject(info);
                if (!removed)
                    foreach (var b in write.Where(b => !b.FromOcr && handlesOf(b).Contains(h))) cover.Add(b);
            }

            foreach (var b in cover)
            {
                var box = b.Box.Inflate(Math.Max(0.8f, b.FontSize * 0.12f));
                float x1, y1, x2, y2;
                page.FrameToUser(box.L, box.B, out x1, out y1);
                page.FrameToUser(box.R, box.T, out x2, out y2);
                p.AddRectangle(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1), b.FromOcr ? b.Background : 0xFFFFFFFF);
                report.Covered++;
            }

            foreach (var b in write.OrderBy(b => b.Order))
            {
                DrawBlock(doc, p, page, project, b, options, report);
                report.Written++;
            }
            p.CommitChanges();
            return report;
        }

        private static void DrawBlock(PdfFile doc, PdfPage p, DocPage page, DocProject project, DocBlock b, PdfWriteOptions options, PageWriteReport report)
        {
            bool translated = Changed(b);
            string text = translated ? b.Translation : b.Source;
            string lang = translated ? project.TargetLang : project.SourceLang;
            bool rtl = translated ? Languages.IsRtl(project.TargetLang) : b.SourceRtl;
            var fonts = options.Fonts;
            var primary = fonts.Primary(FontCatalog.KindOf(b.FontName), b.Bold, b.Italic, lang);
            if (primary == null) throw new InvalidOperationException("No usable font was found on this computer.");
            var fallbacks = fonts.FallbackFaces(lang);
            uint color = b.FromOcr ? (Luma(b.Background) < 110 ? 0xFFFFFFFFu : 0xFF141414u) : b.Color;
            float size = b.FontSize > 0 ? b.FontSize : 10f;

            if (b.IsRotated)
            {
                var first = b.Lines[0];
                var fit = TextFitter.Fit(text.Replace('\n', ' '), Math.Max(first.Advance, size * 2), size * 1.3f, size, 1.2f, true, rtl, primary, fallbacks, options.MinScale);
                Note(b, fit, report);
                var line = fit.Lines[0];
                double offset = AlignOffset(b.Align, rtl, translated && b.SourceRtl != rtl, first.Advance, line.Width);
                double ax = Math.Cos(b.Angle), ay = Math.Sin(b.Angle);
                float ox = (float)(first.OriginX + ax * offset), oy = (float)(first.OriginY + ay * offset);
                DrawLine(doc, p, page, line, fit.Size, ox, oy, b.Angle, color);
                return;
            }

            bool singleLine = b.Lines.Count == 1 && (b.Kind == BlockKind.Label || b.Kind == BlockKind.HeaderFooter || b.Kind == BlockKind.TableCell || b.Kind == BlockKind.Heading);
            float width = b.Box.W;
            if (b.Kind == BlockKind.Label || b.Kind == BlockKind.Heading) width *= 1.15f; // short texts may grow a little
            float height = b.Box.H + (b.Lines.Count > 1 ? (b.LinePitch - 1f) * size : 0f);
            var result = TextFitter.Fit(text, width, height, size, b.LinePitch, singleLine, rtl, primary, fallbacks, options.MinScale);
            Note(b, result, report);
            float baseline = b.Lines[0].Baseline + (size - result.Size) * 0.78f;
            bool flip = translated && b.SourceRtl != rtl;
            foreach (var line in result.Lines)
            {
                float x;
                var align = EffectiveAlign(b.Align, rtl, flip);
                if (align == BlockAlign.Right) x = b.Box.R - line.Width;
                else if (align == BlockAlign.Center) x = b.Box.CX - line.Width / 2f;
                else x = b.Box.L;
                DrawLine(doc, p, page, line, result.Size, x, baseline, 0f, color);
                baseline -= result.Pitch;
            }
        }

        private static void DrawLine(PdfFile doc, PdfPage p, DocPage page, FittedLine line, float size, float x, float y, float frameAngle, uint color)
        {
            double angle = frameAngle + page.FrameToUserAngle;
            double ax = Math.Cos(frameAngle), ay = Math.Sin(frameAngle);
            float along = 0;
            foreach (var run in line.Runs)
            {
                if (run.Text.Trim().Length > 0)
                {
                    var font = doc.LoadFont(run.Face.Key, run.Face.EmbedData);
                    float ux, uy;
                    page.FrameToUser((float)(x + ax * along), (float)(y + ay * along), out ux, out uy);
                    p.AddText(font, size, run.Text, ux, uy, color, angle);
                }
                along += run.Width;
            }
        }

        private static BlockAlign EffectiveAlign(BlockAlign align, bool rtl, bool flip)
        {
            if (align == BlockAlign.Justify) align = rtl ? BlockAlign.Right : BlockAlign.Left;
            if (flip && align == BlockAlign.Left) return BlockAlign.Right;
            if (flip && align == BlockAlign.Right) return BlockAlign.Left;
            return align;
        }

        private static double AlignOffset(BlockAlign align, bool rtl, bool flip, float available, float width)
        {
            var a = EffectiveAlign(align, rtl, flip);
            if (a == BlockAlign.Right) return available - width;
            if (a == BlockAlign.Center) return (available - width) / 2;
            return 0;
        }

        private static void Note(DocBlock b, FitResult fit, PageWriteReport report)
        {
            if (fit.Overflow)
            {
                report.Overflowing++;
                b.Note = "The translation is longer than the original space; it was reduced to " + (int)Math.Round(fit.Scale * 100) + "% and may overlap nearby content.";
            }
            else if (fit.Scale < 0.85f)
            {
                report.Shrunk++;
                b.Note = "Text reduced to " + (int)Math.Round(fit.Scale * 100) + "% to fit the original space.";
            }
            else if (b.Note != null && b.Note.StartsWith("Text reduced", StringComparison.Ordinal) || (b.Note != null && b.Note.StartsWith("The translation is longer", StringComparison.Ordinal)))
            {
                b.Note = null;
            }
        }

        private static int Luma(uint argb)
        {
            return (int)((((argb >> 16) & 255) * 77 + ((argb >> 8) & 255) * 150 + (argb & 255) * 29) >> 8);
        }
    }
}
