using System.Windows.Media;

namespace Open.DataGrid.Rendering
{
    /// <summary>
    /// All background-layer inputs for a single cell. Passed by reference to
    /// <see cref="CellBackgroundResolver.Resolve"/> to determine the final brush.
    /// </summary>
    internal readonly struct CellBackgroundContext
    {
        /// <summary>Pre-computed focus-highlight brush (lighter/darker variant of SelectionBrush). Non-null only when cell is focused.</summary>
        public readonly Brush FocusCellBrush;

        /// <summary>Grid-level selection brush (from SelectionBrush DP).</summary>
        public readonly Brush SelectionBrush;

        /// <summary>Column-level static background (Column.Background DP). May be null.</summary>
        public readonly Brush ColumnBackground;

        /// <summary>Whether Column.Background takes priority over data-bound brush (Column.BackgroundOverridesData DP).</summary>
        public readonly bool BackgroundOverridesData;

        /// <summary>Per-cell brush resolved from CellBackgroundBindingPath. May be null.</summary>
        public readonly Brush DataCellBrush;

        /// <summary>Row-level resolved background (result of rowColorResolver — includes alternate/selector/path/default). May be null.</summary>
        public readonly Brush RowResolvedBackground;

        /// <summary>Whether the row is currently selected.</summary>
        public readonly bool IsRowSelected;

        /// <summary>Whether this cell is the focused cell.</summary>
        public readonly bool IsCellFocused;

        /// <summary>Whether this is an alternate row (visual position, logicalIndex % 2 != 0).</summary>
        public readonly bool IsAlternateRow;

        /// <summary>Shift magnitude for alternate-row color adjustment (0 = disabled). From Grid.AlternateRowShift DP.</summary>
        public readonly double AlternateRowShift;

        /// <summary>
        /// Pre-computed alternate background for Column.Background (cached via Column.GetAlternateBackground).
        /// Null when Column.Background is null, shift is 0, or base is not a SolidColorBrush.
        /// </summary>
        public readonly Brush ColumnAlternateBackground;

        public CellBackgroundContext(
            Brush  focusCellBrush,
            Brush  selectionBrush,
            Brush  columnBackground,
            bool   backgroundOverridesData,
            Brush  dataCellBrush,
            Brush  rowResolvedBackground,
            bool   isRowSelected,
            bool   isCellFocused,
            bool   isAlternateRow,
            double alternateRowShift,
            Brush  columnAlternateBackground)
        {
            FocusCellBrush            = focusCellBrush;
            SelectionBrush            = selectionBrush;
            ColumnBackground          = columnBackground;
            BackgroundOverridesData   = backgroundOverridesData;
            DataCellBrush             = dataCellBrush;
            RowResolvedBackground     = rowResolvedBackground;
            IsRowSelected             = isRowSelected;
            IsCellFocused             = isCellFocused;
            IsAlternateRow            = isAlternateRow;
            AlternateRowShift         = alternateRowShift;
            ColumnAlternateBackground = columnAlternateBackground;
        }
    }
}
