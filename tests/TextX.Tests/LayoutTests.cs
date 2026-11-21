using System;
using System.Collections.Generic;
using System.Linq;
using TextX.Core.Documents;
using TextX.Core.Documents.Layout;
using TextX.Core.Documents.Native;
using TextX.Core.Documents.Ocr;
using static TextX.Tests.TestRunner;

namespace TextX.Tests
{
    /// <summary>Builds synthetic text layers (what PDFium reports) to test the layout reader.</summary>
    public sealed class FakePage
    {
        public readonly List<PdfChar> Chars = new List<PdfChar>();
        public float CharWidth = 0.5f;

        /// <summary>Adds one line of text at (x, baseline) like PDFium does: generated spaces and a CR LF after it.</summary>
        public FakePage Line(string text, float x, float baseline, float size, bool bold = false, uint color = 0xFF000000)
        {
            float cx = x;
            foreach (var ch in text)
            {
                float w = size * CharWidth;
                var c = new PdfChar
                {
                    Index = Chars.Count, Code = ch, Generated = ch == ' ',
                    Left = cx, Right = cx + (ch == ' ' ? 0 : w), Bottom = baseline - size * 0.22f, Top = baseline + size * 0.78f,
                    OriginX = cx, OriginY = baseline, FontSize = size, FontName = bold ? "Arial-BoldMT" : "ArialMT", Bold = bold, Color = color,
                };
                Chars.Add(c);
                cx += w;
            }
            Chars.Add(new PdfChar { Index = Chars.Count, Code = '\r', Generated = true });
            Chars.Add(new PdfChar { Index = Chars.Count, Code = '\n', Generated = true });
            return this;
        }

        /// <summary>A right-to-left line (Hebrew) in logical order, ending at <paramref name="right"/>; digits and Latin runs stay left to right.</summary>
        public FakePage RtlLine(string text, float right, float baseline, float size, bool bold = false)
        {
            float w = size * CharWidth;
            // visual positions: lay the runs out right to left, each LTR run (digits, Latin) left to right inside
            var runs = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool? ltr = null;
            foreach (var ch in text)
            {
                bool isLtr = char.IsLetterOrDigit(ch) && !(ch >= '\u0590' && ch <= '\u05FF');
                bool neutral = !char.IsLetterOrDigit(ch);
                bool cur = neutral ? (ltr ?? false) && sb.Length > 0 && !(ch == ' ') : isLtr;
                if (ltr.HasValue && cur != ltr.Value) { runs.Add((ltr.Value ? "L" : "R") + sb); sb.Clear(); }
                ltr = cur;
                sb.Append(ch);
            }
            if (sb.Length > 0) runs.Add((ltr == true ? "L" : "R") + sb);
            float cx = right;
            foreach (var run in runs)
            {
                var body = run.Substring(1);
                if (run[0] == 'R')
                {
                    foreach (var ch in body) { cx -= w; Chars.Add(Char(ch, cx, w, baseline, size, bold)); }
                }
                else
                {
                    float start = cx - w * body.Length;
                    float x = start;
                    foreach (var ch in body) { Chars.Add(Char(ch, x, w, baseline, size, bold)); x += w; }
                    cx = start;
                }
            }
            Chars.Add(new PdfChar { Index = Chars.Count, Code = '\r', Generated = true });
            Chars.Add(new PdfChar { Index = Chars.Count, Code = '\n', Generated = true });
            return this;
        }

        private PdfChar Char(char ch, float x, float w, float baseline, float size, bool bold)
        {
            return new PdfChar
            {
                Index = Chars.Count, Code = ch, Generated = ch == ' ',
                Left = x, Right = x + (ch == ' ' ? 0 : w), Bottom = baseline - size * 0.22f, Top = baseline + size * 0.78f,
                OriginX = x, OriginY = baseline, FontSize = size,
                FontName = bold ? "Arial-BoldMT" : "ArialMT", Bold = bold, Color = 0xFF000000,
            };
        }

        public DocPage Analyze(float width = 595, float height = 842)
        {
            var page = new DocPage { Index = 0, Width = width, Height = height };
            page.Blocks = LayoutAnalyzer.BuildBlocks(page, LayoutAnalyzer.BuildLines(Chars));
            page.Extracted = true;
            return page;
        }
    }

    public static partial class CoreTests
    {
        public static void Layout()
        {
            // heading + two paragraphs, one with a hyphenated word
            var page = new FakePage()
                .Line("Project Overview", 72, 780, 18, bold: true)
                .Line("The cabinets are made of oak veneer with a", 72, 750, 11)
                .Line("matt lacquer finish. All edges are transla-", 72, 736, 11)
                .Line("tion tested before delivery.", 72, 722, 11)
                .Line("Doors open to 110 degrees with soft-close", 72, 690, 11)
                .Line("hinges from the approved supplier.", 72, 676, 11)
                .Analyze();
            var blocks = page.InReadingOrder.ToList();
            Equal(3, blocks.Count, "three blocks");
            Equal(BlockKind.Heading, blocks[0].Kind, "heading kind");
            Equal("Project Overview", blocks[0].Source, "heading text");
            Equal(BlockKind.Paragraph, blocks[1].Kind, "paragraph kind");
            Equal("The cabinets are made of oak veneer with a matt lacquer finish. All edges are translation tested before delivery.", blocks[1].Source, "paragraph joined, hyphen repaired");
            Equal(3, blocks[1].Lines.Count, "paragraph lines");
            True(Math.Abs(blocks[1].LinePitch - 14f / 11f) < 0.05, "line pitch " + blocks[1].LinePitch);
            Equal("Doors open to 110 degrees with soft-close hinges from the approved supplier.", blocks[2].Source, "second paragraph");
            True(blocks[1].Lines.SelectMany(l => l.Chars).Count() > 50, "char indices kept for removal");
            Equal("p1-1", blocks[0].Id, "block id");

            // table: 3 rows x 3 columns of short cells
            var table = new FakePage()
                .Line("Code", 72, 500, 9, bold: true).Line("Finish", 200, 500, 9, bold: true).Line("Qty", 330, 500, 9, bold: true)
                .Line("D-01", 72, 486, 9).Line("Oak veneer", 200, 486, 9).Line("4", 330, 486, 9)
                .Line("D-02", 72, 472, 9).Line("White HPL", 200, 472, 9).Line("2", 330, 472, 9)
                .Analyze();
            var cells = table.Blocks.Where(b => b.Kind == BlockKind.TableCell).ToList();
            Equal(9, cells.Count, "table cells");
            var oak = table.Blocks.First(b => b.Source == "Oak veneer");
            Equal(1, oak.Row, "cell row");
            Equal(1, oak.Col, "cell column");
            Equal(1, table.TableCount, "one table");
            Equal("Code", table.InReadingOrder.First().Source, "table read row by row");

            // two columns: the left column is read first
            var cols = new FakePage()
                .Line("Left column first line of text here", 72, 700, 10)
                .Line("Right column first line of text here", 320, 700, 10)
                .Line("left column second line of the text", 72, 686, 10)
                .Line("right column second line of the text", 320, 686, 10)
                .Analyze();
            var order = cols.InReadingOrder.Select(b => b.Source).ToList();
            Equal(2, order.Count, "two column blocks");
            True(order[0].StartsWith("Left column") && order[0].Contains("left column second"), "left column joined first: " + order[0]);
            True(order[1].StartsWith("Right column"), "right column second");

            // labels: short isolated text on a drawing
            var drawing = new FakePage().Line("D-01", 300, 400, 8).Line("OAK VENEER", 100, 200, 8).Analyze();
            True(drawing.Blocks.All(b => b.Kind == BlockKind.Label), "drawing callouts are labels");

            // wide gap on one line splits it (title block cells)
            var gap = new FakePage().Line("Drawn by", 72, 100, 8).Analyze();
            var split = new FakePage();
            split.Line("Drawn by", 72, 100, 8);
            split.Line("J. Smith", 200, 100, 8);
            True(split.Analyze().Blocks.Count == 2, "separate cells on one baseline stay separate");

            // list items
            var list = new FakePage().Line("1. Remove the old units.", 72, 600, 10).Line("2. Fit the new carcasses.", 72, 586, 10).Analyze();
            Equal(2, list.Blocks.Count(b => b.Kind == BlockKind.ListItem), "numbered list items");

            // join rules
            Equal("中文文本", LayoutAnalyzer.JoinLines(new[] { "中文", "文本" }), "CJK lines join without a space");
        }

        public static void PageFrame()
        {
            foreach (int rot in new[] { 0, 1, 2, 3 })
            {
                var p = new DocPage { Width = rot % 2 == 1 ? 842 : 595, Height = rot % 2 == 1 ? 595 : 842, Rotation = rot, OriginX = 10, OriginY = 20 };
                float fx, fy, x, y;
                p.UserToFrame(110, 220, out fx, out fy);
                p.FrameToUser(fx, fy, out x, out y);
                True(Math.Abs(x - 110) < 0.01 && Math.Abs(y - 220) < 0.01, "frame round trip, rotation " + rot);
                True(fx >= 0 && fx <= p.Width && fy >= 0 && fy <= p.Height, "frame point on the page, rotation " + rot);
            }
            // 90 degrees: the user-space bottom-left corner is the displayed top-left
            var r1 = new DocPage { Width = 842, Height = 595, Rotation = 1 };
            float ax, ay;
            r1.UserToFrame(0, 0, out ax, out ay);
            True(Math.Abs(ax) < 0.01 && Math.Abs(ay - 595) < 0.01, "rotation 90: origin at the top-left");
        }

        public static void OcrLines()
        {
            var ocr = new OcrResult { ImageWidth = 2480, ImageHeight = 3508, Dpi = 300 };
            Action<string, int, int, int, int> word = (t, l, top, r, b) => ocr.Words.Add(new OcrWord { Text = t, Left = l, Top = top, Right = r, Bottom = b, Confidence = 90, Line = ocr.Words.Count < 3 ? 0 : 1 });
            word("Hello", 300, 300, 520, 360);
            word("scanned", 545, 300, 860, 360);
            word("world", 885, 300, 1110, 360);
            word("Next", 300, 380, 470, 440);
            word("line", 2000, 380, 2160, 440);
            var page = new DocPage { Width = 595.2f, Height = 841.9f };
            var lines = OcrLayout.BuildLines(ocr, page, 300f / 72f);
            Equal(3, lines.Count, "OCR lines (second split at the gap)");
            Equal("Hello scanned world", lines[0].Text, "OCR line text");
            True(Math.Abs(lines[0].Box.L - 72) < 1 && lines[0].Box.T > 760, "OCR line in page points: " + lines[0].Box);
            True(Math.Abs(lines[0].FontSize - 19.4f) < 1.5f, "OCR font size estimate " + lines[0].FontSize);
        }

        public static void ProjectJson()
        {
            var page = new FakePage().Line("Heading", 72, 780, 18, bold: true).Line("Body text line one", 72, 750, 11).Analyze();
            var project = new DocProject { Name = "test.pdf", SourcePath = "C:\\x\\test.pdf", PageCount = 1, SourceLang = "fr", TargetLang = "en" };
            project.Pages.Add(page);
            page.Blocks[1].Translation = "Texte";
            page.Blocks[1].State = BlockState.Edited;
            var json = project.ToJson();
            var back = DocProject.FromJson(json);
            Equal(project.Id, back.Id, "project id");
            Equal(2, back.Pages[0].Blocks.Count, "blocks restored");
            var b = back.FindBlock(page.Blocks[1].Id);
            Equal("Texte", b.Translation, "translation restored");
            Equal(BlockState.Edited, b.State, "state restored");
            Equal(page.Blocks[1].Lines[0].Chars.Count, b.Lines[0].Chars.Count, "char indices restored");
            Equal(page.Blocks[1].Lines[0].Chars.Last(), b.Lines[0].Chars.Last(), "char index values");
            Equal(BlockKind.Heading, back.Pages[0].Blocks[0].Kind, "kind restored");
        }
    }
}
