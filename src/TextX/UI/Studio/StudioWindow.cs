using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;
using TextX.Core;
using TextX.Core.Documents;
using TextX.Core.Documents.Schedules;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Studio
{
    /// <summary>
    /// Text X Studio (command TRStudio): translate PDF documents keeping their layout, read product
    /// schedules from drawings, get text from pictures and screenshots, dictate, and check writing.
    /// Open a file (drop it on the window or click Open), pick what to do, get the result.
    /// No account and no setup: translation uses the service chosen in Text X settings.
    /// </summary>
    public sealed partial class StudioWindow : Form
    {
        private static StudioWindow _instance;
        public static StudioWindow Instance { get { return _instance; } }

        private readonly ThemeScope _scope = new ThemeScope();
        private readonly KeyPicker _srcLang = new KeyPicker("From", false);
        private readonly KeyPicker _tgtLang = new KeyPicker("To", false);
        private readonly TabStrip _tabs = new TabStrip();
        private readonly Panel _page = new Panel();
        private readonly StatusText _status = new StatusText();
        private readonly ProgressBar _progress = new ProgressBar { MinValue = 0, MaxValue = 1000, Visible = false, Width = 180 };
        private FlatButton _cancel;
        private Control[] _pages;
        private CancellationTokenSource _cts;
        private bool _suppress;
        private uint _docSerial;

        private DocProject _project;
        private ScheduleSet _schedules;

        private const int HomeTab = 0, DocumentTab = 1, SchedulesTab = 2, ToolsTab = 3;

        private static TextXSettings Settings { get { return TextXPlugIn.Instance.Options; } }

        public static StudioWindow ShowWindow(RhinoDoc doc, int tab = -1)
        {
            if (_instance == null)
            {
                _instance = new StudioWindow(doc);
                _instance.Owner = RhinoEtoApp.MainWindow;
                _instance.Show();
            }
            else
            {
                if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
                _instance.BringToFront();
                _instance.Focus();
            }
            if (doc != null) _instance._docSerial = doc.RuntimeSerialNumber;
            if (tab >= 0) _instance._tabs.SelectedIndex = tab;
            return _instance;
        }

        private StudioWindow(RhinoDoc doc)
        {
            Theme.Refresh();
            Title = BuildInfo.ProductName + " Studio";
            Resizable = true;
            Minimizable = true;
            Maximizable = true;
            ShowInTaskbar = true;
            MinimumSize = new Size(900, 620);
            ClientSize = new Size(1240, 820);
            Padding = new Padding(0);
            this.UseRhinoStyle();
            Icons.ApplyWindowIcon(this);
            KeyDown += (s, e) => { if (e.Key == Keys.Tab) ThemedDrawable.NoteKeyboardNavigation(); };
            _scope.Bind(this, (x, p) => x.BackgroundColor = p.Window);
            if (doc != null) _docSerial = doc.RuntimeSerialNumber;

            DocumentServices.Configure();
            BuildLanguageControls();
            Content = BuildLayout();
            RestoreStudioBounds();
            ShowHome();
            SetStatus("Drop a PDF, picture or screenshot on this window, or click Open.");
            Diagnostics.Log("studio constructed");
        }

        // ------------------------------------------------------------------ layout
        private Control BuildLayout()
        {
            _tabs.Add("Home", IconKind.Layers);
            _tabs.Add("Document", IconKind.File);
            _tabs.Add("Schedules", IconKind.Format);
            _tabs.Add("Tools", IconKind.Text);
            _pages = new[] { BuildHome(), BuildDocument(), BuildSchedules(), BuildTools() };
            _page.Content = _pages[0];
            _scope.Bind(_page, (x, p) => x.BackgroundColor = p.Window);
            _tabs.SelectedIndexChanged += (s, e) => ShowTab(_tabs.SelectedIndex);

            var root = new TableLayout { Spacing = new Size(0, 0) };
            root.Rows.Add(new TableRow(BuildHeader()));
            root.Rows.Add(new TableRow(_tabs));
            root.Rows.Add(new TableRow(_page) { ScaleHeight = true });
            root.Rows.Add(new TableRow(Ui.HLine(_scope)));
            root.Rows.Add(new TableRow(BuildFooter()));

            // files can be dropped anywhere on the window
            var dropTarget = new Panel { Content = root, AllowDrop = true };
            dropTarget.DragEnter += OnDragOver;
            dropTarget.DragOver += OnDragOver;
            dropTarget.DragDrop += OnDragDrop;
            return _scope.Bind(dropTarget, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control BuildHeader()
        {
            var title = Ui.Label(_scope, BuildInfo.ProductName + " Studio", 16f, true);
            var swap = new FlatButton(string.Empty, IconKind.Swap, ButtonKind.Soft, 30) { ToolTip = "Swap languages", Backdrop = p => p.Surface, Radius = 7 };
            swap.Click += (s, e) => SwapLanguages();
            var languages = new Card(Ui.Row(4, _srcLang, swap, _tgtLang), new Padding(4), p => p.Surface, p => p.Border, 10f);
            var open = new FlatButton("Open", IconKind.File, ButtonKind.Primary, 34) { ToolTip = "Open a PDF or a picture (you can also drop files on the window)" };
            open.Click += (s, e) => OpenFileDialog();
            var settings = new FlatButton(string.Empty, IconKind.Gear, ButtonKind.Secondary, 34) { ToolTip = "Translation service and settings (TRSettings)" };
            settings.Click += (s, e) => SettingsDialog.ShowFor(this);
            var row = Ui.Row(12, title, Ui.Spring(), languages, open, settings);
            var panel = new Panel { Content = row, Padding = new Padding(16, 12, 16, 6) };
            return _scope.Bind(panel, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control BuildFooter()
        {
            _cancel = new FlatButton("Stop", IconKind.Close, ButtonKind.Ghost, 28) { Visible = false, Backdrop = p => p.Footer };
            _cancel.Click += (s, e) => { if (_cts != null) _cts.Cancel(); };
            var row = Ui.Row(10, new StackLayoutItem(_status, true), _progress, _cancel);
            var panel = new Panel { Content = row, Padding = new Padding(16, 8) };
            return _scope.Bind(panel, (x, p) => x.BackgroundColor = p.Footer);
        }

        private void ShowTab(int index)
        {
            if (index < 0 || index >= _pages.Length) return;
            if (_tabs.SelectedIndex != index) { _tabs.SelectedIndex = index; return; }
            _page.Content = _pages[index];
            if (index == HomeTab) RefreshHistory();
            if (index == DocumentTab) RefreshDocument();
            if (index == SchedulesTab) RefreshSchedules();
        }

        private void ShowHome()
        {
            ShowTab(HomeTab);
            RefreshHistory();
        }

        // ------------------------------------------------------------------ languages
        private string SourceLang { get { return _srcLang.SelectedKey ?? Languages.AutoDetect; } }
        private string TargetLang { get { return _tgtLang.SelectedKey ?? "en"; } }

        private void BuildLanguageControls()
        {
            var src = new List<PickerItem> { new PickerItem { Key = Languages.AutoDetect, Text = "Auto-detect" } };
            var tgt = new List<PickerItem>();
            foreach (var l in Languages.All)
            {
                var native = string.IsNullOrEmpty(l.NativeName) || l.NativeName == l.EnglishName ? null : l.NativeName;
                src.Add(new PickerItem { Key = l.Code, Text = l.EnglishName, Secondary = native });
                tgt.Add(new PickerItem { Key = l.Code, Text = l.EnglishName, Secondary = native });
            }
            src[1].SeparatorBefore = true;
            _srcLang.SetItems(src);
            _tgtLang.SetItems(tgt);
            _suppress = true;
            _srcLang.SelectedKey = _srcLang.Contains(Settings.StudioSourceLanguage) ? Settings.StudioSourceLanguage : Languages.AutoDetect;
            _tgtLang.SelectedKey = _tgtLang.Contains(Settings.StudioTargetLanguage) ? Settings.StudioTargetLanguage : "en";
            _suppress = false;
            _srcLang.SelectedKeyChanged += (s, e) => { if (_suppress) return; Settings.StudioSourceLanguage = SourceLang; OnLanguagesChanged(); };
            _tgtLang.SelectedKeyChanged += (s, e) => { if (_suppress) return; Settings.StudioTargetLanguage = TargetLang; OnLanguagesChanged(); };
        }

        private void SwapLanguages()
        {
            var src = SourceLang == Languages.AutoDetect ? (_project != null && !string.IsNullOrEmpty(_project.SourceLang) ? _project.SourceLang : null) : SourceLang;
            if (src == null || !_tgtLang.Contains(src)) { SetStatus("Choose the original language first (it is detected after translating).", StatusText.Level.Warning); return; }
            var tgt = TargetLang;
            _suppress = true;
            _srcLang.SelectedKey = tgt;
            _tgtLang.SelectedKey = src;
            _suppress = false;
            Settings.StudioSourceLanguage = SourceLang;
            Settings.StudioTargetLanguage = TargetLang;
            OnLanguagesChanged();
        }

        private void OnLanguagesChanged()
        {
            if (_project != null && !IsBusy)
            {
                bool changed = _project.TargetLang != TargetLang || (SourceLang != Languages.AutoDetect && _project.SourceLangSetting != SourceLang);
                if (changed) SetStatus("Languages changed: click Translate to translate the document into " + Languages.EnglishName(TargetLang) + ".");
            }
            OnToolLanguagesChanged();
        }

        // ------------------------------------------------------------------ files
        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.ContainsUris) e.Effects = DragEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.ContainsUris) return;
            var file = e.Data.Uris.Where(u => u.IsFile).Select(u => u.LocalPath).FirstOrDefault();
            if (file != null) Application.Instance.AsyncInvoke(() => OpenPath(file));
        }

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

        private void OpenFileDialog()
        {
            var dlg = new Eto.Forms.OpenFileDialog { Title = "Open a PDF or a picture", MultiSelect = false };
            dlg.Filters.Add(new FileFilter("PDF documents and pictures", ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"));
            dlg.Filters.Add(new FileFilter("PDF documents", ".pdf"));
            dlg.Filters.Add(new FileFilter("Pictures", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"));
            if (dlg.ShowDialog(this) == DialogResult.Ok && !string.IsNullOrEmpty(dlg.FileName)) OpenPath(dlg.FileName);
        }

        public void OpenPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { SetStatus("The file was not found.", StatusText.Level.Error); return; }
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ImageExtensions.Contains(ext)) { ReadPictureFile(path); return; }
            if (ext != ".pdf") { SetStatus("Text X Studio opens PDF documents and pictures (PNG, JPEG, TIFF ...).", StatusText.Level.Warning); return; }
            OpenDocument(path);
        }

        // ------------------------------------------------------------------ busy state
        private bool IsBusy { get { return _cts != null; } }

        /// <summary>Runs a long task with the progress bar and Stop button; one task at a time.</summary>
        private async Task<bool> RunBusy(string message, Func<CancellationToken, Task> work)
        {
            if (IsBusy) { SetStatus("Please wait for the current task to finish (or click Stop).", StatusText.Level.Warning); return false; }
            _cts = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(message);
            try
            {
                await work(_cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                SetStatus("Stopped.", StatusText.Level.Warning);
                return false;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("studio task: " + ex);
                SetStatus(Friendly(ex), StatusText.Level.Error);
                return false;
            }
            finally
            {
                var cts = _cts;
                _cts = null;
                if (cts != null) cts.Dispose();
                SetBusy(false);
            }
        }

        private static string Friendly(Exception ex)
        {
            while (ex is AggregateException && ex.InnerException != null) ex = ex.InnerException;
            var msg = ex.Message;
            if (ex is System.Net.Http.HttpRequestException || msg.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0)
                return "No connection to the translation service. Check the internet connection and try again.";
            return msg;
        }

        private void SetBusy(bool busy)
        {
            OnUi(() =>
            {
                _progress.Visible = busy;
                _progress.Indeterminate = busy;
                _cancel.Visible = busy;
            });
        }

        private void SetProgress(int done, int total)
        {
            OnUi(() =>
            {
                if (total <= 0) { _progress.Indeterminate = true; return; }
                _progress.Indeterminate = false;
                _progress.Value = Math.Max(0, Math.Min(1000, (int)(1000.0 * done / total)));
            });
        }

        private void SetStatus(string text, StatusText.Level level = StatusText.Level.Info)
        {
            OnUi(() => _status.Set(text, level));
        }

        private static void OnUi(Action a)
        {
            var app = Application.Instance;
            if (app == null) return;
            app.AsyncInvoke(() =>
            {
                try { a(); }
                catch (Exception ex) { Diagnostics.Log("studio ui: " + ex); }
            });
        }

        private IProgress<string> StatusProgress()
        {
            return new Progress<string>(s => SetStatus(s));
        }

        private RhinoDoc Doc
        {
            get
            {
                var doc = _docSerial == 0 ? null : RhinoDoc.FromRuntimeSerialNumber(_docSerial);
                return doc ?? RhinoDoc.ActiveDoc;
            }
        }

        private void RunRhinoCommand(string script)
        {
            if (Rhino.Commands.Command.InCommand())
            {
                SetStatus("Finish or cancel the active Rhino command first.", StatusText.Level.Warning);
                return;
            }
            RhinoApp.RunScript(script, false);
        }

        private string AskSavePath(string title, string fileName, string filterName, string extension)
        {
            var dlg = new Eto.Forms.SaveFileDialog { Title = title, FileName = fileName };
            dlg.Filters.Add(new FileFilter(filterName, extension));
            if (dlg.ShowDialog(this) != DialogResult.Ok || string.IsNullOrEmpty(dlg.FileName)) return null;
            var path = dlg.FileName;
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) path += extension;
            return path;
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var s = new string((name ?? "document").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return s.Length == 0 ? "document" : s;
        }

        // ------------------------------------------------------------------ bounds and lifetime
        private void RestoreStudioBounds()
        {
            try
            {
                var parts = (Settings.StudioBounds ?? string.Empty).Split(',');
                if (parts.Length != 4) return;
                var v = parts.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var rect = new Rectangle(v[0], v[1], Math.Max(MinimumSize.Width, v[2]), Math.Max(MinimumSize.Height, v[3]));
                if (!Screen.Screens.Any(sc => sc.WorkingArea.Intersects(new RectangleF(rect.X + 40, rect.Y + 10, 120, 40)))) return;
                Location = rect.Location;
                ClientSize = rect.Size;
            }
            catch
            {
                // default position
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                if (WindowState == WindowState.Normal)
                    Settings.StudioBounds = string.Join(",", new[] { Location.X, Location.Y, ClientSize.Width, ClientSize.Height }.Select(v => v.ToString(CultureInfo.InvariantCulture)));
            }
            catch { /* not important */ }
            if (_cts != null) _cts.Cancel();
            SaveProject();
            StopToolTimers();
            TranslationServices.SaveCache();
            _instance = null;
            base.OnClosed(e);
        }
    }
}
