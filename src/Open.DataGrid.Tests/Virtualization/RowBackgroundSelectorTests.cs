using System.Collections;
using System.Windows.Media;
using Open.DataGrid.Tests.Helpers;
using Open.DataGrid.Virtualization;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Virtualization
{
    /// <summary>
    /// Verifies that <see cref="RowEngineConfig.RowColorResolver"/> is invoked during Bind()
    /// and that <see cref="TestRowSlot.LastRowBrush"/> correctly reflects the returned brush.
    ///
    /// The resolver signature is <c>Func&lt;int dataIndex, object item, Brush&gt;</c> — the engine
    /// is agnostic about internal priority logic (selector vs path vs alternate); all composition
    /// happens in OpenDataGrid.BuildRowColorResolver().
    /// </summary>
    [TestFixture]
    public class RowBackgroundSelectorTests
    {
        private const int ColCount = 3;
        private const double RowHeight = 24.0;
        private const double CanvasHeight = 3 * RowHeight;   // 3 fully visible rows

        private static RowEngineConfig BuildConfig(
            IList dataSource,
            int[] sortedIndices,
            System.Func<int, object, Brush> rowColorResolver)
        {
            var layout  = new double[ColCount];
            var offsets = new double[ColCount];
            for (int i = 0; i < ColCount; i++) { layout[i] = 50; offsets[i] = i * 50; }

            var propertyAccessors  = new System.Func<object, object>[ColCount];
            var cellBrushAccessors = new System.Func<object, Brush>[ColCount];
            for (int i = 0; i < ColCount; i++)
            {
                propertyAccessors[i]  = item => item;
                cellBrushAccessors[i] = _ => null;
            }

            return new RowEngineConfig
            {
                Canvas              = new TestGridCanvas(),
                DataSource          = dataSource,
                SortedIndices       = sortedIndices,
                PropertyAccessors   = propertyAccessors,
                CellBrushAccessors  = cellBrushAccessors,
                RowColorResolver    = rowColorResolver,
                RowReadOnlyAccessor = null,
                RowHeight           = RowHeight,
                CanvasHeight        = CanvasHeight,
                HOffset             = 0,
                FirstVisibleCol     = 0,
                LastVisibleCol      = ColCount - 1,
                ColumnWidths        = layout,
                ColumnOffsets       = offsets,
            };
        }

        private struct EngineResult
        {
            public RowVirtualizationEngine Engine;
            public System.Collections.Generic.List<TestRowSlot> Slots;
        }

        private static EngineResult CreateEngine(
            IList dataSource,
            int[] sortedIndices,
            System.Func<int, object, Brush> rowColorResolver)
        {
            var slots = new System.Collections.Generic.List<TestRowSlot>();

            IRowSlot SlotFactory(int poolIndex)
            {
                var slot = new TestRowSlot();
                slot.Initialize(ColCount);
                slot.PoolIndex = poolIndex;
                slots.Add(slot);
                return slot;
            }

            var cfg = BuildConfig(dataSource, sortedIndices, rowColorResolver);
            cfg.SlotFactory = SlotFactory;

            var engine = new RowVirtualizationEngine();
            engine.Initialize(cfg);
            return new EngineResult { Engine = engine, Slots = slots };
        }

        private static IList BuildIntData(int count)
        {
            var list = new ArrayList();
            for (int i = 0; i < count; i++) list.Add(i);
            return list;
        }

        private static int[] IdentityIndices(int count)
        {
            var idx = new int[count];
            for (int i = 0; i < count; i++) idx[i] = i;
            return idx;
        }

        // ─── 1. Null resolver ───────────────────────────────────────────────────

        [Test]
        public void RowColorResolver_Null_LeavesLastRowBrushNull()
        {
            // Given: engine with no row color resolver.
            const int total = 10;
            var result = CreateEngine(BuildIntData(total), IdentityIndices(total), null);
            var slots  = result.Slots;

            // When: engine initializes and binds slots (happens inside CreateEngine).

            // Then: every slot has LastRowBrush == null.
            foreach (var slot in slots)
                Assert.That(slot.LastRowBrush, Is.Null,
                    $"Slot DataIndex={slot.DataIndex} should have null brush when resolver is null.");
        }

        // ─── 2. Resolver returns brush based on data value ──────────────────────

        [Test]
        public void RowColorResolver_Provided_AppliesBrushBasedOnData()
        {
            // Given: engine where data items with value > 5 get Red brush.
            const int total = 10;
            var data    = BuildIntData(total);
            var indices = IdentityIndices(total);

            // Resolver uses item value (which equals dataIndex in this dataset).
            var slots = CreateEngine(data, indices, (_, item) => (int)item > 5 ? Brushes.Red : null).Slots;

            // When: engine initializes and binds slots.

            // Then: slots with dataIndex > 5 have Red; the rest have null.
            foreach (var slot in slots)
            {
                if (slot.DataIndex < 0) continue;   // out-of-range buffer slot

                if (slot.DataIndex > 5)
                    Assert.That(slot.LastRowBrush, Is.EqualTo(Brushes.Red),
                        $"Slot DataIndex={slot.DataIndex} should have Red brush.");
                else
                    Assert.That(slot.LastRowBrush, Is.Null,
                        $"Slot DataIndex={slot.DataIndex} should have null brush.");
            }
        }

        // ─── 3. Resolver returning null for all rows ────────────────────────────

        [Test]
        public void RowColorResolver_ReturnsNullForAll_AllSlotsBrushNull()
        {
            // Given: resolver always returns null (simulates "no match" — all priority
            //        checks exhausted, fallback also null).
            const int total = 10;
            var slots = CreateEngine(BuildIntData(total), IdentityIndices(total), (_, __) => null).Slots;

            // When: engine initializes and binds slots.

            // Then: all slots have null brush.
            foreach (var slot in slots)
                Assert.That(slot.LastRowBrush, Is.Null,
                    $"Slot DataIndex={slot.DataIndex} should have null brush when resolver always returns null.");
        }

        // ─── 4. Resolver uses dataIndex (e.g. alternate logic) ──────────────────

        [Test]
        public void RowColorResolver_UsesDataIndex_AlternateRowsGetBrush()
        {
            // Given: resolver returns Gray for alternate rows (dataIndex % 2 != 0).
            const int total = 10;
            var data    = BuildIntData(total);
            var indices = IdentityIndices(total);

            var slots = CreateEngine(data, indices, (dataIndex, _) => dataIndex % 2 != 0 ? Brushes.Gray : Brushes.White).Slots;

            // When: engine initializes and binds slots.

            // Then: odd dataIndex slots → Gray, even dataIndex slots → White.
            foreach (var slot in slots)
            {
                if (slot.DataIndex < 0) continue;

                var expected = slot.DataIndex % 2 != 0 ? Brushes.Gray : Brushes.White;
                Assert.That(slot.LastRowBrush, Is.EqualTo(expected),
                    $"Slot DataIndex={slot.DataIndex} should have {(slot.DataIndex % 2 != 0 ? "Gray" : "White")} brush.");
            }
        }

        // ─── 5. Brush survives scroll (ring buffer rebind) ──────────────────────

        [Test]
        public void RowColorResolver_AfterScroll_NewSlotHasCorrectBrush()
        {
            // Given: engine with 10 rows, 3 visible rows, resolver marks dataIndex == 8 as Blue.
            const int total = 10;
            var data    = BuildIntData(total);
            var indices = IdentityIndices(total);

            var engineResult = CreateEngine(data, indices, (dataIndex, _) => dataIndex == 8 ? Brushes.Blue : null);
            var engine = engineResult.Engine;
            var slots  = engineResult.Slots;

            // When: scroll to row 7 so that dataIndex 8 enters the viewport.
            engine.ScrollTo(7);

            // Then: the slot currently bound to dataIndex 8 has Blue brush.
            var slot8 = slots.Find(s => s.DataIndex == 8);
            Assert.That(slot8, Is.Not.Null, "Slot for dataIndex=8 should be in the pool after scroll.");
            Assert.That(slot8.LastRowBrush, Is.EqualTo(Brushes.Blue),
                "Slot for dataIndex=8 should carry Blue brush after scrolling into view.");
        }
    }
}
