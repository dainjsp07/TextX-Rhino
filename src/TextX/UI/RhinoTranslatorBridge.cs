using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using TextX.Core;
using TextX.Core.Text;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Studio;
using TextX.UI.Theming;

namespace TextX.UI
{
    /// <summary>What TRPlaceText and TRPlaceLeader need from the translator.</summary>
    public sealed class PlacementRequest
    {
        public TranslationRecord Record;
        public TextFormat Format;
        public bool AutoRightAlign;
    }

    /// <summary>What TRApply needs from the translator.</summary>
    public sealed class ApplyRequest
    {
        public uint DocSerial;
        public readonly List<KeyValuePair<Guid, TranslationRecord>> Items = new List<KeyValuePair<Guid, TranslationRecord>>();
        public TextFormat Format;
        public FormatFields Fields;
    }

    /// <summary>
    /// The Rhino side of the translator on Studio's Home: loads the selected text and leaders, keeps Rhino's
    /// text properties (<see cref="FormatPanel"/>), lists the layers, draws the viewport preview, follows
    /// document switches, and builds the requests of the place / update commands.
    /// </summary>
    public sealed class RhinoTranslatorBridge : TranslatorBridge
    {
        /// <summary>The bridge of the open Studio window (null when it is closed).</summary>
        public static RhinoTranslatorBridge Active { get; private set; }

        private readonly FormatPanel _format = new FormatPanel();
        private readonly PreviewConduit _conduit = new PreviewConduit();
        private uint _docSerial;
        private bool _rebindPending;

        private static TextXSettings Settings { get { return TextXPlugIn.Instance.Options; } }

        public override bool InRhino { get { return true; } }

        public override void Attach(StudioWindow window)
        {
            base.Attach(window);
            Active = this;
            _format.AutoRightAlignRtl = Settings.AutoRightAlignRtl;
            _format.Changed += OnFormatChanged;
            RhinoDoc.CloseDocument += OnCloseDocument;
            RhinoDoc.ActiveDocumentChanged += OnActiveDocumentChanged;
            RhinoApp.Idle += OnRhinoIdle;
            PrintSwapManager.Instance.StateChanged += OnSwapStateChanged;
            BindDocument(RhinoDoc.ActiveDoc);
        }

        public override void Detach()
        {
            _format.Changed -= OnFormatChanged;
            RhinoDoc.CloseDocument -= OnCloseDocument;
            RhinoDoc.ActiveDocumentChanged -= OnActiveDocumentChanged;
            RhinoApp.Idle -= OnRhinoIdle;
            PrintSwapManager.Instance.StateChanged -= OnSwapStateChanged;
            if (_conduit.Enabled)
            {
                _conduit.Enabled = false;
                var doc = Doc;
                if (doc != null) doc.Views.Redraw();
            }
            try { Settings.AutoRightAlignRtl = _format.AutoRightAlignRtl; } catch (Exception) { /* settings gone at shutdown */ }
            if (Active == this) Active = null;
        }

        private void OnFormatChanged(object sender, EventArgs e)
        {
            RaiseStyleChanged();
        }

        private void OnSwapStateChanged(object sender, EventArgs e)
        {
            var w = Window;
            if (w != null) Application.Instance.AsyncInvoke(w.OnPrintStateChanged);
        }

        // ------------------------------------------------------------------ text properties
        public override Control CreateFormatSections(ThemeScope scope) { return _format.CreateFormatPage(scope); }

        public override Control CreateLeaderSections(ThemeScope scope) { return _format.CreateLeaderPage(scope); }

        public override LineStyle ReadStyle()
        {
            var f = _format.Read();
            return new LineStyle
            {
                FontFace = f.FontFace,
                Bold = f.Bold,
                Italic = f.Italic,
                Underline = f.Underline,
                Mask = !f.MaskEnabled ? 0 : (f.MaskUsesViewportColor ? 1 : 2),
                MaskColor = FormatPanel.ToEto(f.MaskColor),
                Frame = !f.MaskEnabled ? 0 : f.MaskFrame == DimensionStyle.MaskFrame.RectFrame ? 1 : f.MaskFrame == DimensionStyle.MaskFrame.CapsuleFrame ? 2 : 0,
                HAlign = f.HAlign == TextHorizontalAlignment.Center ? 1 : f.HAlign == TextHorizontalAlignment.Right ? 2 : 0,
                ColorByLayer = f.ColorByLayer,
                TextColor = FormatPanel.ToEto(f.ObjectColor),
            };
        }

        public override IList<KeyValuePair<int, string>> Layers() { return _format.LayerChoices; }

        public override int LayerIndex
        {
            get { return _format.LayerIndex; }
            set { _format.LayerIndex = value; }
        }

        public override void ResetFormat()
        {
            var doc = Doc;
            if (doc == null) return;
            _format.Load(FormatPanel.DefaultFor(doc));
            _format.ResetDirty();
            RaiseStyleChanged();
        }

        // ------------------------------------------------------------------ documents
        private RhinoDoc Doc
        {
            get
            {
                var doc = _docSerial == 0 ? null : RhinoDoc.FromRuntimeSerialNumber(_docSerial);
                return IsUsable(doc) ? doc : null;
            }
        }

        public static bool IsUsable(RhinoDoc doc)
        {
            if (doc == null) return false;
            try { return !doc.IsClosing && !doc.IsOpening; }
            catch { return false; }
        }

        private void BindDocument(RhinoDoc doc)
        {
            if (!IsUsable(doc)) return;
            bool changed = doc.RuntimeSerialNumber != _docSerial;
            _docSerial = doc.RuntimeSerialNumber;
            _format.BindDocument(doc);
            if (changed)
            {
                _format.Load(FormatPanel.DefaultFor(doc));
                _format.ResetDirty();
                if (Window != null) Window.ForgetTranslatorItems();
            }
            RaiseStyleChanged();
        }

        // Document switches are handled on Rhino's idle event, never inside the document events
        // themselves: while a document is closing or a file is being read its tables must not be touched.
        private void OnActiveDocumentChanged(object sender, DocumentEventArgs e)
        {
            _rebindPending = true;
        }

        private void OnCloseDocument(object sender, DocumentEventArgs e)
        {
            if (e.Document == null || e.Document.RuntimeSerialNumber != _docSerial) return;
            _docSerial = 0;          // flags only while the document is being destroyed
            _rebindPending = true;
        }

        private void OnRhinoIdle(object sender, EventArgs e)
        {
            if (!_rebindPending || Rhino.Commands.Command.InCommand()) return;
            var doc = RhinoDoc.ActiveDoc;
            if (!IsUsable(doc)) return;
            _rebindPending = false;
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
                    _docSerial = 0;
                    BindDocument(doc);
                    ItemsChanged();
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Log("translator rebind: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ loading objects
        public override void LoadSelected()
        {
            var doc = RhinoDoc.ActiveDoc;
            if (!IsUsable(doc)) return;
            var objs = doc.Objects.GetSelectedObjects(false, false).Where(AnnotationEngine.IsSupported).ToList();
            if (objs.Count == 0)
            {
                Window.TranslatorMessage("Select one or more text or leader objects in Rhino first (or run TREdit).", StatusText.Level.Warning);
                return;
            }
            LoadObjects(doc, objs);
        }

        /// <summary>Loads annotation objects into the translator (TR, TREdit, Load selected, Manage).</summary>
        public void LoadObjects(RhinoDoc doc, IList<RhinoObject> objects)
        {
            LoadObjects(doc, objects, -1);
        }

        private void LoadObjects(RhinoDoc doc, IList<RhinoObject> objects, int showIndex)
        {
            BindDocument(doc);
            var cfg = RenderConfig.FromSettings(Settings);
            var items = new List<TranslatorItem>();
            string commonTarget = null;
            bool mixedTargets = false;
            int rtlUntracked = 0;
            foreach (var obj in objects)
            {
                var ann = obj.Geometry as AnnotationBase;
                if (ann == null) continue;
                var item = new TranslatorItem { ObjectId = obj.Id, DocSerial = doc.RuntimeSerialNumber, IsLeader = ann is Leader };
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
                items.Add(item);
            }

            if (items.Count > 0)
            {
                var first = items[showIndex >= 0 && showIndex < items.Count ? showIndex : 0];
                var fmt = first.OriginalFormat as TextFormat;
                if (fmt != null) _format.Load(fmt);
                _format.ResetDirty();
            }
            var msg = "Loaded " + items.Count + (items.Count == 1 ? " object." : " objects.");
            if (mixedTargets) msg += " The objects use different target languages; Update uses the language chosen at the top.";
            if (rtlUntracked > 0) msg += " " + rtlUntracked + " already contain right-to-left text: if it reads backwards, use Fix reversed.";
            Window.SetTranslatorItems(items, mixedTargets ? null : commonTarget, msg,
                mixedTargets || rtlUntracked > 0 ? StatusText.Level.Warning : StatusText.Level.Info);
            if (showIndex > 0 && showIndex < items.Count) Window.ShowTranslatorItemAt(showIndex);
        }

        public override void SelectInRhino(TranslatorItem item)
        {
            var doc = Doc;
            if (doc == null || item == null || item.IsNew) return;
            doc.Objects.UnselectAll();
            doc.Objects.Select(item.ObjectId, true);
            doc.Views.Redraw();
        }

        // ------------------------------------------------------------------ requests of the commands
        public PlacementRequest CreatePlacementRequest()
        {
            var w = Window;
            if (w == null) return null;
            var item = w.CurrentTranslatorItem;
            if (item == null || (string.IsNullOrWhiteSpace(item.Source) && string.IsNullOrWhiteSpace(item.Translation))) return null;
            var rec = item.BuildRecord(w.RecordSourceLanguage(item.Source), w.TranslatorTargetLanguage);
            return new PlacementRequest { Record = rec, Format = _format.Read(), AutoRightAlign = _format.AutoRightAlignRtl };
        }

        public ApplyRequest CreateApplyRequest()
        {
            var req = new ApplyRequest { DocSerial = _docSerial, Format = _format.Read(), Fields = _format.Dirty };
            var w = Window;
            if (w == null) return req;
            foreach (var item in w.TranslatorItems.Where(i => i.Include && !i.IsNew))
                req.Items.Add(new KeyValuePair<Guid, TranslationRecord>(item.ObjectId, item.BuildRecord(w.RecordSourceLanguage(item.Source), w.TranslatorTargetLanguage)));
            return req;
        }

        /// <summary>Called by TRApply after the objects were updated: they are loaded again as they are now.</summary>
        public void OnObjectsApplied(RhinoDoc doc, int count)
        {
            var w = Window;
            if (w == null) return;
            int shown = w.TranslatorItems.IndexOf(w.CurrentTranslatorItem);
            var objs = w.TranslatorItems.Select(i => doc.Objects.FindId(i.ObjectId)).Where(o => o != null).ToList();
            LoadObjects(doc, objs, shown);
            w.TranslatorMessage("Updated " + count + (count == 1 ? " object" : " objects") + ". Undo is available in Rhino.", StatusText.Level.Success);
        }

        public void OnPlaced(string what)
        {
            var w = Window;
            if (w != null) w.OnTranslatorPlaced(what);
        }

        // ------------------------------------------------------------------ viewport preview
        public override void ItemsChanged()
        {
            var doc = Doc;
            var w = Window;
            if (w == null || !w.ViewportPreviewOn || doc == null || w.TranslatorItems.Count == 0)
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
            foreach (var item in w.TranslatorItems.Where(i => i.Include && !i.IsNew))
            {
                var obj = doc.Objects.FindId(item.ObjectId);
                if (obj == null) continue;
                try
                {
                    var ann = AnnotationUpdater.BuildUpdatedGeometry(doc, obj, item.BuildRecord(w.RecordSourceLanguage(item.Source), w.TranslatorTargetLanguage), fmt, fields, cfg, printView);
                    if (ann != null) list.Add(new KeyValuePair<AnnotationBase, Guid>(ann, obj.Attributes.Space == ActiveSpace.PageSpace ? obj.Attributes.ViewportId : Guid.Empty));
                }
                catch
                {
                    // preview only
                }
            }
            _conduit.SetItems(list);
            _conduit.Enabled = list.Count > 0;
            doc.Views.Redraw();
        }

        // ------------------------------------------------------------------ print tools
        public override string PrintInfo()
        {
            var s = Settings;
            return s.AutoSwapEnabled
                ? "Automatic switching is on: text set to show the source on screen switches to the translation during " +
                  string.Join(", ", TextXSettings.SplitList(s.PrintCommands).ToArray()) + " and when exporting " +
                  string.Join(" ", TextXSettings.SplitList(s.PrintLikeExtensions).ToArray()) + " files, then switches back."
                : "Automatic switching is off (Settings). Use Export PDF with translation, or the TRPreview command.";
        }

        public override bool PrintPreviewOn
        {
            get
            {
                var doc = Doc;
                return doc != null && PrintSwapManager.Instance.IsPreviewOn(doc);
            }
        }

        public override void ManageObjects()
        {
            ManageDialog.ShowFor(Doc ?? RhinoDoc.ActiveDoc, Window);
        }
    }
}
