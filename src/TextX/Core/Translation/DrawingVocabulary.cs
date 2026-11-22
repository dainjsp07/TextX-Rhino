using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
            "כתובת הפרוייקט", "Project address", "מתכננת אחראית", "Designer", "מתכנן אחראי", "Designer",
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
            "סכומון", "Cutlery tray", "מגירת פח", "Bin drawer", "שוט נירוסטה", "Stainless steel waste chute", "שוט", "Waste chute",
            "קוריאן/אבן קיסר", "Corian/Caesarstone", "חזית הארונות", "Cabinet fronts", "פנים הארונות", "Cabinet interiors",
            "קלפה", "Flap door", "קלפות", "Flap doors", "פנימית כפולה", "Double internal drawer", "מגירה פנימית", "Internal drawer",
            "מדף משתנה", "Adjustable shelf", "מדפים משתנים", "Adjustable shelves", "מדפים משתנים בגובה", "Height-adjustable shelves",
            "משתנים בגובה", "Height-adjustable", "חלק קבוע", "Fixed part", "גמר ארון", "Cabinet finish", "גמר פנים הארון", "Cabinet interior finish",
            "גוף המחיצה", "Partition body", "מחיצה", "Partition", "גוף הספסל", "Bench body", "גוף האדנית", "Planter body",
            "כיור אינטגראלי/קנוי", "Integral/purchased sink", "כיור אינטגרלי/קנוי", "Integral/purchased sink",
            "כיור אינטגראלי מקוריאן/קנוי", "Integral Corian sink or purchased sink", "ש\"ע לבחירת המעצבת", "Or equivalent, to the designer's choice",
            "חיבורים בגרונג", "Mitred joints", "חיבור בגרונג", "Mitre joint", "פרופיל ניתוק", "Shadow-gap profile",
            "פרופיל ניתוק ע\"פ פרט", "Shadow-gap profile as per detail", "ע\"פ מקרא", "As per legend", "לפי מקרא", "As per legend",
            "ידיות ע\"פ מקרא", "Handles as per legend", "בגוון ע\"פ מקרא", "Shade as per legend", "בגמר וגוון ע\"פ מקרא", "Finish and shade as per legend",
            "מקטע קונזולי לבדיקת הנגר", "Cantilever section, to be checked by the carpenter", "יש לוודא מידה בשטח", "Verify dimension on site",
            "סינר גבס", "Plasterboard bulkhead", "סינר גבס מתעגל", "Curved plasterboard bulkhead", "חיפוי גבס", "Plasterboard cladding",
            "מחזיר שמן", "Door closer", "דלת מנוגרת", "Joinery-clad door", "דלת הדף", "Blast door", "ציר דלת נסתר", "Concealed door hinge",
            "חוץ", "Outside", "פנים", "Inside", "תמונה להמחשה בלבד", "Image for illustration only",
            // kitchens
            "מיקרוגל אינטגרלי", "Built-in microwave", "מיקרוגל אינטגראלי", "Built-in microwave", "מדיח אינטגרלי", "Integrated dishwasher",
            "מדיח אינטגראלי", "Integrated dishwasher", "מקרר אינטגרלי", "Integrated fridge", "תנור אינטגרלי", "Built-in oven", "מדיח", "Dishwasher",
            "מקרר", "Fridge", "מיקרוגל", "Microwave", "תנור", "Oven", "כיריים", "Hob", "קולט אדים", "Cooker hood", "מכונת קפה", "Coffee machine",
            "מ.קפה", "Coffee machine", "מכונת קפה ידנית", "Manual coffee machine", "מ.קפה ידנית", "Manual coffee machine", "מ.קפה ידני", "Manual coffee machine",
            "מכונת קרח", "Ice machine", "מ.קרח", "Ice machine", "תמי4", "Tami4", "תמי 4", "Tami4", "תמי בר", "Tami bar", "ברז סודה", "Soda tap",
            "אי קפיטריה", "Cafeteria island", "אי מטבח", "Kitchen island", "מגירת אשפה", "Bin drawer", "אשפה", "Waste", "עמדת הדפסה", "Printing station",
            "קבלה", "Reception", "ציבורי", "Public", "מדרגות", "Stairs", "מטבחון", "Kitchenette", "מטבח", "Kitchen",
        });

        /// <summary>Labels with a number or a reference in them ("קנ"מ 1:25", "חזית A", "תוכנית 02.1 - 21 NM").</summary>
        private static readonly KeyValuePair<Regex, string>[] Patterns =
        {
            P("^(?<pre>\\d+\\s+)?קנ\\s*[\"״]\\s*מ\\s*(?<r>\\d+\\s*:\\s*\\d+)$", "${pre}Scale ${r}"),
            P("^(?<pre>\\d+\\s+)?(?:חזית)\\s*(?<rest>[A-Za-z0-9][^\\u0590-\\u05FF]*)$", "${pre}Elevation ${rest}"),
            P("^(?<pre>\\d+\\s+)?(?:תוכנית|תכנית)(?<rest>\\s+[^\\u0590-\\u05FF]*)?$", "${pre}Plan${rest}"),
            P("^(?<pre>\\d+\\s+)?(?:חתך)\\s*(?<rest>[A-Za-z0-9][^\\u0590-\\u05FF]*)$", "${pre}Section ${rest}"),
            P("^(?<pre>\\d+\\s+)?(?:פרט)\\s*(?<rest>[A-Za-z0-9][^\\u0590-\\u05FF]*)$", "${pre}Detail ${rest}"),
            P("^צוקל\\s+פנימי\\s*(?<n>\\d+)\\s*ס\\s*[\"״]?\\s*מ$", "Inner plinth ${n} cm"),
        };

        /// <summary>A drawing word followed by its reference: "מבט על JRY-01", "חזית אחורית B", "צוקל 65".</summary>
        private static readonly Regex WordAndReference = new Regex("^(?<he>[\\u05D0-\\u05EA][\\u05D0-\\u05EA\"'\\u05F3\\u05F4 ]*?)\\s*(?<rest>[A-Za-z0-9][^\\u0590-\\u05FF]*)$", RegexOptions.CultureInvariant);

        private static KeyValuePair<Regex, string> P(string pattern, string replacement)
        {
            return new KeyValuePair<Regex, string>(new Regex(pattern, RegexOptions.CultureInvariant), replacement);
        }

        /// <summary>
        /// The same label however it was typed: spaces collapsed, none around dots, slashes and hyphens
        /// ("מ .קפה" and "מ.קפה"), gershayim as a quote.
        /// </summary>
        private static string Compact(string s)
        {
            s = Regex.Replace(s ?? string.Empty, "\\s+", " ").Trim().Replace('״', '"').Replace('׳', '\'');
            return Regex.Replace(s, "\\s*([./\\-])\\s*", "$1");
        }

        /// <summary>Terms of several words, also made consistent inside sentences.</summary>
        private static readonly string[] InSentences =
        {
            "רשימת גמרים", "finish schedule", "טריקה שקטה", "soft-close", "שליפה מלאה", "full extension", "אבן קיסר", "Caesarstone",
            "מנגנון בלומושיין", "Blum motion mechanism", "פתח אשפה", "waste bin opening", "סכומון", "cutlery tray", "מגירת פח", "bin drawer",
            "מבט על", "Top view", "חזית אחורית", "Rear elevation", "חזית קדמית", "Front elevation", "חזית צד", "Side elevation",
            "חתך צד", "Side section", "בלום", "Blum", "חיבורים בגרונג", "mitred joints", "גרונג", "mitre", "מקרא חומרים", "materials legend",
            "מקרא", "legend", "של חב'", "made by", "של חברת", "made by",
            "צוקל", "plinth", "קלפה", "flap door", "מדפים משתנים בגובה", "height-adjustable shelves", "דלת מנוגרת", "joinery-clad door",
            "דלת הדף", "blast door", "מחזיר שמן", "door closer", "פרופיל ניתוק", "shadow-gap profile", "יילקחו בשטח", "taken on site",
            "צבע בתנור", "baked paint",
        };

        private static Dictionary<string, string> Build(string[] pairs)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            // (gershayim and geresh are often typed as quotes, and the other way round: Compact makes them one)
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                d[Compact(Glossary.Normalize(pairs[i]))] = pairs[i + 1];
            return d;
        }

        private static bool Applies(string sourceLang, string targetLang)
        {
            bool src = string.IsNullOrEmpty(sourceLang) || sourceLang == Languages.AutoDetect || sourceLang.StartsWith("he", StringComparison.OrdinalIgnoreCase) || sourceLang == "iw";
            return src && targetLang != null && targetLang.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The English for a whole label (normalized as the glossary does), or null.</summary>
        public static string Lookup(string normalized, string sourceLang, string targetLang, string original = null)
        {
            if (!Applies(sourceLang, targetLang) || string.IsNullOrEmpty(normalized)) return null;
            var key = Compact(normalized);
            string target;
            if (HeEn.TryGetValue(key, out target)) return target;
            // (patterns keep the letters' case: "NM", "A")
            var spaced = Regex.Replace((original ?? normalized).Trim(), "\\s+", " ").Replace('״', '"').TrimEnd(':', '.', ' ');
            foreach (var p in Patterns)
            {
                var m = p.Key.Match(spaced);
                if (m.Success) return m.Result(p.Value).Trim();
            }
            var wr = WordAndReference.Match(spaced);
            string head;
            if (wr.Success && HeEn.TryGetValue(Compact(Glossary.Normalize(wr.Groups["he"].Value)), out head))
                return head + " " + wr.Groups["rest"].Value.Trim();
            return null;
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
