using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TextX.Core.Documents;
using TextX.Core.Documents.Export;
using TextX.Core.Documents.Fonts;
using TextX.Core.Documents.Layout;
using TextX.Core.Translation;
using static TextX.Tests.TestRunner;

namespace TextX.Tests
{
    /// <summary>A predictable translation service for tests: a word list, or "[xx] text".</summary>
    public sealed class DocFakeProvider : ITranslationProvider
    {
        public readonly Dictionary<string, string> Words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int Requests;
        public int MaxItems = 1;

        public string Id { get { return "google-free"; } } // so JoiningProvider wraps it like the real one
        public string DisplayName { get { return "Fake"; } }
        public bool RequiresApiKey { get { return false; } }
        public int MaxBatchItems { get { return MaxItems; } }
        public int MaxBatchChars { get { return 4500; } }

        public Task<IList<string>> TranslateAsync(IList<string> texts, string sourceLang, string targetLang, CancellationToken ct)
        {
            Requests++;
            IList<string> r = texts.Select(t => string.Join("\n", t.Split('\n').Select(line => One(line, targetLang)))).ToList();
            return Task.FromResult(r);
        }

        private string One(string text, string tgt)
        {
            string w;
            if (Words.TryGetValue(text.Trim(), out w)) return w;
            foreach (var kv in Words) text = System.Text.RegularExpressions.Regex.Replace(text, "\\b" + kv.Key + "\\b", kv.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return "[" + tgt + "] " + text;
        }
    }

    public static partial class CoreTests
    {
        private static string FontDir
        {
            get { return Directory.Exists("/usr/share/fonts") ? "/usr/share/fonts" : Environment.GetFolderPath(Environment.SpecialFolder.Fonts); }
        }

        public static void Fonts()
        {
            var catalog = new FontCatalog(FontDir);
            True(catalog.FileCount > 0, "fonts found: " + catalog.FileCount);
            var sans = catalog.Primary(FontKind.Sans, false, false, "en");
            True(sans != null, "sans face");
            if (sans == null) return;
            True(sans.Has('A') && !sans.Font.IsCff, "sans covers Latin (" + sans.Key + ")");
            var w = sans.Font.Measure("Hello", 10);
            True(w > 15 && w < 35, "measured width " + w);
            True(sans.Font.AscentFactor > 0.6 && sans.Font.AscentFactor < 1.2, "ascent " + sans.Font.AscentFactor);
            var bold = catalog.Primary(FontKind.Sans, true, false, "en");
            True(bold != null && bold.Key != sans.Key, "bold face " + (bold == null ? "-" : bold.Key));
            var serif = catalog.Primary(FontCatalog.KindOf("TimesNewRomanPS-BoldMT"), false, false, "en");
            True(serif != null && serif.Key != sans.Key, "serif face " + (serif == null ? "-" : serif.Key));
            Equal(FontKind.Mono, FontCatalog.KindOf("CourierNewPSMT"), "mono kind");

            // a face of a font collection becomes a standalone font that parses again
            var zh = catalog.Primary(FontKind.Sans, false, false, "zh-CN");
            if (zh != null && zh.Key.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
            {
                var data = zh.EmbedData;
                var again = TrueTypeFont.Parse(data);
                True(again.HasChar('中') && again.GlyphCount == zh.Font.GlyphCount, "TTC face extracted as TTF (" + data.Length + " bytes)");
                True(Math.Abs(again.Measure("中文", 10) - zh.Font.Measure("中文", 10)) < 0.01, "extracted face measures the same");
            }
            // subsets keep only the characters used, measure the same and parse again
            var full = sans.EmbedData;
            var sub = sans.Font.Subset(new[] { (int)'H', 'e', 'l', 'o', ' ', '-' });
            var reparsed = TrueTypeFont.Parse(sub);
            True(sub.Length < full.Length / 4, "subset is small: " + sub.Length + " of " + full.Length + " bytes");
            True(reparsed.HasChar('H') && reparsed.HasChar('-') && !reparsed.HasChar('Z'), "subset covers exactly the requested characters");
            True(Math.Abs(reparsed.Measure("Hello - Hello", 10) - sans.Font.Measure("Hello - Hello", 10)) < 0.01, "subset measures the same");
            True(Math.Abs(reparsed.AscentFactor - sans.Font.AscentFactor) < 0.001, "subset keeps the metrics");
            if (zh != null)
            {
                var zsub = zh.Font.Subset(new[] { (int)'中', '文', 'A' });
                var zr = TrueTypeFont.Parse(zsub);
                True(zr.HasChar('中') && zr.HasChar('文') && !zr.HasChar('国') && zsub.Length < 200000, "CJK face subset (" + zsub.Length + " bytes)");
                True(Math.Abs(zr.Measure("中文", 10) - zh.Font.Measure("中文", 10)) < 0.01, "CJK subset measures the same");
            }
            // fallback runs: Latin primary, CJK from a fallback face
            var runs = FontCatalog.Runs("Size 中文 end", sans, catalog.FallbackFaces("en"), 10);
            True(runs.Count >= 2 && runs.Any(r => r.Text.Contains("中")) && runs.First(r => r.Text.Contains("中")).Face != sans, "fallback face for CJK");
        }

        public static void Fitting()
        {
            var catalog = new FontCatalog(FontDir);
            var face = catalog.Primary(FontKind.Sans, false, false, "en");
            if (face == null) { True(false, "no font for fitting tests"); return; }
            var fb = catalog.FallbackFaces("en");
            // fits as it is
            var r = TextFitter.Fit("Short text", 200, 20, 10, 1.2f, false, false, face, fb);
            Equal(1, r.Lines.Count, "one line");
            Equal(10f, r.Size, "no reduction needed");
            // wraps to the box width
            var text = "The quick brown fox jumps over the lazy dog near the riverbank every morning";
            r = TextFitter.Fit(text, 150, 80, 10, 1.2f, false, false, face, fb);
            True(r.Lines.Count > 1 && r.Lines.All(l => l.Width <= 150 * 1.02f + 0.5f), "wrapped within width: " + string.Join(" | ", r.Lines.Select(l => l.Logical)));
            Equal(text, string.Join(" ", r.Lines.Select(l => l.Logical)), "no words lost in wrapping");
            // shrinks when the box is too small
            r = TextFitter.Fit(text, 150, 25, 10, 1.2f, false, false, face, fb);
            True(r.Size < 10 && r.Height <= 25 + r.Size * 0.35f, "reduced to fit: " + r.Size + " lines " + r.Lines.Count);
            // labels stay on one line
            r = TextFitter.Fit("Oak veneer cabinet door", 60, 12, 8, 1.2f, true, false, face, fb);
            Equal(1, r.Lines.Count, "label on one line");
            // RTL lines are reordered for drawing
            var he = catalog.Primary(FontKind.Sans, false, false, "he");
            r = TextFitter.Fit("שלום עולם", 200, 20, 10, 1.2f, false, true, he, catalog.FallbackFaces("he"));
            Equal("םלוע םולש", r.Lines[0].Visual, "Hebrew drawn in visual order");
            // CJK breaks between characters
            var zh = catalog.Primary(FontKind.Sans, false, false, "zh-CN");
            if (zh != null)
            {
                r = TextFitter.Fit("这是一个很长的中文句子需要换行显示在框内", 60, 100, 10, 1.2f, false, false, zh, catalog.FallbackFaces("zh-CN"));
                True(r.Lines.Count >= 3, "CJK wrapped: " + r.Lines.Count + " lines");
            }
        }

        public static void Translator()
        {
            var fake = new DocFakeProvider();
            fake.Words["oak"] = "chêne";
            var glossary = new Glossary();
            glossary.Add("en", "fr", "oak", "bois de chêne");
            var engine = new TranslationEngine(new JoiningProvider(fake), null, glossary, new TranslationCache());
            var page = new FakePage()
                .Line("Kitchen Cabinets", 72, 780, 18, bold: true)
                .Line("The doors are solid oak with a matt finish.", 72, 750, 11)
                .Line("D-01", 300, 600, 8)
                .Line("1200 x 600", 300, 580, 8)
                .Line("Fitted by the joinery team.", 72, 500, 11)
                .Analyze();
            var project = new DocProject { Name = "t", PageCount = 1, SourceLangSetting = "en", TargetLang = "fr" };
            project.Pages.Add(page);
            var translator = new DocumentTranslator(engine);
            int checkpoints = 0;
            translator.Checkpoint = p => checkpoints++;
            translator.TranslateAsync(project, null, false, null, CancellationToken.None).GetAwaiter().GetResult();
            var byText = project.AllBlocks.ToDictionary(b => b.Source);
            Equal(BlockState.Kept, byText["D-01"].State, "code kept");
            Equal(BlockState.Kept, byText["1200 x 600"].State, "dimension kept");
            Equal(BlockState.Translated, byText["Kitchen Cabinets"].State, "heading translated");
            Equal("[fr] Kitchen Cabinets", byText["Kitchen Cabinets"].Translation, "translation stored");
            True(byText["The doors are solid oak with a matt finish."].Translation.Contains("bois de chêne"), "glossary enforced in a sentence: " + byText["The doors are solid oak with a matt finish."].Translation);
            True(fake.Requests <= 3, "paragraphs batched into few requests: " + fake.Requests);
            True(checkpoints >= 1, "checkpoint after a batch");
            True(project.IsTranslated, "project complete");

            // user correction is remembered and reused for identical paragraphs
            var fitted = byText["Fitted by the joinery team."];
            int others = translator.ApplyEdit(project, fitted, "Posé par l'équipe de menuiserie.");
            Equal(0, others, "no identical paragraphs");
            Equal(BlockState.Edited, fitted.State, "edited state");
            var page2 = new FakePage().Line("Fitted by the joinery team.", 72, 500, 11).Analyze();
            page2.Index = 1;
            foreach (var b in page2.Blocks) b.Page = 1;
            project.Pages.Add(page2);
            project.PageCount = 2;
            translator.TranslateAsync(project, null, false, null, CancellationToken.None).GetAwaiter().GetResult();
            Equal("Posé par l'équipe de menuiserie.", page2.Blocks[0].Translation, "correction reused from memory");

            // retranslating everything keeps the user's edits
            translator.TranslateAsync(project, null, true, null, CancellationToken.None).GetAwaiter().GetResult();
            Equal("Posé par l'équipe de menuiserie.", fitted.Translation, "edit survives a full retranslation");
        }

        public static void Exports()
        {
            var page = new FakePage()
                .Line("Kitchen Cabinets", 72, 780, 20, bold: true)
                .Line("The doors are solid oak with a matt finish and soft-close hinges,", 72, 750, 11)
                .Line("fitted by the joinery team before the worktops are installed.", 72, 736, 11)
                .Line("Item", 72, 680, 10, bold: true).Line("Material", 200, 680, 10, bold: true).Line("Finish", 330, 680, 10, bold: true)
                .Line("Doors", 72, 664, 10).Line("Oak veneer", 200, 664, 10).Line("Matt lacquer", 330, 664, 10)
                .Line("Plinth", 72, 648, 10).Line("MDF", 200, 648, 10).Line("RAL 9016", 330, 648, 10)
                .Line("Page 1", 290, 30, 8)
                .Analyze();
            page.Images.Add(new Box(72, 300, 300, 560));
            var project = new DocProject { Name = "Kitchen spec", PageCount = 1, SourceLangSetting = "en", TargetLang = "fr" };
            project.Pages.Add(page);
            var engine = new TranslationEngine(new JoiningProvider(new DocFakeProvider()), null, new Glossary(), new TranslationCache());
            new DocumentTranslator(engine).TranslateAsync(project, null, false, null, CancellationToken.None).GetAwaiter().GetResult();
            True(page.TableCount >= 1, "fake page has a table");

            var dir = Environment.GetEnvironmentVariable("TEXTX_TEST_OUT") ?? Path.Combine(Path.GetTempPath(), "textx-tests");
            Directory.CreateDirectory(dir);
            var docx = Path.Combine(dir, "unit-export.docx");
            var png = TextX.Core.Documents.Imaging.PngWriter.FromGray(Enumerable.Repeat((byte)180, 40 * 30).ToArray(), 40, 30);
            DocxWriter.Write(project, docx, new DocxOptions { Text = ExportText.Bilingual, ImageProvider = (pg, box) => png });
            using (var zip = System.IO.Compression.ZipFile.OpenRead(docx))
            {
                var body = new StreamReader(zip.GetEntry("word/document.xml").Open()).ReadToEnd();
                True(body.Contains("[fr] Kitchen Cabinets") && body.Contains("Kitchen Cabinets"), "bilingual Word export");
                True(body.Contains("Heading1") || body.Contains("Heading2"), "headings use Word heading styles");
                True(body.Contains("<w:tbl>") && body.Contains("[fr] Oak veneer"), "table exported as a Word table");
                True(zip.GetEntry("word/media/image1.png") != null, "image exported");
                System.Xml.Linq.XDocument.Parse(body);
                foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
                    System.Xml.Linq.XDocument.Parse(new StreamReader(e.Open()).ReadToEnd());
                True(true, "Word parts are well-formed XML");
            }
            var txt = TextExporter.Build(project, ExportText.Translated);
            True(txt.Contains("[fr] Kitchen Cabinets") && txt.Contains("\t"), "text export with tab-separated table: " + txt.Replace("\n", " | "));
            Equal("a b -", TranslatedPdfWriter.NormalizeForPdf("a\u00A0b \u00AD\u2011"), "PDF text normalised");
        }

        public static void Joining()
        {
            var fake = new DocFakeProvider();
            var join = new JoiningProvider(fake);
            var r = join.TranslateAsync(new[] { "one", "two", "three" }, "en", "de", CancellationToken.None).GetAwaiter().GetResult();
            Equal(1, fake.Requests, "three paragraphs in one request");
            Equal("[de] two", r[1], "split back by line");
            Equal("bois de chêne massif", DocumentTranslator.ReplaceTerm("chêne massif", "chêne", "bois de chêne"), "term replaced");
            Equal("Bois de chêne massif", DocumentTranslator.ReplaceTerm("Chêne massif", "chêne", "bois de chêne"), "capital kept at sentence start");
            True(DocumentTranslator.ContainsTerm("Solid OAK door", "oak") && !DocumentTranslator.ContainsTerm("cloak", "oak"), "whole-word term match");
        }

        public static void Guessing()
        {
            Equal("he", LanguageGuess.FromScript("שלום עולם, this is mixed"), "Hebrew");
            Equal("fa", LanguageGuess.FromScript("این یک متن فارسی است که پ و گ دارد"), "Persian");
            Equal("uk", LanguageGuess.FromScript("Це український текст із літерою ї"), "Ukrainian");
            Equal("ja", LanguageGuess.FromScript("これは日本語の文章です"), "Japanese");
            Equal("zh-CN", LanguageGuess.FromScript("这是中文"), "Chinese");
            Equal(null, LanguageGuess.FromScript("Bonjour tout le monde"), "Latin left to the service");
            True(!LanguageGuess.HasWords("1200 x 600") && LanguageGuess.HasWords("Oak"), "words vs numbers");
            True(LanguageGuess.IsReference("RAL 9016") && LanguageGuess.IsReference("NCS S 0502-Y") && LanguageGuess.IsReference("EN 13501-1") && LanguageGuess.IsReference("Ø 35 mm"), "references kept as they are");
            True(!LanguageGuess.IsReference("Door 2") && !LanguageGuess.IsReference("RAL") && !LanguageGuess.IsReference("Oak veneer 19 mm"), "words are not references");
            Equal("he", TextX.Core.Translation.Providers.GoogleFreeProvider.ParseDetectedLanguage("[[[\"Hello\",\"שלום\",null,null,10]],null,\"iw\",null,null,null,1,[],[[\"iw\"],null,[1],[\"iw\"]]]"), "detected language parsed");
        }
    }
}
