using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Open.DataGrid;
using Open.DataGrid.Accessors;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;

namespace Open.DataGrid.Filtering.Cells
{
    internal sealed class BooleanFilterController : IColumnFilterController
    {
        private readonly OpenDataGridBooleanColumn _column;
        private readonly OpenDataGrid _grid;
        private readonly string _bindingPath;
        private readonly object _filterKey = new object();
        private readonly ComboBox _comboBox;

        private static string LabelTrue  => GetLabel(true);
        private static string LabelFalse => GetLabel(false);
        private static string LabelEmpty => GetEmptyLabel();

        private static string GetLabel(bool value)
        {
            switch (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            {
                case "pl": return value ? "Tak" : "Nie";
                case "uk": return value ? "Так" : "Ні";
                default:   return value ? "Yes" : "No";
            }
        }

        private static string GetEmptyLabel()
        {
            switch (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            {
                case "pl": return "(brak)";
                case "uk": return "(порожньо)";
                default:   return "(empty)";
            }
        }

        public FrameworkElement Editor => _comboBox;

        public BooleanFilterController(OpenDataGridBooleanColumn column, OpenDataGrid grid)
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
            _comboBox.Items.Add(string.Empty);
            _comboBox.Items.Add(LabelTrue);
            _comboBox.Items.Add(LabelFalse);
            if (column.IsThreeState)
                _comboBox.Items.Add(LabelEmpty);
            _comboBox.SelectedIndex = 0;
            _comboBox.SelectionChanged += OnSelectionChanged;
        }

        public void Attach() { }

        public void Detach() => _grid.UnregisterFilter(_filterKey);

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = _comboBox.SelectedIndex;
            if (idx <= 0) { _grid.UnregisterFilter(_filterKey); return; }

            string bindingPath = _bindingPath;
            var getter = PropertyAccessorCache.CreateGetter(bindingPath);
            Func<object, bool> predicate;
            switch (idx)
            {
                case 1:
                    predicate = item => item != null && getter(item) is bool b1 && b1;
                    break;
                case 2:
                    predicate = item => item != null && getter(item) is bool b2 && !b2;
                    break;
                case 3:
                    predicate = item => item != null && getter(item) == null;
                    break;
                default:
                    _grid.UnregisterFilter(_filterKey);
                    return;
            }
            _grid.RegisterFilter(_filterKey, predicate);
        }
    }
}
