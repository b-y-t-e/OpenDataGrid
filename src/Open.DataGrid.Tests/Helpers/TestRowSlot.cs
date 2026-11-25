using System;
using System.Windows;
using System.Windows.Media;
using Open.DataGrid.Models;
using Open.DataGrid.Virtualization;

namespace Open.DataGrid.Tests.Helpers
{
    /// <summary>
    /// Pure-C# test double for <see cref="IRowSlot"/>.
    /// Tracks which columns received values during Bind / RefreshVisibleCellValues
    /// so viewport tests can assert "column X was loaded" without a WPF visual tree.
    /// </summary>
    public sealed class TestRowSlot : IRowSlot
    {
        private object[] _values;
        private int _columnCount;

        public int DataIndex { get; private set; } = -1;
        public object DataItem { get; private set; }
        public bool IsVisible { get; set; }
        public int PoolIndex { get; set; }
        public Brush LastRowBrush { get; private set; }
        public bool IsAlternate { get; private set; }

        private int _firstVisibleCol = -1;
        private int _lastVisibleCol  = -1;

        public void Initialize(int columnCount)
        {
            _columnCount = columnCount;
            _values = new object[columnCount];
        }

        public void BindPlaceholder(double hOffset, int firstVisibleCol, int lastVisibleCol)
        {
            DataIndex        = RowVirtualizationEngine.PlaceholderDataIndex;
            DataItem         = null;
            _firstVisibleCol = firstVisibleCol;
            _lastVisibleCol  = lastVisibleCol;
        }

        public void Bind(
            int dataIndex,
            object dataItem,
            Func<object, object>[] propertyAccessors,
            Func<object, Brush>[]  cellBrushAccessors,
            Func<bool, object, Brush> rowColorResolver,
            double hOffset,
            int firstVisibleCol,
            int lastVisibleCol,
            bool isSelected,
            bool isChecked,
            bool isAlternate)
        {
            DataIndex = dataIndex;
            DataItem  = dataItem;
            IsAlternate = isAlternate;
            _firstVisibleCol = firstVisibleCol;
            _lastVisibleCol  = lastVisibleCol;
            LastRowBrush = rowColorResolver?.Invoke(isAlternate, dataItem);

            for (int i = firstVisibleCol; i <= lastVisibleCol && i < _columnCount; i++)
                _values[i] = propertyAccessors[i]?.Invoke(dataItem);
        }

        public void UpdateColumnLayout(double[] widths, double[] offsets, double hOffset, int firstVisibleCol, int lastVisibleCol)
        {
            _firstVisibleCol = firstVisibleCol;
            _lastVisibleCol  = lastVisibleCol;
        }

        public void RefreshVisibleCellValues(
            Func<object, object>[] propertyAccessors,
            Func<object, Brush>[] cellBrushAccessors)
        {
            if (DataItem == null) return;
            for (int i = _firstVisibleCol; i <= _lastVisibleCol && i < _columnCount; i++)
                _values[i] = propertyAccessors[i]?.Invoke(DataItem);
        }

        public void UpdateSelection(bool isSelected) { }
        public void Unbind() { }
        public void SetCellFocus(int colIndex, bool showFocus) { }
        public void SetGridFocus(bool isGridFocused) { }
        public void UpdateDefaultCellFont(FontFamily fontFamily, double? fontSize, FontWeight? fontWeight) { }
        public void InvalidateCellFont(int colIndex) { }
        public void SetGridLinePen(Pen pen, Pen verticalPen) { }
        public void SetGridLinesVisibility(GridLinesVisibility visibility) { }
        public int CheckedRowStyleCallCount { get; private set; }
        public Brush LastCheckedRowForeground { get; private set; }
        public Brush LastCheckedRowBackground { get; private set; }
        public FontWeight? LastCheckedRowFontWeight { get; private set; }

        public void UpdateCheckedState(bool isChecked) { }
        public void UpdateCheckedRowStyle(Brush foreground, Brush background, FontWeight? fontWeight)
        {
            CheckedRowStyleCallCount++;
            LastCheckedRowForeground = foreground;
            LastCheckedRowBackground = background;
            LastCheckedRowFontWeight = fontWeight;
        }

        public void UpdateRowBrushes(
            Brush selectionBrush,
            Brush defaultRowBackground,
            Func<bool, object, Brush> colorResolver,
            Brush gridForeground,
            Brush selectionForeground,
            double alternateRowShift) { }

        public void RefreshCellBackground(int colIndex) { }

        public void UpdateCellValue(int colIndex, object value)
        {
            if (colIndex < 0 || colIndex >= _columnCount) return;
            if (colIndex < _firstVisibleCol || colIndex > _lastVisibleCol) return;
            _values[colIndex] = value;
        }

        public object GetCellValue(int colIndex)
        {
            if (colIndex < 0 || colIndex >= _columnCount) return null;
            return _values[colIndex];
        }

        public bool IsCellLoaded(int colIndex)
            => DataItem != null && colIndex >= _firstVisibleCol && colIndex <= _lastVisibleCol;
    }
}
