using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Open.DataGrid.Columns;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Columns
{
    /// <summary>
    /// Tests for OpenDataGridIntegerColumn — CanBeginEdit key filtering and
    /// GetComparison sort ordering.
    /// STA required because OpenDataGridIntegerColumn is a DependencyObject.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class IntegerColumnTests
    {
        private OpenDataGridIntegerColumn _column;

        private sealed class NestedSortItem
        {
            public NestedProduct Towar { get; set; }
        }

        private sealed class NestedProduct
        {
            public int Qty { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            _column = new OpenDataGridIntegerColumn
            {
                BindingPath = "IntValue",
                Minimum = -1000,
                Maximum = 1000
            };
        }

        // ── CanBeginEdit ──────────────────────────────────────────────────────

        [Test]
        [TestCase(System.Windows.Input.Key.D0,       ExpectedResult = true,  TestName = "Digit 0 triggers edit")]
        [TestCase(System.Windows.Input.Key.D9,       ExpectedResult = true,  TestName = "Digit 9 triggers edit")]
        [TestCase(System.Windows.Input.Key.NumPad0,  ExpectedResult = true,  TestName = "NumPad 0 triggers edit")]
        [TestCase(System.Windows.Input.Key.NumPad9,  ExpectedResult = true,  TestName = "NumPad 9 triggers edit")]
        [TestCase(System.Windows.Input.Key.OemMinus, ExpectedResult = true,  TestName = "Minus triggers edit")]
        [TestCase(System.Windows.Input.Key.Subtract, ExpectedResult = true,  TestName = "NumPad minus triggers edit")]
        [TestCase(System.Windows.Input.Key.F2,       ExpectedResult = true,  TestName = "F2 triggers edit")]
        [TestCase(System.Windows.Input.Key.Enter,    ExpectedResult = true,  TestName = "Enter triggers edit")]
        [TestCase(System.Windows.Input.Key.A,        ExpectedResult = false, TestName = "Letter A does not trigger edit")]
        [TestCase(System.Windows.Input.Key.Z,        ExpectedResult = false, TestName = "Letter Z does not trigger edit")]
        [TestCase(System.Windows.Input.Key.OemPeriod,ExpectedResult = false, TestName = "Period does not trigger edit")]
        [TestCase(System.Windows.Input.Key.Space,    ExpectedResult = false, TestName = "Space does not trigger edit")]
        [TestCase(System.Windows.Input.Key.F5,       ExpectedResult = false, TestName = "F5 does not trigger edit")]
        public bool CanBeginEdit_Key_ReturnsExpected(System.Windows.Input.Key key)
        {
            // Given: An integer column configured for editing.
            // When: CanBeginEdit is invoked for a keyboard key.
            // Then: It returns whether that key should start edit mode.
            return _column.CanBeginEdit(key);
        }

        // ── GetClearedValue ───────────────────────────────────────────────────

        [Test]
        public void GetClearedValue_WithinRange_ReturnsZeroInt()
        {
            // Given: A column whose range includes 0.
            // When: The cleared value is requested.
            var result = _column.GetClearedValue();

            // Then: 0 is returned, typed as int (matching the property type).
            Assert.That(result, Is.EqualTo(0));
            Assert.That(result, Is.TypeOf<int>());
        }

        [Test]
        public void GetClearedValue_MinimumAboveZero_ClampsToMinimum()
        {
            // Given: A column whose Minimum is above zero.
            _column.Minimum = 10;

            // When: The cleared value is requested.
            var result = (int)_column.GetClearedValue();

            // Then: 0 is clamped up to Minimum.
            Assert.That(result, Is.EqualTo(10));
        }

        // ── GetComparison ─────────────────────────────────────────────────────

        [Test]
        public void GetComparison_SortsAscendingByIntProperty()
        {
            // Given: Unsorted items with integer values.
            var items = new List<SortItem>
            {
                new SortItem { IntValue = 30 },
                new SortItem { IntValue = 10 },
                new SortItem { IntValue = 20 }
            };
            var cmp = _column.GetComparison();

            // When: Items are sorted with the column comparator.
            var sorted = items.OrderBy(x => (object)x, System.Collections.Generic.Comparer<object>.Create((a, b) => cmp(a, b)))
                              .Cast<SortItem>()
                              .ToList();

            // Then: Items are in ascending integer order.
            Assert.That(sorted[0].IntValue, Is.EqualTo(10));
            Assert.That(sorted[1].IntValue, Is.EqualTo(20));
            Assert.That(sorted[2].IntValue, Is.EqualTo(30));
        }

        [Test]
        public void GetComparison_EqualValues_ReturnsZero()
        {
            // Given: Two items with equal integer values.
            var a = new SortItem { IntValue = 5 };
            var b = new SortItem { IntValue = 5 };
            var cmp = _column.GetComparison();

            // When: The items are compared.
            var result = cmp(a, b);

            // Then: Comparison returns zero.
            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void GetComparison_NestedBindingPath_SortsAscendingByIntProperty()
        {
            // Given: An integer column bound to a nested numeric property.
            var column = new OpenDataGridIntegerColumn { BindingPath = "Towar.Qty" };
            var items = new List<NestedSortItem>
            {
                new NestedSortItem { Towar = new NestedProduct { Qty = 30 } },
                new NestedSortItem { Towar = new NestedProduct { Qty = 10 } },
                new NestedSortItem { Towar = new NestedProduct { Qty = 20 } }
            };
            var cmp = column.GetComparison();

            // When: Items are sorted with the nested-path comparator.
            var sorted = items.OrderBy(x => (object)x, System.Collections.Generic.Comparer<object>.Create((a, b) => cmp(a, b)))
                .Cast<NestedSortItem>()
                .ToList();

            // Then: Items are in ascending nested integer order.
            Assert.That(sorted[0].Towar.Qty, Is.EqualTo(10));
            Assert.That(sorted[1].Towar.Qty, Is.EqualTo(20));
            Assert.That(sorted[2].Towar.Qty, Is.EqualTo(30));
        }

        [Test]
        public void GetComparison_NullItem_TreatedAsMinValue()
        {
            // Given: One null item and one real item with IntValue = 0.
            var real = new SortItem { IntValue = 0 };
            var cmp = _column.GetComparison();

            // When: The comparator compares null against the real item.
            var result = cmp(null, real);

            // Then: Null is treated as smaller and sorts before zero.
            Assert.That(result, Is.LessThan(0));
        }
    }
}
