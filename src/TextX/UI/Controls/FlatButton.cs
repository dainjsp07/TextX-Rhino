using System;
using Eto.Drawing;
using Eto.Forms;
using TextX.UI.Theming;

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
            FontPx = height <= 26 ? 12f : 13f;
            Radius = 8f;
            PaddingX = string.IsNullOrEmpty(_text) ? 0 : (height <= 26 ? 8 : 14);
            IconSize = height <= 26 ? 13f : 15f;
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

        private Font ButtonFont { get { return Theme.UiFont(FontPx, Kind == ButtonKind.Primary); } }

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
                if (_icon != IconKind.None) w += IconSize + 7;
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
            switch (Kind)
            {
                case ButtonKind.Primary:
                    fill = IsPressed ? ThemeColors.Mix(p.Accent, p.Window, 0.25f) : IsHover ? ThemeColors.Mix(p.Accent, p.AccentInk, 0.14f) : p.Accent;
                    stroke = p.AccentEdge;
                    fg = p.OnAccent;
                    break;
                case ButtonKind.Soft:
                    fill = IsHover ? ThemeColors.Mix(p.AccentSoft, p.AccentInk, 0.12f) : p.AccentSoft;
                    stroke = IsHover ? p.AccentLine : Colors.Transparent;
                    fg = p.AccentInk;
                    break;
                case ButtonKind.Ghost:
                    fill = IsPressed ? p.IconBox : IsHover ? p.ItemHover : Colors.Transparent;
                    stroke = Colors.Transparent;
                    fg = IsHover ? p.Text : p.Muted;
                    break;
                case ButtonKind.Dashed:
                    fill = IsHover ? p.ItemHover : Colors.Transparent;
                    stroke = IsHover ? p.Muted : ThemeColors.Mix(p.Border, p.Muted, 0.35f);
                    fg = p.Text;
                    dashed = true;
                    break;
                default:
                    fill = IsPressed ? p.IconBox : IsHover ? ThemeColors.Mix(p.Surface, p.Text, 0.06f) : p.Surface;
                    stroke = IsHover ? ThemeColors.Mix(p.Border, p.Muted, 0.4f) : p.Border;
                    fg = p.Text;
                    break;
            }
            if (!Enabled)
            {
                fill = Kind == ButtonKind.Primary ? ThemeColors.Mix(p.Accent, p.Window, 0.5f) : fill;
                fg = ThemeColors.WithAlpha(fg, 0.45f);
                stroke = ThemeColors.WithAlpha(stroke, 0.5f);
            }

            FillRound(g, r, Radius, fill);
            StrokeRound(g, r, Radius, stroke, 1f, dashed);

            var font = ButtonFont;
            bool hasText = !string.IsNullOrEmpty(_text);
            float textW = hasText ? Measure(font, _text).Width : 0;
            float contentW = textW + (_icon != IconKind.None ? IconSize + (hasText ? 7 : 0) : 0);
            float x = Math.Max(PaddingX, (Width - contentW) / 2f);
            if (_icon != IconKind.None)
            {
                Icons.Draw(g, _icon, new RectangleF(x, (Height - IconSize) / 2f, IconSize, IconSize), fg, Kind == ButtonKind.Primary ? 2.2f : 2f);
                x += IconSize + 7;
            }
            if (hasText)
                DrawLine(g, font, fg, new RectangleF(x, 0, Width - x - 2, Height), _text);
            DrawFocus(g, r, Radius);
        }
    }
}
