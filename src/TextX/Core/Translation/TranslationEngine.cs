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

        /// <summary>For each text: true when part of it got no translation (the service failed); it is returned as given.</summary>
        public IList<bool> Missing { get; internal set; }
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

        /// <summary>
        /// For long jobs (documents): a busy service is asked again after a pause before the fallback is used,
        /// and a service paused by an earlier failure is still tried first.
        /// </summary>
        public bool Patient { get; set; }

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
                        if (Cache.TryGet(TranslationCache.Key(Provider.Id, sourceLang, targetLang, CaseFold.CacheText(core)), out cached))
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
                    // capitals are sent as small letters (services keep words in capitals as names)
                    var asked = batch.Select(CaseFold.Fold).ToList();
                    string usedProvider = Provider.Id;
                    if (hasFallback && InCooldown(Provider.Id) && !Patient)
                    {
                        outcome.Warning = Provider.DisplayName + " failed recently and is paused for a few minutes - used " + Fallback.DisplayName + ".";
                        results = await TranslateWithFallback(asked, sourceLang, targetLang, ct).ConfigureAwait(false);
                        usedProvider = Fallback.Id;
                        outcome.ProviderUsed = Fallback.Id;
                    }
                    else
                    {
                        try
                        {
                            results = await TranslatePrimaryAsync(asked, sourceLang, targetLang, ct).ConfigureAwait(false);
                        }
                        catch (TranslationException ex)
                        {
                            if (!hasFallback) throw;
                            StartCooldown(Provider.Id);
                            outcome.Warning = ex.Message + " - used " + Fallback.DisplayName + " instead.";
                            results = await TranslateWithFallback(asked, sourceLang, targetLang, ct).ConfigureAwait(false);
                            usedProvider = Fallback.Id;
                            outcome.ProviderUsed = Fallback.Id;
                        }
                    }
                    for (int i = 0; i < batch.Count; i++)
                    {
                        var r = CaseFold.Restore(results[i], batch[i]);
                        if (r == null) { outcome.Complete = false; continue; }
                        resolved[batch[i]] = r;
                        outcome.ServiceSegments++;
                        Cache.Set(TranslationCache.Key(usedProvider, sourceLang, targetLang, CaseFold.CacheText(batch[i])), r);
                    }
                }
            }
            else if (pending.Count > 0)
            {
                outcome.Complete = false;
            }

            var missing = new List<bool>();
            outcome.Translations = segmented.Select(doc =>
            {
                bool miss = false;
                var text = TextSegmenter.Join(doc, seg =>
                {
                    string lead, core, trail;
                    TextSegmenter.SplitWhitespace(seg.Text, out lead, out core, out trail);
                    string tr;
                    if (core.Length == 0) return seg.Text;
                    if (!resolved.TryGetValue(core, out tr) || tr == null) { miss = true; return seg.Text; }
                    return lead + tr.Trim() + trail;
                });
                missing.Add(miss);
                return text;
            }).ToList();
            outcome.Missing = missing;
            outcome.Elapsed = sw.Elapsed;
            return outcome;
        }

        /// <summary>
        /// The main service alone (no memory, glossary or backup service), asked again after a pause when it is busy.
        /// </summary>
        public Task<IList<string>> TranslateDirectAsync(IList<string> texts, string src, string tgt, CancellationToken ct)
        {
            return TranslatePrimaryAsync(texts, src, tgt, ct);
        }

        /// <summary>The main service, asked again after a pause when it is busy (too many requests, a timeout).</summary>
        private async Task<IList<string>> TranslatePrimaryAsync(IList<string> batch, string src, string tgt, CancellationToken ct)
        {
            int[] pauses = Patient ? new[] { 2000, 6000, 15000 } : new[] { 1200 };
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await Provider.TranslateAsync(batch, src, tgt, ct).ConfigureAwait(false);
                }
                catch (TranslationException ex) when (attempt < pauses.Length && IsTransient(ex))
                {
                    await Task.Delay(pauses[attempt], ct).ConfigureAwait(false);
                }
            }
        }

        private static bool IsTransient(TranslationException ex)
        {
            var m = ex.Message ?? string.Empty;
            return m.Contains("HTTP 429") || m.Contains("HTTP 50") || m.Contains("timed out") || m.Contains("network error");
        }

        /// <summary>
        /// The fallback service, within its own limits: texts longer than it takes go in pieces (sentences,
        /// then words), and a piece it refuses stays untranslated without stopping the others.
        /// </summary>
        private async Task<IList<string>> TranslateWithFallback(IList<string> batch, string src, string tgt, CancellationToken ct)
        {
            int limit = Math.Max(80, Fallback.MaxBatchChars);
            var owner = new List<int>();
            var pieces = new List<string>();
            for (int i = 0; i < batch.Count; i++)
                foreach (var piece in SplitForLimit(batch[i] ?? string.Empty, limit)) { owner.Add(i); pieces.Add(piece); }
            var translated = new string[pieces.Count];
            int at = 0;
            foreach (var sub in MakeBatches(pieces, Fallback.MaxBatchItems, limit))
            {
                IList<string> r = null;
                try { r = await Fallback.TranslateAsync(sub, src, tgt, ct).ConfigureAwait(false); }
                catch (TranslationException) { r = null; }
                for (int k = 0; k < sub.Count; k++) translated[at + k] = r != null && k < r.Count ? r[k] : null;
                at += sub.Count;
            }
            var results = new List<string>(batch.Count);
            for (int i = 0; i < batch.Count; i++)
            {
                var parts = new List<string>();
                bool ok = true;
                for (int k = 0; k < pieces.Count; k++)
                {
                    if (owner[k] != i) continue;
                    if (translated[k] == null) { ok = false; break; }
                    parts.Add(translated[k].Trim());
                }
                results.Add(ok ? string.Join(" ", parts) : null);
            }
            return results;
        }

        /// <summary>A text cut into pieces of at most <paramref name="limit"/> characters, at sentence ends, else at spaces.</summary>
        internal static List<string> SplitForLimit(string text, int limit)
        {
            var result = new List<string>();
            text = text.Trim();
            while (text.Length > limit)
            {
                int cut = -1;
                for (int k = limit; k > limit / 3; k--)
                    if ((text[k - 1] == '.' || text[k - 1] == '!' || text[k - 1] == '?' || text[k - 1] == ';') && (k == text.Length || char.IsWhiteSpace(text[k]))) { cut = k; break; }
                if (cut < 0) cut = text.LastIndexOf(' ', limit - 1);
                if (cut <= 0) cut = limit;
                result.Add(text.Substring(0, cut).Trim());
                text = text.Substring(cut).Trim();
            }
            if (text.Length > 0 || result.Count == 0) result.Add(text);
            return result;
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
