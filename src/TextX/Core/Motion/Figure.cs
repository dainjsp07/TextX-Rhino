using System;
using System.Collections.Generic;

// After the stage and tour of Hairline by Lucas Marques (MIT), https://github.com/lucasmarkes/hairline (core/stage.ts).

namespace TextX.Core.Motion
{
    /// <summary>The five inks of a line figure. Hi is the only highlight: the accent colour.</summary>
    public enum Ink { None, Lo, Mid, Edge, Hi }

    /// <summary>One thing to paint: closed outlines (filled with the plate colour, then stroked), open lines, or a dot.</summary>
    public sealed class Shape
    {
        public readonly List<V2[]> Closed = new List<V2[]>();
        public readonly List<V2[]> Open = new List<V2[]>();
        public Ink Stroke = Ink.Mid;
        /// <summary>How far the stroke has gone to <see cref="Ink.Hi"/> (0 to 1): the 260 ms fade.</summary>
        public float Bright;
        public bool Fill = true;
        public bool Dash;
        public bool IsDot;
        public V2 Center;
        public float Rx, Ry;
    }

    /// <summary>A figure's drawing for one frame, in paint order (back to front).</summary>
    public sealed class Scene
    {
        public readonly List<Shape> Shapes = new List<Shape>();

        public void Clear() { Shapes.Clear(); }

        /// <summary>A solid: its silhouette (edge ink, filled) and its crease (low ink, not filled).</summary>
        public void Solid(SolidPaths s, float bright = 0f)
        {
            var sil = new Shape { Stroke = Ink.Edge, Bright = bright };
            sil.Closed.AddRange(s.Sil);
            sil.Open.AddRange(s.SilOpen);
            Shapes.Add(sil);
            if (s.Crease.Count == 0) return;
            var cr = new Shape { Stroke = Ink.Lo, Fill = false };
            if (s.CreaseClosed) cr.Closed.AddRange(s.Crease); else cr.Open.AddRange(s.Crease);
            Shapes.Add(cr);
        }

        public void Line(V2[] pts, Ink ink, bool dash = false, float bright = 0f)
        {
            if (pts == null || pts.Length < 2) return;
            var sh = new Shape { Stroke = ink, Fill = false, Dash = dash, Bright = bright };
            sh.Open.Add(pts);
            Shapes.Add(sh);
        }

        public void Outline(V2[] pts, Ink ink, bool fill, bool dash = false)
        {
            var sh = new Shape { Stroke = ink, Fill = fill, Dash = dash };
            sh.Closed.Add(pts);
            Shapes.Add(sh);
        }

        /// <summary>A dot lying flat on a horizontal plane (an ellipse squashed by the camera).</summary>
        public void Dot(V2 c, Camera cam, double r, Ink ink)
        {
            Shapes.Add(new Shape { IsDot = true, Center = c, Rx = (float)(r * cam.S), Ry = (float)(r * cam.S * cam.K), Stroke = ink });
        }
    }

    /// <summary>The bright edge's fade: 260 ms on the colour ease.</summary>
    public sealed class Glow
    {
        private readonly Tween _t = new Tween(0, 260) { Ease = Clock.EaseFade };
        public void Set(bool on, double now) { _t.Set(on ? 1 : 0, now); }
        public float Value(double now) { return (float)_t.Value(now); }
        public bool Done(double now) { return _t.Done(now); }
        public void Jump() { _t.Jump(); }
    }

    /// <summary>
    /// A line figure that answers the pointer: drawn in a 400 x 320 view box, it hears the pointer in
    /// view-box units, moves on springs and tweens, and says what is under the pointer in its caption.
    /// </summary>
    public abstract class Figure
    {
        protected Camera C;
        protected Projector P;
        protected Func<Sample, bool> Front;

        /// <summary>What the figure shows, for screen readers and tooltips.</summary>
        public abstract string Label { get; }

        /// <summary>A few words on what is under the pointer ("rest" when nothing is).</summary>
        public string Caption { get; protected set; }

        /// <summary>Where an unseen pointer stops when the figure plays itself (null = leave).</summary>
        public virtual V2?[] Stops
        {
            get { return new V2?[] { new V2(128, 150), new V2(200, 118), new V2(272, 150), new V2(200, 206), null }; }
        }

        protected void Setup(Camera c)
        {
            C = c;
            P = Iso.Proj(c);
            Front = Iso.Facing(c);
        }

        /// <summary>The pointer at (x, y) in view-box units.</summary>
        public abstract void Move(double x, double y, double now);

        public abstract void Leave(double now);

        /// <summary>Advances the springs by dt seconds. Returns whether anything is still moving.</summary>
        public abstract bool Tick(double dt, double now);

        public abstract void Draw(Scene s, double now);

        /// <summary>The pointer on the ground plane at height z, in world units.</summary>
        protected double[] Ground(double x, double y, double z = 0) { return Iso.Unproj(C, x, y, z); }
    }

    /// <summary>
    /// An unseen pointer that walks a figure's stops the way a hand would: in from the edge, 900 ms to each
    /// stop on the discrete ease, a 1.2 s dwell, a leave and a rest. A real pointer holds it.
    /// </summary>
    public sealed class TourPlayer
    {
        private const double Travel = 900, Dwell = 1200, Rest = 1800;
        private readonly Figure _f;
        private readonly V2?[] _stops;
        private int _i;
        private double _wait, _t = -1;
        private V2? _at;
        private V2 _origin;
        private bool _held;
        private int _laps;

        /// <summary>How many laps to walk (0 = until stopped).</summary>
        public int Laps { get; set; }
        public bool Finished { get; private set; }

        public TourPlayer(Figure f, double delayMs, int laps = 1)
        {
            _f = f;
            _stops = f.Stops ?? new V2?[0];
            _wait = delayMs;
            Laps = laps;
        }

        public void Hold() { _held = true; _at = null; _t = -1; }

        public void Stop()
        {
            if (_at != null) _f.Leave(0);
            _at = null;
            Finished = true;
        }

        private static V2 Entry(V2 p)
        {
            double dx = p.X - 200, dy = p.Y - 160;
            if (dx == 0 && dy == 0) return new V2(200, 320);
            double k = Math.Min(dx != 0 ? (dx > 0 ? 200 : -200) / dx : double.PositiveInfinity, dy != 0 ? (dy > 0 ? 160 : -160) / dy : double.PositiveInfinity);
            return new V2(200 + dx * k, 160 + dy * k);
        }

        /// <summary>Advances by dt seconds. Returns whether it wants more frames.</summary>
        public bool Tick(double dt, double now)
        {
            if (Finished || _held || _stops.Length == 0) return false;
            if (Clock.Reduced) { Stop(); return false; }
            if (_wait > 0) { _wait -= dt * 1000; return true; }
            var stop = _stops[_i];
            if (stop == null)
            {
                if (_at != null) { _at = null; _t = -1; _f.Leave(now); }
                _i = (_i + 1) % _stops.Length;
                _wait = Rest;
                if (_i == 0 && Laps > 0 && ++_laps >= Laps) { Finished = true; return false; }
                return true;
            }
            if (_t < 0) { _origin = _at ?? Entry(stop.Value); _t = 0; }
            _t = Math.Min(Travel, _t + dt * 1000);
            double k = Clock.EaseLift(_t / Travel);
            var at = new V2(_origin.X + (stop.Value.X - _origin.X) * k, _origin.Y + (stop.Value.Y - _origin.Y) * k);
            _at = at;
            _f.Move(at.X, at.Y, now);
            if (_t < Travel) return true;
            _i = (_i + 1) % _stops.Length;
            _t = -1;
            _wait = Dwell;
            return true;
        }
    }
}
