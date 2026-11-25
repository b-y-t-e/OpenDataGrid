using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Open.DataGrid.Columns;
using Open.DataGrid.Models;
using Open.DataGrid.Rendering;
using Open.DataGrid.Virtualization;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// Row container — a lightweight custom Panel that arranges cells horizontally.
    /// Row instances are pooled and recycled; only their data changes during scrolling.
    /// Implements <see cref="IRowSlot"/> so the virtualization engine can work with
    /// test doubles without requiring a WPF visual tree.
    /// </summary>
    public class OpenDataGridRow : Panel, IRowSlot
    {
        private OpenDataGridCell[] _cells;
        private int _columnCount;
        private double[] _columnWidths;
        private double[] _columnOffsets;

        // Pool/rendering state
        public int DataIndex { get; private set; } = -1;
        public int PoolIndex { get; set; }
        public bool IsSelected { get; private set; }
        public bool IsPlaceholder { get; private set; }

        // Cached row data item for edit operations
        public object DataItem { get; private set; }

        private Func<bool, object, Brush> _colorResolver;
        private Brush _resolvedBackground;
        private Brush _defaultRowBackground;   // fallback when _resolvedBackground == null (respects theme)
        private Brush _selectionBrush;
        private Brush _gridForeground;
        private double _alternateRowShift;
        private bool  _isGridFocused;
        private bool _isAlternateRow;
        private FontFamily _defaultCellFontFamily;
        private double? _defaultCellFontSize;
        private FontWeight? _defaultCellFontWeight;
        private bool _isRowChecked;
        private Brush _checkedRowForeground;
        private Brush _checkedRowBackground;
        private FontWeight? _checkedRowFontWeight;

        // Visible column range
        private int    _firstVisibleCol;
        private int    _lastVisibleCol;
        private double _hOffset;

        // Grid lines overlay — drawn on top of all cells via DrawingVisual child
        private readonly DrawingVisual _gridLinesVisual = new DrawingVisual();
        private Pen _gridLinePen;
        private Pen _verticalGridLinePen;
        private GridLinesVisibility _gridLinesVisibility = GridLinesVisibility.Horizontal;

        // Cached accessors for INPC live-refresh
        private Func<object, object>[] _propertyAccessors;
        private Func<object, Brush>[]  _cellBrushAccessors;
        private string[]               _bindingPaths;
        // Item whose PropertyChanged is currently observed (data item, or owner item in info mode).
        private object                 _subscribedItem;

        // Info row mode — lazily created presenter shown instead of cells.
        private InfoRowStyle     _infoStyle;
        private InfoRowPresenter _infoPresenter;
        private Brush            _infoOwnerBackground;
        private bool             _hasInfoRowBelow;

        public bool IsInfoRow { get; private set; }
        public int  InfoOwnerDataIndex { get; private set; } = -1;
        internal InfoRowStyle InfoStyle => _infoStyle;

        /// <summary>
        /// Raised with the observed item when its info property (<see cref="InfoRowStyle.RootPropertyName"/>)
        /// changes — in data mode and in info mode. The grid forwards it to the row engine.
        /// </summary>
        internal event Action<object> InfoSourceChanged;

        public OpenDataGridRow()
        {
            AddVisualChild(_gridLinesVisual);
        }

        // ── Grid lines DrawingVisual overlay ─────────────────────────────────

        protected override int VisualChildrenCount => Children.Count + 1;

        protected override Visual GetVisualChild(int index)
            => index == Children.Count ? _gridLinesVisual : base.GetVisualChild(index);

        public void SetGridLinePen(Pen pen, Pen verticalPen)
        {
            _gridLinePen = pen;
            _verticalGridLinePen = verticalPen;
            InvalidateArrange();
        }

        public void SetGridLinesVisibility(GridLinesVisibility visibility)
        {
            _gridLinesVisibility = visibility;
            InvalidateArrange();
        }

        private void UpdateGridLinesDrawing(Size renderSize)
        {
            using (var dc = _gridLinesVisual.RenderOpen())
            {
                double h = renderSize.Height;
                double w = renderSize.Width;

if (_gridLinePen == null
                    || _gridLinesVisibility == GridLinesVisibility.None
                    || _columnOffsets == null
                    || _columnWidths == null) return;

                // Row separator — bottom edge of row. Omitted above an attached info row so the
                // data row and its info line read as one block.
                if ((_gridLinesVisibility == GridLinesVisibility.All
                     || _gridLinesVisibility == GridLinesVisibility.Horizontal)
                    && !_hasInfoRowBelow)
                    dc.DrawLine(_gridLinePen, new Point(0, h - 0.5), new Point(w, h - 0.5));

                // Column separators — right edge of each visible column (not drawn on info rows)
                if (!IsInfoRow
                    && (_gridLinesVisibility == GridLinesVisibility.All
                        || _gridLinesVisibility == GridLinesVisibility.Vertical))
                {
                    var vPen = _verticalGridLinePen ?? _gridLinePen;
                    for (int i = _firstVisibleCol; i <= _lastVisibleCol && i < _columnCount; i++)
                    {
                        double x = _columnOffsets[i] + _columnWidths[i] - _hOffset - 0.5;
                        if (x <= 0 || x >= w) continue;
                        dc.DrawLine(vPen, new Point(x, 0), new Point(x, h));
                    }
                }
            }
        }

        public void Initialize(
            IReadOnlyList<OpenDataGridColumn> columns,
            double[] columnWidths,
            double[] columnOffsets,
            Func<bool, object, Brush> colorResolver,
            Brush selectionBrush,
            double alternateRowShift = 0.0,
            Brush gridForeground = null,
            Brush selectionForeground = null,
            Brush defaultRowBackground = null)
        {
            _columnCount          = columns.Count;
            _columnWidths         = columnWidths;
            _columnOffsets        = columnOffsets;
            _colorResolver        = colorResolver;
            _selectionBrush       = selectionBrush;
            _gridForeground       = gridForeground;
            _alternateRowShift    = alternateRowShift;
            _defaultRowBackground = defaultRowBackground;

            _bindingPaths = new string[_columnCount];
            _cells = new OpenDataGridCell[_columnCount];
            for (int i = 0; i < _columnCount; i++)
            {
                _bindingPaths[i] = columns[i].BindingPath;
                _cells[i] = new OpenDataGridCell(columns[i], i);
                _cells[i].UpdateDefaultFont(_defaultCellFontFamily, _defaultCellFontSize, _defaultCellFontWeight);
                _cells[i].UpdateSelectionBrush(selectionBrush, alternateRowShift, gridForeground, selectionForeground);
                _cells[i].UpdateCheckedRowStyle(_checkedRowForeground, _checkedRowBackground, _checkedRowFontWeight);
                Children.Add(_cells[i]);
            }

            Background = _defaultRowBackground ?? Brushes.White;
        }

        /// <summary>
        /// Propagates a live theme change to the row: updates brushes, re-resolves the row background
        /// and refreshes the visual state of all cells. Called by the virtualization engine when the
        /// grid's <c>Theme</c> property changes.
        /// </summary>
        public void UpdateRowBrushes(
            Brush selectionBrush,
            Brush defaultRowBackground,
            Func<bool, object, Brush> colorResolver,
            Brush gridForeground,
            Brush selectionForeground,
            double alternateRowShift)
        {
            _selectionBrush       = selectionBrush;
            _defaultRowBackground = defaultRowBackground;
            _colorResolver        = colorResolver;
            _gridForeground       = gridForeground;
            _alternateRowShift    = alternateRowShift;

            // Re-resolve background before updating cells so UpdateRowState gets the fresh value.
            if (DataIndex >= 0 && DataItem != null)
                _resolvedBackground = _colorResolver?.Invoke(_isAlternateRow, DataItem);
            else
                _resolvedBackground = null;

            UpdateBackground(IsSelected);

            // Single pass: push new selection brush and resolved background to all cells.
            if (_cells != null)
                for (int i = 0; i < _columnCount; i++)
                {
                    _cells[i]?.UpdateSelectionBrush(selectionBrush, alternateRowShift, gridForeground, selectionForeground);
                    _cells[i]?.UpdateRowState(_resolvedBackground, IsSelected, _isAlternateRow);
                }
        }

        /// <summary>
        /// Forces the cell at <paramref name="colIndex"/> to re-read its column's Background and repaint.
        /// Called when <see cref="Columns.OpenDataGridColumn.Background"/> changes while this row is live.
        /// </summary>
        public void RefreshCellBackground(int colIndex)
        {
            if (_cells == null || colIndex < 0 || colIndex >= _columnCount) return;
            _cells[colIndex]?.UpdateRowState(_resolvedBackground, IsSelected, _isAlternateRow);
        }

        /// <summary>
        /// Binds this row as an empty placeholder (shown when grid has no data and
        /// <c>ShowEmptyPlaceholderRow</c> is <c>true</c>). All cells are shown empty.
        /// DataIndex is set to <c>-2</c> (sentinel distinct from the "no selection" value of -1).
        /// </summary>
        public void BindPlaceholder(double hOffset, int firstVisibleCol, int lastVisibleCol)
        {
            SubscribeTo(null);
            LeaveInfoMode();
            _hasInfoRowBelow = false;

            DataIndex        = RowVirtualizationEngine.PlaceholderDataIndex;
            DataItem         = null;
            IsPlaceholder    = true;
            _hOffset         = hOffset;
            _firstVisibleCol = firstVisibleCol;
            _lastVisibleCol  = lastVisibleCol;
            _isAlternateRow  = false;
            IsSelected       = false;
            _resolvedBackground = null;

            // Reset checked-state so a recycled slot does not inherit the previous row's
            // checked styling (background / foreground / font weight) onto the placeholder.
            if (_isRowChecked)
            {
                _isRowChecked = false;
                if (_cells != null)
                    for (int i = 0; i < _columnCount; i++)
                        _cells[i]?.UpdateCheckedState(false);
            }

            UpdateBackground(false);

            for (int i = 0; i < _columnCount; i++)
            {
                var cell = _cells[i];
                bool inRange = (i >= firstVisibleCol && i <= lastVisibleCol);
                cell.Visibility = inRange ? Visibility.Visible : Visibility.Collapsed;
                cell.UpdateValue(null);
                cell.UpdateCellBackground(null);
                cell.UpdateRowState(null, false, false);
            }
        }

        /// <summary>
        /// Fast-path rebind: updates all visible cells with new data item values.
        /// Uses pre-compiled property accessors to avoid reflection overhead.
        /// </summary>
        public void Bind(
            int dataIndex,
            object dataItem,
            Func<object, object>[]   propertyAccessors,
            Func<object, Brush>[]    cellBrushAccessors,
            Func<bool, object, Brush> rowColorResolver,
            double hOffset,
            int firstVisibleCol,
            int lastVisibleCol,
            bool isSelected,
            bool isChecked,
            bool isAlternate)
        {
            IsPlaceholder = false;
            LeaveInfoMode();

            DataIndex = dataIndex;
            DataItem  = dataItem;
            _propertyAccessors  = propertyAccessors;
            _cellBrushAccessors = cellBrushAccessors;

            SubscribeTo(dataItem);

            _hOffset         = hOffset;
            _firstVisibleCol = firstVisibleCol;
            _lastVisibleCol  = lastVisibleCol;

            // Row background — single resolver call encapsulates all priority logic.
            // Alternate-row parity is driven by the visual position (isAlternate), NOT the raw
            // DataSource index, so zebra striping stays consistent under filtering / sorting.
            _isAlternateRow     = isAlternate;
            _resolvedBackground = rowColorResolver?.Invoke(isAlternate, dataItem);
            IsSelected          = isSelected;
            UpdateBackground(isSelected);

            // Cell values + backgrounds
            for (int i = 0; i < _columnCount; i++)
            {
                var cell = _cells[i];
                bool inRange = (i >= firstVisibleCol && i <= lastVisibleCol);
                cell.Visibility = inRange ? Visibility.Visible : Visibility.Collapsed;

                if (inRange)
                {
                    var value = propertyAccessors[i]?.Invoke(dataItem);
                    cell.UpdateValue(value);

                    var cellBrush = cellBrushAccessors[i]?.Invoke(dataItem);
                    cell.UpdateCellBackground(cellBrush);
                    cell.UpdateRowState(_resolvedBackground, isSelected, _isAlternateRow);
                }
            }

            UpdateCheckedState(isChecked);
        }

        public void Unbind()
        {
            SubscribeTo(null);
        }

        private void SubscribeTo(object item)
        {
            if (ReferenceEquals(_subscribedItem, item)) return;
            if (_subscribedItem is INotifyPropertyChanged oldInpc)
                oldInpc.PropertyChanged -= OnDataItemPropertyChanged;
            _subscribedItem = item;
            if (item is INotifyPropertyChanged newInpc)
                newInpc.PropertyChanged += OnDataItemPropertyChanged;
        }

        private void OnDataItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Use the observed item, not `sender` — some models (proxies / wrappers) raise
            // PropertyChanged with a different sender than the object we subscribed to.
            var item = _subscribedItem;
            if (Dispatcher.CheckAccess())
                ApplyPropertyChange(item, e.PropertyName);
            else
                Dispatcher.BeginInvoke(new Action(() => ApplyPropertyChange(item, e.PropertyName)));
        }

        private void ApplyPropertyChange(object sender, string propertyName)
        {
            // Stale notification (slot was rebound to another item before the dispatcher ran).
            if (sender == null || !ReferenceEquals(sender, _subscribedItem)) return;

            string infoProperty = _infoStyle?.RootPropertyName;
            if (infoProperty != null && (string.IsNullOrEmpty(propertyName) || propertyName == infoProperty))
            {
                // Raised before the cell refresh — the handler may rebind this slot.
                InfoSourceChanged?.Invoke(sender);
                if (!ReferenceEquals(sender, _subscribedItem)) return;
            }

            if (IsInfoRow) return;
            if (_cells == null || DataItem == null || _propertyAccessors == null || _cellBrushAccessors == null) return;

            // Null or empty name means "all properties changed"
            if (string.IsNullOrEmpty(propertyName))
            {
                _resolvedBackground = _colorResolver?.Invoke(_isAlternateRow, DataItem);
                UpdateBackground(IsSelected);
                RefreshVisibleCellValues(_propertyAccessors, _cellBrushAccessors);
                return;
            }

            for (int i = _firstVisibleCol; i <= _lastVisibleCol && i < _columnCount; i++)
            {
                if (_bindingPaths[i] != propertyName) continue;

                var value = _propertyAccessors[i]?.Invoke(DataItem);
                _cells[i].UpdateValue(value);

                var brush = _cellBrushAccessors[i]?.Invoke(DataItem);
                _cells[i].UpdateCellBackground(brush);
                _cells[i].UpdateRowState(_resolvedBackground, IsSelected, _isAlternateRow);
            }
        }

        // ── Info row mode ────────────────────────────────────────────────────

        /// <summary>Applies the grid's info-row settings. Called once per pooled row by the slot factory.</summary>
        internal void ConfigureInfoRow(InfoRowStyle style)
        {
            _infoStyle = style;
            if (_infoPresenter != null) _infoPresenter.InfoStyle = style;
        }

        public void BindInfo(int ownerDataIndex, object ownerItem, string text,
                             Brush ownerRowBackground, bool isOwnerSelected, double hOffset)
        {
            SubscribeTo(ownerItem);
            SetCellFocus(-1, false);
            UpdateCheckedState(false);

            bool enteringInfoMode = !IsInfoRow;
            IsInfoRow          = true;
            IsPlaceholder      = false;
            InfoOwnerDataIndex = ownerDataIndex;
            DataIndex          = RowVirtualizationEngine.InfoRowDataIndex;
            DataItem           = null;
            _hOffset           = hOffset;
            _hasInfoRowBelow   = false;
            _infoOwnerBackground = ownerRowBackground;

            EnsureInfoPresenter();
            _infoPresenter.SetText(text);

            if (enteringInfoMode)
            {
                if (_cells != null)
                    for (int i = 0; i < _columnCount; i++)
                        _cells[i].Visibility = Visibility.Collapsed;
                _infoPresenter.Visibility = Visibility.Visible;
            }

            IsSelected = isOwnerSelected;
            UpdateInfoBackground();
            InvalidateArrange();
        }

        public void SetHasInfoRowBelow(bool hasInfoRowBelow)
        {
            if (_hasInfoRowBelow == hasInfoRowBelow) return;
            _hasInfoRowBelow = hasInfoRowBelow;
            InvalidateArrange();
        }

        private void LeaveInfoMode()
        {
            if (!IsInfoRow) return;
            IsInfoRow          = false;
            InfoOwnerDataIndex = -1;
            if (_infoPresenter != null) _infoPresenter.Visibility = Visibility.Collapsed;
            // Cell visibility is restored by the caller (Bind / BindPlaceholder set it per column range).
        }

        private void EnsureInfoPresenter()
        {
            if (_infoPresenter != null) return;
            _infoPresenter = new InfoRowPresenter { InfoStyle = _infoStyle };
            _infoPresenter.ApplyFont(_defaultCellFontFamily, _defaultCellFontSize);
            Children.Add(_infoPresenter);
        }

        private void UpdateInfoBackground()
            => Background = _infoPresenter.ResolveBackground(_infoOwnerBackground, IsSelected, _selectionBrush);

        public void UpdateSelection(bool isSelected)
        {
            if (IsSelected == isSelected) return;
            if (IsInfoRow)
            {
                IsSelected = isSelected;
                UpdateInfoBackground();
                return;
            }
            IsSelected = isSelected;
            UpdateBackground(isSelected);

            // Propagate selection state to all cells so CellBackgroundResolver can apply correct priority
            if (_cells == null) return;
            for (int i = 0; i < _columnCount; i++)
                _cells[i]?.UpdateRowState(_resolvedBackground, isSelected, _isAlternateRow);
        }

        private void UpdateBackground(bool isSelected)
        {
            IsSelected = isSelected;
            Brush bg;
            if (isSelected && _selectionBrush != null)
                bg = _selectionBrush;
            else if (_isRowChecked && _checkedRowBackground != null)
                bg = _checkedRowBackground;
            else
                bg = _resolvedBackground ?? _defaultRowBackground ?? Brushes.White;
            Background = bg;
        }

        private int _focusedCellIndex = -1;

        /// <summary>Sets or clears the focused cell indicator. Pass -1 or showFocus=false to clear.</summary>
        public void SetCellFocus(int colIndex, bool showFocus)
        {
            if (_cells == null) return;

            if (_focusedCellIndex >= 0 && _focusedCellIndex < _columnCount)
                _cells[_focusedCellIndex].SetFocused(false);

            if (!showFocus || colIndex < 0 || colIndex >= _columnCount)
            {
                _focusedCellIndex = -1;
                return;
            }

            _focusedCellIndex = colIndex;
            _cells[colIndex].SetFocused(true);
        }

        /// <summary>Updates the grid keyboard-focus state for this row.</summary>
        public void SetGridFocus(bool isGridFocused)
        {
            _isGridFocused = isGridFocused;
        }

        public void UpdateDefaultCellFont(FontFamily fontFamily, double? fontSize, FontWeight? fontWeight)
        {
            _defaultCellFontFamily = fontFamily;
            _defaultCellFontSize = fontSize;
            _defaultCellFontWeight = fontWeight;
            _infoPresenter?.ApplyFont(fontFamily, fontSize);

            if (_cells == null) return;
            for (int i = 0; i < _columnCount; i++)
                _cells[i].UpdateDefaultFont(fontFamily, fontSize, fontWeight);
        }

        public void InvalidateCellFont(int colIndex)
        {
            if (_cells == null || colIndex < 0 || colIndex >= _columnCount) return;
            _cells[colIndex].InvalidateFont();
        }

        public OpenDataGridCell GetCell(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= _columnCount) return null;
            return _cells[columnIndex];
        }

        public int CellCount => _columnCount;

        public void UpdateCheckedState(bool isChecked)
        {
            if (_isRowChecked == isChecked) return;
            _isRowChecked = isChecked;
            UpdateBackground(IsSelected);
            if (_cells == null) return;
            for (int i = 0; i < _columnCount; i++)
                _cells[i]?.UpdateCheckedState(isChecked);
        }

        public void UpdateCheckedRowStyle(Brush foreground, Brush background, FontWeight? fontWeight)
        {
            _checkedRowForeground = foreground;
            _checkedRowBackground = background;
            _checkedRowFontWeight = fontWeight;
            if (_isRowChecked) UpdateBackground(IsSelected);
            if (_cells == null) return;
            for (int i = 0; i < _columnCount; i++)
                _cells[i]?.UpdateCheckedRowStyle(foreground, background, fontWeight);
        }

        // ── IRowSlot explicit members ─────────────────────────────────────────

        bool IRowSlot.IsVisible
        {
            get => Visibility == Visibility.Visible;
            set => Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        public void UpdateCellValue(int colIndex, object value)
        {
            if (_cells == null || colIndex < 0 || colIndex >= _columnCount) return;
            if (colIndex < _firstVisibleCol || colIndex > _lastVisibleCol) return;
            _cells[colIndex].UpdateValue(value);
        }

        /// <summary>Returns the last bound value for the cell at <paramref name="colIndex"/>.</summary>
        public object GetCellValue(int colIndex)
        {
            if (_cells == null || colIndex < 0 || colIndex >= _columnCount) return null;
            return _cells[colIndex].CurrentValue;
        }

        /// <summary>Returns true when the column was in the visible range during the last Bind or Refresh.</summary>
        public bool IsCellLoaded(int colIndex)
            => DataItem != null && colIndex >= _firstVisibleCol && colIndex <= _lastVisibleCol;

        /// <summary>Updates the column layout arrays (called when horizontal scroll changes).</summary>
        public void UpdateColumnLayout(double[] widths, double[] offsets, double hOffset, int firstVisibleCol, int lastVisibleCol)
        {
            _columnWidths    = widths;
            _columnOffsets   = offsets;
            _hOffset         = hOffset;
            _firstVisibleCol = firstVisibleCol;
            _lastVisibleCol  = lastVisibleCol;

            if (!IsInfoRow)
                for (int i = 0; i < _columnCount; i++)
                    _cells[i].Visibility = (i >= firstVisibleCol && i <= lastVisibleCol) ? Visibility.Visible : Visibility.Collapsed;

            InvalidateArrange();
        }

        /// <summary>
        /// Rebinds cell values for the current visible column range.
        /// Called after a horizontal scroll changes which columns are visible — cells that were
        /// Collapsed during the last Bind() have no value set and must be populated now.
        /// </summary>
        public void RefreshVisibleCellValues(
            Func<object, object>[] propertyAccessors,
            Func<object, Brush>[] cellBrushAccessors)
        {
            if (_cells == null || DataItem == null) return;

            for (int i = _firstVisibleCol; i <= _lastVisibleCol && i < _columnCount; i++)
            {
                var value = propertyAccessors[i]?.Invoke(DataItem);
                _cells[i].UpdateValue(value);

                var brush = cellBrushAccessors[i]?.Invoke(DataItem);
                _cells[i].UpdateCellBackground(brush);
                _cells[i].UpdateRowState(_resolvedBackground, IsSelected, _isAlternateRow);
            }
        }

        private double GetTotalColumnsWidth()
        {
            if (_columnWidths == null || _columnCount == 0) return 0;
            int last = _columnCount - 1;
            return _columnOffsets[last] + _columnWidths[last];
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_cells == null) return Size.Empty;

            double desiredHeight = availableSize.Height;
            if (double.IsInfinity(desiredHeight))
                desiredHeight = !double.IsNaN(Height) ? Height : 0;

            double desiredWidth = availableSize.Width;
            if (double.IsInfinity(desiredWidth))
            {
                desiredWidth = 0;
                for (int i = _firstVisibleCol; i <= _lastVisibleCol && i < _columnCount; i++)
                    desiredWidth += _columnWidths[i];
            }

            if (IsInfoRow)
            {
                _infoPresenter.MeasureInRow(new Size(desiredWidth, desiredHeight), _hOffset, GetTotalColumnsWidth());
                return new Size(desiredWidth, desiredHeight);
            }

            for (int i = _firstVisibleCol; i <= _lastVisibleCol && i < _columnCount; i++)
            {
                var cell = _cells[i];
                if (cell.Visibility == Visibility.Collapsed) continue;
                cell.Measure(new Size(_columnWidths[i], desiredHeight));
            }

            return new Size(desiredWidth, desiredHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_cells == null) return finalSize;

            if (IsInfoRow)
            {
                _infoPresenter.ArrangeInRow(finalSize, _hOffset, GetTotalColumnsWidth());
                UpdateGridLinesDrawing(finalSize);
                return finalSize;
            }

            double scrollOrigin = _hOffset;

            for (int i = _firstVisibleCol; i <= _lastVisibleCol && i < _columnCount; i++)
            {
                var cell = _cells[i];
                if (cell.Visibility == Visibility.Collapsed) continue;

                double x = _columnOffsets[i] - scrollOrigin;
                cell.Arrange(new Rect(x, 0, _columnWidths[i], finalSize.Height));
            }

            UpdateGridLinesDrawing(finalSize);
            return finalSize;
        }
    }
}
