using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TextX.Core.Documents.Schedules
{
    public sealed class ExtractOptions
    {
        /// <summary>Read the translated text (when the document has been translated) instead of the original.</summary>
        public bool UseTranslation;
        public bool Dimensions = true;
    }

    /// <summary>
    /// Reads drawings and specifications into schedules: products and items (by their codes or
    /// callouts), materials and finishes, project details from title blocks, notes, dimensions and
    /// the document's own tables. Works from the text and its position on the page with rules and
    /// word lists (no AI, no internet). Everything it finds can be checked against the page and edited.
    /// </summary>
    public static class ScheduleExtractor
    {
        public const string Code = "Code", Category = "Category", Description = "Description", Material = "Material", Finish = "Finish",
            Colour = "Colour", Size = "Size", Qty = "Qty", Tagged = "Tagged", Manufacturer = "Manufacturer", Model = "Model",
            Location = "Location", PageColumn = "Page", Notes = "Notes";

        public static readonly string[] ProductColumns = { Code, Category, Description, Material, Finish, Colour, Size, Qty, Tagged, Manufacturer, Model, Location, PageColumn, Notes };

        private const string CodeCore = @"[A-Z]{1,4}[\-‐‑–.]?\d{1,4}[A-Z]?(?:[.\-]\d{1,3})?";
        internal static readonly Regex CodeToken = new Regex(@"(?<![\p{L}\p{N}\-/.])(?<code>" + CodeCore + @")(?![\p{L}\p{N}]|[\-/]\d|\.\d)", RegexOptions.Compiled);
        private static readonly Regex TagOnly = new Regex(@"^[\(\[]?(?<code>" + CodeCore + @")[\)\]]?(?:\s*\(?\s*[x×]\s?(?<n>\d{1,3})\s*\)?)?$", RegexOptions.Compiled);
        private static readonly Regex DimensionOnly = new Regex(@"^(?:[ØøHWDLRT]\s*=?\s*)?\d{1,6}(?:[.,]\d+)?(?:\s*(?:mm|cm|m|in|""|'|ft))?(?:\s*[x×X*]\s*\d{1,6}(?:[.,]\d+)?(?:\s*(?:mm|cm|m))?){0,3}$", RegexOptions.Compiled);
        private static readonly Regex SizeInText = new Regex(@"(?<![\p{L}\p{N}])(?:(?:[ØøHWDLT]|thk|ep)\s*[=:]?\s*\d+(?:[.,]\d+)?\s*(?:mm|cm|m)?(?![\p{L}\p{N}])|\d+(?:[.,]\d+)?\s*(?:mm|cm|m)?\s*[x×X*]\s*\d+(?:[.,]\d+)?(?:\s*[x×X*]\s*\d+(?:[.,]\d+)?)?\s*(?:mm|cm|m)?(?![\p{L}\p{N}])|\d+(?:[.,]\d+)?\s*(?:mm|cm)(?![\p{L}\p{N}]))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ColourCode = new Regex(@"\bRAL\s*(?:classic\s*)?\d{4}\b|\bNCS\s*(?:S\s*)?\d{4}\s*-?\s*[A-Z]\d{0,2}[A-Z]?\b|\bPantone\s+[\w\-]+(?:\s+[A-Z]{1,2})?\b|\bPMS\s*\d{2,4}\s*[A-Z]?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex QtyInText = new Regex(@"(?:\b(?:qty|quantity|qt[eé]|quantit[eé]|menge|anzahl|cantidad|quantit[aà]|nb|aantal|stk)\.?\s*[:=]?\s*(?<n>\d{1,4})\b)|(?:\b(?<n>\d{1,4})\s*(?:no\.?|nos|pcs|pc|pieces|pièces|stk\.?|stück|uds?\.?|pz|st\.)(?![\p{L}]))|(?:\(\s*[x×]\s?(?<n>\d{1,4})\s*\))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MakerInText = new Regex(@"(?:\b(?:manufacturer|supplier|brand|make|fabricant|fournisseur|marque|hersteller|lieferant|marke|fabricante|proveedor|marca|produttore|fornitore|fabrikant|merk)\s*[:\-]\s*)(?<v>[^,;\n]{2,40})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ModelInText = new Regex(@"(?:\b(?:model|modèle|modele|modell|modelo|modello|art\.?\s*no\.?|art\.-nr\.?|article\s*no\.?|product\s*code|ref\.?)\s*[:#\-]?\s*)(?<v>[A-Z0-9][\w\-./]{1,30})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FirmWords = new Regex(@"\b(?:architect(?:s|e|es|ure)?|architekt(?:en|ur)?|arquitect(?:o|os|ura)|architett(?:o|i|ura)|atelier|studio|associates|associes|design|interiors|partners|ltd|llp|gmbh|sarl|sas|s\.a\.|s\.l\.|srl|spa|inc|llc|b\.v\.)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NoteStart = new Regex(@"^\s*(?:note|notes|n\.?b\.?|nota|hinweis|remarque|opmerking)\s*[:\-.]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NumberedItem = new Regex(@"(?:(?<=^)|(?<=\s))(?=\d{1,2}[.)]\s+\p{L})", RegexOptions.Compiled);
        /// <summary>Abbreviations that stay in capitals when drawing text is put in title case.</summary>
        private static readonly HashSet<string> Abbreviations = new HashSet<string>(StringComparer.Ordinal)
        {
            "MDF", "HDF", "HPL", "CPL", "OSB", "PVC", "LVT", "PMMA", "ABS", "LED", "RAL", "NCS", "PMS", "WC", "MFC", "MR", "FR", "UV", "PU", "PE", "PP", "GRP", "SS", "AISI", "EN", "ISO", "DIN", "BS",
        };

        private static readonly HashSet<string> NotCodePrefixes = new HashSet<string>(StringComparer.Ordinal)
        {
            "RAL", "NCS", "EN", "ISO", "DIN", "BS", "NF", "UNI", "ASTM", "DN", "PN", "IP", "IK", "CE", "UL", "PMS", "REV", "TEL", "FAX", "UNE", "CEN", "ANSI", "NEN", "SN", "VDE",
        };

        private sealed class Item
        {
            public string Code = string.Empty, Key;
            public int Priority = 9;          // 0 table, 1 legend, 2 label near a tag, 3 callout
            public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();
            public readonly List<string> Texts = new List<string>();   // all text describing it (for attributes)
            public readonly List<string> Labels = new List<string>();  // drawing labels not used as the description
            public int Page = -1;
            public Box Box;
            public bool HasBox;
            public readonly List<string> Blocks = new List<string>();
            public int Tags;
        }

        public static ScheduleSet Extract(DocProject project, ExtractOptions options = null)
        {
            options = options ?? new ExtractOptions();
            Func<DocBlock, string> text = b =>
            {
                var t = options.UseTranslation && !string.IsNullOrWhiteSpace(b.Translation) ? b.Translation : b.Source;
                return Regex.Replace(t ?? string.Empty, @"[ \s]+", " ").Replace("­", "-").Trim();
            };
            var set = new ScheduleSet { DocumentName = project.Name };
            var info = new Schedule(ScheduleKind.ProjectInfo, "Project information", "Field", "Value", PageColumn);
            var items = new Dictionary<string, Item>(StringComparer.Ordinal);
            var notes = new Schedule(ScheduleKind.Notes, "Notes", "No.", "Note", PageColumn);
            var dims = new Schedule(ScheduleKind.Dimensions, "Dimensions", "Dimension", "Near", PageColumn);
            var tables = new List<Schedule>();
            var used = new HashSet<DocBlock>();          // blocks consumed as title-block fields, notes ...
            var fields = new HashSet<DocBlock>();        // title-block labels and values (not materials)
            var describing = new HashSet<DocBlock>();    // blocks that describe an item
            var pageTitles = new Dictionary<int, string>();

            foreach (var page in project.Pages.Where(p => p.Extracted))
            {
                var blocks = page.InReadingOrder.Where(b => text(b).Length > 0).ToList();
                bool drawing = IsDrawing(page);

                // 1. tables
                foreach (var group in blocks.Where(b => b.Kind == BlockKind.TableCell && b.Table >= 0).GroupBy(b => b.Table))
                    ReadTable(group.ToList(), page, text, info, items, tables, fields);

                // 2. title block fields outside tables
                ReadFields(blocks, page, text, info, fields);
                used.UnionWith(fields);

                // 3. notes
                ReadNotes(blocks, page, text, notes, used);

                // drawing title of the page: from the title block, else its main heading
                var title = info.Rows.FirstOrDefault(r => r.Page == page.Index && r[0] == "Drawing title");
                if (title != null) pageTitles[page.Index] = title[1];
                else
                {
                    var heading = blocks.Where(b => b.Kind == BlockKind.Heading && !used.Contains(b) && LanguageGuess.HasWords(text(b))).OrderBy(b => b.Level).ThenBy(b => b.Order).FirstOrDefault();
                    if (heading != null) pageTitles[page.Index] = text(heading);
                }

                // 4. legends ("P-01  Oak veneer") and tags ("P-01" alone on the drawing)
                var tags = new List<KeyValuePair<DocBlock, string>>();
                foreach (var b in blocks.Where(b => !used.Contains(b) && b.Kind != BlockKind.TableCell && b.Kind != BlockKind.HeaderFooter))
                {
                    var t = text(b);
                    var tag = TagOnly.Match(t);
                    if (tag.Success && IsCode(tag.Groups["code"].Value))
                    {
                        tags.Add(new KeyValuePair<DocBlock, string>(b, tag.Groups["code"].Value));
                        continue;
                    }
                    foreach (var entry in LegendEntries(b, t, options.UseTranslation))
                    {
                        var item = ItemFor(items, entry.Key);
                        Offer(item, 1, Description, entry.Value);
                        item.Texts.Add(entry.Value);
                        Place(item, page, b);
                        describing.Add(b);
                    }
                }

                // 5. a tag's description is the nearest free label (above it or beside it)
                var free = blocks.Where(b => !used.Contains(b) && !describing.Contains(b) && IsDescriptive(b, text(b)) && b.Kind != BlockKind.HeaderFooter && b.Kind != BlockKind.TableCell).ToList();
                var claims = new List<Tuple<float, DocBlock, DocBlock, string>>();
                foreach (var tag in tags)
                    foreach (var cand in free)
                    {
                        float d = Distance(tag.Key.Box, cand.Box);
                        float reach = Math.Max(60f, Math.Max(tag.Key.FontSize, cand.FontSize) * 7f);
                        if (d > reach) continue;
                        if (cand.Box.B >= tag.Key.Box.T - 1 && cand.Box.OverlapX(tag.Key.Box) > 0) d *= 0.7f; // label above its tag
                        claims.Add(Tuple.Create(d, tag.Key, cand, tag.Value));
                    }
                var tagDone = new HashSet<DocBlock>();
                var labelDone = new HashSet<DocBlock>();
                foreach (var c in claims.OrderBy(c => c.Item1))
                {
                    if (tagDone.Contains(c.Item2) || labelDone.Contains(c.Item3)) continue;
                    tagDone.Add(c.Item2);
                    labelDone.Add(c.Item3);
                    var item = ItemFor(items, c.Item4);
                    var label = text(c.Item3);
                    if (!Offer(item, 2, Description, label) && !item.Labels.Contains(label) && !SameText(item.Fields[Description], label)) item.Labels.Add(label);
                    item.Texts.Add(label);
                    item.Blocks.Add(c.Item3.Id);
                    describing.Add(c.Item3);
                }
                foreach (var tag in tags)
                {
                    var item = ItemFor(items, tag.Value);
                    item.Tags++;
                    Place(item, page, tag.Key);
                }

                // 6. untagged callouts on drawings that name a product, material or finish
                if (drawing)
                    foreach (var b in free.Where(b => !describing.Contains(b) && (b.Kind == BlockKind.Label || b.Lines.Count <= 3)))
                    {
                        var t = text(b);
                        if (!(Vocabulary.Materials.Contains(t) || Vocabulary.Finishes.Contains(t) || Vocabulary.Products.Contains(t) || ColourCode.IsMatch(t))) continue;
                        var key = "~" + Vocabulary.Fold(t);
                        Item item;
                        if (!items.TryGetValue(key, out item)) items[key] = item = new Item { Key = key, Priority = 3 };
                        Offer(item, 3, Description, t);
                        item.Texts.Add(t);
                        item.Tags++;
                        Place(item, page, b);
                        describing.Add(b);
                    }

                // 7. dimensions on drawings
                if (drawing && options.Dimensions)
                    foreach (var b in blocks.Where(b => !used.Contains(b) && b.Kind != BlockKind.HeaderFooter && b.Kind != BlockKind.TableCell))
                    {
                        var t = text(b);
                        if (!DimensionOnly.IsMatch(t) || !t.Any(char.IsDigit)) continue;
                        var near = free.Where(f => f != b).OrderBy(f => Distance(f.Box, b.Box)).FirstOrDefault(f => Distance(f.Box, b.Box) < 150);
                        var row = dims.Add(page.Index, t, near == null ? string.Empty : text(near), (page.Index + 1).ToString(CultureInfo.InvariantCulture));
                        SetPlace(row, b);
                    }
            }

            // products
            var products = new Schedule(ScheduleKind.Products, "Products and items", ProductColumns);
            foreach (var item in items.Values)
            {
                Complete(item);
                string location;
                pageTitles.TryGetValue(item.Page, out location);
                var row = products.Add(item.Page);
                foreach (var col in ProductColumns)
                {
                    string v;
                    item.Fields.TryGetValue(col, out v);
                    row[products.Column(col)] = v ?? string.Empty;
                }
                row[products.Column(Code)] = item.Code;
                row[products.Column(Tagged)] = item.Tags > 0 ? item.Tags.ToString(CultureInfo.InvariantCulture) : string.Empty;
                if (string.IsNullOrEmpty(row[products.Column(Location)])) row[products.Column(Location)] = location ?? string.Empty;
                row[products.Column(PageColumn)] = item.Page >= 0 ? (item.Page + 1).ToString(CultureInfo.InvariantCulture) : string.Empty;
                if (item.HasBox) { row.Box = item.Box; row.HasBox = true; }
                row.Blocks.AddRange(item.Blocks.Distinct());
            }
            products.Rows = products.Rows
                .OrderBy(r => string.IsNullOrEmpty(r[0]) ? 1 : 0)
                .ThenBy(r => r[0], NaturalComparer.Instance)
                .ThenBy(r => r.Page).ToList();

            var materials = ReadMaterials(project, text, fields, products);

            set.Schedules.Add(products);
            set.Schedules.Add(materials);
            set.Schedules.Add(Dedupe(info));
            if (notes.Rows.Count > 0) set.Schedules.Add(notes);
            if (dims.Rows.Count > 0) set.Schedules.Add(dims);
            set.Schedules.AddRange(tables);
            return set;
        }

        // ------------------------------------------------------------------ tables

        private static void ReadTable(List<DocBlock> cells, DocPage page, Func<DocBlock, string> text, Schedule info, Dictionary<string, Item> items, List<Schedule> tables, HashSet<DocBlock> used)
        {
            int rows = cells.Max(c => c.Row) + 1, cols = cells.Max(c => c.Col) + 1;
            var grid = new string[rows, cols];
            var gridBlocks = new DocBlock[rows, cols];
            foreach (var c in cells)
            {
                if (c.Row < 0 || c.Col < 0) continue;
                grid[c.Row, c.Col] = grid[c.Row, c.Col] == null ? text(c) : grid[c.Row, c.Col] + " " + text(c);
                if (gridBlocks[c.Row, c.Col] == null) gridBlocks[c.Row, c.Col] = c;
            }
            Func<int, int, string> at = (r, c) => grid[r, c] ?? string.Empty;

            // key / value table (a title block)
            int fieldRows = Enumerable.Range(0, rows).Count(r => Vocabulary.TitleFields.Exactly(at(r, 0)) != null);
            if (cols >= 2 && fieldRows * 2 >= rows && fieldRows >= 1)
            {
                for (int r = 0; r < rows; r++)
                {
                    var field = Vocabulary.TitleFields.Exactly(at(r, 0));
                    var value = string.Join(" ", Enumerable.Range(1, cols - 1).Select(c => at(r, c)).Where(v => v.Length > 0));
                    if (field == null || value.Length == 0) continue;
                    var row = info.Add(page.Index, field, value, (page.Index + 1).ToString(CultureInfo.InvariantCulture));
                    if (gridBlocks[r, 1] != null) SetPlace(row, gridBlocks[r, 1]);
                }
                foreach (var c in cells) used.Add(c);
                return;
            }

            // a header row naming the columns
            var header = Enumerable.Range(0, cols).Select(c => Vocabulary.Headings.Exactly(at(0, c))).ToArray();
            int named = header.Count(h => h != null);
            bool boldHeader = cells.Where(c => c.Row == 0).All(c => c.Bold) && cells.Where(c => c.Row > 0).Any(c => !c.Bold);
            bool hasHeader = rows >= 2 && (named * 2 >= cols || boldHeader);
            bool productTable = hasHeader && named >= 2 && header.Any(h => h == Code || h == Description);
            if (productTable)
            {
                for (int r = 1; r < rows; r++)
                {
                    var values = new Dictionary<string, string>();
                    var size = new List<string>();
                    var extra = new List<string>();
                    for (int c = 0; c < cols; c++)
                    {
                        var v = at(r, c);
                        if (v.Length == 0) continue;
                        var h = header[c];
                        if (h == "Width" || h == "Height" || h == "Depth" || h == "Length" || h == "Thickness") { size.Add(h.Substring(0, 1) + " " + v); continue; }
                        if (h == null || h == "Unit" || h == "Price" || h == "Total") { extra.Add((h ?? at(0, c)) + ": " + v); continue; }
                        if (!values.ContainsKey(h)) values[h] = v;
                    }
                    if (size.Count > 0 && !values.ContainsKey(Size)) values[Size] = string.Join(" × ", size);
                    string code;
                    values.TryGetValue(Code, out code);
                    if (string.IsNullOrEmpty(code) && !values.ContainsKey(Description)) continue;
                    var item = string.IsNullOrEmpty(code) ? ItemFor(items, null, values[Description]) : ItemFor(items, code);
                    foreach (var kv in values.Where(kv => kv.Key != Code)) Offer(item, 0, kv.Key, kv.Value);
                    if (extra.Count > 0) Offer(item, 0, Notes, string.Join("; ", extra));
                    item.Texts.AddRange(values.Where(kv => kv.Key == Description || kv.Key == Material || kv.Key == Finish || kv.Key == Colour || kv.Key == Notes).Select(kv => kv.Value));
                    var first = Enumerable.Range(0, cols).Select(c => gridBlocks[r, c]).FirstOrDefault(b => b != null);
                    if (first != null) Place(item, page, first);
                    for (int c = 0; c < cols; c++) if (gridBlocks[r, c] != null) item.Blocks.Add(gridBlocks[r, c].Id);
                }
                return;
            }

            // any other table is kept as it is
            var table = new Schedule(ScheduleKind.Table, "Table " + (tables.Count + 1) + " (page " + (page.Index + 1) + ")");
            int start = hasHeader ? 1 : 0;
            for (int c = 0; c < cols; c++) table.Columns.Add(hasHeader && at(0, c).Length > 0 ? at(0, c) : "Column " + (c + 1));
            for (int r = start; r < rows; r++)
            {
                var row = table.Add(page.Index, Enumerable.Range(0, cols).Select(c => at(r, c)).ToArray());
                var first = Enumerable.Range(0, cols).Select(c => gridBlocks[r, c]).FirstOrDefault(b => b != null);
                if (first != null) SetPlace(row, first);
            }
            if (table.Rows.Count > 0) tables.Add(table);
        }

        // ------------------------------------------------------------------ title block fields

        private static void ReadFields(List<DocBlock> blocks, DocPage page, Func<DocBlock, string> text, Schedule info, HashSet<DocBlock> used)
        {
            foreach (var b in blocks)
            {
                if (used.Contains(b)) continue;
                var t = text(b);
                if (t.Length > 120) continue;
                // "Scale: 1:20" in one text
                int colon = t.IndexOf(':');
                if (colon > 0 && colon < 40 && colon < t.Length - 1)
                {
                    var f = Vocabulary.TitleFields.Exactly(t.Substring(0, colon));
                    var v = t.Substring(colon + 1).Trim();
                    if (f != null && v.Length > 0 && !(f == "Scale" && !v.Any(char.IsDigit)))
                    {
                        var row = info.Add(page.Index, f, v, (page.Index + 1).ToString(CultureInfo.InvariantCulture));
                        SetPlace(row, b);
                        used.Add(b);
                        continue;
                    }
                }
                var field = t.Split(' ').Length <= 4 ? Vocabulary.TitleFields.Exactly(t) : null;
                if (field == null) continue;
                // the value: beside the label on the same line, or just below it
                float size = Math.Max(4f, b.FontSize);
                DocBlock value = null;
                float best = float.MaxValue;
                foreach (var o in blocks)
                {
                    if (o == b || used.Contains(o) || Vocabulary.TitleFields.Exactly(text(o)) != null) continue;
                    float d;
                    bool sameLine = o.Box.L >= b.Box.R - 1 && Math.Min(o.Box.T, b.Box.T) - Math.Max(o.Box.B, b.Box.B) > Math.Min(o.Box.H, b.Box.H) * 0.4f;
                    if (sameLine) d = o.Box.L - b.Box.R;
                    else if (o.Box.T <= b.Box.B + 1 && Math.Abs(o.Box.L - b.Box.L) < size * 1.5f) d = (b.Box.B - o.Box.T) * 1.5f + size;
                    else continue;
                    if (d < best && d < Math.Max(page.Width * 0.3f, size * 25)) { best = d; value = o; }
                }
                if (value != null)
                {
                    var row = info.Add(page.Index, field, text(value), (page.Index + 1).ToString(CultureInfo.InvariantCulture));
                    SetPlace(row, value);
                    used.Add(b);
                    used.Add(value);
                }
            }
            // a firm's name in the title block area (bottom right) without a label
            if (!info.Rows.Any(r => r.Page == page.Index && (r[0] == "Architect" || r[0] == "Interior designer")))
            {
                var firm = blocks.FirstOrDefault(b => !used.Contains(b) && b.Box.CX > page.Width * 0.55f && b.Box.CY < page.Height * 0.35f
                                                  && b.Lines.Count <= 2 && FirmWords.IsMatch(text(b)) && text(b).Length <= 80);
                if (firm != null)
                {
                    var row = info.Add(page.Index, "Architect / designer", text(firm), (page.Index + 1).ToString(CultureInfo.InvariantCulture));
                    SetPlace(row, firm);
                    used.Add(firm);
                }
            }
        }

        private static Schedule Dedupe(Schedule info)
        {
            var seen = new HashSet<string>();
            info.Rows = info.Rows.Where(r => seen.Add(r[0] + "\u0001" + Vocabulary.Fold(r[1]))).ToList();
            return info;
        }

        // ------------------------------------------------------------------ notes

        private static void ReadNotes(List<DocBlock> blocks, DocPage page, Func<DocBlock, string> text, Schedule notes, HashSet<DocBlock> used)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (used.Contains(b)) continue;
                var t = text(b);
                bool heading = Vocabulary.NotesHeadings.Exactly(t) != null;
                if (heading)
                {
                    used.Add(b);
                    // the blocks below the heading, in the same column, until the next heading
                    foreach (var o in blocks.Skip(i + 1))
                    {
                        if (o.Kind == BlockKind.Heading || Vocabulary.NotesHeadings.Exactly(text(o)) != null) break;
                        if (o.Box.T > b.Box.B + 1 || o.Box.OverlapX(b.Box) <= 0 && Math.Abs(o.Box.L - b.Box.L) > 40) continue;
                        if (o.Box.T < b.Box.B - Math.Max(200f, page.Height * 0.4f)) break;
                        if (used.Contains(o) || o.Kind == BlockKind.TableCell) continue;
                        AddNotes(notes, page, o, text(o));
                        used.Add(o);
                    }
                }
                else if (NoteStart.IsMatch(t))
                {
                    AddNotes(notes, page, b, NoteStart.Replace(t, string.Empty).Trim());
                    used.Add(b);
                }
            }
        }

        private static void AddNotes(Schedule notes, DocPage page, DocBlock b, string t)
        {
            var parts = Regex.IsMatch(t, @"^\d{1,2}[.)]\s") ? NumberedItem.Split(t).Select(p => p.Trim()).Where(p => p.Length > 0).ToList() : new List<string> { t };
            foreach (var p in parts)
            {
                var m = Regex.Match(p, @"^(?<n>\d{1,2})[.)]\s+(?<t>.+)$");
                string number = m.Success ? m.Groups["n"].Value : (notes.Rows.Count(r => r.Page == page.Index) + 1).ToString(CultureInfo.InvariantCulture);
                var row = notes.Add(page.Index, number, m.Success ? m.Groups["t"].Value : p.TrimStart('-', '•', '*', ' '), (page.Index + 1).ToString(CultureInfo.InvariantCulture));
                SetPlace(row, b);
            }
        }

        // ------------------------------------------------------------------ materials

        private static Schedule ReadMaterials(DocProject project, Func<DocBlock, string> text, HashSet<DocBlock> used, Schedule products)
        {
            var schedule = new Schedule(ScheduleKind.Materials, "Materials and finishes", "Name", "Type", "Used in", "Mentions", "Pages", "Example");
            var entries = new Dictionary<string, ScheduleRow>();
            var pages = new Dictionary<string, SortedSet<int>>();
            var counts = new Dictionary<string, int>();
            Action<string, string, string, DocBlock, string> add = (key, name, type, b, t) =>
            {
                ScheduleRow row;
                if (!entries.TryGetValue(key, out row))
                {
                    row = schedule.Add(b.Page, Nice(name), type, string.Empty, "0", string.Empty, Snippet(t, name));
                    SetPlace(row, b);
                    entries[key] = row;
                    pages[key] = new SortedSet<int>();
                    counts[key] = 0;
                }
                counts[key]++;
                pages[key].Add(b.Page + 1);
                if (!row.Blocks.Contains(b.Id) && row.Blocks.Count < 50) row.Blocks.Add(b.Id);
            };
            foreach (var b in project.Pages.Where(p => p.Extracted).SelectMany(p => p.InReadingOrder))
            {
                if (b.Kind == BlockKind.HeaderFooter || used.Contains(b)) continue;
                var t = text(b);
                foreach (var span in Spans(Vocabulary.Materials, t)) add("m:" + span.Key, span.Value, "Material", b, t);
                foreach (var span in Spans(Vocabulary.Finishes, t)) add("f:" + span.Key, span.Value, "Finish", b, t);
                foreach (Match m in ColourCode.Matches(t)) add("c:" + Regex.Replace(m.Value.ToUpperInvariant(), @"\s+", " "), Regex.Replace(m.Value, @"\s+", " "), "Colour", b, t);
                foreach (var c in Vocabulary.Colours.Find(t)) add("c:" + c.Canonical, c.Found, "Colour", b, t);
            }
            // which products use each one
            int codeCol = products.Column(Code), descCol = products.Column(Description), matCol = products.Column(Material), finCol = products.Column(Finish), colCol = products.Column(Colour);
            foreach (var kv in entries)
            {
                var row = kv.Value;
                var name = Vocabulary.Fold(row[0]);
                var users = products.Rows.Where(p => p[codeCol].Length > 0 && new[] { p[descCol], p[matCol], p[finCol], p[colCol] }.Any(v => Vocabulary.Fold(v).Contains(name)))
                                         .Select(p => p[codeCol]).Distinct().Take(12).ToList();
                row[2] = string.Join(", ", users);
                row[3] = counts[kv.Key].ToString(CultureInfo.InvariantCulture);
                row[4] = string.Join(", ", pages[kv.Key]);
            }
            schedule.Rows = schedule.Rows.OrderBy(r => r[1] == "Material" ? 0 : r[1] == "Finish" ? 1 : 2).ThenByDescending(r => int.Parse(r[3], CultureInfo.InvariantCulture)).ThenBy(r => r[0]).ToList();
            return schedule;
        }

        /// <summary>Adjacent terms read as one ("oak veneer", "placage chêne"): key from the canonical names, text as found.</summary>
        private static List<KeyValuePair<string, string>> Spans(Vocabulary vocab, string t)
        {
            var result = new List<KeyValuePair<string, string>>();
            var found = vocab.Find(t);
            for (int i = 0; i < found.Count; i++)
            {
                var keys = new List<string> { found[i].Canonical };
                int start = found[i].Index, end = found[i].Index + found[i].Found.Length;
                while (i + 1 < found.Count && found[i].Index >= 0 && found[i + 1].Index >= 0)
                {
                    var between = t.Substring(end, Math.Max(0, found[i + 1].Index - end));
                    if (!Regex.IsMatch(between, @"^(?:[\s\-]+|\s+(?:de|du|en|in|of|di|van|aus|d')\s+)$", RegexOptions.IgnoreCase)) break;
                    i++;
                    keys.Add(found[i].Canonical);
                    end = found[i].Index + found[i].Found.Length;
                }
                var surface = start >= 0 ? t.Substring(start, end - start) : found[i].Found;
                result.Add(new KeyValuePair<string, string>(string.Join(" ", keys.Distinct()).ToLowerInvariant(), surface));
            }
            return result;
        }

        // ------------------------------------------------------------------ items

        private static Item ItemFor(Dictionary<string, Item> items, string code, string description = null)
        {
            string key = code != null ? "#" + CodeKey(code) : "~" + Vocabulary.Fold(description ?? string.Empty);
            Item item;
            if (!items.TryGetValue(key, out item))
            {
                items[key] = item = new Item { Key = key, Code = code ?? string.Empty };
                item.Fields[Description] = string.Empty;
            }
            return item;
        }

        /// <summary>"P-01", "P01", "P.1" are the same code.</summary>
        internal static string CodeKey(string code)
        {
            var m = Regex.Match(code.ToUpperInvariant(), @"^([A-Z]+)[\-‐‑–.]?0*(\d+)(.*)$");
            return m.Success ? m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value.Replace("-", ".") : code.ToUpperInvariant();
        }

        internal static bool IsCode(string code)
        {
            var m = Regex.Match(code, @"^([A-Z]+)([\-‐‑–.]?)(\d+)");
            if (!m.Success) return false;
            var prefix = m.Groups[1].Value;
            if (NotCodePrefixes.Contains(prefix)) return false;
            if (Regex.IsMatch(code, @"^A[0-4]$")) return false;                                        // paper sizes
            if (prefix.Length == 1 && m.Groups[2].Value.Length == 0 && m.Groups[3].Value.Length >= 3 && "HLRT".IndexOf(prefix[0]) >= 0) return false; // H900: a dimension
            return true;
        }

        /// <summary>Sets a field if the source is at least as reliable as the one that set it. True when set.</summary>
        private static bool Offer(Item item, int priority, string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string current;
            item.Fields.TryGetValue(field, out current);
            if (!string.IsNullOrEmpty(current) && (field != Description || priority >= item.Priority)) return false;
            if (field == Description && !string.IsNullOrEmpty(current) && !item.Labels.Contains(current)) item.Labels.Add(current);
            item.Fields[field] = value.Trim();
            if (field == Description) item.Priority = priority;
            return true;
        }

        private static void Place(Item item, DocPage page, DocBlock b)
        {
            if (item.Page < 0 || (!item.HasBox))
            {
                item.Page = page.Index;
                item.Box = b.Box;
                item.HasBox = true;
            }
            if (!item.Blocks.Contains(b.Id)) item.Blocks.Add(b.Id);
        }

        private static void SetPlace(ScheduleRow row, DocBlock b)
        {
            row.Box = b.Box;
            row.HasBox = true;
            if (!row.Blocks.Contains(b.Id)) row.Blocks.Add(b.Id);
        }

        /// <summary>Fills material, finish, colour, size, quantity, maker and category from the item's text.</summary>
        private static void Complete(Item item)
        {
            var texts = item.Texts.Concat(item.Labels).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            Func<string, bool> missing = f => { string v; return !item.Fields.TryGetValue(f, out v) || string.IsNullOrWhiteSpace(v); };
            foreach (var t in texts)
            {
                if (missing(Material)) { var s = Spans(Vocabulary.Materials, t); if (s.Count > 0) item.Fields[Material] = Nice(string.Join(", ", s.Select(x => x.Value))); }
                if (missing(Finish)) { var s = Spans(Vocabulary.Finishes, t); if (s.Count > 0) item.Fields[Finish] = Nice(string.Join(", ", s.Select(x => x.Value))); }
                if (missing(Colour))
                {
                    var codes = ColourCode.Matches(t).Cast<Match>().Select(m => Regex.Replace(m.Value, @"\s+", " ")).ToList();
                    codes.AddRange(Vocabulary.Colours.Find(t).Select(c => Nice(c.Found)));
                    if (codes.Count > 0) item.Fields[Colour] = string.Join(", ", codes.Distinct());
                }
                if (missing(Size)) { var s = SizeInText.Matches(t).Cast<Match>().Select(m => m.Value.Trim()).Distinct().ToList(); if (s.Count > 0) item.Fields[Size] = string.Join(", ", s); }
                if (missing(Qty)) { var m = QtyInText.Match(t); if (m.Success) item.Fields[Qty] = m.Groups["n"].Value; }
                if (missing(Manufacturer)) { var m = MakerInText.Match(t); if (m.Success) item.Fields[Manufacturer] = m.Groups["v"].Value.Trim(); }
                if (missing(Model)) { var m = ModelInText.Match(t); if (m.Success && m.Groups["v"].Value.Any(char.IsDigit)) item.Fields[Model] = m.Groups["v"].Value.Trim(); }
            }
            if (missing(Category))
            {
                string category = null;
                foreach (var t in texts) { var p = Vocabulary.Products.First(t); if (p != null) { category = Nice(p.Canonical); break; } }
                if (category == null && item.Code.Length > 0)
                {
                    var prefix = Regex.Match(item.Code.ToUpperInvariant(), @"^[A-Z]+").Value;
                    string c;
                    if (Vocabulary.TagPrefixes.TryGetValue(prefix, out c) && prefix != "P") category = c;
                }
                if (category == null && (!missing(Material) || !missing(Finish))) category = "Material / finish";
                if (category != null) item.Fields[Category] = category;
            }
            if (item.Labels.Count > 0)
            {
                string notes;
                item.Fields.TryGetValue(Notes, out notes);
                var labels = "On drawing: " + string.Join("; ", item.Labels.Distinct());
                item.Fields[Notes] = string.IsNullOrEmpty(notes) ? labels : notes + "; " + labels;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static bool IsDrawing(DocPage page)
        {
            int labels = page.Blocks.Count(b => b.Kind == BlockKind.Label || (b.Lines.Count == 1 && b.Source.Length < 40 && b.Kind != BlockKind.TableCell));
            int prose = page.Blocks.Count(b => b.Kind == BlockKind.Paragraph && (b.Lines.Count >= 2 || b.Source.Length > 60));
            return page.Width > 1000 || page.Height > 1000 || (labels >= 6 && labels > prose * 3);
        }

        private static IEnumerable<KeyValuePair<string, string>> LegendEntries(DocBlock b, string t, bool translated)
        {
            var first = CodeToken.Match(t);
            if (!first.Success || first.Index != 0 || !IsCode(first.Groups["code"].Value)) yield break;
            // rows of a legend: source lines that start with a code
            if (!translated && b.Lines.Count > 1 && b.Lines.Count(l => StartsWithCode(l.Text)) >= 2)
            {
                string code = null;
                var desc = new StringBuilder();
                foreach (var line in b.Lines)
                {
                    var lt = line.Text.Trim();
                    var m = CodeToken.Match(lt);
                    if (m.Success && m.Index == 0 && IsCode(m.Groups["code"].Value))
                    {
                        if (code != null && LanguageGuess.HasWords(desc.ToString())) yield return new KeyValuePair<string, string>(code, desc.ToString().Trim());
                        code = m.Groups["code"].Value;
                        desc.Clear().Append(Separator(lt.Substring(m.Length)));
                    }
                    else desc.Append(' ').Append(lt);
                }
                if (code != null && LanguageGuess.HasWords(desc.ToString())) yield return new KeyValuePair<string, string>(code, desc.ToString().Trim());
                yield break;
            }
            int rows = translated ? Math.Max(1, b.Lines.Count(l => StartsWithCode(l.Text))) : 1;
            var starts = CodeToken.Matches(t).Cast<Match>().Where(m => (m.Index == 0 || char.IsWhiteSpace(t[m.Index - 1])) && IsCode(m.Groups["code"].Value)).Take(rows).ToList();
            for (int i = 0; i < starts.Count; i++)
            {
                int end = i + 1 < starts.Count ? starts[i + 1].Index : t.Length;
                var rest = Separator(t.Substring(starts[i].Index + starts[i].Length, end - starts[i].Index - starts[i].Length));
                if (LanguageGuess.HasWords(rest)) yield return new KeyValuePair<string, string>(starts[i].Groups["code"].Value, rest.Trim());
            }
        }

        private static string Separator(string rest)
        {
            return Regex.Replace(rest, @"^\s*[\-–—:=.)\]]*\s*", string.Empty);
        }

        private static bool StartsWithCode(string line)
        {
            var m = CodeToken.Match(line.TrimStart());
            return m.Success && m.Index == 0 && IsCode(m.Groups["code"].Value);
        }

        private static bool IsDescriptive(DocBlock b, string t)
        {
            if (!LanguageGuess.HasWords(t) || t.Length > 140 || b.Lines.Count > 4) return false;
            if (TagOnly.IsMatch(t) || DimensionOnly.IsMatch(t)) return false;
            if (b.Kind == BlockKind.Heading && b.Level <= 1) return false;
            if (Vocabulary.TitleFields.Exactly(t) != null || Vocabulary.NotesHeadings.Exactly(t) != null) return false;
            return true;
        }

        /// <summary>Gap between two boxes (0 when they touch or overlap).</summary>
        private static float Distance(Box a, Box b)
        {
            float dx = Math.Max(0, Math.Max(a.L - b.R, b.L - a.R));
            float dy = Math.Max(0, Math.Max(a.B - b.T, b.B - a.T));
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool SameText(string a, string b)
        {
            return string.Equals(Vocabulary.Fold(a ?? string.Empty).Trim(), Vocabulary.Fold(b ?? string.Empty).Trim(), StringComparison.Ordinal);
        }

        /// <summary>DRAWING CAPITALS become "Title case"; short abbreviations (MDF, HPL) stay.</summary>
        internal static string Nice(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var words = s.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i];
                if (Abbreviations.Contains(w.Trim(',', '.', ';', ':', '(', ')')) || (w.Any(char.IsDigit) && w.Any(char.IsLetter))) continue;
                if (w.Any(char.IsLetter) && w.Where(char.IsLetter).All(char.IsUpper)) words[i] = w.Substring(0, 1) + w.Substring(1).ToLowerInvariant();
            }
            var r = string.Join(" ", words);
            return r.Length > 0 && char.IsLower(r[0]) ? char.ToUpper(r[0]) + r.Substring(1) : r;
        }

        private static string Snippet(string t, string term)
        {
            if (t.Length <= 90) return t;
            int i = t.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return t.Substring(0, 87) + "…";
            int start = Math.Max(0, i - 35);
            var s = t.Substring(start, Math.Min(90, t.Length - start));
            return (start > 0 ? "…" : string.Empty) + s + (start + s.Length < t.Length ? "…" : string.Empty);
        }
    }
}
