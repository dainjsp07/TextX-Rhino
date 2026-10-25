using System;
using System.Globalization;

namespace TextX.Core
{
    /// <summary>What an annotation shows on screen vs. in print/PDF.</summary>
    public enum TranslationDisplayMode
    {
        /// <summary>Source (English) on screen, translation when printing / exporting PDF.</summary>
        Dual = 0,

        /// <summary>Translation on screen and in print.</summary>
        Translated = 1,

        /// <summary>Source on screen and in print (translation only stored).</summary>
        Source = 2,
    }

    /// <summary>Which text an annotation currently contains.</summary>
    public enum TextState
    {
        Display = 0,
        Print = 1,
    }

    public enum PrintAlignment
    {
        /// <summary>Keep the object's own alignment.</summary>
        Inherit = 0,
        Left = 1,
        Center = 2,
        Right = 3,
    }

    /// <summary>
    /// Translation metadata stored on each annotation object as Attribute User Text
    /// (visible in Rhino's Properties > Attribute User Text panel, survives copy, save,
    /// block definitions, worksessions and export to 3dm).
    /// </summary>
    public sealed class TranslationRecord
    {
        // Kept from version 1.0 ("Translator"): changing it would orphan the data in existing drawings.
        public const string Prefix = "RhinoTranslator.";
        public const string KeyVersion = Prefix + "Version";
        public const string KeySource = Prefix + "Source";
        public const string KeyTranslation = Prefix + "Translation";
        public const string KeySourceLang = Prefix + "SourceLanguage";
        public const string KeyTargetLang = Prefix + "TargetLanguage";
        public const string KeyMode = Prefix + "Mode";
        public const string KeyState = Prefix + "State";
        public const string KeyPrintAlign = Prefix + "PrintAlignment";
        public const string KeyAnchorToSource = Prefix + "PrintAnchorToSourceBox";
        public const string KeyWrapWidth = Prefix + "WrapWidth";
        public const string KeyEdited = Prefix + "ManuallyEdited";
        public const string KeyProvider = Prefix + "Provider";
        public const string KeyUpdated = Prefix + "Updated";
        // saved display-state values (for crash-safe restore after a print swap)
        public const string KeySavedText = Prefix + "SavedDisplayText";
        public const string KeySavedHAlign = Prefix + "SavedHorizontalAlignment";
        public const string KeyAnchorShift = Prefix + "PrintAnchorShift";
        public const string KeySavedWrapped = Prefix + "SavedWrapped";
        public const string KeySavedFormatWidth = Prefix + "SavedFormatWidth";

        public static readonly string[] AllKeys =
        {
            KeyVersion, KeySource, KeyTranslation, KeySourceLang, KeyTargetLang, KeyMode, KeyState, KeyPrintAlign,
            KeyAnchorToSource, KeyWrapWidth, KeyEdited, KeyProvider, KeyUpdated, KeySavedText, KeySavedHAlign,
            KeyAnchorShift, KeySavedWrapped, KeySavedFormatWidth,
        };

        public const string CurrentVersion = "1";

        public string Source = string.Empty;
        public string Translation = string.Empty;
        public string SourceLang = "en";
        public string TargetLang = "he";
        public TranslationDisplayMode Mode = TranslationDisplayMode.Dual;
        public TextState State = TextState.Display;
        public PrintAlignment PrintAlign = PrintAlignment.Inherit;
        public bool AnchorToSourceBox = true;
        public double WrapWidth;
        public bool ManuallyEdited;
        public string Provider = string.Empty;
        public string Updated = string.Empty;

        // saved display state while swapped to print
        public string SavedText;
        public int SavedHAlign = -1;
        /// <summary>Shift applied to the text anchor in print state, in plane coordinates "dx,dy,0".</summary>
        public string AnchorShift;
        public int SavedWrapped = -1;
        public double SavedFormatWidth;

        /// <summary>Which logical text should be shown for the given state.</summary>
        public string TextFor(TextState state)
        {
            switch (Mode)
            {
                case TranslationDisplayMode.Translated:
                    return string.IsNullOrEmpty(Translation) ? Source : Translation;
                case TranslationDisplayMode.Source:
                    return Source;
                default:
                    if (state == TextState.Print && !string.IsNullOrEmpty(Translation)) return Translation;
                    return Source;
            }
        }

        /// <summary>Language of the text shown for the given state.</summary>
        public string LanguageFor(TextState state)
        {
            bool hasTranslation = !string.IsNullOrEmpty(Translation);
            switch (Mode)
            {
                case TranslationDisplayMode.Translated: return hasTranslation ? TargetLang : SourceLang;
                case TranslationDisplayMode.Source: return SourceLang;
                default: return state == TextState.Print && hasTranslation ? TargetLang : SourceLang;
            }
        }

        /// <summary>True when display and print content differ (only these objects are swapped).</summary>
        public bool NeedsPrintSwap
        {
            get { return Mode == TranslationDisplayMode.Dual && !string.IsNullOrEmpty(Translation) && Translation != Source; }
        }

        public static bool HasRecord(Func<string, string> get)
        {
            return !string.IsNullOrEmpty(get(KeyVersion)) || !string.IsNullOrEmpty(get(KeySource));
        }

        public static TranslationRecord Read(Func<string, string> get)
        {
            if (!HasRecord(get)) return null;
            var r = new TranslationRecord
            {
                Source = get(KeySource) ?? string.Empty,
                Translation = get(KeyTranslation) ?? string.Empty,
                SourceLang = get(KeySourceLang) ?? "en",
                TargetLang = get(KeyTargetLang) ?? "he",
                Mode = ParseEnum(get(KeyMode), TranslationDisplayMode.Dual),
                State = ParseEnum(get(KeyState), TextState.Display),
                PrintAlign = ParseEnum(get(KeyPrintAlign), PrintAlignment.Inherit),
                AnchorToSourceBox = get(KeyAnchorToSource) != "0",
                WrapWidth = ParseDouble(get(KeyWrapWidth)),
                ManuallyEdited = get(KeyEdited) == "1",
                Provider = get(KeyProvider) ?? string.Empty,
                Updated = get(KeyUpdated) ?? string.Empty,
                SavedText = get(KeySavedText),
                SavedHAlign = ParseInt(get(KeySavedHAlign), -1),
                AnchorShift = get(KeyAnchorShift),
                SavedWrapped = ParseInt(get(KeySavedWrapped), -1),
                SavedFormatWidth = ParseDouble(get(KeySavedFormatWidth)),
            };
            return r;
        }

        /// <summary>Writes all keys (null values remove a key).</summary>
        public void Write(Action<string, string> set)
        {
            set(KeyVersion, CurrentVersion);
            set(KeySource, Source ?? string.Empty);
            set(KeyTranslation, Translation ?? string.Empty);
            set(KeySourceLang, SourceLang);
            set(KeyTargetLang, TargetLang);
            set(KeyMode, Mode.ToString());
            set(KeyState, State.ToString());
            set(KeyPrintAlign, PrintAlign.ToString());
            set(KeyAnchorToSource, AnchorToSourceBox ? "1" : "0");
            set(KeyWrapWidth, WrapWidth > 0 ? WrapWidth.ToString("R", CultureInfo.InvariantCulture) : null);
            set(KeyEdited, ManuallyEdited ? "1" : null);
            set(KeyProvider, string.IsNullOrEmpty(Provider) ? null : Provider);
            set(KeyUpdated, string.IsNullOrEmpty(Updated) ? null : Updated);
            bool swapped = State == TextState.Print;
            set(KeySavedText, swapped ? SavedText : null);
            set(KeySavedHAlign, swapped && SavedHAlign >= 0 ? SavedHAlign.ToString(CultureInfo.InvariantCulture) : null);
            set(KeyAnchorShift, swapped ? AnchorShift : null);
            set(KeySavedWrapped, swapped && SavedWrapped >= 0 ? SavedWrapped.ToString(CultureInfo.InvariantCulture) : null);
            set(KeySavedFormatWidth, swapped && SavedFormatWidth > 0 ? SavedFormatWidth.ToString("R", CultureInfo.InvariantCulture) : null);
        }

        public static void Remove(Action<string, string> set)
        {
            foreach (var k in AllKeys) set(k, null);
        }

        public void Touch()
        {
            Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        public TranslationRecord Clone()
        {
            return (TranslationRecord)MemberwiseClone();
        }

        private static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            T v;
            return !string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out v) ? v : fallback;
        }

        private static double ParseDouble(string s)
        {
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        private static int ParseInt(string s, int fallback)
        {
            int i;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : fallback;
        }

        public static string FormatPoint(double x, double y, double z)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", x, y, z);
        }

        public static bool TryParsePoint(string s, out double x, out double y, out double z)
        {
            x = y = z = 0;
            if (string.IsNullOrEmpty(s)) return false;
            var p = s.Split(',');
            return p.Length == 3 &&
                   double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                   double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                   double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
        }
    }
}
