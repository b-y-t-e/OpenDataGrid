using System;
using System.Windows;

namespace Open.DataGrid.Columns
{
    public class OpenDataGridIntegerColumn : OpenDataGridNumericColumnBase
    {
        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(OpenDataGridIntegerColumn),
                new PropertyMetadata(int.MinValue));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(OpenDataGridIntegerColumn),
                new PropertyMetadata(int.MaxValue));

        public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

        protected override string FormatValue(object value) =>
            value?.ToString() ?? string.Empty;

        protected override bool ValidateInput(string candidate)
        {
            if (candidate == "-") return true;
            return int.TryParse(candidate, out _);
        }

        protected override IComparable GetSortValue(object rawValue) =>
            rawValue is int i ? i : int.MinValue;

        public override object ExtractEditValue(FrameworkElement element)
        {
            if (element is System.Windows.Controls.TextBox tb)
            {
                var text = tb.Text;
                if (text == string.Empty || text == "-")
                    return Math.Max(Minimum, Math.Min(Maximum, 0));
                if (int.TryParse(text, out int result))
                    return Math.Max(Minimum, Math.Min(Maximum, result));
            }
            return NoValue;
        }

        public override object GetClearedValue() => Math.Max(Minimum, Math.Min(Maximum, 0));

        protected override Freezable CreateInstanceCore() => new OpenDataGridIntegerColumn();
    }
}
