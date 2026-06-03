using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Virtualization
{
    /// <summary>
    /// Regression test for: horizontal scroll not populating cell values in newly visible columns.
    ///
    /// Root cause: UpdateColumnLayout only toggled Visibility — cells that were Collapsed during
    /// Bind() never received a value. RefreshVisibleCellValues() must be called after
    /// UpdateColumnLayout to push data into the newly revealed cells.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class HScrollValueRebindTests
    {
        private class RowData
        {
            public string ColA { get; set; }
            public string ColB { get; set; }
            public string ColC { get; set; }
        }

        private static OpenDataGridRow BuildRow(IReadOnlyList<OpenDataGridColumn> columns,
            double[] widths, double[] offsets, int first, int last)
        {
            var row = new OpenDataGridRow();
            row.Initialize(columns, widths, offsets, colorResolver: null, selectionBrush: null);
            return row;
        }

        [Test]
        public void RefreshVisibleCellValues_AfterColumnRangeExpands_NewColumnsReceiveValues()
        {
            // Given: A row with 3 columns where only column 0 is initially visible and bound.
            var columns = new List<OpenDataGridColumn>
            {
                new OpenDataGridLabelColumn { BindingPath = "ColA", Width = 100 },
                new OpenDataGridLabelColumn { BindingPath = "ColB", Width = 100 },
                new OpenDataGridLabelColumn { BindingPath = "ColC", Width = 100 },
            };

            var widths  = new double[] { 100, 100, 100 };
            var offsets = new double[] { 0, 100, 200 };
            var data    = new RowData { ColA = "A", ColB = "B", ColC = "C" };

            Func<object, object>[] accessors =
            {
                item => ((RowData)item).ColA,
                item => ((RowData)item).ColB,
                item => ((RowData)item).ColC,
            };
            Func<object, Brush>[] brushAccessors = { null, null, null };

            var row = BuildRow(columns, widths, offsets, 0, 2);
            row.Bind(0, data, accessors, brushAccessors, null, hOffset: 0, firstVisibleCol: 0, lastVisibleCol: 0, isSelected: false, isChecked: false);

            Assert.That(row.GetCell(0).CurrentValue, Is.EqualTo("A"), "Col 0 bound initially");
            Assert.That(row.GetCell(1).CurrentValue, Is.Null,         "Col 1 not yet bound");
            Assert.That(row.GetCell(2).CurrentValue, Is.Null,         "Col 2 not yet bound");

            // When: Horizontal layout expands visibility to include columns 1 and 2 and values are refreshed.
            row.UpdateColumnLayout(widths, offsets, hOffset: 0, firstVisibleCol: 0, lastVisibleCol: 2);
            row.RefreshVisibleCellValues(accessors, brushAccessors);

            // Then: Newly visible columns receive values.
            Assert.That(row.GetCell(1).CurrentValue, Is.EqualTo("B"), "Col 1 populated after h-scroll");
            Assert.That(row.GetCell(2).CurrentValue, Is.EqualTo("C"), "Col 2 populated after h-scroll");
        }

        [Test]
        public void RefreshVisibleCellValues_NullDataItem_DoesNotThrow()
        {
            // Given: A row initialized but never bound to a data item.
            var columns = new List<OpenDataGridColumn>
            {
                new OpenDataGridLabelColumn { BindingPath = "ColA", Width = 100 },
            };
            var row = BuildRow(columns, new[] { 100.0 }, new[] { 0.0 }, 0, 0);

            // When: RefreshVisibleCellValues is called.
            TestDelegate act = () =>
                row.RefreshVisibleCellValues(
                    new Func<object, object>[] { item => "X" },
                    new Func<object, Brush>[] { null });

            // Then: The call does not throw.
            Assert.DoesNotThrow(act);
        }

        [Test]
        public void RefreshVisibleCellValues_UpdatesOnlyCurrentlyVisibleRange()
        {
            // Given: A bound row where all three columns are initially visible.
            var columns = new List<OpenDataGridColumn>
            {
                new OpenDataGridLabelColumn { BindingPath = "ColA", Width = 100 },
                new OpenDataGridLabelColumn { BindingPath = "ColB", Width = 100 },
                new OpenDataGridLabelColumn { BindingPath = "ColC", Width = 100 },
            };
            var widths  = new double[] { 100, 100, 100 };
            var offsets = new double[] { 0, 100, 200 };
            var data    = new RowData { ColA = "A", ColB = "B", ColC = "C" };

            Func<object, object>[] accessors =
            {
                item => ((RowData)item).ColA,
                item => ((RowData)item).ColB,
                item => ((RowData)item).ColC,
            };
            Func<object, Brush>[] brushAccessors = { null, null, null };

            var row = BuildRow(columns, widths, offsets, 0, 2);
            row.Bind(0, data, accessors, brushAccessors, null, hOffset: 0, firstVisibleCol: 0, lastVisibleCol: 2, isSelected: false, isChecked: false);

            // When: Visibility changes to only column 1 and refresh runs with new accessors.
            row.UpdateColumnLayout(widths, offsets, hOffset: 100, firstVisibleCol: 1, lastVisibleCol: 1);
            Func<object, object>[] newAccessors =
            {
                _ => "NEW_A",
                _ => "NEW_B",
                _ => "NEW_C",
            };
            row.RefreshVisibleCellValues(newAccessors, brushAccessors);

            // Then: Only the currently visible column is refreshed.
            Assert.That(row.GetCell(0).CurrentValue, Is.EqualTo("A"),     "Col 0 out of range — not refreshed");
            Assert.That(row.GetCell(1).CurrentValue, Is.EqualTo("NEW_B"), "Col 1 in range — refreshed");
            Assert.That(row.GetCell(2).CurrentValue, Is.EqualTo("C"),     "Col 2 out of range — not refreshed");
        }

        [Test]
        public void Measure_WithInfiniteAvailableSize_ReturnsFiniteDesiredSize()
        {
            // Given: An initialized row hosted in a Canvas-like scenario where Measure can receive Infinity.
            var columns = new List<OpenDataGridColumn>
            {
                new OpenDataGridLabelColumn { BindingPath = "ColA", Width = 120 },
                new OpenDataGridLabelColumn { BindingPath = "ColB", Width = 80 },
            };
            var widths = new[] { 120.0, 80.0 };
            var offsets = new[] { 0.0, 120.0 };
            var row = BuildRow(columns, widths, offsets, 0, 1);
            row.Height = 28;
            row.UpdateColumnLayout(widths, offsets, hOffset: 0, firstVisibleCol: 0, lastVisibleCol: 1);

            // When: Row is measured with infinite width and height.
            row.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

            // Then: Desired size is finite (WPF layout invariant).
            Assert.That(double.IsInfinity(row.DesiredSize.Width), Is.False, "Width must not be Infinity");
            Assert.That(double.IsInfinity(row.DesiredSize.Height), Is.False, "Height must not be Infinity");
            Assert.That(row.DesiredSize.Width, Is.EqualTo(200).Within(0.001));
            Assert.That(row.DesiredSize.Height, Is.EqualTo(28).Within(0.001));
        }
    }
}
