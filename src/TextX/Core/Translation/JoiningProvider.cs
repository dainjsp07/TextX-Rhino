using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TextX.Core.Translation
{
    /// <summary>
    /// For services that take one text per request (the free Google endpoint): sends many
    /// paragraphs in one request, one per line, and splits the answer by line. A long document then
    /// needs a few dozen requests instead of thousands, which is faster and stays under the service's
    /// rate limits. If the answer does not have the same number of lines, the batch is sent again one
    /// paragraph at a time.
    /// </summary>
    public sealed class JoiningProvider : ITranslationProvider
    {
        private readonly ITranslationProvider _inner;
        private readonly int _maxChars;

        public JoiningProvider(ITranslationProvider inner, int maxChars = 4200)
        {
            _inner = inner;
            _maxChars = maxChars;
        }

        public string Id { get { return _inner.Id; } } // same cache keys as the wrapped service
        public string DisplayName { get { return _inner.DisplayName; } }
        public bool RequiresApiKey { get { return _inner.RequiresApiKey; } }
        public int MaxBatchItems { get { return 400; } }
        public int MaxBatchChars { get { return _maxChars; } }

        public static bool ShouldWrap(ITranslationProvider p)
        {
            return p != null && p.Id == ProviderIds.GoogleFree;
        }

        public async Task<IList<string>> TranslateAsync(IList<string> texts, string sourceLang, string targetLang, CancellationToken ct)
        {
            if (texts.Count <= 1) return await _inner.TranslateAsync(texts, sourceLang, targetLang, ct).ConfigureAwait(false);
            var clean = texts.Select(t => (t ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ')).ToList();
            var joined = string.Join("\n", clean);
            var answer = await _inner.TranslateAsync(new[] { joined }, sourceLang, targetLang, ct).ConfigureAwait(false);
            var lines = (answer.Count > 0 ? answer[0] ?? string.Empty : string.Empty).Replace("\r\n", "\n").Split('\n');
            if (lines.Length == clean.Count) return lines.Select(l => l.Trim()).ToList();
            // the service merged or split lines: fall back to one request per paragraph
            return await _inner.TranslateAsync(clean, sourceLang, targetLang, ct).ConfigureAwait(false);
        }
    }
}
