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
using TextX.Core.Translation;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Studio
{
    /// <summary>
    /// Text X Studio, the Text X window (commands TR and TRStudio). Home holds the translator for Rhino text
    /// and leaders (type, dictate or load text, check the writing, place text or a leader, update objects);
    /// the other pages translate PDF documents keeping their layout, read product schedules from drawings,
    /// and read the text of pictures and screenshots. No account and no setup.
    /// </summary>
    public sealed partial class StudioWindow : Form
    {
        private static StudioWindow _instance;
        public static StudioWindow Instance { get { return _instance; } }

        private readonly ThemeScope _scope = new ThemeScope();
        private readonly KeyPicker _srcLang = new KeyPicker("From", false);
        private readonly KeyPicker _tgtLang = new KeyPicker("To", false);
        private readonly KeyPicker _provider = new KeyPicker(string.Empty, true);
        private readonly NavRail _tabs = new NavRail();
        private Label _pageTitle, _pageHint;
        private readonly Panel _page = new Panel();
        private readonly StatusText _status = new StatusText();
        private readonly ProgressBar _progress = new ProgressBar { MinValue = 0, MaxValue = 1000, Visible = false, Width = 160 };
        private FlatButton _cancel;
        private Panel _toast;
        private readonly UITimer _toastTimer = new UITimer();
        private Panel _updateBar;
        private Label _updateText;
        private FlatButton _updateInstall, _updateDownload, _updateNotes, _updateLater;
        private Control[] _pages;
        private CancellationTokenSource _cts;
        private bool _suppress;
        private uint _docSerial;

        private DocProject _project;
        private ScheduleSet _schedules;

        private const int HomeTab = 0, DocumentTab = 1, SchedulesTab = 2, PictureTab = 3;

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
            MinimumSize = new Size(760, 600);
            ClientSize = new Size(1280, 760);
            Padding = new Padding(0);
            this.UseRhinoStyle();
            Icons.ApplyWindowIcon(this);
            KeyDown += (s, e) => { if (e.Key == Keys.Tab) ThemedDrawable.NoteKeyboardNavigation(); };
            _scope.Bind(this, (x, p) => x.BackgroundColor = p.Window);
            if (doc != null) _docSerial = doc.RuntimeSerialNumber;

            DocumentServices.Configure();
            BuildLanguageControls();
            Content = BuildLayout();
            // the window can be resized by the system (a screen smaller than the saved size) without SizeChanged
            SizeChanged += (s, e) => FitHeader();
            Shown += (s, e) => FitHeader();
            _page.SizeChanged += (s, e) => FitHeader();
            RestoreStudioBounds();
            ShowHome();
            // once the window is on screen, the Home figures show what they do
            Shown += (s, e) => Application.Instance.AsyncInvoke(() =>
            {
                if (_tabs.SelectedIndex == HomeTab) TourTiles();
                try { if (_tabs.SelectedIndex == HomeTab) _qSource.Focus(); } catch { /* not focusable yet */ }
            });
            _toastTimer.Elapsed += (s, e) => { _toastTimer.Stop(); if (!IsBusy) _toast.Visible = false; };
            try { UpdateService.Instance.Changed += OnUpdateInfoChanged; } catch (Exception ex) { Diagnostics.Log("studio updates: " + ex.Message); }
            UpdateUpdateBar();
            Diagnostics.Log("studio constructed");
        }

        // ------------------------------------------------------------------ layout
        private static readonly string[] PageTitles = { "Text X Studio", "Document", "Schedules", "Picture or screenshot" };
        private static readonly string[] PageHints =
        {
            "Translate the text in Rhino, documents, drawings and pictures.",
            "Translate a PDF and keep its layout. Click any text to edit it.",
            "Products, materials, finishes and title blocks from your drawings.",
            "Its text is read on this computer and translated.",
        };

        private Control BuildLayout()
        {
            _tabs.Add("Home", IconKind.Home);
            _tabs.Add("Document", IconKind.File);
            _tabs.Add("Schedules", IconKind.Grid);
            _tabs.Add("Picture or screenshot", IconKind.Picture);
            _tabs.SetBottom("Settings (translation service, theme, updates ...)", IconKind.Gear);
            _tabs.BottomClicked += (s, e) => OpenSettings();
            _pages = new[] { BuildHome(), BuildDocument(), BuildSchedules(), BuildTools() };
            _page.Content = _pages[0];
            _scope.Bind(_page, (x, p) => x.BackgroundColor = p.Window);
            _tabs.SelectedIndexChanged += (s, e) => ShowTab(_tabs.SelectedIndex);

            var content = new TableLayout { Spacing = new Size(0, 0) };
            content.Rows.Add(new TableRow(BuildHeader()));
            content.Rows.Add(new TableRow(BuildUpdateBar()));
            content.Rows.Add(new TableRow(_page) { ScaleHeight = true });
            content.Rows.Add(new TableRow(BuildFooter()));

            var root = new TableLayout { Spacing = new Size(0, 0) };
            root.Rows.Add(new TableRow(new TableCell(_tabs, false), new TableCell(content, true)) { ScaleHeight = true });

            // files can be dropped anywhere on the window
            var dropTarget = new Panel { Content = root, AllowDrop = true };
            dropTarget.DragEnter += OnDragOver;
            dropTarget.DragOver += OnDragOver;
            dropTarget.DragDrop += OnDragDrop;
            return _scope.Bind(dropTarget, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control BuildHeader()
        {
            _pageTitle = new Label { Text = PageTitles[0], Font = Theme.DisplayFont(20f), VerticalAlignment = VerticalAlignment.Center, Wrap = WrapMode.None };
            _scope.Bind(_pageTitle, (x, p) => x.TextColor = p.Text);
            _pageHint = Ui.Label(_scope, PageHints[0], 12f, false, p => p.Muted);
            _pageHint.Wrap = WrapMode.None;
            var titles = new TableLayout { Spacing = new Size(0, 0) };
            titles.Rows.Add(new TableRow(_pageTitle));
            titles.Rows.Add(new TableRow(_pageHint));

            var swap = new FlatButton(string.Empty, IconKind.Swap, ButtonKind.Strong, 24) { ToolTip = "Swap languages", Backdrop = p => p.Surface, IconSize = 12f };
            swap.Click += (s, e) => SwapLanguages();
            var languages = new Card(Ui.Row(4, _srcLang, swap, _tgtLang), new Padding(8, 2), p => p.Surface, p => p.Border, 16f);
            _provider.DotColor = p => Settings.ProviderId == ProviderIds.Manual ? p.Muted : TranslationEngine.InCooldown(Settings.ProviderId) ? p.Warning : p.Success;
            _provider.ToolTip = "Translation service, for every page (click to change)";
            _provider.ExtraCommands.Add(new KeyValuePair<string, Action>("Service settings...", OpenSettings));
            var open = new FlatButton("Open a file", IconKind.Plus, ButtonKind.Strong, 32) { ToolTip = "Open a PDF or a picture (you can also drop files on the window)" };
            open.Click += (s, e) => OpenFileDialog();
            _openFile = open;
            var row = new TableLayout { Spacing = new Size(12, 0) };
            row.Rows.Add(new TableRow(new TableCell(titles, true), new TableCell(Ui.Row(8, languages, _provider, open), false)));
            var panel = new Panel { Content = row, Padding = new Padding(4, 12, 16, 8) };
            return _scope.Bind(panel, (x, p) => x.BackgroundColor = p.Window);
        }

        private FlatButton _openFile;
        private bool? _headerSmall;

        /// <summary>Small windows: the page hint goes and Open a file keeps only its icon, so the title stays on one line.</summary>
        private void FitHeader()
        {
            if (_pageHint == null || _openFile == null) return;
            int w = ClientSize.Width - _tabs.Width;
            if (w <= 0) return;
            _pageHint.Visible = w >= 950;
            FitDocumentToolbar(w);
            bool small = w < 820;
            if (_headerSmall == small) return;
            _headerSmall = small;
            _openFile.PaddingX = small ? 0 : 14;
            _openFile.Text = small ? string.Empty : "Open a file";
        }

        /// <summary>
        /// Messages appear in a pill under the page for a few seconds, then go; while a long task runs the pill
        /// stays with its progress bar and Stop button.
        /// </summary>
        private Control BuildFooter()
        {
            _status.Backdrop = p => p.Surface;
            _cancel = new FlatButton("Stop", IconKind.Close, ButtonKind.Strong, 24) { Visible = false, Backdrop = p => p.Surface };
            _cancel.Click += (s, e) => { if (_cts != null) _cts.Cancel(); };
            var close = new FlatButton(string.Empty, IconKind.Close, ButtonKind.Ghost, 24) { Backdrop = p => p.Surface, ToolTip = "Hide", IconSize = 12f };
            close.Click += (s, e) => { if (!IsBusy) _toast.Visible = false; };
            var row = Ui.Row(8, new StackLayoutItem(_status, true), _progress, _cancel, close);
            var pill = new Card(row, new Padding(12, 4, 6, 4), p => p.Surface, p => p.Border, 16f);
            _toast = new Panel { Content = pill, Padding = new Padding(4, 0, 16, 12), Visible = false };
            return _scope.Bind(_toast, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control BuildUpdateBar()
        {
            _updateText = Ui.Label(_scope, string.Empty, 12.5f, false, p => p.Text);
            _updateInstall = new FlatButton("Install update", IconKind.Download, ButtonKind.Primary, 28) { Backdrop = p => p.AccentSoft };
            _updateInstall.Click += (s, e) => InstallUpdateNow();
            _updateDownload = new FlatButton("Download", IconKind.Download, ButtonKind.Primary, 28) { Backdrop = p => p.AccentSoft };
            _updateDownload.Click += (s, e) =>
            {
                var latest = UpdateService.Instance.Latest;
                if (latest != null) UpdateService.OpenUrl(latest.BestUrl);
            };
            _updateNotes = new FlatButton("What's new", IconKind.None, ButtonKind.Ghost, 28) { Backdrop = p => p.AccentSoft };
            _updateNotes.Click += (s, e) =>
            {
                var latest = UpdateService.Instance.Latest;
                UpdateService.OpenUrl(latest != null && !string.IsNullOrEmpty(latest.PageUrl) ? latest.PageUrl : BuildInfo.Website);
            };
            _updateLater = new FlatButton("Later", IconKind.None, ButtonKind.Ghost, 28) { Backdrop = p => p.AccentSoft };
            _updateLater.Click += (s, e) => UpdateService.Instance.Dismiss();
            var row = Ui.Row(8, _updateText, Ui.Spring(), _updateInstall, _updateDownload, _updateNotes, _updateLater);
            var card = new Card(row, new Padding(14, 4), p => p.AccentSoft, p => p.AccentLine, 18f);
            _updateBar = new Panel { Content = card, Padding = new Padding(4, 0, 16, 8), Visible = false };
            return _scope.Bind(_updateBar, (x, p) => x.BackgroundColor = p.Window);
        }

        private void OnUpdateInfoChanged(object sender, EventArgs e)
        {
            OnUi(UpdateUpdateBar);
        }

        private void UpdateUpdateBar()
        {
            try
            {
                var svc = UpdateService.Instance;
                bool installing = svc.IsInstalling;
                bool available = svc.ShouldNotify;
                bool restart = !available && svc.ShouldShowRestartNotice;
                if (installing)
                    _updateText.Text = "Downloading and installing " + BuildInfo.ProductName + " " + (svc.Latest != null ? svc.Latest.Version.ToString() : string.Empty) + "...";
                else if (available)
                    _updateText.Text = BuildInfo.ProductName + " " + svc.Latest.Version + " is available (you have " + BuildInfo.VersionText + ").";
                else if (restart)
                    _updateText.Text = BuildInfo.ProductName + " " + svc.PendingVersion + " is installed. Restart Rhino to start using it.";
                _updateInstall.Visible = available && !installing && svc.CanInstallLatest;
                _updateDownload.Visible = available && !installing && !svc.CanInstallLatest;
                _updateNotes.Visible = restart && !installing;
                _updateLater.Visible = !installing;
                _updateLater.Text = restart ? "OK" : "Later";
                _updateBar.Visible = installing || available || restart;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("studio update bar: " + ex.Message);
            }
        }

        private void InstallUpdateNow()
        {
            var svc = UpdateService.Instance;
            var latest = svc.Latest;
            if (latest == null || svc.IsInstalling) return;
            svc.InstallAsync(latest, CancellationToken.None).ContinueWith(t => OnUi(() =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception == null ? null : t.Exception.GetBaseException();
                    SetStatus("Update failed: " + (ex == null ? "unknown error" : ex.Message), StatusText.Level.Error);
                }
                else
                {
                    SetStatus(BuildInfo.ProductName + " " + latest.Version + " is installed. Restart Rhino to start using it.", StatusText.Level.Success);
                }
                UpdateUpdateBar();
            }));
            UpdateUpdateBar();
        }

        private void OpenSettings()
        {
            var before = Settings.ThemeMode;
            SettingsDialog.ShowFor(this);
            _suppress = true;
            try { if (_provider.Contains(Settings.ProviderId)) _provider.SelectedKey = Settings.ProviderId; }
            finally { _suppress = false; }
            _provider.Invalidate();
            if (_qLive != null) _qLive.Checked = Settings.RealTimeTranslation;
            if (Theme.Refresh() || before != Settings.ThemeMode)
            {
                _scope.ApplyAll(this);
                Invalidate();
            }
            UpdateUpdateBar();
            if (_qPrintInfo != null) UpdateQPrintInfo();
            RefreshQPreviews();
        }

        private void ShowTab(int index)
        {
            if (index < 0 || index >= _pages.Length) return;
            if (_tabs.SelectedIndex != index) { _tabs.SelectedIndex = index; return; }
            _page.Content = _pages[index];
            if (_pageTitle != null)
            {
                _pageTitle.Text = index == DocumentTab && _project != null ? _project.Name : PageTitles[index];
                _pageHint.Text = PageHints[index];
            }
            if (index == HomeTab) { RefreshHistory(); if (Loaded) TourTiles(); }
            if (index == PictureTab) UpdateToolLabels();
            if (index == DocumentTab) RefreshDocument();
            if (index == SchedulesTab) RefreshSchedules();
            // an empty page's figure walks through what will happen, once the page is on screen
            if (Loaded)
                Application.Instance.AsyncInvoke(() =>
                {
                    if (index == DocumentTab && _project == null && _docEmptyFigure != null) _docEmptyFigure.Play(1, 300);
                    if (index == SchedulesTab && _schedules == null && _scheduleFigure != null) _scheduleFigure.Play(1, 300);
                });
        }

        private void ShowHome()
        {
            ShowTab(HomeTab);
            RefreshHistory();
        }

        // ------------------------------------------------------------------ languages
        private string SourceLang { get { return _srcLang.SelectedKey ?? Languages.AutoDetect; } }
        private string TargetLang { get { return _tgtLang.SelectedKey ?? "he"; } }

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
            _provider.ShowSecondary = false;
            _provider.SetItems(ProviderIds.All.Select(id => new PickerItem { Key = id, Text = ShortProviderName(id), Secondary = ProviderDetail(id) }));
            // one language pair for the whole app: Rhino text, documents and pictures
            _suppress = true;
            _srcLang.SelectedKey = _srcLang.Contains(Settings.SourceLanguage) ? Settings.SourceLanguage : Languages.AutoDetect;
            _tgtLang.SelectedKey = _tgtLang.Contains(Settings.TargetLanguage) ? Settings.TargetLanguage : "he";
            _provider.SelectedKey = _provider.Contains(Settings.ProviderId) ? Settings.ProviderId : ProviderIds.GoogleFree;
            _suppress = false;
            _srcLang.SelectedKeyChanged += (s, e) => { if (_suppress) return; Settings.SourceLanguage = SourceLang; OnLanguagesChanged(); };
            _tgtLang.SelectedKeyChanged += (s, e) => { if (_suppress) return; Settings.TargetLanguage = TargetLang; OnLanguagesChanged(); };
            _provider.SelectedKeyChanged += (s, e) =>
            {
                if (_suppress) return;
                Settings.ProviderId = _provider.SelectedKey ?? ProviderIds.GoogleFree;
                SetStatus("Translation service for every page: " + ShortProviderName(Settings.ProviderId) + ".");
                OnLanguagesChanged();
            };
        }

        private static string ShortProviderName(string id)
        {
            switch (id)
            {
                case ProviderIds.GoogleFree: return "Google (free)";
                case ProviderIds.GoogleCloud: return "Google Cloud";
                case ProviderIds.DeepL: return "DeepL";
                case ProviderIds.Azure: return "Microsoft Azure";
                case ProviderIds.Claude: return "Claude";
                case ProviderIds.LibreTranslate: return "LibreTranslate";
                case ProviderIds.MyMemory: return "MyMemory";
                case ProviderIds.Manual: return "Manual";
                default: return ProviderIds.DisplayName(id) ?? id;
            }
        }

        private static string ProviderDetail(string id)
        {
            var full = ProviderIds.DisplayName(id) ?? string.Empty;
            int open = full.IndexOf('(');
            return open >= 0 ? full.Substring(open).Trim('(', ')', ' ') : string.Empty;
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
            Settings.SourceLanguage = SourceLang;
            Settings.TargetLanguage = TargetLang;
            OnTranslatorLanguagesSwapped();
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
            OnTranslatorLanguagesChanged();
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
                if (busy) { _toastTimer.Stop(); _toast.Visible = true; }
                else ScheduleToastHide(_status.Text);
            });
        }

        private void ScheduleToastHide(string text)
        {
            _toastTimer.Stop();
            _toastTimer.Interval = Math.Min(9.0, 3.0 + 0.045 * (text ?? string.Empty).Length);
            _toastTimer.Start();
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
            OnUi(() =>
            {
                _status.Set(text, level);
                if (string.IsNullOrEmpty(text)) { if (!IsBusy) _toast.Visible = false; return; }
                _toast.Visible = true;
                if (!IsBusy) ScheduleToastHide(text);
                _provider.Invalidate();
            });
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
            StopTranslator();
            _toastTimer.Stop();
            try { UpdateService.Instance.Changed -= OnUpdateInfoChanged; } catch (Exception) { /* not attached */ }
            TranslationServices.SaveCache();
            _instance = null;
            base.OnClosed(e);
        }
    }
}
