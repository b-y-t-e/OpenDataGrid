using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using Open.DataGrid.Accessors;

namespace Open.DataGrid.Columns
{
    public class OpenDataGridLabelColumn : OpenDataGridColumn
    {
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

        public override FrameworkElement CreateEditElement() => null; // always readonly

        public override void ApplyDisplayValue(FrameworkElement element, object value)
        {
            if (element is TextBlock tb)
                tb.Text = value?.ToString() ?? string.Empty;
        }

        public override void ApplyEditValue(FrameworkElement element, object value) { }

        public override object ExtractEditValue(FrameworkElement element) => NoValue;

        public override bool CanBeginEdit(Key key) => false; // Label never editable

        public override Comparison<object> GetComparison()
        {
            var getter = PropertyAccessorCache.CreateGetter(BindingPath);
            return (a, b) =>
            {
                var va = getter(a)?.ToString() ?? string.Empty;
                var vb = getter(b)?.ToString() ?? string.Empty;
                return string.Compare(va, vb, StringComparison.CurrentCulture);
            };
        }

        internal override Filtering.IColumnFilterController CreateFilterController(Controls.OpenDataGrid grid)
            => new Filtering.Cells.TextFilterController(this, grid);

        protected override Freezable CreateInstanceCore() => new OpenDataGridLabelColumn();
    }
}
