using System;
using Eto.Drawing;
using TextX.UI.Controls;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Studio
{
    /// <summary>
    /// The picture on the Home hero card: an orange sun with the translate mark and what goes in,
    /// drawn in the card's right part (behind its text, which keeps to the left).
    /// </summary>
    public static class HeroArt
    {
        public static void Draw(Graphics g, RectangleF area)
        {
            var p = Theme.Current;
            float size = Math.Min(area.Width, area.Height);
            if (size < 80) return;
            g.AntiAlias = true;
            float r = Math.Min(size * 0.27f, 92f);
            float cx = area.Right - r * 1.85f, cy = area.Y + area.Height * 0.5f;

            // orbits
            using (var pen = new Pen(ThemeColors.WithAlpha(p.Accent, 0.5f), 1.2f))
            {
                pen.DashStyle = new DashStyle(0, new float[] { 2f, 4f });
                g.DrawEllipse(pen, new RectangleF(cx - r * 1.6f, cy - r * 1.6f, r * 3.2f, r * 3.2f));
            }
            using (var pen = new Pen(ThemeColors.WithAlpha(p.OnHero, 0.12f), 1f))
                g.DrawEllipse(pen, new RectangleF(cx - r * 1.22f, cy - r * 1.22f, r * 2.44f, r * 2.44f));

            // the sun
            g.FillEllipse(p.Accent, new RectangleF(cx - r, cy - r, 2 * r, 2 * r));
            Icons.Draw(g, IconKind.Translate, new RectangleF(cx - r * 0.55f, cy - r * 0.55f, r * 1.1f, r * 1.1f), p.OnAccent, 2.2f);

            // what goes in: chips on the outer orbit
            Chip(g, p, "PDF", cx + (float)Math.Cos(-2.3) * r * 1.6f, cy + (float)Math.Sin(-2.3) * r * 1.6f);
            Chip(g, p, "Drawing", cx + (float)Math.Cos(-0.25) * r * 1.6f, cy + (float)Math.Sin(-0.25) * r * 1.6f);
            Chip(g, p, "Picture", cx + (float)Math.Cos(2.25) * r * 1.6f, cy + (float)Math.Sin(2.25) * r * 1.6f);

            // sparkles
            Icons.Draw(g, IconKind.Sparkle, new RectangleF(cx + r * 0.95f, cy + r * 0.85f, 16, 16), p.Accent, 1.6f);
            Icons.Draw(g, IconKind.Sparkle, new RectangleF(cx - r * 1.4f, cy - r * 0.2f, 11, 11), ThemeColors.WithAlpha(p.OnHero, 0.6f), 1.5f);
        }

        private static void Chip(Graphics g, ThemeColors p, string text, float cx, float cy)
        {
            var font = Theme.UiFont(11.5f, true);
            float w = ThemedDrawable.Measure(font, text).Width + 30, h = 26;
            var r = new RectangleF(cx - w / 2f, cy - h / 2f, w, h);
            ThemedDrawable.FillRound(g, r, h / 2f, p.IsDark ? p.Window : p.OnHero);
            g.FillEllipse(p.Accent, new RectangleF(r.X + 10, r.Y + (h - 7) / 2f, 7, 7));
            ThemedDrawable.DrawLine(g, font, p.IsDark ? p.Text : p.Hero, new RectangleF(r.X + 21, r.Y, w - 25, h), text);
        }
    }
}
