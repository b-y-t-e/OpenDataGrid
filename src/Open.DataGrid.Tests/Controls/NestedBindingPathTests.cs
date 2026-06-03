using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Controls
{
    [TestFixture, Apartment(ApartmentState.STA), Category("Integration")]
    public class NestedBindingPathTests
    {
        private sealed class RowItem
        {
            public ProductInfo Towar { get; set; }
        }

        private sealed class ProductInfo
        {
            public string Symbol { get; set; }
        }

        private struct Rig
        {
            public OpenDataGrid Grid;
            public OpenDataGridTextFilter TextFilter;
            public OpenDataGridComboFilter ComboFilter;
            public Window Window;
            public List<RowItem> Data;
        }

        private static Rig CreateRig()
        {
            var data = new List<RowItem>
            {
                new RowItem { Towar = new ProductInfo { Symbol = "AAA" } },
                new RowItem { Towar = new ProductInfo { Symbol = "BBB" } },
                new RowItem { Towar = new ProductInfo { Symbol = "CCC" } },
            };

            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { Header = "Symbol", BindingPath = "Towar.Symbol", Width = 200 });
            grid.DataSource = data;

            var textFilter = new OpenDataGridTextFilter
            {
                Grid = grid,
                FilterFields = "Towar.Symbol",
                Width = 200,
                Height = 30,
            };

            var comboFilter = new OpenDataGridComboFilter
            {
                Grid = grid,
                FilterField = "Towar.Symbol",
                Width = 200,
                Height = 30,
            };

            var stack = new StackPanel();
            stack.Children.Add(textFilter);
            stack.Children.Add(comboFilter);
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

            return new Rig
            {
                Grid = grid,
                TextFilter = textFilter,
                ComboFilter = comboFilter,
                Window = window,
                Data = data,
            };
        }

        [Test]
        public void Grid_RendersNestedBindingPathValue()
        {
            // Given
            var rig = CreateRig();
            try
            {
                // When
                var canvas = (Canvas)rig.Grid.Template.FindName("PART_Canvas", rig.Grid);
                var row = canvas.Children.OfType<OpenDataGridRow>().FirstOrDefault(r => r.DataIndex == 0);

                // Then
                Assert.That(canvas, Is.Not.Null);
                Assert.That(row, Is.Not.Null);
                Assert.That(row.GetCell(0).CurrentValue, Is.EqualTo("AAA"));
            }
            finally { rig.Window.Close(); }
        }

        [Test]
        public void TextFilter_WithNestedField_FiltersRows()
        {
            // Given
            var rig = CreateRig();
            try
            {
                // When
                rig.TextFilter.Text = "BBB";
                rig.Window.UpdateLayout();

                // Then
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(1));
            }
            finally { rig.Window.Close(); }
        }

        [Test]
        public void ComboFilter_WithNestedField_BuildsDistinctValuesAndFiltersRows()
        {
            // Given
            var rig = CreateRig();
            try
            {
                // When
                var values = rig.ComboFilter.Items.Cast<string>().ToList();
                rig.ComboFilter.SelectedItem = "CCC";
                rig.Window.UpdateLayout();

                // Then
                Assert.That(values, Is.EqualTo(new[] { string.Empty, "AAA", "BBB", "CCC" }));
                Assert.That(rig.Grid.TotalVisibleRows, Is.EqualTo(1));
            }
            finally { rig.Window.Close(); }
        }
    }
}
