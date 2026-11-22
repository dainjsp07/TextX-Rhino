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
        /// <summary>Translations not drawn because the same words already stand beside the original (bilingual labels).</summary>
        public int Repeated;
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
            // the drawing around the text: ruled cells, shapes and pictures the translation must not run into
            var geometry = PageGeometry.From(page, p.GetObjects());
            var repeats = Repeats(page);
            foreach (var b in plan.Write) LayoutBlock(page, project, b, options, plan, geometry, repeats);
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

        private static void LayoutBlock(DocPage page, DocProject project, DocBlock b, PdfWriteOptions options, PagePlan plan, PageGeometry geo, HashSet<DocBlock> repeats)
        {
            if (repeats.Contains(b))
            {
                // "Project name | שם פרויקט": the original goes, the English beside it already says it
                plan.Report.Repeated++;
                return;
            }
            bool translated = Changed(b);
            string text = NormalizeForPdf(translated ? b.Translation : b.Source);
            string lang = translated ? project.TargetLang : project.SourceLang;
            bool rtl = translated ? Languages.IsRtl(project.TargetLang) : b.SourceRtl;
            var fonts = options.Fonts;
            var primary = fonts.Primary(FontCatalog.KindOf(b.FontName), b.Bold, b.Italic, lang);
            if (primary == null) throw new InvalidOperationException("No usable font was found on this computer.");
            var fallbacks = fonts.FallbackFaces(lang);
            uint color = b.Color;
            if (b.FromOcr && Math.Abs(Luma(b.Color) - Luma(b.Background)) < 80) color = Luma(b.Background) < 110 ? 0xFFFFFFFFu : 0xFF141414u;
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
            // the room on the block's lines: its ruled cell, the texts and the drawing beside it
            float minX, maxX;
            FreeSpan(page, b, geo, size, out minX, out maxX);
            var align = AnchorAlign(b, rtl, flip, geo, size);
            float height = b.Box.H + (b.Lines.Count > 1 ? (b.LinePitch - 1f) * size : 0f);
            float below = RoomBelow(page, b, geo, size, height);
            if (b.Lines.Count > 1) height = Math.Max(height, below);
            // first the original's own width (or, for one line, the room on its anchored side); when the
            // translation would have to shrink much there, all the free room on its lines
            float anchored = align == BlockAlign.Right ? b.Box.R - minX : align == BlockAlign.Center ? 2f * Math.Min(b.Box.CX - minX, maxX - b.Box.CX) : maxX - b.Box.L;
            float first = b.Lines.Count == 1 ? Math.Max(b.Box.W, anchored) : b.Box.W;
            if (b.Lines.Count == 1 && (b.Kind == BlockKind.Label || b.IsRotated)) first = Math.Min(first, Math.Max(b.Box.W * 2.5f, b.Box.W + 8f * size));
            var result = TextFitter.Fit(text, first, height, size, b.LinePitch, singleLine, rtl, primary, fallbacks, options.MinScale);
            float span = Math.Max(b.Box.W, maxX - minX);
            if ((result.Overflow || result.Scale < 0.85f) && span > first + size)
            {
                // a label that does not fit may also take a second line where there is room under it
                float h2 = singleLine ? Math.Max(height, Math.Min(below, height + result.Pitch * 1.05f)) : height;
                var wider = TextFitter.Fit(text, span, h2, size, b.LinePitch, singleLine, rtl, primary, fallbacks, options.MinScale);
                if ((!wider.Overflow && result.Overflow) || wider.Scale > result.Scale + 0.04f) result = wider;
            }
            Note(b, result, plan.Report);
            float baseline = b.Lines[0].Baseline + (size - result.Size) * 0.78f;
            foreach (var line in result.Lines)
            {
                float x;
                if (align == BlockAlign.Right) x = Math.Min(b.Box.R, maxX) - line.Width;
                else if (align == BlockAlign.Center) x = b.Box.CX - line.Width / 2f;
                else x = Math.Max(b.Box.L, minX);
                // inside the free room when it fits there: a longer text moves away from what is beside it
                if (line.Width <= maxX - minX) x = Math.Max(minX, Math.Min(x, maxX - line.Width));
                plan.Ops.Add(new DrawOp { Line = line, Size = result.Size, X = x, Y = baseline, Angle = 0f, Color = color });
                baseline -= result.Pitch;
            }
        }

        /// <summary>
        /// Where a translation is anchored. A single line keeps its place: it ends where the original ended
        /// (a right-aligned Hebrew label gives a right-aligned English one), or stays centred when the original
        /// was centred in its ruled cell. A paragraph that changes direction turns its alignment round.
        /// </summary>
        private static BlockAlign AnchorAlign(DocBlock b, bool rtl, bool flip, PageGeometry geo, float size)
        {
            var a = b.Align == BlockAlign.Justify ? (b.SourceRtl ? BlockAlign.Right : BlockAlign.Left) : b.Align;
            if (b.Lines.Count == 1)
            {
                float cl, cr, cb, ct;
                geo.CellAround(b.Box, out cl, out cr, out cb, out ct);
                if (!float.IsNaN(cl) && !float.IsNaN(cr))
                {
                    float ml = b.Box.L - cl, mr = cr - b.Box.R, w = cr - cl;
                    if (w < size * 60f && ml > size * 0.6f && mr > size * 0.6f && Math.Abs(ml - mr) < Math.Max(2f, w * 0.12f)) return BlockAlign.Center;
                }
                return a;
            }
            return EffectiveAlign(a, rtl, flip);
        }

        /// <summary>
        /// The free room on a block's lines, left and right: the page margin, the ruled cell around it, a
        /// table's column, and the nearest text, shape or picture beside it.
        /// </summary>
        internal static void FreeSpan(DocPage page, DocBlock b, PageGeometry geo, float size, out float minX, out float maxX)
        {
            float gap = Math.Max(1.5f, size * 0.4f);
            float margin = Math.Min(18f, Math.Min(b.Box.L, page.Width - b.Box.R));
            float left = Math.Min(b.Box.L, margin), right = Math.Max(b.Box.R, page.Width - margin);
            float cl, cr, cb, ct;
            geo.CellAround(b.Box, out cl, out cr, out cb, out ct);
            if (!float.IsNaN(cl)) left = Math.Max(left, cl + gap);
            if (!float.IsNaN(cr)) right = Math.Min(right, cr - gap);
            float bandB = b.Box.B + b.Box.H * 0.15f, bandT = b.Box.T - b.Box.H * 0.15f;
            Action<Box> obstacle = o =>
            {
                if (o.T <= bandB || o.B >= bandT) return;            // not on these lines
                if (o.L >= b.Box.R - 0.5f) right = Math.Min(right, o.L - gap);
                else if (o.R <= b.Box.L + 0.5f) left = Math.Max(left, o.R + gap);
            };
            foreach (var o in page.Blocks) if (!ReferenceEquals(o, b)) obstacle(o.Box);
            foreach (var ink in geo.InkIn(new Box(left, bandB, right, bandT)))
            {
                // a frame or a picture behind the text, or a line drawn on it (an underline): part of the original
                if (ink.L <= b.Box.L + 1f && ink.R >= b.Box.R - 1f && ink.B <= b.Box.B + 1f && ink.T >= b.Box.T - 1f) continue;
                float ow = Math.Min(ink.R, b.Box.R) - Math.Max(ink.L, b.Box.L), oh = Math.Min(ink.T, b.Box.T) - Math.Max(ink.B, b.Box.B);
                if (ow > 0 && oh > 0 && ow * oh > 0.3f * Math.Max(0.5f, ink.W * ink.H)) continue;
                obstacle(ink);
            }
            if (b.Kind == BlockKind.TableCell && b.Table >= 0)
            {
                // the outer columns end near their widest cell (the table's border is a drawn line)
                var column = page.Blocks.Where(o => o.Table == b.Table && o.Col == b.Col).ToList();
                float colL = column.Min(o => o.Box.L), colR = column.Max(o => o.Box.R);
                float spare = Math.Max(2f * size, (colR - colL) * 0.25f);
                if (!page.Blocks.Any(o => o.Table == b.Table && o.Col > b.Col)) right = Math.Min(right, colR + spare);
                if (!page.Blocks.Any(o => o.Table == b.Table && o.Col < b.Col)) left = Math.Max(left, colL - spare);
            }
            minX = Math.Min(left, b.Box.L);
            maxX = Math.Max(right, b.Box.R);
        }

        /// <summary>
        /// Translations that only repeat words already standing beside their original, as in bilingual title
        /// blocks ("Project name  שם פרויקט"): their original is removed and nothing is added.
        /// </summary>
        internal static HashSet<DocBlock> Repeats(DocPage page)
        {
            var set = new HashSet<DocBlock>();
            foreach (var b in page.Blocks.Where(Changed))
            {
                var key = Key(b.Translation);
                if (key.Length < 2) continue;
                float size = Math.Max(4f, b.FontSize);
                foreach (var o in page.Blocks)
                {
                    if (ReferenceEquals(o, b) || Changed(o) || !Similar(key, Key(o.Source))) continue;
                    float vOverlap = Math.Min(o.Box.T, b.Box.T) - Math.Max(o.Box.B, b.Box.B);
                    float hGap = Math.Max(o.Box.L - b.Box.R, b.Box.L - o.Box.R);
                    float vGap = Math.Max(o.Box.B - b.Box.T, b.Box.B - o.Box.T);
                    bool sameLine = vOverlap > Math.Min(o.Box.H, b.Box.H) * 0.4f && hGap < size * 25f;
                    bool stacked = o.Box.OverlapX(b.Box) > Math.Min(o.Box.W, b.Box.W) * 0.3f && vGap < size * 2.5f;
                    if (sameLine || stacked) { set.Add(b); break; }
                }
            }
            return set;
        }

        private static string Key(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in (s ?? string.Empty).ToLowerInvariant()) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            var k = sb.ToString();
            return k.StartsWith("the", StringComparison.Ordinal) && k.Length > 5 ? k.Substring(3) : k;
        }

        /// <summary>The same words, allowing a typing slip in longer ones ("adress").</summary>
        private static bool Similar(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            if (Math.Min(a.Length, b.Length) < 6 || Math.Abs(a.Length - b.Length) > 2) return false;
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length] <= Math.Max(1, Math.Min(a.Length, b.Length) / 8);
        }

        /// <summary>
        /// The width a one-line text may take: the gap to the nearest text or image beside it on the same
        /// line, a table's column, the page margin. Labels on drawings stay close to their spot, since the
        /// lines of the drawing itself are not known.
        /// </summary>
        internal static float RoomAcross(DocPage page, DocBlock b, BlockAlign align, float size)
        {
            float gap = Math.Max(2f, size * 0.6f);
            float margin = Math.Min(18f, Math.Min(b.Box.L, page.Width - b.Box.R));
            float leftLimit = Math.Min(b.Box.L, margin), rightLimit = Math.Max(b.Box.R, page.Width - margin);
            float bandB = b.Box.B + b.Box.H * 0.15f, bandT = b.Box.T - b.Box.H * 0.15f;
            Action<Box> obstacle = o =>
            {
                if (o.T <= bandB || o.B >= bandT) return;          // not on this line
                if (o.L >= b.Box.R - 0.5f) rightLimit = Math.Min(rightLimit, o.L - gap);
                else if (o.R <= b.Box.L + 0.5f) leftLimit = Math.Max(leftLimit, o.R + gap);
            };
            foreach (var o in page.Blocks) if (!ReferenceEquals(o, b)) obstacle(o.Box);
            foreach (var img in page.Images) if (!img.Contains(b.Box.CX, b.Box.CY)) obstacle(img);

            if (b.Kind == BlockKind.TableCell && b.Table >= 0)
            {
                // the outer columns end near their widest cell (the table's border is a drawn line)
                var column = page.Blocks.Where(o => o.Table == b.Table && o.Col == b.Col).ToList();
                float colL = column.Min(o => o.Box.L), colR = column.Max(o => o.Box.R);
                float spare = Math.Max(2f * size, (colR - colL) * 0.25f);
                if (!page.Blocks.Any(o => o.Table == b.Table && o.Col > b.Col)) rightLimit = Math.Min(rightLimit, colR + spare);
                if (!page.Blocks.Any(o => o.Table == b.Table && o.Col < b.Col)) leftLimit = Math.Max(leftLimit, colL - spare);
            }
            leftLimit = Math.Min(leftLimit, b.Box.L);
            rightLimit = Math.Max(rightLimit, b.Box.R);

            float room;
            if (align == BlockAlign.Right) room = b.Box.R - leftLimit;
            else if (align == BlockAlign.Center) room = 2f * Math.Min(b.Box.CX - leftLimit, rightLimit - b.Box.CX);
            else room = rightLimit - b.Box.L;
            if (b.Kind == BlockKind.Label || b.IsRotated) room = Math.Min(room, Math.Max(b.Box.W * 2.5f, b.Box.W + 8f * size));
            return Math.Max(b.Box.W, room);
        }

        /// <summary>The height a paragraph may grow to: down to the next text, ruled line, shape or picture below it.</summary>
        internal static float RoomBelow(DocPage page, DocBlock b, float size, float height)
        {
            return RoomBelow(page, b, PageGeometry.Empty, size, height);
        }

        internal static float RoomBelow(DocPage page, DocBlock b, PageGeometry geo, float size, float height)
        {
            float limit = Math.Min(b.Box.B, 24f);
            Action<Box> obstacle = o =>
            {
                if (o.T > b.Box.B + 0.5f || o.R <= b.Box.L || o.L >= b.Box.R) return;
                limit = Math.Max(limit, o.T + size * 0.6f);
            };
            foreach (var o in page.Blocks) if (!ReferenceEquals(o, b)) obstacle(o.Box);
            foreach (var img in page.Images) obstacle(img);
            float cl, cr, cb, ct;
            geo.CellAround(b.Box, out cl, out cr, out cb, out ct);
            if (!float.IsNaN(cb)) limit = Math.Max(limit, cb + size * 0.3f);
            foreach (var ink in geo.InkIn(new Box(b.Box.L, limit, b.Box.R, b.Box.B)))
            {
                if (ink.T > b.Box.B + 0.5f) continue;                 // reaches up into the text: behind it
                limit = Math.Max(limit, ink.T + size * 0.4f);
            }
            float extra = Math.Max(0f, b.Box.B - limit);
            return height + Math.Min(extra, height * 0.6f);
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
