using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TextX.Core
{
    /// <summary>
    /// Minimal, dependency-free JSON reader/writer (objects map to Dictionary&lt;string, object&gt;,
    /// arrays to List&lt;object&gt;, numbers to double). Avoids depending on a particular
    /// Newtonsoft/System.Text.Json version, which differs between Rhino's .NET runtimes.
    /// </summary>
    public static class Json
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            int i = 0;
            var value = ParseValue(json, ref i);
            SkipWs(json, ref i);
            if (i != json.Length) throw new FormatException("Unexpected trailing characters at " + i);
            return value;
        }

        public static string Serialize(object value, bool indent = false)
        {
            var sb = new StringBuilder();
            Write(sb, value, indent, 0);
            return sb.ToString();
        }

        // -------- navigation helpers
        public static object Get(object node, params object[] path)
        {
            object cur = node;
            foreach (var key in path)
            {
                if (cur == null) return null;
                if (key is string)
                {
                    var d = cur as Dictionary<string, object>;
                    object next;
                    if (d == null || !d.TryGetValue((string)key, out next)) return null;
                    cur = next;
                }
                else
                {
                    var l = cur as List<object>;
                    int idx = Convert.ToInt32(key, CultureInfo.InvariantCulture);
                    if (l == null || idx < 0 || idx >= l.Count) return null;
                    cur = l[idx];
                }
            }
            return cur;
        }

        public static string GetString(object node, params object[] path)
        {
            var v = Get(node, path);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static List<object> GetList(object node, params object[] path)
        {
            return Get(node, path) as List<object>;
        }

        // -------- parser
        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (c == 'f' && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (c == 'n' && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("Expected property name at " + i);
                var key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':' at " + i);
                i++;
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new FormatException("Expected ',' or '}' at " + i);
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var l = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return l; }
                throw new FormatException("Expected ',' or ']' at " + i);
            }
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape \\" + e);
                }
            }
            throw new FormatException("Unterminated string");
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("Unexpected character '" + s[i] + "' at " + i);
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // -------- writer
        private static void Write(StringBuilder sb, object v, bool indent, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            var str = v as string;
            if (str != null) { WriteString(sb, str); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int || v is long || v is double || v is float || v is decimal || v is short)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (indent) { sb.Append('\n').Append(' ', (depth + 1) * 2); }
                    WriteString(sb, kv.Key);
                    sb.Append(indent ? ": " : ":");
                    Write(sb, kv.Value, indent, depth + 1);
                }
                if (indent && dict.Count > 0) sb.Append('\n').Append(' ', depth * 2);
                sb.Append('}');
                return;
            }
            var sdict = v as IDictionary<string, string>;
            if (sdict != null)
            {
                var copy = new Dictionary<string, object>();
                foreach (var kv in sdict) copy[kv.Key] = kv.Value;
                Write(sb, copy, indent, depth);
                return;
            }
            var list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                int count = 0;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (indent) { sb.Append('\n').Append(' ', (depth + 1) * 2); }
                    Write(sb, item, indent, depth + 1);
                    count++;
                }
                if (indent && count > 0) sb.Append('\n').Append(' ', depth * 2);
                sb.Append(']');
                return;
            }
            WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == 0x2028 || c == 0x2029)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder();
            WriteString(sb, s ?? string.Empty);
            return sb.ToString();
        }
    }
}
