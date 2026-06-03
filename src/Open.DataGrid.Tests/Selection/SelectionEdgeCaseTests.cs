using System;
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
    /// <summary>
    /// Regression tests for selection edge cases detected in code review:
    /// - TryConsumePendingSelection always clears PendingSelectedItem (even when not consumed)
    /// - InternalSelectionChange guard covers full SelectByDataIndex operation
    /// - Empty collection after filter zeros out SelectedItem
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Category("Integration")]
    public class SelectionEdgeCaseTests
    {
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

        private static GridResult CreateInitializedGrid(int itemCount = 10)
        {
            var data = MakeData(itemCount);
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
            return new GridResult { Grid = grid, Window = window, Data = data };
        }

        // ── TryConsumePendingSelection always clears PendingSelectedItem ─────

        [Test]
        public void TryConsumePendingSelection_ItemNotInDataSource_ClearsPending()
        {
            // Given: a grid with a ghost pending item (not in datasource)
            var result = CreateInitializedGrid(5);
            var grid = result.Grid;
            try
            {
                var ghost = new SortItem { Name = "Ghost" };
                grid._selection.PendingSelectedItem = ghost;

                // When: DataSource is replaced (FinishInitialize re-evaluates pending)
                grid.DataSource = MakeData(5);
                result.Window.UpdateLayout();

                // Then: PendingSelectedItem is cleared (not orphaned)
                Assert.That(grid._selection.PendingSelectedItem, Is.Null,
                    "PendingSelectedItem must be cleared even when the ghost item is not in new DataSource.");
            }
            finally { result.Window.Close(); }
        }

        [Test]
        public void PendingSelectedItem_ItemInNewDataSource_IsConsumedAndSelected()
        {
            // Given: an item that will exist in the new DataSource
            var result = CreateInitializedGrid(5);
            var grid = result.Grid;
            try
            {
                var newData = MakeData(10);
                var targetItem = newData[7];
                grid._selection.PendingSelectedItem = targetItem;

                // When: DataSource is replaced with one containing the item
                grid.DataSource = newData;
                result.Window.UpdateLayout();

                // Then: pending is consumed, SelectedItem matches
                Assert.That(grid._selection.PendingSelectedItem, Is.Null,
                    "PendingSelectedItem must be cleared after consumption.");
                Assert.That(grid.SelectedItem, Is.EqualTo(targetItem),
                    "SelectedItem must equal the pending item after consumption.");
            }
            finally { result.Window.Close(); }
        }

        // ── SelectedItem stays null when all items are filtered out ──────────

        [Test]
        public void Filter_FiltersOutAllItems_SelectedItemIsNull()
        {
            // Given: a fully initialized grid with a selection
            var result = CreateInitializedGrid(10);
            var grid = result.Grid;
            try
            {
                // Select an item explicitly
                grid.SelectedItem = result.Data[3];
                result.Window.UpdateLayout();
                Assert.That(grid.SelectedItem, Is.Not.Null);

                // When: register a filter that excludes every item
                grid.RegisterFilter(this, _ => false);
                result.Window.UpdateLayout();

                // Then: SelectedItem is null (no visible rows, RequireSelection cannot be satisfied)
                Assert.That(grid.SelectedItem, Is.Null,
                    "SelectedItem must be null when all items are filtered out.");
            }
            finally
            {
                grid.UnregisterFilter(this);
                result.Window.Close();
            }
        }

        // ── InternalSelectionChange guard covers full SelectByDataIndex op ──

        [Test]
        public void SelectByDataIndex_DoesNotTriggerOnSelectedItemChangedReentry()
        {
            // Given: a grid with OnSelectedItemChanged wired to a counter
            var result = CreateInitializedGrid(10);
            var grid = result.Grid;
            try
            {
                int externalCallCount = 0;

                // Monkey-patch: use SelectedItem DP callback indirectly via DependencyPropertyDescriptor
                var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                    OpenDataGrid.SelectedItemProperty, typeof(OpenDataGrid));
                EventHandler handler = (_, __) =>
                {
                    // InternalSelectionChange must be true during the entire SelectByDataIndex call
                    if (!grid._selection.InternalSelectionChange)
                        externalCallCount++;
                };
                dpd.AddValueChanged(grid, handler);

                // When: internal selection by data index
                grid._selection.SelectByDataIndex(5);
                result.Window.UpdateLayout();

                // Then: no external (non-guarded) SelectedItem change was observed
                dpd.RemoveValueChanged(grid, handler);
                Assert.That(externalCallCount, Is.EqualTo(0),
                    "InternalSelectionChange must be true throughout SelectByDataIndex.");
            }
            finally { result.Window.Close(); }
        }

        // ── RequireSelection=true, empty collection ──────────────────────────

        [Test]
        public void RequireSelection_EmptyObservableCollection_SelectedItemIsNull()
        {
            // Given: RequireSelection=true (default), grid with items
            var obs = new ObservableCollection<SortItem>(MakeData(5));
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.DataSource = obs;

            var window = new Window
            {
                Width = 300, Height = 200, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            window.Show();
            window.UpdateLayout();

            try
            {
                Assert.That(grid.SelectedItem, Is.Not.Null, "Precondition: item selected");

                // When: all items removed
                obs.Clear();
                window.UpdateLayout();
                // Allow Dispatcher.BeginInvoke (coalesced rebuild) to process
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle, new System.Action(() => { }));

                // Then: SelectedItem must be null (no rows available)
                Assert.That(grid.SelectedItem, Is.Null,
                    "SelectedItem must be null when ObservableCollection is cleared.");
            }
            finally { window.Close(); }
        }
    }
}
