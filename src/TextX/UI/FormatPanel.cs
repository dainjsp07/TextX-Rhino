using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;
using EtoColor = Eto.Drawing.Color;
using SdColor = System.Drawing.Color;

namespace TextX.UI
{
    /// <summary>
    /// Text and leader formatting controls (the options of Rhino's Text / Leader dialogs).
    /// Tracks which fields the user changed so updates only touch those properties.
    /// </summary>
    public sealed class FormatPanel
    {
        private bool _loading;
        private Guid _currentStyleId = Guid.Empty;

        private readonly DropDown _style = new DropDown();
        private readonly ComboBox _font = new ComboBox { AutoComplete = true };
        private readonly CheckBox _bold = new CheckBox { Text = "Bold" };
        private readonly CheckBox _italic = new CheckBox { Text = "Italic" };
        private readonly CheckBox _underline = new CheckBox { Text = "Underline" };
        private readonly NumericStepper _height = new NumericStepper { DecimalPlaces = 3, MinValue = 0, Increment = 0.5, Width = 90 };
        private readonly RadioButtonList _hAlign = new RadioButtonList { Orientation = Orientation.Horizontal, Spacing = new Size(8, 2) };
        private readonly RadioButtonList _vAlign = new RadioButtonList { Orientation = Orientation.Horizontal, Spacing = new Size(8, 2) };
        private readonly CheckBox _autoRight = new CheckBox { Text = "Right-align new right-to-left text" };
        private readonly NumericStepper _rotation = new NumericStepper { DecimalPlaces = 2, MinValue = -360, MaxValue = 360, Increment = 15, Width = 90 };
        private readonly CheckBox _toView = new CheckBox { Text = "Horizontal to view" };
        private readonly CheckBox _wrap = new CheckBox { Text = "Wrap text at width" };
        private readonly NumericStepper _wrapWidth = new NumericStepper { DecimalPlaces = 3, MinValue = 0, Increment = 1, Width = 90 };
        private readonly DropDown _mask = new DropDown();
        private readonly ColorPicker _maskColor = new ColorPicker();
        private readonly NumericStepper _maskMargin = new NumericStepper { DecimalPlaces = 3, MinValue = 0, Increment = 0.1, Width = 90 };
        private readonly DropDown _maskFrame = new DropDown();
        private readonly DropDown _layer = new DropDown();
        private readonly CheckBox _byLayer = new CheckBox { Text = "Color by layer" };
        private readonly ColorPicker _color = new ColorPicker();

        private readonly DropDown _arrow = new DropDown();
        private readonly NumericStepper _arrowSize = new NumericStepper { DecimalPlaces = 3, MinValue = 0, Increment = 0.5, Width = 90 };
        private readonly DropDown _curve = new DropDown();
        private readonly CheckBox _landing = new CheckBox { Text = "Landing" };
        private readonly NumericStepper _landingLength = new NumericStepper { DecimalPlaces = 3, MinValue = 0, Increment = 0.5, Width = 90 };
        private readonly DropDown _leaderAlign = new DropDown();

        // plain values only: never keep document-owned objects (they become invalid when the document closes)
        private List<KeyValuePair<Guid, string>> _styles = new List<KeyValuePair<Guid, string>>();
        private List<KeyValuePair<int, string>> _layers = new List<KeyValuePair<int, string>>();

        private static readonly TextHorizontalAlignment[] HValues = { TextHorizontalAlignment.Left, TextHorizontalAlignment.Center, TextHorizontalAlignment.Right };
        private static readonly TextVerticalAlignment[] VValues = { TextVerticalAlignment.Top, TextVerticalAlignment.Middle, TextVerticalAlignment.Bottom };
        private static readonly DimensionStyle.ArrowType[] ArrowValues =
        {
            DimensionStyle.ArrowType.SolidTriangle, DimensionStyle.ArrowType.OpenArrow, DimensionStyle.ArrowType.ShortTriangle,
            DimensionStyle.ArrowType.LongTriangle, DimensionStyle.ArrowType.LongerTriangle, DimensionStyle.ArrowType.Dot,
            DimensionStyle.ArrowType.Tick, DimensionStyle.ArrowType.Rectangle, DimensionStyle.ArrowType.None,
        };
        private static readonly TextHorizontalAlignment[] LeaderAlignValues =
        {
            TextHorizontalAlignment.Auto, TextHorizontalAlignment.Left, TextHorizontalAlignment.Center, TextHorizontalAlignment.Right,
        };

        public event EventHandler Changed;

        public FormatFields Dirty { get; private set; }

        public FormatPanel()
        {
            _hAlign.Items.Add("Left");
            _hAlign.Items.Add("Center");
            _hAlign.Items.Add("Right");
            _vAlign.Items.Add("Top");
            _vAlign.Items.Add("Middle");
            _vAlign.Items.Add("Bottom");
            foreach (var s in new[] { "No mask", "Background (viewport) color", "Solid color" }) _mask.Items.Add(s);
            foreach (var s in new[] { "No frame", "Rectangle frame", "Capsule frame" }) _maskFrame.Items.Add(s);
            foreach (var a in ArrowValues) _arrow.Items.Add(a.ToString());
            foreach (var s in new[] { "Polyline", "Spline (curved)" }) _curve.Items.Add(s);
            foreach (var a in LeaderAlignValues) _leaderAlign.Items.Add(a == TextHorizontalAlignment.Auto ? "Auto (from leader direction)" : a.ToString());

            Hook(_style, FormatFields.DimStyle);
            _font.TextChanged += (s, e) => Mark(FormatFields.Font);
            _bold.CheckedChanged += (s, e) => Mark(FormatFields.Bold);
            _italic.CheckedChanged += (s, e) => Mark(FormatFields.Italic);
            _underline.CheckedChanged += (s, e) => Mark(FormatFields.Underline);
            _height.ValueChanged += (s, e) => Mark(FormatFields.Height);
            _hAlign.SelectedIndexChanged += (s, e) => Mark(FormatFields.HAlign);
            _vAlign.SelectedIndexChanged += (s, e) => Mark(FormatFields.VAlign);
            _rotation.ValueChanged += (s, e) => Mark(FormatFields.None);
            _toView.CheckedChanged += (s, e) => Mark(FormatFields.Orientation);
            _wrap.CheckedChanged += (s, e) => { _wrapWidth.Enabled = _wrap.Checked == true; Mark(FormatFields.Wrap); };
            _wrapWidth.ValueChanged += (s, e) => Mark(FormatFields.Wrap);
            _mask.SelectedIndexChanged += (s, e) => { UpdateMaskEnabled(); Mark(FormatFields.Mask); };
            _maskColor.ValueChanged += (s, e) => Mark(FormatFields.Mask);
            _maskMargin.ValueChanged += (s, e) => Mark(FormatFields.Mask);
            Hook(_maskFrame, FormatFields.Mask);
            Hook(_layer, FormatFields.Layer);
            _byLayer.CheckedChanged += (s, e) => { _color.Enabled = _byLayer.Checked != true; Mark(FormatFields.Color); };
            _color.ValueChanged += (s, e) => Mark(FormatFields.Color);
            Hook(_arrow, FormatFields.Arrow);
            _arrowSize.ValueChanged += (s, e) => Mark(FormatFields.Arrow);
            Hook(_curve, FormatFields.LeaderCurve);
            _landing.CheckedChanged += (s, e) => { _landingLength.Enabled = _landing.Checked == true; Mark(FormatFields.Landing); };
            _landingLength.ValueChanged += (s, e) => Mark(FormatFields.Landing);
            Hook(_leaderAlign, FormatFields.HAlign);
            _autoRight.CheckedChanged += (s, e) => Mark(FormatFields.None);

            _height.ToolTip = "Text height (model or page units). Creates an annotation-style override.";
            _wrapWidth.ToolTip = "Right-to-left text is wrapped by the plug-in (Rhino's own wrapping would break reversed lines).";
            _autoRight.ToolTip = "When the screen text is Hebrew/Arabic, new text is right-aligned at the picked point.";
            _rotation.ToolTip = "Rotation of new text in the construction plane (use Rotate for existing objects).";
        }

        private void Hook(DropDown dd, FormatFields field)
        {
            dd.SelectedIndexChanged += (s, e) => Mark(field);
        }

        private void Mark(FormatFields field)
        {
            if (_loading) return;
            Dirty |= field;
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void ResetDirty()
        {
            Dirty = FormatFields.None;
        }

        private void UpdateMaskEnabled()
        {
            bool on = _mask.SelectedIndex > 0;
            _maskColor.Enabled = _mask.SelectedIndex == 2;
            _maskMargin.Enabled = on;
            _maskFrame.Enabled = on;
        }

        public bool AutoRightAlignRtl
        {
            get { return _autoRight.Checked == true; }
            set { _autoRight.Checked = value; }
        }

        // ------------------------------------------------------------------ layout
        private static Control Section(ThemeScope scope, string title, Action<DynamicLayout> fill)
        {
            var layout = new DynamicLayout { DefaultSpacing = new Size(10, 8) };
            layout.BeginVertical();
            layout.AddRow(Ui.Section(scope, title));
            layout.EndVertical();
            layout.BeginVertical(spacing: new Size(10, 8));
            fill(layout);
            layout.EndVertical();
            Ui.ThemeNativeChildren(scope, layout);
            return Ui.SurfaceCard(layout, new Padding(12, 10, 12, 12));
        }

        private static Label Caption(ThemeScope scope, string text)
        {
            var l = Ui.Label(scope, text, 12.5f, false, p => p.Muted);
            l.Width = 118;
            return l;
        }

        private static Label Note(ThemeScope scope, string text)
        {
            return Ui.Label(scope, text, 12f, false, p => p.Muted);
        }

        public Control CreateFormatPage(ThemeScope scope)
        {
            var page = new TableLayout { Spacing = new Size(0, 10), Padding = new Padding(14, 12) };
            page.Rows.Add(new TableRow(Section(scope, "Text", l =>
            {
                l.AddRow(Caption(scope, "Annotation style"), _style);
                l.AddRow(Caption(scope, "Font"), _font);
                l.AddRow(null, TableLayout.Horizontal(12, _bold, _italic, _underline, null));
                l.AddRow(Caption(scope, "Text height"), TableLayout.Horizontal(8, _height, Note(scope, "0 = annotation style height"), null));
            })));
            page.Rows.Add(new TableRow(Section(scope, "Alignment", l =>
            {
                l.AddRow(Caption(scope, "Horizontal"), _hAlign);
                l.AddRow(Caption(scope, "Vertical"), _vAlign);
                l.AddRow(null, _autoRight);
                l.AddRow(Caption(scope, "Rotation (new text)"), TableLayout.Horizontal(8, _rotation, Note(scope, "degrees"), null));
                l.AddRow(null, _toView);
                l.AddRow(null, TableLayout.Horizontal(8, _wrap, _wrapWidth, null));
            })));
            page.Rows.Add(new TableRow(Section(scope, "Mask", l =>
            {
                l.AddRow(Caption(scope, "Mask"), TableLayout.Horizontal(8, _mask, _maskColor, null));
                l.AddRow(Caption(scope, "Margin and frame"), TableLayout.Horizontal(8, _maskMargin, _maskFrame, null));
            })));
            page.Rows.Add(new TableRow(Section(scope, "Layer and color", l =>
            {
                l.AddRow(Caption(scope, "Layer"), _layer);
                l.AddRow(null, TableLayout.Horizontal(8, _byLayer, _color, null));
            })));
            page.Rows.Add(new TableRow { ScaleHeight = true });
            return page;
        }

        public Control CreateLeaderPage(ThemeScope scope)
        {
            var page = new TableLayout { Spacing = new Size(0, 10), Padding = new Padding(14, 12) };
            page.Rows.Add(new TableRow(Section(scope, "Leader", l =>
            {
                l.AddRow(Caption(scope, "Arrowhead"), _arrow);
                l.AddRow(Caption(scope, "Arrow size"), TableLayout.Horizontal(8, _arrowSize, Note(scope, "0 = annotation style size"), null));
                l.AddRow(Caption(scope, "Leader curve"), _curve);
                l.AddRow(null, TableLayout.Horizontal(8, _landing, _landingLength, null));
                l.AddRow(Caption(scope, "Text alignment"), _leaderAlign);
            })));
            var note = Ui.Label(scope, "Text, font, height and mask settings from the Format tab also apply to leaders.", 12.5f, false, p => p.Muted, true);
            page.Rows.Add(new TableRow(new Panel { Content = note, Padding = new Padding(4, 0) }));
            page.Rows.Add(new TableRow { ScaleHeight = true });
            return page;
        }

        // ------------------------------------------------------------------ data
        public void BindDocument(RhinoDoc doc)
        {
            Diagnostics.Log("format.BindDocument");
            _loading = true;
            try
            {
                _styles = new List<KeyValuePair<Guid, string>>();
                _currentStyleId = Guid.Empty;
                if (doc != null)
                {
                    foreach (var st in doc.DimStyles)
                        if (st != null && !st.IsDeleted && !st.IsReference) _styles.Add(new KeyValuePair<Guid, string>(st.Id, st.Name));
                    _currentStyleId = doc.DimStyles.CurrentId;
                }
                _styles.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase));
                _style.Items.Clear();
                foreach (var st in _styles) _style.Items.Add(st.Value);

                _layers = new List<KeyValuePair<int, string>>();
                if (doc != null)
                {
                    for (int i = 0; i < doc.Layers.Count; i++)
                    {
                        var l = doc.Layers[i];
                        if (l == null || l.IsDeleted || l.IsReference) continue;
                        _layers.Add(new KeyValuePair<int, string>(l.Index, l.FullPath));
                    }
                }
                _layers.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase));
                _layer.Items.Clear();
                _layer.Items.Add("(current layer)");
                foreach (var l in _layers) _layer.Items.Add(l.Value);

                if (_font.Items.Count == 0)
                {
                    string[] faces;
                    try { faces = Rhino.DocObjects.Font.AvailableFontFaceNames(); }
                    catch { faces = new string[0]; }
                    foreach (var f in faces.Distinct().OrderBy(f => f)) _font.Items.Add(f);
                }
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>Shows a format in the controls without marking anything as changed.</summary>
        public void Load(TextFormat f)
        {
            _loading = true;
            try
            {
                int si = _styles.FindIndex(s => s.Key == f.DimStyleId);
                if (si < 0) si = _styles.FindIndex(s => s.Key == _currentStyleId);
                _style.SelectedIndex = si;
                _font.Text = f.FontFace ?? string.Empty;
                _bold.Checked = f.Bold;
                _italic.Checked = f.Italic;
                _underline.Checked = f.Underline;
                _height.Value = f.Height;
                _hAlign.SelectedIndex = Math.Max(0, Array.IndexOf(HValues, f.HAlign == TextHorizontalAlignment.Auto ? TextHorizontalAlignment.Left : f.HAlign));
                _vAlign.SelectedIndex = VIndex(f.VAlign);
                _rotation.Value = f.RotationDegrees;
                _toView.Checked = f.HorizontalToView;
                _wrap.Checked = f.Wrap;
                _wrapWidth.Value = f.WrapWidth;
                _wrapWidth.Enabled = f.Wrap;
                _mask.SelectedIndex = !f.MaskEnabled ? 0 : (f.MaskUsesViewportColor ? 1 : 2);
                _maskColor.Value = ToEto(f.MaskColor);
                _maskMargin.Value = f.MaskOffset;
                _maskFrame.SelectedIndex = f.MaskFrame == DimensionStyle.MaskFrame.RectFrame ? 1 : (f.MaskFrame == DimensionStyle.MaskFrame.CapsuleFrame ? 2 : 0);
                UpdateMaskEnabled();
                int li = _layers.FindIndex(l => l.Key == f.LayerIndex);
                _layer.SelectedIndex = li < 0 ? 0 : li + 1;
                _byLayer.Checked = f.ColorByLayer;
                _color.Value = ToEto(f.ObjectColor);
                _color.Enabled = !f.ColorByLayer;
                _arrow.SelectedIndex = Math.Max(0, Array.IndexOf(ArrowValues, f.ArrowType));
                _arrowSize.Value = f.ArrowSize;
                _curve.SelectedIndex = f.CurveStyle == DimensionStyle.LeaderCurveStyle.Spline ? 1 : 0;
                _landing.Checked = f.Landing;
                _landingLength.Value = f.LandingLength;
                _landingLength.Enabled = f.Landing;
                _leaderAlign.SelectedIndex = Math.Max(0, Array.IndexOf(LeaderAlignValues, f.LeaderHAlign));
            }
            finally
            {
                _loading = false;
            }
        }

        private static int VIndex(TextVerticalAlignment v)
        {
            switch (v)
            {
                case TextVerticalAlignment.Top:
                case TextVerticalAlignment.MiddleOfTop:
                case TextVerticalAlignment.BottomOfTop:
                    return 0;
                case TextVerticalAlignment.Middle:
                    return 1;
                default:
                    return 2;
            }
        }

        /// <summary>Reads the controls into a format.</summary>
        public TextFormat Read()
        {
            var f = new TextFormat();
            if (_style.SelectedIndex >= 0 && _style.SelectedIndex < _styles.Count) f.DimStyleId = _styles[_style.SelectedIndex].Key;
            f.FontFace = string.IsNullOrWhiteSpace(_font.Text) ? null : _font.Text.Trim();
            f.Bold = _bold.Checked == true;
            f.Italic = _italic.Checked == true;
            f.Underline = _underline.Checked == true;
            f.Height = _height.Value;
            f.HAlign = HValues[Math.Max(0, _hAlign.SelectedIndex)];
            f.VAlign = VValues[Math.Max(0, _vAlign.SelectedIndex)];
            f.RotationDegrees = _rotation.Value;
            f.HorizontalToView = _toView.Checked == true;
            f.Wrap = _wrap.Checked == true && _wrapWidth.Value > 0;
            f.WrapWidth = _wrapWidth.Value;
            f.MaskEnabled = _mask.SelectedIndex > 0;
            f.MaskUsesViewportColor = _mask.SelectedIndex != 2;
            f.MaskColor = ToSd(_maskColor.Value);
            f.MaskOffset = _maskMargin.Value;
            f.MaskFrame = _maskFrame.SelectedIndex == 1 ? DimensionStyle.MaskFrame.RectFrame :
                          (_maskFrame.SelectedIndex == 2 ? DimensionStyle.MaskFrame.CapsuleFrame : DimensionStyle.MaskFrame.NoFrame);
            f.LayerIndex = _layer.SelectedIndex > 0 ? _layers[_layer.SelectedIndex - 1].Key : -1;
            f.ColorByLayer = _byLayer.Checked == true;
            f.ObjectColor = ToSd(_color.Value);
            f.ArrowType = ArrowValues[Math.Max(0, _arrow.SelectedIndex)];
            f.ArrowSize = _arrowSize.Value;
            f.CurveStyle = _curve.SelectedIndex == 1 ? DimensionStyle.LeaderCurveStyle.Spline : DimensionStyle.LeaderCurveStyle.Polyline;
            f.Landing = _landing.Checked == true;
            f.LandingLength = _landingLength.Value;
            f.LeaderHAlign = LeaderAlignValues[Math.Max(0, _leaderAlign.SelectedIndex)];
            return f;
        }

        /// <summary>Default format for new text: current annotation style and layer.</summary>
        public static TextFormat DefaultFor(RhinoDoc doc)
        {
            var f = new TextFormat();
            if (doc == null) return f;
            var style = doc.DimStyles.Current;
            f.DimStyleId = style.Id;
            f.FontFace = style.Font != null ? style.Font.QuartetName : null;
            f.Height = style.TextHeight;
            f.LayerIndex = -1;
            f.MaskEnabled = style.DrawTextMask;
            f.MaskOffset = style.MaskOffset;
            f.ArrowSize = style.LeaderArrowLength;
            f.LandingLength = style.LeaderLandingLength;
            return f;
        }

        public static EtoColor ToEto(SdColor c)
        {
            return new EtoColor(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
        }

        public static SdColor ToSd(EtoColor c)
        {
            return SdColor.FromArgb(c.Ab, c.Rb, c.Gb, c.Bb);
        }
    }
}
