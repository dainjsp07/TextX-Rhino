using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.UI;
using TextX.Core;
using TextX.Integration;

namespace TextX.UI
{
    /// <summary>Lists every translated annotation in the document, with select / edit / mode / remove tools.</summary>
    public sealed class ManageDialog : Form
    {
        private sealed class Row
        {
            public Guid Id;
            public bool? Checked { get; set; }
            public string Space { get; set; }
            public string Mode { get; set; }
            public string State { get; set; }
            public string Source { get; set; }
            public string Translation { get; set; }
            public string Language { get; set; }
        }

        private static ManageDialog _instance;
        private readonly uint _docSerial;
        private readonly ObservableCollection<Row> _rows = new ObservableCollection<Row>();
        private readonly GridView _grid = new GridView { AllowMultipleSelection = true };
        private readonly Label _summary = new Label();
        private readonly DropDown _mode = new DropDown();

        public static void ShowFor(RhinoDoc doc, Control owner)
        {
            if (doc == null) return;
            if (_instance != null)
            {
                _instance.Close();
            }
            _instance = new ManageDialog(doc);
            _instance.Owner = owner as Window ?? RhinoEtoApp.MainWindow;
            _instance.Show();
        }

        private ManageDialog(RhinoDoc doc)
        {
            _docSerial = doc.RuntimeSerialNumber;
            Title = "Translated objects";
            Resizable = true;
            ShowInTaskbar = false;
            MinimumSize = new Size(720, 420);
            ClientSize = new Size(900, 520);
            Padding = new Padding(8);
            this.UseRhinoStyle();
            Theming.Icons.ApplyWindowIcon(this);

            _grid.Columns.Add(new GridColumn { HeaderText = "", DataCell = new CheckBoxCell { Binding = Binding.Property<Row, bool?>(r => r.Checked) }, Editable = true, Width = 28 });
            _grid.Columns.Add(new GridColumn { HeaderText = "Where", DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.Space) }, Width = 110 });
            _grid.Columns.Add(new GridColumn { HeaderText = "Display", DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.Mode) }, Width = 150 });
            _grid.Columns.Add(new GridColumn { HeaderText = "Now showing", DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.State) }, Width = 90 });
            _grid.Columns.Add(new GridColumn { HeaderText = "Lang", DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.Language) }, Width = 60 });
            _grid.Columns.Add(new GridColumn { HeaderText = "Text", DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.Source) }, Expand = true });
            _grid.Columns.Add(new GridColumn { HeaderText = "Translation", DataCell = new TextBoxCell { Binding = Binding.Property<Row, string>(r => r.Translation) }, Expand = true });
            _grid.DataStore = _rows;

            _mode.Items.Add("English on screen, translation in print");
            _mode.Items.Add("Translation on screen and in print");
            _mode.Items.Add("English on screen and in print");
            _mode.SelectedIndex = 0;

            var refresh = new Button { Text = "Refresh" };
            refresh.Click += (s, e) => Reload();
            var all = new Button { Text = "Check all" };
            all.Click += (s, e) => { foreach (var r in _rows) r.Checked = true; _grid.ReloadData(Enumerable.Range(0, _rows.Count)); };
            var select = new Button { Text = "Select in Rhino" };
            select.Click += (s, e) => SelectInRhino();
            var edit = new Button { Text = "Edit in Text X" };
            edit.Click += (s, e) => EditInTranslator();
            var setMode = new Button { Text = "Set display" };
            setMode.Click += (s, e) => SetMode();
            var remove = new Button { Text = "Remove translation" };
            remove.Click += (s, e) => RemoveTranslation();
            var close = new Button { Text = "Close" };
            close.Click += (s, e) => Close();

            Content = new TableLayout
            {
                Spacing = new Size(6, 6),
                Rows =
                {
                    new TableRow(_summary),
                    new TableRow(_grid) { ScaleHeight = true },
                    new TableRow(TableLayout.Horizontal(6, refresh, all, select, edit, null)),
                    new TableRow(TableLayout.Horizontal(6, _mode, setMode, remove, null, close)),
                },
            };
            Reload();
        }

        private RhinoDoc Doc
        {
            get
            {
                var doc = RhinoDoc.FromRuntimeSerialNumber(_docSerial);
                return TextXWindow.IsUsable(doc) ? doc : null;
            }
        }

        private static string ModeText(TranslationDisplayMode m)
        {
            switch (m)
            {
                case TranslationDisplayMode.Translated: return "Translation everywhere";
                case TranslationDisplayMode.Source: return "Source everywhere";
                default: return "Source / print translation";
            }
        }

        private void Reload()
        {
            _rows.Clear();
            var doc = Doc;
            if (doc == null) return;
            var pages = doc.Views.GetPageViews().ToDictionary(p => p.MainViewport.Id, p => p.PageName);
            int dual = 0;
            foreach (var r in PrintSwapManager.EnumerateRecords(doc))
            {
                var a = r.Object.Attributes;
                string space = "Model";
                string page;
                if (a.Space == ActiveSpace.PageSpace && pages.TryGetValue(a.ViewportId, out page)) space = "Layout: " + page;
                if (r.Record.NeedsPrintSwap) dual++;
                _rows.Add(new Row
                {
                    Id = r.Object.Id,
                    Checked = false,
                    Space = space,
                    Mode = ModeText(r.Record.Mode),
                    State = r.Record.State == TextState.Print ? "print text" : "screen text",
                    Language = r.Record.SourceLang + ">" + r.Record.TargetLang,
                    Source = OneLine(r.Record.Source),
                    Translation = OneLine(r.Record.Translation) + (r.Record.ManuallyEdited ? "  (edited)" : string.Empty),
                });
            }
            _summary.Text = _rows.Count + " translated annotation(s), " + dual + " switch to the translation when printing." +
                            (PrintSwapManager.Instance.IsSwapped(doc) ? "  Print text is currently shown." : string.Empty);
        }

        private static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\r", string.Empty).Replace("\n", " | ");
        }

        private List<Guid> TargetIds()
        {
            var ids = _rows.Where(r => r.Checked == true).Select(r => r.Id).ToList();
            if (ids.Count == 0) ids = _grid.SelectedItems.OfType<Row>().Select(r => r.Id).ToList();
            return ids;
        }

        private void SelectInRhino()
        {
            var doc = Doc;
            if (doc == null) return;
            doc.Objects.UnselectAll();
            foreach (var id in TargetIds()) doc.Objects.Select(id, true, true, true);
            doc.Views.Redraw();
        }

        private void EditInTranslator()
        {
            var doc = Doc;
            if (doc == null) return;
            var objs = TargetIds().Select(id => doc.Objects.FindId(id)).Where(o => o != null).ToList();
            if (objs.Count == 0) return;
            TextXWindow.ShowWindow(doc).LoadObjects(doc, objs);
        }

        private void SetMode()
        {
            var doc = Doc;
            if (doc == null) return;
            var ids = TargetIds();
            if (ids.Count == 0) return;
            var mode = (TranslationDisplayMode)Math.Max(0, _mode.SelectedIndex);
            var cfg = RenderConfig.FromSettings(TextXPlugIn.Instance.Options);
            var undo = doc.BeginUndoRecord("Text X: set display");
            try
            {
                foreach (var id in ids)
                {
                    var obj = doc.Objects.FindId(id);
                    var rec = obj == null ? null : AnnotationEngine.ReadRecord(obj.Attributes);
                    if (rec == null) continue;
                    var updated = rec.Clone();
                    updated.Mode = mode;
                    string error;
                    AnnotationUpdater.Apply(doc, obj, updated, new TextFormat(), FormatFields.None, cfg, out error);
                }
            }
            finally
            {
                doc.EndUndoRecord(undo);
            }
            doc.Views.Redraw();
            Reload();
        }

        private void RemoveTranslation()
        {
            var doc = Doc;
            if (doc == null) return;
            var ids = TargetIds();
            if (ids.Count == 0) return;
            var answer = MessageBox.Show(this, "Remove translation data from " + ids.Count + " object(s)?\n\nYes = keep the English (source) text\nNo = keep the translation as the text", "Remove translation", MessageBoxButtons.YesNoCancel, MessageBoxType.Question);
            if (answer == DialogResult.Cancel) return;
            var cfg = RenderConfig.FromSettings(TextXPlugIn.Instance.Options);
            var undo = doc.BeginUndoRecord("Text X: remove translation");
            try
            {
                foreach (var id in ids)
                {
                    var obj = doc.Objects.FindId(id);
                    if (obj != null) AnnotationUpdater.RemoveTranslation(doc, obj, answer == DialogResult.No, cfg);
                }
            }
            finally
            {
                doc.EndUndoRecord(undo);
            }
            doc.Views.Redraw();
            Reload();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_instance == this) _instance = null;
            base.OnClosed(e);
        }
    }
}
