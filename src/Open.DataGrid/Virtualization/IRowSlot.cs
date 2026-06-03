using System;
using System.Windows;
using System.Windows.Media;
using Open.DataGrid.Models;

namespace Open.DataGrid.Virtualization
{
    /// <summary>
    /// Abstraction over a single row slot in the virtualization pool.
    /// Production code uses <see cref="Controls.OpenDataGridRow"/> (WPF Panel).
    /// Tests use <c>TestRowSlot</c> (pure C#, no visual tree required).
    /// </summary>
    public interface IRowSlot
    {
        // ── Identity / state ─────────────────────────────────────────────────

        int    DataIndex { get; }
        object DataItem  { get; }
        bool   IsVisible { get; set; }

        // ── Data operations (called by RowVirtualizationEngine) ───────────────

        /// <summary>
        /// Binds the slot as an empty placeholder row (no data item, all cells shown as empty).
        /// The slot's <see cref="DataIndex"/> is set to <c>-2</c> (sentinel for placeholder).
        /// </summary>
        void BindPlaceholder(double hOffset, int firstVisibleCol, int lastVisibleCol);

        void Bind(int dataIndex, object dataItem,
                  Func<object, object>[]  propertyAccessors,
                  Func<object, Brush>[]   cellBrushAccessors,
                  Func<int, object, Brush> rowColorResolver,
                  double hOffset,
                  int firstVisibleCol, int lastVisibleCol,
                  bool isSelected,
                  bool isChecked);

        void UpdateColumnLayout(double[] widths, double[] offsets,
                                double hOffset,
                                int firstVisibleCol, int lastVisibleCol);

        void RefreshVisibleCellValues(Func<object, object>[] propertyAccessors,
                                      Func<object, Brush>[]  cellBrushAccessors);

        void UpdateSelection(bool isSelected);

        /// <summary>Unsubscribes any live-refresh event handlers before the slot is discarded.</summary>
        void Unbind();

        void SetCellFocus(int colIndex, bool showFocus);

        /// <summary>Updates the grid keyboard-focus state for this row.</summary>
        void SetGridFocus(bool isGridFocused);

        /// <summary>Updates default cell font settings for all cells in the row slot.</summary>
        void UpdateDefaultCellFont(FontFamily fontFamily, double? fontSize, FontWeight? fontWeight);

        /// <summary>Re-applies font to the cell at the given column index (picks up per-column FontSize override).</summary>
        void InvalidateCellFont(int colIndex);

        /// <summary>Applies or clears the checked-row visual state (background, foreground, font weight override).</summary>
        void UpdateCheckedState(bool isChecked);

        /// <summary>Propagates new checked-row style brushes/font to the row and all its cells.</summary>
        void UpdateCheckedRowStyle(Brush foreground, Brush background, FontWeight? fontWeight);

        /// <summary>Sets the pen used to draw grid lines. Pass <c>null</c> to hide grid lines.</summary>
        void SetGridLinePen(Pen pen);

        /// <summary>Sets which grid line directions are drawn.</summary>
        void SetGridLinesVisibility(GridLinesVisibility visibility);

        // ── Query (used by tests and engine internals) ────────────────────────

        /// <summary>
        /// Updates the display value of a single cell without touching other cells.
        /// No-op when <paramref name="colIndex"/> is outside the current visible range.
        /// </summary>
        void UpdateCellValue(int colIndex, object value);

        /// <summary>Returns the last value bound to the cell at <paramref name="colIndex"/>,
        /// or <c>null</c> if the column was never in the visible range.</summary>
        object GetCellValue(int colIndex);

        /// <summary>Returns <c>true</c> when the cell has received a value
        /// (i.e. the column was in the visible range during the last Bind or Refresh).</summary>
        bool IsCellLoaded(int colIndex);
    }
}
