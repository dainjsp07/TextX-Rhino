using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.UI;
using TextX.Core;
using TextX.Core.Text;
using TextX.Core.Translation;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;

namespace TextX.UI
{
    public sealed class PlacementRequest
    {
        public TranslationRecord Record;
        public TextFormat Format;
        public bool AutoRightAlign;
    }

    public sealed class ApplyRequest
    {
        public uint DocSerial;
        public readonly List<KeyValuePair<Guid, TranslationRecord>> Items = new List<KeyValuePair<Guid, TranslationRecord>>();
        public TextFormat Format;
        public FormatFields Fields;
    }

    /// <summary>
    /// The Text X window (command TR): type or load text, translate in real time, preview with correct
    /// right-to-left rendering, then place text / leaders or update existing objects.
    /// Modeless, so objects can be selected in Rhino while it is open.
    /// Theme: "Graphite and Deep Navy" (follows Rhino's light mode with a light variant).
    /// </summary>
    public sealed class TextXWindow : Form
    {
        private static TextXWindow _instance;
        public static TextXWindow Instance { get { return _instance; } }

        private readonly ObservableCollection<EditItem> _items = new ObservableCollection<EditItem>();
        private readonly EditItem _newItem = new EditItem();
        private EditItem _current;
        private bool _suppress;
        private int _requestId;
        private CancellationTokenSource _cts;
        private readonly UITimer _debounce = new UITimer();
        private readonly UITimer _previewTimer = new UITimer { Interval = 0.25 };
        private readonly PreviewConduit _conduit = new PreviewConduit();
        private uint _docSerial;
        private readonly ThemeScope _scope = new ThemeScope();

        // header
        private readonly KeyPicker _srcLang = new KeyPicker("From", false);
        private readonly KeyPicker _tgtLang = new KeyPicker("To", false);
        private readonly KeyPicker _provider = new KeyPicker(string.Empty, true);

        // objects rail
        private readonly ObjectList _list = new ObjectList();
        private Pill _objectsCount;

        // text page
        private TextArea _source;
        private TextArea _translation;
        private Label _sourceLabel;
        private Label _translationLabel;
        private Pill _rtlPill;
        private Pill _editedPill;
        private readonly ToggleCheck _realtime = new ToggleCheck("Live", 12f);
        private readonly ToggleCheck _viewportPreview = new ToggleCheck("Preview in Rhino viewports");
        private readonly SheetPreview _preview = new SheetPreview();
        private readonly OptionList _mode = new OptionList();

        // print page
        private readonly SegmentedControl _printAlign = new SegmentedControl("Same as screen", "Left", "Center", "Right");
        private readonly ToggleCheck _anchor = new ToggleCheck("Keep the edge of the English text's box");
        private Label _swapInfo;
        private readonly FlatButton _previewPrint = new FlatButton("Show print text in viewports", IconKind.Eye);

        // footer
        private readonly FlatButton _apply = new FlatButton("Update objects", IconKind.Check);
        private readonly StatusText _status = new StatusText();

        // pages
        private readonly TabStrip _tabs = new TabStrip();
        private readonly Panel _page = new Panel();
        private Control[] _pages;
        private Control _updateBar;
        private Label _updateText;

        private readonly FormatPanel _format = new FormatPanel();

        private static TextXSettings Settings { get { return TextXPlugIn.Instance.Options; } }

        public static TextXWindow ShowWindow(RhinoDoc doc)
        {
            if (_instance == null)
            {
                _instance = new TextXWindow(doc);
                _instance.Owner = RhinoEtoApp.MainWindow;
                _instance.Show();
            }
            else
            {
                _instance.BindDocument(doc);
                if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
                _instance.BringToFront();
                _instance.Focus();
            }
            return _instance;
        }

        private TextXWindow(RhinoDoc doc)
        {
            Theme.Refresh();
            Title = BuildInfo.ProductName;
            Resizable = true;
            Minimizable = true;
            Maximizable = false;
            ShowInTaskbar = false;
            MinimumSize = new Size(640, 640);
            ClientSize = new Size(720, 800);
            Padding = new Padding(0);
            this.UseRhinoStyle();
            Icons.ApplyWindowIcon(this);
            // Start with the cursor in the text box.
            Shown += (s, e) =>
            {
                try { _source.Focus(); } catch { /* not focusable yet */ }
            };
            // Focus rings on the custom controls appear only while the keyboard is used to move around.
            KeyDown += (s, e) => { if (e.Key == Keys.Tab) ThemedDrawable.NoteKeyboardNavigation(); };
            _scope.Bind(this, (x, p) => x.BackgroundColor = p.Window);

            BuildLanguageControls();
            Content = BuildLayout();
            RestoreWindowBounds();

            _current = _newItem;
            _newItem.Mode = Settings.DefaultMode;
            _realtime.Checked = Settings.RealTimeTranslation;
            _format.AutoRightAlignRtl = Settings.AutoRightAlignRtl;

            _debounce.Interval = Settings.RealTimeDelayMs / 1000.0;
            _debounce.Elapsed += (s, e) => { _debounce.Stop(); TranslateCurrent(false); };
            _previewTimer.Elapsed += (s, e) => { _previewTimer.Stop(); RebuildViewportPreview(); };

            _source.TextChanged += OnSourceChanged;
            _translation.TextChanged += OnTranslationChanged;
            _realtime.CheckedChanged += (s, e) => Settings.RealTimeTranslation = _realtime.Checked;
            _viewportPreview.CheckedChanged += (s, e) => SchedulePreview();
            _format.Changed += (s, e) => { RefreshSheetPreview(); SchedulePreview(); };
            _mode.SelectedIndexChanged += (s, e) =>
            {
                if (_suppress) return;
                _current.Mode = (TranslationDisplayMode)Math.Max(0, _mode.SelectedIndex);
                RefreshList();
                RefreshSheetPreview();
                SchedulePreview();
            };
            _printAlign.SelectedIndexChanged += (s, e) => { if (_suppress) return; _current.PrintAlign = (PrintAlignment)Math.Max(0, _printAlign.SelectedIndex); };
            _anchor.CheckedChanged += (s, e) => { if (_suppress) return; _current.AnchorToSourceBox = _anchor.Checked; };
            _list.SelectedIndexChanged += (s, e) =>
            {
                int i = _list.SelectedIndex;
                if (i >= 0 && i < _items.Count && _items[i] != _current) ShowItem(_items[i]);
            };
            _list.IncludeToggled += (s, i) =>
            {
                if (i < 0 || i >= _items.Count) return;
                _items[i].Include = !_items[i].Include;
                RefreshList();
                UpdateApplyButton();
                SchedulePreview();
            };
            _list.SelectInRhino += (s, i) => SelectInRhino(i);

            PrintSwapManager.Instance.StateChanged += OnSwapStateChanged;
            UpdateService.Instance.Changed += OnUpdateInfoChanged;
            RhinoDoc.CloseDocument += OnCloseDocument;
            RhinoDoc.ActiveDocumentChanged += OnActiveDocumentChanged;
            RhinoApp.Idle += OnRhinoIdle;

            BindDocument(doc);
            ShowItem(_newItem);
            UpdateLanguageLabels();
            UpdateSwapInfo();
            UpdateUpdateBar();
            SetStatus(BuildInfo.DisplayName + " - type text, or select text / leaders in Rhino and click Load selected.");
            Diagnostics.Log("window constructed");
        }

        // ------------------------------------------------------------------ layout
        private Control BuildLayout()
        {
            var root = new TableLayout { Spacing = new Size(0, 0) };
            root.Rows.Add(new TableRow(BuildHeader()));
            root.Rows.Add(new TableRow(Ui.HLine(_scope)));
            var body = new TableLayout { Spacing = new Size(0, 0) };
            body.Rows.Add(new TableRow(new TableCell(BuildRail(), false), new TableCell(Ui.VLine(_scope), false), new TableCell(BuildMain(), true)) { ScaleHeight = true });
            root.Rows.Add(new TableRow(body) { ScaleHeight = true });
            root.Rows.Add(new TableRow(Ui.HLine(_scope)));
            root.Rows.Add(new TableRow(BuildFooter()));
            return root;
        }

        private Control BuildHeader()
        {
            var swap = new FlatButton(string.Empty, IconKind.Swap, ButtonKind.Soft, 30) { ToolTip = "Swap languages", Backdrop = p => p.Surface, Radius = 7 };
            swap.Click += (s, e) => SwapLanguages();
            var group = new Card(Ui.Row(4, _srcLang, swap, _tgtLang), new Padding(4), p => p.Surface, p => p.Border, 10f);

            _provider.DotColor = p => StatusDot(p);
            _provider.ToolTip = "Translation service (click to change)";
            _provider.ExtraCommands.Add(new KeyValuePair<string, Action>("Service settings...", OpenSettings));

            var settings = new FlatButton(string.Empty, IconKind.Gear, ButtonKind.Secondary, 34) { ToolTip = "Settings (TRSettings)" };
            settings.Click += (s, e) => OpenSettings();
            var about = new FlatButton(string.Empty, IconKind.Info, ButtonKind.Ghost, 34) { ToolTip = "About " + BuildInfo.ProductName + " and updates (TRAbout)" };
            about.Click += (s, e) => AboutDialog.ShowFor(this);

            var row = Ui.Row(10, group, _provider, Ui.Spring(), about, settings);
            var panel = new Panel { Content = row, Padding = new Padding(14, 11) };
            return _scope.Bind(panel, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control BuildRail()
        {
            _objectsCount = new Pill("0", p => p.IconBox, p => p.Text) { Backdrop = p => p.Window };
            var header = Ui.Row(8, Ui.Section(_scope, "Objects"), _objectsCount);

            _list.EmptyText = "No objects loaded.\n\nSelect text or leaders in Rhino, then click Load selected - or type a new text on the right.";
            var scroll = new Scrollable { Content = _list, Border = BorderType.None, ExpandContentWidth = true, ExpandContentHeight = false };
            _scope.Bind(scroll, (x, p) => x.BackgroundColor = p.Window);

            var load = new FlatButton("Load selected", IconKind.Pointer) { Stretch = true, ToolTip = "Load the text and leader objects selected in Rhino" };
            load.Click += (s, e) => LoadSelected();
            var clear = new FlatButton("New text", IconKind.Plus) { Stretch = true, ToolTip = "Stop editing objects and start a new text" };
            clear.Click += (s, e) => ClearItems();
            var translateAll = new FlatButton("Translate all", IconKind.None, ButtonKind.Dashed) { Stretch = true, ToolTip = "Translate every loaded object" };
            translateAll.Click += (s, e) => TranslateAll(false);

            var twoButtons = new TableLayout { Spacing = new Size(6, 0) };
            twoButtons.Rows.Add(new TableRow(new TableCell(clear, true), new TableCell(translateAll, true)));

            var rail = new TableLayout { Spacing = new Size(0, 8), Padding = new Padding(14) };
            rail.Rows.Add(new TableRow(header));
            rail.Rows.Add(new TableRow(scroll) { ScaleHeight = true });
            rail.Rows.Add(new TableRow(load));
            rail.Rows.Add(new TableRow(twoButtons));
            var panel = new Panel { Content = rail, Width = 228 };
            return _scope.Bind(panel, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control BuildMain()
        {
            _tabs.Add("Text", IconKind.Text);
            _tabs.Add("Format", IconKind.Format);
            _tabs.Add("Leader", IconKind.Leader);
            _tabs.Add("Print / PDF", IconKind.Print);

            _pages = new[]
            {
                BuildTextPage(),
                WrapPage(_format.CreateFormatPage(_scope)),
                _format.CreateLeaderPage(_scope),
                BuildPrintPage(),
            };
            _page.Content = _pages[0];
            _scope.Bind(_page, (x, p) => x.BackgroundColor = p.Window);
            _tabs.SelectedIndexChanged += (s, e) => _page.Content = _pages[Math.Max(0, _tabs.SelectedIndex)];

            var main = new TableLayout { Spacing = new Size(0, 0) };
            main.Rows.Add(new TableRow(_tabs));
            main.Rows.Add(new TableRow(BuildUpdateBar()));
            main.Rows.Add(new TableRow(_page) { ScaleHeight = true });
            return _scope.Bind(new Panel { Content = main }, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control WrapPage(Control content)
        {
            var scroll = new Scrollable { Content = content, Border = BorderType.None, ExpandContentWidth = true, ExpandContentHeight = false };
            return _scope.Bind(scroll, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control BuildUpdateBar()
        {
            _updateText = Ui.Label(_scope, string.Empty, 12.5f, false, p => p.Text);
            var download = new FlatButton("Download", IconKind.Download, ButtonKind.Primary, 28) { Backdrop = p => p.AccentSoft };
            download.Click += (s, e) =>
            {
                var latest = UpdateService.Instance.Latest;
                if (latest != null) UpdateService.OpenUrl(latest.BestUrl);
            };
            var later = new FlatButton("Later", IconKind.None, ButtonKind.Ghost, 28) { Backdrop = p => p.AccentSoft };
            later.Click += (s, e) => UpdateService.Instance.Dismiss();
            var row = Ui.Row(8, _updateText, Ui.Spring(), download, later);
            var card = new Card(row, new Padding(12, 6), p => p.AccentSoft, p => p.AccentLine, 8f);
            _updateBar = new Panel { Content = card, Padding = new Padding(14, 10, 14, 0), Visible = false };
            return _scope.Bind((Panel)_updateBar, (x, p) => x.BackgroundColor = p.Window);
        }

        private Control BuildTextPage()
        {
            // ---- source card
            _source = Ui.TextArea(_scope, 15f);
            _sourceLabel = Ui.Label(_scope, "English", 13f, true);
            _realtime.Backdrop = p => p.Surface;
            var translateBtn = new FlatButton("Translate", IconKind.Translate, ButtonKind.Ghost, 26) { Backdrop = p => p.Surface, ToolTip = "Translate now" };
            translateBtn.Click += (s, e) => TranslateCurrent(true);
            var fixSource = new FlatButton("Fix reversed", IconKind.None, ButtonKind.Ghost, 26)
            {
                Backdrop = p => p.Surface,
                ToolTip = "The text is stored backwards (typed or pasted in visual order): restore the reading order.",
            };
            fixSource.Click += (s, e) => { _source.Text = RtlText.VisualToLogical(_source.Text); };
            var sourceCard = TextCard(Ui.Row(6, _sourceLabel, Ui.Spring(), _realtime, translateBtn, fixSource), _source);

            // ---- translation card
            _translation = Ui.TextArea(_scope, 17f);
            _translationLabel = Ui.Label(_scope, "Hebrew", 13f, true);
            _rtlPill = new Pill("right-to-left", p => p.AccentSoft, p => p.AccentInk) { Backdrop = p => p.Surface };
            _editedPill = new Pill("edited", p => p.WarningSoft, p => p.Warning) { Backdrop = p => p.Surface, Visible = false, ToolTip = "The translation was edited by hand; Re-translate discards the edits." };
            var retranslate = new FlatButton("Re-translate", IconKind.Refresh, ButtonKind.Ghost, 26) { Backdrop = p => p.Surface, ToolTip = "Discard manual edits and translate again" };
            retranslate.Click += (s, e) => { _current.TranslationEdited = false; TranslateCurrent(true); };
            var fixTranslation = new FlatButton("Fix reversed", IconKind.None, ButtonKind.Ghost, 26) { Backdrop = p => p.Surface, ToolTip = fixSource.ToolTip };
            fixTranslation.Click += (s, e) => { _translation.Text = RtlText.VisualToLogical(_translation.Text); };
            var translationCard = TextCard(Ui.Row(6, _translationLabel, _rtlPill, _editedPill, Ui.Spring(), retranslate, fixTranslation), _translation);

            // ---- display card
            _mode.Add("English on screen, Hebrew in print", "Recommended");
            _mode.Add("Hebrew on screen and in print");
            _mode.Add("English only (keep translation)");
            _viewportPreview.Backdrop = p => p.Surface;
            _viewportPreview.ToolTip = "Draws what Update would produce on top of the loaded objects (orange).";
            var display = new TableLayout { Spacing = new Size(0, 8) };
            display.Rows.Add(new TableRow(Ui.Section(_scope, "Display")));
            display.Rows.Add(new TableRow(_mode));
            display.Rows.Add(new TableRow(_viewportPreview));
            var displayCard = Ui.SurfaceCard(display, new Padding(12, 10, 12, 12));

            var page = new TableLayout { Spacing = new Size(0, 10), Padding = new Padding(14, 12) };
            page.Rows.Add(new TableRow(sourceCard) { ScaleHeight = true });
            page.Rows.Add(new TableRow(translationCard) { ScaleHeight = true });
            page.Rows.Add(new TableRow(_preview));
            page.Rows.Add(new TableRow(displayCard));
            return _scope.Bind(new Panel { Content = page }, (x, p) => x.BackgroundColor = p.Window);
        }

        private static Control TextCard(Control header, TextArea area)
        {
            var layout = new TableLayout { Spacing = new Size(0, 7) };
            layout.Rows.Add(new TableRow(header));
            layout.Rows.Add(new TableRow(Ui.FieldBox(area)) { ScaleHeight = true });
            var card = Ui.SurfaceCard(layout, new Padding(12, 8, 12, 11));
            card.MinimumSize = new Size(0, 104);
            return card;
        }

        private Control BuildPrintPage()
        {
            _printAlign.ToolTip = "Horizontal alignment of the translation when printing";
            _anchor.Backdrop = p => p.Surface;
            _anchor.ToolTip = "The translation is placed so it keeps the same edge as the English text's box.";
            var align = new TableLayout { Spacing = new Size(0, 8) };
            align.Rows.Add(new TableRow(Ui.Section(_scope, "Alignment in print")));
            align.Rows.Add(new TableRow(_printAlign));
            align.Rows.Add(new TableRow(_anchor));

            _swapInfo = Ui.Label(_scope, string.Empty, 12.5f, false, p => p.Muted, true);
            _previewPrint.Backdrop = p => p.Surface;
            _previewPrint.Click += (s, e) => TogglePrintPreview();
            var exportPdf = new FlatButton("Export PDF with translation...", IconKind.File) { Backdrop = p => p.Surface };
            exportPdf.Click += (s, e) => RunCommand("_TRExportPdf");
            var manage = new FlatButton("Manage translated objects...", IconKind.Layers) { Backdrop = p => p.Surface };
            manage.Click += (s, e) => ManageDialog.ShowFor(RhinoDoc.ActiveDoc, this);
            var restore = new FlatButton("Restore screen text", IconKind.Undo, ButtonKind.Ghost) { Backdrop = p => p.Surface, ToolTip = "Switch every annotation back to its screen text (recovery)" };
            restore.Click += (s, e) => RunCommand("_TRRestore");

            var swap = new TableLayout { Spacing = new Size(0, 8) };
            swap.Rows.Add(new TableRow(Ui.Section(_scope, "Printing and PDF")));
            swap.Rows.Add(new TableRow(_swapInfo));
            swap.Rows.Add(new TableRow(Ui.Row(8, _previewPrint, Ui.Spring())));
            swap.Rows.Add(new TableRow(Ui.Row(8, exportPdf, Ui.Spring())));
            swap.Rows.Add(new TableRow(Ui.Row(8, manage, Ui.Spring())));
            swap.Rows.Add(new TableRow(Ui.Row(8, restore, Ui.Spring())));

            var page = new TableLayout { Spacing = new Size(0, 10), Padding = new Padding(14, 12) };
            page.Rows.Add(new TableRow(Ui.SurfaceCard(align, new Padding(12, 10, 12, 12))));
            page.Rows.Add(new TableRow(Ui.SurfaceCard(swap, new Padding(12, 10, 12, 12))));
            page.Rows.Add(new TableRow { ScaleHeight = true });
            return page;
        }

        private Control BuildFooter()
        {
            var place = new FlatButton("Place text", IconKind.Target, ButtonKind.Primary, 38) { ToolTip = "Pick a point in a viewport or layout to place the text (TRPlaceText)", Backdrop = p => p.Footer };
            place.Click += (s, e) => RunCommand("_TRPlaceText");
            var placeLeader = new FlatButton("Place leader", IconKind.Leader, ButtonKind.Secondary, 38) { ToolTip = "Pick the arrow point and leader points, Enter to finish (TRPlaceLeader)", Backdrop = p => p.Footer };
            placeLeader.Click += (s, e) => RunCommand("_TRPlaceLeader");
            _apply.Backdrop = p => p.Footer;
            _apply.ToolTip = "Write the text and translation into the loaded objects (TRApply)";
            _apply.Click += (s, e) => RunCommand("_TRApply");
            var close = new FlatButton("Close", IconKind.None, ButtonKind.Ghost, 38) { Backdrop = p => p.Footer };
            close.Click += (s, e) => Close();

            var row = Ui.Row(8, place, placeLeader, _apply, new StackLayoutItem(_status, true), close);
            var panel = new Panel { Content = row, Padding = new Padding(14, 11) };
            return _scope.Bind(panel, (x, p) => x.BackgroundColor = p.Footer);
        }

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

            _srcLang.SelectedKey = _srcLang.Contains(Settings.SourceLanguage) ? Settings.SourceLanguage : "en";
            _tgtLang.SelectedKey = _tgtLang.Contains(Settings.TargetLanguage) ? Settings.TargetLanguage : "he";
            _provider.SelectedKey = _provider.Contains(Settings.ProviderId) ? Settings.ProviderId : ProviderIds.GoogleFree;

            _srcLang.SelectedKeyChanged += (s, e) => { if (_suppress) return; Settings.SourceLanguage = SourceLang; UpdateLanguageLabels(); OnLanguagesChanged(); };
            _tgtLang.SelectedKeyChanged += (s, e) => { if (_suppress) return; Settings.TargetLanguage = TargetLang; UpdateLanguageLabels(); OnLanguagesChanged(); };
            _provider.SelectedKeyChanged += (s, e) => { if (_suppress) return; Settings.ProviderId = ProviderId; _lastLevel = StatusText.Level.Info; _provider.Invalidate(); OnLanguagesChanged(); };
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

        // ------------------------------------------------------------------ window bounds
        private void RestoreWindowBounds()
        {
            try
            {
                var parts = (Settings.WindowBounds ?? string.Empty).Split(',');
                if (parts.Length != 4) return;
                var v = parts.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var rect = new Rectangle(v[0], v[1], Math.Max(MinimumSize.Width, v[2]), Math.Max(MinimumSize.Height, v[3]));
                bool visible = Screen.Screens.Any(sc => sc.WorkingArea.Intersects(new RectangleF(rect.X + 40, rect.Y + 10, 120, 40)));
                if (!visible) return;
                Location = rect.Location;
                ClientSize = rect.Size;
            }
            catch
            {
                // use the default position
            }
        }

        private void SaveBounds()
        {
            try
            {
                Settings.WindowBounds = string.Join(",", new[] { Location.X, Location.Y, ClientSize.Width, ClientSize.Height }.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            }
            catch
            {
                // ignore
            }
        }

        // ------------------------------------------------------------------ properties
        public string SourceLang { get { return _srcLang.SelectedKey ?? "en"; } }
        public string TargetLang { get { return _tgtLang.SelectedKey ?? "he"; } }
        public string ProviderId { get { return _provider.SelectedKey ?? ProviderIds.GoogleFree; } }

        /// <summary>The bound document, or null while it is opening/closing or gone.</summary>
        private RhinoDoc Doc
        {
            get
            {
                var doc = RhinoDoc.FromRuntimeSerialNumber(_docSerial);
                return IsUsable(doc) ? doc : null;
            }
        }

        internal static bool IsUsable(RhinoDoc doc)
        {
            if (doc == null) return false;
            try { return !doc.IsClosing && !doc.IsOpening; }
            catch { return false; }
        }

        private void BindDocument(RhinoDoc doc)
        {
            Diagnostics.Log("window.BindDocument");
            if (!IsUsable(doc)) return;
            bool changed = doc.RuntimeSerialNumber != _docSerial;
            _docSerial = doc.RuntimeSerialNumber;
            _format.BindDocument(doc);
            if (changed)
            {
                _items.Clear();
                _format.Load(FormatPanel.DefaultFor(doc));
                _format.ResetDirty();
                ShowItem(_newItem);
                RefreshList();
                UpdateApplyButton();
            }
            UpdateSwapInfo();
        }

        // Document switches are handled on Rhino's idle event, never inside the document events
        // themselves: while a document is closing or a file is being read its tables must not be touched.
        private bool _rebindPending;

        private void OnActiveDocumentChanged(object sender, DocumentEventArgs e)
        {
            Diagnostics.Log("window.OnActiveDocumentChanged");
            _rebindPending = true;
        }

        private void OnCloseDocument(object sender, DocumentEventArgs e)
        {
            Diagnostics.Log("window.OnCloseDocument");
            if (e.Document == null || e.Document.RuntimeSerialNumber != _docSerial) return;
            // only flags here: no Rhino or UI calls while the document is being destroyed
            _docSerial = 0;
            _rebindPending = true;
        }

        private void OnRhinoIdle(object sender, EventArgs e)
        {
            if (!_rebindPending || Rhino.Commands.Command.InCommand()) return;
            var doc = RhinoDoc.ActiveDoc;
            if (!IsUsable(doc)) return;
            _rebindPending = false;
            Diagnostics.Log("window.OnRhinoIdle rebind");
            try
            {
                if (_conduit.Enabled && doc.RuntimeSerialNumber != _docSerial)
                {
                    _conduit.SetItems(null);
                    _conduit.Enabled = false;
                    doc.Views.Redraw();
                }
                if (doc.RuntimeSerialNumber != _docSerial)
                {
                    _items.Clear();
                    _docSerial = 0;
                    ShowItem(_newItem);
                    BindDocument(doc);
                    RefreshList();
                    SchedulePreview();
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
            }
        }

        // ------------------------------------------------------------------ item display
        private void ShowItem(EditItem item)
        {
            _current = item;
            _suppress = true;
            try
            {
                _source.Text = item.Source ?? string.Empty;
                _translation.Text = item.Translation ?? string.Empty;
                _mode.SelectedIndex = (int)item.Mode;
                _printAlign.SelectedIndex = (int)item.PrintAlign;
                _anchor.Checked = item.AnchorToSourceBox;
                _editedPill.Visible = item.TranslationEdited;
                int idx = _items.IndexOf(item);
                _list.SelectedIndex = idx;
            }
            finally
            {
                _suppress = false;
            }
            UpdateDirectionUi();
            UpdateApplyButton();
            RefreshSheetPreview();
            if (!string.IsNullOrEmpty(item.LoadNote)) SetStatus(item.LoadNote, false, StatusText.Level.Warning);
        }

        /// <summary>Rebuilds the rows of the object list from the loaded items.</summary>
        private void RefreshList()
        {
            var rows = new List<ObjectRow>();
            foreach (var item in _items)
            {
                var row = new ObjectRow
                {
                    Title = FirstLine(item.Source, "(empty)"),
                    IsLeader = item.IsLeader,
                    Included = item.Include,
                };
                if (string.IsNullOrWhiteSpace(item.Translation))
                {
                    row.Subtitle = "Not translated yet";
                    row.SubtitleMuted = true;
                    row.Badge = "New";
                    row.BadgeStyle = "muted";
                }
                else
                {
                    row.Subtitle = FirstLine(item.Translation, string.Empty);
                    row.SubtitleRtl = Languages.IsRtl(TargetLang) || RtlText.ContainsRtl(item.Translation);
                    switch (item.Mode)
                    {
                        case TranslationDisplayMode.Dual: row.Badge = "Dual"; row.BadgeStyle = "accent"; break;
                        case TranslationDisplayMode.Translated: row.Badge = Languages.EnglishName(TargetLang); row.BadgeStyle = "success"; break;
                        default: row.Badge = Languages.EnglishName(SourceLang == Languages.AutoDetect ? "en" : SourceLang); row.BadgeStyle = "muted"; break;
                    }
                    if (item.TranslationEdited) row.Badge += " *";
                }
                rows.Add(row);
            }
            _list.SetRows(rows);
            _list.SelectedIndex = _items.IndexOf(_current);
            _objectsCount.Text = _items.Count.ToString(CultureInfo.InvariantCulture);
        }

        private static string FirstLine(string s, string empty)
        {
            if (string.IsNullOrWhiteSpace(s)) return empty;
            var lines = s.Replace("\r", string.Empty).Split('\n');
            return lines.Length > 1 ? lines[0] + " ..." : lines[0];
        }

        private void UpdateLanguageLabels()
        {
            _sourceLabel.Text = SourceLang == Languages.AutoDetect ? "Text (auto-detect)" : Languages.EnglishName(SourceLang);
            _translationLabel.Text = Languages.EnglishName(TargetLang);
            _rtlPill.Visible = Languages.IsRtl(TargetLang);
            UpdateModeLabels();
            UpdateDirectionUi();
            RefreshList();
        }

        private void UpdateModeLabels()
        {
            // the option texts name the languages in use
            var src = SourceLang == Languages.AutoDetect ? "Source text" : Languages.EnglishName(SourceLang);
            var tgt = Languages.EnglishName(TargetLang);
            _modeTexts[0] = src + " on screen, " + tgt + " in print";
            _modeTexts[1] = tgt + " on screen and in print";
            _modeTexts[2] = src + " only (keep translation)";
            _mode.SetTexts(_modeTexts);
        }

        private readonly string[] _modeTexts = new string[3];

        private void UpdateDirectionUi()
        {
            _translation.TextAlignment = Languages.IsRtl(TargetLang) ? TextAlignment.Right : TextAlignment.Left;
            _source.TextAlignment = Languages.IsRtl(SourceLang) || RtlText.ContainsRtl(_source.Text) ? TextAlignment.Right : TextAlignment.Left;
        }

        private void UpdateApplyButton()
        {
            int n = _items.Count(i => i.Include && !i.IsNew);
            _apply.Text = n == 0 ? "Update objects" : (n == 1 ? "Update 1 object" : "Update " + n + " objects");
            _apply.Enabled = n > 0;
            _apply.Invalidate();
        }

        private StatusText.Level _lastLevel = StatusText.Level.Info;

        public void SetStatus(string text, bool error = false)
        {
            SetStatus(text, error, error ? StatusText.Level.Error : StatusText.Level.Info);
        }

        private void SetStatus(string text, bool error, StatusText.Level level)
        {
            _status.Set(text, level);
            if (level == StatusText.Level.Error || level == StatusText.Level.Success || level == StatusText.Level.Warning)
            {
                _lastLevel = level;
                _provider.Invalidate();
            }
        }

        private Color StatusDot(ThemeColors p)
        {
            if (ProviderId == ProviderIds.Manual) return p.Muted;
            if (TranslationEngine.InCooldown(ProviderId)) return p.Warning;
            switch (_lastLevel)
            {
                case StatusText.Level.Error: return p.Error;
                case StatusText.Level.Warning: return p.Warning;
                default: return p.Success;
            }
        }

        private void UpdateSwapInfo()
        {
            var s = Settings;
            var doc = Doc;
            bool preview = doc != null && PrintSwapManager.Instance.IsPreviewOn(doc);
            _previewPrint.Text = preview ? "Show screen text again" : "Show print text in viewports";
            string info = s.AutoSwapEnabled
                ? "Automatic switching is on: annotations set to \"English on screen, translation in print\" switch to the translation during " +
                  string.Join(", ", TextXSettings.SplitList(s.PrintCommands).ToArray()) + " and when exporting " +
                  string.Join(" ", TextXSettings.SplitList(s.PrintLikeExtensions).ToArray()) + " files, then switch back."
                : "Automatic switching is off (TRSettings). Use Export PDF with translation, or the TRPreview command.";
            _swapInfo.Text = info;
        }

        private void OnSwapStateChanged(object sender, EventArgs e)
        {
            Diagnostics.Log("window.OnSwapStateChanged");
            Application.Instance.AsyncInvoke(UpdateSwapInfo);
        }

        private void OnUpdateInfoChanged(object sender, EventArgs e)
        {
            Application.Instance.AsyncInvoke(UpdateUpdateBar);
        }

        private void UpdateUpdateBar()
        {
            var svc = UpdateService.Instance;
            bool show = svc.ShouldNotify;
            if (show) _updateText.Text = BuildInfo.ProductName + " " + svc.Latest.Version + " is available (you have " + BuildInfo.VersionText + ").";
            _updateBar.Visible = show;
        }

        private void OpenSettings()
        {
            var before = Settings.ThemeMode;
            SettingsDialog.ShowFor(this);
            OnSettingsChanged();
            if (Theme.Refresh() || before != Settings.ThemeMode)
            {
                _scope.ApplyAll(this);
                Invalidate();
            }
        }

        // ------------------------------------------------------------------ text editing / translation
        private void OnSourceChanged(object sender, EventArgs e)
        {
            if (_suppress) return;
            _current.Source = _source.Text ?? string.Empty;
            RefreshList();
            UpdateDirectionUi();
            RefreshSheetPreview();
            SchedulePreview();
            if (_realtime.Checked && ProviderId != ProviderIds.Manual && !_current.TranslationEdited)
            {
                _debounce.Stop();
                _debounce.Interval = Settings.RealTimeDelayMs / 1000.0;
                _debounce.Start();
            }
        }

        private void OnTranslationChanged(object sender, EventArgs e)
        {
            if (_suppress) return;
            _current.Translation = _translation.Text ?? string.Empty;
            _current.TranslationEdited = true;
            _editedPill.Visible = true;
            RefreshList();
            RefreshSheetPreview();
            SchedulePreview();
        }

        private void SetTranslationText(EditItem item, string text)
        {
            item.Translation = text ?? string.Empty;
            item.TranslationEdited = false;
            if (item == _current)
            {
                _suppress = true;
                try { _translation.Text = item.Translation; }
                finally { _suppress = false; }
                _editedPill.Visible = false;
                RefreshSheetPreview();
            }
            RefreshList();
            SchedulePreview();
        }

        private void OnLanguagesChanged()
        {
            if (_suppress) return;
            RefreshSheetPreview();
            if (_realtime.Checked && ProviderId != ProviderIds.Manual && !string.IsNullOrWhiteSpace(_current.Source) && !_current.TranslationEdited)
                TranslateCurrent(false);
        }

        private void OnSettingsChanged()
        {
            _realtime.Checked = Settings.RealTimeTranslation;
            _suppress = true;
            try { _provider.SelectedKey = Settings.ProviderId; }
            finally { _suppress = false; }
            UpdateSwapInfo();
            UpdateUpdateBar();
            RefreshSheetPreview();
            SchedulePreview();
        }

        private void SwapLanguages()
        {
            var src = SourceLang;
            if (src == Languages.AutoDetect)
            {
                SetStatus("Choose the source language first (Auto-detect cannot be swapped).", false, StatusText.Level.Warning);
                return;
            }
            var tgt = TargetLang;
            _suppress = true;
            try
            {
                _srcLang.SelectedKey = tgt;
                _tgtLang.SelectedKey = src;
                foreach (var item in _items.Concat(new[] { _newItem }))
                {
                    var t = item.Source;
                    item.Source = item.Translation;
                    item.Translation = t;
                }
            }
            finally
            {
                _suppress = false;
            }
            Settings.SourceLanguage = SourceLang;
            Settings.TargetLanguage = TargetLang;
            UpdateLanguageLabels();
            ShowItem(_current);
            RefreshList();
        }

        private void TranslateCurrent(bool force)
        {
            var item = _current;
            var text = item.Source ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                SetTranslationText(item, string.Empty);
                return;
            }
            if (ProviderId == ProviderIds.Manual)
            {
                if (force) SetStatus("The service is set to Manual: type the translation yourself, or choose a translation service.", false, StatusText.Level.Warning);
                return;
            }
            if (item.TranslationEdited && !force) return;
            if (force) TranslationEngine.ResetCooldowns();
            RunTranslation(new List<EditItem> { item }, new List<string> { text }, force);
        }

        private void TranslateAll(bool force)
        {
            var targets = _items.Where(i => i.Include && !string.IsNullOrWhiteSpace(i.Source) && (force || !i.TranslationEdited)).ToList();
            if (_items.Count == 0 && !string.IsNullOrWhiteSpace(_newItem.Source)) targets.Add(_newItem);
            if (targets.Count == 0)
            {
                SetStatus("Nothing to translate.");
                return;
            }
            RunTranslation(targets, targets.Select(t => t.Source).ToList(), force);
        }

        private void RunTranslation(List<EditItem> items, List<string> texts, bool force)
        {
            if (_cts != null) _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            int id = ++_requestId;
            var src = SourceLang;
            var tgt = TargetLang;
            var providerId = ProviderId;
            TranslationEngine engine;
            try
            {
                engine = TranslationServices.CreateEngine(providerId);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                return;
            }
            SetStatus("Translating" + (items.Count > 1 ? " " + items.Count + " texts" : string.Empty) + " with " + engine.Provider.DisplayName + "...");

            Task.Run(() => engine.TranslateManyAsync(texts, src, tgt, token), token).ContinueWith(t =>
            {
                Application.Instance.AsyncInvoke(() =>
                {
                    if (_instance != this || id != _requestId) return;
                    if (t.IsCanceled) return;
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception == null ? null : t.Exception.GetBaseException();
                        if (ex is OperationCanceledException) return;
                        SetStatus(ex == null ? "Translation failed." : ex.Message, true);
                        return;
                    }
                    var outcome = t.Result;
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].Source != texts[i]) continue; // text changed meanwhile
                        if (items[i].TranslationEdited && !force) continue;
                        items[i].Provider = outcome.ProviderUsed;
                        SetTranslationText(items[i], outcome.Translations[i]);
                    }
                    var details = new List<string>();
                    if (outcome.ServiceSegments > 0) details.Add(outcome.ServiceSegments + " from " + ProviderIds.DisplayName(outcome.ProviderUsed).Split('(')[0].Trim());
                    if (outcome.CachedSegments > 0) details.Add(outcome.CachedSegments + " from memory");
                    if (outcome.GlossarySegments > 0) details.Add(outcome.GlossarySegments + " from glossary");
                    var msg = "Translated in " + (int)outcome.Elapsed.TotalMilliseconds + " ms" + (details.Count > 0 ? " (" + string.Join(", ", details.ToArray()) + ")" : string.Empty) + ".";
                    bool warn = !string.IsNullOrEmpty(outcome.Warning);
                    if (warn) msg += " " + outcome.Warning;
                    SetStatus(msg, false, warn ? StatusText.Level.Warning : StatusText.Level.Success);
                    TranslationServices.SaveCache();
                });
            });
        }

        // ------------------------------------------------------------------ preview
        private void RefreshSheetPreview()
        {
            try
            {
                var fmt = _format.Read();
                var rec = _current.BuildRecord(SourceLang, TargetLang);
                _preview.ScreenText = rec.TextFor(TextState.Display);
                _preview.ScreenLang = rec.LanguageFor(TextState.Display);
                _preview.PrintText = rec.TextFor(TextState.Print);
                _preview.PrintLang = rec.LanguageFor(TextState.Print);
                _preview.FontFace = string.IsNullOrEmpty(fmt.FontFace) ? "Arial" : fmt.FontFace;
                _preview.Bold = fmt.Bold;
                _preview.Italic = fmt.Italic;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("preview failed: " + ex.Message);
            }
            _preview.Invalidate();
        }

        private void SchedulePreview()
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        /// <summary>Builds what Apply would produce for the loaded objects and draws it in the viewports.</summary>
        private void RebuildViewportPreview()
        {
            Diagnostics.Log("window.RebuildViewportPreview");
            var doc = Doc;
            if (!_viewportPreview.Checked || doc == null || _items.Count == 0)
            {
                if (_conduit.Enabled)
                {
                    _conduit.Enabled = false;
                    if (doc != null) doc.Views.Redraw();
                }
                return;
            }
            var cfg = RenderConfig.FromSettings(Settings);
            var fmt = _format.Read();
            var fields = _format.Dirty;
            var list = new List<KeyValuePair<AnnotationBase, Guid>>();
            bool printView = PrintSwapManager.Instance.IsSwapped(doc);
            foreach (var item in _items.Where(i => i.Include && !i.IsNew))
            {
                var obj = doc.Objects.FindId(item.ObjectId);
                if (obj == null) continue;
                try
                {
                    var ann = AnnotationUpdater.BuildUpdatedGeometry(doc, obj, item.BuildRecord(SourceLang, TargetLang), fmt, fields, cfg, printView);
                    if (ann != null) list.Add(new KeyValuePair<AnnotationBase, Guid>(ann, obj.Attributes.Space == ActiveSpace.PageSpace ? obj.Attributes.ViewportId : Guid.Empty));
                }
                catch
                {
                    // ignore preview failures
                }
            }
            _conduit.SetItems(list);
            _conduit.Enabled = list.Count > 0;
            doc.Views.Redraw();
        }

        // ------------------------------------------------------------------ loading objects
        private void LoadSelected()
        {
            var doc = RhinoDoc.ActiveDoc;
            if (!IsUsable(doc)) return;
            BindDocument(doc);
            var objs = doc.Objects.GetSelectedObjects(false, false).Where(AnnotationEngine.IsSupported).ToList();
            if (objs.Count == 0)
            {
                SetStatus("Select one or more text or leader objects in Rhino first (or run TREdit).", false, StatusText.Level.Warning);
                return;
            }
            LoadObjects(doc, objs);
        }

        private void SelectInRhino(int index)
        {
            var doc = Doc;
            if (doc == null || index < 0 || index >= _items.Count) return;
            doc.Objects.UnselectAll();
            doc.Objects.Select(_items[index].ObjectId, true);
            doc.Views.Redraw();
        }

        /// <summary>Loads annotation objects into the editor.</summary>
        public void LoadObjects(RhinoDoc doc, IList<RhinoObject> objects)
        {
            BindDocument(doc);
            var cfg = RenderConfig.FromSettings(Settings);
            _items.Clear();
            string commonTarget = null;
            bool mixedTargets = false;
            int rtlUntracked = 0;
            foreach (var obj in objects)
            {
                var ann = obj.Geometry as AnnotationBase;
                if (ann == null) continue;
                var item = new EditItem { ObjectId = obj.Id, DocSerial = doc.RuntimeSerialNumber, IsLeader = ann is Leader };
                item.OriginalFormat = AnnotationEngine.CaptureFormat(ann, obj.Attributes);
                var rec = AnnotationEngine.ReadRecord(obj.Attributes);
                if (rec != null)
                {
                    var copy = rec.Clone();
                    if (AnnotationEngine.SyncFromObject(ann, copy, cfg))
                        item.LoadNote = "The text of this object was edited in Rhino after it was translated - check the translation.";
                    item.OriginalRecord = rec;
                    item.Source = copy.Source;
                    item.Translation = copy.Translation;
                    item.Mode = copy.Mode;
                    item.PrintAlign = copy.PrintAlign;
                    item.AnchorToSourceBox = copy.AnchorToSourceBox;
                    item.TranslationEdited = copy.ManuallyEdited;
                    item.Provider = copy.Provider;
                    if (commonTarget == null) commonTarget = copy.TargetLang;
                    else if (commonTarget != copy.TargetLang) mixedTargets = true;
                }
                else
                {
                    item.Source = AnnotationEngine.GetText(ann);
                    item.Mode = Settings.DefaultMode;
                    if (RtlText.ContainsRtl(item.Source)) rtlUntracked++;
                }
                _items.Add(item);
            }

            if (commonTarget != null && !mixedTargets && commonTarget != TargetLang && _tgtLang.Contains(commonTarget))
            {
                _suppress = true;
                try { _tgtLang.SelectedKey = commonTarget; }
                finally { _suppress = false; }
                Settings.TargetLanguage = TargetLang;
                UpdateLanguageLabels();
            }

            RefreshList();
            if (_items.Count > 0)
            {
                _format.Load(_items[0].OriginalFormat);
                _format.ResetDirty();
                ShowItem(_items[0]);
            }
            UpdateApplyButton();
            var msg = "Loaded " + _items.Count + " object(s).";
            if (mixedTargets) msg += " The objects use different target languages; Update uses the language selected above.";
            if (rtlUntracked > 0) msg += " " + rtlUntracked + " object(s) already contain right-to-left text: if it reads backwards, use Fix reversed.";
            SetStatus(msg, false, mixedTargets || rtlUntracked > 0 ? StatusText.Level.Warning : StatusText.Level.Info);
            SchedulePreview();
            if (_realtime.Checked && ProviderId != ProviderIds.Manual)
            {
                // translate untranslated objects automatically, but not big selections (service quotas):
                // for those the user clicks "Translate all"
                var untranslated = _items.Where(i => string.IsNullOrEmpty(i.Translation) && !string.IsNullOrWhiteSpace(i.Source)).ToList();
                if (untranslated.Count > 0 && untranslated.Count <= 10)
                    RunTranslation(untranslated, untranslated.Select(i => i.Source).ToList(), false);
                else if (untranslated.Count > 10)
                    SetStatus(msg + " Click Translate all to translate the " + untranslated.Count + " untranslated objects.");
            }
        }

        private void ClearItems()
        {
            _items.Clear();
            ShowItem(_newItem);
            RefreshList();
            UpdateApplyButton();
            SchedulePreview();
            var doc = Doc;
            if (doc != null)
            {
                _format.Load(FormatPanel.DefaultFor(doc));
                _format.ResetDirty();
            }
            SetStatus("Editing a new text. Use Place text or Place leader to add it to the model or a layout.");
        }

        // ------------------------------------------------------------------ requests for commands
        public PlacementRequest CreatePlacementRequest()
        {
            var item = _current;
            if (string.IsNullOrWhiteSpace(item.Source) && string.IsNullOrWhiteSpace(item.Translation)) return null;
            var rec = item.BuildRecord(SourceLang == Languages.AutoDetect ? "en" : SourceLang, TargetLang);
            return new PlacementRequest { Record = rec, Format = _format.Read(), AutoRightAlign = _format.AutoRightAlignRtl };
        }

        public ApplyRequest CreateApplyRequest()
        {
            var req = new ApplyRequest { DocSerial = _docSerial, Format = _format.Read(), Fields = _format.Dirty };
            var src = SourceLang == Languages.AutoDetect ? "en" : SourceLang;
            foreach (var item in _items.Where(i => i.Include && !i.IsNew))
                req.Items.Add(new KeyValuePair<Guid, TranslationRecord>(item.ObjectId, item.BuildRecord(src, TargetLang)));
            return req;
        }

        /// <summary>Called by the apply command after objects were updated.</summary>
        public void OnObjectsApplied(RhinoDoc doc, int count)
        {
            var objs = _items.Select(i => doc.Objects.FindId(i.ObjectId)).Where(o => o != null).ToList();
            int selected = _list.SelectedIndex;
            LoadObjects(doc, objs);
            if (selected >= 0 && selected < _items.Count) ShowItem(_items[selected]);
            SetStatus("Updated " + count + " object(s). Undo is available in Rhino.", false, StatusText.Level.Success);
        }

        public void OnPlaced(string what)
        {
            SetStatus(what + " placed. Edit the text to place another, or load objects to update them.", false, StatusText.Level.Success);
        }

        private void RunCommand(string script)
        {
            if (Rhino.Commands.Command.InCommand())
            {
                SetStatus("Finish or cancel the active Rhino command first.", false, StatusText.Level.Warning);
                return;
            }
            RhinoApp.RunScript(script, false);
        }

        private void TogglePrintPreview()
        {
            RunCommand("_TRPreview _Toggle");
            UpdateSwapInfo();
        }

        // ------------------------------------------------------------------ lifetime
        protected override void OnClosed(EventArgs e)
        {
            Diagnostics.Log("window.OnClosed");
            SaveBounds();
            _debounce.Stop();
            _previewTimer.Stop();
            if (_cts != null) _cts.Cancel();
            _conduit.Enabled = false;
            var doc = Doc;
            if (doc != null) doc.Views.Redraw();
            PrintSwapManager.Instance.StateChanged -= OnSwapStateChanged;
            UpdateService.Instance.Changed -= OnUpdateInfoChanged;
            RhinoDoc.CloseDocument -= OnCloseDocument;
            RhinoDoc.ActiveDocumentChanged -= OnActiveDocumentChanged;
            RhinoApp.Idle -= OnRhinoIdle;
            TranslationServices.SaveCache();
            _instance = null;
            base.OnClosed(e);
        }
    }
}
