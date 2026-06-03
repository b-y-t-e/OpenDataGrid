using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Models;
using Open.DataGrid.Tests.Helpers;
using Open.DataGrid.Virtualization;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Selection
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class PlaceholderRowTests
    {
        // ------------------------------------------------------------------ helpers

        private struct GridResult
        {
            public OpenDataGrid Grid;
            public Window       Window;
        }

        private static GridResult CreateGrid(
            IList data             = null,
            bool showPlaceholder   = true,
            NavigationMode mode    = NavigationMode.Cell)
        {
            var grid = new OpenDataGrid
            {
                NavigationMode          = mode,
                ShowEmptyPlaceholderRow = showPlaceholder,
            };
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name",     Header = "Name",     Width = 150 });
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "IntValue", Header = "IntValue", Width = 100 });
            if (data != null)
                grid.DataSource = data;

            var window = new Window
            {
                Width = 400, Height = 200, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            window.Show();
            window.UpdateLayout();
            return new GridResult { Grid = grid, Window = window };
        }

        private static List<SortItem> MakeData(int count)
        {
            var list = new List<SortItem>(count);
            for (int i = 0; i < count; i++)
                list.Add(new SortItem { Name = $"Item{i}", IntValue = i });
            return list;
        }

        // ------------------------------------------------------------------ DP default

        [Test]
        public void ShowEmptyPlaceholderRow_DefaultIsFalse()
        {
            // Given / When
            var grid = new OpenDataGrid();

            // Then
            Assert.That(grid.ShowEmptyPlaceholderRow, Is.False);
        }

        // ------------------------------------------------------------------ IsPlaceholderActive logic

        [Test]
        public void IsPlaceholderActive_FalseWhenDpOff()
        {
            // Given: DP disabled, no data.
            var grid = new OpenDataGrid { ShowEmptyPlaceholderRow = false };

            // Then
            Assert.That(grid.IsPlaceholderActive, Is.False);
        }

        [Test]
        public void IsPlaceholderActive_TrueWhenDpOnAndNoData()
        {
            // Given: DP enabled, no DataSource set.
            var grid = new OpenDataGrid { ShowEmptyPlaceholderRow = true };

            // Then
            Assert.That(grid.IsPlaceholderActive, Is.True);
        }

        [Test]
        public void IsPlaceholderActive_FalseWhenDpOnAndDataPresent()
        {
            // Given: DP enabled, DataSource has rows.
            var grid = new OpenDataGrid { ShowEmptyPlaceholderRow = true };
            grid.DataSource = MakeData(3);

            // Then
            Assert.That(grid.IsPlaceholderActive, Is.False);
        }

        // ------------------------------------------------------------------ EffectiveRowCount

        [Test]
        public void EffectiveRowCount_IsOneWhenPlaceholderActive()
        {
            // Given
            var grid = new OpenDataGrid { ShowEmptyPlaceholderRow = true };

            // Then
            Assert.That(grid.EffectiveRowCount, Is.EqualTo(1));
        }

        [Test]
        public void EffectiveRowCount_IsZeroWhenDpOffAndNoData()
        {
            // Given
            var grid = new OpenDataGrid { ShowEmptyPlaceholderRow = false };

            // Then
            Assert.That(grid.EffectiveRowCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ SelectedItem stays null (Integration)

        [Test, Category("Integration")]
        public void PlaceholderRow_SelectedItemRemainsNull()
        {
            // Given: grid with placeholder, no data.
            var result = CreateGrid(data: null, showPlaceholder: true);
            using (new WindowScope(result.Window))
            {
                // Then
                Assert.That(result.Grid.SelectedItem, Is.Null);
            }
        }

        [Test, Category("Integration")]
        public void PlaceholderRow_DisabledDp_CanvasStaysEmpty()
        {
            // Given: DP off, no data — old behaviour unchanged.
            var result = CreateGrid(data: null, showPlaceholder: false);
            using (new WindowScope(result.Window))
            {
                // Then: grid is not initialized (TemplateReady state, no data).
                Assert.That(result.Grid.IsGridInitialized, Is.False);
            }
        }

        [Test, Category("Integration")]
        public void PlaceholderRow_GridIsInitializedWhenDpOn()
        {
            // Given: DP on, no data.
            var result = CreateGrid(data: null, showPlaceholder: true);
            using (new WindowScope(result.Window))
            {
                // Then: lifecycle reached Ready so navigation is available.
                Assert.That(result.Grid.IsGridInitialized, Is.True);
            }
        }

        // ------------------------------------------------------------------ Placeholder DataIndex sentinel

        [Test, Category("Integration")]
        public void PlaceholderRow_SlotDataIndexIsSentinel()
        {
            // Given: grid initialized with placeholder.
            var result = CreateGrid(data: null, showPlaceholder: true);
            using (new WindowScope(result.Window))
            {
                // When: find the visible placeholder slot.
                var slot = result.Grid._rowEngine.FindRowByDataIndex(RowVirtualizationEngine.PlaceholderDataIndex);

                // Then
                Assert.That(slot, Is.Not.Null);
                Assert.That(slot.DataIndex, Is.EqualTo(RowVirtualizationEngine.PlaceholderDataIndex));
                Assert.That(slot.DataItem, Is.Null);
            }
        }

        // ------------------------------------------------------------------ Data appears → placeholder disappears (Integration)

        [Test, Category("Integration")]
        public void PlaceholderRow_DisappearsWhenDataAdded()
        {
            // Given: placeholder shown.
            var collection = new ObservableCollection<SortItem>();
            var result = CreateGrid(data: collection, showPlaceholder: true);
            using (new WindowScope(result.Window))
            {
                Assert.That(result.Grid.IsPlaceholderActive, Is.True);

                // When: data arrives — flush the deferred CollectionChanged rebuild.
                collection.Add(new SortItem { Name = "X", IntValue = 1 });
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() => { }));
                result.Window.UpdateLayout();

                // Then: placeholder no longer active.
                Assert.That(result.Grid.IsPlaceholderActive, Is.False);
                Assert.That(result.Grid.TotalVisibleRows, Is.EqualTo(1));
            }
        }

        // ------------------------------------------------------------------ Navigation (Cell mode, Integration)

        [Test, Category("Integration")]
        public void PlaceholderRow_RightArrow_AdvancesColumn()
        {
            // Given: grid with placeholder in Cell mode and 2 columns.
            var result = CreateGrid(data: null, showPlaceholder: true, mode: NavigationMode.Cell);
            using (new WindowScope(result.Window))
            {
                result.Grid.Focus();
                result.Window.UpdateLayout();

                // When: press Right — column should advance from 0 to 1.
                result.Grid._navEngine.HandleKeyDown(MakeKey(Key.Right, result.Window));

                // Then
                Assert.That(result.Grid.FocusedColIndex, Is.EqualTo(1));
            }
        }

        [Test, Category("Integration")]
        public void PlaceholderRow_LeftArrow_ClampsAtZero()
        {
            // Given: focus on column 0.
            var result = CreateGrid(data: null, showPlaceholder: true, mode: NavigationMode.Cell);
            using (new WindowScope(result.Window))
            {
                result.Grid.Focus();
                result.Window.UpdateLayout();

                // When: press Left while at column 0.
                result.Grid._navEngine.HandleKeyDown(MakeKey(Key.Left, result.Window));

                // Then: stays at 0.
                Assert.That(result.Grid.FocusedColIndex, Is.EqualTo(0));
            }
        }

        [Test, Category("Integration")]
        public void PlaceholderRow_UpDownArrows_StayOnSameRow()
        {
            // Given
            var result = CreateGrid(data: null, showPlaceholder: true, mode: NavigationMode.Cell);
            using (new WindowScope(result.Window))
            {
                // When: press Down then Up.
                result.Grid._navEngine.HandleKeyDown(MakeKey(Key.Down, result.Window));
                result.Grid._navEngine.HandleKeyDown(MakeKey(Key.Up, result.Window));

                // Then: still on row 0 (only row available).
                Assert.That(result.Grid._navEngine.CurrentRowIndex, Is.EqualTo(0));
            }
        }

        // ------------------------------------------------------------------ Edit blocked (Integration)

        [Test, Category("Integration")]
        public void PlaceholderRow_F2_DoesNotStartEdit()
        {
            // Given
            var result = CreateGrid(data: null, showPlaceholder: true);
            using (new WindowScope(result.Window))
            {
                // When: try to begin edit.
                result.Grid.TryBeginEdit(0, 0, Key.F2);

                // Then: no editing cell.
                Assert.That(result.Grid._editingCell, Is.Null);
            }
        }

        // ------------------------------------------------------------------ RequireSelection invariant (Integration)

        [Test, Category("Integration")]
        public void PlaceholderRow_RequireSelectionTrue_SelectedItemStaysNull()
        {
            // Given: RequireSelection = true (default), placeholder active.
            var result = CreateGrid(data: null, showPlaceholder: true);
            using (new WindowScope(result.Window))
            {
                // Placeholder is not a data item — RequireSelection applies only to real rows.
                Assert.That(result.Grid.SelectedItem, Is.Null);
            }
        }

        // ------------------------------------------------------------------ Toggle DP at runtime (Integration)

        [Test, Category("Integration")]
        public void PlaceholderRow_ToggleDpOff_RemovesPlaceholder()
        {
            // Given: placeholder shown.
            var result = CreateGrid(data: null, showPlaceholder: true);
            using (new WindowScope(result.Window))
            {
                Assert.That(result.Grid.IsGridInitialized, Is.True);

                // When: DP turned off.
                result.Grid.ShowEmptyPlaceholderRow = false;
                result.Window.UpdateLayout();

                // Then
                Assert.That(result.Grid.IsPlaceholderActive, Is.False);
            }
        }

        // ------------------------------------------------------------------ private helpers

        private static KeyEventArgs MakeKey(Key key, Window window = null)
        {
            var source = window != null
                ? PresentationSource.FromVisual(window)
                : null;
            if (source == null)
                source = PresentationSource.FromVisual(Application.Current.MainWindow);

            return new KeyEventArgs(
                System.Windows.Input.InputManager.Current.PrimaryKeyboardDevice,
                source ?? throw new InvalidOperationException("No PresentationSource"),
                0,
                key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
        }

        private sealed class WindowScope : IDisposable
        {
            private readonly Window _w;
            public WindowScope(Window w) { _w = w; }
            public void Dispose() { _w?.Close(); }
        }
    }
}
