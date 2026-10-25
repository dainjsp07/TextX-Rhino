using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TextX.Core.Translation.Providers
{
    /// <summary>Shared HTTP plumbing for the online providers.</summary>
    public abstract class HttpProviderBase : ITranslationProvider
    {
        private static readonly Lazy<HttpClient> SharedClient = new Lazy<HttpClient>(CreateClient);

        protected readonly ProviderSettings Settings;

        protected HttpProviderBase(ProviderSettings settings)
        {
            Settings = settings ?? new ProviderSettings();
        }

        public abstract string Id { get; }
        public virtual string DisplayName { get { return ProviderIds.DisplayName(Id); } }
        public virtual bool RequiresApiKey { get { return ProviderIds.RequiresKey(Id); } }
        public virtual int MaxBatchItems { get { return 50; } }
        public virtual int MaxBatchChars { get { return 20000; } }

        private static HttpClient CreateClient()
        {
#if NETFRAMEWORK
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            }
            catch
            {
                // older OS; ignore
            }
#endif
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var v = typeof(HttpProviderBase).Assembly.GetName().Version;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TextX/" + (v == null ? "1.0" : v.ToString(3)));
            return client;
        }

        protected static HttpClient Http { get { return SharedClient.Value; } }

        protected void RequireKey()
        {
            if (string.IsNullOrWhiteSpace(Settings.ApiKey))
                throw new TranslationException(DisplayName + ": no API key configured (run TRSettings).");
        }

        protected async Task<string> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(3, Settings.TimeoutSeconds)));
                HttpResponseMessage response;
                try
                {
                    response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TranslationException(DisplayName + ": request timed out.");
                }
                catch (HttpRequestException ex)
                {
                    throw new TranslationException(DisplayName + ": network error - " + ex.Message, ex);
                }
                using (response)
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        int code = (int)response.StatusCode;
                        string detail;
                        if (code == 429) detail = "too many requests - the service is temporarily limiting this computer";
                        else if (code == 401 || code == 403) detail = "access denied - check the API key / plan (" + Shorten(ExtractError(body)) + ")";
                        else detail = Shorten(ExtractError(body));
                        throw new TranslationException(string.Format(CultureInfo.InvariantCulture, "{0}: HTTP {1} {2}", DisplayName, code, detail));
                    }
                    return body;
                }
            }
        }

        private static string ExtractError(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "(no details)";
            var trimmed = body.TrimStart();
            if (trimmed.StartsWith("<", StringComparison.Ordinal))
            {
                // HTML error page: keep only the <title>, if any
                var m = System.Text.RegularExpressions.Regex.Match(body, "<title>(.*?)</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim()) : "(error page)";
            }
            try
            {
                var root = Json.Parse(body);
                return Json.GetString(root, "error", "message") ?? Json.GetString(root, "message") ??
                       Json.GetString(root, "error") ?? body;
            }
            catch
            {
                return body;
            }
        }

        protected static string Shorten(string s)
        {
            if (s == null) return string.Empty;
            s = s.Replace('\n', ' ').Trim();
            return s.Length > 240 ? s.Substring(0, 240) + "..." : s;
        }

        protected static StringContent JsonContent(object body)
        {
            return new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
        }

        protected static List<string> ExpectCount(List<string> result, int count, string provider)
        {
            if (result == null || result.Count != count)
                throw new TranslationException(provider + ": unexpected response (expected " + count + " translations).");
            return result;
        }

        public abstract Task<IList<string>> TranslateAsync(IList<string> texts, string sourceLang, string targetLang, CancellationToken cancellationToken);

        /// <summary>Runs one request per text with limited parallelism (for endpoints without batch support).</summary>
        protected async Task<IList<string>> TranslateEachAsync(IList<string> texts, Func<string, Task<string>> one, int parallel)
        {
            var results = new string[texts.Count];
            using (var gate = new SemaphoreSlim(parallel))
            {
                var tasks = texts.Select(async (t, i) =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try { results[i] = await one(t).ConfigureAwait(false); }
                    finally { gate.Release(); }
                }).ToList();
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            return results;
        }
    }

    /// <summary>No machine translation; the user types the translation.</summary>
    public sealed class ManualProvider : ITranslationProvider
    {
        public string Id { get { return ProviderIds.Manual; } }
        public string DisplayName { get { return ProviderIds.DisplayName(Id); } }
        public bool RequiresApiKey { get { return false; } }
        public int MaxBatchItems { get { return int.MaxValue; } }
        public int MaxBatchChars { get { return int.MaxValue; } }

        public Task<IList<string>> TranslateAsync(IList<string> texts, string sourceLang, string targetLang, CancellationToken cancellationToken)
        {
            IList<string> empty = texts.Select(t => (string)null).ToList();
            return Task.FromResult(empty);
        }
    }

    /// <summary>
    /// Google Translate public web endpoint (translate.googleapis.com, client=gtx).
    /// No key needed; unofficial and rate limited - use an API-key provider for production work.
    /// </summary>
    public sealed class GoogleFreeProvider : HttpProviderBase
    {
        public GoogleFreeProvider(ProviderSettings s) : base(s) { }
        public override string Id { get { return ProviderIds.GoogleFree; } }
        public override int MaxBatchItems { get { return 1; } }
        public override int MaxBatchChars { get { return 4500; } }

        public override Task<IList<string>> TranslateAsync(IList<string> texts, string src, string tgt, CancellationToken ct)
        {
            return TranslateEachAsync(texts, t => TranslateOne(t, src, tgt, ct), 4);
        }

        private async Task<string> TranslateOne(string text, string src, string tgt, CancellationToken ct)
        {
            var url = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t" +
                      "&sl=" + Uri.EscapeDataString(Languages.ForGoogleFree(src)) +
                      "&tl=" + Uri.EscapeDataString(Languages.ForGoogleFree(tgt));
            HttpRequestMessage req;
            if (text.Length < 1500)
            {
                req = new HttpRequestMessage(HttpMethod.Get, url + "&q=" + Uri.EscapeDataString(text));
            }
            else
            {
                req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", text) })
                };
            }
            using (req)
            {
                var body = await SendAsync(req, ct).ConfigureAwait(false);
                return ParseResponse(body);
            }
        }

        public static string ParseResponse(string body)
        {
            var root = Json.Parse(body) as List<object>;
            var sentences = root != null && root.Count > 0 ? root[0] as List<object> : null;
            if (sentences == null) throw new TranslationException("Google: unexpected response.");
            var sb = new StringBuilder();
            foreach (var s in sentences)
            {
                var parts = s as List<object>;
                if (parts != null && parts.Count > 0 && parts[0] is string) sb.Append((string)parts[0]);
            }
            return sb.ToString();
        }
    }

    /// <summary>Google Cloud Translation API v2 (Basic) with an API key.</summary>
    public sealed class GoogleCloudProvider : HttpProviderBase
    {
        public GoogleCloudProvider(ProviderSettings s) : base(s) { }
        public override string Id { get { return ProviderIds.GoogleCloud; } }
        public override int MaxBatchItems { get { return 100; } }
        public override int MaxBatchChars { get { return 25000; } }

        public override async Task<IList<string>> TranslateAsync(IList<string> texts, string src, string tgt, CancellationToken ct)
        {
            RequireKey();
            var body = new Dictionary<string, object>
            {
                { "q", texts.Cast<object>().ToList() },
                { "target", Languages.ForGoogleCloud(tgt) },
                { "format", "text" },
            };
            if (src != Languages.AutoDetect) body["source"] = Languages.ForGoogleCloud(src);
            var url = "https://translation.googleapis.com/language/translate/v2?key=" + Uri.EscapeDataString(Settings.ApiKey.Trim());
            using (var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) })
            {
                var json = await SendAsync(req, ct).ConfigureAwait(false);
                return ParseResponse(json, texts.Count);
            }
        }

        public static IList<string> ParseResponse(string json, int count)
        {
            var list = Json.GetList(Json.Parse(json), "data", "translations");
            return ExpectCount(list == null ? null : list.Select(t => Json.GetString(t, "translatedText")).ToList(), count, "Google Cloud");
        }
    }

    /// <summary>DeepL API (free keys end with ":fx" and use api-free.deepl.com).</summary>
    public sealed class DeepLProvider : HttpProviderBase
    {
        public DeepLProvider(ProviderSettings s) : base(s) { }
        public override string Id { get { return ProviderIds.DeepL; } }
        public override int MaxBatchItems { get { return 50; } }
        public override int MaxBatchChars { get { return 25000; } }

        public override async Task<IList<string>> TranslateAsync(IList<string> texts, string src, string tgt, CancellationToken ct)
        {
            RequireKey();
            var key = Settings.ApiKey.Trim();
            var baseUrl = !string.IsNullOrWhiteSpace(Settings.Endpoint)
                ? Settings.Endpoint.TrimEnd('/')
                : (key.EndsWith(":fx", StringComparison.Ordinal) ? "https://api-free.deepl.com" : "https://api.deepl.com");
            var body = new Dictionary<string, object>
            {
                { "text", texts.Cast<object>().ToList() },
                { "target_lang", Languages.ForDeepL(tgt, true) },
                { "preserve_formatting", true },
            };
            var source = Languages.ForDeepL(src, false);
            if (source != null) body["source_lang"] = source;
            using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v2/translate") { Content = JsonContent(body) })
            {
                req.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + key);
                var json = await SendAsync(req, ct).ConfigureAwait(false);
                return ParseResponse(json, texts.Count);
            }
        }

        public static IList<string> ParseResponse(string json, int count)
        {
            var list = Json.GetList(Json.Parse(json), "translations");
            return ExpectCount(list == null ? null : list.Select(t => Json.GetString(t, "text")).ToList(), count, "DeepL");
        }
    }

    /// <summary>Microsoft Azure AI Translator (Text Translation v3).</summary>
    public sealed class AzureProvider : HttpProviderBase
    {
        public AzureProvider(ProviderSettings s) : base(s) { }
        public override string Id { get { return ProviderIds.Azure; } }
        public override int MaxBatchItems { get { return 100; } }
        public override int MaxBatchChars { get { return 40000; } }

        public override async Task<IList<string>> TranslateAsync(IList<string> texts, string src, string tgt, CancellationToken ct)
        {
            RequireKey();
            var endpoint = string.IsNullOrWhiteSpace(Settings.Endpoint) ? "https://api.cognitive.microsofttranslator.com" : Settings.Endpoint.TrimEnd('/');
            var url = endpoint + "/translate?api-version=3.0&to=" + Uri.EscapeDataString(Languages.ForAzure(tgt));
            var from = Languages.ForAzure(src);
            if (from != null) url += "&from=" + Uri.EscapeDataString(from);
            var body = texts.Select(t => (object)new Dictionary<string, object> { { "Text", t } }).ToList();
            using (var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) })
            {
                req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", Settings.ApiKey.Trim());
                if (!string.IsNullOrWhiteSpace(Settings.Region))
                    req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", Settings.Region.Trim());
                var json = await SendAsync(req, ct).ConfigureAwait(false);
                return ParseResponse(json, texts.Count);
            }
        }

        public static IList<string> ParseResponse(string json, int count)
        {
            var list = Json.Parse(json) as List<object>;
            return ExpectCount(list == null ? null : list.Select(t => Json.GetString(t, "translations", 0, "text")).ToList(), count, "Azure");
        }
    }

    /// <summary>LibreTranslate (self-hosted or hosted instance).</summary>
    public sealed class LibreTranslateProvider : HttpProviderBase
    {
        public LibreTranslateProvider(ProviderSettings s) : base(s) { }
        public override string Id { get { return ProviderIds.LibreTranslate; } }
        public override int MaxBatchItems { get { return 25; } }
        public override int MaxBatchChars { get { return 5000; } }

        public override async Task<IList<string>> TranslateAsync(IList<string> texts, string src, string tgt, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(Settings.Endpoint))
                throw new TranslationException("LibreTranslate: no server URL configured (run TRSettings).");
            var body = new Dictionary<string, object>
            {
                { "q", texts.Cast<object>().ToList() },
                { "source", Languages.ForLibre(src) },
                { "target", Languages.ForLibre(tgt) },
                { "format", "text" },
            };
            if (!string.IsNullOrWhiteSpace(Settings.ApiKey)) body["api_key"] = Settings.ApiKey.Trim();
            using (var req = new HttpRequestMessage(HttpMethod.Post, Settings.Endpoint.TrimEnd('/') + "/translate") { Content = JsonContent(body) })
            {
                var json = await SendAsync(req, ct).ConfigureAwait(false);
                return ParseResponse(json, texts.Count);
            }
        }

        public static IList<string> ParseResponse(string json, int count)
        {
            var root = Json.Parse(json);
            var value = Json.Get(root, "translatedText");
            var list = value as List<object>;
            if (list != null) return ExpectCount(list.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList(), count, "LibreTranslate");
            if (value is string && count == 1) return new List<string> { (string)value };
            throw new TranslationException("LibreTranslate: unexpected response.");
        }
    }

    /// <summary>MyMemory free API (anonymous: ~5000 chars/day; with e-mail: ~50000 chars/day).</summary>
    public sealed class MyMemoryProvider : HttpProviderBase
    {
        public MyMemoryProvider(ProviderSettings s) : base(s) { }
        public override string Id { get { return ProviderIds.MyMemory; } }
        public override int MaxBatchItems { get { return 1; } }
        public override int MaxBatchChars { get { return 480; } }

        public override Task<IList<string>> TranslateAsync(IList<string> texts, string src, string tgt, CancellationToken ct)
        {
            return TranslateEachAsync(texts, t => TranslateOne(t, src, tgt, ct), 2);
        }

        private async Task<string> TranslateOne(string text, string src, string tgt, CancellationToken ct)
        {
            var url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) +
                      "&langpair=" + Uri.EscapeDataString(Languages.ForMyMemory(src) + "|" + Languages.ForMyMemory(tgt));
            if (!string.IsNullOrWhiteSpace(Settings.Email)) url += "&de=" + Uri.EscapeDataString(Settings.Email.Trim());
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                var json = await SendAsync(req, ct).ConfigureAwait(false);
                return ParseResponse(json);
            }
        }

        public static string ParseResponse(string json)
        {
            var root = Json.Parse(json);
            var text = Json.GetString(root, "responseData", "translatedText");
            var status = Json.GetString(root, "responseStatus");
            if (text == null || (status != null && status != "200"))
                throw new TranslationException("MyMemory: " + Shorten(Json.GetString(root, "responseDetails") ?? text ?? "unexpected response"));
            if (text.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
                throw new TranslationException("MyMemory: daily quota exceeded.");
            return System.Net.WebUtility.HtmlDecode(text);
        }
    }

    /// <summary>
    /// Anthropic Claude via the Messages API. Produces context-aware translations of
    /// architectural terminology. Segments are sent as a JSON array and returned as one.
    /// </summary>
    public sealed class ClaudeProvider : HttpProviderBase
    {
        public const string DefaultModel = "claude-haiku-5-5";

        public ClaudeProvider(ProviderSettings s) : base(s) { }
        public override string Id { get { return ProviderIds.Claude; } }
        public override int MaxBatchItems { get { return 60; } }
        public override int MaxBatchChars { get { return 12000; } }

        public override async Task<IList<string>> TranslateAsync(IList<string> texts, string src, string tgt, CancellationToken ct)
        {
            RequireKey();
            var system =
                "You translate short annotation texts from architectural and engineering drawings (title blocks, labels, notes, room names). " +
                "Translate each array item from " + Languages.EnglishName(src) + " to " + Languages.EnglishName(tgt) + ". " +
                "Use standard professional terminology. Keep numbers, dimensions, units, drawing codes, abbreviations that should stay Latin, " +
                "and punctuation. Do not add explanations, quotes or transliterations. " +
                "Reply with only a JSON array of strings with exactly the same number of items, in the same order.";
            var body = new Dictionary<string, object>
            {
                { "model", string.IsNullOrWhiteSpace(Settings.Model) ? DefaultModel : Settings.Model.Trim() },
                { "max_tokens", 4096 },
                { "system", system },
                { "messages", new List<object> { new Dictionary<string, object> { { "role", "user" }, { "content", Json.Serialize(texts.ToList()) } } } },
            };
            var endpoint = string.IsNullOrWhiteSpace(Settings.Endpoint) ? "https://api.anthropic.com" : Settings.Endpoint.TrimEnd('/');
            using (var req = new HttpRequestMessage(HttpMethod.Post, endpoint + "/v1/messages") { Content = JsonContent(body) })
            {
                req.Headers.TryAddWithoutValidation("x-api-key", Settings.ApiKey.Trim());
                req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                var json = await SendAsync(req, ct).ConfigureAwait(false);
                return ParseResponse(json, texts.Count);
            }
        }

        public static IList<string> ParseResponse(string json, int count)
        {
            var root = Json.Parse(json);
            var content = Json.GetList(root, "content");
            if (content == null) throw new TranslationException("Claude: unexpected response.");
            var sb = new StringBuilder();
            foreach (var block in content)
                if (Json.GetString(block, "type") == "text") sb.Append(Json.GetString(block, "text"));
            var text = sb.ToString();
            int a = text.IndexOf('['), b = text.LastIndexOf(']');
            if (a < 0 || b <= a) throw new TranslationException("Claude: response did not contain a JSON array.");
            var arr = Json.Parse(text.Substring(a, b - a + 1)) as List<object>;
            return ExpectCount(arr == null ? null : arr.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList(), count, "Claude");
        }
    }
}
