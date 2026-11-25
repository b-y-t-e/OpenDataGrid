using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Open.DataGrid.Accessors;

namespace Open.DataGrid.Columns
{
    public class OpenDataGridComboColumn : OpenDataGridColumn
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(OpenDataGridComboColumn),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(OpenDataGridComboColumn),
                new PropertyMetadata("Label"));

        public static readonly DependencyProperty ValueMemberPathProperty =
            DependencyProperty.Register(nameof(ValueMemberPath), typeof(string), typeof(OpenDataGridComboColumn),
                new PropertyMetadata("Id"));

        public IEnumerable ItemsSource { get => (IEnumerable)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
        public string DisplayMemberPath { get => (string)GetValue(DisplayMemberPathProperty); set => SetValue(DisplayMemberPathProperty, value); }
        public string ValueMemberPath { get => (string)GetValue(ValueMemberPathProperty); set => SetValue(ValueMemberPathProperty, value); }

        // Cache: value -> display text
        private readonly Dictionary<object, string> _lookupCache = new Dictionary<object, string>();
        private List<object> _itemsList = new List<object>();

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((OpenDataGridComboColumn)d).RebuildCache();
        }

        private void RebuildCache()
        {
            _lookupCache.Clear();
            _itemsList.Clear();
            if (ItemsSource == null) return;

            PropertyInfo valueProp = null;
            PropertyInfo displayProp = null;

            foreach (var item in ItemsSource)
            {
                _itemsList.Add(item);
                if (valueProp == null && item != null)
                {
                    valueProp = item.GetType().GetProperty(ValueMemberPath);
                    displayProp = item.GetType().GetProperty(DisplayMemberPath);
                }
                if (valueProp != null)
                {
                    var key = valueProp.GetValue(item);
                    var display = displayProp?.GetValue(item)?.ToString() ?? string.Empty;
                    if (key != null && !_lookupCache.ContainsKey(key))
                        _lookupCache[key] = display;
                }
            }
        }

        public string GetDisplayText(object value)
        {
            if (value == null) return string.Empty;
            return _lookupCache.TryGetValue(value, out var text) ? text : value.ToString();
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
            var combo = new ComboBox
            {
                Style = InternalStyles.ComboBox,
                Background = System.Windows.Media.Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1),
                BorderThickness = new Thickness(0),
                IsEditable = false
            };
            combo.Tag = this; // back-ref to column for value resolution
            return combo;
        }

        public override void ApplyDisplayValue(FrameworkElement element, object value)
        {
            if (element is TextBlock tb)
                tb.Text = GetDisplayText(value);
        }

        public override string GetClipboardText(object value) => GetDisplayText(value);

        public override void ApplyEditValue(FrameworkElement element, object value)
        {
            if (element is ComboBox combo)
            {
                // Rebuild items if needed
                if (combo.ItemsSource == null)
                {
                    combo.ItemsSource = _itemsList;
                    combo.DisplayMemberPath = DisplayMemberPath;
                    combo.SelectedValuePath = ValueMemberPath;
                }
                combo.SelectedValue = value;
            }
        }

        public override object ExtractEditValue(FrameworkElement element)
        {
            if (element is ComboBox combo && combo.SelectedItem != null)
            {
                var valueProp = combo.SelectedItem.GetType().GetProperty(ValueMemberPath);
                return valueProp?.GetValue(combo.SelectedItem);
            }
            return OpenDataGridColumn.NoValue;
        }

        public override void BeginCharacterEdit(FrameworkElement editElement, Key key)
        {
            if (editElement is ComboBox combo && (key == Key.Space || key == Key.F4))
                combo.IsDropDownOpen = true;
        }

        public override bool CanBeginEdit(Key key)
        {
            return key == Key.Space || key == Key.F2 || key == Key.F4 || key == Key.Enter;
        }

        public override Comparison<object> GetComparison()
        {
            var getter = PropertyAccessorCache.CreateGetter(BindingPath);
            return (a, b) =>
            {
                var va = GetDisplayText(getter(a));
                var vb = GetDisplayText(getter(b));
                return string.Compare(va, vb, StringComparison.CurrentCulture);
            };
        }

        internal override Filtering.IColumnFilterController CreateFilterController(Controls.OpenDataGrid grid)
            => new Filtering.Cells.ComboColumnFilterController(this, grid);

        protected override Freezable CreateInstanceCore() => new OpenDataGridComboColumn();
    }
}
