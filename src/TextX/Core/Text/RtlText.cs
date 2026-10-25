using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TextX.Core.Text
{
    public enum BaseDirection
    {
        /// <summary>First strong character decides (Unicode rules P2/P3).</summary>
        Auto = 0,
        Ltr = 1,
        Rtl = 2,
    }

    /// <summary>How RTL text is written into Rhino annotation objects.</summary>
    public enum RtlRenderMode
    {
        /// <summary>
        /// Text is converted to visual order with the Unicode Bidirectional Algorithm
        /// (and Arabic shaping) before it is stored in the Rhino object. Required for
        /// Rhino 8, whose text engine draws glyphs strictly left-to-right.
        /// </summary>
        Visual = 0,

        /// <summary>Text is stored in logical (typing) order, unchanged.</summary>
        Logical = 1,
    }

    public sealed class RtlOptions
    {
        public BaseDirection Direction = BaseDirection.Auto;
        public bool ShapeArabic = true;
        public bool MirrorBrackets = true;
        public bool StripInvisibleControls = true;
        public bool ProtectRhinoFields = true;

        /// <summary>
        /// Keeps a leading +/- sign attached to its number inside RTL text (e.g. "מפלס -0.45").
        /// Strict Unicode rules would display the sign after the number ("0.45-").
        /// </summary>
        public bool KeepSignWithNumber = true;

        public RtlOptions Clone()
        {
            return (RtlOptions)MemberwiseClone();
        }
    }

    /// <summary>
    /// High-level helpers to turn logical (typed / translated) text into the visual order
    /// that Rhino's left-to-right text renderer needs, line by line.
    /// </summary>
    public static class RtlText
    {
        private const int FieldPlaceholderBase = 0xF0000; // Supplementary Private Use Area-A
        private static readonly Regex FieldRegex = new Regex("%<.*?>%", RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>True when the text contains at least one strong right-to-left character.</summary>
        public static bool ContainsRtl(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = CharExtensions.ConvertToUtf32IfPossible(text, i);
                if (cp > 0xFFFF) i++;
                if (UnicodeProps.IsStrongRtl(cp)) return true;
            }
            return false;
        }

        /// <summary>Normalizes all line breaks to '\n'.</summary>
        public static string NormalizeNewlines(string text)
        {
            if (text == null) return string.Empty;
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace((char)0x2029, '\n').Replace((char)0x2028, '\n');
        }

        public static string[] SplitLines(string text)
        {
            return NormalizeNewlines(text).Split('\n');
        }

        public static int[] ToCodePoints(string s)
        {
            var list = new List<int>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                int cp = CharExtensions.ConvertToUtf32IfPossible(s, i);
                if (cp > 0xFFFF) i++;
                list.Add(cp);
            }
            return list.ToArray();
        }

        public static string FromCodePoints(IEnumerable<int> cps)
        {
            var sb = new StringBuilder();
            foreach (var cp in cps) AppendCodePoint(sb, cp);
            return sb.ToString();
        }

        private static void AppendCodePoint(StringBuilder sb, int cp)
        {
            if (cp > 0xFFFF) sb.Append(char.ConvertFromUtf32(cp));
            else sb.Append((char)cp);
        }

        private static bool IsInvisibleControl(int cp)
        {
            switch (cp)
            {
                case 0x200E: // LRM
                case 0x200F: // RLM
                case 0x061C: // ALM
                case 0x202A: case 0x202B: case 0x202C: case 0x202D: case 0x202E:
                case 0x2066: case 0x2067: case 0x2068: case 0x2069:
                case 0xFEFF:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Converts logical text into visual (left-to-right display) order, line by line.
        /// Text without RTL characters is returned unchanged.
        /// </summary>
        public static string ToVisual(string text, RtlOptions options)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            if (options == null) options = new RtlOptions();
            var normalized = NormalizeNewlines(text);
            if (!ContainsRtl(normalized)) return normalized;

            var lines = normalized.Split('\n');
            var sb = new StringBuilder(normalized.Length + 8);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(LineToVisual(lines[i], options));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Best-effort inverse of <see cref="ToVisual"/>, for text that was typed or pasted in
        /// reversed (visual) order by other tools. Applies the same reordering again, which
        /// restores logical order for typical Hebrew/Arabic lines with embedded numbers and Latin.
        /// Arabic presentation forms are not un-shaped.
        /// </summary>
        public static string VisualToLogical(string text)
        {
            var o = new RtlOptions { Direction = BaseDirection.Rtl, ShapeArabic = false, MirrorBrackets = true };
            return ToVisual(text, o);
        }

        private static string LineToVisual(string line, RtlOptions o)
        {
            if (line.Length == 0 || !ContainsRtl(line)) return line;

            var fields = new List<string>();
            string work = line;
            if (o.ProtectRhinoFields && line.IndexOf("%<", StringComparison.Ordinal) >= 0)
            {
                work = FieldRegex.Replace(line, m =>
                {
                    fields.Add(m.Value);
                    return char.ConvertFromUtf32(FieldPlaceholderBase + fields.Count - 1);
                });
            }

            int[] cps = ToCodePoints(work);
            if (o.ShapeArabic && ArabicShaper.NeedsShaping(cps)) cps = ArabicShaper.Shape(cps, true);
            if (o.KeepSignWithNumber) cps = IsolateSignedNumbers(cps);

            int n = cps.Length;
            var types = new BidiClass[n];
            for (int i = 0; i < n; i++)
                types[i] = IsPlaceholder(cps[i], fields.Count) ? BidiClass.ON : UnicodeProps.GetBidiClass(cps[i]);

            int paraLevel = o.Direction == BaseDirection.Rtl ? 1 : (o.Direction == BaseDirection.Ltr ? 0 : -1);
            var para = new BidiParagraph(cps, types, paraLevel);
            var levels = para.GetLineLevels(0, n);
            var order = para.GetVisualOrder(0, n, true);

            FixCombiningMarks(order, cps, types, levels);

            var sb = new StringBuilder(line.Length + 4);
            foreach (var idx in order)
            {
                int cp = cps[idx];
                if (IsPlaceholder(cp, fields.Count))
                {
                    sb.Append(fields[cp - FieldPlaceholderBase]);
                    continue;
                }
                if (o.StripInvisibleControls && IsInvisibleControl(cp)) continue;
                if (o.MirrorBrackets && (levels[idx] & 1) == 1) cp = UnicodeProps.GetMirror(cp);
                AppendCodePoint(sb, cp);
            }
            return sb.ToString();
        }

        private static bool IsDigit(int cp)
        {
            return (cp >= '0' && cp <= '9') || (cp >= 0x0660 && cp <= 0x0669) || (cp >= 0x06F0 && cp <= 0x06F9);
        }

        private static bool IsSign(int cp)
        {
            return cp == '+' || cp == '-' || cp == 0x2212 || cp == 0x2013;
        }

        /// <summary>
        /// Wraps "-0.45" / "+3.20" style signed numbers in an LTR isolate (LRI ... PDI) so the sign
        /// stays in front of the number. Only applies when the sign starts a token (start of line,
        /// after whitespace or punctuation), so ranges like "1-5" and codes like "A-101" are untouched.
        /// The isolate controls are invisible and are removed from the final output.
        /// </summary>
        private static int[] IsolateSignedNumbers(int[] cps)
        {
            List<int> result = null;
            int n = cps.Length;
            for (int i = 0; i < n; i++)
            {
                int cp = cps[i];
                bool candidate = IsSign(cp) && i + 1 < n && IsDigit(cps[i + 1]);
                if (candidate && i > 0)
                {
                    var prev = UnicodeProps.GetBidiClass(cps[i - 1]);
                    candidate = prev == BidiClass.WS || prev == BidiClass.ON || prev == BidiClass.S || prev == BidiClass.B;
                    if (IsDigit(cps[i - 1])) candidate = false;
                }
                if (!candidate)
                {
                    if (result != null) result.Add(cp);
                    continue;
                }
                if (result == null)
                {
                    result = new List<int>(n + 4);
                    for (int k = 0; k < i; k++) result.Add(cps[k]);
                }
                int end = i + 1;
                while (end < n)
                {
                    if (IsDigit(cps[end])) { end++; continue; }
                    int c = cps[end];
                    if ((c == '.' || c == ',' || c == ':' || c == '/') && end + 1 < n && IsDigit(cps[end + 1])) { end += 2; continue; }
                    break;
                }
                result.Add(0x2066); // LRI
                for (int k = i; k < end; k++) result.Add(cps[k]);
                result.Add(0x2069); // PDI
                i = end - 1;
            }
            return result == null ? cps : result.ToArray();
        }

        private static bool IsPlaceholder(int cp, int count)
        {
            return cp >= FieldPlaceholderBase && cp < FieldPlaceholderBase + count;
        }

        /// <summary>
        /// Rule L3: inside reversed (RTL) runs, keep non-spacing marks after their base
        /// character so that Hebrew points and Arabic harakat attach to the right letter.
        /// </summary>
        private static void FixCombiningMarks(int[] order, int[] cps, BidiClass[] types, byte[] levels)
        {
            for (int k = 0; k < order.Length; k++)
            {
                int idx = order[k];
                if (types[idx] != BidiClass.NSM || (levels[idx] & 1) == 0) continue;
                int start = k, end = k;
                while (end + 1 < order.Length && types[order[end + 1]] == BidiClass.NSM && order[end + 1] == order[end] - 1)
                    end++;
                if (end + 1 < order.Length && order[end + 1] == order[end] - 1)
                {
                    Array.Reverse(order, start, end - start + 2);
                    k = end + 1;
                }
                else
                {
                    k = end;
                }
            }
        }

        /// <summary>
        /// Greedy word wrap of a single logical paragraph using a width measuring callback.
        /// Used for RTL text, because Rhino's own wrapping would break visual-order lines
        /// in the wrong place.
        /// </summary>
        public static List<string> WrapParagraph(string paragraph, double maxWidth, Func<string, double> measure)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(paragraph) || maxWidth <= 0 || measure == null)
            {
                result.Add(paragraph ?? string.Empty);
                return result;
            }
            var words = paragraph.Split(' ');
            var line = new StringBuilder();
            foreach (var word in words)
            {
                if (line.Length == 0)
                {
                    line.Append(word);
                    continue;
                }
                string candidate = line + " " + word;
                if (measure(candidate) <= maxWidth)
                {
                    line.Append(' ').Append(word);
                }
                else
                {
                    result.Add(line.ToString());
                    line.Clear();
                    line.Append(word);
                }
            }
            result.Add(line.ToString());
            return result;
        }

        /// <summary>Wraps every paragraph (line) of a multi-line logical text.</summary>
        public static string WrapText(string text, double maxWidth, Func<string, double> measure)
        {
            var lines = SplitLines(text);
            var output = new List<string>();
            foreach (var l in lines) output.AddRange(WrapParagraph(l, maxWidth, measure));
            return string.Join("\n", output.ToArray());
        }
    }

    internal static class CharExtensions
    {
        /// <summary>Like char.ConvertToUtf32 but tolerant of unpaired surrogates.</summary>
        public static int ConvertToUtf32IfPossible(string s, int index)
        {
            char c = s[index];
            if (char.IsHighSurrogate(c) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
                return char.ConvertToUtf32(c, s[index + 1]);
            return c;
        }
    }
}
