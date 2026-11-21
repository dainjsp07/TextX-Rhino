using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TextX.Core.Documents;
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
            Equal("he", TextX.Core.Translation.Providers.GoogleFreeProvider.ParseDetectedLanguage("[[[\"Hello\",\"שלום\",null,null,10]],null,\"iw\",null,null,null,1,[],[[\"iw\"],null,[1],[\"iw\"]]]"), "detected language parsed");
        }
    }
}
