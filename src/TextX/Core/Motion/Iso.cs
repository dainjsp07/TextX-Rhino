using System;
using System.Collections.Generic;
using System.Linq;

// Isometric line figures after Hairline by Lucas Marques (MIT), https://github.com/lucasmarkes/hairline.
// The drawing maths of its core/iso.ts and slab.ts, ported to C#; see THIRD-PARTY-NOTICES.txt.

namespace TextX.Core.Motion
{
    /// <summary>A point on the screen of a figure, in view-box units (400 x 320).</summary>
    public struct V2
    {
        public float X, Y;
        public V2(double x, double y) { X = (float)x; Y = (float)y; }
        public override string ToString() { return X.ToString("0.#") + "," + Y.ToString("0.#"); }
    }

    /// <summary>A sample on a rounded outline on the ground, with its outward normal.</summary>
    public struct Sample
    {
        public double U, V, Nu, Nv;
        public Sample(double u, double v, double nu, double nv) { U = u; V = v; Nu = nu; Nv = nv; }
    }

    /// <summary>
    /// An orthographic camera: the ground turned by an azimuth, then squashed by k = sin(elevation)
    /// (0.5 is the 2:1 view), scaled by S and moved by an offset that <see cref="Iso.Fit"/> computes.
    /// </summary>
    public sealed class Camera
    {
        public double Az, K, S, Ox, Oy;
    }

    public delegate V2 Projector(double x, double y, double z);

    /// <summary>The outline of a solid: its silhouette (closed) and the one dim crease inside it (open).</summary>
    public sealed class SolidPaths
    {
        public readonly List<V2[]> Sil = new List<V2[]>();
        public readonly List<V2[]> SilOpen = new List<V2[]>();
        public readonly List<V2[]> Crease = new List<V2[]>();
        public bool CreaseClosed;
    }

    /// <summary>
    /// The drawing maths every figure shares. World space is x/y on the ground and z up, in view-box
    /// units; there is no perspective and no hidden-line removal: plates are filled with the ground
    /// colour and painted back to front, so a nearer one simply covers.
    /// </summary>
    public static class Iso
    {
        public const float Width = 400f, Height = 320f;

        public static double Clamp(double v, double a, double b) { return Math.Max(a, Math.Min(b, v)); }
        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        public static double Rad(double d) { return d * Math.PI / 180.0; }

        public static Camera Cam(double azDeg, double k, double s)
        {
            return new Camera { Az = Rad(azDeg), K = k, S = s };
        }

        public static Projector Proj(Camera c)
        {
            double co = Math.Cos(c.Az), si = Math.Sin(c.Az), zf = Math.Sqrt(1 - c.K * c.K);
            double ox = c.Ox, oy = c.Oy, s = c.S, k = c.K;
            return (x, y, z) =>
            {
                double X = x * co - y * si, Y = x * si + y * co;
                return new V2(ox + s * X, oy + s * (Y * k - z * zf));
            };
        }

        /// <summary>The world x/y under a screen point, on the plane at height z: how the pointer reaches the drawing.</summary>
        public static double[] Unproj(Camera c, double sx, double sy, double z)
        {
            double co = Math.Cos(c.Az), si = Math.Sin(c.Az), zf = Math.Sqrt(1 - c.K * c.K);
            double X = (sx - c.Ox) / c.S, Y = ((sy - c.Oy) / c.S + z * zf) / c.K;
            return new[] { X * co + Y * si, -X * si + Y * co };
        }

        /// <summary>Centres the bounding box of the world points on (cx, cy).</summary>
        public static void Fit(Camera c, IEnumerable<double[]> pts, double cx, double cy)
        {
            c.Ox = 0; c.Oy = 0;
            var p = Proj(c);
            double a = 1e9, b = -1e9, d0 = 1e9, d1 = -1e9;
            foreach (var w in pts)
            {
                var q = p(w[0], w[1], w[2]);
                a = Math.Min(a, q.X); b = Math.Max(b, q.X); d0 = Math.Min(d0, q.Y); d1 = Math.Max(d1, q.Y);
            }
            c.Ox = cx - (a + b) / 2; c.Oy = cy - (d0 + d1) / 2;
        }

        /// <summary>A rounded rectangle on the ground, sampled n times per corner, each sample with its outward normal.</summary>
        public static Sample[] RRect(double u0, double v0, double u1, double v1, double r, int n = 4)
        {
            r = Math.Max(0, Math.Min(r, Math.Min((u1 - u0) / 2, (v1 - v0) / 2)));
            var corners = new[] { new[] { u1 - r, v1 - r, 0 }, new[] { u0 + r, v1 - r, 90 }, new[] { u0 + r, v0 + r, 180 }, new[] { u1 - r, v0 + r, 270 } };
            var outp = new List<Sample>();
            foreach (var cr in corners)
                for (int k = 0; k <= n; k++)
                {
                    double a = Rad(cr[2] + 90.0 * k / n), ca = Math.Cos(a), sa = Math.Sin(a);
                    outp.Add(new Sample(cr[0] + r * ca, cr[1] + r * sa, ca, sa));
                }
            return outp.ToArray();
        }

        /// <summary>A circle about (cx, cy) on the ground.</summary>
        public static Sample[] Circ(double cx, double cy, double r, int n = 48)
        {
            var outp = new Sample[n];
            for (int k = 0; k < n; k++)
            {
                double a = (double)k / n * Math.PI * 2, ca = Math.Cos(a), sa = Math.Sin(a);
                outp[k] = new Sample(cx + r * ca, cy + r * sa, ca, sa);
            }
            return outp;
        }

        /// <summary>A rounded footprint and its crease ring, inset by b.</summary>
        public static Sample[][] Rings(double x0, double y0, double x1, double y1, double r, double b, int n = 4)
        {
            return new[] { RRect(x0, y0, x1, y1, r, n), RRect(x0 + b, y0 + b, x1 - b, y1 - b, Math.Max(0.3, r - b), n) };
        }

        /// <summary>Convex hull (monotone chain).</summary>
        public static V2[] Hull(IEnumerable<V2> input)
        {
            var pts = input.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (pts.Count < 3) return pts.ToArray();
            Func<V2, V2, V2, double> cross = (o, a, b) => (a.X - o.X) * (double)(b.Y - o.Y) - (a.Y - o.Y) * (double)(b.X - o.X);
            var lo = new List<V2>();
            var up = new List<V2>();
            foreach (var p in pts)
            {
                while (lo.Count > 1 && cross(lo[lo.Count - 2], lo[lo.Count - 1], p) <= 0) lo.RemoveAt(lo.Count - 1);
                lo.Add(p);
            }
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                var p = pts[i];
                while (up.Count > 1 && cross(up[up.Count - 2], up[up.Count - 1], p) <= 0) up.RemoveAt(up.Count - 1);
                up.Add(p);
            }
            lo.RemoveAt(lo.Count - 1);
            up.RemoveAt(up.Count - 1);
            lo.AddRange(up);
            return lo.ToArray();
        }

        public static V2[] RingAt(Projector p, IList<Sample> ring, double z)
        {
            var outp = new V2[ring.Count];
            for (int i = 0; i < ring.Count; i++) outp[i] = p(ring[i].U, ring[i].V, z);
            return outp;
        }

        /// <summary>Whether a sample's normal faces the camera (the world direction of screen-down is (sin az, cos az)).</summary>
        public static Func<Sample, bool> Facing(Camera c)
        {
            double s = Math.Sin(c.Az), co = Math.Cos(c.Az);
            return q => q.Nu * s + q.Nv * co >= -1e-6;
        }

        /// <summary>The one cyclic run of samples that pass keep, in ring order.</summary>
        public static Sample[] Run(IList<Sample> ring, Func<Sample, bool> keep)
        {
            int n = ring.Count, s = -1;
            for (int i = 0; i < n; i++) if (keep(ring[i]) && !keep(ring[(i + n - 1) % n])) { s = i; break; }
            if (s < 0) return keep(ring[0]) ? ring.ToArray() : new Sample[0];
            var outp = new List<Sample>();
            for (int k = 0; k < n && keep(ring[(s + k) % n]); k++) outp.Add(ring[(s + k) % n]);
            return outp.ToArray();
        }

        /// <summary>
        /// A prism standing from z0 to z1: its silhouette is the hull of the two rings (the vertical corners
        /// are never drawn), and its only inner line is the crease, the front run of the inset ring on the lid.
        /// </summary>
        public static SolidPaths Prism(Projector p, Func<Sample, bool> front, Sample[] ring, Sample[] inner, double z0, double z1)
        {
            var s = new SolidPaths();
            s.Sil.Add(Hull(RingAt(p, ring, z1).Concat(RingAt(p, ring, z0))));
            if (inner != null) s.Crease.Add(RingAt(p, Run(inner, front), z1));
            return s;
        }

        /// <summary>A prism whose lid is drawn whole: the silhouette carries the lid's front edge, the crease is the inset ring all round.</summary>
        public static SolidPaths Slab(Projector p, Func<Sample, bool> front, Sample[] ring, Sample[] inner, double z0, double z1)
        {
            var s = new SolidPaths();
            s.Sil.Add(Hull(RingAt(p, ring, z1).Concat(RingAt(p, ring, z0))));
            s.SilOpen.Add(RingAt(p, Run(ring, front), z1));
            if (inner != null) { s.Crease.Add(RingAt(p, inner, z1)); s.CreaseClosed = true; }
            return s;
        }

        /// <summary>A rounded footprint and its lip, sampled finely enough for a large tile.</summary>
        public static Sample[][] Lid(double x0, double y0, double x1, double y1, double r, double b)
        {
            return Rings(x0, y0, x1, y1, r, b, 8);
        }

        public static double[] W(double x, double y, double z) { return new[] { x, y, z }; }
    }
}
