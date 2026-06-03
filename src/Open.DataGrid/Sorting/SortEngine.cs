using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Open.DataGrid.Columns;
using Open.DataGrid.Models;

namespace Open.DataGrid.Sorting
{
    /// <summary>
    /// Manages sort state via an int[] indirection array.
    /// Sorting never moves actual data — only the index array is sorted.
    /// For large datasets sort runs on a background thread.
    /// </summary>
    public sealed class SortEngine
    {
        // volatile: background thread writes, UI thread reads after SortCompleted event
        private volatile int[] _sortedIndices;
        // Reverse mapping: _dataToSorted[dataIndex] = sortedPosition — O(1) FindSortedPosition
        private volatile int[] _dataToSorted;
        // volatile: background thread updates this in ExecuteSortCore when stale indices are filtered
        // out (DataSource shrank after Initialize). UI thread reads it on the next Sort() call via
        // Array.Copy. The volatile write to _dataToSorted immediately below provides the release
        // fence that makes _totalRows visible to the UI thread before SortCompleted fires.
        private volatile int _totalRows;
        // When filtering is active, _maxDataIndex == DataSource.Count (>= _totalRows).
        // Used to correctly size the reverse map and the snapshot array in ExecuteSortCore.
        private int _maxDataIndex;
        private OpenDataGridColumn _currentColumn;
        private SortDirection _currentDirection = SortDirection.None;
        // volatile: background thread clears this to null before firing SortCompleted; UI thread
        // reads it in CancelPendingSort. Without volatile a stale non-null read could cause the UI
        // thread to Cancel/Dispose an already-completed (and already-null) CTS.
        private volatile CancellationTokenSource _sortCts;

        public int[] SortedIndices => _sortedIndices;
        public OpenDataGridColumn CurrentColumn => _currentColumn;
        public SortDirection CurrentDirection => _currentDirection;

        /// <summary>Raised when sort completes (may be on background thread — marshal to UI if needed).</summary>
        public event EventHandler SortCompleted;

        /// <summary>
        /// Raised when an in-progress sort is cancelled because a newer <see cref="Initialize"/> or
        /// <see cref="Sort"/> call superseded it. Always raised on the thread that called Initialize/Sort.
        /// Consumers must marshal to UI if needed.
        /// </summary>
        public event EventHandler SortCancelled;

        public void Initialize(int totalRows)
        {
            // Cancel any in-progress background sort so it doesn't overwrite the new indices.
            // Emit SortCancelled synchronously so the caller can balance its BeginPendingWork.
            CancelPendingSort();

            _totalRows    = totalRows;
            _maxDataIndex = totalRows;
            var indices = new int[totalRows];
            for (int i = 0; i < totalRows; i++)
                indices[i] = i;
            _sortedIndices = indices;
            _dataToSorted  = (int[])indices.Clone(); // identity mapping: sorted position == data index
            _currentColumn = null;
            _currentDirection = SortDirection.None;
        }

        /// <summary>
        /// Initializes with a pre-filtered subset of data indices.
        /// The sorted output will only contain rows from <paramref name="filteredIndices"/>.
        /// <paramref name="maxDataIndex"/> is the full DataSource.Count — used to size the reverse map.
        /// Filtered-out rows return -1 from <see cref="FindSortedPosition"/>.
        /// </summary>
        public void Initialize(int[] filteredIndices, int maxDataIndex)
        {
            CancelPendingSort();

            _totalRows    = filteredIndices.Length;
            _maxDataIndex = maxDataIndex;

            var indices = new int[filteredIndices.Length];
            Array.Copy(filteredIndices, indices, filteredIndices.Length);
            _sortedIndices = indices;

            // Reverse map sized to full data source — filtered-out entries remain -1
            var reverse = new int[maxDataIndex];
            for (int i = 0; i < maxDataIndex; i++)
                reverse[i] = -1;
            for (int i = 0; i < indices.Length; i++)
                reverse[indices[i]] = i;
            _dataToSorted = reverse;

            _currentColumn = null;
            _currentDirection = SortDirection.None;
        }

        /// <summary>Sorts by the given column, toggling direction if same column.</summary>
        public void Sort(OpenDataGridColumn column, IList dataSource)
        {
            SortDirection newDir;
            if (_currentColumn == column)
            {
                newDir = _currentDirection == SortDirection.Ascending
                    ? SortDirection.Descending
                    : SortDirection.Ascending;
            }
            else
            {
                // Clear previous column indicator
                if (_currentColumn != null)
                    _currentColumn.SortDirection = SortDirection.None;
                newDir = SortDirection.Ascending;
            }

            _currentColumn = column;
            _currentDirection = newDir;
            column.SortDirection = newDir;

            ExecuteSortCore(column.GetComparison(), newDir, dataSource);
        }

        /// <summary>
        /// Sorts by the given column and direction without toggling.
        /// Used to restore sort state after a collection change that rebuilds the index array.
        /// </summary>
        internal void SortExplicit(OpenDataGridColumn column, SortDirection direction, IList dataSource)
        {
            if (direction == SortDirection.None) return;

            _currentColumn    = column;
            _currentDirection = direction;
            column.SortDirection = direction;

            ExecuteSortCore(column.GetComparison(), direction, dataSource);
        }

        /// <summary>
        /// Cancels any in-progress background sort and emits <see cref="SortCancelled"/> when one
        /// was actually running. Called from Initialize overloads and OnGridUnloaded.
        /// </summary>
        internal void CancelPendingSort()
        {
            if (_sortCts == null) return;
            _sortCts.Cancel();
            // Do NOT Dispose here — the background task may still read cts.IsCancellationRequested
            // after Cancel returns. The task's finally block disposes the CTS once it exits.
            _sortCts = null;
            // SortCancelled is the only notification consumers receive for a cancelled sort —
            // SortCompleted is never fired for cancelled tasks. Callers that called BeginPendingWork
            // before Sort/SortExplicit must balance it inside their SortCancelled handler.
            SortCancelled?.Invoke(this, EventArgs.Empty);
        }

        private void ExecuteSortCore(Comparison<object> comparison, SortDirection direction, IList dataSource)
        {
            // Capture on UI thread before Task.Run — both fields may be overwritten by a concurrent
            // Initialize() call once the background task starts.
            int maxIdx  = _maxDataIndex;
            int dsCount = dataSource.Count;

            // Build snapshot AND filter stale indices in one pass.
            // Items removed from DataSource after Initialize() (100 ms coalesce window) have
            // indices >= dsCount. Excluding them prevents null-snapshot entries from reaching
            // the comparator, which would throw NRE and leave the sort task faulted — causing
            // the loading overlay to hang forever because SortCompleted would never fire.
            var allIndices = new int[_totalRows];
            Array.Copy(_sortedIndices, allIndices, _totalRows);

            int validCount = 0;
            for (int i = 0; i < allIndices.Length; i++)
                if (allIndices[i] < dsCount) validCount++;

            var indices  = new int[validCount];
            var snapshot = new object[maxIdx];
            for (int i = 0, j = 0; i < allIndices.Length; i++)
            {
                int idx = allIndices[i];
                if (idx < dsCount)
                {
                    snapshot[idx] = dataSource[idx];
                    indices[j++]  = idx;
                }
            }

            // Cancel any previously running sort. Each Sort() call is paired with one
            // BeginPendingWork by the caller; cancelling the old task must balance that work
            // count immediately, since the new SortCompleted will only decrement once.
            CancelPendingSort();
            var cts = new CancellationTokenSource();
            _sortCts = cts;
            // Capture token before Task.Run so the lambda can safely read it after CTS disposal.
            // CancellationToken retains its IsCancellationRequested state even after the source is disposed.
            var token = cts.Token;

            Task.Run(() =>
            {
                try
                {
                    if (direction == SortDirection.Ascending)
                        Array.Sort(indices, (a, b) => { int c = comparison(snapshot[a], snapshot[b]); return c != 0 ? c : a.CompareTo(b); });
                    else
                        Array.Sort(indices, (a, b) => { int c = comparison(snapshot[b], snapshot[a]); return c != 0 ? c : a.CompareTo(b); });

                    if (token.IsCancellationRequested) return;

                    // Build reverse lookup. All entries in indices are < dsCount <= maxIdx
                    // (guaranteed by the filter above), so no bounds guard is needed here.
                    var reverse = new int[maxIdx];
                    for (int i = 0; i < maxIdx; i++)
                        reverse[i] = -1;
                    for (int i = 0; i < indices.Length; i++)
                        reverse[indices[i]] = i;

                    _sortedIndices = indices;
                    _totalRows     = indices.Length; // stale entries were filtered out above — keep _totalRows in sync
                    _dataToSorted  = reverse;
                    // Clear _sortCts before firing SortCompleted so that any Initialize() call
                    // arriving on the UI thread after SortCompleted sees null and does not
                    // spuriously fire SortCancelled. _sortCts is volatile so the write is
                    // immediately visible to the UI thread without a Dispatcher barrier.
                    _sortCts = null;
                    SortCompleted?.Invoke(this, EventArgs.Empty);
                }
                catch (OperationCanceledException) { /* expected — sort cancelled */ }
                catch (Exception ex)
                {
                    // Unexpected comparator failure (e.g. NRE from null item in DataSource).
                    // Do NOT publish corrupt sorted indices. Fire SortCompleted with the pre-sort
                    // state so the work coordinator and loading overlay are always released.
                    System.Diagnostics.Debug.WriteLine($"[SortEngine] Unexpected sort failure — {ex}");
                    if (!token.IsCancellationRequested)
                    {
                        _sortCts = null;
                        SortCompleted?.Invoke(this, EventArgs.Empty);
                    }
                }
                finally { cts.Dispose(); }
            }, token);
        }

        /// <summary>Clears sort and restores identity mapping.</summary>
        public void ClearSort()
        {
            if (_currentColumn != null)
                _currentColumn.SortDirection = SortDirection.None;
            _currentColumn = null;
            _currentDirection = SortDirection.None;
            Initialize(_totalRows);
        }

        /// <summary>Finds the visual (sorted) position of a given data index in O(1).</summary>
        public int FindSortedPosition(int dataIndex)
        {
            var lookup = _dataToSorted;
            if (lookup == null || dataIndex < 0 || dataIndex >= lookup.Length) return -1;
            return lookup[dataIndex];
        }
    }
}
