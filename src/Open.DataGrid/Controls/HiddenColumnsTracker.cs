using System;
using System.Collections.Generic;
using Open.DataGrid.Columns;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// Tracks which columns were hidden by the <c>HiddenColumns</c> DP and applies
    /// visibility changes incrementally — hiding newly listed columns and restoring
    /// columns removed from the list, without touching columns whose
    /// <see cref="OpenDataGridColumn.IsVisible"/> was set outside this tracker.
    /// </summary>
    internal sealed class HiddenColumnsTracker
    {
        private readonly HashSet<OpenDataGridColumn> _hiddenByDp
            = new HashSet<OpenDataGridColumn>();

        /// <summary>
        /// Applies the current <paramref name="hiddenColumnsDp"/> value to
        /// <paramref name="columns"/>.
        /// </summary>
        /// <returns><c>true</c> if at least one column's <see cref="OpenDataGridColumn.IsVisible"/> changed.</returns>
        public bool Apply(IReadOnlyList<OpenDataGridColumn> columns,
                          string hiddenColumnsDp, bool matchByHeader)
        {
            var targets = ParseNames(hiddenColumnsDp);
            bool changed = false;

            foreach (var col in columns)
            {
                string key = matchByHeader ? col.Header?.Trim() : col.BindingPath?.Trim();
                if (string.IsNullOrEmpty(key)) continue;

                bool dpWantsHidden = targets.Contains(key);
                bool weHidIt       = _hiddenByDp.Contains(col);

                if (dpWantsHidden && !weHidIt && col.IsVisible)
                {
                    // DP newly lists this column and the column is currently visible
                    // (if it's already hidden manually, we don't take ownership).
                    col.IsVisible = false;
                    _hiddenByDp.Add(col);
                    changed = true;
                }
                else if (!dpWantsHidden && weHidIt)
                {
                    // DP no longer lists this column — restore it (we own it).
                    col.IsVisible = true;
                    _hiddenByDp.Remove(col);
                    changed = true;
                }
                // else: DP has nothing to say → leave IsVisible untouched.
            }

            return changed;
        }

        private static HashSet<string> ParseNames(string s)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(s)) return set;

            foreach (var part in s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = part.Trim();
                if (token.Length > 0) set.Add(token);
            }
            return set;
        }
    }
}
