using System;
using Eto.Drawing;

namespace TextX.UI.Theming
{
    public enum IconKind
    {
        None,
        Translate,
        Text,
        Leader,
        Format,
        Print,
        Swap,
        Gear,
        Chevron,
        Pointer,
        Plus,
        Refresh,
        Check,
        Target,
        Layers,
        File,
        Eye,
        Undo,
        Info,
        Download,
        Close,
    }

    /// <summary>
    /// Line icons drawn as vector paths on a 24 x 24 grid (the same drawings as the approved design),
    /// so they stay sharp at any display scale and take the theme colour.
    /// </summary>
    public static class Icons
    {
        public static void Draw(Graphics g, IconKind kind, RectangleF rect, Color color, float strokePx = 2f)
        {
            if (kind == IconKind.None) return;
            float s = Math.Min(rect.Width, rect.Height) / 24f;
            float ox = rect.X + (rect.Width - 24f * s) / 2f;
            float oy = rect.Y + (rect.Height - 24f * s) / 2f;
            Func<float, float, PointF> P = (x, y) => new PointF(ox + x * s, oy + y * s);

            var path = new GraphicsPath();
            Action<float[]> poly = pts =>
            {
                path.StartFigure();
                path.MoveTo(P(pts[0], pts[1]).X, P(pts[0], pts[1]).Y);
                for (int i = 2; i < pts.Length; i += 2)
                {
                    var p = P(pts[i], pts[i + 1]);
                    path.LineTo(p.X, p.Y);
                }
            };
            Action<float[]> closedPoly = pts =>
            {
                poly(pts);
                path.CloseFigure();
            };
            Action<float, float, float, float, float, float, float, float> bez = (x0, y0, x1, y1, x2, y2, x3, y3) =>
            {
                path.StartFigure();
                path.AddBezier(P(x0, y0), P(x1, y1), P(x2, y2), P(x3, y3));
            };
            Action<float, float, float> circle = (cx, cy, r) =>
            {
                path.StartFigure();
                var tl = P(cx - r, cy - r);
                path.AddEllipse(tl.X, tl.Y, 2 * r * s, 2 * r * s);
            };

            switch (kind)
            {
                case IconKind.Translate:
                    poly(new float[] { 4, 5, 12, 5 });
                    poly(new float[] { 8, 3, 8, 5 });
                    bez(10, 5, 9.5f, 8, 7.5f, 11, 4, 13);
                    bez(6, 9, 7, 11, 9, 12.5f, 11, 13);
                    poly(new float[] { 13, 21, 17, 11, 21, 21 });
                    poly(new float[] { 14.5f, 17, 19.5f, 17 });
                    break;
                case IconKind.Text:
                    poly(new float[] { 5, 6, 5, 4, 19, 4, 19, 6 });
                    poly(new float[] { 12, 4, 12, 20 });
                    poly(new float[] { 9, 20, 15, 20 });
                    break;
                case IconKind.Leader:
                    poly(new float[] { 4, 20, 14, 10, 20, 10 });
                    poly(new float[] { 5, 15, 4, 20, 9, 19 });
                    break;
                case IconKind.Format:
                    poly(new float[] { 4, 21, 4, 14 }); poly(new float[] { 4, 10, 4, 3 });
                    poly(new float[] { 12, 21, 12, 12 }); poly(new float[] { 12, 8, 12, 3 });
                    poly(new float[] { 20, 21, 20, 16 }); poly(new float[] { 20, 12, 20, 3 });
                    poly(new float[] { 1, 14, 7, 14 }); poly(new float[] { 9, 8, 15, 8 }); poly(new float[] { 17, 16, 23, 16 });
                    break;
                case IconKind.Print:
                    poly(new float[] { 6, 9, 6, 3, 18, 3, 18, 9 });
                    poly(new float[] { 6, 18, 3.5f, 18, 2, 16.5f, 2, 10.5f, 3.5f, 9, 20.5f, 9, 22, 10.5f, 22, 16.5f, 20.5f, 18, 18, 18 });
                    closedPoly(new float[] { 6, 14, 18, 14, 18, 21, 6, 21 });
                    break;
                case IconKind.Swap:
                    poly(new float[] { 7, 7, 19, 7, 16, 4 });
                    poly(new float[] { 17, 17, 5, 17, 8, 20 });
                    break;
                case IconKind.Gear:
                    circle(12, 12, 3);
                    circle(12, 12, 6.8f);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        float c = (float)Math.Cos(a), n = (float)Math.Sin(a);
                        poly(new[] { 12 + 6.8f * c, 12 + 6.8f * n, 12 + 9.6f * c, 12 + 9.6f * n });
                    }
                    break;
                case IconKind.Chevron:
                    poly(new float[] { 6, 9, 12, 15, 18, 9 });
                    break;
                case IconKind.Pointer:
                    closedPoly(new float[] { 4, 4, 11, 21, 13.5f, 13.5f, 21, 11 });
                    break;
                case IconKind.Plus:
                    poly(new float[] { 12, 5, 12, 19 });
                    poly(new float[] { 5, 12, 19, 12 });
                    break;
                case IconKind.Refresh:
                    {
                        path.StartFigure();
                        var tl = P(3, 3);
                        path.AddArc(tl.X, tl.Y, 18 * s, 18 * s, 0, 315);
                        poly(new float[] { 21, 3, 21, 8.5f, 15.5f, 8.5f });
                    }
                    break;
                case IconKind.Check:
                    poly(new float[] { 5, 12, 10, 17, 20, 7 });
                    break;
                case IconKind.Target:
                    poly(new float[] { 12, 2, 12, 6 }); poly(new float[] { 12, 18, 12, 22 });
                    poly(new float[] { 2, 12, 6, 12 }); poly(new float[] { 18, 12, 22, 12 });
                    circle(12, 12, 4);
                    break;
                case IconKind.Layers:
                    closedPoly(new float[] { 12, 2, 2, 7, 12, 12, 22, 7 });
                    poly(new float[] { 2, 17, 12, 22, 22, 17 });
                    poly(new float[] { 2, 12, 12, 17, 22, 12 });
                    break;
                case IconKind.File:
                    closedPoly(new float[] { 14, 3, 6, 3, 4, 5, 4, 19, 6, 21, 18, 21, 20, 19, 20, 9 });
                    poly(new float[] { 14, 3, 14, 9, 20, 9 });
                    break;
                case IconKind.Eye:
                    bez(2, 12, 5, 5.5f, 19, 5.5f, 22, 12);
                    bez(22, 12, 19, 18.5f, 5, 18.5f, 2, 12);
                    circle(12, 12, 3);
                    break;
                case IconKind.Undo:
                    poly(new float[] { 3, 7, 3, 13, 9, 13 });
                    bez(3, 13, 7, 5.5f, 18, 6, 21, 17);
                    break;
                case IconKind.Info:
                    circle(12, 12, 9.5f);
                    poly(new float[] { 12, 11, 12, 16.5f });
                    poly(new float[] { 12, 7.6f, 12, 7.8f });
                    break;
                case IconKind.Download:
                    poly(new float[] { 12, 3, 12, 15 });
                    poly(new float[] { 7, 10, 12, 15, 17, 10 });
                    poly(new float[] { 5, 21, 19, 21 });
                    break;
                case IconKind.Close:
                    poly(new float[] { 6, 6, 18, 18 });
                    poly(new float[] { 18, 6, 6, 18 });
                    break;
            }

            using (var pen = new Pen(color, Math.Max(1f, strokePx * s)))
            {
                pen.LineCap = PenLineCap.Round;
                pen.LineJoin = PenLineJoin.Round;
                bool aa = g.AntiAlias;
                g.AntiAlias = true;
                g.DrawPath(pen, path);
                g.AntiAlias = aa;
            }
            path.Dispose();
        }

        /// <summary>The Text X window / package icon: the translate symbol on a deep-navy tile.</summary>
        /// <summary>
        /// Gives a window the Text X title-bar icon. The icon is built from PNG data: on Rhino's WPF
        /// platform an Eto icon made directly from a bitmap has no native image, so the window would
        /// fall back to Rhino's own icon.
        /// </summary>
        public static void ApplyWindowIcon(Eto.Forms.Window window)
        {
            if (window == null) return;
            try
            {
                using (var bmp = CreateAppIcon(64))
                using (var ms = new System.IO.MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    window.Icon = new Icon(ms);
                }
            }
            catch (Exception ex)
            {
                TextX.Integration.Diagnostics.Log("window icon: " + ex.Message);
            }
        }

        public static Bitmap CreateAppIcon(int size)
        {
            var p = ThemeColors.GraphiteDeepNavy();
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppRgba);
            using (var g = new Graphics(bmp))
            {
                g.AntiAlias = true;
                float r = size * 0.22f;
                var rect = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
                using (var tile = GraphicsPath.GetRoundRect(rect, r))
                {
                    g.FillPath(p.Accent, tile);
                    g.DrawPath(ThemeColors.WithAlpha(p.AccentInk, 0.55f), tile);
                }
                float inset = size * 0.18f;
                Draw(g, IconKind.Translate, new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset), p.AccentInk,
                    size >= 48 ? 2.2f : 2.6f);
            }
            return bmp;
        }
    }
}
