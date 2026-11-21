using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TextX.Core.Tools
{
    /// <summary>One suggestion for the text: what is wrong and how to fix it.</summary>
    public sealed class WritingIssue
    {
        public int Offset;
        public int Length;
        public string Message = string.Empty;
        public string ShortMessage = string.Empty;
        public string Category = string.Empty;   // Spelling, Grammar, Punctuation, Style ...
        public List<string> Replacements = new List<string>();
        public string Context = string.Empty;

        public string Best { get { return Replacements.Count > 0 ? Replacements[0] : null; } }
    }

    public sealed class WritingResult
    {
        public string Language = string.Empty;       // detected, e.g. "en-GB"
        public string LanguageName = string.Empty;
        public List<WritingIssue> Issues = new List<WritingIssue>();
    }

    /// <summary>
    /// Spelling, grammar and style suggestions from LanguageTool's free public service
    /// (api.languagetool.org: no account or key; about 20 checks a minute, 20 KB of text each).
    /// </summary>
    public static class WritingCheck
    {
        public const string Endpoint = "https://api.languagetool.org/v2/check";
        public const int MaxChars = 18000;
        private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(() =>
        {
#if NETFRAMEWORK
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; } catch { /* older OS */ }
#endif
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
            var v = typeof(WritingCheck).Assembly.GetName().Version;
            c.DefaultRequestHeaders.UserAgent.ParseAdd("TextX/" + (v == null ? "1.0" : v.ToString(3)));
            return c;
        });

        /// <param name="language">Text X language code, or null / "auto" to let the service detect it.</param>
        public static async Task<WritingResult> CheckAsync(string text, string language, CancellationToken ct)
        {
            var result = new WritingResult();
            if (string.IsNullOrWhiteSpace(text)) return result;
            int offset = 0;
            foreach (var chunk in Chunks(text, MaxChars))
            {
                var form = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("text", chunk),
                    new KeyValuePair<string, string>("language", LanguageCode(language)),
                    new KeyValuePair<string, string>("preferredVariants", "en-GB,de-DE,pt-PT,ca-ES,nl-NL"),
                    new KeyValuePair<string, string>("level", "default"),
                };
                using (var content = new FormUrlEncodedContent(form))
                using (var response = await Client.Value.PostAsync(Endpoint, content, ct).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if ((int)response.StatusCode == 429) throw new InvalidOperationException("The writing check is busy (too many checks in a minute). Try again in a moment.");
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The writing check service answered: " + Trim(body, 160));
                    var part = Parse(body);
                    if (string.IsNullOrEmpty(result.Language)) { result.Language = part.Language; result.LanguageName = part.LanguageName; }
                    foreach (var issue in part.Issues) { issue.Offset += offset; result.Issues.Add(issue); }
                }
                offset += chunk.Length;
            }
            return result;
        }

        /// <summary>Reads the service's JSON answer.</summary>
        public static WritingResult Parse(string json)
        {
            var root = Json.Parse(json);
            var result = new WritingResult
            {
                Language = Json.GetString(root, "language", "detectedLanguage", "code") ?? Json.GetString(root, "language", "code") ?? string.Empty,
                LanguageName = Json.GetString(root, "language", "detectedLanguage", "name") ?? Json.GetString(root, "language", "name") ?? string.Empty,
            };
            foreach (var m in Json.GetList(root, "matches") ?? new List<object>())
            {
                var issue = new WritingIssue
                {
                    Offset = Convert.ToInt32(Json.Get(m, "offset") ?? 0, CultureInfo.InvariantCulture),
                    Length = Convert.ToInt32(Json.Get(m, "length") ?? 0, CultureInfo.InvariantCulture),
                    Message = Json.GetString(m, "message") ?? string.Empty,
                    ShortMessage = Json.GetString(m, "shortMessage") ?? string.Empty,
                    Category = Json.GetString(m, "rule", "category", "name") ?? string.Empty,
                    Context = Json.GetString(m, "context", "text") ?? string.Empty,
                };
                foreach (var r in (Json.GetList(m, "replacements") ?? new List<object>()).Take(5))
                {
                    var v = Json.GetString(r, "value");
                    if (v != null) issue.Replacements.Add(v);
                }
                result.Issues.Add(issue);
            }
            return result;
        }

        /// <summary>Applies the first suggestion of each issue (the ones passed), from the end so offsets stay valid.</summary>
        public static string Apply(string text, IEnumerable<WritingIssue> issues)
        {
            var sb = new StringBuilder(text);
            int lastStart = int.MaxValue;
            foreach (var i in issues.Where(x => x.Best != null).OrderByDescending(x => x.Offset))
            {
                if (i.Offset < 0 || i.Offset + i.Length > sb.Length || i.Offset + i.Length > lastStart) continue; // overlapping
                sb.Remove(i.Offset, i.Length).Insert(i.Offset, i.Best);
                lastStart = i.Offset;
            }
            return sb.ToString();
        }

        /// <summary>The issues that remain valid after replacing one of them (later offsets move).</summary>
        public static List<WritingIssue> AfterApplying(List<WritingIssue> issues, WritingIssue applied, string replacement)
        {
            int delta = replacement.Length - applied.Length;
            var rest = new List<WritingIssue>();
            foreach (var i in issues)
            {
                if (i == applied) continue;
                if (i.Offset + i.Length <= applied.Offset) rest.Add(i);
                else if (i.Offset >= applied.Offset + applied.Length) { i.Offset += delta; rest.Add(i); }
            }
            return rest;
        }

        internal static string LanguageCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code == Languages.AutoDetect) return "auto";
            switch (code)
            {
                case "en": return "en-GB";
                case "de": return "de-DE";
                case "pt": return "pt-PT";
                case "pt-BR": return "pt-BR";
                case "nl": return "nl-NL";
                case "zh-CN": case "zh-TW": return "zh-CN";
                default: return code.Split('-')[0];
            }
        }

        /// <summary>Splits long text at paragraph (or sentence) ends.</summary>
        internal static IEnumerable<string> Chunks(string text, int max)
        {
            int start = 0;
            while (start < text.Length)
            {
                int len = Math.Min(max, text.Length - start);
                if (start + len < text.Length)
                {
                    int cut = text.LastIndexOf('\n', start + len - 1, len);
                    if (cut <= start) cut = text.LastIndexOf(". ", start + len - 1, len, StringComparison.Ordinal) + 1;
                    if (cut > start) len = cut - start + 1;
                }
                yield return text.Substring(start, len);
                start += len;
            }
        }

        private static string Trim(string s, int max)
        {
            s = (s ?? string.Empty).Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
