using System.Collections.Generic;
using System.Threading;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Columns
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class HiddenColumnsTrackerTests
    {
        private static OpenDataGridLabelColumn Col(string header, string bindingPath = null)
            => new OpenDataGridLabelColumn { Header = header, BindingPath = bindingPath ?? header };

        private static IReadOnlyList<OpenDataGridColumn> List(params OpenDataGridColumn[] cols)
            => cols;

        // -----------------------------------------------------------------------
        // Basic hide / show
        // -----------------------------------------------------------------------

        [Test]
        public void Apply_HidesColumnListedInDp()
        {
            // Given
            var tracker = new HiddenColumnsTracker();
            var a = Col("A"); var b = Col("B");

            // When
            tracker.Apply(List(a, b), "A", matchByHeader: true);

            // Then
            Assert.That(a.IsVisible, Is.False);
            Assert.That(b.IsVisible, Is.True);
        }

        [Test]
        public void Apply_RestoresColumnRemovedFromDp()
        {
            // Given: DP hides A, then A is removed from the list.
            var tracker = new HiddenColumnsTracker();
            var a = Col("A"); var b = Col("B");
            tracker.Apply(List(a, b), "A", matchByHeader: true);

            // When: DP no longer lists A.
            tracker.Apply(List(a, b), "", matchByHeader: true);

            // Then: A is visible again.
            Assert.That(a.IsVisible, Is.True);
            Assert.That(b.IsVisible, Is.True);
        }

        [Test]
        public void Apply_ReturnsTrue_WhenVisibilityChanges()
        {
            // Given
            var tracker = new HiddenColumnsTracker();
            var a = Col("A");

            // When
            bool result = tracker.Apply(List(a), "A", matchByHeader: true);

            // Then
            Assert.That(result, Is.True);
        }

        [Test]
        public void Apply_ReturnsFalse_WhenNothingChanges()
        {
            // Given: A is already hidden, DP still lists it.
            var tracker = new HiddenColumnsTracker();
            var a = Col("A");
            tracker.Apply(List(a), "A", matchByHeader: true);

            // When: Apply called again with same DP value.
            bool result = tracker.Apply(List(a), "A", matchByHeader: true);

            // Then
            Assert.That(result, Is.False);
        }

        // -----------------------------------------------------------------------
        // Manual IsVisible must not be clobbered
        // -----------------------------------------------------------------------

        [Test]
        public void Apply_DoesNotShowColumnManuallyHidden_WhenNotInDp()
        {
            // Given: Column B was hidden manually (not through the DP).
            var tracker = new HiddenColumnsTracker();
            var a = Col("A"); var b = Col("B");
            b.IsVisible = false;

            // When: DP only lists A.
            tracker.Apply(List(a, b), "A", matchByHeader: true);

            // Then: B stays hidden — tracker must not restore it.
            Assert.That(b.IsVisible, Is.False);
        }

        [Test]
        public void Apply_DoesNotTakeOwnership_OfManuallyHiddenColumnListedInDp()
        {
            // Given: B is already manually hidden.
            var tracker = new HiddenColumnsTracker();
            var b = Col("B");
            b.IsVisible = false;

            // When: DP lists B (B is already hidden, so tracker must NOT claim ownership).
            tracker.Apply(List(b), "B", matchByHeader: true);

            // Then: B stays hidden.
            Assert.That(b.IsVisible, Is.False);

            // When: DP clears B (tracker never owned B → must not restore it).
            tracker.Apply(List(b), "", matchByHeader: true);

            // Then: B still hidden — tracker never took ownership.
            Assert.That(b.IsVisible, Is.False);
        }

        [Test]
        public void Apply_AfterUserManuallyShowsColumnDpStillLists_LeavesColumnVisible()
        {
            // Given: DP hides A, then user sets A.IsVisible = true manually.
            var tracker = new HiddenColumnsTracker();
            var a = Col("A");
            tracker.Apply(List(a), "A", matchByHeader: true);
            a.IsVisible = true; // user override

            // When: DP is cleared (A removed from list).
            tracker.Apply(List(a), "", matchByHeader: true);

            // Then: A remains visible (tracker tries to "restore" it, but IsVisible is already true — no harm).
            Assert.That(a.IsVisible, Is.True);
        }

        // -----------------------------------------------------------------------
        // Parsing: separators, case, whitespace
        // -----------------------------------------------------------------------

        [Test]
        public void Apply_IsCaseInsensitive()
        {
            var tracker = new HiddenColumnsTracker();
            var a = Col("Name");

            tracker.Apply(List(a), "nAmE", matchByHeader: true);

            Assert.That(a.IsVisible, Is.False);
        }

        [Test]
        public void Apply_TrimsWhitespace()
        {
            var tracker = new HiddenColumnsTracker();
            var a = Col("Name");

            tracker.Apply(List(a), "  Name  ", matchByHeader: true);

            Assert.That(a.IsVisible, Is.False);
        }

        [Test]
        public void Apply_SemicolonSeparator()
        {
            var tracker = new HiddenColumnsTracker();
            var a = Col("A"); var b = Col("B");

            tracker.Apply(List(a, b), "A;B", matchByHeader: true);

            Assert.That(a.IsVisible, Is.False);
            Assert.That(b.IsVisible, Is.False);
        }

        [Test]
        public void Apply_MatchByBindingPath_HidesCorrectColumn()
        {
            var tracker = new HiddenColumnsTracker();
            var col = new OpenDataGridLabelColumn { Header = "Qty", BindingPath = "Quantity" };

            tracker.Apply(List(col), "Quantity", matchByHeader: false);

            Assert.That(col.IsVisible, Is.False);
        }

        [Test]
        public void Apply_MatchByBindingPath_HeaderOnlyToken_DoesNotHideColumn()
        {
            var tracker = new HiddenColumnsTracker();
            var col = new OpenDataGridLabelColumn { Header = "Qty", BindingPath = "Quantity" };

            tracker.Apply(List(col), "Qty", matchByHeader: false);

            Assert.That(col.IsVisible, Is.True);
        }

        [Test]
        public void Apply_ColumnWithNullKey_IsNeverHidden()
        {
            var tracker = new HiddenColumnsTracker();
            var col = new OpenDataGridLabelColumn { Header = null, BindingPath = null };

            tracker.Apply(List(col), "null", matchByHeader: true);

            Assert.That(col.IsVisible, Is.True);
        }

        // -----------------------------------------------------------------------
        // Dynamic DP changes
        // -----------------------------------------------------------------------

        [Test]
        public void Apply_ChangingDpSet_HidesAndRestoresCorrectly()
        {
            // Given
            var tracker = new HiddenColumnsTracker();
            var a = Col("A"); var b = Col("B"); var c = Col("C");

            // When: first apply hides A
            tracker.Apply(List(a, b, c), "A", matchByHeader: true);
            Assert.That(a.IsVisible, Is.False);
            Assert.That(b.IsVisible, Is.True);

            // When: change to hide B and C, restore A
            tracker.Apply(List(a, b, c), "B,C", matchByHeader: true);

            // Then
            Assert.That(a.IsVisible, Is.True);
            Assert.That(b.IsVisible, Is.False);
            Assert.That(c.IsVisible, Is.False);
        }
    }
}
