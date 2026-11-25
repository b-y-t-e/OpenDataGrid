using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Accessors;

namespace Open.DataGrid.Columns
{
    public enum DateTimeDisplayMode { Date, DateTime, Time }

    public class OpenDataGridDateTimeColumn : OpenDataGridColumn
    {
        static OpenDataGridDateTimeColumn()
        {
            HorizontalContentAlignmentProperty.OverrideMetadata(
                typeof(OpenDataGridDateTimeColumn),
                new PropertyMetadata(HorizontalAlignment.Right));
        }

        // Empty string = auto-format from DisplayMode; non-empty = explicit override.
        public static readonly DependencyProperty DateFormatProperty =
            DependencyProperty.Register(nameof(DateFormat), typeof(string), typeof(OpenDataGridDateTimeColumn),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty DisplayModeProperty =
            DependencyProperty.Register(nameof(DisplayMode), typeof(DateTimeDisplayMode), typeof(OpenDataGridDateTimeColumn),
                new PropertyMetadata(DateTimeDisplayMode.Date));

        public static readonly DependencyProperty MinDateProperty =
            DependencyProperty.Register(nameof(MinDate), typeof(DateTime), typeof(OpenDataGridDateTimeColumn),
                new PropertyMetadata(DateTime.MinValue));

        public static readonly DependencyProperty MaxDateProperty =
            DependencyProperty.Register(nameof(MaxDate), typeof(DateTime), typeof(OpenDataGridDateTimeColumn),
                new PropertyMetadata(DateTime.MaxValue));

        public string DateFormat { get => (string)GetValue(DateFormatProperty); set => SetValue(DateFormatProperty, value); }
        public DateTimeDisplayMode DisplayMode { get => (DateTimeDisplayMode)GetValue(DisplayModeProperty); set => SetValue(DisplayModeProperty, value); }
        public DateTime MinDate { get => (DateTime)GetValue(MinDateProperty); set => SetValue(MinDateProperty, value); }
        public DateTime MaxDate { get => (DateTime)GetValue(MaxDateProperty); set => SetValue(MaxDateProperty, value); }

        private string GetEffectiveFormat()
        {
            if (!string.IsNullOrEmpty(DateFormat)) return DateFormat;
            switch (DisplayMode)
            {
                case DateTimeDisplayMode.DateTime: return "yyyy-MM-dd HH:mm:ss";
                case DateTimeDisplayMode.Time:     return "HH:mm:ss";
                default:                           return "yyyy-MM-dd";
            }
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
                Background = System.Windows.Media.Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1),
                BorderThickness = new Thickness(0)
            };
        }

        public override void ApplyDisplayValue(FrameworkElement element, object value)
        {
            if (element is TextBlock tb)
                tb.Text = value is DateTime dt ? dt.ToString(GetEffectiveFormat()) : value?.ToString() ?? string.Empty;
        }

        public override string GetClipboardText(object value)
            => value is DateTime dt ? dt.ToString(GetEffectiveFormat()) : value?.ToString() ?? string.Empty;

        public override void ApplyEditValue(FrameworkElement element, object value)
        {
            if (element is TextBox tb)
            {
                tb.Text = value is DateTime dt ? dt.ToString(GetEffectiveFormat()) : value?.ToString() ?? string.Empty;
                tb.SelectAll();
            }
        }

        public override object ExtractEditValue(FrameworkElement element)
        {
            if (element is TextBox tb && DateTime.TryParse(tb.Text, out DateTime result))
            {
                if (result < MinDate) result = MinDate;
                if (result > MaxDate) result = MaxDate;
                return result;
            }
            return NoValue;
        }

        public override bool CanBeginEdit(Key key) => key == Key.F2 || key == Key.Enter;

        public override Comparison<object> GetComparison()
        {
            var getter = PropertyAccessorCache.CreateGetter(BindingPath);
            return (a, b) =>
            {
                var va = getter(a) is DateTime dtA ? dtA : DateTime.MinValue;
                var vb = getter(b) is DateTime dtB ? dtB : DateTime.MinValue;
                return va.CompareTo(vb);
            };
        }

        internal override Filtering.IColumnFilterController CreateFilterController(Controls.OpenDataGrid grid)
            => new Filtering.Cells.TextFilterController(this, grid,
                string.IsNullOrEmpty(FilterStringFormat) ? GetEffectiveFormat() : FilterStringFormat);

        protected override Freezable CreateInstanceCore() => new OpenDataGridDateTimeColumn();
    }
}
