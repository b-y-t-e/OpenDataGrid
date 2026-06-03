using System.Collections.Generic;
using System.Threading;
using System.Windows;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Models;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Selection
{
    /// <summary>
    /// Regression tests for focused column stability.
    /// Guards against the "column jumps to 0 after column visibility change or navigation mode flip"
    /// class of bugs caused by NavigationEngine.CurrentColumnIndex being reset independently
    /// of _focusedColIndex.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Category("Integration")]
    public class FocusColumnStabilityTests
    {
        private struct GridResult
        {
            public OpenDataGrid Grid;
            public Window Window;
            public List<SortItem> Data;
            public OpenDataGridLabelColumn Col0;
            public OpenDataGridLabelColumn Col1;
            public OpenDataGridLabelColumn Col2;
            public OpenDataGridLabelColumn Col3;
        }

        private static GridResult CreateGrid(NavigationMode mode = NavigationMode.Cell)
        {
            var data = new List<SortItem>();
            for (int i = 0; i < 20; i++)
                data.Add(new SortItem { Name = $"Item{i}", IntValue = i });

            var col0 = new OpenDataGridLabelColumn { Header = "Col0", BindingPath = "Name",    Width = 100 };
            var col1 = new OpenDataGridLabelColumn { Header = "Col1", BindingPath = "IntValue", Width = 100 };
            var col2 = new OpenDataGridLabelColumn { Header = "Col2", BindingPath = "Name",    Width = 100 };
            var col3 = new OpenDataGridLabelColumn { Header = "Col3", BindingPath = "IntValue", Width = 100 };

            var grid = new OpenDataGrid { NavigationMode = mode };
            grid.Columns.Add(col0);
            grid.Columns.Add(col1);
            grid.Columns.Add(col2);
            grid.Columns.Add(col3);
            grid.DataSource = data;

            var window = new Window { Width = 600, Height = 400, Content = grid };
            window.Show();
            window.UpdateLayout();

            return new GridResult { Grid = grid, Window = window, Data = data,
                                    Col0 = col0, Col1 = col1, Col2 = col2, Col3 = col3 };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Window w in Application.Current?.Windows ?? new WindowCollection())
                w.Close();
        }

        // ── Column visibility change: focused column must not reset to 0 ────────

        [Test]
        public void ColumnVisibilityChange_DoesNotResetFocusedColumnToZero()
        {
            // Given: grid initialized, focus placed on Col3 (index 3).
            var result = CreateGrid();
            var grid   = result.Grid;
            grid.SelectedItem = result.Data[5];
            grid.SelectColumn("Col3");
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(3), "setup: focus on Col3");

            // When: a column visibility change triggers RefreshAfterColumnVisibilityChanged.
            result.Col0.IsVisible = false;
            result.Grid.UpdateLayout();
            result.Col0.IsVisible = true;
            result.Grid.UpdateLayout();

            // Then: the key regression — FocusedColumnIndex must NOT jump to 0.
            // (Clamping keeps it near the original position, not at 0.)
            Assert.That(grid.FocusedColumnIndex, Is.Not.EqualTo(0),
                "FocusedColumnIndex must not reset to 0 after column visibility change");
            Assert.That(grid.FocusedColumnIndex, Is.GreaterThan(0));

            result.Window.Close();
        }

        [Test]
        public void ColumnVisibilityChange_FocusedColumnClampedToNewRange()
        {
            // Given: grid initialized with focus on Col2 (index 2).
            var result = CreateGrid();
            var grid   = result.Grid;
            grid.SelectedItem = result.Data[3];
            grid.SelectColumn("Col2");
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2), "setup: focus on Col2");

            // When: Col0 is hidden (visible = [Col1, Col2, Col3], indices 0–2).
            result.Col0.IsVisible = false;
            result.Grid.UpdateLayout();

            // Then: FocusedColumnIndex is clamped — still 2 (unchanged since 3-col range is 0–2).
            // Critical: it is NOT 0.
            Assert.That(grid.FocusedColumnIndex, Is.Not.EqualTo(0));
            // FocusedColumnIndex stays at 2 because visible range is [0..2] and 2 fits.
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2));

            // When: Col0 is shown again (visible = [Col0, Col1, Col2, Col3], indices 0–3).
            result.Col0.IsVisible = true;
            result.Grid.UpdateLayout();

            // Then: still 2 (no clamping needed for 4-col range).
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2));

            result.Window.Close();
        }

        [Test]
        public void ColumnVisibilityChange_WhenFocusedColHidden_ClampsToLastVisible()
        {
            // Given: 4 columns, focus on Col3 (index 3).
            var result = CreateGrid();
            var grid   = result.Grid;
            grid.SelectedItem = result.Data[0];
            grid.SelectColumn("Col3");
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(3), "setup: focus on Col3");

            // When: Col3 is hidden (visible = [Col0, Col1, Col2], colCount = 3).
            result.Col3.IsVisible = false;
            result.Grid.UpdateLayout();

            // Then: index clamped to colCount-1 = 2. NOT reset to 0.
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2));

            result.Window.Close();
        }

        // ── NavigationMode change must preserve focused column ─────────────────

        [Test]
        public void NavigationModeFlip_CellToRow_AndBack_PreservesFocusedColumn()
        {
            // Given: Cell-mode grid, focus on Col2.
            var result = CreateGrid(NavigationMode.Cell);
            var grid   = result.Grid;
            grid.SelectedItem = result.Data[0];
            grid.SelectColumn("Col2");
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2), "setup");

            // When: flip to Row mode and back to Cell mode.
            grid.NavigationMode = NavigationMode.Row;
            grid.NavigationMode = NavigationMode.Cell;
            grid.UpdateLayout();

            // Then: focused column must NOT reset to 0.
            Assert.That(grid.FocusedColumnIndex, Is.Not.EqualTo(0),
                "FocusedColumnIndex must not reset to 0 after NavigationMode flip");
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2));

            result.Window.Close();
        }

        // ── SelectedColumn DP resolves on first init only ──────────────────────

        [Test]
        public void SelectedColumn_ResolvedOnInit_PersistsAfterDataSourceReplace()
        {
            // Given: Cell-mode grid with SelectedColumn = "Col2" set before DataSource is assigned.
            var data = new List<SortItem>();
            for (int i = 0; i < 10; i++) data.Add(new SortItem { Name = $"Row{i}", IntValue = i });

            var col0 = new OpenDataGridLabelColumn { Header = "Col0", BindingPath = "Name",    Width = 100 };
            var col1 = new OpenDataGridLabelColumn { Header = "Col1", BindingPath = "IntValue", Width = 100 };
            var col2 = new OpenDataGridLabelColumn { Header = "Col2", BindingPath = "Name",    Width = 100 };
            var col3 = new OpenDataGridLabelColumn { Header = "Col3", BindingPath = "IntValue", Width = 100 };

            var grid = new OpenDataGrid { NavigationMode = NavigationMode.Cell, SelectedColumn = "Col2" };
            grid.Columns.Add(col0); grid.Columns.Add(col1);
            grid.Columns.Add(col2); grid.Columns.Add(col3);
            grid.DataSource = data;

            var window = new Window { Width = 600, Height = 400, Content = grid };
            window.Show();
            window.UpdateLayout();
            try
            {
                // Then: first init resolved SelectedColumn = "Col2" → index 2.
                Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2),
                    "SelectedColumn 'Col2' should resolve to index 2 on first init");

                // When: DataSource is replaced (simulates Load 500k / 100k demo scenario).
                var newData = new List<SortItem>();
                for (int i = 0; i < 5; i++) newData.Add(new SortItem { Name = $"New{i}", IntValue = i });
                grid.DataSource = newData;
                grid.UpdateLayout();

                // Then: column focus preserved at 2 (user already navigated here on first init;
                // SelectedColumn DP is not re-applied on subsequent DataSource changes).
                Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2),
                    "Column focus should be preserved after DataSource replacement");
            }
            finally { window.Close(); }
        }
    }
}
