using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TextX.Core.Translation
{
    public sealed class TextSegment
    {
        public TextSegment(string text, bool translate)
        {
            Text = text;
            Translate = translate;
        }

        public string Text { get; private set; }

        /// <summary>False for protected content: Rhino fields, do-not-translate terms, numbers/punctuation-only.</summary>
        public bool Translate { get; private set; }

        public override string ToString() { return (Translate ? "T:" : "K:") + Text; }
    }

    /// <summary>
    /// Splits annotation text into lines and segments so that Rhino text fields
    /// (%&lt;...&gt;%), do-not-translate terms and pure numbers are never sent to a
    /// translation service, and line structure (important for title blocks) is preserved.
    /// </summary>
    public static class TextSegmenter
    {
        private static readonly Regex FieldRegex = new Regex("%<.*?>%", RegexOptions.Singleline | RegexOptions.Compiled);

        public static List<List<TextSegment>> Split(string text, IEnumerable<string> doNotTranslate)
        {
            var lines = new List<List<TextSegment>>();
            if (text == null) text = string.Empty;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            Regex dnt = BuildDoNotTranslateRegex(doNotTranslate);
            foreach (var line in text.Split('\n'))
            {
                var segs = new List<TextSegment>();
                foreach (var piece in SplitByRegex(line, FieldRegex))
                {
                    if (piece.Value) { segs.Add(new TextSegment(piece.Key, false)); continue; }
                    if (dnt == null) { AddText(segs, piece.Key); continue; }
                    foreach (var sub in SplitByRegex(piece.Key, dnt))
                    {
                        if (sub.Value) segs.Add(new TextSegment(sub.Key, false));
                        else AddText(segs, sub.Key);
                    }
                }
                lines.Add(segs);
            }
            return lines;
        }

        private static void AddText(List<TextSegment> segs, string text)
        {
            if (text.Length == 0) return;
            segs.Add(new TextSegment(text, HasLetters(text) && !IsCodeLike(text.Trim())));
        }

        /// <summary>
        /// Drawing / sheet codes such as "A-101", "DWG02", "R1.2" are kept verbatim:
        /// a single token (no spaces) that mixes Latin letters with digits.
        /// </summary>
        public static bool IsCodeLike(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 24) return false;
            bool digit = false, letter = false;
            foreach (var c in token)
            {
                if (char.IsWhiteSpace(c)) return false;
                if (char.IsDigit(c)) digit = true;
                else if (char.IsLetter(c))
                {
                    if (c > 0x024F) return false; // only Latin letters
                    letter = true;
                }
            }
            return digit && letter;
        }

        public static bool HasLetters(string s)
        {
            foreach (var c in s) if (char.IsLetter(c)) return true;
            return false;
        }

        private static Regex BuildDoNotTranslateRegex(IEnumerable<string> terms)
        {
            if (terms == null) return null;
            var list = terms.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().OrderByDescending(t => t.Length).ToList();
            if (list.Count == 0) return null;
            var pattern = "(?<![\\p{L}\\p{N}])(?:" + string.Join("|", list.Select(Regex.Escape)) + ")(?![\\p{L}\\p{N}])";
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>Returns pieces of the input; Value = true for regex matches.</summary>
        private static IEnumerable<KeyValuePair<string, bool>> SplitByRegex(string input, Regex regex)
        {
            int pos = 0;
            foreach (Match m in regex.Matches(input))
            {
                if (m.Index > pos) yield return new KeyValuePair<string, bool>(input.Substring(pos, m.Index - pos), false);
                yield return new KeyValuePair<string, bool>(m.Value, true);
                pos = m.Index + m.Length;
            }
            if (pos < input.Length) yield return new KeyValuePair<string, bool>(input.Substring(pos), false);
        }

        /// <summary>Splits a segment into leading whitespace, core text and trailing whitespace.</summary>
        public static void SplitWhitespace(string s, out string lead, out string core, out string trail)
        {
            int a = 0;
            while (a < s.Length && char.IsWhiteSpace(s[a])) a++;
            int b = s.Length;
            while (b > a && char.IsWhiteSpace(s[b - 1])) b--;
            lead = s.Substring(0, a);
            core = s.Substring(a, b - a);
            trail = s.Substring(b);
        }

        public static string Join(List<List<TextSegment>> lines, Func<TextSegment, string> map)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                foreach (var s in lines[i]) sb.Append(s.Translate ? map(s) : s.Text);
            }
            return sb.ToString();
        }
    }
}
