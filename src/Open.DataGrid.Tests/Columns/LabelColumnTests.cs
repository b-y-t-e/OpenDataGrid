using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Open.DataGrid.Columns;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Columns
{
    /// <summary>
    /// Tests for OpenDataGridLabelColumn — always read-only, string-based sort comparison.
    /// STA required because DependencyObject.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class LabelColumnTests
    {
        private OpenDataGridLabelColumn _column;

        private sealed class NestedSortItem
        {
            public NestedProduct Towar { get; set; }
        }

        private sealed class NestedProduct
        {
            public string Symbol { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            _column = new OpenDataGridLabelColumn { BindingPath = "Name" };
        }

        // ── IsVisible DP ───────────────────────────────────────────────────────

        [Test]
        public void IsVisible_DefaultIsTrue()
        {
            // Given: A new label column.

            // When: IsVisible is read.
            var isVisible = _column.IsVisible;

            // Then: Default value is true.
            Assert.That(isVisible, Is.True);
        }

        [Test]
        public void IsVisible_RoundTrips()
        {
            // Given: A new label column.

            // When: IsVisible is set to false.
            _column.IsVisible = false;

            // Then: Property returns false.
            Assert.That(_column.IsVisible, Is.False);
        }

        // ── CanBeginEdit — must always return false ────────────────────────────

        [Test]
        [TestCase(System.Windows.Input.Key.A,     TestName = "Letter A")]
        [TestCase(System.Windows.Input.Key.D0,    TestName = "Digit 0")]
        [TestCase(System.Windows.Input.Key.F2,    TestName = "F2")]
        [TestCase(System.Windows.Input.Key.Enter, TestName = "Enter")]
        [TestCase(System.Windows.Input.Key.Space, TestName = "Space")]
        public void CanBeginEdit_AnyKey_AlwaysFalse(System.Windows.Input.Key key)
        {
            // Given: A label column that is always read-only.
            // When: CanBeginEdit is called with any keyboard key.
            // Then: The method always returns false.
            Assert.That(_column.CanBeginEdit(key), Is.False);
        }

        // ── GetComparison ─────────────────────────────────────────────────────

        [Test]
        public void GetComparison_SortsAlphabetically()
        {
            // Given: Unsorted items with string Name values.
            var items = new List<SortItem>
            {
                new SortItem { Name = "Zebra"  },
                new SortItem { Name = "Apple"  },
                new SortItem { Name = "Mango"  }
            };
            var cmp = _column.GetComparison();

            // When: Items are sorted using the label-column comparison.
            var sorted = items.OrderBy(x => (object)x, System.Collections.Generic.Comparer<object>.Create((a, b) => cmp(a, b)))
                              .Cast<SortItem>()
                              .ToList();

            // Then: Items are sorted alphabetically.
            Assert.That(sorted[0].Name, Is.EqualTo("Apple"));
            Assert.That(sorted[1].Name, Is.EqualTo("Mango"));
            Assert.That(sorted[2].Name, Is.EqualTo("Zebra"));
        }

        [Test]
        public void GetComparison_EqualStrings_ReturnsZero()
        {
            // Given: Two items with identical Name values.
            var a = new SortItem { Name = "Same" };
            var b = new SortItem { Name = "Same" };

            // When: The comparison delegate compares both items.
            var result = _column.GetComparison()(a, b);

            // Then: Comparison result is zero.
            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void GetComparison_NestedBindingPath_SortsAlphabetically()
        {
            // Given: A label column bound to a nested string property.
            var column = new OpenDataGridLabelColumn { BindingPath = "Towar.Symbol" };
            var items = new List<NestedSortItem>
            {
                new NestedSortItem { Towar = new NestedProduct { Symbol = "Zebra" } },
                new NestedSortItem { Towar = new NestedProduct { Symbol = "Apple" } },
                new NestedSortItem { Towar = new NestedProduct { Symbol = "Mango" } }
            };
            var cmp = column.GetComparison();

            // When: Items are sorted using the nested-path comparison.
            var sorted = items.OrderBy(x => (object)x, System.Collections.Generic.Comparer<object>.Create((a, b) => cmp(a, b)))
                .Cast<NestedSortItem>()
                .ToList();

            // Then: Items are sorted alphabetically by the nested property.
            Assert.That(sorted[0].Towar.Symbol, Is.EqualTo("Apple"));
            Assert.That(sorted[1].Towar.Symbol, Is.EqualTo("Mango"));
            Assert.That(sorted[2].Towar.Symbol, Is.EqualTo("Zebra"));
        }

        [Test]
        public void GetComparison_NullName_SortsBeforeNonNull()
        {
            // Given: One item with null Name and one with a non-null Name.
            var nullItem = new SortItem { Name = null };
            var realItem = new SortItem { Name = "A" };
            var cmp = _column.GetComparison();

            // When: The items are compared.
            var result = cmp(nullItem, realItem);

            // Then: Null (treated as empty) sorts before "A".
            Assert.That(result, Is.LessThan(0));
        }

        [Test]
        public void GetComparison_NullItem_HandledGracefully()
        {
            // Given: A null item and a valid item.
            var real = new SortItem { Name = "X" };
            var cmp = _column.GetComparison();

            // When: The comparator is invoked with a null item.
            TestDelegate act = () => cmp(null, real);

            // Then: No exception is thrown.
            Assert.DoesNotThrow(act);
        }
    }
}
