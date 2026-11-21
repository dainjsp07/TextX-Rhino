using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using TextX.Core.Motion;
using TextX.UI.Theming;

namespace TextX.UI.Motion
{
    /// <summary>The five inks of a figure on one background.</summary>
    public sealed class FigureInks
    {
        public Color Plate, Lo, Mid, Edge, Hi;

        /// <summary>On a card or the window: warm greys, and the accent as the one highlight.</summary>
        public static FigureInks On(ThemeColors p, Color plate)
        {
            return new FigureInks
            {
                Plate = plate,
                Lo = ThemeColors.Mix(plate, p.Muted, p.IsDark ? 0.28f : 0.22f),
                Mid = ThemeColors.Mix(plate, p.Muted, p.IsDark ? 0.5f : 0.45f),
                Edge = ThemeColors.Mix(plate, p.Text, p.IsDark ? 0.5f : 0.42f),
                Hi = p.IsDark ? p.AccentInk : p.Accent,
            };
        }

        /// <summary>On the orange tile: the ink colour is the highlight.</summary>
        public static FigureInks OnAccent(ThemeColors p)
        {
            var plate = p.Accent;
            return new FigureInks
            {
                Plate = plate,
                Lo = ThemeColors.Mix(plate, p.OnAccent, 0.28f),
                Mid = ThemeColors.Mix(plate, p.OnAccent, 0.42f),
                Edge = ThemeColors.Mix(plate, p.OnAccent, 0.62f),
                Hi = p.OnAccent,
            };
        }

        public Color Of(Ink k)
        {
            switch (k)
            {
                case Ink.Lo: return Lo;
                case Ink.Mid: return Mid;
                case Ink.Edge: return Edge;
                case Ink.Hi: return Hi;
                default: return Colors.Transparent;
            }
        }
    }

    /// <summary>Paints a figure's scene into a rectangle with hairline strokes (1.1 px at any size).</summary>
    public static class FigurePainter
    {
        private static readonly Dictionary<Type, RectangleF> Bounds = new Dictionary<Type, RectangleF>();

        /// <summary>
        /// The part of the 400 x 320 view box a figure uses in any pose (rest and every tour stop), so a
        /// small tile can show it large without anything leaving the tile.
        /// </summary>
        public static RectangleF ContentBox(Figure f)
        {
            RectangleF box;
            var type = f.GetType();
            if (Bounds.TryGetValue(type, out box)) return box;
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            try
            {
                var probe = (Figure)Activator.CreateInstance(type);
                var scene = new Scene();
                Action add = () =>
                {
                    foreach (var q in Points(scene)) { x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y); }
                    scene.Clear();
                };
                double now = 0;
                probe.Draw(scene, now);
                add();
                foreach (var stop in probe.Stops.Where(s => s != null))
                {
                    probe.Move(stop.Value.X, stop.Value.Y, now);
                    for (int i = 0; i < 150; i++) { now += 1000 / 60.0; probe.Tick(1 / 60.0, now); }
                    probe.Draw(scene, now);
                    add();
                }
            }
            catch (Exception)
            {
                x0 = 0; y0 = 0; x1 = Iso.Width; y1 = Iso.Height;
            }
            if (x1 <= x0) { x0 = 0; y0 = 0; x1 = Iso.Width; y1 = Iso.Height; }
            box = new RectangleF(x0 - 6, y0 - 6, x1 - x0 + 12, y1 - y0 + 12);
            Bounds[type] = box;
            return box;
        }

        private static IEnumerable<V2> Points(Scene s)
        {
            foreach (var sh in s.Shapes)
            {
                if (sh.IsDot) { yield return sh.Center; continue; }
                foreach (var poly in sh.Closed) foreach (var q in poly) yield return q;
                foreach (var poly in sh.Open) foreach (var q in poly) yield return q;
            }
        }

        /// <summary>Where the figure lands in rect: the scale and offset from view-box units to the control.</summary>
        public static void Transform(Figure f, RectangleF rect, out float scale, out float ox, out float oy)
        {
            var box = ContentBox(f);
            scale = Math.Min(rect.Width / box.Width, rect.Height / box.Height);
            ox = rect.X + (rect.Width - box.Width * scale) / 2f - box.X * scale;
            oy = rect.Y + (rect.Height - box.Height * scale) / 2f - box.Y * scale;
        }

        /// <summary>From control coordinates back to the figure's view box (for the pointer).</summary>
        public static PointF ToViewBox(Figure f, RectangleF rect, PointF pt)
        {
            float s, ox, oy;
            Transform(f, rect, out s, out ox, out oy);
            return new PointF((pt.X - ox) / s, (pt.Y - oy) / s);
        }

        public static void Paint(Graphics g, Figure f, Scene scene, RectangleF rect, FigureInks inks, double now)
        {
            float s, ox, oy;
            Transform(f, rect, out s, out ox, out oy);
            scene.Clear();
            f.Draw(scene, now);
            bool aa = g.AntiAlias;
            g.AntiAlias = true;
            Func<V2, PointF> T = q => new PointF(ox + q.X * s, oy + q.Y * s);
            foreach (var sh in scene.Shapes)
            {
                var stroke = sh.Bright > 0 ? ThemeColors.Mix(inks.Of(sh.Stroke == Ink.None ? Ink.Edge : sh.Stroke), inks.Hi, sh.Bright) : inks.Of(sh.Stroke);
                if (sh.IsDot)
                {
                    var c = T(sh.Center);
                    float rx = sh.Rx * s, ry = sh.Ry * s;
                    g.FillEllipse(stroke, new RectangleF(c.X - rx, c.Y - ry, 2 * rx, 2 * ry));
                    continue;
                }
                using (var path = new GraphicsPath())
                {
                    foreach (var poly in sh.Closed)
                    {
                        if (poly.Length < 2) continue;
                        path.StartFigure();
                        path.AddLines(poly.Select(T));
                        path.CloseFigure();
                    }
                    bool hasClosed = sh.Closed.Count > 0;
                    if (hasClosed && sh.Fill) g.FillPath(inks.Plate, path);
                    foreach (var poly in sh.Open)
                    {
                        if (poly.Length < 2) continue;
                        path.StartFigure();
                        path.AddLines(poly.Select(T));
                    }
                    if (stroke.A <= 0) continue;
                    using (var pen = new Pen(stroke, sh.Bright > 0.5f ? 1.35f : 1.1f))
                    {
                        pen.LineJoin = PenLineJoin.Round;
                        pen.LineCap = PenLineCap.Round;
                        if (sh.Dash) pen.DashStyle = new DashStyle(0, new[] { 1f, 3f });
                        g.DrawPath(pen, path);
                    }
                }
            }
            g.AntiAlias = aa;
        }
    }
}
