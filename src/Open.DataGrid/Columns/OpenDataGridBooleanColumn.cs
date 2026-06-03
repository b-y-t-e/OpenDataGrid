using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Accessors;

namespace Open.DataGrid.Columns
{
    public class OpenDataGridBooleanColumn : OpenDataGridColumn
    {
        static OpenDataGridBooleanColumn()
        {
            HorizontalContentAlignmentProperty.OverrideMetadata(
                typeof(OpenDataGridBooleanColumn),
                new PropertyMetadata(HorizontalAlignment.Center));
        }

        public static readonly DependencyProperty IsThreeStateProperty =
            DependencyProperty.Register(nameof(IsThreeState), typeof(bool), typeof(OpenDataGridBooleanColumn),
                new PropertyMetadata(false));

        public bool IsThreeState
        {
            get => (bool)GetValue(IsThreeStateProperty);
            set => SetValue(IsThreeStateProperty, value);
        }

        public override FrameworkElement CreateDisplayElement()
        {
            return new CheckBox
            {
                Style = InternalStyles.CheckBox,
                HorizontalAlignment = HorizontalContentAlignment,
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
                IsHitTestVisible = false
            };
        }

        public override FrameworkElement CreateEditElement()
        {
            return new CheckBox
            {
                Style = InternalStyles.CheckBox,
                HorizontalAlignment = HorizontalContentAlignment,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public override void ApplyDisplayValue(FrameworkElement element, object value)
        {
            if (element is CheckBox checkBox)
            {
                checkBox.IsThreeState = IsThreeState;
                checkBox.IsChecked = ToNullableBoolean(value);
            }
        }

        public override void ApplyEditValue(FrameworkElement element, object value)
        {
            if (element is CheckBox checkBox)
            {
                checkBox.IsThreeState = IsThreeState;
                checkBox.IsChecked = ToNullableBoolean(value);
            }
        }

        public override object ExtractEditValue(FrameworkElement element)
        {
            if (element is CheckBox checkBox)
                return IsThreeState ? (object)checkBox.IsChecked : checkBox.IsChecked == true;
            return OpenDataGridColumn.NoValue;
        }

        public override bool AbsorbNavigationKeysInEdit => true;

        public override bool CanBeginEdit(Key key)
        {
            return key == Key.Space
                || key == Key.F2
                || key == Key.Enter
                || key == Key.D0
                || key == Key.D1
                || key == Key.NumPad0
                || key == Key.NumPad1;
        }

        public override void BeginCharacterEdit(FrameworkElement editElement, Key key)
        {
            if (!(editElement is CheckBox checkBox)) return;

            if (key == Key.D1 || key == Key.NumPad1)
            {
                checkBox.IsChecked = true;
                return;
            }

            if (key == Key.D0 || key == Key.NumPad0)
            {
                checkBox.IsChecked = false;
                return;
            }

            if (key == Key.Space)
            {
                if (IsThreeState)
                {
                    if (checkBox.IsChecked == false) checkBox.IsChecked = true;
                    else if (checkBox.IsChecked == true) checkBox.IsChecked = null;
                    else checkBox.IsChecked = false;
                }
                else
                {
                    checkBox.IsChecked = checkBox.IsChecked != true;
                }
            }
        }

        public override Comparison<object> GetComparison()
        {
            var getter = PropertyAccessorCache.CreateGetter(BindingPath);
            return (a, b) => ToSortValue(getter(a)).CompareTo(ToSortValue(getter(b)));
        }

        private static bool? ToNullableBoolean(object value)
        {
            if (value == null) return null;
            if (value is bool b) return b;

            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) ? (bool?)parsed : null;
        }

        private static int ToSortValue(object value)
        {
            var b = ToNullableBoolean(value);
            if (!b.HasValue) return -1;
            return b.Value ? 1 : 0;
        }

        internal override Filtering.IColumnFilterController CreateFilterController(Controls.OpenDataGrid grid)
            => new Filtering.Cells.BooleanFilterController(this, grid);

        protected override Freezable CreateInstanceCore() => new OpenDataGridBooleanColumn();
    }
}
