using System;
using Eto.Drawing;

namespace TextX.UI.Theming
{
    /// <summary>The colours of one theme. All window colours come from here.</summary>
    public sealed class ThemeColors
    {
        public string Name;
        public bool IsDark;

        public Color Window;      // window background
        public Color Surface;     // cards
        public Color Field;       // text boxes
        public Color Border;      // card and control outlines
        public Color Divider;     // separator lines
        public Color Footer;      // bottom bar
        public Color Item;        // list rows
        public Color ItemHover;
        public Color IconBox;     // small square behind list icons
        public Color Text;
        public Color Muted;       // secondary text, inactive tabs
        public Color Subtle;      // text on tinted backgrounds

        public Color Accent;      // fill of the main button (signal orange)
        public Color OnAccent;    // text on Accent
        public Color AccentInk;   // accent used for text, icons, outlines (readable on Window)
        public Color AccentSoft;  // selected rows / options background
        public Color AccentLine;  // outline of selected rows
        public Color AccentEdge;  // outline of the main button

        public Color Strong;      // filled chips and active segments (ink on paper, paper on charcoal)
        public Color OnStrong;    // text on Strong
        public Color Rail;        // navigation rail
        public Color RailIcon;    // inactive rail icons
        public Color Stage;       // behind document pages
        public Color Hero;        // the large feature card
        public Color OnHero;      // text on Hero

        public Color SheetBg;     // sheet preview paper
        public Color SheetText;
        public Color SheetLabel;
        public Color SheetDash;

        public Color Success;
        public Color SuccessSoft;
        public Color SuccessText;
        public Color Warning;
        public Color WarningSoft;
        public Color Error;
        public Color ErrorSoft;

        public static Color Hex(string hex, float alpha = 1f)
        {
            var h = hex.TrimStart('#');
            int r = Convert.ToInt32(h.Substring(0, 2), 16);
            int g = Convert.ToInt32(h.Substring(2, 2), 16);
            int b = Convert.ToInt32(h.Substring(4, 2), 16);
            return new Color(r / 255f, g / 255f, b / 255f, alpha);
        }

        /// <summary>Opaque mix of two colours: t = 0 gives a, t = 1 gives b.</summary>
        public static Color Mix(Color a, Color b, float t)
        {
            return new Color(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, 1f);
        }

        public static Color WithAlpha(Color c, float alpha)
        {
            return new Color(c.R, c.G, c.B, alpha);
        }

        /// <summary>
        /// Dark theme: charcoal surfaces, warm paper text and one strong accent (signal orange),
        /// after the reference palette #FF6A00 #111111 #333333 #777777 #C9BCA7 #E8E4DB #F6F4E9.
        /// </summary>
        public static ThemeColors Charcoal()
        {
            var p = new ThemeColors { Name = "Dark", IsDark = true };
            p.Window = Hex("#111111");
            p.Surface = Hex("#1B1B1B");
            p.Field = Hex("#161616");
            p.Border = Hex("#2C2C2C");
            p.Divider = Hex("#242424");
            p.Footer = Hex("#1B1B1B");
            p.Item = Hex("#1B1B1B");
            p.ItemHover = Hex("#252525");
            p.IconBox = Hex("#2A2A2A");
            p.Text = Hex("#F6F4E9");
            p.Muted = Hex("#A19C92");
            p.Subtle = Hex("#C9BCA7");

            p.Accent = Hex("#FF6A00");
            p.OnAccent = Hex("#111111");
            p.AccentInk = Hex("#FF7F24");
            p.AccentSoft = Mix(p.Surface, p.Accent, 0.16f);
            p.AccentLine = WithAlpha(p.Accent, 0.55f);
            p.AccentEdge = Hex("#FF6A00");

            p.Strong = Hex("#F6F4E9");
            p.OnStrong = Hex("#111111");
            p.Rail = Hex("#1B1B1B");
            p.RailIcon = Hex("#8E897F");
            p.Stage = Hex("#161616");
            p.Hero = Hex("#1B1B1B");
            p.OnHero = Hex("#F6F4E9");

            p.SheetBg = Hex("#161616");
            p.SheetText = Hex("#F6F4E9");
            p.SheetLabel = Hex("#C9BCA7");
            p.SheetDash = WithAlpha(p.Accent, 0.55f);

            p.Success = Hex("#4ADE80");
            p.SuccessSoft = Hex("#1F2E24");
            p.SuccessText = Hex("#86EFAC");
            p.Warning = Hex("#FACC15");
            p.WarningSoft = Hex("#2F2A16");
            p.Error = Hex("#F87171");
            p.ErrorSoft = Hex("#352022");
            return p;
        }

        /// <summary>Light theme: warm paper background, white cards, charcoal ink and the same orange.</summary>
        public static ThemeColors Paper()
        {
            var p = new ThemeColors { Name = "Light", IsDark = false };
            p.Window = Hex("#F6F4E9");
            p.Surface = Hex("#FFFFFF");
            p.Field = Hex("#FBFAF5");
            p.Border = Hex("#E8E4DB");
            p.Divider = Hex("#E8E4DB");
            p.Footer = Hex("#FFFFFF");
            p.Item = Hex("#FFFFFF");
            p.ItemHover = Hex("#F4F1E8");
            p.IconBox = Hex("#F0ECE2");
            p.Text = Hex("#111111");
            p.Muted = Hex("#6B665C");
            p.Subtle = Hex("#333333");

            p.Accent = Hex("#FF6A00");
            p.OnAccent = Hex("#111111");
            p.AccentInk = Hex("#C2410C");
            p.AccentSoft = Hex("#FFEBDC");
            p.AccentLine = WithAlpha(Hex("#FF6A00"), 0.6f);
            p.AccentEdge = Hex("#F26300");

            p.Strong = Hex("#111111");
            p.OnStrong = Hex("#F6F4E9");
            p.Rail = Hex("#111111");
            p.RailIcon = Hex("#C9BCA7");
            p.Stage = Hex("#E8E4DB");
            p.Hero = Hex("#111111");
            p.OnHero = Hex("#F6F4E9");

            p.SheetBg = Hex("#F6F4E9");
            p.SheetText = Hex("#111111");
            p.SheetLabel = Hex("#6B665C");
            p.SheetDash = WithAlpha(Hex("#FF6A00"), 0.6f);

            p.Success = Hex("#15803D");
            p.SuccessSoft = Hex("#E3F4E8");
            p.SuccessText = Hex("#166534");
            p.Warning = Hex("#A16207");
            p.WarningSoft = Hex("#FBF1D3");
            p.Error = Hex("#DC2626");
            p.ErrorSoft = Hex("#FDE4E4");
            return p;
        }
    }
}
