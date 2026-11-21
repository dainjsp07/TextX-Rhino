using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using TextX.Core.Documents.Fonts;

namespace TextX.Core.Documents.Export
{
    public enum ExportText
    {
        /// <summary>The translation (original text where nothing was translated).</summary>
        Translated = 0,
        /// <summary>The original text only.</summary>
        Original = 1,
        /// <summary>Each paragraph in the original followed by its translation.</summary>
        Bilingual = 2,
    }

    public sealed class DocxOptions
    {
        public ExportText Text = ExportText.Translated;
        /// <summary>Returns a PNG or JPEG of an image area of a page (page, box in the page frame), or null to leave it out.</summary>
        public Func<DocPage, Box, byte[]> ImageProvider;
        public bool PageBreaks = true;
        public FontCatalog Fonts;
    }

    /// <summary>
    /// Writes a Word document (.docx, Office Open XML) rebuilt from the read layout: headings, paragraphs,
    /// list items, tables, images and page breaks, with sizes, bold / italic and colours, right-to-left
    /// paragraphs for Hebrew and Arabic. Needs no Office installation and no libraries.
    /// </summary>
    public static class DocxWriter
    {
        private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        public static void Write(DocProject project, string path, DocxOptions options)
        {
            options = options ?? new DocxOptions();
            var images = new List<byte[]>();
            var body = new StringBuilder();
            bool rtlDoc = options.Text != ExportText.Original ? Languages.IsRtl(project.TargetLang) : project.AllBlocks.Count(b => b.SourceRtl) * 2 > project.TotalBlocks;
            string family = FamilyFor(options, project, rtlDoc);
            var pages = project.Pages.Where(p => p.Extracted).ToList();
            for (int pi = 0; pi < pages.Count; pi++)
            {
                var page = pages[pi];
                var items = new List<KeyValuePair<float, Action>>();
                var blocks = page.InReadingOrder.ToList();
                // tables are written once, where their first cell is in the reading order
                var tablesWritten = new HashSet<int>();
                int seq = 0;
                foreach (var b in blocks)
                {
                    float key = seq++;
                    var block = b;
                    if (b.Kind == BlockKind.TableCell && b.Table >= 0)
                    {
                        if (!tablesWritten.Add(b.Table)) continue;
                        var cells = blocks.Where(x => x.Table == b.Table).ToList();
                        items.Add(new KeyValuePair<float, Action>(key, () => Table(body, cells, project, options, family)));
                    }
                    else
                    {
                        items.Add(new KeyValuePair<float, Action>(key, () => Paragraph(body, block, project, options, family)));
                    }
                }
                // images go before the first block that starts below them
                if (options.ImageProvider != null)
                {
                    float pageArea = page.Width * page.Height;
                    foreach (var img in page.Images)
                    {
                        if ((page.Scanned || page.HasOcrLayer) && img.W * img.H > pageArea * 0.5f) continue; // the scan itself
                        byte[] png;
                        try { png = options.ImageProvider(page, img); }
                        catch { png = null; }
                        if (png == null) continue;
                        int index = blocks.FindIndex(x => x.Box.T < img.T - 1);
                        float key = index < 0 ? seq + 0.5f : index - 0.5f;
                        images.Add(png);
                        int id = images.Count;
                        var box = img;
                        items.Add(new KeyValuePair<float, Action>(key, () => Image(body, id, box, page)));
                    }
                }
                foreach (var item in items.OrderBy(i => i.Key)) item.Value();
                if (options.PageBreaks && pi < pages.Count - 1) body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
            }
            if (pages.Count == 0) body.Append("<w:p/>");
            var first = pages.FirstOrDefault();
            body.Append(SectionProperties(first));
            WritePackage(path, project, body.ToString(), images, family, rtlDoc);
        }

        private static string FamilyFor(DocxOptions options, DocProject project, bool rtl)
        {
            string lang = options.Text == ExportText.Original ? project.SourceLang : project.TargetLang;
            if (options.Fonts != null)
            {
                var face = options.Fonts.Primary(FontKind.Sans, false, false, lang);
                if (face != null && !string.IsNullOrEmpty(face.Font.FamilyName)) return face.Font.FamilyName;
            }
            switch ((lang ?? string.Empty).Split('-')[0])
            {
                case "zh": return lang == "zh-TW" ? "Microsoft JhengHei" : "Microsoft YaHei";
                case "ja": return "Yu Gothic";
                case "ko": return "Malgun Gothic";
                case "th": return "Leelawadee UI";
                case "hi": return "Nirmala UI";
                default: return "Arial";
            }
        }

        // ------------------------------------------------------------------ content
        private static string TextFor(DocBlock b, ExportText mode, bool translated)
        {
            if (!translated || mode == ExportText.Original) return b.Source;
            return b.DisplayText;
        }

        private static void Paragraph(StringBuilder sb, DocBlock b, DocProject project, DocxOptions options, string family)
        {
            if (options.Text == ExportText.Bilingual && b.DisplayText != b.Source)
            {
                ParagraphXml(sb, b, b.Source, b.SourceRtl, project.SourceLang, family, 0xFF6B7280, true);
                ParagraphXml(sb, b, b.DisplayText, Languages.IsRtl(project.TargetLang), project.TargetLang, family, b.Color, false);
                return;
            }
            bool original = options.Text == ExportText.Original;
            string text = original ? b.Source : b.DisplayText;
            bool translated = !original && b.DisplayText != b.Source;
            bool rtl = translated ? Languages.IsRtl(project.TargetLang) : b.SourceRtl;
            ParagraphXml(sb, b, text, rtl, translated ? project.TargetLang : project.SourceLang, family, b.Color, false);
        }

        private static void ParagraphXml(StringBuilder sb, DocBlock b, string text, bool rtl, string lang, string family, uint color, bool secondary)
        {
            sb.Append("<w:p><w:pPr>");
            if (b.Kind == BlockKind.Heading && !secondary) sb.Append("<w:pStyle w:val=\"Heading").Append(Math.Max(1, Math.Min(3, b.Level))).Append("\"/>");
            else if (b.Kind == BlockKind.Caption || b.Kind == BlockKind.HeaderFooter) sb.Append("<w:pStyle w:val=\"Caption\"/>");
            if (b.Kind == BlockKind.ListItem) sb.Append("<w:ind w:left=\"360\" w:hanging=\"360\"/>");
            if (rtl) sb.Append("<w:bidi/>");
            var jc = Justification(b, rtl);
            if (jc != null) sb.Append("<w:jc w:val=\"").Append(jc).Append("\"/>");
            if (secondary) sb.Append("<w:spacing w:after=\"40\"/>");
            sb.Append("</w:pPr>");
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append("<w:r>").Append(RunProps(b, rtl, lang, family, color, secondary));
                if (i > 0) sb.Append("<w:br/>");
                sb.Append("<w:t xml:space=\"preserve\">").Append(Xml(lines[i])).Append("</w:t></w:r>");
            }
            sb.Append("</w:p>");
        }

        private static string Justification(DocBlock b, bool rtl)
        {
            switch (b.Align)
            {
                case BlockAlign.Center: return "center";
                case BlockAlign.Justify: return "both";
                default: return null; // natural: left for LTR, right for RTL paragraphs
            }
        }

        private static string RunProps(DocBlock b, bool rtl, string lang, string family, uint color, bool secondary)
        {
            var sb = new StringBuilder("<w:rPr>");
            sb.Append("<w:rFonts w:ascii=\"").Append(Xml(family)).Append("\" w:hAnsi=\"").Append(Xml(family)).Append("\" w:cs=\"").Append(Xml(family)).Append("\" w:eastAsia=\"").Append(Xml(family)).Append("\"/>");
            if (b.Bold && !secondary) sb.Append("<w:b/><w:bCs/>");
            if (b.Italic || secondary) sb.Append("<w:i/><w:iCs/>");
            uint rgb = color & 0xFFFFFF;
            if (rgb != 0 && Luma(color) < 200) sb.Append("<w:color w:val=\"").Append(rgb.ToString("X6", CultureInfo.InvariantCulture)).Append("\"/>");
            if (b.Kind != BlockKind.Heading || secondary)
            {
                int halfPoints = (int)Math.Round(Math.Max(6f, Math.Min(72f, b.FontSize > 0 ? b.FontSize : 11f)) * 2 * (secondary ? 0.9f : 1f));
                sb.Append("<w:sz w:val=\"").Append(halfPoints).Append("\"/><w:szCs w:val=\"").Append(halfPoints).Append("\"/>");
            }
            if (rtl) sb.Append("<w:rtl/>");
            if (!string.IsNullOrEmpty(lang) && lang != Languages.AutoDetect)
            {
                if (rtl) sb.Append("<w:lang w:bidi=\"").Append(Xml(lang)).Append("\"/>");
                else sb.Append("<w:lang w:val=\"").Append(Xml(lang)).Append("\"/>");
            }
            sb.Append("</w:rPr>");
            return sb.ToString();
        }

        private static int Luma(uint argb)
        {
            return (int)((((argb >> 16) & 255) * 77 + ((argb >> 8) & 255) * 150 + (argb & 255) * 29) >> 8);
        }

        private static void Table(StringBuilder sb, List<DocBlock> cells, DocProject project, DocxOptions options, string family)
        {
            int rows = cells.Max(c => c.Row) + 1, cols = cells.Max(c => c.Col) + 1;
            float left = cells.Min(c => c.Box.L), right = cells.Max(c => c.Box.R);
            // column widths from where the columns start
            var starts = Enumerable.Range(0, cols).Select(c => cells.Where(x => x.Col == c).Select(x => x.Box.L).DefaultIfEmpty(left + (right - left) * c / cols).Min()).ToList();
            var widths = new List<int>();
            for (int c = 0; c < cols; c++)
            {
                float end = c + 1 < cols ? starts[c + 1] : right + 6;
                widths.Add(Math.Max(400, (int)((end - starts[c]) * 20)));
            }
            bool rtl = options.Text != ExportText.Original && Languages.IsRtl(project.TargetLang);
            sb.Append("<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/>");
            if (rtl) sb.Append("<w:bidiVisual/>");
            sb.Append("<w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/></w:tblPr><w:tblGrid>");
            foreach (var w in widths) sb.Append("<w:gridCol w:w=\"").Append(w).Append("\"/>");
            sb.Append("</w:tblGrid>");
            for (int r = 0; r < rows; r++)
            {
                sb.Append("<w:tr>");
                for (int c = 0; c < cols; c++)
                {
                    sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(widths[c]).Append("\" w:type=\"dxa\"/></w:tcPr>");
                    var inCell = cells.Where(x => x.Row == r && x.Col == c).OrderBy(x => x.Order).ToList();
                    if (inCell.Count == 0) sb.Append("<w:p/>");
                    foreach (var cell in inCell) Paragraph(sb, cell, project, options, family);
                    sb.Append("</w:tc>");
                }
                sb.Append("</w:tr>");
            }
            sb.Append("</w:tbl><w:p/>");
        }

        private static void Image(StringBuilder sb, int id, Box box, DocPage page)
        {
            // keep the image's size on the page, but no wider than the text area
            double maxWidthPt = Math.Max(100, page.Width - 2 * 72);
            double w = box.W, h = box.H;
            if (w > maxWidthPt) { h *= maxWidthPt / w; w = maxWidthPt; }
            long cx = (long)(w * 12700), cy = (long)(h * 12700);
            sb.Append("<w:p><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">")
              .Append("<wp:extent cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/>")
              .Append("<wp:docPr id=\"").Append(id).Append("\" name=\"Image ").Append(id).Append("\"/>")
              .Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>")
              .Append("<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">")
              .Append("<pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:nvPicPr><pic:cNvPr id=\"").Append(id).Append("\" name=\"image").Append(id).Append(".png\"/><pic:cNvPicPr/></pic:nvPicPr>")
              .Append("<pic:blipFill><a:blip r:embed=\"rIdImg").Append(id).Append("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>")
              .Append("<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic>")
              .Append("</a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
        }

        /// <summary>".jpeg" for JPEG data (the image provider may compress photos), else ".png".</summary>
        internal static string ImageExtension(byte[] data)
        {
            return data != null && data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF ? ".jpeg" : ".png";
        }

        private static string SectionProperties(DocPage page)
        {
            int w = page == null ? 11906 : (int)Math.Round(page.Width * 20);
            int h = page == null ? 16838 : (int)Math.Round(page.Height * 20);
            int margin = Math.Min(1440, Math.Min(w, h) / 10);
            var orient = w > h ? " w:orient=\"landscape\"" : string.Empty;
            return "<w:sectPr><w:pgSz w:w=\"" + w + "\" w:h=\"" + h + "\"" + orient + "/><w:pgMar w:top=\"" + margin + "\" w:right=\"" + margin +
                   "\" w:bottom=\"" + margin + "\" w:left=\"" + margin + "\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>";
        }

        // ------------------------------------------------------------------ package
        private static void WritePackage(string path, DocProject project, string body, List<byte[]> images, string family, bool rtl)
        {
            var temp = path + ".partial";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var ct = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" +
                    "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                    "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                    "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                    "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/></Types>");
                Entry(zip, "[Content_Types].xml", ct.ToString());
                Entry(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                    "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/></Relationships>");
                var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
                for (int i = 0; i < images.Count; i++)
                {
                    var ext = ImageExtension(images[i]);
                    rels.Append("<Relationship Id=\"rIdImg").Append(i + 1).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image").Append(i + 1).Append(ext).Append("\"/>");
                    var e = zip.CreateEntry("word/media/image" + (i + 1) + ext, CompressionLevel.NoCompression);
                    using (var s = e.Open()) s.Write(images[i], 0, images[i].Length);
                }
                rels.Append("</Relationships>");
                Entry(zip, "word/_rels/document.xml.rels", rels.ToString());
                Entry(zip, "word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"" + W + "\" " +
                    "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">" +
                    "<w:body>" + body + "</w:body></w:document>");
                Entry(zip, "word/styles.xml", Styles(family, rtl));
                var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                Entry(zip, "docProps/core.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                    "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                    "<dc:title>" + Xml(Path.GetFileNameWithoutExtension(project.Name)) + "</dc:title><dc:creator>Text X</dc:creator>" +
                    "<dc:language>" + Xml(project.TargetLang) + "</dc:language>" +
                    "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + now + "</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + now + "</dcterms:modified></cp:coreProperties>");
                Entry(zip, "docProps/app.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>Text X</Application></Properties>");
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static string Styles(string family, bool rtl)
        {
            Func<string, string, int, string, string> heading = (id, name, size, color) =>
                "<w:style w:type=\"paragraph\" w:styleId=\"" + id + "\"><w:name w:val=\"" + name + "\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
                "<w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"80\"/><w:outlineLvl w:val=\"" + (id[id.Length - 1] - '1') + "\"/></w:pPr>" +
                "<w:rPr><w:b/><w:bCs/><w:color w:val=\"" + color + "\"/><w:sz w:val=\"" + size + "\"/><w:szCs w:val=\"" + size + "\"/></w:rPr></w:style>";
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"" + W + "\">" +
                "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"" + Xml(family) + "\" w:hAnsi=\"" + Xml(family) + "\" w:cs=\"" + Xml(family) + "\" w:eastAsia=\"" + Xml(family) + "\"/>" +
                "<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"264\" w:lineRule=\"auto\"/>" + (rtl ? "<w:bidi/>" : "") + "</w:pPr></w:pPrDefault></w:docDefaults>" +
                "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" +
                heading("Heading1", "heading 1", 36, "132D52") + heading("Heading2", "heading 2", 28, "132D52") + heading("Heading3", "heading 3", 24, "1F3F6E") +
                "<w:style w:type=\"paragraph\" w:styleId=\"Caption\"><w:name w:val=\"caption\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:color w:val=\"5B6472\"/><w:sz w:val=\"18\"/></w:rPr></w:style>" +
                "<w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/><w:tblPr><w:tblBorders>" +
                "<w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"8A94A6\"/><w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"8A94A6\"/>" +
                "<w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"8A94A6\"/><w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"8A94A6\"/>" +
                "<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"8A94A6\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"8A94A6\"/>" +
                "</w:tblBorders><w:tblCellMar><w:left w:w=\"80\" w:type=\"dxa\"/><w:right w:w=\"80\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>" +
                "</w:styles>";
        }

        private static void Entry(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = e.Open())
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }
        }

        internal static string Xml(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 16);
            foreach (var c in s)
            {
                // characters XML 1.0 does not allow are dropped
                if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') continue;
                if (c == 0xFFFE || c == 0xFFFF) continue;
                sb.Append(c);
            }
            return SecurityElement.Escape(sb.ToString());
        }
    }

    /// <summary>Plain text export: paragraphs in reading order, tables as tab-separated rows.</summary>
    public static class TextExporter
    {
        public static string Build(DocProject project, ExportText mode, bool pageHeadings = true)
        {
            var sb = new StringBuilder();
            foreach (var page in project.Pages.Where(p => p.Extracted))
            {
                if (pageHeadings && project.Pages.Count > 1)
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.AppendLine("--- Page " + (page.Index + 1) + " ---").AppendLine();
                }
                var tables = new HashSet<int>();
                foreach (var b in page.InReadingOrder)
                {
                    if (b.Kind == BlockKind.TableCell && b.Table >= 0)
                    {
                        if (!tables.Add(b.Table)) continue;
                        var cells = page.Blocks.Where(x => x.Table == b.Table).ToList();
                        int rows = cells.Max(c => c.Row) + 1, cols = cells.Max(c => c.Col) + 1;
                        for (int r = 0; r < rows; r++)
                        {
                            var row = new List<string>();
                            for (int c = 0; c < cols; c++)
                                row.Add(string.Join(" ", cells.Where(x => x.Row == r && x.Col == c).OrderBy(x => x.Order).Select(x => Pick(x, mode == ExportText.Bilingual ? ExportText.Translated : mode).Replace('\n', ' '))));
                            sb.AppendLine(string.Join("\t", row));
                        }
                        sb.AppendLine();
                        continue;
                    }
                    if (mode == ExportText.Bilingual && b.DisplayText != b.Source)
                    {
                        sb.AppendLine(b.Source);
                        sb.AppendLine(b.DisplayText);
                    }
                    else
                    {
                        sb.AppendLine(Pick(b, mode));
                    }
                    sb.AppendLine();
                }
            }
            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        private static string Pick(DocBlock b, ExportText mode)
        {
            return mode == ExportText.Original ? b.Source : b.DisplayText;
        }

        public static void Write(DocProject project, string path, ExportText mode)
        {
            File.WriteAllText(path, Build(project, mode), new UTF8Encoding(true));
        }
    }
}
