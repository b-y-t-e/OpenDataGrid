using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Open.DataGrid.Columns;
using Open.DataGrid.Filtering;

namespace Open.DataGrid.Controls
{
    internal sealed class FilterRowPresenter
    {
        private static readonly SolidColorBrush DefaultFilterableBackground = CreateFrozen(Colors.White);
        private static readonly SolidColorBrush DefaultInertBackground      = CreateFrozen(Color.FromRgb(0xF2, 0xF2, 0xF2));

        private static SolidColorBrush CreateFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private readonly OpenDataGrid _grid;
        private Canvas _filterCanvas;
        private FrameworkElement _filterContainer;

        private readonly List<FilterCellInfo> _cells = new List<FilterCellInfo>();
        private readonly List<DpdSubscription> _filteringEnabledSubscriptions = new List<DpdSubscription>();

        private struct FilterCellInfo
        {
            public Border CellBorder;
            public IColumnFilterController Controller;
        }

        private struct DpdSubscription
        {
            public readonly System.ComponentModel.DependencyPropertyDescriptor Dpd;
            public readonly object Target;
            public readonly EventHandler Handler;
            public DpdSubscription(System.ComponentModel.DependencyPropertyDescriptor dpd, object target, EventHandler handler)
            { Dpd = dpd; Target = target; Handler = handler; }
        }

        public FilterRowPresenter(OpenDataGrid grid)
        {
            _grid = grid;
        }

        public bool IsKeyboardFocusWithin => _filterCanvas?.IsKeyboardFocusWithin == true;

        public void SetTemplateParts(FrameworkElement container, Canvas canvas)
        {
            _filterContainer = container;
            _filterCanvas    = canvas;
        }

        public void RebuildCells()
        {
            DetachAllControllers();
            ClearFilteringEnabledSubscriptions();

            var allColumns = _grid._headerPresenter.GetAllColumnList();

            var dpdForFilteringEnabled = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                OpenDataGridColumn.FilteringEnabledProperty, typeof(OpenDataGridColumn));

            foreach (var col in allColumns)
            {
                if (dpdForFilteringEnabled != null)
                {
                    EventHandler handler = OnFilteringEnabledChanged;
                    dpdForFilteringEnabled.AddValueChanged(col, handler);
                    _filteringEnabledSubscriptions.Add(new DpdSubscription(dpdForFilteringEnabled, col, handler));
                }
            }

            var columns = _grid._headerPresenter.GetVisibleColumnList();

            bool anyEnabled = false;
            foreach (var col in columns)
                if (col.FilteringEnabled) { anyEnabled = true; break; }

            UpdateContainerVisibility(anyEnabled);

            if (_filterCanvas == null) return;
            _filterCanvas.Children.Clear();
            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                IColumnFilterController controller = null;

                if (col.FilteringEnabled)
                {
                    controller = col.CreateFilterController(_grid);
                    controller?.Attach();
                    FilterKeyboardNavigation.AttachTo(controller?.Editor, _grid);
                }

                var border = new Border
                {
                    Height          = _grid.FilterRowHeight,
                    Background      = col.FilteringEnabled
                                        ? (Brush)(_grid.RowBackground ?? DefaultFilterableBackground)
                                        : (_grid.HeaderBackground ?? DefaultInertBackground),
                    BorderBrush     = _grid.GetEffectiveColumnSeparatorBrush(),
                    BorderThickness = new Thickness(0, 0, 1, 0),
                    Padding         = col.FilteringEnabled ? new Thickness(2) : new Thickness(0),
                    ClipToBounds    = true,
                    Child           = controller?.Editor
                };

                _cells.Add(new FilterCellInfo { CellBorder = border, Controller = controller });
                _filterCanvas.Children.Add(border);
            }

            LayoutCells();
        }

        public void LayoutCells()
        {
            if (_filterCanvas == null) return;
            var columns = _grid._headerPresenter.GetVisibleColumnList();
            double offset = -_grid._hOffset;
            for (int i = 0; i < _cells.Count && i < columns.Count; i++)
            {
                var cell = _cells[i].CellBorder;
                Canvas.SetLeft(cell, offset);
                cell.Width = columns[i].Width;
                offset    += columns[i].Width;
            }
        }

        public void Detach()
        {
            DetachAllControllers();
            ClearFilteringEnabledSubscriptions();
        }

        public void UpdateColumnSeparatorBrush()
        {
            if (_cells.Count == 0) return;
            var brush = _grid.GetEffectiveColumnSeparatorBrush();
            for (int i = 0; i < _cells.Count; i++)
                _cells[i].CellBorder.BorderBrush = brush;
        }

        /// <summary>
        /// Re-reads <c>RowBackground</c> / <c>HeaderBackground</c> from the grid and applies
        /// the updated colours to all existing filter cells. Called when the theme changes.
        /// </summary>
        public void UpdateRowBackground()
        {
            if (_cells.Count == 0) return;
            var columns = _grid._headerPresenter.GetVisibleColumnList();
            for (int i = 0; i < _cells.Count && i < columns.Count; i++)
            {
                _cells[i].CellBorder.Background = columns[i].FilteringEnabled
                    ? (_grid.RowBackground ?? DefaultFilterableBackground)
                    : (_grid.HeaderBackground ?? DefaultInertBackground);
            }
        }

        private void DetachAllControllers()
        {
            foreach (var cell in _cells)
                cell.Controller?.Detach();
            _cells.Clear();
        }

        private void ClearFilteringEnabledSubscriptions()
        {
            foreach (var s in _filteringEnabledSubscriptions)
                s.Dpd.RemoveValueChanged(s.Target, s.Handler);
            _filteringEnabledSubscriptions.Clear();
        }

        private void UpdateContainerVisibility(bool anyEnabled)
        {
            if (_filterContainer == null) return;
            _filterContainer.Visibility = anyEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnFilteringEnabledChanged(object sender, EventArgs e)
        {
            RebuildCells();
        }
    }
}
