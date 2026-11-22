using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TextX.Core.Documents.Fonts;
using TextX.Core.Text;

namespace TextX.Core.Documents.Layout
{
    public sealed class FittedLine
    {
        public string Logical = string.Empty;  // reading order
        public string Visual = string.Empty;   // drawing order (RTL lines reordered and shaped)
        public List<TextRun> Runs = new List<TextRun>();
        public float Width;
    }

    public sealed class FitResult
    {
        public float Size;          // font size used
        public float Pitch;         // distance between baselines, points
        public List<FittedLine> Lines = new List<FittedLine>();
        public bool Overflow;       // did not fit even at the smallest size
        public float Scale;         // Size / requested size
        public float Height { get { return Lines.Count == 0 ? 0 : Size + (Lines.Count - 1) * Pitch; } }
    }

    /// <summary>
    /// Fits translated text into the box the original text used: wraps it at the box width with
    /// exact font metrics and reduces the size step by step (down to <c>minScale</c>) until it fits.
    /// Labels prefer to stay on one line. Right-to-left lines are wrapped in reading order and
    /// then reordered for drawing.
    /// </summary>
    public static class TextFitter
    {
        private const string NoLineStart = "、。，．・：；？！）」』】〕〉》〟’”…‥ー々ゝゞヽヾ,.!?)]}:;%";

        public static FitResult Fit(string text, float width, float height, float size, float pitchFactor, bool singleLine,
                                    bool rtl, FontFace primary, IList<FontFace> fallbacks, float minScale = 0.55f)
        {
            text = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            if (pitchFactor < 0.9f || pitchFactor > 3f) pitchFactor = 1.2f;
            width = Math.Max(width, size * 2);
            float minSize = Math.Max(3.5f, size * minScale);
            FitResult best = null;
            // labels: one line while the text keeps three quarters of its size, then wrapping where the height
            // allows, then one smaller line; other text wraps
            var passes = singleLine
                ? new[] { new KeyValuePair<bool, float>(true, Math.Max(minSize, size * 0.75f)), new KeyValuePair<bool, float>(false, minSize), new KeyValuePair<bool, float>(true, minSize) }
                : new[] { new KeyValuePair<bool, float>(false, minSize) };
            foreach (var pass in passes)
            {
                bool oneLine = pass.Key;
                float floor = pass.Value;
                for (float s = size; ; s *= 0.94f)
                {
                    if (s < floor) s = floor;
                    // a word is cut between letters only as a last resort, at the smallest size
                    bool cut;
                    var lines = Wrap(text, width * 1.02f, s, primary, fallbacks, out cut);
                    // (a label rather gets smaller on one line than a word cut in two)
                    if (cut && !oneLine && (singleLine || s > floor))
                    {
                        if (s <= floor) break;
                        continue;
                    }
                    if (cut && !oneLine)
                    {
                        // at the smallest size: a word a little wider than the box stands whole on its line
                        bool still;
                        var whole = Wrap(text, width * 1.02f, s, primary, fallbacks, out still, 1.6f);
                        if (!still) lines = whole;
                    }
                    var r = new FitResult { Size = s, Pitch = s * pitchFactor, Scale = s / size };
                    r.Lines = lines.Select(l => MakeLine(l, s, rtl, primary, fallbacks)).ToList();
                    // one line may reach a little past the box; more lines must stay inside it (the text below is close)
                    bool fitsHeight = r.Height <= height + s * (r.Lines.Count <= 1 ? 0.35f : 0.1f);
                    if (fitsHeight && (!oneLine || lines.Count <= 1)) return r;
                    if (best == null || !oneLine) best = r;
                    if (s <= floor) break;
                }
            }
            // nothing fits: keep the smallest size; a label stays on one line
            if (singleLine)
            {
                var one = MakeLine(text.Replace('\n', ' '), minSize, rtl, primary, fallbacks);
                best = new FitResult { Size = minSize, Pitch = minSize * pitchFactor, Scale = minSize / size, Lines = new List<FittedLine> { one } };
            }
            best.Overflow = true;
            return best;
        }

        private static FittedLine MakeLine(string logical, float size, bool rtl, FontFace primary, IList<FontFace> fallbacks)
        {
            var line = new FittedLine { Logical = logical };
            line.Visual = rtl || RtlText.ContainsRtl(logical)
                ? RtlText.ToVisual(logical, new RtlOptions { Direction = rtl ? BaseDirection.Rtl : BaseDirection.Ltr })
                : logical;
            line.Runs = FontCatalog.Runs(line.Visual, primary, fallbacks, size);
            line.Width = line.Runs.Sum(r => r.Width);
            return line;
        }

        /// <summary>Greedy word wrap at <paramref name="width"/>; explicit newlines are kept.</summary>
        public static List<string> Wrap(string text, float width, float size, FontFace primary, IList<FontFace> fallbacks)
        {
            bool cut;
            return Wrap(text, width, size, primary, fallbacks, out cut);
        }

        /// <summary>Word wrap; <paramref name="cutWord"/> tells whether a word longer than a line had to be cut.</summary>
        public static List<string> Wrap(string text, float width, float size, FontFace primary, IList<FontFace> fallbacks, out bool cutWord, float overhang = 1f)
        {
            cutWord = false;
            var result = new List<string>();
            foreach (var paragraph in text.Split('\n'))
            {
                var tokens = Tokens(paragraph);
                var line = new StringBuilder();
                float lineWidth = 0;
                foreach (var token in tokens)
                {
                    float w = FontCatalog.Measure(token, primary, fallbacks, size);
                    if (line.Length > 0 && lineWidth + w > width && !string.IsNullOrWhiteSpace(token) && !NoLineStart.Contains(token[0]))
                    {
                        result.Add(line.ToString().TrimEnd());
                        line.Clear();
                        lineWidth = 0;
                        if (token == " ") continue;
                    }
                    if (line.Length == 0 && token == " ") continue;
                    if (w > width && w <= width * overhang && !string.IsNullOrWhiteSpace(token))
                    {
                        // a word a little longer than the line: whole, on a line of its own
                        if (line.Length > 0) { result.Add(line.ToString().TrimEnd()); line.Clear(); }
                        line.Append(token);
                        lineWidth = w;
                        continue;
                    }
                    if (w > width && token.Length > 1 && !string.IsNullOrWhiteSpace(token))
                    {
                        // a word longer than the line: break it between characters
                        cutWord = true;
                        foreach (var ch in SplitChars(token))
                        {
                            float cw = FontCatalog.Measure(ch, primary, fallbacks, size);
                            if (line.Length > 0 && lineWidth + cw > width)
                            {
                                result.Add(line.ToString());
                                line.Clear();
                                lineWidth = 0;
                            }
                            line.Append(ch);
                            lineWidth += cw;
                        }
                        continue;
                    }
                    line.Append(token);
                    lineWidth += w;
                }
                result.Add(line.ToString().TrimEnd());
            }
            while (result.Count > 1 && result[result.Count - 1].Length == 0) result.RemoveAt(result.Count - 1);
            return result;
        }

        /// <summary>
        /// Words with their trailing punctuation; spaces as separate tokens; CJK characters one by one.
        /// Scripts without spaces and with combining marks (Thai, Khmer ...) stay whole and are only
        /// split when longer than a line.
        /// </summary>
        private static List<string> Tokens(string s)
        {
            var tokens = new List<string>();
            var word = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == 0x3000)
                {
                    if (word.Length > 0) { tokens.Add(word.ToString()); word.Clear(); }
                    tokens.Add(" ");
                }
                else if (LayoutAnalyzer.IsCjk(c))
                {
                    if (word.Length > 0) { tokens.Add(word.ToString()); word.Clear(); }
                    tokens.Add(c.ToString());
                }
                else
                {
                    word.Append(c);
                    // allow a break after a hyphen or slash inside long words
                    if ((c == '-' || c == '/') && word.Length > 3) { tokens.Add(word.ToString()); word.Clear(); }
                }
            }
            if (word.Length > 0) tokens.Add(word.ToString());
            return tokens;
        }

        private static IEnumerable<string> SplitChars(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length) { yield return s.Substring(i, 2); i++; }
                else yield return s[i].ToString();
            }
        }
    }
}
