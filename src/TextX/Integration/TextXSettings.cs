using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Rhino;
using Rhino.PlugIns;
using TextX.Core;
using TextX.Core.Text;
using TextX.Core.Translation;

namespace TextX.Integration
{
    public enum StaleTranslationHandling
    {
        /// <summary>Re-translate edited source text just before printing (falls back to the source text).</summary>
        Retranslate = 0,
        /// <summary>Print the edited source text unchanged.</summary>
        PrintSource = 1,
        /// <summary>Print the previously stored translation.</summary>
        PrintOldTranslation = 2,
    }

    public enum SwapScope
    {
        AllObjects = 0,
        LayoutObjectsOnly = 1,
    }

    /// <summary>
    /// Persistent plug-in settings (stored by Rhino in the plug-in settings XML).
    /// API keys are encrypted with Windows DPAPI for the current user.
    /// </summary>
    public sealed class TextXSettings
    {
        private readonly PersistentSettings _s;

        public TextXSettings(PersistentSettings settings, string settingsDirectory)
        {
            _s = settings;
            SettingsDirectory = settingsDirectory;
            try { if (!Directory.Exists(settingsDirectory)) Directory.CreateDirectory(settingsDirectory); }
            catch { /* read-only profile; caching just won't persist */ }
        }

        public string SettingsDirectory { get; private set; }
        public string CachePath { get { return Path.Combine(SettingsDirectory, "translation-cache.json"); } }
        public string GlossaryPath { get { return Path.Combine(SettingsDirectory, "glossary.json"); } }

        // ------------------------------------------------------------ languages
        public string SourceLanguage
        {
            get { return _s.GetString("SourceLanguage", "en"); }
            set { _s.SetString("SourceLanguage", value ?? "en"); }
        }

        public string TargetLanguage
        {
            get { return _s.GetString("TargetLanguage", "he"); }
            set { _s.SetString("TargetLanguage", value ?? "he"); }
        }

        // ------------------------------------------------------------ providers
        public string ProviderId
        {
            get { return _s.GetString("Provider", ProviderIds.GoogleFree); }
            set { _s.SetString("Provider", value ?? ProviderIds.GoogleFree); }
        }

        public string FallbackProviderId
        {
            get { return _s.GetString("FallbackProvider", ProviderIds.MyMemory); }
            set { _s.SetString("FallbackProvider", value ?? string.Empty); }
        }

        public bool RealTimeTranslation
        {
            get { return _s.GetBool("RealTime", true); }
            set { _s.SetBool("RealTime", value); }
        }

        public int RealTimeDelayMs
        {
            get { return Math.Max(200, _s.GetInteger("RealTimeDelayMs", 700)); }
            set { _s.SetInteger("RealTimeDelayMs", value); }
        }

        public int TimeoutSeconds
        {
            get { return Math.Max(3, _s.GetInteger("TimeoutSeconds", 20)); }
            set { _s.SetInteger("TimeoutSeconds", value); }
        }

        public ProviderSettings GetProviderSettings(string providerId)
        {
            return new ProviderSettings
            {
                ApiKey = SecretProtector.Unprotect(_s.GetString("Key." + providerId, string.Empty)),
                Endpoint = _s.GetString("Endpoint." + providerId, string.Empty),
                Region = _s.GetString("Region." + providerId, string.Empty),
                Model = _s.GetString("Model." + providerId, providerId == ProviderIds.Claude ? Core.Translation.Providers.ClaudeProvider.DefaultModel : string.Empty),
                Email = _s.GetString("Email." + providerId, string.Empty),
                TimeoutSeconds = TimeoutSeconds,
            };
        }

        public void SetProviderSettings(string providerId, ProviderSettings ps)
        {
            _s.SetString("Key." + providerId, string.IsNullOrEmpty(ps.ApiKey) ? string.Empty : SecretProtector.Protect(ps.ApiKey));
            _s.SetString("Endpoint." + providerId, ps.Endpoint ?? string.Empty);
            _s.SetString("Region." + providerId, ps.Region ?? string.Empty);
            _s.SetString("Model." + providerId, ps.Model ?? string.Empty);
            _s.SetString("Email." + providerId, ps.Email ?? string.Empty);
        }

        // ------------------------------------------------------------ defaults for new / updated text
        public TranslationDisplayMode DefaultMode
        {
            get { return ParseEnum(_s.GetString("DefaultMode", "Dual"), TranslationDisplayMode.Dual); }
            set { _s.SetString("DefaultMode", value.ToString()); }
        }

        public bool AutoRightAlignRtl
        {
            get { return _s.GetBool("AutoRightAlignRtl", true); }
            set { _s.SetBool("AutoRightAlignRtl", value); }
        }

        // ------------------------------------------------------------ RTL rendering
        public RtlRenderMode ScreenRtlMode
        {
            get { return ParseEnum(_s.GetString("ScreenRtlMode", "Visual"), RtlRenderMode.Visual); }
            set { _s.SetString("ScreenRtlMode", value.ToString()); }
        }

        public RtlRenderMode PrintRtlMode
        {
            get { return ParseEnum(_s.GetString("PrintRtlMode", "Visual"), RtlRenderMode.Visual); }
            set { _s.SetString("PrintRtlMode", value.ToString()); }
        }

        public bool KeepSignWithNumber
        {
            get { return _s.GetBool("KeepSignWithNumber", true); }
            set { _s.SetBool("KeepSignWithNumber", value); }
        }

        public bool ShapeArabic
        {
            get { return _s.GetBool("ShapeArabic", true); }
            set { _s.SetBool("ShapeArabic", value); }
        }

        public bool MirrorBrackets
        {
            get { return _s.GetBool("MirrorBrackets", true); }
            set { _s.SetBool("MirrorBrackets", value); }
        }

        public RtlOptions CreateRtlOptions()
        {
            return new RtlOptions
            {
                KeepSignWithNumber = KeepSignWithNumber,
                ShapeArabic = ShapeArabic,
                MirrorBrackets = MirrorBrackets,
                ProtectRhinoFields = true,
                StripInvisibleControls = true,
            };
        }

        // ------------------------------------------------------------ print / PDF swap
        public bool AutoSwapEnabled
        {
            get { return _s.GetBool("AutoSwap", true); }
            set { _s.SetBool("AutoSwap", value); }
        }

        /// <summary>Commands that always print with translations (swap at command start).</summary>
        public string PrintCommands
        {
            get { return _s.GetString("PrintCommands", "Print"); }
            set { _s.SetString("PrintCommands", value ?? string.Empty); }
        }

        /// <summary>Commands that may write a print-like file (swap when the file type is known).</summary>
        public string ExportCommands
        {
            get { return _s.GetString("ExportCommands", "Export;SaveAs;ExportWithOrigin;SaveAsTemplate"); }
            set { _s.SetString("ExportCommands", value ?? string.Empty); }
        }

        /// <summary>File types that receive translated text when written by Export / Save As.</summary>
        public string PrintLikeExtensions
        {
            get { return _s.GetString("PrintLikeExtensions", ".pdf;.ai;.eps;.svg"); }
            set { _s.SetString("PrintLikeExtensions", value ?? string.Empty); }
        }

        /// <summary>
        /// Set automatically when Rhino turns out not to report the file name before writing a PDF:
        /// the swap then happens at the start of Export / Save As commands instead.
        /// </summary>
        public bool SwapExportsAtCommandStart
        {
            get { return _s.GetBool("SwapExportsAtCommandStart", false); }
            set { _s.SetBool("SwapExportsAtCommandStart", value); }
        }

        public SwapScope Scope
        {
            get { return ParseEnum(_s.GetString("SwapScope", "AllObjects"), SwapScope.AllObjects); }
            set { _s.SetString("SwapScope", value.ToString()); }
        }

        public bool IncludeBlockDefinitions
        {
            get { return _s.GetBool("IncludeBlockDefinitions", true); }
            set { _s.SetBool("IncludeBlockDefinitions", value); }
        }

        public StaleTranslationHandling StaleHandling
        {
            get { return ParseEnum(_s.GetString("StaleHandling", "Retranslate"), StaleTranslationHandling.Retranslate); }
            set { _s.SetString("StaleHandling", value.ToString()); }
        }

        public bool ShowSwapMessages
        {
            get { return _s.GetBool("ShowSwapMessages", true); }
            set { _s.SetBool("ShowSwapMessages", value); }
        }

        // ------------------------------------------------------------ appearance
        /// <summary>"Auto" (follow Rhino's light/dark mode), "Dark" or "Light".</summary>
        public string ThemeMode
        {
            get { return _s.GetString("ThemeMode", "Auto"); }
            set { _s.SetString("ThemeMode", string.IsNullOrEmpty(value) ? "Auto" : value); }
        }

        /// <summary>Last window position and size as "x,y,width,height" (screen units).</summary>
        public string WindowBounds
        {
            get { return _s.GetString("WindowBounds", string.Empty); }
            set { _s.SetString("WindowBounds", value ?? string.Empty); }
        }

        // ------------------------------------------------------------ updates
        public bool CheckForUpdates
        {
            get { return _s.GetBool("CheckForUpdates", true); }
            set { _s.SetBool("CheckForUpdates", value); }
        }

        public bool IncludePreReleases
        {
            get { return _s.GetBool("IncludePreReleases", false); }
            set { _s.SetBool("IncludePreReleases", value); }
        }

        /// <summary>Overrides the update feed built into the plug-in (empty = use the built-in one).</summary>
        public string UpdateFeedOverride
        {
            get { return _s.GetString("UpdateFeedOverride", string.Empty); }
            set { _s.SetString("UpdateFeedOverride", (value ?? string.Empty).Trim()); }
        }

        public DateTime LastUpdateCheckUtc
        {
            get
            {
                DateTime t;
                return DateTime.TryParse(_s.GetString("LastUpdateCheckUtc", string.Empty), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t) ? t : DateTime.MinValue;
            }
            set { _s.SetString("LastUpdateCheckUtc", value.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture)); }
        }

        /// <summary>The latest release seen by the last update check, as feed JSON (empty if none).</summary>
        public string LatestReleaseJson
        {
            get { return _s.GetString("LatestReleaseJson", string.Empty); }
            set { _s.SetString("LatestReleaseJson", value ?? string.Empty); }
        }

        /// <summary>A version the user chose to be reminded about later (the banner stays hidden for it).</summary>
        public string DismissedUpdateVersion
        {
            get { return _s.GetString("DismissedUpdateVersion", string.Empty); }
            set { _s.SetString("DismissedUpdateVersion", value ?? string.Empty); }
        }

        /// <summary>Version that ran last time (used to announce an update once).</summary>
        public string LastRunVersion
        {
            get { return _s.GetString("LastRunVersion", string.Empty); }
            set { _s.SetString("LastRunVersion", value ?? string.Empty); }
        }

        public static IEnumerable<string> SplitList(string list)
        {
            return (list ?? string.Empty).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0);
        }

        public bool IsPrintCommand(string englishName)
        {
            return SplitList(PrintCommands).Any(c => string.Equals(c.TrimStart('-', '_', '!'), englishName, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsExportCommand(string englishName)
        {
            return SplitList(ExportCommands).Any(c => string.Equals(c.TrimStart('-', '_', '!'), englishName, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsPrintLikeFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            var ext = Path.GetExtension(fileName);
            return SplitList(PrintLikeExtensions).Any(e => string.Equals(e.StartsWith(".") ? e : "." + e, ext, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------ PDF export command defaults
        public int PdfDpi
        {
            get { return Math.Max(72, _s.GetInteger("PdfDpi", 300)); }
            set { _s.SetInteger("PdfDpi", value); }
        }

        public bool PdfOpenAfterExport
        {
            get { return _s.GetBool("PdfOpenAfterExport", true); }
            set { _s.SetBool("PdfOpenAfterExport", value); }
        }

        // ------------------------------------------------------------ helpers
        private static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            T v;
            return Enum.TryParse(s, true, out v) ? v : fallback;
        }
    }

    /// <summary>Encrypts secrets for the current Windows user (DPAPI). Falls back to base64 elsewhere.</summary>
    internal static class SecretProtector
    {
        private const string DpapiPrefix = "dpapi:";
        private const string PlainPrefix = "b64:";

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob pDataIn, string szDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        private static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        public static string Protect(string secret)
        {
            if (string.IsNullOrEmpty(secret)) return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(secret);
            if (IsWindows)
            {
                try
                {
                    var result = Transform(bytes, true);
                    if (result != null) return DpapiPrefix + Convert.ToBase64String(result);
                }
                catch
                {
                    // fall through
                }
            }
            return PlainPrefix + Convert.ToBase64String(bytes);
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            try
            {
                if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal) && IsWindows)
                {
                    var result = Transform(Convert.FromBase64String(stored.Substring(DpapiPrefix.Length)), false);
                    return result == null ? string.Empty : Encoding.UTF8.GetString(result);
                }
                if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
                    return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(PlainPrefix.Length)));
            }
            catch
            {
                return string.Empty;
            }
            return stored;
        }

        private static byte[] Transform(byte[] data, bool protect)
        {
            var input = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            var output = new DataBlob();
            try
            {
                Marshal.Copy(data, 0, input.pbData, data.Length);
                bool ok = protect
                    ? CryptProtectData(ref input, "RhinoTranslator", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output);
                if (!ok) return null;
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }
    }
}
