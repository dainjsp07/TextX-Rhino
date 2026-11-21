using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TextX.Core.Documents.Export;
using TextX.Core.Documents.Fonts;
using TextX.Core.Documents.Layout;

namespace TextX.Core.Documents.Schedules
{
    /// <summary>Excel workbook (.xlsx) with one sheet per schedule: bold frozen header, filters, sized columns.</summary>
    public static class XlsxWriter
    {
        private static readonly HashSet<string> NumberColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Qty", "Tagged", "Page", "Mentions", "No.", "Quantity",
        };

        public static void Write(IList<Schedule> schedules, string path, string title = null)
        {
            var temp = path + ".partial";
            if (File.Exists(temp)) File.Delete(temp);
            var names = SheetNames(schedules);
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var types = new StringBuilder();
                types.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                types.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
                types.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
                types.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
                types.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
                types.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
                for (int i = 0; i < schedules.Count; i++)
                    types.Append("<Override PartName=\"/xl/worksheets/sheet").Append(i + 1).Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                types.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
                types.Append("</Types>");
                Entry(zip, "[Content_Types].xml", types.ToString());

                Entry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>");
                Entry(zip, "docProps/core.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                    "<dc:title>" + X(title ?? "Schedules") + "</dc:title><dc:creator>Text X</dc:creator>" +
                    "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "</dcterms:created></cp:coreProperties>");

                var wb = new StringBuilder();
                wb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
                for (int i = 0; i < schedules.Count; i++)
                    wb.Append("<sheet name=\"").Append(X(names[i])).Append("\" sheetId=\"").Append(i + 1).Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
                wb.Append("</sheets><definedNames>");
                for (int i = 0; i < schedules.Count; i++)
                {
                    if (schedules[i].Rows.Count == 0 || schedules[i].Columns.Count == 0) continue;
                    wb.Append("<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"").Append(i).Append("\" hidden=\"1\">'")
                      .Append(X(names[i].Replace("'", "''"))).Append("'!$A$1:$").Append(ColumnName(schedules[i].Columns.Count - 1)).Append("$").Append(schedules[i].Rows.Count + 1).Append("</definedName>");
                }
                wb.Append("</definedNames></workbook>");
                Entry(zip, "xl/workbook.xml", wb.ToString().Replace("<definedNames></definedNames>", string.Empty));

                var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                for (int i = 0; i < schedules.Count; i++)
                    rels.Append("<Relationship Id=\"rId").Append(i + 1).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet").Append(i + 1).Append(".xml\"/>");
                rels.Append("<Relationship Id=\"rId").Append(schedules.Count + 1).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                Entry(zip, "xl/_rels/workbook.xml.rels", rels.ToString());

                Entry(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Arial\"/><family val=\"2\"/></font><font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Arial\"/><family val=\"2\"/></font></fonts>" +
                    "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F2A44\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                    "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style=\"thin\"><color rgb=\"FFD0D5DE\"/></left><right style=\"thin\"><color rgb=\"FFD0D5DE\"/></right><top style=\"thin\"><color rgb=\"FFD0D5DE\"/></top><bottom style=\"thin\"><color rgb=\"FFD0D5DE\"/></bottom><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +
                    "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                    "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"top\"/></xf></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");

                for (int i = 0; i < schedules.Count; i++) Entry(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", Sheet(schedules[i], i == 0));
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static string Sheet(Schedule s, bool selected)
        {
            int cols = Math.Max(1, s.Columns.Count);
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"").Append(selected ? " tabSelected=\"1\"" : string.Empty).Append("><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sb.Append("<sheetFormatPr defaultRowHeight=\"14\"/><cols>");
            for (int c = 0; c < cols; c++)
            {
                int len = Math.Max(Length(c < s.Columns.Count ? s.Columns[c] : string.Empty) + 2, s.Rows.Select(r => Length(r[c])).DefaultIfEmpty(0).Max() + 2);
                double width = Math.Max(6, Math.Min(60, len));
                sb.Append("<col min=\"").Append(c + 1).Append("\" max=\"").Append(c + 1).Append("\" width=\"").Append(width.ToString("0.#", CultureInfo.InvariantCulture)).Append("\" customWidth=\"1\"/>");
            }
            sb.Append("</cols><sheetData>");
            sb.Append("<row r=\"1\">");
            for (int c = 0; c < cols; c++) Cell(sb, c, 1, c < s.Columns.Count ? s.Columns[c] : string.Empty, 1, false);
            sb.Append("</row>");
            for (int r = 0; r < s.Rows.Count; r++)
            {
                sb.Append("<row r=\"").Append(r + 2).Append("\">");
                for (int c = 0; c < cols; c++)
                {
                    var v = s.Rows[r][c];
                    bool number = c < s.Columns.Count && (NumberColumns.Contains(s.Columns[c]) || s.Kind == ScheduleKind.Table) && Regex.IsMatch(v, @"^-?(?:0|[1-9]\d{0,14})(?:\.\d+)?$");
                    Cell(sb, c, r + 2, v, number ? 3 : 2, number);
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            if (s.Rows.Count > 0) sb.Append("<autoFilter ref=\"A1:").Append(ColumnName(cols - 1)).Append(s.Rows.Count + 1).Append("\"/>");
            sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/><pageSetup orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static int Length(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return s.Split('\n').Max(l => l.Length + l.Count(ch => ch > 0x2E80) );
        }

        private static void Cell(StringBuilder sb, int col, int row, string value, int style, bool number)
        {
            string r = ColumnName(col) + row.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(value)) { sb.Append("<c r=\"").Append(r).Append("\" s=\"").Append(style).Append("\"/>"); return; }
            if (number) sb.Append("<c r=\"").Append(r).Append("\" s=\"").Append(style).Append("\"><v>").Append(value).Append("</v></c>");
            else sb.Append("<c r=\"").Append(r).Append("\" s=\"").Append(style).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(X(value)).Append("</t></is></c>");
        }

        internal static string ColumnName(int index)
        {
            var name = string.Empty;
            index++;
            while (index > 0)
            {
                int m = (index - 1) % 26;
                name = (char)('A' + m) + name;
                index = (index - m - 1) / 26;
            }
            return name;
        }

        internal static List<string> SheetNames(IList<Schedule> schedules)
        {
            var names = new List<string>();
            foreach (var s in schedules)
            {
                var n = Regex.Replace(s.Title ?? "Sheet", @"[\[\]:*?/\\']", " ").Trim();
                if (n.Length == 0) n = "Sheet";
                if (n.Length > 31) n = n.Substring(0, 31).Trim();
                var unique = n;
                for (int k = 2; names.Contains(unique, StringComparer.OrdinalIgnoreCase); k++)
                {
                    var suffix = " (" + k + ")";
                    unique = (n.Length + suffix.Length > 31 ? n.Substring(0, 31 - suffix.Length) : n) + suffix;
                }
                names.Add(unique);
            }
            return names;
        }

        private static void Entry(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
        }

        /// <summary>XML text, without characters XML cannot hold.</summary>
        internal static string X(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '"') sb.Append("&quot;");
                else if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c != 0xFFFE && c != 0xFFFF)) sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>Comma separated values (UTF-8, opens in Excel with accents intact).</summary>
    public static class CsvWriter
    {
        /// <param name="separator">',' or the user's list separator (';' where commas are decimal marks).</param>
        public static void Write(Schedule s, string path, char separator = ',')
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(separator.ToString(), s.Columns.Select(c => Quote(c, separator))));
            foreach (var r in s.Rows)
                sb.AppendLine(string.Join(separator.ToString(), Enumerable.Range(0, s.Columns.Count).Select(c => Quote(r[c], separator))));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static string Quote(string v, char separator)
        {
            v = v ?? string.Empty;
            if (v.IndexOf(separator) >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0 || (v.Length > 0 && (v[0] == ' ' || v[v.Length - 1] == ' ')))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            // a leading = + - @ would be run as a formula by spreadsheets
            if (Regex.IsMatch(v, @"^(?:[=@]|[+\-][\p{L}(])")) return "'" + v;
            return v;
        }
    }

    /// <summary>Printable schedule sheets (PDF): title, project details, then the table across as many pages as needed.</summary>
    public static class SchedulePdfWriter
    {
        private const uint Ink = 0xFF1A1F2B, Muted = 0xFF5B6475, HeaderFill = 0xFF1F2A44, HeaderInk = 0xFFFFFFFF, Rule = 0xFFD0D5DE, Stripe = 0xFFF4F6F9;

        public static void Write(IList<Schedule> schedules, string path, FontCatalog fonts, string documentName, Schedule projectInfo = null)
        {
            var pdf = new PdfComposer(fonts);
            foreach (var s in schedules.Where(x => x.Columns.Count > 0)) Draw(pdf, s, documentName, projectInfo);
            if (pdf.PageCount == 0) Draw(pdf, new Schedule(ScheduleKind.Table, "Empty", "-"), documentName, null);
            pdf.Save(path);
        }

        private static void Draw(PdfComposer pdf, Schedule s, string documentName, Schedule info)
        {
            int cols = s.Columns.Count;
            bool wide = cols > 8;
            float pw = wide ? 1190.55f : 841.89f, ph = wide ? 841.89f : 595.28f;
            const float margin = 36f, size = 8f, headSize = 8f, pad = 4f;
            float tableW = pw - 2 * margin;

            // column widths from their content (measured), shared out over the page width
            var natural = new float[cols];
            for (int c = 0; c < cols; c++)
            {
                float w = Measure(pdf, s.Columns[c], headSize, true);
                foreach (var r in s.Rows.Take(400)) w = Math.Max(w, Math.Min(260f, Measure(pdf, r[c], size, false)));
                natural[c] = Math.Max(28f, w + 2 * pad);
            }
            float total = natural.Sum();
            var widths = natural.Select(w => total <= tableW ? w * tableW / total : w).ToArray();
            if (total > tableW)
            {
                // shrink the widest columns first, so short ones (codes, quantities) stay on one line
                float excess = total - tableW;
                for (int pass = 0; pass < 30 && excess > 0.5f; pass++)
                {
                    float max = widths.Max();
                    var widest = Enumerable.Range(0, cols).Where(c => widths[c] >= max * 0.85f).ToList();
                    foreach (var c in widest)
                    {
                        float cut = Math.Min(excess / widest.Count, widths[c] - 40f);
                        if (cut <= 0) continue;
                        widths[c] -= cut;
                        excess -= cut;
                    }
                    if (widest.All(c => widths[c] <= 40.5f)) break;
                }
                float sum = widths.Sum();
                for (int c = 0; c < cols; c++) widths[c] *= tableW / sum;
            }

            PdfComposer.Page page = null;
            float y = 0;
            int pageNo = 0;
            Action newPage = () =>
            {
                page = pdf.AddPage(pw, ph);
                pageNo++;
                y = ph - margin;
                Text(pdf, page, s.Title, margin, y - 14f, 14f, true, Ink, tableW);
                y -= 22f;
                var sub = (documentName ?? string.Empty) + (pageNo > 1 ? "  —  continued" : string.Empty);
                Text(pdf, page, sub, margin, y - 9f, 9f, false, Muted, tableW);
                y -= 16f;
                if (pageNo == 1 && info != null && info.Rows.Count > 0 && s.Kind != ScheduleKind.ProjectInfo)
                {
                    var line = string.Join("     ", info.Rows.Take(6).Select(r => r[0] + ": " + r[1]));
                    foreach (var l in pdf.Wrap(line, tableW, 8f).Take(2))
                    {
                        pdf.Line(page, l, 8f, margin, y - 8f, Muted);
                        y -= 11f;
                    }
                    y -= 4f;
                }
                // header row
                var heads = Enumerable.Range(0, cols).Select(c => pdf.Wrap(s.Columns[c], widths[c] - 2 * pad, headSize, true)).ToList();
                float hh = Math.Max(1, heads.Max(h => h.Count)) * headSize * 1.25f + 2 * pad;
                pdf.Rect(page, margin, y - hh, tableW, hh, HeaderFill);
                float x = margin;
                for (int c = 0; c < cols; c++)
                {
                    float ly = y - pad - headSize * 0.95f;
                    foreach (var l in heads[c]) { pdf.Line(page, l, headSize, x + pad, ly, HeaderInk); ly -= headSize * 1.25f; }
                    x += widths[c];
                }
                y -= hh;
                Text(pdf, page, "Page " + pageNo, pw - margin - 60f, margin - 18f, 7f, false, Muted, 60f);
                Text(pdf, page, "Text X • " + DateTime.Now.ToString("d", CultureInfo.CurrentCulture), margin, margin - 18f, 7f, false, Muted, 200f);
            };
            newPage();
            for (int r = 0; r < s.Rows.Count; r++)
            {
                var cells = Enumerable.Range(0, cols).Select(c => pdf.Wrap(s.Rows[r][c], widths[c] - 2 * pad, size)).ToList();
                float rh = Math.Max(1, cells.Max(cl => cl.Count)) * size * 1.25f + 2 * pad;
                if (y - rh < margin) newPage();
                if (r % 2 == 1) pdf.Rect(page, margin, y - rh, tableW, rh, Stripe);
                pdf.Rect(page, margin, y - rh, tableW, 0.5f, Rule);
                float x = margin;
                for (int c = 0; c < cols; c++)
                {
                    float ly = y - pad - size * 0.95f;
                    foreach (var l in cells[c]) { pdf.Line(page, l, size, x + pad, ly, Ink); ly -= size * 1.25f; }
                    x += widths[c];
                }
                y -= rh;
            }
            if (s.Rows.Count == 0) Text(pdf, page, "Nothing found.", margin, y - 16f, 9f, false, Muted, tableW);
        }

        private static float Measure(PdfComposer pdf, string text, float size, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var lines = pdf.Wrap(text, 100000f, size, bold);
            return lines.Count == 0 ? 0 : lines.Max(l => l.Width);
        }

        private static void Text(PdfComposer pdf, PdfComposer.Page page, string text, float x, float y, float size, bool bold, uint color, float width)
        {
            var lines = pdf.Wrap(text, width, size, bold);
            if (lines.Count > 0) pdf.Line(page, lines[0], size, x, y, color);
        }
    }
}
