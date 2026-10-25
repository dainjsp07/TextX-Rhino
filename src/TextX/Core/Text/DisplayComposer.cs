using System;

namespace TextX.Core.Text
{
    /// <summary>
    /// Produces the exact string written into a Rhino annotation for a logical text.
    /// </summary>
    public static class DisplayComposer
    {
        /// <param name="logical">Text in logical (typing) order.</param>
        /// <param name="language">Language code of the text (decides the base direction).</param>
        /// <param name="mode">Visual (default for Rhino 8) or Logical.</param>
        /// <param name="options">RTL options (direction is overridden from the language).</param>
        /// <param name="wrapWidth">Manual wrap width in model units (0 = no wrapping).</param>
        /// <param name="measure">Measures the rendered width of a logical single-line string.</param>
        public static string Compose(string logical, string language, RtlRenderMode mode, RtlOptions options,
                                     double wrapWidth = 0, Func<string, double> measure = null)
        {
            var text = RtlText.NormalizeNewlines(logical ?? string.Empty);
            if (!RtlText.ContainsRtl(text)) return text;
            if (wrapWidth > 0 && measure != null) text = RtlText.WrapText(text, wrapWidth, measure);
            if (mode == RtlRenderMode.Logical) return text;
            var o = (options ?? new RtlOptions()).Clone();
            o.Direction = Core.Languages.IsRtl(language) ? BaseDirection.Rtl : BaseDirection.Auto;
            return RtlText.ToVisual(text, o);
        }

        /// <summary>True when the composed string differs from the logical text (i.e. needs RTL handling).</summary>
        public static bool NeedsRtlHandling(string logical)
        {
            return RtlText.ContainsRtl(logical);
        }
    }
}
