using System;

namespace TextX.Core.Motion.Figures
{
    /// <summary>
    /// Schedule: a drawing with three cabinets on it, each tagged with dots on its lid, and beside it a
    /// schedule tray with one row per cabinet, tagged the same way. The pointer picks a cabinet or a row:
    /// the cabinet lifts off the drawing and its row slides out of the tray, the near one first, and both
    /// take the bright edge. At rest the first row is half out and bright.
    ///
    /// The pattern: discrete pairs (tweens), a stagger by distance, a hit test on the resting footprints.
    /// </summary>
    public sealed class ScheduleFigure : Figure
    {
        private const double DW = 100, DD = 96, PT = 2, BT = 4, TX = 118, TW = 74, TT = 4, LIFT = 10, OUT = 16;
        // cabinets on the drawing: x0, y0, x1, y1, height
        private static readonly double[][] Items =
        {
            new double[] { 10, 12, 36, 32, 14 },
            new double[] { 50, 10, 70, 44, 22 },
            new double[] { 70, 56, 90, 84, 10 },
        };
        private const double RowY0 = 12, RowH = 16, RowGap = 9;

        private readonly Sample[][] _board, _drawing, _tray;
        private readonly Sample[][][] _items = new Sample[3][][];
        private readonly Tween[] _up = new Tween[3], _out = new Tween[3];
        private readonly Glow[] _glow = new Glow[3];
        private int _act = -2;

        public override string Label { get { return "Cabinets on a drawing and their rows in a schedule"; } }

        public ScheduleFigure()
        {
            var c = Iso.Cam(45, 0.5, 1.6);
            Iso.Fit(c, new[] { Iso.W(-10, -10, -BT), Iso.W(TX + TW + 10, -10, -BT), Iso.W(TX + TW + 10, DD + 10, -BT), Iso.W(-10, DD + 10, -BT), Iso.W(50, 10, PT + 22 + LIFT + 2) }, 200, 166);
            Setup(c);
            _board = Iso.Lid(-10, -10, TX + TW + 10, DD + 10, 9, 2);
            _drawing = Iso.Lid(0, 0, DW, DD, 4, 1.2);
            _tray = Iso.Lid(TX, 0, TX + TW, DD, 6, 2.4);
            for (int i = 0; i < 3; i++)
            {
                var it = Items[i];
                _items[i] = Iso.Rings(it[0], it[1], it[2], it[3], 2.6, 0.9);
                _up[i] = new Tween(0);
                _out[i] = new Tween(0);
                _glow[i] = new Glow();
            }
            Over(-1, true, 0);
            for (int i = 0; i < 3; i++) { _up[i].Jump(); _out[i].Jump(); _glow[i].Jump(); }
        }

        public override V2?[] Stops
        {
            get { return new V2?[] { ItemAt(1), ItemAt(2), RowAt(0), null }; }
        }

        private V2 ItemAt(int i) { var it = Items[i]; return P((it[0] + it[2]) / 2, (it[1] + it[3]) / 2, PT + it[4]); }
        private V2 RowAt(int i) { return P(TX + TW / 2, RowY0 + i * (RowH + RowGap) + RowH / 2, TT); }

        private void Over(int a, bool fromItem, double now)
        {
            if (a == _act) return;
            _act = a;
            for (int i = 0; i < 3; i++)
            {
                bool on = i == a;
                // the part under the pointer moves first, its twin 110 ms later
                _up[i].Set(on ? LIFT : 0, now, on && !fromItem ? 110 : 0);
                _out[i].Set(on ? OUT : a < 0 && i == 0 ? OUT * 0.5 : 0, now, on && fromItem ? 110 : 0);
                _glow[i].Set(a < 0 ? i == 0 : on, now);
            }
            Caption = a < 0 ? "rest  ·  3 items, 3 rows" : "item " + (a + 1) + "  →  row " + (a + 1);
        }

        public override void Move(double x, double y, double now)
        {
            // rows: tested on the tray's resting top
            var t = Ground(x, y, TT);
            if (t[0] >= TX - 2 && t[0] <= TX + TW + 2 && t[1] >= 0 && t[1] <= DD)
            {
                int r = (int)Math.Floor((t[1] - RowY0 + RowGap / 2) / (RowH + RowGap));
                Over(Math.Max(0, Math.Min(2, r)), false, now);
                return;
            }
            // cabinets: each against its own resting lid, the nearest middle wins
            int best = -1;
            double bd = 1e9;
            for (int i = 0; i < 3; i++)
            {
                var it = Items[i];
                var g = Ground(x, y, PT + it[4]);
                double cx = (it[0] + it[2]) / 2, cy = (it[1] + it[3]) / 2;
                double dx = Math.Max(0, Math.Abs(g[0] - cx) - (it[2] - it[0]) / 2), dy = Math.Max(0, Math.Abs(g[1] - cy) - (it[3] - it[1]) / 2);
                double d = dx * dx + dy * dy;
                if (d < bd) { bd = d; best = i; }
            }
            var gd = Ground(x, y, PT);
            bool onDrawing = gd[0] >= -4 && gd[0] <= DW + 4 && gd[1] >= -4 && gd[1] <= DD + 4;
            if (bd < 40) Over(best, true, now);
            else if (!onDrawing) Over(-1, true, now);
        }

        public override void Leave(double now) { Over(-1, true, now); }

        public override bool Tick(double dt, double now)
        {
            for (int i = 0; i < 3; i++)
                if (!_up[i].Done(now) || !_out[i].Done(now) || !_glow[i].Done(now)) return true;
            return false;
        }

        public override void Draw(Scene sc, double now)
        {
            sc.Solid(Iso.Slab(P, Front, _board[0], _board[1], -BT, 0));
            sc.Solid(Iso.Slab(P, Front, _drawing[0], _drawing[1], 0, PT));
            // a dashed grid line on the drawing, behind the cabinets
            sc.Line(new[] { P(6, 50, PT), P(DW - 6, 50, PT) }, Ink.Lo, true);
            sc.Line(new[] { P(44, 6, PT), P(44, DD - 6, PT) }, Ink.Lo, true);
            for (int i = 0; i < 3; i++)
            {
                var it = Items[i];
                double z = PT + _up[i].Value(now);
                if (z > PT + 0.2) sc.Outline(Iso.RingAt(P, _items[i][0], PT), Ink.Lo, false, true);
                sc.Solid(Iso.Prism(P, Front, _items[i][0], _items[i][1], z, z + it[4]), _glow[i].Value(now));
                Tag(sc, (it[0] + it[2]) / 2, (it[1] + it[3]) / 2, z + it[4], i + 1, _glow[i].Value(now) > 0.5f);
            }
            sc.Solid(Iso.Slab(P, Front, _tray[0], _tray[1], 0, TT));
            for (int i = 0; i < 3; i++)
            {
                // a row slides out of the tray towards its cabinet, like a drawer
                double o = _out[i].Value(now), y0 = RowY0 + i * (RowH + RowGap);
                var r = Iso.Rings(TX + 6 - o, y0, TX + TW - 6 - o, y0 + RowH, 2.4, 0.8);
                float g = _glow[i].Value(now);
                sc.Solid(Iso.Slab(P, Front, r[0], null, TT, TT + 1.6), g);
                Tag(sc, TX + 13 - o, y0 + RowH / 2, TT + 1.6, i + 1, g > 0.5f);
            }
        }

        /// <summary>n dots in a row on a lid: the tag that pairs a cabinet with its row.</summary>
        private void Tag(Scene sc, double x, double y, double z, int n, bool bright)
        {
            for (int k = 0; k < n; k++) sc.Dot(P(x - (n - 1) * 1.6 + k * 3.2, y, z), C, 0.9, bright ? Ink.Hi : Ink.Edge);
        }
    }
}
