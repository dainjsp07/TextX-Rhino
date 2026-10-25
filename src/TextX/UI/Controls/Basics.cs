using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using TextX.Integration;
using TextX.UI.Theming;

namespace TextX.UI.Controls
{
    /// <summary>A rounded panel that hosts other controls (cards, text-box frames, groups).</summary>
    public sealed class Card : Drawable
    {
        public Func<ThemeColors, Color> Fill { get; set; }
        public Func<ThemeColors, Color> Stroke { get; set; }
        public Func<ThemeColors, Color> Backdrop { get; set; }
        public float Radius { get; set; }

        public Card(Control content, Padding padding, Func<ThemeColors, Color> fill, Func<ThemeColors, Color> stroke, float radius = 10f)
        {
            Content = content;
            Padding = padding;
            Fill = fill;
            Stroke = stroke;
            Radius = radius;
            Backdrop = p => p.Window;
            Paint += (s, e) =>
            {
                try
                {
                    var p = Theme.Current;
                    var g = e.Graphics;
                    g.AntiAlias = true;
                    g.FillRectangle(Backdrop(p), new RectangleF(0, 0, Width, Height));
                    var r = new RectangleF(0, 0, Width, Height);
                    ThemedDrawable.FillRound(g, r, Radius, Fill(p));
                    if (Stroke != null) ThemedDrawable.StrokeRound(g, r, Radius, Stroke(p), 1f);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log("card paint failed: " + ex.Message);
                }
            };
        }
    }

    /// <summary>A small rounded label ("right-to-left", "edited", counts).</summary>
    public sealed class Pill : ThemedDrawable
    {
        private string _text;
        public Func<ThemeColors, Color> Fill { get; set; }
        public Func<ThemeColors, Color> Foreground { get; set; }
        public Func<ThemeColors, Color> Outline { get; set; }

        public Pill(string text, Func<ThemeColors, Color> fill, Func<ThemeColors, Color> foreground) : base(false)
        {
            Fill = fill;
            Foreground = foreground;
            Text = text;
        }

        public string Text
        {
            get { return _text; }
            set
            {
                _text = value ?? string.Empty;
                Size = new Size((int)Math.Ceiling(Measure(Theme.UiFont(11), _text).Width) + 16, 19);
                Invalidate();
            }
        }

        protected override void OnDraw(Graphics g)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            FillRound(g, r, 9, Fill(P));
            if (Outline != null) StrokeRound(g, r, 9, Outline(P));
            DrawLine(g, Theme.UiFont(11), Foreground(P), new RectangleF(8, 0, Width - 16, Height), _text, FormattedTextAlignment.Center);
        }
    }

    /// <summary>A themed check box with its label.</summary>
    public sealed class ToggleCheck : ThemedDrawable
    {
        private bool _checked;
        private string _text;
        private readonly float _fontPx;

        public event EventHandler<EventArgs> CheckedChanged;

        public ToggleCheck(string text, float fontPx = 13f) : base(true)
        {
            _fontPx = fontPx;
            Cursor = Cursors.Pointer;
            Text = text;
        }

        public string Text
        {
            get { return _text; }
            set
            {
                _text = value ?? string.Empty;
                Size = new Size((int)Math.Ceiling(Measure(Theme.UiFont(_fontPx), _text).Width) + 26, 24);
                Invalidate();
            }
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnActivated(PointF location)
        {
            Checked = !Checked;
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            var box = new RectangleF(1, (Height - 15) / 2f, 15, 15);
            if (_checked)
            {
                FillRound(g, box, 4, Enabled ? p.AccentInk : p.Muted);
                Icons.Draw(g, IconKind.Check, new RectangleF(box.X + 1.5f, box.Y + 1.5f, 12, 12), p.IsDark ? p.Window : p.OnAccent, 3f);
            }
            else
            {
                FillRound(g, box, 4, p.Field);
                StrokeRound(g, box, 4, IsHover ? p.AccentInk : ThemeColors.Mix(p.Border, p.Muted, 0.5f), 1.2f);
            }
            DrawLine(g, Theme.UiFont(_fontPx), Enabled ? p.Text : p.Muted, new RectangleF(23, 0, Width - 23, Height), _text);
            if (ShowFocus) StrokeRound(g, new RectangleF(box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4), 6, p.AccentInk, 1.2f);
        }
    }

    /// <summary>Radio-button rows (one choice), drawn as selectable cards.</summary>
    public sealed class OptionList : ThemedDrawable
    {
        private readonly List<KeyValuePair<string, string>> _items = new List<KeyValuePair<string, string>>();
        private int _selected = -1;
        private int _hover = -1;
        private const int RowH = 36, Gap = 6;

        public event EventHandler<EventArgs> SelectedIndexChanged;

        public OptionList() : base(true)
        {
            Cursor = Cursors.Pointer;
            Backdrop = p => p.Surface;
        }

        /// <summary>Adds an option with an optional note shown at the right ("Recommended").</summary>
        public void Add(string text, string note = null)
        {
            _items.Add(new KeyValuePair<string, string>(text, note ?? string.Empty));
            Height = _items.Count * RowH + (_items.Count - 1) * Gap;
            Invalidate();
        }

        /// <summary>Replaces the option texts (the notes stay).</summary>
        public void SetTexts(string[] texts)
        {
            for (int i = 0; i < Math.Min(texts.Length, _items.Count); i++)
                if (texts[i] != null) _items[i] = new KeyValuePair<string, string>(texts[i], _items[i].Value);
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value == _selected) return;
                _selected = value;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        private int HitTest(PointF pt)
        {
            if (pt.Y < 0) return -1;
            int i = (int)(pt.Y / (RowH + Gap));
            if (i < 0 || i >= _items.Count) return -1;
            return pt.Y - i * (RowH + Gap) <= RowH ? i : -1;
        }

        protected override void OnMouseMoveAt(PointF location)
        {
            int h = HitTest(location);
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnHoverChanged()
        {
            if (!IsHover) _hover = -1;
        }

        protected override void OnActivated(PointF location)
        {
            if (location.X < 0) { SelectedIndex = (_selected + 1) % Math.Max(1, _items.Count); return; }
            int i = HitTest(location);
            if (i >= 0) SelectedIndex = i;
        }

        protected override void OnKey(KeyEventArgs e)
        {
            if (e.Key == Keys.Down) { SelectedIndex = Math.Min(_items.Count - 1, _selected + 1); e.Handled = true; }
            else if (e.Key == Keys.Up) { SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; }
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            for (int i = 0; i < _items.Count; i++)
            {
                var r = new RectangleF(0.5f, i * (RowH + Gap) + 0.5f, Width - 1, RowH - 1);
                bool sel = i == _selected;
                FillRound(g, r, 8, sel ? p.AccentSoft : (i == _hover ? p.ItemHover : p.Surface));
                StrokeRound(g, r, 8, sel ? p.AccentInk : p.Border);
                var dot = new RectangleF(r.X + 11, r.Y + (RowH - 14) / 2f, 14, 14);
                using (var pen = new Pen(sel ? p.AccentInk : ThemeColors.Mix(p.Border, p.Muted, 0.6f), 1.5f))
                    g.DrawEllipse(pen, dot);
                if (sel) g.FillEllipse(p.AccentInk, new RectangleF(dot.X + 3.5f, dot.Y + 3.5f, 7, 7));
                var note = _items[i].Value;
                float noteW = string.IsNullOrEmpty(note) ? 0 : Measure(Theme.UiFont(11), note).Width + 12;
                DrawLine(g, Theme.UiFont(13, sel), p.Text, new RectangleF(r.X + 34, r.Y, r.Width - 44 - noteW, RowH), _items[i].Key);
                if (noteW > 0) DrawLine(g, Theme.UiFont(11), sel ? p.Subtle : p.Muted, new RectangleF(r.Right - noteW - 4, r.Y, noteW, RowH), note);
            }
            if (ShowFocus && _selected >= 0)
                StrokeRound(g, new RectangleF(0, _selected * (RowH + Gap) - 1, Width, RowH + 2), 9, p.AccentInk, 1.5f);
        }
    }

    /// <summary>A row of mutually exclusive segments (print alignment).</summary>
    public sealed class SegmentedControl : ThemedDrawable
    {
        private readonly string[] _items;
        private int _selected;
        private int _hover = -1;

        public event EventHandler<EventArgs> SelectedIndexChanged;

        public SegmentedControl(params string[] items) : base(true)
        {
            _items = items;
            Height = 34;
            Cursor = Cursors.Pointer;
            Backdrop = p => p.Surface;
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value == _selected) return;
                _selected = value;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        private int HitTest(PointF pt)
        {
            if (_items.Length == 0 || Width <= 0) return -1;
            int i = (int)((pt.X - 3) / ((Width - 6f) / _items.Length));
            return i >= 0 && i < _items.Length ? i : -1;
        }

        protected override void OnMouseMoveAt(PointF location)
        {
            int h = HitTest(location);
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnHoverChanged()
        {
            if (!IsHover) _hover = -1;
        }

        protected override void OnActivated(PointF location)
        {
            if (location.X < 0) { SelectedIndex = (_selected + 1) % _items.Length; return; }
            int i = HitTest(location);
            if (i >= 0) SelectedIndex = i;
        }

        protected override void OnKey(KeyEventArgs e)
        {
            if (e.Key == Keys.Right) { SelectedIndex = Math.Min(_items.Length - 1, _selected + 1); e.Handled = true; }
            else if (e.Key == Keys.Left) { SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; }
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            FillRound(g, outer, 9, p.Field);
            StrokeRound(g, outer, 9, p.Border);
            float segW = (Width - 6f) / _items.Length;
            for (int i = 0; i < _items.Length; i++)
            {
                var r = new RectangleF(3 + i * segW, 3, segW - (i < _items.Length - 1 ? 3 : 0), Height - 6);
                bool sel = i == _selected;
                if (sel)
                {
                    FillRound(g, r, 7, p.AccentSoft);
                    StrokeRound(g, r, 7, p.AccentLine);
                }
                else if (i == _hover)
                {
                    FillRound(g, r, 7, p.ItemHover);
                }
                DrawLine(g, Theme.UiFont(12.5f, sel), sel ? p.AccentInk : p.Muted, r, _items[i], FormattedTextAlignment.Center);
            }
            DrawFocus(g, outer, 9);
        }
    }

    /// <summary>One-line status message with a coloured dot (trimmed with an ellipsis).</summary>
    public sealed class StatusText : ThemedDrawable
    {
        public enum Level { Info, Success, Warning, Error }

        private string _text = string.Empty;
        private Level _level = Level.Info;

        public StatusText() : base(false)
        {
            Height = 22;
            Backdrop = p => p.Footer;
        }

        public void Set(string text, Level level)
        {
            _text = text ?? string.Empty;
            _level = level;
            ToolTip = _text;
            Invalidate();
        }

        public string Text { get { return _text; } }

        protected override void OnDraw(Graphics g)
        {
            if (string.IsNullOrEmpty(_text)) return;
            var p = P;
            Color dot, fg;
            switch (_level)
            {
                case Level.Success: dot = p.Success; fg = p.Muted; break;
                case Level.Warning: dot = p.Warning; fg = p.Warning; break;
                case Level.Error: dot = p.Error; fg = p.Error; break;
                default: dot = p.Muted; fg = p.Muted; break;
            }
            g.FillEllipse(dot, new RectangleF(4, (Height - 7) / 2f, 7, 7));
            DrawLine(g, Theme.UiFont(12), fg, new RectangleF(17, 0, Width - 19, Height), _text);
        }
    }
}
