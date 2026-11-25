using System;
using System.Windows.Input;
using Open.DataGrid.Models;

namespace Open.DataGrid.Navigation
{
    /// <summary>
    /// Manages keyboard navigation state for OpenDataGrid.
    /// Operates on logical indices (sorted positions) independently of data indices.
    /// </summary>
    public sealed class NavigationEngine
    {
        private NavigationMode _mode;
        private int _totalRows;
        private int _totalColumns;
        private int _visibleRows;

        public int CurrentRowIndex { get; private set; } = -1;
        public int CurrentColumnIndex { get; private set; } = 0;
        public bool IsEditing { get; private set; }

        public event EventHandler<NavigationEventArgs> NavigationRequested;
        public event EventHandler<EditEventArgs> EditRequested;
        public event EventHandler EditCommitRequested;
        public event EventHandler EditCancelRequested;

        /// <summary>
        /// Called during editing when Left or Right is pressed.
        /// Should return true if the caret is at the boundary for that direction,
        /// so the key commits the edit and moves to the adjacent column.
        /// </summary>
        public Func<Key, bool> IsAtEdgeForNavigation { get; set; }

        /// <summary>
        /// When returns true, all navigation keys (arrows, PageUp, PageDown) are absorbed
        /// during edit mode without committing or moving. Set by the grid based on the active
        /// column's <c>AbsorbNavigationKeysInEdit</c> property.
        /// </summary>
        public Func<bool> ShouldAbsorbNavigationKeys { get; set; }

        public void Initialize(NavigationMode mode, int totalRows, int totalColumns, int visibleRows)
        {
            _mode = mode;
            _totalRows = totalRows;
            _totalColumns = totalColumns;
            _visibleRows = visibleRows;

            // Preserve current position; only clamp to new bounds so callers
            // don't need to restore it after every re-initialization.
            if (totalRows == 0) { CurrentRowIndex = -1; CurrentColumnIndex = 0; return; }
            CurrentRowIndex    = CurrentRowIndex    < 0 ? 0 : Math.Min(CurrentRowIndex,    totalRows    - 1);
            CurrentColumnIndex = CurrentColumnIndex < 0 ? 0 : Math.Min(CurrentColumnIndex, Math.Max(0, totalColumns - 1));
        }

        public void UpdateStats(int totalRows, int visibleRows)
        {
            _totalRows = totalRows;
            _visibleRows = visibleRows;
            if (CurrentRowIndex >= _totalRows)
                CurrentRowIndex = _totalRows - 1;
        }

        public void UpdateMode(NavigationMode mode)
        {
            _mode = mode;
        }

        public void HandleKeyDown(KeyEventArgs e)
        {
            if (_totalRows == 0) return;

            if (IsEditing)
            {
                HandleEditingKey(e);
                return;
            }

            switch (e.Key)
            {
                case Key.Up:
                    Move(-1, 0);
                    e.Handled = true;
                    break;
                case Key.Down:
                    Move(1, 0);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    Move(-_visibleRows, 0);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    Move(_visibleRows, 0);
                    e.Handled = true;
                    break;
                case Key.Home:
                    MoveHome((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0);
                    e.Handled = true;
                    break;
                case Key.End:
                    MoveEnd((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0);
                    e.Handled = true;
                    break;
                case Key.Left when _mode == NavigationMode.Cell:
                    MoveColumn(-1);
                    e.Handled = true;
                    break;
                case Key.Right when _mode == NavigationMode.Cell:
                    MoveColumn(1);
                    e.Handled = true;
                    break;
                case Key.Tab when _mode == NavigationMode.Cell:
                    if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0)
                        MoveColumn(-1);
                    else
                        MoveColumn(1);
                    e.Handled = true;
                    break;
                case Key.F2:
                    RequestEdit(e.Key);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    RequestEdit(e.Key);
                    e.Handled = true;
                    break;
                default:
                    // Pass character keys to edit handler for smart entry
                    RequestEdit(e.Key);
                    break;
            }
        }

        private void HandleEditingKey(KeyEventArgs e)
        {
            bool absorb = ShouldAbsorbNavigationKeys?.Invoke() == true;
            switch (e.Key)
            {
                case Key.Escape:
                    IsEditing = false;
                    EditCancelRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    IsEditing = false;
                    EditCommitRequested?.Invoke(this, EventArgs.Empty);
                    // Move down after commit
                    Move(1, 0);
                    e.Handled = true;
                    break;
                case Key.Tab when _mode == NavigationMode.Cell:
                    IsEditing = false;
                    EditCommitRequested?.Invoke(this, EventArgs.Empty);
                    if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0)
                        MoveColumn(-1);
                    else
                        MoveColumn(1);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                case Key.Up:
                    if (!absorb)
                    {
                        IsEditing = false;
                        EditCommitRequested?.Invoke(this, EventArgs.Empty);
                        Move(-1, 0);
                    }
                    e.Handled = true;
                    break;
                case Key.PageDown:
                case Key.Down:
                    if (!absorb)
                    {
                        IsEditing = false;
                        EditCommitRequested?.Invoke(this, EventArgs.Empty);
                        Move(1, 0);
                    }
                    e.Handled = true;
                    break;
                case Key.Left when _mode == NavigationMode.Cell && absorb:
                case Key.Right when _mode == NavigationMode.Cell && absorb:
                    e.Handled = true;
                    break;
                case Key.Left when _mode == NavigationMode.Cell && (IsAtEdgeForNavigation?.Invoke(Key.Left) ?? false):
                    IsEditing = false;
                    EditCommitRequested?.Invoke(this, EventArgs.Empty);
                    MoveColumn(-1);
                    e.Handled = true;
                    break;
                case Key.Right when _mode == NavigationMode.Cell && (IsAtEdgeForNavigation?.Invoke(Key.Right) ?? false):
                    IsEditing = false;
                    EditCommitRequested?.Invoke(this, EventArgs.Empty);
                    MoveColumn(1);
                    e.Handled = true;
                    break;
            }
        }

        private void RequestEdit(Key key)
        {
            EditRequested?.Invoke(this, new EditEventArgs(CurrentRowIndex, CurrentColumnIndex, key));
        }

        private void Move(int rowDelta, int colDelta)
        {
            int newRow = Math.Max(0, Math.Min(_totalRows - 1, CurrentRowIndex + rowDelta));
            int newCol = _mode == NavigationMode.Cell
                ? Math.Max(0, Math.Min(_totalColumns - 1, CurrentColumnIndex + colDelta))
                : CurrentColumnIndex;
            SetPosition(newRow, newCol);
        }

        /// <summary>
        /// Home: Ctrl → first row (column kept); Cell mode → first column (row kept);
        /// Row mode → first row.
        /// </summary>
        internal void MoveHome(bool ctrl)
        {
            if (_totalRows == 0) return;
            if (ctrl || _mode != NavigationMode.Cell)
                SetPosition(0, CurrentColumnIndex);
            else
                SetPosition(Math.Max(0, CurrentRowIndex), 0);
        }

        /// <summary>
        /// End: Ctrl → last row (column kept); Cell mode → last column (row kept);
        /// Row mode → last row.
        /// </summary>
        internal void MoveEnd(bool ctrl)
        {
            if (_totalRows == 0) return;
            if (ctrl || _mode != NavigationMode.Cell)
                SetPosition(_totalRows - 1, CurrentColumnIndex);
            else
                SetPosition(Math.Max(0, CurrentRowIndex), Math.Max(0, _totalColumns - 1));
        }

        private void MoveColumn(int delta)
        {
            int newCol = Math.Max(0, Math.Min(_totalColumns - 1, CurrentColumnIndex + delta));
            SetPosition(CurrentRowIndex, newCol);
        }

        private void SetPosition(int row, int col)
        {
            bool changed = row != CurrentRowIndex || col != CurrentColumnIndex;
            CurrentRowIndex = row;
            CurrentColumnIndex = col;
            if (changed)
                NavigationRequested?.Invoke(this, new NavigationEventArgs(row, col));
        }

        public void SetEditingState(bool isEditing) => IsEditing = isEditing;

        public void SetCurrentPosition(int row, int col)
        {
            CurrentRowIndex = row;
            CurrentColumnIndex = col;
        }
    }

    public class NavigationEventArgs : EventArgs
    {
        public int RowIndex { get; }
        public int ColumnIndex { get; }
        public NavigationEventArgs(int row, int col) { RowIndex = row; ColumnIndex = col; }
    }

    public class EditEventArgs : EventArgs
    {
        public int RowIndex { get; }
        public int ColumnIndex { get; }
        public Key TriggerKey { get; }
        public EditEventArgs(int row, int col, Key key)
        {
            RowIndex = row; ColumnIndex = col; TriggerKey = key;
        }
    }
}
