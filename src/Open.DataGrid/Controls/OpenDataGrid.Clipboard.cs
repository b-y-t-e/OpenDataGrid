using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Open.DataGrid.Accessors;
using Open.DataGrid.Columns;
using Open.DataGrid.Models;

namespace Open.DataGrid.Controls
{
    public partial class OpenDataGrid
    {
        #region Clipboard

        /// <summary>
        /// When <c>true</c> (default), Ctrl+C outside edit mode copies the focused cell
        /// (<see cref="Models.NavigationMode.Cell"/>) or the focused row (<see cref="Models.NavigationMode.Row"/>)
        /// to the clipboard; Ctrl+Shift+C does the same with the column headers as the first line.
        /// </summary>
        public static readonly DependencyProperty AllowCopyProperty =
            DependencyProperty.Register(nameof(AllowCopy), typeof(bool), typeof(OpenDataGrid),
                new PropertyMetadata(true));

        /// <summary>Enables Ctrl+C / Ctrl+Shift+C copying. Default: <c>true</c>.</summary>
        public bool AllowCopy { get => (bool)GetValue(AllowCopyProperty); set => SetValue(AllowCopyProperty, value); }

        /// <summary>Clipboard sink — replaced in tests so they never touch the real clipboard.</summary>
        internal Action<string> ClipboardWriter { get; set; } = WriteToSystemClipboard;

        /// <summary>Range copies above this many cells are built on a background thread. Internal for tests.</summary>
        internal int AsyncCopyCellThreshold { get; set; } = 200000;

        /// <summary>The background build of the last large copy (null when none ran). Internal for tests.</summary>
        internal System.Threading.Tasks.Task PendingCopyTask { get; private set; }

        // Bumped by every copy — a newer copy supersedes a large copy still being built.
        private int _copyGeneration;

        /// <summary>
        /// Builds the text Ctrl+C (or Ctrl+Shift+C with <paramref name="includeHeaders"/>) copies:
        /// in Cell mode the focused cell's displayed text, in Row mode all visible columns of the
        /// focused row separated by tabs (pastes into separate Excel cells). With headers, the header
        /// line comes first. Returns <c>null</c> when there is nothing to copy.
        /// </summary>
        public string GetCopyText(bool includeHeaders = false)
        {
            if (IsPlaceholderActive) return null;

            if (_rangeSelection.HasRanges)
            {
                var plan = CreateRangeCopyPlan(includeHeaders, threadSafe: false);
                return plan == null ? null : BuildRangeCopyText(plan, null);
            }

            var item = GetFocusedDataItem();
            if (item == null) return null;

            var columns = GetVisibleColumnList();
            if (columns.Count == 0) return null;

            int first = 0, last = columns.Count - 1;
            int focusedCol = FocusedColIndex;
            if (NavigationMode == NavigationMode.Cell && focusedCol >= 0 && focusedCol < columns.Count)
                first = last = focusedCol;

            var sb = new StringBuilder();
            if (includeHeaders)
            {
                for (int i = first; i <= last; i++)
                {
                    if (i > first) sb.Append('\t');
                    sb.Append(EscapeClipboardField(columns[i].Header));
                }
                sb.Append(Environment.NewLine);
            }

            for (int i = first; i <= last; i++)
            {
                if (i > first) sb.Append('\t');
                sb.Append(EscapeClipboardField(columns[i].GetClipboardText(GetCellValueForCopy(columns, i, item))));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Copies <see cref="GetCopyText"/> to the clipboard. Returns <c>false</c> when there is
        /// nothing to copy or the clipboard is locked by another process.
        /// </summary>
        public bool CopyToClipboard(bool includeHeaders = false)
        {
            int generation = System.Threading.Interlocked.Increment(ref _copyGeneration);

            var box = _rangeSelection.GetBoundingRange();
            if (box.HasValue && !IsPlaceholderActive
                && (long)box.Value.RowCount * box.Value.ColumnCount > AsyncCopyCellThreshold)
            {
                var plan = CreateRangeCopyPlan(includeHeaders, threadSafe: true);
                if (plan != null)
                {
                    StartBackgroundCopy(plan, generation);
                    return true;
                }
                // A column type without a background-safe formatter: build on the UI thread below.
            }

            var text = GetCopyText(includeHeaders);
            if (text == null) return false;
            return WriteClipboard(text);
        }

        private bool WriteClipboard(string text)
        {
            try
            {
                ClipboardWriter(text);
                return true;
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        /// <summary>
        /// Builds a large range copy on a background thread with the loading indicator shown, then
        /// writes the clipboard on the UI thread — unless a newer copy started meanwhile.
        /// </summary>
        private void StartBackgroundCopy(RangeCopyPlan plan, int generation)
        {
            var work = _workCoordinator.BeginWork();
            Func<bool> superseded = () => System.Threading.Volatile.Read(ref _copyGeneration) != generation;
            PendingCopyTask = System.Threading.Tasks.Task.Run(() =>
            {
                string text = null;
                try { text = BuildRangeCopyText(plan, superseded); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[EDG] background copy failed: " + ex.Message); }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (text != null && !superseded()) WriteClipboard(text);
                    }
                    finally { work.Dispose(); }
                }));
            });
        }

        /// <summary>Everything a range copy needs, captured on the UI thread.</summary>
        private sealed class RangeCopyPlan
        {
            public IList Source;
            public int[] Sorted;
            public CellRange Box;
            public CellRange[] Ranges;
            public Func<object, object>[] Getters;     // per column of Box
            public Func<object, string>[] Formatters;  // per column of Box
            public string[] Headers;                   // null = no header line
        }

        /// <summary>
        /// Captures the selected ranges for copying. With <paramref name="threadSafe"/> the cell
        /// formatters must be background-safe; returns <c>null</c> when one is not (or nothing to copy).
        /// </summary>
        private RangeCopyPlan CreateRangeCopyPlan(bool includeHeaders, bool threadSafe)
        {
            var box = _rangeSelection.GetBoundingRange();
            var source = DataSource;
            var sorted = _sortEngine.SortedIndices;
            var columns = GetVisibleColumnList();
            if (!box.HasValue || source == null || sorted == null || columns.Count == 0) return null;

            var b = box.Value;
            if (b.LastRow >= sorted.Length || b.LastColumn >= columns.Count) return null;

            int count = b.ColumnCount;
            var getters    = new Func<object, object>[count];
            var formatters = new Func<object, string>[count];
            var headers    = includeHeaders ? new string[count] : null;
            for (int i = 0; i < count; i++)
            {
                int c = b.FirstColumn + i;
                var column = columns[c];
                getters[i] = CreateCopyGetter(columns, c);
                formatters[i] = threadSafe ? column.CreateThreadSafeClipboardFormatter() : column.GetClipboardText;
                if (formatters[i] == null) return null;
                if (headers != null) headers[i] = column.Header;
            }

            var ranges = new CellRange[_rangeSelection.Ranges.Count];
            for (int i = 0; i < ranges.Length; i++) ranges[i] = _rangeSelection.Ranges[i];

            return new RangeCopyPlan
            {
                Source = source, Sorted = sorted, Box = b, Ranges = ranges,
                Getters = getters, Formatters = formatters, Headers = headers,
            };
        }

        private Func<object, object> CreateCopyGetter(IReadOnlyList<OpenDataGridColumn> columns, int colIndex)
        {
            var getters = _propertyGetters;
            if (getters != null && getters.Length == columns.Count && getters[colIndex] != null)
                return getters[colIndex];

            var path = columns[colIndex].BindingPath;
            if (string.IsNullOrEmpty(path)) return item => item;
            var getter = PropertyAccessorCache.CreateGetter(path);
            return item => getter(item);
        }

        /// <summary>
        /// The bounding block of all ranges as tab-separated text (rows separated by
        /// <see cref="Environment.NewLine"/>); cells not covered by any range are empty fields.
        /// Returns <c>null</c> when <paramref name="cancelled"/> reports a newer copy.
        /// </summary>
        private static string BuildRangeCopyText(RangeCopyPlan plan, Func<bool> cancelled)
        {
            var b = plan.Box;
            var sb = new StringBuilder();
            if (plan.Headers != null)
            {
                for (int i = 0; i < plan.Headers.Length; i++)
                {
                    if (i > 0) sb.Append('\t');
                    sb.Append(EscapeClipboardField(plan.Headers[i]));
                }
                sb.Append(Environment.NewLine);
            }

            var ranges = plan.Ranges;
            bool singleRange = ranges.Length == 1;
            for (int r = b.FirstRow; r <= b.LastRow; r++)
            {
                if (cancelled != null && (r & 1023) == 0 && cancelled()) return null;
                if (r > b.FirstRow) sb.Append(Environment.NewLine);

                int dataIndex = r < plan.Sorted.Length ? plan.Sorted[r] : -1;
                object item = dataIndex >= 0 && dataIndex < plan.Source.Count ? plan.Source[dataIndex] : null;

                for (int c = b.FirstColumn; c <= b.LastColumn; c++)
                {
                    if (c > b.FirstColumn) sb.Append('\t');
                    if (item == null) continue;
                    if (!singleRange && !AnyRangeContains(ranges, r, c)) continue;
                    int i = c - b.FirstColumn;
                    sb.Append(EscapeClipboardField(plan.Formatters[i](plan.Getters[i](item))));
                }
            }
            return sb.ToString();
        }

        private static bool AnyRangeContains(CellRange[] ranges, int row, int column)
        {
            for (int i = 0; i < ranges.Length; i++)
                if (ranges[i].Contains(row, column)) return true;
            return false;
        }

        /// <summary>Ctrl+C / Ctrl+Shift+C outside edit mode. Returns <c>true</c> when the key was consumed.</summary>
        internal bool TryHandleCopyKey(Key key, ModifierKeys modifiers)
        {
            if (!AllowCopy || _navEngine.IsEditing || key != Key.C) return false;
            if ((modifiers & ModifierKeys.Control) == 0 || (modifiers & ModifierKeys.Alt) != 0) return false;

            CopyToClipboard(includeHeaders: (modifiers & ModifierKeys.Shift) != 0);
            return true;
        }

        private object GetFocusedDataItem()
        {
            var source = DataSource;
            var sorted = _sortEngine.SortedIndices;
            int focusedRow = FocusedRowSortedIndex;
            if (source != null && sorted != null && focusedRow >= 0 && focusedRow < sorted.Length)
            {
                int dataIndex = sorted[focusedRow];
                if (dataIndex >= 0 && dataIndex < source.Count) return source[dataIndex];
            }
            return SelectedItem;
        }

        private object GetCellValueForCopy(IReadOnlyList<OpenDataGridColumn> columns, int colIndex, object item)
        {
            var getters = _propertyGetters;
            if (getters != null && getters.Length == columns.Count && getters[colIndex] != null)
                return getters[colIndex](item);

            var path = columns[colIndex].BindingPath;
            return string.IsNullOrEmpty(path) ? item : PropertyAccessorCache.GetValue(item, path);
        }

        /// <summary>
        /// Quotes a field that would break the tab-separated layout (tab, line break or quote),
        /// doubling inner quotes — the convention Excel uses for pasted text.
        /// </summary>
        internal static string EscapeClipboardField(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.IndexOfAny(new[] { '\t', '\r', '\n', '"' }) < 0) return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static void WriteToSystemClipboard(string text) => Clipboard.SetDataObject(text, true);

        #endregion
    }
}
