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

        public Color Accent;      // fill of the main button (deep navy)
        public Color OnAccent;    // text on Accent
        public Color AccentInk;   // accent used for text, icons, outlines (readable on Window)
        public Color AccentSoft;  // selected rows / options background
        public Color AccentLine;  // outline of selected rows
        public Color AccentEdge;  // outline of the main button

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

        /// <summary>Option A, "Graphite and Deep Navy".</summary>
        public static ThemeColors GraphiteDeepNavy()
        {
            var p = new ThemeColors { Name = "Dark", IsDark = true };
            p.Window = Hex("#1E1F22");
            p.Surface = Hex("#2A2B2F");
            p.Field = Hex("#232428");
            p.Border = Hex("#3A3C41");
            p.Divider = Hex("#2E3034");
            p.Footer = Hex("#1A1B1E");
            p.Item = Hex("#26272B");
            p.ItemHover = Hex("#2D2E33");
            p.IconBox = Hex("#34363B");
            p.Text = Hex("#ECECEE");
            p.Muted = Hex("#A3A6AD");
            p.Subtle = Hex("#C9CBD1");

            p.Accent = Hex("#132D52");
            p.OnAccent = Hex("#FFFFFF");
            p.AccentInk = Hex("#9EC0F0");
            p.AccentSoft = Mix(p.Item, p.Accent, 0.65f);
            p.AccentLine = WithAlpha(p.AccentInk, 0.35f);
            p.AccentEdge = WithAlpha(p.AccentInk, 0.45f);

            p.SheetBg = Mix(Hex("#121316"), p.Accent, 0.25f);
            p.SheetText = Hex("#FFFFFF");
            p.SheetLabel = Hex("#C9CBD1");
            p.SheetDash = WithAlpha(p.AccentInk, 0.45f);

            p.Success = Hex("#4ADE80");
            p.SuccessSoft = Hex("#23382B");
            p.SuccessText = Hex("#86EFAC");
            p.Warning = Hex("#FBBF24");
            p.WarningSoft = Hex("#3A3120");
            p.Error = Hex("#F87171");
            p.ErrorSoft = Hex("#3B2224");
            return p;
        }

        /// <summary>The same design for Rhino's light mode.</summary>
        public static ThemeColors LightDeepNavy()
        {
            var p = new ThemeColors { Name = "Light", IsDark = false };
            p.Window = Hex("#F3F4F6");
            p.Surface = Hex("#FFFFFF");
            p.Field = Hex("#F7F8FA");
            p.Border = Hex("#D5D9E0");
            p.Divider = Hex("#E1E4E9");
            p.Footer = Hex("#EBEDF1");
            p.Item = Hex("#FFFFFF");
            p.ItemHover = Hex("#F0F2F6");
            p.IconBox = Hex("#E7EAF0");
            p.Text = Hex("#1A1B1E");
            p.Muted = Hex("#5B606B");
            p.Subtle = Hex("#3D424C");

            p.Accent = Hex("#132D52");
            p.OnAccent = Hex("#FFFFFF");
            p.AccentInk = Hex("#1E3A8A");
            p.AccentSoft = Hex("#E3EAF5");
            p.AccentLine = WithAlpha(p.AccentInk, 0.40f);
            p.AccentEdge = Hex("#132D52");

            p.SheetBg = Hex("#EEF2F8");
            p.SheetText = Hex("#10213D");
            p.SheetLabel = Hex("#4B5563");
            p.SheetDash = WithAlpha(p.AccentInk, 0.45f);

            p.Success = Hex("#16A34A");
            p.SuccessSoft = Hex("#DCFCE7");
            p.SuccessText = Hex("#166534");
            p.Warning = Hex("#B45309");
            p.WarningSoft = Hex("#FEF3C7");
            p.Error = Hex("#DC2626");
            p.ErrorSoft = Hex("#FEE2E2");
            return p;
        }
    }
}
