using System;
using System.Runtime.InteropServices;

namespace TextX.Core.Documents.Native
{
    /// <summary>
    /// P/Invoke declarations for the PDFium C API (public/fpdfview.h, fpdf_text.h, fpdf_edit.h,
    /// fpdf_save.h). Windows x64: C <c>unsigned long</c> is 32 bits. PDFium is not thread-safe:
    /// call it only through <see cref="Pdfium"/>, which serialises every call.
    /// </summary>
    internal static class PdfiumNative
    {
        private const string Lib = "textx_pdfium";
        private const CallingConvention CC = CallingConvention.Cdecl;

        // ---- fpdfview.h
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDF_InitLibrary();
        [DllImport(Lib, CallingConvention = CC)] public static extern uint FPDF_GetLastError();
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDF_LoadCustomDocument(IntPtr access, [MarshalAs(UnmanagedType.LPStr)] string password);
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr size, [MarshalAs(UnmanagedType.LPStr)] string password);
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDF_CloseDocument(IntPtr document);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDF_GetPageCount(IntPtr document);
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Lib, CallingConvention = CC)] public static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Lib, CallingConvention = CC)] public static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFPage_GetRotation(IntPtr page);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFPage_GetCropBox(IntPtr page, out float left, out float bottom, out float right, out float top);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFPage_GetMediaBox(IntPtr page, out float left, out float bottom, out float right, out float top);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFTextObj_GetTextRenderMode(IntPtr textObject);
        [DllImport(Lib, CallingConvention = CC)] public static extern uint FPDF_GetMetaText(IntPtr document, [MarshalAs(UnmanagedType.LPStr)] string tag, IntPtr buffer, uint buflen);

        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFBitmap_GetStride(IntPtr bitmap);
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

        // ---- fpdf_text.h
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDFText_ClosePage(IntPtr textPage);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFText_CountChars(IntPtr textPage);
        [DllImport(Lib, CallingConvention = CC)] public static extern uint FPDFText_GetUnicode(IntPtr textPage, int index);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFText_IsGenerated(IntPtr textPage, int index);
        [DllImport(Lib, CallingConvention = CC)] public static extern double FPDFText_GetFontSize(IntPtr textPage, int index);
        [DllImport(Lib, CallingConvention = CC)] public static extern uint FPDFText_GetFontInfo(IntPtr textPage, int index, IntPtr buffer, uint buflen, out int flags);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFText_GetFontWeight(IntPtr textPage, int index);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFText_GetFillColor(IntPtr textPage, int index, out uint r, out uint g, out uint b, out uint a);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFText_GetLooseCharBox(IntPtr textPage, int index, out RectF rect);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFText_GetCharBox(IntPtr textPage, int index, out double left, out double right, out double bottom, out double top);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFText_GetCharOrigin(IntPtr textPage, int index, out double x, out double y);
        [DllImport(Lib, CallingConvention = CC)] public static extern float FPDFText_GetCharAngle(IntPtr textPage, int index);
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFText_GetTextObject(IntPtr textPage, int index);

        // ---- fpdf_edit.h
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDF_CreateNewDocument();
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFPage_New(IntPtr document, int pageIndex, double width, double height);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFPage_CountObjects(IntPtr page);
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDFPage_InsertObject(IntPtr page, IntPtr pageObject);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFPage_RemoveObject(IntPtr page, IntPtr pageObject);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFPage_GenerateContent(IntPtr page);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFPageObj_GetType(IntPtr pageObject);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFPageObj_GetBounds(IntPtr pageObject, out float left, out float bottom, out float right, out float top);
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDFPageObj_Destroy(IntPtr pageObject);
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDFPageObj_Transform(IntPtr pageObject, double a, double b, double c, double d, double e, double f);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFPageObj_SetFillColor(IntPtr pageObject, uint r, uint g, uint b, uint a);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFFormObj_CountObjects(IntPtr formObject);
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFFormObj_GetObject(IntPtr formObject, uint index);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFFormObj_RemoveObject(IntPtr formObject, IntPtr pageObject);

        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFText_LoadFont(IntPtr document, byte[] data, uint size, int fontType, int cid);
        [DllImport(Lib, CallingConvention = CC)] public static extern void FPDFFont_Close(IntPtr font);
        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFPageObj_CreateTextObj(IntPtr document, IntPtr font, float fontSize);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFText_SetText(IntPtr textObject, [MarshalAs(UnmanagedType.LPWStr)] string text);

        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFPageObj_CreateNewRect(float x, float y, float w, float h);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFPath_SetDrawMode(IntPtr path, int fillMode, int stroke);

        [DllImport(Lib, CallingConvention = CC)] public static extern IntPtr FPDFPageObj_NewImageObj(IntPtr document);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFImageObj_SetBitmap(IntPtr pages, int count, IntPtr imageObject, IntPtr bitmap);
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDFImageObj_SetMatrix(IntPtr imageObject, double a, double b, double c, double d, double e, double f);

        // ---- fpdf_save.h
        [DllImport(Lib, CallingConvention = CC)] public static extern int FPDF_SaveAsCopy(IntPtr document, ref FileWrite fileWrite, uint flags);

        // ---- constants
        public const int ObjText = 1, ObjPath = 2, ObjImage = 3, ObjShading = 4, ObjForm = 5;
        public const int RenderAnnotations = 0x01, RenderLcdText = 0x02, RenderGrayscale = 0x08, RenderPrinting = 0x800;
        public const int BitmapGray = 1, BitmapBgr = 2, BitmapBgrx = 3, BitmapBgra = 4;
        public const int FontTrueType = 2;
        public const int FillAlternate = 1, FillWinding = 2;
        public const uint SaveNoIncremental = 2;

        [UnmanagedFunctionPointer(CC)]
        public delegate int GetBlockDelegate(IntPtr param, uint position, IntPtr buffer, uint size);

        [UnmanagedFunctionPointer(CC)]
        public delegate int WriteBlockDelegate(IntPtr self, IntPtr data, uint size);

        [StructLayout(LayoutKind.Sequential)]
        public struct FileAccess
        {
            public uint FileLength;
            public IntPtr GetBlock;
            public IntPtr Param;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FileWrite
        {
            public int Version;
            public IntPtr WriteBlock;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RectF
        {
            public float Left, Top, Right, Bottom;
        }
    }
}
