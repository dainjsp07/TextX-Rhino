using System;
using System.Linq;

namespace TextX.Core.Motion.Figures
{
    /// <summary>
    /// Pages: a document and its translation, two sheets side by side on a board, each with a heading, a
    /// picture and lines of text in the same places (the translation's lines a little longer or shorter).
    /// The pointer picks a piece on either sheet: the piece and its twin on the other sheet lift together,
    /// the near one first, and take the bright edge. That is what Text X does: every paragraph is translated
    /// where it stands. At rest the translation's heading is bright.
    ///
    /// The pattern: discrete items (tweens), a stagger by distance between the twins, and a hit test on the
    /// sheets' resting tops, which never move.
    /// </summary>
    public sealed class PagesFigure : Figure
    {
        private const double SW = 72, SD = 100, GAP = 20, ST = 2, LIFT = 11, BT = 4;
        // pieces on a sheet: x0, y0, x1, y1, thickness; the translation's length as a share of the original's
        private static readonly double[][] Pieces =
        {
            new double[] { 8, 9, 50, 17, 2.6 },
            new double[] { 8, 25, 64, 29, 1.4 },
            new double[] { 8, 34, 58, 38, 1.4 },
            new double[] { 8, 43, 62, 47, 1.4 },
            new double[] { 8, 56, 34, 84, 5.0 },
            new double[] { 41, 57, 64, 61, 1.4 },
            new double[] { 41, 66, 58, 70, 1.4 },
            new double[] { 41, 75, 61, 79, 1.4 },
        };
        private static readonly double[] Longer = { 1.12, 0.9, 1.1, 0.84, 1, 1.0, 1.18, 0.86 };
        private static readonly string[] Names = { "heading", "paragraph 1", "paragraph 2", "paragraph 3", "picture", "caption", "note 1", "note 2" };

        private readonly Tween[,] _lift = new Tween[2, Pieces.Length];
        private readonly Glow[] _glow = new Glow[Pieces.Length];
        private readonly Sample[][] _board;
        private readonly Sample[][][] _sheet = new Sample[2][][];
        private int _act = -2; // -2 = never set, -1 = rest

        public override string Label { get { return "A document and its translation: every piece stays in its place"; } }

        public PagesFigure()
        {
            var c = Iso.Cam(45, 0.5, 1.62);
            Iso.Fit(c, new[] { Iso.W(-10, -10, -BT), Iso.W(SW * 2 + GAP + 10, SD + 10, -BT), Iso.W(SW * 2 + GAP + 10, -10, -BT), Iso.W(-10, SD + 10, -BT), Iso.W(0, 0, ST + LIFT + 6) }, 200, 166);
            Setup(c);
            _board = Iso.Lid(-10, -10, SW * 2 + GAP + 10, SD + 10, 9, 2);
            for (int s = 0; s < 2; s++)
            {
                double x0 = s * (SW + GAP);
                _sheet[s] = Iso.Lid(x0, 0, x0 + SW, SD, 4, 1.2);
                for (int i = 0; i < Pieces.Length; i++)
                {
                    var p = Pieces[i];
                    double len = (p[2] - p[0]) * (s == 1 ? Longer[i] : 1);
                    double x1 = Math.Min(SW - 6, p[0] + len);
                    var r = Iso.Rings(x0 + p[0], p[1], x0 + x1, p[3], p[4] > 3 ? 2.2 : 1.4, p[4] > 3 ? 1.1 : 0.5);
                    _ring[s, i] = r;
                    _lift[s, i] = new Tween(0);
                }
            }
            for (int i = 0; i < Pieces.Length; i++) _glow[i] = new Glow();
            Over(-1, 0, 0);
            foreach (var t in _lift) t.Jump();
            foreach (var g in _glow) g.Jump();
        }

        private readonly Sample[,][][] _ring = new Sample[2, Pieces.Length][][];

        public override V2?[] Stops
        {
            get
            {
                // a heading, a paragraph and the picture on the original, then a line on the translation
                return new V2?[] { At(0, 0), At(0, 2), At(0, 4), At(1, 6), null };
            }
        }

        private V2 At(int s, int i)
        {
            var p = Pieces[i];
            return P(s * (SW + GAP) + (p[0] + p[2]) / 2, (p[1] + p[3]) / 2, ST);
        }

        private int Hit(double x, double y, out int sheet)
        {
            var g = Ground(x, y, ST);
            sheet = -1;
            for (int s = 0; s < 2; s++)
            {
                double x0 = s * (SW + GAP);
                if (g[0] < x0 - 4 || g[0] > x0 + SW + 4 || g[1] < -4 || g[1] > SD + 4) continue;
                sheet = s;
                double lx = g[0] - x0, ly = g[1];
                int best = -1;
                double bd = 7;
                for (int i = 0; i < Pieces.Length; i++)
                {
                    var p = Pieces[i];
                    double dx = Math.Max(0, Math.Max(p[0] - lx, lx - p[2])), dy = Math.Max(0, Math.Max(p[1] - ly, ly - p[3]));
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < bd) { bd = d; best = i; }
                }
                return best;
            }
            return -1;
        }

        private void Over(int a, int sheet, double now)
        {
            if (a == _act) return;
            _act = a;
            for (int i = 0; i < Pieces.Length; i++)
                for (int s = 0; s < 2; s++)
                {
                    double to = i == a ? LIFT : 0;
                    // the twin on the other sheet follows 90 ms later: the motion spreads from the pointer
                    _lift[s, i].Set(to, now, i == a && s != sheet ? 90 : 0);
                }
            int bright = a < 0 ? 0 : a;
            for (int i = 0; i < Pieces.Length; i++) _glow[i].Set(i == bright, now);
            Caption = a < 0 ? "rest  ·  same layout" : Names[a] + (a == 4 ? "  ·  kept as it is" : "  ·  same place");
        }

        public override void Move(double x, double y, double now)
        {
            int sheet;
            int a = Hit(x, y, out sheet);
            if (a < 0 && sheet < 0) { Over(-1, 0, now); return; }
            if (a < 0) return; // on a sheet between pieces: keep the choice
            Over(a, sheet, now);
        }

        public override void Leave(double now) { Over(-1, 0, now); }

        public override bool Tick(double dt, double now)
        {
            bool moving = false;
            for (int i = 0; i < Pieces.Length; i++)
            {
                if (!_glow[i].Done(now)) moving = true;
                for (int s = 0; s < 2; s++) if (!_lift[s, i].Done(now)) moving = true;
            }
            return moving;
        }

        public override void Draw(Scene sc, double now)
        {
            sc.Solid(Iso.Slab(P, Front, _board[0], _board[1], -BT, 0));
            for (int s = 0; s < 2; s++)
            {
                sc.Solid(Iso.Slab(P, Front, _sheet[s][0], _sheet[s][1], 0, ST));
                // back to front on the sheet: by the depth of each piece's centre
                var order = Enumerable.Range(0, Pieces.Length).OrderBy(i => Pieces[i][0] + Pieces[i][2] + Pieces[i][1] + Pieces[i][3]).ToList();
                foreach (int i in order)
                {
                    double z = ST + _lift[s, i].Value(now);
                    var r = _ring[s, i];
                    float bright = s == 1 || _act >= 0 ? _glow[i].Value(now) : 0f;
                    if (z > ST + 0.2)
                    {
                        // a dashed drop from where the piece was, painted before it
                        var foot = Iso.RingAt(P, r[0], ST);
                        sc.Outline(foot, Ink.Lo, false, true);
                    }
                    sc.Solid(Pieces[i][4] > 3 ? Iso.Prism(P, Front, r[0], r[1], z, z + Pieces[i][4]) : Iso.Slab(P, Front, r[0], null, z, z + Pieces[i][4]), bright);
                }
            }
        }
    }
}
