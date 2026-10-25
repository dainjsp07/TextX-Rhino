using System;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using TextX.Core;

namespace TextX.Integration
{
    /// <summary>Updates existing text / leader objects in place (same object id, attributes preserved).</summary>
    public static class AnnotationUpdater
    {
        /// <summary>
        /// Builds the updated geometry without touching the document (used for Apply and the viewport preview).
        /// </summary>
        public static AnnotationBase BuildUpdatedGeometry(RhinoDoc doc, RhinoObject obj, TranslationRecord rec, TextFormat fmt,
                                                          FormatFields fields, RenderConfig cfg, bool printState)
        {
            ObjectAttributes attrs;
            return Build(doc, obj, rec, fmt, fields, cfg, printState, out attrs);
        }

        private static AnnotationBase Build(RhinoDoc doc, RhinoObject obj, TranslationRecord rec, TextFormat fmt,
                                            FormatFields fields, RenderConfig cfg, bool printState, out ObjectAttributes attrs)
        {
            attrs = obj.Attributes.Duplicate();
            var ann = obj.Geometry.Duplicate() as AnnotationBase;
            if (ann == null) return null;

            // bring the object back to its screen state first (restores alignment, anchor and wrapping)
            var existing = AnnotationEngine.ReadRecord(obj.Attributes);
            if (existing != null && existing.State == TextState.Print)
                AnnotationEngine.ApplyState(ann, existing, TextState.Display, cfg);
            if (existing != null && rec.WrapWidth <= 0) rec.WrapWidth = existing.WrapWidth;

            AnnotationEngine.ApplyFormat(doc, ann, attrs, fmt, fields & ~(FormatFields.Wrap | FormatFields.Font | FormatFields.Bold | FormatFields.Italic | FormatFields.Underline));

            double? wrapRequest = null;
            if ((fields & FormatFields.Wrap) != 0) wrapRequest = fmt.Wrap ? fmt.WrapWidth : 0;
            else if (!ann.TextIsWrapped && rec.WrapWidth > 0) wrapRequest = rec.WrapWidth;

            rec.State = TextState.Display;
            rec.SavedHAlign = -1;
            rec.SavedWrapped = -1;
            rec.SavedFormatWidth = 0;
            rec.AnchorShift = null;
            AnnotationEngine.ComposeInto(ann, rec, TextState.Display, cfg, wrapRequest);

            // explicit run formatting after the text was replaced
            AnnotationEngine.ApplyFormat(doc, ann, null, fmt, fields & (FormatFields.Font | FormatFields.Bold | FormatFields.Italic | FormatFields.Underline));

            if (printState && rec.NeedsPrintSwap) AnnotationEngine.ApplyState(ann, rec, TextState.Print, cfg);
            return ann;
        }

        /// <summary>Applies text, translation metadata and changed format fields to a document object.</summary>
        public static bool Apply(RhinoDoc doc, RhinoObject obj, TranslationRecord rec, TextFormat fmt, FormatFields fields, RenderConfig cfg, out string error)
        {
            error = null;
            if (obj == null) { error = "object not found"; return false; }
            if (obj.IsReference) { error = "worksession reference objects cannot be changed"; return false; }
            if (string.IsNullOrWhiteSpace(rec.TextFor(TextState.Display)))
            {
                error = "the text is empty";
                return false;
            }
            ObjectAttributes attrs;
            var ann = Build(doc, obj, rec, fmt, fields, cfg, false, out attrs);
            if (ann == null) { error = "not a text or leader object"; return false; }
            AnnotationEngine.WriteRecord(attrs, rec);
            var id = obj.Id;
            if (!doc.Objects.Replace(id, ann, true)) { error = "Rhino refused to replace the object"; return false; }
            doc.Objects.ModifyAttributes(id, attrs, true);
            PrintSwapManager.Instance.ApplyCurrentStateToObject(doc, id);
            return true;
        }

        /// <summary>Removes translation data; optionally leaves the translation (instead of the source) as plain text.</summary>
        public static bool RemoveTranslation(RhinoDoc doc, RhinoObject obj, bool keepTranslation, RenderConfig cfg)
        {
            var rec = AnnotationEngine.ReadRecord(obj.Attributes);
            if (rec == null) return false;
            var ann = obj.Geometry.Duplicate() as AnnotationBase;
            if (ann == null) return false;
            if (rec.State == TextState.Print) AnnotationEngine.ApplyState(ann, rec, TextState.Display, cfg);
            var final = rec.Clone();
            final.Mode = keepTranslation ? TranslationDisplayMode.Translated : TranslationDisplayMode.Source;
            AnnotationEngine.ComposeInto(ann, final, TextState.Display, cfg, null);
            var attrs = obj.Attributes.Duplicate();
            AnnotationEngine.RemoveRecord(attrs);
            doc.Objects.Replace(obj.Id, ann, true);
            doc.Objects.ModifyAttributes(obj.Id, attrs, true);
            return true;
        }
    }
}
