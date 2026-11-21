using System;

// The arrangement and motion of Hairline's Stack figure (MIT, https://github.com/lucasmarkes/hairline), as documents.

namespace TextX.Core.Motion.Figures
{
    /// <summary>
    /// Library: five documents laid one over another on a base plate, each a thin slab with a quiet inset
    /// rim. The pointer's height picks a document: the ones above it lift away on the discrete curve, the
    /// top one furthest, so the chosen one opens to the eye and takes the bright edge. At rest the stack
    /// is closed with the top document, the latest, bright.
    ///
    /// The pattern: discrete items, a stagger by distance, a hit test on static bands of the resting stack.
    /// </summary>
    public sealed class LibraryFigure : Figure
    {
        private const int N = 5;
        private const double T = 4, PITCH = 9, Z0 = 7, FW = 56, LIFTMAX = 30, BT = 4, HYS = 5;
        private const double OPEN = 700, CLOSE = 1250, STEP = 55, CSTEP = 45;
        private static readonly double[][] Off = { new[] { -1.5, 1.0 }, new[] { 1.5, -1.2 }, new[] { -0.5, -1.8 }, new[] { 2, 1.2 }, new[] { 0.0, 0 } };

        private readonly Sample[][] _base, _recess;
        private readonly Sample[][] _ring = new Sample[N][], _rim = new Sample[N][];
        private readonly Tween[] _tw = new Tween[N];
        private readonly Glow[] _glow = new Glow[N];
        private readonly double[] _mid = new double[N];
        private double _xa, _xb, _ya, _yb;
        private int _act = -2;

        public override string Label { get { return "Your documents, the latest on top"; } }

        public LibraryFigure()
        {
            var c = Iso.Cam(45, 0.5, 2.2);
            double ztop = Z0 + (N - 1) * PITCH + T;
            Iso.Fit(c, new[] { Iso.W(-8, -8, 0), Iso.W(64, 64, 0), Iso.W(-8, -8, ztop + 46), Iso.W(64, 64, ztop + 46) }, 200, 166);
            Setup(c);
            _base = Iso.Lid(-8, -8, 64, 64, 7, 2);
            _recess = Iso.Rings(4, 4, 52, 52, 4, 1);
            for (int j = 0; j < N; j++)
            {
                double x0 = Off[j][0], y0 = Off[j][1];
                _ring[j] = Iso.Lid(x0, y0, x0 + FW, y0 + FW, 5, 1.1)[0];
                _rim[j] = Iso.Lid(x0 + 4, y0 + 4, x0 + FW - 4, y0 + FW - 4, 2.5, 0.6)[0];
                _tw[j] = new Tween(0);
                _glow[j] = new Glow();
                _mid[j] = P(FW / 2 + Off[j][0], FW - 9 + Off[j][1], Z0 + j * PITCH + T).Y;
            }
            var foot = Iso.RingAt(P, _base[0], 0);
            _xa = 1e9; _xb = -1e9; _yb = -1e9;
            foreach (var q in foot) { _xa = Math.Min(_xa, q.X); _xb = Math.Max(_xb, q.X); _yb = Math.Max(_yb, q.Y); }
            _xa += 4; _xb -= 4;
            _ya = _mid[N - 1] - 150;
            Over(-1, 0);
            for (int j = 0; j < N; j++) { _tw[j].Jump(); _glow[j].Jump(); }
        }

        public override V2?[] Stops
        {
            get { return new V2?[] { new V2(200, (float)_mid[1]), new V2(200, (float)_mid[3]), new V2(200, (float)_mid[0]), null }; }
        }

        /// <summary>How far document j stands above its place when document a is chosen: the top one furthest.</summary>
        private static double Up(int j, int a)
        {
            return j <= a ? 0 : LIFTMAX * (0.4 + 0.6 * (j - a) / (N - 1 - a));
        }

        private int Hit(double x, double y)
        {
            if (x < _xa || x > _xb || y > _yb || y < _ya) return -1;
            if (_act >= 0)
            {
                double lo = _act < N - 1 ? (_mid[_act] + _mid[_act + 1]) / 2 : -1e9, hi = _act > 0 ? (_mid[_act - 1] + _mid[_act]) / 2 : 1e9;
                if (y >= lo - HYS && y <= hi + HYS) return _act;
            }
            int a = 0;
            while (a < N - 1 && y < (_mid[a] + _mid[a + 1]) / 2) a++;
            return a;
        }

        private void Over(int a, double now)
        {
            if (a == _act) return;
            int from = a < 0 ? (_act < 0 ? N - 1 : _act) : a;
            _act = a;
            int s = a < 0 ? N - 1 : a;
            for (int j = 0; j < N; j++)
            {
                double to = a < 0 ? 0 : Up(j, a), cur = _tw[j].Value(now);
                if (to != _tw[j].To)
                {
                    bool closing = to < cur;
                    _tw[j].Set(to, now, closing ? (a < 0 ? j * CSTEP : Math.Abs(j - from) * 30) : Math.Abs(j - from) * STEP);
                    _tw[j].Dur = closing ? CLOSE : OPEN;
                }
                _glow[j].Set(j == s, now);
            }
            Caption = a < 0 ? "rest  ·  the latest on top" : "document " + (N - a) + " of " + N;
        }

        public override void Move(double x, double y, double now) { Over(Hit(x, y), now); }

        public override void Leave(double now) { Over(-1, now); }

        public override bool Tick(double dt, double now)
        {
            for (int j = 0; j < N; j++) if (!_tw[j].Done(now) || !_glow[j].Done(now)) return true;
            return false;
        }

        public override void Draw(Scene sc, double now)
        {
            sc.Solid(Iso.Slab(P, Front, _base[0], _base[1], 0, BT));
            sc.Outline(Iso.RingAt(P, _recess[0], BT), Ink.Lo, false);
            for (int j = 0; j < N; j++)
            {
                double z = Z0 + j * PITCH + _tw[j].Value(now);
                var s = Iso.Slab(P, Front, _ring[j], _rim[j], z, z + T);
                sc.Solid(s, _glow[j].Value(now));
            }
        }
    }
}
