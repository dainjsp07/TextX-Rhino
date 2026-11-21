using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TextX.Core.Translation;

namespace TextX.Core.Documents
{
    public enum QualityIssueKind
    {
        Failed,
        NotTranslated,
        Empty,
        NumbersDiffer,
        LinkChanged,
        GlossaryMissing,
        LengthUnusual,
        WrongAlphabet,
        DoesNotFit,
        MeaningDrift,
    }

    public sealed class QualityIssue
    {
        public DocBlock Block;
        public QualityIssueKind Kind;
        public string Message;
        /// <summary>True for problems that change the meaning (numbers, missing text); false for things worth a look.</summary>
        public bool Important;

        public override string ToString()
        {
            return Block.Id + " " + Kind + ": " + Message;
        }
    }

    /// <summary>
    /// Checks a translated document for the mistakes machine translation makes: numbers or links that
    /// changed, missing or untranslated text, glossary terms not used, text in the wrong alphabet,
    /// translations much shorter or longer than the original, and text that had to be shrunk to fit.
    /// All checks are rules and run offline; the optional back-translation check uses the service.
    /// </summary>
    public static class QualityChecker
    {
        private static readonly Regex Digits = new Regex(@"\d+", RegexOptions.Compiled);
        private static readonly Regex Links = new Regex(@"(https?://|www\.)[^\s<>""')\]]+|[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<QualityIssue> Check(DocProject project, Glossary glossary = null)
        {
            var issues = new List<QualityIssue>();
            var terms = glossary == null ? new List<GlossaryEntry>() : glossary.For(project.SourceLang, project.TargetLang).ToList();
            foreach (var b in project.AllBlocks.OrderBy(x => x.Page).ThenBy(x => x.Order)) issues.AddRange(Check(b, project, terms));
            return issues;
        }

        public static IEnumerable<QualityIssue> Check(DocBlock b, DocProject project, IList<GlossaryEntry> terms)
        {
            Func<QualityIssueKind, string, bool, QualityIssue> issue = (k, m, imp) => new QualityIssue { Block = b, Kind = k, Message = m, Important = imp };
            if (b.State == BlockState.Failed)
            {
                yield return issue(QualityIssueKind.Failed, "The translation service could not translate this text.", true);
                yield break;
            }
            if (b.State == BlockState.Pending)
            {
                if (b.NeedsTranslation) yield return issue(QualityIssueKind.NotTranslated, "Not translated yet.", true);
                yield break;
            }
            if (b.State == BlockState.Kept || b.Translation == null) yield break;

            string src = b.Source ?? string.Empty, tr = b.Translation;
            if (tr.Trim().Length == 0)
            {
                yield return issue(QualityIssueKind.Empty, "The translation is empty.", true);
                yield break;
            }

            var a = Digits.Matches(src).Cast<Match>().Select(m => m.Value.TrimStart('0')).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var c = Digits.Matches(tr).Cast<Match>().Select(m => m.Value.TrimStart('0')).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (!a.SequenceEqual(c) && string.Concat(Digits.Matches(src).Cast<Match>().Select(m => m.Value)) != string.Concat(Digits.Matches(tr).Cast<Match>().Select(m => m.Value)))
            {
                var missing = Minus(a, c);
                var added = Minus(c, a);
                string detail = missing.Count > 0 ? "missing " + string.Join(", ", missing.Take(4)) : string.Empty;
                if (added.Count > 0) detail += (detail.Length > 0 ? "; " : string.Empty) + "added " + string.Join(", ", added.Take(4));
                yield return issue(QualityIssueKind.NumbersDiffer, "Numbers differ from the original (" + detail + ").", true);
            }

            foreach (Match m in Links.Matches(src))
            {
                var link = m.Value.TrimEnd('.', ',', ';', ':');
                if (tr.IndexOf(link, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    yield return issue(QualityIssueKind.LinkChanged, "The link or e-mail address “" + link + "” was changed.", true);
                    break;
                }
            }

            foreach (var e in terms)
            {
                if (string.IsNullOrWhiteSpace(e.Source) || string.IsNullOrWhiteSpace(e.Target)) continue;
                if (DocumentTranslator.ContainsTerm(src, e.Source) && tr.IndexOf(e.Target, StringComparison.CurrentCultureIgnoreCase) < 0)
                    yield return issue(QualityIssueKind.GlossaryMissing, "Glossary: “" + e.Source + "” should be translated as “" + e.Target + "”.", false);
            }

            bool sameText = string.Equals(Squash(src), Squash(tr), StringComparison.OrdinalIgnoreCase);
            int words = WordCount(src);
            if (sameText && words >= 3 && !string.Equals(project.SourceLang, project.TargetLang, StringComparison.OrdinalIgnoreCase))
                yield return issue(QualityIssueKind.NotTranslated, "The translation is identical to the original.", false);

            if (!sameText && src.Trim().Length >= 25)
            {
                double ratio = Weight(tr) / Math.Max(1.0, Weight(src));
                if (ratio < 0.4) yield return issue(QualityIssueKind.LengthUnusual, "Much shorter than the original: part of the text may be missing.", true);
                else if (ratio > 2.8) yield return issue(QualityIssueKind.LengthUnusual, "Much longer than the original: check for repeated or added text.", false);
            }

            if (words >= 2 && !sameText)
            {
                var expected = ScriptOf(project.TargetLang);
                var found = LanguageGuess.FromScript(tr);
                string foundScript = found == null ? (HasLatin(tr) ? "latin" : null) : ScriptOf(found);
                if (foundScript != null && expected != null && foundScript != expected)
                    yield return issue(QualityIssueKind.WrongAlphabet, "The translation is not written in " + Languages.EnglishName(project.TargetLang) + ".", true);
            }

            if (!string.IsNullOrEmpty(b.Note) && (b.Note.StartsWith("The translation is longer", StringComparison.Ordinal) || b.Note.StartsWith("Text reduced", StringComparison.Ordinal)))
                yield return issue(QualityIssueKind.DoesNotFit, b.Note, false);
        }

        /// <summary>
        /// Translates the translation back and compares it with the original: very different wording
        /// often means the meaning drifted. Costs one request per batch; run it on demand.
        /// </summary>
        public static async Task<List<QualityIssue>> BackTranslateCheckAsync(TranslationEngine engine, DocProject project, IList<DocBlock> blocks, CancellationToken ct)
        {
            var list = blocks.Where(b => (b.State == BlockState.Translated || b.State == BlockState.Edited) && !string.IsNullOrWhiteSpace(b.Translation) && WordCount(b.Source) >= 4).ToList();
            var issues = new List<QualityIssue>();
            if (list.Count == 0) return issues;
            var outcome = await engine.TranslateManyAsync(list.Select(b => b.Translation).ToList(), project.TargetLang, project.SourceLang, ct).ConfigureAwait(false);
            for (int i = 0; i < list.Count && i < outcome.Translations.Count; i++)
            {
                var back = outcome.Translations[i];
                if (string.IsNullOrWhiteSpace(back)) continue;
                double sim = Similarity(list[i].Source, back);
                if (sim < 0.3)
                    issues.Add(new QualityIssue { Block = list[i], Kind = QualityIssueKind.MeaningDrift, Important = false, Message = "Translated back, this reads differently from the original (" + (int)Math.Round(sim * 100) + "% similar): “" + Shorten(back, 120) + "”" });
            }
            return issues;
        }

        /// <summary>Word overlap (0-1) of two texts in the same language; CJK text is compared by character pairs.</summary>
        public static double Similarity(string a, string b)
        {
            var x = Tokens(a);
            var y = Tokens(b);
            if (x.Count == 0 || y.Count == 0) return 0;
            int common = x.Intersect(y).Count();
            return 2.0 * common / (x.Count + y.Count);
        }

        internal static HashSet<string> Tokens(string text)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return set;
            var lower = text.ToLowerInvariant();
            var word = new System.Text.StringBuilder();
            char prevCjk = '\0';
            foreach (var ch in lower)
            {
                if (IsCjk(ch))
                {
                    if (word.Length > 0) { Add(set, word.ToString()); word.Clear(); }
                    if (prevCjk != '\0') set.Add(new string(new[] { prevCjk, ch }));
                    else set.Add(ch.ToString());
                    prevCjk = ch;
                    continue;
                }
                prevCjk = '\0';
                if (char.IsLetterOrDigit(ch)) word.Append(ch);
                else if (word.Length > 0) { Add(set, word.ToString()); word.Clear(); }
            }
            if (word.Length > 0) Add(set, word.ToString());
            return set;
        }

        private static void Add(HashSet<string> set, string w)
        {
            // crude stemming so "doors" matches "door"
            if (w.Length > 4 && w.EndsWith("s", StringComparison.Ordinal)) w = w.Substring(0, w.Length - 1);
            if (w.Length >= 2 || char.IsDigit(w[0])) set.Add(w);
        }

        private static List<string> Minus(List<string> a, List<string> b)
        {
            var rest = new List<string>(b);
            var result = new List<string>();
            foreach (var x in a) { if (!rest.Remove(x)) result.Add(x); }
            return result;
        }

        private static string Squash(string s)
        {
            return Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim();
        }

        internal static int WordCount(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int cjk = s.Count(IsCjk);
            return Regex.Matches(s, @"[^\W\d_]{2,}").Count + cjk / 2;
        }

        /// <summary>Length in "Latin character equivalents": ideographs and syllables carry more meaning each.</summary>
        private static double Weight(string s)
        {
            double w = 0;
            foreach (var ch in s)
            {
                if (char.IsWhiteSpace(ch)) w += 1;
                else if (IsCjk(ch)) w += 2.6;
                else if (ch >= 0xAC00 && ch <= 0xD7AF) w += 2.2;
                else w += 1;
            }
            return w;
        }

        internal static bool IsCjk(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x3040 && c <= 0x30FF) || (c >= 0xF900 && c <= 0xFAFF);
        }

        private static bool HasLatin(string s)
        {
            int latin = 0, letters = 0;
            foreach (var c in s)
            {
                if (!char.IsLetter(c)) continue;
                letters++;
                if (c < 0x0250) latin++;
            }
            return letters > 0 && latin * 2 > letters;
        }

        /// <summary>The alphabet a language is written in (null when unknown).</summary>
        internal static string ScriptOf(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return null;
            var code = lang.ToLowerInvariant();
            int dash = code.IndexOf('-');
            var baseCode = dash > 0 ? code.Substring(0, dash) : code;
            switch (baseCode)
            {
                case "ar": case "fa": case "ur": case "ps": case "sd": case "ug": return "arabic";
                case "he": case "iw": case "yi": return "hebrew";
                case "ru": case "uk": case "bg": case "sr": case "mk": case "be": case "kk": case "ky": case "mn": case "tg": return "cyrillic";
                case "el": return "greek";
                case "zh": case "ja": return "cjk";
                case "ko": return "hangul";
                case "th": return "thai";
                case "hi": case "mr": case "ne": return "devanagari";
                case "ka": return "georgian";
                case "hy": return "armenian";
                case "bn": case "ta": case "te": case "gu": case "pa": case "kn": case "ml": case "si": case "km": case "lo": case "my": case "am": return null;
                default: return "latin";
            }
        }

        private static string Shorten(string s, int max)
        {
            s = Squash(s);
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
