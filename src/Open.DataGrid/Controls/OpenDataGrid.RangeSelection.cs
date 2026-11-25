using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Open.DataGrid.Models;

namespace Open.DataGrid.Controls
{
    public partial class OpenDataGrid
    {
        #region Cell range selection

        /// <summary>
        /// Enables Excel-like cell range selection in <see cref="Models.NavigationMode.Cell"/>: mouse drag,
        /// Ctrl+click/drag (more ranges), Shift+click and Shift+navigation keys (extend), Ctrl+A (all).
        /// Ctrl+C then copies the selected block. Default: <c>true</c>. Ignored in Row mode.
        /// </summary>
        public static readonly DependencyProperty AllowRangeSelectionProperty =
            DependencyProperty.Register(nameof(AllowRangeSelection), typeof(bool), typeof(OpenDataGrid),
                new PropertyMetadata(true, OnAllowRangeSelectionChanged));

        /// <summary>
        /// Background of cells inside a selected range. <c>null</c> (default) = <see cref="SelectionBrush"/>
        /// blended halfway into <see cref="RowBackground"/> (lighter than the selection, follows the theme).
        /// </summary>
        public static readonly DependencyProperty RangeSelectionBrushProperty =
            DependencyProperty.Register(nameof(RangeSelectionBrush), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnRangeSelectionBrushChanged));

        public bool AllowRangeSelection { get => (bool)GetValue(AllowRangeSelectionProperty); set => SetValue(AllowRangeSelectionProperty, value); }
        public Brush RangeSelectionBrush { get => (Brush)GetValue(RangeSelectionBrushProperty); set => SetValue(RangeSelectionBrushProperty, value); }

        /// <summary>Raised whenever the set of selected cell ranges changes (including being cleared).</summary>
        public event EventHandler RangeSelectionChanged;

        /// <summary>
        /// The selected cell ranges in selection order (rows = visual positions after sort/filter,
        /// columns = visible-column indices). Empty when only the active cell is selected.
        /// </summary>
        public IReadOnlyList<CellRange> SelectedRanges => _rangeSelection.Ranges;

        /// <summary>
        /// Selects the block between two corner cells (visual row positions, visible-column indices;
        /// clamped to the view). With <paramref name="add"/> the block is added to the current ranges.
        /// No-op in Row mode or when <see cref="AllowRangeSelection"/> is off.
        /// </summary>
        public void SelectRange(int firstRow, int firstColumn, int lastRow, int lastColumn, bool add = false)
            => _rangeSelection.SelectRange(firstRow, firstColumn, lastRow, lastColumn, add);

        /// <summary>Removes all selected cell ranges (the active cell stays).</summary>
        public void ClearRangeSelection() => _rangeSelection.Clear();

        internal void RaiseRangeSelectionChanged() => RangeSelectionChanged?.Invoke(this, EventArgs.Empty);

        private static void OnAllowRangeSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) ((OpenDataGrid)d)._rangeSelection.Clear();
        }

        private static void OnRangeSelectionBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((OpenDataGrid)d)._rangeSelection.RefreshHighlight();

        // ── Pointer API (called by the canvas mouse handlers; internal for tests) ──────

        internal void BeginRangePointer(int row, int column, ModifierKeys modifiers) => _rangeSelection.BeginPointer(row, column, modifiers);
        internal void UpdateRangePointer(int row, int column) => _rangeSelection.UpdatePointer(row, column);
        internal void EndRangePointer() => _rangeSelection.EndPointer();

        // ── Keyboard ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Range keys outside edit mode: Ctrl+A, Shift+navigation (extend), Escape (clear). A plain
        /// navigation key clears the ranges and is then handled as usual (returns <c>false</c>).
        /// Returns <c>true</c> when the key was consumed.
        /// </summary>
        internal bool TryHandleRangeKey(Key key, ModifierKeys modifiers)
        {
            if (_navEngine.IsEditing) return false;
            bool ctrl  = (modifiers & ModifierKeys.Control) != 0;
            bool shift = (modifiers & ModifierKeys.Shift) != 0;
            bool alt   = (modifiers & ModifierKeys.Alt) != 0;
            if (alt) return false;

            if (key == Key.A && ctrl && !shift)
                return _rangeSelection.SelectAll();

            if (key == Key.Escape)
            {
                if (!_rangeSelection.HasRanges) return false;
                _rangeSelection.Clear();
                return true;
            }

            if (shift && RangeSelectionController.IsRangeNavigationKey(key))
                return _rangeSelection.ExtendByKey(key, ctrl);

            if (_rangeSelection.HasRanges && RangeSelectionController.IsCollapsingKey(key))
                _rangeSelection.Clear();
            return false;
        }

        // ── Scrolling during a drag (auto-scroll) ───────────────────────────────────

        internal void ScrollLinesForRangeDrag(int lines)
        {
            if (_vScrollBar == null || !_rowEngine.IsInitialized || lines == 0) return;
            int target = Math.Max(0, Math.Min(_rowEngine.GetMaxFirstVisibleIndex(), _rowEngine.GetFirstVisibleIndex() + lines));
            _vScrollBar.Value = target;
            _rowEngine.ScrollTo(target);
        }

        internal void ScrollHorizontallyForRangeDrag(double delta)
        {
            if (_hScrollBar == null || _canvas == null) return;
            double offset = Math.Max(0, Math.Min(_hScrollBar.Maximum, _hOffset + delta));
            if (Math.Abs(offset - _hOffset) < HorizontalOffsetEpsilon) return;
            _hScrollBar.Value = offset;
            _hOffset = offset;
            UpdateColumnRange();
            LayoutHeaders();
            LayoutFilterRow();
        }

        #endregion
    }
}
