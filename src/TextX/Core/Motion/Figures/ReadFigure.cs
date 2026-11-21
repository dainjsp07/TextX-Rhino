using System;

namespace TextX.Core.Motion.Figures
{
    /// <summary>
    /// Read: a picture lying on a board with lines of text that are only marks (dashed), and a reading bar
    /// standing across it. The pointer drags the bar along the picture on a spring; behind the bar the
    /// marks have become text (solid strips), in front of it they are still marks. The line at the
    /// pointer's height takes the bright edge; at rest the bar does, two fifths of the way across.
    ///
    /// The pattern: follow a position. One spring for the bar, set from the pointer on the picture's plane,
    /// which never moves; the strips' lengths follow the bar.
    /// </summary>
    public sealed class ReadFigure : Figure
    {
        private const double PW = 150, PD = 104, PT = 3, BT = 4, BAR = 3, BH = 20, X0 = 12;
        private static readonly double[] LineLen = { 108, 124, 92, 118, 70 };
        private const double LY0 = 14, LPITCH = 18, LH = 5;
        private const double RestAt = 0.42;

        private readonly Sample[][] _board, _photo;
        private readonly Spring _bar = new Spring(X0 + (PW - 2 * X0) * RestAt, 100, 18, 0.02);
        private readonly Glow _barGlow = new Glow();
        private readonly Glow[] _glow = new Glow[5];
        private int _row = -2;

        public override string Label { get { return "A picture whose lines become text as the reading bar passes"; } }

        public ReadFigure()
        {
            var c = Iso.Cam(45, 0.5, 1.6);
            Iso.Fit(c, new[] { Iso.W(-8, -8, -BT), Iso.W(PW + 8, -8, -BT), Iso.W(PW + 8, PD + 8, -BT), Iso.W(-8, PD + 8, -BT), Iso.W(0, -6, PT + BH) }, 200, 166);
            Setup(c);
            _board = Iso.Lid(-8, -8, PW + 8, PD + 8, 9, 2);
            _photo = Iso.Lid(0, 0, PW, PD, 4, 3);
            for (int i = 0; i < 5; i++) _glow[i] = new Glow();
            Row(-1, 0);
            _barGlow.Jump();
            foreach (var g in _glow) g.Jump();
            Say();
        }

        public override V2?[] Stops
        {
            get { return new V2?[] { P(30, LY0 + 2, PT), P(PW - 16, LY0 + LPITCH * 2, PT), P(PW * 0.6, LY0 + LPITCH * 4, PT), null }; }
        }

        private void Row(int r, double now)
        {
            if (r == _row) return;
            _row = r;
            for (int i = 0; i < 5; i++) _glow[i].Set(i == r, now);
            _barGlow.Set(r < 0, now);
        }

        /// <summary>How much of the text the bar has read, as a share of all the line lengths.</summary>
        private int ReadPercent()
        {
            double all = 0, read = 0;
            for (int i = 0; i < 5; i++) { all += LineLen[i]; read += Iso.Clamp(_bar.T - X0, 0, LineLen[i]); }
            return (int)Math.Round(100 * read / all / 5) * 5;
        }

        private void Say()
        {
            int pct = ReadPercent();
            Caption = _row < 0 ? "rest  ·  " + pct + "% read" : "reading  ·  " + pct + "% is text now";
        }

        public override void Move(double x, double y, double now)
        {
            var g = Ground(x, y, PT);
            if (g[0] < -12 || g[0] > PW + 12 || g[1] < -12 || g[1] > PD + 12) { Leave(now); return; }
            _bar.T = Iso.Clamp(g[0], X0 - 2, PW - 4);
            int r = (int)Math.Floor((g[1] - LY0 + LPITCH / 2) / LPITCH);
            Row(Math.Max(0, Math.Min(4, r)), now);
            Say();
        }

        public override void Leave(double now)
        {
            _bar.T = X0 + (PW - 2 * X0) * RestAt;
            Row(-1, now);
            Say();
        }

        public override bool Tick(double dt, double now)
        {
            bool m = _bar.Step(dt);
            if (!_barGlow.Done(now)) m = true;
            for (int i = 0; i < 5; i++) if (!_glow[i].Done(now)) m = true;
            return m;
        }

        public override void Draw(Scene sc, double now)
        {
            sc.Solid(Iso.Slab(P, Front, _board[0], _board[1], -BT, 0));
            sc.Solid(Iso.Slab(P, Front, _photo[0], _photo[1], 0, PT));
            double bx = _bar.X;
            // behind the bar: what has been read, as text strips
            for (int i = 0; i < 5; i++)
            {
                double y0 = LY0 + i * LPITCH, x1 = Math.Min(X0 + LineLen[i], bx - BAR / 2 - 1);
                if (x1 - X0 < 2) continue;
                var r = Iso.Rings(X0, y0, x1, y0 + LH, 1.6, 0.5);
                sc.Solid(Iso.Slab(P, Front, r[0], null, PT, PT + 1.6), _glow[i].Value(now));
            }
            // the bar: a thin upright plate across the picture
            var bar = Iso.Rings(bx - BAR / 2, -6, bx + BAR / 2, PD + 6, 1.4, 0.6);
            sc.Solid(Iso.Prism(P, Front, bar[0], bar[1], PT, PT + BH), _barGlow.Value(now));
            // in front of the bar: marks not read yet
            for (int i = 0; i < 5; i++)
            {
                double y = LY0 + i * LPITCH + LH / 2, xa = Math.Max(X0, bx + BAR / 2 + 1), xb = X0 + LineLen[i];
                if (xb - xa < 2) continue;
                sc.Line(new[] { P(xa, y, PT), P(xb, y, PT) }, Ink.Mid, true);
            }
        }
    }
}
