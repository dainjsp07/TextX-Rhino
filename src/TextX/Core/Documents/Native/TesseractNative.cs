using System;
using System.Runtime.InteropServices;

namespace TextX.Core.Documents.Native
{
    /// <summary>P/Invoke declarations for the Tesseract 5 C API (tesseract/capi.h).</summary>
    internal static class TesseractNative
    {
        private const string Lib = "tesseract50";
        private const CallingConvention CC = CallingConvention.Cdecl;

        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr TessVersion();
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr TessBaseAPICreate();
        [DllImport(Lib, CallingConvention = CC)] public static extern void TessBaseAPIDelete(IntPtr handle);
        [DllImport(Lib, CallingConvention = CC)] public static extern int TessBaseAPIInit2(IntPtr handle, byte[] datapath, byte[] language, int oem);
        [DllImport(Lib, CallingConvention = CC)] public static extern int TessBaseAPISetVariable(IntPtr handle, byte[] name, byte[] value);
        [DllImport(Lib, CallingConvention = CC)] public static extern void TessBaseAPISetPageSegMode(IntPtr handle, int mode);
        [DllImport(Lib, CallingConvention = CC)] public static extern void TessBaseAPISetImage(IntPtr handle, byte[] imagedata, int width, int height, int bytesPerPixel, int bytesPerLine);
        [DllImport(Lib, CallingConvention = CC)] public static extern void TessBaseAPISetSourceResolution(IntPtr handle, int ppi);
        [DllImport(Lib, CallingConvention = CC)] public static extern int TessBaseAPIRecognize(IntPtr handle, IntPtr monitor);
        [DllImport(Lib, CallingConvention = CC)] public static extern int TessBaseAPIDetectOrientationScript(IntPtr handle, out int orientDeg, out float orientConf, out IntPtr scriptName, out float scriptConf);
        [DllImport(Lib, CallingConvention = CC)] public static extern void TessBaseAPIClear(IntPtr handle);
        [DllImport(Lib, CallingConvention = CC)] public static extern void TessBaseAPIEnd(IntPtr handle);

        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr TessBaseAPIGetIterator(IntPtr handle);
        [DllImport(Lib, CallingConvention = CC)] public static extern void TessResultIteratorDelete(IntPtr iterator);
        [DllImport(Lib, CallingConvention = CC)] public static extern int TessResultIteratorNext(IntPtr iterator, int level);
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr TessResultIteratorGetUTF8Text(IntPtr iterator, int level);
        [DllImport(Lib, CallingConvention = CC)] public static extern float TessResultIteratorConfidence(IntPtr iterator, int level);
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr TessResultIteratorGetPageIterator(IntPtr iterator);
        [DllImport(Lib, CallingConvention = CC)] public static extern int TessPageIteratorIsAtBeginningOf(IntPtr pageIterator, int level);
        [DllImport(Lib, CallingConvention = CC)] public static extern int TessPageIteratorBoundingBox(IntPtr pageIterator, int level, out int left, out int top, out int right, out int bottom);
        [DllImport(Lib, CallingConvention = CC)] public static extern void TessDeleteText(IntPtr text);

        public const int OemTesseractOnly = 0, OemLstmOnly = 1, OemDefault = 3;
        public const int PsmOsdOnly = 0, PsmAutoOsd = 1, PsmAuto = 3, PsmSingleBlock = 6, PsmSparseText = 11;
        public const int LevelBlock = 0, LevelPara = 1, LevelLine = 2, LevelWord = 3;

        public static byte[] Utf8Z(string s)
        {
            var b = System.Text.Encoding.UTF8.GetBytes(s ?? string.Empty);
            Array.Resize(ref b, b.Length + 1);
            return b;
        }

        public static string FromUtf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return string.Empty;
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            var bytes = new byte[len];
            Marshal.Copy(p, bytes, 0, len);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
