using System.Threading;
using Open.DataGrid.Tests.Helpers;
using Open.DataGrid.Virtualization;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Virtualization
{
    /// <summary>
    /// Tests for ColumnVirtualizationEngine — prefix-sum layout and binary-search visibility.
    /// All tests run on STA thread because column objects are DependencyObjects.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class ColumnVirtualizationEngineTests
    {
        private ColumnVirtualizationEngine _engine;

        [SetUp]
        public void SetUp()
        {
            _engine = new ColumnVirtualizationEngine();
        }

        // ── TotalWidth ────────────────────────────────────────────────────────

        [Test]
        public void TotalWidth_ThreeColumns_IsSumOfWidths()
        {
            // Given: Three columns with known widths.
            _engine.Rebuild(TestColumns.WithWidths(100, 200, 150));

            // When: TotalWidth is read.
            var totalWidth = _engine.TotalWidth;

            // Then: TotalWidth equals the sum of all widths.
            Assert.That(totalWidth, Is.EqualTo(450.0));
        }

        [Test]
        public void TotalWidth_SingleColumn_EqualsColumnWidth()
        {
            // Given: A single column.
            _engine.Rebuild(TestColumns.WithWidths(250));

            // When: TotalWidth is read.
            var totalWidth = _engine.TotalWidth;

            // Then: TotalWidth equals that column width.
            Assert.That(totalWidth, Is.EqualTo(250.0));
        }

        // ── GetColumnOffset ───────────────────────────────────────────────────

        [Test]
        public void GetColumnOffset_FirstColumn_IsZero()
        {
            // Given: Rebuilt engine with three columns.
            _engine.Rebuild(TestColumns.WithWidths(100, 200, 150));

            // When: Offset of the first column is requested.
            var offset = _engine.GetColumnOffset(0);

            // Then: Offset equals zero.
            Assert.That(offset, Is.EqualTo(0.0));
        }

        [Test]
        public void GetColumnOffset_SecondColumn_IsFirstWidth()
        {
            // Given: Rebuilt engine with widths 100, 200, 150.
            _engine.Rebuild(TestColumns.WithWidths(100, 200, 150));

            // When: Offset of the second column is requested.
            var offset = _engine.GetColumnOffset(1);

            // Then: Offset equals width of the first column.
            Assert.That(offset, Is.EqualTo(100.0));
        }

        [Test]
        public void GetColumnOffset_ThirdColumn_IsSumOfFirstTwo()
        {
            // Given: Rebuilt engine with widths 100, 200, 150.
            _engine.Rebuild(TestColumns.WithWidths(100, 200, 150));

            // When: Offset of the third column is requested.
            var offset = _engine.GetColumnOffset(2);

            // Then: Offset equals prefix sum of first two widths.
            Assert.That(offset, Is.EqualTo(300.0));
        }

        [Test]
        public void GetColumnOffset_NegativeIndex_ReturnsZero()
        {
            // Given: Rebuilt engine with columns.
            _engine.Rebuild(TestColumns.WithWidths(100, 200));

            // When: Offset is requested for a negative index.
            var offset = _engine.GetColumnOffset(-1);

            // Then: Zero is returned.
            Assert.That(offset, Is.EqualTo(0.0));
        }

        [Test]
        public void GetColumnOffset_IndexBeyondCount_ReturnsZero()
        {
            // Given: Rebuilt engine with two columns.
            _engine.Rebuild(TestColumns.WithWidths(100, 200));

            // When: Offset is requested for an out-of-range index.
            var offset = _engine.GetColumnOffset(5);

            // Then: Zero is returned.
            Assert.That(offset, Is.EqualTo(0.0));
        }

        // ── GetColumnWidths ───────────────────────────────────────────────────

        [Test]
        public void GetColumnWidths_ReturnsCorrectWidthForEachColumn()
        {
            // Given: Three columns with known widths.
            _engine.Rebuild(TestColumns.WithWidths(80, 120, 60));

            // When: Width array is requested.
            var widths = _engine.GetColumnWidths();

            // Then: Returned widths match the configured values.
            Assert.That(widths[0], Is.EqualTo(80.0));
            Assert.That(widths[1], Is.EqualTo(120.0));
            Assert.That(widths[2], Is.EqualTo(60.0));
        }

        // ── GetColumnOffsets ──────────────────────────────────────────────────

        [Test]
        public void GetColumnOffsets_ReturnsCorrectPrefixSums()
        {
            // Given: Three columns with widths 80, 120, 60.
            _engine.Rebuild(TestColumns.WithWidths(80, 120, 60));

            // When: Offset array is requested.
            var offsets = _engine.GetColumnOffsets();

            // Then: Returned offsets match prefix sums.
            Assert.That(offsets[0], Is.EqualTo(0.0));
            Assert.That(offsets[1], Is.EqualTo(80.0));
            Assert.That(offsets[2], Is.EqualTo(200.0));
        }

        // ── GetVisibleColumnRange ─────────────────────────────────────────────

        [Test]
        public void GetVisibleColumnRange_OffsetZeroFullViewport_AllColumnsVisible()
        {
            // Given: Three equal-width columns and viewport covering total width.
            _engine.Rebuild(TestColumns.WithWidths(100, 100, 100)); // 300 total

            // When: Visible column range is calculated from offset zero.
            _engine.GetVisibleColumnRange(0, 300, out int first, out int last);

            // Then: All columns are visible.
            Assert.That(first, Is.EqualTo(0));
            Assert.That(last, Is.EqualTo(2));
        }

        [Test]
        public void GetVisibleColumnRange_ScrolledExactlyPastFirstColumn_FirstVisibleIs1()
        {
            // Given: Three equal-width columns.
            _engine.Rebuild(TestColumns.WithWidths(100, 100, 100));

            // When: Viewport starts exactly at the second column.
            _engine.GetVisibleColumnRange(100, 100, out int first, out int last);

            // Then: First visible column is index 1.
            Assert.That(first, Is.EqualTo(1));
        }

        [Test]
        public void GetVisibleColumnRange_PartialOverlapFirstColumn_FirstVisibleIs0()
        {
            // Given: Three equal-width columns.
            _engine.Rebuild(TestColumns.WithWidths(100, 100, 100));

            // When: Viewport partially overlaps the first column.
            _engine.GetVisibleColumnRange(50, 100, out int first, out int last);

            // Then: First visible column remains index 0.
            Assert.That(first, Is.EqualTo(0));
        }

        [Test]
        public void GetVisibleColumnRange_ViewportExceedsTotal_LastVisibleIsClamped()
        {
            // Given: Three equal-width columns.
            _engine.Rebuild(TestColumns.WithWidths(100, 100, 100));

            // When: Viewport width exceeds total column width.
            _engine.GetVisibleColumnRange(0, 9999, out _, out int last);

            // Then: Last visible index is clamped to the final column.
            Assert.That(last, Is.EqualTo(2));
        }

        [Test]
        public void GetVisibleColumnRange_OffsetBeyondEnd_LastVisibleIsLast()
        {
            // Given: Three equal-width columns.
            _engine.Rebuild(TestColumns.WithWidths(100, 100, 100));

            // When: Viewport starts near the end.
            _engine.GetVisibleColumnRange(250, 200, out _, out int last);

            // Then: Last visible index is the final column.
            Assert.That(last, Is.EqualTo(2));
        }

        [Test]
        public void GetVisibleColumnRange_SinglePixelViewport_ReturnsOneColumn()
        {
            // Given: Three equal-width columns.
            _engine.Rebuild(TestColumns.WithWidths(100, 100, 100));

            // When: Viewport width is one pixel in the middle of column 1.
            _engine.GetVisibleColumnRange(150, 1, out int first, out int last);

            // Then: Exactly one column is visible.
            Assert.That(first, Is.EqualTo(1));
            Assert.That(last, Is.EqualTo(1));
        }

        [Test]
        public void GetVisibleColumnRange_DifferentWidths_CorrectVisibleRange()
        {
            // Given: Columns with non-uniform widths.
            _engine.Rebuild(TestColumns.WithWidths(50, 200, 80, 120)); // offsets: 0, 50, 250, 330

            // When: Viewport [60, 260] is evaluated.
            _engine.GetVisibleColumnRange(60, 200, out int first, out int last);

            // Then: Columns 1 and 2 are visible.
            Assert.That(first, Is.EqualTo(1));
            Assert.That(last, Is.EqualTo(2));
        }
    }
}
