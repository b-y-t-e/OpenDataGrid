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
    /// Verifies OpenDataGridTextFilter.HorizontalArrowsNavigateGridControls — Left at the caret start and
    /// Right at the caret end move focus to the previous/next grid control (grid / text filter / combo
    /// filter) in TabIndex order, skipping unrelated controls entirely.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Category("Integration")]
    public class TextFilterTabOnArrowsTests
    {
        private sealed class Item { public string Name { get; set; } }

        private struct Rig
        {
            public OpenDataGrid Grid;
            public OpenDataGridTextFilter Filter;
            public OpenDataGridComboFilter Combo;
            public Button PlainBefore;
            public Button PlainAfter;
        }

        /// <summary>
        /// Tab order is declared via TabIndex and deliberately does NOT match tree order, so a traversal
        /// that merely walked the visual tree would fail. Plain buttons sit between the grid controls in
        /// TabIndex order — Tab would stop on them, this feature must not.
        /// </summary>
        /// <remarks>
        /// TabIndex order: plainBefore(1), combo(2), filter(3), plainAfter(4), grid(5).
        /// Expected: Left from filter → combo, Right from filter → grid.
        /// </remarks>
        private static Rig CreateRig()
        {
            var data = new List<Item> { new Item { Name = "alfa" }, new Item { Name = "beta" } };

            var grid = new OpenDataGrid { TabIndex = 5 };
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.DataSource = data;

            var filter = new OpenDataGridTextFilter { Grid = grid, Width = 200, Height = 30, TabIndex = 3 };
            var combo = new OpenDataGridComboFilter { Grid = grid, FilterField = "Name", Width = 120, TabIndex = 2 };
            var plainBefore = new Button { Content = "before", TabIndex = 1, Width = 60 };
            var plainAfter = new Button { Content = "after", TabIndex = 4, Width = 60 };

            var stack = new StackPanel();
            // Tree order is scrambled relative to TabIndex on purpose.
            stack.Children.Add(plainAfter);
            stack.Children.Add(grid);
            stack.Children.Add(filter);
            stack.Children.Add(combo);
            stack.Children.Add(plainBefore);

            var window = new Window
            {
                Width = 500,
                Height = 400,
                Content = stack,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
            };
            window.Show();
            window.UpdateLayout();

            return new Rig
            {
                Grid = grid,
                Filter = filter,
                Combo = combo,
                PlainBefore = plainBefore,
                PlainAfter = plainAfter,
            };
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
        public void HorizontalArrowsNavigateGridControls_Default_IsFalse()
        {
            // Given / When: A new text filter.
            var filter = new OpenDataGridTextFilter();

            // Then: Off by default — Left/Right keep their legacy grid-focusing behaviour.
            Assert.That(filter.HorizontalArrowsNavigateGridControls, Is.False);
        }

        [Test]
        public void Right_AtTextEnd_MovesToNextGridControlSkippingPlainControl()
        {
            // Given: Caret at the end; the next TabIndex stop is a plain button, then the grid.
            var rig = CreateRig();
            rig.Filter.HorizontalArrowsNavigateGridControls = true;
            rig.Filter.Text = "abc";
            rig.Filter.Focus();
            rig.Filter.CaretIndex = 3;

            // When: Right is pressed with nothing left to travel.
            PressKey(rig.Filter, Key.Right);

            // Then: The plain button is skipped — focus lands on the next grid control.
            Assert.That(rig.Grid.IsKeyboardFocusWithin, Is.True);
            Assert.That(rig.PlainAfter.IsKeyboardFocused, Is.False);
        }

        [Test]
        public void Left_AtTextStart_MovesToPreviousGridControlSkippingPlainControl()
        {
            // Given: Caret at the start; the previous grid control by TabIndex is the combo filter.
            var rig = CreateRig();
            rig.Filter.HorizontalArrowsNavigateGridControls = true;
            rig.Filter.Text = "abc";
            rig.Filter.Focus();
            rig.Filter.CaretIndex = 0;

            // When: Left is pressed with nothing left to travel.
            PressKey(rig.Filter, Key.Left);

            // Then: Focus lands on the combo filter, not on the plain button before it.
            Assert.That(rig.Combo.IsKeyboardFocusWithin, Is.True);
            Assert.That(rig.PlainBefore.IsKeyboardFocused, Is.False);
        }

        [Test]
        public void Left_AtFirstGridControl_SwallowsKeyWithoutEscaping()
        {
            // Given: Focus on the combo filter's neighbour-less end — the first grid control by TabIndex.
            var rig = CreateRig();
            rig.Filter.HorizontalArrowsNavigateGridControls = true;
            rig.Combo.TabIndex = 99; // push combo behind the filter, making the filter the first grid control
            rig.Filter.Text = "abc";
            rig.Filter.Focus();
            rig.Filter.CaretIndex = 0;

            // When: Left is pressed with no previous grid control to go to.
            PressKey(rig.Filter, Key.Left);

            // Then: The key is swallowed — focus stays put rather than escaping to a plain control.
            Assert.That(rig.Filter.IsKeyboardFocused, Is.True);
            Assert.That(rig.PlainBefore.IsKeyboardFocused, Is.False);
        }

        [Test]
        public void Right_MidText_KeepsFocusForCaretMovement()
        {
            // Given: Caret in the middle of the text, traversal enabled.
            var rig = CreateRig();
            rig.Filter.HorizontalArrowsNavigateGridControls = true;
            rig.Filter.Text = "abc";
            rig.Filter.Focus();
            rig.Filter.CaretIndex = 1;

            // When: Right is pressed while the caret still has text to travel.
            PressKey(rig.Filter, Key.Right);

            // Then: The key belongs to the caret — focus stays in the filter.
            Assert.That(rig.Filter.IsKeyboardFocused, Is.True);
            Assert.That(rig.Grid.IsKeyboardFocusWithin, Is.False);
        }

        [Test]
        public void Left_MidText_KeepsFocusForCaretMovement()
        {
            // Given: Caret in the middle of the text, traversal enabled.
            var rig = CreateRig();
            rig.Filter.HorizontalArrowsNavigateGridControls = true;
            rig.Filter.Text = "abc";
            rig.Filter.Focus();
            rig.Filter.CaretIndex = 2;

            // When: Left is pressed while the caret still has text to travel.
            PressKey(rig.Filter, Key.Left);

            // Then: The key belongs to the caret — focus stays in the filter.
            Assert.That(rig.Filter.IsKeyboardFocused, Is.True);
            Assert.That(rig.Combo.IsKeyboardFocusWithin, Is.False);
        }

        [Test]
        public void Right_AtEndWithTextSelected_KeepsFocus()
        {
            // Given: Caret at the end but with an active selection.
            var rig = CreateRig();
            rig.Filter.HorizontalArrowsNavigateGridControls = true;
            rig.Filter.Text = "abc";
            rig.Filter.Focus();
            rig.Filter.SelectAll();

            // When: Right is pressed — normally it would collapse the selection.
            PressKey(rig.Filter, Key.Right);

            // Then: A live selection blocks traversal, matching the SelectionLength == 0 rule.
            Assert.That(rig.Filter.IsKeyboardFocused, Is.True);
            Assert.That(rig.Grid.IsKeyboardFocusWithin, Is.False);
        }

        [Test]
        public void Right_AtTextEnd_TakesPrecedenceOverFocusGridOnConfirm()
        {
            // Given: Traversal enabled alongside the grid-focusing flag, which fires on the same condition.
            var rig = CreateRig();
            rig.Filter.HorizontalArrowsNavigateGridControls = true;
            rig.Filter.FocusGridOnConfirm = true;
            rig.Filter.Text = "abc";
            rig.Filter.Focus();
            rig.Filter.CaretIndex = 3;

            // When: Right is pressed at the text end.
            PressKey(rig.Filter, Key.Right);

            // Then: Both paths would target the grid here; traversal is the one that ran.
            Assert.That(rig.Grid.IsKeyboardFocusWithin, Is.True);
        }

        [Test]
        public void Left_AtTextStart_NotGatedByFocusGridOnConfirm()
        {
            // Given: The grid-focusing path fully disabled.
            var rig = CreateRig();
            rig.Filter.HorizontalArrowsNavigateGridControls = true;
            rig.Filter.FocusGridOnConfirm = false;
            rig.Filter.Text = "abc";
            rig.Filter.Focus();
            rig.Filter.CaretIndex = 0;

            // When: Left is pressed at the text start.
            PressKey(rig.Filter, Key.Left);

            // Then: Traversal is independent of the grid link — it still moves to the combo filter.
            Assert.That(rig.Combo.IsKeyboardFocusWithin, Is.True);
        }

        [Test]
        public void Left_AtTextStart_Disabled_FocusesGridAsBefore()
        {
            // Given: Traversal off, legacy grid-focusing path on.
            var rig = CreateRig();
            rig.Filter.HorizontalArrowsNavigateGridControls = false;
            rig.Filter.Text = "abc";
            rig.Filter.Focus();
            rig.Filter.CaretIndex = 0;

            // When: Left is pressed at the text start.
            PressKey(rig.Filter, Key.Left);

            // Then: Existing behaviour is untouched — focus goes to the grid, not the combo filter.
            Assert.That(rig.Grid.IsKeyboardFocusWithin, Is.True);
            Assert.That(rig.Combo.IsKeyboardFocusWithin, Is.False);
        }
    }
}
