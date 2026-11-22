using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TextX.Core.Translation;
using TextX.Core.Translation.Providers;

namespace TextX.Core.Documents
{
    public sealed class TranslateProgress
    {
        public int Done, Total;
        public string Status = string.Empty;
        public TimeSpan Elapsed;
        public TimeSpan? Remaining;
        public double Fraction { get { return Total == 0 ? 1 : Done / (double)Total; } }
    }

    /// <summary>
    /// Translates a document paragraph by paragraph (whole paragraphs keep each sentence's context),
    /// in batches so large documents stay fast and can be resumed. Numbers, codes and dimensions are
    /// kept as they are, glossary terms are enforced inside sentences, and the user's own corrections
    /// are remembered and reused.
    /// </summary>
    public sealed class DocumentTranslator
    {
        private readonly TranslationEngine _engine;

        public DocumentTranslator(TranslationEngine engine)
        {
            _engine = engine;
        }

        public TranslationEngine Engine { get { return _engine; } }

        /// <summary>Called after every batch (save the project there so work survives interruptions).</summary>
        public Action<DocProject> Checkpoint { get; set; }

        /// <summary>Characters per batch: one request to the free Google service, a progress step.</summary>
        public int BatchChars = 4000;

        private static readonly HashSet<string> NameFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "Architect", "Interior designer", "Drawn by", "Checked by", "Approved by", "Client", "Contractor", "Engineer", "Manufacturer",
        };

        /// <summary>
        /// Names in title blocks (the architect, who drew or checked the drawing, the client ...): kept as
        /// they are, since translating "Atelier Moreau" or "J. Smith" makes them wrong.
        /// </summary>
        internal static HashSet<DocBlock> NameValues(DocProject project)
        {
            var names = new HashSet<DocBlock>();
            Func<DocBlock, bool> isNameField = b => { var f = Schedules.Vocabulary.TitleFields.Exactly(b.Source); return f != null && NameFields.Contains(f); };
            foreach (var page in project.Pages.Where(p => p.Extracted))
            {
                foreach (var row in page.Blocks.Where(b => b.Kind == BlockKind.TableCell && b.Table >= 0).GroupBy(b => b.Table * 10000 + b.Row))
                {
                    var cells = row.OrderBy(b => b.Col).ToList();
                    if (cells.Count >= 2 && isNameField(cells[0]))
                        foreach (var b in cells.Skip(1)) names.Add(b);
                }
                foreach (var label in page.Blocks.Where(b => b.Kind != BlockKind.TableCell && b.Lines.Count <= 2 && b.Source.Length <= 40 && isNameField(b)))
                {
                    float size = Math.Max(4f, label.FontSize);
                    var value = page.Blocks
                        .Where(o => o != label && o.Box.L >= label.Box.R - 1 && o.Box.L - label.Box.R < Math.Max(page.Width * 0.3f, size * 25) &&
                                    Math.Min(o.Box.T, label.Box.T) - Math.Max(o.Box.B, label.Box.B) > Math.Min(o.Box.H, label.Box.H) * 0.4f)
                        .OrderBy(o => o.Box.L - label.Box.R).FirstOrDefault();
                    if (value != null && Schedules.Vocabulary.TitleFields.Exactly(value.Source) == null && value.Source.Length <= 80) names.Add(value);
                }
            }
            return names;
        }

        // ------------------------------------------------------------------ language
        /// <summary>The document's language: the alphabet decides when it can, the online detector otherwise.</summary>
        public async Task<string> DetectLanguageAsync(DocProject project, CancellationToken ct)
        {
            var sample = string.Join(" ", project.AllBlocks.Where(b => b.Source.Length >= 12 && LanguageGuess.HasWords(b.Source))
                .Select(b => b.Source).Take(40));
            if (sample.Length > 1500) sample = sample.Substring(0, 1500);
            if (sample.Length == 0) sample = string.Join(" ", project.AllBlocks.Select(b => b.Source).Take(40));
            var byScript = LanguageGuess.FromScript(sample);
            if (byScript != null) return byScript;
            try
            {
                return await new GoogleFreeProvider(new ProviderSettings { TimeoutSeconds = 10 }).DetectAsync(sample, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null; // offline: the translation service detects it per request
            }
        }

        // ------------------------------------------------------------------ translation
        /// <summary>
        /// Translates every block that still needs it (or all machine-translated blocks with
        /// <paramref name="again"/>; the user's own edits are never replaced).
        /// </summary>
        public async Task TranslateAsync(DocProject project, IList<int> pages, bool again, IProgress<TranslateProgress> progress, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            if (project.SourceLangSetting != Languages.AutoDetect && !string.IsNullOrEmpty(project.SourceLangSetting))
                project.SourceLang = project.SourceLangSetting;
            else if (string.IsNullOrEmpty(project.SourceLang))
            {
                Report(progress, 0, 0, "Detecting the language...", sw, null);
                project.SourceLang = await DetectLanguageAsync(project, ct).ConfigureAwait(false) ?? string.Empty;
            }
            string src = string.IsNullOrEmpty(project.SourceLang) ? Languages.AutoDetect : project.SourceLang;
            string tgt = project.TargetLang;

            var pageSet = pages == null ? null : new HashSet<int>(pages);
            var blocks = project.Pages.Where(p => p.Extracted && (pageSet == null || pageSet.Contains(p.Index)))
                .SelectMany(p => p.InReadingOrder)
                .Where(b => b.State != BlockState.Edited && (again ? true : b.NeedsTranslation))
                .ToList();

            var names = NameValues(project);
            var work = new List<DocBlock>();
            foreach (var b in blocks)
            {
                // names in title blocks are kept as written, unless they are in another alphabet (a Hebrew name
                // in an English drawing is written out in English letters)
                bool keepName = names.Contains(b) && b.Source.All(ch => !char.IsLetter(ch) || ch < 0x0250);
                if (!LanguageGuess.HasWords(b.Source) || TextSegmenter.IsCodeLike(b.Source.Trim()) || LanguageGuess.IsReference(b.Source) || SameLanguage(src, tgt) || keepName)
                {
                    b.State = BlockState.Kept;
                    b.Translation = null;
                }
                else
                {
                    work.Add(b);
                }
            }

            int done = 0, failures = 0;
            Report(progress, 0, work.Count, "Translating...", sw, null);
            foreach (var batch in Batches(work, BatchChars))
            {
                ct.ThrowIfCancellationRequested();
                TranslationOutcome outcome;
                try
                {
                    outcome = await _engine.TranslateManyAsync(batch.Select(b => Clean(b.Source)).ToList(), src, tgt, ct).ConfigureAwait(false);
                    failures = 0;
                }
                catch (TranslationException ex)
                {
                    foreach (var b in batch) { b.State = BlockState.Failed; b.Note = ex.Message; }
                    if (++failures >= 2) throw;
                    done += batch.Count;
                    continue;
                }
                var enforced = await EnforceGlossaryAsync(batch, outcome.Translations, src, tgt, ct).ConfigureAwait(false);
                for (int i = 0; i < batch.Count; i++)
                {
                    var b = batch[i];
                    var tr = enforced[i];
                    if (string.IsNullOrWhiteSpace(tr)) { b.State = BlockState.Failed; b.Note = outcome.Warning ?? "No translation was returned."; continue; }
                    if (string.Equals(tr.Trim(), b.Source.Trim(), StringComparison.Ordinal)) { b.State = BlockState.Kept; b.Translation = null; continue; }
                    b.Translation = tr.Trim();
                    b.State = BlockState.Translated;
                    if (b.Note != null && b.Note.Contains("service")) b.Note = null;
                }
                done += batch.Count;
                project.Provider = outcome.ProviderUsed;
                project.Modified = DateTime.UtcNow;
                var elapsed = sw.Elapsed;
                TimeSpan? remaining = done > 0 && done < work.Count ? TimeSpan.FromTicks((long)(elapsed.Ticks * (work.Count - done) / (double)done)) : (TimeSpan?)null;
                Report(progress, done, work.Count, "Translating " + done + " of " + work.Count + " paragraphs...", sw, remaining);
                var cp = Checkpoint;
                if (cp != null) cp(project);
            }
            project.Modified = DateTime.UtcNow;
            Report(progress, work.Count, work.Count, "Translated", sw, null);
        }

        private static bool SameLanguage(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || a == Languages.AutoDetect) return false;
            return string.Equals(a.Split('-')[0], b.Split('-')[0], StringComparison.OrdinalIgnoreCase) && !a.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string s)
        {
            return Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim();
        }

        private static IEnumerable<List<DocBlock>> Batches(List<DocBlock> blocks, int maxChars)
        {
            var batch = new List<DocBlock>();
            int chars = 0;
            foreach (var b in blocks)
            {
                if (batch.Count > 0 && (chars + b.Source.Length > maxChars || batch.Count >= 120))
                {
                    yield return batch;
                    batch = new List<DocBlock>();
                    chars = 0;
                }
                batch.Add(b);
                chars += b.Source.Length + 1;
            }
            if (batch.Count > 0) yield return batch;
        }

        private static void Report(IProgress<TranslateProgress> p, int done, int total, string status, Stopwatch sw, TimeSpan? remaining)
        {
            if (p != null) p.Report(new TranslateProgress { Done = done, Total = total, Status = status, Elapsed = sw.Elapsed, Remaining = remaining });
        }

        // ------------------------------------------------------------------ glossary inside sentences
        /// <summary>
        /// Makes translations use the glossary's terms: where a glossary term occurs in a paragraph and
        /// the machine translation used its own word for it, that word is replaced by the glossary's.
        /// </summary>
        public async Task<IList<string>> EnforceGlossaryAsync(IList<DocBlock> blocks, IList<string> translations, string src, string tgt, CancellationToken ct)
        {
            var entries = _engine.Glossary.For(src, tgt).Where(e => !string.IsNullOrWhiteSpace(e.Source) && !string.IsNullOrWhiteSpace(e.Target)).ToList();
            var result = translations.ToList();
            if (entries.Count == 0) return result;
            var needed = new Dictionary<GlossaryEntry, List<int>>();
            for (int i = 0; i < blocks.Count; i++)
            {
                if (result[i] == null) continue;
                foreach (var e in entries)
                {
                    if (!ContainsTerm(blocks[i].Source, e.Source)) continue;
                    if (result[i].IndexOf(e.Target, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    List<int> list;
                    if (!needed.TryGetValue(e, out list)) needed[e] = list = new List<int>();
                    list.Add(i);
                }
            }
            if (needed.Count == 0) return result;
            // how the service translates each term on its own (cached), bypassing the glossary
            var terms = needed.Keys.ToList();
            var machine = new string[terms.Count];
            var ask = new List<int>();
            for (int k = 0; k < terms.Count; k++)
            {
                string cached;
                if (_engine.Cache.TryGet(TermKey(src, tgt, terms[k].Source), out cached)) machine[k] = cached;
                else ask.Add(k);
            }
            if (ask.Count > 0)
            {
                try
                {
                    var answers = await _engine.Provider.TranslateAsync(ask.Select(k => terms[k].Source).ToList(), src, tgt, ct).ConfigureAwait(false);
                    for (int a = 0; a < ask.Count && a < answers.Count; a++)
                    {
                        machine[ask[a]] = answers[a];
                        if (!string.IsNullOrWhiteSpace(answers[a])) _engine.Cache.Set(TermKey(src, tgt, terms[ask[a]].Source), answers[a]);
                    }
                }
                catch (TranslationException)
                {
                    return result; // leave as is; the quality check will flag the term
                }
            }
            for (int k = 0; k < terms.Count; k++)
            {
                var mt = (machine[k] ?? string.Empty).Trim().TrimEnd('.', ':');
                if (mt.Length == 0) continue;
                foreach (var i in needed[terms[k]])
                    result[i] = ReplaceTerm(result[i], mt, terms[k].Target);
            }
            return result;
        }

        private static string TermKey(string src, string tgt, string term)
        {
            return TranslationCache.Key("term", src, tgt, term);
        }

        public static bool ContainsTerm(string text, string term)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(term)) return false;
            return Regex.IsMatch(text, "(?<![\\p{L}\\p{N}])" + Regex.Escape(term.Trim()) + "(?![\\p{L}\\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>Replaces <paramref name="find"/> (whole words, any case) with <paramref name="replacement"/>.</summary>
        public static string ReplaceTerm(string text, string find, string replacement)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(find)) return text;
            var pattern = "(?<![\\p{L}\\p{N}])" + Regex.Escape(find) + "(?![\\p{L}\\p{N}])";
            return Regex.Replace(text, pattern, m =>
            {
                // keep a capital first letter at the start of a sentence
                if (m.Value.Length > 0 && char.IsUpper(m.Value[0]) && replacement.Length > 0 && char.IsLower(replacement[0]))
                    return char.ToUpper(replacement[0]) + replacement.Substring(1);
                return replacement;
            }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // ------------------------------------------------------------------ single blocks, selections, corrections
        /// <summary>Translates one block again (asking the service afresh, not the memory).</summary>
        public async Task RetranslateAsync(DocProject project, DocBlock block, CancellationToken ct)
        {
            string src = string.IsNullOrEmpty(project.SourceLang) ? Languages.AutoDetect : project.SourceLang;
            _engine.Cache.Remove(TranslationCache.Key(_engine.Provider.Id, src, project.TargetLang, Clean(block.Source)));
            var outcome = await _engine.TranslateAsync(Clean(block.Source), src, project.TargetLang, ct).ConfigureAwait(false);
            var tr = (await EnforceGlossaryAsync(new[] { block }, outcome.Translations, src, project.TargetLang, ct).ConfigureAwait(false))[0];
            if (string.IsNullOrWhiteSpace(tr)) throw new TranslationException("No translation was returned.");
            block.Translation = tr.Trim();
            block.State = string.Equals(block.Translation, block.Source.Trim(), StringComparison.Ordinal) ? BlockState.Kept : BlockState.Translated;
            project.Modified = DateTime.UtcNow;
        }

        /// <summary>Translates any piece of text (a selection) between the document's languages.</summary>
        public async Task<string> TranslateTextAsync(string text, string src, string tgt, CancellationToken ct)
        {
            var outcome = await _engine.TranslateAsync(text, string.IsNullOrEmpty(src) ? Languages.AutoDetect : src, tgt, ct).ConfigureAwait(false);
            return outcome.Translations.Count > 0 ? outcome.Translations[0] : string.Empty;
        }

        /// <summary>
        /// Stores the user's correction of a block: it is used for this paragraph from now on, for every
        /// identical paragraph in the document, and for identical text in future documents.
        /// Returns how many other blocks were updated.
        /// </summary>
        public int ApplyEdit(DocProject project, DocBlock block, string translation)
        {
            block.Translation = translation;
            block.State = BlockState.Edited;
            block.Note = null;
            string src = string.IsNullOrEmpty(project.SourceLang) ? Languages.AutoDetect : project.SourceLang;
            var key = Clean(block.Source);
            _engine.Cache.Set(TranslationCache.Key(_engine.Provider.Id, src, project.TargetLang, key), translation);
            int others = 0;
            foreach (var b in project.AllBlocks)
            {
                if (b == block || b.State == BlockState.Edited) continue;
                if (!string.Equals(Clean(b.Source), key, StringComparison.Ordinal)) continue;
                b.Translation = translation;
                b.State = BlockState.Edited;
                others++;
            }
            project.Modified = DateTime.UtcNow;
            return others;
        }
    }
}
