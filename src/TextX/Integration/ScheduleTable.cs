using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using TextX.Core.Documents.Schedules;
using TextX.Core.Text;

namespace TextX.Integration
{
    /// <summary>A schedule laid out as a table: cell texts and grid lines, in plane coordinates (origin top-left, y up).</summary>
    public sealed class TableLayoutResult
    {
        public sealed class Cell
        {
            public string Text;
            public double X, Y;      // top-left (or top-right for right-to-left text) of the text
            public double Wrap;      // wrap width, 0 = one line
            public bool Bold;
            public bool RightToLeft;
            public double Height;    // text height
        }

        public double Width, Height, TextHeight;
        public readonly List<Cell> Cells = new List<Cell>();
        public readonly List<Line> Lines = new List<Line>();
    }

    /// <summary>Draws schedules in Rhino as text and lines (grouped), sized from the real text widths.</summary>
    public static class ScheduleTable
    {
        public static TableLayoutResult Layout(RhinoDoc doc, DimensionStyle style, Schedule schedule, double textHeight, string documentName)
        {
            double h = Math.Max(1e-6, textHeight), pad = 0.45 * h, maxWidth = 38 * h, minWidth = 2.5 * h;
            var result = new TableLayoutResult { TextHeight = h };
            var cfg = RenderConfig.FromSettings(TextXPlugIn.Instance.Options);

            // columns with something in them (empty Manufacturer / Model columns are left out)
            var cols = Enumerable.Range(0, schedule.Columns.Count)
                .Where(c => c == 0 || schedule.Columns[c] == ScheduleExtractor.Description || schedule.Rows.Any(r => r[c].Trim().Length > 0)).ToList();
            Func<string, string> display = t =>
            {
                t = RtlText.NormalizeNewlines(t ?? string.Empty);
                return RtlText.ContainsRtl(t) ? DisplayComposer.Compose(t, null, cfg.ScreenMode, cfg.Rtl) : t;
            };

            double y = 0;
            if (!string.IsNullOrEmpty(documentName) || !string.IsNullOrEmpty(schedule.Title))
            {
                var title = schedule.Title + (string.IsNullOrEmpty(documentName) ? string.Empty : " — " + documentName);
                result.Cells.Add(new TableLayoutResult.Cell { Text = display(title), X = 0, Y = 0, Bold = true, Height = 1.4 * h, RightToLeft = RtlText.ContainsRtl(title) });
                y = -(1.4 * h + 1.2 * h);
            }

            var widths = new double[cols.Count];
            for (int i = 0; i < cols.Count; i++)
            {
                int c = cols[i];
                double w = Measure(style, display(schedule.Columns[c]), h, true, 0).Width;
                foreach (var r in schedule.Rows) w = Math.Max(w, Math.Min(maxWidth, Measure(style, display(r[c]), h, false, 0).Width));
                widths[i] = Math.Max(minWidth, Math.Min(maxWidth, w)) + 2 * pad;
            }
            double tableWidth = widths.Sum();
            double top = y;
            result.Lines.Add(new Line(new Point3d(0, y, 0), new Point3d(tableWidth, y, 0)));

            var rows = new List<string[]> { cols.Select(c => schedule.Columns[c]).ToArray() };
            rows.AddRange(schedule.Rows.Select(r => cols.Select(c => r[c]).ToArray()));
            for (int r = 0; r < rows.Count; r++)
            {
                bool header = r == 0;
                double rowHeight = h;
                double x = 0;
                var pending = new List<TableLayoutResult.Cell>();
                for (int i = 0; i < cols.Count; i++)
                {
                    var text = display(rows[r][i]);
                    if (text.Trim().Length > 0)
                    {
                        double inner = widths[i] - 2 * pad;
                        var single = Measure(style, text, h, header, 0);
                        bool wrap = single.Width > inner + 1e-9;
                        var size = wrap ? Measure(style, text, h, header, inner) : single;
                        bool rtl = RtlText.ContainsRtl(rows[r][i]);
                        pending.Add(new TableLayoutResult.Cell { Text = text, X = rtl ? x + widths[i] - pad : x + pad, Y = y - pad, Wrap = wrap ? inner : 0, Bold = header, Height = h, RightToLeft = rtl });
                        rowHeight = Math.Max(rowHeight, size.Height);
                    }
                    x += widths[i];
                }
                result.Cells.AddRange(pending);
                y -= rowHeight + 2 * pad;
                result.Lines.Add(new Line(new Point3d(0, y, 0), new Point3d(tableWidth, y, 0)));
                if (header) result.Lines.Add(new Line(new Point3d(0, y + h * 0.08, 0), new Point3d(tableWidth, y + h * 0.08, 0))); // double rule under the heading
            }
            double edge = 0;
            result.Lines.Add(new Line(new Point3d(0, top, 0), new Point3d(0, y, 0)));
            foreach (var w in widths)
            {
                edge += w;
                result.Lines.Add(new Line(new Point3d(edge, top, 0), new Point3d(edge, y, 0)));
            }
            result.Width = tableWidth;
            result.Height = -y;
            return result;
        }

        /// <summary>Adds the table to the document on the plane (origin = top-left corner). Returns the number of objects.</summary>
        public static int Add(RhinoDoc doc, RhinoView view, Plane plane, TableLayoutResult table)
        {
            var style = doc.DimStyles.Current;
            var attrs = doc.CreateDefaultAttributes();
            var page = view as RhinoPageView;
            if (page != null && page.PageIsActive)
            {
                attrs.Space = ActiveSpace.PageSpace;
                attrs.ViewportId = page.MainViewport.Id;
            }
            var ids = new List<Guid>();
            uint undo = doc.BeginUndoRecord("Place schedule");
            try
            {
                foreach (var line in table.Lines)
                {
                    var id = doc.Objects.AddLine(plane.PointAt(line.FromX, line.FromY), plane.PointAt(line.ToX, line.ToY), attrs);
                    if (id != Guid.Empty) ids.Add(id);
                }
                foreach (var cell in table.Cells)
                {
                    var p = plane;
                    p.Origin = plane.PointAt(cell.X, cell.Y);
                    var te = CreateText(style, cell.Text, p, cell.Height, cell.Bold, cell.Wrap, cell.RightToLeft);
                    if (te == null) continue;
                    var id = doc.Objects.AddText(te, attrs);
                    if (id != Guid.Empty) ids.Add(id);
                }
                if (ids.Count > 1) doc.Groups.Add(ids);
                doc.Objects.UnselectAll();
                doc.Objects.Select(ids);
            }
            finally
            {
                doc.EndUndoRecord(undo);
            }
            return ids.Count;
        }

        private static TextEntity CreateText(DimensionStyle style, string text, Plane plane, double height, bool bold, double wrap, bool rightToLeft)
        {
            var te = TextEntity.Create(string.IsNullOrEmpty(text) ? " " : text, plane, style, wrap > 0, wrap, 0);
            if (te == null) return null;
            te.TextHeight = height;
            te.DimensionScale = 1.0;
            te.TextVerticalAlignment = TextVerticalAlignment.Top;
            te.TextHorizontalAlignment = rightToLeft ? TextHorizontalAlignment.Right : TextHorizontalAlignment.Left;
            if (bold) te.SetBold(true);
            return te;
        }

        private struct Size2 { public double Width, Height; }

        private static Size2 Measure(DimensionStyle style, string text, double height, bool bold, double wrap)
        {
            if (string.IsNullOrWhiteSpace(text)) return new Size2 { Width = 0, Height = height };
            var te = CreateText(style, text, Plane.WorldXY, height, bold, wrap, false);
            if (te == null) return new Size2 { Width = text.Length * height * 0.6, Height = height };
            var bb = te.GetBoundingBox(true);
            if (!bb.IsValid) return new Size2 { Width = text.Length * height * 0.6, Height = height };
            return new Size2 { Width = bb.Max.X - bb.Min.X, Height = Math.Max(height, bb.Max.Y - bb.Min.Y) };
        }
    }
}
