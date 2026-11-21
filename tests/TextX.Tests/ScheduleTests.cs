using System;
using System.Collections.Generic;
using System.Linq;
using TextX.Core.Documents;
using TextX.Core.Documents.Schedules;
using static TextX.Tests.TestRunner;

namespace TextX.Tests
{
    public static partial class CoreTests
    {
        /// <summary>An A3 joinery drawing (French) and a specification page.</summary>
        internal static DocProject ScheduleSample()
        {
            var drawing = new FakePage()
                .Line("PLAN DE TRAVAIL QUARTZ", 400, 710, 9)
                .Line("PLACAGE CHÊNE", 330, 518, 9).Line("POIGNÉE INOX", 620, 518, 9)
                .Line("P-01", 340, 478, 10, bold: true).Line("T-02", 640, 478, 10, bold: true)
                .Line("P-01", 180, 400, 10, bold: true)
                .Line("2400", 490, 278, 9)
                .Line("P-01 Placage chêne, vernis mat", 60, 220, 9)
                .Line("T-02 Poignée inox brossé L 160 mm, qté 4", 60, 206, 9)
                .Line("NOTES", 60, 160, 10, bold: true)
                .Line("1. Toutes les cotes sont en mm.", 60, 146, 9)
                .Line("2. Ne pas mesurer sur le plan.", 60, 132, 9)
                .Line("Projet", 860, 180, 8, bold: true).Line("Résidence Les Pins", 960, 180, 9)
                .Line("Architecte", 860, 160, 8, bold: true).Line("Atelier Moreau", 960, 160, 9)
                .Line("Échelle", 860, 140, 8, bold: true).Line("1:20", 960, 140, 9)
                .Analyze(1191, 842);
            var spec = new FakePage()
                .Line("Kitchen specification", 72, 780, 20, bold: true)
                .Line("Client: Famille Martin", 72, 750, 10)
                .Line("Date", 72, 730, 10, bold: true).Line("12/03/2025", 160, 730, 10)
                .Line("Code", 72, 680, 10, bold: true).Line("Description", 160, 680, 10, bold: true).Line("Finish", 330, 680, 10, bold: true).Line("Qty", 470, 680, 10, bold: true)
                .Line("D-01", 72, 664, 10).Line("Tall door", 160, 664, 10).Line("Matt lacquer RAL 9016", 330, 664, 10).Line("2", 470, 664, 10)
                .Line("D-02", 72, 648, 10).Line("Base drawer", 160, 648, 10).Line("Oak veneer, oiled", 330, 648, 10).Line("6", 470, 648, 10)
                .Line("The carcasses are melamine faced particleboard, 18 mm thick.", 72, 600, 10)
                .Analyze();
            spec.Index = 1;
            foreach (var b in spec.Blocks) b.Page = 1;
            var project = new DocProject { Name = "Kitchen", PageCount = 2, SourceLang = "fr", TargetLang = "en" };
            project.Pages.Add(drawing);
            project.Pages.Add(spec);
            return project;
        }

        public static void Schedules()
        {
            True(ScheduleExtractor.IsCode("P-01") && ScheduleExtractor.IsCode("D101") && ScheduleExtractor.IsCode("FF.03"), "codes recognised");
            True(!ScheduleExtractor.IsCode("H900") && !ScheduleExtractor.IsCode("A3") && !ScheduleExtractor.IsCode("RAL9010") && !ScheduleExtractor.IsCode("EN13501"), "not codes");
            Equal(ScheduleExtractor.CodeKey("P-01"), ScheduleExtractor.CodeKey("P1"), "P-01 and P1 are the same code");
            True(Vocabulary.Materials.Contains("Façades en CHÊNE massif") && Vocabulary.Fold("Größe Chêne") == "grosse chene", "accent and case insensitive words");
            Equal("veneer oak", string.Join(" ", Vocabulary.Materials.Find("plaqué chêne").Select(m => m.Canonical)), "material names");

            var project = ScheduleSample();
            var set = ScheduleExtractor.Extract(project, new ExtractOptions { Dimensions = true });
            var products = set.Find(ScheduleKind.Products);
            Func<string, int> col = products.Column;
            Func<string, ScheduleRow> byCode = c => products.Rows.FirstOrDefault(r => r[col("Code")] == c);
            var dump = string.Join("\n", products.Rows.Select(r => string.Join(" | ", r.Cells)));

            var p01 = byCode("P-01");
            True(p01 != null, "P-01 found:\n" + dump);
            if (p01 == null) return;
            Equal("Placage chêne, vernis mat", p01[col("Description")], "legend gives the description");
            True(p01[col("Material")].StartsWith("Placage chêne", StringComparison.OrdinalIgnoreCase), "material: " + p01[col("Material")]);
            True(p01[col("Finish")].IndexOf("vernis mat", StringComparison.OrdinalIgnoreCase) >= 0, "finish: " + p01[col("Finish")]);
            Equal("2", p01[col("Tagged")], "tagged twice on the drawing");
            True(p01[col("Notes")].Contains("PLACAGE CHÊNE"), "drawing label kept in notes: " + p01[col("Notes")]);

            var t02 = byCode("T-02");
            True(t02 != null && t02[col("Category")] == "Handle" && t02[col("Material")] == "Inox" && t02[col("Size")].Contains("160 mm") && t02[col("Qty")] == "4",
                "T-02 read: " + (t02 == null ? "-" : string.Join(" | ", t02.Cells)));

            var worktop = products.Rows.FirstOrDefault(r => r[col("Description")] == "PLAN DE TRAVAIL QUARTZ");
            True(worktop != null && worktop[col("Code")] == "" && worktop[col("Category")] == "Worktop" && worktop[col("Material")] == "Quartz", "untagged callout becomes an item");

            var d01 = byCode("D-01");
            True(d01 != null && d01[col("Description")] == "Tall door" && d01[col("Finish")] == "Matt lacquer RAL 9016" && d01[col("Qty")] == "2" && d01[col("Colour")] == "RAL 9016" && d01[col("Category")] == "Door",
                "table row read: " + (d01 == null ? "-" : string.Join(" | ", d01.Cells)));
            var d02 = byCode("D-02");
            True(d02 != null && d02[col("Material")] == "Oak veneer" && d02[col("Finish")] == "Oak veneer, oiled" && d02[col("Page")] == "2", "second table row: " + (d02 == null ? "-" : string.Join(" | ", d02.Cells)));
            True(products.Rows.IndexOf(byCode("D-01")) < products.Rows.IndexOf(byCode("P-01")), "codes sorted");
            True(!products.Rows.Any(r => r[col("Code")] == "A3" || r[col("Description")] == "2400" || r[col("Description")].StartsWith("Atelier")), "no title-block or dimension rows");

            var info = set.Find(ScheduleKind.ProjectInfo);
            Func<string, string> field = f => info.Rows.Where(r => r[0] == f).Select(r => r[1]).FirstOrDefault();
            Equal("Résidence Les Pins", field("Project"), "project from the title block");
            Equal("Atelier Moreau", field("Architect"), "architect");
            Equal("1:20", field("Scale"), "scale");
            Equal("Famille Martin", field("Client"), "client from \"Client: ...\"");
            Equal("12/03/2025", field("Date"), "date beside its label");

            var notes = set.Find(ScheduleKind.Notes);
            True(notes != null && notes.Rows.Count == 2 && notes.Rows[0][1] == "Toutes les cotes sont en mm.", "notes: " + (notes == null ? "-" : string.Join(" / ", notes.Rows.Select(r => r[0] + " " + r[1]))));
            var dims = set.Find(ScheduleKind.Dimensions);
            True(dims != null && dims.Rows.Any(r => r[0] == "2400"), "dimension found");

            var materials = set.Find(ScheduleKind.Materials);
            var names = materials.Rows.Select(r => r[1] + ":" + r[0]).ToList();
            True(materials.Rows.Any(r => r[1] == "Material" && Vocabulary.Fold(r[0]) == "particleboard") || materials.Rows.Any(r => Vocabulary.Fold(r[0]).Contains("particleboard")), "particleboard from the specification text: " + string.Join(", ", names));
            True(materials.Rows.Any(r => r[1] == "Colour" && r[0] == "RAL 9016"), "colour code listed");
            var oakRow = materials.Rows.FirstOrDefault(r => Vocabulary.Fold(r[0]) == "oak veneer");
            True(oakRow != null && oakRow[2].Contains("D-02"), "material linked to the item using it: " + (oakRow == null ? string.Join(", ", names) : oakRow[2]));

            // search, sort and save
            Equal(1, products.Search("quartz").Count(), "search");
            products.Sort(col("Code"), true);
            Equal("T-02", products.Rows[0][col("Code")], "sort descending");
            var again = ScheduleSet.FromJson(set.ToJson());
            Equal(set.Schedules.Count, again.Schedules.Count, "schedules saved and read back");
            Equal(products.Rows[0][col("Description")], again.Find(ScheduleKind.Products).Rows[0][col("Description")], "rows saved");
            True(again.Find(ScheduleKind.Products).Rows[0].HasBox, "position saved");

            // names in title blocks are not translated
            var nameBlocks = DocumentTranslator.NameValues(project);
            True(nameBlocks.Any(b => b.Source == "Atelier Moreau") && !nameBlocks.Any(b => b.Source == "Résidence Les Pins" || b.Source == "1:20"), "architect's name kept: " + string.Join(", ", nameBlocks.Select(b => b.Source)));

            // exports
            Equal("A", XlsxWriter.ColumnName(0), "column A");
            Equal("Z", XlsxWriter.ColumnName(25), "column Z");
            Equal("AA", XlsxWriter.ColumnName(26), "column AA");
            Equal("AAA", XlsxWriter.ColumnName(702), "column AAA");
            Equal("\"a,b\"", CsvWriter.Quote("a,b", ','), "csv quoting");
            Equal("'=SUM(A1)", CsvWriter.Quote("=SUM(A1)", ','), "csv formula guarded");
            Equal("-5", CsvWriter.Quote("-5", ','), "negative number kept");
            var sheets = XlsxWriter.SheetNames(new[] { new Schedule { Title = "Products: [main]" }, new Schedule { Title = "Products: [main]" } });
            True(sheets[0] == "Products   main" && sheets[1] == "Products   main (2)", "sheet names: " + string.Join(" | ", sheets));

            var dir = System.IO.Path.Combine(Environment.GetEnvironmentVariable("TEXTX_TEST_OUT") ?? System.IO.Path.GetTempPath(), "schedules");
            System.IO.Directory.CreateDirectory(dir);
            var xlsx = System.IO.Path.Combine(dir, "unit-schedules.xlsx");
            XlsxWriter.Write(set.Schedules, xlsx, "Kitchen");
            using (var zip = System.IO.Compression.ZipFile.OpenRead(xlsx))
            {
                foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
                    System.Xml.Linq.XDocument.Parse(new System.IO.StreamReader(e.Open()).ReadToEnd());
                var sheet1 = new System.IO.StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml").Open()).ReadToEnd();
                True(sheet1.Contains("Placage chêne, vernis mat") && sheet1.Contains("<autoFilter") && sheet1.Contains("state=\"frozen\""), "workbook sheet content");
                Equal(set.Schedules.Count, zip.Entries.Count(e => e.FullName.StartsWith("xl/worksheets/")), "one sheet per schedule");
            }
            var csv = System.IO.Path.Combine(dir, "unit-products.csv");
            CsvWriter.Write(set.Find(ScheduleKind.Products), csv, ';');
            var csvText = System.IO.File.ReadAllText(csv);
            True(csvText.StartsWith("Code;Category;Description") && csvText.Contains("Placage chêne, vernis mat"), "csv content");
        }
    }
}
