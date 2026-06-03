using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Open.DataGrid;
using Open.DataGrid.Accessors;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;

namespace Open.DataGrid.Filtering.Cells
{
    internal sealed class ComboColumnFilterController : IColumnFilterController
    {
        private readonly OpenDataGridComboColumn _column;
        private readonly OpenDataGrid _grid;
        private readonly string _bindingPath;
        private readonly object _filterKey = new object();
        private readonly ComboBox _comboBox;
        private DependencyPropertyDescriptor _dataSourceDpd;
        private DependencyPropertyDescriptor _itemsSourceDpd;
        private bool _suppressSelectionChanged;

        public FrameworkElement Editor => _comboBox;

        public ComboColumnFilterController(OpenDataGridComboColumn column, OpenDataGrid grid)
        {
            _column      = column;
            _grid        = grid;
            _bindingPath = column.BindingPath?.Trim() ?? string.Empty;

            _comboBox = new ComboBox
            {
                Style                      = InternalStyles.ComboBox,
                BorderThickness            = new Thickness(0),
                Background                 = Brushes.Transparent,
                HorizontalContentAlignment = column.HorizontalContentAlignment,
                IsEditable                 = false,
                IsEnabled                  = _bindingPath.Length > 0
            };
            _comboBox.SelectionChanged += OnSelectionChanged;
        }

        public void Attach()
        {
            _dataSourceDpd = DependencyPropertyDescriptor.FromProperty(
                OpenDataGrid.DataSourceProperty, typeof(OpenDataGrid));
            _dataSourceDpd?.AddValueChanged(_grid, OnDataSourceChanged);

            _itemsSourceDpd = DependencyPropertyDescriptor.FromProperty(
                OpenDataGridComboColumn.ItemsSourceProperty, typeof(OpenDataGridComboColumn));
            _itemsSourceDpd?.AddValueChanged(_column, OnItemsSourceChanged);

            RebuildItems();
        }

        public void Detach()
        {
            _dataSourceDpd?.RemoveValueChanged(_grid, OnDataSourceChanged);
            _dataSourceDpd = null;
            _itemsSourceDpd?.RemoveValueChanged(_column, OnItemsSourceChanged);
            _itemsSourceDpd = null;
            _grid.UnregisterFilter(_filterKey);
        }

        private void OnDataSourceChanged(object sender, EventArgs e)
        {
            string saved = _comboBox.SelectedIndex > 0 ? _comboBox.SelectedItem as string : null;
            RebuildItems();
            if (saved != null)
                RestoreSelection(saved);
        }

        private void OnItemsSourceChanged(object sender, EventArgs e)
        {
            string saved = _comboBox.SelectedIndex > 0 ? _comboBox.SelectedItem as string : null;
            RebuildItems();
            if (saved != null)
                RestoreSelection(saved);
        }

        private void RebuildItems()
        {
            _suppressSelectionChanged = true;
            try
            {
                _comboBox.Items.Clear();
                _comboBox.Items.Add(string.Empty);

                var data = _grid.DataSource;
                if (data != null && data.Count > 0 && _bindingPath.Length > 0)
                    foreach (var v in CollectDistinctDisplayValues(data))
                        _comboBox.Items.Add(v);

                _comboBox.SelectedIndex = 0;
            }
            finally
            {
                _suppressSelectionChanged = false;
            }
            _grid.UnregisterFilter(_filterKey);
        }

        private void RestoreSelection(string value)
        {
            for (int i = 1; i < _comboBox.Items.Count; i++)
            {
                if (string.Equals(_comboBox.Items[i] as string, value, StringComparison.Ordinal))
                {
                    _comboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        private List<string> CollectDistinctDisplayValues(IList dataSource)
        {
            var getter = PropertyAccessorCache.CreateGetter(_bindingPath);
            var seen   = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            int count  = dataSource.Count;
            for (int i = 0; i < count; i++)
            {
                object item = dataSource[i];
                if (item == null) continue;
                string display = _column.GetDisplayText(getter(item));
                if (seen.Add(display))
                    result.Add(display);
            }
            result.Sort(StringComparer.CurrentCultureIgnoreCase);
            return result;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionChanged) return;

            string selected = _comboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selected))
            {
                _grid.UnregisterFilter(_filterKey);
                return;
            }

            string bindingPath = _bindingPath;
            var getter = PropertyAccessorCache.CreateGetter(bindingPath);
            _grid.RegisterFilter(_filterKey, item =>
            {
                if (item == null) return false;
                return string.Equals(_column.GetDisplayText(getter(item)), selected, StringComparison.Ordinal);
            });
        }
    }
}
