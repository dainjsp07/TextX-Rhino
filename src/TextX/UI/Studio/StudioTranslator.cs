using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using TextX.Core;
using TextX.Core.Text;
using TextX.Core.Tools;
using TextX.Core.Translation;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Studio
{
    /// <summary>
    /// The translator on Home (what the Text X window did): type, dictate or load text and leaders from
    /// Rhino, translate as you type, check the writing, choose what shows on screen and in print, then
    /// place text or a leader, or update the loaded objects. Rhino's side is in <see cref="TranslatorBridge"/>.
    /// </summary>
    public sealed partial class StudioWindow
    {
        /// <summary>Set by the plug-in at load: creates the Rhino side of the translator.</summary>
        public static Func<TranslatorBridge> BridgeFactory;

        private TranslatorBridge _bridge;
        private readonly List<TranslatorItem> _qItems = new List<TranslatorItem>();
        private readonly TranslatorItem _qNew = new TranslatorItem();
        private TranslatorItem _qCurrent;

        private TextArea _qSource, _qTarget;
        private Label _qSourceLabel, _qTargetLabel, _qIssueNote, _qPrintInfo;
        private Pill _qRtl, _qEdited;
        private HeroSwitch _qLive;
        private HeroChip _qVoice, _qCheck, _qObjects, _qLoad, _qLayer, _qProps, _qUpdate, _qLeader, _qViewport;
        private FlatButton _qPlace, _qPrintPreview;
        private readonly HeroChip[] _qModes = new HeroChip[3];
        private LinePreview _qScreen, _qPrint;
        private Panel _qBody, _qIssuesHost, _qModesHost;
        private Control _qFields, _qPropsPanel;
        private StackLayout _qIssueRow;
        private SegmentedControl _qPrintAlign;
        private ToggleCheck _qAnchor;

        private readonly UITimer _qDebounce = new UITimer { Interval = 0.6 };
        private readonly UITimer _qPreviewTimer = new UITimer { Interval = 0.25 };
        private int _qRequest;
        private CancellationTokenSource _qCts;
        private bool _qSuppress, _qSuppressIssues, _qListening;
        private List<WritingIssue> _qIssues = new List<WritingIssue>();
        private string _qIssueLanguage = string.Empty;

        // ------------------------------------------------------------------ public surface (commands and the Rhino side)
        /// <summary>Opens Studio on Home with the translator (TR).</summary>
        public static StudioWindow ShowTranslator(Rhino.RhinoDoc doc)
        {
            var w = ShowWindow(doc, HomeTab);
            if (w._qBody != null && w._qBody.Content != w._qFields) w.ToggleProperties(false);
            return w;
        }

        public TranslatorBridge Bridge { get { return _bridge; } }

        /// <summary>The text being edited: a new text or one of the loaded objects.</summary>
        public TranslatorItem CurrentTranslatorItem { get { return _qCurrent; } }

        /// <summary>The text and leaders loaded from Rhino.</summary>
        public IList<TranslatorItem> TranslatorItems { get { return _qItems; } }

        public bool ViewportPreviewOn { get { return _qViewport != null && _qViewport.On; } }

        /// <summary>The source language to record with a text (Auto-detect is resolved from its script).</summary>
        public string RecordSourceLanguage(string text)
        {
            if (SourceLang != Languages.AutoDetect) return SourceLang;
            return Core.Documents.LanguageGuess.FromScript(text ?? string.Empty) ?? "en";
        }

        public string TranslatorTargetLanguage { get { return TargetLang; } }

        public void TranslatorMessage(string text, StatusText.Level level)
        {
            SetStatus(text, level);
        }

        /// <summary>Called by the Rhino side when objects were loaded (Load selected, TR, TREdit).</summary>
        public void SetTranslatorItems(IList<TranslatorItem> items, string commonTarget, string message, StatusText.Level level)
        {
            _qItems.Clear();
            _qItems.AddRange(items);
            if (!string.IsNullOrEmpty(commonTarget) && commonTarget != TargetLang && _tgtLang.Contains(commonTarget))
            {
                _suppress = true;
                try { _tgtLang.SelectedKey = commonTarget; }
                finally { _suppress = false; }
                Settings.TargetLanguage = TargetLang;
                UpdateQLanguageLabels();
            }
            if (_tabs.SelectedIndex != HomeTab) ShowTab(HomeTab);
            if (_qBody.Content != _qFields) ToggleProperties(false);
            ShowTranslatorItem(_qItems.Count > 0 ? _qItems[0] : _qNew);
            UpdateQObjects();
            SetStatus(message, level);
            SchedulePreview();
            if (_qLive.Checked && Settings.ProviderId != ProviderIds.Manual)
            {
                // untranslated objects are translated at once, but not big selections (service quotas)
                var untranslated = _qItems.Where(i => string.IsNullOrEmpty(i.Translation) && !string.IsNullOrWhiteSpace(i.Source)).ToList();
                if (untranslated.Count > 0 && untranslated.Count <= 10)
                    RunQTranslation(untranslated, untranslated.Select(i => i.Source).ToList(), false);
                else if (untranslated.Count > 10)
                    SetStatus(message + " Open the objects menu and click Translate all to translate the " + untranslated.Count + " untranslated objects.", StatusText.Level.Info);
            }
        }

        /// <summary>The Rhino side switched documents: the loaded objects belonged to the old one.</summary>
        public void ForgetTranslatorItems()
        {
            if (_qItems.Count == 0) return;
            _qItems.Clear();
            ShowTranslatorItem(_qNew);
            UpdateQObjects();
        }

        /// <summary>Starts a new text from another page (text read from a picture), ready to place.</summary>
        public void StartNewText(string source, string translation)
        {
            if (_qItems.Count > 0) ClearTranslatorItems();
            _qNew.Source = source ?? string.Empty;
            _qNew.Translation = translation ?? string.Empty;
            _qNew.TranslationEdited = false;
            ShowTab(HomeTab);
            if (_qBody.Content != _qFields) ToggleProperties(false);
            ShowTranslatorItem(_qNew);
            SchedulePreview();
            if (string.IsNullOrEmpty(translation) && !string.IsNullOrWhiteSpace(source)) TranslateCurrentItem(false);
            SetStatus("The text is on Home. Use Place text or Place leader to add it to the model or a layout.", StatusText.Level.Success);
        }

        /// <summary>Shows one of the loaded objects (after Update reloads them).</summary>
        public void ShowTranslatorItemAt(int index)
        {
            if (index >= 0 && index < _qItems.Count) ShowTranslatorItem(_qItems[index]);
        }

        /// <summary>Printing or the print preview switched the annotations' text.</summary>
        public void OnPrintStateChanged()
        {
            UpdateQPrintInfo();
        }

        public void OnTranslatorPlaced(string what)
        {
            SetStatus(what + " placed. Edit the text to place another, or load objects to update them.", StatusText.Level.Success);
        }

        // ------------------------------------------------------------------ layout
        private Control BuildTranslator()
        {
            _bridge = BridgeFactory != null ? BridgeFactory() : new TranslatorBridge();
            _qCurrent = _qNew;
            try { _qNew.Mode = Settings.DefaultMode; } catch (Exception) { /* settings without a default mode */ }

            // top: what it does, and what is loaded from Rhino
            var title1 = new Label { Text = "Translate anything.", Font = Theme.DisplayFont(16f), VerticalAlignment = VerticalAlignment.Center };
            _scope.Bind(title1, (x, p) => x.TextColor = p.OnHero);
            var title2 = new Label { Text = "Keep every layout.", Font = Theme.DisplayFont(16f), VerticalAlignment = VerticalAlignment.Center };
            _scope.Bind(title2, (x, p) => x.TextColor = p.Accent);
            _qObjects = new HeroChip("No objects loaded", IconKind.Layers, HeroChipStyle.Quiet, 28) { ToolTip = "The text and leaders loaded from Rhino: choose the one to edit, skip one, or translate them all", MaxTextWidth = 220 };
            _qObjects.Click += (s, e) => ShowObjectsMenu(_qObjects);
            _qLoad = new HeroChip("Load selected", IconKind.Pointer, HeroChipStyle.Outline, 28) { ToolTip = "Load the text and leaders selected in Rhino (TREdit)" };
            _qLoad.Click += (s, e) => _bridge.LoadSelected();
            var top = Ui.Row(8, title1, title2, Ui.Spring(), _qObjects, _qLoad);

            // the two texts
            _qSource = HeroTextArea(14f);
            _qTarget = HeroTextArea(14f);
            _qSourceLabel = HeroLabel(string.Empty, 11f, true, p => HeroInk.Muted(p));
            _qTargetLabel = HeroLabel(string.Empty, 11f, true, p => p.Accent);
            _qRtl = new Pill("right-to-left", p => HeroInk.Chip(p), p => p.OnHero) { Backdrop = p => HeroInk.Field(p) };
            _qEdited = new Pill("edited", p => p.WarningSoft, p => p.Warning) { Backdrop = p => HeroInk.Field(p), Visible = false, ToolTip = "The translation was edited by hand; Re-translate discards the edits." };
            _qLive = new HeroSwitch("Live") { Backdrop = p => HeroInk.Field(p), ToolTip = "Translate as you type" };
            _qLive.Init(Settings.RealTimeTranslation);
            _qLive.CheckedChanged += (s, e) => Settings.RealTimeTranslation = _qLive.Checked;

            _qVoice = new HeroChip("Voice typing", IconKind.Mic, HeroChipStyle.Outline, 24) { Backdrop = p => HeroInk.Field(p), ToolTip = "Windows voice typing writes what you say; the translation follows as you speak (Win + H)" };
            _qVoice.Click += (s, e) => ToggleVoiceTyping();
            _qCheck = new HeroChip("Check writing", IconKind.Check, HeroChipStyle.Outline, 24) { Backdrop = p => HeroInk.Field(p), ToolTip = "Spelling and grammar suggestions; click one to apply it (LanguageTool, free, no account)" };
            _qCheck.Click += (s, e) => CheckTranslatorWriting();
            var fixSource = new HeroChip("Fix reversed", IconKind.None, HeroChipStyle.Quiet, 24) { Backdrop = p => HeroInk.Field(p), ToolTip = "The text is stored backwards (typed or pasted in visual order): restore the reading order." };
            fixSource.Click += (s, e) => { _qSource.Text = RtlText.VisualToLogical(_qSource.Text); };
            var retranslate = new HeroChip("Re-translate", IconKind.Refresh, HeroChipStyle.Quiet, 24) { Backdrop = p => HeroInk.Field(p), ToolTip = "Discard manual edits and translate again" };
            retranslate.Click += (s, e) => { _qCurrent.TranslationEdited = false; TranslateCurrentItem(true); };
            var fixTarget = new HeroChip("Fix reversed", IconKind.None, HeroChipStyle.Quiet, 24) { Backdrop = p => HeroInk.Field(p), ToolTip = fixSource.ToolTip };
            fixTarget.Click += (s, e) => { _qTarget.Text = RtlText.VisualToLogical(_qTarget.Text); };

            var sourceField = HeroField(Ui.Row(6, _qSourceLabel, Ui.Spring()), _qSource, Ui.Row(6, _qVoice, _qCheck, Ui.Spring(), fixSource), false);
            var targetField = HeroField(Ui.Row(6, _qTargetLabel, _qRtl, _qEdited, Ui.Spring(), _qLive), _qTarget, Ui.Row(6, Ui.Spring(), retranslate, fixTarget), true);
            var swap = new HeroChip(string.Empty, IconKind.Swap, HeroChipStyle.Outline, 32) { ToolTip = "Swap languages" };
            swap.Click += (s, e) => SwapLanguages();
            var swapColumn = new TableLayout();
            swapColumn.Rows.Add(new TableRow { ScaleHeight = true });
            swapColumn.Rows.Add(new TableRow(swap));
            swapColumn.Rows.Add(new TableRow { ScaleHeight = true });
            var fields = new TableLayout { Spacing = new Size(8, 0) };
            fields.Rows.Add(new TableRow(new TableCell(sourceField, true), new TableCell(swapColumn, false), new TableCell(targetField, true)) { ScaleHeight = true });
            _qFields = fields;

            // writing suggestions, under the texts while they are shown
            _qIssueRow = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center };
            _qIssueNote = HeroLabel(string.Empty, 12f, false, p => HeroInk.Muted(p));
            var closeIssues = new HeroChip(string.Empty, IconKind.Close, HeroChipStyle.Outline, 24) { ToolTip = "Close the suggestions" };
            closeIssues.Click += (s, e) => ShowIssues(null);
            var issues = new TableLayout { Spacing = new Size(8, 0) };
            issues.Rows.Add(new TableRow(new TableCell(HeroLabel(Theme.Spaced("Check writing"), 11f, true, p => p.Accent), false), new TableCell(_qIssueRow, false),
                new TableCell(_qIssueNote, true), new TableCell(closeIssues, false)));
            _qIssuesHost = new Panel { Content = issues, Visible = false };

            // what shows on screen and in print
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _qModes[i] = new HeroChip(string.Empty, IconKind.None, HeroChipStyle.Outline, 28);
                _qModes[i].Click += (s, e) => SetQMode(index);
            }
            _qViewport = new HeroChip("Preview in Rhino", IconKind.Eye, HeroChipStyle.Outline, 28) { ToolTip = "Draws what Update would do on the loaded objects in the Rhino viewports (orange)", Visible = _bridge.InRhino };
            _qViewport.Click += (s, e) => { _qViewport.On = !_qViewport.On; SchedulePreview(); };
            _qModesHost = new Panel { Content = Ui.Row(6, _qModes[0], _qModes[1], _qModes[2], Ui.Spring(), _qViewport) };

            _qScreen = new LinePreview("On screen", false);
            _qPrint = new LinePreview("In print · PDF", true);
            var previews = new TableLayout { Spacing = new Size(8, 0) };
            previews.Rows.Add(new TableRow(new TableCell(_qScreen, true), new TableCell(_qPrint, true)));

            // actions
            _qPlace = new FlatButton("Place text", IconKind.Target, ButtonKind.Primary, 32) { Backdrop = p => p.Hero, ToolTip = "Pick a point in a viewport or layout to place the text (TRPlaceText)" };
            _qPlace.Click += (s, e) => PlaceFromTranslator("_TRPlaceText");
            _qLeader = new HeroChip("Place leader", IconKind.Leader, HeroChipStyle.Light, 32) { ToolTip = "Pick the arrow point and the leader points, Enter to finish (TRPlaceLeader)" };
            _qLeader.Click += (s, e) => PlaceFromTranslator("_TRPlaceLeader");
            _qUpdate = new HeroChip("Update objects", IconKind.Check, HeroChipStyle.Outline, 32) { ToolTip = "Write the text and translation into the loaded objects (TRApply). Undo works in Rhino." };
            _qUpdate.Click += (s, e) => RunRhinoCommand("_TRApply");
            _qLayer = new HeroChip("Layer · Current layer", IconKind.Layers, HeroChipStyle.Outline, 32) { MaxTextWidth = 190, ToolTip = "The layer for the text and leaders you place: Rhino's current layer, or any layer of the open file" };
            _qLayer.Click += (s, e) => ShowLayerMenu(_qLayer);
            _qProps = new HeroChip("Text properties", IconKind.Format, HeroChipStyle.Outline, 32) { ToolTip = "Rhino's text properties: annotation style, font, height, alignment, mask, color and leader" };
            _qProps.Click += (s, e) => ToggleProperties(_qBody.Content == _qFields);
            var actions = Ui.Row(8, _qPlace, _qLeader, _qUpdate, Ui.Spring(), _qLayer, _qProps);

            _qPropsPanel = BuildTextProperties();
            _qBody = new Panel { Content = _qFields, MinimumSize = new Size(0, 132) };

            var layout = new TableLayout { Spacing = new Size(0, 8) };
            layout.Rows.Add(new TableRow(top));
            layout.Rows.Add(new TableRow(_qBody) { ScaleHeight = true });
            layout.Rows.Add(new TableRow(_qIssuesHost));
            layout.Rows.Add(new TableRow(_qModesHost));
            layout.Rows.Add(new TableRow(previews));
            layout.Rows.Add(new TableRow(actions));
            var card = new Card(layout, new Padding(16, 12, 16, 12), p => p.Hero, p => p.IsDark ? p.Border : p.Hero, 20f);

            // behaviour
            _qDebounce.Interval = Math.Max(0.15, Settings.RealTimeDelayMs / 1000.0);
            _qDebounce.Elapsed += (s, e) => { _qDebounce.Stop(); TranslateCurrentItem(false); };
            _qPreviewTimer.Elapsed += (s, e) => { _qPreviewTimer.Stop(); _bridge.ItemsChanged(); };
            _qSource.TextChanged += OnQSourceChanged;
            _qTarget.TextChanged += OnQTargetChanged;
            _bridge.StyleChanged += (s, e) => { RefreshQPreviews(); UpdateQLayer(); SchedulePreview(); };
            _bridge.Attach(this);

            ShowTranslatorItem(_qNew);
            UpdateQLanguageLabels();
            UpdateQObjects();
            UpdateQLayer();
            return card;
        }

        private TextArea HeroTextArea(float px)
        {
            var t = new TextArea
            {
                AcceptsReturn = true,
                AcceptsTab = false,
                Wrap = true,
                SpellCheck = false,
                Border = BorderType.None,
                Font = Theme.UiFont(px),
            };
            return _scope.Bind(t, (x, p) =>
            {
                x.BackgroundColor = HeroInk.Field(p);
                x.TextColor = p.OnHero;
            });
        }

        private Label HeroLabel(string text, float px, bool bold, Func<ThemeColors, Color> color)
        {
            return Ui.Label(_scope, text, px, bold, color);
        }

        /// <summary>A text field on the card: a label row, the text box, and a row of small actions.</summary>
        private static Control HeroField(Control header, TextArea area, Control footer, bool accent)
        {
            var layout = new TableLayout { Spacing = new Size(0, 4) };
            layout.Rows.Add(new TableRow(header));
            layout.Rows.Add(new TableRow(area) { ScaleHeight = true });
            layout.Rows.Add(new TableRow(footer));
            return new Card(layout, new Padding(12, 8, 12, 8), p => HeroInk.Field(p), p => accent ? p.AccentLine : HeroInk.InputLine(p), 16f) { Backdrop = p => p.Hero };
        }

        /// <summary>Rhino's text and leader properties, and the print tools, in place of the texts.</summary>
        private Control BuildTextProperties()
        {
            var format = _bridge.CreateFormatSections(_scope) ?? PropertiesPlaceholder("Text, alignment, mask, layer and color",
                "In Rhino: the annotation style, font, height, bold, italic, underline, alignment, rotation, wrapping, mask, layer and color of the text you place or update.");
            var leader = _bridge.CreateLeaderSections(_scope) ?? PropertiesPlaceholder("Leader", "In Rhino: arrowhead, arrow size, leader curve, landing and text alignment.");

            _qPrintAlign = new SegmentedControl("Same", "Left", "Center", "Right") { ToolTip = "Horizontal alignment of the translation in print", Height = 32 };
            _qPrintAlign.SelectedIndexChanged += (s, e) =>
            {
                if (_qSuppress) return;
                _qCurrent.PrintAlign = (PrintAlignment)Math.Max(0, _qPrintAlign.SelectedIndex);
                RefreshQPreviews();
                SchedulePreview();
            };
            _qAnchor = new ToggleCheck("Keep the edge of the source text's box", 12.5f) { Backdrop = p => p.Surface, ToolTip = "The translation is placed so it keeps the same edge as the source text's box." };
            _qAnchor.CheckedChanged += (s, e) => { if (_qSuppress) return; _qCurrent.AnchorToSourceBox = _qAnchor.Checked; SchedulePreview(); };
            _qPrintInfo = Ui.Label(_scope, _bridge.PrintInfo(), 12f, false, p => p.Muted, true);
            var export = new FlatButton("Export PDF with translation", IconKind.Print, ButtonKind.Strong, 28) { Backdrop = p => p.Surface, ToolTip = "Pick the layouts, then a file name (TRExportPdf)" };
            export.Click += (s, e) => RunRhinoCommand("_TRExportPdf");
            _qPrintPreview = new FlatButton("Show print text in viewports", IconKind.Eye, ButtonKind.Secondary, 28) { Backdrop = p => p.Surface, ToolTip = "Switch the annotations to their print text to check them (TRPreview)" };
            _qPrintPreview.Click += (s, e) => { RunRhinoCommand("_TRPreview _Toggle"); UpdateQPrintInfo(); };
            var manage = new FlatButton("Manage translated objects", IconKind.Layers, ButtonKind.Secondary, 28) { Backdrop = p => p.Surface, ToolTip = "Find, select and change every translated object of the file (TRManage)" };
            manage.Click += (s, e) => _bridge.ManageObjects();
            var restore = new FlatButton("Restore screen text", IconKind.Undo, ButtonKind.Ghost, 28) { Backdrop = p => p.Surface, ToolTip = "Switch every annotation back to its screen text (recovery, TRRestore)" };
            restore.Click += (s, e) => RunRhinoCommand("_TRRestore");

            var print = new TableLayout { Spacing = new Size(0, 8) };
            print.Rows.Add(new TableRow(Ui.Section(_scope, "Print and PDF")));
            print.Rows.Add(new TableRow(Ui.Label(_scope, "Alignment of the translation in print", 12f, false, p => p.Muted)));
            print.Rows.Add(new TableRow(_qPrintAlign));
            print.Rows.Add(new TableRow(_qAnchor));
            print.Rows.Add(new TableRow(_qPrintInfo));
            print.Rows.Add(new TableRow(Ui.Row(8, export, Ui.Spring())));
            print.Rows.Add(new TableRow(Ui.Row(8, _qPrintPreview, Ui.Spring())));
            print.Rows.Add(new TableRow(Ui.Row(8, manage, restore, Ui.Spring())));
            var printCard = Ui.SurfaceCard(print, new Padding(12, 10, 12, 12));

            var right = new TableLayout { Spacing = new Size(0, 0) };
            right.Rows.Add(new TableRow(leader));
            right.Rows.Add(new TableRow(new Panel { Content = printCard, Padding = new Padding(14, 0, 14, 12) }));
            right.Rows.Add(new TableRow { ScaleHeight = true });
            var columns = new TableLayout { Spacing = new Size(0, 0) };
            columns.Rows.Add(new TableRow(new TableCell(format, true), new TableCell(right, true)));
            var scroll = new Scrollable { Content = columns, Border = BorderType.None, ExpandContentWidth = true, ExpandContentHeight = false };
            _scope.Bind(scroll, (x, p) => x.BackgroundColor = p.Window);

            var done = new FlatButton("Done", IconKind.Check, ButtonKind.Strong, 28) { Backdrop = p => p.Window };
            done.Click += (s, e) => ToggleProperties(false);
            var note = Ui.Label(_scope, "Used by Place text, Place leader and Update objects.", 12f, false, p => p.Muted);
            var head = Ui.Row(10, Ui.Section(_scope, "Text properties"), note, Ui.Spring(), done);
            var layout = new TableLayout { Spacing = new Size(0, 0) };
            layout.Rows.Add(new TableRow(new Panel { Content = head, Padding = new Padding(16, 10, 10, 2) }));
            layout.Rows.Add(new TableRow(scroll) { ScaleHeight = true });
            return new Card(layout, new Padding(0, 0, 0, 6), p => p.Window, null, 16f) { Backdrop = p => p.Hero };
        }

        private Control PropertiesPlaceholder(string title, string text)
        {
            var body = new TableLayout { Spacing = new Size(0, 8) };
            body.Rows.Add(new TableRow(Ui.Section(_scope, title)));
            body.Rows.Add(new TableRow(Ui.Label(_scope, text, 12.5f, false, p => p.Muted, true)));
            return new Panel { Content = Ui.SurfaceCard(body, new Padding(12, 10, 12, 12)), Padding = new Padding(14, 12, 14, 12) };
        }

        private void ToggleProperties(bool open)
        {
            if (_qBody == null) return;
            _qBody.Content = open ? _qPropsPanel : _qFields;
            _qModesHost.Visible = !open;
            if (open) _qIssuesHost.Visible = false;
            _qProps.On = open;
            if (open) UpdateQPrintInfo();
        }

        private void UpdateQPrintInfo()
        {
            if (_qPrintInfo == null) return;
            _qPrintInfo.Text = _bridge.PrintInfo();
            _qPrintPreview.Text = _bridge.PrintPreviewOn ? "Show screen text again" : "Show print text in viewports";
        }

        // ------------------------------------------------------------------ the text being edited
        private void ShowTranslatorItem(TranslatorItem item)
        {
            _qCurrent = item;
            _qSuppress = true;
            try
            {
                _qSource.Text = item.Source ?? string.Empty;
                _qTarget.Text = item.Translation ?? string.Empty;
                if (_qPrintAlign != null) _qPrintAlign.SelectedIndex = (int)item.PrintAlign;
                if (_qAnchor != null) _qAnchor.Checked = item.AnchorToSourceBox;
                _qEdited.Visible = item.TranslationEdited;
            }
            finally
            {
                _qSuppress = false;
            }
            ShowIssues(null);
            UpdateQModes();
            UpdateQDirection();
            UpdateQObjects();
            RefreshQPreviews();
            if (!string.IsNullOrEmpty(item.LoadNote)) SetStatus(item.LoadNote, StatusText.Level.Warning);
        }

        private void UpdateQObjects()
        {
            if (_qObjects == null) return;
            int n = _qItems.Count;
            string text = n == 0 ? "No objects loaded" : (n == 1 ? "1 object loaded" : n + " objects loaded");
            if (n > 1 && !_qCurrent.IsNew) text += " · editing " + (_qItems.IndexOf(_qCurrent) + 1);
            _qObjects.Text = text;
            int k = _qItems.Count(i => i.Include && !i.IsNew);
            _qUpdate.Text = k == 0 ? "Update objects" : (k == 1 ? "Update 1 object" : "Update " + k + " objects");
            _qUpdate.Enabled = k > 0;
        }

        private void UpdateQLanguageLabels()
        {
            if (_qSourceLabel == null) return;
            _qSourceLabel.Text = Theme.Spaced(SourceLang == Languages.AutoDetect ? "Text · auto-detect" : Languages.EnglishName(SourceLang));
            _qTargetLabel.Text = Theme.Spaced(Languages.EnglishName(TargetLang));
            _qRtl.Visible = Languages.IsRtl(TargetLang);
            UpdateQModes();
            UpdateQDirection();
            RefreshQPreviews();
        }

        private static readonly string[] ModeTips =
        {
            "Recommended for layouts: one drawing, two languages (the translation is switched in when you print or export a PDF)",
            "Replaces the text everywhere",
            "Keeps the translation for later",
        };

        private void UpdateQModes()
        {
            if (_qModes[0] == null) return;
            var src = SourceLang == Languages.AutoDetect ? "Source" : Languages.EnglishName(SourceLang);
            var tgt = Languages.EnglishName(TargetLang);
            var texts = new[] { src + " on screen, " + tgt + " in print", tgt + " on screen and in print", src + " only (keep the translation)" };
            for (int i = 0; i < 3; i++)
            {
                _qModes[i].Text = texts[i];
                _qModes[i].ToolTip = ModeTips[i];
                _qModes[i].On = (int)_qCurrent.Mode == i;
            }
        }

        private void SetQMode(int index)
        {
            _qCurrent.Mode = (TranslationDisplayMode)index;
            UpdateQModes();
            RefreshQPreviews();
            SchedulePreview();
        }

        private void UpdateQDirection()
        {
            _qTarget.TextAlignment = Languages.IsRtl(TargetLang) ? TextAlignment.Right : TextAlignment.Left;
            _qSource.TextAlignment = (SourceLang != Languages.AutoDetect && Languages.IsRtl(SourceLang)) || RtlText.ContainsRtl(_qSource.Text) ? TextAlignment.Right : TextAlignment.Left;
        }

        private void RefreshQPreviews()
        {
            if (_qScreen == null || _qCurrent == null) return;
            try
            {
                var style = _bridge.ReadStyle() ?? new LineStyle();
                var rec = _qCurrent.BuildRecord(RecordSourceLanguage(_qCurrent.Source), TargetLang);
                _qScreen.Set(rec.TextFor(TextState.Display), rec.LanguageFor(TextState.Display), style, style.HAlign);
                int printAlign = _qCurrent.PrintAlign == PrintAlignment.Inherit ? style.HAlign : (int)_qCurrent.PrintAlign - 1;
                _qPrint.Set(rec.TextFor(TextState.Print), rec.LanguageFor(TextState.Print), style, printAlign);
            }
            catch (Exception ex)
            {
                Diagnostics.Log("translator preview: " + ex.Message);
            }
        }

        private void SchedulePreview()
        {
            _qPreviewTimer.Stop();
            _qPreviewTimer.Start();
        }

        private void OnQSourceChanged(object sender, EventArgs e)
        {
            if (_qSuppress) return;
            _qCurrent.Source = _qSource.Text ?? string.Empty;
            if (!_qSuppressIssues && _qIssuesHost.Visible) ShowIssues(null);
            UpdateQObjects();
            UpdateQDirection();
            RefreshQPreviews();
            SchedulePreview();
            if (_qLive.Checked && Settings.ProviderId != ProviderIds.Manual && !_qCurrent.TranslationEdited)
            {
                _qDebounce.Stop();
                _qDebounce.Interval = Math.Max(0.15, Settings.RealTimeDelayMs / 1000.0);
                _qDebounce.Start();
            }
        }

        private void OnQTargetChanged(object sender, EventArgs e)
        {
            if (_qSuppress) return;
            _qCurrent.Translation = _qTarget.Text ?? string.Empty;
            _qCurrent.TranslationEdited = true;
            _qEdited.Visible = true;
            RefreshQPreviews();
            SchedulePreview();
        }

        private void SetQTranslation(TranslatorItem item, string text)
        {
            item.Translation = text ?? string.Empty;
            item.TranslationEdited = false;
            if (item == _qCurrent)
            {
                _qSuppress = true;
                try { _qTarget.Text = item.Translation; }
                finally { _qSuppress = false; }
                _qEdited.Visible = false;
                RefreshQPreviews();
            }
            SchedulePreview();
        }

        /// <summary>Called when the languages at the top change.</summary>
        private void OnTranslatorLanguagesChanged()
        {
            UpdateQLanguageLabels();
            if (_qLive != null && _qLive.Checked && Settings.ProviderId != ProviderIds.Manual && !string.IsNullOrWhiteSpace(_qCurrent.Source) && !_qCurrent.TranslationEdited)
                TranslateCurrentItem(false);
        }

        /// <summary>The languages were swapped: each text and its translation change places.</summary>
        private void OnTranslatorLanguagesSwapped()
        {
            foreach (var item in _qItems.Concat(new[] { _qNew }))
            {
                var t = item.Source;
                item.Source = item.Translation;
                item.Translation = t;
            }
            ShowTranslatorItem(_qCurrent);
            UpdateQLanguageLabels();
        }

        private void ClearTranslatorItems()
        {
            _qItems.Clear();
            ShowTranslatorItem(_qNew);
            UpdateQObjects();
            SchedulePreview();
            _bridge.ResetFormat();
            SetStatus("Editing a new text. Use Place text or Place leader to add it to the model or a layout.");
        }

        private void PlaceFromTranslator(string command)
        {
            if (string.IsNullOrWhiteSpace(_qCurrent.Source) && string.IsNullOrWhiteSpace(_qCurrent.Translation))
            {
                SetStatus("Type a text first, or load text from Rhino with Load selected.", StatusText.Level.Warning);
                _qSource.Focus();
                return;
            }
            RunRhinoCommand(command);
        }

        // ------------------------------------------------------------------ translation
        private void TranslateCurrentItem(bool force)
        {
            var item = _qCurrent;
            var text = item.Source ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) { SetQTranslation(item, string.Empty); return; }
            if (Settings.ProviderId == ProviderIds.Manual)
            {
                if (force) SetStatus("The service is set to Manual: type the translation yourself, or choose a translation service at the top.", StatusText.Level.Warning);
                return;
            }
            if (item.TranslationEdited && !force) return;
            if (force) TranslationEngine.ResetCooldowns();
            RunQTranslation(new List<TranslatorItem> { item }, new List<string> { text }, force);
        }

        private void TranslateAllItems(bool force)
        {
            var targets = _qItems.Where(i => i.Include && !string.IsNullOrWhiteSpace(i.Source) && (force || !i.TranslationEdited)).ToList();
            if (_qItems.Count == 0 && !string.IsNullOrWhiteSpace(_qNew.Source)) targets.Add(_qNew);
            if (targets.Count == 0) { SetStatus("Nothing to translate."); return; }
            RunQTranslation(targets, targets.Select(t => t.Source).ToList(), force);
        }

        private void RunQTranslation(List<TranslatorItem> items, List<string> texts, bool force)
        {
            if (_qCts != null) _qCts.Cancel();
            _qCts = new CancellationTokenSource();
            var token = _qCts.Token;
            int id = ++_qRequest;
            string src = SourceLang, tgt = TargetLang;
            TranslationEngine engine;
            try { engine = TranslationServices.CreateEngine(); }
            catch (Exception ex) { SetStatus(ex.Message, StatusText.Level.Error); return; }
            if (items.Count > 1) SetStatus("Translating " + items.Count + " texts with " + engine.Provider.DisplayName + "...");

            Task.Run(() => engine.TranslateManyAsync(texts, src, tgt, token), token).ContinueWith(t =>
            {
                Application.Instance.AsyncInvoke(() =>
                {
                    if (_instance != this || id != _qRequest || t.IsCanceled) return;
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception == null ? null : t.Exception.GetBaseException();
                        if (ex is OperationCanceledException) return;
                        SetStatus(ex == null ? "Translation failed." : Friendly(ex), StatusText.Level.Error);
                        return;
                    }
                    var outcome = t.Result;
                    for (int i = 0; i < items.Count && i < outcome.Translations.Count; i++)
                    {
                        if (items[i].Source != texts[i]) continue; // the text changed meanwhile
                        if (items[i].TranslationEdited && !force) continue;
                        items[i].Provider = outcome.ProviderUsed;
                        SetQTranslation(items[i], outcome.Translations[i]);
                    }
                    if (!string.IsNullOrEmpty(outcome.Warning)) SetStatus(outcome.Warning, StatusText.Level.Warning);
                    else if (items.Count > 1) SetStatus("Translated " + items.Count + " texts.", StatusText.Level.Success);
                    TranslationServices.SaveCache();
                });
            });
        }

        // ------------------------------------------------------------------ voice typing and writing check
        private void ToggleVoiceTyping()
        {
            if (_tabs.SelectedIndex != HomeTab) ShowTab(HomeTab);
            if (_qBody.Content != _qFields) ToggleProperties(false);
            _qListening = !_qListening;
            _qVoice.On = _qListening;
            _qVoice.Pulse = _qListening;
            _qVoice.Text = _qListening ? "Listening" : "Voice typing";
            _qSource.Focus();
            _qSource.CaretIndex = (_qSource.Text ?? string.Empty).Length;
            bool listening = _qListening;
            // the text box has the keyboard focus before Windows voice typing starts or stops
            var timer = new UITimer { Interval = 0.25 };
            timer.Elapsed += (s, e) =>
            {
                timer.Stop();
                bool ok = DocumentServices.StartVoiceTyping();
                if (!listening) { SetStatus("Voice typing stopped."); return; }
                if (ok) SetStatus("Speak now: Windows voice typing writes into the text and the translation follows. Click Listening (or Win + H) to stop.");
                else SetStatus("Press Win + H to start Windows voice typing in the text box.", StatusText.Level.Warning);
            };
            timer.Start();
        }

        private async void CheckTranslatorWriting()
        {
            var text = _qSource.Text ?? string.Empty;
            if (text.Trim().Length == 0) { SetStatus("Type the text to check first.", StatusText.Level.Warning); _qSource.Focus(); return; }
            _qCheck.On = true;
            SetStatus("Checking the writing (LanguageTool, free, no account)...");
            WritingResult result = null;
            string lang = SourceLang;
            try
            {
                result = await Task.Run(() => WritingCheck.CheckAsync(text, lang, CancellationToken.None));
            }
            catch (Exception ex)
            {
                SetStatus(Friendly(ex), StatusText.Level.Error);
            }
            finally
            {
                _qCheck.On = false;
            }
            if (result == null || (_qSource.Text ?? string.Empty) != text) return;
            _qIssueLanguage = result.LanguageName ?? string.Empty;
            ShowIssues(result.Issues);
            SetStatus(result.Issues.Count == 0 ? "No problems found" + (_qIssueLanguage.Length > 0 ? " (" + _qIssueLanguage + ")." : ".")
                                               : result.Issues.Count + (result.Issues.Count == 1 ? " suggestion" : " suggestions") + ": click one to apply it.",
                result.Issues.Count == 0 ? StatusText.Level.Success : StatusText.Level.Info);
        }

        /// <summary>Shows the writing suggestions as chips under the texts (null hides them).</summary>
        private void ShowIssues(List<WritingIssue> issues)
        {
            if (_qIssuesHost == null) return;
            _qIssueRow.Items.Clear();
            if (issues == null) { _qIssues = new List<WritingIssue>(); _qIssuesHost.Visible = false; return; }
            _qIssues = issues;
            var text = _qSource.Text ?? string.Empty;
            foreach (var issue in issues.Take(5))
            {
                var bad = issue.Offset >= 0 && issue.Length > 0 && issue.Offset + issue.Length <= text.Length ? text.Substring(issue.Offset, issue.Length) : string.Empty;
                string label;
                if (issue.Best == null) label = string.IsNullOrEmpty(issue.ShortMessage) ? issue.Category : issue.ShortMessage;
                else if (issue.Best.Trim().Length == 0) label = "Remove \"" + bad.Trim() + "\"";
                else label = bad.Length > 0 ? bad.Trim() + "  →  " + issue.Best : issue.Best;
                var chip = new HeroChip(label, IconKind.None, HeroChipStyle.Outline, 24)
                {
                    MaxTextWidth = 200,
                    ToolTip = issue.Message + (issue.Best != null ? "  Click to apply." : string.Empty),
                };
                var captured = issue;
                chip.Click += (s, e) => ApplyIssue(captured);
                _qIssueRow.Items.Add(chip);
            }
            int n = issues.Count;
            _qIssueNote.Text = n == 0 ? "No problems found" + (_qIssueLanguage.Length > 0 ? " (" + _qIssueLanguage + ")" : string.Empty)
                : (n > 5 ? "+" + (n - 5) + " more after these.  " : string.Empty) + "Click one to apply it.";
            _qIssuesHost.Visible = _qBody.Content == _qFields;
        }

        private void ApplyIssue(WritingIssue issue)
        {
            if (issue.Best == null) { SetStatus(issue.Message); return; }
            var text = _qSource.Text ?? string.Empty;
            if (issue.Offset < 0 || issue.Offset + issue.Length > text.Length) return;
            var fixedText = text.Substring(0, issue.Offset) + issue.Best + text.Substring(issue.Offset + issue.Length);
            var remaining = WritingCheck.AfterApplying(_qIssues, issue, issue.Best);
            _qSuppressIssues = true;
            try { _qSource.Text = fixedText; }
            finally { _qSuppressIssues = false; }
            ShowIssues(remaining);
            SetStatus(remaining.Count == 0 ? "Applied. No more suggestions." : "Applied. " + remaining.Count + (remaining.Count == 1 ? " suggestion left." : " suggestions left."), StatusText.Level.Success);
        }

        // ------------------------------------------------------------------ menus
        private static string MenuText(string s)
        {
            return (s ?? string.Empty).Replace("&", "&&");
        }

        private void ShowObjectsMenu(Control anchor)
        {
            var menu = new ContextMenu();
            if (_qItems.Count == 0)
            {
                menu.Items.Add(new ButtonMenuItem { Text = "No objects loaded", Enabled = false });
                var load = new ButtonMenuItem { Text = "Load the text and leaders selected in Rhino" };
                load.Click += (s, e) => _bridge.LoadSelected();
                menu.Items.Add(load);
                menu.Show(anchor);
                return;
            }
            for (int i = 0; i < _qItems.Count; i++)
            {
                var item = _qItems[i];
                var text = (item == _qCurrent ? "●  " : "     ") + (i + 1) + ".  " + item.Title + (item.IsLeader ? "   (leader)" : string.Empty) + (item.Include ? string.Empty : "   (skipped)");
                var mi = new ButtonMenuItem { Text = MenuText(text) };
                mi.Click += (s, e) => ShowTranslatorItem(item);
                menu.Items.Add(mi);
            }
            menu.Items.AddSeparator();
            var cur = _qCurrent;
            if (!cur.IsNew)
            {
                var include = new ButtonMenuItem { Text = cur.Include ? "Skip this object in Update" : "Include this object in Update again" };
                include.Click += (s, e) => { cur.Include = !cur.Include; UpdateQObjects(); SchedulePreview(); };
                menu.Items.Add(include);
                var select = new ButtonMenuItem { Text = "Select this object in Rhino" };
                select.Click += (s, e) => _bridge.SelectInRhino(cur);
                menu.Items.Add(select);
            }
            var all = new ButtonMenuItem { Text = "Translate all" };
            all.Click += (s, e) => TranslateAllItems(false);
            menu.Items.Add(all);
            var fresh = new ButtonMenuItem { Text = "New text (unload the objects)" };
            fresh.Click += (s, e) => ClearTranslatorItems();
            menu.Items.Add(fresh);
            menu.Show(anchor);
        }

        private void ShowLayerMenu(Control anchor)
        {
            var layers = _bridge.Layers();
            int chosen = _bridge.LayerIndex;
            var menu = new ContextMenu();
            menu.Items.Add(new ButtonMenuItem { Text = "Layer for placed text and leaders", Enabled = false });
            Action<int, string> add = (index, name) =>
            {
                var mi = new ButtonMenuItem { Text = (index == chosen ? "●  " : "     ") + MenuText(name) };
                mi.Click += (s, e) => { _bridge.LayerIndex = index; UpdateQLayer(); };
                menu.Items.Add(mi);
            };
            add(-1, "Current layer");
            if (layers.Count == 0)
                menu.Items.Add(new ButtonMenuItem { Text = _bridge.InRhino ? "(the file has no other layers)" : "The layers of the open Rhino file are listed here", Enabled = false });
            foreach (var l in layers) add(l.Key, l.Value);
            menu.Show(anchor);
        }

        private void UpdateQLayer()
        {
            if (_qLayer == null) return;
            int index = _bridge.LayerIndex;
            string name = "Current layer";
            if (index >= 0)
            {
                foreach (var l in _bridge.Layers())
                    if (l.Key == index) { name = l.Value; break; }
            }
            _qLayer.Text = "Layer · " + name;
        }

        private void StopTranslator()
        {
            _qDebounce.Stop();
            _qPreviewTimer.Stop();
            if (_qCts != null) _qCts.Cancel();
            try { if (_bridge != null) _bridge.Detach(); }
            catch (Exception ex) { Diagnostics.Log("translator detach: " + ex.Message); }
        }
    }
}
