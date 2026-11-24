using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using TextX.Core.Documents;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Studio
{
    /// <summary>
    /// Shows one rendered PDF page fitted to the control, with the selected paragraph outlined and
    /// paragraphs to check marked. Clicking text on the page selects its paragraph.
    /// Zoom like a Rhino viewport: the mouse wheel zooms around the pointer, dragging (any button) moves
    /// the page, double-click shows the whole page again (or zooms to 200 %). Shift + wheel scrolls
    /// sideways, Ctrl + wheel up and down.
    /// </summary>
    public sealed class PageView : Drawable
    {
        private Bitmap _image;
        private DocPage _page;
        private float _pixelsPerPoint = 1f;
        private string _placeholder = string.Empty;
        // zoom (1 = the whole page fits) and the point of the page (0..1 of its width and height) at the centre
        private float _zoom = 1f, _cx = 0.5f, _cy = 0.5f;
        private PointF? _dragFrom;
        private bool _dragged;
        public const float MaxZoom = 8f;

        public event EventHandler<DocBlock> BlockClicked;
        /// <summary>Raised when the user zooms or scrolls (to keep a second view in step).</summary>
        public event EventHandler ViewChanged;

        public float Zoom { get { return _zoom; } }

        /// <summary>Shows the page at <paramref name="zoom"/> with the given point (0..1) at the centre.</summary>
        public void SetView(float zoom, float centerX, float centerY)
        {
            _zoom = Math.Max(1f, Math.Min(MaxZoom, zoom));
            _cx = centerX;
            _cy = centerY;
            Clamp();
            Invalidate();
        }

        public void GetView(out float zoom, out float centerX, out float centerY)
        {
            zoom = _zoom;
            centerX = _cx;
            centerY = _cy;
        }

        /// <summary>Zooms by a factor around the centre of the view (or a point on screen).</summary>
        public void ZoomBy(float factor, PointF? around = null)
        {
            if (_image == null) return;
            var rect = ImageRect();
            var at = around ?? new PointF(Width / 2f, AreaTop + AreaHeight / 2f);
            float u = rect.Width > 0 ? (at.X - rect.X) / rect.Width : 0.5f, v = rect.Height > 0 ? (at.Y - rect.Y) / rect.Height : 0.5f;
            float nz = Math.Max(1f, Math.Min(MaxZoom, _zoom * factor));
            if (Math.Abs(nz - _zoom) < 0.0001f) return;
            _zoom = nz;
            var fit = FitSize();
            float w = fit.Width * _zoom, h = fit.Height * _zoom;
            // keep the page point under the pointer where it is
            float left = at.X - u * w, top = at.Y - v * h;
            _cx = (Width / 2f - left) / w;
            _cy = (AreaTop + AreaHeight / 2f - top) / h;
            Clamp();
            Invalidate();
            Changed();
        }

        /// <summary>Back to the whole page.</summary>
        public void ZoomToFit()
        {
            _zoom = 1f;
            _cx = _cy = 0.5f;
            Invalidate();
            Changed();
        }

        private void Changed()
        {
            var h = ViewChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private float AreaTop { get { return string.IsNullOrEmpty(Caption) ? 12f : 40f; } }
        private float AreaHeight { get { return Math.Max(10, Height - AreaTop - 12f); } }

        private SizeF FitSize()
        {
            if (_image == null) return SizeF.Empty;
            float aw = Math.Max(10, Width - 24f), ah = AreaHeight;
            float s = Math.Min(aw / _image.Width, ah / _image.Height);
            return new SizeF(_image.Width * s, _image.Height * s);
        }

        /// <summary>Keeps the page on screen: centred when it fits, edges not pulled past the view.</summary>
        private void Clamp()
        {
            var fit = FitSize();
            if (fit.Width <= 0) { _cx = _cy = 0.5f; return; }
            float w = fit.Width * _zoom, h = fit.Height * _zoom;
            float aw = Math.Max(10, Width - 24f), ah = AreaHeight;
            _cx = w <= aw ? 0.5f : Math.Max(aw / 2f / w, Math.Min(1f - aw / 2f / w, _cx));
            _cy = h <= ah ? 0.5f : Math.Max(ah / 2f / h, Math.Min(1f - ah / 2f / h, _cy));
        }

        public PageView()
        {
            CanFocus = false;
            BackgroundColor = Theme.Current.Stage;
            ToolTip = "Wheel: zoom  ·  Drag: move  ·  Double-click: whole page";
        }

        /// <summary>Paragraphs drawn with a warning outline.</summary>
        public HashSet<DocBlock> Flagged { get; } = new HashSet<DocBlock>();

        public DocBlock Selected { get; private set; }

        public string Caption { get; set; }

        public void SetImage(Bitmap image, DocPage page, float pixelsPerPoint)
        {
            var old = _image;
            _image = image;
            _page = page;
            _pixelsPerPoint = pixelsPerPoint <= 0 ? 1f : pixelsPerPoint;
            _placeholder = string.Empty;
            if (old != null && !ReferenceEquals(old, image)) old.Dispose();
            Invalidate();
        }

        public void SetPlaceholder(string text)
        {
            if (_image != null) { _image.Dispose(); _image = null; }
            _placeholder = text ?? string.Empty;
            Invalidate();
        }

        public void Select(DocBlock block)
        {
            Selected = block;
            Invalidate();
        }

        private RectangleF ImageRect()
        {
            if (_image == null) return RectangleF.Empty;
            var fit = FitSize();
            float w = fit.Width * _zoom, h = fit.Height * _zoom;
            return new RectangleF(Width / 2f - _cx * w, AreaTop + AreaHeight / 2f - _cy * h, w, h);
        }

        /// <summary>A paragraph's box (page points) on screen.</summary>
        private RectangleF ToScreen(Box b, RectangleF rect)
        {
            if (_page == null || _image == null) return RectangleF.Empty;
            float l, t, w, h;
            _page.DisplayRect(b, out l, out t, out w, out h);
            float s = rect.Width / _image.Width * _pixelsPerPoint;
            return new RectangleF(rect.X + l * s, rect.Y + t * s, w * s, h * s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.Current;
            g.AntiAlias = true;
            g.FillRectangle(p.Stage, new RectangleF(0, 0, Width, Height));
            PaintPage(g, p);
            if (!string.IsNullOrEmpty(Caption))
            {
                // the caption as a small chip: "ORIGINAL", "TRANSLATION"
                var font = Theme.UiFont(11f, true);
                var text = Theme.Spaced(Caption);
                float w = font.MeasureString(text).Width + 30;
                var chip = new RectangleF(12, 6, w, 24);
                global::TextX.UI.Controls.ThemedDrawable.FillRound(g, chip, 12, p.Surface);
                g.FillEllipse(Caption.StartsWith("Trans", StringComparison.OrdinalIgnoreCase) ? p.Accent : p.Muted, new RectangleF(chip.X + 10, chip.Y + 8.5f, 7, 7));
                global::TextX.UI.Controls.ThemedDrawable.DrawLine(g, font, p.Text, new RectangleF(chip.X + 21, chip.Y, w - 24, chip.Height), text);
            }
            if (_image != null && _zoom > 1.001f)
            {
                // the zoom, as a small chip at the top right (double-click to see the whole page)
                var font = Theme.UiFont(11f, true);
                var text = Math.Round(_zoom * 100) + " %";
                float w = font.MeasureString(text).Width + 20;
                var chip = new RectangleF(Width - w - 12, 6, w, 24);
                global::TextX.UI.Controls.ThemedDrawable.FillRound(g, chip, 12, p.Surface);
                global::TextX.UI.Controls.ThemedDrawable.DrawLine(g, font, p.Text, new RectangleF(chip.X + 10, chip.Y, w - 14, chip.Height), text);
            }
        }

        private void PaintPage(Graphics g, ThemeColors p)
        {
            if (_image == null)
            {
                if (!string.IsNullOrEmpty(_placeholder))
                {
                    var font = Theme.UiFont(13f);
                    var size = g.MeasureString(font, _placeholder);
                    g.DrawText(font, p.Muted, new PointF((Width - size.Width) / 2f, (Height - size.Height) / 2f), _placeholder);
                }
                return;
            }
            var rect = ImageRect();
            for (int i = 3; i >= 1; i--)
                g.FillRectangle(ThemeColors.WithAlpha(Colors.Black, (p.IsDark ? 0.16f : 0.05f) * (4 - i) / 3f), new RectangleF(rect.X - i + 2, rect.Y - i + 4, rect.Width + 2 * i - 2, rect.Height + 2 * i - 2));
            g.ImageInterpolation = ImageInterpolation.High;
            g.DrawImage(_image, rect);
            foreach (var b in Flagged)
            {
                var r = ToScreen(b.Box, rect);
                r.Inflate(2, 2);
                g.DrawRectangle(new Pen(p.Warning, 1.4f), r);
            }
            if (Selected != null && _page != null && Selected.Page == _page.Index)
            {
                var r = ToScreen(Selected.Box, rect);
                r.Inflate(3, 3);
                global::TextX.UI.Controls.ThemedDrawable.FillRound(g, r, 4, ThemeColors.WithAlpha(p.Accent, 0.16f));
                global::TextX.UI.Controls.ThemedDrawable.StrokeRound(g, r, 4, p.Accent, 2f);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_image == null) return;
            float delta = e.Delta.Height;
            if (delta == 0) return;
            bool scroll = (e.Modifiers & (Keys.Control | Keys.Shift)) != 0;
            if (!scroll)
            {
                ZoomBy(delta > 0 ? 1.25f : 0.8f, e.Location);
                e.Handled = true;
                return;
            }
            if (_zoom <= 1.001f) return;
            var rect = ImageRect();
            float step = 80f * Math.Sign(delta);
            if ((e.Modifiers & Keys.Shift) != 0) _cx -= step / rect.Width;
            else _cy -= step / rect.Height;
            Clamp();
            Invalidate();
            Changed();
            e.Handled = true;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_image == null) return;
            if (_zoom > 1.001f) ZoomToFit();
            else ZoomBy(2f, e.Location);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragFrom == null || _image == null || (e.Buttons & (MouseButtons.Primary | MouseButtons.Middle | MouseButtons.Alternate)) == 0) return;
            float dx = e.Location.X - _dragFrom.Value.X, dy = e.Location.Y - _dragFrom.Value.Y;
            if (!_dragged && Math.Abs(dx) + Math.Abs(dy) < 4) return;
            _dragged = true;
            if (_zoom <= 1.001f) return;
            var rect = ImageRect();
            _cx -= dx / rect.Width;
            _cy -= dy / rect.Height;
            _dragFrom = e.Location;
            Clamp();
            Invalidate();
            Changed();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool click = _dragFrom != null && !_dragged;
            _dragFrom = null;
            if (!click || e.Buttons != MouseButtons.Primary && e.Buttons != MouseButtons.None) return;
            SelectAt(e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _dragFrom = e.Location;
            _dragged = false;
            e.Handled = true;
        }

        private void SelectAt(PointF location)
        {
            if (_page == null || _image == null) return;
            var rect = ImageRect();
            var hit = _page.Blocks
                .Where(b => ToScreen(b.Box, rect).Contains(location) || Inflated(ToScreen(b.Box, rect), 3).Contains(location))
                .OrderBy(b => b.Box.W * b.Box.H).FirstOrDefault();
            if (hit == null) return;
            Select(hit);
            var h = BlockClicked;
            if (h != null) h(this, hit);
        }

        private static RectangleF Inflated(RectangleF r, float d)
        {
            r.Inflate(d, d);
            return r;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Clamp();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null) { _image.Dispose(); _image = null; }
            base.Dispose(disposing);
        }
    }
}
