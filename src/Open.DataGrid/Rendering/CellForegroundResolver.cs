using System.Windows.Media;

namespace Open.DataGrid.Rendering
{
    /// <summary>
    /// Pure function for resolving the final cell foreground brush.
    ///
    /// Priority hierarchy:
    ///   Highlighted (selected or focused): auto-contrast foreground derived from the resolved background
    ///     — luminance &lt; <see cref="BrushShift.ForegroundLuminanceThreshold"/> → lighten 80% (light text on dark bg)
    ///     — luminance ≥ <see cref="BrushShift.ForegroundLuminanceThreshold"/> → darken 80% (dark text on light bg)
    ///   Normal cell:
    ///     1. Column.Foreground — always used if set (explicit user choice)
    ///     2. Grid.Foreground with contrast check — if grid foreground and row background are on the
    ///        same side of <see cref="BrushShift.ForegroundLuminanceThreshold"/> (both light or both dark),
    ///        auto-contrast is applied instead to keep text legible on rows with custom backgrounds (e.g. light rows in dark mode)
    ///     3. null — caller should ClearValue(ForegroundProperty) to restore WPF default
    /// </summary>
    internal static class CellForegroundResolver
    {
        /// <summary>
        /// Returns the foreground brush for a cell.
        /// Returns null when no foreground is configured and the cell is not highlighted
        /// (caller should ClearValue(ForegroundProperty) to restore WPF default).
        /// </summary>
        public static Brush Resolve(
            Brush  resolvedBackground,
            bool   isHighlighted,
            Brush  columnForeground,
            Brush  gridForeground)
        {
            if (isHighlighted)
                return BrushShift.ComputeForegroundBrush(resolvedBackground);

            // Column-level foreground always wins (explicit user choice).
            if (columnForeground != null)
                return columnForeground;

            // Auto-contrast when the grid foreground would be illegible against the resolved background.
            // Handles dark-mode grids where some rows have light backgrounds
            // (RowColor binding, RowBackgroundSelector).
            if (gridForeground is SolidColorBrush gridSolid &&
                resolvedBackground is SolidColorBrush bgSolid)
            {
                double bgLum = 0.299 * bgSolid.Color.R  + 0.587 * bgSolid.Color.G  + 0.114 * bgSolid.Color.B;
                double fgLum = 0.299 * gridSolid.Color.R + 0.587 * gridSolid.Color.G + 0.114 * gridSolid.Color.B;
                // Same "side" of the luminance spectrum → poor contrast → auto-contrast instead.
                if ((bgLum > BrushShift.ForegroundLuminanceThreshold) == (fgLum > BrushShift.ForegroundLuminanceThreshold))
                    return BrushShift.ComputeForegroundBrush(resolvedBackground);
            }

            return gridForeground; // null → ClearValue → WPF inherits default
        }
    }
}
