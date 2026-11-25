using System;
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

        /// <summary>
        /// Builds the text Ctrl+C (or Ctrl+Shift+C with <paramref name="includeHeaders"/>) copies:
        /// in Cell mode the focused cell's displayed text, in Row mode all visible columns of the
        /// focused row separated by tabs (pastes into separate Excel cells). With headers, the header
        /// line comes first. Returns <c>null</c> when there is nothing to copy.
        /// </summary>
        public string GetCopyText(bool includeHeaders = false)
        {
            if (IsPlaceholderActive) return null;

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
            var text = GetCopyText(includeHeaders);
            if (text == null) return false;
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
