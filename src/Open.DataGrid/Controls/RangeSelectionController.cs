using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Open.DataGrid.Models;
using Open.DataGrid.Rendering;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// Excel-like cell range selection for <see cref="OpenDataGrid"/> in
    /// <see cref="NavigationMode.Cell"/>: mouse drag (with auto-scroll), Ctrl+click/drag for further
    /// ranges, Shift+click and Shift+navigation keys to extend, Ctrl+A for everything.
    ///
    /// The active cell (focus, <c>SelectedItem</c>, editing) is unchanged by a drag or a Shift
    /// extension — it stays at the anchor, as in Excel. Ranges are stored as coordinates only
    /// (sorted row positions × visible-column indices), so their size does not matter; they are
    /// positional and therefore cleared whenever the view is rebuilt (sort, filter, data or column
    /// changes, navigation mode change).
    /// </summary>
    internal sealed class RangeSelectionController
    {
        /// <summary>Interval of the auto-scroll timer while dragging outside the data viewport.</summary>
        internal static readonly TimeSpan AutoScrollInterval = TimeSpan.FromMilliseconds(50);

        private const double AutoScrollHorizontalStep = 20;
        private const int    AutoScrollMaxLinesPerStep = 10;

        private readonly OpenDataGrid _grid;
        private readonly List<CellRange> _ranges = new List<CellRange>();
        private readonly ReadOnlyCollection<CellRange> _readOnlyRanges;

        // Anchor = the fixed corner of the range being shaped; End = the moving corner.
        private int _anchorRow = -1, _anchorCol = -1;
        private int _endRow = -1, _endCol = -1;

        // Index in _ranges of the range the current gesture (drag / Shift) reshapes; -1 = none yet.
        private int _shapedRangeIndex = -1;

        // The ranges last reported through RangeSelectionChanged — the event fires only when they differ.
        private CellRange[] _notifiedRanges = new CellRange[0];

        // True while the controller itself moves the active cell (Ctrl+press, plain press).
        private bool _movingActiveCell;

        // Pointer gesture state.
        private bool _pointerActive;
        private bool _pointerMoved;
        private bool _pointerHadModifiers;
        private int  _pressRow = -1, _pressCol = -1;
        private DispatcherTimer _autoScrollTimer;

        public RangeSelectionController(OpenDataGrid grid)
        {
            _grid = grid;
            _readOnlyRanges = _ranges.AsReadOnly();
        }

        public IReadOnlyList<CellRange> Ranges => _readOnlyRanges;

        public bool HasRanges => _ranges.Count > 0;

        public bool IsPointerActive => _pointerActive;

        /// <summary>Range selection applies only in Cell mode with <c>AllowRangeSelection</c> on and real data rows.</summary>
        public bool IsEnabled =>
            _grid.AllowRangeSelection
            && _grid.NavigationMode == NavigationMode.Cell
            && !_grid.IsPlaceholderActive
            && RowCount > 0
            && ColumnCount > 0;

        private int RowCount    => _grid.TotalVisibleRows;
        private int ColumnCount => _grid.GetVisibleColumnList().Count;

        /// <summary><c>true</c> when (row, column) lies in any selected range.</summary>
        public bool Contains(int row, int column)
        {
            for (int i = 0; i < _ranges.Count; i++)
                if (_ranges[i].Contains(row, column)) return true;
            return false;
        }

        /// <summary>Smallest rectangle covering every selected range; <c>null</c> when there are none.</summary>
        public CellRange? GetBoundingRange()
        {
            if (_ranges.Count == 0) return null;
            int r1 = int.MaxValue, c1 = int.MaxValue, r2 = int.MinValue, c2 = int.MinValue;
            foreach (var r in _ranges)
            {
                r1 = Math.Min(r1, r.FirstRow);    r2 = Math.Max(r2, r.LastRow);
                c1 = Math.Min(c1, r.FirstColumn); c2 = Math.Max(c2, r.LastColumn);
            }
            return new CellRange(r1, c1, r2, c2);
        }

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>Removes every range (no-op when there are none).</summary>
        public void Clear()
        {
            EndPointerCore(runClickAction: false);
            _anchorRow = _anchorCol = _endRow = _endCol = -1;
            ClearRanges();
        }

        private void ClearRanges()
        {
            _shapedRangeIndex = -1;
            if (_ranges.Count == 0) return;
            _ranges.Clear();
            NotifyIfRangesChanged();
        }

        /// <summary>Selects (or with <paramref name="add"/> adds) a range; coordinates are clamped to the view.</summary>
        public void SelectRange(int firstRow, int firstColumn, int lastRow, int lastColumn, bool add)
        {
            if (!IsEnabled) return;
            int r1 = ClampRow(firstRow), r2 = ClampRow(lastRow);
            int c1 = ClampCol(firstColumn), c2 = ClampCol(lastColumn);

            if (!add) _ranges.Clear();
            _ranges.Add(new CellRange(r1, c1, r2, c2));
            _anchorRow = r1; _anchorCol = c1;
            _endRow    = r2; _endCol    = c2;
            _shapedRangeIndex = _ranges.Count - 1;
            NormalizeAndNotify();
        }

        /// <summary>Ctrl+A — every row × every visible column. Returns <c>false</c> when not applicable.</summary>
        public bool SelectAll()
        {
            if (!IsEnabled) return false;
            _ranges.Clear();
            _ranges.Add(new CellRange(0, 0, RowCount - 1, ColumnCount - 1));
            _anchorRow = 0; _anchorCol = 0;
            _endRow = RowCount - 1; _endCol = ColumnCount - 1;
            _shapedRangeIndex = 0;
            NotifyIfRangesChanged();
            return true;
        }

        // -----------------------------------------------------------------------
        // Keyboard
        // -----------------------------------------------------------------------

        /// <summary>
        /// Shift+navigation key: moves the range's moving corner (the active cell stays).
        /// Returns <c>true</c> when the key was consumed.
        /// </summary>
        public bool ExtendByKey(Key key, bool ctrl)
        {
            if (!IsEnabled) return false;
            if (!IsRangeNavigationKey(key)) return false;

            EnsureAnchor();
            int row = _ranges.Count > 0 && _endRow >= 0 ? _endRow : _anchorRow;
            int col = _ranges.Count > 0 && _endCol >= 0 ? _endCol : _anchorCol;
            int page = Math.Max(1, _grid._rowEngine.GetVisibleDataRowCount());

            switch (key)
            {
                case Key.Up:       row = ctrl ? 0 : row - 1; break;
                case Key.Down:     row = ctrl ? RowCount - 1 : row + 1; break;
                case Key.Left:     col = ctrl ? 0 : col - 1; break;
                case Key.Right:    col = ctrl ? ColumnCount - 1 : col + 1; break;
                case Key.PageUp:   row -= page; break;
                case Key.PageDown: row += page; break;
                case Key.Home:     if (ctrl) row = 0; else col = 0; break;
                case Key.End:      if (ctrl) row = RowCount - 1; else col = ColumnCount - 1; break;
            }

            ShapeTo(ClampRow(row), ClampCol(col));
            _grid._selection.EnsureRowVisible(_endRow);
            _grid._selection.EnsureColumnVisible(_endCol);
            return true;
        }

        internal static bool IsRangeNavigationKey(Key key)
            => key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right
               || key == Key.PageUp || key == Key.PageDown || key == Key.Home || key == Key.End;

        /// <summary>A plain navigation key (or Tab / Enter) collapses the selection back to the active cell.</summary>
        internal static bool IsCollapsingKey(Key key) => IsRangeNavigationKey(key) || key == Key.Tab || key == Key.Enter;

        // -----------------------------------------------------------------------
        // Pointer
        // -----------------------------------------------------------------------

        /// <summary>
        /// Left press on a data cell (Cell mode). Plain: the pressed cell becomes the active cell and
        /// the anchor, ranges are cleared. Ctrl: adds a new range at the pressed cell (the previous
        /// active cell joins the selection) and moves the active cell there. Shift: range from the
        /// anchor to the pressed cell, active cell unchanged. The gesture continues with
        /// <see cref="UpdatePointer"/> and ends with <see cref="EndPointer"/>.
        /// </summary>
        public void BeginPointer(int row, int col, ModifierKeys modifiers)
        {
            if (!IsEnabled) return;
            row = ClampRow(row);
            col = ClampCol(col);
            bool ctrl  = (modifiers & ModifierKeys.Control) != 0;
            bool shift = (modifiers & ModifierKeys.Shift) != 0;

            _pointerActive       = true;
            _pointerMoved        = false;
            _pointerHadModifiers = ctrl || shift;
            _pressRow = row;
            _pressCol = col;

            if (shift)
            {
                EnsureAnchor();
                ShapeTo(row, col);
                _pointerMoved = true;   // never a click
                return;
            }

            if (ctrl)
            {
                int activeRow = _grid.FocusedRowSortedIndex, activeCol = _grid.FocusedColIndex;
                if (_ranges.Count == 0 && activeRow >= 0 && activeCol >= 0 && (activeRow != row || activeCol != col))
                    _ranges.Add(new CellRange(activeRow, activeCol, activeRow, activeCol));

                MoveActiveCell(row, col);
                _ranges.Add(new CellRange(row, col, row, col));
                _shapedRangeIndex = _ranges.Count - 1;
                _anchorRow = _endRow = row;
                _anchorCol = _endCol = col;
                NormalizeAndNotify();
                return;
            }

            ClearRanges();
            MoveActiveCell(row, col);
            _anchorRow = _endRow = row;
            _anchorCol = _endCol = col;
            _shapedRangeIndex = -1;
        }

        /// <summary>The pointer moved (mouse captured) over (row, col) — reshape the range when the cell changed.</summary>
        public void UpdatePointer(int row, int col)
        {
            if (!_pointerActive || !IsEnabled) return;
            row = ClampRow(row);
            col = ClampCol(col);
            if (row == _endRow && col == _endCol) return;
            _pointerMoved = true;
            ShapeTo(row, col);
        }

        /// <summary>
        /// Left button released. A plain press that never moved to another cell is a click: its
        /// deferred cell action runs now (check column toggle, combo column editor).
        /// </summary>
        public void EndPointer() => EndPointerCore(runClickAction: true);

        private void EndPointerCore(bool runClickAction)
        {
            if (!_pointerActive) return;
            _pointerActive = false;
            StopAutoScroll();

            var canvas = _grid._canvas;
            if (canvas != null && canvas.IsMouseCaptured)
                canvas.ReleaseMouseCapture();

            if (runClickAction && !_pointerMoved && !_pointerHadModifiers && _pressRow >= 0)
                _grid._selection.RunCellClickAction(_pressRow, _pressCol, clickCount: 1);
        }

        /// <summary>Pointer lost its capture (e.g. window deactivated) — end the gesture without a click.</summary>
        public void CancelPointer() => EndPointerCore(runClickAction: false);

        // ── Mouse event handlers (wired by the grid on the data canvas) ─────────

        internal void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (!_pointerActive) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndPointer(); return; }
            UpdatePointerFromCanvasPoint(e.GetPosition(_grid._canvas));
        }

        internal void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_pointerActive) return;
            EndPointer();
            e.Handled = true;
        }

        internal void OnCanvasLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_pointerActive && !_grid._canvas.IsMouseCaptured)
                CancelPointer();
        }

        /// <summary>Maps a canvas point (may lie outside the canvas) to a cell and reshapes; starts/stops auto-scroll.</summary>
        internal void UpdatePointerFromCanvasPoint(Point p)
        {
            if (!_pointerActive) return;
            int row, col;
            if (TryMapPoint(p, out row, out col))
                UpdatePointer(row, col);

            if (IsOutsideViewport(p)) StartAutoScroll();
            else StopAutoScroll();
        }

        /// <summary>
        /// One auto-scroll step for a pointer at <paramref name="p"/> (canvas coordinates) outside
        /// the viewport: scrolls toward the pointer and extends the range to the cell now under it.
        /// </summary>
        internal void AutoScrollStep(Point p)
        {
            if (!_pointerActive) return;
            var canvas = _grid._canvas;
            if (canvas == null) return;

            double rowHeight = _grid.RowHeight > 0 ? _grid.RowHeight : 1;
            if (p.Y < 0)
                _grid.ScrollLinesForRangeDrag(-Math.Min(AutoScrollMaxLinesPerStep, 1 + (int)(-p.Y / rowHeight)));
            else if (p.Y > canvas.ActualHeight)
                _grid.ScrollLinesForRangeDrag(Math.Min(AutoScrollMaxLinesPerStep, 1 + (int)((p.Y - canvas.ActualHeight) / rowHeight)));

            if (p.X < 0)
                _grid.ScrollHorizontallyForRangeDrag(-AutoScrollHorizontalStep);
            else if (p.X > canvas.ActualWidth)
                _grid.ScrollHorizontallyForRangeDrag(AutoScrollHorizontalStep);

            int row, col;
            if (TryMapPoint(p, out row, out col))
                UpdatePointer(row, col);
        }

        private bool IsOutsideViewport(Point p)
        {
            var canvas = _grid._canvas;
            return canvas != null && (p.Y < 0 || p.Y > canvas.ActualHeight || p.X < 0 || p.X > canvas.ActualWidth);
        }

        private void StartAutoScroll()
        {
            if (_autoScrollTimer == null)
            {
                _autoScrollTimer = new DispatcherTimer(DispatcherPriority.Input, _grid.Dispatcher) { Interval = AutoScrollInterval };
                _autoScrollTimer.Tick += (s, e) =>
                {
                    if (!_pointerActive || _grid._canvas == null) { StopAutoScroll(); return; }
                    AutoScrollStep(Mouse.GetPosition(_grid._canvas));
                };
            }
            if (!_autoScrollTimer.IsEnabled) _autoScrollTimer.Start();
        }

        private void StopAutoScroll()
        {
            if (_autoScrollTimer != null && _autoScrollTimer.IsEnabled) _autoScrollTimer.Stop();
        }

        /// <summary>
        /// Cell under a canvas point, clamped to the data: above/below the viewport maps to the
        /// first/last rendered line, an info row maps to its owner data row, left/right of the
        /// columns maps to the first/last column.
        /// </summary>
        internal bool TryMapPoint(Point p, out int row, out int col)
        {
            row = col = -1;
            var canvas = _grid._canvas;
            var engine = _grid._rowEngine;
            if (canvas == null || !engine.IsInitialized || RowCount == 0) return false;

            double height = Math.Max(1, canvas.ActualHeight);
            double y = Math.Max(0, Math.Min(height - 0.5, p.Y));
            int line = _grid._selection.LineAtCanvasY(y);
            line = Math.Max(0, Math.Min(engine.TotalLines - 1, line));
            row = ClampRow(engine.LineToSorted(line));   // info line → owner data row

            var columns = _grid.GetVisibleColumnList();
            double width = Math.Max(1, canvas.ActualWidth);
            double xInData = Math.Max(0, Math.Min(width - 0.5, p.X)) + _grid._hOffset;
            col = columns.Count - 1;
            double cumX = 0;
            for (int i = 0; i < columns.Count; i++)
            {
                cumX += columns[i].Width;
                if (xInData < cumX) { col = i; break; }
            }
            return col >= 0;
        }

        // -----------------------------------------------------------------------
        // Rendering
        // -----------------------------------------------------------------------

        /// <summary>Re-applies the highlight to the visible rows (call after theme / brush changes).</summary>
        public void RefreshHighlight()
        {
            if (!_grid._rowEngine.IsInitialized) return;
            if (_ranges.Count == 0)
                _grid._rowEngine.SetRangeHighlight(null, null);
            else
                _grid._rowEngine.SetRangeHighlight(Contains, ResolveRangeBrush());
        }

        /// <summary>
        /// <c>RangeSelectionBrush</c> when set; otherwise the selection brush blended halfway into the
        /// row background — clearly lighter than the selection in Light, subdued in Dark.
        /// </summary>
        internal Brush ResolveRangeBrush()
        {
            if (_grid.RangeSelectionBrush != null) return _grid.RangeSelectionBrush;
            var rowBackground = _grid.RowBackground ?? ThemePalette.Light.RowBackground;
            var selection     = _grid.SelectionBrush ?? ThemePalette.Light.SelectionBrush;
            return BrushShift.Blend(rowBackground, selection, 0.45);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private void EnsureAnchor()
        {
            if (_ranges.Count > 0 && _anchorRow >= 0 && _anchorCol >= 0) return;
            _anchorRow = ClampRow(_grid.FocusedRowSortedIndex);
            _anchorCol = ClampCol(_grid.FocusedColIndex);
            _endRow = _anchorRow;
            _endCol = _anchorCol;
            _shapedRangeIndex = -1;
        }

        /// <summary>Moves the moving corner to (row, col) and reshapes the range of the current gesture.</summary>
        private void ShapeTo(int row, int col)
        {
            _endRow = row;
            _endCol = col;
            var range = new CellRange(_anchorRow, _anchorCol, row, col);

            if (_shapedRangeIndex < 0 || _shapedRangeIndex >= _ranges.Count)
            {
                // Shift on an existing selection replaces its last range (Excel's "active range").
                if (_ranges.Count > 0) _shapedRangeIndex = _ranges.Count - 1;
                else { _ranges.Add(range); _shapedRangeIndex = 0; NormalizeAndNotify(); return; }
            }
            _ranges[_shapedRangeIndex] = range;
            NormalizeAndNotify();
        }

        /// <summary>A lone single-cell range on the active cell is the same as no range.</summary>
        private void NormalizeAndNotify()
        {
            if (_ranges.Count == 1 && _ranges[0].IsSingleCell
                && _ranges[0].FirstRow == _grid.FocusedRowSortedIndex
                && _ranges[0].FirstColumn == _grid.FocusedColIndex)
            {
                _ranges.Clear();
                _shapedRangeIndex = -1;
            }
            NotifyIfRangesChanged();
        }

        private void NotifyIfRangesChanged()
        {
            if (RangesMatchLastNotified()) return;
            _notifiedRanges = _ranges.ToArray();
            RefreshHighlight();
            _grid.RaiseRangeSelectionChanged();
        }

        private bool RangesMatchLastNotified()
        {
            if (_ranges.Count != _notifiedRanges.Length) return false;
            for (int i = 0; i < _ranges.Count; i++)
                if (_ranges[i] != _notifiedRanges[i]) return false;
            return true;
        }

        /// <summary>
        /// The active cell is about to move. A move made by something other than a range gesture
        /// (keyboard navigation, <c>SelectedItem</c> from the view model, <c>ScrollToItem</c>,
        /// <c>SelectColumn</c>) leaves the ranges and their anchor stale, so they are cleared.
        /// </summary>
        internal void OnActiveCellMoving()
        {
            if (!_movingActiveCell) Clear();
        }

        /// <summary>Moves the active cell as part of a range gesture, keeping the ranges.</summary>
        private void MoveActiveCell(int row, int col)
        {
            _movingActiveCell = true;
            try { _grid.ApplyCellFocus(row, col, false); }
            finally { _movingActiveCell = false; }
        }

        private int ClampRow(int row) => Math.Max(0, Math.Min(RowCount - 1, row));
        private int ClampCol(int col) => Math.Max(0, Math.Min(ColumnCount - 1, col));
    }
}
