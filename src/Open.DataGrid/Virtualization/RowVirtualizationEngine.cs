using System;
using System.Collections;
using System.Windows;
using System.Windows.Media;
using Open.DataGrid.Models;

namespace Open.DataGrid.Virtualization
{
    /// <summary>
    /// Core virtualization engine. Manages the row pool, maps scroll positions to data indices,
    /// and updates only the rows that changed during scrolling.
    /// Never creates more than (visibleRowCount + 2*bufferRows) IRowSlot instances.
    ///
    /// Depends on <see cref="IGridCanvas"/> and <see cref="IRowSlot"/> so the entire engine
    /// can be exercised in unit tests without a WPF visual tree.
    /// </summary>
    public sealed class RowVirtualizationEngine
    {
        private const int BufferRows = 4;

        /// <summary>Sentinel DataIndex used by placeholder slots (distinct from -1 = "no selection").</summary>
        public const int PlaceholderDataIndex = -2;

        private readonly RowPool<IRowSlot> _pool = new RowPool<IRowSlot>();
        private IGridCanvas _canvas;
        private Func<int, IRowSlot> _slotFactory;
        private IList _dataSource;
        private int[] _sortedIndices;
        private Func<object, object>[] _propertyAccessors;
        private Func<object, Brush>[]  _cellBrushAccessors;
        private Func<int, object, Brush> _rowColorResolver;
        private Func<object, bool>     _rowReadOnlyAccessor;
        private Func<object, bool>     _isCheckedAccessor;
        private Brush                  _checkedRowForeground;
        private Brush                  _checkedRowBackground;
        private FontWeight?            _checkedRowFontWeight;

        private double _rowHeight;
        private int    _fullVisibleRowCount;
        private int    _renderVisibleRowCount;
        private int    _poolSize;
        private int    _firstLogicalIndex;
        private int    _totalRows;
        private int    _selectedDataIndex  = -1;
        private int    _focusedDataIndex   = -1;
        private int    _focusedColumnIndex = -1;
        private bool   _showCellFocus;
        private bool   _isGridFocused;
        private bool   _placeholderActive;

        // Column range
        private double   _hOffset;
        private int      _firstVisibleCol;
        private int      _lastVisibleCol;
        private double[] _columnWidths;
        private double[] _columnOffsets;

        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Initialises the engine and creates the row pool.
        /// All parameters are bundled in <see cref="RowEngineConfig"/> to keep the call site readable.
        /// </summary>
        public void Initialize(RowEngineConfig cfg)
        {
            _canvas              = cfg.Canvas;
            _slotFactory         = cfg.SlotFactory;
            _dataSource          = cfg.DataSource;
            _sortedIndices       = cfg.SortedIndices;
            _propertyAccessors   = cfg.PropertyAccessors;
            _cellBrushAccessors  = cfg.CellBrushAccessors;
            _rowColorResolver    = cfg.RowColorResolver;
            _rowReadOnlyAccessor = cfg.RowReadOnlyAccessor;
            _isCheckedAccessor   = cfg.IsCheckedAccessor;
            _checkedRowForeground = cfg.CheckedRowForeground;
            _checkedRowBackground = cfg.CheckedRowBackground;
            _checkedRowFontWeight = cfg.CheckedRowFontWeight;
            _rowHeight       = cfg.RowHeight;
            _hOffset         = cfg.HOffset;
            _firstVisibleCol = cfg.FirstVisibleCol;
            _lastVisibleCol  = cfg.LastVisibleCol;
            _columnWidths    = cfg.ColumnWidths;
            _columnOffsets   = cfg.ColumnOffsets;
            _totalRows         = cfg.SortedIndices?.Length ?? 0;
            _placeholderActive = cfg.IsPlaceholderMode && _totalRows == 0;
            if (_placeholderActive) _totalRows = 1;

            RecalculateVisibleRowCounts(cfg.CanvasHeight);
            _poolSize          = _renderVisibleRowCount + 2 * BufferRows;
            _firstLogicalIndex = -BufferRows;

            _canvas.Clear();
            int oldPoolSize = _pool.Size;
            for (int i = 0; i < oldPoolSize; i++)
                _pool.GetRow(i).Unbind();

            _pool.Initialize(_poolSize, poolIndex =>
            {
                var slot = _slotFactory(poolIndex);
                _canvas.AddSlot(slot);
                return slot;
            });

            IsInitialized = true;
            FullRefresh(-BufferRows);

            // Propagate checked-row style to all newly created pool slots.
            if (_checkedRowForeground != null || _checkedRowBackground != null || _checkedRowFontWeight.HasValue)
            {
                for (int i = 0; i < _poolSize; i++)
                    _pool.GetRow(i).UpdateCheckedRowStyle(_checkedRowForeground, _checkedRowBackground, _checkedRowFontWeight);
            }

            // New slot instances start with _isGridFocused = false.
            // If the engine already knows the grid has keyboard focus (state persists across Initialize
            // calls), propagate that state to the fresh slots so the first scroll into the focused row
            // shows the cell border immediately — without waiting for the next GotKeyboardFocus event.
            if (_isGridFocused)
                SetGridFocused(_isGridFocused);
        }

        public void UpdateColumnRange(int firstVisibleCol, int lastVisibleCol,
                                      double hOffset,
                                      double[] columnWidths, double[] columnOffsets)
        {
            _hOffset         = hOffset;
            _firstVisibleCol = firstVisibleCol;
            _lastVisibleCol  = lastVisibleCol;
            _columnWidths    = columnWidths;
            _columnOffsets   = columnOffsets;

            // Propagate layout + rebind values for newly visible columns.
            // UpdateColumnLayout only toggles visibility; cells that were out of range during
            // Bind() have never received a value, so RefreshVisibleCellValues must follow.
            if (_pool != null)
            {
                for (int i = 0; i < _poolSize; i++)
                {
                    var slot = _pool.GetRow(i);
                    slot.UpdateColumnLayout(columnWidths, columnOffsets, hOffset, firstVisibleCol, lastVisibleCol);
                    slot.RefreshVisibleCellValues(_propertyAccessors, _cellBrushAccessors);
                }
            }
        }

        public void SetSelection(int selectedDataIndex)
        {
            int oldSelected = _selectedDataIndex;
            if (oldSelected == selectedDataIndex) return;

            _selectedDataIndex = selectedDataIndex;

            for (int i = 0; i < _poolSize; i++)
            {
                var slot = _pool.GetRow(i);
                if (slot.DataIndex == oldSelected)
                    slot.UpdateSelection(false);
                else if (slot.DataIndex == selectedDataIndex)
                    slot.UpdateSelection(true);
            }
        }

        /// <summary>Updates the grid keyboard-focus state. Propagates to all rows in pool.</summary>
        public void SetGridFocused(bool isGridFocused)
        {
            _isGridFocused = isGridFocused;

            for (int i = 0; i < _poolSize; i++)
                _pool.GetRow(i).SetGridFocus(isGridFocused);
        }

        public void SetFocus(int sortedIndex, int colIndex, bool showCellFocus)
        {
            int dataIndex;
            if (_placeholderActive && sortedIndex == 0)
                dataIndex = PlaceholderDataIndex;
            else
                dataIndex = (sortedIndex >= 0 && sortedIndex < (_sortedIndices?.Length ?? 0))
                    ? _sortedIndices[sortedIndex] : -1;

            for (int i = 0; i < _poolSize; i++)
            {
                var slot = _pool.GetRow(i);
                if (slot.DataIndex == _focusedDataIndex)
                    slot.SetCellFocus(-1, false);
                if (slot.DataIndex == dataIndex)
                    slot.SetCellFocus(colIndex, showCellFocus);
            }
            _focusedDataIndex   = dataIndex;
            _focusedColumnIndex = colIndex;
            _showCellFocus      = showCellFocus;
        }

        /// <summary>
        /// Updates only the specified cells for the pool slot that currently renders <paramref name="item"/>.
        /// </summary>
        public void RefreshSpecificCellsForItem(object item, int[] colIndices, Func<object, object>[] propertyAccessors)
        {
            if (_pool == null || item == null || colIndices == null) return;
            for (int i = 0; i < _poolSize; i++)
            {
                var slot = _pool.GetRow(i);
                if (ReferenceEquals(slot.DataItem, item))
                {
                    foreach (int ci in colIndices)
                        slot.UpdateCellValue(ci, propertyAccessors[ci]?.Invoke(item));
                    break;
                }
            }
        }

        /// <summary>
        /// Refreshes only the specified columns for all occupied pool slots.
        /// </summary>
        public void RefreshAllCellValuesForColumns(int[] colIndices, Func<object, object>[] propertyAccessors)
        {
            if (_pool == null || colIndices == null || colIndices.Length == 0) return;
            for (int i = 0; i < _poolSize; i++)
            {
                var slot = _pool.GetRow(i);
                if (slot.DataItem == null) continue;
                foreach (int ci in colIndices)
                    slot.UpdateCellValue(ci, propertyAccessors[ci]?.Invoke(slot.DataItem));
            }
        }

        /// <summary>
        /// Updates the checked-row visual style for all pool slots and re-renders any currently checked rows.
        /// Call when <c>CheckedRowForeground</c>, <c>CheckedRowBackground</c>, or <c>CheckedRowFontWeight</c> DPs change.
        /// </summary>
        public void UpdateCheckedRowStyle(Brush foreground, Brush background, FontWeight? fontWeight)
        {
            _checkedRowForeground = foreground;
            _checkedRowBackground = background;
            _checkedRowFontWeight = fontWeight;
            if (_pool == null) return;
            for (int i = 0; i < _poolSize; i++)
                _pool.GetRow(i).UpdateCheckedRowStyle(foreground, background, fontWeight);
        }

        /// <summary>
        /// Updates the checked state for the single slot that currently renders <paramref name="item"/>.
        /// Called immediately after <see cref="ToggleChecked"/> so the row re-renders without a full refresh.
        /// </summary>
        public void UpdateCheckedStateForItem(object item, bool isChecked)
        {
            if (_pool == null || item == null) return;
            for (int i = 0; i < _poolSize; i++)
            {
                var slot = _pool.GetRow(i);
                if (ReferenceEquals(slot.DataItem, item))
                {
                    slot.UpdateCheckedState(isChecked);
                    break;
                }
            }
        }

        /// <summary>
        /// Re-evaluates the checked state for every occupied slot using <paramref name="isCheckedAccessor"/>.
        /// Called when the CheckedItems collection changes externally (Add/Remove/Reset from VM).
        /// </summary>
        public void RefreshAllCheckedStates(Func<object, bool> isCheckedAccessor)
        {
            if (_pool == null || isCheckedAccessor == null) return;
            for (int i = 0; i < _poolSize; i++)
            {
                var slot = _pool.GetRow(i);
                if (slot.DataItem != null)
                    slot.UpdateCheckedState(isCheckedAccessor(slot.DataItem));
            }
        }

        /// <summary>Propagates a changed grid line pen to all rows in the pool.</summary>
        public void SetGridLinePen(Pen pen)
        {
            for (int i = 0; i < _poolSize; i++)
                _pool.GetRow(i).SetGridLinePen(pen);
        }

        /// <summary>Propagates changed grid line visibility to all rows in the pool.</summary>
        public void SetGridLinesVisibility(GridLinesVisibility visibility)
        {
            for (int i = 0; i < _poolSize; i++)
                _pool.GetRow(i).SetGridLinesVisibility(visibility);
        }

        public void UpdateDefaultCellFont(FontFamily fontFamily, double? fontSize, FontWeight? fontWeight)
        {
            for (int i = 0; i < _poolSize; i++)
                _pool.GetRow(i).UpdateDefaultCellFont(fontFamily, fontSize, fontWeight);
        }

        public void InvalidateColumnFont(int colIndex)
        {
            for (int i = 0; i < _poolSize; i++)
                _pool.GetRow(i).InvalidateCellFont(colIndex);
        }

        /// <summary>
        /// Forces every row in the pool to repaint the cell at <paramref name="colIndex"/> by
        /// re-reading the column's Background DP. Called when
        /// <see cref="Open.DataGrid.Columns.OpenDataGridColumn.Background"/> changes while rows are live.
        /// </summary>
        public void RefreshColumnBackground(int colIndex)
        {
            if (_pool == null) return;
            for (int i = 0; i < _poolSize; i++)
                _pool.GetRow(i).RefreshCellBackground(colIndex);
        }

        /// <summary>
        /// Propagates a theme brush change to every row in the pool.
        /// Rows re-resolve their background colours and refresh cell foregrounds immediately.
        /// Called when the grid's <c>Theme</c> property changes.
        /// </summary>
        public void UpdateRowBrushes(
            Brush selectionBrush,
            Brush defaultRowBackground,
            Func<int, object, Brush> colorResolver,
            Brush gridForeground,
            Brush selectionForeground,
            double alternateRowShift)
        {
            if (_pool == null) return;
            for (int i = 0; i < _poolSize; i++)
                _pool.GetRow(i).UpdateRowBrushes(
                    selectionBrush, defaultRowBackground, colorResolver,
                    gridForeground, selectionForeground, alternateRowShift);
        }

        /// <summary>
        /// Main scroll handler. Updates visible rows based on new first visible data index.
        /// Only rebinds rows that actually changed (incremental delta updates).
        /// </summary>
        public void ScrollTo(int newFirstVisibleIndex)
        {
            if (!IsInitialized) return;
            newFirstVisibleIndex = Math.Max(0, Math.Min(GetMaxFirstVisibleIndex(), newFirstVisibleIndex));

            int currentFirstVisible = _firstLogicalIndex + BufferRows;
            int delta = newFirstVisibleIndex - currentFirstVisible;

            if (delta == 0) return;

            int newFirstLogical = newFirstVisibleIndex - BufferRows;

            if (Math.Abs(delta) >= _poolSize)
            {
                _firstLogicalIndex = newFirstLogical;
                FullRefresh(newFirstLogical);
                return;
            }

            if (delta > 0)
            {
                _pool.RotateForward(delta);
                for (int i = _poolSize - delta; i < _poolSize; i++)
                    BindSlotAtPoolOffset(i, newFirstLogical + i);
            }
            else
            {
                int absCount = -delta;
                _pool.RotateBackward(absCount);
                for (int i = 0; i < absCount; i++)
                    BindSlotAtPoolOffset(i, newFirstLogical + i);
            }

            _firstLogicalIndex = newFirstLogical;
            RepositionSlots(newFirstLogical, newFirstVisibleIndex);
        }

        private void FullRefresh(int firstLogicalIndex)
        {
            _firstLogicalIndex = firstLogicalIndex;
            int firstVisibleIndex = firstLogicalIndex + BufferRows;

            for (int i = 0; i < _poolSize; i++)
                BindSlotAtPoolOffset(i, firstLogicalIndex + i);

            RepositionSlots(firstLogicalIndex, firstVisibleIndex);
        }

        private void BindSlotAtPoolOffset(int poolOffset, int logicalIndex)
        {
            var slot = _pool.GetRow(poolOffset);
            if (logicalIndex < 0 || logicalIndex >= _totalRows)
            {
                slot.IsVisible = false;
                return;
            }

            if (_placeholderActive && logicalIndex == 0)
            {
                slot.IsVisible = true;
                slot.BindPlaceholder(_hOffset, _firstVisibleCol, _lastVisibleCol);
                slot.SetCellFocus(
                    _focusedColumnIndex,
                    _showCellFocus && _focusedDataIndex == PlaceholderDataIndex);
                return;
            }

            slot.IsVisible = true;
            int dataIndex = _sortedIndices[logicalIndex];
            if (dataIndex < 0 || dataIndex >= _dataSource.Count)
            {
                // _sortedIndices is stale (DataSource shrank but rebuild is still pending on
                // the dispatcher). Hide the slot; the upcoming FullRefresh will remap it.
                slot.IsVisible = false;
                return;
            }
            object dataItem   = _dataSource[dataIndex];
            bool   isSelected = (dataIndex == _selectedDataIndex);
            bool   isChecked  = _isCheckedAccessor?.Invoke(dataItem) ?? false;

            slot.Bind(dataIndex, dataItem, _propertyAccessors, _cellBrushAccessors,
                      _rowColorResolver, _hOffset, _firstVisibleCol, _lastVisibleCol, isSelected, isChecked);

            slot.SetCellFocus(
                dataIndex == _focusedDataIndex ? _focusedColumnIndex : -1,
                _showCellFocus && dataIndex == _focusedDataIndex);
        }

        private void RepositionSlots(int firstLogicalIndex, int firstVisibleIndex)
        {
            for (int i = 0; i < _poolSize; i++)
            {
                var slot = _pool.GetRow(i);
                if (!slot.IsVisible) continue;

                int    logicalIndex = firstLogicalIndex + i;
                double top          = (logicalIndex - firstVisibleIndex) * _rowHeight;
                double width        = _canvas.Width > 0 ? _canvas.Width : 1000;
                _canvas.PositionSlot(slot, top, width, _rowHeight);
            }
        }

        public void Resize(double newCanvasHeight)
        {
            if (!IsInitialized) return;

            int oldRenderVisibleRowCount = _renderVisibleRowCount;
            RecalculateVisibleRowCounts(newCanvasHeight);

            if (_renderVisibleRowCount != oldRenderVisibleRowCount)
            {
                int firstVisible  = _firstLogicalIndex + BufferRows;
                _poolSize = _renderVisibleRowCount + 2 * BufferRows;

                int prevPoolSize = _pool.Size;
                for (int i = 0; i < prevPoolSize; i++)
                    _pool.GetRow(i).Unbind();

                _canvas.Clear();
                _pool.Initialize(_poolSize, poolIndex =>
                {
                    var slot = _slotFactory(poolIndex);
                    _canvas.AddSlot(slot);
                    return slot;
                });

                for (int i = 0; i < _poolSize; i++)
                    _pool.GetRow(i).UpdateCheckedRowStyle(_checkedRowForeground, _checkedRowBackground, _checkedRowFontWeight);

                _firstLogicalIndex = Math.Max(-BufferRows, firstVisible - BufferRows);
                FullRefresh(_firstLogicalIndex);
            }
            else
            {
                RepositionSlots(_firstLogicalIndex, _firstLogicalIndex + BufferRows);
            }
        }

        public int GetFirstVisibleIndex() => _firstLogicalIndex + BufferRows;
        public int GetVisibleRowCount()   => _fullVisibleRowCount;

        public int GetMaxFirstVisibleIndex()
        {
            if (_totalRows <= 0) return 0;
            if (_fullVisibleRowCount <= 0) return _totalRows - 1;
            return Math.Max(0, _totalRows - _fullVisibleRowCount);
        }

        private void RecalculateVisibleRowCounts(double canvasHeight)
        {
            if (_rowHeight <= 0)
            {
                _fullVisibleRowCount = 0;
                _renderVisibleRowCount = 0;
                return;
            }

            double effectiveHeight = Math.Max(0, canvasHeight);
            _fullVisibleRowCount = (int)Math.Floor(effectiveHeight / _rowHeight);

            bool hasPartialRow = effectiveHeight - (_fullVisibleRowCount * _rowHeight) > 0.0001;
            _renderVisibleRowCount = _fullVisibleRowCount + (hasPartialRow ? 1 : 0);
        }

        /// <summary>Finds the pool slot for a given data index; returns <c>null</c> if not in pool.</summary>
        public IRowSlot FindRowByDataIndex(int dataIndex)
        {
            for (int i = 0; i < _poolSize; i++)
            {
                var slot = _pool.GetRow(i);
                if (slot.DataIndex == dataIndex && slot.IsVisible) return slot;
            }
            return null;
        }
    }
}
