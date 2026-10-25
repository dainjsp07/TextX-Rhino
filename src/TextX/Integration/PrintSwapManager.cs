using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using TextX.Core;
using TextX.Core.Text;

namespace TextX.Integration
{
    public sealed class SwapReport
    {
        public int Objects;
        public int BlockDefinitions;
        public int Stale;
        public int Retranslated;
        public int PrintedAsSource;
        public readonly List<string> Messages = new List<string>();

        public override string ToString()
        {
            var parts = new List<string> { Objects + " annotation(s)" };
            if (BlockDefinitions > 0) parts.Add(BlockDefinitions + " block definition(s)");
            if (Stale > 0) parts.Add(Stale + " edited since translation");
            if (Retranslated > 0) parts.Add(Retranslated + " re-translated");
            if (PrintedAsSource > 0) parts.Add(PrintedAsSource + " kept in source language");
            return string.Join(", ", parts.ToArray());
        }
    }

    public struct AnnotationRef
    {
        public RhinoObject Object;
        public TranslationRecord Record;
    }

    /// <summary>
    /// Keeps layout/model annotations in the source language (English) on screen and switches
    /// translated annotations to their translation while Rhino prints or writes a PDF:
    ///  * Print (and any command listed in settings): switched at command start, restored at command end.
    ///  * Export / Save As to .pdf/.ai/.eps/.svg: switched when Rhino reports the file name
    ///    (BeginSaveDocument) and restored after the file is written.
    ///  * Saving a .3dm while switched writes the screen (source) text, never the print text.
    ///  * Crash safety: the state is stored on every object, so any leftover print state is
    ///    restored automatically when a file is opened.
    /// </summary>
    public sealed class PrintSwapManager
    {
        public const string OwnerPreview = "preview";
        public const string OwnerSave = "save";
        public const string OwnerExportCommand = "export-pdf";

        private static readonly PrintSwapManager _instance = new PrintSwapManager();
        public static PrintSwapManager Instance { get { return _instance; } }

        private readonly Dictionary<uint, HashSet<string>> _owners = new Dictionary<uint, HashSet<string>>();
        private readonly Dictionary<uint, bool> _modifiedBefore = new Dictionary<uint, bool>();
        private readonly HashSet<uint> _saveGuard = new HashSet<uint>();
        private readonly HashSet<uint> _recoveryQueue = new HashSet<uint>();
        private readonly Dictionary<uint, string> _pendingExport = new Dictionary<uint, string>();
        private bool _attached;
        private bool _busy;

        /// <summary>Raised when a document switches between screen and print text.</summary>
        public event EventHandler StateChanged;

        private static TextXSettings Settings { get { return TextXPlugIn.Instance.Options; } }

        // ------------------------------------------------------------------ wiring
        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            Command.BeginCommand += OnBeginCommand;
            Command.EndCommand += OnEndCommand;
            RhinoDoc.BeginSaveDocument += OnBeginSaveDocument;
            RhinoDoc.EndSaveDocument += OnEndSaveDocument;
            RhinoDoc.EndOpenDocument += OnEndOpenDocument;
            RhinoDoc.CloseDocument += OnCloseDocument;
            FilePdf.PreWrite += OnPdfPreWrite;
            RhinoApp.Idle += OnIdle;
            foreach (var doc in RhinoDoc.OpenDocuments()) _recoveryQueue.Add(doc.RuntimeSerialNumber);
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            Command.BeginCommand -= OnBeginCommand;
            Command.EndCommand -= OnEndCommand;
            RhinoDoc.BeginSaveDocument -= OnBeginSaveDocument;
            RhinoDoc.EndSaveDocument -= OnEndSaveDocument;
            RhinoDoc.EndOpenDocument -= OnEndOpenDocument;
            RhinoDoc.CloseDocument -= OnCloseDocument;
            FilePdf.PreWrite -= OnPdfPreWrite;
            RhinoApp.Idle -= OnIdle;
        }

        // ------------------------------------------------------------------ public API
        public bool IsSwapped(RhinoDoc doc)
        {
            HashSet<string> owners;
            return doc != null && _owners.TryGetValue(doc.RuntimeSerialNumber, out owners) && owners.Count > 0 && !_saveGuard.Contains(doc.RuntimeSerialNumber);
        }

        public bool IsPreviewOn(RhinoDoc doc)
        {
            HashSet<string> owners;
            return doc != null && _owners.TryGetValue(doc.RuntimeSerialNumber, out owners) && owners.Contains(OwnerPreview);
        }

        /// <summary>Requests print text for the document. The first owner performs the switch.</summary>
        public SwapReport Acquire(RhinoDoc doc, string owner)
        {
            if (doc == null) return null;
            HashSet<string> owners;
            if (!_owners.TryGetValue(doc.RuntimeSerialNumber, out owners))
            {
                owners = new HashSet<string>();
                _owners[doc.RuntimeSerialNumber] = owners;
            }
            bool first = owners.Count == 0;
            owners.Add(owner);
            if (!first) return null;
            _modifiedBefore[doc.RuntimeSerialNumber] = doc.Modified;
            var report = SetDocState(doc, TextState.Print);
            if (report.Objects == 0 && report.BlockDefinitions == 0)
            {
                // nothing to switch - keep the owner registered so Release stays balanced
                return report;
            }
            if (Settings.ShowSwapMessages)
                RhinoApp.WriteLine("Text X: print text active ({0}).", report);
            foreach (var m in report.Messages) RhinoApp.WriteLine("Text X: " + m);
            RaiseStateChanged();
            return report;
        }

        /// <summary>Releases an owner; the last owner restores the screen text.</summary>
        public SwapReport Release(RhinoDoc doc, string owner)
        {
            if (doc == null) return null;
            HashSet<string> owners;
            if (!_owners.TryGetValue(doc.RuntimeSerialNumber, out owners) || !owners.Remove(owner)) return null;
            if (owners.Count > 0) return null;
            _owners.Remove(doc.RuntimeSerialNumber);
            var report = SetDocState(doc, TextState.Display);
            bool wasModified;
            if (_modifiedBefore.TryGetValue(doc.RuntimeSerialNumber, out wasModified) && owner != OwnerPreview)
                doc.Modified = wasModified;
            _modifiedBefore.Remove(doc.RuntimeSerialNumber);
            if (report.Objects > 0 && Settings.ShowSwapMessages)
                RhinoApp.WriteLine("Text X: screen text restored ({0} annotation(s)).", report.Objects + report.BlockDefinitions);
            RaiseStateChanged();
            return report;
        }

        /// <summary>Drops all owners and restores every annotation that is in print state.</summary>
        public SwapReport ForceRestore(RhinoDoc doc)
        {
            if (doc == null) return null;
            _owners.Remove(doc.RuntimeSerialNumber);
            _saveGuard.Remove(doc.RuntimeSerialNumber);
            _modifiedBefore.Remove(doc.RuntimeSerialNumber);
            var report = SetDocState(doc, TextState.Display);
            RaiseStateChanged();
            return report;
        }

        public void RestoreAllOnShutdown()
        {
            foreach (var doc in RhinoDoc.OpenDocuments())
            {
                try { ForceRestore(doc); }
                catch { /* shutting down */ }
            }
        }

        /// <summary>Applies print state to a single object (used when editing while preview is on).</summary>
        public void ApplyCurrentStateToObject(RhinoDoc doc, Guid id)
        {
            if (!IsSwapped(doc)) return;
            var obj = doc.Objects.FindId(id);
            if (obj == null) return;
            var rec = AnnotationEngine.ReadRecord(obj.Attributes);
            if (rec == null || !rec.NeedsPrintSwap || rec.State == TextState.Print) return;
            if (!InScope(obj)) return;
            ReplaceState(doc, obj, rec, TextState.Print, RenderConfig.FromSettings(Settings));
        }

        // ------------------------------------------------------------------ enumeration
        public static IEnumerable<AnnotationRef> EnumerateRecords(RhinoDoc doc)
        {
            var settings = new ObjectEnumeratorSettings
            {
                NormalObjects = true,
                LockedObjects = true,
                HiddenObjects = true,
                ActiveObjects = true,
                ReferenceObjects = false,
                IncludeLights = false,
                IncludeGrips = false,
                ObjectTypeFilter = ObjectType.Annotation,
            };
            foreach (var obj in doc.Objects.GetObjectList(settings))
            {
                if (obj.IsReference || !AnnotationEngine.IsSupported(obj)) continue;
                var rec = AnnotationEngine.ReadRecord(obj.Attributes);
                if (rec != null) yield return new AnnotationRef { Object = obj, Record = rec };
            }
        }

        public static bool DocumentHasDualText(RhinoDoc doc)
        {
            return EnumerateRecords(doc).Any(r => r.Record.NeedsPrintSwap);
        }

        internal static bool IsUsable(RhinoDoc doc)
        {
            if (doc == null) return false;
            try { return !doc.IsClosing && !doc.IsOpening; }
            catch { return false; }
        }

        private static bool InScope(RhinoObject obj)
        {
            return Settings.Scope == SwapScope.AllObjects || obj.Attributes.Space == ActiveSpace.PageSpace;
        }

        // ------------------------------------------------------------------ the switch itself
        private SwapReport SetDocState(RhinoDoc doc, TextState target)
        {
            var report = new SwapReport();
            if (_busy || !IsUsable(doc)) return report;
            _busy = true;
            bool modified = doc.Modified;
            try
            {
                var cfg = RenderConfig.FromSettings(Settings);
                var items = EnumerateRecords(doc)
                    .Where(r => target == TextState.Print
                        ? r.Record.NeedsPrintSwap && r.Record.State == TextState.Display && InScope(r.Object)
                        : r.Record.State == TextState.Print)
                    .ToList();

                if (target == TextState.Print) HandleStaleText(doc, items, cfg, report);

                foreach (var item in items)
                {
                    if (item.Record.State == target) continue;
                    if (target == TextState.Print && !item.Record.NeedsPrintSwap) continue;
                    try
                    {
                        if (ReplaceState(doc, item.Object, item.Record, target, cfg)) report.Objects++;
                    }
                    catch (Exception ex)
                    {
                        report.Messages.Add("could not update object " + item.Object.Id + ": " + ex.Message);
                    }
                }

                if (Settings.IncludeBlockDefinitions) SwapBlockDefinitions(doc, target, cfg, report);
                if (report.Objects > 0 || report.BlockDefinitions > 0) doc.Views.Redraw();
            }
            finally
            {
                // switching between screen and print text is not a user edit
                if (report.Objects > 0 || report.BlockDefinitions > 0) doc.Modified = modified;
                _busy = false;
            }
            return report;
        }

        /// <summary>Writes the state change of one document object (geometry + attributes).</summary>
        internal static bool ReplaceState(RhinoDoc doc, RhinoObject obj, TranslationRecord rec, TextState target, RenderConfig cfg)
        {
            var ann = obj.Geometry.Duplicate() as AnnotationBase;
            if (ann == null) return false;
            if (!AnnotationEngine.ApplyState(ann, rec, target, cfg)) return false;
            var attrs = obj.Attributes.Duplicate();
            AnnotationEngine.WriteRecord(attrs, rec);
            var id = obj.Id;
            if (!doc.Objects.Replace(id, ann, true)) return false;
            doc.Objects.ModifyAttributes(id, attrs, true);
            return true;
        }

        /// <summary>
        /// Handles annotations whose source text was edited with Rhino's own text tools after
        /// they were translated (their stored translation no longer matches).
        /// </summary>
        private void HandleStaleText(RhinoDoc doc, List<AnnotationRef> items, RenderConfig cfg, SwapReport report)
        {
            var stale = new List<AnnotationRef>();
            foreach (var item in items)
            {
                var ann = item.Object.Geometry as AnnotationBase;
                if (ann == null) continue;
                if (AnnotationEngine.SyncFromObject(ann, item.Record, cfg)) stale.Add(item);
            }
            if (stale.Count == 0) return;
            report.Stale = stale.Count;

            var handling = Settings.StaleHandling;
            if (handling == StaleTranslationHandling.Retranslate)
            {
                var groups = stale.GroupBy(s => s.Record.SourceLang + "|" + s.Record.TargetLang);
                foreach (var g in groups)
                {
                    var list = g.ToList();
                    string error;
                    var outcome = TranslationServices.TranslateBlocking(list.Select(s => s.Record.Source).ToList(),
                        list[0].Record.SourceLang, list[0].Record.TargetLang, TimeSpan.FromSeconds(15), out error);
                    if (outcome != null && outcome.Complete)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            list[i].Record.Translation = outcome.Translations[i];
                            list[i].Record.ManuallyEdited = false;
                            list[i].Record.Touch();
                            report.Retranslated++;
                        }
                    }
                    else
                    {
                        report.Messages.Add("re-translation of edited text failed (" + (error ?? "incomplete") + "); those annotations print in the source language.");
                        foreach (var s in list) PersistSource(doc, s, report);
                    }
                }
            }
            else if (handling == StaleTranslationHandling.PrintSource)
            {
                foreach (var s in stale) PersistSource(doc, s, report);
            }
            // PrintOldTranslation: nothing to do, the stored translation is used.
        }

        /// <summary>Stores the edited source and excludes the object from this print swap.</summary>
        private static void PersistSource(RhinoDoc doc, AnnotationRef item, SwapReport report)
        {
            var attrs = item.Object.Attributes.Duplicate();
            AnnotationEngine.WriteRecord(attrs, item.Record);
            doc.Objects.ModifyAttributes(item.Object.Id, attrs, true);
            item.Record.Translation = item.Record.Source; // makes NeedsPrintSwap false for this run only
            report.PrintedAsSource++;
        }

        private void SwapBlockDefinitions(RhinoDoc doc, TextState target, RenderConfig cfg, SwapReport report)
        {
            foreach (var idef in doc.InstanceDefinitions)
            {
                if (idef == null || idef.IsDeleted || idef.IsReference || idef.IsTenuous) continue;
                if (idef.UpdateType != InstanceDefinitionUpdateType.Static && (int)idef.UpdateType != 1) continue; // static or (legacy) embedded
                var objects = idef.GetObjects();
                bool needs = false;
                foreach (var o in objects)
                {
                    if (!AnnotationEngine.IsSupported(o)) continue;
                    var r = AnnotationEngine.ReadRecord(o.Attributes);
                    if (r == null) continue;
                    if (target == TextState.Print ? (r.NeedsPrintSwap && r.State == TextState.Display) : r.State == TextState.Print)
                    {
                        needs = true;
                        break;
                    }
                }
                if (!needs) continue;

                var geometry = new List<GeometryBase>(objects.Length);
                var attributes = new List<ObjectAttributes>(objects.Length);
                foreach (var o in objects)
                {
                    var g = o.Geometry.Duplicate();
                    var a = o.Attributes.Duplicate();
                    var ann = g as AnnotationBase;
                    var r = AnnotationEngine.IsSupported(o) ? AnnotationEngine.ReadRecord(a) : null;
                    if (ann != null && r != null)
                    {
                        bool apply = target == TextState.Print ? (r.NeedsPrintSwap && r.State == TextState.Display) : r.State == TextState.Print;
                        if (apply && AnnotationEngine.ApplyState(ann, r, target, cfg)) AnnotationEngine.WriteRecord(a, r);
                    }
                    geometry.Add(g);
                    attributes.Add(a);
                }
                if (doc.InstanceDefinitions.ModifyGeometry(idef.Index, geometry, attributes)) report.BlockDefinitions++;
                else report.Messages.Add("could not update block definition '" + idef.Name + "'.");
            }
        }

        // ------------------------------------------------------------------ event handlers
        private void OnBeginCommand(object sender, CommandEventArgs e)
        {
            Diagnostics.Log("swap.OnBeginCommand");
            try
            {
                if (_busy || !Settings.AutoSwapEnabled) return;
                var doc = e.Document ?? RhinoDoc.ActiveDoc;
                if (doc == null) return;
                var name = e.CommandEnglishName ?? string.Empty;
                if (Settings.IsPrintCommand(name))
                {
                    if (DocumentHasDualText(doc)) Acquire(doc, "cmd:" + name);
                }
                else if (Settings.IsExportCommand(name))
                {
                    _pendingExport[doc.RuntimeSerialNumber] = name;
                    if (Settings.SwapExportsAtCommandStart && DocumentHasDualText(doc)) Acquire(doc, "cmd:" + name);
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Text X: print switch failed - " + ex.Message);
            }
        }

        private void OnEndCommand(object sender, CommandEventArgs e)
        {
            Diagnostics.Log("swap.OnEndCommand");
            try
            {
                var doc = e.Document ?? RhinoDoc.ActiveDoc;
                if (doc == null) return;
                var name = e.CommandEnglishName ?? string.Empty;
                _pendingExport.Remove(doc.RuntimeSerialNumber);
                Release(doc, "cmd:" + name);
                Release(doc, OwnerSave);
                if (_saveGuard.Remove(doc.RuntimeSerialNumber) && IsSwapped(doc)) SetDocState(doc, TextState.Print);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Text X: restoring screen text failed - " + ex.Message + " (run TRRestore).");
            }
        }

        private void OnBeginSaveDocument(object sender, DocumentSaveEventArgs e)
        {
            Diagnostics.Log("swap.OnBeginSaveDocument");
            try
            {
                var doc = e.Document;
                if (doc == null || _busy) return;
                if (Settings.AutoSwapEnabled && Settings.IsPrintLikeFile(e.FileName))
                {
                    if (DocumentHasDualText(doc)) Acquire(doc, OwnerSave);
                }
                else if (IsSwapped(doc))
                {
                    // never write print text into a model file: restore temporarily
                    _saveGuard.Add(doc.RuntimeSerialNumber);
                    SetDocState(doc, TextState.Display);
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Text X: " + ex.Message);
            }
        }

        private void OnEndSaveDocument(object sender, DocumentSaveEventArgs e)
        {
            Diagnostics.Log("swap.OnEndSaveDocument");
            try
            {
                var doc = e.Document;
                if (doc == null) return;
                if (_saveGuard.Remove(doc.RuntimeSerialNumber))
                {
                    HashSet<string> owners;
                    if (_owners.TryGetValue(doc.RuntimeSerialNumber, out owners) && owners.Count > 0)
                        SetDocState(doc, TextState.Print);
                }
                Release(doc, OwnerSave);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Text X: " + ex.Message);
            }
        }

        private void OnPdfPreWrite(object sender, FilePdfEventArgs e)
        {
            try
            {
                if (!Settings.AutoSwapEnabled) return;
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null || IsSwapped(doc)) return;
                if (!DocumentHasDualText(doc)) return;
                string pending;
                if (_pendingExport.TryGetValue(doc.RuntimeSerialNumber, out pending) && !Settings.SwapExportsAtCommandStart)
                {
                    Settings.SwapExportsAtCommandStart = true;
                    RhinoApp.WriteLine("Text X: this PDF was written before Rhino reported its file name, so it contains the screen text.");
                    RhinoApp.WriteLine("Text X: from now on translated text is switched in at the start of " + pending +
                                       ". Please export the PDF again (or use TRExportPdf).");
                }
                else
                {
                    RhinoApp.WriteLine("Text X: a PDF is being written without translated text. Use TRExportPdf or add the command to Text X settings > Print commands.");
                }
            }
            catch
            {
                // never break PDF writing
            }
        }

        private void OnEndOpenDocument(object sender, DocumentOpenEventArgs e)
        {
            Diagnostics.Log("swap.OnEndOpenDocument");
            if (e.Document != null) _recoveryQueue.Add(e.Document.RuntimeSerialNumber);
        }

        private void OnCloseDocument(object sender, DocumentEventArgs e)
        {
            Diagnostics.Log("swap.OnCloseDocument");
            if (e.Document == null) return;
            var sn = e.Document.RuntimeSerialNumber;
            _owners.Remove(sn);
            _saveGuard.Remove(sn);
            _modifiedBefore.Remove(sn);
            _recoveryQueue.Remove(sn);
            _pendingExport.Remove(sn);
        }

        private void OnIdle(object sender, EventArgs e)
        {
            if (_busy || Command.InCommand()) return;
            try
            {
                // 1) recover documents that were saved / crashed while showing print text
                if (_recoveryQueue.Count > 0)
                {
                    var serials = _recoveryQueue.ToList();
                    _recoveryQueue.Clear();
                    foreach (var sn in serials)
                    {
                        var doc = RhinoDoc.FromRuntimeSerialNumber(sn);
                        if (doc == null || IsSwapped(doc)) continue;
                        if (!IsUsable(doc)) { _recoveryQueue.Add(sn); continue; }
                        var report = SetDocState(doc, TextState.Display);
                        if (report.Objects + report.BlockDefinitions > 0)
                            RhinoApp.WriteLine("Text X: restored screen text for {0} annotation(s) that were saved in print state.", report.Objects + report.BlockDefinitions);
                    }
                }

                // 2) safety net: a command ended without EndCommand reaching us
                foreach (var kv in _owners.ToList())
                {
                    var stale = kv.Value.Where(o => o.StartsWith("cmd:", StringComparison.Ordinal) || o == OwnerSave).ToList();
                    if (stale.Count == 0) continue;
                    var doc = RhinoDoc.FromRuntimeSerialNumber(kv.Key);
                    if (doc == null) { _owners.Remove(kv.Key); continue; }
                    foreach (var o in stale) Release(doc, o);
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Text X: " + ex.Message);
            }
        }

        private void RaiseStateChanged()
        {
            var h = StateChanged;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
