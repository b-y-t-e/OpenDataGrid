using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Open.DataGrid.Accessors;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// A ComboBox that filters rows in a linked <see cref="OpenDataGrid"/> by an exact value match.
    /// The dropdown is populated with distinct values (sorted alphabetically) from the specified field.
    /// The first item is always empty — selecting it removes the filter.
    /// Becomes read-only (disabled) when <see cref="FilterField"/> is not set.
    /// Multiple instances linked to the same grid apply simultaneously (AND logic with other filters).
    /// </summary>
    public class OpenDataGridComboFilter : ComboBox
    {
        static OpenDataGridComboFilter()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(OpenDataGridComboFilter),
                new FrameworkPropertyMetadata(typeof(OpenDataGridComboFilter)));
        }

        #region Dependency Properties

        public static readonly DependencyProperty GridProperty =
            DependencyProperty.Register(
                nameof(Grid), typeof(OpenDataGrid), typeof(OpenDataGridComboFilter),
                new PropertyMetadata(null, OnGridChanged));

        /// <summary>The grid to filter. Set via ElementName binding in XAML.</summary>
        public OpenDataGrid Grid
        {
            get => (OpenDataGrid)GetValue(GridProperty);
            set => SetValue(GridProperty, value);
        }

        public static readonly DependencyProperty FilterFieldProperty =
            DependencyProperty.Register(
                nameof(FilterField), typeof(string), typeof(OpenDataGridComboFilter),
                new PropertyMetadata(string.Empty, OnFilterFieldChanged));

        /// <summary>
        /// Name of the property to filter on (required).
        /// When empty the combo is disabled and no filter is applied.
        /// </summary>
        public string FilterField
        {
            get => (string)GetValue(FilterFieldProperty);
            set => SetValue(FilterFieldProperty, value);
        }

        #endregion

        private DependencyPropertyDescriptor _dataSourceDpd;
        private Func<object, object> _cachedGetter;
        private string _cachedGetterField;
        // Suppress filter updates while items are being rebuilt
        private bool _suppressSelectionChanged;
        private bool _wasUnloaded;
        // Snapshot of the active filter value taken in Unloaded; restored in Loaded if still present.
        private string _savedFilterValue;

        public OpenDataGridComboFilter()
        {
            Unloaded += OnComboUnloaded;
            Loaded   += OnComboLoaded;
        }

        private void OnComboUnloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _wasUnloaded = true;
            // Snapshot the active selection before reset so it can be restored on reload.
            _savedFilterValue = SelectedIndex > 0 ? SelectedItem as string : null;
            var grid = Grid;
            UnsubscribeFromGrid(grid);          // removes DPD + UnregisterFilter
            grid?.NotifyFilterReady(this);      // release any pending-filter signal
        }

        private void OnComboLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!_wasUnloaded) return;
            _wasUnloaded = false;
            SubscribeToGrid(Grid);
            RebuildItems();  // resets SelectedIndex = 0; _suppressSelectionChanged protects this

            // Restore the previously active filter value if it still exists in the rebuilt list.
            // Setting SelectedIndex here fires OnSelectionChanged (suppress is false by now),
            // which calls RegisterFilter and re-applies the predicate.
            if (_savedFilterValue != null)
            {
                // Copy and clear first — setting SelectedIndex below fires OnSelectionChanged
                // which calls back into the grid. Clearing before avoids any re-entrant path
                // seeing a stale _savedFilterValue and attempting a double-restore.
                string toRestore = _savedFilterValue;
                _savedFilterValue = null;
                for (int i = 1; i < Items.Count; i++)
                {
                    if (string.Equals(Items[i] as string, toRestore, StringComparison.Ordinal))
                    {
                        SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private static void OnGridChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var combo = (OpenDataGridComboFilter)d;
            combo.UnsubscribeFromGrid((OpenDataGrid)e.OldValue);
            // When unloaded, skip SubscribeToGrid and RebuildItems — Loaded will call both once
            // the combo re-enters the visual tree. Subscribing here would produce a second DPD
            // listener on top of the one Loaded adds, causing duplicate OnGridDataSourceChanged
            // callbacks for every DataSource change.
            if (combo._wasUnloaded) return;
            combo.SubscribeToGrid((OpenDataGrid)e.NewValue);
            combo.RebuildItems();
        }

        private static void OnFilterFieldChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var combo = (OpenDataGridComboFilter)d;
            combo._cachedGetter = null;
            combo._cachedGetterField = null;
            combo.RebuildItems();
        }

        private void SubscribeToGrid(OpenDataGrid grid)
        {
            if (grid == null) return;
            _dataSourceDpd = DependencyPropertyDescriptor.FromProperty(
                OpenDataGrid.DataSourceProperty, typeof(OpenDataGrid));
            _dataSourceDpd?.AddValueChanged(grid, OnGridDataSourceChanged);
        }

        private void UnsubscribeFromGrid(OpenDataGrid grid)
        {
            if (grid == null) return;
            _dataSourceDpd?.RemoveValueChanged(grid, OnGridDataSourceChanged);
            _dataSourceDpd = null;
            grid.UnregisterFilter(this);
        }

        private void OnGridDataSourceChanged(object sender, EventArgs e)
        {
            // Invalidate cached getter — new DataSource may have a different element type.
            _cachedGetter = null;
            _cachedGetterField = null;
            RebuildItems();
        }

        private void RebuildItems()
        {
            _suppressSelectionChanged = true;
            try
            {
                string field = FilterField?.Trim() ?? string.Empty;
                bool hasField = field.Length > 0;

                IsEnabled = hasField;

                Items.Clear();
                Items.Add(string.Empty); // "no filter" entry at top

                if (hasField)
                {
                    var data = Grid?.DataSource;
                    if (data != null && data.Count > 0)
                    {
                        foreach (var v in CollectDistinctValues(data, field))
                            Items.Add(v);
                    }
                }

                SelectedIndex = 0;
            }
            finally
            {
                _suppressSelectionChanged = false;
            }

            // Remove any active filter — selection was reset to "all"
            Grid?.UnregisterFilter(this);
        }

        private List<string> CollectDistinctValues(IList dataSource, string fieldName)
        {
            EnsureGetter(dataSource, fieldName);
            if (_cachedGetter == null) return new List<string>();

            var seen   = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();

            int count = dataSource.Count;
            for (int i = 0; i < count; i++)
            {
                object item = dataSource[i];
                if (item == null) continue;
                string val = _cachedGetter(item)?.ToString() ?? string.Empty;
                if (seen.Add(val))
                    result.Add(val);
            }

            result.Sort(StringComparer.CurrentCultureIgnoreCase);
            return result;
        }

        private void EnsureGetter(IList dataSource, string fieldName)
        {
            if (_cachedGetter != null && _cachedGetterField == fieldName) return;

            _cachedGetter = null;
            _cachedGetterField = null;

            object first = dataSource?[0];
            if (first == null) return;

            Func<object, object> getter;
            if (PropertyAccessorCache.TryCreateGetter(first.GetType(), fieldName, out getter))
            {
                _cachedGetter = getter;
                _cachedGetterField = fieldName;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[OpenDataGridComboFilter] FilterField '{fieldName}' not found on type '{first.GetType().Name}'.");
            }
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            base.OnSelectionChanged(e);
            if (_suppressSelectionChanged) return;

            var grid = Grid;
            if (grid == null) return;

            string selected = SelectedItem as string;
            if (string.IsNullOrEmpty(selected))
            {
                grid.UnregisterFilter(this);
                return;
            }

            string fieldName = FilterField?.Trim() ?? string.Empty;
            if (fieldName.Length == 0) return;

            grid.RegisterFilter(this, BuildPredicate(fieldName, selected));
        }

        private Func<object, bool> BuildPredicate(string fieldName, string matchValue)
        {
            Func<object, object> getter = _cachedGetterField == fieldName ? _cachedGetter : null;

            return item =>
            {
                if (item == null) return false;
                if (getter == null)
                    PropertyAccessorCache.TryCreateGetter(item.GetType(), fieldName, out getter);
                if (getter == null) return false;
                string val = getter(item)?.ToString() ?? string.Empty;
                return string.Equals(val, matchValue, StringComparison.Ordinal);
            };
        }
    }
}
