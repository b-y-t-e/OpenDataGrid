using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Open.DataGrid;
using Open.DataGrid.Accessors;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;

namespace Open.DataGrid.Filtering.Cells
{
    internal sealed class NumericFilterController : IColumnFilterController
    {
        private static readonly string[] OperatorLabels = { "~", "=", "≠", "<", "≤", ">", "≥" };
        private static readonly FilterOperator[] OperatorValues =
        {
            FilterOperator.Contains,
            FilterOperator.Equal,
            FilterOperator.NotEqual,
            FilterOperator.Less,
            FilterOperator.LessOrEqual,
            FilterOperator.Greater,
            FilterOperator.GreaterOrEqual
        };

        // ComboBox template without the dropdown arrow — just clickable text + popup
        private static readonly ControlTemplate OperatorComboTemplate = BuildOperatorComboTemplate();

        private static ControlTemplate BuildOperatorComboTemplate()
        {
            var outerBorder = new FrameworkElementFactory(typeof(Border));
            outerBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
            outerBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            outerBorder.SetValue(Border.PaddingProperty, new Thickness(3, 0, 3, 0));

            var root = new FrameworkElementFactory(typeof(Grid));
            outerBorder.AppendChild(root);

            // Transparent toggle button covering the whole cell — click opens the popup
            var toggleButton = new FrameworkElementFactory(typeof(ToggleButton));
            toggleButton.SetValue(ToggleButton.FocusableProperty, false);
            toggleButton.SetBinding(ToggleButton.IsCheckedProperty,
                new Binding("IsDropDownOpen") { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });

            var tbBorder = new FrameworkElementFactory(typeof(Border));
            tbBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var tbTemplate = new ControlTemplate(typeof(ToggleButton)) { VisualTree = tbBorder };
            tbTemplate.Seal();
            toggleButton.SetValue(ToggleButton.TemplateProperty, tbTemplate);
            root.AppendChild(toggleButton);

            // Selected item label — sits on top, not hit-test visible so clicks go to the toggle button
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetBinding(ContentPresenter.ContentProperty,
                new Binding("SelectionBoxItem") { RelativeSource = RelativeSource.TemplatedParent });
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.IsHitTestVisibleProperty, false);
            root.AppendChild(cp);

            // Dropdown popup
            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.SetValue(Popup.NameProperty, "PART_Popup");
            popup.SetBinding(Popup.IsOpenProperty,
                new Binding("IsDropDownOpen") { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.StaysOpenProperty, false);
            popup.SetBinding(Popup.PlacementTargetProperty,
                new Binding { RelativeSource = RelativeSource.TemplatedParent });

            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, SystemColors.WindowBrush);
            popupBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewer.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scrollViewer.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            popupBorder.AppendChild(scrollViewer);
            popup.AppendChild(popupBorder);
            root.AppendChild(popup);

            var template = new ControlTemplate(typeof(ComboBox)) { VisualTree = outerBorder };
            template.Seal();
            return template;
        }

        private readonly OpenDataGrid _grid;
        private readonly string _bindingPath;
        private readonly string _filterFormat;
        private readonly object _filterKey = new object();
        private readonly Grid _editor;
        private readonly ComboBox _operatorCombo;
        private readonly TextBox _valueTextBox;
        private DispatcherTimer _debounceTimer;

        public FrameworkElement Editor => _editor;

        public NumericFilterController(OpenDataGridColumn column, OpenDataGrid grid, string filterFormat = null)
        {
            _grid         = grid;
            _bindingPath  = column.BindingPath?.Trim() ?? string.Empty;
            _filterFormat = filterFormat;

            _operatorCombo = new ComboBox
            {
                Style                    = InternalStyles.ComboBox,
                Template                 = OperatorComboTemplate,
                BorderThickness          = new Thickness(0),
                Padding                  = new Thickness(3, 0, 3, 0),
                Background               = Brushes.Transparent,
                VerticalAlignment        = VerticalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEditable               = false,
                FontSize                 = 11
            };
            foreach (var label in OperatorLabels)
                _operatorCombo.Items.Add(label);
            _operatorCombo.SelectedIndex = 0;
            _operatorCombo.SelectionChanged += OnOperatorChanged;

            _valueTextBox = new TextBox
            {
                Style                      = InternalStyles.TextBox,
                Padding                    = new Thickness(2, 2, 2, 2),
                VerticalContentAlignment   = VerticalAlignment.Center,
                HorizontalContentAlignment = column.HorizontalContentAlignment,
                BorderThickness            = new Thickness(0),
                Background                 = Brushes.Transparent,
                IsEnabled                  = _bindingPath.Length > 0
            };
            _valueTextBox.TextChanged += OnTextChanged;

            _editor = new Grid();
            _editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_operatorCombo, 0);
            Grid.SetColumn(_valueTextBox, 1);
            _editor.Children.Add(_operatorCombo);
            _editor.Children.Add(_valueTextBox);
        }

        public void Attach() { }

        public void Detach()
        {
            _debounceTimer?.Stop();
            _grid.UnregisterFilter(_filterKey);
        }

        private void OnOperatorChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_valueTextBox.Text))
                ApplyFilter();
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_valueTextBox.Text))
            {
                _debounceTimer?.Stop();
                _grid.UnregisterFilter(_filterKey);
                return;
            }

            if (_valueTextBox.IsKeyboardFocused)
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
            string text = _valueTextBox.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                _grid.UnregisterFilter(_filterKey);
                return;
            }

            var op = OperatorValues[Math.Max(0, _operatorCombo.SelectedIndex)];
            var predicate = BuildPredicate(text, op);
            if (predicate != null)
                _grid.RegisterFilter(_filterKey, predicate);
            else
                _grid.UnregisterFilter(_filterKey);
        }

        private Func<object, bool> BuildPredicate(string filterText, FilterOperator op)
        {
            string bindingPath = _bindingPath;
            if (bindingPath.Length == 0) return null;

            string format = string.IsNullOrEmpty(_filterFormat) ? null : _filterFormat;
            var getter = PropertyAccessorCache.CreateGetter(bindingPath);

            if (op == FilterOperator.Contains)
            {
                string[] terms = filterText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length == 0) return null;
                return item =>
                {
                    if (item == null) return false;
                    string val = NumericToString(getter(item), format);
                    for (int t = 0; t < terms.Length; t++)
                        if (val.IndexOf(terms[t], StringComparison.OrdinalIgnoreCase) < 0)
                            return false;
                    return true;
                };
            }

            if (!decimal.TryParse(filterText, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal filterVal))
                return null;

            return item =>
            {
                if (item == null) return false;
                object raw = getter(item);
                if (raw == null) return false;
                decimal? itemVal = ToDecimal(raw);
                if (itemVal == null) return false;

                switch (op)
                {
                    case FilterOperator.Equal:          return itemVal.Value == filterVal;
                    case FilterOperator.NotEqual:       return itemVal.Value != filterVal;
                    case FilterOperator.Less:           return itemVal.Value <  filterVal;
                    case FilterOperator.LessOrEqual:    return itemVal.Value <= filterVal;
                    case FilterOperator.Greater:        return itemVal.Value >  filterVal;
                    case FilterOperator.GreaterOrEqual: return itemVal.Value >= filterVal;
                    default:                            return false;
                }
            };
        }

        private static string NumericToString(object value, string format)
        {
            if (value == null) return string.Empty;
            if (format != null && value is IFormattable f)
                return f.ToString(format, CultureInfo.CurrentCulture);
            return value.ToString();
        }

        private static decimal? ToDecimal(object value)
        {
            try { return Convert.ToDecimal(value, CultureInfo.CurrentCulture); }
            catch { return null; }
        }
    }
}
