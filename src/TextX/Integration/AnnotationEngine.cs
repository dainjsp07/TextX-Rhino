using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using TextX.Core;
using TextX.Core.Text;

namespace TextX.Integration
{
    /// <summary>Which formatting fields to apply when updating existing objects.</summary>
    [Flags]
    public enum FormatFields
    {
        None = 0,
        DimStyle = 1 << 0,
        Font = 1 << 1,
        Bold = 1 << 2,
        Italic = 1 << 3,
        Underline = 1 << 4,
        Height = 1 << 5,
        HAlign = 1 << 6,
        VAlign = 1 << 7,
        Orientation = 1 << 8,
        Wrap = 1 << 9,
        Mask = 1 << 10,
        Layer = 1 << 11,
        Color = 1 << 12,
        Arrow = 1 << 13,
        LeaderCurve = 1 << 14,
        Landing = 1 << 15,
        All = (1 << 16) - 1,
    }

    /// <summary>Formatting state, mirroring the options of Rhino's Text / Leader dialogs.</summary>
    public sealed class TextFormat
    {
        public Guid DimStyleId = Guid.Empty;
        public string FontFace;            // null / empty = annotation style font
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public double Height;              // 0 = style height
        public TextHorizontalAlignment HAlign = TextHorizontalAlignment.Left;
        public TextVerticalAlignment VAlign = TextVerticalAlignment.Bottom;
        public double RotationDegrees;     // new text only
        public bool HorizontalToView;
        public bool Wrap;
        public double WrapWidth;
        public bool MaskEnabled;
        public bool MaskUsesViewportColor = true;
        public Color MaskColor = Color.White;
        public double MaskOffset;
        public DimensionStyle.MaskFrame MaskFrame = DimensionStyle.MaskFrame.NoFrame;
        public int LayerIndex = -1;        // -1 = current layer
        public bool ColorByLayer = true;
        public Color ObjectColor = Color.Black;
        // leader
        public DimensionStyle.ArrowType ArrowType = DimensionStyle.ArrowType.SolidTriangle;
        public double ArrowSize;           // 0 = style
        public DimensionStyle.LeaderCurveStyle CurveStyle = DimensionStyle.LeaderCurveStyle.Polyline;
        public bool Landing = true;
        public double LandingLength;       // 0 = style
        public TextHorizontalAlignment LeaderHAlign = TextHorizontalAlignment.Auto;

        public TextFormat Clone() { return (TextFormat)MemberwiseClone(); }
    }

    /// <summary>Rendering configuration used when writing text into objects.</summary>
    public sealed class RenderConfig
    {
        public RtlRenderMode ScreenMode = RtlRenderMode.Visual;
        public RtlRenderMode PrintMode = RtlRenderMode.Visual;
        public RtlOptions Rtl = new RtlOptions();

        public static RenderConfig FromSettings(TextXSettings s)
        {
            return new RenderConfig { ScreenMode = s.ScreenRtlMode, PrintMode = s.PrintRtlMode, Rtl = s.CreateRtlOptions() };
        }

        public RtlRenderMode ModeFor(TextState state)
        {
            return state == TextState.Print ? PrintMode : ScreenMode;
        }
    }

    /// <summary>
    /// All direct manipulation of Rhino annotation geometry: reading/writing text while preserving
    /// formatting, capturing/applying formatting, RTL wrapping and the display/print state swap.
    /// </summary>
    public static class AnnotationEngine
    {
        // ------------------------------------------------------------------ record I/O
        public static TranslationRecord ReadRecord(ObjectAttributes attrs)
        {
            if (attrs == null) return null;
            return TranslationRecord.Read(attrs.GetUserString);
        }

        public static void WriteRecord(ObjectAttributes attrs, TranslationRecord rec)
        {
            rec.Write((k, v) =>
            {
                if (v == null) attrs.DeleteUserString(k);
                else attrs.SetUserString(k, v);
            });
        }

        public static void RemoveRecord(ObjectAttributes attrs)
        {
            TranslationRecord.Remove((k, v) => attrs.DeleteUserString(k));
        }

        public static bool IsSupported(RhinoObject obj)
        {
            return obj != null && (obj.Geometry is TextEntity || obj.Geometry is Leader);
        }

        // ------------------------------------------------------------------ text read/write
        /// <summary>Current plain text with fields unevaluated, normalized to '\n' line breaks.</summary>
        public static string GetText(AnnotationBase ann)
        {
            string text;
            try { text = ann.PlainTextWithFields; }
            catch { text = ann.PlainText; }
            return RtlText.NormalizeNewlines(text ?? string.Empty);
        }

        /// <summary>
        /// Replaces the text content while keeping uniform run formatting (font override, bold,
        /// italic, underline) that would otherwise be lost when plain text is assigned.
        /// </summary>
        public static void SetText(AnnotationBase ann, string text)
        {
            if (string.IsNullOrEmpty(text)) text = " ";
            bool bold = false, italic = false, underline = false;
            string runFace = null;
            try
            {
                bold = ann.IsAllBold();
                italic = ann.IsAllItalic();
                underline = ann.IsAllUnderlined();
                var first = ann.FirstCharFont;
                var baseFont = ann.Font;
                if (first != null && baseFont != null &&
                    !string.Equals(first.QuartetName, baseFont.QuartetName, StringComparison.OrdinalIgnoreCase))
                    runFace = first.QuartetName;
            }
            catch
            {
                // empty annotations have no runs
            }

            ann.PlainText = RtlText.NormalizeNewlines(text).Replace("\n", System.Environment.NewLine);

            if (!string.IsNullOrEmpty(runFace)) ann.SetFacename(true, runFace);
            if (bold && !ann.IsAllBold()) ann.SetBold(true);
            if (italic && !ann.IsAllItalic()) ann.SetItalic(true);
            if (underline && !ann.IsAllUnderlined()) ann.SetUnderline(true);
            if (ann.TextIsWrapped) ann.WrapText();
        }

        /// <summary>Returns a function measuring the model width of a single-line string in the annotation's style.</summary>
        public static Func<string, double> CreateMeasurer(AnnotationBase template)
        {
            var probe = (AnnotationBase)template.Duplicate();
            probe.TextIsWrapped = false;
            return s =>
            {
                probe.PlainText = string.IsNullOrEmpty(s) ? " " : s;
                return probe.TextModelWidth;
            };
        }

        public static TextHorizontalAlignment GetHAlign(AnnotationBase ann)
        {
            var te = ann as TextEntity;
            if (te != null) return te.TextHorizontalAlignment;
            var ld = ann as Leader;
            return ld != null ? ld.LeaderTextHorizontalAlignment : TextHorizontalAlignment.Left;
        }

        public static void SetHAlign(AnnotationBase ann, TextHorizontalAlignment h)
        {
            var te = ann as TextEntity;
            if (te != null) { te.TextHorizontalAlignment = h == TextHorizontalAlignment.Auto ? TextHorizontalAlignment.Left : h; return; }
            var ld = ann as Leader;
            if (ld != null) ld.LeaderTextHorizontalAlignment = h;
        }

        public static TextVerticalAlignment GetVAlign(AnnotationBase ann)
        {
            var te = ann as TextEntity;
            if (te != null) return te.TextVerticalAlignment;
            var ld = ann as Leader;
            return ld != null ? ld.LeaderTextVerticalAlignment : TextVerticalAlignment.Bottom;
        }

        public static void SetVAlign(AnnotationBase ann, TextVerticalAlignment v)
        {
            var te = ann as TextEntity;
            if (te != null) { te.TextVerticalAlignment = v; return; }
            var ld = ann as Leader;
            if (ld != null) ld.LeaderTextVerticalAlignment = v;
        }

        private static TextHorizontalAlignment ToHAlign(PrintAlignment a)
        {
            switch (a)
            {
                case PrintAlignment.Center: return TextHorizontalAlignment.Center;
                case PrintAlignment.Right: return TextHorizontalAlignment.Right;
                default: return TextHorizontalAlignment.Left;
            }
        }

        // ------------------------------------------------------------------ format capture/apply
        public static TextFormat CaptureFormat(AnnotationBase ann, ObjectAttributes attrs)
        {
            var f = new TextFormat();
            f.DimStyleId = ann.DimensionStyleId;
            try
            {
                var first = ann.FirstCharFont ?? ann.Font;
                if (first != null) f.FontFace = first.QuartetName;
                f.Bold = ann.IsAllBold();
                f.Italic = ann.IsAllItalic();
                f.Underline = ann.IsAllUnderlined();
            }
            catch
            {
                // no runs
            }
            f.Height = ann.TextHeight;
            f.HAlign = GetHAlign(ann);
            f.VAlign = GetVAlign(ann);
            f.RotationDegrees = ann.TextRotationDegrees;
            var te = ann as TextEntity;
            if (te != null) f.HorizontalToView = te.TextOrientation == TextOrientation.InView;
            f.Wrap = ann.TextIsWrapped;
            f.WrapWidth = ann.FormatWidth;
            f.MaskEnabled = ann.MaskEnabled;
            f.MaskUsesViewportColor = ann.MaskUsesViewportColor;
            f.MaskColor = ann.MaskColor;
            f.MaskOffset = ann.MaskOffset;
            f.MaskFrame = ann.MaskFrame;
            var ld = ann as Leader;
            if (ld != null)
            {
                f.ArrowType = ld.LeaderArrowType;
                f.ArrowSize = ld.LeaderArrowSize;
                f.CurveStyle = ld.LeaderCurveStyle;
                f.Landing = ld.LeaderHasLanding;
                f.LandingLength = ld.LeaderLandingLength;
                f.LeaderHAlign = ld.LeaderTextHorizontalAlignment;
            }
            if (attrs != null)
            {
                f.LayerIndex = attrs.LayerIndex;
                f.ColorByLayer = attrs.ColorSource == ObjectColorSource.ColorFromLayer;
                f.ObjectColor = attrs.ObjectColor;
            }
            return f;
        }

        /// <summary>
        /// Applies the selected fields. Text wrapping is handled by <see cref="ComposeInto"/> because it
        /// depends on the text direction.
        /// </summary>
        public static void ApplyFormat(RhinoDoc doc, AnnotationBase ann, ObjectAttributes attrs, TextFormat f, FormatFields fields)
        {
            if ((fields & FormatFields.DimStyle) != 0 && f.DimStyleId != Guid.Empty && f.DimStyleId != ann.DimensionStyleId)
            {
                var style = doc.DimStyles.FindId(f.DimStyleId);
                if (style != null) ann.DimensionStyleId = style.Id;
            }
            if ((fields & FormatFields.Font) != 0)
            {
                var styleFont = ann.Font;
                if (!string.IsNullOrWhiteSpace(f.FontFace))
                {
                    if (styleFont == null || !string.Equals(styleFont.QuartetName, f.FontFace, StringComparison.OrdinalIgnoreCase))
                    {
                        var font = Rhino.DocObjects.Font.FromQuartetProperties(f.FontFace, false, false) ?? new Rhino.DocObjects.Font(f.FontFace);
                        ann.Font = font;
                    }
                    ann.SetFacename(true, f.FontFace);
                }
            }
            if ((fields & FormatFields.Bold) != 0) ann.SetBold(f.Bold);
            if ((fields & FormatFields.Italic) != 0) ann.SetItalic(f.Italic);
            if ((fields & FormatFields.Underline) != 0) ann.SetUnderline(f.Underline);
            if ((fields & FormatFields.Height) != 0 && f.Height > 0 && Math.Abs(ann.TextHeight - f.Height) > 1e-9) ann.TextHeight = f.Height;
            if ((fields & FormatFields.HAlign) != 0)
            {
                if (ann is Leader) SetHAlign(ann, f.LeaderHAlign);
                else SetHAlign(ann, f.HAlign);
            }
            if ((fields & FormatFields.VAlign) != 0) SetVAlign(ann, f.VAlign);
            var te = ann as TextEntity;
            if (te != null && (fields & FormatFields.Orientation) != 0)
                te.TextOrientation = f.HorizontalToView ? TextOrientation.InView : TextOrientation.InPlane;
            if ((fields & FormatFields.Mask) != 0)
            {
                ann.MaskEnabled = f.MaskEnabled;
                if (f.MaskEnabled)
                {
                    ann.MaskUsesViewportColor = f.MaskUsesViewportColor;
                    if (!f.MaskUsesViewportColor) ann.MaskColor = f.MaskColor;
                    ann.MaskOffset = f.MaskOffset;
                    ann.MaskFrame = f.MaskFrame;
                }
            }
            var ld = ann as Leader;
            if (ld != null)
            {
                if ((fields & FormatFields.Arrow) != 0)
                {
                    ld.LeaderArrowType = f.ArrowType;
                    if (f.ArrowSize > 0) ld.LeaderArrowSize = f.ArrowSize;
                }
                if ((fields & FormatFields.LeaderCurve) != 0) ld.LeaderCurveStyle = f.CurveStyle;
                if ((fields & FormatFields.Landing) != 0)
                {
                    ld.LeaderHasLanding = f.Landing;
                    if (f.LandingLength > 0) ld.LeaderLandingLength = f.LandingLength;
                }
            }
            if (attrs != null)
            {
                if ((fields & FormatFields.Layer) != 0 && f.LayerIndex >= 0) attrs.LayerIndex = f.LayerIndex;
                if ((fields & FormatFields.Color) != 0)
                {
                    if (f.ColorByLayer) attrs.ColorSource = ObjectColorSource.ColorFromLayer;
                    else
                    {
                        attrs.ColorSource = ObjectColorSource.ColorFromObject;
                        attrs.ObjectColor = f.ObjectColor;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ composition
        /// <summary>
        /// Writes the text for <paramref name="state"/> into the annotation, handling RTL ordering and wrapping.
        /// For RTL text in Visual mode, Rhino's own word-wrap is replaced by explicit line breaks
        /// (Rhino would otherwise wrap the reversed string from the wrong end).
        /// </summary>
        /// <param name="ann">The text or leader to write into.</param>
        /// <param name="rec">The object's source text and translation.</param>
        /// <param name="state">Which text to show (screen or print).</param>
        /// <param name="cfg">Right-to-left rendering settings.</param>
        /// <param name="wrapRequest">null = keep the current wrap configuration; otherwise the wanted wrap width (0 = off).</param>
        public static void ComposeInto(AnnotationBase ann, TranslationRecord rec, TextState state, RenderConfig cfg, double? wrapRequest)
        {
            string logical = rec.TextFor(state);
            string lang = rec.LanguageFor(state);
            var mode = cfg.ModeFor(state);
            bool rtlManual = RtlText.ContainsRtl(logical) && mode == RtlRenderMode.Visual;

            double width;
            if (wrapRequest.HasValue) width = wrapRequest.Value;
            else if (ann.TextIsWrapped && ann.FormatWidth > 0) width = ann.FormatWidth;
            else width = rec.WrapWidth;

            if (rtlManual)
            {
                if (width > 0)
                {
                    rec.WrapWidth = width;
                    ann.TextIsWrapped = false;
                    var composed = DisplayComposer.Compose(logical, lang, mode, cfg.Rtl, width, CreateMeasurer(ann));
                    SetText(ann, composed);
                }
                else
                {
                    rec.WrapWidth = 0;
                    ann.TextIsWrapped = false;
                    SetText(ann, DisplayComposer.Compose(logical, lang, mode, cfg.Rtl));
                }
            }
            else
            {
                var composed = DisplayComposer.Compose(logical, lang, mode, cfg.Rtl);
                if (width > 0)
                {
                    ann.FormatWidth = width;
                    ann.TextIsWrapped = true;
                    rec.WrapWidth = width;
                }
                else if (wrapRequest.HasValue)
                {
                    ann.TextIsWrapped = false;
                    rec.WrapWidth = 0;
                }
                SetText(ann, composed);
            }
        }

        /// <summary>Text that should currently be in the object if nobody edited it natively.</summary>
        public static string ExpectedText(AnnotationBase ann, TranslationRecord rec, TextState state, RenderConfig cfg)
        {
            string logical = rec.TextFor(state);
            string lang = rec.LanguageFor(state);
            var mode = cfg.ModeFor(state);
            bool rtlManual = RtlText.ContainsRtl(logical) && mode == RtlRenderMode.Visual;
            if (rtlManual && rec.WrapWidth > 0)
                return DisplayComposer.Compose(logical, lang, mode, cfg.Rtl, rec.WrapWidth, CreateMeasurer(ann));
            return DisplayComposer.Compose(logical, lang, mode, cfg.Rtl);
        }

        /// <summary>
        /// Whitespace-insensitive form used to detect native edits. Line breaks are ignored because
        /// Rhino may report soft-wrapped lines differently from the text that was assigned.
        /// </summary>
        public static string NormalizeForCompare(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(s ?? string.Empty, "\\s+", " ").Trim();
        }

        /// <summary>
        /// Detects that the object's text was edited with Rhino's own tools after the translation was
        /// stored. Updates the record's source (or translation) from the object and returns true.
        /// </summary>
        public static bool SyncFromObject(AnnotationBase ann, TranslationRecord rec, RenderConfig cfg)
        {
            if (rec.State != TextState.Display) return false;
            var current = NormalizeForCompare(GetText(ann));
            var expected = NormalizeForCompare(ExpectedText(ann, rec, TextState.Display, cfg));
            if (current == expected) return false;

            string logical = RtlText.ContainsRtl(current) && cfg.ScreenMode == RtlRenderMode.Visual
                ? RtlText.VisualToLogical(current)
                : current;
            if (rec.Mode == TranslationDisplayMode.Translated && !string.IsNullOrEmpty(rec.Translation))
            {
                rec.Translation = logical;
                rec.ManuallyEdited = true;
            }
            else
            {
                rec.Source = logical;
            }
            return true;
        }

        // ------------------------------------------------------------------ state swap
        /// <summary>
        /// Switches an annotation between its display (screen) and print content.
        /// Saves everything needed to restore the display state in the record.
        /// </summary>
        public static bool ApplyState(AnnotationBase ann, TranslationRecord rec, TextState target, RenderConfig cfg)
        {
            if (rec.State == target) return false;

            if (target == TextState.Print)
            {
                rec.SavedWrapped = ann.TextIsWrapped ? 1 : 0;
                rec.SavedFormatWidth = ann.FormatWidth;
                rec.SavedHAlign = (int)GetHAlign(ann);

                double dx = 0;
                var te = ann as TextEntity;
                if (te != null && rec.PrintAlign != PrintAlignment.Inherit && rec.AnchorToSourceBox)
                {
                    var plane = te.Plane;
                    var bb = te.GetBoundingBox(plane);
                    if (bb.IsValid)
                    {
                        switch (rec.PrintAlign)
                        {
                            case PrintAlignment.Left: dx = bb.Min.X; break;
                            case PrintAlignment.Right: dx = bb.Max.X; break;
                            case PrintAlignment.Center: dx = 0.5 * (bb.Min.X + bb.Max.X); break;
                        }
                    }
                }

                double width = ann.TextIsWrapped && ann.FormatWidth > 0 ? ann.FormatWidth : rec.WrapWidth;
                double savedRecWrap = rec.WrapWidth;
                ComposeInto(ann, rec, TextState.Print, cfg, width);
                rec.WrapWidth = savedRecWrap;

                if (rec.PrintAlign != PrintAlignment.Inherit) SetHAlign(ann, ToHAlign(rec.PrintAlign));
                if (te != null && Math.Abs(dx) > 1e-12) ann.Translate(te.Plane.XAxis * dx);
                rec.AnchorShift = TranslationRecord.FormatPoint(dx, 0, 0);
                rec.State = TextState.Print;
                return true;
            }
            else
            {
                double dx, dy, dz;
                var te = ann as TextEntity;
                if (te != null && TranslationRecord.TryParsePoint(rec.AnchorShift, out dx, out dy, out dz) && Math.Abs(dx) > 1e-12)
                    ann.Translate(te.Plane.XAxis * -dx);
                if (rec.SavedHAlign >= 0) SetHAlign(ann, (TextHorizontalAlignment)rec.SavedHAlign);

                double? wrapRequest = null;
                if (rec.SavedWrapped >= 0)
                {
                    if (rec.SavedWrapped == 1 && rec.SavedFormatWidth > 0) wrapRequest = rec.SavedFormatWidth;
                    else if (rec.SavedWrapped == 0) wrapRequest = rec.WrapWidth > 0 && RtlText.ContainsRtl(rec.TextFor(TextState.Display)) ? rec.WrapWidth : 0;
                }
                double savedRecWrap = rec.WrapWidth;
                ComposeInto(ann, rec, TextState.Display, cfg, wrapRequest);
                if (rec.SavedWrapped == 0 && !RtlText.ContainsRtl(rec.TextFor(TextState.Display))) rec.WrapWidth = savedRecWrap;

                rec.State = TextState.Display;
                rec.SavedHAlign = -1;
                rec.SavedWrapped = -1;
                rec.SavedFormatWidth = 0;
                rec.AnchorShift = null;
                return true;
            }
        }

        // ------------------------------------------------------------------ creation
        public static Plane PlacementPlane(RhinoView view, Point3d point, double rotationDegrees)
        {
            var plane = view != null ? view.ActiveViewport.ConstructionPlane() : Plane.WorldXY;
            plane.Origin = point;
            if (Math.Abs(rotationDegrees) > 1e-9) plane.Rotate(RhinoMath.ToRadians(rotationDegrees), plane.ZAxis);
            return plane;
        }

        public static DimensionStyle ResolveStyle(RhinoDoc doc, Guid id)
        {
            DimensionStyle style = null;
            if (id != Guid.Empty) style = doc.DimStyles.FindId(id);
            return style ?? doc.DimStyles.Current;
        }

        public static TextEntity CreateText(RhinoDoc doc, Plane plane, TranslationRecord rec, TextFormat fmt, RenderConfig cfg, bool autoRightAlignRtl)
        {
            var style = ResolveStyle(doc, fmt.DimStyleId);
            var te = TextEntity.Create(" ", plane, style, false, 0, 0);
            if (te == null) return null;
            ApplyFormat(doc, te, null, fmt, FormatFields.All & ~(FormatFields.Layer | FormatFields.Color | FormatFields.Arrow | FormatFields.LeaderCurve | FormatFields.Landing));
            if (autoRightAlignRtl && RtlText.ContainsRtl(rec.TextFor(TextState.Display)) && fmt.HAlign == TextHorizontalAlignment.Left)
                te.TextHorizontalAlignment = TextHorizontalAlignment.Right;
            rec.State = TextState.Display;
            ComposeInto(te, rec, TextState.Display, cfg, fmt.Wrap ? fmt.WrapWidth : 0);
            ApplyRunFormat(te, fmt);
            return te;
        }

        public static Leader CreateLeader(RhinoDoc doc, Plane plane, Point3d[] points, TranslationRecord rec, TextFormat fmt, RenderConfig cfg)
        {
            var style = ResolveStyle(doc, fmt.DimStyleId);
            var ld = Leader.Create(" ", plane, style, points);
            if (ld == null) return null;
            ApplyFormat(doc, ld, null, fmt, FormatFields.All & ~(FormatFields.Layer | FormatFields.Color | FormatFields.Orientation));
            rec.State = TextState.Display;
            ComposeInto(ld, rec, TextState.Display, cfg, fmt.Wrap ? fmt.WrapWidth : 0);
            ApplyRunFormat(ld, fmt);
            return ld;
        }

        private static void ApplyRunFormat(AnnotationBase ann, TextFormat fmt)
        {
            if (!string.IsNullOrWhiteSpace(fmt.FontFace)) ann.SetFacename(true, fmt.FontFace);
            if (fmt.Bold) ann.SetBold(true);
            if (fmt.Italic) ann.SetItalic(true);
            if (fmt.Underline) ann.SetUnderline(true);
        }

        /// <summary>Attributes for a new object: current layer/defaults, page space if a layout page is active.</summary>
        public static ObjectAttributes NewAttributes(RhinoDoc doc, RhinoView view, TextFormat fmt)
        {
            var attrs = doc.CreateDefaultAttributes();
            var page = view as RhinoPageView;
            if (page != null && page.PageIsActive)
            {
                attrs.Space = ActiveSpace.PageSpace;
                attrs.ViewportId = page.MainViewport.Id;
            }
            if (fmt.LayerIndex >= 0 && fmt.LayerIndex < doc.Layers.Count && !doc.Layers[fmt.LayerIndex].IsDeleted)
                attrs.LayerIndex = fmt.LayerIndex;
            if (!fmt.ColorByLayer)
            {
                attrs.ColorSource = ObjectColorSource.ColorFromObject;
                attrs.ObjectColor = fmt.ObjectColor;
            }
            return attrs;
        }
    }
}
