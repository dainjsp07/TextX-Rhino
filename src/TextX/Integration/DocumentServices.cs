using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TextX.Core.Documents;
using TextX.Core.Documents.Export;
using TextX.Core.Documents.Fonts;
using TextX.Core.Documents.Imaging;
using TextX.Core.Documents.Native;
using TextX.Core.Documents.Ocr;
using TextX.Core.Tools;

namespace TextX.Integration
{
    /// <summary>Document translation, OCR and history for the Studio window, set up for this computer.</summary>
    public static class DocumentServices
    {
        private static DocumentLibrary _library;
        private static FontCatalog _fonts;
        private static readonly object Lock = new object();

        private static TextXSettings Settings { get { return TextXPlugIn.Instance.Options; } }

        public static DocumentLibrary Library
        {
            get
            {
                lock (Lock)
                {
                    if (_library == null) _library = new DocumentLibrary(Settings.DocumentsDirectory);
                    return _library;
                }
            }
        }

        /// <summary>The fonts installed on this computer (indexed once).</summary>
        public static FontCatalog Fonts
        {
            get
            {
                lock (Lock)
                {
                    if (_fonts == null) _fonts = FontCatalog.System();
                    return _fonts;
                }
            }
        }

        /// <summary>Points the native engines at the plug-in folder and the OCR data at the settings folder.</summary>
        public static void Configure()
        {
            try { Directory.CreateDirectory(Settings.TessDataDirectory); } catch (Exception) { /* read-only profile */ }
            OcrEngine.DataDirectory = Settings.TessDataDirectory;
        }

        /// <summary>Null when the PDF engine is ready, else a message for the user.</summary>
        public static string CheckPdfEngine()
        {
            try
            {
                NativeLibraries.EnsurePdfium();
                return null;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("PDF engine: " + ex);
                return ex.Message;
            }
        }

        /// <summary>Reads every page of a project that has not been read yet (text, layout, OCR for scans).</summary>
        public static async Task ReadPagesAsync(DocProject project, IProgress<string> status, IProgress<int> pagesDone, CancellationToken ct)
        {
            Configure();
            using (var doc = PdfFile.Open(project.PdfPath))
            {
                if (project.PageCount == 0) project.PageCount = doc.PageCount;
                while (project.Pages.Count < doc.PageCount) project.Pages.Add(new DocPage { Index = project.Pages.Count });
                for (int i = 0; i < doc.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (project.Pages[i].Extracted) continue;
                    if (status != null) status.Report("Reading page " + (i + 1) + " of " + doc.PageCount + "...");
                    var options = new ReadOptions { SourceLang = project.SourceLangSetting, AllowOcr = true, Status = status };
                    project.Pages[i] = await PageReader.ReadAsync(doc, i, options, ct).ConfigureAwait(false);
                    if (pagesDone != null) pagesDone.Report(i + 1);
                }
            }
        }

        /// <summary>A small picture of the first page for the history list.</summary>
        public static void SaveThumbnail(DocProject project)
        {
            try
            {
                using (var doc = PdfFile.Open(project.PdfPath))
                using (var page = doc.LoadPage(0))
                {
                    float scale = 160f / Math.Max(page.Width, page.Height);
                    var img = page.Render(scale);
                    Library.SaveThumbnail(project.Id, PngWriter.FromBgra(img.Pixels, img.Width, img.Height));
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log("thumbnail: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ pictures

        /// <summary>Reads an image file (PNG, JPEG, BMP, GIF, TIFF) as grey pixels.</summary>
        public static GrayImage LoadImage(string path)
        {
            using (var bmp = new System.Drawing.Bitmap(path)) return FromBitmap(bmp);
        }

        /// <summary>Reads image bytes (for example a picture from the clipboard saved as PNG).</summary>
        public static GrayImage LoadImage(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var bmp = new System.Drawing.Bitmap(ms)) return FromBitmap(bmp);
        }

        public static GrayImage FromBitmap(System.Drawing.Bitmap source)
        {
            using (var bmp = new System.Drawing.Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp)) g.DrawImage(source, 0, 0, source.Width, source.Height);
                var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
                var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    var bytes = new byte[stride * bmp.Height];
                    Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                    return GrayImage.FromBgra(bytes, bmp.Width, bmp.Height, stride);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
            }
        }

        // ------------------------------------------------------------------ Windows tools

        /// <summary>Opens the Windows snipping tool (the same as Win + Shift + S).</summary>
        public static bool StartScreenClip()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("screen clip: " + ex.Message);
                return SendKeys(VK_LWIN, VK_SHIFT, 'S');
            }
        }

        /// <summary>
        /// Starts Windows voice typing (Win + H): speech is typed into the focused text box.
        /// Needs a microphone; Windows may ask to turn on online speech recognition the first time.
        /// </summary>
        public static bool StartVoiceTyping()
        {
            return SendKeys(VK_LWIN, 'H');
        }

        public static void OpenFile(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Diagnostics.Log("open file: " + ex.Message); }
        }

        public static void ShowInFolder(string path)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { Diagnostics.Log("show in folder: " + ex.Message); }
        }

        private const byte VK_LWIN = 0x5B, VK_SHIFT = 0x10;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private static bool SendKeys(params int[] keys)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
            try
            {
                foreach (var k in keys) keybd_event((byte)k, 0, 0, UIntPtr.Zero);
                for (int i = keys.Length - 1; i >= 0; i--) keybd_event((byte)keys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("keys: " + ex.Message);
                return false;
            }
        }
    }
}
