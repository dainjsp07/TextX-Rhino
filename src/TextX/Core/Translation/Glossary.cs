using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TextX.Core.Translation
{
    public sealed class GlossaryEntry
    {
        public string SourceLang { get; set; }
        public string TargetLang { get; set; }
        public string Source { get; set; }
        public string Target { get; set; }
    }

    /// <summary>
    /// User terminology. Two features:
    ///  * exact phrase entries: a text segment that equals the entry's source term
    ///    (case-insensitive, ignoring surrounding whitespace and a trailing ':' or '.')
    ///    uses the entry's translation instead of machine translation. Ideal for
    ///    title-block labels ("Scale", "Drawn by", "Date") so they stay consistent.
    ///  * do-not-translate terms: brand names, project codes etc. are kept verbatim.
    /// </summary>
    public sealed class Glossary
    {
        private readonly List<GlossaryEntry> _entries = new List<GlossaryEntry>();
        private readonly List<string> _doNotTranslate = new List<string>();

        public IList<GlossaryEntry> Entries { get { return _entries; } }
        public IList<string> DoNotTranslate { get { return _doNotTranslate; } }

        public static string Normalize(string s)
        {
            if (s == null) return string.Empty;
            s = s.Trim();
            while (s.Length > 0 && (s[s.Length - 1] == ':' || s[s.Length - 1] == '.')) s = s.Substring(0, s.Length - 1).TrimEnd();
            return s.ToLowerInvariant();
        }

        public void Add(string sourceLang, string targetLang, string source, string target)
        {
            if (string.IsNullOrWhiteSpace(source) || target == null) return;
            var norm = Normalize(source);
            _entries.RemoveAll(e => e.TargetLang == targetLang && LangMatches(e.SourceLang, sourceLang) && Normalize(e.Source) == norm);
            _entries.Add(new GlossaryEntry { SourceLang = sourceLang, TargetLang = targetLang, Source = source.Trim(), Target = target.Trim() });
        }

        private static bool LangMatches(string entryLang, string lang)
        {
            return string.IsNullOrEmpty(entryLang) || entryLang == "*" || lang == Languages.AutoDetect ||
                   string.Equals(entryLang, lang, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Looks up a segment. Returns the translated segment with the original leading /
        /// trailing whitespace and trailing punctuation preserved, or null if not found.
        /// </summary>
        public string Lookup(string segment, string sourceLang, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(segment) || _entries.Count == 0) return null;
            var norm = Normalize(segment);
            var entry = _entries.FirstOrDefault(e =>
                string.Equals(e.TargetLang, targetLang, StringComparison.OrdinalIgnoreCase) &&
                LangMatches(e.SourceLang, sourceLang) &&
                Normalize(e.Source) == norm);
            if (entry == null) return null;

            int lead = 0;
            while (lead < segment.Length && char.IsWhiteSpace(segment[lead])) lead++;
            int trailWs = 0;
            while (trailWs < segment.Length - lead && char.IsWhiteSpace(segment[segment.Length - 1 - trailWs])) trailWs++;
            string core = segment.Substring(lead, segment.Length - lead - trailWs);
            int punct = 0;
            while (punct < core.Length && (core[core.Length - 1 - punct] == ':' || core[core.Length - 1 - punct] == '.')) punct++;
            // keep the punctuation the user typed unless the glossary target already ends with it
            string tail = core.Substring(core.Length - punct);
            string target = entry.Target;
            if (tail.Length > 0 && target.EndsWith(tail, StringComparison.Ordinal)) tail = string.Empty;
            return segment.Substring(0, lead) + target + tail + segment.Substring(segment.Length - trailWs);
        }

        // ---------------- persistence (JSON) ----------------
        public string ToJson()
        {
            var root = new Dictionary<string, object>();
            root["entries"] = _entries.Select(e => (object)new Dictionary<string, object>
            {
                { "sourceLang", e.SourceLang ?? "" },
                { "targetLang", e.TargetLang ?? "" },
                { "source", e.Source ?? "" },
                { "target", e.Target ?? "" },
            }).ToList();
            root["doNotTranslate"] = _doNotTranslate.Cast<object>().ToList();
            return Json.Serialize(root, true);
        }

        public static Glossary FromJson(string json)
        {
            var g = new Glossary();
            if (string.IsNullOrWhiteSpace(json)) return g;
            var root = Json.Parse(json);
            var entries = Json.GetList(root, "entries");
            if (entries != null)
            {
                foreach (var e in entries)
                {
                    g._entries.Add(new GlossaryEntry
                    {
                        SourceLang = Json.GetString(e, "sourceLang"),
                        TargetLang = Json.GetString(e, "targetLang"),
                        Source = Json.GetString(e, "source"),
                        Target = Json.GetString(e, "target"),
                    });
                }
            }
            var dnt = Json.GetList(root, "doNotTranslate");
            if (dnt != null) g._doNotTranslate.AddRange(dnt.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).Where(x => !string.IsNullOrWhiteSpace(x)));
            return g;
        }

        /// <summary>
        /// Imports a CSV/TSV glossary: "source,target" or "sourceLang,targetLang,source,target".
        /// Lines starting with '#' are ignored. A row with only one column is a do-not-translate term.
        /// </summary>
        public int ImportCsv(string text, string defaultSourceLang, string defaultTargetLang)
        {
            int count = 0;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var cols = SplitCsv(line);
                if (cols.Count == 1)
                {
                    if (!_doNotTranslate.Contains(cols[0])) _doNotTranslate.Add(cols[0]);
                    count++;
                }
                else if (cols.Count == 2)
                {
                    Add(defaultSourceLang, defaultTargetLang, cols[0], cols[1]);
                    count++;
                }
                else if (cols.Count >= 4)
                {
                    Add(cols[0], cols[1], cols[2], cols[3]);
                    count++;
                }
            }
            return count;
        }

        public string ExportCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# sourceLang,targetLang,source,target");
            foreach (var e in _entries)
                sb.AppendLine(string.Join(",", new[] { e.SourceLang, e.TargetLang, e.Source, e.Target }.Select(CsvQuote)));
            foreach (var t in _doNotTranslate) sb.AppendLine(CsvQuote(t));
            return sb.ToString();
        }

        private static string CsvQuote(string s)
        {
            s = s ?? string.Empty;
            if (s.IndexOfAny(new[] { ',', '"', '\t' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static List<string> SplitCsv(string line)
        {
            var result = new List<string>();
            char sep = line.IndexOf('\t') >= 0 && line.IndexOf(',') < 0 ? '\t' : ',';
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') inQuotes = false;
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == sep) { result.Add(sb.ToString().Trim()); sb.Clear(); }
                else sb.Append(c);
            }
            result.Add(sb.ToString().Trim());
            return result;
        }

        public void Save(string path)
        {
            File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
        }

        public static Glossary Load(string path)
        {
            if (!File.Exists(path)) return new Glossary();
            try { return FromJson(File.ReadAllText(path, Encoding.UTF8)); }
            catch { return new Glossary(); }
        }
    }
}
