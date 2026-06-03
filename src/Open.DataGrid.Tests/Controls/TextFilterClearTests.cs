using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Controls
{
    /// <summary>
    /// Verifies that setting OpenDataGridTextFilter.Text to empty clears the filter immediately,
    /// even when the TextFilter has keyboard focus (VM-driven clear while user has focus scenario).
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Category("Integration")]
    public class TextFilterClearTests
    {
        private sealed class Item { public string Name { get; set; } }

        private struct Rig
        {
            public OpenDataGrid Grid;
            public OpenDataGridTextFilter Filter;
            public Window Window;
            public List<Item> Data;
        }

        private static Rig CreateRig() => CreateRig("alfa", "beta", "gamma", "lod\u00f3wka");

        private static Rig CreateRig(params string[] names)
        {
            var data = new List<Item>();
            foreach (string n in names)
                data.Add(new Item { Name = n });

            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.DataSource = data;

            var filter = new OpenDataGridTextFilter
            {
                Grid = grid,
                Width = 200,
                Height = 30,
            };

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

            return new Rig { Grid = grid, Filter = filter, Window = window, Data = data };
        }

        // -----------------------------------------------------------------------

        [Test]
        public void ClearText_WithoutFocus_ClearsFilterImmediately()
        {
            // Given: filter applied ("alfa"), no keyboard focus on filter.
            var rig = CreateRig();
            try
            {
                rig.Filter.Text = "alfa";
                rig.Window.UpdateLayout();
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(1), "precondition: filter active");

                // When: VM sets Text="" while filter has no keyboard focus.
                rig.Filter.Text = "";

                // Then: filter cleared synchronously — no debounce wait needed.
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(rig.Data.Count));
            }
            finally { rig.Window.Close(); }
        }

        [Test]
        public void ClearText_WhileFilterHasKeyboardFocus_ClearsFilterImmediately()
        {
            // Given: filter text "alfa" applied (focus on grid so TextChanged takes immediate path),
            //        then focus moved back to the filter to simulate user being in the textbox.
            var rig = CreateRig();
            try
            {
                // Move focus to the grid so filter.Text change below is not debounced.
                rig.Grid.Focus();
                rig.Filter.Text = "alfa";
                rig.Window.UpdateLayout();
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(1), "precondition: filter active");

                // Move keyboard focus back to the filter (user clicked into the textbox).
                rig.Filter.Focus();

                // When: VM clears the text while focus is still on the filter (barcode/command scenario).
                rig.Filter.Text = "";

                // Then: filter cleared immediately — debounce must NOT delay the clear.
                // Before the fix this would leave filter active for ~300 ms.
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(rig.Data.Count));
            }
            finally { rig.Window.Close(); }
        }

        [Test]
        public void ClearText_WhilePendingDebounce_StopsTimerAndClearsImmediately()
        {
            // Given: "alfa" filter is active (1 row), user has focus and types an extra char
            //        → ScheduleFilterUpdate fires (debounce starts, old "alfa" predicate still active).
            var rig = CreateRig();
            try
            {
                rig.Grid.Focus();
                rig.Filter.Text = "alfa";
                rig.Window.UpdateLayout();
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(1), "precondition: filter active");

                // User has focus and types "alfax" — debounce starts, old predicate still registered.
                rig.Filter.Focus();
                rig.Filter.Text = "alfax";   // IsKeyboardFocused=true → ScheduleFilterUpdate (timer armed)

                // When: VM immediately clears Text (barcode scan / command) — timer still pending.
                rig.Filter.Text = "";

                // Then: debounce stopped, filter unregistered synchronously.
                // Before the fix, "alfa" predicate would remain until the 300 ms timer fired.
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(rig.Data.Count));
            }
            finally { rig.Window.Close(); }
        }

        [Test]
        public void ClearText_WhitespaceOnly_ClearsFilterImmediately()
        {
            // Given: filter applied ("beta").
            var rig = CreateRig();
            try
            {
                rig.Filter.Text = "beta";
                rig.Window.UpdateLayout();
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(1), "precondition: filter active");

                // When: VM sets whitespace-only text (trims to empty).
                rig.Filter.Text = "   ";

                // Then: filter cleared immediately.
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(rig.Data.Count));
            }
            finally { rig.Window.Close(); }
        }

        [Test]
        public void IgnoreDiacritics_DefaultTrue_MatchesAsciiEquivalent()
        {
            // Given: data contains an accented "lodowka" variant.
            var rig = CreateRig();
            try
            {
                // When: searching with the ASCII equivalent while using the default setting.
                rig.Filter.Text = "lodowka";
                rig.Window.UpdateLayout();

                // Then: default behavior ignores diacritics.
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(1));
            }
            finally { rig.Window.Close(); }
        }

        [Test]
        public void IgnoreDiacritics_WhenFalse_DoesNotMatchAsciiEquivalent()
        {
            // Given: data contains an accented "lodowka" variant.
            var rig = CreateRig();
            try
            {
                rig.Filter.IgnoreDiacritics = false;

                // When: searching with the ASCII equivalent.
                rig.Filter.Text = "lodowka";
                rig.Window.UpdateLayout();

                // Then: accent-sensitive behavior is still available when explicitly requested.
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(0));
            }
            finally { rig.Window.Close(); }
        }

        // -----------------------------------------------------------------------
        // Polish diacritic coverage — each pair: (ASCII query, accented data value)

        [TestCase("zywiec",  "żywiec",  Description = "ż → z via NFD + NonSpacingMark strip")]
        [TestCase("ZYWIEC",  "Żywiec",  Description = "Ż → Z case-insensitive")]
        [TestCase("lodz",    "Łódź",    Description = "ł (no NFD decomp) + ó + ź mixed")]
        [TestCase("zazolc",  "Zażółć",  Description = "ą, ó, ł, ć combined")]
        [TestCase("gesla",   "Gęślą",   Description = "ę, ś, ą combined")]
        [TestCase("zrodlo",  "źródło",  Description = "ź, ó, ó, ł combined")]
        [TestCase("pen",     "pęn",     Description = "ę → e")]
        [TestCase("sen",     "sęn",     Description = "ę → e distinct from ś")]
        public void IgnoreDiacritics_MatchesPolishLetters(string query, string dataValue)
        {
            // Given: data contains exactly one item whose Name uses Polish diacritics.
            var rig = CreateRig(dataValue);
            try
            {
                // When: filter typed without diacritics.
                rig.Filter.Text = query;
                rig.Window.UpdateLayout();

                // Then: item found — diacritic-insensitive match works for this character set.
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(1),
                    $"query '{query}' should match data value '{dataValue}' when IgnoreDiacritics=true");
            }
            finally { rig.Window.Close(); }
        }

        [Test]
        public void FilterRemovedFromTree_ClearsFilterImmediately()
        {
            // Given: filter applied ("alfa"), then the filter control is permanently
            //        removed from the visual tree (DataTemplate swap / parent recycle scenario).
            var rig = CreateRig();
            try
            {
                rig.Filter.Text = "alfa";
                rig.Window.UpdateLayout();
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(1), "precondition: filter active");

                // When: parent removes the filter from the tree (fires Unloaded).
                var stack = (StackPanel)rig.Filter.Parent;
                stack.Children.Remove(rig.Filter);
                rig.Window.UpdateLayout();
                // Unloaded is raised via a posted dispatcher operation — flush the queue.
                rig.Window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

                // Then: predicate unregistered — grid shows all rows, no zombie filter.
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(rig.Data.Count));
            }
            finally { rig.Window.Close(); }
        }

        [Test]
        public void FilterRemovedFromTree_DoesNotLeaveStalePendingSignal()
        {
            // Given: filter applied ("alfa"), then permanently removed from the visual tree.
            // Regression: a stale NotifyFilterPending entry in GridWorkCoordinator would leave
            // the loading overlay visible forever with no control left to clear it.
            var rig = CreateRig();
            try
            {
                rig.Filter.Text = "alfa";
                rig.Window.UpdateLayout();
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(1), "precondition: filter active");

                // When: filter permanently removed (fires Unloaded, Grid DP still bound).
                var stack = (StackPanel)rig.Filter.Parent;
                stack.Children.Remove(rig.Filter);
                rig.Window.UpdateLayout();
                rig.Window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

                // Then: no stale pending signal — overlay would otherwise be stuck forever.
                Assert.That(rig.Grid.HasPendingFilterSignals, Is.False);
            }
            finally { rig.Window.Close(); }
        }
    }
}
