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
        /// <summary>List every dimension on drawings (off by default: drawings carry hundreds).</summary>
        public bool Dimensions;
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
            Colour = "Colour", Hardware = "Hardware", Size = "Size", Qty = "Qty", Tagged = "Tagged", Manufacturer = "Manufacturer", Model = "Model",
            Location = "Location", PageColumn = "Page", Notes = "Notes";

        public static readonly string[] ProductColumns = { Code, Category, Description, Material, Finish, Hardware, Colour, Size, Qty, Tagged, Manufacturer, Model, Location, PageColumn, Notes };

        private const string CodeCore = @"[A-Z]{1,4}[\-‐‑–.]?\d{1,4}[A-Z]?(?:[.\-]\d{1,3})?";
        internal static readonly Regex CodeToken = new Regex(@"(?<![\p{L}\p{N}\-/.])(?<code>" + CodeCore + @")(?![\p{L}\p{N}]|[\-/]\d|\.\d)", RegexOptions.Compiled);
        private static readonly Regex TagOnly = new Regex(@"^[\(\[]?(?<code>" + CodeCore + @")[\)\]]?(?:\s*\(?\s*[x×]\s?(?<n>\d{1,3})\s*\)?)?$", RegexOptions.Compiled);
        private static readonly Regex DimensionOnly = new Regex(@"^(?:[ØøHWDLRT]\s*=?\s*)?\d{1,6}(?:[.,]\d+)?(?:\s*(?:mm|cm|m|in|""|'|ft))?(?:\s*[x×X*]\s*\d{1,6}(?:[.,]\d+)?(?:\s*(?:mm|cm|m))?){0,3}$", RegexOptions.Compiled);
        private static readonly Regex SizeInText = new Regex(@"(?<![\p{L}\p{N}])(?:(?:[ØøHWDLT]|thk|ep)\s*[=:]?\s*\d+(?:[.,]\d+)?\s*(?:mm|cm|m)?(?![\p{L}\p{N}])|\d+(?:[.,]\d+)?\s*(?:mm|cm|m)?\s*[x×X*]\s*\d+(?:[.,]\d+)?(?:\s*[x×X*]\s*\d+(?:[.,]\d+)?)?\s*(?:mm|cm|m)?(?![\p{L}\p{N}])|\d+(?:[.,]\d+)?\s*(?:mm|cm)(?![\p{L}\p{N}]))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ColourCode = new Regex(@"\bRAL\s*(?:classic\s*)?\d{4}\b|\bNCS\s*(?:S\s*)?\d{4}\s*-?\s*[A-Z]\d{0,2}[A-Z]?\b|\bPantone\s+[\w\-]+(?:\s+[A-Z]{1,2})?\b|\bPMS\s*\d{2,4}\s*[A-Z]?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex QtyInText = new Regex(@"(?:\b(?:qty|quantity|qt[eé]|quantit[eé]|menge|anzahl|cantidad|quantit[aà]|nb|aantal|stk)\.?\s*[:=]?\s*(?<n>\d{1,4})\b)|(?:\b(?<n>\d{1,4})\s*(?:no\.?|nos|pcs|pc|pieces|pièces|stk\.?|stück|uds?\.?|pz|st\.)(?![\p{L}]))|(?:\(\s*[x×]\s?(?<n>\d{1,4})\s*\))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MakerInText = new Regex(@"(?:\b(?:manufacturer|supplier|brand|make|fabricant|fournisseur|marque|hersteller|lieferant|marke|fabricante|proveedor|marca|produttore|fornitore|fabrikant|merk)\s*[:\-]\s*)(?<v>[^,;\n]{2,40})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ModelInText = new Regex(@"(?:\b(?:model|modèle|modele|modell|modelo|modello|art\.?\s*no\.?|art\.-nr\.?|article\s*no\.?|product\s*code|ref\.?)\s*[:#\-]?\s*)(?<v>[A-Z0-9][\w\-./]{1,30})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MakerWords = new Regex(@"(?:made by|manufactured by|supplied by|fabrique par|hergestellt von|fabricado por|prodotto da|(?:\u05D1|\u05DE)?\u05EA\u05D5\u05E6\u05E8\u05EA|\u05E9\u05DC \u05D7\u05D1\u05E8\u05EA|\u05DE\u05D7\u05D1\u05E8\u05EA)\s+(?<v>[\p{L}\d][\p{L}\d&.\-]*(?:\s+[A-Z][\w&.\-]*)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
            public bool SheetItem;
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
            var pageLocations = new Dictionary<int, string>();

            foreach (var page in project.Pages.Where(p => p.Extracted))
            {
                var blocks = page.InReadingOrder.Where(b => text(b).Length > 0).ToList();
                bool drawing = IsDrawing(page);

                // 1. tables
                var rawTables = new List<KeyValuePair<Schedule, List<DocBlock>>>();
                var productCells = new HashSet<DocBlock>();
                foreach (var group in blocks.Where(b => b.Kind == BlockKind.TableCell && b.Table >= 0).GroupBy(b => b.Table))
                    if (ReadTable(group.ToList(), page, text, info, items, rawTables, fields)) productCells.UnionWith(group);

                // 2. title block fields and an item's properties (finishes, hardware ...) outside tables
                var itemFields = ReadFields(blocks.Where(b => !productCells.Contains(b)).ToList(), page, text, info, fields);
                used.UnionWith(fields);
                // tables that were really label / value strips are not listed again
                foreach (var t in rawTables)
                {
                    int letters = t.Value.Count(b => LanguageGuess.HasWords(text(b)));
                    // two-row grids on drawings are mostly dimension chains or label pairs: worth listing from three rows
                    if (t.Key.Rows.Count < 3 || t.Key.Columns.Count < 2 || letters * 10 < t.Value.Count * 4) continue;
                    if (t.Value.Count(b => used.Contains(b)) * 2 >= t.Value.Count) continue;
                    t.Key.Title = "Table " + (tables.Count + 1) + " (page " + (page.Index + 1) + ")";
                    tables.Add(t.Key);
                }

                // 3. notes
                ReadNotes(blocks, page, text, notes, used);

                // 3b. a sheet about one item: its code in the view titles ("JN-01.1 rear elevation"),
                // its name in the title block, its finishes and hardware in the labelled strip
                var sheetCode = SheetCode(blocks, text) ?? CodeInDrawingNumber(info, page, items, blocks, text);
                if (sheetCode != null)
                {
                    var item = ItemFor(items, sheetCode);
                    var name = info.Rows.FirstOrDefault(r => r.Page == page.Index && r[0] == "Drawing title");
                    if (name != null && !string.Equals(CodeKey(name[1]), CodeKey(sheetCode), StringComparison.Ordinal))
                    {
                        Offer(item, 1, Description, name[1]);
                        item.Texts.Insert(0, name[1]);
                    }
                    AddItemFields(item, itemFields);
                    var anchor = blocks.FirstOrDefault(b => text(b).Contains(sheetCode));
                    if (anchor != null) Place(item, page, anchor);
                    item.SheetItem = true;
                }
                // where the page's items are: the title block's location or room
                var place = info.Rows.FirstOrDefault(r => r.Page == page.Index && (r[0] == "Location" || r[0] == "Room"));
                if (place != null) pageLocations[page.Index] = place[1];
                // drawing title of the page: from the title block, else its main heading
                var title = info.Rows.FirstOrDefault(r => r.Page == page.Index && r[0] == "Drawing title");
                DocBlock mainHeading = null;
                if (title != null) pageTitles[page.Index] = title[1];
                else
                {
                    // the biggest heading with a few words (a lone word is usually a name in the title block)
                    mainHeading = blocks.Where(b => b.Kind == BlockKind.Heading && !used.Contains(b) && LanguageGuess.HasWords(text(b)))
                                        .OrderBy(b => text(b).Trim().Contains(" ") ? 0 : 1).ThenBy(b => b.Level).ThenByDescending(b => b.FontSize).ThenBy(b => b.Order).FirstOrDefault();
                    if (mainHeading != null) pageTitles[page.Index] = text(mainHeading);
                }
                // no code, but finishes and hardware listed: they belong to the item the sheet is named after
                // (its "item name" field, else the drawing title or main heading)
                List<string> named;
                string itemName = itemFields.TryGetValue("Name", out named) ? named[0] : null;
                if (sheetCode == null && itemFields.Count > 0 && (itemName != null || pageTitles.ContainsKey(page.Index)))
                {
                    var name = itemName ?? pageTitles[page.Index];
                    var item = ItemFor(items, null, name);
                    Offer(item, 1, Description, name);
                    if (!item.Texts.Contains(name)) item.Texts.Insert(0, name);
                    AddItemFields(item, itemFields);
                    var anchor = (itemName != null ? null : mainHeading) ?? blocks.FirstOrDefault(b => text(b) == name);
                    if (anchor != null) { Place(item, page, anchor); describing.Add(anchor); }
                    item.SheetItem = true;
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
                        bool material = Vocabulary.Materials.Contains(t) || Vocabulary.Finishes.Contains(t) || ColourCode.IsMatch(t);
                        bool product = Vocabulary.Products.Contains(t) && t.Length <= 40 && t.IndexOf(',') < 0;
                        if (!material && !product) continue;
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
                if (!pageLocations.TryGetValue(item.Page, out location)) pageTitles.TryGetValue(item.Page, out location);
                var row = products.Add(item.Page);
                foreach (var col in ProductColumns)
                {
                    string v;
                    item.Fields.TryGetValue(col, out v);
                    row[products.Column(col)] = v ?? string.Empty;
                }
                row[products.Column(Code)] = item.Code;
                row[products.Column(Tagged)] = item.Tags > 0 ? item.Tags.ToString(CultureInfo.InvariantCulture) : string.Empty;
                if (string.IsNullOrEmpty(row[products.Column(Location)]) && !SameText(location, row[products.Column(Description)]))
                    row[products.Column(Location)] = location ?? string.Empty;
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

        /// <summary>Reads one table; true when it was a product schedule.</summary>
        private static bool ReadTable(List<DocBlock> cells, DocPage page, Func<DocBlock, string> text, Schedule info, Dictionary<string, Item> items, List<KeyValuePair<Schedule, List<DocBlock>>> tables, HashSet<DocBlock> used)
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
                return false;
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
                return true;
            }

            // any other table is kept as it is
            var table = new Schedule(ScheduleKind.Table, "Table");
            int start = hasHeader ? 1 : 0;
            for (int c = 0; c < cols; c++) table.Columns.Add(hasHeader && at(0, c).Length > 0 ? at(0, c) : "Column " + (c + 1));
            for (int r = start; r < rows; r++)
            {
                var row = table.Add(page.Index, Enumerable.Range(0, cols).Select(c => at(r, c)).ToArray());
                var first = Enumerable.Range(0, cols).Select(c => gridBlocks[r, c]).FirstOrDefault(b => b != null);
                if (first != null) SetPlace(row, first);
            }
            if (table.Rows.Count > 0) tables.Add(new KeyValuePair<Schedule, List<DocBlock>>(table, cells));
            return false;
        }

        // ------------------------------------------------------------------ title block fields

        private static bool IsFieldLabel(string t)
        {
            return t.Length <= 40 && (Vocabulary.TitleFields.Exactly(t) != null || Vocabulary.ItemFields.Exactly(t) != null);
        }

        /// <summary>
        /// Title-block fields ("Scale 1:20", "Architect ...") go to the project information; an item's
        /// properties ("Exterior finish: ...", "Hardware: ...") are returned for the sheet's item. Values sit
        /// after the label in reading order: to the right, or to the left for right-to-left labels, or below.
        /// </summary>
        private static Dictionary<string, List<string>> ReadFields(List<DocBlock> blocks, DocPage page, Func<DocBlock, string> text, Schedule info, HashSet<DocBlock> used)
        {
            var itemFields = new Dictionary<string, List<string>>();
            Action<string, string> addItem = (f, v) =>
            {
                List<string> list;
                if (!itemFields.TryGetValue(f, out list)) itemFields[f] = list = new List<string>();
                if (!list.Contains(v)) list.Add(v);
            };
            foreach (var b in blocks)
            {
                if (used.Contains(b)) continue;
                var t = text(b);
                if (t.Length > 200) continue;
                // "Scale: 1:20" in one text
                int colon = t.IndexOf(':');
                if (colon > 0 && colon < 40 && colon < t.Length - 1)
                {
                    var label = t.Substring(0, colon);
                    var v = t.Substring(colon + 1).Trim();
                    var f = Vocabulary.TitleFields.Exactly(label);
                    if (f != null && FieldValueOk(f, v))
                    {
                        var row = info.Add(page.Index, f, v, (page.Index + 1).ToString(CultureInfo.InvariantCulture));
                        SetPlace(row, b);
                        used.Add(b);
                        continue;
                    }
                    var itf = Vocabulary.ItemFields.Exactly(label);
                    if (itf != null && v.Length > 1 && v.Any(char.IsLetterOrDigit)) { addItem(itf, v); used.Add(b); continue; }
                }
                // a cell holding two labels on two lines ("item name" over "description") is read by its first
                if (b.Lines.Count == 2 && b.Lines.All(l => IsFieldLabel(l.Text.Trim(' ', ':'))) && text(b) == b.Source) t = b.Lines[0].Text.Trim();
                bool shortLabel = t.Length <= 40 && t.Split(' ').Length <= 4;
                var field = shortLabel ? Vocabulary.TitleFields.Exactly(t) : null;
                var itemField = shortLabel && field == null ? Vocabulary.ItemFields.Exactly(t) : null;
                if (field == null && itemField == null) continue;
                bool rtl = b.SourceRtl || TextX.Core.Text.RtlText.ContainsRtl(t);
                float size = Math.Max(4f, b.FontSize);
                float reach = itemField != null ? page.Width * 0.3f : Math.Min(page.Width * 0.25f, size * 18);
                DocBlock value = null;
                float best = float.MaxValue;
                foreach (var o in blocks)
                {
                    if (o == b || used.Contains(o) || IsFieldLabel(text(o))) continue;
                    if (field != null ? !FieldValueOk(field, text(o)) : !text(o).Any(char.IsLetterOrDigit)) continue;
                    if (Vocabulary.NotesHeadings.Exactly(text(o)) != null || (text(o).TrimEnd().EndsWith(":", StringComparison.Ordinal) && text(o).Length <= 40)) continue; // another label
                    float overlap = Math.Min(o.Box.T, b.Box.T) - Math.Max(o.Box.B, b.Box.B);
                    bool sameLine = overlap > Math.Min(o.Box.H, b.Box.H) * 0.4f;
                    float d;
                    if (sameLine && !rtl && o.Box.L >= b.Box.R - 1) d = o.Box.L - b.Box.R;
                    else if (sameLine && rtl && o.Box.R <= b.Box.L + 1) d = b.Box.L - o.Box.R;
                    else if (o.Box.T <= b.Box.B + 1 && b.Box.B - o.Box.T < size * 2.2f)
                    {
                        // below: aligned with the label's start edge, or starting under the label (title-block cells)
                        float edge = rtl ? Math.Abs(o.Box.R - b.Box.R) : Math.Abs(o.Box.L - b.Box.L);
                        bool under = rtl ? o.Box.R <= b.Box.R + size && o.Box.R >= b.Box.L - size * 4 : o.Box.L >= b.Box.L - size && o.Box.L <= b.Box.R + size * 4;
                        if (edge >= size * 1.5f && !under) continue;
                        d = (b.Box.B - o.Box.T) * 1.5f + size + (edge < size * 1.5f ? 0 : size);
                    }
                    else continue;
                    if (d < best && d < reach) { best = d; value = o; }
                }
                if (value == null) continue;
                used.Add(b);
                used.Add(value);
                if (field != null)
                {
                    var row = info.Add(page.Index, field, text(value), (page.Index + 1).ToString(CultureInfo.InvariantCulture));
                    SetPlace(row, value);
                    continue;
                }
                // a property's text may go on over the lines below (not a name, a quantity or a size)
                var parts = new List<string> { text(value) };
                var last = value;
                int more = itemField == "Name" || itemField == "Quantity" || itemField == "Size" ? 0 : 5;
                for (int k = 0; k < more; k++)
                {
                    float lineSize = Math.Max(4f, last.FontSize);
                    var next = blocks.Where(o => !used.Contains(o) && o.Box.T <= last.Box.B + 1 && last.Box.B - o.Box.T < lineSize * 1.2f &&
                                                 (rtl ? Math.Abs(o.Box.R - last.Box.R) : Math.Abs(o.Box.L - last.Box.L)) < lineSize * 2 && !IsFieldLabel(text(o)) &&
                                                 !blocks.Any(l => l != o && SameLine(l.Box, o.Box) && IsFieldLabel(text(l))))   // the value of the next label
                                     .OrderByDescending(o => o.Box.T).FirstOrDefault();
                    if (next == null) break;
                    parts.Add(text(next));
                    used.Add(next);
                    last = next;
                }
                addItem(itemField, string.Join("; ", parts));
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
            return itemFields;
        }

        private static bool SameLine(Box a, Box b)
        {
            float overlap = Math.Min(a.T, b.T) - Math.Max(a.B, b.B);
            return overlap > Math.Min(a.H, b.H) * 0.4f;
        }

        private static readonly Regex ScaleValue = new Regex(@"(^|[^\d])1\s*[:/]\s*\d+|\bN\.?\s?T\.?\s?S\b|\bas\s+shown\b|\bwithout\b|ללא", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Whether a text can be the value of a title-block field (a scale looks like 1:20, a phone has digits ...).</summary>
        internal static bool FieldValueOk(string field, string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length == 0 || value.Length > 120 || !value.Any(char.IsLetterOrDigit)) return false;
            int digits = value.Count(char.IsDigit), letters = value.Count(char.IsLetter);
            switch (field)
            {
                case "Scale": return ScaleValue.IsMatch(value);
                case "Telephone": return digits >= 6 && letters <= 3;
                case "Email": return value.Contains("@");
                case "Website": return value.Contains(".") && !value.Contains(" ");
                case "Date": return digits >= 2;
                case "Revision":
                case "Sheet": return value.Length <= 20;
                case "Drawing number":
                case "Project number": return digits > 0 && value.Length <= 40;
                case "Paper size": return Regex.IsMatch(value, @"\b[AB][0-4]\b|\d", RegexOptions.IgnoreCase);
                case "Client":
                case "Architect":
                case "Interior designer":
                case "Contractor":
                case "Manufacturer":
                case "Engineer":
                case "Drawn by":
                case "Checked by":
                case "Approved by":
                    // a name: mostly letters, not a phone number or a date
                    return letters >= 2 && digits * 2 <= letters && value.Length <= 80;
                default: return letters + digits >= 2 || digits == 1 && value.Length == 1;
            }
        }

        /// <summary>
        /// The item code inside the sheet's drawing number ("ABC-P10-JN-23-L07" is the sheet of JN-23),
        /// when exactly one of its parts is a code already seen in the set.
        /// </summary>
        private static string CodeInDrawingNumber(Schedule info, DocPage page, Dictionary<string, Item> items, List<DocBlock> blocks, Func<DocBlock, string> text)
        {
            var number = info.Rows.FirstOrDefault(r => r.Page == page.Index && r[0] == "Drawing number");
            if (number == null) return null;
            var parts = number[1].Split('-', '_', ' ');
            var found = new List<string>();
            for (int i = 0; i + 1 < parts.Length; i++)
            {
                var candidate = parts[i] + "-" + parts[i + 1];
                if (!IsCode(candidate)) continue;
                // a sub-number ("JN-01.1") stays with its code
                var key = CodeKey(candidate);
                bool elsewhere = blocks.Any(b => !text(b).Contains(number[1]) && CodeToken.Matches(text(b)).Cast<Match>().Any(m => CodeKey(m.Groups["code"].Value) == key));
                if (items.ContainsKey("#" + key) || elsewhere) found.Add(candidate);
            }
            return found.Count == 1 ? found[0] : null;
        }

        /// <summary>The code a sheet is about: named in most of its view titles ("JN-01.1 rear elevation").</summary>
        private static string SheetCode(List<DocBlock> blocks, Func<DocBlock, string> text)
        {
            var counts = new Dictionary<string, KeyValuePair<string, int>>();
            foreach (var b in blocks)
            {
                if (b.Kind == BlockKind.Paragraph || b.Kind == BlockKind.ListItem || b.Kind == BlockKind.HeaderFooter) continue;
                var t = text(b);
                if (t.Length > 60) continue;
                var codes = CodeToken.Matches(t).Cast<Match>().Select(m => m.Groups["code"].Value).Where(IsCode).Distinct().ToList();
                if (codes.Count != 1) continue;
                var rest = Regex.Replace(t.Replace(codes[0], " "), @"[\d\W_]+", " ");
                if (!Regex.IsMatch(rest, @"\p{L}{2,}")) continue;   // a view title has words besides the code
                var key = CodeKey(codes[0]);
                KeyValuePair<string, int> c;
                counts.TryGetValue(key, out c);
                counts[key] = new KeyValuePair<string, int>(c.Key ?? codes[0], c.Value + 1);
            }
            if (counts.Count == 0) return null;
            var ordered = counts.Values.OrderByDescending(v => v.Value).ToList();
            if (ordered[0].Value < 2 && counts.Count > 1) return null;
            if (ordered.Count > 1 && ordered[1].Value == ordered[0].Value) return null;
            return ordered[0].Key;
        }

        private static void AddItemFields(Item item, Dictionary<string, List<string>> fields)
        {
            Func<string, string> get = f => { List<string> l; return fields.TryGetValue(f, out l) ? string.Join("; ", l) : null; };
            var finishes = new List<string>();
            var ext = get("Exterior finish");
            var inside = get("Interior finish");
            var plain = get("Finish");
            if (ext != null) finishes.Add("Exterior: " + ext);
            if (inside != null) finishes.Add("Interior: " + inside);
            if (plain != null) finishes.Add(plain);
            if (finishes.Count > 0) Offer(item, 1, Finish, string.Join(" | ", finishes));
            var hardware = get("Hardware");
            if (hardware != null) Offer(item, 1, Hardware, hardware);
            var material = get("Material");
            if (material != null) Offer(item, 1, Material, material);
            var colour = get("Colour");
            if (colour != null) Offer(item, 1, Colour, colour);
            var qty = get("Quantity");
            if (qty != null && Regex.IsMatch(qty, @"^\s*\d+\s*$")) Offer(item, 1, Qty, qty.Trim());
            var size = get("Size");
            if (size != null && size.Any(char.IsDigit)) Offer(item, 1, Size, size);
            var other = new List<string>();
            var light = get("Lighting");
            if (light != null) other.Add("Lighting: " + light);
            var power = get("Electrical");
            if (power != null) other.Add("Electrical: " + power);
            if (other.Count > 0) Offer(item, 1, Notes, string.Join("; ", other));
            foreach (var l in fields.Values) item.Texts.AddRange(l);
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
            if (prefix.Length == 1 && m.Groups[2].Value.Length == 0 && "HLRT".IndexOf(prefix[0]) >= 0) return false; // R50, H900: dimensions
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
                if (missing(Material)) { var s = Spans(Vocabulary.Materials, t); if (s.Count > 0) item.Fields[Material] = Nice(string.Join(", ", s.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase))); }
                if (missing(Finish)) { var s = Spans(Vocabulary.Finishes, t); if (s.Count > 0) item.Fields[Finish] = Nice(string.Join(", ", s.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase))); }
                if (missing(Colour))
                {
                    var codes = ColourCode.Matches(t).Cast<Match>().Select(m => Regex.Replace(m.Value, @"\s+", " ")).ToList();
                    codes.AddRange(Vocabulary.Colours.Find(t).Select(c => Nice(c.Found)));
                    if (codes.Count > 0) item.Fields[Colour] = string.Join(", ", codes.Distinct());
                }
                if (missing(Size)) { var s = SizeInText.Matches(t).Cast<Match>().Select(m => m.Value.Trim()).Distinct().ToList(); if (s.Count > 0) item.Fields[Size] = string.Join(", ", s); }
                if (missing(Qty)) { var m = QtyInText.Match(t); if (m.Success) item.Fields[Qty] = m.Groups["n"].Value; }
                if (missing(Manufacturer)) { var m = MakerInText.Match(t); if (!m.Success) m = MakerWords.Match(t); if (m.Success) item.Fields[Manufacturer] = m.Groups["v"].Value.Trim(); }
                if (missing(Model)) { var m = ModelInText.Match(t); if (m.Success && m.Groups["v"].Value.Any(char.IsDigit)) item.Fields[Model] = m.Groups["v"].Value.Trim(); }
            }
            if (missing(Category))
            {
                string category = null;
                // what the item is comes from its name first; hinges and handles in its hardware are not the item
                string desc, hardware;
                item.Fields.TryGetValue(Description, out desc);
                item.Fields.TryGetValue(Hardware, out hardware);
                var named = new List<string>();
                if (!string.IsNullOrWhiteSpace(desc)) named.Add(desc);
                named.AddRange(texts.Where(t => string.IsNullOrEmpty(hardware) || hardware.IndexOf(t, StringComparison.Ordinal) < 0));
                foreach (var t in named) { var p = Vocabulary.Products.First(t); if (p != null) { category = Nice(p.Canonical); break; } }
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

        private static readonly Regex DrawingNumberLike = new Regex(@"^[A-Z0-9]+(?:[\-_.][A-Z0-9]+){3,}$", RegexOptions.CultureInvariant);

        private static bool IsDescriptive(DocBlock b, string t)
        {
            if (!LanguageGuess.HasWords(t) || t.Length > 140 || b.Lines.Count > 4) return false;
            if (TagOnly.IsMatch(t) || DimensionOnly.IsMatch(t)) return false;
            if (TextX.Core.Translation.TextSegmenter.IsCodeLike(t.Trim()) || DrawingNumberLike.IsMatch(t.Trim())) return false;   // "ABC-P10-JN-02-L09"
            if (t.TrimEnd().EndsWith(":", StringComparison.Ordinal) && t.Length <= 40) return false;                                  // a label ("Front:")
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
