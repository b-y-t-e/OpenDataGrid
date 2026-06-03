using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Models;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Selection
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class SelectedItemTests
    {
        // ------------------------------------------------------------------ helpers

        private static OpenDataGrid CreateUninitializedGrid()
        {
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            return grid;
        }

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

        /// <summary>Creates a fully initialized grid inside a Window.</summary>
        private static GridResult CreateInitializedGrid(
            int itemCount = 20,
            bool autoScroll = true,
            double windowWidth = 300,
            double windowHeight = 200,
            double columnWidth = 200)
        {
            var data = MakeData(itemCount);
            var grid = new OpenDataGrid { AutoScrollToSelectedItem = autoScroll };
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = columnWidth });
            grid.DataSource = data;

            var window = new Window
            {
                Width = windowWidth,
                Height = windowHeight,
                Content = grid,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            window.Show();
            window.UpdateLayout();

            return new GridResult { Grid = grid, Window = window, Data = data };
        }

        // ------------------------------------------------------------------ Tier 1: no Window

        // --- Feature 1: _pendingSelectedItem ---

        [Test]
        public void SelectedItem_SetBeforeDataSource_StashedAsPending()
        {
            // Given: An uninitialized grid without DataSource.
            var grid = CreateUninitializedGrid();
            var item = new SortItem { Name = "A" };

            // When: SelectedItem is set.
            grid.SelectedItem = item;

            // Then: Item is stashed as pending selection.
            Assert.That(grid._selection.PendingSelectedItem, Is.SameAs(item));
        }

        [Test]
        public void SelectedItem_SetBeforeInitialized_StashedEvenWithDataSource()
        {
            // Given: Grid has DataSource but is still not initialized.
            var grid = CreateUninitializedGrid();
            var data = MakeData(5);
            grid.DataSource = data;

            // When: SelectedItem is set before visual initialization.
            grid.SelectedItem = data[2];

            // Then: Selection is stored as pending.
            Assert.That(grid._selection.PendingSelectedItem, Is.SameAs(data[2]));
        }

        [Test]
        public void SelectedItem_SetToNull_ClearsPending()
        {
            // Given: Grid with a pending selected item.
            var grid = CreateUninitializedGrid();
            var item = new SortItem { Name = "A" };
            grid.SelectedItem = item;
            Assert.That(grid._selection.PendingSelectedItem, Is.Not.Null);

            // When: SelectedItem is set to null.
            grid.SelectedItem = null;

            // Then: Pending selection is cleared.
            Assert.That(grid._selection.PendingSelectedItem, Is.Null);
        }

        [Test]
        public void SelectedItem_SetTwiceBeforeInit_LastOneWins()
        {
            // Given: Uninitialized grid and two candidate items.
            var grid = CreateUninitializedGrid();
            var itemA = new SortItem { Name = "A" };
            var itemB = new SortItem { Name = "B" };

            // When: SelectedItem is set twice before initialization.
            grid.SelectedItem = itemA;
            grid.SelectedItem = itemB;

            // Then: Last assignment is kept as pending.
            Assert.That(grid._selection.PendingSelectedItem, Is.SameAs(itemB));
        }

        // --- Feature 2: AutoScrollToSelectedItem DP ---

        [Test]
        public void AutoScrollToSelectedItem_DefaultIsTrue()
        {
            // Given: A new grid instance.
            var grid = new OpenDataGrid();

            // When: AutoScrollToSelectedItem is read.
            var autoScroll = grid.AutoScrollToSelectedItem;

            // Then: Default value is true.
            Assert.That(autoScroll, Is.True);
        }

        [Test]
        public void AutoScrollToSelectedItem_RoundTrips()
        {
            // Given: A new grid instance.
            var grid = new OpenDataGrid();

            // When: AutoScrollToSelectedItem is set to false.
            grid.AutoScrollToSelectedItem = false;

            // Then: Property returns false.
            Assert.That(grid.AutoScrollToSelectedItem, Is.False);
        }

        // --- Feature 3: _internalSelectionChange guard ---

        [Test]
        public void InternalSelectionChange_DefaultIsFalse()
        {
            // Given: A new grid instance.
            var grid = new OpenDataGrid();

            // When: Internal selection guard value is read.
            var guard = grid._selection.InternalSelectionChange;

            // Then: Default guard value is false.
            Assert.That(guard, Is.False);
        }

        [Test]
        public void SelectedItem_SetWhileInternalGuard_DoesNotStashPending()
        {
            // Given: Uninitialized grid with internal selection guard enabled.
            var grid = CreateUninitializedGrid();
            var item = new SortItem { Name = "A" };
            grid._selection.InternalSelectionChange = true;

            // When: SelectedItem is set under guard.
            grid.SelectedItem = item;

            // Then: No pending selection is stashed.
            Assert.That(grid._selection.PendingSelectedItem, Is.Null);
            grid._selection.InternalSelectionChange = false;
        }

        [Test]
        public void SelectedItem_SetWhileInternalGuard_DoesNotClearExistingPending()
        {
            // Given: Existing pending selection and internal guard enabled.
            var grid = CreateUninitializedGrid();
            var itemA = new SortItem { Name = "A" };
            var itemB = new SortItem { Name = "B" };
            grid.SelectedItem = itemA;   // stash A
            grid._selection.InternalSelectionChange = true;

            // When: SelectedItem changes while guard is active.
            grid.SelectedItem = itemB;   // guarded — should not touch pending

            // Then: Previously pending item remains unchanged.
            Assert.That(grid._selection.PendingSelectedItem, Is.SameAs(itemA));
            grid._selection.InternalSelectionChange = false;
        }

        // ------------------------------------------------------------------ Tier 2: Window required

        [Test, Category("Integration")]
        public void PendingSelectedItem_ReplayedOnInitialize()
        {
            // Given: Grid with pending SelectedItem before visual initialization.
            var data = MakeData(10);
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.DataSource = data;
            grid.SelectedItem = data[5];
            Assert.That(grid._selection.PendingSelectedItem, Is.Not.Null, "Should be stashed");

            var window = new Window
            {
                Width = 300, Height = 200, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            try
            {
                // When: Grid initializes inside a real Window.
                window.Show();
                window.UpdateLayout();

                // Then: Pending selection is consumed and reflected as active selection.
                Assert.That(grid._selection.PendingSelectedItem, Is.Null, "Pending should be consumed");
                Assert.That(grid.SelectedIndex, Is.EqualTo(5));
                Assert.That(grid.SelectedItem, Is.SameAs(data[5]));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void PendingSelectedItem_ItemNotInDataSource_Ignored()
        {
            // Given: Pending selection item that does not belong to DataSource.
            var data = MakeData(10);
            var ghost = new SortItem { Name = "Ghost" };
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.DataSource = data;
            grid.SelectedItem = ghost;

            var window = new Window
            {
                Width = 300, Height = 200, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            try
            {
                // When: Grid is initialized.
                window.Show();
                window.UpdateLayout();

                // Then: Pending is cleared. Since no valid item was found, grid falls back to
                // selecting the first item in the collection (DataSource[0] fallback).
                Assert.That(grid._selection.PendingSelectedItem, Is.Null);
                Assert.That(grid.SelectedIndex, Is.EqualTo(0));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void AutoScrollTrue_VMSetsOffScreenItem_ScrollBarMoves()
        {
            // Given: Initialized grid with AutoScrollToSelectedItem enabled.
            var result = CreateInitializedGrid(itemCount: 50, autoScroll: true);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                var scrollBar = (ScrollBar)grid.Template.FindName("PART_VerticalScrollBar", grid);
                Assert.That(scrollBar, Is.Not.Null);
                Assert.That(scrollBar.Value, Is.EqualTo(0).Within(0.01), "Should start at top");

                // When: ViewModel sets SelectedItem to an off-screen row.
                grid.SelectedItem = data[40];

                // Then: Selection changes and scrollbar moves.
                Assert.That(grid.SelectedIndex, Is.EqualTo(40));
                Assert.That(scrollBar.Value, Is.GreaterThan(0), "Scrollbar should have moved");
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void AutoScrollTrue_VMSetsAlreadyVisibleItem_ScrollBarStays()
        {
            // Given: Initialized grid with AutoScrollToSelectedItem enabled and viewport moved down.
            var result = CreateInitializedGrid(itemCount: 50, autoScroll: true);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                var scrollBar = (ScrollBar)grid.Template.FindName("PART_VerticalScrollBar", grid);
                Assert.That(scrollBar, Is.Not.Null);

                grid.SelectedItem = data[40];
                int firstVisible = (int)scrollBar.Value;
                int visibleCandidate = firstVisible + 1;
                Assert.That(visibleCandidate, Is.LessThan(data.Count));

                double before = scrollBar.Value;

                // When: ViewModel selects an item that is already visible.
                grid.SelectedItem = data[visibleCandidate];

                // Then: Scrollbar value stays unchanged and selection is applied.
                Assert.That(scrollBar.Value, Is.EqualTo(before).Within(0.01));
                Assert.That(grid.SelectedIndex, Is.EqualTo(visibleCandidate));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void AutoScrollTrue_SelectLastItem_ScrollBarReachesMaximum()
        {
            // Given: Initialized grid with many rows and AutoScroll enabled.
            var result = CreateInitializedGrid(itemCount: 50, autoScroll: true);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                var scrollBar = (ScrollBar)grid.Template.FindName("PART_VerticalScrollBar", grid);
                Assert.That(scrollBar, Is.Not.Null);

                // When: ViewModel selects the last item.
                grid.SelectedItem = data[data.Count - 1];

                // Then: Scrollbar reaches max and last item is selected.
                Assert.That(grid.SelectedIndex, Is.EqualTo(data.Count - 1));
                Assert.That(scrollBar.Value, Is.EqualTo(scrollBar.Maximum).Within(0.01));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void AutoScrollFalse_VMSetsOffScreenItem_ScrollBarStays()
        {
            // Given: Initialized grid with AutoScrollToSelectedItem disabled.
            var result = CreateInitializedGrid(itemCount: 50, autoScroll: false);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                var scrollBar = (ScrollBar)grid.Template.FindName("PART_VerticalScrollBar", grid);
                Assert.That(scrollBar, Is.Not.Null);

                // When: ViewModel sets SelectedItem to an off-screen row.
                grid.SelectedItem = data[40];

                // Then: Selection changes but scrollbar stays at top.
                Assert.That(grid.SelectedIndex, Is.EqualTo(40));
                Assert.That(scrollBar.Value, Is.EqualTo(0).Within(0.01), "Scrollbar should not move");
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void AutoScrollTrue_WithHorizontalScrollbar_StillReachesBottom()
        {
            // Given: Narrow window and wide column to force horizontal scrollbar.
            var result = CreateInitializedGrid(itemCount: 50, autoScroll: true, windowWidth: 180, windowHeight: 200, columnWidth: 600);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                var vScrollBar = (ScrollBar)grid.Template.FindName("PART_VerticalScrollBar", grid);
                var hScrollBar = (ScrollBar)grid.Template.FindName("PART_HorizontalScrollBar", grid);
                Assert.That(vScrollBar, Is.Not.Null);
                Assert.That(hScrollBar, Is.Not.Null);
                Assert.That(hScrollBar.Visibility, Is.EqualTo(Visibility.Visible));

                // When: ViewModel selects the last item.
                grid.SelectedItem = data[data.Count - 1];

                // Then: Vertical scrollbar still reaches its maximum.
                Assert.That(vScrollBar.Value, Is.EqualTo(vScrollBar.Maximum).Within(0.01));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void AutoScrollTrue_TinyViewport_DoesNotThrowAndSelectsItem()
        {
            // Given: Very small viewport height.
            var result = CreateInitializedGrid(itemCount: 50, autoScroll: true, windowWidth: 300, windowHeight: 80, columnWidth: 200);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                var scrollBar = (ScrollBar)grid.Template.FindName("PART_VerticalScrollBar", grid);
                Assert.That(scrollBar, Is.Not.Null);

                // When: ViewModel selects far item.
                Assert.DoesNotThrow(() => grid.SelectedItem = data[40]);

                // Then: Selection is applied and scroll value stays in range.
                Assert.That(grid.SelectedIndex, Is.EqualTo(40));
                Assert.That(scrollBar.Value, Is.InRange(scrollBar.Minimum, scrollBar.Maximum));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void InternalGuardCleanedUpAfterSelection()
        {
            // Given: Initialized grid.
            var result = CreateInitializedGrid();
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                // When: SelectedItem is changed.
                grid.SelectedItem = data[3];

                // Then: Internal selection guard is cleared after operation.
                Assert.That(grid._selection.InternalSelectionChange, Is.False, "Guard must be cleared after selection");
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void HeaderAndCellFonts_SetBeforeInitialize_AreApplied()
        {
            // Given: Grid with configured header/cell default fonts before visual initialization.
            var data = MakeData(20);
            var grid = new OpenDataGrid
            {
                HeaderFontFamily = new FontFamily("Segoe UI"),
                HeaderFontSize = 14,
                HeaderFontWeight = FontWeights.Bold,
                HeaderBackground = Brushes.DarkSlateGray,
                HeaderForeground = Brushes.White,
                CellFontFamily = new FontFamily("Consolas"),
                CellFontSize = 13,
                CellFontWeight = FontWeights.Medium
            };
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Header = "Name", Width = 200 });
            grid.DataSource = data;

            var window = new Window
            {
                Width = 300, Height = 200, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            try
            {
                // When: Grid initializes in a Window.
                window.Show();
                window.UpdateLayout();

                // Then: Header and first visible cell inherit configured fonts.
                var headerCanvas = (Canvas)grid.Template.FindName("PART_HeaderCanvas", grid);
                Assert.That(headerCanvas, Is.Not.Null);
                var headerBorder = headerCanvas.Children[0] as Border;
                Assert.That(headerBorder, Is.Not.Null);
                var headerPanel = ((Grid)headerBorder.Child).Children[0] as StackPanel;
                var headerText = headerPanel.Children[0] as TextBlock;
                Assert.That(headerText, Is.Not.Null);
                Assert.That(headerText.FontFamily.Source, Is.EqualTo("Segoe UI"));
                Assert.That(headerText.FontSize, Is.EqualTo(14).Within(0.01));
                Assert.That(headerText.FontWeight, Is.EqualTo(FontWeights.Bold));
                Assert.That(headerBorder.Background, Is.SameAs(Brushes.DarkSlateGray));
                Assert.That(headerText.Foreground, Is.SameAs(Brushes.White));

                var canvas = (Canvas)grid.Template.FindName("PART_Canvas", grid);
                Assert.That(canvas, Is.Not.Null);
                var row = canvas.Children.OfType<OpenDataGridRow>().FirstOrDefault(r => r.DataIndex == 0);
                Assert.That(row, Is.Not.Null);
                var cell = row.GetCell(0);
                Assert.That(cell, Is.Not.Null);
                Assert.That(cell.FontFamily.Source, Is.EqualTo("Consolas"));
                Assert.That(cell.FontSize, Is.EqualTo(13).Within(0.01));
                Assert.That(cell.FontWeight, Is.EqualTo(FontWeights.Medium));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void HeaderAndCellFonts_RuntimeChange_UpdatesVisualsWithoutChangingSelection()
        {
            // Given: Initialized grid with selection.
            var result = CreateInitializedGrid(itemCount: 50, autoScroll: true);
            var grid = result.Grid; var window = result.Window; var data = result.Data;
            try
            {
                grid.SelectedItem = data[10];
                Assert.That(grid.SelectedIndex, Is.EqualTo(10));

                // When: Font defaults are changed at runtime.
                grid.HeaderFontFamily = new FontFamily("Calibri");
                grid.HeaderFontSize = 16;
                grid.HeaderFontWeight = FontWeights.ExtraBold;
                grid.HeaderBackground = Brushes.MidnightBlue;
                grid.HeaderForeground = Brushes.LightYellow;
                grid.CellFontFamily = new FontFamily("Courier New");
                grid.CellFontSize = 12;
                grid.CellFontWeight = FontWeights.DemiBold;
                window.UpdateLayout();

                // Then: Selection is preserved and visuals are updated.
                Assert.That(grid.SelectedItem, Is.SameAs(data[10]));
                Assert.That(grid.SelectedIndex, Is.EqualTo(10));

                var headerCanvas = (Canvas)grid.Template.FindName("PART_HeaderCanvas", grid);
                var headerBorder = headerCanvas.Children[0] as Border;
                var headerPanel = ((Grid)headerBorder.Child).Children[0] as StackPanel;
                var headerText = headerPanel.Children[0] as TextBlock;
                Assert.That(headerText.FontFamily.Source, Is.EqualTo("Calibri"));
                Assert.That(headerText.FontSize, Is.EqualTo(16).Within(0.01));
                Assert.That(headerText.FontWeight, Is.EqualTo(FontWeights.ExtraBold));
                Assert.That(headerBorder.Background, Is.SameAs(Brushes.MidnightBlue));
                Assert.That(headerText.Foreground, Is.SameAs(Brushes.LightYellow));

                var canvas = (Canvas)grid.Template.FindName("PART_Canvas", grid);
                Assert.That(canvas, Is.Not.Null);
                var row = canvas.Children.OfType<OpenDataGridRow>().FirstOrDefault(r => r.DataIndex == 10);
                Assert.That(row, Is.Not.Null);
                var cell = row.GetCell(0);
                Assert.That(cell.FontFamily.Source, Is.EqualTo("Courier New"));
                Assert.That(cell.FontSize, Is.EqualTo(12).Within(0.01));
                Assert.That(cell.FontWeight, Is.EqualTo(FontWeights.DemiBold));
            }
            finally { window.Close(); }
        }

        // ------------------------------------------------------------------ SelectedColumn

        private static GridResult CreateCellModeGrid(
            string selectedColumn = null,
            int itemCount = 20,
            double windowWidth = 500,
            double windowHeight = 200)
        {
            var data = MakeData(itemCount);
            var grid = new OpenDataGrid { NavigationMode = NavigationMode.Cell };
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name",     Header = "Nazwa",  Width = 150 });
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "IntValue", Header = "Liczba", Width = 150 });
            grid.DataSource = data;
            if (selectedColumn != null)
                grid.SelectedColumn = selectedColumn;

            var window = new Window
            {
                Width = windowWidth,
                Height = windowHeight,
                Content = grid,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            window.Show();
            window.UpdateLayout();
            return new GridResult { Grid = grid, Window = window, Data = data };
        }

        [Test, Category("Integration")]
        public void SelectedColumn_ByHeader_SetsDefaultCellOnFocus()
        {
            // Given: Cell-mode grid with SelectedColumn pointing to second column by Header.
            var result = CreateCellModeGrid(selectedColumn: "Liczba");
            var grid = result.Grid; var window = result.Window;
            try
            {
                // When: Grid receives keyboard focus.
                grid.Focus();

                // Then: Focused column is the second one.
                Assert.That(grid.FocusedColumnIndex,  Is.EqualTo(1));
                Assert.That(grid.FocusedColumnHeader, Is.EqualTo("Liczba"));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void SelectedColumn_ByBindingPath_SetsDefaultCellOnFocus()
        {
            // Given: Cell-mode grid with SelectedColumn pointing to second column by BindingPath.
            var result = CreateCellModeGrid(selectedColumn: "IntValue");
            var grid = result.Grid; var window = result.Window;
            try
            {
                // When: Grid receives keyboard focus.
                grid.Focus();

                // Then: Focused column is the second one (matched by BindingPath).
                Assert.That(grid.FocusedColumnIndex,  Is.EqualTo(1));
                Assert.That(grid.FocusedColumnHeader, Is.EqualTo("Liczba"));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void SelectedColumn_UnknownName_FallsBackToFirstVisibleColumn()
        {
            // Given: Cell-mode grid with SelectedColumn set to a non-existing name.
            var result = CreateCellModeGrid(selectedColumn: "NonExistentColumn");
            var grid = result.Grid; var window = result.Window;
            try
            {
                // When: Grid receives keyboard focus.
                grid.Focus();

                // Then: Focus falls back to first visible column (index 0).
                Assert.That(grid.FocusedColumnIndex, Is.EqualTo(0));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void SelectedColumn_IgnoredInRowMode()
        {
            // Given: Row-mode grid with SelectedColumn set.
            var data = MakeData(10);
            var grid = new OpenDataGrid { NavigationMode = NavigationMode.Row };
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name",     Header = "Nazwa",  Width = 150 });
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "IntValue", Header = "Liczba", Width = 150 });
            grid.DataSource = data;
            grid.SelectedColumn = "Liczba";

            var window = new Window
            {
                Width = 400, Height = 200, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            window.Show();
            window.UpdateLayout();
            try
            {
                // When: Grid receives keyboard focus.
                grid.Focus();

                // Then: FocusedColumnIndex is not changed to "Liczba" (index 1) by SelectedColumn
                // because SelectedColumn is a no-op in Row mode.
                // After normal initialization, FocusedColumnIndex is 0 (first column).
                Assert.That(grid.FocusedColumnIndex, Is.Not.EqualTo(1),
                    "SelectedColumn should not move focus to 'Liczba' (index 1) in Row mode");
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void SelectedColumn_DoesNotOverridePreviousFocus()
        {
            // Given: Cell-mode grid focused on second column; a sibling TextBox to steal focus.
            var data = MakeData(10);
            var grid = new OpenDataGrid { NavigationMode = NavigationMode.Cell, SelectedColumn = "Liczba" };
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name",     Header = "Nazwa",  Width = 150 });
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "IntValue", Header = "Liczba", Width = 150 });
            grid.DataSource = data;

            var textBox = new TextBox { Width = 100, Height = 30 };
            var panel = new StackPanel();
            panel.Children.Add(grid);
            panel.Children.Add(textBox);

            var window = new Window
            {
                Width = 500, Height = 300, Content = panel,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            window.Show();
            window.UpdateLayout();
            try
            {
                // Given (continued): First focus applies default column.
                grid.Focus();
                Assert.That(grid.FocusedColumnIndex, Is.EqualTo(1), "First focus: should land on 'Liczba'");

                // When: Focus moves away and returns.
                textBox.Focus();
                grid.Focus();

                // Then: Position unchanged — SelectedColumn does not override user's previous focus.
                Assert.That(grid.FocusedColumnIndex, Is.EqualTo(1),
                    "Second focus: should stay on 'Liczba', not reset to default");
            }
            finally { window.Close(); }
        }
    }
}
