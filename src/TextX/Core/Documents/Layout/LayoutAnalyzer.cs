using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TextX.Core.Translation;
using TextX.Core.Documents.Native;
using TextX.Core.Text;

namespace TextX.Core.Documents.Layout
{
    /// <summary>
    /// Turns the characters of a page into lines, then into blocks (paragraphs, headings, list items,
    /// table cells, labels), classifies them and puts them in reading order. Works the same on
    /// characters read from the PDF's text layer and on words read by OCR.
    /// </summary>
    public static class LayoutAnalyzer
    {
        // bullets may touch their text ("*Note", "•Item"); dashes and numbers need a space after them
        private static readonly Regex ListStart = new Regex(@"^\s*(?:[•◦▪●‣⁃·∙\*]\s*(?=\S)|[\-–—]\s+|\(?(?:\d{1,3}|[a-zA-Z]|[ivxIVX]{1,4})[\.\)]\s+)", RegexOptions.Compiled);

        // ------------------------------------------------------------------ characters to lines
        /// <summary>Builds lines from a page's text layer. Generated characters mark spaces and line breaks.</summary>
        public static List<DocLine> BuildLines(IList<PdfChar> chars, PageGeometry geometry = null)
        {
            var geo = geometry ?? PageGeometry.Empty;
            // 1. runs of characters as the PDF gives them
            var fragments = new List<List<PdfChar>>();
            var current = new List<PdfChar>();
            PdfChar? prev = null;
            Action cut = () =>
            {
                if (current.Count > 0) fragments.Add(current);
                current = new List<PdfChar>();
            };
            foreach (var raw in chars)
            {
                var c = raw;
                if (c.Code == '\r' || c.Code == '\n')
                {
                    cut();
                    prev = null;
                    continue;
                }
                if (c.Code == 0xAD || c.Code == 0x2011) c.Code = '-';   // soft / non-breaking hyphen
                bool isSpace = c.Code == ' ' || c.Code == 0xA0 || c.Code == 0x3000 || c.Code == '\t';
                if (c.Code < 32 && !isSpace) continue;
                if (!isSpace && c.Right - c.Left <= 0 && c.Top - c.Bottom <= 0) continue;
                if (prev.HasValue && !isSpace && current.Count > 0 && (BreaksLine(prev.Value, c) || RuleBetween(geo, prev.Value, c))) cut();
                current.Add(c);
                if (!isSpace) prev = c;
            }
            cut();

            // 2. runs on one baseline that touch (words placed one by one, right-to-left text) form one line
            var lines = new List<DocLine>();
            foreach (var f in MergeFragments(fragments, geo)) Flush(f.Value, lines, f.Key);
            return lines;
        }

        /// <summary>
        /// A ruled line in the gap between two characters of level text (table cells, title block fields).
        /// A line drawn through a word (a section mark over a drawing number) does not part it.
        /// </summary>
        private static bool RuleBetween(PageGeometry geo, PdfChar p, PdfChar c)
        {
            if (geo.VRules.Count == 0 || Math.Abs(Math.Sin(p.Angle)) > 0.05) return false;
            float x1, x2;
            if (c.Left >= p.Right - 0.01f) { x1 = p.Right; x2 = c.Left; }
            else if (p.Left >= c.Right - 0.01f) { x1 = c.Right; x2 = p.Left; }
            else return false;
            float size = Math.Max(4f, Math.Max(p.FontSize, c.FontSize));
            if (x2 - x1 < size * 0.15f) return false;
            return geo.VerticalRuleBetween(x1 - 0.3f, x2 + 0.3f, Math.Min(p.Bottom, c.Bottom), Math.Max(p.Top, c.Top));
        }

        private sealed class Fragment
        {
            public List<PdfChar> Chars;
            public int Order;
            public float Angle, Size;
            public double V, UMin, UMax;
            public bool Merged;
        }

        /// <summary>
        /// Joins runs that sit on the same baseline with no more than a word space between them, in either
        /// direction. PDFs written word by word, or right-to-left text, come apart into such runs.
        /// The flag says the text must be rebuilt from the positions of the characters.
        /// </summary>
        private static List<KeyValuePair<bool, List<PdfChar>>> MergeFragments(List<List<PdfChar>> fragments, PageGeometry geo)
        {
            var frags = new List<Fragment>();
            for (int i = 0; i < fragments.Count; i++)
            {
                var visible = fragments[i].Where(c => !IsSpace(c.Code)).ToList();
                if (visible.Count == 0) continue;
                float angle = visible[0].Angle;
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                var f = new Fragment { Chars = fragments[i], Order = i, Angle = angle, Size = Math.Max(1f, Median(visible.Select(c => c.FontSize))) };
                f.V = Median(visible.Select(c => (float)(-c.OriginX * sin + c.OriginY * cos)));
                f.UMin = visible.Min(c => StartAlong(c, cos, sin));
                f.UMax = visible.Max(c => EndAlong(c, cos, sin));
                frags.Add(f);
            }
            var result = new List<Fragment>();
            foreach (var group in frags.GroupBy(f => (int)Math.Round(Pdfium.NormalizeAngle(f.Angle) / 0.05)))
            {
                var pending = group.OrderBy(f => f.V).ThenBy(f => f.UMin).ToList();
                // baselines: fragments whose baseline is within a third of the text size
                var rows = new List<List<Fragment>>();
                foreach (var f in pending)
                {
                    var row = rows.LastOrDefault();
                    if (row != null && Math.Abs(row[0].V - f.V) < Math.Min(row[0].Size, f.Size) * 0.35) row.Add(f);
                    else rows.Add(new List<Fragment> { f });
                }
                foreach (var row in rows)
                {
                    Fragment open = null;
                    foreach (var f in row.OrderBy(x => x.UMin))
                    {
                        if (open != null)
                        {
                            float size = Math.Max(open.Size, f.Size);
                            double gap = f.UMin - open.UMax;
                            float ratio = Math.Max(open.Size, f.Size) / Math.Min(open.Size, f.Size);
                            bool inside = f.UMin >= open.UMin - size * 0.1 && f.UMax <= open.UMax + size * 0.1 && !Duplicates(open, f);
                            // a ruled line between them: two cells, not one line
                            bool ruled = !inside && gap >= size * 0.15 && Math.Abs(Math.Sin(f.Angle)) < 0.05 && geo.VRules.Count > 0 &&
                                         geo.VerticalRuleBetween((float)open.UMax - 0.3f, (float)f.UMin + 0.3f, (float)f.V - size * 0.2f, (float)f.V + size * 0.7f);
                            if (((gap > -size * 0.5 && gap < size * 0.75) || inside) && ratio <= 1.25f && !ruled)
                            {
                                open.Chars = open.Chars.Concat(new[] { new PdfChar { Code = ' ', Index = -1 } }).Concat(f.Chars).ToList();
                                open.UMax = Math.Max(open.UMax, f.UMax);
                                open.Order = Math.Min(open.Order, f.Order);
                                open.Merged = true;
                                continue;
                            }
                        }
                        open = f;
                        result.Add(f);
                    }
                }
            }
            return result.OrderBy(f => f.Order).Select(f => new KeyValuePair<bool, List<PdfChar>>(f.Merged, f.Chars)).ToList();
        }

        /// <summary>True when the run is drawn over the other one (bold made by printing twice, shadows).</summary>
        private static bool Duplicates(Fragment a, Fragment b)
        {
            var ac = a.Chars.Where(c => !IsSpace(c.Code)).ToList();
            foreach (var c in b.Chars.Where(c => !IsSpace(c.Code)))
                if (ac.Any(o => o.Code == c.Code && Math.Abs(o.Left - c.Left) < 1f && Math.Abs(o.Bottom - c.Bottom) < 1f)) return true;
            return false;
        }

        private static double StartAlong(PdfChar c, double cos, double sin)
        {
            return Math.Min(c.Left * cos + c.Bottom * sin, Math.Min(c.Right * cos + c.Top * sin, Math.Min(c.Left * cos + c.Top * sin, c.Right * cos + c.Bottom * sin)));
        }

        private static double EndAlong(PdfChar c, double cos, double sin)
        {
            return Math.Max(c.Left * cos + c.Bottom * sin, Math.Max(c.Right * cos + c.Top * sin, Math.Max(c.Left * cos + c.Top * sin, c.Right * cos + c.Bottom * sin)));
        }

        /// <summary>True when <paramref name="c"/> cannot continue the line that ended with <paramref name="p"/>.</summary>
        private static bool BreaksLine(PdfChar p, PdfChar c)
        {
            float size = Math.Max(4f, Math.Max(p.FontSize, c.FontSize));
            if (Math.Abs(AngleDiff(p.Angle, c.Angle)) > 0.05) return true;
            double cos = Math.Cos(p.Angle), sin = Math.Sin(p.Angle);
            // positions along (u) and across (v) the baseline direction
            double du = (c.OriginX - p.OriginX) * cos + (c.OriginY - p.OriginY) * sin;
            double dv = -(c.OriginX - p.OriginX) * sin + (c.OriginY - p.OriginY) * cos;
            if (Math.Abs(dv) > size * 0.6) return true;                 // different baseline
            bool rtlPair = UnicodeProps.IsStrongRtl(p.Code) || UnicodeProps.IsStrongRtl(c.Code);
            float pAdvance = (float)Math.Max(p.Right - p.Left, size * 0.3);
            if (!rtlPair)
            {
                if (du < -size * 0.5) return true;                       // went back to the left: new line
                double gap = du - pAdvance;
                if (gap > size * 2.2) return true;                       // wide gap: another column or cell
            }
            else if (Math.Abs(du) > size * 3.5)
            {
                return true;
            }
            return false;
        }

        private static double AngleDiff(float a, float b)
        {
            double d = a - b;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            return d;
        }

        private static void Flush(List<PdfChar> chars, List<DocLine> lines, bool geometric)
        {
            FlushCore(new List<PdfChar>(chars), lines, geometric);
        }

        private static void FlushCore(List<PdfChar> chars, List<DocLine> lines, bool geometric)
        {
            while (chars.Count > 0 && IsSpace(chars[chars.Count - 1].Code)) chars.RemoveAt(chars.Count - 1);
            while (chars.Count > 0 && IsSpace(chars[0].Code)) chars.RemoveAt(0);
            if (chars.Count == 0) return;
            var visible = chars.Where(c => !IsSpace(c.Code)).ToList();
            var line = new DocLine();
            bool rtl = visible.Count(c => UnicodeProps.IsStrongRtl(c.Code)) * 2 > visible.Count(c => char.IsLetter(SafeChar(c.Code)) || UnicodeProps.IsStrongRtl(c.Code));
            if (geometric || visible.Any(c => UnicodeProps.IsStrongRtl(c.Code)))
            {
                // the order on the page is the truth: characters left to right (along the baseline), spaces
                // where the gaps (or the PDF's own space characters) are, then right-to-left text back into reading order
                var spaces = chars.Where(c => IsSpace(c.Code) && !c.Generated && c.Right - c.Left > 0.01f).ToList();
                line.Text = GeometricText(visible, rtl, spaces);
                foreach (var c in visible) line.Chars.Add(c.Index);
            }
            else
            {
                var sb = new StringBuilder();
                bool lastSpace = false;
                foreach (var c in chars)
                {
                    if (IsSpace(c.Code))
                    {
                        if (!lastSpace) sb.Append(' ');
                        lastSpace = true;
                        continue;
                    }
                    lastSpace = false;
                    sb.Append(CharText(c.Code));
                    line.Chars.Add(c.Index);
                }
                line.Text = sb.ToString();
            }
            var box = new Box(visible[0].Left, visible[0].Bottom, visible[0].Right, visible[0].Top);
            foreach (var c in visible) box = box.Union(new Box(c.Left, c.Bottom, c.Right, c.Top));
            line.Box = box;
            line.FontSize = Median(visible.Select(c => c.FontSize));
            line.Bold = visible.Count(c => c.Bold) * 2 > visible.Count;
            line.Italic = visible.Count(c => c.Italic) * 2 > visible.Count;
            line.Color = visible.GroupBy(c => c.Color).OrderByDescending(g => g.Count()).First().Key;
            line.FontName = visible.GroupBy(c => c.FontName ?? string.Empty).OrderByDescending(g => g.Count()).First().Key;
            line.Angle = visible[0].Angle;
            line.Rtl = rtl;
            line.Baseline = Median(visible.Select(c => c.OriginY));
            var first = visible.OrderBy(c => Along(c, line.Angle)).First();
            var last = visible.OrderBy(c => Along(c, line.Angle)).Last();
            line.OriginX = first.OriginX;
            line.OriginY = first.OriginY;
            line.Advance = line.IsRotated
                ? (float)(Along(last, line.Angle) - Along(first, line.Angle)) + line.FontSize * 0.6f
                : box.W;
            lines.Add(line);
        }

        private static string CharText(int code)
        {
            return char.ConvertFromUtf32(code >= 0 && code <= 0x10FFFF && (code < 0xD800 || code > 0xDFFF) ? code : 0xFFFD);
        }

        /// <summary>Text of a line from where its characters are: visual order, gaps as spaces, then logical order.</summary>
        internal static string GeometricText(List<PdfChar> visible, bool rtl, List<PdfChar> spaces = null)
        {
            if (visible.Count == 0) return string.Empty;
            float angle = visible[0].Angle;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            // combining marks stay with the letter before them
            var clusters = new List<List<PdfChar>>();
            foreach (var c in visible)
            {
                bool mark = c.Code < 0x10000 && char.GetUnicodeCategory((char)c.Code) == System.Globalization.UnicodeCategory.NonSpacingMark;
                if (mark && clusters.Count > 0) clusters[clusters.Count - 1].Add(c);
                else clusters.Add(new List<PdfChar> { c });
            }
            var ordered = clusters.OrderBy(k => (StartAlong(k[0], cos, sin) + EndAlong(k[0], cos, sin)) / 2).ToList();
            float size = Math.Max(1f, Median(visible.Select(c => c.FontSize)));
            // the line as it sits on the page, left to right: clusters, with spaces where the gaps are
            var parts = new List<string>();
            bool anyRtl = false;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                {
                    double end = EndAlong(ordered[i - 1][0], cos, sin), start = StartAlong(ordered[i][0], cos, sin);
                    double gap = start - end;
                    bool spaceChar = spaces != null && spaces.Any(sp =>
                    {
                        double mid = (StartAlong(sp, cos, sin) + EndAlong(sp, cos, sin)) / 2;
                        return mid > end - size * 0.05 && mid < start + size * 0.05;
                    });
                    if (gap > size * 0.17 || (spaceChar && gap > size * 0.05)) parts.Add(" ");
                }
                var cluster = new StringBuilder();
                foreach (var c in ordered[i])
                {
                    cluster.Append(CharText(c.Code));
                    if (UnicodeProps.IsStrongRtl(c.Code)) anyRtl = true;
                }
                parts.Add(cluster.ToString());
            }
            if (!anyRtl) return string.Concat(parts);
            // right-to-left text is read from the right; numbers and Latin inside it keep their order
            return RtlText.VisualLineToLogical(parts, rtl);
        }

        private static double Along(PdfChar c, float angle)
        {
            return c.OriginX * Math.Cos(angle) + c.OriginY * Math.Sin(angle);
        }

        private static char SafeChar(int code)
        {
            return code >= 0 && code < 0xD800 ? (char)code : 'x';
        }

        private static bool IsSpace(int code)
        {
            return code == ' ' || code == 0xA0 || code == 0x3000 || code == '\t';
        }

        internal static float Median(IEnumerable<float> values)
        {
            var v = values.Where(x => x > 0 && !float.IsNaN(x)).OrderBy(x => x).ToList();
            if (v.Count == 0) return 0;
            return v[v.Count / 2];
        }

        // ------------------------------------------------------------------ lines to blocks
        /// <summary>
        /// Groups lines into blocks, classifies them and sets the reading order.
        /// <paramref name="bodySize"/> is the document's usual text size (0: work it out from this page).
        /// </summary>
        public static List<DocBlock> BuildBlocks(DocPage page, List<DocLine> lines, float bodySize = 0, PageGeometry geometry = null)
        {
            var geo = geometry ?? PageGeometry.Empty;
            if (bodySize <= 0) bodySize = BodySize(lines);
            var blocks = new List<DocBlock>();
            var open = new List<DocBlock>();
            var cellLike = CellLikeLines(lines, page.Width);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text)) continue;
                DocBlock target = null;
                if (!line.IsRotated && !ListStart.IsMatch(line.Text))
                {
                    float best = float.MaxValue;
                    // newest first: the previous line usually continues the paragraph
                    // (drawings list their texts in any order: the paragraph may have been left a while ago)
                    for (int i = open.Count - 1; i >= 0 && i >= open.Count - 32; i--)
                    {
                        float score;
                        var tail = open[i].Lines[open[i].Lines.Count - 1];
                        if (cellLike.Contains(tail) && cellLike.Contains(line)) continue; // two table rows
                        if (CanContinue(open[i], line, out score) && score < best && !RuledApart(geo, open[i], line)) { best = score; target = open[i]; }
                    }
                }
                if (target == null)
                {
                    target = new DocBlock { Page = page.Index, Angle = line.Angle };
                    blocks.Add(target);
                    open.Add(target);
                    if (open.Count > 48) open.RemoveAt(0);
                }
                target.Lines.Add(line);
                target.Box = target.Lines.Count == 1 ? line.Box : target.Box.Union(line.Box);
            }

            foreach (var b in blocks) Finish(b, page, bodySize);
            DetectTables(page, blocks);
            ClassifyLabels(blocks, bodySize);
            Order(page, blocks);
            for (int i = 0; i < blocks.Count; i++) blocks[i].Id = "p" + (page.Index + 1) + "-" + (blocks[i].Order + 1);
            return blocks;
        }

        /// <summary>A ruled line between the block's last line and the next one (rows of a table, fields of a title block).</summary>
        private static bool RuledApart(PageGeometry geo, DocBlock block, DocLine line)
        {
            if (geo.HRules.Count == 0) return false;
            var last = block.Lines[block.Lines.Count - 1];
            float xL = Math.Max(block.Box.L, line.Box.L), xR = Math.Min(block.Box.R, line.Box.R);
            if (xR <= xL) { xL = Math.Min(block.Box.L, line.Box.L); xR = Math.Max(block.Box.R, line.Box.R); }
            bool below = line.Baseline <= last.Baseline;
            return geo.RuleBetweenLines(below ? last : line, below ? line : last, xL, xR);
        }

        /// <summary>
        /// Lines that look like table cells: short pieces of text sharing a baseline with at least two others
        /// close by (a row of a table). Callouts of two drawings side by side, or two columns of text, are
        /// not rows. Two-column body text has wider lines and is not affected.
        /// </summary>
        private static HashSet<DocLine> CellLikeLines(List<DocLine> lines, float pageWidth)
        {
            var result = new HashSet<DocLine>();
            var horizontal = lines.Where(l => !l.IsRotated).OrderBy(l => l.Baseline).ToList();
            for (int i = 0; i < horizontal.Count; i++)
            {
                var l = horizontal[i];
                float tol = Math.Max(1f, l.FontSize * 0.3f);
                var same = horizontal.Where(o => Math.Abs(o.Baseline - l.Baseline) <= tol).OrderBy(o => o.Box.L).ToList();
                if (same.Count < 2) continue;
                // running prose in columns: every line on the baseline is a long run of words
                if (same.All(o => o.Text.Length >= 28 && o.Text.Count(ch => ch == ' ') >= 4)) continue;
                // the row: the lines chained to this one by gaps a table's columns leave
                float em = Math.Max(4f, l.FontSize);
                int at = same.IndexOf(l);
                int a = at, z = at;
                while (a > 0 && same[a].Box.L - same[a - 1].Box.R <= em * 20f) a--;
                while (z < same.Count - 1 && same[z + 1].Box.L - same[z].Box.R <= em * 20f) z++;
                bool row = z - a + 1 >= 3;
                // two narrow ones, one of them a word or two: a label and its value (two columns of
                // longer lines, like a legend, are read as paragraphs)
                if (!row && z > a)
                {
                    var x = same[a]; var y = same[z];
                    Func<DocLine, bool> shortText = o => o.Text.Trim().Length <= 16 && o.Text.Trim().Count(ch => ch == ' ') <= 1;
                    row = x.Box.W < pageWidth * 0.3f && y.Box.W < pageWidth * 0.3f && y.Box.L - x.Box.R <= em * 10f && (shortText(x) || shortText(y));
                }
                if (row)
                    for (int k = a; k <= z; k++) result.Add(same[k]);
            }
            return result;
        }

        /// <summary>The most common text size (weighted by characters), the size body text uses.</summary>
        public static float BodySize(IEnumerable<DocLine> lines)
        {
            var hist = new Dictionary<float, int>();
            foreach (var l in lines)
            {
                if (l.FontSize <= 0) continue;
                float k = (float)Math.Round(l.FontSize * 2) / 2f;
                int n;
                hist.TryGetValue(k, out n);
                hist[k] = n + l.Text.Length;
            }
            return hist.Count == 0 ? 10f : hist.OrderByDescending(kv => kv.Value).First().Key;
        }

        private static bool CanContinue(DocBlock block, DocLine line, out float score)
        {
            score = 0;
            var last = block.Lines[block.Lines.Count - 1];
            if (block.IsRotated || last.IsRotated) return false;
            if ((last.Chars.Count == 0) != (line.Chars.Count == 0)) return false; // OCR text and real text stay apart
            if (ListStart.IsMatch(line.Text) && block.Lines.Count > 0) return false;
            float size = Math.Max(last.FontSize, line.FontSize);
            if (size <= 0) return false;
            float ratio = Math.Max(last.FontSize, line.FontSize) / Math.Max(0.1f, Math.Min(last.FontSize, line.FontSize));
            if (ratio > 1.18f) return false;
            if (last.Bold != line.Bold && block.Lines.Count == 1 && (last.Text.Length < 80 || line.Text.Length < 80)) return false;
            // vertical: the new line sits just below the last one
            float pitch = last.Baseline - line.Baseline;
            if (pitch <= size * 0.5f || pitch > size * 1.9f) return false;
            if (block.Lines.Count >= 2)
            {
                float prevPitch = block.Lines[block.Lines.Count - 2].Baseline - last.Baseline;
                // paragraph spacing (OCR baselines are estimates, so allow them more play)
                if (Math.Abs(pitch - prevPitch) > size * (line.FontName == string.Empty && last.FontName == string.Empty ? 0.6f : 0.45f)) return false;
            }
            // horizontal: overlapping columns, or the same left / right / centre alignment
            float overlap = block.Box.OverlapX(line.Box);
            float minW = Math.Min(block.Box.W, line.Box.W);
            bool aligned = Math.Abs(block.Box.L - line.Box.L) < size * 1.5f || Math.Abs(block.Box.R - line.Box.R) < size * 1.5f ||
                           Math.Abs(block.Box.CX - line.Box.CX) < size;
            if (overlap < minW * 0.5f && !aligned) return false;
            if (overlap <= 0) return false;
            // a heading-like line (short, ends without punctuation) followed by body text starts a new block
            if (block.Lines.Count == 1 && last.FontSize > line.FontSize * 1.1f) return false;
            score = Math.Abs(pitch - size * 1.2f) + Math.Abs(block.Box.L - line.Box.L) * 0.1f;
            return true;
        }

        private static void Finish(DocBlock b, DocPage page, float bodySize)
        {
            var lines = b.Lines;
            b.FontSize = Median(lines.Select(l => l.FontSize));
            b.Bold = lines.Count(l => l.Bold) * 2 > lines.Count;
            b.Italic = lines.Count(l => l.Italic) * 2 > lines.Count;
            b.Color = lines.GroupBy(l => l.Color).OrderByDescending(g => g.Sum(l => l.Text.Length)).First().Key;
            b.FontName = lines.GroupBy(l => l.FontName).OrderByDescending(g => g.Sum(l => l.Text.Length)).First().Key;
            b.SourceRtl = lines.Count(l => l.Rtl) * 2 > lines.Count;
            b.Angle = lines[0].Angle;
            b.LinePitch = 1.2f;
            if (lines.Count >= 2 && b.FontSize > 0)
            {
                var pitches = new List<float>();
                for (int i = 1; i < lines.Count; i++) pitches.Add((lines[i - 1].Baseline - lines[i].Baseline) / b.FontSize);
                float p = Median(pitches);
                if (p > 0.8f && p < 2.5f) b.LinePitch = p;
            }
            b.Align = DetectAlign(b);
            b.Source = JoinLines(lines.Select(l => l.Text).ToList());

            // classification
            int len = b.Source.Length;
            bool endsSentence = Regex.IsMatch(b.Source, @"[\.\!\?。！？:;]\s*$");
            float pageH = page.Height; // blocks are in the page frame: 0..Height, y up
            if (ListStart.IsMatch(b.Source)) b.Kind = BlockKind.ListItem;
            else if (lines.Count <= 3 && len <= 140 && LanguageGuess.HasWords(b.Source) && !TextSegmenter.IsCodeLike(b.Source.Trim()) && (b.FontSize >= bodySize * 1.2f || (b.Bold && b.FontSize >= bodySize * 0.95f && !endsSentence)))
            {
                b.Kind = BlockKind.Heading;
                b.Level = b.FontSize >= bodySize * 1.75f ? 1 : b.FontSize >= bodySize * 1.3f ? 2 : 3;
            }
            else if (len <= 90 && lines.Count <= 2 && (b.Box.B > pageH * 0.93f || b.Box.T < pageH * 0.07f))
                b.Kind = BlockKind.HeaderFooter;
            else if (b.FontSize > 0 && b.FontSize <= bodySize * 0.85f && len <= 200)
                b.Kind = BlockKind.Caption;
            else
                b.Kind = BlockKind.Paragraph;
        }

        private static BlockAlign DetectAlign(DocBlock b)
        {
            if (b.Lines.Count < 2) return b.SourceRtl ? BlockAlign.Right : BlockAlign.Left;
            float tol = Math.Max(2f, b.FontSize * 0.6f);
            int leftAligned = b.Lines.Count(l => Math.Abs(l.Box.L - b.Box.L) < tol);
            int rightAligned = b.Lines.Count(l => Math.Abs(l.Box.R - b.Box.R) < tol);
            int centred = b.Lines.Count(l => Math.Abs(l.Box.CX - b.Box.CX) < tol);
            int n = b.Lines.Count;
            if (leftAligned == n && rightAligned >= n - 1 && n >= 3) return BlockAlign.Justify;
            if (centred == n && leftAligned < n) return BlockAlign.Center;
            if (rightAligned == n && leftAligned < n) return BlockAlign.Right;
            if (b.SourceRtl && rightAligned >= leftAligned) return BlockAlign.Right;
            return BlockAlign.Left;
        }

        /// <summary>Joins wrapped lines into one paragraph, repairing words split with a hyphen.</summary>
        public static string JoinLines(IList<string> lines)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.Length == 0) continue;
                if (sb.Length == 0) { sb.Append(t); continue; }
                char lastCh = sb[sb.Length - 1];
                char firstCh = t[0];
                if ((lastCh == '-' || lastCh == '­') && sb.Length >= 2 && char.IsLetter(sb[sb.Length - 2]) && char.IsLower(firstCh))
                {
                    sb.Length--; // hyphenated word: join without the hyphen
                    sb.Append(t);
                }
                else if (IsCjk(lastCh) && IsCjk(firstCh))
                {
                    sb.Append(t);
                }
                else
                {
                    sb.Append(' ').Append(t);
                }
            }
            return sb.ToString();
        }

        public static bool IsCjk(char c)
        {
            return (c >= 0x3040 && c <= 0x30FF) || (c >= 0x3400 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF) ||
                   (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0xFF00 && c <= 0xFFEF) || (c >= 0x3000 && c <= 0x303F);
        }

        // ------------------------------------------------------------------ tables
        /// <summary>
        /// Finds grids of short blocks: rows whose blocks share a top edge, repeated over rows whose cells
        /// line up in columns. Marks them as table cells with row and column numbers.
        /// </summary>
        private static void DetectTables(DocPage page, List<DocBlock> blocks)
        {
            var candidates = blocks.Where(b => !b.IsRotated && b.Lines.Count <= 4 && b.Source.Length <= 160 && b.Kind != BlockKind.HeaderFooter).ToList();
            if (candidates.Count < 4) return;
            // rows: blocks whose tops are level
            var rows = new List<List<DocBlock>>();
            foreach (var b in candidates.OrderByDescending(b => b.Box.T))
            {
                float tol = Math.Max(2f, b.FontSize * 0.6f);
                var row = rows.FirstOrDefault(r => Math.Abs(r[0].Box.T - b.Box.T) <= tol);
                if (row == null) rows.Add(row = new List<DocBlock>());
                row.Add(b);
            }
            // text far away on the same line (a note on the left of a sheet, a title block on the right) is not one row
            rows = rows.Select(r => r.OrderBy(b => b.Box.L).ToList()).SelectMany(SplitAtWideGaps)
                       .Where(r => r.Count >= 2).OrderByDescending(r => r[0].Box.T).ToList();
            if (rows.Count < 2) return;

            // chain the rows below each other whose cells share column positions
            int tableId = page.TableCount;
            var used = new HashSet<DocBlock>();
            var taken = new HashSet<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (taken.Contains(i)) continue;
                var group = new List<List<DocBlock>> { rows[i] };
                var members = new List<int> { i };
                for (int j = i + 1; j < rows.Count; j++)
                {
                    if (taken.Contains(j)) continue;
                    var prev = group[group.Count - 1];
                    var next = rows[j];
                    float gl = group.SelectMany(r => r).Min(b => b.Box.L), gr = group.SelectMany(r => r).Max(b => b.Box.R);
                    if (next.Max(b => b.Box.R) < gl || next.Min(b => b.Box.L) > gr) continue;   // beside the table, not below it
                    float size = Math.Max(4f, prev.Average(b => b.FontSize));
                    float gap = prev.Min(b => b.Box.B) - next.Max(b => b.Box.T);
                    if (gap > size * 3f) break;
                    if (ColumnMatch(prev, next, size) < 0.6) break;
                    group.Add(next);
                    members.Add(j);
                }
                int cells = group.Sum(r => r.Count);
                // two rows of two labels are common on drawings; a table has more structure
                if ((group.Count >= 3 || group.Max(r => r.Count) >= 3) && cells >= 4 && group.Max(r => r.Count) >= 2)
                {
                    var columns = ColumnStarts(group);
                    for (int r = 0; r < group.Count; r++)
                    {
                        foreach (var b in group[r])
                        {
                            if (!used.Add(b)) continue;
                            b.Kind = BlockKind.TableCell;
                            b.Table = tableId;
                            b.Row = r;
                            b.Col = NearestColumn(columns, b);
                        }
                    }
                    tableId++;
                    taken.UnionWith(members);
                }
            }
            page.TableCount = tableId;
        }

        /// <summary>Splits a row (sorted left to right) where a gap is much wider than the others.</summary>
        private static IEnumerable<List<DocBlock>> SplitAtWideGaps(List<DocBlock> row)
        {
            if (row.Count < 3) { yield return row; yield break; }
            var gaps = new List<float>();
            for (int k = 1; k < row.Count; k++) gaps.Add(row[k].Box.L - row[k - 1].Box.R);
            float size = Math.Max(4f, row.Average(b => b.FontSize));
            float limit = Math.Max(size * 15f, Math.Max(1f, gaps.Min()) * 4f);
            var part = new List<DocBlock> { row[0] };
            for (int k = 1; k < row.Count; k++)
            {
                if (gaps[k - 1] > limit) { yield return part; part = new List<DocBlock>(); }
                part.Add(row[k]);
            }
            yield return part;
        }

        private static double ColumnMatch(List<DocBlock> a, List<DocBlock> b, float size)
        {
            int matched = 0;
            foreach (var x in b)
                if (a.Any(y => Math.Abs(y.Box.L - x.Box.L) < size * 2 || Math.Abs(y.Box.R - x.Box.R) < size * 2 || Math.Abs(y.Box.CX - x.Box.CX) < size * 2 || y.Box.OverlapX(x.Box) > Math.Min(x.Box.W, y.Box.W) * 0.5f))
                    matched++;
            return matched / (double)Math.Max(1, Math.Min(a.Count, b.Count));
        }

        private static List<float> ColumnStarts(List<List<DocBlock>> rows)
        {
            var lefts = rows.SelectMany(r => r).Select(b => b.Box.L).OrderBy(x => x).ToList();
            var cols = new List<float>();
            float tol = Math.Max(6f, rows.SelectMany(r => r).Average(b => b.FontSize) * 1.5f);
            foreach (var x in lefts) if (cols.Count == 0 || x - cols[cols.Count - 1] > tol) cols.Add(x);
            return cols;
        }

        private static int NearestColumn(List<float> cols, DocBlock b)
        {
            int best = 0;
            for (int i = 1; i < cols.Count; i++) if (Math.Abs(cols[i] - b.Box.L) < Math.Abs(cols[best] - b.Box.L)) best = i;
            return best;
        }

        /// <summary>Short isolated texts (drawing callouts, dimensions, tags) are labels: kept on one line.</summary>
        private static void ClassifyLabels(List<DocBlock> blocks, float bodySize)
        {
            foreach (var b in blocks)
            {
                if (b.Kind != BlockKind.Paragraph && b.Kind != BlockKind.Caption) continue;
                if (b.Lines.Count == 1 && b.Source.Length <= 40 && !Regex.IsMatch(b.Source, @"[\.\!\?]\s*$")) b.Kind = BlockKind.Label;
                if (b.IsRotated) b.Kind = BlockKind.Label;
            }
        }

        // ------------------------------------------------------------------ reading order
        /// <summary>
        /// Recursive XY-cut: split the page at wide horizontal gaps (top to bottom), then at vertical
        /// gaps (columns: left to right, right to left for RTL text), and repeat inside each part.
        /// </summary>
        private static void Order(DocPage page, List<DocBlock> blocks)
        {
            bool rtl = blocks.Count(b => b.SourceRtl) * 2 > blocks.Count;
            var ordered = new List<DocBlock>();
            Cut(blocks, ordered, rtl, true, 0);
            for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        }

        private static void Cut(List<DocBlock> items, List<DocBlock> output, bool rtl, bool horizontalFirst, int depth)
        {
            if (items.Count <= 1 || depth > 24)
            {
                output.AddRange(items.OrderByDescending(b => b.Box.T).ThenBy(b => rtl ? -b.Box.R : b.Box.L));
                return;
            }
            float size = Math.Max(4f, Median(items.Select(b => b.FontSize)));
            var parts = horizontalFirst ? SplitRows(items, size * 0.5f) : SplitColumns(items, size * 1.5f, rtl);
            if (parts.Count <= 1)
            {
                parts = horizontalFirst ? SplitColumns(items, size * 1.5f, rtl) : SplitRows(items, size * 0.5f);
                if (parts.Count <= 1)
                {
                    output.AddRange(items.OrderByDescending(b => b.Box.T).ThenBy(b => rtl ? -b.Box.R : b.Box.L));
                    return;
                }
                horizontalFirst = !horizontalFirst;
            }
            foreach (var part in parts) Cut(part, output, rtl, !horizontalFirst, depth + 1);
        }

        private static List<List<DocBlock>> SplitRows(List<DocBlock> items, float minGap)
        {
            var sorted = items.OrderByDescending(b => b.Box.T).ToList();
            var parts = new List<List<DocBlock>>();
            var current = new List<DocBlock>();
            float bottom = float.MaxValue;
            foreach (var b in sorted)
            {
                if (current.Count > 0 && b.Box.T < bottom - minGap)
                {
                    parts.Add(current);
                    current = new List<DocBlock>();
                    bottom = float.MaxValue;
                }
                current.Add(b);
                bottom = Math.Min(bottom, b.Box.B);
            }
            if (current.Count > 0) parts.Add(current);
            return parts;
        }

        private static List<List<DocBlock>> SplitColumns(List<DocBlock> items, float minGap, bool rtl)
        {
            var sorted = items.OrderBy(b => b.Box.L).ToList();
            var parts = new List<List<DocBlock>>();
            var current = new List<DocBlock>();
            float right = float.MinValue;
            foreach (var b in sorted)
            {
                if (current.Count > 0 && b.Box.L > right + minGap)
                {
                    parts.Add(current);
                    current = new List<DocBlock>();
                    right = float.MinValue;
                }
                current.Add(b);
                right = Math.Max(right, b.Box.R);
            }
            if (current.Count > 0) parts.Add(current);
            if (rtl) parts.Reverse();
            return parts;
        }
    }
}
