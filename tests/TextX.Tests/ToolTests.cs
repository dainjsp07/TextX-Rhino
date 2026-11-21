using System;
using System.Collections.Generic;
using System.Linq;
using TextX.Core.Documents.Ocr;
using TextX.Core.Tools;
using static TextX.Tests.TestRunner;

namespace TextX.Tests
{
    public static partial class CoreTests
    {
        public static void Tools()
        {
            // OCR words to text: lines, paragraphs, right-to-left order
            var ocr = new OcrResult();
            Action<string, int, int, int, int, int, int> w = (t, l, top, r, b, para, line) =>
                ocr.Words.Add(new OcrWord { Text = t, Left = l, Top = top, Right = r, Bottom = b, Confidence = 90, Paragraph = para, Line = line });
            w("world", 200, 10, 300, 40, 0, 0); w("Hello", 50, 10, 180, 40, 0, 0);
            w("second", 50, 50, 200, 80, 0, 1);
            w("New", 50, 140, 140, 170, 1, 2); w("paragraph", 150, 140, 330, 170, 1, 2);
            w("עולם", 50, 200, 150, 230, 2, 3); w("שלום", 170, 200, 270, 230, 2, 3);
            Equal("Hello world\nsecond\n\nNew paragraph\n\nשלום עולם", ImageText.ToPlainText(ocr), "OCR text assembled");

            // pixels
            var bgra = new byte[2 * 1 * 4] { 0, 0, 0, 255, 255, 255, 255, 0 };   // black, transparent
            var g = GrayImage.FromBgra(bgra, 2, 1);
            True(g.Pixels[0] == 0 && g.Pixels[1] == 255, "grey from BGRA (transparent is white)");
            var big = new GrayImage(new byte[] { 0, 255, 255, 0 }, 2, 2).Scale(2);
            True(big.Width == 4 && big.Height == 4 && big.Pixels[0] == 0 && big.Pixels[3] == 255, "scaled image");
            var dark = new GrayImage(Enumerable.Repeat((byte)20, 100).ToArray(), 10, 10).DarkOnLight();
            Equal((byte)235, dark.Pixels[0], "dark screenshots inverted");

            // writing check answers
            var json = "{\"language\":{\"name\":\"English (GB)\",\"code\":\"en-GB\",\"detectedLanguage\":{\"name\":\"English (GB)\",\"code\":\"en-GB\"}}," +
                       "\"matches\":[{\"message\":\"Possible spelling mistake found.\",\"shortMessage\":\"Spelling mistake\",\"replacements\":[{\"value\":\"cabinet\"}],\"offset\":4,\"length\":7,\"context\":{\"text\":\"The cabinat doors\"},\"rule\":{\"category\":{\"name\":\"Possible Typo\"}}}," +
                       "{\"message\":\"Use \\\"are\\\".\",\"replacements\":[{\"value\":\"are\"},{\"value\":\"were\"}],\"offset\":18,\"length\":2,\"rule\":{\"category\":{\"name\":\"Grammar\"}}}]}";
            var result = WritingCheck.Parse(json);
            True(result.Language == "en-GB" && result.Issues.Count == 2 && result.Issues[0].Best == "cabinet" && result.Issues[1].Replacements.Count == 2, "writing check answer read");
            var text = "The cabinat doors is oak.";
            Equal("The cabinet doors are oak.", WritingCheck.Apply(text, result.Issues), "all suggestions applied");
            var rest = WritingCheck.AfterApplying(result.Issues, result.Issues[0], "cabinet");
            True(rest.Count == 1 && rest[0].Offset == 18, "offsets after one fix");
            Equal("en-GB", WritingCheck.LanguageCode("en"), "language variant");
            Equal("auto", WritingCheck.LanguageCode("auto"), "automatic language");
            var chunks = WritingCheck.Chunks(string.Join("\n", Enumerable.Repeat("Sentence number one is here.", 50)), 200).ToList();
            True(chunks.All(c => c.Length <= 200) && string.Concat(chunks) == string.Join("\n", Enumerable.Repeat("Sentence number one is here.", 50)), "long text split for the service");
        }
    }
}
