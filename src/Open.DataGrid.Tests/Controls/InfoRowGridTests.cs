using System.Collections.Generic;
using INotifyPropertyChanged = System.ComponentModel.INotifyPropertyChanged;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;
using PropertyChangedEventHandler = System.ComponentModel.PropertyChangedEventHandler;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Controls
{
    /// <summary>
    /// <see cref="OpenDataGrid.InfoRowBindingPath"/> end-to-end: rendering of info rows,
    /// navigation / scrolling in line space and dynamic text changes through INotifyPropertyChanged.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class InfoRowGridTests
    {
        // ------------------------------------------------------------------ helpers

        private sealed class PromoItem : INotifyPropertyChanged
        {
            private string _promo;
            public string Name { get; set; }
            public string Promo
            {
                get => _promo;
                set { _promo = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Promo))); }
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        /// <summary>Model raising PropertyChanged with a foreign sender (as proxies / wrappers do).</summary>
        private sealed class ProxySenderItem : INotifyPropertyChanged
        {
            private string _name;
            public string Name
            {
                get => _name;
                set { _name = value; PropertyChanged?.Invoke(new object(), new PropertyChangedEventArgs(nameof(Name))); }
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        private struct GridResult
        {
            public OpenDataGrid Grid;
            public Window Window;
            public List<PromoItem> Data;
        }

        private static GridResult CreateGrid(int count, params int[] withPromo)
        {
            var data = Enumerable.Range(0, count)
                .Select(i => new PromoItem { Name = $"Item{i}", Promo = withPromo.Contains(i) ? $"Promo {i}" : null })
                .ToList();
            var grid = new OpenDataGrid { InfoRowBindingPath = nameof(PromoItem.Promo), RowHeight = 20 };
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

        private static void Flush(Window window)
            => window.Dispatcher.Invoke(DispatcherPriority.Background, new System.Action(() => { }));

        private static void SortAndWait(GridResult result, OpenDataGridColumn column)
        {
            bool completed = false;
            void OnCompleted(object s, System.EventArgs e) => completed = true;
            result.Grid._sortEngine.SortCompleted += OnCompleted;
            result.Grid._sortEngine.Sort(column, result.Grid.DataSource);
            SpinWait.SpinUntil(() => completed, 3000);
            result.Grid._sortEngine.SortCompleted -= OnCompleted;
            Flush(result.Window);
        }

        private static IEnumerable<OpenDataGridRow> VisibleRows(OpenDataGrid grid)
            => grid._canvas.Children.OfType<OpenDataGridRow>().Where(r => r.Visibility == Visibility.Visible);

        // ------------------------------------------------------------------ tests

        [Test]
        public void InfoRowDefaults()
        {
            // Given / When: A new grid instance.
            var grid = new OpenDataGrid();

            // Then: Feature off, info rows pinned during horizontal scroll, italic text.
            Assert.That(grid.InfoRowBindingPath, Is.Null);
            Assert.That(grid.InfoRowScrollsHorizontally, Is.False);
            Assert.That(grid.InfoRowFontStyle, Is.EqualTo(FontStyles.Italic));
            Assert.That(grid.InfoRowBackground, Is.Null);
        }

        [Test, Category("Integration")]
        public void InfoRow_IsRenderedBelowOwner()
        {
            // Given: 10 rows, promo on row 1.
            var result = CreateGrid(10, 1);
            try
            {
                // When: Grid is laid out.
                var infoRows = VisibleRows(result.Grid).Where(r => r.IsInfoRow).ToList();

                // Then: Exactly one info row, owned by data index 1.
                Assert.That(infoRows, Has.Count.EqualTo(1));
                Assert.That(infoRows[0].InfoOwnerDataIndex, Is.EqualTo(1));
                Assert.That(result.Grid._rowEngine.TotalLines, Is.EqualTo(11));
            }
            finally { result.Window.Close(); }
        }

        [Test, Category("Integration")]
        public void DownArrow_StepsOverInfoRows_AndKeepsOwnerWithInfoVisible()
        {
            // Given: Every row has a promo (every other line is an info line); row 0 focused.
            var result = CreateGrid(20, Enumerable.Range(0, 20).ToArray());
            var grid = result.Grid;
            try
            {
                grid.ApplyCellFocus(0, 0, true);
                var source = PresentationSource.FromVisual(result.Window);
                var engine = grid._rowEngine;

                for (int expected = 1; expected < 20; expected++)
                {
                    // When: Down arrow is pressed.
                    grid._navEngine.HandleKeyDown(
                        new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Down) { RoutedEvent = Keyboard.KeyDownEvent });

                    // Then: The next data row is selected and it is fully visible together with its info line.
                    Assert.That(grid.SelectedItem, Is.SameAs(result.Data[expected]));
                    int firstLine = engine.GetFirstVisibleIndex();
                    int lastFullyVisible = firstLine + engine.GetVisibleRowCount() - 1;
                    Assert.That(engine.SortedToLine(expected), Is.GreaterThanOrEqualTo(firstLine));
                    Assert.That(engine.GetLastLineOfSorted(expected), Is.LessThanOrEqualTo(lastFullyVisible));
                }
            }
            finally { result.Window.Close(); }
        }

        [Test, Category("Integration")]
        public void ClickOnInfoRow_DoesNotChangeSelection()
        {
            // Given: Promo on row 1; row 3 selected.
            var result = CreateGrid(10, 1);
            var grid = result.Grid;
            try
            {
                grid.ApplyCellFocus(3, 0, false);
                int infoLine = grid._rowEngine.SortedToLine(1) + 1;

                // When: The click position is resolved over the info line.
                int hit = grid._selection.DataRowAtLine(grid._selection.LineAtCanvasY(infoLine * grid.RowHeight + 1));

                // Then: No data row is hit and the selection stays on row 3.
                Assert.That(grid._rowEngine.IsInfoLine(infoLine), Is.True);
                Assert.That(hit, Is.EqualTo(-1));
                Assert.That(grid.SelectedItem, Is.SameAs(result.Data[3]));
            }
            finally { result.Window.Close(); }
        }

        [Test, Category("Integration")]
        public void SortAndFilter_InfoRowStaysDirectlyBelowOwner()
        {
            // Given: Promo on rows 2 and 7.
            var result = CreateGrid(10, 2, 7);
            var grid = result.Grid;
            var engine = grid._rowEngine;
            try
            {
                // When: Filtered to rows 2, 5, 7 and sorted descending by Name (two header sorts).
                grid.RegisterFilter("t", item => new[] { "Item2", "Item5", "Item7" }.Contains(((PromoItem)item).Name));
                SortAndWait(result, grid.Columns[0]);
                SortAndWait(result, grid.Columns[0]);

                // Then: Every info line directly follows its owner's data line.
                Assert.That(engine.TotalLines, Is.EqualTo(5));
                var indices = grid._sortEngine.SortedIndices;
                Assert.That(indices, Is.EqualTo(new[] { 7, 5, 2 }));
                for (int s = 0; s < indices.Length; s++)
                {
                    int line = engine.SortedToLine(s);
                    bool hasPromo = result.Data[indices[s]].Promo != null;
                    Assert.That(engine.IsInfoLine(line + 1) && engine.LineToSorted(line + 1) == s, Is.EqualTo(hasPromo));
                }
            }
            finally { result.Window.Close(); }
        }

        [Test, Category("Integration")]
        public void PromoAppearsAboveViewport_FirstVisibleDataRowDoesNotMove()
        {
            // Given: 50 rows without promo, scrolled so row 20 is on top.
            var result = CreateGrid(50);
            var grid = result.Grid;
            try
            {
                grid._vScrollBar.Value = 20;
                grid._rowEngine.ScrollTo(20);
                Flush(result.Window);
                Assert.That(grid._rowEngine.GetFirstVisibleSortedIndex(), Is.EqualTo(20));

                // When: Rows above the viewport gain promos (row 3 is out of the pool and silent, row 19 notifies).
                result.Data[3].Promo = "a";
                result.Data[19].Promo = "b";
                Flush(result.Window);

                // Then: Row 20 is still the first visible data row and the scroll bar follows the line.
                Assert.That(grid._rowEngine.GetFirstVisibleSortedIndex(), Is.EqualTo(20));
                Assert.That(grid._vScrollBar.Value, Is.EqualTo(grid._rowEngine.GetFirstVisibleIndex()));
            }
            finally { result.Window.Close(); }
        }

        [Test, Category("Integration")]
        public void StyleDpChangedAtRuntime_ReachesInfoRows()
        {
            // Given: Promo on row 1 with default style.
            var result = CreateGrid(10, 1);
            var grid = result.Grid;
            try
            {
                // When: Style DPs are changed at runtime.
                grid.InfoRowForeground = Brushes.Red;
                grid.InfoRowBackground = Brushes.Yellow;
                grid.InfoRowAccentBrush = Brushes.Blue;
                grid.InfoRowFontStyle = FontStyles.Normal;
                grid.InfoRowScrollsHorizontally = true;

                // Then: The visible info row uses the new style.
                var style = VisibleRows(grid).Single(r => r.IsInfoRow).InfoStyle;
                Assert.That(style.Foreground, Is.SameAs(Brushes.Red));
                Assert.That(style.Background, Is.SameAs(Brushes.Yellow));
                Assert.That(style.AccentBrush, Is.SameAs(Brushes.Blue));
                Assert.That(style.FontStyle, Is.EqualTo(FontStyles.Normal));
                Assert.That(style.ScrollsHorizontally, Is.True);
            }
            finally { result.Window.Close(); }
        }

        [Test, Category("Integration")]
        public void ScrollToLastItem_ShowsItsInfoRow()
        {
            // Given: 50 rows, promo on the last row.
            var result = CreateGrid(50, 49);
            var grid = result.Grid;
            try
            {
                // When: Scrolled to the last item.
                grid.ScrollToItem(49);

                // Then: Scroll reaches the very last line (the info row of row 49).
                int lastLine = grid._rowEngine.TotalLines - 1;
                int visible  = grid._rowEngine.GetVisibleRowCount();
                Assert.That(grid._rowEngine.GetFirstVisibleIndex() + visible - 1, Is.EqualTo(lastLine));
                Assert.That(grid.SelectedItem, Is.SameAs(result.Data[49]));
            }
            finally { result.Window.Close(); }
        }

        [Test, Category("Integration")]
        public void PromoAppears_ViaPropertyChanged_InfoRowIsInserted()
        {
            // Given: No promo rows.
            var result = CreateGrid(10);
            var grid = result.Grid;
            try
            {
                Assert.That(grid._rowEngine.TotalLines, Is.EqualTo(10));

                // When: The VM sets a promo on a visible row.
                result.Data[2].Promo = "Promo 1+1: second item for 0.01";
                Flush(result.Window);

                // Then: Line map rebuilt and the info row rendered.
                Assert.That(grid._rowEngine.TotalLines, Is.EqualTo(11));
                Assert.That(VisibleRows(grid).Any(r => r.IsInfoRow && r.InfoOwnerDataIndex == 2), Is.True);
            }
            finally { result.Window.Close(); }
        }

        [Test, Category("Integration")]
        public void PropertyChanged_WithForeignSender_StillRefreshesCell()
        {
            // Given: Grid bound to items that raise PropertyChanged with a sender other than themselves.
            var data = Enumerable.Range(0, 5).Select(i => new ProxySenderItem { Name = $"Item{i}" }).ToList();
            var grid = new OpenDataGrid { RowHeight = 20 };
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Header = "Name", Width = 200 });
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
                // When: A visible item changes its Name.
                data[1].Name = "Zmieniony";
                Flush(window);

                // Then: The cell shows the new value (live refresh is not dropped because of the sender).
                var row = VisibleRows(grid).Single(r => ReferenceEquals(r.DataItem, data[1]));
                Assert.That(row.GetCellValue(0), Is.EqualTo("Zmieniony"));
            }
            finally { window.Close(); }
        }

        [Test, Category("Integration")]
        public void PromoCleared_ViaPropertyChanged_InfoRowIsRemoved()
        {
            // Given: Promo on row 2.
            var result = CreateGrid(10, 2);
            var grid = result.Grid;
            try
            {
                // When: The VM clears the promo.
                result.Data[2].Promo = null;
                Flush(result.Window);

                // Then: No info lines remain.
                Assert.That(grid._rowEngine.TotalLines, Is.EqualTo(10));
                Assert.That(VisibleRows(grid).Any(r => r.IsInfoRow), Is.False);
            }
            finally { result.Window.Close(); }
        }

        [Test, Category("Integration")]
        public void InfoRowBindingPath_SetAfterLoad_EnablesInfoRows()
        {
            // Given: Grid loaded with the feature off.
            var result = CreateGrid(10, 4);
            var grid = result.Grid;
            try
            {
                grid.InfoRowBindingPath = null;
                Assert.That(grid._rowEngine.TotalLines, Is.EqualTo(10));

                // When: The path is set at runtime.
                grid.InfoRowBindingPath = nameof(PromoItem.Promo);

                // Then: Info rows appear.
                Assert.That(grid._rowEngine.TotalLines, Is.EqualTo(11));
            }
            finally { result.Window.Close(); }
        }
    }
}
