using System;
using System.Windows.Media;

namespace Open.DataGrid.Rendering
{
    /// <summary>
    /// Utility for computing shifted/adjusted brush colors.
    /// Light colors are darkened, dark colors are lightened — preserving contrast.
    /// </summary>
    internal static class BrushShift
    {
        /// <summary>
        /// Luminance threshold (0–255 scale) used to decide whether a background is "dark" or "light"
        /// when computing auto-contrast foreground colors. Shared with <see cref="CellForegroundResolver"/>.
        /// </summary>
        internal const double ForegroundLuminanceThreshold = 120.0;

        /// <summary>
        /// Returns a new <see cref="SolidColorBrush"/> with each channel shifted by <paramref name="shift"/> (0.0–1.0).
        /// Light base colors are darkened; dark base colors are lightened. Result is frozen.
        /// Returns <paramref name="source"/> unchanged when it is not a <see cref="SolidColorBrush"/>.
        /// </summary>
        public static Brush Apply(Brush source, double shift)
        {
            if (!(source is SolidColorBrush solid)) return source;
            var c = solid.Color;
            double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            byte Adjust(byte ch) => lum > 0.5
                ? (byte)(ch * (1.0 - shift))
                : (byte)(ch + (255 - ch) * shift);
            var result = new SolidColorBrush(Color.FromRgb(Adjust(c.R), Adjust(c.G), Adjust(c.B)));
            result.Freeze();
            return result;
        }

        /// <summary>
        /// Returns a focus-highlight brush derived from <paramref name="selectionBrush"/>.
        /// Light selection colors are darkened slightly; dark ones are lightened. Result is frozen.
        /// </summary>
        public static SolidColorBrush ComputeFocusBrush(Brush selectionBrush)
        {
            var baseColor = (selectionBrush as SolidColorBrush)?.Color ?? System.Windows.SystemColors.HighlightColor;
            double luminance = 0.299 * baseColor.R + 0.587 * baseColor.G + 0.114 * baseColor.B;
            Color adjusted = luminance < 160
                ? Color.FromRgb(Lighten(baseColor.R), Lighten(baseColor.G), Lighten(baseColor.B))
                : Color.FromRgb(Darken(baseColor.R),  Darken(baseColor.G),  Darken(baseColor.B));
            var result = new SolidColorBrush(adjusted);
            result.Freeze();
            return result;
        }

        /// <summary>
        /// Returns a foreground brush derived from <paramref name="background"/> for highlighted cells.
        /// Dark backgrounds (luminance &lt; 160) produce a light foreground; light backgrounds produce a dark one.
        /// Shift magnitude is 80% toward white or black. Result is frozen.
        /// </summary>
        public static SolidColorBrush ComputeForegroundBrush(Brush background)
        {
            var color = (background as SolidColorBrush)?.Color ?? Colors.White;
            double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
            Color adjusted = luminance < ForegroundLuminanceThreshold
                ? Color.FromRgb(Lighten60(color.R), Lighten60(color.G), Lighten60(color.B))
                : Color.FromRgb(Darken60(color.R),  Darken60(color.G),  Darken60(color.B));
            var result = new SolidColorBrush(adjusted);
            result.Freeze();
            return result;
        }

        private static byte Lighten(byte channel)
            => (byte)Math.Min(255, channel + (int)Math.Round((255 - channel) * 0.4));

        private static byte Darken(byte channel)
            => (byte)Math.Max(0, (int)Math.Round(channel * 0.6));

        private static byte Lighten60(byte ch) => (byte)Math.Min(255, ch + (int)Math.Round((255 - ch) * 0.8));
        private static byte Darken60(byte ch)  => (byte)Math.Max(0,   (int)Math.Round(ch * 0.2));
    }
}
