using System;
using System.Collections;
using System.Reflection;
using Eto.Drawing;
using Eto.Forms;
using TextX.Integration;
using TextX.UI.Theming;

namespace TextX.UI.Controls
{
    /// <summary>
    /// Colours for native grids, which Eto cannot theme: on Rhino's WPF platform the data grid, its
    /// header and its lines get the palette through the WPF objects (found by reflection, so nothing
    /// changes on other platforms).
    /// </summary>
    public static class NativeTheme
    {
        private static readonly Type BrushType = Type.GetType("System.Windows.Media.SolidColorBrush, PresentationCore");
        private static readonly Type MediaColor = Type.GetType("System.Windows.Media.Color, PresentationCore");
        private static readonly Type StyleType = Type.GetType("System.Windows.Style, PresentationFramework");
        private static readonly Type SetterType = Type.GetType("System.Windows.Setter, PresentationFramework");
        private static readonly Type ControlType = Type.GetType("System.Windows.Controls.Control, PresentationFramework");
        private static readonly Type HeaderType = Type.GetType("System.Windows.Controls.Primitives.DataGridColumnHeader, PresentationFramework");
        private static readonly Type ThicknessType = Type.GetType("System.Windows.Thickness, PresentationFramework");
        private static readonly Type FontWeights = Type.GetType("System.Windows.FontWeights, PresentationCore");

        public static void StyleGrid(GridView grid, ThemeColors p)
        {
            if (grid == null || BrushType == null || MediaColor == null) return;
            try
            {
                var native = grid.ControlObject;
                if (native == null || native.GetType().GetProperty("RowBackground") == null) return;
                Set(native, "Background", Brush(p.Surface));
                Set(native, "RowBackground", Brush(p.Surface));
                Set(native, "AlternatingRowBackground", Brush(ThemeColors.Mix(p.Surface, p.Window, p.IsDark ? 0.45f : 0.55f)));
                Set(native, "Foreground", Brush(p.Text));
                Set(native, "BorderBrush", Brush(p.Surface));
                Set(native, "HorizontalGridLinesBrush", Brush(p.Divider));
                Set(native, "VerticalGridLinesBrush", Brush(p.Divider));
                if (StyleType == null || SetterType == null || ControlType == null || HeaderType == null) return;
                var style = Activator.CreateInstance(StyleType, HeaderType);
                var setters = StyleType.GetProperty("Setters").GetValue(style, null) as IList;
                if (setters == null) return;
                Action<string, object> add = (name, value) =>
                {
                    var dp = ControlType.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    if (dp == null || value == null) return;
                    setters.Add(Activator.CreateInstance(SetterType, dp.GetValue(null), value));
                };
                add("BackgroundProperty", Brush(p.Surface));
                add("ForegroundProperty", Brush(p.Muted));
                add("BorderBrushProperty", Brush(p.Border));
                if (ThicknessType != null)
                {
                    add("BorderThicknessProperty", Activator.CreateInstance(ThicknessType, 0.0, 0.0, 1.0, 1.0));
                    add("PaddingProperty", Activator.CreateInstance(ThicknessType, 8.0, 7.0, 8.0, 7.0));
                }
                if (FontWeights != null)
                {
                    var semi = FontWeights.GetProperty("SemiBold", BindingFlags.Public | BindingFlags.Static);
                    if (semi != null) add("FontWeightProperty", semi.GetValue(null, null));
                }
                Set(native, "ColumnHeaderStyle", style);
            }
            catch (Exception ex)
            {
                Diagnostics.Log("grid theme: " + ex.Message);
            }
        }

        private static object Brush(Color c)
        {
            var fromArgb = MediaColor.GetMethod("FromArgb", new[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte) });
            var color = fromArgb.Invoke(null, new object[] { (byte)c.Ab, (byte)c.Rb, (byte)c.Gb, (byte)c.Bb });
            var brush = Activator.CreateInstance(BrushType, color);
            var freeze = BrushType.GetMethod("Freeze", Type.EmptyTypes);
            if (freeze != null) freeze.Invoke(brush, null);
            return brush;
        }

        private static void Set(object target, string property, object value)
        {
            var pi = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null && pi.CanWrite) pi.SetValue(target, value, null);
        }
    }
}
