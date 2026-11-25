using System;
using System.Windows.Input;
using Open.DataGrid.Columns;
using Open.DataGrid.Navigation;

namespace Open.DataGrid.Controls
{
    public partial class OpenDataGrid
    {
        #region Editing

        private static bool IsPotentialCharacterEditKey(Key key)
        {
            return (key >= Key.D0 && key <= Key.D9)
                || (key >= Key.NumPad0 && key <= Key.NumPad9)
                || (key >= Key.A && key <= Key.Z)
                || key == Key.Space
                || key == Key.OemMinus || key == Key.Subtract
                || key == Key.Decimal || key == Key.OemPeriod || key == Key.OemComma;
        }

        private void OnEditRequested(object sender, EditEventArgs e)
        {
            TryBeginEdit(e.RowIndex, e.ColumnIndex, e.TriggerKey);
        }

        private void OnEditCommitRequested(object sender, EventArgs e)
        {
            CommitEdit();
        }

        private void OnEditCancelRequested(object sender, EventArgs e)
        {
            CancelEdit();
        }

        internal void TryBeginEdit(int sortedRowIndex, int colIndex, Key triggerKey)
        {
            if (IsPlaceholderActive) return;
            if (IsReadOnly) return;

            var columns = GetVisibleColumnList();
            if (colIndex < 0 || colIndex >= columns.Count) return;
            var col = columns[colIndex];

            if (col.IsReadOnly) return;
            if (!col.CanBeginEdit(triggerKey)) return;

            // Check row-level readonly
            var sortedIndices = _sortEngine.SortedIndices;
            if (sortedIndices == null || sortedRowIndex < 0 || sortedRowIndex >= sortedIndices.Length) return;
            int dataIndex = sortedIndices[sortedRowIndex];
            if (dataIndex < 0 || dataIndex >= (DataSource?.Count ?? 0)) return;

            var dataItem = DataSource[dataIndex];
            if (_rowReadOnlyGetter?.Invoke(dataItem) == true) return;

            // Find cell in rendered rows
            var row = _rowEngine.FindRowByDataIndex(dataIndex) as OpenDataGridRow;
            if (row == null)
            {
                // Row not visible, scroll to it first
                EnsureRowVisible(sortedRowIndex);
                row = _rowEngine.FindRowByDataIndex(dataIndex) as OpenDataGridRow;
                if (row == null) return;
            }

            var cell = row.GetCell(colIndex);
            if (cell == null) return;

            CommitEdit(); // commit any previous edit

            if (cell.BeginEdit())
            {
                _editingCell = cell;
                _editingDataIndex = dataIndex;
                _navEngine.SetEditingState(true);

                _editValueChangedHandler = (s, ev) => OnLiveEditValueChanged();
                _editingCell.EditValueChanged += _editValueChangedHandler;

                bool isCharKey = IsPotentialCharacterEditKey(triggerKey)
                    || (col is Columns.OpenDataGridComboColumn && triggerKey == Key.F4);
                var capturedCol = col;
                var capturedCell = cell;
                var capturedKey = triggerKey;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    if (!ReferenceEquals(_editingCell, capturedCell)) return;

                    var editEl = capturedCell.EditElement;
                    if (editEl == null) return;

                    if (isCharKey)
                        capturedCol.BeginCharacterEdit(editEl, capturedKey);

                    if (ReferenceEquals(_editingCell, capturedCell))
                        editEl.Focus();
                }));
            }
        }

        /// <summary>
        /// Zeroes the value of the currently focused cell if it belongs to an editable numeric column
        /// (Integer/Decimal) and is not read-only (Grid/Column/Row cascade). Invoked by Delete
        /// outside edit mode when <see cref="ClearNumericCellOnDelete"/> is <c>true</c>.
        /// </summary>
        internal bool TryClearFocusedNumericCell()
        {
            if (IsPlaceholderActive || IsReadOnly) return false;

            var columns = GetVisibleColumnList();
            int colIndex = FocusedColIndex;
            if (colIndex < 0 || colIndex >= columns.Count) return false;

            var numeric = columns[colIndex] as OpenDataGridNumericColumnBase;
            if (numeric == null || numeric.IsReadOnly) return false;

            var sortedIndices = _sortEngine.SortedIndices;
            int sortedRowIndex = FocusedRowSortedIndex;
            if (sortedIndices == null || sortedRowIndex < 0 || sortedRowIndex >= sortedIndices.Length) return false;

            int dataIndex = sortedIndices[sortedRowIndex];
            if (dataIndex < 0 || dataIndex >= (DataSource?.Count ?? 0)) return false;

            var dataItem = DataSource[dataIndex];
            if (_rowReadOnlyGetter?.Invoke(dataItem) == true) return false;

            var setter = _propertySetters[colIndex];
            if (setter == null) return false;

            var cleared = numeric.GetClearedValue();
            setter(dataItem, cleared);

            // Refresh the visible cell directly — no WPF binding on the hot path.
            var row = _rowEngine.FindRowByDataIndex(dataIndex) as OpenDataGridRow;
            row?.GetCell(colIndex)?.UpdateValue(cleared);
            return true;
        }

        private void OnLiveEditValueChanged()
        {
            if (!LiveEdit || _editingCell == null) return;
            if (_editingDataIndex < 0 || _editingDataIndex >= (DataSource?.Count ?? 0)) return;
            var colIndex = _editingCell.ColumnIndex;
            var value = _editingCell.Column.ExtractEditValue(_editingCell.EditElement);
            if (!ReferenceEquals(value, OpenDataGridColumn.NoValue))
                _propertySetters[colIndex]?.Invoke(DataSource[_editingDataIndex], value);
        }

        protected internal void CommitEdit(bool restoreFocus = true)
        {
            if (_editingCell == null) return;

            var cell         = _editingCell;
            var editingIndex = _editingDataIndex;

            if (_editValueChangedHandler != null)
            {
                cell.EditValueChanged -= _editValueChangedHandler;
                _editValueChangedHandler = null;
            }

            try
            {
                var value = cell.CommitEdit();
                // When LiveEdit is active, OnLiveEditValueChanged already wrote each keystroke to the model.
                // Writing again here would cause a duplicate PropertyChanged notification.
                if (!LiveEdit
                    && !ReferenceEquals(value, OpenDataGridColumn.NoValue)
                    && editingIndex >= 0
                    && editingIndex < (DataSource?.Count ?? 0))
                {
                    _propertySetters[cell.ColumnIndex]?.Invoke(DataSource[editingIndex], value);
                }
            }
            finally
            {
                _editingCell      = null;
                _editingDataIndex = -1;
                _navEngine.SetEditingState(false);
                RunInfoLayoutRebuildDeferredByEdit();
                if (restoreFocus)
                {
                    _selection.SuppressRestoreColumnOnceForEditExit();
                    Focus();
                }
            }
        }

        internal void CancelEdit()
        {
            if (_editingCell == null) return;

            var cell         = _editingCell;
            var editingIndex = _editingDataIndex;

            if (_editValueChangedHandler != null)
            {
                cell.EditValueChanged -= _editValueChangedHandler;
                _editValueChangedHandler = null;
            }

            try
            {
                if (LiveEdit && editingIndex >= 0 && editingIndex < (DataSource?.Count ?? 0))
                    _propertySetters[cell.ColumnIndex]?.Invoke(DataSource[editingIndex], cell.PreEditValue);

                cell.CancelEdit();
            }
            finally
            {
                _editingCell      = null;
                _editingDataIndex = -1;
                _navEngine.SetEditingState(false);
                RunInfoLayoutRebuildDeferredByEdit();
                _selection.SuppressRestoreColumnOnceForEditExit();
                Focus();
            }
        }

        #endregion
    }
}
