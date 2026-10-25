using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using TextX.Core.Translation;

namespace TextX.Integration
{
    /// <summary>Shared translation engine, cache and glossary for the plug-in.</summary>
    public static class TranslationServices
    {
        private static TranslationCache _cache;
        private static Glossary _glossary;
        private static readonly object Lock = new object();

        private static TextXSettings Settings { get { return TextXPlugIn.Instance.Options; } }

        public static TranslationCache Cache
        {
            get
            {
                lock (Lock)
                {
                    if (_cache == null) _cache = TranslationCache.Load(Settings.CachePath);
                    return _cache;
                }
            }
        }

        public static Glossary Glossary
        {
            get
            {
                lock (Lock)
                {
                    if (_glossary == null) _glossary = Glossary.Load(Settings.GlossaryPath);
                    return _glossary;
                }
            }
        }

        public static void ReplaceGlossary(Glossary g)
        {
            lock (Lock) _glossary = g ?? new Glossary();
            SaveGlossary();
        }

        public static TranslationEngine CreateEngine(string providerId = null)
        {
            var s = Settings;
            var id = string.IsNullOrEmpty(providerId) ? s.ProviderId : providerId;
            var provider = ProviderFactory.Create(id, s.GetProviderSettings(id));
            ITranslationProvider fallback = null;
            var fb = s.FallbackProviderId;
            if (!string.IsNullOrEmpty(fb) && fb != id && fb != ProviderIds.Manual)
                fallback = ProviderFactory.Create(fb, s.GetProviderSettings(fb));
            return new TranslationEngine(provider, fallback, Glossary, Cache);
        }

        public static void SaveCache()
        {
            try
            {
                if (_cache != null && _cache.IsDirty) _cache.Save(Settings.CachePath);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Text X: could not save translation cache ({0}).", ex.Message);
            }
        }

        public static void SaveGlossary()
        {
            try
            {
                if (_glossary != null) _glossary.Save(Settings.GlossaryPath);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Text X: could not save glossary ({0}).", ex.Message);
            }
        }

        /// <summary>
        /// Runs a translation from a command or event handler while keeping Rhino responsive.
        /// Esc cancels. Returns null and sets <paramref name="error"/> on failure.
        /// </summary>
        public static TranslationOutcome TranslateBlocking(IList<string> texts, string sourceLang, string targetLang,
                                                           TimeSpan timeout, out string error, string providerId = null)
        {
            error = null;
            var engine = CreateEngine(providerId);
            using (var cts = new CancellationTokenSource(timeout))
            {
                EventHandler onEscape = (s, e) => cts.Cancel();
                RhinoApp.EscapeKeyPressed += onEscape;
                try
                {
                    var task = Task.Run(() => engine.TranslateManyAsync(texts, sourceLang, targetLang, cts.Token));
                    while (!task.IsCompleted)
                    {
                        RhinoApp.Wait();
                        Thread.Sleep(15);
                    }
                    if (task.IsCanceled)
                    {
                        error = "translation cancelled or timed out";
                        return null;
                    }
                    if (task.IsFaulted)
                    {
                        var ex = task.Exception == null ? null : task.Exception.GetBaseException();
                        error = ex is OperationCanceledException ? "translation cancelled or timed out" : (ex == null ? "unknown error" : ex.Message);
                        return null;
                    }
                    SaveCache();
                    return task.Result;
                }
                finally
                {
                    RhinoApp.EscapeKeyPressed -= onEscape;
                }
            }
        }
    }
}
