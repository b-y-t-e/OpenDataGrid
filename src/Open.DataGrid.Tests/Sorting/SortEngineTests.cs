using System;
using System.Collections.Generic;
using System.Threading;
using Open.DataGrid.Columns;
using Open.DataGrid.Models;
using Open.DataGrid.Sorting;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Sorting
{
    /// <summary>
    /// Tests for SortEngine — index-indirection sort, direction toggling, FindSortedPosition.
    /// STA required because OpenDataGridColumn (Freezable) is a DependencyObject.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class SortEngineTests
    {
        private SortEngine _engine;
        private List<SortItem> _data;
        private OpenDataGridLabelColumn _nameColumn;
        private OpenDataGridIntegerColumn _intColumn;

        [SetUp]
        public void SetUp()
        {
            // Unsorted data: Charlie=3, Alice=1, Bob=2
            _data = new List<SortItem>
            {
                new SortItem { Name = "Charlie", IntValue = 3 },
                new SortItem { Name = "Alice",   IntValue = 1 },
                new SortItem { Name = "Bob",     IntValue = 2 }
            };
            _engine = new SortEngine();
            _engine.Initialize(_data.Count);

            _nameColumn = new OpenDataGridLabelColumn { BindingPath = "Name" };
            _intColumn  = new OpenDataGridIntegerColumn { BindingPath = "IntValue" };
        }

        /// <summary>Sorts synchronously by spinning until SortCompleted fires.</summary>
        private void SortSync(OpenDataGridColumn column)
        {
            bool done = false;
            EventHandler handler = (_, __) => done = true;
            _engine.SortCompleted += handler;
            _engine.Sort(column, _data);
            SpinWait.SpinUntil(() => done, TimeSpan.FromSeconds(3));
            _engine.SortCompleted -= handler;
            Assert.That(done, Is.True, "SortCompleted must fire within 3 s.");
        }

        // ── Initialize ────────────────────────────────────────────────────────

        [Test]
        public void Initialize_SetsIdentityMapping()
        {
            // Given: A freshly initialized sort engine.
            var idx = _engine.SortedIndices;

            // When: Identity mapping is inspected.

            // Then: Indices map to their original positions.
            Assert.That(idx[0], Is.EqualTo(0));
            Assert.That(idx[1], Is.EqualTo(1));
            Assert.That(idx[2], Is.EqualTo(2));
        }

        [Test]
        public void Initialize_DirectionIsNone()
        {
            // Given: A freshly initialized sort engine.

            // When: CurrentDirection is read.
            var direction = _engine.CurrentDirection;

            // Then: Direction is None.
            Assert.That(direction, Is.EqualTo(SortDirection.None));
        }

        [Test]
        public void Initialize_CurrentColumnIsNull()
        {
            // Given: A freshly initialized sort engine.

            // When: CurrentColumn is read.
            var currentColumn = _engine.CurrentColumn;

            // Then: No sort column is selected.
            Assert.That(currentColumn, Is.Null);
        }

        // ── Sort ascending ────────────────────────────────────────────────────

        [Test]
        public void Sort_ByNameAscending_AliceFirstCharlieLast()
        {
            // Given: Unsorted data and the Name column.

            // When: Sorting is executed on the Name column.
            SortSync(_nameColumn);

            // Then: Sorted indices represent ascending alphabetical order.
            Assert.That(_engine.SortedIndices[0], Is.EqualTo(1), "Alice should be first");
            Assert.That(_engine.SortedIndices[1], Is.EqualTo(2), "Bob should be second");
            Assert.That(_engine.SortedIndices[2], Is.EqualTo(0), "Charlie should be last");
        }

        [Test]
        public void Sort_ByIntAscending_CorrectOrder()
        {
            // Given: Unsorted data and the IntValue column.

            // When: Sorting is executed on the integer column.
            SortSync(_intColumn);

            // Then: Values are ordered ascending.
            Assert.That(_data[_engine.SortedIndices[0]].IntValue, Is.EqualTo(1));
            Assert.That(_data[_engine.SortedIndices[1]].IntValue, Is.EqualTo(2));
            Assert.That(_data[_engine.SortedIndices[2]].IntValue, Is.EqualTo(3));
        }

        [Test]
        public void Sort_FirstSortSetsDirectionAscending()
        {
            // Given: A sort engine without active direction.

            // When: The first sort is performed.
            SortSync(_nameColumn);

            // Then: Direction becomes Ascending.
            Assert.That(_engine.CurrentDirection, Is.EqualTo(SortDirection.Ascending));
        }

        // ── Direction toggle ──────────────────────────────────────────────────

        [Test]
        public void Sort_SameColumnTwice_TogglesDirectionToDescending()
        {
            // Given: A column that has already been sorted once.
            SortSync(_nameColumn);

            // When: Sorting the same column is triggered again.
            SortSync(_nameColumn);

            // Then: Direction toggles to Descending.
            Assert.That(_engine.CurrentDirection, Is.EqualTo(SortDirection.Descending));
        }

        [Test]
        public void Sort_SameColumnDescending_CharlieFirstAliceLast()
        {
            // Given: Name column sorted once ascending.
            SortSync(_nameColumn);

            // When: Sorting the same column again to descending.
            SortSync(_nameColumn);

            // Then: Sorted indices represent descending alphabetical order.
            Assert.That(_engine.SortedIndices[0], Is.EqualTo(0), "Charlie should be first");
            Assert.That(_engine.SortedIndices[2], Is.EqualTo(1), "Alice should be last");
        }

        [Test]
        public void Sort_SameColumnThreeTimes_DirectionIsAscendingAgain()
        {
            // Given: The same column sorted twice (descending state reached).
            SortSync(_nameColumn);
            SortSync(_nameColumn);

            // When: Sorting the same column a third time.
            SortSync(_nameColumn);

            // Then: Direction toggles back to Ascending.
            Assert.That(_engine.CurrentDirection, Is.EqualTo(SortDirection.Ascending));
        }

        [Test]
        public void Sort_DifferentColumn_ResetsDirectionToAscending()
        {
            // Given: One column already sorted.
            SortSync(_nameColumn);

            // When: A different column is sorted.
            SortSync(_intColumn);

            // Then: Direction resets to Ascending and current column changes.
            Assert.That(_engine.CurrentDirection, Is.EqualTo(SortDirection.Ascending));
            Assert.That(_engine.CurrentColumn, Is.SameAs(_intColumn));
        }

        [Test]
        public void Sort_DifferentColumn_PreviousColumnDirectionIsCleared()
        {
            // Given: Name column has active sort state.
            SortSync(_nameColumn);

            // When: Sorting switches to another column.
            SortSync(_intColumn);

            // Then: Previous column direction is cleared.
            Assert.That(_nameColumn.SortDirection, Is.EqualTo(SortDirection.None));
        }

        // ── ClearSort ─────────────────────────────────────────────────────────

        [Test]
        public void ClearSort_AfterSort_RestoresIdentityMapping()
        {
            // Given: A sorted engine state.
            SortSync(_nameColumn);

            // When: ClearSort is invoked.
            _engine.ClearSort();

            // Then: Sorted indices return to identity mapping.
            Assert.That(_engine.SortedIndices[0], Is.EqualTo(0));
            Assert.That(_engine.SortedIndices[1], Is.EqualTo(1));
            Assert.That(_engine.SortedIndices[2], Is.EqualTo(2));
        }

        [Test]
        public void ClearSort_DirectionBecomesNone()
        {
            // Given: A sorted engine state.
            SortSync(_nameColumn);

            // When: Sort is cleared.
            _engine.ClearSort();

            // Then: CurrentDirection becomes None.
            Assert.That(_engine.CurrentDirection, Is.EqualTo(SortDirection.None));
        }

        [Test]
        public void ClearSort_CurrentColumnBecomesNull()
        {
            // Given: A sorted engine state.
            SortSync(_nameColumn);

            // When: Sort is cleared.
            _engine.ClearSort();

            // Then: CurrentColumn becomes null.
            Assert.That(_engine.CurrentColumn, Is.Null);
        }

        // ── FindSortedPosition ────────────────────────────────────────────────

        [Test]
        public void FindSortedPosition_IdentityMapping_ReturnsSameIndex()
        {
            // Given: Identity mapping after initialization.

            // When: Looking up sorted positions for valid data indices.
            var pos0 = _engine.FindSortedPosition(0);
            var pos2 = _engine.FindSortedPosition(2);

            // Then: Returned positions equal original indices.
            Assert.That(pos0, Is.EqualTo(0));
            Assert.That(pos2, Is.EqualTo(2));
        }

        [Test]
        public void FindSortedPosition_AfterSort_MapsDataIndexToVisualPosition()
        {
            // Given: Engine sorted by Name ascending.
            SortSync(_nameColumn); // Alice(1)=0, Bob(2)=1, Charlie(0)=2

            // When: Data indices are translated to visual positions.
            var alicePos = _engine.FindSortedPosition(1);
            var bobPos = _engine.FindSortedPosition(2);
            var charliePos = _engine.FindSortedPosition(0);

            // Then: Positions match sorted order.
            Assert.That(alicePos, Is.EqualTo(0), "Alice at position 0");
            Assert.That(bobPos, Is.EqualTo(1), "Bob at position 1");
            Assert.That(charliePos, Is.EqualTo(2), "Charlie at position 2");
        }

        [Test]
        public void FindSortedPosition_OutOfBoundsIndex_ReturnsMinus1()
        {
            // Given: A valid initialized sort engine.

            // When: Looking up negative and out-of-range data indices.
            var neg = _engine.FindSortedPosition(-1);
            var tooBig = _engine.FindSortedPosition(999);

            // Then: Missing positions are reported as -1.
            Assert.That(neg, Is.EqualTo(-1));
            Assert.That(tooBig, Is.EqualTo(-1));
        }

        // ── SortCompleted event ───────────────────────────────────────────────

        [Test]
        public void Sort_AlwaysFiresSortCompletedEvent()
        {
            // Given: A listener attached to SortCompleted.
            bool fired = false;
            _engine.SortCompleted += (_, __) => fired = true;

            // When: Sorting is triggered.
            _engine.Sort(_nameColumn, _data);
            SpinWait.SpinUntil(() => fired, TimeSpan.FromSeconds(3));

            // Then: SortCompleted is fired.
            Assert.That(fired, Is.True);
        }

        // ── Shared identity indices (memory) ──────────────────────────────────

        [Test]
        public void Initialize_SameRowCount_SharesIdentityArrayBetweenEngines()
        {
            // Given: Two engines initialized with the same row count (two grids over one source).
            var other = new SortEngine();

            // When: Both are in identity mode.
            other.Initialize(_data.Count);

            // Then: They share a single read-only identity array instead of allocating one each.
            Assert.That(other.SortedIndices, Is.SameAs(_engine.SortedIndices));
        }

        [Test]
        public void Sort_DoesNotMutateSharedIdentityArray()
        {
            // Given: A second engine sharing the identity array with the first.
            var other = new SortEngine();
            other.Initialize(_data.Count);
            var shared = other.SortedIndices;

            // When: The first engine sorts.
            SortSync(_nameColumn);

            // Then: The first engine got its own sorted array; the shared identity array is intact.
            Assert.That(_engine.SortedIndices, Is.Not.SameAs(shared));
            Assert.That(_engine.SortedIndices, Is.EqualTo(new[] { 1, 2, 0 }));
            Assert.That(shared, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(other.FindSortedPosition(0), Is.EqualTo(0));
        }

        [Test]
        public void FindSortedPosition_AfterSortThenClearSort_ReturnsIdentityAgain()
        {
            // Given: A sorted engine (Alice=1, Bob=2, Charlie=0).
            SortSync(_nameColumn);
            Assume.That(_engine.FindSortedPosition(0), Is.EqualTo(2));

            // When: Sort is cleared (back to identity mode without a reverse map).
            _engine.ClearSort();

            // Then: Positions are the identity again and out-of-range stays -1.
            Assert.That(_engine.FindSortedPosition(0), Is.EqualTo(0));
            Assert.That(_engine.FindSortedPosition(2), Is.EqualTo(2));
            Assert.That(_engine.FindSortedPosition(3), Is.EqualTo(-1));
        }

        [Test]
        public void FindSortedPosition_FilteredThenIdentity_UsesCurrentMode()
        {
            // Given: A filtered engine (only data index 2 visible).
            _engine.Initialize(new[] { 2 }, _data.Count);
            Assume.That(_engine.FindSortedPosition(0), Is.EqualTo(-1));

            // When: The filter is removed (identity re-initialization).
            _engine.Initialize(_data.Count);

            // Then: The stale filtered reverse map is no longer consulted.
            Assert.That(_engine.FindSortedPosition(0), Is.EqualTo(0));
            Assert.That(_engine.FindSortedPosition(2), Is.EqualTo(2));
        }
    }
}
