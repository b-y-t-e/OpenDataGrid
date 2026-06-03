using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Media;
using Open.DataGrid.Tests.Helpers;
using Open.DataGrid.Virtualization;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Virtualization
{
    /// <summary>
    /// Tests the virtual "viewport window" of RowVirtualizationEngine.
    ///
    /// Uses pure-C# test doubles (TestRowSlot, TestGridCanvas) so no WPF visual tree
    /// or STA thread is required.
    ///
    /// Invariants verified:
    ///   1. After scroll: rows inside the visible+buffer range are loaded; rows outside are not.
    ///   2. After UpdateColumnRange: cells in the new column range have values; cells outside don't.
    ///   3. Combined scroll + column-range change: all visible cells are correctly loaded.
    /// </summary>
    [TestFixture]
    public class VirtualViewportTests
    {
        // ─── helpers ────────────────────────────────────────────────────────────

        private const int ColCount = 20;
        private const double RowHeight = 24.0;
        private const double DefaultCanvasHeight = 5 * RowHeight;   // 5 fully visible rows
        private const int TotalRows = 100;

        private static readonly Func<object, object>[] Accessors;
        private static readonly Func<object, Brush>[]  BrushAccessors;

        static VirtualViewportTests()
        {
            Accessors = new Func<object, object>[ColCount];
            BrushAccessors = new Func<object, Brush>[ColCount];
            for (int c = 0; c < ColCount; c++)
            {
                int captured = c;
                Accessors[c] = item => ((int[])item)[captured];
                BrushAccessors[c] = _ => null;
            }
        }

        /// <summary>Builds a data source of <paramref name="count"/> int[] rows.</summary>
        private static IList BuildData(int count)
        {
            var list = new ArrayList();
            for (int r = 0; r < count; r++)
            {
                var row = new int[ColCount];
                for (int c = 0; c < ColCount; c++)
                    row[c] = r * 100 + c;
                list.Add(row);
            }
            return list;
        }

        private static int[] IdentityIndices(int count)
        {
            var idx = new int[count];
            for (int i = 0; i < count; i++) idx[i] = i;
            return idx;
        }

        private struct ColumnLayout
        {
            public double[] Widths;
            public double[] Offsets;
        }

        private struct EngineResult
        {
            public RowVirtualizationEngine Engine;
            public TestGridCanvas Canvas;
            public List<TestRowSlot> Slots;
        }

        private static ColumnLayout UniformColumns(int count, double colWidth = 50.0)
        {
            var widths  = new double[count];
            var offsets = new double[count];
            for (int i = 0; i < count; i++)
            {
                widths[i]  = colWidth;
                offsets[i] = i * colWidth;
            }
            return new ColumnLayout { Widths = widths, Offsets = offsets };
        }

        private EngineResult CreateEngine(int firstCol, int lastCol, double canvasHeight = DefaultCanvasHeight)
        {
            var layout = UniformColumns(ColCount);
            var canvas = new TestGridCanvas();
            var slots  = new List<TestRowSlot>();

            IRowSlot SlotFactory(int poolIndex)
            {
                var slot = new TestRowSlot();
                slot.Initialize(ColCount);
                slot.PoolIndex = poolIndex;
                slots.Add(slot);
                return slot;
            }

            var engine = new RowVirtualizationEngine();
            engine.Initialize(new RowEngineConfig
            {
                Canvas              = canvas,
                SlotFactory         = SlotFactory,
                DataSource          = BuildData(TotalRows),
                SortedIndices       = IdentityIndices(TotalRows),
                PropertyAccessors   = Accessors,
                CellBrushAccessors  = BrushAccessors,
                RowColorResolver    = null,
                RowReadOnlyAccessor = null,
                RowHeight           = RowHeight,
                CanvasHeight        = canvasHeight,
                HOffset             = 0,
                FirstVisibleCol     = firstCol,
                LastVisibleCol      = lastCol,
                ColumnWidths        = layout.Widths,
                ColumnOffsets       = layout.Offsets,
            });

            return new EngineResult { Engine = engine, Canvas = canvas, Slots = slots };
        }

        // ─── 1. Vertical viewport ───────────────────────────────────────────────

        [Test]
        public void AfterInitialize_VisibleRowsAreLoaded()
        {
            // Given: Engine initialized with 5 fully visible rows and full column range.
            // 5 full rows + 4 buffer above + 4 buffer below = pool of 13
            var result = CreateEngine(0, ColCount - 1);
            var engine = result.Engine; var slots = result.Slots;

            // When: Visible rows in the initial viewport are inspected.

            // Then: Rows 0-4 are loaded, visible, and contain correct values.
            for (int r = 0; r < 5; r++)
            {
                var slot = FindSlotByDataIndex(slots, r);
                Assert.That(slot, Is.Not.Null, $"Row {r} not found in pool");
                Assert.That(slot.IsVisible, Is.True, $"Row {r} should be visible");
                Assert.That(slot.GetCellValue(0), Is.EqualTo(r * 100 + 0), $"Row {r} col 0 value");
            }
        }

        [Test]
        public void AfterScrollTo_NewRowsLoaded_OldRowsNotVisible()
        {
            // Given: Initialized engine at top of dataset.
            var result = CreateEngine(0, ColCount - 1);
            var engine = result.Engine; var slots = result.Slots;

            // When: Engine scrolls to row 50.
            engine.ScrollTo(50);

            // Then: Rows 50-54 are visible and row 0 is out of pool visibility.
            for (int r = 50; r <= 54; r++)
            {
                var slot = FindSlotByDataIndex(slots, r);
                Assert.That(slot, Is.Not.Null, $"Row {r} not found in pool after scroll");
                Assert.That(slot.IsVisible, Is.True, $"Row {r} should be visible after scroll");
                Assert.That(slot.GetCellValue(0), Is.EqualTo(r * 100 + 0), $"Row {r} col 0 value");
            }

            var row0Slot = FindSlotByDataIndex(slots, 0);
            Assert.That(row0Slot, Is.Null, "Row 0 should not be in the pool at scroll position 50");
        }

        [Test]
        public void AfterScrollToNearEnd_RowsClampedCorrectly()
        {
            // Given: Initialized engine with finite row count.
            var result = CreateEngine(0, ColCount - 1);
            var engine = result.Engine; var slots = result.Slots;

            // When: Scroll target goes beyond the end.
            engine.ScrollTo(TotalRows); // beyond end — should clamp

            // Then: The last 5 rows are visible.
            for (int r = TotalRows - 5; r < TotalRows; r++)
            {
                var slot = FindSlotByDataIndex(slots, r);
                Assert.That(slot, Is.Not.Null, $"Row {r} not found near end");
                Assert.That(slot.IsVisible, Is.True, $"Row {r} should be visible near end");
            }
        }

        [Test]
        public void PartialBottomRow_ClampStillUsesFullRows()
        {
            // Given: 5 full rows and 1 partial row at the bottom.
            var result = CreateEngine(0, ColCount - 1, canvasHeight: 5 * RowHeight + 1);
            var engine = result.Engine;

            // When: Scrolling beyond end.
            engine.ScrollTo(TotalRows);

            // Then: Clamp is based on 5 full rows.
            Assert.That(engine.GetFirstVisibleIndex(), Is.EqualTo(TotalRows - 5));
        }

        [Test]
        public void TinyViewport_NoFullRows_ClampAllowsLastRowStart()
        {
            // Given: Canvas too small to fit any full row.
            var result = CreateEngine(0, ColCount - 1, canvasHeight: RowHeight - 1);
            var engine = result.Engine;

            // When: Scrolling beyond end.
            engine.ScrollTo(TotalRows);

            // Then: First visible index can reach the last data row.
            Assert.That(engine.GetVisibleRowCount(), Is.EqualTo(0));
            Assert.That(engine.GetFirstVisibleIndex(), Is.EqualTo(TotalRows - 1));
        }

        [Test]
        public void Resize_FromTinyToFullRow_UpdatesVisibleRowCount()
        {
            // Given: Engine initialized with tiny viewport.
            var result = CreateEngine(0, ColCount - 1, canvasHeight: RowHeight - 1);
            var engine = result.Engine;
            Assert.That(engine.GetVisibleRowCount(), Is.EqualTo(0));

            // When: Viewport grows to fit exactly one row.
            engine.Resize(RowHeight);

            // Then: One full row is reported as visible.
            Assert.That(engine.GetVisibleRowCount(), Is.EqualTo(1));
        }

        // ─── 2. Horizontal viewport ─────────────────────────────────────────────

        [Test]
        public void AfterInitialize_ColumnsInRangeAreLoaded()
        {
            // Given: Engine initialized with visible column range [2..7].
            int firstCol = 2, lastCol = 7;
            var result = CreateEngine(firstCol, lastCol);
            var engine = result.Engine; var slots = result.Slots;

            // When: Row 0 loaded-cell flags are inspected.
            var slot = FindSlotByDataIndex(slots, 0);
            Assert.That(slot, Is.Not.Null);

            // Then: In-range columns are loaded and out-of-range are not.
            for (int c = firstCol; c <= lastCol; c++)
                Assert.That(slot.IsCellLoaded(c), Is.True, $"Column {c} should be loaded");

            Assert.That(slot.IsCellLoaded(0),  Is.False, "Column 0 outside range — must not be loaded");
            Assert.That(slot.IsCellLoaded(15), Is.False, "Column 15 outside range — must not be loaded");
        }

        [Test]
        public void UpdateColumnRange_NewColumnsGetValues_OldColumnsAreUnloaded()
        {
            // Given: Initial visible column range [0..4].
            var result = CreateEngine(0, 4);
            var engine = result.Engine; var slots = result.Slots;
            var layout = UniformColumns(ColCount);

            // When: Visible range is updated to [10..15].
            engine.UpdateColumnRange(10, 15, hOffset: 0, layout.Widths, layout.Offsets);

            // Then: New range is loaded and old range is unloaded.
            var slot = FindSlotByDataIndex(slots, 0);
            Assert.That(slot, Is.Not.Null);

            for (int c = 10; c <= 15; c++)
                Assert.That(slot.IsCellLoaded(c), Is.True, $"Column {c} should be loaded after range update");

            for (int c = 0; c <= 4; c++)
                Assert.That(slot.IsCellLoaded(c), Is.False, $"Column {c} should not be loaded after range update");
        }

        [Test]
        public void UpdateColumnRange_CellValuesAreCorrect()
        {
            // Given: Initial visible range [0..4].
            var result = CreateEngine(0, 4);
            var engine = result.Engine; var slots = result.Slots;
            var layout = UniformColumns(ColCount);

            // When: Visible range changes to [5..9].
            engine.UpdateColumnRange(5, 9, hOffset: 0, layout.Widths, layout.Offsets);

            // Then: Row 0 contains expected values for loaded columns.
            var slot = FindSlotByDataIndex(slots, 0);
            Assert.That(slot, Is.Not.Null);

            for (int c = 5; c <= 9; c++)
                Assert.That(slot.GetCellValue(c), Is.EqualTo(0 * 100 + c),
                    $"Row 0, col {c}: expected {c}, got {slot.GetCellValue(c)}");
        }

        // ─── 3. Combined scroll + column range change ───────────────────────────

        [Test]
        public void ScrollThenUpdateColumnRange_AllVisibleCellsCorrect()
        {
            // Given: Engine with initial range [0..4].
            var result = CreateEngine(0, 4);
            var engine = result.Engine; var slots = result.Slots;
            var layout = UniformColumns(ColCount);

            // When: View scrolls to row 20 and column range changes to [8..12].
            engine.ScrollTo(20);
            engine.UpdateColumnRange(8, 12, hOffset: 0, layout.Widths, layout.Offsets);

            // Then: Visible rows 20-24 have correct values in visible columns.
            for (int r = 20; r <= 24; r++)
            {
                var slot = FindSlotByDataIndex(slots, r);
                Assert.That(slot, Is.Not.Null, $"Row {r} not found");
                Assert.That(slot.IsVisible, Is.True);

                for (int c = 8; c <= 12; c++)
                    Assert.That(slot.GetCellValue(c), Is.EqualTo(r * 100 + c),
                        $"Row {r} col {c}: expected {r * 100 + c}");
            }
        }

        // ─── 4. Resize — checked-row style propagation ──────────────────────────

        [Test]
        public void Resize_GrowingPool_PropagatesCheckedRowStyleToNewSlots()
        {
            // Given — engine initialized with tiny viewport (0 full rows) plus checked-row style
            var layout = UniformColumns(ColCount);
            var canvas = new TestGridCanvas();
            var allSlots = new List<TestRowSlot>();

            IRowSlot SlotFactory(int poolIndex)
            {
                var slot = new TestRowSlot();
                slot.Initialize(ColCount);
                slot.PoolIndex = poolIndex;
                allSlots.Add(slot);
                return slot;
            }

            var fgBrush = System.Windows.Media.Brushes.Red;

            var engine = new RowVirtualizationEngine();
            engine.Initialize(new RowEngineConfig
            {
                Canvas              = canvas,
                SlotFactory         = SlotFactory,
                DataSource          = BuildData(TotalRows),
                SortedIndices       = IdentityIndices(TotalRows),
                PropertyAccessors   = Accessors,
                CellBrushAccessors  = BrushAccessors,
                RowColorResolver    = null,
                RowReadOnlyAccessor = null,
                RowHeight           = RowHeight,
                CanvasHeight        = RowHeight - 1, // tiny — 0 full rows
                HOffset             = 0,
                FirstVisibleCol     = 0,
                LastVisibleCol      = ColCount - 1,
                ColumnWidths        = layout.Widths,
                ColumnOffsets       = layout.Offsets,
                CheckedRowForeground = fgBrush,
                CheckedRowBackground = null,
                CheckedRowFontWeight = null,
            });

            int initialSlotCount = allSlots.Count;

            // When — grow viewport so new slots are created
            engine.Resize(5 * RowHeight);

            // Then — new slots were created and all of them received UpdateCheckedRowStyle
            Assert.That(allSlots.Count, Is.GreaterThan(initialSlotCount), "Resize should create additional slots");
            foreach (var slot in allSlots)
            {
                Assert.That(slot.CheckedRowStyleCallCount, Is.GreaterThanOrEqualTo(1),
                    $"Slot {slot.PoolIndex} did not receive UpdateCheckedRowStyle after Resize");
                Assert.That(slot.LastCheckedRowForeground, Is.SameAs(fgBrush));
            }
        }

        // ─── helpers ────────────────────────────────────────────────────────────

        private static TestRowSlot FindSlotByDataIndex(List<TestRowSlot> slots, int dataIndex)
        {
            foreach (var s in slots)
                if (s.DataIndex == dataIndex && s.IsVisible) return s;
            return null;
        }
    }
}
