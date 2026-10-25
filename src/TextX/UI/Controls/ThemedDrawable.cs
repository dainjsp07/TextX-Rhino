using System;
using Eto.Drawing;
using Eto.Forms;
using TextX.Integration;
using TextX.UI.Theming;

namespace TextX.UI.Controls
{
    /// <summary>
    /// Base class of the custom-drawn controls: tracks hover / pressed / focus and paints with the
    /// current palette, so every control follows the theme without native styling.
    /// </summary>
    public abstract class ThemedDrawable : Drawable
    {
        protected bool IsHover { get; private set; }
        protected bool IsPressed { get; private set; }

        /// <summary>
        /// True while the control has focus and the keyboard is being used to move around (Tab), so
        /// clicks, and the focus a dialog gives its first control when it opens, show no ring.
        /// </summary>
        protected bool ShowFocus { get { return HasFocus && s_keyboardNavigation; } }
        private static bool s_keyboardNavigation;

        /// <summary>Called when Tab is pressed anywhere in a Text X window.</summary>
        public static void NoteKeyboardNavigation() { s_keyboardNavigation = true; }

        protected static ThemeColors P { get { return Theme.Current; } }

        /// <summary>Background behind rounded corners (the colour of the parent).</summary>
        public Func<ThemeColors, Color> Backdrop { get; set; }

        protected ThemedDrawable(bool focusable)
        {
            CanFocus = focusable;
            Backdrop = p => p.Window;
            MouseEnter += (s, e) => { IsHover = true; OnHoverChanged(); Invalidate(); };
            MouseLeave += (s, e) => { IsHover = false; IsPressed = false; OnHoverChanged(); Invalidate(); };
            MouseDown += (s, e) =>
            {
                s_keyboardNavigation = false;
                if (!Enabled) return;
                if (e.Buttons == MouseButtons.Primary)
                {
                    IsPressed = true;
                    Invalidate();
                }
                OnMouseDownAt(e);
            };
            MouseUp += (s, e) =>
            {
                bool wasPressed = IsPressed;
                IsPressed = false;
                Invalidate();
                if (!Enabled) return;
                if (wasPressed && e.Buttons == MouseButtons.Primary && new RectangleF(0, 0, Width, Height).Contains(e.Location))
                    OnActivated(e.Location);
            };
            MouseMove += (s, e) => OnMouseMoveAt(e.Location);
            GotFocus += (s, e) => Invalidate();
            LostFocus += (s, e) => Invalidate();
            KeyDown += (s, e) =>
            {
                if (e.Key == Keys.Tab || e.Key == Keys.Left || e.Key == Keys.Right || e.Key == Keys.Up || e.Key == Keys.Down)
                {
                    if (!s_keyboardNavigation) { s_keyboardNavigation = true; Invalidate(); }
                }
                if (!Enabled) return;
                if (e.Key == Keys.Space || e.Key == Keys.Enter)
                {
                    e.Handled = true;
                    OnActivated(new PointF(-1, -1));
                }
                else
                {
                    OnKey(e);
                }
            };
            Paint += (s, e) =>
            {
                try
                {
                    var g = e.Graphics;
                    g.AntiAlias = true;
                    var back = Backdrop != null ? Backdrop(P) : P.Window;
                    g.FillRectangle(back, new RectangleF(0, 0, Width, Height));
                    OnDraw(g);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log(GetType().Name + " paint failed: " + ex.Message);
                }
            };
        }

        protected abstract void OnDraw(Graphics g);

        /// <summary>Click, Space or Enter. <paramref name="location"/> is (-1,-1) for the keyboard.</summary>
        protected virtual void OnActivated(PointF location) { }

        protected virtual void OnMouseDownAt(MouseEventArgs e) { }
        protected virtual void OnMouseMoveAt(PointF location) { }
        protected virtual void OnHoverChanged() { }
        protected virtual void OnKey(KeyEventArgs e) { }

        // ------------------------------------------------------------------ drawing helpers
        public static void FillRound(Graphics g, RectangleF r, float radius, Color fill)
        {
            if (fill.A <= 0) return;
            using (var path = GraphicsPath.GetRoundRect(r, radius)) g.FillPath(fill, path);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color stroke, float width = 1f, bool dashed = false)
        {
            if (stroke.A <= 0) return;
            var rr = new RectangleF(r.X + width / 2f, r.Y + width / 2f, r.Width - width, r.Height - width);
            using (var path = GraphicsPath.GetRoundRect(rr, Math.Max(0, radius - width / 2f)))
            using (var pen = new Pen(stroke, width))
            {
                if (dashed) pen.DashStyle = new DashStyle(0, new float[] { 3f, 2f });
                g.DrawPath(pen, path);
            }
        }

        public static SizeF Measure(Font font, string text)
        {
            if (string.IsNullOrEmpty(text)) return new SizeF(0, font.LineHeight);
            return font.MeasureString(text);
        }

        /// <summary>Draws one line of text vertically centred in <paramref name="r"/>, trimmed with an ellipsis.</summary>
        public static void DrawLine(Graphics g, Font font, Color color, RectangleF r, string text,
            FormattedTextAlignment align = FormattedTextAlignment.Left)
        {
            if (string.IsNullOrEmpty(text) || r.Width <= 2) return;
            // The layout box must be taller than the text line, otherwise WPF draws nothing at all.
            var size = font.MeasureString(text);
            float h = Math.Max(size.Height, font.LineHeight) + 6f;
            var tr = new RectangleF(r.X, r.Y + (r.Height - size.Height) / 2f, r.Width, h);
            using (var brush = new SolidBrush(color))
                g.DrawText(font, brush, tr, text, FormattedTextWrapMode.None, align, FormattedTextTrimming.CharacterEllipsis);
        }

        protected void DrawFocus(Graphics g, RectangleF r, float radius)
        {
            if (ShowFocus) StrokeRound(g, new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), radius + 1, P.AccentInk, 1.5f);
        }
    }
}
