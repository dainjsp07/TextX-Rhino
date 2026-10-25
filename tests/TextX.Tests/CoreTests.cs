using TextX.Core.Text;
using static TextX.Tests.TestRunner;

namespace TextX.Tests
{
    public static partial class CoreTests
    {
        private static readonly RtlOptions Rtl = new RtlOptions { Direction = BaseDirection.Rtl };

        public static void HebrewVisualOrder()
        {
            // Pure Hebrew: characters reversed for left-to-right drawing.
            Equal("םולש", RtlText.ToVisual("שלום", Rtl), "hebrew word");
            Equal("םלוע םולש", RtlText.ToVisual("שלום עולם", Rtl), "hebrew two words");
            // Final letters are distinct code points; they must stay attached to word end (now on the left).
            Equal("ןולח", RtlText.ToVisual("חלון", Rtl), "final nun");
            // English text is never touched.
            Equal("Ground Floor Plan", RtlText.ToVisual("Ground Floor Plan", Rtl), "ltr untouched");
            // Multi-line: each line reordered independently.
            Equal("תינכות\nהמוק", RtlText.ToVisual("תוכנית\r\nקומה", Rtl), "multi line");
        }

        public static void MixedContent()
        {
            // Numbers keep their internal order and sit on the left end of an RTL line.
            Equal("1:50 הדימ הנק", RtlText.ToVisual("קנה מידה 1:50", Rtl), "scale with ratio");
            // Embedded Latin run keeps LTR order.
            Equal("A-101 ןויליג", RtlText.ToVisual("גיליון A-101", Rtl), "latin code");
            // Punctuation at end of RTL sentence appears at the far left.
            Equal(".הנבמ", RtlText.ToVisual("מבנה.", Rtl), "trailing period");
            // Brackets are mirrored inside RTL runs.
            Equal("(ינופצ) תיזח", RtlText.ToVisual("חזית (צפוני)", Rtl), "mirrored brackets");
            // Number followed by a Latin unit: standard UBA (same as browsers / Word) puts the unit
            // run to the left of the number in an RTL line.
            Equal("cm 120 הבוג", RtlText.ToVisual("גובה 120 cm", Rtl), "units");
            // Hebrew units keep the natural order.
            Equal("מ\"ס 120 הבוג", RtlText.ToVisual("גובה 120 ס\"מ", Rtl), "hebrew units");
            // Percent and decimals.
            Equal("2.5% עופיש", RtlText.ToVisual("שיפוע 2.5%", Rtl), "percent");
            // Signed level values keep the sign in front of the number (KeepSignWithNumber).
            Equal("-0.45 סלפמ", RtlText.ToVisual("מפלס -0.45", Rtl), "negative number");
            Equal("+3.20 סלפמ", RtlText.ToVisual("מפלס +3.20", Rtl), "positive number");
            Equal("±0.00 סלפמ", RtlText.ToVisual("מפלס ±0.00", Rtl), "plus-minus");
            Equal("1-5 םירדח", RtlText.ToVisual("חדרים 1-5", Rtl), "range untouched");
            var strict = new RtlOptions { Direction = BaseDirection.Rtl, KeepSignWithNumber = false };
            Equal("0.45- סלפמ", RtlText.ToVisual("מפלס -0.45", strict), "strict uba sign");
            // LTR paragraph containing a Hebrew word.
            Equal("Room 12 םולש", RtlText.ToVisual("Room שלום 12", new RtlOptions { Direction = BaseDirection.Auto }), "ltr paragraph with hebrew");
            // Visual -> logical round trip for typical lines
            Equal("קנה מידה 1:50", RtlText.VisualToLogical("1:50 הדימ הנק"), "visual to logical");
        }

        public static void FieldsAndMarks()
        {
            // Rhino text fields are kept intact and treated as a unit.
            Equal("%<PageNumber()>% :ןויליג", RtlText.ToVisual("גיליון: %<PageNumber()>%", Rtl), "field protected");
            Equal("%<Date(\"dd/MM/yyyy\")>% :ךיראת", RtlText.ToVisual("תאריך: %<Date(\"dd/MM/yyyy\")>%", Rtl), "field with quotes");
            // Hebrew points stay after their base letter: bet + dagesh, shin + shin-dot + qamats
            Equal("םולשָׁ", RtlText.ToVisual("שָׁלום", Rtl), "niqqud order");
            // Bidi control marks are removed from output
            Equal("םולש", RtlText.ToVisual("‏שלום‏", Rtl), "rlm stripped");
        }
    }
}
