using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Open.DataGrid;
using Open.DataGrid.Accessors;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;

namespace Open.DataGrid.Filtering.Cells
{
    internal sealed class TextFilterController : IColumnFilterController
    {
        private readonly OpenDataGridColumn _column;
        private readonly OpenDataGrid _grid;
        private readonly string _bindingPath;
        private readonly string _filterFormat;
        private readonly object _filterKey = new object();
        private readonly TextBox _textBox;
        private DispatcherTimer _debounceTimer;

        public FrameworkElement Editor => _textBox;

        public TextFilterController(OpenDataGridColumn column, OpenDataGrid grid, string filterFormat = null)
        {
            _column      = column;
            _grid        = grid;
            _bindingPath = column.BindingPath?.Trim() ?? string.Empty;
            _filterFormat = filterFormat;

            _textBox = new TextBox
            {
                Style                    = InternalStyles.TextBox,
                Padding                  = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = column.HorizontalContentAlignment,
                BorderThickness          = new Thickness(0),
                Background               = System.Windows.Media.Brushes.Transparent,
                IsEnabled                = _bindingPath.Length > 0
            };
            _textBox.TextChanged += OnTextChanged;
        }

        public void Attach() { }

        public void Detach()
        {
            _debounceTimer?.Stop();
            _grid.UnregisterFilter(_filterKey);
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_textBox.Text))
            {
                _debounceTimer?.Stop();
                _grid.UnregisterFilter(_filterKey);
                return;
            }

            if (_textBox.IsKeyboardFocused)
                ScheduleFilterUpdate();
            else
                ApplyFilter();
        }

        private void ScheduleFilterUpdate()
        {
            if (_debounceTimer == null)
            {
                _debounceTimer = new DispatcherTimer(DispatcherPriority.Background);
                _debounceTimer.Interval = TimeSpan.FromMilliseconds(300);
                _debounceTimer.Tick += (s, e) => { _debounceTimer.Stop(); ApplyFilter(); };
            }
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void ApplyFilter()
        {
            string text = _textBox.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                _grid.UnregisterFilter(_filterKey);
                return;
            }
            _grid.RegisterFilter(_filterKey, BuildPredicate(text));
        }

        private Func<object, bool> BuildPredicate(string filterText)
        {
            bool ignoreDiacritics = _column.IgnoreDiacritics;
            string format = string.IsNullOrEmpty(_filterFormat) ? null : _filterFormat;

            string[] terms = filterText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) return _ => true;

            if (ignoreDiacritics)
                for (int i = 0; i < terms.Length; i++)
                    terms[i] = DiacriticsHelper.RemoveDiacritics(terms[i]);

            string bindingPath = _bindingPath;
            if (bindingPath.Length == 0) return _ => true;

            var getter = PropertyAccessorCache.CreateGetter(bindingPath);

            return item =>
            {
                if (item == null) return false;
                object raw = getter(item);
                string val = ValueToString(raw, format);
                if (ignoreDiacritics && val != null)
                    val = DiacriticsHelper.RemoveDiacritics(val);

                for (int t = 0; t < terms.Length; t++)
                    if (val == null || val.IndexOf(terms[t], StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                return true;
            };
        }

        private static string ValueToString(object value, string format)
        {
            if (value == null) return string.Empty;
            if (format != null && value is IFormattable f)
                return f.ToString(format, CultureInfo.CurrentCulture);
            return value.ToString();
        }
    }
}
