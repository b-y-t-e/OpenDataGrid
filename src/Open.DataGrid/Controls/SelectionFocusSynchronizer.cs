using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Columns;
using Open.DataGrid.Models;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// Centralizes all selection/focus synchronization logic for <see cref="OpenDataGrid"/>:
    /// mouse-click selection, keyboard focus entry/exit, cell focus application,
    /// ensure-visible scrolling, SelectedItem DP sync, and column resolution.
    ///
    /// The single source of truth for the current cell position remains
    /// <c>NavigationEngine.CurrentRowIndex / CurrentColumnIndex</c>; this class
    /// reads those values through <c>_grid.FocusedRowSortedIndex</c> /
    /// <c>_grid.FocusedColIndex</c> and writes them via
    /// <c>_grid._navEngine.SetCurrentPosition</c>.
    /// </summary>
    internal sealed class SelectionFocusSynchronizer
    {
        private readonly OpenDataGrid _grid;

        // -----------------------------------------------------------------------
        // State (previously scattered across OpenDataGrid fields)
        // -----------------------------------------------------------------------

        /// <summary>
        /// True after the first <see cref="ApplyCellFocus"/> call.
        /// Distinguishes "never focused" from "focused at row 0" (both have CurrentRowIndex == 0
        /// after NavigationEngine.Initialize sets its position).
        /// </summary>
        private bool _cellFocusApplied;

        /// <summary>
        /// Guard set around internal writes to <c>SelectedItem</c> so that
        /// <c>OnSelectedItemChanged</c> ignores UI-driven changes and does not re-enter.
        /// </summary>
        internal bool InternalSelectionChange;

        /// <summary>
        /// Stores a <c>SelectedItem</c> set by the ViewModel before the grid is initialized
        /// or before the item is present in <c>DataSource</c>.
        /// Consumed by <see cref="OpenDataGrid.FinishInitialize"/>.
        /// </summary>
        internal object PendingSelectedItem;

        private bool _isGridFocused;

        /// <summary>
        /// Consumed once in <see cref="HandleGotKeyboardFocus"/> to skip <see cref="TryRestoreDefaultColumnOnFocus"/>
        /// when focus returns to the grid after an internal edit exit (CommitEdit/CancelEdit).
        /// </summary>
        private bool _suppressRestoreColumnOnce;

        public bool CellFocusApplied => _cellFocusApplied;

        public SelectionFocusSynchronizer(OpenDataGrid grid)
        {
            _grid = grid;
        }

        // -----------------------------------------------------------------------
        // State management
        // -----------------------------------------------------------------------

        /// <summary>
        /// Resets the "cell focus was applied" flag so that
        /// <see cref="ApplyDefaultCellSelectionIfNeeded"/> re-applies the default column
        /// on the next keyboard-focus event.
        /// </summary>
        public void ResetCellFocusApplied() => _cellFocusApplied = false;

        // -----------------------------------------------------------------------
        // Canvas mouse click  (from OpenDataGrid.Selection.cs)
        // -----------------------------------------------------------------------

        public void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_grid._editingCell != null)
            {
                var source = e.OriginalSource as DependencyObject;
                bool clickInsideCurrentEditor = source != null && _grid._editingCell.IsAncestorOf(source);
                if (!clickInsideCurrentEditor)
                    _grid.CommitEdit();
            }

            _grid.Focus();
            var pos = e.GetPosition(_grid._canvas);
            int rowVisualIndex = (int)(pos.Y / _grid.RowHeight) + _grid._rowEngine.GetFirstVisibleIndex();
            int totalVisible = _grid.IsPlaceholderActive ? 1 : (_grid._sortEngine.SortedIndices?.Length ?? 0);
            if (rowVisualIndex < 0 || rowVisualIndex >= totalVisible) return;

            var columns = _grid.GetVisibleColumnList();

            // Always compute clicked column from x-position so CheckColumn toggle works in
            // both Cell and Row navigation modes.
            double xInData = pos.X + _grid._hOffset;
            int detectedCol = columns.Count > 0 ? columns.Count - 1 : 0;
            {
                double cumX = 0;
                for (int i = 0; i < columns.Count; i++)
                {
                    cumX += columns[i].Width;
                    if (xInData < cumX) { detectedCol = i; break; }
                }
            }

            // In Cell mode cell-focus tracks the click; in Row mode keep the current column.
            int clickedCol = _grid.NavigationMode == NavigationMode.Cell
                ? detectedCol
                : (_grid.FocusedColIndex >= 0 ? _grid.FocusedColIndex : 0);

            ApplyCellFocus(rowVisualIndex, clickedCol, ensureVisible: false);

            // CheckColumn toggle — active in both Cell and Row mode; single click only.
            if (detectedCol >= 0 && detectedCol < columns.Count
                && columns[detectedCol] is OpenDataGridCheckColumn clickedCheckCol
                && (_grid.CheckedItems != null || !string.IsNullOrEmpty(clickedCheckCol.BindingPath))
                && e.ClickCount == 1)
            {
                var sortedIndices = _grid._sortEngine.SortedIndices;
                if (sortedIndices != null && rowVisualIndex >= 0 && rowVisualIndex < sortedIndices.Length)
                {
                    var dataItem = _grid.DataSource?[sortedIndices[rowVisualIndex]];
                    if (dataItem != null)
                        _grid.ToggleChecked(dataItem, clickedCheckCol);
                }
            }
            else if (_grid.NavigationMode == NavigationMode.Cell)
            {
                if (columns[detectedCol] is OpenDataGridComboColumn && e.ClickCount == 1)
                    _grid.TryBeginEdit(rowVisualIndex, detectedCol, Key.F4);
                else if (e.ClickCount == 2 && !(columns[detectedCol] is OpenDataGridComboColumn))
                    _grid.TryBeginEdit(rowVisualIndex, detectedCol, Key.F2);
            }

            e.Handled = true;
        }

        // -----------------------------------------------------------------------
        // Selection helpers  (from OpenDataGrid.Selection.cs)
        // -----------------------------------------------------------------------

        public void SelectBySortedIndex(int sortedIndex)
        {
            if (sortedIndex < 0 || sortedIndex >= (_grid._sortEngine.SortedIndices?.Length ?? 0)) return;
            int dataIndex = _grid._sortEngine.SortedIndices[sortedIndex];
            SelectByDataIndex(dataIndex);
        }

        public void SelectByDataIndex(int dataIndex)
        {
            // Guard against stale SortedIndices: when items are removed from an ObservableCollection
            // the rebuild is deferred via Dispatcher.BeginInvoke. In the gap between the collection
            // shrinking and the rebuild completing, dataIndex may exceed the new DataSource.Count.
            var dataSource = _grid.DataSource;
            if (dataSource == null || dataIndex < 0 || dataIndex >= dataSource.Count) return;

            // InternalSelectionChange guards the entire block — SetSelection/SelectedIndex changes
            // must not be observable as external changes by OnSelectedItemChanged re-entry.
            InternalSelectionChange = true;
            try
            {
                _grid._rowEngine.SetSelection(dataIndex);
                _grid.SelectedIndex = dataIndex;
                _grid.SelectedItem = dataSource[dataIndex];
            }
            finally { InternalSelectionChange = false; }
        }

        /// <summary>
        /// Jedna ścieżka aplikowania focusu komórki: selekcja wiersza, pozycja nawigacji,
        /// render focus na wierszu, aktualizacja bindowalnych właściwości i (opcjonalnie)
        /// scroll w zakres widoczny. Używane przez klik myszą, nawigację klawiaturą
        /// i domyślną selekcję na focus.
        /// </summary>
        public void ApplyCellFocus(int sortedRowIndex, int colIndex, bool ensureVisible)
        {
            _cellFocusApplied = true;
            _grid._navEngine.SetCurrentPosition(sortedRowIndex, colIndex);
            if (!_grid.IsPlaceholderActive)
                SelectBySortedIndex(sortedRowIndex);

            if (ensureVisible)
            {
                EnsureRowVisible(sortedRowIndex);
                EnsureColumnVisible(colIndex);
            }

            _grid._rowEngine.SetFocus(sortedRowIndex, colIndex, _grid.NavigationMode == NavigationMode.Cell);

            _grid.FocusedColumnIndex = colIndex;
            var cols = _grid.GetVisibleColumnList();
            _grid.FocusedColumnHeader = (colIndex >= 0 && colIndex < cols.Count)
                ? cols[colIndex].Header ?? string.Empty
                : string.Empty;
        }

        // -----------------------------------------------------------------------
        // Focus event handlers  (from OpenDataGrid.Navigation.cs)
        // -----------------------------------------------------------------------

        public void HandleGotFocus()
        {
            if (!_grid.IsGridInitialized) return;
            if (_grid.SelectedItem == null && _grid.EffectiveRowCount > 0 && !_cellFocusApplied)
            {
                int firstVisible = _grid._rowEngine.GetFirstVisibleIndex();
                int col = _grid.FocusedColIndex >= 0 ? _grid.FocusedColIndex : 0;
                ApplyCellFocus(firstVisible, col, ensureVisible: false);
            }
        }

        public void HandleLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            var newFocus = e.NewFocus as DependencyObject;
            bool focusLeft = newFocus == null || !_grid.IsAncestorOf(newFocus);

            if (focusLeft && _isGridFocused)
            {
                _isGridFocused = false;
                if (_grid.IsGridInitialized) _grid.SetGridFocusVisual(false);
            }

            bool keepEditingForComboPopup = false;
            if (focusLeft && _grid._editingCell?.Column is OpenDataGridComboColumn)
            {
                var combo = _grid._editingCell.EditElement as ComboBox;
                keepEditingForComboPopup = combo != null && combo.IsDropDownOpen;
            }

            if (_grid._editingCell != null && focusLeft && !keepEditingForComboPopup)
                _grid.CommitEdit(restoreFocus: false);
        }

        public void HandleGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
#if DEBUG
            Debug.WriteLine($"[EDG] OnGotKeyboardFocus: NewFocus={e.NewFocus?.GetType().Name} isThis={e.NewFocus == _grid} _cellFocusApplied={_cellFocusApplied} vScrollVal={_grid._vScrollBar?.Value} vScrollMax={_grid._vScrollBar?.Maximum}");
#endif
            if (!_isGridFocused)
            {
                _isGridFocused = true;
                if (_grid.IsGridInitialized) _grid.SetGridFocusVisual(true);
            }
            if (e.NewFocus == _grid)
            {
                ApplyDefaultCellSelectionIfNeeded();
                if (_suppressRestoreColumnOnce)
                    _suppressRestoreColumnOnce = false;
                else
                    TryRestoreDefaultColumnOnFocus();
            }
        }

        // -----------------------------------------------------------------------
        // Edit-exit helper
        // -----------------------------------------------------------------------

        /// <summary>
        /// Called by CommitEdit / CancelEdit before Focus() so that the resulting
        /// GotKeyboardFocus does not reset the focused column to SelectedColumn.
        /// </summary>
        internal void SuppressRestoreColumnOnceForEditExit() => _suppressRestoreColumnOnce = true;

        // -----------------------------------------------------------------------
        // Default-column selection  (from OpenDataGrid.Navigation.cs)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Jeśli NavigationMode to Cell i nie ma jeszcze sfokusowanej komórki,
        /// ustawia domyślną selekcję komórki (wiersz 0, kolumna z <see cref="OpenDataGrid.SelectedColumn"/>).
        /// </summary>
        public void ApplyDefaultCellSelectionIfNeeded()
        {
            if (!_grid.IsGridInitialized) return;
            if (_grid.NavigationMode != NavigationMode.Cell) return;
            if (_cellFocusApplied) return;
            if (_grid.EffectiveRowCount == 0) return;

            int colIndex = ResolveDefaultCellColumnIndex();
            if (colIndex < 0) return;

            ApplyCellFocus(sortedRowIndex: 0, colIndex: colIndex, ensureVisible: true);
        }

        /// <summary>
        /// Jeśli <see cref="OpenDataGrid.RestoreDefaultColumnOnFocus"/> jest <c>true</c> i
        /// <see cref="OpenDataGrid.SelectedColumn"/> wskazuje istniejącą kolumnę, przy każdym uzyskaniu
        /// keyboard focusu przestawia kolumnę focusu na domyślną — zachowując bieżący wiersz.
        /// </summary>
        private void TryRestoreDefaultColumnOnFocus()
        {
            if (!_grid.RestoreDefaultColumnOnFocus) return;
            if (!_grid.IsGridInitialized) return;
            if (_grid.NavigationMode != NavigationMode.Cell) return;
            if (!_cellFocusApplied) return;
            if (string.IsNullOrEmpty(_grid.SelectedColumn)) return;

            var cols = _grid.GetVisibleColumnList();
            if (cols.Count == 0) return;
            int colIndex = -1;
            for (int i = 0; i < cols.Count; i++)
            {
                if (string.Equals(cols[i].Header?.Trim(), _grid.SelectedColumn.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cols[i].BindingPath?.Trim(), _grid.SelectedColumn.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    colIndex = i;
                    break;
                }
            }
            if (colIndex < 0) return;

            int row = _grid.FocusedRowSortedIndex >= 0 ? _grid.FocusedRowSortedIndex : 0;
            ApplyCellFocus(row, colIndex, ensureVisible: false);
        }

        // -----------------------------------------------------------------------
        // Public selection API  (from OpenDataGrid.Navigation.cs)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Przesuwa focus komórki do kolumny wskazanej przez <paramref name="nameOrPath"/>
        /// (dopasowanie po <c>Header</c> lub <c>BindingPath</c>, case-insensitive).
        /// </summary>
        public void SelectColumn(string nameOrPath)
        {
            if (!_grid.IsGridInitialized) return;
            if (_grid.NavigationMode != NavigationMode.Cell) return;
            if (_grid.EffectiveRowCount == 0) return;

            int colIndex = ResolveColumnIndex(nameOrPath?.Trim());
            if (colIndex < 0) return;

            int rowIndex = _grid.FocusedRowSortedIndex >= 0 ? _grid.FocusedRowSortedIndex : 0;
            ApplyCellFocus(rowIndex, colIndex, ensureVisible: true);
        }

        // -----------------------------------------------------------------------
        // Column resolution  (from OpenDataGrid.Navigation.cs)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Rozwiązuje wartość <see cref="OpenDataGrid.SelectedColumn"/> na indeks widocznej kolumny.
        /// Zwraca 0 (fallback) gdy brak dopasowania; -1 gdy lista jest pusta.
        /// </summary>
        public int ResolveDefaultCellColumnIndex()
        {
            var cols = _grid.GetVisibleColumnList();
            if (cols.Count == 0) return -1;
            return ResolveColumnIndex(_grid.SelectedColumn?.Trim());
        }

        /// <summary>
        /// Rozwiązuje <paramref name="key"/> na indeks widocznej kolumny.
        /// Dopasowuje po <c>Header</c> lub <c>BindingPath</c> (case-insensitive).
        /// Zwraca -1 gdy lista kolumn jest pusta, 0 (fallback) gdy brak dopasowania.
        /// </summary>
        public int ResolveColumnIndex(string key)
        {
            var cols = _grid.GetVisibleColumnList();
            if (cols.Count == 0) return -1;

            if (!string.IsNullOrEmpty(key))
            {
                for (int i = 0; i < cols.Count; i++)
                {
                    var header = cols[i].Header?.Trim();
                    var path   = cols[i].BindingPath?.Trim();
                    if (string.Equals(header, key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(path,   key, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return 0;
        }

        // -----------------------------------------------------------------------
        // Ensure-visible  (from OpenDataGrid.Navigation.cs)
        // -----------------------------------------------------------------------

        public void EnsureColumnVisible(int colIndex)
        {
            if (_grid._hScrollBar == null || _grid._canvas == null) return;
            if (colIndex < 0) return;

            double left  = _grid._colEngine.GetColumnOffset(colIndex);
            var    cols  = _grid.GetVisibleColumnList();
            double width = (colIndex < cols.Count) ? cols[colIndex].Width : 0;
            double right = left + width;
            double viewport = _grid._canvas.ActualWidth;

            double newOffset = _grid._hOffset;
            if (left < _grid._hOffset)
                newOffset = left;
            else if (right > _grid._hOffset + viewport)
                newOffset = right - viewport;

            if (Math.Abs(newOffset - _grid._hOffset) < OpenDataGrid.HorizontalOffsetEpsilon) return;

            newOffset = Math.Max(0, Math.Min(_grid._hScrollBar.Maximum, newOffset));
            _grid._hScrollBar.Value = newOffset;
            _grid._hOffset = newOffset;
            _grid.UpdateColumnRange();
            _grid.LayoutHeaders();
            _grid.LayoutFilterRow();
        }

        public void EnsureRowVisible(int sortedIndex)
        {
            if (_grid._vScrollBar == null) return;

            int firstVisible  = _grid._rowEngine.GetFirstVisibleIndex();
            int fullVisibleRows = _grid._rowEngine.GetVisibleRowCount();
            int targetFirstVisible;

            if (fullVisibleRows <= 0)
            {
                targetFirstVisible = sortedIndex;
            }
            else
            {
                int lastFullyVisible = firstVisible + fullVisibleRows - 1;

                if (sortedIndex < firstVisible)
                    targetFirstVisible = sortedIndex;
                else if (sortedIndex > lastFullyVisible)
                    targetFirstVisible = sortedIndex - fullVisibleRows + 1;
                else
                {
#if DEBUG
                    Debug.WriteLine($"[EDG] EnsureRowVisible: sortedIndex={sortedIndex} already visible [first={firstVisible} last={lastFullyVisible}] — skipping scroll");
#endif
                    return;
                }
            }

            targetFirstVisible = Math.Max(0, Math.Min(_grid._rowEngine.GetMaxFirstVisibleIndex(), targetFirstVisible));
#if DEBUG
            Debug.WriteLine($"[EDG] EnsureRowVisible: sortedIndex={sortedIndex} first={firstVisible} visRows={fullVisibleRows} maxFirst={_grid._rowEngine.GetMaxFirstVisibleIndex()} target={targetFirstVisible} vScrollMax={_grid._vScrollBar.Maximum} vScrollValBefore={_grid._vScrollBar.Value}");
#endif
            _grid._vScrollBar.Value = targetFirstVisible;
#if DEBUG
            Debug.WriteLine($"[EDG] EnsureRowVisible: vScrollValAfter={_grid._vScrollBar.Value} (expected {targetFirstVisible})");
#endif
            _grid._rowEngine.ScrollTo(targetFirstVisible);
        }

        // -----------------------------------------------------------------------
        // ScrollToItem  (from OpenDataGrid.Navigation.cs)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Scrolluje grid do podanego wiersza (indeks danych) i kolumny.
        /// </summary>
        public void ScrollToItem(int dataIndex, int columnIndex = 0)
        {
            if (!_grid.IsGridInitialized || _grid.DataSource == null) return;
            if (dataIndex < 0 || dataIndex >= _grid.DataSource.Count) return;

            int sortedIndex = _grid._sortEngine.FindSortedPosition(dataIndex);
            if (sortedIndex < 0) return;

            // In Row mode the column index is irrelevant — preserve the current focused column.
            int col = _grid.NavigationMode == NavigationMode.Row
                ? _grid.FocusedColIndex
                : columnIndex;
            ApplyCellFocus(sortedIndex, col, ensureVisible: true);
        }

        // -----------------------------------------------------------------------
        // SelectedItem from ViewModel  (from OpenDataGrid.cs)
        // -----------------------------------------------------------------------

        public void ApplySelectedItemFromViewModel(object item)
        {
            if (_grid.DataSource == null) return;
            int dataIndex = _grid.DataSource.IndexOf(item);
            if (dataIndex < 0) return;

            int sortedIndex = _grid._sortEngine.FindSortedPosition(dataIndex);
#if DEBUG
            Debug.WriteLine($"[EDG] ApplySelectedItemFromViewModel: dataIndex={dataIndex} sortedIndex={sortedIndex} AutoScroll={_grid.AutoScrollToSelectedItem} vScrollMax={_grid._vScrollBar?.Maximum} vScrollVal={_grid._vScrollBar?.Value}");
#endif
            if (sortedIndex < 0)
            {
                // Item is in DataSource but filtered out. Applying an invisible selection would
                // desync SelectedItem DP from the rendered UI, so we always clear it; the fallback
                // to the first visible row is gated by RequireSelection.
                if (_grid.RequireSelection)
                    ClearSelectionAndFallbackToFirst();
                else
                    ClearSelectedItem();
                return;
            }

            if (_grid.AutoScrollToSelectedItem)
                EnsureRowVisible(sortedIndex);

            if (_grid.IsGridInitialized && _grid.NavigationMode == NavigationMode.Cell)
            {
                int colIndex = !_cellFocusApplied
                    ? ResolveDefaultCellColumnIndex()
                    : _grid.FocusedColIndex;
                if (colIndex < 0) colIndex = 0;
                ApplyCellFocus(sortedIndex, colIndex, ensureVisible: false);
            }
            else
            {
                SelectByDataIndex(dataIndex);
            }
        }

        /// <summary>
        /// Sets SelectedItem to null under the InternalSelectionChange guard so that
        /// OnSelectedItemChanged treats the change as internal (no recursive re-entry).
        /// </summary>
        internal void ClearSelectedItem()
        {
            InternalSelectionChange = true;
            try { _grid.SelectedItem = null; }
            finally { InternalSelectionChange = false; }
        }

        /// <summary>
        /// Clears a stale SelectedItem (no longer present in DataSource) and selects the first
        /// visible item in the current sorted/filtered set, or leaves SelectedItem = null when
        /// the grid is empty.
        /// </summary>
        public void ClearSelectionAndFallbackToFirst()
        {
            if (_grid.DataSource == null) return;

            ClearSelectedItem();

            var indices = _grid._sortEngine.SortedIndices;
            if (indices == null || indices.Length == 0) return;

            // CurrentRowIndex has already been clamped to [0, newTotal-1] by the time this runs:
            //   • InitializeNavEngine() → NavigationEngine.Initialize() clamps on DataSource replace.
            //   • NavigationEngine.UpdateStats() clamps after collection or filter change.
            // So FocusedRowSortedIndex already points to the "nearest" position:
            //   deleted at N, new length > N  → N (item that moved into that slot, i.e. next below)
            //   deleted at N, new length ≤ N  → new last item
            // Falls back to index 0 when FocusedRowSortedIndex == -1 (no prior focus).
            int target    = Math.Max(0, Math.Min(_grid.FocusedRowSortedIndex, indices.Length - 1));
            int dataIndex = indices[target];
            if (dataIndex >= 0 && dataIndex < _grid.DataSource.Count)
                ApplySelectedItemFromViewModel(_grid.DataSource[dataIndex]);
        }
    }
}
