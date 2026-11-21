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
            // hero: what Studio does, in two lines, and the two ways in
            var kicker = Ui.Label(_scope, Theme.Spaced("Text X Studio"), 10.5f, true, p => p.Accent);
            var line1 = new Label { Text = "Translate anything.", Font = Theme.DisplayFont(34f) };
            _scope.Bind(line1, (x, p) => x.TextColor = p.OnHero);
            var line2 = new Label { Text = "Keep every layout.", Font = Theme.DisplayFont(34f) };
            _scope.Bind(line2, (x, p) => x.TextColor = p.Accent);
            var heroText = Ui.Label(_scope, "Drop a PDF, a drawing or a picture anywhere on this window. Documents keep their layout, drawings become product schedules, pictures become text. No account, nothing to set up.",
                13f, false, p => ThemeColors.WithAlpha(p.OnHero, 0.72f), true);
            var open = new FlatButton("Open a file", IconKind.Plus, ButtonKind.Primary, 40) { Backdrop = p => p.Hero };
            open.Click += (s, e) => OpenFileDialog();
            var paste = new FlatButton("Paste a picture", IconKind.Picture, ButtonKind.Secondary, 40) { Backdrop = p => p.Hero, ToolTip = "Read the text of a picture or screenshot on the clipboard" };
            paste.Click += (s, e) => { ShowTab(ToolsTab); PastePicture(); };
            var heroBody = new TableLayout { Spacing = new Size(0, 6) };
            heroBody.Rows.Add(new TableRow(kicker));
            heroBody.Rows.Add(new TableRow(line1));
            heroBody.Rows.Add(new TableRow(line2));
            heroBody.Rows.Add(new TableRow(new Panel { Height = 4 }));
            heroBody.Rows.Add(new TableRow(heroText));
            heroBody.Rows.Add(new TableRow(new Panel { Height = 8 }));
            heroBody.Rows.Add(new TableRow(Ui.Row(10, open, paste, Ui.Spring())));
            heroBody.Rows.Add(new TableRow { ScaleHeight = true });
            heroText.Width = 400;
            var heroRow = new TableLayout { Spacing = new Size(16, 0) };
            heroRow.Rows.Add(new TableRow(new TableCell(heroBody, false), new TableCell(null, true)) { ScaleHeight = true });
            var hero = new Card(heroRow, new Padding(30, 26, 22, 24), p => p.Hero, p => p.IsDark ? p.Border : p.Hero, 26f) { Height = 352 };
            // the picture sits in the free space at the right (drawn behind the text)
            hero.Paint += (s, e) =>
            {
                try { HeroArt.Draw(e.Graphics, new RectangleF(0, 0, hero.Width, hero.Height), 26f, new RectangleF(450, 0, Math.Max(0, hero.Width - 450), hero.Height)); }
                catch (Exception ex) { Diagnostics.Log("hero art: " + ex.Message); }
            };
            hero.SizeChanged += (s, e) => hero.Invalidate();

            // what can be done
            var translate = new BentoTile(IconKind.Translate, "Translate a PDF", "Any language, layout kept. Compare side by side, edit, save as PDF, Word or text.", "Documents") { Featured = true };
            translate.Click += (s, e) => { if (_project != null) ShowTab(DocumentTab); else OpenFileDialog(); };
            var schedule = new BentoTile(IconKind.Grid, "Product schedule", "Codes, products, materials, finishes and title blocks from drawings, to Excel, PDF or Rhino.", "Drawings");
            schedule.Click += (s, e) => { ShowTab(SchedulesTab); if (_project == null) OpenFileDialog(); };
            var picture = new BentoTile(IconKind.Picture, "Picture or screenshot", "Take a screenshot or open a picture: its text is read and translated.", "Pictures");
            picture.Click += (s, e) => { ShowTab(ToolsTab); ShowTool(0); };
            var speak = new BentoTile(IconKind.Mic, "Speak or check writing", "Dictate and see the translation as you speak; fix spelling and grammar.", "Words");
            speak.Click += (s, e) => { ShowTab(ToolsTab); ShowTool(1); };

            var side = new TableLayout { Spacing = new Size(0, 16) };
            side.Rows.Add(new TableRow(translate));
            side.Rows.Add(new TableRow(schedule));
            var top = new TableLayout { Spacing = new Size(16, 0) };
            top.Rows.Add(new TableRow(new TableCell(hero, true), new TableCell(new Panel { Content = side, Width = 300 }, false)));
            var second = new TableLayout { Spacing = new Size(16, 0) };
            second.Rows.Add(new TableRow(new TableCell(picture, true), new TableCell(speak, true), new TableCell(BuildHistoryCard(), true)));

            var layout = new TableLayout { Spacing = new Size(0, 16), Padding = new Padding(10, 6, 24, 16) };
            layout.Rows.Add(new TableRow(top));
            layout.Rows.Add(new TableRow(second));
            layout.Rows.Add(new TableRow(_historyList));
            layout.Rows.Add(new TableRow { ScaleHeight = true });
            var scroll = new Scrollable { Content = layout, Border = BorderType.None, ExpandContentWidth = true, ExpandContentHeight = false };
            return _scope.Bind(scroll, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control _historyList;
        private Label _historyCount;

        /// <summary>The "recent" tile (a count and the last document) and, below the tiles, the list itself.</summary>
        private Control BuildHistoryCard()
        {
            _history = new StackLayout { Orientation = Orientation.Vertical, Spacing = 8, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            _historyEmpty = Ui.Label(_scope, "Documents you open appear here, with their translations and edits.", 12.5f, false, p => p.Muted, true);
            var listHeader = Ui.Row(8, Ui.Section(_scope, "Recent documents"), Ui.Spring());
            var list = new TableLayout { Spacing = new Size(0, 10) };
            list.Rows.Add(new TableRow(listHeader));
            list.Rows.Add(new TableRow(_historyEmpty));
            list.Rows.Add(new TableRow(_history));
            _historyList = list;

            _historyCount = new Label { Text = "0", Font = Theme.DisplayFont(44f) };
            _scope.Bind(_historyCount, (x, p) => x.TextColor = p.Text);
            var caption = Ui.Label(_scope, "documents in your history", 12.5f, false, p => p.Muted);
            var body = new TableLayout { Spacing = new Size(0, 2) };
            body.Rows.Add(new TableRow(Ui.Section(_scope, "Library")));
            body.Rows.Add(new TableRow { ScaleHeight = true });
            body.Rows.Add(new TableRow(_historyCount));
            body.Rows.Add(new TableRow(caption));
            return new Card(body, new Padding(20, 18), p => p.Surface, p => p.Border, 22f) { Height = 168 };
        }

        private void RefreshHistory()
        {
            if (_history == null) return;
            System.Collections.Generic.List<LibraryEntry> entries;
            try { entries = DocumentServices.Library.List(); }
            catch (Exception ex) { Diagnostics.Log("history: " + ex.Message); entries = new System.Collections.Generic.List<LibraryEntry>(); }
            _history.Items.Clear();
            _historyEmpty.Visible = entries.Count == 0;
            if (_historyCount != null) _historyCount.Text = entries.Count.ToString(CultureInfo.CurrentCulture);
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

            var name = Ui.Label(_scope, entry.Name, 14f, true);
            string langs = (string.IsNullOrEmpty(entry.SourceLang) ? "?" : Languages.EnglishName(entry.SourceLang)) + " → " + Languages.EnglishName(entry.TargetLang);
            int pct = entry.TotalBlocks == 0 ? 0 : (int)Math.Round(100.0 * entry.TranslatedBlocks / entry.TotalBlocks);
            string state = entry.TotalBlocks == 0 ? "not read yet" : entry.IsTranslated ? "translated" : pct > 0 ? pct + "% translated" : "not translated";
            var details = Ui.Label(_scope, entry.PageCount + (entry.PageCount == 1 ? " page" : " pages") + "   ·   " + langs + "   ·   " +
                entry.Modified.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), 12f, false, p => p.Muted);
            var text = new TableLayout { Spacing = new Size(0, 2) };
            text.Rows.Add(new TableRow(name));
            text.Rows.Add(new TableRow(details));

            var badge = new Pill(state, p => entry.IsTranslated ? p.SuccessSoft : p.IconBox, p => entry.IsTranslated ? p.SuccessText : p.Subtle) { Backdrop = p => p.Surface };
            var open = new FlatButton(string.Empty, IconKind.ArrowUpRight, ButtonKind.Strong, 34) { Backdrop = p => p.Surface, ToolTip = "Open" };
            open.Click += (s, e) => OpenFromHistory(entry);
            var remove = new FlatButton(string.Empty, IconKind.Close, ButtonKind.Ghost, 34) { Backdrop = p => p.Surface, ToolTip = "Remove from the history (deletes the stored copy, translations and schedules)" };
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
            var row = new TableLayout { Spacing = new Size(14, 0) };
            row.Rows.Add(new TableRow(new TableCell(thumb, false), new TableCell(text, true), new TableCell(Ui.Row(8, badge, remove, open), false)));
            return new Card(row, new Padding(12, 10, 12, 10), p => p.Surface, p => p.Border, 20f);
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
