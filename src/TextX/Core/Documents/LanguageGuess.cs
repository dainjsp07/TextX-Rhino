using System;
using System.Collections.Generic;
using System.Linq;

namespace TextX.Core.Documents
{
    /// <summary>
    /// Offline guess of a text's language from its alphabet (used before or without the online
    /// detector). Distinguishes scripts reliably; Latin-script languages are left to the service.
    /// </summary>
    public static class LanguageGuess
    {
        /// <summary>A Text X language code, or null when the script does not identify the language (Latin).</summary>
        public static string FromScript(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var counts = new Dictionary<string, int>();
            Action<string> add = k => { int n; counts.TryGetValue(k, out n); counts[k] = n + 1; };
            foreach (var c in text)
            {
                if (c >= 0x0590 && c <= 0x05FF) add("he");
                else if (c >= 0x0600 && c <= 0x06FF)
                {
                    if (c == 0x067E || c == 0x0686 || c == 0x0698 || c == 0x06AF || c == 0x06CC) add("fa");
                    else if (c == 0x0679 || c == 0x0688 || c == 0x0691 || c == 0x06BA || c == 0x06BE || c == 0x06C1 || c == 0x06D2) add("ur");
                    else add("ar");
                }
                else if (c >= 0x0400 && c <= 0x04FF)
                {
                    if (c == 'і' || c == 'ї' || c == 'є' || c == 'ґ' || c == 'І' || c == 'Ї' || c == 'Є' || c == 'Ґ') add("uk");
                    else if (c == 'ђ' || c == 'ћ' || c == 'џ' || c == 'Ђ' || c == 'Ћ' || c == 'Џ' || c == 'ј' || c == 'Ј') add("sr");
                    else add("ru");
                }
                else if (c >= 0x0370 && c <= 0x03FF) add("el");
                else if (c >= 0x3040 && c <= 0x30FF) add("ja");
                else if (c >= 0xAC00 && c <= 0xD7AF) add("ko");
                else if (c >= 0x4E00 && c <= 0x9FFF) add("zh");
                else if (c >= 0x0E00 && c <= 0x0E7F) add("th");
                else if (c >= 0x0900 && c <= 0x097F) add("hi");
                else if (c >= 0x10A0 && c <= 0x10FF) add("ka");
                else if (c >= 0x0530 && c <= 0x058F) add("hy");
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= 0x00C0 && c <= 0x024F)) add("latin");
            }
            if (counts.Count == 0) return null;
            // Arabic-script and Cyrillic variants: a few telltale letters decide
            int ar = Get(counts, "ar"), fa = Get(counts, "fa"), ur = Get(counts, "ur");
            if (ar + fa + ur > 0) { counts.Remove("fa"); counts.Remove("ur"); counts["ar"] = ar + fa + ur; }
            int ru = Get(counts, "ru"), uk = Get(counts, "uk"), sr = Get(counts, "sr");
            if (ru + uk + sr > 0) { counts.Remove("uk"); counts.Remove("sr"); counts["ru"] = ru + uk + sr; }
            // kana means Japanese even when most characters are kanji
            if (Get(counts, "ja") > 0 && Get(counts, "zh") > 0) { counts["ja"] += counts["zh"]; counts.Remove("zh"); }
            // a non-Latin script that makes up a good share of the letters decides (documents mixing in English)
            int total = counts.Values.Sum();
            var nonLatin = counts.Where(kv => kv.Key != "latin").OrderByDescending(kv => kv.Value).ToList();
            if (nonLatin.Count == 0 || nonLatin[0].Value < total * 0.3) return null;
            var top = nonLatin[0];
            if (top.Key == "ar") return fa > ur && fa > 0 ? "fa" : ur > 0 ? "ur" : "ar";
            if (top.Key == "ru") return uk > 0 && uk >= sr ? "uk" : sr > 0 ? "sr" : "ru";
            if (top.Key == "zh") return "zh-CN";
            return top.Key;
        }

        private static int Get(Dictionary<string, int> d, string k)
        {
            int n;
            return d.TryGetValue(k, out n) ? n : 0;
        }

        /// <summary>
        /// True for references that read the same in every language: colour and standard codes
        /// ("RAL 9016", "NCS S 0502-Y", "EN 13501-1", "Ø 35 mm") — short capital abbreviations and units
        /// around numbers, with no ordinary word.
        /// </summary>
        public static bool IsReference(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 40) return false;
            bool number = false;
            foreach (var token in text.Split(new[] { ' ', '\t', '\n', '/', ',', ';', ':', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Any(char.IsDigit)) { number = true; continue; }
                var letters = new string(token.Where(char.IsLetter).ToArray());
                if (letters.Length == 0) continue;
                if (letters.Length <= 4 && letters.All(c => c < 0x0250 && char.IsUpper(c))) continue;
                if (Units.Contains(letters)) continue;
                return false;
            }
            return number;
        }

        private static readonly HashSet<string> Units = new HashSet<string>(StringComparer.Ordinal)
        {
            "mm", "cm", "m", "km", "in", "ft", "kg", "g", "t", "l", "ml", "kW", "W", "V", "A", "Hz", "Pa", "kPa", "MPa", "kN", "N", "dB", "lx", "lm", "K", "x", "X", "Ø", "pcs"
        };

        /// <summary>True when the text contains letters (otherwise it is numbers, codes or symbols: nothing to translate).</summary>
        public static bool HasWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int letters = 0;
            foreach (var c in text) if (char.IsLetter(c)) letters++;
            return letters >= 2 || (letters == 1 && text.Trim().Length == 1 && char.IsLetter(text.Trim()[0]) && text.Trim()[0] > 0x2E80);
        }
    }
}
