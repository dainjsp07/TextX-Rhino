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
        private readonly Panel _toolBody = new Panel();
        private TextArea _toolInput, _toolOutput;
        private Label _toolInputLabel, _toolOutputLabel;
        private readonly ToggleCheck _toolLive = new ToggleCheck("Translate as I type", 12.5f) { Checked = true };
        private readonly UITimer _toolDebounce = new UITimer { Interval = 0.8 };
        private readonly UITimer _clipWatch = new UITimer { Interval = 0.5 };
        private uint _clipSequence;
        private DateTime _clipUntil;
        private int _toolRequest;
        private bool _toolSuppress;
        private string _detectedToolLang;

        /// <summary>Picture or screenshot: take a screenshot, paste or open a picture; its text is read and translated.</summary>
        private Control BuildTools()
        {
            var screenshot = new FlatButton("Take a screenshot", IconKind.Target, ButtonKind.Primary, 32) { ToolTip = "Select an area of the screen (Win + Shift + S); its text is read as soon as it is taken" };
            screenshot.Click += (s, e) => TakeScreenshot();
            var paste = new FlatButton("Paste picture", IconKind.Download, ButtonKind.Secondary, 32) { ToolTip = "Read the picture on the clipboard (Ctrl + V)" };
            paste.Click += (s, e) => PastePicture();
            var openPicture = new FlatButton("Open a picture", IconKind.Picture, ButtonKind.Secondary, 32);
            openPicture.Click += (s, e) => OpenPictureDialog();
            var view = new FlatButton("Rhino view", IconKind.Eye, ButtonKind.Secondary, 32) { ToolTip = "Read the text shown in the active Rhino viewport (for example a reference picture)" };
            view.Click += (s, e) => ReadRhinoView();
            var hint = Ui.Label(_scope, "Its text is read on this computer (OCR) and translated.", 12f, false, p => p.Muted);
            var bar = Ui.Row(8, screenshot, paste, openPicture, view, hint, Ui.Spring());

            // the text found and its translation side by side
            _toolInput = Ui.TextArea(_scope, 14f);
            _toolOutput = Ui.TextArea(_scope, 14f);
            _toolInputLabel = Ui.Section(_scope, "Text found in the picture");
            _toolOutputLabel = Ui.Section(_scope, "Translation");
            _toolInput.TextChanged += (s, e) =>
            {
                if (_toolSuppress || !_toolLive.Checked) return;
                _toolDebounce.Stop();
                _toolDebounce.Start();
            };
            _toolDebounce.Elapsed += (s, e) => { _toolDebounce.Stop(); TranslateToolText(); };
            var translateNow = new FlatButton("Translate", IconKind.Translate, ButtonKind.Secondary, 28);
            translateNow.Click += (s, e) => TranslateToolText();
            var copy = new FlatButton("Copy translation", IconKind.None, ButtonKind.Strong, 28);
            copy.Click += (s, e) => CopyText(_toolOutput.Text);
            var toRhino = new FlatButton("Use on Home", IconKind.Target, ButtonKind.Primary, 28) { ToolTip = "Put the text and its translation in the translator on Home, to place them as text or a leader" };
            toRhino.Click += (s, e) => SendToTextX();
            var clear = new FlatButton("Clear", IconKind.Close, ButtonKind.Ghost, 28);
            clear.Click += (s, e) => { _toolSuppress = true; _toolInput.Text = string.Empty; _toolOutput.Text = string.Empty; _toolSuppress = false; };

            _scope.Bind(_toolInput, (x, p) => x.BackgroundColor = p.Surface);
            _scope.Bind(_toolOutput, (x, p) => x.BackgroundColor = p.Surface);
            var columns = new TableLayout { Spacing = new Size(12, 0) };
            columns.Rows.Add(new TableRow(new TableCell(TextCard(_toolInputLabel, _toolInput, false), true), new TableCell(TextCard(_toolOutputLabel, _toolOutput, true), true)) { ScaleHeight = true });
            var body = new TableLayout { Spacing = new Size(0, 8) };
            body.Rows.Add(new TableRow(columns) { ScaleHeight = true });
            body.Rows.Add(new TableRow(Ui.Row(8, _toolLive, translateNow, Ui.Spring(), clear, copy, toRhino)));
            _toolBody.Content = body;
            _scope.Bind(_toolBody, (x, p) => x.BackgroundColor = p.Window);
            _clipWatch.Elapsed += (s, e) => WatchClipboard();

            var layout = new TableLayout { Spacing = new Size(0, 12), Padding = new Padding(4, 0, 16, 8) };
            layout.Rows.Add(new TableRow(bar));
            layout.Rows.Add(new TableRow(_toolBody) { ScaleHeight = true });
            UpdateToolLabels();
            return _scope.Bind(new Panel { Content = layout }, (x, p) => x.BackgroundColor = p.Window);
        }

        /// <summary>A large card with a small label and a text box (or grid) filling it.</summary>
        private Control TextCard(Control label, Control content, bool accent)
        {
            var dot = new DotMark(p => accent ? p.Accent : p.Muted);
            var head = Ui.Row(8, dot, label, Ui.Spring());
            var body = new TableLayout { Spacing = new Size(0, 8) };
            body.Rows.Add(new TableRow(head));
            body.Rows.Add(new TableRow(content) { ScaleHeight = true });
            return new Card(body, new Padding(16, 12, 16, 12), p => p.Surface, p => accent ? p.AccentLine : p.Border, 20f);
        }

        private void UpdateToolLabels()
        {
            if (_toolOutputLabel == null) return;
            _toolOutputLabel.Text = Theme.Spaced("Translation") + "   ·   " + Theme.Spaced(Languages.EnglishName(TargetLang));
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
            ShowTab(PictureTab);
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
            if (_tabs.SelectedIndex != PictureTab) ShowTab(PictureTab);
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

        /// <summary>Puts the text and its translation in the translator on Home, ready to place in Rhino.</summary>
        private void SendToTextX()
        {
            var text = _toolInput.Text ?? string.Empty;
            if (text.Trim().Length == 0) { SetStatus("There is no text yet.", StatusText.Level.Warning); return; }
            StartNewText(text, _toolOutput.Text);
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
    }
}
