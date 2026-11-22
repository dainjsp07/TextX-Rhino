using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using TextX.Core.Motion;
using TextX.UI.Motion;
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
        // the orange circle slides between items on the discrete clock (a little quicker than 700 ms)
        private readonly Tween _slide = new Tween(0, 350);
        private readonly Tween _hoverDot = new Tween(0, 150) { Ease = Clock.EaseFade };
        private const float RailW = 46f, ItemD = 36f, Gap = 4f, Logo = 46f, Top = 12f;

        public event EventHandler<EventArgs> SelectedIndexChanged;
        public event EventHandler<EventArgs> BottomClicked;

        public NavRail() : base(true)
        {
            Width = 68;
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
                _slide.Set(value, Animator.Now);
                Animate();
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        private float X0 { get { return (Width - RailW) / 2f; } }

        private RectangleF TrackRect()
        {
            float y = Top + Logo + 12;
            float h = _items.Count * ItemD + (_items.Count - 1) * Gap + 10;
            return new RectangleF(X0, y, RailW, h);
        }

        private RectangleF ItemRect(int i)
        {
            var t = TrackRect();
            return new RectangleF(t.X + (RailW - ItemD) / 2f, t.Y + 5 + i * (ItemD + Gap), ItemD, ItemD);
        }

        private RectangleF BottomRect()
        {
            return new RectangleF(X0 + (RailW - ItemD) / 2f, Height - 12 - ItemD - 5, ItemD, ItemD);
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
            _hoverDot.From = 0; _hoverDot.To = 0; _hoverDot.Set(1, Animator.Now);
            Animate();
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

        protected override bool Step(double dt, double now)
        {
            bool a = base.Step(dt, now);
            return !_slide.Done(now) | !_hoverDot.Done(now) | a;
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            double now = Animator.Now;
            // the mark: an orange disc with the translate symbol
            var logo = new RectangleF((Width - Logo) / 2f, Top, Logo, Logo);
            g.FillEllipse(p.Accent, logo);
            Icons.Draw(g, IconKind.Translate, new RectangleF(logo.X + 13, logo.Y + 13, Logo - 26, Logo - 26), p.OnAccent, 2.2f);

            var track = TrackRect();
            FillRound(g, track, RailW / 2f, p.Rail);
            // the chosen item's circle, sliding between items
            float pos = (float)_slide.Value(now);
            if (_items.Count > 0)
            {
                var first = ItemRect(0);
                g.FillEllipse(p.Accent, new RectangleF(first.X, first.Y + pos * (ItemD + Gap), ItemD, ItemD));
            }
            float hov = (float)_hoverDot.Value(now) * Hover;
            for (int i = 0; i < _items.Count; i++)
            {
                var r = ItemRect(i);
                float on = (float)Math.Max(0, 1 - Math.Abs(pos - i)); // how much of the circle is on this item
                if (i == _hover && i != _selected) g.FillEllipse(ThemeColors.WithAlpha(p.RailIcon, 0.18f * hov), r);
                var rest = i == _hover ? ThemeColors.Mix(p.RailIcon, p.Accent, 0.6f * hov) : p.RailIcon;
                var color = ThemeColors.Mix(rest, p.OnAccent, on);
                // the icon under the pointer rises a little
                float dy = i == _hover && i != _selected ? -1.5f * hov : 0;
                Icons.Draw(g, _items[i].Value, new RectangleF(r.X + 8, r.Y + 8 + dy, ItemD - 16, ItemD - 16), color, on > 0.5f ? 2.1f : 1.8f);
                if (i == _selected && ShowFocus) using (var pen = new Pen(p.AccentInk, 1.5f)) g.DrawEllipse(pen, new RectangleF(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6));
            }

            if (_bottomIcon != IconKind.None)
            {
                var b = BottomRect();
                var ring = new RectangleF(b.X - 5, b.Y - 5, b.Width + 10, b.Height + 10);
                g.FillEllipse(p.Rail, ring);
                float bh = _hover == -1 ? hov : 0;
                if (bh > 0) g.FillEllipse(ThemeColors.WithAlpha(p.RailIcon, 0.18f * bh), b);
                Icons.Draw(g, _bottomIcon, new RectangleF(b.X + 8, b.Y + 8, ItemD - 16, ItemD - 16), ThemeColors.Mix(p.RailIcon, p.Accent, bh), 1.8f);
            }
        }
    }

    /// <summary>
    /// A bento tile: a line figure that answers the pointer and says what it shows, a title, a short text
    /// and a round arrow button; the whole tile is one button. Without a figure, an icon in a disc.
    /// </summary>
    public sealed class BentoTile : ThemedDrawable
    {
        private readonly IconKind _icon;
        private readonly string _title, _tag;
        private string _text;
        private readonly Figure _figure;
        private readonly Scene _scene = new Scene();
        private TourPlayer _tour;
        private bool _inFigure;

        public bool Featured { get; set; }

        private bool _compact;

        /// <summary>
        /// A small tile for a grid of four: the figure at the top left without its caption, the arrow in the
        /// corner, the title and two lines of text.
        /// </summary>
        public bool Compact
        {
            get { return _compact; }
            set { _compact = value; if (value) { Height = -1; MinimumSize = new Size(0, 136); } else Height = _figure == null ? 168 : 268; Invalidate(); }
        }

        private bool _drawCompact;

        /// <summary>Drawn like a compact tile (figure above, title and text at the foot) without changing its size rules.</summary>
        public bool DrawCompact
        {
            get { return _drawCompact; }
            set { if (_drawCompact == value) return; _drawCompact = value; Invalidate(); }
        }

        private bool UseCompact
        {
            get { return _compact || _drawCompact; }
        }

        public event EventHandler<EventArgs> Click;

        public BentoTile(IconKind icon, string title, string text, string tag = null, Figure figure = null) : base(true)
        {
            _icon = icon;
            _title = title ?? string.Empty;
            _text = text ?? string.Empty;
            _tag = tag;
            _figure = figure;
            Cursor = Cursors.Pointer;
            Height = figure == null ? 168 : 268;
            ToolTip = figure == null ? _title : _title + ": " + figure.Label;
        }

        public string Text
        {
            get { return _text; }
            set { _text = value ?? string.Empty; Invalidate(); }
        }

        public Figure Figure { get { return _figure; } }

        /// <summary>Walks the figure through its stops once (after delayMs), so it shows what it does; a pointer takes over.</summary>
        public void PlayOnce(double delayMs)
        {
            if (_figure == null || Clock.Reduced) return;
            if (_tour != null && !_tour.Finished) return;
            _tour = new TourPlayer(_figure, delayMs, 1);
            Animate();
        }

        /// <summary>Puts an unseen pointer on the figure at a view-box point, or takes it away (null): for previews.</summary>
        public void SimulatePointer(PointF? viewBox)
        {
            if (_figure == null) return;
            if (_tour != null) _tour.Hold();
            if (viewBox.HasValue) _figure.Move(viewBox.Value.X, viewBox.Value.Y, Animator.Now);
            else _figure.Leave(Animator.Now);
            Animate();
        }

        /// <summary>Room for the title and two lines of text at the foot of a compact tile.</summary>
        private const float CompactTextHeight = 22 + 34 + 12;

        private RectangleF FigureRect
        {
            get
            {
                if (UseCompact)
                {
                    // the figure grows with a taller tile; the title and text keep to the foot of the tile
                    float fh = Math.Max(64, Math.Min(112, Height - 16 - CompactTextHeight));
                    return new RectangleF(8, 8, Math.Max(40, Math.Min(fh * 2.3f, Width - 60)), fh);
                }
                return new RectangleF(10, 34, Width - 20, Height - 34 - 110);
            }
        }

        protected override void OnMouseMoveAt(PointF location)
        {
            if (_figure == null) return;
            var r = FigureRect;
            bool inside = r.Contains(location);
            if (inside)
            {
                if (_tour != null) _tour.Hold();
                var v = FigurePainter.ToViewBox(_figure, r, location);
                _figure.Move(v.X, v.Y, Animator.Now);
                _inFigure = true;
                Animate();
            }
            else if (_inFigure)
            {
                _inFigure = false;
                _figure.Leave(Animator.Now);
                Animate();
            }
        }

        protected override void OnHoverChanged()
        {
            if (_figure != null && !IsHover && _inFigure)
            {
                _inFigure = false;
                _figure.Leave(Animator.Now);
                Animate();
            }
        }

        protected override bool Step(double dt, double now)
        {
            bool more = base.Step(dt, now);
            if (_figure != null)
            {
                if (_tour != null && !_tour.Finished && _tour.Tick(dt, now)) more = true;
                if (_figure.Tick(dt, now)) more = true;
            }
            return more;
        }

        protected override void OnActivated(PointF location)
        {
            var h = Click;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            float h = Hover;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            var plate = Featured ? p.Accent : p.Surface;
            float radius = UseCompact ? 18 : 20;
            FillRound(g, r, radius, plate);
            if (!Featured) StrokeRound(g, r, radius, ThemeColors.Mix(p.Border, p.Accent, 0.75f * h), 1f + 0.4f * h);

            var ink = Featured ? p.OnAccent : p.Text;
            var muted = Featured ? ThemeColors.WithAlpha(p.OnAccent, 0.72f) : p.Muted;

            // round arrow button: fills with the accent and the arrow leans out while the pointer is on the tile
            float ad = 28;
            var arrow = new RectangleF(Width - 12 - ad, 12, ad, ad);
            if (Featured) g.FillEllipse(p.OnAccent, arrow);
            else
            {
                if (h > 0) g.FillEllipse(ThemeColors.WithAlpha(p.Accent, h), arrow);
                using (var pen = new Pen(ThemeColors.Mix(p.Border, p.Accent, h), 1.2f)) g.DrawEllipse(pen, arrow);
            }
            var arrowInk = Featured ? p.Accent : ThemeColors.Mix(p.Text, p.OnAccent, h);
            Icons.Draw(g, IconKind.ArrowUpRight, new RectangleF(arrow.X + 7 + 1.6f * h, arrow.Y + 7 - 1.6f * h, 14, 14), arrowInk, 2.2f);

            float y;
            if (_figure != null && UseCompact)
            {
                var fr = FigureRect;
                var inks = FigureInks.On(p, plate);
                FigurePainter.Paint(g, _figure, _scene, fr, inks, Animator.Now);
                y = Math.Max(fr.Bottom + 4, Height - CompactTextHeight);
                DrawLine(g, Theme.UiFont(14f, true), ink, new RectangleF(16, y, Width - 32, 22), _title);
                using (var brush = new SolidBrush(muted))
                    g.DrawText(Theme.UiFont(12f), brush, new RectangleF(16, y + 22, Width - 32, Height - y - 22 - 12), _text,
                        FormattedTextWrapMode.Word, FormattedTextAlignment.Left, FormattedTextTrimming.WordEllipsis);
                DrawFocus(g, r, radius);
                return;
            }
            if (_figure != null)
            {
                if (!string.IsNullOrEmpty(_tag))
                    DrawLine(g, Theme.UiFont(11f, true), muted, new RectangleF(16, 12, Width - 70, 28), Theme.Spaced(_tag));
                var fr = FigureRect;
                var inks = Featured ? FigureInks.OnAccent(p) : FigureInks.On(p, plate);
                FigurePainter.Paint(g, _figure, _scene, fr, inks, Animator.Now);
                // what the figure is showing, in a few words, under it
                var cap = _figure.Caption ?? string.Empty;
                var capColor = Featured ? ThemeColors.WithAlpha(p.OnAccent, 0.8f) : (cap.StartsWith("rest", StringComparison.Ordinal) ? p.Muted : p.AccentInk);
                DrawLine(g, Theme.UiFont(11f, true), capColor, new RectangleF(16, fr.Bottom - 2, Width - 32, 18), cap.StartsWith("rest", StringComparison.Ordinal) ? "Point at the picture" : cap, FormattedTextAlignment.Left);
                y = fr.Bottom + 20;
            }
            else
            {
                var disc = new RectangleF(18, 18, 42, 42);
                g.FillEllipse(Featured ? p.OnAccent : p.AccentSoft, disc);
                Icons.Draw(g, _icon, new RectangleF(disc.X + 11, disc.Y + 11, 20, 20), Featured ? p.Accent : p.AccentInk, 2.1f);
                if (!string.IsNullOrEmpty(_tag))
                    DrawLine(g, Theme.UiFont(11f, true), muted, new RectangleF(70, 18, Width - 70 - 64, 42), Theme.Spaced(_tag));
                y = 76;
            }
            DrawLine(g, Theme.UiFont(16f, true), ink, new RectangleF(16, y, Width - 32, 24), _title);
            using (var brush = new SolidBrush(muted))
                g.DrawText(Theme.UiFont(12f), brush, new RectangleF(16, y + 26, Width - 32, Height - y - 26 - 10), _text,
                    FormattedTextWrapMode.Word, FormattedTextAlignment.Left, FormattedTextTrimming.WordEllipsis);
            DrawFocus(g, r, radius);
        }
    }

    /// <summary>
    /// A line figure on its own (empty pages): it walks its stops a few times so it shows what it does,
    /// answers the pointer, and says what it shows under it.
    /// </summary>
    public sealed class FigureView : ThemedDrawable
    {
        private readonly Figure _figure;
        private readonly Scene _scene = new Scene();
        private TourPlayer _tour;

        public FigureView(Figure figure) : base(false)
        {
            _figure = figure;
            ToolTip = figure.Label;
            Backdrop = p => p.Stage;
        }

        public Figure Figure { get { return _figure; } }

        public void Play(int laps, double delayMs = 600)
        {
            if (Clock.Reduced) return;
            _tour = new TourPlayer(_figure, delayMs, laps);
            Animate();
        }

        private RectangleF FigureRect { get { return new RectangleF(0, 0, Width, Height - 22); } }

        protected override void OnMouseMoveAt(PointF location)
        {
            if (_tour != null) _tour.Hold();
            var v = FigurePainter.ToViewBox(_figure, FigureRect, location);
            _figure.Move(v.X, v.Y, Animator.Now);
            Animate();
        }

        protected override void OnHoverChanged()
        {
            if (!IsHover) { _figure.Leave(Animator.Now); Animate(); }
        }

        protected override bool Step(double dt, double now)
        {
            bool more = base.Step(dt, now);
            if (_tour != null && !_tour.Finished && _tour.Tick(dt, now)) more = true;
            if (_figure.Tick(dt, now)) more = true;
            return more;
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            var plate = Backdrop(p);
            FigurePainter.Paint(g, _figure, _scene, FigureRect, FigureInks.On(p, plate), Animator.Now);
            var cap = _figure.Caption ?? string.Empty;
            bool rest = cap.StartsWith("rest", StringComparison.Ordinal);
            DrawLine(g, Theme.UiFont(11f, true), rest ? p.Muted : p.AccentInk, new RectangleF(0, Height - 22, Width, 20), rest ? cap.Substring(Math.Min(cap.Length, 8)).Trim() : cap, FormattedTextAlignment.Center);
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
