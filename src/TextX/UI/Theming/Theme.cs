using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;

namespace TextX.UI.Theming
{
    /// <summary>The active palette and fonts. "Auto" follows Rhino's light / dark mode.</summary>
    public static class Theme
    {
        private static ThemeColors _current;
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();

        public static ThemeColors Current
        {
            get
            {
                if (_current == null) Refresh();
                return _current;
            }
        }

        /// <summary>Re-reads the theme setting. Returns true when the palette changed.</summary>
        public static bool Refresh()
        {
            string mode = "Auto";
            try { mode = TextXPlugIn.Instance.Options.ThemeMode; }
            catch { /* settings not available (designer / tests) */ }
            bool dark;
            if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase)) dark = true;
            else if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase)) dark = false;
            else
            {
                try { dark = Rhino.Runtime.HostUtils.RunningInDarkMode; }
                catch { dark = true; }
            }
            var changed = _current == null || _current.IsDark != dark;
            if (changed) _current = dark ? ThemeColors.GraphiteDeepNavy() : ThemeColors.LightDeepNavy();
            return changed;
        }

        /// <summary>UI font; size in design pixels (1 px = 0.75 pt).</summary>
        public static Font UiFont(float px, bool bold = false)
        {
            return GetFont("Segoe UI", px, bold);
        }

        public static Font GetFont(string family, float px, bool bold = false, bool italic = false)
        {
            var key = family + "|" + px + "|" + bold + "|" + italic;
            Font f;
            if (Fonts.TryGetValue(key, out f)) return f;
            var style = (bold ? FontStyle.Bold : FontStyle.None) | (italic ? FontStyle.Italic : FontStyle.None);
            try
            {
                f = new Font(string.IsNullOrEmpty(family) ? "Segoe UI" : family, px * 0.75f, style);
            }
            catch
            {
                f = SystemFonts.Default(px * 0.75f);
            }
            Fonts[key] = f;
            return f;
        }
    }

    /// <summary>
    /// Remembers how colours are assigned to native controls so they can be re-applied when the
    /// theme changes (custom-drawn controls read the palette when they paint).
    /// </summary>
    public sealed class ThemeScope
    {
        private readonly List<Action<ThemeColors>> _actions = new List<Action<ThemeColors>>();

        public T Bind<T>(T control, Action<T, ThemeColors> apply)
        {
            Action<ThemeColors> a = p => apply(control, p);
            _actions.Add(a);
            a(Theme.Current);
            return control;
        }

        public void ApplyAll(Container root)
        {
            var p = Theme.Current;
            foreach (var a in _actions)
            {
                try { a(p); }
                catch { /* control disposed */ }
            }
            if (root == null) return;
            foreach (var c in root.Children)
            {
                var d = c as Drawable;
                if (d != null) d.Invalidate();
            }
        }
    }
}
