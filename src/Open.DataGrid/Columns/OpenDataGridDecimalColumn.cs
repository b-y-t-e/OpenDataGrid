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

        public static readonly DependencyProperty TrimTrailingZerosProperty =
            DependencyProperty.Register(nameof(TrimTrailingZeros), typeof(bool), typeof(OpenDataGridDecimalColumn),
                new PropertyMetadata(false));

        public static readonly DependencyProperty EmptyWhenZeroProperty =
            DependencyProperty.Register(nameof(EmptyWhenZero), typeof(bool), typeof(OpenDataGridDecimalColumn),
                new PropertyMetadata(false));

        public decimal Minimum { get => (decimal)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public decimal Maximum { get => (decimal)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public int DecimalPlaces { get => (int)GetValue(DecimalPlacesProperty); set => SetValue(DecimalPlacesProperty, value); }

        /// <summary>
        /// When <c>true</c>, trailing zeros of the fractional part are trimmed in display mode
        /// ("4234.100" → "4234.1", "53463.000" → "53463"). Does not affect edit mode.
        /// </summary>
        public bool TrimTrailingZeros { get => (bool)GetValue(TrimTrailingZerosProperty); set => SetValue(TrimTrailingZerosProperty, value); }

        /// <summary>
        /// When <c>true</c>, a zero value (0, 0.0, ...) or <c>null</c> is displayed as an empty
        /// string in display mode. Does not affect edit mode.
        /// </summary>
        public bool EmptyWhenZero { get => (bool)GetValue(EmptyWhenZeroProperty); set => SetValue(EmptyWhenZeroProperty, value); }

        protected override string FormatValue(object value)
            => FormatDecimal(value, EmptyWhenZero, DecimalPlaces, TrimTrailingZeros, CultureInfo.CurrentCulture);

        internal override Func<object, string> CreateClipboardFormatter()
        {
            bool emptyWhenZero = EmptyWhenZero;
            int  decimalPlaces = DecimalPlaces;
            bool trimZeros     = TrimTrailingZeros;
            var  culture       = CultureInfo.CurrentCulture;
            return value => FormatDecimal(value, emptyWhenZero, decimalPlaces, trimZeros, culture);
        }

        private static string FormatDecimal(object value, bool emptyWhenZero, int decimalPlaces, bool trimZeros, CultureInfo culture)
        {
            if (!(value is decimal d))
                return value?.ToString() ?? string.Empty;

            if (emptyWhenZero && d == 0m)
                return string.Empty;

            var text = d.ToString("N" + decimalPlaces, culture);
            return trimZeros ? TrimZeros(text, culture) : text;
        }

        private static string TrimZeros(string text, CultureInfo culture)
        {
            var sep = culture.NumberFormat.NumberDecimalSeparator;
            int sepPos = text.IndexOf(sep, StringComparison.Ordinal);
            if (sepPos < 0)
                return text; // no fractional part

            text = text.TrimEnd('0');
            if (text.EndsWith(sep, StringComparison.Ordinal))
                text = text.Substring(0, text.Length - sep.Length); // drop the dangling separator
            return text;
        }

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

        public override object GetClearedValue() => Math.Max(Minimum, Math.Min(Maximum, 0m));

        protected override Freezable CreateInstanceCore() => new OpenDataGridDecimalColumn();
    }
}
