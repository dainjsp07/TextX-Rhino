using System;
using Eto.Drawing;
using TextX.UI.Controls;
using TextX.UI.Theming;
using Theme = TextX.UI.Theming.Theme;

namespace TextX.UI.Studio
{
    /// <summary>The picture on the Home hero card: an orange sun with the translate mark and what goes in.</summary>
    public sealed class HeroArt : ThemedDrawable
    {
        public HeroArt() : base(false)
        {
            Backdrop = p => p.Hero;
        }

        protected override void OnDraw(Graphics g)
        {
            var p = P;
            float size = Math.Min(Width, Height);
            if (size < 60) return;
            float cx = Width / 2f, cy = Height / 2f - 6;
            float r = size * 0.30f;

            // orbit
            using (var pen = new Pen(ThemeColors.WithAlpha(p.Accent, 0.45f), 1.2f))
            {
                pen.DashStyle = new DashStyle(0, new float[] { 2f, 4f });
                g.DrawEllipse(pen, new RectangleF(cx - r * 1.55f, cy - r * 1.55f, r * 3.1f, r * 3.1f));
            }
            using (var pen = new Pen(ThemeColors.WithAlpha(p.OnHero, 0.12f), 1f))
                g.DrawEllipse(pen, new RectangleF(cx - r * 1.2f, cy - r * 1.2f, r * 2.4f, r * 2.4f));

            // the sun
            g.FillEllipse(p.Accent, new RectangleF(cx - r, cy - r, 2 * r, 2 * r));
            Icons.Draw(g, IconKind.Translate, new RectangleF(cx - r * 0.55f, cy - r * 0.55f, r * 1.1f, r * 1.1f), p.OnAccent, 2.2f);

            // what goes in: chips on the orbit
            Chip(g, "PDF", cx + (float)Math.Cos(-2.4) * r * 1.55f, cy + (float)Math.Sin(-2.4) * r * 1.55f);
            Chip(g, "Drawing", cx + (float)Math.Cos(-0.35) * r * 1.55f, cy + (float)Math.Sin(-0.35) * r * 1.55f);
            Chip(g, "Picture", cx + (float)Math.Cos(2.2) * r * 1.55f, cy + (float)Math.Sin(2.2) * r * 1.55f);

            // sparkles
            Icons.Draw(g, IconKind.Sparkle, new RectangleF(cx + r * 0.95f, cy + r * 0.9f, 16, 16), p.Accent, 1.6f);
            Icons.Draw(g, IconKind.Sparkle, new RectangleF(cx - r * 1.35f, cy - r * 0.15f, 11, 11), ThemeColors.WithAlpha(p.OnHero, 0.6f), 1.5f);
        }

        private void Chip(Graphics g, string text, float cx, float cy)
        {
            var p = P;
            var font = Theme.UiFont(11.5f, true);
            float w = Measure(font, text).Width + 30, h = 26;
            var r = new RectangleF(cx - w / 2f, cy - h / 2f, w, h);
            FillRound(g, r, h / 2f, p.IsDark ? p.Window : p.OnHero);
            g.FillEllipse(p.Accent, new RectangleF(r.X + 10, r.Y + (h - 7) / 2f, 7, 7));
            DrawLine(g, font, p.IsDark ? p.Text : p.Hero, new RectangleF(r.X + 21, r.Y, w - 25, h), text);
        }
    }
}
