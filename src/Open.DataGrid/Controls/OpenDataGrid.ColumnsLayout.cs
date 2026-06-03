// Header and columns layout logic has been moved to HeaderPresenter.
// This file contains only thin forwarders so the rest of the partial class compiles.
using System;
using System.Collections.Generic;
using System.Windows.Input;
using Open.DataGrid.Columns;

namespace Open.DataGrid.Controls
{
    public partial class OpenDataGrid
    {
        #region Columns + Headers  (forwarders to HeaderPresenter)

        internal IReadOnlyList<OpenDataGridColumn> GetAllColumnList()     => _headerPresenter.GetAllColumnList();
        internal IReadOnlyList<OpenDataGridColumn> GetVisibleColumnList() => _headerPresenter.GetVisibleColumnList();
        internal void InvalidateColumnCaches()                            => _headerPresenter.InvalidateColumnCaches();

        private void BuildHeaders()                                       => _headerPresenter.BuildHeaders(OnHeaderCellClick);
        internal void LayoutHeaders()                                     => _headerPresenter.LayoutHeaders();
        internal void LayoutFilterRow()                                   => _filterRowPresenter.LayoutCells();
        internal void RebuildFilterRow()                                  => _filterRowPresenter.RebuildCells();

        private void RebuildColumnVisibilitySubscriptions()               => _headerPresenter.RebuildColumnVisibilitySubscriptions();
        private void ApplyHiddenColumnsToColumns()                        => _headerPresenter.ApplyHiddenColumnsToColumns();
        private void ApplyHiddenColumns()                                 => _headerPresenter.ApplyHiddenColumns();
        private void RefreshAfterColumnVisibilityChanged()                => _headerPresenter.RefreshAfterColumnVisibilityChanged();
        private void ApplyHeaderFontDefaultsToExistingHeaders()           => _headerPresenter.ApplyHeaderFontDefaultsToExistingHeaders();
        private void ApplyHeaderHorizontalAlignmentToExistingHeaders()    => _headerPresenter.ApplyHeaderHorizontalAlignmentToExistingHeaders();

        /// <summary>
        /// Programmatically reorders a column. <paramref name="fromVisibleIndex"/> is the
        /// current position and <paramref name="toVisibleIndex"/> is the insertion point
        /// (0 = before first column, N = after last column) in the visible column list.
        /// Requires <see cref="AllowColumnReorder"/> to be <c>true</c>.
        /// </summary>
        public void ReorderColumn(int fromVisibleIndex, int toVisibleIndex)
        {
            if (!AllowColumnReorder) return;
            _headerPresenter.PerformReorder(fromVisibleIndex, toVisibleIndex);
        }

        #endregion
    }
}
