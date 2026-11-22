using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using TextX.Core;
using TextX.Core.Documents;
using TextX.Core.Motion.Figures;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Studio
{
    public sealed partial class StudioWindow
    {
        private StackLayout _recent;
        private Label _recentEmpty;
        private FlatButton _recentManage;
        private Panel _homeHost, _homePage;
        private Control _heroCard, _tileGrid, _tileButtons;
        private BentoTile _featuredTile;
        private string _homeArrangement;

        /// <summary>Width of the page from which the tiles sit beside the translator.</summary>
        private static int HomeWideFrom = 1060;

        /// <summary>Width of the column of tiles beside the translator.</summary>
        private const int SideWidth = 400;

        /// <summary>Space around Home's content: the same right margin as the header, room under the Recent row.</summary>
        private static readonly Padding HomePadding = new Padding(4, 0, 16, 12);

        private const int RecentRowHeight = 28, TileRowHeight = 170;

        private Control BuildHome()
        {
            // the translator: what the Text X window did, now the heart of Home
            _heroCard = BuildTranslator();

            // what else Studio does: each tile has a line figure that answers the pointer and says what it shows
            var translate = new BentoTile(IconKind.Translate, "Translate a PDF", "Layout kept. Compare, edit, save as PDF, Word or text.", "Documents", new PagesFigure()) { Featured = true, Height = -1, MinimumSize = new Size(0, 156) };
            translate.Click += (s, e) => OpenDocumentTile();
            var schedule = new BentoTile(IconKind.Grid, "Product schedule", "Items, materials and finishes from your drawings.", "Drawings", new ScheduleFigure()) { Compact = true };
            schedule.Click += (s, e) => OpenSchedulesTile();
            var picture = new BentoTile(IconKind.Picture, "Picture or screenshot", "Its text is read on this computer and translated.", "Pictures", new ReadFigure()) { Compact = true };
            picture.Click += (s, e) => ShowTab(PictureTab);
            var voice = new BentoTile(IconKind.Mic, "Voice typing", "Speak into the text; the translation follows.", "Voice", new VoiceFigure()) { Compact = true };
            voice.Click += (s, e) => ToggleVoiceTyping();
            _library = new BentoTile(IconKind.Layers, "Library", "Documents you open, with their translations.", "History", new LibraryFigure()) { Compact = true };
            _library.Click += (s, e) => OpenLatest();
            _tiles = new List<BentoTile> { translate, schedule, picture, voice, _library };
            _featuredTile = translate;

            _tileGrid = null;

            // a window too short for the tiles under the translator: the same five, as buttons
            var bPdf = new FlatButton("Translate a PDF", IconKind.Translate, ButtonKind.Secondary, 32);
            bPdf.Click += (s, e) => OpenDocumentTile();
            var bSchedule = new FlatButton("Product schedule", IconKind.Grid, ButtonKind.Secondary, 32);
            bSchedule.Click += (s, e) => OpenSchedulesTile();
            var bPicture = new FlatButton("Picture", IconKind.Picture, ButtonKind.Secondary, 32) { ToolTip = "Picture or screenshot" };
            bPicture.Click += (s, e) => ShowTab(PictureTab);
            var bLibrary = new FlatButton("Library", IconKind.Layers, ButtonKind.Secondary, 32);
            bLibrary.Click += (s, e) => OpenLatest();
            _tileButtons = Ui.Row(8, bPdf, bSchedule, bPicture, bLibrary, Ui.Spring());

            // documents opened before, as chips
            _recent = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, VerticalContentAlignment = VerticalAlignment.Center };
            _recentEmpty = Ui.Label(_scope, "Documents you open appear here, with their translations and edits.", 12f, false, p => p.Muted);
            _recentEmpty.Wrap = WrapMode.None;
            _recentManage = new FlatButton("Manage", IconKind.None, ButtonKind.Ghost, 28) { ToolTip = "Remove documents from the history", Visible = false };
            _recentManage.Click += (s, e) => ShowRecentMenu(_recentManage);
            var recent = new TableLayout { Spacing = new Size(12, 0) };
            // the chips get the room there is (they are clipped, never pushing the page wider)
            recent.Rows.Add(new TableRow(new TableCell(Ui.Section(_scope, "Recent"), false), new TableCell(Ui.Row(8, _recentEmpty, _recent), true), new TableCell(_recentManage, false)));

            _homeHost = new Panel();
            var layout = new TableLayout { Spacing = new Size(0, 12), Padding = HomePadding };
            layout.Rows.Add(new TableRow(_homeHost) { ScaleHeight = true });
            layout.Rows.Add(new TableRow(recent));
            // no scrolling: the arrangement follows the page's size, so nothing is ever cut off or pushed aside
            _homePage = _scope.Bind(new Panel { Content = layout }, (x, p) => x.BackgroundColor = p.Window);
            _homePage.SizeChanged += (s, e) => ArrangeHome();
            ArrangeHome();
            return _homePage;
        }

        private void OpenDocumentTile()
        {
            if (_project != null) ShowTab(DocumentTab); else OpenFileDialog();
        }

        private void OpenSchedulesTile()
        {
            ShowTab(SchedulesTab);
            if (_project == null) OpenFileDialog();
        }

        /// <summary>
        /// Wide pages: the translator on the left, Translate a PDF and the four tiles in a column on the right.
        /// Narrower pages: the tiles go under the translator, or, on a short page, become a row of buttons.
        /// Every part gets an exact width, so the page never needs more room than the window has.
        /// </summary>
        private void ArrangeHome()
        {
            if (_homeHost == null) return;
            int pageW = _homePage != null && _homePage.Width > 0 ? _homePage.Width : ClientSize.Width - _tabs.Width;
            int pageH = _homePage != null && _homePage.Height > 0 ? _homePage.Height : ClientSize.Height - 80;
            int w = pageW - HomePadding.Horizontal;
            int h = pageH - HomePadding.Vertical - RecentRowHeight - 12;
            if (w <= 0) return;
            int sideHero = w - SideWidth - 12;
            string arrangement = w >= HomeWideFrom && sideHero >= TranslatorMinWidth ? "wide"
                : h >= HeroMinHeight + 12 + TileRowHeight ? "narrow" : "short";
            int heroW = arrangement == "wide" ? sideHero : w;
            FitTranslator(heroW);
            _heroCard.Width = heroW;
            if (_homeArrangement == arrangement) return;
            _homeArrangement = arrangement;
            foreach (var c in new Control[] { _heroCard, _tileButtons }.Concat(_tiles)) if (c.Parent != null) c.Detach();
            _featuredTile.DrawCompact = arrangement == "narrow";
            var layout = new TableLayout { Spacing = new Size(12, 12) };
            if (arrangement == "wide")
            {
                // Translate a PDF over the four other tiles, two by two
                var grid = new TableLayout { Spacing = new Size(12, 12) };
                grid.Rows.Add(new TableRow(new TableCell(_tiles[1], true), new TableCell(_tiles[2], true)) { ScaleHeight = true });
                grid.Rows.Add(new TableRow(new TableCell(_tiles[3], true), new TableCell(_tiles[4], true)) { ScaleHeight = true });
                _tileGrid = grid;
                var side = new TableLayout { Spacing = new Size(0, 12) };
                side.Rows.Add(new TableRow(_featuredTile) { ScaleHeight = true });
                side.Rows.Add(new TableRow(grid) { ScaleHeight = true });
                var sideBox = new Panel { Content = side, Width = SideWidth };
                layout.Rows.Add(new TableRow(new TableCell(_heroCard, true), new TableCell(sideBox, false)) { ScaleHeight = true });
            }
            else if (arrangement == "narrow")
            {
                // the five tiles in one row under the translator
                var row = new TableLayout { Spacing = new Size(12, 0) };
                row.Rows.Add(new TableRow(_tiles.Select(t => new TableCell(t, true)).ToArray()));
                var rowBox = new Panel { Content = row, Height = TileRowHeight };
                layout.Rows.Add(new TableRow(_heroCard) { ScaleHeight = true });
                layout.Rows.Add(new TableRow(rowBox));
            }
            else
            {
                layout.Rows.Add(new TableRow(_heroCard) { ScaleHeight = true });
                layout.Rows.Add(new TableRow(_tileButtons));
            }
            _homeHost.Content = layout;
        }

        private List<BentoTile> _tiles = new List<BentoTile>();
        private BentoTile _library;
        private bool _toured;

        /// <summary>The first time Home is shown, each figure walks through what it does once, one after another.</summary>
        private void TourTiles()
        {
            if (_toured) return;
            _toured = true;
            for (int i = 0; i < _tiles.Count; i++) _tiles[i].PlayOnce(500 + i * 900);
        }

        private void OpenLatest()
        {
            List<LibraryEntry> entries;
            try { entries = DocumentServices.Library.List(); }
            catch (Exception) { entries = new List<LibraryEntry>(); }
            if (entries.Count == 0) { OpenFileDialog(); return; }
            OpenFromHistory(entries[0]);
        }

        private void RefreshHistory()
        {
            if (_recent == null) return;
            List<LibraryEntry> entries;
            try { entries = DocumentServices.Library.List(); }
            catch (Exception ex) { Diagnostics.Log("history: " + ex.Message); entries = new List<LibraryEntry>(); }
            _recent.Items.Clear();
            _recentEmpty.Visible = entries.Count == 0;
            _recentManage.Visible = entries.Count > 0;
            if (_library != null)
                _library.Text = entries.Count == 0 ? "Documents you open, with their translations."
                    : entries.Count + (entries.Count == 1 ? " document" : " documents") + ", with translations. Opens the latest.";
            foreach (var entry in entries.Take(5)) _recent.Items.Add(RecentChip(entry));
        }

        private Control RecentChip(LibraryEntry entry)
        {
            int pct = entry.TotalBlocks == 0 ? 0 : (int)Math.Round(100.0 * entry.TranslatedBlocks / entry.TotalBlocks);
            string state = entry.TotalBlocks == 0 ? "not read yet" : entry.IsTranslated ? "translated" : pct > 0 ? pct + "% translated" : "not translated";
            string langs = (string.IsNullOrEmpty(entry.SourceLang) ? "?" : Languages.EnglishName(entry.SourceLang)) + " → " + Languages.EnglishName(entry.TargetLang);
            var name = entry.Name.Length > 28 ? entry.Name.Substring(0, 26) + "…" : entry.Name;
            var chip = new FlatButton(ShownName(name) + "  ·  " + state, IconKind.File, ButtonKind.Secondary, 28)
            {
                ToolTip = entry.Name + "\n" + entry.PageCount + (entry.PageCount == 1 ? " page" : " pages") + "   ·   " + langs + "   ·   " + entry.Modified.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            };
            chip.Click += (s, e) => OpenFromHistory(entry);
            return chip;
        }

        private void ShowRecentMenu(Control anchor)
        {
            List<LibraryEntry> entries;
            try { entries = DocumentServices.Library.List(); }
            catch (Exception) { entries = new List<LibraryEntry>(); }
            var menu = new ContextMenu();
            foreach (var entry in entries.Take(30))
            {
                var captured = entry;
                var mi = new ButtonMenuItem { Text = "Remove \"" + entry.Name.Replace("&", "&&") + "\"..." };
                mi.Click += (s, e) => RemoveFromHistory(captured);
                menu.Items.Add(mi);
            }
            if (entries.Count > 0) menu.Show(anchor);
        }

        private void RemoveFromHistory(LibraryEntry entry)
        {
            var answer = MessageBox.Show(this, "Remove \"" + entry.Name + "\" from the history?\n\nIts stored copy, translations and schedules are deleted. The original file is not touched.",
                "Text X Studio", MessageBoxButtons.YesNo, MessageBoxType.Question);
            if (answer != DialogResult.Yes) return;
            if (_project != null && _project.Id == entry.Id) CloseProject();
            try { DocumentServices.Library.Delete(entry.Id); }
            catch (Exception ex) { SetStatus("Could not remove it: " + ex.Message, StatusText.Level.Error); }
            RefreshHistory();
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
