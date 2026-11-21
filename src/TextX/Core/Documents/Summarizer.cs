using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TextX.Core.Documents
{
    public sealed class DocSummary
    {
        public sealed class Heading
        {
            public int Level;
            public int Page;
            public string Text;
        }

        public List<Heading> Outline = new List<Heading>();
        public List<string> KeyPoints = new List<string>();
        public int Pages, ScannedPages, Words, Tables, Images;
        public string Language = string.Empty;

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append(Pages).Append(Pages == 1 ? " page" : " pages");
            if (ScannedPages > 0) sb.Append(" (").Append(ScannedPages).Append(" scanned)");
            sb.Append(", about ").Append(Words.ToString("N0")).Append(" words");
            if (Tables > 0) sb.Append(", ").Append(Tables).Append(Tables == 1 ? " table" : " tables");
            if (Images > 0) sb.Append(", ").Append(Images).Append(Images == 1 ? " image" : " images");
            if (!string.IsNullOrEmpty(Language)) sb.Append(". Language: ").Append(Language);
            sb.AppendLine(".");
            if (KeyPoints.Count > 0)
            {
                sb.AppendLine().AppendLine("Key points");
                foreach (var k in KeyPoints) sb.Append("• ").AppendLine(k);
            }
            if (Outline.Count > 0)
            {
                sb.AppendLine().AppendLine("Contents");
                foreach (var h in Outline) sb.Append(new string(' ', 2 * Math.Max(0, h.Level - 1))).Append(h.Text).Append("  (p. ").Append(h.Page + 1).AppendLine(")");
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Extractive summary: the sentences most connected to the rest of the document (TextRank),
    /// plus the outline from the headings and a few figures. Works in any language and offline;
    /// every key point is a real sentence of the document, so nothing is made up.
    /// </summary>
    public static class Summarizer
    {
        private static readonly Regex SentenceEnd = new Regex(@"(?<=[.!?;])\s+(?=[\p{Lu}\p{Lo}\d""'(«“])|(?<=[。！？؟۔।])", RegexOptions.Compiled);

        /// <param name="translated">Summarise the translation (true) or the original text.</param>
        public static DocSummary Summarize(DocProject project, bool translated = true, int maxPoints = 0)
        {
            var summary = new DocSummary
            {
                Pages = project.PageCount,
                ScannedPages = project.Pages.Count(p => p.Scanned),
                Tables = project.Pages.Sum(p => p.TableCount),
                Images = project.Pages.Sum(p => p.Images.Count),
                Language = Languages.EnglishName(translated ? project.TargetLang : project.SourceLang),
            };
            Func<DocBlock, string> text = b => translated && !string.IsNullOrWhiteSpace(b.Translation) ? b.Translation : b.Source;
            var blocks = project.Pages.SelectMany(p => p.InReadingOrder).ToList();
            summary.Words = blocks.Where(b => b.Kind != BlockKind.HeaderFooter).Sum(b => QualityChecker.WordCount(text(b)));

            foreach (var b in blocks.Where(b => b.Kind == BlockKind.Heading))
            {
                var t = Regex.Replace(text(b), @"\s+", " ").Trim();
                if (t.Length < 2 || t.Length > 120) continue;
                if (summary.Outline.Count > 0 && summary.Outline[summary.Outline.Count - 1].Text == t) continue;
                summary.Outline.Add(new DocSummary.Heading { Level = Math.Max(1, b.Level), Page = b.Page, Text = t });
            }
            if (summary.Outline.Count > 40) summary.Outline = summary.Outline.Where(h => h.Level <= 1).Take(40).ToList();

            var sentences = new List<string>();
            var leads = new HashSet<int>();
            foreach (var b in blocks.Where(b => b.Kind == BlockKind.Paragraph || b.Kind == BlockKind.ListItem))
            {
                bool first = true;
                foreach (var s in SplitSentences(text(b)))
                {
                    if (QualityChecker.WordCount(s) < 5) continue;
                    if (first) leads.Add(sentences.Count);
                    first = false;
                    sentences.Add(s);
                }
            }
            summary.KeyPoints = Rank(sentences, leads, maxPoints);
            return summary;
        }

        public static IEnumerable<string> SplitSentences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            var flat = Regex.Replace(text, @"\s+", " ").Trim();
            foreach (var part in SentenceEnd.Split(flat))
            {
                var s = part.Trim();
                if (s.Length > 0) yield return s;
            }
        }

        /// <summary>The most central sentences (TextRank), in document order, without near-duplicates.</summary>
        public static List<string> Rank(IList<string> sentences, ICollection<int> leads = null, int maxPoints = 0)
        {
            int n = sentences.Count;
            if (n == 0) return new List<string>();
            int k = maxPoints > 0 ? maxPoints : Math.Max(3, Math.Min(8, (int)Math.Round(n * 0.12)));
            if (n <= k) return sentences.ToList();

            var tokens = sentences.Select(s => QualityChecker.Tokens(s)).ToList();
            // words in most sentences carry no information (articles, "the", "de" ...)
            var df = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var set in tokens) foreach (var t in set) { int c; df.TryGetValue(t, out c); df[t] = c + 1; }
            int limit = Math.Max(3, (int)(n * 0.35));
            for (int i = 0; i < n; i++) tokens[i].RemoveWhere(t => df[t] > limit || (t.Length <= 2 && !char.IsDigit(t[0]) && !QualityChecker.IsCjk(t[0])));

            var w = new double[n, n];
            var rowSum = new double[n];
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (tokens[i].Count == 0 || tokens[j].Count == 0) continue;
                    int common = tokens[i].Count(tokens[j].Contains);
                    if (common == 0) continue;
                    double v = common / (Math.Log(tokens[i].Count + 1) + Math.Log(tokens[j].Count + 1));
                    w[i, j] = w[j, i] = v;
                    rowSum[i] += v;
                    rowSum[j] += v;
                }
            var score = Enumerable.Repeat(1.0, n).ToArray();
            for (int iter = 0; iter < 40; iter++)
            {
                var next = new double[n];
                double delta = 0;
                for (int i = 0; i < n; i++)
                {
                    double s = 0;
                    for (int j = 0; j < n; j++) if (w[j, i] > 0 && rowSum[j] > 0) s += w[j, i] / rowSum[j] * score[j];
                    next[i] = 0.15 + 0.85 * s;
                    delta += Math.Abs(next[i] - score[i]);
                }
                score = next;
                if (delta < 1e-6 * n) break;
            }
            for (int i = 0; i < n; i++)
            {
                if (leads != null && leads.Contains(i)) score[i] *= 1.15;   // opening sentences state the topic
                int len = sentences[i].Length;
                if (len > 400) score[i] *= 0.7;                              // run-on extractions read badly
                score[i] *= 1.0 + 0.1 * (1.0 - (double)i / n);                // slight preference for earlier text
            }

            var chosen = new List<int>();
            foreach (var i in Enumerable.Range(0, n).OrderByDescending(i => score[i]))
            {
                if (chosen.Count >= k) break;
                if (chosen.Any(c => QualityChecker.Similarity(sentences[c], sentences[i]) > 0.6)) continue;
                chosen.Add(i);
            }
            return chosen.OrderBy(i => i).Select(i => sentences[i]).ToList();
        }
    }
}
