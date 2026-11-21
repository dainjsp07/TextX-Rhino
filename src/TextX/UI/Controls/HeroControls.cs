using System;
using Eto.Drawing;
using Eto.Forms;
using TextX.Core;
using TextX.Core.Motion;
using TextX.Core.Text;
using TextX.UI.Motion;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Controls
{
    /// <summary>
    /// Colours of things that sit on the dark translator card (the hero), which is charcoal in both themes.
    /// Text on it is paper; lines and fields are paper at low strength; the accent stays the same orange.
    /// </summary>
    public static class HeroInk
    {
        public static Color Field(ThemeColors p) { return ThemeColors.Mix(p.Hero, p.OnHero, 0.06f); }
        public static Color Chip(ThemeColors p) { return ThemeColors.Mix(p.Hero, p.OnHero, 0.10f); }
        public static Color Line(ThemeColors p) { return ThemeColors.Mix(p.Hero, p.OnHero, 0.22f); }
        /// <summary>Outline of fields and inputs on the card: 3:1 against it (WCAG 1.4.11).</summary>
        public static Color InputLine(ThemeColors p) { return ThemeColors.Mix(p.Hero, p.OnHero, 0.40f); }
        public static Color Muted(ThemeColors p) { return ThemeColors.Mix(p.Hero, p.OnHero, 0.72f); }
        public static Color Faint(ThemeColors p) { return ThemeColors.Mix(p.Hero, p.OnHero, 0.56f); }
        /// <summary>The light button on the card (Place leader): white on the light theme's charcoal card, black on the dark one.</summary>
        public static Color Light(ThemeColors p) { return p.IsDark ? ThemeColors.Hex("#111111") : ThemeColors.Hex("#FFFFFF"); }
        public static Color OnLight(ThemeColors p) { return p.IsDark ? p.OnHero : ThemeColors.Hex("#111111"); }
    }

    public enum HeroChipStyle
    {
        /// <summary>Outline on the card; accent outline on hover; accent fill when <see cref="HeroChip.On"/>.</summary>
        Outline,
        /// <summary>Accent fill (the main action on the card).</summary>
        Accent,
        /// <summary>Light fill (white on charcoal): a second action.</summary>
        Light,
        /// <summary>No outline until hover (small actions inside fields).</summary>
        Quiet,
    }

    /// <summary>
    /// A pill button for the translator card: 24, 28 or 32 px high, an optional icon, and an on state
    /// (filled with the accent) for toggles such as the display options or voice typing.
    /// </summary>
    public sealed class HeroChip : ThemedDrawable
    {
        private string _text;
        private IconKind _icon;
        private readonly int _height;
        private bool _on;
        private bool _pulse;
        private int _maxTextWidth;

        public HeroChipStyle Look { get; set; }
        public event EventHandler<EventArgs> Click;

        public HeroChip(string text, IconKind icon = IconKind.None, HeroChipStyle style = HeroChipStyle.Outline, int height = 28) : base(true)
        {
            _text = text ?? string.Empty;
            _icon = icon;
            _height = height;
            Look = style;
            Cursor = Cursors.Pointer;
            Backdrop = p => p.Hero;
            UpdateSize();
        }

        private float FontPx { get { return _height <= 24 ? 12f : 13f; } }
        private float IconPx { get { return _height <= 24 ? 12f : 14f; } }
        private int PadX { get { return _height <= 24 ? 9 : 12; } }
        private Font ChipFont { get { return Theme.UiFont(FontPx, Look == HeroChipStyle.Accent || _on); } }

        public string Text
        {
            get { return _text; }
            set { _text = value ?? string.Empty; UpdateSize(); Invalidate(); }
        }

        public IconKind Icon
        {
            get { return _icon; }
            set { _icon = value; UpdateSize(); Invalidate(); }
        }

        /// <summary>Longest text shown before it is trimmed with an ellipsis (0 = no limit).</summary>
        public int MaxTextWidth
        {
            get { return _maxTextWidth; }
            set { _maxTextWidth = value; UpdateSize(); }
        }

        public bool On
        {
            get { return _on; }
            set { if (_on == value) return; _on = value; UpdateSize(); Invalidate(); if (_pulse) Animate(); }
        }

        /// <summary>A slowly pulsing dot before the text (voice typing is listening).</summary>
        public bool Pulse
        {
            get { return _pulse; }
            set { if (_pulse == value) return; _pulse = value; UpdateSize(); Invalidate(); if (value) Animate(); }
        }

        private void UpdateSize()
        {
            float w;
            if (string.IsNullOrEmpty(_text)) w = _height;
            else
            {
                float tw = Measure(ChipFont, _text).Width;
                if (_maxTextWidth > 0) tw = Math.Min(tw, _maxTextWidth);
                w = PadX * 2 + tw;
                if (_icon != IconKind.None) w += IconPx + 6;
                if (_pulse) w += 12;
            }
            Size = new Size((int)Math.Ceiling(w) + 2, _height);
        }

        public void PerformClick()
        {
            var h = Click;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnActivated(PointF location)
        {
            PerformClick();
        }

        protected override bool Step(double dt, double now)
        {
            bool more = base.Step(dt, now);
            return _pulse || more;
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            float h = Hover;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = Height / 2f;
            Color fill, stroke, fg;
            if (_on || Look == HeroChipStyle.Accent)
            {
                fill = ThemeColors.Mix(p.Accent, ThemeColors.Mix(p.Accent, p.Hero, 0.12f), h);
                if (IsPressed) fill = ThemeColors.Mix(p.Accent, p.Hero, 0.25f);
                stroke = Colors.Transparent;
                fg = p.OnAccent;
            }
            else if (Look == HeroChipStyle.Light)
            {
                fill = HeroInk.Light(p);
                stroke = ThemeColors.WithAlpha(p.Accent, h);
                fg = HeroInk.OnLight(p);
            }
            else if (Look == HeroChipStyle.Quiet)
            {
                fill = ThemeColors.WithAlpha(HeroInk.Chip(p), h);
                stroke = Colors.Transparent;
                fg = ThemeColors.Mix(HeroInk.Muted(p), p.OnHero, h);
            }
            else
            {
                fill = IsPressed ? HeroInk.Chip(p) : ThemeColors.WithAlpha(HeroInk.Chip(p), 0.6f * h);
                stroke = ThemeColors.Mix(HeroInk.Line(p), p.Accent, h);
                fg = p.OnHero;
            }
            if (!Enabled)
            {
                fg = ThemeColors.WithAlpha(fg, 0.45f);
                stroke = ThemeColors.WithAlpha(stroke, 0.5f);
            }
            FillRound(g, r, radius, fill);
            StrokeRound(g, r, radius, stroke, Look == HeroChipStyle.Light ? 2f : 1f);

            var font = ChipFont;
            bool hasText = !string.IsNullOrEmpty(_text);
            float tw = hasText ? Measure(font, _text).Width : 0;
            if (_maxTextWidth > 0) tw = Math.Min(tw, _maxTextWidth);
            float content = tw + (_icon != IconKind.None ? IconPx + (hasText ? 6 : 0) : 0) + (_pulse ? 12 : 0);
            float x = hasText ? PadX : (Width - content) / 2f;
            if (_pulse)
            {
                double t = Animator.Now / 1000.0;
                float a = 0.35f + 0.65f * (float)(0.5 + 0.5 * Math.Cos(t * Math.PI * 2 / 1.2));
                g.FillEllipse(ThemeColors.WithAlpha(fg, a), new RectangleF(x, (Height - 6) / 2f, 6, 6));
                x += 12;
            }
            if (_icon != IconKind.None)
            {
                Icons.Draw(g, _icon, new RectangleF(x, (Height - IconPx) / 2f, IconPx, IconPx), fg, 2f);
                x += IconPx + 6;
            }
            if (hasText) DrawLine(g, font, fg, new RectangleF(x, 0, Math.Max(0, Width - x - PadX + 2), Height), _text);
            DrawFocus(g, r, radius);
        }
    }

    /// <summary>A small switch with its label for the translator card ("Live").</summary>
    public sealed class HeroSwitch : ThemedDrawable
    {
        private bool _checked;
        private readonly string _text;
        private readonly Tween _knob = new Tween(0, 250);

        public event EventHandler<EventArgs> CheckedChanged;

        public HeroSwitch(string text) : base(true)
        {
            _text = text ?? string.Empty;
            Cursor = Cursors.Pointer;
            Backdrop = p => p.Hero;
            Size = new Size((int)Math.Ceiling(Measure(Theme.UiFont(12f, true), _text).Width) + 44, 24);
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                _knob.Set(value ? 1 : 0, Animator.Now);
                Animate();
                Invalidate();
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        /// <summary>Sets the state without animation (when a window opens).</summary>
        public void Init(bool value)
        {
            _checked = value;
            _knob.To = value ? 1 : 0;
            _knob.Jump();
            Invalidate();
        }

        protected override bool Step(double dt, double now)
        {
            bool a = base.Step(dt, now);
            return !_knob.Done(now) | a;
        }

        protected override void OnActivated(PointF location)
        {
            Checked = !Checked;
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            float k = (float)_knob.Value(Animator.Now);
            var track = new RectangleF(4, (Height - 14) / 2f, 26, 14);
            FillRound(g, track, 7, ThemeColors.Mix(HeroInk.Line(p), p.Accent, k));
            g.FillEllipse(Colors.White, new RectangleF(track.X + 2 + 12 * k, track.Y + 2, 10, 10));
            if (IsHover) FillRound(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f, ThemeColors.WithAlpha(HeroInk.Chip(p), 0.5f * Hover));
            DrawLine(g, Theme.UiFont(12f, true), p.OnHero, new RectangleF(36, 0, Width - 38, Height), _text);
            DrawFocus(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f);
        }
    }

    /// <summary>How a text will look, for the one-line previews (read from the text properties).</summary>
    public sealed class LineStyle
    {
        public string FontFace;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        /// <summary>0 = no mask, 1 = background colour, 2 = solid colour.</summary>
        public int Mask;
        public Color MaskColor = Colors.White;
        /// <summary>0 = no frame, 1 = rectangle, 2 = capsule.</summary>
        public int Frame;
        /// <summary>0 = left, 1 = center, 2 = right.</summary>
        public int HAlign;
        public bool ColorByLayer = true;
        public Color TextColor = Colors.Black;
    }

    /// <summary>
    /// One line of a text as it will appear on screen or in print, with its font, emphasis, mask, frame
    /// and alignment, in a pill with a label ("ON SCREEN", "IN PRINT · PDF").
    /// </summary>
    public sealed class LinePreview : ThemedDrawable
    {
        private readonly string _label;
        private readonly bool _print;
        private string _text = string.Empty;
        private string _lang = "en";
        private LineStyle _style = new LineStyle();
        private int _align;

        public LinePreview(string label, bool print) : base(false)
        {
            _label = label ?? string.Empty;
            _print = print;
            Height = 32;
            Backdrop = p => p.Hero;
        }

        /// <param name="align">0 left, 1 center, 2 right (already resolved for print).</param>
        public void Set(string text, string lang, LineStyle style, int align)
        {
            _text = text ?? string.Empty;
            _lang = lang ?? "en";
            _style = style ?? new LineStyle();
            _align = align;
            ToolTip = _text;
            Invalidate();
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            var r = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
            StrokeRound(g, r, Height / 2f, _print ? p.Accent : HeroInk.Line(p), 1.3f, !_print);
            var labelFont = Theme.UiFont(11f, true);
            var label = Theme.Spaced(_label);
            float lw = Measure(labelFont, label).Width;
            DrawLine(g, labelFont, _print ? p.Accent : HeroInk.Muted(p), new RectangleF(14, 0, lw + 2, Height), label);

            float x0 = 14 + lw + 12, x1 = Width - 14;
            if (x1 - x0 < 20) return;
            bool empty = string.IsNullOrWhiteSpace(_text);
            var text = empty ? "Your text" : _text.Replace("\r", string.Empty).Replace("\n", "  ⏎  ");
            bool rtl = !empty && (Languages.IsRtl(_lang) || RtlText.ContainsRtl(_text));
            var font = PreviewFont(_style);
            float tw = Math.Min(Measure(font, text).Width, x1 - x0 - 16);
            bool boxed = _style.Mask > 0 || _style.Frame > 0;
            float padX = boxed ? 8 : 0;
            float bw = tw + 2 * padX;
            int align = _align == 0 && rtl ? 2 : _align;
            float bx = align == 1 ? x0 + (x1 - x0 - bw) / 2f : align == 2 ? x1 - bw : x0;
            var box = new RectangleF(bx, (Height - 22) / 2f, bw, 22);
            Color ink = empty ? HeroInk.Faint(p) : (!_style.ColorByLayer ? _style.TextColor : p.OnHero);
            if (_style.Mask == 1) FillRound(g, box, _style.Frame == 2 ? 11 : 4, HeroInk.Chip(p));
            else if (_style.Mask == 2)
            {
                FillRound(g, box, _style.Frame == 2 ? 11 : 4, _style.MaskColor);
                ink = Luma(_style.MaskColor) > 0.55f ? ThemeColors.Hex("#111111") : Colors.White;
            }
            if (_style.Frame > 0) StrokeRound(g, box, _style.Frame == 2 ? 11 : 2, p.OnHero, 1.2f);
            DrawLine(g, font, ink, new RectangleF(bx + padX, 0, tw + 2, Height), text, rtl ? FormattedTextAlignment.Right : FormattedTextAlignment.Left);
        }

        private static readonly System.Collections.Generic.Dictionary<string, Font> Fonts = new System.Collections.Generic.Dictionary<string, Font>();

        private static Font PreviewFont(LineStyle s)
        {
            var face = string.IsNullOrEmpty(s.FontFace) ? "Segoe UI" : s.FontFace;
            var key = face + "|" + s.Bold + "|" + s.Italic + "|" + s.Underline;
            Font f;
            if (Fonts.TryGetValue(key, out f)) return f;
            try
            {
                var style = (s.Bold ? FontStyle.Bold : FontStyle.None) | (s.Italic ? FontStyle.Italic : FontStyle.None);
                f = new Font(face, 13f * 0.75f, style, s.Underline ? FontDecoration.Underline : FontDecoration.None);
            }
            catch
            {
                f = Theme.UiFont(13f, s.Bold);
            }
            Fonts[key] = f;
            return f;
        }

        private static float Luma(Color c)
        {
            return 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
        }
    }
}
