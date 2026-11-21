using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TextX.Core.Documents;
using TextX.Core.Documents.Export;
using TextX.Core.Documents.Fonts;
using TextX.Core.Documents.Imaging;
using TextX.Core.Translation;
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

        // ------------------------------------------------------------------ full pipeline on a realistic sample
        private static readonly Dictionary<string, string> FrEn = new Dictionary<string, string>
        {
            { "Cuisine – Lot 4", "Kitchen – Package 4" },
            { "Description des travaux", "Description of works" },
            { "Les caissons sont fabriqués en panneaux de particules mélaminés de 19 mm. Les façades sont en placage de chêne avec une finition laquée mate, appliquée en atelier avant la livraison.", "The carcasses are made of 19 mm melamine-faced particleboard. The fronts are oak veneer with a matt lacquer finish, applied in the workshop before delivery." },
            { "Toutes les quincailleries doivent être approuvées par l'architecte avant la commande.", "All hardware must be approved by the architect before ordering." },
            { "Code", "Code" }, { "Désignation", "Description" }, { "Finition", "Finish" },
            { "Porte haute", "Tall door" }, { "Chêne laqué mat", "Matt lacquered oak" },
            { "Tiroir", "Drawer" }, { "Blanc RAL 9010", "White RAL 9010" },
            { "Page 1 sur 3", "Page 1 of 3" },
            { "Mise en œuvre", "Installation" },
            { "Le plan de travail est posé après la fixation des caissons hauts et bas. Les joints entre éléments sont réalisés au silicone transparent.", "The worktop is installed after the upper and lower carcasses are fixed. Joints between units are sealed with clear silicone." },
            { "Les plinthes sont clipsées sur des pieds réglables en hauteur de 100 à 150 mm.", "The plinths clip onto height-adjustable legs from 100 to 150 mm." },
            { "Vérifier les niveaux avant la pose.", "Check the levels before fitting." },
            { "Protéger les façades pendant les travaux.", "Protect the fronts during the works." },
            { "PLACAGE CHÊNE", "OAK VENEER" }, { "POIGNÉE INOX", "STAINLESS STEEL HANDLE" }, { "PLAN DE TRAVAIL QUARTZ", "QUARTZ WORKTOP" },
            { "Projet", "Project" }, { "Résidence Les Pins", "Les Pins Residence" }, { "Architecte", "Architect" },
            { "Dessiné par", "Drawn by" }, { "Échelle", "Scale" }, { "Élévation cuisine", "Kitchen elevation" },
        };

        private static void Draw(PdfPage page, IntPtr font, float size, string text, float x, float y, uint color = 0xFF000000)
        {
            page.AddText(font, size, text, x, y, color);
        }

        /// <summary>
        /// Writes a sample with the kinds of content the translator must keep (headings, paragraphs, a
        /// table, an image, columns, a drawing) and a scanned copy of its first page. Returns the scan's path.
        /// </summary>
        public static string MakeSample(string path)
        {
            var fonts = FontsDir;
            var regular = File.ReadAllBytes(Path.Combine(fonts, "arial.ttf"));
            var bold = File.ReadAllBytes(Path.Combine(fonts, "arialbd.ttf"));
            var serif = File.ReadAllBytes(Path.Combine(fonts, "times.ttf"));
            using (var doc = PdfFile.CreateNew())
            {
                var r = doc.LoadFont("r", regular);
                var b = doc.LoadFont("b", bold);
                var t = doc.LoadFont("t", serif);
                using (var p = doc.AddPage(595, 842))
                {
                    p.AddRectangle(0, 790, 595, 52, 0xFF132D52);
                    Draw(p, b, 22, "Cuisine – Lot 4", 56, 808, 0xFFFFFFFF);
                    Draw(p, b, 14, "Description des travaux", 56, 750, 0xFF132D52);
                    var para = new[] { "Les caissons sont fabriqués en panneaux de particules mélaminés de 19 mm. Les", "façades sont en placage de chêne avec une finition laquée mate, appliquée en", "atelier avant la livraison." };
                    for (int i = 0; i < para.Length; i++) Draw(p, t, 11, para[i], 56, 726 - i * 14);
                    Draw(p, t, 11, "Toutes les quincailleries doivent être approuvées par l'architecte avant la commande.", 56, 672);
                    // table with borders
                    float tx = 56, ty = 620, cw0 = 80, cw1 = 200, cw2 = 160, rh = 22;
                    p.AddRectangle(tx, ty - rh, cw0 + cw1 + cw2, rh, 0xFFE6ECF5);
                    for (int row = 0; row <= 3; row++) p.AddRectangle(tx, ty - row * rh, cw0 + cw1 + cw2, 0.8f, 0xFF7A8699);
                    foreach (var x in new[] { tx, tx + cw0, tx + cw0 + cw1, tx + cw0 + cw1 + cw2 }) p.AddRectangle(x, ty - 3 * rh, 0.8f, 3 * rh, 0xFF7A8699);
                    var cells = new[] { new[] { "Code", "Désignation", "Finition" }, new[] { "P-01", "Porte haute", "Chêne laqué mat" }, new[] { "T-02", "Tiroir", "Blanc RAL 9010" } };
                    for (int row = 0; row < 3; row++)
                        for (int col = 0; col < 3; col++)
                            Draw(p, row == 0 ? b : r, 9.5f, cells[row][col], tx + 6 + (col == 0 ? 0 : col == 1 ? cw0 : cw0 + cw1), ty - row * rh - 15);
                    // an image
                    var px = new byte[240 * 150 * 4];
                    for (int y = 0; y < 150; y++)
                        for (int x = 0; x < 240; x++)
                        {
                            int o = (y * 240 + x) * 4;
                            px[o] = (byte)(120 + x / 4); px[o + 1] = (byte)(90 + y / 3); px[o + 2] = (byte)(60 + (x + y) / 6); px[o + 3] = 255;
                        }
                    p.AddImage(px, 240, 150, 56, 330, 240, 150);
                    Draw(p, r, 8, "Page 1 sur 3", 270, 30, 0xFF666666);
                    p.CommitChanges();
                }
                using (var p = doc.AddPage(595, 842))
                {
                    Draw(p, b, 14, "Mise en œuvre", 56, 780, 0xFF132D52);
                    var left = new[] { "Le plan de travail est posé après la", "fixation des caissons hauts et bas. Les", "joints entre éléments sont réalisés au", "silicone transparent." };
                    var right = new[] { "Les plinthes sont clipsées sur des", "pieds réglables en hauteur de 100 à", "150 mm." };
                    for (int i = 0; i < left.Length; i++) Draw(p, t, 11, left[i], 56, 750 - i * 14);
                    for (int i = 0; i < right.Length; i++) Draw(p, t, 11, right[i], 315, 750 - i * 14);
                    Draw(p, r, 10.5f, "• Vérifier les niveaux avant la pose.", 56, 660);
                    Draw(p, r, 10.5f, "• Protéger les façades pendant les travaux.", 56, 644);
                    p.CommitChanges();
                }
                using (var p = doc.AddPage(1191, 842)) // A3 landscape shop drawing
                {
                    p.AddRectangle(40, 40, 1111, 1.2f, 0xFF000000); p.AddRectangle(40, 802, 1111, 1.2f, 0xFF000000);
                    p.AddRectangle(40, 40, 1.2f, 763, 0xFF000000); p.AddRectangle(1150, 40, 1.2f, 763, 0xFF000000);
                    // cabinet elevation
                    p.AddRectangle(200, 300, 600, 2, 0xFF333333); p.AddRectangle(200, 700, 600, 2, 0xFF333333);
                    p.AddRectangle(200, 300, 2, 400, 0xFF333333); p.AddRectangle(800, 300, 2, 400, 0xFF333333); p.AddRectangle(500, 300, 2, 400, 0xFF333333);
                    Draw(p, r, 9, "PLACAGE CHÊNE", 330, 520);
                    Draw(p, r, 9, "POIGNÉE INOX", 620, 520);
                    Draw(p, r, 9, "PLAN DE TRAVAIL QUARTZ", 400, 712);
                    Draw(p, b, 10, "P-01", 340, 480); Draw(p, b, 10, "T-02", 640, 480);
                    Draw(p, r, 9, "2400", 490, 280);
                    // title block
                    p.AddRectangle(850, 40, 300, 160, 0xFFF4F6FA);
                    Draw(p, b, 8, "Projet", 860, 182); Draw(p, r, 9, "Résidence Les Pins", 960, 182);
                    Draw(p, b, 8, "Architecte", 860, 162); Draw(p, r, 9, "Atelier Moreau", 960, 162);
                    Draw(p, b, 8, "Dessiné par", 860, 142); Draw(p, r, 9, "J. Smith", 960, 142);
                    Draw(p, b, 8, "Échelle", 860, 122); Draw(p, r, 9, "1:20", 960, 122);
                    Draw(p, b, 12, "Élévation cuisine", 860, 80);
                    p.CommitChanges();
                }
                doc.Save(path);
            }
            // a scanned copy of page 1 (image only), appended as page 4
            var scanned = path + ".scan.pdf";
            RenderedImage img;
            using (var src = PdfFile.Open(path))
            using (var p = src.LoadPage(0))
                img = p.Render(200f / 72f);
            using (var doc = PdfFile.CreateNew())
            using (var p = doc.AddPage(595, 842))
            {
                p.AddImage(img.Pixels, img.Width, img.Height, 0, 0, 595, 842);
                p.CommitChanges();
                doc.Save(scanned);
            }
            return scanned;
        }

        private static void SaveSideBySide(string name, RenderedImage a, RenderedImage b)
        {
            int gap = 12, w = a.Width + gap + b.Width, h = Math.Max(a.Height, b.Height);
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = 60; px[i + 1] = 50; px[i + 2] = 40; px[i + 3] = 255; }
            for (int y = 0; y < a.Height; y++) Buffer.BlockCopy(a.Pixels, y * a.Width * 4, px, y * w * 4, a.Width * 4);
            for (int y = 0; y < b.Height; y++) Buffer.BlockCopy(b.Pixels, y * b.Width * 4, px, (y * w + a.Width + gap) * 4, b.Width * 4);
            File.WriteAllBytes(Path.Combine(OutDir, name), PngWriter.FromBgra(px, w, h));
        }

        private static DocProject ReadAll(string pdf, string sourceLang)
        {
            var project = new DocProject { Name = Path.GetFileName(pdf), SourcePath = pdf, SourceLangSetting = sourceLang, TargetLang = "en" };
            using (var doc = PdfFile.Open(pdf))
            {
                project.PageCount = doc.PageCount;
                for (int i = 0; i < doc.PageCount; i++)
                    project.Pages.Add(PageReader.ReadAsync(doc, i, new ReadOptions { SourceLang = sourceLang }, System.Threading.CancellationToken.None).GetAwaiter().GetResult());
            }
            return project;
        }

        private static string Dump(DocProject p)
        {
            var sb = new StringBuilder();
            foreach (var page in p.Pages)
            {
                sb.AppendLine("== page " + (page.Index + 1) + (page.Scanned ? " (scanned, OCR " + page.OcrLanguages + ", confidence " + page.OcrConfidence.ToString("0") + ")" : ""));
                foreach (var b in page.InReadingOrder)
                    sb.AppendLine(string.Format("{0,-7} {1,-11} {2,-34} {3,5:0.0}pt {4}{5}| {6}{7}", b.Id, b.Kind + (b.Kind == BlockKind.Heading ? b.Level.ToString() : ""),
                        b.Box, b.FontSize, b.Bold ? "B " : "", b.Table >= 0 ? "t" + b.Table + " r" + b.Row + " c" + b.Col + " " : "", b.Source, b.Translation == null ? "" : "  =>  " + b.Translation));
            }
            return sb.ToString();
        }

        public static void Pipeline()
        {
            OcrEngine.DataDirectory = Environment.GetEnvironmentVariable("TEXTX_TESSDATA");
            var pdf = Path.Combine(OutDir, "sample-fr.pdf");
            var scan = MakeSample(pdf);
            var project = ReadAll(pdf, "fr");
            var scanProject = ReadAll(scan, "fr");
            File.WriteAllText(Path.Combine(OutDir, "sample-layout.txt"), Dump(project) + Dump(scanProject));

            var p1 = project.Pages[0];
            True(p1.Blocks.Any(b => b.Kind == BlockKind.Heading && b.Source == "Description des travaux"), "heading found");
            True(p1.Blocks.Any(b => b.Kind == BlockKind.Paragraph && b.Source.StartsWith("Les caissons") && b.Source.EndsWith("livraison.")), "paragraph joined across lines");
            Equal(9, p1.Blocks.Count(b => b.Kind == BlockKind.TableCell), "table cells on page 1");
            True(p1.Images.Count >= 1, "image found");
            True(project.Pages[1].InReadingOrder.First(b => b.Kind == BlockKind.Paragraph).Source.StartsWith("Le plan de travail"), "left column read first");
            True(project.Pages[2].Blocks.Any(b => b.Source == "PLACAGE CHÊNE"), "drawing label read");
            var sp = scanProject.Pages[0];
            True(sp.Scanned && sp.Blocks.Count >= 5, "scanned page read by OCR: " + sp.Blocks.Count + " blocks, confidence " + sp.OcrConfidence);
            True(sp.Blocks.Any(b => b.Source.Contains("caissons")), "OCR text: " + string.Join(" | ", sp.Blocks.Select(b => b.Source).Take(6)));

            // translate with a fixed French-English word list (no network)
            var fake = new DocFakeProvider();
            foreach (var kv in FrEn) fake.Words[kv.Key] = kv.Value;
            var engine = new TranslationEngine(new JoiningProvider(fake), null, new Glossary(), new TranslationCache());
            var translator = new DocumentTranslator(engine);
            translator.TranslateAsync(project, null, false, null, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            translator.TranslateAsync(scanProject, null, false, null, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(OutDir, "sample-layout.txt"), Dump(project) + Dump(scanProject));

            var fonts = FontCatalog.System();
            var options = new PdfWriteOptions { Fonts = fonts };
            var output = Path.Combine(OutDir, "sample-en.pdf");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var reports = TranslatedPdfWriter.Write(project, output, options, System.Threading.CancellationToken.None);
            True(reports.Sum(r => r.Written) > 20, "blocks written: " + reports.Sum(r => r.Written) + " in " + sw.ElapsedMilliseconds + " ms");
            var scanOut = Path.Combine(OutDir, "sample-scan-en.pdf");
            TranslatedPdfWriter.Write(scanProject, scanOut, options, System.Threading.CancellationToken.None);

            using (var doc = PdfFile.Open(output))
            {
                Equal(3, doc.PageCount, "page count kept");
                for (int i = 0; i < doc.PageCount; i++)
                {
                    using (var page = doc.LoadPage(i))
                    {
                        var text = Text(page.GetChars());
                        if (i == 0)
                        {
                            True(text.Contains("Description of works") && text.Contains("oak veneer with a matt lacquer") && !text.Contains("caissons"), "page 1 translated: " + text.Replace("\n", " | "));
                            True(page.GetObjects().Any(o => o.IsImage), "image kept");
                        }
                        if (i == 2) True(text.Contains("OAK VENEER") && text.Contains("P-01") && text.Contains("2400"), "drawing labels translated, codes kept: " + text.Replace("\n", " | "));
                        var orig = TranslatedPdfWriter.RenderOriginal(pdf, i, 1.4f);
                        var tr = page.Render(1.4f);
                        SaveSideBySide("compare-page" + (i + 1) + ".png", orig, tr);
                    }
                }
            }
            using (var doc = PdfFile.Open(scanOut))
            using (var page = doc.LoadPage(0))
            {
                var text = Text(page.GetChars());
                True(text.Contains("Description of works") || text.Contains("Kitchen"), "scanned page translated: " + text.Replace("\n", " | "));
                SaveSideBySide("compare-scan.png", TranslatedPdfWriter.RenderOriginal(scan, 0, 1.4f), page.Render(1.4f));
            }
            True(new FileInfo(output).Length < 8 * 1024 * 1024, "output size " + new FileInfo(output).Length / 1024 + " KB");

            // Hebrew output: right-to-left text in the PDF
            foreach (var blk in project.AllBlocks.Where(x => x.Translation != null)) blk.Translation = "תרגום: " + blk.Translation;
            project.TargetLang = "he";
            var heOut = Path.Combine(OutDir, "sample-he.pdf");
            TranslatedPdfWriter.Write(project, heOut, options, System.Threading.CancellationToken.None);
            using (var doc = PdfFile.Open(heOut))
            using (var page = doc.LoadPage(0))
                SaveSideBySide("compare-page1-he.png", TranslatedPdfWriter.RenderOriginal(pdf, 0, 1.4f), page.Render(1.4f));
            OcrEngine.Release();
        }
    }
}