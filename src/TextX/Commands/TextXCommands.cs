using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using TextX.Core;
using TextX.Core.Text;
using TextX.Integration;
using TextX.UI;

namespace TextX.Commands
{
    internal static class CommandHelpers
    {
        public static TextXSettings Settings { get { return TextXPlugIn.Instance.Options; } }

        public static List<RhinoObject> GetAnnotations(string prompt, bool allowPreselect = true)
        {
            var go = new GetObject();
            go.SetCommandPrompt(prompt);
            go.GeometryFilter = ObjectType.Annotation;
            go.SubObjectSelect = false;
            go.EnablePreSelect(allowPreselect, true);
            go.GetMultiple(1, 0);
            if (go.CommandResult() != Result.Success) return null;
            var list = new List<RhinoObject>();
            for (int i = 0; i < go.ObjectCount; i++)
            {
                var obj = go.Object(i).Object();
                if (AnnotationEngine.IsSupported(obj)) list.Add(obj);
            }
            return list;
        }

        public static string OptionName(Language l)
        {
            var chars = l.EnglishName.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }
    }

    /// <summary>TR: opens the Text X window (loads pre-selected text and leaders).</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e01")]
    public class TrCommand : Command
    {
        public override string EnglishName { get { return "TR"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var window = TextXWindow.ShowWindow(doc);
            var selected = doc.Objects.GetSelectedObjects(false, false).Where(AnnotationEngine.IsSupported).ToList();
            if (selected.Count > 0) window.LoadObjects(doc, selected);
            return Result.Success;
        }
    }

    /// <summary>TextX: same as TR (use it if an alias named TR exists in Rhino).</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e0e")]
    public class TextXCommand : Command
    {
        public override string EnglishName { get { return "TextX"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var window = TextXWindow.ShowWindow(doc);
            var selected = doc.Objects.GetSelectedObjects(false, false).Where(AnnotationEngine.IsSupported).ToList();
            if (selected.Count > 0) window.LoadObjects(doc, selected);
            return Result.Success;
        }
    }

    /// <summary>Select text / leader objects and load them into the Text X window.</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e02")]
    public class TrEditCommand : Command
    {
        public override string EnglishName { get { return "TREdit"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var objs = CommandHelpers.GetAnnotations("Select text or leaders to translate / edit");
            if (objs == null) return Result.Cancel;
            if (objs.Count == 0) return Result.Nothing;
            TextXWindow.ShowWindow(doc).LoadObjects(doc, objs);
            return Result.Success;
        }
    }

    /// <summary>Places the text from the Text X window at a picked point (like the Text command).</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e03")]
    public class TrPlaceTextCommand : Command
    {
        public override string EnglishName { get { return "TRPlaceText"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var window = TextXWindow.Instance;
            if (window == null)
            {
                TextXWindow.ShowWindow(doc);
                RhinoApp.WriteLine("Type the text in the Text X window, then click \"Place text\".");
                return Result.Nothing;
            }
            var req = window.CreatePlacementRequest();
            if (req == null)
            {
                RhinoApp.WriteLine("Text X: the text is empty.");
                return Result.Nothing;
            }
            var cfg = RenderConfig.FromSettings(CommandHelpers.Settings);

            var cache = new Dictionary<Guid, TextEntity>();
            var gp = new GetPoint();
            gp.SetCommandPrompt("Text location");
            gp.DynamicDraw += (s, e) =>
            {
                try
                {
                    TextEntity te;
                    var vp = e.Viewport;
                    if (!cache.TryGetValue(vp.Id, out te))
                    {
                        var basePlane = vp.ConstructionPlane();
                        basePlane.Origin = Point3d.Origin;
                        if (Math.Abs(req.Format.RotationDegrees) > 1e-9) basePlane.Rotate(RhinoMath.ToRadians(req.Format.RotationDegrees), basePlane.ZAxis);
                        te = AnnotationEngine.CreateText(doc, basePlane, req.Record.Clone(), req.Format, cfg, req.AutoRightAlign);
                        cache[vp.Id] = te;
                    }
                    if (te != null) e.Display.DrawText(te, Color.FromArgb(230, 120, 0), Transform.Translation(e.CurrentPoint - Point3d.Origin));
                }
                catch
                {
                    // preview only
                }
            };
            gp.Get();
            if (gp.CommandResult() != Result.Success) return gp.CommandResult();

            var view = gp.View();
            var plane = AnnotationEngine.PlacementPlane(view, gp.Point(), req.Format.RotationDegrees);
            var text = AnnotationEngine.CreateText(doc, plane, req.Record, req.Format, cfg, req.AutoRightAlign);
            if (text == null)
            {
                RhinoApp.WriteLine("Text X: unable to create the text.");
                return Result.Failure;
            }
            var attrs = AnnotationEngine.NewAttributes(doc, view, req.Format);
            AnnotationEngine.WriteRecord(attrs, req.Record);
            var id = doc.Objects.AddText(text, attrs);
            if (id == Guid.Empty) return Result.Failure;
            doc.Objects.UnselectAll();
            doc.Objects.Select(id);
            PrintSwapManager.Instance.ApplyCurrentStateToObject(doc, id);
            doc.Views.Redraw();
            window.OnPlaced("Text");
            return Result.Success;
        }
    }

    /// <summary>Places the text from the Text X window as a leader (like the Leader command).</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e04")]
    public class TrPlaceLeaderCommand : Command
    {
        public override string EnglishName { get { return "TRPlaceLeader"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var window = TextXWindow.Instance;
            if (window == null)
            {
                TextXWindow.ShowWindow(doc);
                RhinoApp.WriteLine("Type the text in the Text X window, then click \"Place leader\".");
                return Result.Nothing;
            }
            var req = window.CreatePlacementRequest();
            if (req == null)
            {
                RhinoApp.WriteLine("Text X: the text is empty.");
                return Result.Nothing;
            }
            var cfg = RenderConfig.FromSettings(CommandHelpers.Settings);

            var points = new List<Point3d>();
            RhinoView view = null;
            var gp = new GetPoint();
            gp.SetCommandPrompt("Start of leader (arrow point)");
            gp.Get();
            if (gp.CommandResult() != Result.Success) return gp.CommandResult();
            points.Add(gp.Point());
            view = gp.View();
            var plane = view.ActiveViewport.ConstructionPlane();
            plane.Origin = points[0];

            while (true)
            {
                var next = new GetPoint();
                next.SetCommandPrompt(points.Count < 2 ? "Next point of leader" : "Next point of leader. Press Enter when done");
                next.AcceptNothing(points.Count >= 2);
                next.SetBasePoint(points[points.Count - 1], true);
                next.DrawLineFromPoint(points[points.Count - 1], true);
                next.Constrain(plane, false);
                var snapshot = points.ToArray();
                next.DynamicDraw += (s, e) =>
                {
                    try
                    {
                        var pts = snapshot.Concat(new[] { e.CurrentPoint }).ToArray();
                        var ld = AnnotationEngine.CreateLeader(doc, plane, pts, req.Record.Clone(), req.Format, cfg);
                        if (ld != null) e.Display.DrawAnnotation(ld, Color.FromArgb(230, 120, 0));
                    }
                    catch
                    {
                        // preview only
                    }
                };
                var res = next.Get();
                if (res == GetResult.Nothing) break;
                if (next.CommandResult() != Result.Success) return next.CommandResult();
                points.Add(next.Point());
            }

            var leader = AnnotationEngine.CreateLeader(doc, plane, points.ToArray(), req.Record, req.Format, cfg);
            if (leader == null)
            {
                RhinoApp.WriteLine("Text X: unable to create the leader.");
                return Result.Failure;
            }
            var attrs = AnnotationEngine.NewAttributes(doc, view, req.Format);
            AnnotationEngine.WriteRecord(attrs, req.Record);
            var id = doc.Objects.AddLeader(leader, attrs);
            if (id == Guid.Empty) return Result.Failure;
            doc.Objects.UnselectAll();
            doc.Objects.Select(id);
            PrintSwapManager.Instance.ApplyCurrentStateToObject(doc, id);
            doc.Views.Redraw();
            window.OnPlaced("Leader");
            return Result.Success;
        }
    }

    /// <summary>Applies the Text X window edits to the loaded objects (undoable).</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e05")]
    public class TrApplyCommand : Command
    {
        public override string EnglishName { get { return "TRApply"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var window = TextXWindow.Instance;
            if (window == null) return Result.Nothing;
            var req = window.CreateApplyRequest();
            if (req.Items.Count == 0)
            {
                RhinoApp.WriteLine("Text X: no objects to update. Load objects with \"Load selected\" first.");
                return Result.Nothing;
            }
            if (req.DocSerial != doc.RuntimeSerialNumber)
            {
                RhinoApp.WriteLine("Text X: the loaded objects belong to another document.");
                return Result.Failure;
            }
            var cfg = RenderConfig.FromSettings(CommandHelpers.Settings);
            int count = 0;
            foreach (var kv in req.Items)
            {
                var obj = doc.Objects.FindId(kv.Key);
                string error;
                if (AnnotationUpdater.Apply(doc, obj, kv.Value, req.Format, req.Fields, cfg, out error)) count++;
                else RhinoApp.WriteLine("Text X: object {0} not updated - {1}.", kv.Key, error);
            }
            doc.Views.Redraw();
            window.OnObjectsApplied(doc, count);
            RhinoApp.WriteLine("Text X: updated {0} object(s).", count);
            return count > 0 ? Result.Success : Result.Failure;
        }
    }

    /// <summary>Translates many existing annotations at once from the command line.</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e06")]
    public class TrBatchCommand : Command
    {
        public override string EnglishName { get { return "TRBatch"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var s = CommandHelpers.Settings;
            var languages = Languages.All.ToList();
            int target = Math.Max(0, languages.FindIndex(l => l.Code == s.TargetLanguage));
            int displayMode = (int)s.DefaultMode;
            var overwrite = new OptionToggle(false, "No", "Yes");
            var modeNames = new[] { "EnglishOnScreen_TranslationInPrint", "TranslationEverywhere", "EnglishEverywhere" };

            var go = new GetObject();
            go.SetCommandPrompt("Select text and leaders to translate");
            go.GeometryFilter = ObjectType.Annotation;
            go.EnablePreSelect(true, true);
            go.GroupSelect = true;
            while (true)
            {
                go.ClearCommandOptions();
                int optLang = go.AddOptionList("Language", languages.Select(CommandHelpers.OptionName).ToArray(), target);
                int optMode = go.AddOptionList("Display", modeNames, displayMode);
                go.AddOptionToggle("RetranslateExisting", ref overwrite);
                var res = go.GetMultiple(1, 0);
                if (res == GetResult.Option)
                {
                    var opt = go.Option();
                    if (opt.Index == optLang) target = opt.CurrentListOptionIndex;
                    else if (opt.Index == optMode) displayMode = opt.CurrentListOptionIndex;
                    go.EnablePreSelect(false, true);
                    continue;
                }
                if (res != GetResult.Object) return go.CommandResult();
                break;
            }

            var tgt = languages[target].Code;
            var src = s.SourceLanguage == Languages.AutoDetect ? "en" : s.SourceLanguage;
            var cfg = RenderConfig.FromSettings(s);
            var work = new List<KeyValuePair<RhinoObject, TranslationRecord>>();
            int skipped = 0;
            for (int i = 0; i < go.ObjectCount; i++)
            {
                var obj = go.Object(i).Object();
                if (!AnnotationEngine.IsSupported(obj)) continue;
                var ann = (AnnotationBase)obj.Geometry;
                var existing = AnnotationEngine.ReadRecord(obj.Attributes);
                TranslationRecord rec;
                if (existing != null)
                {
                    rec = existing.Clone();
                    bool edited = AnnotationEngine.SyncFromObject(ann, rec, cfg);
                    if (!overwrite.CurrentValue && !edited && !string.IsNullOrEmpty(rec.Translation) && rec.TargetLang == tgt)
                    {
                        skipped++;
                        continue;
                    }
                }
                else
                {
                    rec = new TranslationRecord { Source = AnnotationEngine.GetText(ann), SourceLang = src };
                }
                rec.TargetLang = tgt;
                rec.Mode = (TranslationDisplayMode)displayMode;
                work.Add(new KeyValuePair<RhinoObject, TranslationRecord>(obj, rec));
            }
            if (work.Count == 0)
            {
                RhinoApp.WriteLine("Text X: nothing to translate ({0} already translated; use RetranslateExisting=Yes).", skipped);
                return Result.Nothing;
            }

            RhinoApp.WriteLine("Text X: translating {0} annotation(s) to {1}... (Esc cancels)", work.Count, languages[target].EnglishName);
            string error;
            var outcome = TranslationServices.TranslateBlocking(work.Select(w => w.Value.Source).ToList(), src, tgt, TimeSpan.FromSeconds(120), out error);
            if (outcome == null)
            {
                RhinoApp.WriteLine("Text X: translation failed - " + error);
                return Result.Failure;
            }

            int updated = 0;
            for (int i = 0; i < work.Count; i++)
            {
                var rec = work[i].Value;
                rec.Translation = outcome.Translations[i];
                rec.ManuallyEdited = false;
                rec.Provider = outcome.ProviderUsed;
                rec.Touch();
                var displayRec = rec.Clone();
                displayRec.State = TextState.Display;
                string err;
                if (AnnotationUpdater.Apply(doc, doc.Objects.FindId(work[i].Key.Id), displayRec, new TextFormat(), FormatFields.None, cfg, out err)) updated++;
                else RhinoApp.WriteLine("Text X: {0} - {1}", work[i].Key.Id, err);
            }
            doc.Views.Redraw();
            RhinoApp.WriteLine("Text X: {0} annotation(s) translated{1}{2}.", updated,
                skipped > 0 ? ", " + skipped + " already translated (skipped)" : string.Empty,
                string.IsNullOrEmpty(outcome.Warning) ? string.Empty : " - " + outcome.Warning);
            if (!outcome.Complete) RhinoApp.WriteLine("Text X: some segments could not be translated and were left unchanged.");
            return Result.Success;
        }
    }

    /// <summary>Shows the print text (translations) in the viewports, or switches back.</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e07")]
    public class TrPreviewCommand : Command
    {
        public override string EnglishName { get { return "TRPreview"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var mgr = PrintSwapManager.Instance;
            var go = new GetOption();
            go.SetCommandPrompt("Show print text (translations) in viewports");
            int on = go.AddOption("On");
            int off = go.AddOption("Off");
            int toggle = go.AddOption("Toggle");
            go.AcceptNothing(true);
            var res = go.Get();
            bool target;
            if (res == GetResult.Option)
            {
                int idx = go.Option().Index;
                target = idx == on || (idx == toggle && !mgr.IsPreviewOn(doc));
                if (idx == off) target = false;
            }
            else if (res == GetResult.Nothing)
            {
                target = !mgr.IsPreviewOn(doc);
            }
            else return Result.Cancel;

            if (target)
            {
                var report = mgr.Acquire(doc, PrintSwapManager.OwnerPreview);
                if (report != null && report.Objects + report.BlockDefinitions == 0 && !PrintSwapManager.DocumentHasDualText(doc))
                    RhinoApp.WriteLine("Text X: no annotations are set to \"English on screen, translation in print\".");
                RhinoApp.WriteLine("Text X: print preview ON - viewports show what will be printed. Run TRPreview again to switch back.");
            }
            else
            {
                mgr.Release(doc, PrintSwapManager.OwnerPreview);
                RhinoApp.WriteLine("Text X: print preview OFF.");
            }
            return Result.Success;
        }
    }

    /// <summary>Recovery: switches every annotation back to its screen text.</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e08")]
    public class TrRestoreCommand : Command
    {
        public override string EnglishName { get { return "TRRestore"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var report = PrintSwapManager.Instance.ForceRestore(doc);
            RhinoApp.WriteLine("Text X: restored screen text for {0} annotation(s), {1} block definition(s).",
                report == null ? 0 : report.Objects, report == null ? 0 : report.BlockDefinitions);
            return Result.Success;
        }
    }

    /// <summary>Exports layouts to PDF with translated text, independent of Rhino's print hooks.</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e09")]
    public class TrExportPdfCommand : Command
    {
        public override string EnglishName { get { return "TRExportPdf"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var s = CommandHelpers.Settings;
            var pages = doc.Views.GetPageViews().OrderBy(p => p.PageNumber).ToList();
            if (pages.Count == 0)
            {
                RhinoApp.WriteLine("Text X: the document has no layouts. Use Print for model views.");
                return Result.Nothing;
            }

            int which = 0; // 0 all, 1 current, 2 select
            var dpi = new OptionInteger(s.PdfDpi, 72, 2400);
            var open = new OptionToggle(s.PdfOpenAfterExport, "No", "Yes");
            var go = new GetOption();
            go.SetCommandPrompt("Export layouts to PDF with translated text");
            go.AcceptNothing(true);
            while (true)
            {
                go.ClearCommandOptions();
                int optLayouts = go.AddOptionList("Layouts", new[] { "All", "Current", "Select" }, which);
                go.AddOptionInteger("Dpi", ref dpi);
                go.AddOptionToggle("OpenPdf", ref open);
                var res = go.Get();
                if (res == GetResult.Option)
                {
                    if (go.Option().Index == optLayouts) which = go.Option().CurrentListOptionIndex;
                    continue;
                }
                if (res == GetResult.Nothing) break;
                return Result.Cancel;
            }
            s.PdfDpi = dpi.CurrentValue;
            s.PdfOpenAfterExport = open.CurrentValue;

            List<RhinoPageView> selected;
            if (which == 1)
            {
                var active = doc.Views.ActiveView as RhinoPageView;
                if (active == null)
                {
                    RhinoApp.WriteLine("Text X: the active view is not a layout.");
                    return Result.Failure;
                }
                selected = new List<RhinoPageView> { active };
            }
            else if (which == 2)
            {
                var names = pages.Select(p => p.PageName).ToList();
                var chosen = Rhino.UI.Dialogs.ShowMultiListBox("Export PDF", "Layouts to export", names, names);
                if (chosen == null || chosen.Length == 0) return Result.Cancel;
                selected = pages.Where(p => chosen.Contains(p.PageName)).ToList();
            }
            else selected = pages;

            string path;
            var defaultName = (string.IsNullOrEmpty(doc.Name) ? "Layouts" : Path.GetFileNameWithoutExtension(doc.Name)) + ".pdf";
            if (mode == RunMode.Interactive)
            {
                var sfd = new Rhino.UI.SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", DefaultExt = "pdf", FileName = defaultName, Title = "Export PDF with translation" };
                if (!string.IsNullOrEmpty(doc.Path)) sfd.InitialDirectory = Path.GetDirectoryName(doc.Path);
                if (!sfd.ShowSaveDialog()) return Result.Cancel;
                path = sfd.FileName;
            }
            else
            {
                if (RhinoGet.GetString("PDF file name", false, ref defaultName) != Result.Success) return Result.Cancel;
                path = defaultName;
            }
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";

            var mgr = PrintSwapManager.Instance;
            mgr.Acquire(doc, PrintSwapManager.OwnerExportCommand);
            try
            {
                var pdf = FilePdf.Create();
                if (pdf == null)
                {
                    RhinoApp.WriteLine("Text X: Rhino's PDF writer is not available. Use Print with the \"Rhino PDF\" destination (translations are switched in automatically).");
                    return Result.Failure;
                }
                foreach (var page in selected)
                {
                    var settings = new ViewCaptureSettings(page, dpi.CurrentValue);
                    if (!settings.IsValid)
                    {
                        RhinoApp.WriteLine("Text X: skipped layout '{0}' (invalid page settings).", page.PageName);
                        continue;
                    }
                    pdf.AddPage(settings);
                }
                pdf.Write(path);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Text X: PDF export failed - " + ex.Message);
                return Result.Failure;
            }
            finally
            {
                mgr.Release(doc, PrintSwapManager.OwnerExportCommand);
            }
            RhinoApp.WriteLine("Text X: wrote {0} layout(s) to {1}", selected.Count, path);
            if (open.CurrentValue)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { /* no PDF viewer */ }
            }
            return Result.Success;
        }
    }

    /// <summary>Lists all translated annotations in the document.</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e0a")]
    public class TrManageCommand : Command
    {
        public override string EnglishName { get { return "TRManage"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            ManageDialog.ShowFor(doc, null);
            return Result.Success;
        }
    }

    /// <summary>TRSettings: opens the Text X settings.</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e0b")]
    public class TrSettingsCommand : Command
    {
        public override string EnglishName { get { return "TRSettings"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            SettingsDialog.ShowFor(null);
            return Result.Success;
        }
    }

    /// <summary>Removes translation data from objects (keeps the source or the translation as plain text).</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e0c")]
    public class TrRemoveCommand : Command
    {
        public override string EnglishName { get { return "TRRemove"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var keep = new OptionToggle(false, "Source", "Translation");
            var go = new GetObject();
            go.SetCommandPrompt("Select translated text / leaders to remove translation data from");
            go.GeometryFilter = ObjectType.Annotation;
            go.EnablePreSelect(true, true);
            while (true)
            {
                go.ClearCommandOptions();
                go.AddOptionToggle("KeepText", ref keep);
                var res = go.GetMultiple(1, 0);
                if (res == GetResult.Option) { go.EnablePreSelect(false, true); continue; }
                if (res != GetResult.Object) return go.CommandResult();
                break;
            }
            var cfg = RenderConfig.FromSettings(CommandHelpers.Settings);
            int n = 0;
            for (int i = 0; i < go.ObjectCount; i++)
            {
                var obj = go.Object(i).Object();
                if (AnnotationEngine.IsSupported(obj) && AnnotationUpdater.RemoveTranslation(doc, obj, keep.CurrentValue, cfg)) n++;
            }
            doc.Views.Redraw();
            RhinoApp.WriteLine("Text X: removed translation data from {0} object(s).", n);
            return Result.Success;
        }
    }

    /// <summary>
    /// Places calibration samples to verify right-to-left rendering on screen and in PDF output.
    /// </summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e0d")]
    public class TrRtlTestCommand : Command
    {
        public override string EnglishName { get { return "TRRtlTest"; } }

        private const string HebrewSample = "שלום עולם 123 (בדיקה) מפלס -0.45, קנה מידה 1:50";
        private const string ArabicSample = "مرحبا بالعالم 123 (اختبار)";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            Point3d origin;
            if (RhinoGet.GetPoint("Location of the right-to-left test samples", false, out origin) != Result.Success) return Result.Cancel;
            var view = doc.Views.ActiveView;
            var style = doc.DimStyles.Current;
            double h = style.TextHeight > 0 ? style.TextHeight : 1;
            double step = h * 2.2;
            var opts = CommandHelpers.Settings.CreateRtlOptions();
            opts.Direction = BaseDirection.Rtl;
            var cfg = RenderConfig.FromSettings(CommandHelpers.Settings);

            var rows = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("A  Hebrew, Visual mode (plug-in default):", RtlText.ToVisual(HebrewSample, opts)),
                new KeyValuePair<string, string>("B  Hebrew, Logical mode (as typed):", HebrewSample),
                new KeyValuePair<string, string>("C  Arabic, Visual + shaping:", RtlText.ToVisual(ArabicSample, opts)),
                new KeyValuePair<string, string>("D  Arabic, Logical (as typed):", ArabicSample),
                new KeyValuePair<string, string>("Check: the correct row reads right-to-left; 123, -0.45 and 1:50 keep their digit order; brackets face the right way.", null),
            };

            doc.Objects.UnselectAll();
            var ids = new List<Guid>();
            for (int i = 0; i < rows.Count; i++)
            {
                var y = origin + new Vector3d(0, -i * step, 0);
                ids.Add(AddPlain(doc, view, rows[i].Key, y, style, TextHorizontalAlignment.Left));
                if (i < 4)
                    ids.Add(AddPlain(doc, view, rows[i].Value, y + new Vector3d(h * 75, 0, 0), style, TextHorizontalAlignment.Right));
            }

            // Dual-mode sample to test the print/PDF switch
            var rec = new TranslationRecord
            {
                Source = "Ground floor plan  -0.45  1:50",
                Translation = "תוכנית קומת קרקע  -0.45  1:50",
                SourceLang = "en",
                TargetLang = "he",
                Mode = TranslationDisplayMode.Dual,
                PrintAlign = PrintAlignment.Inherit,
            };
            var fmt = new TextFormat { DimStyleId = style.Id, Height = 0 };
            var plane = AnnotationEngine.PlacementPlane(view, origin + new Vector3d(0, -(rows.Count + 1) * step, 0), 0);
            var te = AnnotationEngine.CreateText(doc, plane, rec, fmt, cfg, false);
            var attrs = AnnotationEngine.NewAttributes(doc, view, fmt);
            AnnotationEngine.WriteRecord(attrs, rec);
            ids.Add(doc.Objects.AddText(te, attrs));
            ids.Add(AddPlain(doc, view, "E  Print / PDF test: this line is English on screen and Hebrew when printed (try Print > Rhino PDF).",
                origin + new Vector3d(0, -(rows.Count + 2) * step, 0), style, TextHorizontalAlignment.Left));

            foreach (var id in ids.Where(g => g != Guid.Empty)) doc.Objects.Select(id);
            doc.Views.Redraw();
            RhinoApp.WriteLine("Text X RTL test: row A (and C) must read correctly on screen. If row B reads correctly instead, set Settings > Right-to-left > Screen to \"Logical\".");
            RhinoApp.WriteLine("Then print the view/layout to PDF: row E must appear in Hebrew in the PDF and in English on screen. If the PDF reverses Hebrew, change Settings > Right-to-left > Print.");
            return Result.Success;
        }

        private static Guid AddPlain(RhinoDoc doc, RhinoView view, string text, Point3d at, DimensionStyle style, TextHorizontalAlignment align)
        {
            var plane = AnnotationEngine.PlacementPlane(view, at, 0);
            var te = TextEntity.Create(text, plane, style, false, 0, 0);
            if (te == null) return Guid.Empty;
            te.TextHorizontalAlignment = align;
            return doc.Objects.AddText(te, AnnotationEngine.NewAttributes(doc, view, new TextFormat()));
        }
    }

    /// <summary>TRAbout: version, install location and update status.</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e0f")]
    public class TrAboutCommand : Command
    {
        public override string EnglishName { get { return "TRAbout"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine(string.Format("{0} {1} ({2}) - {3}", BuildInfo.ProductName, BuildInfo.VersionText, BuildInfo.BuildDate, BuildInfo.InstallFolder));
            if (mode == RunMode.Interactive) AboutDialog.ShowFor(TextXWindow.Instance);
            return Result.Success;
        }
    }

    /// <summary>TRUpdate: checks for a newer release now and opens its download page.</summary>
    [Guid("2d0d6d3e-5a63-4c51-9f0f-6a1d4a7b9e10")]
    public class TrUpdateCommand : Command
    {
        public override string EnglishName { get { return "TRUpdate"; } }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var updates = UpdateService.Instance;
            if (!updates.IsConfigured)
            {
                RhinoApp.WriteLine("{0} {1}: this build has no update location. Ask whoever gave you Text X for the latest version.", BuildInfo.ProductName, BuildInfo.VersionText);
                return Result.Nothing;
            }
            RhinoApp.WriteLine("{0}: checking for updates...", BuildInfo.ProductName);
            Core.Updates.ReleaseInfo latest;
            try
            {
                var task = System.Threading.Tasks.Task.Run(() => updates.CheckAsync(System.Threading.CancellationToken.None));
                while (!task.IsCompleted) RhinoApp.Wait();
                latest = task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("{0}: could not check for updates - {1}", BuildInfo.ProductName, ex.GetBaseException().Message);
                return Result.Failure;
            }
            if (!updates.UpdateAvailable)
            {
                RhinoApp.WriteLine("{0} {1} is the latest version.", BuildInfo.ProductName, BuildInfo.VersionText);
                return Result.Success;
            }
            RhinoApp.WriteLine("{0} {1} is available (you have {2}).", BuildInfo.ProductName, latest.Version, BuildInfo.VersionText);
            if (mode == RunMode.Interactive)
            {
                var answer = Rhino.UI.Dialogs.ShowMessage(
                    BuildInfo.ProductName + " " + latest.Version + " is available (you have " + BuildInfo.VersionText + ").\n\n" +
                    "Open the download page now?\n\nTo install: drag the downloaded .yak file into Rhino, then restart Rhino.",
                    BuildInfo.ProductName + " update", Rhino.UI.ShowMessageButton.YesNo, Rhino.UI.ShowMessageIcon.Information);
                if (answer == Rhino.UI.ShowMessageResult.Yes) UpdateService.OpenUrl(latest.BestUrl);
            }
            else
            {
                RhinoApp.WriteLine("Download: {0}", latest.BestUrl);
            }
            return Result.Success;
        }
    }
}
