using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Selection
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class SelectionInvariantTests
    {
        // ------------------------------------------------------------------ helpers

        private static List<SortItem> MakeData(int count)
        {
            var list = new List<SortItem>(count);
            for (int i = 0; i < count; i++)
                list.Add(new SortItem { Name = $"Item{i}", IntValue = i });
            return list;
        }

        private struct GridResult
        {
            public OpenDataGrid Grid;
            public Window Window;
            public List<SortItem> Data;
        }

        private static GridResult CreateInitializedGrid(int itemCount = 20, List<SortItem> data = null)
        {
            data = data ?? MakeData(itemCount);
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Header = "Name", Width = 200 });
            grid.DataSource = data;

            var window = new Window
            {
                Width = 300, Height = 200, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            window.Show();
            window.UpdateLayout();
            return new GridResult { Grid = grid, Window = window, Data = data };
        }

        /// <summary>Forces SelectedItem = null bypassing the RequireSelection guard.</summary>
        private static void ForceNullSelection(OpenDataGrid grid) => grid._selection.ClearSelectedItem();

        // ------------------------------------------------------------------ unit (no Window)

        [Test]
        public void RequireSelection_DefaultIsTrue()
        {
            // Given / When: A new grid instance.
            var grid = new OpenDataGrid();

            // Then: RequireSelection defaults to true.
            Assert.That(grid.RequireSelection, Is.True);
        }

        [Test]
        public void RequireSelection_RoundTrips()
        {
            // Given: A new grid instance.
            var grid = new OpenDataGrid();

            // When: RequireSelection is set to false.
            grid.RequireSelection = false;

            // Then: Property returns false.
            Assert.That(grid.RequireSelection, Is.False);
        }

        // ------------------------------------------------------------------ VM sets null (LUKA #4)

        [Test, Category("Integration")]
        public void RequireSelection_True_VmSetsNull_GridReselectsFirstVisible()
        {
            // Given: Fully initialized grid; RequireSelection = true (default).
            var result = CreateInitializedGrid(itemCount: 10);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                grid.SelectedItem = data[5];
                Assert.That(grid.SelectedItem, Is.SameAs(data[5]));

                // When: ViewModel sets SelectedItem = null.
                grid.SelectedItem = null;

                // Then: Grid reselects the first visible item.
                Assert.That(grid.SelectedItem, Is.Not.Null, "SelectedItem must not be null when RequireSelection=true and rows exist");
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void RequireSelection_False_VmSetsNull_GridAcceptsNull()
        {
            // Given: Fully initialized grid; RequireSelection = false.
            var result = CreateInitializedGrid(itemCount: 10);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                grid.RequireSelection = false;
                grid.SelectedItem = data[5];

                // When: ViewModel sets SelectedItem = null.
                grid.SelectedItem = null;

                // Then: Grid accepts null — no forced fallback.
                Assert.That(grid.SelectedItem, Is.Null, "SelectedItem should be null when RequireSelection=false");
            }
            finally { window.Close(); }
        }

        // ------------------------------------------------------------------ FinishApplyFilters (LUKA #1)

        [Test, Category("Integration")]
        public void FinishApplyFilters_EmptySelection_SelectsFirstVisible_WhenRequireSelection()
        {
            // Given: Initialized grid with no current selection (forced via internal guard).
            var result = CreateInitializedGrid(itemCount: 10);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                ForceNullSelection(grid);
                Assert.That(grid.SelectedItem, Is.Null, "Precondition: selection forced to null");

                // When: A filter is applied that passes some rows (all IntValue >= 0).
                grid.RegisterFilter("test", item => ((SortItem)item).IntValue < 5);

                // Then: Grid selects the first visible item.
                Assert.That(grid.SelectedItem, Is.Not.Null, "Grid must select first visible after filter when RequireSelection=true");
                Assert.That(data.IndexOf((SortItem)grid.SelectedItem), Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                grid.UnregisterFilter("test");
                window.Close();
            }
        }

        [Test, Category("Integration")]
        public void UnregisterFilter_EmptySelection_SelectsFirstVisible_WhenRequireSelection()
        {
            // Given: Grid with active filter; selection forced to null.
            var result = CreateInitializedGrid(itemCount: 10);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                grid.RegisterFilter("test", item => ((SortItem)item).IntValue < 5);
                ForceNullSelection(grid);
                Assert.That(grid.SelectedItem, Is.Null, "Precondition: selection forced to null");

                // When: Filter is removed — all rows become visible again.
                grid.UnregisterFilter("test");

                // Then: Grid selects first visible item.
                Assert.That(grid.SelectedItem, Is.Not.Null, "Grid must select first visible after filter removed when RequireSelection=true");
            }
            finally { window.Close(); }
        }

        // ------------------------------------------------------------------ OnCollectionChanged (LUKA #3)

        [Test, Category("Integration")]
        public void OnCollectionChanged_AddToEmptyCollection_SelectsFirst_WhenRequireSelection()
        {
            // Given: Grid initialized with empty ObservableCollection (no selection possible).
            var data = new ObservableCollection<SortItem>();
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.DataSource = data;

            var window = new Window
            {
                Width = 300, Height = 200, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            window.Show();
            window.UpdateLayout();
            try
            {
                Assert.That(grid.SelectedItem, Is.Null, "Precondition: empty collection — no selection");

                // When: Items are added to the collection.
                var newItem = new SortItem { Name = "First", IntValue = 0 };
                data.Add(newItem);
                data.Add(new SortItem { Name = "Second", IntValue = 1 });

                // Wait for coalesced dispatcher rebuild.
                for (int i = 0; i < 50; i++)
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Background, new System.Action(() => { }));
                    if (grid.SelectedItem != null) break;
                }

                // Then: First item is selected.
                Assert.That(grid.SelectedItem, Is.Not.Null, "Grid must select first item after Add when RequireSelection=true");
            }
            finally { window.Close(); }
        }

        // ------------------------------------------------------------------ OnSortCompleted (LUKA #2)

        [Test, Category("Integration")]
        public void OnSortCompleted_EmptySelection_SelectsFirstVisible_WhenRequireSelection()
        {
            // Given: Initialized grid; selection forced to null; sort triggered.
            var result = CreateInitializedGrid(itemCount: 10);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                ForceNullSelection(grid);
                Assert.That(grid.SelectedItem, Is.Null, "Precondition: selection forced to null");

                // When: Sort is triggered on the first column.
                var col = grid.GetVisibleColumnList()[0];
                bool completed = false;
                grid._sortEngine.SortCompleted += (s, e) => completed = true;
                grid._sortEngine.Sort(col, grid.DataSource);

                // Wait for sort + dispatcher.
                SpinWait.SpinUntil(() => completed, 3000);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background, new System.Action(() => { }));

                // Then: Grid selects the first visible item.
                Assert.That(grid.SelectedItem, Is.Not.Null, "Grid must select first visible after sort when RequireSelection=true");
            }
            finally { window.Close(); }
        }

        // ------------------------------------------------------------------ RequireSelection=false symmetric tests

        [Test, Category("Integration")]
        public void FinishApplyFilters_EmptySelection_StaysNull_WhenRequireSelectionFalse()
        {
            // Given: Initialized grid with RequireSelection=false; selection forced to null.
            var result = CreateInitializedGrid(itemCount: 10);
            var grid = result.Grid; var window = result.Window;
            try
            {
                grid.RequireSelection = false;
                ForceNullSelection(grid);

                // When: A filter is applied.
                grid.RegisterFilter("test", item => ((SortItem)item).IntValue < 5);

                // Then: Grid leaves SelectedItem = null (no forced fallback).
                Assert.That(grid.SelectedItem, Is.Null, "RequireSelection=false should not force selection on filter");
            }
            finally
            {
                grid.UnregisterFilter("test");
                window.Close();
            }
        }

        [Test, Category("Integration")]
        public void OnSortCompleted_EmptySelection_StaysNull_WhenRequireSelectionFalse()
        {
            // Given: Initialized grid with RequireSelection=false; selection forced to null.
            var result = CreateInitializedGrid(itemCount: 10);
            var grid = result.Grid; var window = result.Window;
            try
            {
                grid.RequireSelection = false;
                ForceNullSelection(grid);

                // When: Sort is triggered.
                var col = grid.GetVisibleColumnList()[0];
                bool completed = false;
                grid._sortEngine.SortCompleted += (s, e) => completed = true;
                grid._sortEngine.Sort(col, grid.DataSource);
                SpinWait.SpinUntil(() => completed, 3000);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background, new System.Action(() => { }));

                // Then: SelectedItem remains null.
                Assert.That(grid.SelectedItem, Is.Null, "RequireSelection=false should not force selection on sort");
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void OnCollectionChanged_AddToEmptyCollection_StaysNull_WhenRequireSelectionFalse()
        {
            // Given: Empty ObservableCollection; RequireSelection=false from the start.
            var data = new ObservableCollection<SortItem>();
            var grid = new OpenDataGrid { RequireSelection = false };
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.DataSource = data;

            var window = new Window
            {
                Width = 300, Height = 200, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            window.Show();
            window.UpdateLayout();
            try
            {
                Assert.That(grid.SelectedItem, Is.Null, "Precondition: empty collection — no selection");

                // When: Items are added.
                data.Add(new SortItem { Name = "First", IntValue = 0 });
                data.Add(new SortItem { Name = "Second", IntValue = 1 });

                SpinWait.SpinUntil(() =>
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Background, new System.Action(() => { }));
                    return grid._sortEngine.SortedIndices?.Length == 2;
                }, 3000);

                // Then: Rebuild ran (sortedIndices reflect added items) and SelectedItem remains null.
                Assert.That(grid._sortEngine.SortedIndices?.Length, Is.EqualTo(2), "Rebuild must have processed both added items");
                Assert.That(grid.SelectedItem, Is.Null, "RequireSelection=false should not force selection after Add");
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void VmSelectsItemOutsideFilter_StaysNull_WhenRequireSelectionFalse()
        {
            // Given: Filter active, RequireSelection=false, selection on visible item.
            var result = CreateInitializedGrid(itemCount: 10);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                grid.RequireSelection = false;
                grid.RegisterFilter("test", item => ((SortItem)item).IntValue < 5);
                grid.SelectedItem = data[2]; // visible
                Assert.That(grid.SelectedItem, Is.SameAs(data[2]), "Precondition");

                // When: VM sets SelectedItem to a filtered-out item.
                grid.SelectedItem = data[7];

                // Then: SelectedItem becomes null (item not displayable, no forced fallback).
                Assert.That(grid.SelectedItem, Is.Null, "RequireSelection=false: filtered-out item should clear selection, not fall back");
            }
            finally
            {
                grid.UnregisterFilter("test");
                window.Close();
            }
        }

        [Test, Category("Integration")]
        public void FinishApplyFilters_SelectedItemFilteredOut_StaysNull_WhenRequireSelectionFalse()
        {
            // Given: Grid with a selected item; RequireSelection=false.
            var result = CreateInitializedGrid(itemCount: 10);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                grid.RequireSelection = false;
                grid.SelectedItem = data[7]; // item that will be filtered out
                Assert.That(grid.SelectedItem, Is.SameAs(data[7]), "Precondition");

                // When: A filter is applied that excludes the selected item.
                grid.RegisterFilter("test", item => ((SortItem)item).IntValue < 5);

                // Then: SelectedItem is cleared; no fallback to first visible (RequireSelection=false).
                Assert.That(grid.SelectedItem, Is.Null, "RequireSelection=false: filtered-out selected item should clear, not fall back");
            }
            finally
            {
                grid.UnregisterFilter("test");
                window.Close();
            }
        }

        // ------------------------------------------------------------------ Item outside filter (LUKA #10)

        [Test, Category("Integration")]
        public void VmSelectsItemOutsideFilter_FallsBackToFirstVisible()
        {
            // Given: Grid with active filter (only IntValue < 5 visible); selection on visible item.
            var result = CreateInitializedGrid(itemCount: 10);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                grid.RegisterFilter("test", item => ((SortItem)item).IntValue < 5);
                grid.SelectedItem = data[2]; // visible (IntValue=2)
                Assert.That(grid.SelectedItem, Is.SameAs(data[2]), "Precondition: item[2] selected");

                // When: ViewModel sets SelectedItem to an item that is filtered out.
                grid.SelectedItem = data[7]; // filtered out (IntValue=7)

                // Then: Grid falls back to a visible item, not the filtered-out one.
                Assert.That(grid.SelectedItem, Is.Not.SameAs(data[7]), "Filtered-out item must not remain selected");
                Assert.That(grid.SelectedItem, Is.Not.Null, "Some visible item must be selected");
                int selectedIdx = data.IndexOf((SortItem)grid.SelectedItem);
                Assert.That(selectedIdx, Is.LessThan(5), "Selected item must be within filter range (IntValue < 5)");
            }
            finally
            {
                grid.UnregisterFilter("test");
                window.Close();
            }
        }

        // ------------------------------------------------------------------ PendingSelectedItem (LUKA #6)

        [Test, Category("Integration")]
        public void PendingSelectedItem_ConsumedAfterCollectionAdd()
        {
            // Given: Grid with ObservableCollection; SelectedItem set to item NOT yet in collection.
            var data = new ObservableCollection<SortItem>();
            data.Add(new SortItem { Name = "Existing", IntValue = 0 });

            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.DataSource = data;

            var window = new Window
            {
                Width = 300, Height = 200, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            window.Show();
            window.UpdateLayout();
            try
            {
                // Set SelectedItem to a future item (not yet in DataSource) → stored as pending.
                var futureItem = new SortItem { Name = "Future", IntValue = 99 };
                grid.SelectedItem = futureItem;
                Assert.That(grid._selection.PendingSelectedItem, Is.SameAs(futureItem),
                    "Precondition: item should be pending since it's not in DataSource");

                // When: The pending item is added to the collection.
                data.Add(futureItem);

                // Wait for coalesced dispatcher rebuild.
                for (int i = 0; i < 50; i++)
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        System.Windows.Threading.DispatcherPriority.Background, new System.Action(() => { }));
                    if (grid._selection.PendingSelectedItem == null) break;
                }

                // Then: Pending is consumed and futureItem becomes the active selection.
                Assert.That(grid._selection.PendingSelectedItem, Is.Null, "Pending should be consumed after item added");
                Assert.That(grid.SelectedItem, Is.SameAs(futureItem), "FutureItem should be selected after Add");
            }
            finally { window.Close(); }
        }
    }
}
