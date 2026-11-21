using System;
using Eto.Drawing;
using Eto.Forms;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Controls
{
    public enum ButtonKind
    {
        /// <summary>Filled with the accent colour (the main action).</summary>
        Primary,
        /// <summary>Surface fill with an outline.</summary>
        Secondary,
        /// <summary>Text only; a background appears on hover.</summary>
        Ghost,
        /// <summary>Dashed outline.</summary>
        Dashed,
        /// <summary>Accent-tinted fill with accent text (swap, small accent actions).</summary>
        Soft,
        /// <summary>Filled with the ink colour (charcoal on paper, paper on charcoal): active chips.</summary>
        Strong,
    }

    /// <summary>A themed push button with an optional line icon.</summary>
    public sealed class FlatButton : ThemedDrawable
    {
        private string _text;
        private IconKind _icon;
        private readonly int _height;
        private bool _stretch;

        public ButtonKind Kind { get; set; }
        public float FontPx { get; set; }
        public float Radius { get; set; }
        public int PaddingX { get; set; }
        public float IconSize { get; set; }

        public event EventHandler<EventArgs> Click;

        public FlatButton(string text, IconKind icon = IconKind.None, ButtonKind kind = ButtonKind.Secondary, int height = 34)
            : base(true)
        {
            _text = text ?? string.Empty;
            _icon = icon;
            _height = height;
            Kind = kind;
            FontPx = height <= 28 ? 12f : 13f;
            Radius = height / 2f;   // pills, and circles for icon buttons
            PaddingX = string.IsNullOrEmpty(_text) ? 0 : (height <= 24 ? 10 : height <= 28 ? 12 : 14);
            IconSize = height <= 24 ? 12f : 14f;
            Cursor = Cursors.Pointer;
            UpdateSize();
        }

        public string Text
        {
            get { return _text; }
            set
            {
                _text = value ?? string.Empty;
                UpdateSize();
                Invalidate();
            }
        }

        public IconKind Icon
        {
            get { return _icon; }
            set { _icon = value; UpdateSize(); Invalidate(); }
        }

        /// <summary>Fill the width given by the layout instead of sizing to the text.</summary>
        public bool Stretch
        {
            get { return _stretch; }
            set { _stretch = value; UpdateSize(); }
        }

        private Font ButtonFont { get { return Theme.UiFont(FontPx, Kind == ButtonKind.Primary || Kind == ButtonKind.Strong); } }

        private void UpdateSize()
        {
            if (_stretch)
            {
                Size = new Size(-1, _height);
                return;
            }
            float w;
            bool hasText = !string.IsNullOrEmpty(_text);
            if (!hasText) w = _height; // square icon button
            else
            {
                w = PaddingX * 2 + Measure(ButtonFont, _text).Width;
                if (_icon != IconKind.None) w += IconSize + 6;
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

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color fill, stroke, fg;
            bool dashed = false;
            float h = Hover;
            switch (Kind)
            {
                case ButtonKind.Primary:
                    fill = ThemeColors.Mix(p.Accent, ThemeColors.Mix(p.Accent, p.Text, p.IsDark ? 0.14f : 0.10f), h);
                    if (IsPressed) fill = ThemeColors.Mix(p.Accent, p.Window, 0.22f);
                    stroke = Colors.Transparent;
                    fg = p.OnAccent;
                    break;
                case ButtonKind.Strong:
                    fill = ThemeColors.Mix(p.Strong, ThemeColors.Mix(p.Strong, p.Accent, 0.18f), h);
                    if (IsPressed) fill = ThemeColors.Mix(p.Strong, p.Window, 0.25f);
                    stroke = Colors.Transparent;
                    fg = p.OnStrong;
                    break;
                case ButtonKind.Soft:
                    fill = ThemeColors.Mix(p.AccentSoft, ThemeColors.Mix(p.AccentSoft, p.Accent, 0.18f), h);
                    stroke = Colors.Transparent;
                    fg = p.AccentInk;
                    break;
                case ButtonKind.Ghost:
                    fill = IsPressed ? p.IconBox : ThemeColors.WithAlpha(p.ItemHover, h);
                    stroke = Colors.Transparent;
                    fg = ThemeColors.Mix(p.Muted, p.Text, h);
                    break;
                case ButtonKind.Dashed:
                    fill = ThemeColors.WithAlpha(p.ItemHover, h);
                    stroke = ThemeColors.Mix(ThemeColors.Mix(p.Border, p.Muted, 0.45f), p.Accent, h);
                    fg = p.Text;
                    dashed = true;
                    break;
                default:
                    fill = IsPressed ? p.IconBox : ThemeColors.Mix(p.Surface, p.ItemHover, h);
                    stroke = ThemeColors.Mix(p.Border, ThemeColors.Mix(p.Border, p.Text, 0.35f), h);
                    fg = p.Text;
                    break;
            }
            if (!Enabled)
            {
                fill = Kind == ButtonKind.Primary || Kind == ButtonKind.Strong ? ThemeColors.Mix(fill, p.Window, 0.55f) : fill;
                fg = ThemeColors.WithAlpha(fg, 0.45f);
                stroke = ThemeColors.WithAlpha(stroke, 0.5f);
            }

            FillRound(g, r, Radius, fill);
            StrokeRound(g, r, Radius, stroke, 1f, dashed);

            var font = ButtonFont;
            bool hasText = !string.IsNullOrEmpty(_text);
            float textW = hasText ? Measure(font, _text).Width : 0;
            float contentW = textW + (_icon != IconKind.None ? IconSize + (hasText ? 6 : 0) : 0);
            float x = Math.Max(PaddingX, (Width - contentW) / 2f);
            if (_icon != IconKind.None)
            {
                // arrows lean the way they point while the pointer is on the button
                float nx = 0, ny = 0;
                if (_icon == IconKind.ArrowUpRight) { nx = 1.6f * Hover; ny = -1.6f * Hover; }
                else if (_icon == IconKind.Next) nx = 1.6f * Hover;
                else if (_icon == IconKind.Prev) nx = -1.6f * Hover;
                else if (_icon == IconKind.Download) ny = 1.2f * Hover;
                Icons.Draw(g, _icon, new RectangleF(x + nx, (Height - IconSize) / 2f + ny, IconSize, IconSize), fg, Kind == ButtonKind.Primary || Kind == ButtonKind.Strong ? 2.3f : 2f);
                x += IconSize + 6;
            }
            if (hasText)
                DrawLine(g, font, fg, new RectangleF(x, 0, Width - x - 2, Height), _text);
            DrawFocus(g, r, Radius);
        }
    }
}
