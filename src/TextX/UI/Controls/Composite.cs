using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using TextX.Core;
using TextX.Core.Text;
using TextX.UI.Theming;

namespace TextX.UI.Controls
{
    /// <summary>Tabs drawn as text with icons and an accent underline.</summary>
    public sealed class TabStrip : ThemedDrawable
    {
        private readonly List<KeyValuePair<string, IconKind>> _tabs = new List<KeyValuePair<string, IconKind>>();
        private readonly List<RectangleF> _rects = new List<RectangleF>();
        private int _selected;
        private int _hover = -1;

        public event EventHandler<EventArgs> SelectedIndexChanged;

        public TabStrip() : base(true)
        {
            Height = 42;
            Cursor = Cursors.Pointer;
        }

        public void Add(string text, IconKind icon)
        {
            _tabs.Add(new KeyValuePair<string, IconKind>(text, icon));
            Layout();
        }

        private void Layout()
        {
            _rects.Clear();
            float x = 12;
            foreach (var t in _tabs)
            {
                float w = 10 + 14 + 6 + Measure(Theme.UiFont(13, true), t.Key).Width + 10;
                _rects.Add(new RectangleF(x, 0, w, 42));
                x += w + 2;
            }
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value == _selected || value < 0 || value >= _tabs.Count) return;
                _selected = value;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        private int HitTest(PointF pt)
        {
            for (int i = 0; i < _rects.Count; i++) if (_rects[i].Contains(pt)) return i;
            return -1;
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
            if (location.X < 0) { SelectedIndex = (_selected + 1) % Math.Max(1, _tabs.Count); return; }
            int i = HitTest(location);
            if (i >= 0) SelectedIndex = i;
        }

        protected override void OnKey(KeyEventArgs e)
        {
            if (e.Key == Keys.Right) { SelectedIndex = Math.Min(_tabs.Count - 1, _selected + 1); e.Handled = true; }
            else if (e.Key == Keys.Left) { SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; }
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            g.FillRectangle(p.Divider, new RectangleF(0, Height - 1, Width, 1));
            for (int i = 0; i < _tabs.Count; i++)
            {
                var r = _rects[i];
                bool sel = i == _selected;
                var fg = sel ? p.Text : (i == _hover ? p.Text : p.Muted);
                Icons.Draw(g, _tabs[i].Value, new RectangleF(r.X + 10, (Height - 14) / 2f, 14, 14), fg, 2f);
                DrawLine(g, Theme.UiFont(13, sel), fg, new RectangleF(r.X + 30, 0, r.Width - 30, Height), _tabs[i].Key);
                if (sel) g.FillRectangle(p.AccentInk, new RectangleF(r.X, Height - 2, r.Width, 2));
                if (sel && ShowFocus) StrokeRound(g, new RectangleF(r.X + 2, 6, r.Width - 4, Height - 12), 6, p.AccentLine, 1.2f);
            }
        }
    }

    /// <summary>An entry of a <see cref="KeyPicker"/>.</summary>
    public sealed class PickerItem
    {
        public string Key;
        public string Text;
        public string Secondary;
        public bool SeparatorBefore;
    }

    /// <summary>
    /// A drop-down button (language and service selection): caption, value, optional native name and a
    /// chevron; opens a menu with the choices.
    /// </summary>
    public sealed class KeyPicker : ThemedDrawable
    {
        private readonly List<PickerItem> _items = new List<PickerItem>();
        private string _key;
        private readonly string _caption;
        private readonly bool _chip;

        public event EventHandler<EventArgs> SelectedKeyChanged;

        /// <summary>Show the item's secondary text on the button (it is always shown in the menu).</summary>
        public bool ShowSecondary { get; set; }

        /// <summary>Extra commands appended to the menu (e.g. "Settings...").</summary>
        public readonly List<KeyValuePair<string, Action>> ExtraCommands = new List<KeyValuePair<string, Action>>();

        private Func<ThemeColors, Color> _dotColor;

        /// <summary>Colour of the status dot drawn by chip-style pickers (null = no dot).</summary>
        public Func<ThemeColors, Color> DotColor
        {
            get { return _dotColor; }
            set { _dotColor = value; UpdateSize(); Invalidate(); }
        }

        public KeyPicker(string caption, bool chip) : base(true)
        {
            _caption = caption ?? string.Empty;
            _chip = chip;
            ShowSecondary = true;
            Cursor = Cursors.Pointer;
            Backdrop = p => chip ? p.Window : p.Surface;
        }

        public void SetItems(IEnumerable<PickerItem> items)
        {
            _items.Clear();
            _items.AddRange(items);
            UpdateSize();
        }

        public bool Contains(string key)
        {
            return _items.Any(i => i.Key == key);
        }

        public string SelectedKey
        {
            get { return _key; }
            set
            {
                if (value == _key || !Contains(value)) return;
                _key = value;
                UpdateSize();
                Invalidate();
                var h = SelectedKeyChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public PickerItem SelectedItem { get { return _items.FirstOrDefault(i => i.Key == _key); } }

        private void UpdateSize()
        {
            var it = SelectedItem;
            float w = _chip ? 12 : 10;
            if (DotColor != null && _chip) w += 8 + 7;
            if (_caption.Length > 0) w += Measure(Theme.UiFont(11), _caption).Width + 7;
            w += Measure(Theme.UiFont(13, true), it == null ? "-" : it.Text).Width;
            if (ShowSecondary && it != null && !string.IsNullOrEmpty(it.Secondary)) w += 7 + Measure(Theme.UiFont(13), it.Secondary).Width;
            w += 7 + 13 + (_chip ? 12 : 10);
            Size = new Size((int)Math.Ceiling(w), _chip ? 34 : 32);
        }

        protected override void OnActivated(PointF location)
        {
            var menu = new ContextMenu();
            foreach (var it in _items)
            {
                if (it.SeparatorBefore) menu.Items.Add(new SeparatorMenuItem());
                var item = it;
                var mi = new CheckMenuItem { Text = string.IsNullOrEmpty(it.Secondary) ? it.Text : it.Text + "  -  " + it.Secondary, Checked = it.Key == _key };
                mi.Click += (s, e) => SelectedKey = item.Key;
                menu.Items.Add(mi);
            }
            if (ExtraCommands.Count > 0)
            {
                menu.Items.Add(new SeparatorMenuItem());
                foreach (var c in ExtraCommands)
                {
                    var action = c.Value;
                    var mi = new ButtonMenuItem { Text = c.Key };
                    mi.Click += (s, e) => action();
                    menu.Items.Add(mi);
                }
            }
            menu.Show(this, new PointF(0, Height + 2));
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            if (_chip)
            {
                FillRound(g, r, Height / 2f, IsHover ? ThemeColors.Mix(p.Surface, p.Text, 0.06f) : p.Surface);
                StrokeRound(g, r, Height / 2f, p.Border);
            }
            else if (IsHover)
            {
                FillRound(g, r, 7, p.ItemHover);
            }
            float x = _chip ? 12 : 10;
            if (_chip && DotColor != null)
            {
                g.FillEllipse(DotColor(p), new RectangleF(x, (Height - 8) / 2f, 8, 8));
                x += 8 + 7;
            }
            if (_caption.Length > 0)
            {
                float cw = Measure(Theme.UiFont(11), _caption).Width;
                DrawLine(g, Theme.UiFont(11), p.Muted, new RectangleF(x, 0, cw + 2, Height), _caption);
                x += cw + 7;
            }
            var it = SelectedItem;
            var main = it == null ? "-" : it.Text;
            float mw = Measure(Theme.UiFont(13, !_chip), main).Width;
            DrawLine(g, Theme.UiFont(13, !_chip), p.Text, new RectangleF(x, 0, mw + 2, Height), main);
            x += mw + 7;
            if (ShowSecondary && it != null && !string.IsNullOrEmpty(it.Secondary))
            {
                float sw = Measure(Theme.UiFont(13), it.Secondary).Width;
                DrawLine(g, Theme.UiFont(13), p.Muted, new RectangleF(x, 0, sw + 2, Height), it.Secondary);
                x += sw + 7;
            }
            Icons.Draw(g, IconKind.Chevron, new RectangleF(x, (Height - 13) / 2f, 13, 13), p.Muted, 2f);
            DrawFocus(g, r, _chip ? Height / 2f : 7);
        }
    }

    /// <summary>One row of the object list.</summary>
    public sealed class ObjectRow
    {
        public string Title;
        public string Subtitle;
        public bool SubtitleRtl;
        public bool SubtitleMuted;
        public bool IsLeader;
        public bool Included = true;
        public string Badge;
        /// <summary>"accent", "success" or "muted".</summary>
        public string BadgeStyle = "muted";
    }

    /// <summary>
    /// The list of loaded text / leader objects (custom drawn so it matches the theme).
    /// Place it inside a <see cref="Scrollable"/>.
    /// </summary>
    public sealed class ObjectList : ThemedDrawable
    {
        private const int RowH = 50, Gap = 6;
        private IList<ObjectRow> _rows = new List<ObjectRow>();
        private int _selected = -1;
        private int _hover = -1;

        public event EventHandler<EventArgs> SelectedIndexChanged;
        /// <summary>Raised when the user asks to include / skip a row (index).</summary>
        public event EventHandler<int> IncludeToggled;
        /// <summary>Raised for "Select in Rhino" (index).</summary>
        public event EventHandler<int> SelectInRhino;

        public string EmptyText { get; set; }

        public ObjectList() : base(true)
        {
            EmptyText = "No objects loaded.";
            Cursor = Cursors.Pointer;
            SizeChanged += (s, e) => Invalidate();
        }

        public void SetRows(IList<ObjectRow> rows)
        {
            _rows = rows ?? new List<ObjectRow>();
            if (_selected >= _rows.Count) _selected = _rows.Count - 1;
            Height = Math.Max(120, _rows.Count * (RowH + Gap));
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
            int i = (int)(pt.Y / (RowH + Gap));
            if (i < 0 || i >= _rows.Count) return -1;
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

        protected override void OnMouseDownAt(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i < 0) return;
            if (e.Buttons == MouseButtons.Alternate)
            {
                SelectedIndex = i;
                var menu = new ContextMenu();
                var toggle = new ButtonMenuItem { Text = _rows[i].Included ? "Skip this object when updating" : "Include this object when updating" };
                toggle.Click += (s, a) => { var h = IncludeToggled; if (h != null) h(this, i); };
                var select = new ButtonMenuItem { Text = "Select in Rhino" };
                select.Click += (s, a) => { var h = SelectInRhino; if (h != null) h(this, i); };
                menu.Items.Add(toggle);
                menu.Items.Add(select);
                menu.Show(this, e.Location);
            }
        }

        protected override void OnActivated(PointF location)
        {
            if (location.X < 0) return;
            int i = HitTest(location);
            if (i >= 0) SelectedIndex = i;
        }

        protected override void OnKey(KeyEventArgs e)
        {
            if (_rows.Count == 0) return;
            if (e.Key == Keys.Down) { SelectedIndex = Math.Min(_rows.Count - 1, _selected + 1); e.Handled = true; }
            else if (e.Key == Keys.Up) { SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; }
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            if (_rows.Count == 0)
            {
                using (var brush = new SolidBrush(p.Muted))
                    g.DrawText(Theme.UiFont(12), brush, new RectangleF(6, 8, Width - 12, Height - 16), EmptyText ?? string.Empty,
                        FormattedTextWrapMode.Word, FormattedTextAlignment.Left, FormattedTextTrimming.None);
                return;
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var r = new RectangleF(0.5f, i * (RowH + Gap) + 0.5f, Width - 1, RowH - 1);
                bool sel = i == _selected;
                FillRound(g, r, 8, sel ? p.AccentSoft : (i == _hover ? p.ItemHover : p.Item));
                if (sel) StrokeRound(g, r, 8, p.AccentLine);
                var dim = row.Included ? 1f : 0.45f;

                var iconBox = new RectangleF(r.X + 9, r.Y + (RowH - 26) / 2f, 26, 26);
                FillRound(g, iconBox, 6, p.IconBox);
                Icons.Draw(g, row.IsLeader ? IconKind.Leader : IconKind.Text, new RectangleF(iconBox.X + 6.5f, iconBox.Y + 6.5f, 13, 13),
                    ThemeColors.WithAlpha(sel ? p.AccentInk : p.Subtle, dim), 2f);

                float badgeW = 0;
                var badge = row.Included ? row.Badge : "Skipped";
                if (!string.IsNullOrEmpty(badge))
                {
                    badgeW = Measure(Theme.UiFont(11), badge).Width + 14;
                    var br = new RectangleF(r.Right - badgeW - 8, r.Y + (RowH - 19) / 2f, badgeW, 19);
                    Color bf, bt;
                    var style = row.Included ? row.BadgeStyle : "muted";
                    if (style == "accent") { bf = p.AccentSoft; bt = p.AccentInk; }
                    else if (style == "success") { bf = p.SuccessSoft; bt = p.SuccessText; }
                    else { bf = p.IconBox; bt = p.Subtle; }
                    if (sel && style == "accent") bf = ThemeColors.Mix(p.AccentSoft, p.Window, 0.35f);
                    FillRound(g, br, 9, bf);
                    DrawLine(g, Theme.UiFont(11), bt, br, badge, FormattedTextAlignment.Center);
                    badgeW += 8;
                }

                float tx = r.X + 44;
                float tw = r.Width - 44 - 10 - badgeW;
                DrawLine(g, Theme.UiFont(13), ThemeColors.WithAlpha(p.Text, dim), new RectangleF(tx, r.Y + 6, tw, 19), row.Title);
                var subColor = row.SubtitleMuted ? p.Muted : (sel ? p.Subtle : p.Muted);
                DrawLine(g, Theme.UiFont(12), ThemeColors.WithAlpha(subColor, dim), new RectangleF(tx, r.Y + 25, tw, 18), row.Subtitle,
                    row.SubtitleRtl ? FormattedTextAlignment.Right : FormattedTextAlignment.Left);
            }
            if (ShowFocus && _selected >= 0)
                StrokeRound(g, new RectangleF(0, _selected * (RowH + Gap), Width, RowH), 9, p.AccentInk, 1.2f);
        }
    }

    /// <summary>
    /// Sheet preview: what the text looks like on screen and in print / PDF, side by side.
    /// The UI toolkit applies the Unicode bidi algorithm itself, so logical text is drawn here: this is
    /// the reference appearance the Rhino objects must match.
    /// </summary>
    public sealed class SheetPreview : ThemedDrawable
    {
        public string ScreenText = string.Empty;
        public string ScreenLang = "en";
        public string PrintText = string.Empty;
        public string PrintLang = "he";
        public string FontFace = "Arial";
        public bool Bold;
        public bool Italic;

        public SheetPreview() : base(false)
        {
            Height = 124;
            Backdrop = p => p.Window;
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            FillRound(g, outer, 10, p.Surface);
            StrokeRound(g, outer, 10, p.Border);
            DrawLine(g, Theme.UiFont(11, true), p.Muted, new RectangleF(12, 0, Width - 24, 32), "SHEET PREVIEW");

            var area = new RectangleF(1, 32, Width - 2, Height - 33);
            using (var sheet = GraphicsPath.GetRoundRect(area, 0, 0, 9, 9))
                g.FillPath(p.SheetBg, sheet);
            float pad = 10, gap = 10;
            float bw = (area.Width - 2 * pad - gap) / 2f;
            var screenBox = new RectangleF(area.X + pad, area.Y + pad, bw, area.Height - 2 * pad);
            var printBox = new RectangleF(screenBox.Right + gap, screenBox.Y, bw, screenBox.Height);
            StrokeRound(g, screenBox, 6, p.SheetDash, 1f, true);
            StrokeRound(g, printBox, 6, p.AccentInk, 1f);
            DrawBox(g, screenBox, "ON SCREEN", p.SheetLabel, ScreenText, ScreenLang);
            DrawBox(g, printBox, "PRINT / PDF", p.AccentInk, PrintText, PrintLang);
        }

        private void DrawBox(Graphics g, RectangleF box, string label, Color labelColor, string text, string lang)
        {
            var p = P;
            DrawLine(g, Theme.UiFont(10, true), labelColor, new RectangleF(box.X + 10, box.Y + 6, box.Width - 20, 16), label);
            bool rtl = Languages.IsRtl(lang) || RtlText.ContainsRtl(text);
            Font font;
            try { font = Theme.GetFont(string.IsNullOrEmpty(FontFace) ? "Arial" : FontFace, rtl ? 14 : 13, Bold, Italic); }
            catch { font = Theme.UiFont(13); }
            var display = string.IsNullOrEmpty(text) ? "-" : text.Replace("\r", string.Empty).Replace("\n", " ⏎ ");
            using (var brush = new SolidBrush(p.SheetText))
                g.DrawText(font, brush, new RectangleF(box.X + 10, box.Y + 24, box.Width - 20, box.Height - 28), display,
                    FormattedTextWrapMode.Word, rtl ? FormattedTextAlignment.Right : FormattedTextAlignment.Left, FormattedTextTrimming.CharacterEllipsis);
        }
    }
}
