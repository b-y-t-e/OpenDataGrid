using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Controls
{
    /// <summary>
    /// Verifies OpenDataGridTextFilter.KeepFocusOnNavigation — Up/Down/PageUp/PageDown move the grid's
    /// row selection remotely while keyboard focus stays in the filter (search-box-over-list pattern).
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Category("Integration")]
    public class TextFilterNavigationForwardTests
    {
        private sealed class Item { public string Name { get; set; } }

        private struct Rig
        {
            public OpenDataGrid Grid;
            public OpenDataGridTextFilter Filter;
            public List<Item> Data;
        }

        private static Rig CreateRig()
        {
            var data = new List<Item>
            {
                new Item { Name = "alfa" },
                new Item { Name = "beta" },
                new Item { Name = "gamma" },
            };

            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.DataSource = data;

            var filter = new OpenDataGridTextFilter { Grid = grid, Width = 200, Height = 30 };

            var stack = new StackPanel();
            stack.Children.Add(filter);
            stack.Children.Add(grid);

            var window = new Window
            {
                Width = 400,
                Height = 300,
                Content = stack,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
            };
            window.Show();
            window.UpdateLayout();

            return new Rig { Grid = grid, Filter = filter, Data = data };
        }

        private static void PressKey(UIElement target, Key key)
        {
            target.RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
            {
                RoutedEvent = UIElement.PreviewKeyDownEvent
            });
        }

        // -----------------------------------------------------------------------

        [Test]
        public void KeepFocusOnNavigation_Default_IsFalse()
        {
            // Given / When: A new text filter.
            var filter = new OpenDataGridTextFilter();

            // Then: Off by default — arrows move focus to the grid (legacy behaviour).
            Assert.That(filter.KeepFocusOnNavigation, Is.False);
        }

        [Test]
        public void ArrowDown_Disabled_MovesFocusToGridWithoutMovingSelection()
        {
            // Given: A filter with keep-focus off, selection on the first row.
            var rig = CreateRig();
            rig.Filter.Focus();
            var before = rig.Grid.SelectedItem;

            // When: ArrowDown is pressed in the filter.
            PressKey(rig.Filter, Key.Down);

            // Then: Legacy path — focus transfers, selection stays put.
            Assert.That(rig.Grid.IsKeyboardFocusWithin, Is.True);
            Assert.That(rig.Grid.SelectedItem, Is.SameAs(before));
        }

        [Test]
        public void ArrowDown_Enabled_MovesSelectionDownAndKeepsFocusInFilter()
        {
            // Given: A filter with keep-focus on, selection on the first row.
            var rig = CreateRig();
            rig.Filter.KeepFocusOnNavigation = true;
            rig.Filter.Focus();
            Assume.That(rig.Grid.SelectedItem, Is.SameAs(rig.Data[0]));

            // When: ArrowDown is pressed in the filter.
            PressKey(rig.Filter, Key.Down);

            // Then: The grid advanced one row, but focus never left the filter.
            Assert.That(rig.Grid.SelectedItem, Is.SameAs(rig.Data[1]));
            Assert.That(rig.Filter.IsKeyboardFocused, Is.True);
            Assert.That(rig.Grid.IsKeyboardFocusWithin, Is.False);
        }

        [Test]
        public void ArrowUp_Enabled_MovesSelectionBackAndKeepsFocusInFilter()
        {
            // Given: A filter with keep-focus on, selection walked down to the second row.
            var rig = CreateRig();
            rig.Filter.KeepFocusOnNavigation = true;
            rig.Filter.Focus();
            PressKey(rig.Filter, Key.Down);
            Assume.That(rig.Grid.SelectedItem, Is.SameAs(rig.Data[1]));

            // When: ArrowUp is pressed in the filter.
            PressKey(rig.Filter, Key.Up);

            // Then: The selection moved back up and focus stayed in the filter.
            Assert.That(rig.Grid.SelectedItem, Is.SameAs(rig.Data[0]));
            Assert.That(rig.Filter.IsKeyboardFocused, Is.True);
        }

        [Test]
        public void PageDown_Enabled_MovesSelectionAndKeepsFocusInFilter()
        {
            // Given: A filter with keep-focus on, selection on the first row.
            var rig = CreateRig();
            rig.Filter.KeepFocusOnNavigation = true;
            rig.Filter.Focus();

            // When: PageDown is pressed in the filter.
            PressKey(rig.Filter, Key.PageDown);

            // Then: The selection advanced past the first row, focus stayed in the filter.
            Assert.That(rig.Grid.SelectedItem, Is.Not.SameAs(rig.Data[0]));
            Assert.That(rig.Filter.IsKeyboardFocused, Is.True);
        }

        [Test]
        public void ArrowDown_Enabled_TakesPrecedenceOverFocusGridOnConfirm()
        {
            // Given: Keep-focus on together with the focus-transfer flag also on.
            var rig = CreateRig();
            rig.Filter.KeepFocusOnNavigation = true;
            rig.Filter.FocusGridOnConfirm = true;
            rig.Filter.Focus();

            // When: ArrowDown is pressed in the filter.
            PressKey(rig.Filter, Key.Down);

            // Then: Keep-focus wins for navigation keys — focus does not transfer.
            Assert.That(rig.Grid.SelectedItem, Is.SameAs(rig.Data[1]));
            Assert.That(rig.Grid.IsKeyboardFocusWithin, Is.False);
        }

        [Test]
        public void Enter_Enabled_StillMovesFocusToGrid()
        {
            // Given: A filter with keep-focus on and no EnterCommand bound.
            var rig = CreateRig();
            rig.Filter.KeepFocusOnNavigation = true;
            rig.Filter.Focus();

            // When: Enter is pressed in the filter.
            PressKey(rig.Filter, Key.Enter);

            // Then: Enter confirms rather than navigates — it is unaffected by KeepFocusOnNavigation.
            Assert.That(rig.Grid.IsKeyboardFocusWithin, Is.True);
        }

        [Test]
        public void ArrowLeft_Enabled_StaysInTextBoxForCaretMovement()
        {
            // Given: Keep-focus on, caret at the start of non-empty filter text.
            var rig = CreateRig();
            rig.Filter.KeepFocusOnNavigation = true;
            rig.Filter.Text = "abc";
            rig.Filter.Focus();
            rig.Filter.CaretIndex = 1;
            var before = rig.Grid.SelectedItem;

            // When: ArrowLeft is pressed.
            PressKey(rig.Filter, Key.Left);

            // Then: Left is not a grid navigation key — the grid is untouched.
            Assert.That(rig.Grid.SelectedItem, Is.SameAs(before));
        }
    }
}
