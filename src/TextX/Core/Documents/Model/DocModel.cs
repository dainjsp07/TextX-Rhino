using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TextX.Core.Documents.Ocr;

namespace TextX.Core.Documents
{
    /// <summary>Axis-aligned rectangle in PDF user space (points, origin bottom-left).</summary>
    public struct Box
    {
        public float L, B, R, T;

        public Box(float l, float b, float r, float t)
        {
            L = Math.Min(l, r); R = Math.Max(l, r);
            B = Math.Min(b, t); T = Math.Max(b, t);
        }

        public float W { get { return R - L; } }
        public float H { get { return T - B; } }
        public float CX { get { return (L + R) / 2f; } }
        public float CY { get { return (B + T) / 2f; } }
        public bool IsEmpty { get { return W <= 0 && H <= 0; } }

        public static readonly Box Empty = new Box();

        public Box Union(Box o)
        {
            if (IsEmpty && L == 0 && B == 0) return o;
            if (o.IsEmpty && o.L == 0 && o.B == 0) return this;
            return new Box(Math.Min(L, o.L), Math.Min(B, o.B), Math.Max(R, o.R), Math.Max(T, o.T));
        }

        public Box Inflate(float d) { return new Box(L - d, B - d, R + d, T + d); }

        public bool Contains(float x, float y) { return x >= L && x <= R && y >= B && y <= T; }

        public bool Intersects(Box o) { return o.L < R && o.R > L && o.B < T && o.T > B; }

        /// <summary>Horizontal overlap length.</summary>
        public float OverlapX(Box o) { return Math.Max(0, Math.Min(R, o.R) - Math.Max(L, o.L)); }

        /// <summary>Vertical overlap length.</summary>
        public float OverlapY(Box o) { return Math.Max(0, Math.Min(T, o.T) - Math.Max(B, o.B)); }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0:0.#},{1:0.#} - {2:0.#},{3:0.#}]", L, B, R, T);
        }

        internal List<object> ToJson() { return new List<object> { Round(L), Round(B), Round(R), Round(T) }; }

        internal static Box FromJson(object o)
        {
            var l = o as List<object>;
            if (l == null || l.Count < 4) return Empty;
            return new Box(F(l[0]), F(l[1]), F(l[2]), F(l[3]));
        }

        internal static double Round(float v) { return Math.Round(v, 2); }
        internal static float F(object o) { return o is double ? (float)(double)o : 0f; }
    }

    public enum BlockKind { Paragraph = 0, Heading = 1, ListItem = 2, TableCell = 3, Caption = 4, HeaderFooter = 5, Label = 6 }

    public enum BlockAlign { Left = 0, Center = 1, Right = 2, Justify = 3 }

    public enum BlockState
    {
        /// <summary>Not translated yet.</summary>
        Pending = 0,
        /// <summary>Machine translation.</summary>
        Translated = 1,
        /// <summary>Changed by the user (never overwritten by machine translation).</summary>
        Edited = 2,
        /// <summary>Nothing to translate (numbers, codes, already in the target language); shown as is.</summary>
        Kept = 3,
        /// <summary>The translation service failed for this block.</summary>
        Failed = 4,
    }

    /// <summary>One line of source text as it sits on the page (page-frame coordinates).</summary>
    public sealed class DocLine
    {
        public string Text = string.Empty;
        public Box Box;
        public float Baseline;     // y of the baseline (horizontal text)
        public float FontSize;
        public bool Bold, Italic;
        public uint Color = 0xFF000000;
        public string FontName = string.Empty;
        public float Angle;        // radians, counter-clockwise
        public float OriginX, OriginY; // origin of the first character
        public float Advance;      // length along the baseline
        public bool Rtl;
        public List<int> Chars = new List<int>(); // character indices in the page's text layer

        public bool IsRotated { get { return Math.Abs(Math.Sin(Angle)) > 0.05; } }
    }

    /// <summary>A paragraph, heading, list item, table cell or label: the unit that is translated.</summary>
    public sealed class DocBlock
    {
        public string Id = string.Empty;
        public int Page;
        public int Order;           // reading order on the page
        public BlockKind Kind;
        public int Level;           // heading level 1-3
        public Box Box;
        public List<DocLine> Lines = new List<DocLine>();
        public float FontSize;
        public bool Bold, Italic;
        public uint Color = 0xFF000000;
        public string FontName = string.Empty;
        public BlockAlign Align;
        public float LinePitch;     // distance between baselines / font size (1.2 typical)
        public float Angle;
        public bool SourceRtl;
        public bool FromOcr;        // text read by OCR (or an invisible OCR layer): covered, not removed
        public uint Background = 0xFFFFFFFF; // colour behind OCR text
        public int Table = -1, Row = -1, Col = -1;

        public string Source = string.Empty;
        public string Translation;
        public BlockState State;
        public string Note;         // quality or layout remark shown to the user

        public string DisplayText
        {
            get { return State == BlockState.Kept || string.IsNullOrEmpty(Translation) ? Source : Translation; }
        }

        public bool NeedsTranslation
        {
            get { return State == BlockState.Pending || State == BlockState.Failed; }
        }

        public IEnumerable<int> CharIndices { get { return Lines.SelectMany(l => l.Chars); } }

        public bool IsRotated { get { return Math.Abs(Math.Sin(Angle)) > 0.05; } }

        public override string ToString() { return Id + " " + Kind + " " + Box + " \"" + Source + "\""; }
    }

    /// <summary>A page and what was read from it.</summary>
    public sealed class DocPage
    {
        public int Index;
        public float Width, Height;       // displayed size in points (rotation applied) = size of the page frame
        public int Rotation;              // quarter turns clockwise
        public float OriginX, OriginY;    // lower-left of the visible area in user space
        public bool Scanned;              // image-only page read by OCR
        public bool HasOcrLayer;          // scanned page that already had an invisible text layer
        public bool OutlinedText;         // drawing whose words are outlines, read by OCR
        public float OcrConfidence;
        public string OcrLanguages;
        public bool Extracted;

        /// <summary>
        /// How the page was read. Pages read by an older version are read again when the document is opened
        /// (paragraphs whose text is unchanged keep their translations and edits), so improvements reach documents
        /// in the history. Scanned pages are not read by OCR again for this.
        /// </summary>
        public int LayoutVersion;
        public const int CurrentLayout = 5;
        public List<DocBlock> Blocks = new List<DocBlock>();
        public List<Box> Images = new List<Box>();
        public int TableCount;

        /// <summary>Unrotated size of the visible area (user space).</summary>
        public float UserWidth { get { return Rotation % 2 == 1 ? Height : Width; } }
        public float UserHeight { get { return Rotation % 2 == 1 ? Width : Height; } }

        // Blocks and lines are stored in the page frame: the page as displayed (rotation applied), in
        // points, origin bottom-left, y up. PDF user space is used only when writing PDF objects.

        /// <summary>PDF user space to the page frame.</summary>
        public void UserToFrame(float x, float y, out float fx, out float fy)
        {
            x -= OriginX;
            y -= OriginY;
            float w = UserWidth, h = UserHeight;
            float dx, dy; // displayed, from the top-left corner
            switch (Rotation)
            {
                case 1: dx = y; dy = x; break;
                case 2: dx = w - x; dy = y; break;
                case 3: dx = h - y; dy = w - x; break;
                default: dx = x; dy = h - y; break;
            }
            fx = dx;
            fy = Height - dy;
        }

        /// <summary>The page frame to PDF user space.</summary>
        public void FrameToUser(float fx, float fy, out float x, out float y)
        {
            float w = UserWidth, h = UserHeight;
            float dx = fx, dy = Height - fy;
            switch (Rotation)
            {
                case 1: x = dy; y = dx; break;
                case 2: x = w - dx; y = dy; break;
                case 3: x = w - dy; y = h - dx; break;
                default: x = dx; y = h - dy; break;
            }
            x += OriginX;
            y += OriginY;
        }

        /// <summary>Angle (radians) to add to a page-frame direction to get the PDF user-space direction.</summary>
        public float FrameToUserAngle { get { return (float)(Rotation * Math.PI / 2); } }

        /// <summary>A page-frame box as a displayed rectangle (left, top, width, height in points from the top-left).</summary>
        public void DisplayRect(Box b, out float left, out float top, out float width, out float height)
        {
            left = b.L;
            top = Height - b.T;
            width = b.W;
            height = b.H;
        }

        /// <summary>A displayed point (points from the top-left) to the page frame.</summary>
        public void DisplayToFrame(float dx, float dy, out float fx, out float fy)
        {
            fx = dx;
            fy = Height - dy;
        }

        public IEnumerable<DocBlock> InReadingOrder { get { return Blocks.OrderBy(b => b.Order); } }
    }

    /// <summary>A translation job: the source PDF, its pages and every translation and edit.</summary>
    public sealed class DocProject
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = string.Empty;
        public string SourcePath = string.Empty;   // where the user's file was
        public string StoredPdf = string.Empty;    // copy kept in the history folder (if any)
        public long FileSize;
        public int PageCount;
        public string SourceLangSetting = Languages.AutoDetect;
        public string SourceLang = string.Empty;   // detected or chosen
        public string TargetLang = "en";
        public string Provider = string.Empty;
        public DateTime Created = DateTime.UtcNow;
        public DateTime Modified = DateTime.UtcNow;
        public List<DocPage> Pages = new List<DocPage>();
        public string Summary = string.Empty;

        /// <summary>Path of the PDF to read: the stored copy if present, else the original.</summary>
        public string PdfPath
        {
            get { return !string.IsNullOrEmpty(StoredPdf) && System.IO.File.Exists(StoredPdf) ? StoredPdf : SourcePath; }
        }

        public IEnumerable<DocBlock> AllBlocks
        {
            get { return Pages.SelectMany(p => p.InReadingOrder); }
        }

        public int ExtractedPages { get { return Pages.Count(p => p.Extracted); } }

        public int TranslatedBlocks
        {
            get { return AllBlocks.Count(b => b.State == BlockState.Translated || b.State == BlockState.Edited || b.State == BlockState.Kept); }
        }

        public int TotalBlocks { get { return AllBlocks.Count(); } }

        public bool IsTranslated
        {
            get { return ExtractedPages == PageCount && PageCount > 0 && AllBlocks.All(b => !b.NeedsTranslation); }
        }

        public DocBlock FindBlock(string id)
        {
            return AllBlocks.FirstOrDefault(b => b.Id == id);
        }

        // ------------------------------------------------------------------ persistence
        public string ToJson()
        {
            var pages = new List<object>();
            foreach (var p in Pages)
            {
                var blocks = new List<object>();
                foreach (var b in p.Blocks)
                {
                    var lines = new List<object>();
                    foreach (var l in b.Lines)
                    {
                        lines.Add(new Dictionary<string, object>
                        {
                            { "t", l.Text }, { "box", l.Box.ToJson() }, { "bl", Box.Round(l.Baseline) }, { "fs", Box.Round(l.FontSize) },
                            { "b", l.Bold }, { "i", l.Italic }, { "c", (double)l.Color }, { "f", l.FontName }, { "a", Math.Round(l.Angle, 4) },
                            { "ox", Box.Round(l.OriginX) }, { "oy", Box.Round(l.OriginY) }, { "adv", Box.Round(l.Advance) }, { "rtl", l.Rtl },
                            { "ch", CompressRanges(l.Chars) },
                        });
                    }
                    var d = new Dictionary<string, object>
                    {
                        { "id", b.Id }, { "o", (double)b.Order }, { "k", (double)(int)b.Kind }, { "lv", (double)b.Level }, { "box", b.Box.ToJson() },
                        { "fs", Box.Round(b.FontSize) }, { "b", b.Bold }, { "i", b.Italic }, { "c", (double)b.Color }, { "f", b.FontName },
                        { "al", (double)(int)b.Align }, { "lp", Box.Round(b.LinePitch) }, { "a", Math.Round(b.Angle, 4) }, { "rtl", b.SourceRtl },
                        { "ocr", b.FromOcr }, { "bg", (double)b.Background }, { "tb", (double)b.Table }, { "r", (double)b.Row }, { "col", (double)b.Col },
                        { "src", b.Source }, { "st", (double)(int)b.State }, { "lines", lines },
                    };
                    if (b.Translation != null) d["tr"] = b.Translation;
                    if (!string.IsNullOrEmpty(b.Note)) d["note"] = b.Note;
                    blocks.Add(d);
                }
                pages.Add(new Dictionary<string, object>
                {
                    { "i", (double)p.Index }, { "w", Box.Round(p.Width) }, { "h", Box.Round(p.Height) }, { "rot", (double)p.Rotation },
                    { "ox", Box.Round(p.OriginX) }, { "oy", Box.Round(p.OriginY) }, { "scan", p.Scanned }, { "ocrl", p.HasOcrLayer }, { "outl", p.OutlinedText },
                    { "ocrc", Box.Round(p.OcrConfidence) }, { "ocrlang", p.OcrLanguages ?? string.Empty }, { "x", p.Extracted }, { "layout", (double)p.LayoutVersion },
                    { "img", p.Images.Select(i => (object)i.ToJson()).ToList() }, { "tables", (double)p.TableCount }, { "blocks", blocks },
                });
            }
            var root = new Dictionary<string, object>
            {
                { "format", "textx-document" }, { "version", 1.0 }, { "id", Id }, { "name", Name }, { "source", SourcePath },
                { "stored", StoredPdf }, { "size", (double)FileSize }, { "pages", (double)PageCount }, { "srcSetting", SourceLangSetting },
                { "src", SourceLang }, { "tgt", TargetLang }, { "provider", Provider },
                { "created", Created.ToString("o", CultureInfo.InvariantCulture) }, { "modified", Modified.ToString("o", CultureInfo.InvariantCulture) },
                { "summary", Summary }, { "pageData", pages },
            };
            return Json.Serialize(root);
        }

        public static DocProject FromJson(string json)
        {
            var root = Json.Parse(json) as Dictionary<string, object>;
            if (root == null || Json.GetString(root, "format") != "textx-document") throw new FormatException("Not a Text X document project.");
            var p = new DocProject
            {
                Id = Json.GetString(root, "id") ?? Guid.NewGuid().ToString("N"),
                Name = Json.GetString(root, "name") ?? string.Empty,
                SourcePath = Json.GetString(root, "source") ?? string.Empty,
                StoredPdf = Json.GetString(root, "stored") ?? string.Empty,
                FileSize = (long)Num(root, "size"),
                PageCount = (int)Num(root, "pages"),
                SourceLangSetting = Json.GetString(root, "srcSetting") ?? Languages.AutoDetect,
                SourceLang = Json.GetString(root, "src") ?? string.Empty,
                TargetLang = Json.GetString(root, "tgt") ?? "en",
                Provider = Json.GetString(root, "provider") ?? string.Empty,
                Summary = Json.GetString(root, "summary") ?? string.Empty,
            };
            DateTime dt;
            if (DateTime.TryParse(Json.GetString(root, "created"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt)) p.Created = dt;
            if (DateTime.TryParse(Json.GetString(root, "modified"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt)) p.Modified = dt;
            foreach (var po in Json.GetList(root, "pageData") ?? new List<object>())
            {
                var pd = po as Dictionary<string, object>;
                if (pd == null) continue;
                var page = new DocPage
                {
                    Index = (int)Num(pd, "i"), Width = (float)Num(pd, "w"), Height = (float)Num(pd, "h"), Rotation = (int)Num(pd, "rot"),
                    OriginX = (float)Num(pd, "ox"), OriginY = (float)Num(pd, "oy"), Scanned = Bool(pd, "scan"), HasOcrLayer = Bool(pd, "ocrl"), OutlinedText = Bool(pd, "outl"),
                    OcrConfidence = (float)Num(pd, "ocrc"), OcrLanguages = Json.GetString(pd, "ocrlang"),
                    LayoutVersion = (int)Num(pd, "layout"),
                    // read again by a newer version; a scanned page only when OCR read it with Latin models (it may be Hebrew)
                    Extracted = Bool(pd, "x") && ((int)Num(pd, "layout") >= DocPage.CurrentLayout ||
                                                 (Bool(pd, "scan") && !OcrLanguages.IsLatinOnly(Json.GetString(pd, "ocrlang")))),
                    TableCount = (int)Num(pd, "tables"),
                };
                foreach (var io in Json.GetList(pd, "img") ?? new List<object>()) page.Images.Add(Box.FromJson(io));
                foreach (var bo in Json.GetList(pd, "blocks") ?? new List<object>())
                {
                    var bd = bo as Dictionary<string, object>;
                    if (bd == null) continue;
                    var b = new DocBlock
                    {
                        Id = Json.GetString(bd, "id") ?? string.Empty, Page = page.Index, Order = (int)Num(bd, "o"), Kind = (BlockKind)(int)Num(bd, "k"),
                        Level = (int)Num(bd, "lv"), Box = Box.FromJson(Json.Get(bd, "box")), FontSize = (float)Num(bd, "fs"), Bold = Bool(bd, "b"),
                        Italic = Bool(bd, "i"), Color = (uint)Num(bd, "c"), FontName = Json.GetString(bd, "f") ?? string.Empty,
                        Align = (BlockAlign)(int)Num(bd, "al"), LinePitch = (float)Num(bd, "lp"), Angle = (float)Num(bd, "a"), SourceRtl = Bool(bd, "rtl"),
                        FromOcr = Bool(bd, "ocr"), Background = (uint)Num(bd, "bg"), Table = (int)Num(bd, "tb"), Row = (int)Num(bd, "r"), Col = (int)Num(bd, "col"),
                        Source = Json.GetString(bd, "src") ?? string.Empty, Translation = Json.GetString(bd, "tr"), State = (BlockState)(int)Num(bd, "st"),
                        Note = Json.GetString(bd, "note"),
                    };
                    foreach (var lo in Json.GetList(bd, "lines") ?? new List<object>())
                    {
                        var ld = lo as Dictionary<string, object>;
                        if (ld == null) continue;
                        b.Lines.Add(new DocLine
                        {
                            Text = Json.GetString(ld, "t") ?? string.Empty, Box = Box.FromJson(Json.Get(ld, "box")), Baseline = (float)Num(ld, "bl"),
                            FontSize = (float)Num(ld, "fs"), Bold = Bool(ld, "b"), Italic = Bool(ld, "i"), Color = (uint)Num(ld, "c"),
                            FontName = Json.GetString(ld, "f") ?? string.Empty, Angle = (float)Num(ld, "a"), OriginX = (float)Num(ld, "ox"),
                            OriginY = (float)Num(ld, "oy"), Advance = (float)Num(ld, "adv"), Rtl = Bool(ld, "rtl"),
                            Chars = ExpandRanges(Json.GetList(ld, "ch")),
                        });
                    }
                    page.Blocks.Add(b);
                }
                p.Pages.Add(page);
            }
            return p;
        }

        private static double Num(object node, string key)
        {
            var v = Json.Get(node, key);
            return v is double ? (double)v : 0;
        }

        private static bool Bool(object node, string key)
        {
            var v = Json.Get(node, key);
            return v is bool && (bool)v;
        }

        /// <summary>[3,4,5,9] becomes [3,3, 9,1] (start, count pairs).</summary>
        private static List<object> CompressRanges(List<int> values)
        {
            var result = new List<object>();
            int i = 0;
            while (i < values.Count)
            {
                int start = values[i], n = 1;
                while (i + n < values.Count && values[i + n] == start + n) n++;
                result.Add((double)start);
                result.Add((double)n);
                i += n;
            }
            return result;
        }

        private static List<int> ExpandRanges(List<object> pairs)
        {
            var list = new List<int>();
            if (pairs == null) return list;
            for (int i = 0; i + 1 < pairs.Count; i += 2)
            {
                int start = (int)(double)pairs[i], n = (int)(double)pairs[i + 1];
                for (int k = 0; k < n; k++) list.Add(start + k);
            }
            return list;
        }
    }
}
