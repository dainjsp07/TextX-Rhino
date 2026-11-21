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
        /// <summary>One line of text to draw (page frame coordinates).</summary>
        private sealed class DrawOp
        {
            public FittedLine Line;
            public float Size, X, Y, Angle;
            public uint Color;
        }

        private sealed class PagePlan
        {
            public DocPage Page;
            public List<DocBlock> Write = new List<DocBlock>();
            public List<DrawOp> Ops = new List<DrawOp>();
            public PageWriteReport Report;
        }

        public static List<PageWriteReport> Write(DocProject project, string outputPath, PdfWriteOptions options, CancellationToken ct)
        {
            var temp = outputPath + ".partial";
            var plans = new List<PagePlan>();
            using (var doc = PdfFile.Open(project.PdfPath))
            {
                // pass 1: what to replace and how the translations are laid out
                foreach (var page in project.Pages.Where(p => p.Extracted))
                {
                    ct.ThrowIfCancellationRequested();
                    using (var p = doc.LoadPage(page.Index)) plans.Add(Plan(p, page, project, options));
                }
                // embed each font once, with only the characters the document uses
                var fonts = LoadFonts(doc, plans);
                // pass 2: replace the text
                int done = 0;
                foreach (var plan in plans)
                {
                    ct.ThrowIfCancellationRequested();
                    if (plan.Write.Count > 0)
                        using (var p = doc.LoadPage(plan.Page.Index)) Apply(p, plan, fonts);
                    done++;
                    if (options.PagesDone != null) options.PagesDone.Report(done);
                }
                doc.Save(temp);
            }
            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(temp, outputPath);
            return plans.Select(pl => pl.Report).ToList();
        }

        /// <summary>Renders one page as it will look translated (nothing is saved).</summary>
        public static RenderedImage RenderPage(DocProject project, int pageIndex, float scale, PdfWriteOptions options, out PageWriteReport report)
        {
            using (var doc = PdfFile.Open(project.PdfPath))
            using (var p = doc.LoadPage(pageIndex))
            {
                var page = project.Pages[pageIndex];
                report = null;
                if (page.Extracted)
                {
                    var plan = Plan(p, page, project, options);
                    if (plan.Write.Count > 0) Apply(p, plan, LoadFonts(doc, new[] { plan }));
                    report = plan.Report;
                }
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

        private static Func<DocBlock, IEnumerable<IntPtr>> HandlesOf(List<PdfChar> chars)
        {
            return b => b.CharIndices.Where(i => i >= 0 && i < chars.Count).Select(i => chars[i].TextObject).Where(h => h != IntPtr.Zero).Distinct();
        }

        /// <summary>
        /// The blocks to rewrite: changed ones, plus any block sharing a text object with them (removing
        /// the object would remove that block's text too).
        /// </summary>
        private static HashSet<DocBlock> WriteSet(DocPage page, List<PdfChar> chars)
        {
            var write = new HashSet<DocBlock>(page.Blocks.Where(Changed));
            if (write.Count == 0) return write;
            var handlesOf = HandlesOf(chars);
            var remove = new HashSet<IntPtr>();
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var b in write.Where(b => !b.FromOcr).ToList())
                    foreach (var h in handlesOf(b)) remove.Add(h);
                foreach (var b in page.Blocks.Where(b => !b.FromOcr && !write.Contains(b)))
                    if (handlesOf(b).Any(remove.Contains)) { write.Add(b); grew = true; }
            }
            return write;
        }

        private static PagePlan Plan(PdfPage p, DocPage page, DocProject project, PdfWriteOptions options)
        {
            var plan = new PagePlan { Page = page, Report = new PageWriteReport { Page = page.Index } };
            if (!page.Blocks.Any(Changed)) return plan;
            var chars = p.GetChars();
            plan.Write = WriteSet(page, chars).OrderBy(b => b.Order).ToList();
            foreach (var b in plan.Write) LayoutBlock(page, project, b, options, plan);
            return plan;
        }

        private static Dictionary<FontFace, IntPtr> LoadFonts(PdfFile doc, IEnumerable<PagePlan> plans)
        {
            var used = new Dictionary<FontFace, HashSet<int>>();
            foreach (var op in plans.SelectMany(pl => pl.Ops))
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
            return fonts;
        }

        private static void Apply(PdfPage p, PagePlan plan, Dictionary<FontFace, IntPtr> fonts)
        {
            var page = plan.Page;
            var report = plan.Report;
            var chars = p.GetChars();
            var objects = p.GetObjects();
            var objectByHandle = new Dictionary<IntPtr, PdfObjectInfo>();
            foreach (var o in objects) if (o.IsText && !objectByHandle.ContainsKey(o.Handle)) objectByHandle[o.Handle] = o;
            var handlesOf = HandlesOf(chars);
            var remove = new HashSet<IntPtr>(plan.Write.Where(b => !b.FromOcr).SelectMany(handlesOf));

            var cover = new HashSet<DocBlock>(plan.Write.Where(b => b.FromOcr));
            foreach (var h in remove)
            {
                PdfObjectInfo info;
                bool removed = objectByHandle.TryGetValue(h, out info) && p.RemoveObject(info);
                if (!removed)
                    foreach (var b in plan.Write.Where(b => !b.FromOcr && handlesOf(b).Contains(h))) cover.Add(b);
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

            foreach (var op in plan.Ops)
            {
                double angle = op.Angle + page.FrameToUserAngle;
                double ax = Math.Cos(op.Angle), ay = Math.Sin(op.Angle);
                float along = 0;
                foreach (var run in op.Line.Runs)
                {
                    if (run.Text.Trim().Length > 0)
                    {
                        float ux, uy;
                        page.FrameToUser((float)(op.X + ax * along), (float)(op.Y + ay * along), out ux, out uy);
                        p.AddText(fonts[run.Face], op.Size, run.Text, ux, uy, op.Color, angle);
                    }
                    along += run.Width;
                }
            }
            report.Written = plan.Write.Count;
            p.CommitChanges();
        }

        /// <summary>Characters that share a glyph with a plainer one are drawn as the plain one.</summary>
        public static string NormalizeForPdf(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace(' ', ' ').Replace(' ', ' ').Replace(' ', ' ').Replace("­", string.Empty)
                    .Replace('‑', '-').Replace("﻿", string.Empty).Replace("​", string.Empty);
        }

        private static void LayoutBlock(DocPage page, DocProject project, DocBlock b, PdfWriteOptions options, PagePlan plan)
        {
            bool translated = Changed(b);
            string text = NormalizeForPdf(translated ? b.Translation : b.Source);
            string lang = translated ? project.TargetLang : project.SourceLang;
            bool rtl = translated ? Languages.IsRtl(project.TargetLang) : b.SourceRtl;
            var fonts = options.Fonts;
            var primary = fonts.Primary(FontCatalog.KindOf(b.FontName), b.Bold, b.Italic, lang);
            if (primary == null) throw new InvalidOperationException("No usable font was found on this computer.");
            var fallbacks = fonts.FallbackFaces(lang);
            uint color = b.FromOcr ? (Luma(b.Background) < 110 ? 0xFFFFFFFFu : 0xFF141414u) : b.Color;
            float size = b.FontSize > 0 ? b.FontSize : 10f;
            bool flip = translated && b.SourceRtl != rtl;

            if (b.IsRotated)
            {
                var first = b.Lines[0];
                var fit = TextFitter.Fit(text.Replace('\n', ' '), Math.Max(first.Advance, size * 2), size * 1.3f, size, 1.2f, true, rtl, primary, fallbacks, options.MinScale);
                Note(b, fit, plan.Report);
                var line = fit.Lines[0];
                double offset = AlignOffset(b.Align, rtl, flip, first.Advance, line.Width);
                double ax = Math.Cos(b.Angle), ay = Math.Sin(b.Angle);
                plan.Ops.Add(new DrawOp { Line = line, Size = fit.Size, X = (float)(first.OriginX + ax * offset), Y = (float)(first.OriginY + ay * offset), Angle = b.Angle, Color = color });
                return;
            }

            bool singleLine = b.Lines.Count == 1 && (b.Kind == BlockKind.Label || b.Kind == BlockKind.HeaderFooter || b.Kind == BlockKind.TableCell || b.Kind == BlockKind.Heading);
            float width = b.Box.W;
            if (b.Kind == BlockKind.Label || b.Kind == BlockKind.Heading) width *= 1.15f; // short texts may grow a little
            float height = b.Box.H + (b.Lines.Count > 1 ? (b.LinePitch - 1f) * size : 0f);
            var result = TextFitter.Fit(text, width, height, size, b.LinePitch, singleLine, rtl, primary, fallbacks, options.MinScale);
            Note(b, result, plan.Report);
            float baseline = b.Lines[0].Baseline + (size - result.Size) * 0.78f;
            var align = EffectiveAlign(b.Align, rtl, flip);
            foreach (var line in result.Lines)
            {
                float x;
                if (align == BlockAlign.Right) x = b.Box.R - line.Width;
                else if (align == BlockAlign.Center) x = b.Box.CX - line.Width / 2f;
                else x = b.Box.L;
                plan.Ops.Add(new DrawOp { Line = line, Size = result.Size, X = x, Y = baseline, Angle = 0f, Color = color });
                baseline -= result.Pitch;
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
            bool layoutNote = b.Note != null && (b.Note.StartsWith("Text reduced", StringComparison.Ordinal) || b.Note.StartsWith("The translation is longer", StringComparison.Ordinal));
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
            else if (layoutNote)
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
