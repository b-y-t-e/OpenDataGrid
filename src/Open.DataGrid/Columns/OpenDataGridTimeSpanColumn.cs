using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Accessors;

namespace Open.DataGrid.Columns
{
    public class OpenDataGridTimeSpanColumn : OpenDataGridColumn
    {
        // Format passed to TimeSpan.ToString(). Use verbatim escaping for colons: @"hh\:mm\:ss".
        public static readonly DependencyProperty TimeFormatProperty =
            DependencyProperty.Register(nameof(TimeFormat), typeof(string), typeof(OpenDataGridTimeSpanColumn),
                new PropertyMetadata(@"hh\:mm\:ss"));

        public string TimeFormat { get => (string)GetValue(TimeFormatProperty); set => SetValue(TimeFormatProperty, value); }

        private string FormatValue(object value)
        {
            if (value is TimeSpan ts)
                return ts.ToString(TimeFormat);
            return value?.ToString() ?? string.Empty;
        }

        public override FrameworkElement CreateDisplayElement()
        {
            return new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalContentAlignment,
                Margin = new Thickness(4, 0, 4, 0),
                TextTrimming = TextTrimming
            };
        }

        public override FrameworkElement CreateEditElement()
        {
            return new TextBox
            {
                Style = InternalStyles.TextBox,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1),
                BorderThickness = new Thickness(0)
            };
        }

        public override void ApplyDisplayValue(FrameworkElement element, object value)
        {
            if (element is TextBlock tb)
                tb.Text = FormatValue(value);
        }

        public override void ApplyEditValue(FrameworkElement element, object value)
        {
            if (element is TextBox tb)
            {
                tb.Text = FormatValue(value);
                tb.SelectAll();
            }
        }

        public override object ExtractEditValue(FrameworkElement element)
        {
            if (element is TextBox tb && TimeSpan.TryParse(tb.Text, out TimeSpan result))
                return result;
            return NoValue;
        }

        public override bool CanBeginEdit(Key key) => key == Key.F2 || key == Key.Enter;

        public override Comparison<object> GetComparison()
        {
            var getter = PropertyAccessorCache.CreateGetter(BindingPath);
            return (a, b) =>
            {
                var va = getter(a) is TimeSpan tsA ? tsA : TimeSpan.Zero;
                var vb = getter(b) is TimeSpan tsB ? tsB : TimeSpan.Zero;
                return va.CompareTo(vb);
            };
        }

        internal override Filtering.IColumnFilterController CreateFilterController(Controls.OpenDataGrid grid)
            => new Filtering.Cells.TextFilterController(this, grid,
                string.IsNullOrEmpty(FilterStringFormat) ? TimeFormat : FilterStringFormat);

        protected override Freezable CreateInstanceCore() => new OpenDataGridTimeSpanColumn();
    }
}
