using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Models;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Controls
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class LoadingStateInteractionTests
    {
        private sealed class Row { public string Name { get; set; } public string Value { get; set; } }

        private static OpenDataGrid CreateGridWithColumns()
        {
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { Header = "Name", BindingPath = "Name", Width = 100, IsSortable = true });
            grid.Columns.Add(new OpenDataGridLabelColumn { Header = "Value", BindingPath = "Value", Width = 100, IsSortable = true });
            return grid;
        }

        // OnHeaderCellClick is internal (InternalsVisibleTo). The event args are never read,
        // so null is safe here.
        private static void SimulateHeaderClick(OpenDataGrid grid, int colIndex)
            => grid.OnHeaderCellClick(new Border { Tag = colIndex }, null);

        // -----------------------------------------------------------------------

        [Test]
        public void HeaderClick_WithNullDataSource_DoesNotSetSortDirection()
        {
            // Given: Grid with no DataSource.
            var grid = CreateGridWithColumns();
            var col = (OpenDataGridColumn)grid.Columns[0];

            // When: Sort triggered while DataSource is null.
            SimulateHeaderClick(grid, 0);

            // Then: Column SortDirection stays None.
            Assert.That(col.SortDirection, Is.EqualTo(SortDirection.None));
        }

        [Test]
        public void HeaderClick_WithEmptyDataSource_DoesNotSetSortDirection()
        {
            // Given: Grid with empty DataSource.
            var grid = CreateGridWithColumns();
            grid.DataSource = new List<Row>();
            var col = (OpenDataGridColumn)grid.Columns[0];

            // When: Sort triggered before any data arrives.
            SimulateHeaderClick(grid, 0);

            // Then: Column SortDirection stays None.
            Assert.That(col.SortDirection, Is.EqualTo(SortDirection.None));
        }

        [Test, Category("Integration")]
        public void TryInitialize_ClearsStaleSortDirectionFromColumn()
        {
            // Given: Grid in a window; column has a stale SortDirection set before data arrived.
            var grid = CreateGridWithColumns();
            var window = new Window { Content = grid, Width = 400, Height = 200 };
            window.Show();
            window.UpdateLayout();

            var col = (OpenDataGridColumn)grid.Columns[0];
            col.SortDirection = SortDirection.Ascending;

            // When: DataSource is assigned (triggers TryInitialize → BeginReinitialize → reset).
            grid.DataSource = new List<Row> { new Row { Name = "A" }, new Row { Name = "B" } };
            window.UpdateLayout();

            // Then: Stale arrow cleared — no phantom sort indicator.
            Assert.That(col.SortDirection, Is.EqualTo(SortDirection.None));

            window.Close();
        }

        [Test, Category("Integration")]
        public void Load_AfterHeaderClickOnEmpty_GridInitializesCorrectly()
        {
            // Given: Grid hosted in a window, no DataSource yet.
            var grid = CreateGridWithColumns();
            var window = new Window { Content = grid, Width = 600, Height = 400 };
            window.Show();
            window.UpdateLayout();

            // Simulate sort click while DataSource is null.
            SimulateHeaderClick(grid, 0);

            // When: DataSource arrives.
            grid.DataSource = new List<Row>
            {
                new Row { Name = "Alpha" },
                new Row { Name = "Beta" },
                new Row { Name = "Gamma" },
            };
            window.UpdateLayout();

            // Then: Grid initialized and no stale sort arrow.
            Assert.That(grid.IsGridInitialized, Is.True);
            Assert.That(((OpenDataGridColumn)grid.Columns[0]).SortDirection, Is.EqualTo(SortDirection.None));

            window.Close();
        }
    }
}
