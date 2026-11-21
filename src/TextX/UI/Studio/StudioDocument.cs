using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using TextX.Core;
using TextX.Core.Documents;
using TextX.Core.Documents.Export;
using TextX.Core.Documents.Imaging;
using TextX.Core.Translation;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Studio
{
    public sealed partial class StudioWindow
    {
        private readonly PageView _originalView = new PageView { Caption = "Original" };
        private readonly PageView _translatedView = new PageView { Caption = "Translation" };
        private readonly SegmentedControl _viewMode = new SegmentedControl("Side by side", "Translation", "Original");
        private readonly GridView _blocks = new GridView { AllowMultipleSelection = false, ShowHeader = true, GridLines = GridLines.Horizontal };
        private Label _docTitle, _docInfo, _pageLabel, _blocksTitle;
        private TextArea _editOriginal, _editTranslation;
        private DocBlock _editing;
        private Pill _checkPill;
        private FlatButton _translate;
        private TableLayout _viewer;
        private Panel _viewerHost;
        private int _pageIndex;
        private int _renderId;
        private readonly Dictionary<DocBlock, List<QualityIssue>> _issues = new Dictionary<DocBlock, List<QualityIssue>>();
        private readonly Dictionary<int, byte[]> _originalCache = new Dictionary<int, byte[]>();

        private Control BuildDocument()
        {
            _docTitle = Ui.Label(_scope, "No document open", 15f, true);
            _docInfo = Ui.Label(_scope, "Open a PDF to translate it.", 12f, false, p => p.Muted);
            _checkPill = new Pill(string.Empty, p => p.WarningSoft, p => p.Text) { Visible = false, Backdrop = p => p.Window };

            _translate = new FlatButton("Translate", IconKind.Translate, ButtonKind.Primary, 34) { ToolTip = "Translate the whole document into the language chosen at the top" };
            _translate.Click += (s, e) => TranslateDocument(false);
            var export = new FlatButton("Save as", IconKind.Download, ButtonKind.Secondary, 34) { ToolTip = "Save the translation as PDF (same layout), Word or text" };
            export.Click += (s, e) => ShowExportMenu(export);
            var summary = new FlatButton("Summary", IconKind.Info, ButtonKind.Secondary, 34) { ToolTip = "Key points, contents and figures of the document" };
            summary.Click += (s, e) => ShowSummary();
            var check = new FlatButton("Check", IconKind.Check, ButtonKind.Secondary, 34) { ToolTip = "Look for changed numbers, missing text, glossary terms and more" };
            check.Click += (s, e) => ShowCheckMenu(check);
            var schedule = new FlatButton("Schedule", IconKind.Format, ButtonKind.Secondary, 34) { ToolTip = "Read product codes, materials and finishes into schedules" };
            schedule.Click += (s, e) => { ShowTab(SchedulesTab); if (_schedules == null) ReadSchedules(); };

            var titleBlock = new TableLayout { Spacing = new Size(0, 2) };
            titleBlock.Rows.Add(new TableRow(Ui.Row(8, _docTitle, _checkPill)));
            titleBlock.Rows.Add(new TableRow(_docInfo));
            var toolbar = new TableLayout { Spacing = new Size(8, 0) };
            toolbar.Rows.Add(new TableRow(new TableCell(titleBlock, true), new TableCell(Ui.Row(8, _translate, export, summary, check, schedule), false)));

            // page navigation and view mode
            var prev = new FlatButton("Previous", IconKind.None, ButtonKind.Secondary, 28) { ToolTip = "Previous page (Page Up)" };
            prev.Click += (s, e) => GoToPage(_pageIndex - 1);
            var next = new FlatButton("Next", IconKind.None, ButtonKind.Secondary, 28) { ToolTip = "Next page (Page Down)" };
            next.Click += (s, e) => GoToPage(_pageIndex + 1);
            _pageLabel = Ui.Label(_scope, string.Empty, 12.5f);
            _viewMode.Width = 320;
            _viewMode.SelectedIndexChanged += (s, e) => LayoutViewer();
            var nav = Ui.Row(8, prev, _pageLabel, next, Ui.Spring(), _viewMode);

            _viewer = new TableLayout { Spacing = new Size(10, 0) };
            _viewerHost = new Panel { Content = _viewer };
            _scope.Bind(_viewerHost, (x, p) => x.BackgroundColor = p.Window);
            _originalView.BlockClicked += (s, b) => SelectBlock(b, true);
            _translatedView.BlockClicked += (s, b) => SelectBlock(b, true);
            LayoutViewer();

            var left = new TableLayout { Spacing = new Size(0, 10) };
            left.Rows.Add(new TableRow(nav));
            left.Rows.Add(new TableRow(_viewerHost) { ScaleHeight = true });

            var body = new TableLayout { Spacing = new Size(14, 0) };
            body.Rows.Add(new TableRow(new TableCell(left, true), new TableCell(BuildBlockPanel(), false)) { ScaleHeight = true });

            var layout = new TableLayout { Spacing = new Size(0, 12), Padding = new Padding(16, 12, 16, 12) };
            layout.Rows.Add(new TableRow(toolbar));
            layout.Rows.Add(new TableRow(body) { ScaleHeight = true });
            KeyDown += (s, e) =>
            {
                if (_tabs.SelectedIndex != DocumentTab) return;
                if (e.Key == Keys.PageDown) { GoToPage(_pageIndex + 1); e.Handled = true; }
                else if (e.Key == Keys.PageUp) { GoToPage(_pageIndex - 1); e.Handled = true; }
            };
            return _scope.Bind(new Panel { Content = layout }, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control BuildBlockPanel()
        {
            _blocksTitle = Ui.Section(_scope, "Text on this page");
            _blocks.Columns.Add(new GridColumn
            {
                HeaderText = "Original",
                DataCell = new TextBoxCell { Binding = Binding.Delegate<DocBlock, string>(b => Short(b.Source)) },
                Editable = false, Resizable = true, Width = 150,
            });
            _blocks.Columns.Add(new GridColumn
            {
                HeaderText = "Translation (click to edit)",
                DataCell = new TextBoxCell { Binding = Binding.Delegate<DocBlock, string>(b => b.Translation ?? (b.State == BlockState.Kept ? b.Source : string.Empty), (b, v) => EditBlock(b, v)) },
                Editable = true, Resizable = true, Width = 170,
            });
            _blocks.Columns.Add(new GridColumn
            {
                HeaderText = "Check",
                DataCell = new TextBoxCell { Binding = Binding.Delegate<DocBlock, string>(b => IssueText(b)) },
                Editable = false, Resizable = true, Width = 90,
            });
            _blocks.SelectionChanged += (s, e) =>
            {
                var b = _blocks.SelectedItem as DocBlock;
                if (b != null) SelectBlock(b, false);
            };
            _blocks.CellFormatting += (s, e) =>
            {
                var b = e.Item as DocBlock;
                if (b == null) return;
                if (e.Column.HeaderText == "Check" && _issues.ContainsKey(b)) e.ForegroundColor = Theme.Current.Warning;
                if (b.State == BlockState.Edited && e.Column.HeaderText.StartsWith("Translation", StringComparison.Ordinal)) e.ForegroundColor = Theme.Current.AccentInk;
            };

            // editor for the selected paragraph
            _editOriginal = Ui.TextArea(_scope, 12.5f);
            _editOriginal.ReadOnly = true;
            _editOriginal.Height = 58;
            _editTranslation = Ui.TextArea(_scope, 13f);
            _editTranslation.Height = 76;
            var save = new FlatButton("Save change", IconKind.Check, ButtonKind.Primary, 28) { ToolTip = "Use your text for this paragraph (also for identical paragraphs)" };
            save.Click += (s, e) => SaveEditor();
            var again = new FlatButton("Translate again", IconKind.Refresh, ButtonKind.Secondary, 28) { ToolTip = "Translate the selected paragraph again (your edit is replaced)" };
            again.Click += (s, e) => RetranslateSelected();
            var keep = new FlatButton("Keep original", IconKind.Undo, ButtonKind.Secondary, 28) { ToolTip = "Leave the selected text untranslated" };
            keep.Click += (s, e) => KeepSelected();
            var term = new FlatButton("Glossary", IconKind.Plus, ButtonKind.Ghost, 28) { ToolTip = "Always translate a word or phrase your way" };
            term.Click += (s, e) => AddGlossaryTerm();

            var panel = new TableLayout { Spacing = new Size(0, 6) };
            panel.Rows.Add(new TableRow(_blocksTitle));
            panel.Rows.Add(new TableRow(_blocks) { ScaleHeight = true });
            panel.Rows.Add(new TableRow(Ui.Section(_scope, "Original")));
            panel.Rows.Add(new TableRow(Ui.FieldBox(_editOriginal)));
            panel.Rows.Add(new TableRow(Ui.Section(_scope, "Translation")));
            panel.Rows.Add(new TableRow(Ui.FieldBox(_editTranslation)));
            panel.Rows.Add(new TableRow(Ui.Row(6, save, again, keep, Ui.Spring(), term)));
            return new Panel { Content = panel, Width = 420 };
        }

        private static string Short(string s)
        {
            s = (s ?? string.Empty).Replace('\n', ' ');
            return s.Length <= 300 ? s : s.Substring(0, 297) + "…";
        }

        private string IssueText(DocBlock b)
        {
            List<QualityIssue> list;
            if (b.State == BlockState.Failed) return "Not translated (service error)";
            if (!_issues.TryGetValue(b, out list) || list.Count == 0) return string.Empty;
            return string.Join("; ", list.Select(i => i.Message));
        }

        private void LayoutViewer()
        {
            if (_viewer == null) return;
            var row = new TableRow { ScaleHeight = true };
            int mode = Math.Max(0, _viewMode.SelectedIndex);
            _viewer = new TableLayout { Spacing = new Size(10, 0) };
            _originalView.Visible = mode != 1;
            _translatedView.Visible = mode != 2;
            if (mode == 0) { row.Cells.Add(new TableCell(_originalView, true)); row.Cells.Add(new TableCell(_translatedView, true)); }
            else if (mode == 1) row.Cells.Add(new TableCell(_translatedView, true));
            else row.Cells.Add(new TableCell(_originalView, true));
            _viewer.Rows.Add(row);
            _viewerHost.Content = _viewer;
        }

        // ------------------------------------------------------------------ project
        private void OpenDocument(string path)
        {
            if (IsBusy) { SetStatus("Please wait for the current task to finish.", StatusText.Level.Warning); return; }
            var problem = DocumentServices.CheckPdfEngine();
            if (problem != null) { SetStatus("The PDF engine could not start: " + problem, StatusText.Level.Error); return; }
            DocProject project;
            try { project = DocumentServices.Library.Add(path, TargetLang); }
            catch (Exception ex) { SetStatus("Could not open the PDF: " + ex.Message, StatusText.Level.Error); return; }
            if (string.IsNullOrEmpty(project.SourceLangSetting) || project.Pages.Count == 0) project.SourceLangSetting = SourceLang;
            SetProject(project);
            ShowTab(DocumentTab);
            ReadDocumentPages();
        }

        private void SetProject(DocProject project)
        {
            SaveProject();
            _project = project;
            _schedules = LoadSchedules(project);
            _issues.Clear();
            _originalCache.Clear();
            _pageIndex = 0;
            if (project != null && project.IsTranslated) RunQualityChecks(false);
            RefreshDocument();
        }

        private void CloseProject()
        {
            _project = null;
            _schedules = null;
            _issues.Clear();
            _originalCache.Clear();
            RefreshDocument();
            RefreshSchedules();
        }

        private void SaveProject()
        {
            var p = _project;
            if (p == null) return;
            try { DocumentServices.Library.Save(p); }
            catch (Exception ex) { Diagnostics.Log("save project: " + ex.Message); }
        }

        private async void ReadDocumentPages()
        {
            var project = _project;
            if (project == null) return;
            await RunBusy("Reading the document...", async ct =>
            {
                var pages = new Progress<int>(n => { SetProgress(n, project.PageCount); if (n == 1 || n == _pageIndex + 1) OnUi(RefreshDocument); });
                await Task.Run(() => DocumentServices.ReadPagesAsync(project, StatusProgress(), pages, ct), ct);
                DocumentServices.SaveThumbnail(project);
                SaveProject();
            });
            if (_project != project) return;
            RefreshDocument();
            int scanned = project.Pages.Count(p => p.Scanned);
            if (project.Pages.All(p => p.Extracted))
                SetStatus(project.PageCount + (project.PageCount == 1 ? " page read" : " pages read") + (scanned > 0 ? " (" + scanned + " scanned, read with OCR)" : string.Empty) +
                          ". Click Translate, or Schedule for a product list.", StatusText.Level.Success);
        }

        private async void TranslateDocument(bool again)
        {
            var project = _project;
            if (project == null) { OpenFileDialog(); return; }
            bool targetChanged = project.TranslatedBlocks > 0 && project.TargetLang != TargetLang;
            if (targetChanged)
            {
                int edits = project.AllBlocks.Count(b => b.State == BlockState.Edited);
                if (edits > 0 && MessageBox.Show(this, "The document is translated into " + Languages.EnglishName(project.TargetLang) + ". Translate it into " + Languages.EnglishName(TargetLang) +
                        " instead?\n\nYour " + edits + " edited paragraph(s) will be translated again.", "Text X Studio", MessageBoxButtons.YesNo, MessageBoxType.Question) != DialogResult.Yes) return;
                foreach (var b in project.AllBlocks.Where(b => b.State != BlockState.Kept || b.Translation != null)) { b.Translation = null; b.State = BlockState.Pending; b.Note = null; }
            }
            project.TargetLang = TargetLang;
            if (SourceLang != Languages.AutoDetect) { project.SourceLangSetting = SourceLang; project.SourceLang = SourceLang; }
            else if (project.SourceLangSetting != Languages.AutoDetect) { project.SourceLangSetting = Languages.AutoDetect; project.SourceLang = string.Empty; }
            if (project.SourceLang == project.TargetLang && !string.IsNullOrEmpty(project.SourceLang))
            {
                SetStatus("The document is already in " + Languages.EnglishName(project.TargetLang) + ". Choose another language at the top.", StatusText.Level.Warning);
                return;
            }
            var ok = await RunBusy("Translating...", async ct =>
            {
                if (project.Pages.Any(p => !p.Extracted) || project.Pages.Count < project.PageCount)
                    await Task.Run(() => DocumentServices.ReadPagesAsync(project, StatusProgress(), null, ct), ct);
                var translator = new DocumentTranslator(TranslationServices.CreateDocumentEngine()) { Checkpoint = SaveCheckpoint };
                var progress = new Progress<TranslateProgress>(p =>
                {
                    SetProgress(p.Done, p.Total);
                    var eta = p.Remaining.HasValue && p.Remaining.Value.TotalSeconds > 5 ? "  (about " + Duration(p.Remaining.Value) + " left)" : string.Empty;
                    SetStatus((string.IsNullOrEmpty(p.Status) ? "Translating" : p.Status) + eta);
                });
                await Task.Run(() => translator.TranslateAsync(project, null, again, progress, ct), ct);
            });
            TranslationServices.SaveCache();
            SaveProject();
            if (_project != project) return;
            RunQualityChecks(false);
            RefreshDocument();
            if (ok)
            {
                int failed = project.AllBlocks.Count(b => b.State == BlockState.Failed);
                if (failed > 0) SetStatus(failed + " paragraph(s) could not be translated. Click Translate to try them again.", StatusText.Level.Warning);
                else SetStatus("Translated from " + Languages.EnglishName(project.SourceLang) + " into " + Languages.EnglishName(project.TargetLang) +
                               (_issues.Count > 0 ? ". " + _issues.Count + " paragraph(s) to check (marked in orange)." : ". Click Save as to keep it."), _issues.Count > 0 ? StatusText.Level.Warning : StatusText.Level.Success);
            }
        }

        private void SaveCheckpoint(DocProject p)
        {
            try { DocumentServices.Library.Save(p); } catch (Exception ex) { Diagnostics.Log("checkpoint: " + ex.Message); }
            if (p == _project) OnUi(() => { RenderPages(); RefreshBlockList(); });
        }

        private static string Duration(TimeSpan t)
        {
            if (t.TotalMinutes >= 1.5) return Math.Round(t.TotalMinutes) + " min";
            return Math.Max(5, (int)Math.Round(t.TotalSeconds / 5) * 5) + " s";
        }

        // ------------------------------------------------------------------ display
        private void RefreshDocument()
        {
            var project = _project;
            if (_docTitle == null) return;
            if (project == null)
            {
                _docTitle.Text = "No document open";
                _docInfo.Text = "Open a PDF to translate it, or drop it on the window.";
                _pageLabel.Text = string.Empty;
                _checkPill.Visible = false;
                _blocks.DataStore = new List<DocBlock>();
                _originalView.SetPlaceholder("Open a PDF to see it here");
                _translatedView.SetPlaceholder(string.Empty);
                return;
            }
            _docTitle.Text = project.Name;
            int scanned = project.Pages.Count(p => p.Scanned);
            string src = string.IsNullOrEmpty(project.SourceLang) ? "language detected when translating" : Languages.EnglishName(project.SourceLang);
            int pct = project.TotalBlocks == 0 ? 0 : (int)Math.Round(100.0 * project.TranslatedBlocks / project.TotalBlocks);
            _docInfo.Text = project.PageCount + (project.PageCount == 1 ? " page" : " pages") + (scanned > 0 ? " (" + scanned + " scanned)" : string.Empty) +
                            "  •  " + src + " → " + Languages.EnglishName(project.TargetLang) +
                            "  •  " + (project.IsTranslated ? "translated" : pct > 0 ? pct + "% translated" : "not translated yet");
            _translate.Text = project.TranslatedBlocks > 0 && project.TargetLang == TargetLang ? (project.IsTranslated ? "Translate again" : "Continue") : "Translate";
            _checkPill.Text = _issues.Count + " to check";
            _checkPill.Visible = _issues.Count > 0;
            _pageIndex = Math.Max(0, Math.Min(_pageIndex, Math.Max(0, project.PageCount - 1)));
            _pageLabel.Text = "Page " + (_pageIndex + 1) + " of " + project.PageCount;
            RefreshBlockList();
            RenderPages();
        }

        private void RefreshBlockList()
        {
            var page = CurrentPage;
            var selected = _blocks.SelectedItem as DocBlock;
            var list = page == null ? new List<DocBlock>() : page.InReadingOrder.ToList();
            _blocks.DataStore = list;
            if (selected != null && list.Contains(selected)) _blocks.SelectedRow = list.IndexOf(selected);
            _blocksTitle.Text = ("Text on page " + (_pageIndex + 1) + (page != null && page.Scanned ? " (read by OCR)" : string.Empty)).ToUpperInvariant();
            var flagged = new HashSet<DocBlock>(list.Where(b => _issues.ContainsKey(b) || b.State == BlockState.Failed));
            _translatedView.Flagged.Clear();
            foreach (var b in flagged) _translatedView.Flagged.Add(b);
        }

        private DocPage CurrentPage
        {
            get
            {
                var p = _project;
                return p != null && _pageIndex >= 0 && _pageIndex < p.Pages.Count ? p.Pages[_pageIndex] : null;
            }
        }

        private void GoToPage(int index)
        {
            var p = _project;
            if (p == null) return;
            index = Math.Max(0, Math.Min(p.PageCount - 1, index));
            if (index == _pageIndex) return;
            _pageIndex = index;
            _pageLabel.Text = "Page " + (_pageIndex + 1) + " of " + p.PageCount;
            RefreshBlockList();
            RenderPages();
        }

        private void SelectBlock(DocBlock block, bool fromPage)
        {
            if (block == null) return;
            if (_project != null && block.Page != _pageIndex) GoToPage(block.Page);
            _originalView.Select(block);
            _translatedView.Select(block);
            FillEditor(block);
            if (fromPage)
            {
                var list = _blocks.DataStore as List<DocBlock>;
                if (list != null && list.Contains(block)) { _blocks.SelectedRow = list.IndexOf(block); _blocks.ScrollToRow(list.IndexOf(block)); }
            }
            List<QualityIssue> issues;
            if (_issues.TryGetValue(block, out issues) && issues.Count > 0) SetStatus(string.Join(" ", issues.Select(i => i.Message)), StatusText.Level.Warning);
        }

        /// <summary>Renders the current page (original and translated) in the background.</summary>
        private void RenderPages()
        {
            var project = _project;
            var page = CurrentPage;
            if (project == null || page == null) return;
            int id = ++_renderId;
            int index = _pageIndex;
            float scale = Math.Min(2.2f, Math.Max(1f, 1500f / Math.Max(page.Width, page.Height)));
            byte[] cached;
            _originalCache.TryGetValue(index, out cached);
            bool translated = project.AllBlocks.Any(b => b.Page == index && (b.State == BlockState.Translated || b.State == BlockState.Edited));
            if (!translated) _translatedView.SetPlaceholder(project.TranslatedBlocks > 0 ? "This page has nothing to translate" : "Click Translate to see the translation here");
            Task.Run(() =>
            {
                byte[] original = cached;
                if (original == null)
                {
                    var img = TranslatedPdfWriter.RenderOriginal(project.PdfPath, index, scale);
                    original = PngWriter.FromBgra(img.Pixels, img.Width, img.Height);
                }
                byte[] result = null;
                if (translated)
                {
                    PageWriteReport report;
                    var img = TranslatedPdfWriter.RenderPage(project, index, scale, new PdfWriteOptions { Fonts = DocumentServices.Fonts }, out report);
                    result = PngWriter.FromBgra(img.Pixels, img.Width, img.Height);
                }
                return new[] { original, result };
            }).ContinueWith(t =>
            {
                OnUi(() =>
                {
                    if (id != _renderId || _project != project) return;
                    if (t.IsFaulted)
                    {
                        Diagnostics.Log("render: " + t.Exception);
                        _originalView.SetPlaceholder("This page could not be shown.");
                        return;
                    }
                    var images = t.Result;
                    _originalCache[index] = images[0];
                    _originalView.SetImage(new Bitmap(images[0]), page, scale);
                    if (images[1] != null) _translatedView.SetImage(new Bitmap(images[1]), page, scale);
                    RefreshBlockList();
                });
            });
        }

        // ------------------------------------------------------------------ editing
        private void EditBlock(DocBlock block, string text)
        {
            var project = _project;
            if (project == null || block == null) return;
            text = (text ?? string.Empty).Trim();
            if (text == (block.Translation ?? string.Empty).Trim()) return;
            if (text.Length == 0) { block.Translation = null; block.State = BlockState.Kept; }
            else
            {
                var translator = new DocumentTranslator(TranslationServices.CreateDocumentEngine());
                int others = translator.ApplyEdit(project, block, text);
                if (others > 0) SetStatus("Also changed " + others + " identical paragraph(s) elsewhere in the document.", StatusText.Level.Success);
            }
            _issues.Remove(block);
            foreach (var issue in QualityChecker.Check(block, project, TranslationServices.Glossary.For(project.SourceLang, project.TargetLang).ToList()))
            {
                List<QualityIssue> list;
                if (!_issues.TryGetValue(block, out list)) _issues[block] = list = new List<QualityIssue>();
                list.Add(issue);
            }
            TranslationServices.SaveCache();
            SaveProject();
            RenderPages();
        }

        private void FillEditor(DocBlock block)
        {
            _editing = block;
            if (_editOriginal == null) return;
            _editOriginal.Text = block == null ? string.Empty : block.Source;
            _editTranslation.Text = block == null ? string.Empty : (block.Translation ?? (block.State == BlockState.Kept ? block.Source : string.Empty));
        }

        private void SaveEditor()
        {
            var block = _editing;
            if (block == null) { SetStatus("Select a paragraph in the list (or click it on the page) first.", StatusText.Level.Warning); return; }
            EditBlock(block, _editTranslation.Text);
            RefreshBlockList();
            SetStatus("Change saved.", StatusText.Level.Success);
        }

        private async void RetranslateSelected()
        {
            var block = _blocks.SelectedItem as DocBlock;
            var project = _project;
            if (block == null || project == null) { SetStatus("Select a paragraph in the list (or click it on the page) first.", StatusText.Level.Warning); return; }
            if (string.IsNullOrEmpty(project.SourceLang)) project.SourceLang = SourceLang == Languages.AutoDetect ? string.Empty : SourceLang;
            project.TargetLang = TargetLang;
            await RunBusy("Translating the paragraph...", async ct =>
            {
                var translator = new DocumentTranslator(TranslationServices.CreateDocumentEngine());
                await Task.Run(() => translator.RetranslateAsync(project, block, ct), ct);
            });
            _issues.Remove(block);
            SaveProject();
            RefreshBlockList();
            FillEditor(block);
            RenderPages();
        }

        private void KeepSelected()
        {
            var block = _blocks.SelectedItem as DocBlock;
            if (block == null) { SetStatus("Select a paragraph in the list (or click it on the page) first.", StatusText.Level.Warning); return; }
            block.Translation = null;
            block.State = BlockState.Kept;
            _issues.Remove(block);
            SaveProject();
            RefreshBlockList();
            FillEditor(block);
            RenderPages();
        }

        private void AddGlossaryTerm()
        {
            var project = _project;
            string src = project != null && !string.IsNullOrEmpty(project.SourceLang) ? project.SourceLang : (SourceLang == Languages.AutoDetect ? "auto" : SourceLang);
            var dlg = new GlossaryTermDialog(Languages.EnglishName(src == "auto" ? null : src) ?? "Original", Languages.EnglishName(TargetLang));
            if (!dlg.ShowModal(this)) return;
            TranslationServices.Glossary.Add(src, TargetLang, dlg.Term, dlg.Translation);
            TranslationServices.SaveGlossary();
            if (project == null) { SetStatus("Glossary term added.", StatusText.Level.Success); return; }
            // translate again the paragraphs that use the term (the user's own edits stay)
            int count = 0;
            foreach (var b in project.AllBlocks.Where(b => b.State == BlockState.Translated && DocumentTranslator.ContainsTerm(b.Source, dlg.Term)))
            {
                b.State = BlockState.Pending;
                count++;
            }
            if (count == 0) { SetStatus("Glossary term added. It is used from now on.", StatusText.Level.Success); return; }
            SetStatus("Glossary term added; translating the " + count + " paragraph(s) that use it...");
            TranslateDocument(false);
        }

        // ------------------------------------------------------------------ checks and summary
        private void RunQualityChecks(bool announce)
        {
            var project = _project;
            _issues.Clear();
            if (project == null) return;
            foreach (var issue in QualityChecker.Check(project, TranslationServices.Glossary))
            {
                if (issue.Kind == QualityIssueKind.NotTranslated && issue.Block.State == BlockState.Pending) continue;
                List<QualityIssue> list;
                if (!_issues.TryGetValue(issue.Block, out list)) _issues[issue.Block] = list = new List<QualityIssue>();
                list.Add(issue);
            }
            if (announce)
            {
                if (_issues.Count == 0) SetStatus("No problems found: numbers, links, glossary terms and lengths match the original.", StatusText.Level.Success);
                else SetStatus(_issues.Count + " paragraph(s) to check. They are marked in orange; the Check column says why.", StatusText.Level.Warning);
            }
            RefreshDocument();
        }

        private void ShowCheckMenu(Control anchor)
        {
            var menu = new ContextMenu();
            var all = new ButtonMenuItem { Text = "Check the translation" };
            all.Click += (s, e) => RunQualityChecks(true);
            var meaning = new ButtonMenuItem { Text = "Check the meaning on this page (translates it back)" };
            meaning.Click += (s, e) => CheckMeaning();
            var nextIssue = new ButtonMenuItem { Text = "Go to the next paragraph to check" };
            nextIssue.Click += (s, e) => NextIssue();
            menu.Items.Add(all);
            menu.Items.Add(meaning);
            menu.Items.Add(nextIssue);
            menu.Show(anchor);
        }

        private void NextIssue()
        {
            if (_issues.Count == 0) { SetStatus("Nothing to check.", StatusText.Level.Success); return; }
            var current = _blocks.SelectedItem as DocBlock;
            var ordered = _issues.Keys.OrderBy(b => b.Page).ThenBy(b => b.Order).ToList();
            var next = current == null ? ordered[0] : ordered.FirstOrDefault(b => b.Page > current.Page || (b.Page == current.Page && b.Order > current.Order)) ?? ordered[0];
            SelectBlock(next, true);
        }

        private async void CheckMeaning()
        {
            var project = _project;
            var page = CurrentPage;
            if (project == null || page == null || !project.AllBlocks.Any(b => b.Page == page.Index && b.State == BlockState.Translated)) { SetStatus("Translate the document first.", StatusText.Level.Warning); return; }
            List<QualityIssue> found = null;
            await RunBusy("Translating page " + (page.Index + 1) + " back to compare...", async ct =>
            {
                var engine = TranslationServices.CreateDocumentEngine();
                found = await Task.Run(() => QualityChecker.BackTranslateCheckAsync(engine, project, page.Blocks, ct), ct);
            });
            if (found == null) return;
            foreach (var issue in found)
            {
                List<QualityIssue> list;
                if (!_issues.TryGetValue(issue.Block, out list)) _issues[issue.Block] = list = new List<QualityIssue>();
                list.Add(issue);
            }
            RefreshDocument();
            SetStatus(found.Count == 0 ? "The meaning on this page reads the same when translated back." : found.Count + " paragraph(s) read differently when translated back (see the Check column).",
                found.Count == 0 ? StatusText.Level.Success : StatusText.Level.Warning);
        }

        private void ShowSummary()
        {
            var project = _project;
            if (project == null) { OpenFileDialog(); return; }
            var summary = Summarizer.Summarize(project, project.TranslatedBlocks > 0);
            project.Summary = summary.ToText();
            SaveProject();
            new TextDialog("Summary — " + project.Name, project.Summary, "The key points are sentences of the document itself, chosen because the rest of the text is about them.").ShowModal(this);
        }

        // ------------------------------------------------------------------ export
        private void ShowExportMenu(Control anchor)
        {
            if (_project == null) { OpenFileDialog(); return; }
            var menu = new ContextMenu();
            Action<string, Action> add = (text, action) => { var i = new ButtonMenuItem { Text = text }; i.Click += (s, e) => action(); menu.Items.Add(i); };
            add("PDF — same layout as the original", ExportPdf);
            add("Word document", () => ExportDocx(ExportText.Translated));
            add("Word — original and translation", () => ExportDocx(ExportText.Bilingual));
            add("Text file", () => ExportTextFile(ExportText.Translated));
            add("Text — original and translation", () => ExportTextFile(ExportText.Bilingual));
            menu.Show(anchor);
        }

        private string ExportName(string suffix)
        {
            var p = _project;
            return SafeFileName(p.Name + " (" + Languages.EnglishName(p.TargetLang) + ")" + suffix);
        }

        private bool EnsureTranslated()
        {
            if (_project != null && _project.TranslatedBlocks > 0) return true;
            SetStatus("Translate the document first (click Translate).", StatusText.Level.Warning);
            return false;
        }

        private async void ExportPdf()
        {
            if (!EnsureTranslated()) return;
            var project = _project;
            var path = AskSavePath("Save the translated PDF", ExportName(".pdf"), "PDF document", ".pdf");
            if (path == null) return;
            var ok = await RunBusy("Writing the PDF...", async ct =>
            {
                var options = new PdfWriteOptions { Fonts = DocumentServices.Fonts, PagesDone = new Progress<int>(n => SetProgress(n, project.PageCount)) };
                await Task.Run(() => TranslatedPdfWriter.Write(project, path, options, ct), ct);
            });
            if (!ok) return;
            SaveProject();
            int shrunk = project.AllBlocks.Count(b => b.Note != null && (b.Note.StartsWith("Text reduced", StringComparison.Ordinal) || b.Note.StartsWith("The translation is longer", StringComparison.Ordinal)));
            SetStatus("Saved " + Path.GetFileName(path) + (shrunk > 0 ? " (" + shrunk + " paragraph(s) made smaller to fit)." : "."), StatusText.Level.Success);
            DocumentServices.OpenFile(path);
        }

        private async void ExportDocx(ExportText mode)
        {
            if (mode != ExportText.Original && !EnsureTranslated()) return;
            var project = _project;
            var path = AskSavePath("Save as a Word document", ExportName(".docx"), "Word document", ".docx");
            if (path == null) return;
            var ok = await RunBusy("Writing the Word document...", async ct =>
            {
                await Task.Run(() =>
                {
                    using (var images = new PageImages(project.PdfPath))
                        DocxWriter.Write(project, path, new DocxOptions { Text = mode, ImageProvider = images.Crop, Fonts = DocumentServices.Fonts });
                }, ct);
            });
            if (!ok) return;
            SetStatus("Saved " + Path.GetFileName(path) + ".", StatusText.Level.Success);
            DocumentServices.OpenFile(path);
        }

        private void ExportTextFile(ExportText mode)
        {
            if (mode != ExportText.Original && !EnsureTranslated()) return;
            var path = AskSavePath("Save as text", ExportName(".txt"), "Text file", ".txt");
            if (path == null) return;
            try
            {
                TextExporter.Write(_project, path, mode);
                SetStatus("Saved " + Path.GetFileName(path) + ".", StatusText.Level.Success);
                DocumentServices.OpenFile(path);
            }
            catch (Exception ex) { SetStatus("Could not save: " + ex.Message, StatusText.Level.Error); }
        }
    }
}
