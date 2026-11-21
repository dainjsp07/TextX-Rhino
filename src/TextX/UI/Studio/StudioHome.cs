using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using TextX.Core;
using TextX.Core.Documents;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Studio
{
    public sealed partial class StudioWindow
    {
        private StackLayout _history;
        private Label _historyEmpty;

        private Control BuildHome()
        {
            // drop area
            var dropTitle = Ui.Label(_scope, "Drop a PDF, a drawing or a picture here", 18f, true);
            var dropText = Ui.Label(_scope, "Translate documents keeping their layout, make product schedules from drawings, or read the text in a picture.", 13f, false, p => p.Muted, true);
            var open = new FlatButton("Open a file...", IconKind.File, ButtonKind.Primary, 38) { Backdrop = p => p.Surface };
            open.Click += (s, e) => OpenFileDialog();
            var paste = new FlatButton("Paste a picture", IconKind.Download, ButtonKind.Secondary, 38) { Backdrop = p => p.Surface, ToolTip = "Read the text of a picture or screenshot on the clipboard" };
            paste.Click += (s, e) => { ShowTab(ToolsTab); PastePicture(); };
            var dropBody = new TableLayout { Spacing = new Size(0, 10) };
            dropBody.Rows.Add(new TableRow(dropTitle));
            dropBody.Rows.Add(new TableRow(dropText));
            dropBody.Rows.Add(new TableRow(Ui.Row(10, open, paste, Ui.Spring())));
            var drop = new Card(dropBody, new Padding(28, 24), p => p.Surface, p => p.AccentLine, 14f);

            // what can be done
            var tiles = new TableLayout { Spacing = new Size(12, 12) };
            tiles.Rows.Add(new TableRow(
                new TableCell(Tile(IconKind.Translate, "Translate a PDF", "Any language, layout kept. Preview side by side, edit, then save as PDF, Word or text.", () => { if (_project != null) ShowTab(DocumentTab); else OpenFileDialog(); }), true),
                new TableCell(Tile(IconKind.Format, "Product schedule", "Codes, products, materials, finishes, sizes and title-block details from drawings, to Excel, PDF or Rhino.", () => { ShowTab(SchedulesTab); if (_project == null) OpenFileDialog(); }), true)));
            tiles.Rows.Add(new TableRow(
                new TableCell(Tile(IconKind.Eye, "Picture or screenshot", "Take a screenshot or open a picture: its text is read and translated.", () => { ShowTab(ToolsTab); ShowTool(0); }), true),
                new TableCell(Tile(IconKind.Text, "Speak or check writing", "Dictate and see the translation as you speak; fix spelling and grammar.", () => { ShowTab(ToolsTab); ShowTool(1); }), true)));

            // history
            _history = new StackLayout { Orientation = Orientation.Vertical, Spacing = 8, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            _historyEmpty = Ui.Label(_scope, "Documents you open appear here, with their translations and edits.", 12.5f, false, p => p.Muted);
            var historyHeader = Ui.Row(8, Ui.Section(_scope, "Recent documents"), Ui.Spring());

            var layout = new TableLayout { Spacing = new Size(0, 16), Padding = new Padding(24, 18, 24, 24) };
            layout.Rows.Add(new TableRow(drop));
            layout.Rows.Add(new TableRow(tiles));
            layout.Rows.Add(new TableRow(historyHeader));
            layout.Rows.Add(new TableRow(_historyEmpty));
            layout.Rows.Add(new TableRow(_history));
            layout.Rows.Add(new TableRow { ScaleHeight = true });
            var scroll = new Scrollable { Content = layout, Border = BorderType.None, ExpandContentWidth = true, ExpandContentHeight = false };
            return _scope.Bind(scroll, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control Tile(IconKind icon, string title, string text, Action click)
        {
            var button = new FlatButton(title, icon, ButtonKind.Ghost, 34) { Backdrop = p => p.Surface, FontPx = 14f };
            button.Click += (s, e) => click();
            var body = new TableLayout { Spacing = new Size(0, 4) };
            body.Rows.Add(new TableRow(button));
            body.Rows.Add(new TableRow(new Panel { Content = Ui.Label(_scope, text, 12.5f, false, p => p.Muted, true), Padding = new Padding(8, 0, 8, 4) }));
            return new Card(body, new Padding(10, 10), p => p.Surface, p => p.Border, 12f);
        }

        private void RefreshHistory()
        {
            if (_history == null) return;
            System.Collections.Generic.List<LibraryEntry> entries;
            try { entries = DocumentServices.Library.List(); }
            catch (Exception ex) { Diagnostics.Log("history: " + ex.Message); entries = new System.Collections.Generic.List<LibraryEntry>(); }
            _history.Items.Clear();
            _historyEmpty.Visible = entries.Count == 0;
            foreach (var entry in entries.Take(30)) _history.Items.Add(new StackLayoutItem(HistoryRow(entry), HorizontalAlignment.Stretch));
        }

        private Control HistoryRow(LibraryEntry entry)
        {
            Control thumb;
            try
            {
                thumb = File.Exists(entry.ThumbnailPath)
                    ? (Control)new ImageView { Image = new Bitmap(File.ReadAllBytes(entry.ThumbnailPath)), Size = new Size(44, 56) }
                    : new Panel { Size = new Size(44, 56) };
            }
            catch (Exception) { thumb = new Panel { Size = new Size(44, 56) }; }

            var name = Ui.Label(_scope, entry.Name, 13.5f, true);
            string langs = (string.IsNullOrEmpty(entry.SourceLang) ? "?" : Languages.EnglishName(entry.SourceLang)) + " → " + Languages.EnglishName(entry.TargetLang);
            int pct = entry.TotalBlocks == 0 ? 0 : (int)Math.Round(100.0 * entry.TranslatedBlocks / entry.TotalBlocks);
            string state = entry.TotalBlocks == 0 ? "not read yet" : entry.IsTranslated ? "translated" : pct > 0 ? pct + "% translated" : "not translated";
            var details = Ui.Label(_scope, entry.PageCount + (entry.PageCount == 1 ? " page" : " pages") + "  •  " + langs + "  •  " + state + "  •  " +
                entry.Modified.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), 12f, false, p => p.Muted);
            var text = new TableLayout { Spacing = new Size(0, 2) };
            text.Rows.Add(new TableRow(name));
            text.Rows.Add(new TableRow(details));

            var open = new FlatButton("Open", IconKind.None, ButtonKind.Secondary, 30) { Backdrop = p => p.Surface };
            open.Click += (s, e) => OpenFromHistory(entry);
            var remove = new FlatButton(string.Empty, IconKind.Close, ButtonKind.Ghost, 30) { Backdrop = p => p.Surface, ToolTip = "Remove from the history (deletes the stored copy, translations and schedules)" };
            remove.Click += (s, e) =>
            {
                var answer = MessageBox.Show(this, "Remove \"" + entry.Name + "\" from the history?\n\nIts stored copy, translations and schedules are deleted. The original file is not touched.",
                    "Text X Studio", MessageBoxButtons.YesNo, MessageBoxType.Question);
                if (answer != DialogResult.Yes) return;
                if (_project != null && _project.Id == entry.Id) CloseProject();
                try { DocumentServices.Library.Delete(entry.Id); }
                catch (Exception ex) { SetStatus("Could not remove it: " + ex.Message, StatusText.Level.Error); }
                RefreshHistory();
            };
            var row = new TableLayout { Spacing = new Size(12, 0) };
            row.Rows.Add(new TableRow(new TableCell(thumb, false), new TableCell(text, true), new TableCell(Ui.Row(6, open, remove), false)));
            return new Card(row, new Padding(10, 8), p => p.Surface, p => p.Border, 10f);
        }

        private void OpenFromHistory(LibraryEntry entry)
        {
            if (IsBusy) { SetStatus("Please wait for the current task to finish.", StatusText.Level.Warning); return; }
            DocProject project;
            try { project = DocumentServices.Library.Load(entry.Id); }
            catch (Exception ex) { SetStatus("Could not open it: " + ex.Message, StatusText.Level.Error); return; }
            if (project == null || !File.Exists(project.PdfPath)) { SetStatus("The stored copy of this document is missing.", StatusText.Level.Error); return; }
            SetProject(project);
            ShowTab(DocumentTab);
            if (project.Pages.Count < project.PageCount || project.Pages.Any(p => !p.Extracted)) ReadDocumentPages();
        }
    }
}
