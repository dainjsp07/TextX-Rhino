using System;
using System.Collections.Generic;
using System.Linq;

namespace TextX.Core
{
    public sealed class Language
    {
        public Language(string code, string englishName, string nativeName, bool rtl)
        {
            Code = code;
            EnglishName = englishName;
            NativeName = nativeName;
            IsRightToLeft = rtl;
        }

        /// <summary>BCP-47 / ISO 639-1 code used internally (e.g. "he", "zh-CN").</summary>
        public string Code { get; private set; }
        public string EnglishName { get; private set; }
        public string NativeName { get; private set; }
        public bool IsRightToLeft { get; private set; }

        public string DisplayName
        {
            get
            {
                return string.IsNullOrEmpty(NativeName) || NativeName == EnglishName
                    ? EnglishName
                    : EnglishName + " - " + NativeName;
            }
        }

        public override string ToString() { return DisplayName; }
    }

    /// <summary>Supported languages and provider-specific language code mapping.</summary>
    public static class Languages
    {
        public const string AutoDetect = "auto";

        public static readonly IList<Language> All = new List<Language>
        {
            new Language("en", "English", "English", false),
            new Language("he", "Hebrew", "עברית", true),
            new Language("ar", "Arabic", "العربية", true),
            new Language("fa", "Persian", "فارسی", true),
            new Language("ur", "Urdu", "اردو", true),
            new Language("yi", "Yiddish", "ייִדיש", true),
            new Language("fr", "French", "Français", false),
            new Language("de", "German", "Deutsch", false),
            new Language("es", "Spanish", "Español", false),
            new Language("it", "Italian", "Italiano", false),
            new Language("pt", "Portuguese", "Português", false),
            new Language("nl", "Dutch", "Nederlands", false),
            new Language("ru", "Russian", "Русский", false),
            new Language("uk", "Ukrainian", "Українська", false),
            new Language("pl", "Polish", "Polski", false),
            new Language("cs", "Czech", "Čeština", false),
            new Language("sk", "Slovak", "Slovenčina", false),
            new Language("hu", "Hungarian", "Magyar", false),
            new Language("ro", "Romanian", "Română", false),
            new Language("bg", "Bulgarian", "Български", false),
            new Language("el", "Greek", "Ελληνικά", false),
            new Language("tr", "Turkish", "Türkçe", false),
            new Language("sv", "Swedish", "Svenska", false),
            new Language("da", "Danish", "Dansk", false),
            new Language("no", "Norwegian", "Norsk", false),
            new Language("fi", "Finnish", "Suomi", false),
            new Language("et", "Estonian", "Eesti", false),
            new Language("lv", "Latvian", "Latviešu", false),
            new Language("lt", "Lithuanian", "Lietuvių", false),
            new Language("sl", "Slovenian", "Slovenščina", false),
            new Language("hr", "Croatian", "Hrvatski", false),
            new Language("sr", "Serbian", "Српски", false),
            new Language("zh-CN", "Chinese (Simplified)", "简体中文", false),
            new Language("zh-TW", "Chinese (Traditional)", "繁體中文", false),
            new Language("ja", "Japanese", "日本語", false),
            new Language("ko", "Korean", "한국어", false),
            new Language("hi", "Hindi", "हिन्दी", false),
            new Language("th", "Thai", "ไทย", false),
            new Language("vi", "Vietnamese", "Tiếng Việt", false),
            new Language("id", "Indonesian", "Bahasa Indonesia", false),
            new Language("ms", "Malay", "Bahasa Melayu", false),
            new Language("ka", "Georgian", "ქართული", false),
            new Language("hy", "Armenian", "Հայերեն", false),
        };

        public static Language Find(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            return All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsRtl(string code)
        {
            var l = Find(code);
            if (l != null) return l.IsRightToLeft;
            if (string.IsNullOrEmpty(code)) return false;
            var primary = code.Split('-')[0].ToLowerInvariant();
            return primary == "he" || primary == "iw" || primary == "ar" || primary == "fa" || primary == "ur" ||
                   primary == "yi" || primary == "ps" || primary == "sd" || primary == "ug" || primary == "dv" || primary == "ckb";
        }

        public static string EnglishName(string code)
        {
            if (code == AutoDetect) return "auto-detected language";
            var l = Find(code);
            return l != null ? l.EnglishName : code;
        }

        /// <summary>Code for the legacy Google endpoint (uses "iw" for Hebrew).</summary>
        public static string ForGoogleFree(string code)
        {
            if (code == AutoDetect) return "auto";
            if (string.Equals(code, "he", StringComparison.OrdinalIgnoreCase)) return "iw";
            return code;
        }

        public static string ForGoogleCloud(string code)
        {
            return code;
        }

        /// <summary>DeepL uses upper-case codes and regional variants for some targets.</summary>
        public static string ForDeepL(string code, bool isTarget)
        {
            if (code == AutoDetect) return null;
            var c = code.ToUpperInvariant();
            if (isTarget)
            {
                if (c == "EN") return "EN-US";
                if (c == "PT") return "PT-PT";
                if (c == "ZH-CN") return "ZH-HANS";
                if (c == "ZH-TW") return "ZH-HANT";
                if (c == "NO") return "NB";
                return c;
            }
            if (c.StartsWith("ZH")) return "ZH";
            if (c == "NO") return "NB";
            return c.Split('-')[0];
        }

        public static string ForAzure(string code)
        {
            if (code == AutoDetect) return null;
            if (string.Equals(code, "zh-CN", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
            if (string.Equals(code, "zh-TW", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
            if (string.Equals(code, "no", StringComparison.OrdinalIgnoreCase)) return "nb";
            return code;
        }

        public static string ForLibre(string code)
        {
            if (code == AutoDetect) return "auto";
            if (string.Equals(code, "zh-CN", StringComparison.OrdinalIgnoreCase)) return "zh";
            if (string.Equals(code, "zh-TW", StringComparison.OrdinalIgnoreCase)) return "zt";
            if (string.Equals(code, "no", StringComparison.OrdinalIgnoreCase)) return "nb";
            return code;
        }

        public static string ForMyMemory(string code)
        {
            if (code == AutoDetect) return "en";
            return code;
        }
    }
}
