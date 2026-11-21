using System;
using System.Collections.Generic;
using System.Linq;
using TextX.Core.Motion;
using TextX.Core.Motion.Figures;
using static TextX.Tests.TestRunner;

namespace TextX.Tests
{
    public static partial class CoreTests
    {
        /// <summary>The line figures and their clocks (after Hairline's rules: frame, settle, steady hits).</summary>
        public static void Motion()
        {
            // projection round trip on the ground plane and at a height
            var cam = Iso.Cam(45, 0.5, 1.7);
            Iso.Fit(cam, new[] { Iso.W(0, 0, 0), Iso.W(100, 80, 30) }, 200, 166);
            var p = Iso.Proj(cam);
            var s = p(37, 21, 6);
            var back = Iso.Unproj(cam, s.X, s.Y, 6);
            True(Math.Abs(back[0] - 37) < 0.01 && Math.Abs(back[1] - 21) < 0.01, "unproject inverts project: " + back[0] + "," + back[1]);
            var hull = Iso.Hull(new[] { new V2(0, 0), new V2(10, 0), new V2(10, 10), new V2(0, 10), new V2(5, 5) });
            Equal(4, hull.Length, "hull of a square with a point inside");
            Equal(20, Iso.RRect(0, 0, 10, 10, 2).Length, "rounded rectangle: 5 samples per corner");

            // the clocks
            var ease = Clock.EaseLift;
            True(ease(0) == 0 && ease(1) == 1 && ease(0.5) > 0.75 && ease(0.25) < ease(0.5), "the lift ease starts fast and lands slowly");
            var sp = new Spring(0) { T = 10 };
            int frames = 0;
            while (sp.Step(1 / 60.0) && frames < 600) frames++;
            True(Math.Abs(sp.X - 10) < 1e-9 && frames < 120, "a spring settles on its target in under 2 s: " + frames + " frames");
            var tw = new Tween(0);
            tw.Set(5, 1000, 100);
            True(tw.Value(1050) == 0 && tw.Value(1100 + 700) == 5 && tw.Done(1800) && !tw.Done(1500), "a tween waits for its delay and lands after 700 ms");
            Clock.Reduced = true;
            var sr = new Spring(0) { T = 3 };
            bool moving = sr.Step(1 / 60.0);
            Clock.Reduced = false;
            True(!moving && sr.X == 3, "with animations off, springs land at once");

            // every figure: composed at rest, inside its frame in every pose, settles, and its hits hold still
            var figures = new Figure[] { new PagesFigure(), new ScheduleFigure(), new ReadFigure(), new VoiceFigure(), new LibraryFigure() };
            foreach (var f in figures)
            {
                string name = f.GetType().Name;
                double now = 0;
                var scene = new Scene();
                f.Draw(scene, now);
                True(scene.Shapes.Count > 4 && !string.IsNullOrEmpty(f.Caption), name + " has a composed rest and a caption");
                True(InFrame(scene), name + " fits its frame at rest");
                foreach (var stop in f.Stops.Where(x => x != null))
                {
                    f.Move(stop.Value.X, stop.Value.Y, now);
                    string caption = f.Caption;
                    now = Run(f, now, 3.0);
                    scene.Clear();
                    f.Draw(scene, now);
                    True(InFrame(scene), name + " stays in its frame at " + stop.Value + " (" + f.Caption + ")");
                    // rule 01: once the figure has moved, the same pointer still picks the same thing
                    f.Move(stop.Value.X, stop.Value.Y, now);
                    Equal(caption, f.Caption, name + ": the pick does not move with the drawing");
                    True(!f.Tick(1 / 60.0, now + 5000), name + " settles after moving");
                }
                f.Leave(now);
                now = Run(f, now, 3.0);
                True(f.Caption.StartsWith("rest", StringComparison.Ordinal), name + " returns to rest: " + f.Caption);
            }

            // the tour walks the stops once and stops
            var fig = new ScheduleFigure();
            var tour = new TourPlayer(fig, 0, 1);
            var seen = new HashSet<string>();
            double t = 0;
            for (int i = 0; i < 60 * 20 && !tour.Finished; i++)
            {
                t += 1000 / 60.0;
                tour.Tick(1 / 60.0, t);
                fig.Tick(1 / 60.0, t);
                seen.Add(fig.Caption);
            }
            True(tour.Finished && seen.Count >= 3 && fig.Caption.StartsWith("rest", StringComparison.Ordinal), "one lap of the tour: " + string.Join(" | ", seen));
        }

        private static double Run(Figure f, double now, double seconds)
        {
            for (int i = 0; i < seconds * 60; i++) { now += 1000 / 60.0; f.Tick(1 / 60.0, now); }
            return now;
        }

        private static bool InFrame(Scene s)
        {
            foreach (var sh in s.Shapes)
            {
                if (sh.IsDot) { if (!In(sh.Center)) return false; continue; }
                foreach (var poly in sh.Closed.Concat(sh.Open))
                    foreach (var q in poly) if (!In(q)) return false;
            }
            return true;
        }

        private static bool In(V2 q) { return q.X >= 0 && q.X <= Iso.Width && q.Y >= 0 && q.Y <= Iso.Height; }
    }
}
