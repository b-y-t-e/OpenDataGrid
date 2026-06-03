using System.Threading;
using Open.DataGrid.Controls;
using Open.DataGrid.Models;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Controls
{
    /// <summary>
    /// Tests for the GridLinesVisibility dependency property on OpenDataGrid.
    ///
    /// Note: the actual rendering effect (pen vs no pen per row) requires a visual tree
    /// and is not covered here — these tests guard the DP contract (default value, accepted
    /// values, no coercion errors). Integration-level rendering is considered out of scope
    /// for headless unit tests.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class GridLinesVisibilityTests
    {
        [Test]
        public void GridLinesVisibility_DefaultIsHorizontal()
        {
            // Given: Fresh grid.
            var grid = new OpenDataGrid();

            // Then: The default draws horizontal row separators only.
            Assert.That(grid.GridLinesVisibility, Is.EqualTo(GridLinesVisibility.Horizontal));
        }

        [TestCase(GridLinesVisibility.None,       TestName = "None mode is stored")]
        [TestCase(GridLinesVisibility.Horizontal, TestName = "Horizontal mode is stored")]
        [TestCase(GridLinesVisibility.All,        TestName = "All mode is stored")]
        public void GridLinesVisibility_AllModes_AcceptedByDP(GridLinesVisibility mode)
        {
            // Given: Fresh grid.
            var grid = new OpenDataGrid();

            // When: Any visibility mode is assigned.
            grid.GridLinesVisibility = mode;

            // Then: DP stores the selected mode without coercion errors.
            Assert.That(grid.GridLinesVisibility, Is.EqualTo(mode));
        }
    }
}
