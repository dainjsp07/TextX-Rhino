using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TextX.Core.Translation
{
    public sealed class TranslationOutcome
    {
        public IList<string> Translations { get; internal set; }
        public int ServiceSegments { get; internal set; }
        public int CachedSegments { get; internal set; }
        public int GlossarySegments { get; internal set; }
        public string ProviderUsed { get; internal set; }
        public string Warning { get; internal set; }
        public TimeSpan Elapsed { get; internal set; }

        /// <summary>True when every segment that needed a translation got one.</summary>
        public bool Complete { get; internal set; }
    }

    /// <summary>
    /// Orchestrates translation: segmentation (protecting fields / numbers / do-not-translate
    /// terms), glossary, cache, batching and provider fallback.
    /// </summary>
    public sealed class TranslationEngine
    {
        // After a provider fails, it is skipped for a while (when a fallback exists) so that
        // real-time translation does not wait for a blocked service on every keystroke.
        private static readonly Dictionary<string, DateTime> Cooldown = new Dictionary<string, DateTime>();
        private static readonly object CooldownLock = new object();
        public static TimeSpan CooldownDuration = TimeSpan.FromMinutes(3);

        public static void ResetCooldowns()
        {
            lock (CooldownLock) Cooldown.Clear();
        }

        public static bool InCooldown(string providerId)
        {
            lock (CooldownLock)
            {
                DateTime until;
                return Cooldown.TryGetValue(providerId, out until) && until > DateTime.UtcNow;
            }
        }

        private static void StartCooldown(string providerId)
        {
            lock (CooldownLock) Cooldown[providerId] = DateTime.UtcNow + CooldownDuration;
        }

        public TranslationEngine(ITranslationProvider provider, ITranslationProvider fallback, Glossary glossary, TranslationCache cache)
        {
            Provider = provider;
            Fallback = fallback;
            Glossary = glossary ?? new Glossary();
            Cache = cache ?? new TranslationCache();
        }

        public ITranslationProvider Provider { get; private set; }
        public ITranslationProvider Fallback { get; private set; }
        public Glossary Glossary { get; private set; }
        public TranslationCache Cache { get; private set; }

        public async Task<TranslationOutcome> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct)
        {
            return await TranslateManyAsync(new[] { text }, sourceLang, targetLang, ct).ConfigureAwait(false);
        }

        public async Task<TranslationOutcome> TranslateManyAsync(IList<string> texts, string sourceLang, string targetLang, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var outcome = new TranslationOutcome { ProviderUsed = Provider.Id, Complete = true };

            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
            {
                outcome.Translations = texts.ToList();
                outcome.Elapsed = sw.Elapsed;
                return outcome;
            }

            var segmented = texts.Select(t => TextSegmenter.Split(t ?? string.Empty, Glossary.DoNotTranslate)).ToList();

            // collect unique core strings that need translation
            var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
            var pending = new List<string>();
            foreach (var doc in segmented)
            {
                foreach (var line in doc)
                {
                    foreach (var seg in line.Where(s => s.Translate))
                    {
                        string lead, core, trail;
                        TextSegmenter.SplitWhitespace(seg.Text, out lead, out core, out trail);
                        if (core.Length == 0 || resolved.ContainsKey(core) || pending.Contains(core)) continue;

                        var g = Glossary.Lookup(core, sourceLang, targetLang);
                        if (g != null)
                        {
                            resolved[core] = g;
                            outcome.GlossarySegments++;
                            continue;
                        }
                        string cached;
                        if (Cache.TryGet(TranslationCache.Key(Provider.Id, sourceLang, targetLang, core), out cached))
                        {
                            resolved[core] = cached;
                            outcome.CachedSegments++;
                            continue;
                        }
                        pending.Add(core);
                    }
                }
            }

            bool hasFallback = Fallback != null && Fallback.Id != Provider.Id && Fallback.Id != ProviderIds.Manual;
            if (pending.Count > 0 && Provider.Id != ProviderIds.Manual)
            {
                foreach (var batch in MakeBatches(pending, Provider.MaxBatchItems, Provider.MaxBatchChars))
                {
                    ct.ThrowIfCancellationRequested();
                    IList<string> results;
                    string usedProvider = Provider.Id;
                    if (hasFallback && InCooldown(Provider.Id))
                    {
                        outcome.Warning = Provider.DisplayName + " failed recently and is paused for a few minutes - used " + Fallback.DisplayName + ".";
                        results = await TranslateWithFallback(batch, sourceLang, targetLang, ct).ConfigureAwait(false);
                        usedProvider = Fallback.Id;
                        outcome.ProviderUsed = Fallback.Id;
                    }
                    else
                    {
                        try
                        {
                            results = await Provider.TranslateAsync(batch, sourceLang, targetLang, ct).ConfigureAwait(false);
                        }
                        catch (TranslationException ex)
                        {
                            if (!hasFallback) throw;
                            StartCooldown(Provider.Id);
                            outcome.Warning = ex.Message + " - used " + Fallback.DisplayName + " instead.";
                            results = await TranslateWithFallback(batch, sourceLang, targetLang, ct).ConfigureAwait(false);
                            usedProvider = Fallback.Id;
                            outcome.ProviderUsed = Fallback.Id;
                        }
                    }
                    for (int i = 0; i < batch.Count; i++)
                    {
                        var r = results[i];
                        if (r == null) { outcome.Complete = false; continue; }
                        resolved[batch[i]] = r;
                        outcome.ServiceSegments++;
                        Cache.Set(TranslationCache.Key(usedProvider, sourceLang, targetLang, batch[i]), r);
                    }
                }
            }
            else if (pending.Count > 0)
            {
                outcome.Complete = false;
            }

            outcome.Translations = segmented.Select(doc => TextSegmenter.Join(doc, seg =>
            {
                string lead, core, trail;
                TextSegmenter.SplitWhitespace(seg.Text, out lead, out core, out trail);
                string tr;
                if (core.Length == 0) return seg.Text;
                if (!resolved.TryGetValue(core, out tr) || tr == null) return seg.Text;
                return lead + tr.Trim() + trail;
            })).ToList();
            outcome.Elapsed = sw.Elapsed;
            return outcome;
        }

        private async Task<IList<string>> TranslateWithFallback(IList<string> batch, string src, string tgt, CancellationToken ct)
        {
            var results = new List<string>(batch.Count);
            foreach (var sub in MakeBatches(batch, Fallback.MaxBatchItems, Fallback.MaxBatchChars))
                results.AddRange(await Fallback.TranslateAsync(sub, src, tgt, ct).ConfigureAwait(false));
            return results;
        }

        public static IEnumerable<IList<string>> MakeBatches(IList<string> items, int maxItems, int maxChars)
        {
            var batch = new List<string>();
            int chars = 0;
            foreach (var item in items)
            {
                if (batch.Count > 0 && (batch.Count >= maxItems || chars + item.Length > maxChars))
                {
                    yield return batch;
                    batch = new List<string>();
                    chars = 0;
                }
                batch.Add(item);
                chars += item.Length;
            }
            if (batch.Count > 0) yield return batch;
        }
    }
}
