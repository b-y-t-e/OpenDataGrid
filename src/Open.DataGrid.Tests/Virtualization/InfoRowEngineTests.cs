using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Open.DataGrid.Tests.Helpers;
using Open.DataGrid.Virtualization;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Virtualization
{
    /// <summary>
    /// Info rows in <see cref="RowVirtualizationEngine"/>: line mapping (data rows + info rows),
    /// binding of info slots, zebra parity, selection propagation and dynamic layout changes.
    /// </summary>
    [TestFixture]
    public class InfoRowEngineTests
    {
        // ─── helpers ────────────────────────────────────────────────────────────

        private const int ColCount = 2;
        private const double RowHeight = 20.0;

        private sealed class InfoItem
        {
            public int    Value { get; set; }
            public string Info  { get; set; }
        }

        private struct EngineResult
        {
            public RowVirtualizationEngine Engine;
            public List<TestRowSlot> Slots;
            public List<InfoItem> Data;
        }

        private static readonly Func<object, object>[] Accessors =
        {
            item => ((InfoItem)item).Value,
            item => ((InfoItem)item).Info,
        };

        private static readonly Func<object, Brush>[] BrushAccessors = { _ => null, _ => null };

        private static readonly Func<object, string> InfoAccessor = item => ((InfoItem)item).Info;

        private static List<InfoItem> BuildData(int count, params int[] withInfo)
        {
            var list = new List<InfoItem>(count);
            for (int i = 0; i < count; i++)
                list.Add(new InfoItem { Value = i, Info = withInfo.Contains(i) ? $"Promo {i}" : null });
            return list;
        }

        private static EngineResult CreateEngine(List<InfoItem> data, int visibleRows = 5,
                                                 Func<object, string> infoAccessor = null,
                                                 bool useInfo = true)
        {
            var slots = new List<TestRowSlot>();
            IRowSlot SlotFactory(int poolIndex)
            {
                var slot = (TestRowSlot)NewSlot(poolIndex);
                slots.Add(slot);
                return slot;
            }

            var engine = new RowVirtualizationEngine();
            engine.Initialize(CreateConfig(data, Enumerable.Range(0, data.Count).ToArray(), SlotFactory,
                                           useInfo ? (infoAccessor ?? InfoAccessor) : null, visibleRows));
            return new EngineResult { Engine = engine, Slots = slots, Data = data };
        }

        private static RowEngineConfig CreateConfig(List<InfoItem> data, int[] sortedIndices,
                                                    Func<int, IRowSlot> slotFactory,
                                                    Func<object, string> infoAccessor, int visibleRows = 5)
            => new RowEngineConfig
            {
                Canvas             = new TestGridCanvas(),
                SlotFactory        = slotFactory,
                DataSource         = data,
                SortedIndices      = sortedIndices,
                PropertyAccessors  = Accessors,
                CellBrushAccessors = BrushAccessors,
                RowHeight          = RowHeight,
                CanvasHeight       = visibleRows * RowHeight,
                FirstVisibleCol    = 0,
                LastVisibleCol     = ColCount - 1,
                ColumnWidths       = new[] { 50.0, 50.0 },
                ColumnOffsets      = new[] { 0.0, 50.0 },
                InfoTextAccessor   = infoAccessor,
            };

        private static IRowSlot NewSlot(int poolIndex)
        {
            var slot = new TestRowSlot();
            slot.Initialize(ColCount);
            slot.PoolIndex = poolIndex;
            return slot;
        }

        private static TestRowSlot InfoSlotOf(List<TestRowSlot> slots, int ownerDataIndex)
            => slots.FirstOrDefault(s => s.IsVisible && s.IsInfoRow && s.InfoOwnerDataIndex == ownerDataIndex);

        private static TestRowSlot DataSlotOf(List<TestRowSlot> slots, int dataIndex)
            => slots.FirstOrDefault(s => s.IsVisible && !s.IsInfoRow && s.DataIndex == dataIndex);

        // ─── line mapping ───────────────────────────────────────────────────────

        [Test]
        public void NoInfoAccessor_LinesEqualRows()
        {
            // Given: Data with info texts but the feature disabled (no accessor).
            var result = CreateEngine(BuildData(10, 2, 5), useInfo: false);

            // When: Nothing — inspect the mapping after Initialize.
            var engine = result.Engine;

            // Then: Identity mapping, no info slots bound.
            Assert.That(engine.HasInfoRows, Is.False);
            Assert.That(engine.TotalLines, Is.EqualTo(10));
            Assert.That(engine.SortedToLine(7), Is.EqualTo(7));
            Assert.That(result.Slots.Any(s => s.IsInfoRow), Is.False);
        }

        [Test]
        public void AccessorWithoutAnyInfoText_IdentityMapping()
        {
            // Given: Info accessor set, but no item has info text.
            var result = CreateEngine(BuildData(10));

            // When: Nothing — inspect the mapping after Initialize.
            var engine = result.Engine;

            // Then: No line map is allocated.
            Assert.That(engine.HasInfoRows, Is.False);
            Assert.That(engine.TotalLines, Is.EqualTo(10));
        }

        [Test]
        public void InfoTexts_AddOneLineBelowEachOwner()
        {
            // Given: 10 rows, info on rows 2 and 5.
            var result = CreateEngine(BuildData(10, 2, 5));

            // When: Mapping is queried.
            var engine = result.Engine;

            // Then: Lines: 0,1,2,[i2],3,4,5,[i5],6..9 → 12 lines.
            Assert.That(engine.HasInfoRows, Is.True);
            Assert.That(engine.TotalLines, Is.EqualTo(12));
            Assert.That(engine.SortedToLine(2), Is.EqualTo(2));
            Assert.That(engine.SortedToLine(3), Is.EqualTo(4));
            Assert.That(engine.SortedToLine(6), Is.EqualTo(8));
            Assert.That(engine.IsInfoLine(3), Is.True);
            Assert.That(engine.IsInfoLine(7), Is.True);
            Assert.That(engine.IsInfoLine(4), Is.False);
            Assert.That(engine.LineToSorted(3), Is.EqualTo(2), "info line maps to its owner");
            Assert.That(engine.LineToSorted(8), Is.EqualTo(6));
            Assert.That(engine.GetLastLineOfSorted(2), Is.EqualTo(3));
            Assert.That(engine.GetLastLineOfSorted(3), Is.EqualTo(4));
        }

        [Test]
        public void MaxFirstVisibleIndex_CountsInfoLines()
        {
            // Given: 10 rows + 2 info rows, 5 visible lines.
            var result = CreateEngine(BuildData(10, 2, 5), visibleRows: 5);

            // When: Max scroll position is queried.
            int max = result.Engine.GetMaxFirstVisibleIndex();

            // Then: 12 lines − 5 visible = 7.
            Assert.That(max, Is.EqualTo(7));
        }

        // ─── binding ────────────────────────────────────────────────────────────

        [Test]
        public void InfoSlot_IsBoundWithOwnerAndText_DataSlotAboveKnowsIt()
        {
            // Given: Info on row 1.
            var result = CreateEngine(BuildData(10, 1));

            // When: Initial viewport is bound.
            var info  = InfoSlotOf(result.Slots, 1);
            var owner = DataSlotOf(result.Slots, 1);
            var other = DataSlotOf(result.Slots, 2);

            // Then: Info slot carries owner + text and sentinel DataIndex; owner suppresses its separator.
            Assert.That(info, Is.Not.Null);
            Assert.That(info.InfoText, Is.EqualTo("Promo 1"));
            Assert.That(info.DataIndex, Is.EqualTo(RowVirtualizationEngine.InfoRowDataIndex));
            Assert.That(info.DataItem, Is.Null, "info slot must not match item-based lookups");
            Assert.That(owner.HasInfoRowBelow, Is.True);
            Assert.That(other.HasInfoRowBelow, Is.False);
        }

        [Test]
        public void ZebraParity_FollowsDataRows_NotLines()
        {
            // Given: Info on row 0 — every following data row sits one line lower.
            var result = CreateEngine(BuildData(10, 0));

            // When: Rows 1 and 2 are inspected.
            var row1 = DataSlotOf(result.Slots, 1);
            var row2 = DataSlotOf(result.Slots, 2);

            // Then: Parity is based on the data row position (1 = alternate, 2 = not).
            Assert.That(row1.IsAlternate, Is.True);
            Assert.That(row2.IsAlternate, Is.False);
        }

        [Test]
        public void FindRowByDataIndex_NeverReturnsInfoSlot()
        {
            // Given: Info on row 3.
            var result = CreateEngine(BuildData(10, 3));

            // When: The slot for data index 3 is looked up.
            var slot = (TestRowSlot)result.Engine.FindRowByDataIndex(3);

            // Then: The data slot is returned, never the info slot.
            Assert.That(slot, Is.Not.Null);
            Assert.That(slot.IsInfoRow, Is.False);
        }

        [Test]
        public void SetSelection_PropagatesToOwnersInfoSlot()
        {
            // Given: Info on rows 1 and 3.
            var result = CreateEngine(BuildData(10, 1, 3));
            var engine = result.Engine;

            // When: Row 1 is selected, then row 3.
            engine.SetSelection(1);
            bool info1SelectedFirst = InfoSlotOf(result.Slots, 1).IsSelected;
            engine.SetSelection(3);

            // Then: Info slot follows its owner's selection.
            Assert.That(info1SelectedFirst, Is.True);
            Assert.That(InfoSlotOf(result.Slots, 1).IsSelected, Is.False);
            Assert.That(InfoSlotOf(result.Slots, 3).IsSelected, Is.True);
        }

        [Test]
        public void ScrollTo_LineSpace_BindsInfoSlotsAtCorrectPositions()
        {
            // Given: 30 rows, info on row 20.
            var result = CreateEngine(BuildData(30, 20));
            var engine = result.Engine;

            // When: Scrolled so the first visible line is row 20's data line.
            engine.ScrollTo(20);

            // Then: The first visible sorted row is 20 and its info slot is rendered.
            Assert.That(engine.GetFirstVisibleIndex(), Is.EqualTo(20));
            Assert.That(engine.GetFirstVisibleSortedIndex(), Is.EqualTo(20));
            Assert.That(InfoSlotOf(result.Slots, 20), Is.Not.Null);
        }

        [Test]
        public void FirstVisibleSortedIndex_SkipsInfoLineAtTop()
        {
            // Given: 30 rows, info on row 20 (its info row is line 21).
            var result = CreateEngine(BuildData(30, 20));
            var engine = result.Engine;

            // When: Scrolled so the top line is the info line.
            engine.ScrollTo(21);

            // Then: First visible data row is 21 (owner 20 is out of view).
            Assert.That(engine.GetFirstVisibleSortedIndex(), Is.EqualTo(21));
        }

        // ─── dynamic changes ────────────────────────────────────────────────────

        [Test]
        public void InfoTextAppears_RefreshInfoForItem_RaisesInvalidated_RebuildAddsLine()
        {
            // Given: No info rows yet.
            var result = CreateEngine(BuildData(10));
            var engine = result.Engine;
            int raised = 0;
            engine.InfoLayoutInvalidated += (s, e) => raised++;

            // When: Row 2 gains info text and the engine is notified, then rebuilt.
            result.Data[2].Info = "Promo 1+1";
            engine.RefreshInfoForItem(result.Data[2]);
            int raisedBeforeRebuild = raised;
            engine.RebuildInfoLayout();

            // Then: Invalidated once; line map contains the new info row.
            Assert.That(raisedBeforeRebuild, Is.EqualTo(1));
            Assert.That(engine.TotalLines, Is.EqualTo(11));
            Assert.That(InfoSlotOf(result.Slots, 2)?.InfoText, Is.EqualTo("Promo 1+1"));
        }

        [Test]
        public void InfoTextChanged_SameStructure_UpdatesTextWithoutInvalidation()
        {
            // Given: Info on row 2.
            var result = CreateEngine(BuildData(10, 2));
            var engine = result.Engine;
            int raised = 0;
            engine.InfoLayoutInvalidated += (s, e) => raised++;

            // When: The text changes (still non-empty).
            result.Data[2].Info = "Nowa promocja";
            engine.RefreshInfoForItem(result.Data[2]);

            // Then: Text updated in place, no layout rebuild requested.
            Assert.That(raised, Is.EqualTo(0));
            Assert.That(InfoSlotOf(result.Slots, 2).InfoText, Is.EqualTo("Nowa promocja"));
        }

        [Test]
        public void InfoTextDisappears_RaisesInvalidated_RebuildRemovesLine()
        {
            // Given: Info on row 2.
            var result = CreateEngine(BuildData(10, 2));
            var engine = result.Engine;
            int raised = 0;
            engine.InfoLayoutInvalidated += (s, e) => raised++;

            // When: The text is cleared and the engine is notified, then rebuilt.
            result.Data[2].Info = null;
            engine.RefreshInfoForItem(result.Data[2]);
            engine.RebuildInfoLayout();

            // Then: Invalidated once; mapping back to identity.
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(engine.HasInfoRows, Is.False);
            Assert.That(engine.TotalLines, Is.EqualTo(10));
            Assert.That(result.Slots.Any(s => s.IsVisible && s.IsInfoRow), Is.False);
        }

        [Test]
        public void SilentChange_DetectedWhenRowIsScrolledIntoView()
        {
            // Given: 40 rows without info; row 30 gains info text without notification (no INPC).
            var result = CreateEngine(BuildData(40));
            var engine = result.Engine;
            int raised = 0;
            engine.InfoLayoutInvalidated += (s, e) => raised++;
            result.Data[30].Info = "cicha zmiana";

            // When: Row 30 is scrolled into view (rebinds it).
            engine.ScrollTo(28);

            // Then: Mismatch between map and data is reported.
            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public void RebuildInfoLayout_KeepsFirstVisibleRowAnchored()
        {
            // Given: 50 rows, scrolled so row 20 is on top.
            var result = CreateEngine(BuildData(50));
            var engine = result.Engine;
            engine.ScrollTo(20);

            // When: Rows above the viewport gain info texts and the layout is rebuilt.
            result.Data[3].Info = "a";
            result.Data[7].Info = "b";
            int first = engine.RebuildInfoLayout();

            // Then: Row 20 stays the first visible data row (now at line 22).
            Assert.That(first, Is.EqualTo(22));
            Assert.That(engine.GetFirstVisibleIndex(), Is.EqualTo(22));
            Assert.That(engine.GetFirstVisibleSortedIndex(), Is.EqualTo(20));
        }

        // ─── sort / filter re-initialisation ────────────────────────────────────

        [Test]
        public void SortedAndFilteredIndices_InfoLineDirectlyBelowOwner()
        {
            // Given: 10 rows, info on rows 2 and 7.
            var data = BuildData(10, 2, 7);
            var engine = new RowVirtualizationEngine();
            engine.Initialize(CreateConfig(data, Enumerable.Range(0, 10).ToArray(), NewSlot, InfoAccessor));

            // When: Re-initialised with a sorted (descending) and filtered set: 9, 7, 5, 2.
            engine.Initialize(CreateConfig(data, new[] { 9, 7, 5, 2 }, NewSlot, InfoAccessor));

            // Then: Lines: 9, 7, [i7], 5, 2, [i2].
            Assert.That(engine.TotalLines, Is.EqualTo(6));
            Assert.That(engine.SortedToLine(1), Is.EqualTo(1));
            Assert.That(engine.IsInfoLine(2) && engine.LineToSorted(2) == 1, Is.True);
            Assert.That(engine.SortedToLine(3), Is.EqualTo(4));
            Assert.That(engine.IsInfoLine(5) && engine.LineToSorted(5) == 3, Is.True);
            Assert.That(engine.IsInfoLine(3), Is.False);
        }

        [Test]
        public void ReInitialize_WithoutAnyInfoText_DoesNotRescanDataSource()
        {
            // Given: An engine over 1000 rows without info text, counting getter calls.
            var data = BuildData(1000);
            int calls = 0;
            Func<object, string> countingAccessor = item => { calls++; return ((InfoItem)item).Info; };
            var engine = new RowVirtualizationEngine();
            engine.Initialize(CreateConfig(data, Enumerable.Range(0, 1000).ToArray(), NewSlot, countingAccessor));
            calls = 0;

            // When: Re-initialised after a sort (same source, same accessor).
            engine.Initialize(CreateConfig(data, Enumerable.Range(0, 1000).Reverse().ToArray(), NewSlot, countingAccessor));

            // Then: Only the bound pool rows read the getter — no full O(n) scan.
            Assert.That(engine.HasInfoRows, Is.False);
            Assert.That(calls, Is.LessThan(100));
        }

        [Test]
        public void InvalidateInfoCache_PicksUpChangedItemsOnNextInitialize()
        {
            // Given: An engine without info text.
            var data = BuildData(20);
            var engine = new RowVirtualizationEngine();
            engine.Initialize(CreateConfig(data, Enumerable.Range(0, 20).ToArray(), NewSlot, InfoAccessor));

            // When: An item gains info text (e.g. collection replace) and the cache is invalidated.
            data[15].Info = "nowa";
            engine.InvalidateInfoCache();
            engine.Initialize(CreateConfig(data, Enumerable.Range(0, 20).ToArray(), NewSlot, InfoAccessor));

            // Then: The info line is present.
            Assert.That(engine.TotalLines, Is.EqualTo(21));
            Assert.That(engine.IsInfoLine(16), Is.True);
        }
    }
}
