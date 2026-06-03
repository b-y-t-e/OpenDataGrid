using System.Windows.Media;

namespace Open.DataGrid.Rendering
{
    /// <summary>
    /// Pure function for resolving the final cell foreground brush.
    ///
    /// Priority hierarchy:
    ///   Highlighted (selected or focused): auto-contrast foreground derived from the resolved background
    ///     — luminance &lt; 160 → lighten 60% (light text on dark bg)
    ///     — luminance ≥ 160 → darken 60% (dark text on light bg)
    ///   Normal cell: Column.Foreground ?? Grid.Foreground ?? null (WPF default inherited)
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

            return columnForeground ?? gridForeground;
        }
    }
}
