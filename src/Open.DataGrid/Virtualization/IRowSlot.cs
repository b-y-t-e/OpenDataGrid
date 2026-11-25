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
                  Func<bool, object, Brush> rowColorResolver,
                  double hOffset,
                  int firstVisibleCol, int lastVisibleCol,
                  bool isSelected,
                  bool isChecked,
                  bool isAlternate);

        // ── Info row ─────────────────────────────────────────────────────────

        /// <summary><c>true</c> when the slot currently renders an info row (full-width, non-focusable text line).</summary>
        bool IsInfoRow { get; }

        /// <summary>Data index of the row owning the info line; <c>-1</c> when the slot is not an info row.</summary>
        int InfoOwnerDataIndex { get; }

        /// <summary>
        /// Binds the slot as an info row rendered below the data row <paramref name="ownerDataIndex"/>.
        /// <see cref="DataIndex"/> becomes <see cref="RowVirtualizationEngine.InfoRowDataIndex"/> and
        /// <see cref="DataItem"/> becomes <c>null</c>, so selection / focus / edit lookups never match it.
        /// </summary>
        void BindInfo(int ownerDataIndex, object ownerItem, string text,
                      Brush ownerRowBackground, bool isOwnerSelected, double hOffset);

        /// <summary>Tells a data row whether its info row is rendered directly below it (suppresses the separator line).</summary>
        void SetHasInfoRowBelow(bool hasInfoRowBelow);

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

        /// <summary>
        /// Forces the cell at <paramref name="colIndex"/> to re-read its column's Background and
        /// repaint. Called when <see cref="Open.DataGrid.Columns.OpenDataGridColumn.Background"/>
        /// changes while the row is already visible in the pool.
        /// </summary>
        void RefreshCellBackground(int colIndex);

        /// <summary>
        /// Propagates a theme brush change to the row: updates selection brush, default row background,
        /// color resolver and foreground, then re-applies background to the row and cells.
        /// Called by <see cref="Open.DataGrid.Virtualization.RowVirtualizationEngine.UpdateRowBrushes"/>.
        /// </summary>
        void UpdateRowBrushes(
            Brush selectionBrush,
            Brush defaultRowBackground,
            Func<bool, object, Brush> colorResolver,
            Brush gridForeground,
            Brush selectionForeground,
            double alternateRowShift);

        /// <summary>Sets the pens used to draw grid lines. Pass <c>null</c> as <paramref name="pen"/> to hide grid lines.
        /// <paramref name="verticalPen"/> is used for column separators; pass <c>null</c> to reuse <paramref name="pen"/>.</summary>
        void SetGridLinePen(Pen pen, Pen verticalPen);

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
