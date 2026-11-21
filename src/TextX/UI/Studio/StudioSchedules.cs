using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using TextX.Core;
using TextX.Core.Documents;
using TextX.Core.Documents.Schedules;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;

namespace TextX.UI.Studio
{
    public sealed partial class StudioWindow
    {
        private readonly DropDown _scheduleList = new DropDown { Width = 230 };
        private readonly SearchBox _scheduleSearch = new SearchBox { PlaceholderText = "Search", Width = 220 };
        private readonly GridView _scheduleGrid = new GridView { AllowMultipleSelection = true, ShowHeader = true, GridLines = GridLines.Both };
        private readonly ToggleCheck _fromTranslation = new ToggleCheck("Read the translation", 12.5f);
        private Label _scheduleInfo, _scheduleEmpty;
        private int _sortColumn = -1;
        private bool _sortDescending;
        private bool _scheduleSuppress;

        /// <summary>The schedule shown in the window (for TRPlaceSchedule).</summary>
        public Schedule CurrentSchedule
        {
            get
            {
                var set = _schedules;
                int i = _scheduleList.SelectedIndex;
                return set != null && i >= 0 && i < set.Schedules.Count ? set.Schedules[i] : null;
            }
        }

        public string CurrentDocumentName { get { return _project == null ? string.Empty : _project.Name; } }

        private Control BuildSchedules()
        {
            _scheduleInfo = Ui.Label(_scope, string.Empty, 12f, false, p => p.Muted);
            _scheduleEmpty = Ui.Label(_scope, "Open a drawing or a specification (PDF), then click Read schedules.\nProduct codes, names, materials, finishes, colours, sizes, quantities and title-block details are listed here.", 13f, false, p => p.Muted, true);

            var read = new FlatButton("Read schedules", IconKind.Refresh, ButtonKind.Primary, 32) { ToolTip = "Read the document again into schedules (edited rows are kept)" };
            read.Click += (s, e) => ReadSchedules();
            var add = new FlatButton("Add row", IconKind.Plus, ButtonKind.Secondary, 32);
            add.Click += (s, e) => AddScheduleRow();
            var delete = new FlatButton("Delete", IconKind.Close, ButtonKind.Secondary, 32) { ToolTip = "Delete the selected rows" };
            delete.Click += (s, e) => DeleteScheduleRows();
            var show = new FlatButton("Show on page", IconKind.Eye, ButtonKind.Secondary, 32) { ToolTip = "Show where the selected row was found" };
            show.Click += (s, e) => ShowScheduleRowOnPage();
            var excel = new FlatButton("Excel", IconKind.Download, ButtonKind.Secondary, 32) { ToolTip = "Save all schedules as an Excel workbook (one sheet each)" };
            excel.Click += (s, e) => ExportSchedulesExcel();
            var pdf = new FlatButton("PDF", IconKind.Print, ButtonKind.Secondary, 32) { ToolTip = "Save printable schedule sheets" };
            pdf.Click += (s, e) => ExportSchedulesPdf();
            var csv = new FlatButton("CSV", IconKind.Download, ButtonKind.Ghost, 32) { ToolTip = "Save this schedule as comma separated values" };
            csv.Click += (s, e) => ExportScheduleCsv();
            var place = new FlatButton("Place in Rhino", IconKind.Target, ButtonKind.Primary, 32) { ToolTip = "Draw this schedule as a table in the model or a layout (TRPlaceSchedule)" };
            place.Click += (s, e) => PlaceSchedule();

            _scheduleList.SelectedIndexChanged += (s, e) => { if (!_scheduleSuppress) { _sortColumn = -1; ShowSchedule(); } };
            _scheduleSearch.TextChanged += (s, e) => FillScheduleGrid();
            _fromTranslation.Checked = Settings.SchedulesFromTranslation;
            _fromTranslation.CheckedChanged += (s, e) => Settings.SchedulesFromTranslation = _fromTranslation.Checked;
            _scheduleGrid.ColumnHeaderClick += (s, e) =>
            {
                var schedule = CurrentSchedule;
                if (schedule == null) return;
                int col = _scheduleGrid.Columns.IndexOf(e.Column);
                _sortDescending = col == _sortColumn && !_sortDescending;
                _sortColumn = col;
                schedule.Sort(col, _sortDescending);
                FillScheduleGrid();
                SaveSchedules();
            };
            _scheduleGrid.CellEdited += (s, e) => SaveSchedules();
            _scheduleGrid.CellDoubleClick += (s, e) => { if (e.Column < 0) ShowScheduleRowOnPage(); };

            var top = Ui.Row(8, read, _fromTranslation, Ui.Spring(), _scheduleList, _scheduleSearch);
            var bottom = Ui.Row(8, add, delete, show, Ui.Spring(), csv, excel, pdf, place);
            var layout = new TableLayout { Spacing = new Size(0, 10), Padding = new Padding(16, 12, 16, 12) };
            layout.Rows.Add(new TableRow(top));
            layout.Rows.Add(new TableRow(_scheduleInfo));
            layout.Rows.Add(new TableRow(_scheduleEmpty));
            layout.Rows.Add(new TableRow(_scheduleGrid) { ScaleHeight = true });
            layout.Rows.Add(new TableRow(bottom));
            return _scope.Bind(new Panel { Content = layout }, (x, p) => x.BackgroundColor = p.Window);
        }

        private void RefreshSchedules()
        {
            if (_scheduleInfo == null) return;
            var set = _schedules;
            _scheduleSuppress = true;
            int keep = _scheduleList.SelectedIndex;
            _scheduleList.Items.Clear();
            if (set != null)
                foreach (var s in set.Schedules) _scheduleList.Items.Add(s.Title + "  (" + s.Rows.Count + ")");
            _scheduleList.SelectedIndex = set == null || set.Schedules.Count == 0 ? -1 : Math.Max(0, Math.Min(keep, set.Schedules.Count - 1));
            _scheduleSuppress = false;
            _scheduleEmpty.Visible = set == null;
            _scheduleGrid.Visible = set != null;
            if (_project != null && set == null) _scheduleInfo.Text = _project.Name + ": click Read schedules.";
            ShowSchedule();
        }

        private void ShowSchedule()
        {
            var schedule = CurrentSchedule;
            _scheduleGrid.Columns.Clear();
            if (schedule == null) { _scheduleGrid.DataStore = new List<ScheduleRow>(); if (_schedules == null && _project == null) _scheduleInfo.Text = string.Empty; return; }
            for (int i = 0; i < schedule.Columns.Count; i++)
            {
                int c = i;
                var name = schedule.Columns[c];
                bool narrow = name == ScheduleExtractor.Qty || name == ScheduleExtractor.Tagged || name == ScheduleExtractor.PageColumn || name == "No." || name == "Mentions";
                bool wide = name == ScheduleExtractor.Description || name == ScheduleExtractor.Notes || name == "Note" || name == "Value" || name == "Example";
                _scheduleGrid.Columns.Add(new GridColumn
                {
                    HeaderText = name,
                    DataCell = new TextBoxCell
                    {
                        Binding = Binding.Delegate<ScheduleRow, string>(r => r[c], (r, v) => { if (r[c] != (v ?? string.Empty)) { r[c] = v; r.Edited = true; } }),
                    },
                    Editable = true,
                    Resizable = true,
                    Sortable = true,
                    Width = narrow ? 60 : wide ? 260 : 130,
                });
            }
            FillScheduleGrid();
        }

        private void FillScheduleGrid()
        {
            var schedule = CurrentSchedule;
            if (schedule == null) return;
            var rows = schedule.Search(_scheduleSearch.Text).ToList();
            _scheduleGrid.DataStore = rows;
            _scheduleInfo.Text = (_project == null ? string.Empty : _project.Name + "  •  ") + schedule.Title + ": " +
                                 (rows.Count == schedule.Rows.Count ? rows.Count + (rows.Count == 1 ? " row" : " rows") : rows.Count + " of " + schedule.Rows.Count + " rows") +
                                 "  •  click a cell to edit, a heading to sort";
        }

        private async void ReadSchedules()
        {
            var project = _project;
            if (project == null) { OpenFileDialog(); return; }
            bool useTranslation = _fromTranslation.Checked && project.TranslatedBlocks > 0;
            ScheduleSet result = null;
            var ok = await RunBusy("Reading schedules...", async ct =>
            {
                if (project.Pages.Any(p => !p.Extracted) || project.Pages.Count < project.PageCount)
                    await Task.Run(() => DocumentServices.ReadPagesAsync(project, StatusProgress(), null, ct), ct);
                result = await Task.Run(() => ScheduleExtractor.Extract(project, new ExtractOptions { UseTranslation = useTranslation }), ct);
            });
            if (!ok || result == null || _project != project) return;
            // rows the user edited or added are kept
            if (_schedules != null)
            {
                foreach (var old in _schedules.Schedules)
                {
                    var target = result.Schedules.FirstOrDefault(s => s.Kind == old.Kind && s.Title == old.Title);
                    var edited = old.Rows.Where(r => r.Edited).ToList();
                    if (edited.Count == 0) continue;
                    if (target == null) { result.Schedules.Add(old); continue; }
                    int key = 0;
                    foreach (var row in edited)
                    {
                        var match = row[key].Length > 0 ? target.Rows.FirstOrDefault(r => r[key] == row[key]) : null;
                        if (match != null) target.Rows[target.Rows.IndexOf(match)] = row;
                        else target.Rows.Add(row);
                    }
                }
            }
            _schedules = result;
            SaveSchedules();
            RefreshSchedules();
            var products = result.Find(ScheduleKind.Products);
            var materials = result.Find(ScheduleKind.Materials);
            SetStatus("Found " + (products == null ? 0 : products.Rows.Count) + " products and items, " + (materials == null ? 0 : materials.Rows.Count) + " materials and finishes" +
                      (useTranslation ? " (read from the translation)." : "."), StatusText.Level.Success);
        }

        private void AddScheduleRow()
        {
            var schedule = CurrentSchedule;
            if (schedule == null) { SetStatus("Read the schedules first.", StatusText.Level.Warning); return; }
            var row = schedule.Add(-1, new string[schedule.Columns.Count]);
            row.Edited = true;
            _scheduleSearch.Text = string.Empty;
            FillScheduleGrid();
            _scheduleGrid.SelectedRow = schedule.Rows.Count - 1;
            _scheduleGrid.ScrollToRow(schedule.Rows.Count - 1);
            _scheduleGrid.BeginEdit(schedule.Rows.Count - 1, 0);
            SaveSchedules();
        }

        private void DeleteScheduleRows()
        {
            var schedule = CurrentSchedule;
            var selected = _scheduleGrid.SelectedItems.OfType<ScheduleRow>().ToList();
            if (schedule == null || selected.Count == 0) { SetStatus("Select the rows to delete first.", StatusText.Level.Warning); return; }
            foreach (var r in selected) schedule.Rows.Remove(r);
            FillScheduleGrid();
            SaveSchedules();
            RefreshScheduleTitles();
            SetStatus(selected.Count + (selected.Count == 1 ? " row deleted." : " rows deleted."));
        }

        private void RefreshScheduleTitles()
        {
            var set = _schedules;
            if (set == null) return;
            _scheduleSuppress = true;
            int i = _scheduleList.SelectedIndex;
            for (int k = 0; k < set.Schedules.Count && k < _scheduleList.Items.Count; k++)
                _scheduleList.Items[k].Text = set.Schedules[k].Title + "  (" + set.Schedules[k].Rows.Count + ")";
            _scheduleList.SelectedIndex = i;
            _scheduleSuppress = false;
        }

        private void ShowScheduleRowOnPage()
        {
            var row = _scheduleGrid.SelectedItem as ScheduleRow;
            var project = _project;
            if (row == null || project == null) return;
            DocBlock block = row.Blocks.Select(id => project.FindBlock(id)).FirstOrDefault(b => b != null);
            if (block == null && row.Page < 0) { SetStatus("This row was added by hand.", StatusText.Level.Info); return; }
            ShowTab(DocumentTab);
            if (block != null) SelectBlock(block, true);
            else GoToPage(row.Page);
        }

        // ------------------------------------------------------------------ storage
        private static string SchedulesPath(DocProject project)
        {
            return Path.Combine(DocumentServices.Library.FolderOf(project.Id), "schedules.json");
        }

        private static ScheduleSet LoadSchedules(DocProject project)
        {
            if (project == null) return null;
            try
            {
                var path = SchedulesPath(project);
                return File.Exists(path) ? ScheduleSet.FromJson(File.ReadAllText(path, Encoding.UTF8)) : null;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("load schedules: " + ex.Message);
                return null;
            }
        }

        private void SaveSchedules()
        {
            var project = _project;
            var set = _schedules;
            if (project == null || set == null) return;
            try
            {
                var path = SchedulesPath(project);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, set.ToJson(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Diagnostics.Log("save schedules: " + ex.Message); }
        }

        // ------------------------------------------------------------------ export
        private bool HasSchedules()
        {
            if (_schedules != null && _schedules.Schedules.Count > 0) return true;
            SetStatus("Read the schedules first.", StatusText.Level.Warning);
            return false;
        }

        private void ExportSchedulesExcel()
        {
            if (!HasSchedules()) return;
            var path = AskSavePath("Save the schedules as an Excel workbook", SafeFileName(CurrentDocumentName + " schedules") + ".xlsx", "Excel workbook", ".xlsx");
            if (path == null) return;
            try
            {
                XlsxWriter.Write(_schedules.Schedules.Where(s => s.Rows.Count > 0).ToList(), path, CurrentDocumentName);
                SetStatus("Saved " + Path.GetFileName(path) + ".", StatusText.Level.Success);
                DocumentServices.OpenFile(path);
            }
            catch (IOException ex) { SetStatus("Could not save (is the file open in Excel?): " + ex.Message, StatusText.Level.Error); }
            catch (Exception ex) { SetStatus("Could not save: " + ex.Message, StatusText.Level.Error); }
        }

        private void ExportScheduleCsv()
        {
            var schedule = CurrentSchedule;
            if (schedule == null) { SetStatus("Read the schedules first.", StatusText.Level.Warning); return; }
            var path = AskSavePath("Save this schedule as CSV", SafeFileName(CurrentDocumentName + " " + schedule.Title) + ".csv", "CSV file", ".csv");
            if (path == null) return;
            try
            {
                // Excel expects the list separator of the computer's region (';' where ',' is the decimal mark)
                var sep = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
                CsvWriter.Write(schedule, path, string.IsNullOrEmpty(sep) ? ',' : sep[0]);
                SetStatus("Saved " + Path.GetFileName(path) + ".", StatusText.Level.Success);
            }
            catch (Exception ex) { SetStatus("Could not save: " + ex.Message, StatusText.Level.Error); }
        }

        private async void ExportSchedulesPdf()
        {
            if (!HasSchedules()) return;
            var path = AskSavePath("Save printable schedules", SafeFileName(CurrentDocumentName + " schedules") + ".pdf", "PDF document", ".pdf");
            if (path == null) return;
            var set = _schedules;
            var name = CurrentDocumentName;
            var ok = await RunBusy("Writing the schedules...", async ct =>
            {
                await Task.Run(() => SchedulePdfWriter.Write(set.Schedules.Where(s => s.Rows.Count > 0 && s.Kind != ScheduleKind.ProjectInfo).ToList(), path, DocumentServices.Fonts, name, set.Find(ScheduleKind.ProjectInfo)), ct);
            });
            if (!ok) return;
            SetStatus("Saved " + Path.GetFileName(path) + ".", StatusText.Level.Success);
            DocumentServices.OpenFile(path);
        }

        private void PlaceSchedule()
        {
            var schedule = CurrentSchedule;
            if (schedule == null || schedule.Rows.Count == 0) { SetStatus("Read the schedules first (or choose one with rows).", StatusText.Level.Warning); return; }
            RunRhinoCommand("_TRPlaceSchedule");
        }

        /// <summary>Called by TRPlaceSchedule after the table was drawn.</summary>
        public void OnSchedulePlaced(int objects)
        {
            SetStatus("Schedule placed in Rhino (" + objects + " objects, grouped).", StatusText.Level.Success);
        }
    }
}
