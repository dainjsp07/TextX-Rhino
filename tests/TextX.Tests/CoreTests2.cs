using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TextX.Core;
using TextX.Core.Text;
using TextX.Core.Translation;
using TextX.Core.Translation.Providers;
using static TextX.Tests.TestRunner;

namespace TextX.Tests
{
    public static partial class CoreTests
    {
        public static void ArabicShaping()
        {
            foreach (var c in ShapingData.Cases)
            {
                var shaped = RtlText.FromCodePoints(ArabicShaper.Shape(RtlText.ToCodePoints(c[0]), false));
                Equal(c[1], shaped, "shape " + c[0]);
            }
            // Visual output of an Arabic word: shaped, then reversed.
            var visual = RtlText.ToVisual("سلام", new RtlOptions { Direction = BaseDirection.Rtl });
            Equal("ﻡﻼﺳ", visual, "arabic visual");
        }

        public static void Wrapping()
        {
            Func<string, double> measure = s => s.Length; // 1 unit per character
            var lines = RtlText.WrapParagraph("אחת שתיים שלוש ארבע", 9, measure);
            Equal(2, lines.Count, "wrap count");
            Equal("אחת שתיים", lines[0], "wrap line 1");
            Equal("שלוש ארבע", lines[1], "wrap line 2");
            Equal(4, RtlText.WrapParagraph("אחת שתיים שלוש ארבע", 8, measure).Count, "wrap narrower");
            // A long single word is not broken.
            Equal(1, RtlText.WrapParagraph("אבגדהוזחטיכלמנ", 5, measure).Count, "long word");
            // Composer wraps first, then reorders each line.
            var composed = DisplayComposer.Compose("אחת שתיים שלוש", "he", RtlRenderMode.Visual, new RtlOptions(), 9, measure);
            Equal("םייתש תחא\nשולש", composed, "compose wrapped");
            // Logical mode leaves order alone.
            Equal("שלום", DisplayComposer.Compose("שלום", "he", RtlRenderMode.Logical, new RtlOptions()), "logical mode");
            // English is untouched in every mode.
            Equal("Level 1", DisplayComposer.Compose("Level 1", "en", RtlRenderMode.Visual, new RtlOptions()), "english compose");
        }

        public static void Json()
        {
            var obj = Core.Json.Parse("{\"a\":[1,2.5,\"x\\n\\u05d0\"],\"b\":{\"c\":true,\"d\":null}}");
            Equal(2.5, (double)Core.Json.Get(obj, "a", 1), "json number");
            Equal("x\nא", Core.Json.GetString(obj, "a", 2), "json string escapes");
            Equal(true, (bool)Core.Json.Get(obj, "b", "c"), "json bool");
            Equal(null, Core.Json.Get(obj, "b", "d"), "json null");
            var round = Core.Json.Serialize(obj);
            Equal(round, Core.Json.Serialize(Core.Json.Parse(round)), "json round trip");
            Equal("\"q\\\"\\\\\\n\"", Core.Json.Quote("q\"\\\n"), "json quote");
        }

        public static void Segmentation()
        {
            var lines = TextSegmenter.Split("Scale: %<DetailScale(\"x\")>%\nA-101\nRhinoTower Ground Floor", new[] { "RhinoTower" });
            Equal(3, lines.Count, "segment lines");
            Equal(2, lines[0].Count, "line1 segments");
            Equal(true, lines[0][0].Translate, "label translatable");
            Equal(false, lines[0][1].Translate, "field protected");
            Equal(false, lines[1][0].Translate, "drawing code kept");
        }

        public static void Glossary()
        {
            var g = new Glossary();
            g.Add("en", "he", "Scale", "קנה מידה");
            g.Add("en", "he", "Drawn by", "שורטט ע\"י");
            Equal("קנה מידה:", g.Lookup("Scale:", "en", "he"), "glossary punctuation");
            Equal("  קנה מידה ", g.Lookup("  scale ", "en", "he"), "glossary whitespace + case");
            Equal(null, g.Lookup("Scale bar", "en", "he"), "glossary exact only");
            Equal(null, g.Lookup("Scale", "en", "ar"), "glossary language");
            var g2 = Core.Translation.Glossary.FromJson(g.ToJson());
            Equal(2, g2.Entries.Count, "glossary json");
            int n = g2.ImportCsv("# comment\nDate,תאריך\nen,he,Revision,גרסה\nACME", "en", "he");
            Equal(3, n, "glossary csv rows");
            Equal("תאריך", g2.Lookup("Date", "en", "he"), "glossary csv entry");
            Equal(true, g2.DoNotTranslate.Contains("ACME"), "glossary dnt");

            // Engine end-to-end with a fake provider
            var fake = new FakeProvider();
            var engine = new TranslationEngine(fake, null, g2, new TranslationCache());
            var outcome = engine.TranslateManyAsync(new[] { "Date: %<Date()>%\nACME Tower Plan", "Plan" }, "en", "he", CancellationToken.None).Result;
            Equal("תאריך: %<Date()>%\nACME [he:Tower Plan]", outcome.Translations[0], "engine assembly");
            Equal("[he:Plan]", outcome.Translations[1], "engine second");
            Equal(1, outcome.GlossarySegments, "engine glossary count");
            Equal(2, outcome.ServiceSegments, "engine service count");
            var again = engine.TranslateManyAsync(new[] { "Plan" }, "en", "he", CancellationToken.None).Result;
            Equal(1, again.CachedSegments, "engine cache hit");
            Equal(1, fake.Calls, "engine single batch call");

            // Fallback when the primary provider fails
            var failing = new FakeProvider { Fail = true };
            var engine2 = new TranslationEngine(failing, new FakeProvider(), new Glossary(), new TranslationCache());
            var o2 = engine2.TranslateAsync("Section", "en", "he", CancellationToken.None).Result;
            Equal("[he:Section]", o2.Translations[0], "fallback result");
            True(o2.Warning != null, "fallback warning");

            // Cooldown: after a failure the primary is skipped while a fallback exists
            TranslationEngine.ResetCooldowns();
            var flaky = new FakeProvider { Fail = true };
            var backup = new FakeProvider();
            var engine3 = new TranslationEngine(flaky, backup, new Glossary(), new TranslationCache());
            engine3.TranslateAsync("Door", "en", "he", CancellationToken.None).Wait();
            engine3.TranslateAsync("Window", "en", "he", CancellationToken.None).Wait();
            Equal(1, flaky.Calls, "cooldown skips failed provider");
            Equal(2, backup.Calls, "cooldown uses fallback");
            TranslationEngine.ResetCooldowns();
            engine3.TranslateAsync("Wall", "en", "he", CancellationToken.None).Wait();
            Equal(2, flaky.Calls, "cooldown reset retries provider");
            TranslationEngine.ResetCooldowns();

            // Batching respects limits
            var batches = TranslationEngine.MakeBatches(new[] { "aaaa", "bbbb", "cccc" }, 2, 100).ToList();
            Equal(2, batches.Count, "batch by items");
            batches = TranslationEngine.MakeBatches(new[] { "aaaa", "bbbb", "cccc" }, 10, 8).ToList();
            Equal(2, batches.Count, "batch by chars");
        }

        public static void Metadata()
        {
            var store = new Dictionary<string, string>();
            Func<string, string> get = k => store.ContainsKey(k) ? store[k] : null;
            Action<string, string> set = (k, v) => { if (v == null) store.Remove(k); else store[k] = v; };
            Equal(false, TranslationRecord.HasRecord(get), "no record");
            var r = new TranslationRecord { Source = "Ground floor\nPlan", Translation = "קומת קרקע\nתוכנית", WrapWidth = 12.5, PrintAlign = PrintAlignment.Right };
            r.Write(set);
            var back = TranslationRecord.Read(get);
            Equal(r.Source, back.Source, "meta source");
            Equal(r.Translation, back.Translation, "meta translation");
            Equal(PrintAlignment.Right, back.PrintAlign, "meta align");
            Equal(12.5, back.WrapWidth, "meta wrap");
            Equal(TranslationDisplayMode.Dual, back.Mode, "meta mode");
            Equal("Ground floor\nPlan", back.TextFor(TextState.Display), "dual display");
            Equal("קומת קרקע\nתוכנית", back.TextFor(TextState.Print), "dual print");
            Equal("he", back.LanguageFor(TextState.Print), "dual print lang");
            Equal(true, back.NeedsPrintSwap, "dual needs swap");
            back.Mode = TranslationDisplayMode.Translated;
            Equal("קומת קרקע\nתוכנית", back.TextFor(TextState.Display), "translated display");
            Equal(false, back.NeedsPrintSwap, "translated no swap");
            Equal(false, store.ContainsKey(TranslationRecord.KeySavedText), "saved text only while swapped");
            r.State = TextState.Print;
            r.SavedText = "x";
            r.SavedHAlign = 2;
            r.AnchorShift = TranslationRecord.FormatPoint(1.5, -2, 0);
            r.Write(set);
            var swapped = TranslationRecord.Read(get);
            Equal(TextState.Print, swapped.State, "state print");
            Equal(2, swapped.SavedHAlign, "saved align");
            double x, y, z;
            True(TranslationRecord.TryParsePoint(swapped.AnchorShift, out x, out y, out z) && x == 1.5 && y == -2, "saved origin");
            TranslationRecord.Remove(set);
            Equal(0, store.Count, "remove all keys");
        }

        public static void Languages()
        {
            Equal(true, Core.Languages.IsRtl("he"), "he rtl");
            Equal(true, Core.Languages.IsRtl("ar"), "ar rtl");
            Equal(false, Core.Languages.IsRtl("en"), "en ltr");
            Equal("iw", Core.Languages.ForGoogleFree("he"), "google he->iw");
            Equal("HE", Core.Languages.ForDeepL("he", true), "deepl he");
            Equal("EN-US", Core.Languages.ForDeepL("en", true), "deepl en target");
            Equal("EN", Core.Languages.ForDeepL("en", false), "deepl en source");
            Equal("zh-Hans", Core.Languages.ForAzure("zh-CN"), "azure zh");
            True(Core.Languages.All.Count(l => l.IsRightToLeft) >= 5, "rtl languages listed");
        }

        public static void ProviderParsing()
        {
            Equal("שלום עולם", GoogleFreeProvider.ParseResponse("[[[\"שלום \",\"Hello \",null,null,10],[\"עולם\",\"world\",null,null,10]],null,\"en\"]"), "google free parse");
            Equal("קיר", GoogleCloudProvider.ParseResponse("{\"data\":{\"translations\":[{\"translatedText\":\"קיר\"}]}}", 1)[0], "google cloud parse");
            Equal("דלת", DeepLProvider.ParseResponse("{\"translations\":[{\"detected_source_language\":\"EN\",\"text\":\"דלת\"}]}", 1)[0], "deepl parse");
            Equal("חלון", AzureProvider.ParseResponse("[{\"translations\":[{\"text\":\"חלון\",\"to\":\"he\"}]}]", 1)[0], "azure parse");
            Equal("גג", LibreTranslateProvider.ParseResponse("{\"translatedText\":[\"גג\"]}", 1)[0], "libre parse array");
            Equal("גג", LibreTranslateProvider.ParseResponse("{\"translatedText\":\"גג\"}", 1)[0], "libre parse single");
            Equal("רצפה & תקרה", MyMemoryProvider.ParseResponse("{\"responseData\":{\"translatedText\":\"רצפה &amp; תקרה\"},\"responseStatus\":200}"), "mymemory parse");
            var claude = ClaudeProvider.ParseResponse("{\"content\":[{\"type\":\"text\",\"text\":\"[\\\"מדרגות\\\", \\\"מעלית\\\"]\"}]}", 2);
            Equal("מעלית", claude[1], "claude parse");
            bool threw = false;
            try { DeepLProvider.ParseResponse("{\"translations\":[]}", 1); } catch (TranslationException) { threw = true; }
            True(threw, "count mismatch throws");
        }

        private sealed class FakeProvider : ITranslationProvider
        {
            public int Calls;
            public bool Fail;
            public string Id { get { return Fail ? "fail" : "fake"; } }
            public string DisplayName { get { return "Fake"; } }
            public bool RequiresApiKey { get { return false; } }
            public int MaxBatchItems { get { return 10; } }
            public int MaxBatchChars { get { return 1000; } }

            public Task<IList<string>> TranslateAsync(IList<string> texts, string sourceLang, string targetLang, CancellationToken cancellationToken)
            {
                Calls++;
                if (Fail) throw new TranslationException("offline");
                IList<string> r = texts.Select(t => "[" + targetLang + ":" + t + "]").ToList();
                return Task.FromResult(r);
            }
        }
    }
}
