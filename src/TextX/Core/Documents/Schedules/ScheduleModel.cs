using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TextX.Core.Documents.Schedules
{
    public enum ScheduleKind
    {
        Products,
        Materials,
        ProjectInfo,
        Dimensions,
        Notes,
        Table,
    }

    /// <summary>One row of a schedule, with where it was found in the document.</summary>
    public sealed class ScheduleRow
    {
        public List<string> Cells = new List<string>();
        public int Page = -1;
        public Box Box;
        public bool HasBox;
        /// <summary>Ids of the document blocks the row was read from (to show them on the page).</summary>
        public List<string> Blocks = new List<string>();
        /// <summary>True when the user changed the row (kept when the schedule is read again).</summary>
        public bool Edited;

        public string this[int i]
        {
            get { return i >= 0 && i < Cells.Count ? Cells[i] ?? string.Empty : string.Empty; }
            set
            {
                while (Cells.Count <= i) Cells.Add(string.Empty);
                Cells[i] = value ?? string.Empty;
            }
        }
    }

    /// <summary>A table of extracted information: products, materials, project details ...</summary>
    public sealed class Schedule
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Title = string.Empty;
        public ScheduleKind Kind;
        public List<string> Columns = new List<string>();
        public List<ScheduleRow> Rows = new List<ScheduleRow>();

        public Schedule() { }

        public Schedule(ScheduleKind kind, string title, params string[] columns)
        {
            Kind = kind;
            Title = title;
            Columns.AddRange(columns);
        }

        public int Column(string name)
        {
            return Columns.FindIndex(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        }

        public ScheduleRow Add(int page, params string[] cells)
        {
            var row = new ScheduleRow { Page = page };
            row.Cells.AddRange(cells.Select(c => c ?? string.Empty));
            while (row.Cells.Count < Columns.Count) row.Cells.Add(string.Empty);
            Rows.Add(row);
            return row;
        }

        /// <summary>Rows containing the text in any cell (case and accent insensitive).</summary>
        public IEnumerable<ScheduleRow> Search(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Rows;
            var needle = Vocabulary.Fold(text.Trim());
            return Rows.Where(r => r.Cells.Any(c => Vocabulary.Fold(c ?? string.Empty).Contains(needle)));
        }

        /// <summary>Sorts by a column: numbers and codes in natural order ("P-2" before "P-10").</summary>
        public void Sort(int column, bool descending = false)
        {
            if (column < 0) return;
            // empty cells stay at the end either way
            var filled = Rows.Where(r => r[column].Trim().Length > 0).OrderBy(r => r[column], NaturalComparer.Instance).ToList();
            if (descending) filled.Reverse();
            filled.AddRange(Rows.Where(r => r[column].Trim().Length == 0));
            Rows = filled;
        }

        internal Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>
            {
                { "id", Id }, { "title", Title }, { "kind", Kind.ToString() },
                { "columns", Columns.Cast<object>().ToList() },
                { "rows", Rows.Select(r =>
                    {
                        var o = new Dictionary<string, object> { { "cells", r.Cells.Cast<object>().ToList() }, { "page", r.Page } };
                        if (r.HasBox) o["box"] = new List<object> { Math.Round(r.Box.L, 1), Math.Round(r.Box.B, 1), Math.Round(r.Box.R, 1), Math.Round(r.Box.T, 1) };
                        if (r.Blocks.Count > 0) o["blocks"] = r.Blocks.Cast<object>().ToList();
                        if (r.Edited) o["edited"] = true;
                        return (object)o;
                    }).ToList() },
            };
        }

        internal static Schedule FromJson(object node)
        {
            var s = new Schedule
            {
                Id = Json.GetString(node, "id") ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                Title = Json.GetString(node, "title") ?? string.Empty,
            };
            ScheduleKind kind;
            if (Enum.TryParse(Json.GetString(node, "kind") ?? string.Empty, out kind)) s.Kind = kind;
            foreach (var c in Json.GetList(node, "columns") ?? new List<object>()) s.Columns.Add(Convert.ToString(c, CultureInfo.InvariantCulture));
            foreach (var r in Json.GetList(node, "rows") ?? new List<object>())
            {
                var row = new ScheduleRow();
                foreach (var c in Json.GetList(r, "cells") ?? new List<object>()) row.Cells.Add(Convert.ToString(c, CultureInfo.InvariantCulture));
                var page = Json.Get(r, "page");
                if (page != null) row.Page = Convert.ToInt32(page, CultureInfo.InvariantCulture);
                var box = Json.GetList(r, "box");
                if (box != null && box.Count == 4)
                {
                    row.Box = new Box(Convert.ToSingle(box[0], CultureInfo.InvariantCulture), Convert.ToSingle(box[1], CultureInfo.InvariantCulture),
                                      Convert.ToSingle(box[2], CultureInfo.InvariantCulture), Convert.ToSingle(box[3], CultureInfo.InvariantCulture));
                    row.HasBox = true;
                }
                foreach (var b in Json.GetList(r, "blocks") ?? new List<object>()) row.Blocks.Add(Convert.ToString(b, CultureInfo.InvariantCulture));
                row.Edited = Json.Get(r, "edited") is bool && (bool)Json.Get(r, "edited");
                s.Rows.Add(row);
            }
            return s;
        }
    }

    /// <summary>All schedules read from one document.</summary>
    public sealed class ScheduleSet
    {
        public string DocumentName = string.Empty;
        public DateTime Created = DateTime.UtcNow;
        public List<Schedule> Schedules = new List<Schedule>();

        public Schedule Find(ScheduleKind kind)
        {
            return Schedules.FirstOrDefault(s => s.Kind == kind);
        }

        public string ToJson()
        {
            return Json.Serialize(new Dictionary<string, object>
            {
                { "format", "textx-schedules" }, { "version", 1 }, { "document", DocumentName },
                { "created", Created.ToString("o", CultureInfo.InvariantCulture) },
                { "schedules", Schedules.Select(s => (object)s.ToJson()).ToList() },
            }, true);
        }

        public static ScheduleSet FromJson(string json)
        {
            var node = Json.Parse(json);
            var set = new ScheduleSet { DocumentName = Json.GetString(node, "document") ?? string.Empty };
            DateTime created;
            if (DateTime.TryParse(Json.GetString(node, "created"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out created)) set.Created = created;
            foreach (var s in Json.GetList(node, "schedules") ?? new List<object>()) set.Schedules.Add(Schedule.FromJson(s));
            return set;
        }
    }

    /// <summary>Compares text with embedded numbers by value: "D-2" &lt; "D-10", "9" &lt; "10".</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();

        public int Compare(string a, string b)
        {
            a = a ?? string.Empty; b = b ?? string.Empty;
            if (a.Length == 0 || b.Length == 0) return b.Length.CompareTo(a.Length); // empty last
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    var na = a.Substring(si, i - si).TrimStart('0');
                    var nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = string.Compare(a[i].ToString(), b[j].ToString(), StringComparison.CurrentCultureIgnoreCase);
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
