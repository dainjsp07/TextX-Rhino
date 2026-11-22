using System;
using System.Collections.Generic;
using System.Linq;
using TextX.Core.Documents;
using TextX.Core.Documents.Layout;
using TextX.Core.Documents.Native;
using TextX.Core.Documents.Ocr;
using TextX.Core.Documents.Schedules;
using TextX.Core.Text;
using TextX.Core.Translation;
using static TextX.Tests.TestRunner;

namespace TextX.Tests
{
    public static partial class CoreTests
    {
        /// <summary>Right-to-left drawings, title-block checks and OCR clean-up for drawings (made-up content).</summary>
        public static void Drawings()
        {
            // --- lines of a page read back: numbers and Latin keep their order, brackets come out right
            Func<string, bool, string> logical = (visual, rtl) => RtlText.VisualLineToLogical(visual.Select(ch => ch.ToString()).ToList(), rtl);
            Equal("רחוב הדוגמה5, תל אביב", logical("ביבא לת ,5המגודה בוחר", true), "comma stays with the number");
            Equal("חזית אחורית JRY-01.1", logical("JRY-01.1 תירוחא תיזח", true), "code kept in order");
            Equal("ביצוע (\"SHOP\") ראשון", logical("ןושאר (\"SHOP\") עוציב", true), "brackets around Latin");
            Equal("FURNIPART דגם וגוון", logical("FURNIPART ןווגו םגד", false), "Hebrew inside a Latin line");
            Equal("ביצוע (\"SHOP\") ראשון", RtlText.BalanceBrackets("ביצוע )\"SHOP\") ראשון"), "same bracket on both sides");
            Equal("a) b)", RtlText.BalanceBrackets("a) b)"), "list letters keep their brackets");
            var pieces = TranslationEngine.SplitForLimit(new string('א', 300) + ". " + new string('ב', 300), 480);
            True(pieces.Count == 2 && pieces.All(x => x.Length <= 480), "long text cut for a service limit");
            True(LanguageGuess.IsReference("A-12 3") && !LanguageGuess.IsReference("חתך1"), "a word joined to a number is translated");
            Equal("Scale", DrawingVocabulary.Lookup(TextX.Core.Translation.Glossary.Normalize("קנ\"מ:"), "he", "en"), "title block word");
            Func<string, string> vocab = t => DrawingVocabulary.Lookup(TextX.Core.Translation.Glossary.Normalize(t), "he", "en", t);
            Equal("Scale 1:25", vocab("קנ\"מ1:25"), "scale with its ratio");
            Equal("1 Scale 1:25", vocab("1 קנ\" מ1:25"), "scale with a loose gershayim");
            Equal("Elevation A 02.2 - 21 NM", vocab("חזית A 02.2 - 21 NM"), "elevation reference keeps its case");
            Equal("Plan 02.1 - 21 NM", vocab("תוכנית 02.1 - 21 NM"), "plan reference");
            Equal("Coffee machine", vocab("מ .קפה"), "abbreviation typed with a loose dot");
            Equal("Mitred joints", vocab("חיבורים בגרונג."), "trade word");
            Equal("Inner plinth 2 cm", vocab("צוקל פנימי2ס \"מ"), "plinth with its size");
            True(vocab("חזית הבניין הצפונית") == null, "a sentence is not a reference");

            // --- Hebrew prefixes: only on longer words, only valid combinations
            Equal("laminate", Nice(Vocabulary.Materials.Find("גמר ופורמייקה לבנה")), "prefix ו on a long word");
            True(Vocabulary.Materials.Find("בלבד").Count == 0, "בד is not found inside בלבד");
            True(Vocabulary.Materials.Find("בוטיק").Count == 0, "טיק is not found inside בוטיק");
            Equal("sandwich board", Nice(Vocabulary.Materials.Find("מהסנדוויץ")), "prefix מה on a long word");

            // --- title-block values must look like their field
            True(ScheduleExtractor.FieldValueOk("Scale", "1:20") && ScheduleExtractor.FieldValueOk("Scale", "1/50") && !ScheduleExtractor.FieldValueOk("Scale", "JRY-04"), "scale values");
            True(ScheduleExtractor.FieldValueOk("Telephone", "03-5550123") && !ScheduleExtractor.FieldValueOk("Telephone", "תל אביב"), "phone values");
            True(!ScheduleExtractor.FieldValueOk("Address", ":") && ScheduleExtractor.FieldValueOk("Address", "רחוב הדוגמה 5"), "punctuation is not a value");
            True(!ScheduleExtractor.FieldValueOk("Architect", "03-5550123") && ScheduleExtractor.FieldValueOk("Architect", "סטודיו דוגמה"), "names are words");

            // --- a right-to-left sheet: labels on the right, values on their left
            var sheet = new FakePage()
                .RtlLine("ארון מטבח עליון", 900, 700, 14, bold: true)
                .RtlLine("גמר חוץ:", 900, 640, 9, bold: true).RtlLine("פורמייקה לבנה מט", 850, 640, 9)
                .RtlLine("פרזול:", 900, 625, 9, bold: true).RtlLine("צירים בלום, ידית אלומיניום", 850, 625, 9)
                .RtlLine("קנה מידה", 1150, 120, 8, bold: true).RtlLine("1:20", 1090, 120, 9)
                .RtlLine("תאריך", 1150, 105, 8, bold: true).RtlLine("12/03/2026", 1090, 105, 9)
                .RtlLine("טלפון", 1150, 90, 8, bold: true).RtlLine("קנה מידה", 1090, 90, 9)
                .Analyze(1191, 842);
            var titleLine = sheet.Blocks.FirstOrDefault(b => b.Source.Contains("מטבח"));
            Equal("ארון מטבח עליון", titleLine == null ? "-" : titleLine.Source, "Hebrew line read in logical order");
            var project = new DocProject { Name = "Sheet", PageCount = 1, SourceLang = "he", TargetLang = "en" };
            project.Pages.Add(sheet);
            var set = ScheduleExtractor.Extract(project);
            var info = set.Find(ScheduleKind.ProjectInfo);
            Func<string, string> field = f => info == null ? null : info.Rows.Where(r => r[0] == f).Select(r => r[1]).FirstOrDefault();
            Equal("1:20", field("Scale"), "scale to the left of its label");
            Equal("12/03/2026", field("Date"), "date to the left of its label");
            True(field("Telephone") == null, "a phone label without a number stays empty: " + field("Telephone"));
            var products = set.Find(ScheduleKind.Products);
            var dump = products == null ? "-" : string.Join("\n", products.Rows.Select(r => string.Join(" | ", r.Cells)));
            var cabinet = products == null ? null : products.Rows.FirstOrDefault(r => r[products.Column("Hardware")].Contains("בלום"));
            True(cabinet != null, "hardware strip read:\n" + dump);
            if (cabinet != null)
            {
                True(cabinet[products.Column("Finish")].Contains("פורמייקה"), "exterior finish: " + cabinet[products.Column("Finish")]);
                True(cabinet[products.Column("Category")] != "Hinge", "category is not taken from the hardware: " + cabinet[products.Column("Category")]);
            }

            // --- header and footer zones use the page frame, whatever the crop box origin
            var cropped = new FakePage()
                .Line("Kitchen elevation", 100, 500, 10)
                .Line("Page 2", 100, 20, 8)
                .Analyze(1191, 842);
            True(cropped.Blocks.Count == 2, "two blocks");
            cropped.OriginX = 180; cropped.OriginY = 120;
            var again = LayoutAnalyzer.BuildBlocks(cropped, cropped.Blocks.SelectMany(b => b.Lines).ToList());
            var mid = again.First(b => b.Source.StartsWith("Kitchen", StringComparison.Ordinal));
            var foot = again.First(b => b.Source.StartsWith("Page", StringComparison.Ordinal));
            True(mid.Kind != BlockKind.HeaderFooter && foot.Kind == BlockKind.HeaderFooter, "header/footer by frame position: " + mid.Kind + ", " + foot.Kind);

            // --- OCR on drawings: direction marks removed, line work and unsure Latin misreads dropped
            Equal("NAN", OcrLayout.StripMarks("‎NAN‏"), "direction marks removed");
            var words = new List<OcrWord>
            {
                new OcrWord { Text = "ארון", Confidence = 92 },
                new OcrWord { Text = "Ov", Confidence = 72 },
                new OcrWord { Text = "==", Confidence = 90 },
                new OcrWord { Text = "מבואה", Confidence = 89 },
                new OcrWord { Text = "120", Confidence = 88 },
                new OcrWord { Text = "ו", Confidence = 29 },
            };
            Equal("ארון מבואה 120", string.Join(" ", OcrLayout.DrawingWords(words).Select(w => w.Text)), "drawing OCR words kept");

            // --- outlined drawing text is recognised: many glyph-sized shapes, almost no real letters
            var shapes = Enumerable.Range(0, 700).Select(i => new PdfObjectInfo { Type = 2, Left = i, Bottom = 10, Right = i + 4, Top = 16 }).ToList();
            True(PageReader.LooksOutlined(new List<PdfChar>(), shapes), "outlined text detected");
            var letters = Enumerable.Range(0, 200).Select(i => new PdfChar { Code = 'a' }).ToList();
            True(!PageReader.LooksOutlined(letters, shapes), "a page with real words is read from its text");
        }

        public static void DocxImages()
        {
            Equal(".jpeg", TextX.Core.Documents.Export.DocxWriter.ImageExtension(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }), "JPEG data detected");
            Equal(".png", TextX.Core.Documents.Export.DocxWriter.ImageExtension(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "PNG data detected");
            int w = 4, h = 2;
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = 200; px[i + 1] = 100; px[i + 2] = (byte)(i < 16 ? 0 : 255); px[i + 3] = 255; }
            var small = TextX.Core.Documents.Export.PageImages.Shrink(px, ref w, ref h, 2);
            True(w == 2 && h == 1 && small.Length == 8 && small[0] == 200 && small[3] == 255, "large crops are shrunk by averaging");
        }

        private static string Nice(List<TermMatch> matches)
        {
            return string.Join(",", matches.Select(m => m.Canonical));
        }
    }
}
