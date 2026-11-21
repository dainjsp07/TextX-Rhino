using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TextX.Core.Documents.Imaging;
using TextX.Core.Documents.Native;
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
            foreach (var c in chars) sb.Append(c.Code == '\r' ? "" : char.ConvertFromUtf32(Math.Max(1, c.Code)));
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
                    True(text.Contains("Hello world from Text X"), "text extracted: " + text.Replace("\n", "|"));
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
                    True(img.Width == 893, "render width (" + img.Width + ")");

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
                var text = Text(page.GetChars());
                True(text.Contains("this is a test") && !text.Contains("Bonjour"), "edited text: " + text.Replace("\n", "|"));
                var img = page.Render(1.5f);
                File.WriteAllBytes(Path.Combine(OutDir, "smoke-edited.png"), PngWriter.FromBgra(img.Pixels, img.Width, img.Height));
            }
        }
    }
}
