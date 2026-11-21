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
    /// </summary>
    public sealed class PageView : Drawable
    {
        private Bitmap _image;
        private DocPage _page;
        private float _pixelsPerPoint = 1f;
        private string _placeholder = string.Empty;

        public event EventHandler<DocBlock> BlockClicked;

        public PageView()
        {
            CanFocus = false;
            BackgroundColor = Theme.Current.Stage;
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
            float pad = 12f, top = string.IsNullOrEmpty(Caption) ? pad : 40f;
            float aw = Math.Max(10, Width - 2 * pad), ah = Math.Max(10, Height - top - pad);
            float s = Math.Min(aw / _image.Width, ah / _image.Height);
            float w = _image.Width * s, h = _image.Height * s;
            return new RectangleF((Width - w) / 2f, top + (ah - h) / 2f, w, h);
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
            if (!string.IsNullOrEmpty(Caption))
            {
                // the caption as a small chip: "ORIGINAL", "TRANSLATION"
                var font = Theme.UiFont(10.5f, true);
                var text = Theme.Spaced(Caption);
                float w = font.MeasureString(text).Width + 30;
                var chip = new RectangleF(12, 6, w, 24);
                global::TextX.UI.Controls.ThemedDrawable.FillRound(g, chip, 12, p.Surface);
                g.FillEllipse(Caption.StartsWith("Trans", StringComparison.OrdinalIgnoreCase) ? p.Accent : p.Muted, new RectangleF(chip.X + 10, chip.Y + 8.5f, 7, 7));
                global::TextX.UI.Controls.ThemedDrawable.DrawLine(g, font, p.Text, new RectangleF(chip.X + 21, chip.Y, w - 24, chip.Height), text);
            }
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_page == null || _image == null) return;
            var rect = ImageRect();
            var hit = _page.Blocks
                .Where(b => ToScreen(b.Box, rect).Contains(e.Location) || Inflated(ToScreen(b.Box, rect), 3).Contains(e.Location))
                .OrderBy(b => b.Box.W * b.Box.H).FirstOrDefault();
            if (hit == null) return;
            Select(hit);
            var h = BlockClicked;
            if (h != null) h(this, hit);
            e.Handled = true;
        }

        private static RectangleF Inflated(RectangleF r, float d)
        {
            r.Inflate(d, d);
            return r;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null) { _image.Dispose(); _image = null; }
            base.Dispose(disposing);
        }
    }
}
