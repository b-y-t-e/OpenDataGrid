using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Columns
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class HiddenColumnsTests
    {
        private static OpenDataGrid CreateGrid()
        {
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { Header = "Name", BindingPath = "Name", Width = 120 });
            grid.Columns.Add(new OpenDataGridLabelColumn { Header = "Category", BindingPath = "Category", Width = 120 });
            grid.Columns.Add(new OpenDataGridIntegerColumn { Header = "Qty", BindingPath = "Quantity", Width = 80 });
            return grid;
        }

        private static IReadOnlyList<OpenDataGridColumn> GetColumns(OpenDataGrid grid)
            => grid.Columns.Cast<OpenDataGridColumn>().ToList();

        [Test]
        public void HiddenColumns_DefaultEmptyString()
        {
            // Given: A new grid.
            var grid = new OpenDataGrid();

            // When: HiddenColumns is read.
            var hiddenColumns = grid.HiddenColumns;

            // Then: Default value is empty string.
            Assert.That(hiddenColumns, Is.EqualTo(string.Empty));
        }

        [Test]
        public void HiddenColumns_ByHeader_CommaSeparated_HidesMatchingHeaders()
        {
            // Given: Grid with HiddenColumnsByHeader = true (default).
            var grid = CreateGrid();

            // When: HiddenColumns contains two Header values separated by comma.
            grid.HiddenColumns = "Name, Qty";
            var columns = GetColumns(grid);

            // Then: Matching headers are hidden and others stay visible.
            Assert.That(columns[0].IsVisible, Is.False);
            Assert.That(columns[1].IsVisible, Is.True);
            Assert.That(columns[2].IsVisible, Is.False);
        }

        [Test]
        public void HiddenColumns_ByBindingPath_CommaSeparated_HidesMatchingBindingPaths()
        {
            // Given: Grid with HiddenColumnsByHeader = false.
            var grid = CreateGrid();
            grid.HiddenColumnsByHeader = false;

            // When: HiddenColumns contains two BindingPath values separated by comma.
            grid.HiddenColumns = "Name, Quantity";
            var columns = GetColumns(grid);

            // Then: Matching binding paths are hidden and others stay visible.
            Assert.That(columns[0].IsVisible, Is.False);
            Assert.That(columns[1].IsVisible, Is.True);
            Assert.That(columns[2].IsVisible, Is.False);
        }

        [Test]
        public void HiddenColumns_SemicolonSeparated_HidesMatchingHeaders()
        {
            // Given: Grid with HiddenColumnsByHeader = true (default).
            var grid = CreateGrid();

            // When: HiddenColumns contains one header separated by semicolon.
            grid.HiddenColumns = "Category;";
            var columns = GetColumns(grid);

            // Then: Matching header column is hidden.
            Assert.That(columns[0].IsVisible, Is.True);
            Assert.That(columns[1].IsVisible, Is.False);
            Assert.That(columns[2].IsVisible, Is.True);
        }

        [Test]
        public void HiddenColumns_ByHeader_IsCaseInsensitive_AndTrimmed()
        {
            // Given: Grid with HiddenColumnsByHeader = true (default).
            var grid = CreateGrid();

            // When: HiddenColumns uses different case and extra spaces (by Header).
            grid.HiddenColumns = "  name ; qTy  ";
            var columns = GetColumns(grid);

            // Then: Matches are applied ignoring case and spaces.
            Assert.That(columns[0].IsVisible, Is.False);
            Assert.That(columns[1].IsVisible, Is.True);
            Assert.That(columns[2].IsVisible, Is.False);
        }

        [Test]
        public void HiddenColumns_ByBindingPath_IsCaseInsensitive_AndTrimmed()
        {
            // Given: Grid with HiddenColumnsByHeader = false.
            var grid = CreateGrid();
            grid.HiddenColumnsByHeader = false;

            // When: HiddenColumns uses different case and extra spaces (by BindingPath).
            grid.HiddenColumns = "  name ; quAnTiTy  ";
            var columns = GetColumns(grid);

            // Then: Matches are applied ignoring case and spaces.
            Assert.That(columns[0].IsVisible, Is.False);
            Assert.That(columns[1].IsVisible, Is.True);
            Assert.That(columns[2].IsVisible, Is.False);
        }

        [Test]
        public void HiddenColumns_ByHeader_HidesColumnMatchingHeader()
        {
            // Given: Grid where Header (Qty) differs from BindingPath (Quantity).
            var grid = CreateGrid();

            // When: HiddenColumns contains only the Header token (Qty).
            grid.HiddenColumns = "Qty";
            var columns = GetColumns(grid);

            // Then: Column is hidden because HiddenColumnsByHeader = true (default).
            Assert.That(columns[2].IsVisible, Is.False);
            Assert.That(columns[0].IsVisible, Is.True);
            Assert.That(columns[1].IsVisible, Is.True);
        }

        [Test]
        public void HiddenColumns_ByBindingPath_HeaderOnlyValue_DoesNotHideColumn()
        {
            // Given: Grid with HiddenColumnsByHeader = false; Header (Qty) differs from BindingPath (Quantity).
            var grid = CreateGrid();
            grid.HiddenColumnsByHeader = false;

            // When: HiddenColumns contains only Header token (Qty), not BindingPath (Quantity).
            grid.HiddenColumns = "Qty";
            var columns = GetColumns(grid);

            // Then: No column is hidden — BindingPath "Quantity" doesn't match "Qty".
            Assert.That(columns.All(c => c.IsVisible), Is.True);
        }

        [Test]
        public void HiddenColumns_EmptyString_ShowsAllColumnsAgain()
        {
            // Given: Grid with one hidden column.
            var grid = CreateGrid();
            grid.HiddenColumns = "Category";

            // When: HiddenColumns is cleared.
            grid.HiddenColumns = string.Empty;
            var columns = GetColumns(grid);

            // Then: All columns are visible again.
            Assert.That(columns.All(c => c.IsVisible), Is.True);
        }

        [Test]
        public void HiddenColumns_ByBindingPath_MatchesBindingPath()
        {
            // Given: Grid with HiddenColumnsByHeader = false.
            var grid = CreateGrid();
            grid.HiddenColumnsByHeader = false;

            // When: HiddenColumns contains BindingPath token.
            grid.HiddenColumns = "Quantity";
            var columns = GetColumns(grid);

            // Then: Matching BindingPath column is hidden.
            Assert.That(columns[0].IsVisible, Is.True);
            Assert.That(columns[1].IsVisible, Is.True);
            Assert.That(columns[2].IsVisible, Is.False);
        }

        [Test]
        public void HiddenColumns_ByHeader_MatchesHeader()
        {
            // Given: Grid with HiddenColumnsByHeader = true (default).
            var grid = CreateGrid();

            // When: HiddenColumns contains Header token (Qty, not BindingPath Quantity).
            grid.HiddenColumns = "Qty";
            var columns = GetColumns(grid);

            // Then: Matching Header column is hidden.
            Assert.That(columns[0].IsVisible, Is.True);
            Assert.That(columns[1].IsVisible, Is.True);
            Assert.That(columns[2].IsVisible, Is.False);
        }

        [Test]
        public void HiddenColumns_DoesNotRestoreColumnManuallyHidden()
        {
            // Given: Grid where Category is hidden manually (not through HiddenColumns DP).
            var grid = CreateGrid();
            var columns = GetColumns(grid);
            columns[1].IsVisible = false;   // Category — manual

            // When: HiddenColumns lists only Name (not Category).
            grid.HiddenColumns = "Name";

            // Then: Name is hidden by DP, Category stays hidden (manual), Qty visible.
            Assert.That(columns[0].IsVisible, Is.False);
            Assert.That(columns[1].IsVisible, Is.False);
            Assert.That(columns[2].IsVisible, Is.True);
        }

        [Test]
        public void HiddenColumns_ClearingDp_DoesNotRestoreManuallyHiddenColumn()
        {
            // Given: Grid where Category is hidden manually.
            var grid = CreateGrid();
            var columns = GetColumns(grid);
            columns[1].IsVisible = false;   // manual

            // When: HiddenColumns is set and then cleared.
            grid.HiddenColumns = "Name";
            grid.HiddenColumns = string.Empty;

            // Then: Name is restored (was hidden by DP), Category stays hidden (was never DP-owned).
            Assert.That(columns[0].IsVisible, Is.True);
            Assert.That(columns[1].IsVisible, Is.False);
            Assert.That(columns[2].IsVisible, Is.True);
        }

        [Test]
        public void HiddenColumns_ChangesDynamically_UpdatesVisibility()
        {
            // Given: Grid with no hidden columns (HiddenColumnsByHeader = true, default).
            var grid = CreateGrid();

            // When: HiddenColumns changes from one set to another (matching by Header).
            grid.HiddenColumns = "Name";
            grid.HiddenColumns = "Category;Qty";
            var columns = GetColumns(grid);

            // Then: Visibility reflects the latest HiddenColumns value.
            Assert.That(columns[0].IsVisible, Is.True);
            Assert.That(columns[1].IsVisible, Is.False);
            Assert.That(columns[2].IsVisible, Is.False);
        }
    }
}
