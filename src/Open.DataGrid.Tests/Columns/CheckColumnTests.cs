using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Columns
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class CheckColumnTests
    {
        // ------------------------------------------------------------------ helpers

        private static OpenDataGridCheckColumn CreateColumn(string bindingPath = null)
        {
            var col = new OpenDataGridCheckColumn { Width = 50 };
            if (bindingPath != null) col.BindingPath = bindingPath;
            return col;
        }

        private static OpenDataGrid CreateGridWithCheckColumn(string bindingPath = null)
        {
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.Columns.Add(CreateColumn(bindingPath));
            grid.CheckedItems = new ObservableCollection<SortItem>();
            return grid;
        }

        // ------------------------------------------------------------------ CanBeginEdit

        [Test]
        public void CanBeginEdit_AlwaysReturnsFalse()
        {
            // Given
            var col = CreateColumn();

            // When / Then
            Assert.That(col.CanBeginEdit(Key.Space), Is.False);
            Assert.That(col.CanBeginEdit(Key.F2), Is.False);
            Assert.That(col.CanBeginEdit(Key.Enter), Is.False);
            Assert.That(col.CanBeginEdit(Key.A), Is.False);
        }

        // ------------------------------------------------------------------ ApplyDisplayValue — unbound mode

        [Test]
        public void ApplyDisplayValue_UnboundMode_ShowsChecked_WhenItemInGrid()
        {
            // Given
            var data = new SortItem { Name = "X" };
            var grid = CreateGridWithCheckColumn();
            grid.DataSource = new System.Collections.Generic.List<SortItem> { data };

            var col = (OpenDataGridCheckColumn)grid.Columns[1];
            var cb = new CheckBox();
            grid.CheckedItems.Add(data);

            // When
            col.ApplyDisplayValue(cb, data); // unbound: value IS the dataItem

            // Then
            Assert.That(cb.IsChecked, Is.True);
        }

        [Test]
        public void ApplyDisplayValue_UnboundMode_ShowsUnchecked_WhenItemNotInGrid()
        {
            // Given
            var data = new SortItem { Name = "Y" };
            var grid = CreateGridWithCheckColumn();
            grid.DataSource = new System.Collections.Generic.List<SortItem> { data };

            var col = (OpenDataGridCheckColumn)grid.Columns[1];
            var cb = new CheckBox();

            // When
            col.ApplyDisplayValue(cb, data);

            // Then
            Assert.That(cb.IsChecked, Is.False);
        }

        [Test]
        public void ApplyDisplayValue_UnboundMode_ShowsUnchecked_WhenGridNotAttached()
        {
            // Given — column not attached to any grid
            var col = CreateColumn();
            var cb = new CheckBox();
            var data = new SortItem { Name = "Z" };

            // When
            col.ApplyDisplayValue(cb, data);

            // Then
            Assert.That(cb.IsChecked, Is.False);
        }

        // ------------------------------------------------------------------ ApplyDisplayValue — bound mode

        [Test]
        public void ApplyDisplayValue_BoundMode_ShowsChecked_WhenValueIsTrue()
        {
            // Given
            var col = CreateColumn("BoolValue");
            var cb = new CheckBox();

            // When
            col.ApplyDisplayValue(cb, true);

            // Then
            Assert.That(cb.IsChecked, Is.True);
        }

        [Test]
        public void ApplyDisplayValue_BoundMode_ShowsUnchecked_WhenValueIsFalse()
        {
            // Given
            var col = CreateColumn("BoolValue");
            var cb = new CheckBox();

            // When
            col.ApplyDisplayValue(cb, false);

            // Then
            Assert.That(cb.IsChecked, Is.False);
        }

        [Test]
        public void ApplyDisplayValue_BoundMode_ShowsUnchecked_WhenValueIsNull()
        {
            // Given
            var col = CreateColumn("BoolValue");
            var cb = new CheckBox();

            // When
            col.ApplyDisplayValue(cb, null);

            // Then
            Assert.That(cb.IsChecked, Is.False);
        }

        // ------------------------------------------------------------------ GetComparison — unbound mode

        [Test]
        public void GetComparison_UnboundMode_SortsCheckedBeforeUnchecked()
        {
            // Given
            var data1 = new SortItem { Name = "A" };
            var data2 = new SortItem { Name = "B" };
            var grid = CreateGridWithCheckColumn();
            grid.DataSource = new System.Collections.Generic.List<SortItem> { data1, data2 };
            grid.CheckedItems.Add(data1);

            var col = (OpenDataGridCheckColumn)grid.Columns[1];

            // When
            var comparer = col.GetComparison();
            int result = comparer(data1, data2); // checked vs unchecked

            // Then: data1 (checked=true=1) > data2 (unchecked=false=0) → positive
            Assert.That(result, Is.GreaterThan(0));
        }

        // ------------------------------------------------------------------ GetComparison — bound mode

        [Test]
        public void GetComparison_BoundMode_ComparesByPropertyValue()
        {
            // Given
            var data1 = new SortItem { Name = "A", BoolValue = true };
            var data2 = new SortItem { Name = "B", BoolValue = false };
            var col = CreateColumn("BoolValue");

            // When
            var comparer = col.GetComparison();
            int result = comparer(data1, data2); // true > false

            // Then
            Assert.That(result, Is.GreaterThan(0));
        }

        // ------------------------------------------------------------------ ApplyDisplayValue — null / bool? handling

        [Test]
        public void ApplyDisplayValue_BoundMode_NullableBoolTrue_SetsChecked()
        {
            // Given
            var col = CreateColumn("NullableBoolValue");
            var cb = new CheckBox();

            // When
            col.ApplyDisplayValue(cb, (bool?)true);

            // Then
            Assert.That(cb.IsChecked, Is.True);
        }

        [Test]
        public void ApplyDisplayValue_BoundMode_NullableBoolNull_TreatsAsFalse()
        {
            // Given
            var col = CreateColumn("NullableBoolValue");
            var cb = new CheckBox();

            // When
            col.ApplyDisplayValue(cb, (bool?)null);

            // Then
            Assert.That(cb.IsChecked, Is.False);
        }

        [Test]
        public void GetComparison_BoundMode_HandlesNullableBool()
        {
            // Given — null and false both map to false; true maps to true
            var trueItem  = new SortItem { NullableBoolValue = true };
            var falseItem = new SortItem { NullableBoolValue = false };
            var nullItem  = new SortItem { NullableBoolValue = null };
            var col = CreateColumn("NullableBoolValue");

            // When
            var cmp = col.GetComparison();

            // Then — true > false, true > null, false == null (all treated as false)
            Assert.That(cmp(trueItem,  falseItem), Is.GreaterThan(0));
            Assert.That(cmp(trueItem,  nullItem),  Is.GreaterThan(0));
            Assert.That(cmp(falseItem, nullItem),  Is.EqualTo(0));
            Assert.That(cmp(nullItem,  trueItem),  Is.LessThan(0));
        }

        // ------------------------------------------------------------------ CreateInstanceCore (Freezable)

        [Test]
        public void Clone_ReturnsSameType()
        {
            // Given
            var col = CreateColumn("BoolValue");

            // When
            var clone = col.Clone();

            // Then
            Assert.That(clone, Is.InstanceOf<OpenDataGridCheckColumn>());
        }
    }
}
