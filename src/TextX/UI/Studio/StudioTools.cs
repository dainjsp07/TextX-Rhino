using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using TextX.Core;
using TextX.Core.Tools;
using TextX.Core.Translation;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Studio
{
    public sealed partial class StudioWindow
    {
        private readonly SegmentedControl _toolMode = new SegmentedControl("Picture or screenshot", "Speak", "Type or paste", "Check writing");
        private readonly Panel _toolBar = new Panel();
        private readonly Panel _toolBody = new Panel();
        private TextArea _toolInput, _toolOutput, _writingText;
        private Label _toolInputLabel, _toolOutputLabel, _writingInfo;
        private Control _translateBody, _writingBody;
        private Control[] _toolBars;
        private readonly ToggleCheck _toolLive = new ToggleCheck("Translate as I type", 12.5f) { Checked = true };
        private readonly UITimer _toolDebounce = new UITimer { Interval = 0.8 };
        private readonly UITimer _clipWatch = new UITimer { Interval = 0.5 };
        private readonly GridView _writingIssues = new GridView { AllowMultipleSelection = true, ShowHeader = true, GridLines = GridLines.Horizontal };
        private List<WritingIssue> _issuesFound = new List<WritingIssue>();
        private uint _clipSequence;
        private DateTime _clipUntil;
        private int _toolRequest;
        private bool _toolSuppress;
        private string _detectedToolLang;

        private Control BuildTools()
        {
            // input buttons for each mode
            var openPicture = new FlatButton("Open picture...", IconKind.File, ButtonKind.Secondary, 32);
            openPicture.Click += (s, e) => OpenPictureDialog();
            var screenshot = new FlatButton("Take a screenshot", IconKind.Target, ButtonKind.Primary, 32) { ToolTip = "Select an area of the screen (Win + Shift + S); its text is read as soon as it is taken" };
            screenshot.Click += (s, e) => TakeScreenshot();
            var paste = new FlatButton("Paste picture", IconKind.Download, ButtonKind.Secondary, 32) { ToolTip = "Read the picture on the clipboard (Ctrl + V)" };
            paste.Click += (s, e) => PastePicture();
            var view = new FlatButton("Rhino view", IconKind.Eye, ButtonKind.Secondary, 32) { ToolTip = "Read the text shown in the active Rhino viewport (for example a reference picture)" };
            view.Click += (s, e) => ReadRhinoView();
            var pictureBar = Ui.Row(8, screenshot, paste, openPicture, view, Ui.Spring());

            var speak = new FlatButton("Start speaking", IconKind.Text, ButtonKind.Primary, 32) { ToolTip = "Windows voice typing (Win + H) writes what you say into the box; the translation follows" };
            speak.Click += (s, e) => StartSpeaking();
            var speakHint = Ui.Label(_scope, "Speak in the language chosen at the top (or any language with Auto-detect). Windows may ask to allow speech recognition the first time.", 12f, false, p => p.Muted, true);
            var speakBar = new TableLayout { Spacing = new Size(12, 0) };
            speakBar.Rows.Add(new TableRow(new TableCell(speak, false), new TableCell(speakHint, true)));

            var typeHint = Ui.Label(_scope, "Type or paste any text — for example a paragraph copied from a PDF or an e-mail. It is translated as you type.", 12f, false, p => p.Muted, true);

            var check = new FlatButton("Check writing", IconKind.Check, ButtonKind.Primary, 32) { ToolTip = "Spelling, grammar and style suggestions" };
            check.Click += (s, e) => CheckWriting();
            var fixSelected = new FlatButton("Fix selected", IconKind.None, ButtonKind.Secondary, 32);
            fixSelected.Click += (s, e) => FixWriting(_writingIssues.SelectedItems.OfType<WritingIssue>().ToList());
            var fixAll = new FlatButton("Fix all", IconKind.None, ButtonKind.Secondary, 32);
            fixAll.Click += (s, e) => FixWriting(_issuesFound.ToList());
            var writingBar = Ui.Row(8, check, fixSelected, fixAll, Ui.Spring());
            _toolBars = new Control[] { pictureBar, speakBar, typeHint, writingBar };

            // translate body: text and translation side by side
            _toolInput = Ui.TextArea(_scope, 14f);
            _toolOutput = Ui.TextArea(_scope, 14f);
            _toolInputLabel = Ui.Section(_scope, "Text");
            _toolOutputLabel = Ui.Section(_scope, "Translation");
            _toolInput.TextChanged += (s, e) =>
            {
                if (_toolSuppress || !_toolLive.Checked) return;
                _toolDebounce.Stop();
                _toolDebounce.Start();
            };
            _toolDebounce.Elapsed += (s, e) => { _toolDebounce.Stop(); TranslateToolText(); };
            var translateNow = new FlatButton("Translate", IconKind.Translate, ButtonKind.Secondary, 30);
            translateNow.Click += (s, e) => TranslateToolText();
            var copy = new FlatButton("Copy translation", IconKind.None, ButtonKind.Secondary, 30);
            copy.Click += (s, e) => CopyText(_toolOutput.Text);
            var toRhino = new FlatButton("Place in Rhino...", IconKind.Target, ButtonKind.Primary, 30) { ToolTip = "Open the text and its translation in the Text X window to place them as text or a leader" };
            toRhino.Click += (s, e) => SendToTextX();
            var clear = new FlatButton("Clear", IconKind.None, ButtonKind.Ghost, 30);
            clear.Click += (s, e) => { _toolSuppress = true; _toolInput.Text = string.Empty; _toolOutput.Text = string.Empty; _toolSuppress = false; };

            var columns = new TableLayout { Spacing = new Size(12, 6) };
            columns.Rows.Add(new TableRow(new TableCell(_toolInputLabel, true), new TableCell(_toolOutputLabel, true)));
            columns.Rows.Add(new TableRow(new TableCell(Ui.FieldBox(_toolInput), true), new TableCell(Ui.FieldBox(_toolOutput), true)) { ScaleHeight = true });
            var translateLayout = new TableLayout { Spacing = new Size(0, 10) };
            translateLayout.Rows.Add(new TableRow(columns) { ScaleHeight = true });
            translateLayout.Rows.Add(new TableRow(Ui.Row(8, _toolLive, translateNow, Ui.Spring(), clear, copy, toRhino)));
            _translateBody = translateLayout;

            // writing body: text and suggestions
            _writingText = Ui.TextArea(_scope, 14f);
            _writingInfo = Ui.Label(_scope, "Checked with LanguageTool's free online service (no account). The text is sent to languagetool.org to be checked.", 12f, false, p => p.Muted, true);
            _writingIssues.Columns.Add(new GridColumn { HeaderText = "Problem", DataCell = new TextBoxCell { Binding = Binding.Delegate<WritingIssue, string>(i => i.Message) }, Width = 220, Resizable = true });
            _writingIssues.Columns.Add(new GridColumn { HeaderText = "Suggestion", DataCell = new TextBoxCell { Binding = Binding.Delegate<WritingIssue, string>(i => i.Replacements.Count == 0 ? "(no suggestion)" : string.Join(" / ", i.Replacements.Take(3))) }, Width = 150, Resizable = true });
            _writingIssues.Columns.Add(new GridColumn { HeaderText = "Kind", DataCell = new TextBoxCell { Binding = Binding.Delegate<WritingIssue, string>(i => i.Category) }, Width = 100, Resizable = true });
            _writingIssues.SelectionChanged += (s, e) =>
            {
                var issue = _writingIssues.SelectedItem as WritingIssue;
                if (issue == null) return;
                try { _writingText.Selection = new Range<int>(issue.Offset, Math.Max(issue.Offset, issue.Offset + issue.Length - 1)); } catch { /* text changed */ }
            };
            _writingIssues.CellDoubleClick += (s, e) => { var i = e.Item as WritingIssue; if (i != null) FixWriting(new List<WritingIssue> { i }); };
            var writingColumns = new TableLayout { Spacing = new Size(12, 6) };
            writingColumns.Rows.Add(new TableRow(new TableCell(Ui.Section(_scope, "Your text"), true), new TableCell(Ui.Section(_scope, "Suggestions (double-click to apply)"), true)));
            writingColumns.Rows.Add(new TableRow(new TableCell(Ui.FieldBox(_writingText), true), new TableCell(_writingIssues, true)) { ScaleHeight = true });
            var writingLayout = new TableLayout { Spacing = new Size(0, 10) };
            writingLayout.Rows.Add(new TableRow(writingColumns) { ScaleHeight = true });
            writingLayout.Rows.Add(new TableRow(_writingInfo));
            _writingBody = writingLayout;

            _toolMode.Width = 560;
            _toolMode.SelectedIndex = 0;
            _toolMode.SelectedIndexChanged += (s, e) => ShowTool(_toolMode.SelectedIndex);
            _scope.Bind(_toolBar, (x, p) => x.BackgroundColor = p.Window);
            _scope.Bind(_toolBody, (x, p) => x.BackgroundColor = p.Window);
            _clipWatch.Elapsed += (s, e) => WatchClipboard();

            var layout = new TableLayout { Spacing = new Size(0, 12), Padding = new Padding(16, 12, 16, 12) };
            layout.Rows.Add(new TableRow(Ui.Row(8, _toolMode, Ui.Spring())));
            layout.Rows.Add(new TableRow(_toolBar));
            layout.Rows.Add(new TableRow(_toolBody) { ScaleHeight = true });
            ShowTool(0);
            return _scope.Bind(new Panel { Content = layout }, (x, p) => x.BackgroundColor = p.Window);
        }

        private void ShowTool(int index)
        {
            if (_toolBars == null) return;
            index = Math.Max(0, Math.Min(3, index));
            if (_toolMode.SelectedIndex != index) { _toolMode.SelectedIndex = index; return; }
            _toolBar.Content = _toolBars[index];
            _toolBody.Content = index == 3 ? _writingBody : _translateBody;
            _toolInputLabel.Text = (index == 0 ? "Text found in the picture" : index == 1 ? "What you said" : "Text").ToUpperInvariant();
            UpdateToolLabels();
        }

        private void UpdateToolLabels()
        {
            if (_toolOutputLabel == null) return;
            _toolOutputLabel.Text = ("Translation — " + Languages.EnglishName(TargetLang)).ToUpperInvariant();
        }

        private void OnToolLanguagesChanged()
        {
            UpdateToolLabels();
            if (_toolInput != null && !string.IsNullOrWhiteSpace(_toolInput.Text) && _toolLive.Checked) TranslateToolText();
        }

        private void StopToolTimers()
        {
            _toolDebounce.Stop();
            _clipWatch.Stop();
        }

        // ------------------------------------------------------------------ pictures
        private void OpenPictureDialog()
        {
            var dlg = new Eto.Forms.OpenFileDialog { Title = "Open a picture", MultiSelect = false };
            dlg.Filters.Add(new FileFilter("Pictures", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"));
            if (dlg.ShowDialog(this) == DialogResult.Ok && !string.IsNullOrEmpty(dlg.FileName)) ReadPictureFile(dlg.FileName);
        }

        private void ReadPictureFile(string path)
        {
            ShowTab(ToolsTab);
            ShowTool(0);
            GrayImage image;
            try { image = DocumentServices.LoadImage(path); }
            catch (Exception ex) { SetStatus("This picture could not be opened: " + ex.Message, StatusText.Level.Error); return; }
            ReadPicture(image, Path.GetFileName(path));
        }

        private void TakeScreenshot()
        {
            _clipSequence = ClipboardSequence();
            _clipUntil = DateTime.UtcNow.AddSeconds(90);
            if (!DocumentServices.StartScreenClip())
            {
                SetStatus("Press Win + Shift + S, select an area, then click Paste picture.", StatusText.Level.Warning);
                return;
            }
            SetStatus("Select an area of the screen. Its text is read as soon as the screenshot is taken.");
            _clipWatch.Start();
        }

        private void WatchClipboard()
        {
            if (DateTime.UtcNow > _clipUntil) { _clipWatch.Stop(); return; }
            uint seq = ClipboardSequence();
            if (seq == 0 || seq == _clipSequence) return;
            _clipSequence = seq;
            var bmp = ClipboardImage();
            if (bmp == null) return;   // something else was copied; keep waiting
            _clipWatch.Stop();
            BringToFront();
            ReadBitmap(bmp, "screenshot");
        }

        private void PastePicture()
        {
            ShowTool(0);
            try
            {
                var clip = Clipboard.Instance;
                if (clip.ContainsUris)
                {
                    var file = clip.Uris.Where(u => u.IsFile).Select(u => u.LocalPath).FirstOrDefault();
                    if (file != null) { OpenPath(file); return; }
                }
            }
            catch (Exception) { /* no file on the clipboard */ }
            var bmp = ClipboardImage();
            if (bmp == null) { SetStatus("There is no picture on the clipboard. Take a screenshot or copy a picture first.", StatusText.Level.Warning); return; }
            ReadBitmap(bmp, "picture from the clipboard");
        }

        private static Bitmap ClipboardImage()
        {
            try
            {
                var clip = Clipboard.Instance;
                if (!clip.ContainsImage) return null;
                return clip.Image as Bitmap;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("clipboard: " + ex.Message);
                return null;
            }
        }

        private void ReadBitmap(Bitmap bmp, string what)
        {
            GrayImage image;
            try { image = DocumentServices.LoadImage(bmp.ToByteArray(ImageFormat.Png)); }
            catch (Exception ex) { SetStatus("The picture could not be read: " + ex.Message, StatusText.Level.Error); return; }
            ReadPicture(image, what);
        }

        private void ReadRhinoView()
        {
            GrayImage image;
            try { image = RhinoCapture.ActiveView(Doc); }
            catch (Exception ex) { SetStatus("The Rhino view could not be captured: " + ex.Message, StatusText.Level.Error); return; }
            if (image == null) { SetStatus("No Rhino view is open.", StatusText.Level.Warning); return; }
            ReadPicture(image, "the Rhino view");
        }

        private async void ReadPicture(GrayImage image, string what)
        {
            DocumentServices.Configure();
            ImageTextResult result = null;
            string lang = SourceLang;
            var ok = await RunBusy("Reading the text of " + what + "...", async ct =>
            {
                result = await Task.Run(() => ImageText.ReadAsync(image, lang, StatusProgress(), ct), ct);
            });
            if (!ok || result == null) return;
            if (result.Text.Trim().Length == 0) { SetStatus("No text was found in " + what + ".", StatusText.Level.Warning); return; }
            _toolSuppress = true;
            _toolInput.Text = result.Text;
            _toolSuppress = false;
            SetStatus("Read " + result.Words + " words from " + what + (result.Confidence < 70 ? " (the picture is not very clear: check the text)." : "."),
                result.Confidence < 70 ? StatusText.Level.Warning : StatusText.Level.Success);
            TranslateToolText();
        }

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        private static uint ClipboardSequence()
        {
            try { return GetClipboardSequenceNumber(); }
            catch (Exception) { return 0; }
        }

        // ------------------------------------------------------------------ voice
        private void StartSpeaking()
        {
            ShowTool(1);
            _toolInput.Focus();
            _toolInput.CaretIndex = (_toolInput.Text ?? string.Empty).Length;
            // give the text box the keyboard focus before Windows voice typing starts
            var timer = new UITimer { Interval = 0.25 };
            timer.Elapsed += (s, e) =>
            {
                timer.Stop();
                if (!DocumentServices.StartVoiceTyping()) SetStatus("Press Win + H to start Windows voice typing in the text box.", StatusText.Level.Warning);
                else SetStatus("Speak now. Windows voice typing writes into the box; press Win + H again to stop.");
            };
            timer.Start();
        }

        // ------------------------------------------------------------------ translation
        private async void TranslateToolText()
        {
            var text = _toolInput.Text ?? string.Empty;
            if (text.Trim().Length == 0) { _toolOutput.Text = string.Empty; return; }
            int id = ++_toolRequest;
            string src = SourceLang, tgt = TargetLang;
            try
            {
                var engine = TranslationServices.CreateEngine();
                var outcome = await Task.Run(() => engine.TranslateAsync(text, src, tgt, CancellationToken.None));
                TranslationServices.SaveCache();
                if (id != _toolRequest) return;
                _toolOutput.Text = outcome.Translations.Count > 0 ? outcome.Translations[0] : string.Empty;
                _detectedToolLang = src == Languages.AutoDetect ? LanguageGuessOrNull(text) : src;
            }
            catch (Exception ex)
            {
                if (id != _toolRequest) return;
                SetStatus(Friendly(ex), StatusText.Level.Error);
            }
        }

        private static string LanguageGuessOrNull(string text)
        {
            return Core.Documents.LanguageGuess.FromScript(text);
        }

        private void SendToTextX()
        {
            var text = _toolInput.Text ?? string.Empty;
            if (text.Trim().Length == 0) { SetStatus("There is no text yet.", StatusText.Level.Warning); return; }
            var doc = Doc;
            var window = TextXWindow.ShowWindow(doc);
            string src = SourceLang != Languages.AutoDetect ? SourceLang : (_detectedToolLang ?? Languages.AutoDetect);
            window.StartNewText(text, _toolOutput.Text, src, TargetLang);
        }

        private void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) { SetStatus("Nothing to copy yet.", StatusText.Level.Warning); return; }
            try
            {
                Clipboard.Instance.Text = text;
                SetStatus("Copied.", StatusText.Level.Success);
            }
            catch (Exception ex) { SetStatus("Could not copy: " + ex.Message, StatusText.Level.Error); }
        }

        // ------------------------------------------------------------------ writing check
        private async void CheckWriting()
        {
            var text = _writingText.Text ?? string.Empty;
            if (text.Trim().Length == 0) { SetStatus("Type or paste the text to check first.", StatusText.Level.Warning); return; }
            WritingResult result = null;
            string lang = SourceLang;
            var ok = await RunBusy("Checking the writing...", async ct =>
            {
                result = await Task.Run(() => WritingCheck.CheckAsync(text, lang, ct), ct);
            });
            if (!ok || result == null) return;
            _issuesFound = result.Issues;
            _writingIssues.DataStore = _issuesFound.ToList();
            SetStatus(result.Issues.Count == 0 ? "No problems found" + (string.IsNullOrEmpty(result.LanguageName) ? "." : " (" + result.LanguageName + ").")
                                               : result.Issues.Count + " suggestion(s)" + (string.IsNullOrEmpty(result.LanguageName) ? "." : " (" + result.LanguageName + "). Double-click one to apply it, or click Fix all."),
                result.Issues.Count == 0 ? StatusText.Level.Success : StatusText.Level.Info);
        }

        private void FixWriting(List<WritingIssue> issues)
        {
            var usable = issues.Where(i => i.Best != null).ToList();
            if (usable.Count == 0) { SetStatus("Select suggestions that have a replacement.", StatusText.Level.Warning); return; }
            var text = _writingText.Text ?? string.Empty;
            // apply from the end so the positions of the others stay right
            var remaining = _issuesFound.ToList();
            foreach (var issue in usable.OrderByDescending(i => i.Offset))
            {
                if (issue.Offset < 0 || issue.Offset + issue.Length > text.Length) continue;
                text = text.Substring(0, issue.Offset) + issue.Best + text.Substring(issue.Offset + issue.Length);
                remaining = WritingCheck.AfterApplying(remaining, issue, issue.Best);
            }
            _writingText.Text = text;
            _issuesFound = remaining;
            _writingIssues.DataStore = _issuesFound.ToList();
            SetStatus(usable.Count + " suggestion(s) applied.", StatusText.Level.Success);
        }
    }
}
