using System;
using System.Windows;
using System.Windows.Input;
using Open.DataGrid.Navigation;

namespace Open.DataGrid.Controls
{
    public partial class OpenDataGrid
    {
        #region Keyboard + Navigation

        protected override void OnGotFocus(RoutedEventArgs e)
        {
            base.OnGotFocus(e);
            _selection.HandleGotFocus();
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            _selection.HandleLostKeyboardFocus(e);
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            _selection.HandleGotKeyboardFocus(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Handled) return;

            // Filter row editors handle their own navigation — don't intercept keys there.
            if (_filterRowPresenter?.IsKeyboardFocusWithin == true) return;

            if (e.Key == Key.Enter && !_navEngine.IsEditing && TryExecuteEnterCommand())
            {
                e.Handled = true;
                return;
            }

            if (!_navEngine.IsEditing
                && e.Key == ToggleCheckedKey
                && (CheckedItems != null || HasAnyBoundCheckColumn()))
            {
                // Let columns that begin edit on this key (e.g. BooleanColumn on Space) take priority.
                var cols = GetVisibleColumnList();
                int fc = FocusedColIndex;
                bool colWantsEdit = fc >= 0 && fc < cols.Count && cols[fc].CanBeginEdit(e.Key);
                if (!colWantsEdit)
                {
                    // Resolve toggle target from the focused row rather than SelectedItem so that
                    // keyboard intent (where the cursor is) matches the toggled item exactly.
                    // Fallback to SelectedItem for edge cases where focus position is not yet set.
                    object toggleItem = null;
                    var sorted = _sortEngine.SortedIndices;
                    int focusedRow = FocusedRowSortedIndex;
                    if (focusedRow >= 0 && sorted != null
                        && focusedRow < sorted.Length
                        && DataSource != null)
                    {
                        int di = sorted[focusedRow];
                        if (di >= 0 && di < DataSource.Count) toggleItem = DataSource[di];
                    }
                    if (toggleItem == null) toggleItem = SelectedItem;
                    if (toggleItem == null) return;

                    // Pass the focused CheckColumn (if any) so SyncCheckedStateToBindingPath
                    // only writes to that column's BindingPath — not to every bound CheckColumn.
                    var focusedCheckCol = (fc >= 0 && fc < cols.Count)
                        ? cols[fc] as Columns.OpenDataGridCheckColumn
                        : null;
                    ToggleChecked(toggleItem, focusedCheckCol);
                    e.Handled = true;
                    return;
                }
            }

            _navEngine.HandleKeyDown(e);
        }

        private bool TryExecuteEnterCommand()
        {
            var cmd = EnterCommand;
            if (cmd == null) return false;
            var param = SelectedItem;
            if (!cmd.CanExecute(param)) return false;
            cmd.Execute(param);
            return true;
        }

        private void OnNavigationRequested(object sender, NavigationEventArgs e)
        {
            _selection.ApplyCellFocus(e.RowIndex, e.ColumnIndex, ensureVisible: true);
        }

        /// <summary>
        /// Scrolluje grid do podanego wiersza (indeks danych) i kolumny.
        /// Działa niezależnie od aktualnego sortowania.
        /// </summary>
        public void ScrollToItem(int dataIndex, int columnIndex = 0)
            => _selection.ScrollToItem(dataIndex, columnIndex);

        /// <summary>
        /// Przesuwa focus komórki do kolumny wskazanej przez <paramref name="nameOrPath"/>
        /// (dopasowanie po <c>Header</c> lub <c>BindingPath</c>, case-insensitive).
        /// </summary>
        public void SelectColumn(string nameOrPath)
            => _selection.SelectColumn(nameOrPath);

        // Internal forwarding helpers used by other partial classes
        internal void EnsureColumnVisible(int colIndex) => _selection.EnsureColumnVisible(colIndex);

        #endregion
    }
}
