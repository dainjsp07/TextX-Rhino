using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;
using TextX.Core;
using TextX.Core.Text;
using TextX.Core.Translation;
using TextX.Integration;

namespace TextX.UI
{
    /// <summary>Settings: translation services, right-to-left rendering, print/PDF switching and glossary.</summary>
    public sealed class SettingsDialog : Dialog<bool>
    {
        private sealed class GlossaryRow
        {
            public string Source { get; set; }
            public string Target { get; set; }
            public string TargetLang { get; set; }
            public string SourceLang { get; set; }
        }

        private readonly TextXSettings _s;
        private readonly Dictionary<string, ProviderSettings> _providerSettings = new Dictionary<string, ProviderSettings>();
        private string _shownProvider;

        // service
        private readonly DropDown _provider = new DropDown();
        private readonly DropDown _fallback = new DropDown();
        private readonly PasswordBox _apiKey = new PasswordBox();
        private readonly TextBox _endpoint = new TextBox();
        private readonly TextBox _region = new TextBox();
        private readonly TextBox _model = new TextBox();
        private readonly TextBox _email = new TextBox();
        private readonly Label _providerHelp = new Label { Wrap = WrapMode.Word };
        private readonly CheckBox _realtime = new CheckBox { Text = "Translate while typing" };
        private readonly NumericStepper _delay = new NumericStepper { MinValue = 200, MaxValue = 5000, Increment = 100, Width = 90 };
        private readonly NumericStepper _timeout = new NumericStepper { MinValue = 3, MaxValue = 120, Increment = 1, Width = 90 };
        private readonly Label _testResult = new Label { Wrap = WrapMode.Word };

        // rtl
        private readonly DropDown _screenMode = new DropDown();
        private readonly DropDown _printMode = new DropDown();
        private readonly CheckBox _keepSign = new CheckBox { Text = "Keep +/- signs in front of numbers (\"מפלס -0.45\")" };
        private readonly CheckBox _shape = new CheckBox { Text = "Connect Arabic / Persian letters (contextual shaping)" };
        private readonly CheckBox _mirror = new CheckBox { Text = "Mirror brackets inside right-to-left text" };
        private readonly DropDown _defaultMode = new DropDown();
        private readonly CheckBox _autoRight = new CheckBox { Text = "Right-align new right-to-left text" };

        // print
        private readonly CheckBox _autoSwap = new CheckBox { Text = "Automatically use translations when printing and exporting PDF" };
        private readonly TextBox _printCommands = new TextBox();
        private readonly TextBox _exportCommands = new TextBox();
        private readonly TextBox _extensions = new TextBox();
        private readonly CheckBox _exportAtStart = new CheckBox { Text = "Switch at the start of Export / Save As (set automatically if Rhino does not report the PDF file name in time)" };
        private readonly DropDown _scope = new DropDown();
        private readonly CheckBox _blocks = new CheckBox { Text = "Include text inside block definitions (title blocks)" };
        private readonly DropDown _stale = new DropDown();
        private readonly CheckBox _messages = new CheckBox { Text = "Report switching in the command line" };

        // appearance and updates
        private readonly DropDown _theme = new DropDown();
        private readonly CheckBox _checkUpdates = new CheckBox { Text = "Check for new versions automatically (once a day)" };
        private readonly CheckBox _preReleases = new CheckBox { Text = "Include test versions (pre-releases)" };
        private readonly CheckBox _autoInstall = new CheckBox { Text = "Install new versions automatically (they are used the next time Rhino starts)" };
        private readonly TextBox _feed = new TextBox();
        private readonly Label _updateResult = new Label { Wrap = WrapMode.Word };

        // glossary
        private readonly ObservableCollection<GlossaryRow> _glossaryRows = new ObservableCollection<GlossaryRow>();
        private readonly GridView _glossaryGrid = new GridView { Height = 220 };
        private readonly TextArea _dnt = new TextArea { Height = 80 };
        private readonly Label _cacheInfo = new Label();

        public static void ShowFor(Control parent)
        {
            var dlg = new SettingsDialog();
            dlg.ShowModal(parent ?? (Control)RhinoEtoApp.MainWindow);
        }

        private SettingsDialog()
        {
            _s = TextXPlugIn.Instance.Options;
            Title = "Text X Settings";
            Resizable = true;
            MinimumSize = new Size(640, 560);
            Padding = new Padding(10);
            this.UseRhinoStyle();
            Theming.Icons.ApplyWindowIcon(this);
            KeyDown += (s, e) => { if (e.Key == Keys.Tab) TextX.UI.Controls.ThemedDrawable.NoteKeyboardNavigation(); };

            foreach (var id in ProviderIds.All)
            {
                _provider.Items.Add(new ListItem { Text = ProviderIds.DisplayName(id), Key = id });
                _fallback.Items.Add(new ListItem { Text = ProviderIds.DisplayName(id), Key = id });
                _providerSettings[id] = _s.GetProviderSettings(id);
            }
            _fallback.Items.Insert(0, new ListItem { Text = "(none)", Key = string.Empty });

            var tabs = new TabControl();
            tabs.Pages.Add(new TabPage { Text = "Translation service", Content = BuildServicePage() });
            tabs.Pages.Add(new TabPage { Text = "Right-to-left", Content = BuildRtlPage() });
            tabs.Pages.Add(new TabPage { Text = "Print / PDF", Content = BuildPrintPage() });
            tabs.Pages.Add(new TabPage { Text = "Glossary", Content = BuildGlossaryPage() });
            tabs.Pages.Add(new TabPage { Text = "Appearance and updates", Content = BuildUpdatesPage() });

            var ok = new Button { Text = "OK" };
            ok.Click += (s, e) => { Save(); TranslationEngine.ResetCooldowns(); Close(true); };
            var cancel = new Button { Text = "Cancel" };
            cancel.Click += (s, e) => Close(false);
            DefaultButton = ok;
            AbortButton = cancel;

            Content = new TableLayout
            {
                Spacing = new Size(6, 8),
                Rows =
                {
                    new TableRow(tabs) { ScaleHeight = true },
                    new TableRow(TableLayout.Horizontal(6, null, ok, cancel)),
                },
            };
            LoadValues();
        }

        // ------------------------------------------------------------------ pages
        private Control BuildServicePage()
        {
            _provider.SelectedIndexChanged += (s, e) => ShowProvider(_provider.SelectedKey);
            var test = new Button { Text = "Test" };
            test.Click += (s, e) => TestProvider();

            var l = new DynamicLayout { DefaultSpacing = new Size(6, 6), Padding = new Padding(8) };
            l.AddRow(new Label { Text = "Service" }, _provider);
            l.AddRow(null, _providerHelp);
            l.AddRow(new Label { Text = "API key" }, _apiKey);
            l.AddRow(new Label { Text = "Endpoint / server URL" }, _endpoint);
            l.AddRow(new Label { Text = "Region (Azure)" }, _region);
            l.AddRow(new Label { Text = "Model (Claude)" }, _model);
            l.AddRow(new Label { Text = "E-mail (MyMemory)" }, _email);
            l.AddRow(null, TableLayout.Horizontal(6, test, new TableCell(_testResult, true)));
            l.AddRow(new Label { Text = "Fallback service" }, _fallback);
            l.AddRow(null, _realtime);
            l.AddRow(new Label { Text = "Typing delay (ms)" }, TableLayout.Horizontal(6, _delay, null));
            l.AddRow(new Label { Text = "Timeout (s)" }, TableLayout.Horizontal(6, _timeout, null));
            l.AddRow(null, new Label { Text = "API keys are stored encrypted for your Windows user account.", Wrap = WrapMode.Word });
            l.Add(null);
            return new Scrollable { Content = l, Border = BorderType.None, ExpandContentWidth = true };
        }

        private Control BuildRtlPage()
        {
            foreach (var dd in new[] { _screenMode, _printMode })
            {
                dd.Items.Add("Visual - reorder for Rhino's left-to-right renderer (correct for Rhino 8)");
                dd.Items.Add("Logical - store as typed (only if Rhino renders right-to-left text itself)");
            }
            _defaultMode.Items.Add("English on screen, translation in print / PDF");
            _defaultMode.Items.Add("Translation on screen and in print");
            _defaultMode.Items.Add("English on screen and in print");

            var l = new DynamicLayout { DefaultSpacing = new Size(6, 6), Padding = new Padding(8) };
            l.AddRow(new Label { Text = "Screen (viewports)" }, _screenMode);
            l.AddRow(new Label { Text = "Print and PDF" }, _printMode);
            l.AddRow(null, new Label { Text = "Run TRRtlTest to check both on your system: it places samples in each mode.", Wrap = WrapMode.Word });
            l.AddRow(null, _keepSign);
            l.AddRow(null, _shape);
            l.AddRow(null, _mirror);
            l.AddRow(new Label { Text = "Default display for new text" }, _defaultMode);
            l.AddRow(null, _autoRight);
            l.Add(null);
            return l;
        }

        private Control BuildPrintPage()
        {
            _scope.Items.Add("All translated annotations (model and layouts)");
            _scope.Items.Add("Only annotations placed on layouts");
            _stale.Items.Add("Re-translate automatically before printing");
            _stale.Items.Add("Print the edited English text");
            _stale.Items.Add("Print the old translation");

            var l = new DynamicLayout { DefaultSpacing = new Size(6, 6), Padding = new Padding(8) };
            l.AddRow(null, _autoSwap);
            l.AddRow(new Label { Text = "Print commands" }, _printCommands);
            l.AddRow(null, new Label { Text = "Translations are used for the whole command (Print dialog preview included). Separate with ';'.", Wrap = WrapMode.Word });
            l.AddRow(new Label { Text = "Export commands" }, _exportCommands);
            l.AddRow(new Label { Text = "Translated file types" }, _extensions);
            l.AddRow(null, new Label { Text = "Export / Save As to these file types use the translations. Saving .3dm files always keeps the screen text.", Wrap = WrapMode.Word });
            l.AddRow(null, _exportAtStart);
            l.AddRow(new Label { Text = "Scope" }, _scope);
            l.AddRow(null, _blocks);
            l.AddRow(new Label { Text = "Text edited after translation" }, _stale);
            l.AddRow(null, _messages);
            l.Add(null);
            return new Scrollable { Content = l, Border = BorderType.None, ExpandContentWidth = true };
        }

        private Control BuildGlossaryPage()
        {
            _glossaryGrid.Columns.Add(new GridColumn { HeaderText = "English / source term", DataCell = new TextBoxCell { Binding = Binding.Property<GlossaryRow, string>(r => r.Source) }, Editable = true, Expand = true });
            _glossaryGrid.Columns.Add(new GridColumn { HeaderText = "Translation", DataCell = new TextBoxCell { Binding = Binding.Property<GlossaryRow, string>(r => r.Target) }, Editable = true, Expand = true });
            _glossaryGrid.Columns.Add(new GridColumn { HeaderText = "Lang", DataCell = new TextBoxCell { Binding = Binding.Property<GlossaryRow, string>(r => r.TargetLang) }, Editable = true, Width = 50 });
            _glossaryGrid.DataStore = _glossaryRows;

            var add = new Button { Text = "Add" };
            add.Click += (s, e) => _glossaryRows.Add(new GlossaryRow { Source = "", Target = "", TargetLang = _s.TargetLanguage, SourceLang = _s.SourceLanguage == Languages.AutoDetect ? "en" : _s.SourceLanguage });
            var remove = new Button { Text = "Remove" };
            remove.Click += (s, e) => { var r = _glossaryGrid.SelectedItem as GlossaryRow; if (r != null) _glossaryRows.Remove(r); };
            var import = new Button { Text = "Import CSV..." };
            import.Click += (s, e) => ImportCsv();
            var export = new Button { Text = "Export CSV..." };
            export.Click += (s, e) => ExportCsv();
            var clearCache = new Button { Text = "Clear translation memory" };
            clearCache.Click += (s, e) => { TranslationServices.Cache.Clear(); TranslationServices.SaveCache(); UpdateCacheInfo(); };

            var l = new DynamicLayout { DefaultSpacing = new Size(6, 6), Padding = new Padding(8) };
            l.AddRow(new Label { Text = "Fixed translations: a text (or line) that exactly matches a term uses this translation. A trailing ':' is kept. Use it for title-block labels so they are always translated the same way.", Wrap = WrapMode.Word });
            l.Add(_glossaryGrid, true, true);
            l.AddSeparateRow(add, remove, null, import, export);
            l.AddRow(new Label { Text = "Never translate (one term per line: names, project codes, brands)" });
            l.Add(_dnt, true, false);
            l.AddSeparateRow(_cacheInfo, null, clearCache);
            return l;
        }

        // ------------------------------------------------------------------ load / save
        private void LoadValues()
        {
            var theme = (_s.ThemeMode ?? "Auto").ToLowerInvariant();
            _theme.SelectedIndex = theme == "dark" ? 1 : (theme == "light" ? 2 : 0);
            _checkUpdates.Checked = _s.CheckForUpdates;
            _preReleases.Checked = _s.IncludePreReleases;
            _autoInstall.Checked = _s.InstallUpdatesAutomatically;
            _autoInstall.Enabled = UpdateService.Instance.CanInstall;
            _feed.Text = _s.UpdateFeedOverride;
            _provider.SelectedKey = _s.ProviderId;
            if (_provider.SelectedIndex < 0) _provider.SelectedIndex = 0;
            _fallback.SelectedKey = _s.FallbackProviderId ?? string.Empty;
            if (_fallback.SelectedIndex < 0) _fallback.SelectedIndex = 0;
            ShowProvider(_provider.SelectedKey);
            _realtime.Checked = _s.RealTimeTranslation;
            _delay.Value = _s.RealTimeDelayMs;
            _timeout.Value = _s.TimeoutSeconds;

            _screenMode.SelectedIndex = (int)_s.ScreenRtlMode;
            _printMode.SelectedIndex = (int)_s.PrintRtlMode;
            _keepSign.Checked = _s.KeepSignWithNumber;
            _shape.Checked = _s.ShapeArabic;
            _mirror.Checked = _s.MirrorBrackets;
            _defaultMode.SelectedIndex = (int)_s.DefaultMode;
            _autoRight.Checked = _s.AutoRightAlignRtl;

            _autoSwap.Checked = _s.AutoSwapEnabled;
            _printCommands.Text = _s.PrintCommands;
            _exportCommands.Text = _s.ExportCommands;
            _extensions.Text = _s.PrintLikeExtensions;
            _exportAtStart.Checked = _s.SwapExportsAtCommandStart;
            _scope.SelectedIndex = (int)_s.Scope;
            _blocks.Checked = _s.IncludeBlockDefinitions;
            _stale.SelectedIndex = (int)_s.StaleHandling;
            _messages.Checked = _s.ShowSwapMessages;

            var g = TranslationServices.Glossary;
            _glossaryRows.Clear();
            foreach (var e in g.Entries)
                _glossaryRows.Add(new GlossaryRow { Source = e.Source, Target = e.Target, TargetLang = e.TargetLang, SourceLang = e.SourceLang });
            _dnt.Text = string.Join(Environment.NewLine, g.DoNotTranslate.ToArray());
            UpdateCacheInfo();
        }

        private void UpdateCacheInfo()
        {
            _cacheInfo.Text = "Translation memory: " + TranslationServices.Cache.Count + " entries";
        }

        private void StoreShownProvider()
        {
            if (_shownProvider == null) return;
            var ps = _providerSettings[_shownProvider];
            ps.ApiKey = _apiKey.Text ?? string.Empty;
            ps.Endpoint = _endpoint.Text ?? string.Empty;
            ps.Region = _region.Text ?? string.Empty;
            ps.Model = _model.Text ?? string.Empty;
            ps.Email = _email.Text ?? string.Empty;
        }

        private void ShowProvider(string id)
        {
            StoreShownProvider();
            if (string.IsNullOrEmpty(id)) return;
            _shownProvider = id;
            var ps = _providerSettings[id];
            _apiKey.Text = ps.ApiKey ?? string.Empty;
            _endpoint.Text = ps.Endpoint ?? string.Empty;
            _region.Text = ps.Region ?? string.Empty;
            _model.Text = ps.Model ?? string.Empty;
            _email.Text = ps.Email ?? string.Empty;
            _apiKey.Enabled = ProviderIds.RequiresKey(id) || id == ProviderIds.LibreTranslate;
            _endpoint.Enabled = id == ProviderIds.LibreTranslate || id == ProviderIds.Azure || id == ProviderIds.DeepL || id == ProviderIds.Claude;
            _region.Enabled = id == ProviderIds.Azure;
            _model.Enabled = id == ProviderIds.Claude;
            _email.Enabled = id == ProviderIds.MyMemory;
            _providerHelp.Text = Help(id);
            _testResult.Text = string.Empty;
        }

        private static string Help(string id)
        {
            switch (id)
            {
                case ProviderIds.GoogleFree: return "Works without setup. It is Google's public web endpoint: unofficial, rate-limited and may change. For production work use an API-key service.";
                case ProviderIds.GoogleCloud: return "Google Cloud Translation API (Basic v2). Create an API key in the Google Cloud console.";
                case ProviderIds.DeepL: return "DeepL API. Free keys end with ':fx' and use api-free.deepl.com automatically. Check that DeepL supports your target language.";
                case ProviderIds.Azure: return "Azure AI Translator. Key from the Azure portal; Region is required for regional resources (e.g. westeurope).";
                case ProviderIds.Claude: return "Anthropic Claude via the Messages API: context-aware terminology for drawings. Requires an API key from console.anthropic.com.";
                case ProviderIds.LibreTranslate: return "LibreTranslate server URL, e.g. http://localhost:5000 for a self-hosted instance. API key only if the server requires one.";
                case ProviderIds.MyMemory: return "Free service with a daily quota (higher with an e-mail address). Good as a fallback.";
                default: return "No machine translation: type the translation yourself. Glossary entries still apply.";
            }
        }

        private void TestProvider()
        {
            StoreShownProvider();
            var id = _shownProvider;
            var ps = _providerSettings[id];
            var provider = ProviderFactory.Create(id, ps);
            var tgt = _s.TargetLanguage;
            _testResult.Text = "Testing...";
            Task.Run(() => provider.TranslateAsync(new[] { "Ground floor plan" }, "en", tgt, CancellationToken.None)).ContinueWith(t =>
            {
                Application.Instance.AsyncInvoke(() =>
                {
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception == null ? null : t.Exception.GetBaseException();
                        _testResult.Text = "Failed: " + (ex == null ? "unknown error" : ex.Message);
                        _testResult.TextColor = Colors.Red;
                    }
                    else
                    {
                        var r = t.Result != null && t.Result.Count > 0 ? t.Result[0] : null;
                        _testResult.Text = r == null ? "OK (manual service)" : "OK: \"Ground floor plan\" -> " + r;
                        _testResult.TextColor = SystemColors.ControlText;
                    }
                });
            });
        }

        private Control BuildUpdatesPage()
        {
            foreach (var s in new[] { "Follow Rhino (dark or light)", "Dark - Graphite and Deep Navy", "Light" }) _theme.Items.Add(s);
            var builtIn = BuildInfo.UpdateFeedUrl;
            _feed.PlaceholderText = string.IsNullOrEmpty(builtIn) ? "(not set for this build)" : builtIn;
            var check = new Button { Text = "Check now" };
            check.Click += (s, e) => CheckUpdatesNow();

            var l = new DynamicLayout { DefaultSpacing = new Size(6, 6), Padding = new Padding(8) };
            l.AddRow(new Label { Text = "Window theme" }, _theme);
            l.AddRow(null, new Label { Text = "Changes apply when you close this dialog.", Wrap = WrapMode.Word });
            l.AddRow(new Label { Text = " " });
            l.AddRow(new Label { Text = "Installed version" }, new Label { Text = BuildInfo.DisplayName + (string.IsNullOrEmpty(BuildInfo.BuildDate) ? string.Empty : "  (built " + BuildInfo.BuildDate + ")") });
            l.AddRow(new Label { Text = "Installed in" }, new Label { Text = BuildInfo.InstallFolder, Wrap = WrapMode.Character });
            l.AddRow(null, _checkUpdates);
            l.AddRow(null, _autoInstall);
            if (!UpdateService.Instance.CanInstall)
                l.AddRow(null, new Label { Text = "This copy was not installed from a .yak package, so it cannot install new versions itself. Install the .yak once (drag it into Rhino) to get automatic updates.", Wrap = WrapMode.Word });
            l.AddRow(null, _preReleases);
            l.AddRow(new Label { Text = "Update location" }, _feed);
            l.AddRow(null, new Label { Text = "Leave empty to use the location built into this version. It can also be a latest.json file in a shared folder (see RELEASING.md).", Wrap = WrapMode.Word });
            l.AddRow(null, TableLayout.Horizontal(6, check, new TableCell(_updateResult, true)));
            l.Add(null);
            return l;
        }

        private void CheckUpdatesNow()
        {
            _s.UpdateFeedOverride = _feed.Text;
            _s.IncludePreReleases = _preReleases.Checked == true;
            var svc = UpdateService.Instance;
            if (!svc.IsConfigured)
            {
                _updateResult.Text = "No update location is set.";
                return;
            }
            _updateResult.Text = "Checking...";
            svc.CheckAsync(CancellationToken.None).ContinueWith(t =>
            {
                Application.Instance.AsyncInvoke(() =>
                {
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception == null ? null : t.Exception.GetBaseException();
                        _updateResult.Text = "Could not check: " + (ex == null ? "unknown error" : ex.Message);
                        return;
                    }
                    var pending = svc.PendingVersion;
                    _updateResult.Text = svc.UpdateAvailable
                        ? BuildInfo.ProductName + " " + t.Result.Version + " is available. Run TRUpdate or use the button in the Text X window."
                        : pending != null
                            ? BuildInfo.ProductName + " " + pending + " is installed and will be used the next time you start Rhino."
                            : "You have the latest version (" + BuildInfo.VersionText + ").";
                });
            });
        }

        private void Save()
        {
            StoreShownProvider();
            _s.ThemeMode = _theme.SelectedIndex == 1 ? "Dark" : (_theme.SelectedIndex == 2 ? "Light" : "Auto");
            _s.CheckForUpdates = _checkUpdates.Checked == true;
            _s.IncludePreReleases = _preReleases.Checked == true;
            _s.InstallUpdatesAutomatically = _autoInstall.Checked == true;
            _s.UpdateFeedOverride = _feed.Text;
            foreach (var kv in _providerSettings) _s.SetProviderSettings(kv.Key, kv.Value);
            _s.ProviderId = _provider.SelectedKey ?? ProviderIds.GoogleFree;
            _s.FallbackProviderId = _fallback.SelectedKey ?? string.Empty;
            _s.RealTimeTranslation = _realtime.Checked == true;
            _s.RealTimeDelayMs = (int)_delay.Value;
            _s.TimeoutSeconds = (int)_timeout.Value;

            _s.ScreenRtlMode = (RtlRenderMode)Math.Max(0, _screenMode.SelectedIndex);
            _s.PrintRtlMode = (RtlRenderMode)Math.Max(0, _printMode.SelectedIndex);
            _s.KeepSignWithNumber = _keepSign.Checked == true;
            _s.ShapeArabic = _shape.Checked == true;
            _s.MirrorBrackets = _mirror.Checked == true;
            _s.DefaultMode = (TranslationDisplayMode)Math.Max(0, _defaultMode.SelectedIndex);
            _s.AutoRightAlignRtl = _autoRight.Checked == true;

            _s.AutoSwapEnabled = _autoSwap.Checked == true;
            _s.PrintCommands = _printCommands.Text;
            _s.ExportCommands = _exportCommands.Text;
            _s.PrintLikeExtensions = _extensions.Text;
            _s.SwapExportsAtCommandStart = _exportAtStart.Checked == true;
            _s.Scope = (SwapScope)Math.Max(0, _scope.SelectedIndex);
            _s.IncludeBlockDefinitions = _blocks.Checked == true;
            _s.StaleHandling = (StaleTranslationHandling)Math.Max(0, _stale.SelectedIndex);
            _s.ShowSwapMessages = _messages.Checked == true;

            var g = new Glossary();
            foreach (var r in _glossaryRows)
            {
                if (string.IsNullOrWhiteSpace(r.Source) || string.IsNullOrWhiteSpace(r.Target)) continue;
                g.Add(string.IsNullOrWhiteSpace(r.SourceLang) ? "*" : r.SourceLang.Trim(), string.IsNullOrWhiteSpace(r.TargetLang) ? _s.TargetLanguage : r.TargetLang.Trim(), r.Source, r.Target);
            }
            foreach (var line in (_dnt.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
                if (!string.IsNullOrWhiteSpace(line)) g.DoNotTranslate.Add(line.Trim());
            TranslationServices.ReplaceGlossary(g);
        }

        private void ImportCsv()
        {
            var ofd = new Eto.Forms.OpenFileDialog { Title = "Import glossary" };
            ofd.Filters.Add(new FileFilter("CSV / text", ".csv", ".txt", ".tsv"));
            if (ofd.ShowDialog(this) != DialogResult.Ok) return;
            try
            {
                var g = new Glossary();
                var src = _s.SourceLanguage == Languages.AutoDetect ? "en" : _s.SourceLanguage;
                int n = g.ImportCsv(File.ReadAllText(ofd.FileName, Encoding.UTF8), src, _s.TargetLanguage);
                foreach (var e in g.Entries) _glossaryRows.Add(new GlossaryRow { Source = e.Source, Target = e.Target, TargetLang = e.TargetLang, SourceLang = e.SourceLang });
                if (g.DoNotTranslate.Count > 0)
                    _dnt.Text = (_dnt.Text ?? string.Empty).TrimEnd() + Environment.NewLine + string.Join(Environment.NewLine, g.DoNotTranslate.ToArray());
                MessageBox.Show(this, "Imported " + n + " rows.", "Glossary", MessageBoxType.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Glossary import failed", MessageBoxType.Error);
            }
        }

        private void ExportCsv()
        {
            var sfd = new Eto.Forms.SaveFileDialog { Title = "Export glossary", FileName = "glossary.csv" };
            sfd.Filters.Add(new FileFilter("CSV", ".csv"));
            if (sfd.ShowDialog(this) != DialogResult.Ok) return;
            var g = new Glossary();
            foreach (var r in _glossaryRows)
                if (!string.IsNullOrWhiteSpace(r.Source)) g.Add(r.SourceLang ?? "*", r.TargetLang ?? _s.TargetLanguage, r.Source, r.Target ?? string.Empty);
            foreach (var line in (_dnt.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
                if (!string.IsNullOrWhiteSpace(line)) g.DoNotTranslate.Add(line.Trim());
            File.WriteAllText(sfd.FileName, g.ExportCsv(), new UTF8Encoding(true));
        }
    }
}
