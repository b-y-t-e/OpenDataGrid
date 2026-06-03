using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Models;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Selection
{
    /// <summary>
    /// Regression tests for RestoreDefaultColumnOnFocus after CommitEdit / CancelEdit.
    /// Guards against: post-edit Focus() re-triggering TryRestoreDefaultColumnOnFocus
    /// and snapping the focused column back to SelectedColumn.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Category("Integration")]
    public class RestoreDefaultColumnOnFocusTests
    {
        private struct GridResult
        {
            public OpenDataGrid Grid;
            public Window Window;
            public List<SortItem> Data;
        }

        private static GridResult CreateGrid(string selectedColumn = "Name")
        {
            var data = new List<SortItem>();
            for (int i = 0; i < 20; i++)
                data.Add(new SortItem { Name = $"Item{i}", IntValue = i, DecimalValue = i });

            var col0 = new OpenDataGridLabelColumn   { Header = "Name",    BindingPath = "Name",         Width = 120 };
            var col1 = new OpenDataGridDecimalColumn { Header = "Decimal", BindingPath = "DecimalValue", Width = 120 };
            var col2 = new OpenDataGridIntegerColumn { Header = "Integer", BindingPath = "IntValue",     Width = 120 };

            var grid = new OpenDataGrid
            {
                NavigationMode              = NavigationMode.Cell,
                SelectedColumn              = selectedColumn,
                RestoreDefaultColumnOnFocus = true
            };
            grid.Columns.Add(col0);
            grid.Columns.Add(col1);
            grid.Columns.Add(col2);
            grid.DataSource = data;

            var window = new Window { Width = 600, Height = 400, Content = grid };
            window.Show();
            window.UpdateLayout();

            return new GridResult { Grid = grid, Window = window, Data = data };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Window w in Application.Current?.Windows ?? new WindowCollection())
                w.Close();
        }

        [Test]
        public void CommitEdit_DoesNotSnapFocusedColumnToSelectedColumn()
        {
            // Given: RestoreDefaultColumnOnFocus=true, SelectedColumn="Name" (index 0).
            // User moves focus to col 1 ("Decimal") and starts editing.
            var result = CreateGrid(selectedColumn: "Name");
            var grid   = result.Grid;

            grid.SelectedItem = result.Data[3];
            grid.SelectColumn("Decimal");
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(1), "setup: focused on Decimal");

            grid.TryBeginEdit(sortedRowIndex: 3, colIndex: 1, triggerKey: Key.F2);
            Assert.That(grid._editingCell, Is.Not.Null, "setup: edit started");

            // When: commit edit (Enter behaviour).
            grid.CommitEdit(restoreFocus: true);
            grid.UpdateLayout();

            // Then: focused column must stay on Decimal (index 1), NOT snap back to Name (0).
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(1),
                "CommitEdit must not snap focused column back to SelectedColumn");
        }

        [Test]
        public void CancelEdit_DoesNotSnapFocusedColumnToSelectedColumn()
        {
            // Given: RestoreDefaultColumnOnFocus=true, SelectedColumn="Name" (index 0).
            // User moves focus to col 2 ("Integer") and starts editing.
            var result = CreateGrid(selectedColumn: "Name");
            var grid   = result.Grid;

            grid.SelectedItem = result.Data[5];
            grid.SelectColumn("Integer");
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2), "setup: focused on Integer");

            grid.TryBeginEdit(sortedRowIndex: 5, colIndex: 2, triggerKey: Key.F2);
            Assert.That(grid._editingCell, Is.Not.Null, "setup: edit started");

            // When: cancel edit (Esc behaviour).
            grid.CancelEdit();
            grid.UpdateLayout();

            // Then: focused column must stay on Integer (index 2), NOT snap back to Name (0).
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2),
                "CancelEdit must not snap focused column back to SelectedColumn");
        }

        [Test]
        public void FocusFromOutside_RestoresDefaultColumn()
        {
            // Given: RestoreDefaultColumnOnFocus=true, SelectedColumn="Name" (index 0).
            // A separate window with a button represents "outside" the grid.
            var result = CreateGrid(selectedColumn: "Name");
            var grid   = result.Grid;

            grid.SelectedItem = result.Data[2];
            grid.SelectColumn("Integer");
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(2), "setup: focused on Integer");

            // When: focus moves to an unrelated button in a different window, then back to the grid.
            var otherWindow = new Window { Width = 200, Height = 100, Content = new Button { Content = "Outside" } };
            otherWindow.Show();
            otherWindow.UpdateLayout();
            ((Button)otherWindow.Content).Focus();
            otherWindow.UpdateLayout();

            grid.Focus();
            result.Window.UpdateLayout();

            otherWindow.Close();

            // Then: RestoreDefaultColumnOnFocus must kick in — column snaps back to Name (0).
            Assert.That(grid.FocusedColumnIndex, Is.EqualTo(0),
                "Focus from outside must restore SelectedColumn");
        }
    }
}
