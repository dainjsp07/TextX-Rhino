using System;

namespace TextX.Core.Motion.Figures
{
    /// <summary>
    /// Voice: a row of bars on a plinth, a voice drawn as its level, and in front of it a strip of text
    /// that grows as the voice goes on. The pointer is the voice: the bars near it rise on springs with a
    /// clamped falloff, and the text strip runs out to where the pointer is. Bars above half height take
    /// the bright edge. At rest the bars hold a spoken phrase, two rises, and the strip is short.
    ///
    /// The pattern: a falloff in one dimension (Terrain's, in a line), each bar on its own spring, the
    /// pointer read on the ground plane.
    /// </summary>
    public sealed class VoiceFigure : Figure
    {
        private const int N = 12;
        private const double CELL = 12, FOOT = 8.5, HMAX = 40, PB = 5, R = 30, TY0 = 24, TY1 = 30;

        private readonly Spring[] _h = new Spring[N];
        private readonly double[] _rest = new double[N];
        private readonly Sample[][][] _bars = new Sample[N][][];
        private readonly Sample[][] _plinth;
        private readonly Spring _text = new Spring(34, 100, 18, 0.05);
        private readonly Glow[] _glow = new Glow[N];
        private bool _over;

        public override string Label { get { return "A voice as bars, and the text it becomes"; } }

        /// <summary>1 at the pointer, .31 at 42% of the reach, .09 at the edge and beyond.</summary>
        private static double Falloff(double u)
        {
            return u <= 0 ? 1 : u <= 0.417 ? 1 - u / 0.417 * 0.6875 : u <= 1 ? 0.3125 - (u - 0.417) / 0.583 * 0.2185 : 0.094;
        }

        public VoiceFigure()
        {
            double ext = N * CELL;
            var c = Iso.Cam(45, 0.5, 1.9);
            Iso.Fit(c, new[] { Iso.W(-6, -6, -PB), Iso.W(ext + 6, -6, -PB), Iso.W(ext + 6, TY1 + 6, -PB), Iso.W(-6, TY1 + 6, -PB), Iso.W(0, 4, HMAX), Iso.W(ext, 4, HMAX) }, 200, 166);
            Setup(c);
            _plinth = Iso.Rings(-6, -6, ext + 6, TY1 + 6, 7, 2.2);
            for (int i = 0; i < N; i++)
            {
                double u = (double)i / (N - 1);
                _rest[i] = 4 + 20 * Math.Exp(-Math.Pow(u - 0.28, 2) / 0.02) + 11 * Math.Exp(-Math.Pow(u - 0.72, 2) / 0.012);
                double x0 = i * CELL + (CELL - FOOT) / 2;
                _bars[i] = Iso.Rings(x0, 4, x0 + FOOT, 4 + FOOT, 2.6, 0.9);
                _h[i] = new Spring(_rest[i], 100, 18, 0.04);
                _glow[i] = new Glow();
            }
            Caption = "rest  ·  speak, see text";
            Glows(0);
            foreach (var g in _glow) g.Jump();
        }

        public override V2?[] Stops
        {
            get { return new V2?[] { P(CELL * 2, 8, 0), P(CELL * 7, 8, 0), P(CELL * 11, 8, 0), null }; }
        }

        private void Glows(double now)
        {
            for (int i = 0; i < N; i++) _glow[i].Set(_h[i].T > HMAX * 0.5, now);
        }

        public override void Move(double x, double y, double now)
        {
            var g = Ground(x, y, 0);
            if (g[0] < -14 || g[0] > N * CELL + 14 || g[1] < -30 || g[1] > TY1 + 30) { Leave(now); return; }
            _over = true;
            for (int i = 0; i < N; i++)
            {
                double cx = (i + 0.5) * CELL;
                _h[i].T = Math.Max(3, HMAX * Falloff(Math.Abs(cx - g[0]) / R));
            }
            _text.T = Iso.Clamp(g[0], 10, N * CELL - 2);
            Glows(now);
            int words = 1 + (int)Math.Floor((_text.T - 10) / 28);
            Caption = "speaking  ·  " + words + (words == 1 ? " word" : " words") + " typed";
        }

        public override void Leave(double now)
        {
            if (!_over && Caption != null) return;
            _over = false;
            for (int i = 0; i < N; i++) _h[i].T = _rest[i];
            _text.T = 34;
            Glows(now);
            Caption = "rest  ·  speak, see text";
        }

        public override bool Tick(double dt, double now)
        {
            bool m = _text.Step(dt);
            for (int i = 0; i < N; i++)
            {
                if (_h[i].Step(dt)) m = true;
                if (!_glow[i].Done(now)) m = true;
            }
            return m;
        }

        public override void Draw(Scene sc, double now)
        {
            sc.Solid(Iso.Prism(P, Front, _plinth[0], _plinth[1], -PB, 0));
            for (int i = 0; i < N; i++)
                sc.Solid(Iso.Prism(P, Front, _bars[i][0], _bars[i][1], 0, Math.Max(0.6, _h[i].X)), _glow[i].Value(now));
            // the text the voice became: a strip as long as what has been said, then marks still to come
            double x1 = _text.X;
            var strip = Iso.Rings(4, TY0, Math.Max(8, x1), TY1, 1.8, 0.6);
            sc.Solid(Iso.Slab(P, Front, strip[0], null, 0, 1.8));
            if (x1 + 4 < N * CELL - 2) sc.Line(new[] { P(x1 + 4, (TY0 + TY1) / 2, 0), P(N * CELL - 2, (TY0 + TY1) / 2, 0) }, Ink.Lo, true);
        }
    }
}
