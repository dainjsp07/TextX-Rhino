using System;
using Eto.Drawing;
using Eto.Forms;
using TextX.UI.Theming;

namespace TextX.UI.Controls
{
    /// <summary>Factory helpers that create native controls already bound to the theme.</summary>
    public static class Ui
    {
        public static Label Label(ThemeScope scope, string text, float px = 13f, bool bold = false, Func<ThemeColors, Color> color = null, bool wrap = false)
        {
            var l = new Label
            {
                Text = text ?? string.Empty,
                Font = Theme.UiFont(px, bold),
                Wrap = wrap ? WrapMode.Word : WrapMode.None,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var c = color ?? (p => p.Text);
            return scope.Bind(l, (x, p) => x.TextColor = c(p));
        }

        /// <summary>Small upper-case section heading ("OBJECTS", "DISPLAY").</summary>
        public static Label Section(ThemeScope scope, string text)
        {
            return Label(scope, (text ?? string.Empty).ToUpperInvariant(), 11f, true, p => p.Muted);
        }

        public static Panel HLine(ThemeScope scope)
        {
            return scope.Bind(new Panel { Height = 1 }, (x, p) => x.BackgroundColor = p.Divider);
        }

        public static Panel VLine(ThemeScope scope)
        {
            return scope.Bind(new Panel { Width = 1 }, (x, p) => x.BackgroundColor = p.Divider);
        }

        /// <summary>A multi-line text box without its native border (use inside <see cref="FieldBox"/>).</summary>
        public static TextArea TextArea(ThemeScope scope, float px)
        {
            var t = new TextArea
            {
                AcceptsReturn = true,
                AcceptsTab = false,
                Wrap = true,
                SpellCheck = false,
                Border = BorderType.None,
                Font = Theme.UiFont(px),
            };
            return scope.Bind(t, (x, p) =>
            {
                x.BackgroundColor = p.Field;
                x.TextColor = p.Text;
            });
        }

        /// <summary>Rounded frame around a text box.</summary>
        public static Card FieldBox(Control content)
        {
            return new Card(content, new Padding(10, 7), p => p.Field, p => p.Border, 8f) { Backdrop = p => p.Surface };
        }

        /// <summary>A surface card (rounded, outlined) on the window background.</summary>
        public static Card SurfaceCard(Control content, Padding padding)
        {
            return new Card(content, padding, p => p.Surface, p => p.Border, 10f);
        }

        /// <summary>Applies theme text colours to native labels and check boxes inside a page.</summary>
        public static void ThemeNativeChildren(ThemeScope scope, Container root)
        {
            foreach (var c in root.Children)
            {
                var label = c as Eto.Forms.Label;
                if (label != null) { scope.Bind(label, (x, p) => x.TextColor = p.Text); continue; }
                var check = c as CheckBox;
                if (check != null) { scope.Bind(check, (x, p) => x.TextColor = p.Text); continue; }
                var radios = c as RadioButtonList;
                if (radios != null) { scope.Bind(radios, (x, p) => x.TextColor = p.Text); continue; }
            }
        }

        public static StackLayout Row(int spacing, params StackLayoutItem[] items)
        {
            var s = new StackLayout { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var i in items) s.Items.Add(i);
            return s;
        }

        /// <summary>A flexible gap in a <see cref="Row"/>.</summary>
        public static StackLayoutItem Spring()
        {
            return new StackLayoutItem(null, true);
        }
    }
}
