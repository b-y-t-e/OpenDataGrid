using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace Open.DataGrid.Columns
{
    public class OpenDataGridDecimalColumn : OpenDataGridNumericColumnBase
    {
        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(decimal), typeof(OpenDataGridDecimalColumn),
                new PropertyMetadata(decimal.MinValue));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(decimal), typeof(OpenDataGridDecimalColumn),
                new PropertyMetadata(decimal.MaxValue));

        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(OpenDataGridDecimalColumn),
                new PropertyMetadata(2));

        public decimal Minimum { get => (decimal)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public decimal Maximum { get => (decimal)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public int DecimalPlaces { get => (int)GetValue(DecimalPlacesProperty); set => SetValue(DecimalPlacesProperty, value); }

        protected override string FormatValue(object value) =>
            value is decimal d ? d.ToString("N" + DecimalPlaces) : value?.ToString() ?? string.Empty;

        protected override bool ValidateInput(string candidate)
        {
            var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            if (candidate == "-" || candidate == sep) return true;
            return decimal.TryParse(candidate, NumberStyles.Any, CultureInfo.CurrentCulture, out _);
        }

        protected override bool IsDecimalSeparatorKey(Key key) =>
            key == Key.Decimal || key == Key.OemPeriod || key == Key.OemComma;

        protected override string KeyToChar(Key key)
        {
            if (key == Key.Decimal || key == Key.OemPeriod || key == Key.OemComma)
                return CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            return base.KeyToChar(key);
        }

        protected override IComparable GetSortValue(object rawValue) =>
            rawValue is decimal d ? d : decimal.MinValue;

        public override object ExtractEditValue(FrameworkElement element)
        {
            if (element is System.Windows.Controls.TextBox tb)
            {
                var text = tb.Text.Replace(" ", "").Replace("\u00A0", "");
                if (text == string.Empty || text == "-")
                    return Math.Max(Minimum, Math.Min(Maximum, 0m));
                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out decimal result))
                    return Math.Max(Minimum, Math.Min(Maximum, result));
            }
            return NoValue;
        }

        protected override Freezable CreateInstanceCore() => new OpenDataGridDecimalColumn();
    }
}
