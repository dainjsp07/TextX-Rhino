using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TextX.Core.Translation
{
    public sealed class TranslationException : Exception
    {
        public TranslationException(string message) : base(message) { }
        public TranslationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Per-provider configuration (keys are stored encrypted by the plugin settings).</summary>
    public sealed class ProviderSettings
    {
        public string ApiKey { get; set; }
        public string Endpoint { get; set; }
        public string Region { get; set; }
        public string Model { get; set; }
        public string Email { get; set; }
        public int TimeoutSeconds { get; set; }

        public ProviderSettings()
        {
            TimeoutSeconds = 20;
        }
    }

    public interface ITranslationProvider
    {
        /// <summary>Stable identifier stored in settings and object metadata.</summary>
        string Id { get; }
        string DisplayName { get; }
        bool RequiresApiKey { get; }

        /// <summary>Maximum number of segments per request.</summary>
        int MaxBatchItems { get; }

        /// <summary>Maximum total characters per request.</summary>
        int MaxBatchChars { get; }

        /// <summary>Translates segments (same count and order in the result).</summary>
        Task<IList<string>> TranslateAsync(IList<string> texts, string sourceLang, string targetLang, CancellationToken cancellationToken);
    }

    public static class ProviderIds
    {
        public const string Manual = "manual";
        public const string GoogleFree = "google-free";
        public const string GoogleCloud = "google-cloud";
        public const string DeepL = "deepl";
        public const string Azure = "azure";
        public const string LibreTranslate = "libretranslate";
        public const string MyMemory = "mymemory";
        public const string Claude = "claude";

        public static readonly string[] All = { GoogleFree, GoogleCloud, DeepL, Azure, Claude, LibreTranslate, MyMemory, Manual };

        public static string DisplayName(string id)
        {
            switch (id)
            {
                case Manual: return "Manual (no machine translation)";
                case GoogleFree: return "Google Translate (free web endpoint, no key)";
                case GoogleCloud: return "Google Cloud Translation (API key)";
                case DeepL: return "DeepL (API key)";
                case Azure: return "Microsoft Azure Translator (API key)";
                case LibreTranslate: return "LibreTranslate (server URL)";
                case MyMemory: return "MyMemory (free, limited daily quota)";
                case Claude: return "Anthropic Claude (API key)";
                default: return id;
            }
        }

        public static bool RequiresKey(string id)
        {
            return id == GoogleCloud || id == DeepL || id == Azure || id == Claude;
        }
    }

    public static class ProviderFactory
    {
        public static ITranslationProvider Create(string id, ProviderSettings settings)
        {
            settings = settings ?? new ProviderSettings();
            switch (id)
            {
                case ProviderIds.GoogleFree: return new Providers.GoogleFreeProvider(settings);
                case ProviderIds.GoogleCloud: return new Providers.GoogleCloudProvider(settings);
                case ProviderIds.DeepL: return new Providers.DeepLProvider(settings);
                case ProviderIds.Azure: return new Providers.AzureProvider(settings);
                case ProviderIds.LibreTranslate: return new Providers.LibreTranslateProvider(settings);
                case ProviderIds.MyMemory: return new Providers.MyMemoryProvider(settings);
                case ProviderIds.Claude: return new Providers.ClaudeProvider(settings);
                default: return new Providers.ManualProvider();
            }
        }
    }
}
