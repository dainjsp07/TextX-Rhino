using System.Linq;
using System.Text.RegularExpressions;

namespace TextX.Core.Translation
{
    /// <summary>
    /// Drawings are often lettered in capitals ("3X3 GROOVE AS PER CLIENT"). Translation services take words
    /// written in capitals for names and leave them as they are ("3X3 GROOVE לפי לקוח"), so such text is
    /// sent in small letters and its capitals are put back afterwards: all of it for a Latin-script
    /// language, the English names and codes kept in it for Hebrew and other scripts.
    /// </summary>
    public static class CaseFold
    {
        private static readonly Regex Word = new Regex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant);

        /// <summary>True for Latin text in capitals with at least one real word (four letters or more in all).</summary>
        public static bool IsShouting(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int upper = 0, lower = 0;
            bool word = false;
            foreach (Match m in Word.Matches(text))
            {
                var t = m.Value;
                if (t.Any(char.IsDigit)) continue;
                if (t.Any(c => char.IsLetter(c) && c > 0x024F)) return false;
                upper += t.Count(char.IsUpper);
                lower += t.Count(char.IsLower);
                if (t.Count(char.IsLetter) >= 3) word = true;
            }
            return word && lower == 0 && upper >= 4;
        }

        /// <summary>The text in small letters, codes with digits ("3X3", "EP-207") kept as they are.</summary>
        public static string Fold(string text)
        {
            if (!IsShouting(text)) return text;
            return Word.Replace(text, m => m.Value.Any(char.IsDigit) ? m.Value : m.Value.ToLowerInvariant());
        }

        /// <summary>Puts the capitals back in a translation of <paramref name="source"/>.</summary>
        public static string Restore(string translation, string source)
        {
            if (string.IsNullOrEmpty(translation) || !IsShouting(source)) return translation;
            // a Latin-script translation is lettered like the original
            if (!translation.Any(c => char.IsLetter(c) && c > 0x024F)) return translation.ToUpperInvariant();
            // another script: English names and codes the service kept get their capitals again
            var words = new System.Collections.Generic.HashSet<string>(Word.Matches(source).Cast<Match>().Select(m => m.Value.ToLowerInvariant()));
            return Word.Replace(translation, m => m.Value.All(c => !char.IsLetter(c) || c <= 0x024F) && words.Contains(m.Value.ToLowerInvariant())
                ? m.Value.ToUpperInvariant() : m.Value);
        }

        /// <summary>The memory key of a text sent in small letters (older answers to the capitals are not reused).</summary>
        public static string CacheText(string text)
        {
            return IsShouting(text) ? text + "\u0001lower" : text;
        }
    }
}
