using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Accessors;

namespace Open.DataGrid.Columns
{
    public class OpenDataGridCustomColumn : OpenDataGridColumn
    {
        public static readonly DependencyProperty CellTemplateProperty =
            DependencyProperty.Register(nameof(CellTemplate), typeof(DataTemplate), typeof(OpenDataGridCustomColumn));

        public static readonly DependencyProperty CellEditingTemplateProperty =
            DependencyProperty.Register(nameof(CellEditingTemplate), typeof(DataTemplate), typeof(OpenDataGridCustomColumn));

        public DataTemplate CellTemplate { get => (DataTemplate)GetValue(CellTemplateProperty); set => SetValue(CellTemplateProperty, value); }
        public DataTemplate CellEditingTemplate { get => (DataTemplate)GetValue(CellEditingTemplateProperty); set => SetValue(CellEditingTemplateProperty, value); }

        public override FrameworkElement CreateDisplayElement()
        {
            if (CellTemplate != null)
            {
                var content = CellTemplate.LoadContent() as FrameworkElement;
                return content ?? CreateFallbackElement();
            }
            return CreateFallbackElement();
        }

        public override FrameworkElement CreateEditElement()
        {
            if (CellEditingTemplate != null)
            {
                var content = CellEditingTemplate.LoadContent() as FrameworkElement;
                return content;
            }
            return null;
        }

        public override void ApplyDisplayValue(FrameworkElement element, object value)
        {
            if (element != null)
                element.DataContext = value;
        }

        public override void ApplyEditValue(FrameworkElement element, object value)
        {
            if (element != null)
                element.DataContext = value;
        }

        public override object ExtractEditValue(FrameworkElement element)
        {
            return element?.DataContext ?? NoValue;
        }

        public override bool CanBeginEdit(Key key) => key == Key.F2 || key == Key.Enter;

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

        private FrameworkElement CreateFallbackElement()
        {
            return new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
        }

        internal override Filtering.IColumnFilterController CreateFilterController(Controls.OpenDataGrid grid)
        {
            string path = BindingPath?.Trim() ?? string.Empty;
            if (path.Length == 0) return null;
            return new Filtering.Cells.TextFilterController(this, grid);
        }

        protected override Freezable CreateInstanceCore() => new OpenDataGridCustomColumn();
    }
}
