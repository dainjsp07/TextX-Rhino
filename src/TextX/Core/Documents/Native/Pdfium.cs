using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using N = TextX.Core.Documents.Native.PdfiumNative;

namespace TextX.Core.Documents.Native
{
    public sealed class PdfException : Exception
    {
        public PdfException(string message) : base(message) { }
    }

    /// <summary>One character of a page's text layer, in PDF points (origin bottom-left).</summary>
    public struct PdfChar
    {
        public int Index;
        public int Code;
        public bool Generated;
        public float Left, Bottom, Right, Top;
        public float OriginX, OriginY;
        public float FontSize;
        public string FontName;
        public bool Bold, Italic;
        public uint Color;   // 0xAARRGGBB
        public float Angle;  // radians, counter-clockwise (PDF convention), 0 to 2 pi
        public IntPtr TextObject;

        public float CenterX { get { return (Left + Right) / 2f; } }
        public float CenterY { get { return (Bottom + Top) / 2f; } }
        public override string ToString() { return char.ConvertFromUtf32(Math.Max(32, Math.Min(Code, 0x10FFFF))); }
    }

    /// <summary>A page object (text run, path, image, form) with its bounds in points.</summary>
    public struct PdfObjectInfo
    {
        public IntPtr Handle;
        public IntPtr Parent; // the form object holding it, or zero for page level
        public int Type;
        public float Left, Bottom, Right, Top;
        /// <summary>Number of segments of a path (a drawn rectangle has 4 or 5); -1 when not known.</summary>
        public int Segments;
        public bool IsText { get { return Type == N.ObjText; } }
        public bool IsImage { get { return Type == N.ObjImage; } }
        public bool IsPath { get { return Type == N.ObjPath; } }
        public float Width { get { return Right - Left; } }
        public float Height { get { return Top - Bottom; } }
    }

    /// <summary>
    /// Entry point to PDFium. Every call is serialised on <see cref="Sync"/> because PDFium is not
    /// thread-safe; keep work done inside a lock short so the window stays responsive.
    /// </summary>
    public static class Pdfium
    {
        /// <summary>An angle in radians brought into [0, 2 pi).</summary>
        public static float NormalizeAngle(double a)
        {
            const double TwoPi = Math.PI * 2;
            a %= TwoPi;
            if (a < 0) a += TwoPi;
            if (a > TwoPi - 1e-4) a = 0;
            return (float)a;
        }

        public static readonly object Sync = new object();
        private static bool _initialised;

        public static void EnsureInitialised()
        {
            lock (Sync)
            {
                if (_initialised) return;
                NativeLibraries.EnsurePdfium();
                N.FPDF_InitLibrary();
                _initialised = true;
            }
        }

        internal static string ErrorText(uint code)
        {
            switch (code)
            {
                case 2: return "the file could not be opened";
                case 3: return "the file is not a PDF or is damaged";
                case 4: return "the PDF is protected by a password";
                case 5: return "the PDF's security settings are not supported";
                case 6: return "the page was not found";
                default: return "unknown PDF error " + code;
            }
        }
    }

    /// <summary>An open PDF document. Large files are read from disk on demand, not loaded into memory.</summary>
    public sealed class PdfFile : IDisposable
    {
        private IntPtr _doc;
        private FileStream _stream;
        private N.GetBlockDelegate _getBlock; // kept alive while the document is open
        private readonly Dictionary<string, IntPtr> _fonts = new Dictionary<string, IntPtr>();

        private PdfFile() { }

        public IntPtr Handle { get { return _doc; } }
        public string Path { get; private set; }

        public static PdfFile Open(string path, string password = null)
        {
            Pdfium.EnsureInitialised();
            var f = new PdfFile { Path = path };
            var info = new FileInfo(path);
            if (!info.Exists) throw new PdfException("The file " + path + " was not found.");
            if (info.Length > uint.MaxValue) throw new PdfException("PDF files larger than 4 GB are not supported.");
            lock (Pdfium.Sync)
            {
                f._stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
                var buffer = new byte[1 << 16];
                f._getBlock = (param, position, dest, size) =>
                {
                    try
                    {
                        f._stream.Position = position;
                        int remaining = (int)size, offset = 0;
                        while (remaining > 0)
                        {
                            int n = f._stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                            if (n <= 0) return 0;
                            Marshal.Copy(buffer, 0, dest + offset, n);
                            offset += n;
                            remaining -= n;
                        }
                        return 1;
                    }
                    catch
                    {
                        return 0;
                    }
                };
                var access = new N.FileAccess
                {
                    FileLength = (uint)info.Length,
                    GetBlock = Marshal.GetFunctionPointerForDelegate(f._getBlock),
                    Param = IntPtr.Zero,
                };
                f._accessMemory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(N.FileAccess)));
                Marshal.StructureToPtr(access, f._accessMemory, false);
                f._doc = N.FPDF_LoadCustomDocument(f._accessMemory, password);
                if (f._doc == IntPtr.Zero)
                {
                    var code = N.FPDF_GetLastError();
                    f.Dispose();
                    if (code == 4) throw new PdfPasswordException(password != null);
                    throw new PdfException("Could not open " + System.IO.Path.GetFileName(path) + ": " + Pdfium.ErrorText(code) + ".");
                }
            }
            return f;
        }

        private IntPtr _accessMemory;

        /// <summary>Creates a new, empty document (used for tests and for scanned-page output).</summary>
        public static PdfFile CreateNew()
        {
            Pdfium.EnsureInitialised();
            lock (Pdfium.Sync)
            {
                var doc = N.FPDF_CreateNewDocument();
                if (doc == IntPtr.Zero) throw new PdfException("Could not create a PDF document.");
                return new PdfFile { _doc = doc };
            }
        }

        public int PageCount
        {
            get { lock (Pdfium.Sync) return N.FPDF_GetPageCount(_doc); }
        }

        public string Title
        {
            get { return MetaText("Title"); }
        }

        public string MetaText(string tag)
        {
            lock (Pdfium.Sync)
            {
                uint len = N.FPDF_GetMetaText(_doc, tag, IntPtr.Zero, 0);
                if (len <= 2) return string.Empty;
                var buf = Marshal.AllocHGlobal((int)len);
                try
                {
                    N.FPDF_GetMetaText(_doc, tag, buf, len);
                    var bytes = new byte[len];
                    Marshal.Copy(buf, bytes, 0, (int)len);
                    return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        public PdfPage LoadPage(int index)
        {
            lock (Pdfium.Sync)
            {
                var page = N.FPDF_LoadPage(_doc, index);
                if (page == IntPtr.Zero) throw new PdfException("Page " + (index + 1) + " could not be read.");
                return new PdfPage(this, page, index);
            }
        }

        public PdfPage AddPage(double width, double height)
        {
            lock (Pdfium.Sync)
            {
                int index = N.FPDF_GetPageCount(_doc);
                var page = N.FPDFPage_New(_doc, index, width, height);
                if (page == IntPtr.Zero) throw new PdfException("Could not add a page.");
                return new PdfPage(this, page, index);
            }
        }

        /// <summary>Embeds a TrueType font (cached per key). Text written with it stays searchable.</summary>
        public IntPtr LoadFont(string key, byte[] trueTypeData)
        {
            lock (Pdfium.Sync)
            {
                IntPtr font;
                if (_fonts.TryGetValue(key, out font)) return font;
                font = N.FPDFText_LoadFont(_doc, trueTypeData, (uint)trueTypeData.Length, N.FontTrueType, 1);
                if (font == IntPtr.Zero) throw new PdfException("The font " + key + " could not be embedded.");
                _fonts[key] = font;
                return font;
            }
        }

        /// <summary>Writes the document (with all page changes) to a new file.</summary>
        public void Save(string path)
        {
            lock (Pdfium.Sync)
            {
                using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    Exception error = null;
                    N.WriteBlockDelegate write = (self, data, size) =>
                    {
                        try
                        {
                            var bytes = new byte[size];
                            Marshal.Copy(data, bytes, 0, (int)size);
                            output.Write(bytes, 0, bytes.Length);
                            return 1;
                        }
                        catch (Exception ex)
                        {
                            error = ex;
                            return 0;
                        }
                    };
                    var fw = new N.FileWrite { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(write) };
                    int ok = N.FPDF_SaveAsCopy(_doc, ref fw, N.SaveNoIncremental);
                    GC.KeepAlive(write);
                    if (error != null) throw new PdfException("Could not write " + path + ": " + error.Message);
                    if (ok == 0) throw new PdfException("The PDF could not be saved.");
                }
            }
        }

        public void Dispose()
        {
            lock (Pdfium.Sync)
            {
                foreach (var font in _fonts.Values) N.FPDFFont_Close(font);
                _fonts.Clear();
                if (_doc != IntPtr.Zero) N.FPDF_CloseDocument(_doc);
                _doc = IntPtr.Zero;
                if (_accessMemory != IntPtr.Zero) Marshal.FreeHGlobal(_accessMemory);
                _accessMemory = IntPtr.Zero;
                if (_stream != null) _stream.Dispose();
                _stream = null;
                GC.KeepAlive(_getBlock);
                _getBlock = null;
            }
        }
    }

    public sealed class PdfPasswordException : Exception
    {
        public PdfPasswordException(bool wrongPassword)
            : base(wrongPassword ? "The password is not correct." : "This PDF is protected by a password.")
        {
            WrongPassword = wrongPassword;
        }

        public bool WrongPassword { get; private set; }
    }

    /// <summary>A loaded page. Dispose it when done (PDFium keeps pages in memory until closed).</summary>
    public sealed class PdfPage : IDisposable
    {
        private readonly PdfFile _file;
        private IntPtr _page;
        private IntPtr _text;
        private static bool _textObjectApiMissing;
        private static bool _formRemoveApiMissing;
        private static bool _segmentApiMissing;

        internal PdfPage(PdfFile file, IntPtr page, int index)
        {
            _file = file;
            _page = page;
            Index = index;
            Width = N.FPDF_GetPageWidthF(page);
            Height = N.FPDF_GetPageHeightF(page);
            Rotation = N.FPDFPage_GetRotation(page) & 3;
            float l, b, r, t;
            if (N.FPDFPage_GetCropBox(page, out l, out b, out r, out t) != 0 || N.FPDFPage_GetMediaBox(page, out l, out b, out r, out t) != 0)
            {
                OriginX = Math.Min(l, r);
                OriginY = Math.Min(b, t);
            }
        }

        /// <summary>Lower-left corner of the visible page area in PDF user space (usually 0, 0).</summary>
        public float OriginX { get; private set; }
        public float OriginY { get; private set; }

        /// <summary>True when the text object is drawn invisibly (render mode 3: an OCR layer over a scan).</summary>
        public static bool IsInvisibleText(IntPtr textObject)
        {
            if (textObject == IntPtr.Zero) return false;
            lock (Pdfium.Sync)
            {
                try { return N.FPDFTextObj_GetTextRenderMode(textObject) == 3; }
                catch (EntryPointNotFoundException) { return false; }
            }
        }

        public int Index { get; private set; }
        public float Width { get; private set; }
        public float Height { get; private set; }

        /// <summary>Page rotation in quarter turns clockwise (0-3). Width and Height are the displayed size.</summary>
        public int Rotation { get; private set; }

        public IntPtr Handle { get { return _page; } }

        private IntPtr TextPage
        {
            get
            {
                if (_text == IntPtr.Zero) _text = N.FPDFText_LoadPage(_page);
                return _text;
            }
        }

        /// <summary>Reads every character of the text layer with its position, font and colour.</summary>
        public List<PdfChar> GetChars()
        {
            lock (Pdfium.Sync)
            {
                var tp = TextPage;
                var list = new List<PdfChar>();
                if (tp == IntPtr.Zero) return list;
                int count = N.FPDFText_CountChars(tp);
                IntPtr lastObject = IntPtr.Zero;
                string lastFont = string.Empty;
                int lastFlags = 0;
                var nameBuffer = Marshal.AllocHGlobal(512);
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        var c = new PdfChar { Index = i, Code = (int)N.FPDFText_GetUnicode(tp, i) };
                        c.Generated = N.FPDFText_IsGenerated(tp, i) == 1;
                        N.RectF r;
                        if (N.FPDFText_GetLooseCharBox(tp, i, out r) != 0)
                        {
                            c.Left = Math.Min(r.Left, r.Right); c.Right = Math.Max(r.Left, r.Right);
                            c.Bottom = Math.Min(r.Bottom, r.Top); c.Top = Math.Max(r.Bottom, r.Top);
                        }
                        double ox, oy;
                        if (N.FPDFText_GetCharOrigin(tp, i, out ox, out oy) != 0) { c.OriginX = (float)ox; c.OriginY = (float)oy; }
                        c.FontSize = (float)N.FPDFText_GetFontSize(tp, i);
                        // PDFium measures the angle clockwise; Text X uses the PDF convention (counter-clockwise)
                        c.Angle = Pdfium.NormalizeAngle(-N.FPDFText_GetCharAngle(tp, i));
                        // text set at size 1 and scaled by its matrix reports 1 pt, and text squeezed by its matrix
                        // (CAD exports) reports far more than it shows: take the size from the glyph box then
                        double across = Math.Abs(c.Right - c.Left) * Math.Abs(Math.Sin(c.Angle)) + Math.Abs(c.Top - c.Bottom) * Math.Abs(Math.Cos(c.Angle));
                        if (!c.Generated && across > 0 && (c.FontSize < across * 0.5 || c.FontSize > across * 2.5)) c.FontSize = (float)(across * 0.88);
                        uint cr, cg, cb, ca;
                        if (N.FPDFText_GetFillColor(tp, i, out cr, out cg, out cb, out ca) != 0)
                            c.Color = (ca << 24) | (cr << 16) | (cg << 8) | cb;
                        else
                            c.Color = 0xFF000000;
                        c.TextObject = GetTextObject(tp, i);
                        if (!c.Generated)
                        {
                            if (c.TextObject == IntPtr.Zero || c.TextObject != lastObject)
                            {
                                int flags;
                                uint len = N.FPDFText_GetFontInfo(tp, i, nameBuffer, 512, out flags);
                                lastFont = len > 1 ? Marshal.PtrToStringAnsi(nameBuffer, (int)Math.Min(len - 1, 511)) : string.Empty;
                                lastFlags = flags;
                                lastObject = c.TextObject;
                            }
                            c.FontName = lastFont;
                            int weight = N.FPDFText_GetFontWeight(tp, i);
                            c.Bold = weight >= 600 || lastFont.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     lastFont.IndexOf("Black", StringComparison.OrdinalIgnoreCase) >= 0 || lastFont.IndexOf("Heavy", StringComparison.OrdinalIgnoreCase) >= 0;
                            c.Italic = (lastFlags & 0x40) != 0 || lastFont.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       lastFont.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        list.Add(c);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(nameBuffer);
                }
                return list;
            }
        }

        private static IntPtr GetTextObject(IntPtr tp, int index)
        {
            if (_textObjectApiMissing) return IntPtr.Zero;
            try
            {
                return N.FPDFText_GetTextObject(tp, index);
            }
            catch (EntryPointNotFoundException)
            {
                _textObjectApiMissing = true;
                return IntPtr.Zero;
            }
        }

        /// <summary>All page objects, including those nested in form objects.</summary>
        public List<PdfObjectInfo> GetObjects()
        {
            lock (Pdfium.Sync)
            {
                var list = new List<PdfObjectInfo>();
                int n = N.FPDFPage_CountObjects(_page);
                for (int i = 0; i < n; i++) AddObject(list, N.FPDFPage_GetObject(_page, i), IntPtr.Zero, 0);
                return list;
            }
        }

        private static void AddObject(List<PdfObjectInfo> list, IntPtr obj, IntPtr parent, int depth)
        {
            if (obj == IntPtr.Zero) return;
            var info = new PdfObjectInfo { Handle = obj, Parent = parent, Type = N.FPDFPageObj_GetType(obj), Segments = -1 };
            float l, b, r, t;
            if (N.FPDFPageObj_GetBounds(obj, out l, out b, out r, out t) != 0)
            {
                info.Left = l; info.Bottom = b; info.Right = r; info.Top = t;
            }
            if (info.Type == N.ObjPath && !_segmentApiMissing)
            {
                try { info.Segments = N.FPDFPath_CountSegments(obj); }
                catch (EntryPointNotFoundException) { _segmentApiMissing = true; }
            }
            list.Add(info);
            if (info.Type == N.ObjForm && depth < 8)
            {
                int n = N.FPDFFormObj_CountObjects(obj);
                for (int i = 0; i < n; i++) AddObject(list, N.FPDFFormObj_GetObject(obj, (uint)i), obj, depth + 1);
            }
        }

        /// <summary>Removes an object from the page (or from the form holding it). False if it could not be removed.</summary>
        public bool RemoveObject(PdfObjectInfo obj)
        {
            lock (Pdfium.Sync)
            {
                bool removed;
                if (obj.Parent == IntPtr.Zero)
                {
                    removed = N.FPDFPage_RemoveObject(_page, obj.Handle) != 0;
                }
                else
                {
                    if (_formRemoveApiMissing) return false;
                    try { removed = N.FPDFFormObj_RemoveObject(obj.Parent, obj.Handle) != 0; }
                    catch (EntryPointNotFoundException) { _formRemoveApiMissing = true; return false; }
                }
                if (removed) N.FPDFPageObj_Destroy(obj.Handle);
                return removed;
            }
        }

        /// <summary>Adds one line of text with its baseline origin at (x, y) points, rotated by <paramref name="angle"/> radians.</summary>
        public void AddText(IntPtr font, float size, string text, double x, double y, uint argb, double angle = 0)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (Pdfium.Sync)
            {
                var obj = N.FPDFPageObj_CreateTextObj(_file.Handle, font, size);
                if (obj == IntPtr.Zero) throw new PdfException("Could not create text.");
                N.FPDFText_SetText(obj, text);
                N.FPDFPageObj_SetFillColor(obj, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF, (argb >> 24) & 0xFF);
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                if (Math.Abs(sin) < 1e-6) { cos = Math.Sign(cos); sin = 0; }
                N.FPDFPageObj_Transform(obj, cos, sin, -sin, cos, x, y);
                N.FPDFPage_InsertObject(_page, obj);
            }
        }

        /// <summary>Adds a filled rectangle (used to cover scanned text).</summary>
        public void AddRectangle(float x, float y, float w, float h, uint argb)
        {
            lock (Pdfium.Sync)
            {
                var rect = N.FPDFPageObj_CreateNewRect(x, y, w, h);
                N.FPDFPageObj_SetFillColor(rect, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF, (argb >> 24) & 0xFF);
                N.FPDFPath_SetDrawMode(rect, N.FillWinding, 0);
                N.FPDFPage_InsertObject(_page, rect);
            }
        }

        /// <summary>Adds an image (BGRA pixels) covering the rectangle (x, y, w, h) in points.</summary>
        public void AddImage(byte[] bgra, int pixelWidth, int pixelHeight, double x, double y, double w, double h)
        {
            lock (Pdfium.Sync)
            {
                var bmp = N.FPDFBitmap_Create(pixelWidth, pixelHeight, 1);
                try
                {
                    var buf = N.FPDFBitmap_GetBuffer(bmp);
                    int stride = N.FPDFBitmap_GetStride(bmp);
                    for (int row = 0; row < pixelHeight; row++) Marshal.Copy(bgra, row * pixelWidth * 4, buf + row * stride, pixelWidth * 4);
                    var img = N.FPDFPageObj_NewImageObj(_file.Handle);
                    N.FPDFImageObj_SetBitmap(IntPtr.Zero, 0, img, bmp);
                    N.FPDFImageObj_SetMatrix(img, w, 0, 0, h, x, y);
                    N.FPDFPage_InsertObject(_page, img);
                }
                finally
                {
                    N.FPDFBitmap_Destroy(bmp);
                }
            }
        }

        /// <summary>Writes the page's changes into its content stream (call before saving or rendering).</summary>
        public void CommitChanges()
        {
            lock (Pdfium.Sync)
            {
                if (_text != IntPtr.Zero) { N.FPDFText_ClosePage(_text); _text = IntPtr.Zero; }
                if (N.FPDFPage_GenerateContent(_page) == 0) throw new PdfException("Page " + (Index + 1) + " could not be updated.");
            }
        }

        /// <summary>
        /// Renders the page as displayed (its rotation applied) at <paramref name="scale"/> pixels per point.
        /// Returns BGRA pixels. <see cref="Width"/> and <see cref="Height"/> are already the displayed size.
        /// </summary>
        public RenderedImage Render(float scale, bool grayscale = false)
        {
            lock (Pdfium.Sync)
            {
                int w = Math.Max(1, (int)Math.Round(Width * scale));
                int h = Math.Max(1, (int)Math.Round(Height * scale));
                return RenderCore(w, h, (bmp) => N.FPDF_RenderPageBitmap(bmp, _page, 0, 0, w, h, 0, N.RenderAnnotations | (grayscale ? N.RenderGrayscale : 0)));
            }
        }

        private static RenderedImage RenderCore(int w, int h, Action<IntPtr> draw)
        {
            if ((long)w * h > 200L * 1000 * 1000) throw new PdfException("The page is too large to render at this zoom.");
            var bmp = N.FPDFBitmap_Create(w, h, 0);
            if (bmp == IntPtr.Zero) throw new PdfException("Not enough memory to render the page.");
            try
            {
                N.FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF);
                draw(bmp);
                var buf = N.FPDFBitmap_GetBuffer(bmp);
                int stride = N.FPDFBitmap_GetStride(bmp);
                var pixels = new byte[w * h * 4];
                for (int row = 0; row < h; row++) Marshal.Copy(buf + row * stride, pixels, row * w * 4, w * 4);
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                return new RenderedImage(w, h, pixels);
            }
            finally
            {
                N.FPDFBitmap_Destroy(bmp);
            }
        }

        public void Dispose()
        {
            lock (Pdfium.Sync)
            {
                if (_text != IntPtr.Zero) N.FPDFText_ClosePage(_text);
                _text = IntPtr.Zero;
                if (_page != IntPtr.Zero) N.FPDF_ClosePage(_page);
                _page = IntPtr.Zero;
            }
        }
    }

    /// <summary>Pixels in BGRA order, top row first.</summary>
    public sealed class RenderedImage
    {
        public RenderedImage(int width, int height, byte[] bgra)
        {
            Width = width;
            Height = height;
            Pixels = bgra;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public byte[] Pixels { get; private set; }

        /// <summary>Luminance (0-255) per pixel, for OCR.</summary>
        public byte[] ToGray()
        {
            var g = new byte[Width * Height];
            for (int i = 0, p = 0; i < g.Length; i++, p += 4)
                g[i] = (byte)((Pixels[p] * 29 + Pixels[p + 1] * 150 + Pixels[p + 2] * 77) >> 8);
            return g;
        }
    }
}
