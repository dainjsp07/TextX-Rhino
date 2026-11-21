using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TextX.Core.Documents.Imaging;
using TextX.Core.Documents.Native;
using TextX.Core.Documents.Ocr;
using static TextX.Tests.TestRunner;

namespace TextX.Tests
{
    /// <summary>
    /// Tests against the real native libraries (Windows). They run when TEXTX_NATIVES points at the
    /// folder holding textx_pdfium.dll (tools\build.ps1 sets it); outputs go to TEXTX_TEST_OUT.
    /// </summary>
    public static class NativeDocumentTests
    {
        public static string OutDir
        {
            get
            {
                var d = Environment.GetEnvironmentVariable("TEXTX_TEST_OUT");
                if (string.IsNullOrEmpty(d)) d = Path.Combine(Path.GetTempPath(), "textx-test-output");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string FontsDir
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.Fonts); }
        }

        public static bool Available(TextWriter log)
        {
            var natives = Environment.GetEnvironmentVariable("TEXTX_NATIVES");
            if (string.IsNullOrEmpty(natives) || !NativeLibraries.IsWindows)
            {
                log.WriteLine("  skipped (set TEXTX_NATIVES on Windows to run the PDF / OCR engine tests)");
                return false;
            }
            NativeLibraries.Directory = natives;
            return true;
        }

        public static string Text(List<PdfChar> chars)
        {
            var sb = new StringBuilder();
            foreach (var c in chars)
            {
                if (c.Code == '\n') sb.Append('\n');
                else if (c.Code == 0xA0) sb.Append(' ');
                else if (c.Code >= 32 && c.Code <= 0x10FFFF && (c.Code < 0xD800 || c.Code > 0xDFFF)) sb.Append(char.ConvertFromUtf32(c.Code));
            }
            return sb.ToString();
        }

        public static string Codes(List<PdfChar> chars, int max = 40)
        {
            var sb = new StringBuilder();
            foreach (var c in chars.Take(max)) sb.Append(c.Code.ToString("X")).Append(c.Generated ? "g " : " ");
            return sb.ToString();
        }

        public static void Smoke()
        {
            var arial = File.ReadAllBytes(Path.Combine(FontsDir, "arial.ttf"));
            var created = Path.Combine(OutDir, "smoke-created.pdf");
            using (var doc = PdfFile.CreateNew())
            using (var page = doc.AddPage(595, 842))
            {
                var font = doc.LoadFont("arial", arial);
                page.AddText(font, 24, "Hello world from Text X", 72, 760, 0xFF000000);
                page.AddText(font, 12, "Bonjour le monde, ceci est un test.", 72, 720, 0xFF1A3A6A);
                page.AddRectangle(72, 500, 200, 100, 0xFFE8EEF8);
                var px = new byte[100 * 50 * 4];
                for (int y = 0; y < 50; y++)
                    for (int x = 0; x < 100; x++)
                    {
                        int p = (y * 100 + x) * 4;
                        px[p] = (byte)(x * 2); px[p + 1] = (byte)(y * 4); px[p + 2] = 200; px[p + 3] = 255;
                    }
                page.AddImage(px, 100, 50, 300, 500, 200, 100);
                page.CommitChanges();
                doc.Save(created);
            }
            True(File.Exists(created) && new FileInfo(created).Length > 1000, "PDF created and saved");

            using (var doc = PdfFile.Open(created))
            {
                Equal(1, doc.PageCount, "page count");
                using (var page = doc.LoadPage(0))
                {
                    Equal(595f, (float)Math.Round(page.Width), "page width");
                    var chars = page.GetChars();
                    var text = Text(chars);
                    True(text.Contains("Hello world from Text X"), "text extracted: " + text.Replace("\n", "|") + " codes: " + Codes(chars));
                    var h = chars.First(c => c.Code == 'H');
                    True(Math.Abs(h.Left - 72) < 3, "char position x (" + h.Left + ")");
                    True(Math.Abs(h.FontSize - 24) < 0.5, "font size (" + h.FontSize + ")");
                    True((h.FontName ?? "").IndexOf("Arial", StringComparison.OrdinalIgnoreCase) >= 0, "font name (" + h.FontName + ")");
                    True(h.TextObject != IntPtr.Zero, "char maps to its text object");
                    var b = chars.First(c => c.Code == 'B');
                    Equal(0xFF1A3A6Au, b.Color, "text colour");
                    var objects = page.GetObjects();
                    True(objects.Count(o => o.IsText) >= 2, "text objects: " + objects.Count(o => o.IsText));
                    True(objects.Any(o => o.IsImage), "image object");

                    var img = page.Render(1.5f);
                    File.WriteAllBytes(Path.Combine(OutDir, "smoke-created.png"), PngWriter.FromBgra(img.Pixels, img.Width, img.Height));
                    True(Math.Abs(img.Width - 893) <= 1, "render width (" + img.Width + ")");

                    // replace the French line
                    var frenchObjects = new HashSet<IntPtr>(chars.Where(c => c.TextObject != IntPtr.Zero && c.OriginY < 730 && c.OriginY > 710).Select(c => c.TextObject));
                    int removed = 0;
                    foreach (var o in objects.Where(o => frenchObjects.Contains(o.Handle))) if (page.RemoveObject(o)) removed++;
                    True(removed >= 1, "text object removed");
                    var font = doc.LoadFont("arial", arial);
                    page.AddText(font, 12, "Hello world, this is a test.", 72, 720, 0xFF1A3A6A);
                    page.CommitChanges();
                }
                doc.Save(Path.Combine(OutDir, "smoke-edited.pdf"));
            }

            using (var doc = PdfFile.Open(Path.Combine(OutDir, "smoke-edited.pdf")))
            using (var page = doc.LoadPage(0))
            {
                var edited = page.GetChars();
                var text = Text(edited);
                True(text.Contains("this is a test") && !text.Contains("Bonjour"), "edited text: " + text.Replace("\n", "|") + " codes: " + Codes(edited, 60));
                var img = page.Render(1.5f);
                File.WriteAllBytes(Path.Combine(OutDir, "smoke-edited.png"), PngWriter.FromBgra(img.Pixels, img.Width, img.Height));
            }
        }

        public static void Ocr()
        {
            OcrEngine.DataDirectory = Environment.GetEnvironmentVariable("TEXTX_TESSDATA");
            True(!string.IsNullOrEmpty(OcrEngine.Version), "Tesseract version " + OcrEngine.Version);
            RenderedImage img;
            using (var doc = PdfFile.Open(Path.Combine(OutDir, "smoke-created.pdf")))
            using (var page = doc.LoadPage(0))
                img = page.Render(300f / 72f);
            var gray = img.ToGray();
            var osd = OcrEngine.DetectScript(gray, img.Width, img.Height, 300);
            True(osd != null && osd.Script == "Latin", "script detected: " + (osd == null ? "none" : osd.Script + " " + osd.ScriptConfidence + ", rotated " + osd.Orientation));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = OcrEngine.Recognize(gray, img.Width, img.Height, 300, "eng");
            var words = string.Join(" ", result.Words.Select(w => w.Text));
            File.WriteAllText(Path.Combine(OutDir, "ocr-words.txt"), string.Join("\n", result.Words.Select(w =>
                w.Text + "\t" + w.Left + "," + w.Top + "," + w.Right + "," + w.Bottom + "\t" + w.Confidence.ToString("0") + "\tline " + w.Line)));
            True(words.Contains("Hello world from Text X"), "OCR text (" + sw.ElapsedMilliseconds + " ms): " + words);
            var hello = result.Words.FirstOrDefault(w => w.Text == "Hello");
            True(hello != null && Math.Abs(hello.Left - 72 * 300 / 72) < 30, "OCR word position");
            True(result.MeanConfidence > 80, "OCR confidence " + result.MeanConfidence.ToString("0"));
            OcrEngine.Release();
        }
    }
}