using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Controls
{
    /// <summary>
    /// The navigation rail: the Text X mark at the top, then a rounded track of icon buttons (the
    /// chosen one sits in an orange circle), and an extra button at the bottom (settings).
    /// </summary>
    public sealed class NavRail : ThemedDrawable
    {
        private readonly List<KeyValuePair<string, IconKind>> _items = new List<KeyValuePair<string, IconKind>>();
        private string _bottomText;
        private IconKind _bottomIcon = IconKind.None;
        private int _selected;
        private int _hover = -2; // -1 = bottom button, -2 = nothing
        private const float RailW = 52f, ItemD = 40f, Gap = 6f, Logo = 44f, Top = 14f;

        public event EventHandler<EventArgs> SelectedIndexChanged;
        public event EventHandler<EventArgs> BottomClicked;

        public NavRail() : base(true)
        {
            Width = 76;
            Cursor = Cursors.Pointer;
        }

        public void Add(string text, IconKind icon)
        {
            _items.Add(new KeyValuePair<string, IconKind>(text, icon));
            Invalidate();
        }

        public void SetBottom(string text, IconKind icon)
        {
            _bottomText = text;
            _bottomIcon = icon;
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value == _selected || value < 0 || value >= _items.Count) return;
                _selected = value;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        private float X0 { get { return (Width - RailW) / 2f; } }

        private RectangleF TrackRect()
        {
            float y = Top + Logo + 16;
            float h = _items.Count * ItemD + (_items.Count - 1) * Gap + 12;
            return new RectangleF(X0, y, RailW, h);
        }

        private RectangleF ItemRect(int i)
        {
            var t = TrackRect();
            return new RectangleF(t.X + (RailW - ItemD) / 2f, t.Y + 6 + i * (ItemD + Gap), ItemD, ItemD);
        }

        private RectangleF BottomRect()
        {
            return new RectangleF(X0 + (RailW - ItemD) / 2f, Height - 16 - ItemD - 6, ItemD, ItemD);
        }

        private int HitTest(PointF pt)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var r = ItemRect(i);
                r.Inflate(3, 3);
                if (r.Contains(pt)) return i;
            }
            if (_bottomIcon != IconKind.None)
            {
                var b = BottomRect();
                b.Inflate(3, 3);
                if (b.Contains(pt)) return -1;
            }
            return -2;
        }

        protected override void OnMouseMoveAt(PointF location)
        {
            int h = HitTest(location);
            if (h == _hover) return;
            _hover = h;
            ToolTip = h >= 0 ? _items[h].Key : h == -1 ? _bottomText : null;
            Invalidate();
        }

        protected override void OnHoverChanged()
        {
            if (!IsHover) _hover = -2;
        }

        protected override void OnActivated(PointF location)
        {
            if (location.X < 0) { SelectedIndex = (_selected + 1) % Math.Max(1, _items.Count); return; }
            int i = HitTest(location);
            if (i >= 0) SelectedIndex = i;
            else if (i == -1)
            {
                var h = BottomClicked;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnKey(KeyEventArgs e)
        {
            if (e.Key == Keys.Down) { SelectedIndex = Math.Min(_items.Count - 1, _selected + 1); e.Handled = true; }
            else if (e.Key == Keys.Up) { SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; }
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            // the mark: an orange disc with the translate symbol
            var logo = new RectangleF((Width - Logo) / 2f, Top, Logo, Logo);
            g.FillEllipse(p.Accent, logo);
            Icons.Draw(g, IconKind.Translate, new RectangleF(logo.X + 11, logo.Y + 11, Logo - 22, Logo - 22), p.OnAccent, 2.4f);

            var track = TrackRect();
            FillRound(g, track, RailW / 2f, p.Rail);
            for (int i = 0; i < _items.Count; i++)
            {
                var r = ItemRect(i);
                bool sel = i == _selected;
                if (sel) g.FillEllipse(p.Accent, r);
                else if (i == _hover) g.FillEllipse(ThemeColors.WithAlpha(p.RailIcon, 0.18f), r);
                var color = sel ? p.OnAccent : (i == _hover ? ThemeColors.Mix(p.RailIcon, p.Accent, 0.5f) : p.RailIcon);
                Icons.Draw(g, _items[i].Value, new RectangleF(r.X + 11, r.Y + 11, ItemD - 22, ItemD - 22), color, sel ? 2.3f : 2f);
                if (sel && ShowFocus) using (var pen = new Pen(p.AccentInk, 1.5f)) g.DrawEllipse(pen, new RectangleF(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6));
            }

            if (_bottomIcon != IconKind.None)
            {
                var b = BottomRect();
                var ring = new RectangleF(b.X - 6, b.Y - 6, b.Width + 12, b.Height + 12);
                g.FillEllipse(p.Rail, ring);
                if (_hover == -1) g.FillEllipse(ThemeColors.WithAlpha(p.RailIcon, 0.18f), b);
                Icons.Draw(g, _bottomIcon, new RectangleF(b.X + 11, b.Y + 11, ItemD - 22, ItemD - 22), _hover == -1 ? p.Accent : p.RailIcon, 2f);
            }
        }
    }

    /// <summary>
    /// A bento tile: an icon in a disc, a title, a short text and a round arrow button; the whole tile
    /// is one button.
    /// </summary>
    public sealed class BentoTile : ThemedDrawable
    {
        private readonly IconKind _icon;
        private readonly string _title, _text, _tag;

        public bool Featured { get; set; }

        public event EventHandler<EventArgs> Click;

        public BentoTile(IconKind icon, string title, string text, string tag = null) : base(true)
        {
            _icon = icon;
            _title = title ?? string.Empty;
            _text = text ?? string.Empty;
            _tag = tag;
            Cursor = Cursors.Pointer;
            Height = 168;
            ToolTip = _title;
        }

        protected override void OnActivated(PointF location)
        {
            var h = Click;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            var fill = Featured ? p.Accent : (IsHover ? ThemeColors.Mix(p.Surface, p.Text, p.IsDark ? 0.04f : 0.02f) : p.Surface);
            FillRound(g, r, 22, fill);
            if (!Featured) StrokeRound(g, r, 22, IsHover ? ThemeColors.Mix(p.Border, p.Accent, 0.6f) : p.Border, IsHover ? 1.3f : 1f);

            var ink = Featured ? p.OnAccent : p.Text;
            var muted = Featured ? ThemeColors.WithAlpha(p.OnAccent, 0.75f) : p.Muted;

            // icon disc
            var disc = new RectangleF(18, 18, 42, 42);
            g.FillEllipse(Featured ? p.OnAccent : p.AccentSoft, disc);
            Icons.Draw(g, _icon, new RectangleF(disc.X + 11, disc.Y + 11, 20, 20), Featured ? p.Accent : p.AccentInk, 2.1f);

            // round arrow button
            var arrow = new RectangleF(Width - 18 - 36, 18, 36, 36);
            if (Featured) g.FillEllipse(p.OnAccent, arrow);
            else
            {
                if (IsHover) g.FillEllipse(p.Strong, arrow);
                else using (var pen = new Pen(p.Border, 1.2f)) g.DrawEllipse(pen, arrow);
            }
            var arrowInk = Featured ? p.Accent : (IsHover ? p.OnStrong : p.Text);
            Icons.Draw(g, IconKind.ArrowUpRight, new RectangleF(arrow.X + 10, arrow.Y + 10, 16, 16), arrowInk, 2.2f);

            if (!string.IsNullOrEmpty(_tag))
                DrawLine(g, Theme.UiFont(10f, true), muted, new RectangleF(70, 18, Width - 70 - 64, 42), Theme.Spaced(_tag));

            float y = 76;
            DrawLine(g, Theme.UiFont(16f, true), ink, new RectangleF(20, y, Width - 40, 26), _title);
            using (var brush = new SolidBrush(muted))
                g.DrawText(Theme.UiFont(12.5f), brush, new RectangleF(20, y + 30, Width - 40, Height - y - 30 - 14), _text,
                    FormattedTextWrapMode.Word, FormattedTextAlignment.Left, FormattedTextTrimming.WordEllipsis);
            DrawFocus(g, r, 22);
        }
    }

    /// <summary>A small coloured dot before a label.</summary>
    public sealed class DotMark : ThemedDrawable
    {
        public Func<ThemeColors, Color> Color { get; set; }

        public DotMark(Func<ThemeColors, Color> color, float size = 8f) : base(false)
        {
            Color = color;
            Size = new Size((int)Math.Ceiling(size), (int)Math.Ceiling(size));
            Backdrop = p => p.Surface;
        }

        protected override void OnDraw(Graphics g)
        {
            g.FillEllipse(Color(P), new RectangleF(0, 0, Width, Height));
        }
    }
}
