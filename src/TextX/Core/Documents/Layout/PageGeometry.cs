using System;
using System.Collections.Generic;
using System.Linq;
using TextX.Core.Documents.Native;

namespace TextX.Core.Documents.Layout
{
    /// <summary>
    /// What is drawn on a page besides its text, in page-frame coordinates: the ruled lines of tables and
    /// title blocks (thin lines and drawn rectangles) and every other shape or picture ("ink").
    /// Rules keep the texts of neighbouring cells apart when the page is read; ink tells a translation
    /// where it may grow without running into the drawing.
    /// </summary>
    public sealed class PageGeometry
    {
        private const float Thin = 2.5f;
        private const float CellSize = 48f;

        /// <summary>Vertical ruled lines (zero or small width).</summary>
        public readonly List<Box> VRules = new List<Box>();
        /// <summary>Horizontal ruled lines.</summary>
        public readonly List<Box> HRules = new List<Box>();
        private readonly List<Box> _ink = new List<Box>();
        private readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();

        public static readonly PageGeometry Empty = new PageGeometry();

        public int InkCount { get { return _ink.Count; } }

        public static PageGeometry From(DocPage page, IList<PdfObjectInfo> objects)
        {
            var g = new PageGeometry();
            if (objects == null) return g;
            float pageArea = Math.Max(1f, page.Width * page.Height);
            foreach (var o in objects)
            {
                if (!(o.IsPath || o.IsImage || o.Type == PdfiumNative.ObjShading)) continue;
                var b = ToFrame(page, o);
                if (b.W <= 0.01f && b.H <= 0.01f) continue;
                // off the page
                if (b.R < 0 || b.L > page.Width || b.T < 0 || b.B > page.Height) continue;
                if (b.W * b.H > pageArea * 0.6f)
                {
                    // the sheet's frame or its background: only its edges count
                    if (o.IsPath) g.AddEdges(b);
                    continue;
                }
                if (o.IsPath)
                {
                    if (b.W <= Thin && b.H > 4f) g.VRules.Add(b);
                    else if (b.H <= Thin && b.W > 4f) g.HRules.Add(b);
                    else if (o.Segments >= 4 && o.Segments <= 6 && b.W > 4f && b.H > 4f) g.AddEdges(b); // a drawn rectangle: a cell or a box
                }
                g.AddInk(b);
            }
            return g;
        }

        private static Box ToFrame(DocPage page, PdfObjectInfo o)
        {
            float x1, y1, x2, y2;
            page.UserToFrame(o.Left, o.Bottom, out x1, out y1);
            page.UserToFrame(o.Right, o.Top, out x2, out y2);
            return new Box(x1, y1, x2, y2);
        }

        private void AddEdges(Box b)
        {
            VRules.Add(new Box(b.L, b.B, b.L, b.T));
            VRules.Add(new Box(b.R, b.B, b.R, b.T));
            HRules.Add(new Box(b.L, b.B, b.R, b.B));
            HRules.Add(new Box(b.L, b.T, b.R, b.T));
        }

        private void AddInk(Box b)
        {
            int index = _ink.Count;
            _ink.Add(b);
            foreach (var key in Cells(b))
            {
                List<int> list;
                if (!_grid.TryGetValue(key, out list)) _grid[key] = list = new List<int>();
                list.Add(index);
            }
        }

        private static IEnumerable<long> Cells(Box b)
        {
            int x0 = (int)Math.Floor(b.L / CellSize), x1 = (int)Math.Floor(b.R / CellSize);
            int y0 = (int)Math.Floor(b.B / CellSize), y1 = (int)Math.Floor(b.T / CellSize);
            // a huge shape is listed in a bounded number of cells (it is found from its corners' cells)
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 4096) { x1 = Math.Min(x1, x0 + 63); y1 = Math.Min(y1, y0 + 63); }
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    yield return ((long)x << 32) ^ (uint)y;
        }

        /// <summary>The shapes and pictures touching an area.</summary>
        public IEnumerable<Box> InkIn(Box area)
        {
            var seen = new HashSet<int>();
            foreach (var key in Cells(area))
            {
                List<int> list;
                if (!_grid.TryGetValue(key, out list)) continue;
                foreach (var i in list)
                {
                    if (!seen.Add(i)) continue;
                    var b = _ink[i];
                    if (b.R < area.L || b.L > area.R || b.T < area.B || b.B > area.T) continue;
                    yield return b;
                }
            }
        }

        /// <summary>True when a vertical rule stands between x1 and x2 across the text band from yLow to yHigh.</summary>
        public bool VerticalRuleBetween(float x1, float x2, float yLow, float yHigh)
        {
            if (x2 < x1) { var t = x1; x1 = x2; x2 = t; }
            float mid = (yLow + yHigh) / 2f, half = Math.Max(0.5f, (yHigh - yLow) * 0.3f);
            foreach (var r in VRules)
                if (r.CX > x1 + 0.2f && r.CX < x2 - 0.2f && r.B <= mid - half && r.T >= mid + half) return true;
            return false;
        }

        /// <summary>True when a horizontal rule runs between two lines (yLow below yHigh) over most of the span xL..xR.</summary>
        public bool HorizontalRuleBetween(float yLow, float yHigh, float xL, float xR)
        {
            if (yHigh < yLow) { var t = yLow; yLow = yHigh; yHigh = t; }
            float span = Math.Max(1f, xR - xL);
            foreach (var r in HRules)
            {
                if (r.CY <= yLow + 0.2f || r.CY >= yHigh - 0.2f) continue;
                float overlap = Math.Min(r.R, xR) - Math.Max(r.L, xL);
                if (overlap >= Math.Min(span * 0.5f, r.W * 0.9f)) return true;
            }
            return false;
        }

        /// <summary>
        /// The ruled cell around a box: the nearest rules to its left, right, below and above that pass its
        /// middle (an edge with no rule is left open, as NaN).
        /// </summary>
        public void CellAround(Box b, out float left, out float right, out float bottom, out float top)
        {
            left = right = bottom = top = float.NaN;
            float cy = b.CY, cx = b.CX;
            foreach (var r in VRules)
            {
                if (r.B > cy || r.T < cy) continue;
                if (r.CX <= b.L + 0.5f && (float.IsNaN(left) || r.CX > left)) left = r.CX;
                if (r.CX >= b.R - 0.5f && (float.IsNaN(right) || r.CX < right)) right = r.CX;
            }
            foreach (var r in HRules)
            {
                if (r.L > cx || r.R < cx) continue;
                if (r.CY <= b.B + 0.5f && (float.IsNaN(bottom) || r.CY > bottom)) bottom = r.CY;
                if (r.CY >= b.T - 0.5f && (float.IsNaN(top) || r.CY < top)) top = r.CY;
            }
        }
    }
}
