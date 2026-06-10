using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Open.DataGrid.Columns;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// Manages header cell creation, layout, resize thumbs, DPD subscriptions for
    /// sort-direction and column-visibility changes, column-list caching, and
    /// hidden-columns logic for <see cref="OpenDataGrid"/>.
    /// </summary>
    internal sealed class HeaderPresenter
    {
        private const int SortIconDefaultFontSize = 9;
        private const int ResizeThumbWidth = 4;

        // Frozen fallback used when HeaderBackground is null (Light theme default = #F0F0F0).
        // Shared across all instances and calls — avoids per-cell allocations in the update loop.
        internal static readonly Brush DefaultHeaderBackground = CreateFrozen(Color.FromRgb(240, 240, 240));

        private static SolidColorBrush CreateFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private readonly OpenDataGrid _grid;

        // -----------------------------------------------------------------------
        // State (previously in OpenDataGrid fields)
        // -----------------------------------------------------------------------

        private Canvas _headerCanvas;

        private readonly List<HeaderVisual> _headerCells = new List<HeaderVisual>();

        private List<OpenDataGridColumn> _cachedAllColumnList;
        private List<OpenDataGridColumn> _cachedVisibleColumnList;

        private bool _suppressColumnVisibilityRefresh;

        private readonly HiddenColumnsTracker _hiddenTracker = new HiddenColumnsTracker();

        private readonly List<DpdSubscription> _dpdSubscriptions  = new List<DpdSubscription>();
        private readonly List<DpdSubscription> _visibilitySubscriptions = new List<DpdSubscription>();

        // Drag-to-reorder state
        private Point?          _dragStartPoint;
        private int             _dragSourceIndex;
        private int             _dragTargetIndex;
        private bool            _isDragging;
        private Border          _dragBorder;
        private Rectangle       _dropIndicator;
        private KeyEventHandler _escapeHandler;

        /// <summary>
        /// Brush used for the column-reorder drop indicator rectangle.
        /// <c>null</c> reverts to the built-in DodgerBlue default.
        /// Set by the theme engine when <see cref="OpenDataGrid.Theme"/> changes.
        /// </summary>
        public Brush DropIndicatorBrush { get; set; }

        // -----------------------------------------------------------------------
        // Inner types
        // -----------------------------------------------------------------------

        private sealed class HeaderVisual
        {
            public Border     Border;
            public TextBlock  HeaderText;
            public TextBlock  SortIcon;
            public StackPanel TextPanel;
        }

        private struct DpdSubscription
        {
            public readonly System.ComponentModel.DependencyPropertyDescriptor Dpd;
            public readonly object      Target;
            public readonly EventHandler Handler;
            public DpdSubscription(
                System.ComponentModel.DependencyPropertyDescriptor dpd,
                object target, EventHandler handler)
            { Dpd = dpd; Target = target; Handler = handler; }
        }

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------

        public HeaderPresenter(OpenDataGrid grid)
        {
            _grid = grid;
        }

        // -----------------------------------------------------------------------
        // Template wiring
        // -----------------------------------------------------------------------

        /// <summary>
        /// Called at the <em>start</em> of <c>OnApplyTemplate</c> to remove DPD
        /// subscriptions that reference the old template's column objects.
        /// Must be called before <c>base.OnApplyTemplate()</c>.
        /// </summary>
        public void DetachVisibilitySubscriptions()
        {
            foreach (var s in _visibilitySubscriptions)
                s.Dpd.RemoveValueChanged(s.Target, s.Handler);
            _visibilitySubscriptions.Clear();
        }

        /// <summary>Called from <c>OnApplyTemplate</c> after the canvas reference is acquired.</summary>
        public void SetHeaderCanvas(Canvas canvas)
        {
            _headerCanvas = canvas;
        }

        // -----------------------------------------------------------------------
        // Column list cache
        // -----------------------------------------------------------------------

        public void InvalidateColumnCaches()
        {
            _cachedAllColumnList    = null;
            _cachedVisibleColumnList = null;
        }

        public IReadOnlyList<OpenDataGridColumn> GetAllColumnList()
        {
            var cols = _grid.Columns;
            if (cols == null) return Array.Empty<OpenDataGridColumn>();
            return _cachedAllColumnList ?? (_cachedAllColumnList = cols.ToList());
        }

        public IReadOnlyList<OpenDataGridColumn> GetVisibleColumnList()
        {
            if (_cachedVisibleColumnList != null) return _cachedVisibleColumnList;
            _cachedVisibleColumnList = GetAllColumnList().Where(c => c.IsVisible).ToList();
            return _cachedVisibleColumnList;
        }

        // -----------------------------------------------------------------------
        // Header lifecycle
        // -----------------------------------------------------------------------

        /// <summary>
        /// Rebuilds all header cells from scratch.
        /// <paramref name="onHeaderClick"/> is the sort-trigger handler on the grid.
        /// </summary>
        public void BuildHeaders(MouseButtonEventHandler onHeaderClick)
        {
            // If a drag was in progress, clean it up — the border we're dragging from
            // is about to be discarded. Leaves no dangling cursor, capture or escape handler.
            AbortDragIfActive();

            // Remove DPD subscriptions from previous build to prevent handler accumulation.
            foreach (var s in _dpdSubscriptions)
                s.Dpd.RemoveValueChanged(s.Target, s.Handler);
            _dpdSubscriptions.Clear();

            if (_headerCanvas == null) return;

            _headerCanvas.Children.Clear();
            _headerCells.Clear();

            var columns = GetVisibleColumnList();
            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                var headerVisual = CreateHeaderCell(col, i, onHeaderClick);
                _headerCells.Add(headerVisual);
                _headerCanvas.Children.Add(headerVisual.Border);
            }
            LayoutHeaders();
        }

        private HeaderVisual CreateHeaderCell(OpenDataGridColumn col, int colIndex, MouseButtonEventHandler onHeaderClick)
        {
            var sortIcon = new TextBlock
            {
                FontSize          = SortIconDefaultFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(2, 0, 2, 0),
                Text              = string.Empty
            };

            var headerText = new TextBlock
            {
                Text              = col.Header,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight        = FontWeights.SemiBold,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                Margin            = new Thickness(4, 0, 0, 0)
            };

            ApplyHeaderFontDefaults(headerText, sortIcon);

            var textPanel = new StackPanel
            {
                Orientation          = Orientation.Horizontal,
                VerticalAlignment    = VerticalAlignment.Center,
                HorizontalAlignment  = col.HeaderHorizontalAlignment ?? _grid.HeaderHorizontalAlignment,
                Children             = { headerText, sortIcon }
            };

            // Resize thumb on right edge.
            var resizeThumb = new Thumb
            {
                Width                = ResizeThumbWidth,
                Cursor               = Cursors.SizeWE,
                HorizontalAlignment  = HorizontalAlignment.Right,
                Background           = Brushes.Transparent,
                Opacity              = 0
            };

            var capturedCol = col;
            resizeThumb.DragDelta += (s, ev) =>
            {
                double newWidth = capturedCol.Width + ev.HorizontalChange;
                newWidth = Math.Max(capturedCol.MinWidth, Math.Min(capturedCol.MaxWidth, newWidth));
                capturedCol.Width = newWidth;
                OnColumnWidthChanged();
            };

            var innerGrid = new Grid();
            innerGrid.Children.Add(textPanel);
            innerGrid.Children.Add(resizeThumb);

            var border = new Border
            {
                Height           = _grid.HeaderHeight,
                Background       = _grid.HeaderBackground ?? new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                BorderBrush      = _grid.GetEffectiveColumnSeparatorBrush(),
                BorderThickness  = new Thickness(0, 0, 1, 1),
                Child            = innerGrid,
                Tag              = colIndex
            };

            // Sort trigger is attached in AttachReorderHandlers (on MouseUp without drag)
            // to avoid firing sort when the user is about to drag the header for reorder.
            AttachReorderHandlers(border, colIndex, col.IsSortable ? onHeaderClick : null);

            // Monitor SortDirection changes via DependencyPropertyDescriptor.
            var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                OpenDataGridColumn.SortDirectionProperty, typeof(OpenDataGridColumn));
            if (dpd != null)
            {
                EventHandler sortDirHandler = (s, e) => UpdateHeaderSortIcon(sortIcon, col.SortDirection);
                dpd.AddValueChanged(col, sortDirHandler);
                _dpdSubscriptions.Add(new DpdSubscription(dpd, col, sortDirHandler));
            }

            var headerVisual = new HeaderVisual { Border = border, HeaderText = headerText, SortIcon = sortIcon, TextPanel = textPanel };

            var dpdHeader = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                OpenDataGridColumn.HeaderHorizontalAlignmentProperty, typeof(OpenDataGridColumn));
            if (dpdHeader != null)
            {
                EventHandler headerAlignHandler = (s, e) => textPanel.HorizontalAlignment = col.HeaderHorizontalAlignment ?? _grid.HeaderHorizontalAlignment;
                dpdHeader.AddValueChanged(col, headerAlignHandler);
                _dpdSubscriptions.Add(new DpdSubscription(dpdHeader, col, headerAlignHandler));
            }

            var dpdFontSize = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                OpenDataGridColumn.FontSizeProperty, typeof(OpenDataGridColumn));
            if (dpdFontSize != null)
            {
                EventHandler fontSizeHandler = (s, e) => _grid.InvalidateColumnFont(col);
                dpdFontSize.AddValueChanged(col, fontSizeHandler);
                _dpdSubscriptions.Add(new DpdSubscription(dpdFontSize, col, fontSizeHandler));
            }

            return headerVisual;
        }

        private void ApplyHeaderFontDefaults(TextBlock headerText, TextBlock sortIcon)
        {
            if (_grid.HeaderFontFamily != null)
            {
                headerText.FontFamily = _grid.HeaderFontFamily;
                sortIcon.FontFamily   = _grid.HeaderFontFamily;
            }
            else
            {
                headerText.ClearValue(TextBlock.FontFamilyProperty);
                sortIcon.ClearValue(TextBlock.FontFamilyProperty);
            }

            if (_grid.HeaderFontSize.HasValue)
            {
                headerText.FontSize = _grid.HeaderFontSize.Value;
                sortIcon.FontSize   = _grid.HeaderFontSize.Value;
            }
            else
            {
                headerText.ClearValue(TextBlock.FontSizeProperty);
                sortIcon.FontSize = SortIconDefaultFontSize;
            }

            headerText.FontWeight = _grid.HeaderFontWeight;

            if (_grid.HeaderForeground != null)
            {
                headerText.Foreground = _grid.HeaderForeground;
                sortIcon.Foreground   = _grid.HeaderForeground;
            }
            else
            {
                headerText.ClearValue(TextBlock.ForegroundProperty);
                sortIcon.ClearValue(TextBlock.ForegroundProperty);
            }
        }

        public void ApplyHeaderFontDefaultsToExistingHeaders()
        {
            if (_headerCells.Count == 0) return;
            for (int i = 0; i < _headerCells.Count; i++)
            {
                var h = _headerCells[i];
                ApplyHeaderFontDefaults(h.HeaderText, h.SortIcon);
                h.Border.Background = _grid.HeaderBackground ?? DefaultHeaderBackground;
            }
        }

        public void ApplyHeaderHorizontalAlignmentToExistingHeaders()
        {
            if (_headerCells.Count == 0) return;
            var visible = GetVisibleColumnList();
            for (int i = 0; i < _headerCells.Count && i < visible.Count; i++)
                _headerCells[i].TextPanel.HorizontalAlignment = visible[i].HeaderHorizontalAlignment ?? _grid.HeaderHorizontalAlignment;
        }

        public void UpdateColumnSeparatorBrush()
        {
            if (_headerCells.Count == 0) return;
            var brush = _grid.GetEffectiveColumnSeparatorBrush();
            for (int i = 0; i < _headerCells.Count; i++)
                _headerCells[i].Border.BorderBrush = brush;
        }

        private void OnColumnWidthChanged()
        {
            _grid.BuildColumnVirtualization();
            LayoutHeaders();
            _grid.UpdateColumnRange();
            _grid.LayoutFilterRow();
        }

        private static void UpdateHeaderSortIcon(TextBlock icon, Models.SortDirection dir)
        {
            icon.Text = dir == Models.SortDirection.Ascending  ? " ▲"
                      : dir == Models.SortDirection.Descending ? " ▼"
                      : string.Empty;
        }

        public void LayoutHeaders()
        {
            if (_headerCanvas == null) return;
            var columns = GetVisibleColumnList();
            double offset = -_grid._hOffset;
            for (int i = 0; i < _headerCells.Count && i < columns.Count; i++)
            {
                var hc = _headerCells[i].Border;
                Canvas.SetLeft(hc, offset);
                hc.Width = columns[i].Width;
                offset  += columns[i].Width;
            }
        }

        // -----------------------------------------------------------------------
        // Column-visibility subscriptions
        // -----------------------------------------------------------------------

        public void RebuildColumnVisibilitySubscriptions()
        {
            foreach (var s in _visibilitySubscriptions)
                s.Dpd.RemoveValueChanged(s.Target, s.Handler);
            _visibilitySubscriptions.Clear();

            var descriptorForIsVisible = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                OpenDataGridColumn.IsVisibleProperty, typeof(OpenDataGridColumn));
            if (descriptorForIsVisible == null) return;

            foreach (var col in GetAllColumnList())
            {
                EventHandler handler = OnColumnVisibilityPropertyChanged;
                descriptorForIsVisible.AddValueChanged(col, handler);
                _visibilitySubscriptions.Add(new DpdSubscription(descriptorForIsVisible, col, handler));
            }
        }

        private void OnColumnVisibilityPropertyChanged(object sender, EventArgs e)
        {
            if (_suppressColumnVisibilityRefresh) return;
            RefreshAfterColumnVisibilityChanged();
        }

        // -----------------------------------------------------------------------
        // Hidden columns
        // -----------------------------------------------------------------------

        public void ApplyHiddenColumns()
        {
            InvalidateColumnCaches();
            ApplyHiddenColumnsToColumns();
        }

        public void ApplyHiddenColumnsToColumns()
        {
            var columns = GetAllColumnList();
            if (columns.Count == 0) return;

            bool changed;
            _suppressColumnVisibilityRefresh = true;
            try
            {
                changed = _hiddenTracker.Apply(columns, _grid.HiddenColumns, _grid.HiddenColumnsByHeader);
            }
            finally
            {
                _suppressColumnVisibilityRefresh = false;
            }

            if (changed || !_grid.IsGridInitialized)
                RefreshAfterColumnVisibilityChanged();
        }

        // -----------------------------------------------------------------------
        // Full refresh after visibility/structure change
        // -----------------------------------------------------------------------

        public void RefreshAfterColumnVisibilityChanged()
        {
            InvalidateColumnCaches();

            var visible  = GetVisibleColumnList();
            int colCount = visible.Count;

            if (colCount == 0)
            {
                foreach (var s in _dpdSubscriptions)
                    s.Dpd.RemoveValueChanged(s.Target, s.Handler);
                _dpdSubscriptions.Clear();

                _headerCanvas?.Children.Clear();
                _headerCells.Clear();
                _grid._canvas?.Children.Clear();
                _grid._colEngine.Rebuild(new List<OpenDataGridColumn>());
                _grid._hOffset = 0;
                if (_grid._hScrollBar != null) { _grid._hScrollBar.Maximum = 0; _grid._hScrollBar.Value = 0; }
                _grid._navEngine?.UpdateStats(0, 0);
                _grid.FocusedColumnIndex = -1;
                _grid.FocusedColumnHeader = string.Empty;
                _grid.RebuildFilterRow();
                return;
            }

            _grid.BuildPropertyAccessors();
            _grid.BuildColumnVirtualization();
            BuildHeaders(_grid.OnHeaderCellClick);
            _grid.RebuildFilterRow();

            if (_grid._hScrollBar != null)
            {
                _grid._hOffset = Math.Max(0, Math.Min(_grid._hScrollBar.Maximum, _grid._hOffset));
                _grid._hScrollBar.Value = _grid._hOffset;
            }

            if (_grid.IsGridInitialized && _grid._canvas != null && _grid.DataSource != null && _grid.DataSource.Count > 0)
            {
                _grid.InitializeRowEngine();
                _grid.InitializeNavEngine();
                _grid.UpdateColumnRange();
            }

            int savedRow = _grid.FocusedRowSortedIndex;
            int savedCol = Math.Max(0, Math.Min(colCount - 1, _grid.FocusedColIndex));

            if (savedRow >= 0)
            {
                _grid.ApplyCellFocus(savedRow, savedCol, false);
            }
            else
            {
                _grid.FocusedColumnIndex  = savedCol;
                _grid.FocusedColumnHeader = visible[savedCol].Header ?? string.Empty;
            }
        }

        // -----------------------------------------------------------------------
        // Column drag-to-reorder
        // -----------------------------------------------------------------------

        private void AttachReorderHandlers(Border border, int colIndex, MouseButtonEventHandler onHeaderClick)
        {
            border.PreviewMouseLeftButtonDown += (s, e) => OnHeaderMouseDown(border, colIndex, e);
            border.PreviewMouseMove           += (s, e) => OnHeaderMouseMove(border, e);
            border.PreviewMouseLeftButtonUp   += (s, e) => OnHeaderMouseUp(border, onHeaderClick, e);
            border.LostMouseCapture           += (s, e) => OnHeaderLostCapture(border);
        }

        private void OnHeaderMouseDown(Border border, int colIndex, MouseButtonEventArgs e)
        {
            // Skip if the click went to the resize thumb (or any of its template children) —
            // thumb has its own drag behavior for column resize.
            if (IsInsideThumb(e.OriginalSource)) return;
            _dragStartPoint  = e.GetPosition(_headerCanvas);
            _dragSourceIndex = colIndex;
            _dragBorder      = border;
        }

        private void OnHeaderMouseMove(Border border, MouseEventArgs e)
        {
            if (_dragStartPoint == null || !_grid.AllowColumnReorder) return;
            // Another element (e.g. resize Thumb) has mouse capture — do not start reorder.
            if (Mouse.Captured != null && !ReferenceEquals(Mouse.Captured, border)) return;

            var current = e.GetPosition(_headerCanvas);
            double dx = current.X - _dragStartPoint.Value.X;

            if (!_isDragging && Math.Abs(dx) > SystemParameters.MinimumHorizontalDragDistance)
            {
                _isDragging = true;

                // Create indicator BEFORE CaptureMouse: CaptureMouse triggers MouseDevice.Synchronize()
                // which re-fires PreviewMouseMove re-entrantly. Null _dropIndicator at that point
                // would cause Canvas.SetLeft to throw ArgumentNullException.
                _dropIndicator = new Rectangle
                {
                    Width            = 2,
                    Height           = _grid.HeaderHeight,
                    Fill             = DropIndicatorBrush ?? Brushes.DodgerBlue,
                    IsHitTestVisible = false
                };
                _headerCanvas.Children.Add(_dropIndicator);
                Panel.SetZIndex(_dropIndicator, 999);

                border.CaptureMouse();
                border.Cursor = Cursors.Hand;

                _escapeHandler = (ks, ke) =>
                {
                    if (ke.Key == Key.Escape) { ke.Handled = true; border.ReleaseMouseCapture(); }
                };
                _grid.AddHandler(UIElement.PreviewKeyDownEvent, _escapeHandler, true);
            }

            if (_isDragging)
            {
                _dragTargetIndex = ComputeDropIndex(current.X);
                Canvas.SetLeft(_dropIndicator, ComputeDropIndicatorX(_dragTargetIndex) - 1);
                e.Handled = true;
            }
        }

        private void OnHeaderMouseUp(Border border, MouseButtonEventHandler onHeaderClick, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                int src = _dragSourceIndex;
                int tgt = _dragTargetIndex;
                EndDrag(border);
                PerformReorder(src, tgt);
                e.Handled = true;
                return;
            }

            // No drag — treat as a click and fire sort if column is sortable.
            // Require that MouseDown occurred on THIS border (resize thumb causes MouseUp to
            // bubble here without a prior MouseDown tracking entry).
            bool hadMouseDown = _dragStartPoint != null && ReferenceEquals(_dragBorder, border);
            _dragStartPoint = null;
            _dragBorder     = null;
            if (!hadMouseDown || onHeaderClick == null || IsInsideThumb(e.OriginalSource)) return;
            // Cancel click if the mouse was released outside the border (standard button UX).
            var p = e.GetPosition(border);
            if (p.X < 0 || p.Y < 0 || p.X > border.ActualWidth || p.Y > border.ActualHeight) return;
            onHeaderClick(border, e);
        }

        private void OnHeaderLostCapture(Border border)
        {
            if (!_isDragging) { _dragStartPoint = null; return; }
            EndDrag(border);
            // No PerformReorder: capture loss without LMB-up = cancelled drag (e.g. Escape).
        }

        private void AbortDragIfActive()
        {
            if (_dragBorder == null && _dropIndicator == null && _escapeHandler == null && !_isDragging)
                return;
            if (_dragBorder != null)
            {
                if (_dragBorder.IsMouseCaptured) _dragBorder.ReleaseMouseCapture();
                _dragBorder.Cursor = null;
            }
            _isDragging     = false;
            _dragStartPoint = null;
            _dragBorder     = null;
            RemoveDropIndicator();
            if (_escapeHandler != null)
            {
                _grid.RemoveHandler(UIElement.PreviewKeyDownEvent, _escapeHandler);
                _escapeHandler = null;
            }
        }

        private void EndDrag(Border border)
        {
            _isDragging      = false;
            _dragStartPoint  = null;
            _dragBorder      = null;
            border.Cursor    = null;

            RemoveDropIndicator();

            if (_escapeHandler != null)
            {
                _grid.RemoveHandler(UIElement.PreviewKeyDownEvent, _escapeHandler);
                _escapeHandler = null;
            }
        }

        private void RemoveDropIndicator()
        {
            if (_dropIndicator != null)
            {
                _headerCanvas?.Children.Remove(_dropIndicator);
                _dropIndicator = null;
            }
        }

        private int ComputeDropIndex(double mouseCanvasX)
        {
            var visible   = GetVisibleColumnList();
            double contentX = mouseCanvasX + _grid._hOffset;
            double cumWidth = 0;
            for (int i = 0; i < visible.Count; i++)
            {
                if (contentX <= cumWidth + visible[i].Width / 2.0)
                    return i;
                cumWidth += visible[i].Width;
            }
            return visible.Count;
        }

        private double ComputeDropIndicatorX(int dropIndex)
        {
            var visible   = GetVisibleColumnList();
            double cumWidth = 0;
            for (int i = 0; i < dropIndex && i < visible.Count; i++)
                cumWidth += visible[i].Width;
            return cumWidth - _grid._hOffset;
        }

        /// <summary>
        /// Reorders a visible column from <paramref name="srcVisible"/> to the insertion
        /// point <paramref name="tgtVisible"/> (0 = before first, N = after last).
        /// </summary>
        internal void PerformReorder(int srcVisible, int tgtVisible)
        {
            var visible = GetVisibleColumnList();
            if (visible.Count < 2) return;
            if ((uint)srcVisible >= (uint)visible.Count) return;
            if ((uint)tgtVisible > (uint)visible.Count) return;
            if (srcVisible == tgtVisible || srcVisible + 1 == tgtVisible) return;

            // Capture focused column reference so we can remap after rebuild.
            int savedRow = _grid.FocusedRowSortedIndex;
            OpenDataGridColumn focusedCol = null;
            int fc = _grid.FocusedColIndex;
            if (fc >= 0 && fc < visible.Count)
                focusedCol = visible[fc];

            var srcCol = visible[srcVisible];
            var cols   = _grid.Columns;

            // Find full-list indices.
            int srcFull = FindFullIndex(cols, srcCol);

            bool insertAtEnd = tgtVisible >= visible.Count;
            int anchorFull;
            if (insertAtEnd)
            {
                anchorFull = cols.Count;
            }
            else
            {
                anchorFull = FindFullIndex(cols, visible[tgtVisible]);
            }

            System.Diagnostics.Debug.Assert(srcFull >= 0, "srcCol missing from Columns — visible list out of sync");
            System.Diagnostics.Debug.Assert(anchorFull >= 0, "anchor column missing from Columns");
            if (srcFull < 0 || anchorFull < 0) return;

            _suppressColumnVisibilityRefresh = true;
            try
            {
                cols.RemoveAt(srcFull);
                // After removal, indices > srcFull shift by -1.
                int insertAt = insertAtEnd
                    ? cols.Count
                    : anchorFull > srcFull ? anchorFull - 1 : anchorFull;
                cols.Insert(insertAt, srcCol);
            }
            finally
            {
                _suppressColumnVisibilityRefresh = false;
            }

            RefreshAfterColumnVisibilityChanged();

            // Remap focused column to its new visible index.
            if (savedRow >= 0 && focusedCol != null)
            {
                var newVisible = GetVisibleColumnList();
                int newCol = 0;
                for (int i = 0; i < newVisible.Count; i++)
                    if (ReferenceEquals(newVisible[i], focusedCol)) { newCol = i; break; }
                _grid.ApplyCellFocus(savedRow, newCol, false);
            }
        }

        private static bool IsInsideThumb(object source)
        {
            var d = source as DependencyObject;
            while (d != null)
            {
                if (d is Thumb) return true;
                // Walk up logical (template) tree — OriginalSource is often a template-internal element.
                DependencyObject parent = null;
                if (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
                    parent = VisualTreeHelper.GetParent(d);
                if (parent == null && d is FrameworkElement fe)
                    parent = fe.TemplatedParent ?? fe.Parent;
                if (parent == null && d is FrameworkContentElement fce)
                    parent = fce.Parent;
                d = parent;
            }
            return false;
        }

        private static int FindFullIndex(System.Windows.FreezableCollection<OpenDataGridColumn> cols, OpenDataGridColumn target)
        {
            for (int i = 0; i < cols.Count; i++)
                if (ReferenceEquals(cols[i], target)) return i;
            return -1;
        }
    }
}
