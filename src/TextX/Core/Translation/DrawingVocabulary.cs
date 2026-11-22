using System;
using System.Collections.Generic;
using System.Linq;

namespace TextX.Core.Translation
{
    /// <summary>
    /// The words of drawings and title blocks, Hebrew to English, as a draughtsman would write them.
    /// On a sheet these labels stand alone, without a sentence around them, and a translation service
    /// then guesses ("קנ"מ" becomes "KNM", "עמוד" a "column", "רשימת גמרים" a "list of finalists").
    /// Used after the user's own glossary, which always wins.
    /// </summary>
    public static class DrawingVocabulary
    {
        private static readonly Dictionary<string, string> HeEn = Build(new[]
        {
            // title blocks
            "קנ\"מ", "Scale", "קנה מידה", "Scale", "תאריך", "Date", "מהדורה", "Revision", "רשימת עדכונים", "Revisions",
            "תיאור השינוי", "Description of change", "גודל גליון", "Sheet size", "גודל גיליון", "Sheet size", "גליון", "Sheet",
            "גיליון", "Sheet", "עמוד", "Page", "כמות", "Quantity", "מידות", "Dimensions", "מיקום", "Location", "פרזול", "Hardware",
            "הערות", "Notes", "הערה", "Note", "תיאור", "Description", "שם פריט", "Item name", "שם התכנית", "Drawing title",
            "שם התוכנית", "Drawing title", "שם פרויקט", "Project name", "שם הפרויקט", "Project name", "כתובת הפרויקט", "Project address",
            "כתובת הפרוייקט", "Project address", "מתכננת אחראית", "Designer in charge", "מתכנן אחראי", "Designer in charge",
            "לפי שרטוט", "As per drawing", "לאישור", "For approval", "לתיאום", "For coordination", "למכרז", "For tender",
            "לביצוע", "For construction", "מספר שרטוט", "Drawing number", "שרטט", "Drawn by", "בדק", "Checked by", "אושר", "Approved",
            "אדריכל", "Architect", "מעצבת פנים", "Interior designer", "מעצב פנים", "Interior designer", "לקוח", "Client", "יזם", "Developer",
            // views
            "חתך", "Section", "חתך צד", "Side section", "חתך אורך", "Longitudinal section", "חתך רוחב", "Cross section",
            "חזית", "Elevation", "חזית אחורית", "Rear elevation", "חזית קדמית", "Front elevation", "חזית צד", "Side elevation",
            "מבט על", "Top view", "מבט צד", "Side view", "תכנית", "Plan", "תוכנית", "Plan", "פרט", "Detail", "מקרא", "Legend",
            "מקרא חומרים", "Materials legend", "הדמיה", "Rendering",
            // joinery
            "נגרות", "Joinery", "נגרות מיוחדת", "Custom joinery", "רשימת נגרות", "Joinery schedule", "צוקל", "Plinth",
            "גמר", "Finish", "גמר פנים", "Interior finish", "גמר חוץ", "Exterior finish", "רשימת גמרים", "Finish schedule",
            "לפי רשימת גמרים", "As per finish schedule", "בגמר לפי רשימת גמרים", "Finish as per finish schedule",
            "ידיות", "Handles", "ידית", "Handle", "כיור", "Sink", "ברז", "Faucet", "משטח", "Countertop", "מגירות", "Drawers",
            "מגרות", "Drawers", "מגירה", "Drawer", "מדפים", "Shelves", "מדף", "Shelf", "דלתות", "Doors", "דלת", "Door",
            "ארון", "Cabinet", "ארונות", "Cabinets", "פתח אשפה", "Waste bin opening", "טריקה שקטה", "Soft-close",
            "דלתות טריקה שקטה", "Soft-close doors", "שליפה מלאה", "Full extension", "מגירות שליפה מלאה", "Full-extension drawers",
            "מנגנון בלומושיין", "Blum motion mechanism", "צירים", "Hinges", "ציר", "Hinge", "דופן", "Side panel",
            "גוף היחידה", "Carcass", "אבן קיסר", "Caesarstone", "נירוסטה", "Stainless steel", "ש\"ע", "or equivalent",
            "או ש\"ע", "or equivalent", "דלפק", "Counter", "דלפק מזכירה", "Reception desk", "ס\"מ", "cm", "מ\"מ", "mm",
        });

        /// <summary>Terms of several words, also made consistent inside sentences.</summary>
        private static readonly string[] InSentences =
        {
            "רשימת גמרים", "finish schedule", "טריקה שקטה", "soft-close", "שליפה מלאה", "full extension", "אבן קיסר", "Caesarstone",
            "מנגנון בלומושיין", "Blum motion mechanism", "פתח אשפה", "waste bin opening",
        };

        private static Dictionary<string, string> Build(string[] pairs)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                d[Glossary.Normalize(pairs[i])] = pairs[i + 1];
                // Hebrew gershayim and geresh are often typed as quotes, and the other way round
                var alt = pairs[i].Replace('"', '״');
                if (alt != pairs[i]) d[Glossary.Normalize(alt)] = pairs[i + 1];
            }
            return d;
        }

        private static bool Applies(string sourceLang, string targetLang)
        {
            bool src = string.IsNullOrEmpty(sourceLang) || sourceLang == Languages.AutoDetect || sourceLang.StartsWith("he", StringComparison.OrdinalIgnoreCase) || sourceLang == "iw";
            return src && targetLang != null && targetLang.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The English for a whole label (normalized as the glossary does), or null.</summary>
        public static string Lookup(string normalized, string sourceLang, string targetLang)
        {
            if (!Applies(sourceLang, targetLang) || string.IsNullOrEmpty(normalized)) return null;
            string target;
            return HeEn.TryGetValue(normalized, out target) ? target : null;
        }

        /// <summary>The terms kept consistent inside sentences, as glossary entries.</summary>
        public static IEnumerable<GlossaryEntry> SentenceTerms(string sourceLang, string targetLang)
        {
            if (!Applies(sourceLang, targetLang)) return Enumerable.Empty<GlossaryEntry>();
            var list = new List<GlossaryEntry>();
            for (int i = 0; i + 1 < InSentences.Length; i += 2)
                list.Add(new GlossaryEntry { SourceLang = "he", TargetLang = "en", Source = InSentences[i], Target = InSentences[i + 1] });
            return list;
        }
    }
}
