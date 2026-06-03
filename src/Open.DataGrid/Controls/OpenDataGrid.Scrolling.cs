using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Open.DataGrid.Controls
{
    public partial class OpenDataGrid
    {
        #region Scrolling

        private void OnVerticalScroll(object sender, ScrollEventArgs e)
        {
            if (_suppressScrollEvents) return;
            int firstVisible = (int)_vScrollBar.Value;
#if DEBUG
            Debug.WriteLine($"[EDG] OnVerticalScroll: firstVisible={firstVisible}");
#endif
            _rowEngine.ScrollTo(firstVisible);
        }

        private void OnHorizontalScroll(object sender, ScrollEventArgs e)
        {
            _hOffset = _hScrollBar.Value;
            UpdateColumnRange();
            LayoutHeaders();
            LayoutFilterRow();
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Height <= 0 || e.NewSize.Width <= 0) return;

            bool hasData = DataSource != null && DataSource.Count > 0;
            if (!_lifecycle.CanHandleInput && (hasData || ShowEmptyPlaceholderRow))
            {
                // First real layout pass — canvas now has a size, initialize fully.
                // Also triggered when ShowEmptyPlaceholderRow is on and data is absent.
                TryInitialize();
                return;
            }

            if (!_lifecycle.CanHandleInput) return;

            // DataSource changed while grid was hidden (canvas had no size).
            // Full re-init now that dimensions are available — rebuilds sort engine,
            // property accessors and row pool in the correct order (same as TryInitialize).
            if (_lifecycle.NeedsViewport)
            {
                TryInitialize();
                return;
            }

            _rowEngine.Resize(e.NewSize.Height);
            UpdateScrollBarRange();
            UpdateHScrollBar();
            UpdateColumnRange();
            LayoutHeaders();
            LayoutFilterRow();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_vScrollBar == null) return;
            double delta = e.Delta > 0 ? -MouseWheelRowsPerNotch : MouseWheelRowsPerNotch;
            double newVal = Math.Max(_vScrollBar.Minimum,
                Math.Min(_vScrollBar.Maximum, _vScrollBar.Value + delta));
            _vScrollBar.Value = newVal;
            _rowEngine.ScrollTo((int)newVal);
            e.Handled = true;
        }

        private void UpdateScrollBarRange()
        {
            if (_vScrollBar == null) return;

            int visible = _rowEngine.GetVisibleRowCount();
            int viewportRows = Math.Max(1, visible);
            int maxFirstVisible = _rowEngine.GetMaxFirstVisibleIndex();
#if DEBUG
            Debug.WriteLine($"[EDG] UpdateScrollBarRange: maxFirst={maxFirstVisible} viewportRows={viewportRows} valBefore={_vScrollBar.Value} maxBefore={_vScrollBar.Maximum}");
#endif
            _suppressScrollEvents = true;
            _vScrollBar.Minimum = 0;
            _vScrollBar.Maximum = maxFirstVisible;
            _vScrollBar.ViewportSize = viewportRows;
            _vScrollBar.LargeChange = viewportRows;
            _vScrollBar.SmallChange = 1;

            if (_vScrollBar.Value > _vScrollBar.Maximum)
                _vScrollBar.Value = _vScrollBar.Maximum;
#if DEBUG
            Debug.WriteLine($"[EDG] UpdateScrollBarRange: valAfter={_vScrollBar.Value} maxAfter={_vScrollBar.Maximum}");
#endif
            _suppressScrollEvents = false;
        }

        private int GetNavigationVisibleRows()
        {
            // Navigation/page step should always move at least one row,
            // even when viewport is too small to fit a full row.
            return Math.Max(1, _rowEngine.GetVisibleRowCount());
        }

        internal void UpdateHScrollBar()
        {
            if (_hScrollBar == null) return;
            double totalWidth = _colEngine.TotalWidth;
            double viewport = _canvas?.ActualWidth ?? 0;
            _hScrollBar.Minimum = 0;
            _hScrollBar.Maximum = Math.Max(0, totalWidth - viewport);
            _hScrollBar.ViewportSize = viewport;
            _hScrollBar.LargeChange = viewport;
            _hScrollBar.SmallChange = HorizontalScrollSmallChange;
            _hScrollBar.Visibility = totalWidth > viewport ? Visibility.Visible : Visibility.Collapsed;
        }

        internal void UpdateColumnRange()
        {
            if (!_lifecycle.CanHandleInput || _canvas == null) return;
            double viewportWidth = _canvas.ActualWidth;
            _colEngine.GetVisibleColumnRange(_hOffset, viewportWidth, out int firstVis, out int lastVis);
            var widths = _colEngine.GetColumnWidths();
            var offsets = _colEngine.GetColumnOffsets();
            _rowEngine.UpdateColumnRange(firstVis, lastVis, _hOffset, widths, offsets);
            _rowEngine.ScrollTo(_rowEngine.GetFirstVisibleIndex()); // refresh
        }

        #endregion
    }
}
