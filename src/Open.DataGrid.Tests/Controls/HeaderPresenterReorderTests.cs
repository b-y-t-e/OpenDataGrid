using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Controls
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class HeaderPresenterReorderTests
    {
        private static OpenDataGrid CreateGrid(params string[] headers)
        {
            var grid = new OpenDataGrid();
            double w = 100;
            foreach (var h in headers)
                grid.Columns.Add(new OpenDataGridLabelColumn { Header = h, BindingPath = h, Width = w });
            return grid;
        }

        private static List<string> VisibleHeaders(OpenDataGrid grid)
            => grid.GetVisibleColumnList().Select(c => c.Header).ToList();

        private static List<string> AllHeaders(OpenDataGrid grid)
            => grid.Columns.Cast<OpenDataGridColumn>().Select(c => c.Header).ToList();

        // -----------------------------------------------------------------------

        [Test]
        public void AllowColumnReorder_Default_IsTrue()
        {
            // Given / When: A new grid.
            var grid = new OpenDataGrid();

            // Then: AllowColumnReorder defaults to true.
            Assert.That(grid.AllowColumnReorder, Is.True);
        }

        [Test]
        public void ReorderColumn_WhenNotAllowed_DoesNothing()
        {
            // Given: Grid with AllowColumnReorder explicitly set to false, columns A B C.
            // Note: the default is true — this test must opt-out explicitly.
            var grid = CreateGrid("A", "B", "C");
            grid.AllowColumnReorder = false;

            // When: ReorderColumn is called.
            grid.ReorderColumn(0, 2);

            // Then: Column order unchanged.
            Assert.That(VisibleHeaders(grid), Is.EqualTo(new[] { "A", "B", "C" }));
        }

        [Test]
        public void PerformReorder_MovesFirstColumnToEnd()
        {
            // Given: Grid with three columns A B C, AllowColumnReorder = true.
            var grid = CreateGrid("A", "B", "C");
            grid.AllowColumnReorder = true;

            // When: Move column at index 0 to insertion point 3 (after last).
            grid.ReorderColumn(0, 3);

            // Then: Visible order is B C A.
            Assert.That(VisibleHeaders(grid), Is.EqualTo(new[] { "B", "C", "A" }));
        }

        [Test]
        public void PerformReorder_MovesLastColumnToFront()
        {
            // Given: Grid with three columns A B C, AllowColumnReorder = true.
            var grid = CreateGrid("A", "B", "C");
            grid.AllowColumnReorder = true;

            // When: Move column at index 2 to insertion point 0 (before first).
            grid.ReorderColumn(2, 0);

            // Then: Visible order is C A B.
            Assert.That(VisibleHeaders(grid), Is.EqualTo(new[] { "C", "A", "B" }));
        }

        [Test]
        public void PerformReorder_SwapsMiddleColumns()
        {
            // Given: Grid with four columns A B C D, AllowColumnReorder = true.
            var grid = CreateGrid("A", "B", "C", "D");
            grid.AllowColumnReorder = true;

            // When: Move column B (index 1) to after C (insertion point 3).
            grid.ReorderColumn(1, 3);

            // Then: Visible order is A C B D.
            Assert.That(VisibleHeaders(grid), Is.EqualTo(new[] { "A", "C", "B", "D" }));
        }

        [Test]
        public void PerformReorder_SamePosition_IsNoOp()
        {
            // Given: Grid with three columns A B C.
            var grid = CreateGrid("A", "B", "C");
            grid.AllowColumnReorder = true;

            // When: Move column B to its own insertion point (1) or directly after (2).
            grid.ReorderColumn(1, 1);
            grid.ReorderColumn(1, 2);

            // Then: Order unchanged.
            Assert.That(VisibleHeaders(grid), Is.EqualTo(new[] { "A", "B", "C" }));
        }

        [Test]
        public void PerformReorder_WithHiddenColumn_PreservesHiddenInFullList()
        {
            // Given: Grid with four columns A B C D, A is hidden, AllowColumnReorder = true.
            var grid = CreateGrid("A", "B", "C", "D");
            grid.AllowColumnReorder = true;
            grid.HiddenColumns = "A";   // hides A; visible = B C D

            // When: Move visible column B (srcVisible=0) after C (tgtVisible=2).
            // Expected visible result: C B D
            grid.ReorderColumn(0, 2);

            // Then: Visible order is C B D.
            Assert.That(VisibleHeaders(grid), Is.EqualTo(new[] { "C", "B", "D" }));

            // And: A (hidden) is still in the full columns collection and still hidden.
            var all = AllHeaders(grid);
            Assert.That(all, Does.Contain("A"));
            Assert.That(grid.Columns.Cast<OpenDataGridColumn>().First(c => c.Header == "A").IsVisible, Is.False);
        }

        [Test]
        public void PerformReorder_SingleColumn_IsNoOp()
        {
            // Given: Grid with a single column.
            var grid = CreateGrid("Only");
            grid.AllowColumnReorder = true;

            // When: ReorderColumn is called.
            Assert.DoesNotThrow(() => grid.ReorderColumn(0, 0));

            // Then: Still one column.
            Assert.That(VisibleHeaders(grid), Is.EqualTo(new[] { "Only" }));
        }

        [Test, Category("Integration")]
        public void PerformReorder_PreservesSortDirection()
        {
            // Given: Grid hosted in a window with columns A B C.
            var grid = CreateGrid("A", "B", "C");
            grid.AllowColumnReorder = true;

            var window = new Window { Content = grid, Width = 600, Height = 400 };
            window.Show();
            window.UpdateLayout();

            // Set sort direction AFTER grid is initialized (before data would reset it).
            var colB = grid.Columns.Cast<OpenDataGridColumn>().First(c => c.Header == "B");
            colB.SortDirection = Open.DataGrid.Models.SortDirection.Descending;

            // When: Move B (index 1) to front (insertion point 0).
            grid.ReorderColumn(1, 0);

            // Then: B is now at visible index 0 and retains its SortDirection.
            Assert.That(grid.GetVisibleColumnList()[0].Header, Is.EqualTo("B"));
            Assert.That(colB.SortDirection, Is.EqualTo(Open.DataGrid.Models.SortDirection.Descending));

            window.Close();
        }
    }
}
