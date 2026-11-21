using System;

// The two clocks of Hairline by Lucas Marques (MIT), https://github.com/lucasmarkes/hairline (core/motion.ts).

namespace TextX.Core.Motion
{
    /// <summary>
    /// The two clocks. A discrete change (which item) gets a long ease-out on a <see cref="Tween"/>; a
    /// continuous input (where the pointer is) gets a <see cref="Spring"/>, because its target moves every
    /// frame. With <see cref="Reduced"/> on (Windows "Show animations" off), both land at once.
    /// </summary>
    public static class Clock
    {
        public static bool Reduced { get; set; }

        /// <summary>The discrete clock: 700 ms on cubic-bezier(.32, .72, 0, 1).</summary>
        public static readonly Func<double, double> EaseLift = Bezier(0.32, 0.72, 0, 1);

        /// <summary>The stroke and colour fade: cubic-bezier(.5, 0, .1, 1).</summary>
        public static readonly Func<double, double> EaseFade = Bezier(0.5, 0, 0.1, 1);

        /// <summary>A CSS cubic-bezier as a function of progress: Newton first, bisection if it strays.</summary>
        public static Func<double, double> Bezier(double x1, double y1, double x2, double y2)
        {
            double cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx;
            double cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
            Func<double, double> X = u => ((ax * u + bx) * u + cx) * u;
            Func<double, double> Y = u => ((ay * u + by) * u + cy) * u;
            Func<double, double> dX = u => (3 * ax * u + 2 * bx) * u + cx;
            return t =>
            {
                if (t <= 0) return 0;
                if (t >= 1) return 1;
                double u = t;
                for (int i = 0; i < 8; i++)
                {
                    double e = X(u) - t;
                    if (Math.Abs(e) < 1e-5) break;
                    double d = dX(u);
                    if (Math.Abs(d) < 1e-6) break;
                    u -= e / d;
                }
                if (!(u >= 0 && u <= 1) || Math.Abs(X(u) - t) > 1e-4)
                {
                    double lo = 0, hi = 1;
                    u = t;
                    for (int i = 0; i < 24; i++) { if (X(u) < t) lo = u; else hi = u; u = (lo + hi) / 2; }
                }
                return Y(u);
            };
        }
    }

    /// <summary>A spring toward <see cref="T"/>: k 100, c 18, m 1 unless told otherwise.</summary>
    public sealed class Spring
    {
        public double X, V, T, K = 100, C = 18, M = 1, Eps = 0.01;

        public Spring(double x, double k = 100, double c = 18, double eps = 0.01)
        {
            X = x; T = x; K = k; C = c; Eps = eps;
        }

        /// <summary>Advances by dt seconds, substepped at 240 Hz. Returns whether it is still moving.</summary>
        public bool Step(double dt)
        {
            if (Clock.Reduced) { X = T; V = 0; return false; }
            int n = Math.Max(1, (int)Math.Ceiling(dt * 240));
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                double a = (-K * (X - T) - C * V) / M;
                V += a * h;
                X += V * h;
            }
            if (Math.Abs(X - T) < Eps && Math.Abs(V) < Eps * 10) { X = T; V = 0; return false; }
            return true;
        }

        public bool Moving { get { return X != T || V != 0; } }
    }

    /// <summary>A tween on an ease (the discrete clock by default). Times are in milliseconds.</summary>
    public sealed class Tween
    {
        public double From, To, T0 = -1e9, Dur;
        public Func<double, double> Ease = Clock.EaseLift;

        public Tween(double v, double dur = 700)
        {
            From = v; To = v; Dur = dur;
        }

        public double Value(double now)
        {
            double p = Iso.Clamp((now - T0) / Math.Max(1, Dur), 0, 1);
            return From + (To - From) * (Clock.Reduced ? 1 : Ease(p));
        }

        /// <summary>Retargets from wherever it is now, starting after delay ms. A no-op if the target is unchanged.</summary>
        public void Set(double to, double now, double delay = 0)
        {
            if (to == To) return;
            From = Value(now);
            To = to;
            T0 = now + delay;
        }

        public bool Done(double now) { return Clock.Reduced || now >= T0 + Dur; }

        /// <summary>Lands on the target now, with no motion (the first frame of a rest pose).</summary>
        public void Jump() { From = To; T0 = -1e9; }
    }
}
