using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TextX.Core.Documents.Native
{
    /// <summary>
    /// Loads the native libraries shipped next to TextX.rhp (Windows x64):
    /// <c>textx_pdfium.dll</c> and <c>x64\tesseract50.dll</c> (+ Leptonica).
    /// They are loaded by full path once, so the P/Invoke declarations bind to exactly these files
    /// whatever else Rhino has loaded.
    /// </summary>
    public static class NativeLibraries
    {
        public const string PdfiumFile = "textx_pdfium.dll";
        public const string TesseractFile = "tesseract50.dll";

        private static readonly object Lock = new object();
        private static string _directory;
        private static bool _pdfiumLoaded, _tesseractLoaded;

        /// <summary>Folder holding the native libraries; defaults to the folder of TextX.rhp.</summary>
        public static string Directory
        {
            get
            {
                if (!string.IsNullOrEmpty(_directory)) return _directory;
                try { return Path.GetDirectoryName(typeof(NativeLibraries).Assembly.Location) ?? string.Empty; }
                catch { return string.Empty; }
            }
            set { _directory = value; }
        }

        public static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        public static string PdfiumPath { get { return Path.Combine(Directory, PdfiumFile); } }
        public static string TesseractPath { get { return Path.Combine(Directory, "x64", TesseractFile); } }

        public static bool PdfiumAvailable { get { return File.Exists(PdfiumPath); } }
        public static bool TesseractAvailable { get { return File.Exists(TesseractPath); } }

        public static void EnsurePdfium()
        {
            lock (Lock)
            {
                if (_pdfiumLoaded) return;
                if (!File.Exists(PdfiumPath))
                    throw new DllNotFoundException("The PDF engine (" + PdfiumFile + ") is missing from " + Directory + ". Reinstall Text X.");
                Load(PdfiumPath);
                _pdfiumLoaded = true;
            }
        }

        public static void EnsureTesseract()
        {
            lock (Lock)
            {
                if (_tesseractLoaded) return;
                if (!File.Exists(TesseractPath))
                    throw new DllNotFoundException("The OCR engine (" + TesseractFile + ") is missing from " + Path.GetDirectoryName(TesseractPath) + ". Reinstall Text X.");
                var dir = Path.GetDirectoryName(TesseractPath);
                foreach (var lept in System.IO.Directory.GetFiles(dir, "leptonica*.dll")) Load(lept);
                Load(TesseractPath);
                _tesseractLoaded = true;
            }
        }

        private static void Load(string path)
        {
            if (!IsWindows) return; // elsewhere the runtime's own probing is used (development only)
            var handle = LoadLibraryExW(path, IntPtr.Zero, LoadWithAlteredSearchPath);
            if (handle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                var hint = err == 126 ? " A Microsoft Visual C++ runtime may be missing." : err == 193 ? " The file is not a 64-bit Windows library." : string.Empty;
                throw new DllNotFoundException("Could not load " + Path.GetFileName(path) + " (Windows error " + err + ")." + hint);
            }
        }

        private const uint LoadWithAlteredSearchPath = 0x00000008;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);
    }
}
