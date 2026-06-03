using System.Collections.Generic;
using System.Threading;
using Open.DataGrid.Controls;
using Open.DataGrid.Virtualization;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Virtualization
{
    /// <summary>
    /// Tests for RowPool ring-buffer semantics.
    /// Verifies that O(1) rotation correctly maps pool offsets to row instances
    /// without allocating new objects during scroll.
    /// STA required because OpenDataGridRow extends Panel (DependencyObject).
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class RowPoolTests
    {
        private struct PoolResult
        {
            public RowPool<OpenDataGridRow> Pool;
            public List<OpenDataGridRow> Rows;
        }

        /// <summary>Creates a pool of size N and returns it together with the ordered row list.</summary>
        private static PoolResult CreatePool(int size)
        {
            var rows = new List<OpenDataGridRow>(size);
            var pool = new RowPool<OpenDataGridRow>();
            pool.Initialize(size, _ =>
            {
                var r = new OpenDataGridRow();
                rows.Add(r);
                return r;
            });
            return new PoolResult { Pool = pool, Rows = rows };
        }

        // ── Initialization ────────────────────────────────────────────────────

        [Test]
        public void Size_AfterInitialize_MatchesRequestedSize()
        {
            // Given: A row pool initialized with a requested size.
            var result = CreatePool(5); var pool = result.Pool;

            // When: Pool size is read.
            var size = pool.Size;

            // Then: Size matches the requested value.
            Assert.That(size, Is.EqualTo(5));
        }

        [Test]
        public void GetRow_InitialState_OffsetMapsToCreationOrder()
        {
            // Given: A freshly initialized pool and creation-ordered rows.
            var result = CreatePool(4); var pool = result.Pool; var rows = result.Rows;

            // When: Rows are read by logical pool offsets.

            // Then: Offsets map to original creation order.
            for (int i = 0; i < 4; i++)
                Assert.That(pool.GetRow(i), Is.SameAs(rows[i]), $"pool offset {i}");
        }

        // ── RotateForward ─────────────────────────────────────────────────────

        [Test]
        public void RotateForward_1_HeadShiftsOnePosition()
        {
            // Given: A pool with three rows.
            var result = CreatePool(3); var pool = result.Pool; var rows = result.Rows;

            // When: The pool rotates forward by one.
            pool.RotateForward(1);

            // Then: Head shifts by one with wrap-around.
            Assert.That(pool.GetRow(0), Is.SameAs(rows[1]));
            Assert.That(pool.GetRow(1), Is.SameAs(rows[2]));
            Assert.That(pool.GetRow(2), Is.SameAs(rows[0])); // wrapped around
        }

        [Test]
        public void RotateForward_FullSize_ReturnsSameMapping()
        {
            // Given: A pool with five rows.
            var result = CreatePool(5); var pool = result.Pool; var rows = result.Rows;

            // When: Rotation equals pool size.
            pool.RotateForward(5); // full wrap = identity

            // Then: Logical mapping stays unchanged.
            for (int i = 0; i < 5; i++)
                Assert.That(pool.GetRow(i), Is.SameAs(rows[i]), $"offset {i}");
        }

        [Test]
        public void RotateForward_MoreThanSize_WrapsBehavesCorrectly()
        {
            // Given: A pool of size four.
            var result = CreatePool(4); var pool = result.Pool; var rows = result.Rows;

            // When: Rotating by a value greater than size.
            pool.RotateForward(6); // 6 mod 4 = 2

            // Then: Mapping matches modulo-based wrap behavior.
            Assert.That(pool.GetRow(0), Is.SameAs(rows[2]));
            Assert.That(pool.GetRow(1), Is.SameAs(rows[3]));
            Assert.That(pool.GetRow(2), Is.SameAs(rows[0]));
            Assert.That(pool.GetRow(3), Is.SameAs(rows[1]));
        }

        // ── RotateBackward ────────────────────────────────────────────────────

        [Test]
        public void RotateBackward_1_HeadShiftsToEnd()
        {
            // Given: A pool with three rows.
            var result = CreatePool(3); var pool = result.Pool; var rows = result.Rows;

            // When: The pool rotates backward by one.
            pool.RotateBackward(1);

            // Then: Last row becomes first.
            Assert.That(pool.GetRow(0), Is.SameAs(rows[2])); // last becomes first
            Assert.That(pool.GetRow(1), Is.SameAs(rows[0]));
            Assert.That(pool.GetRow(2), Is.SameAs(rows[1]));
        }

        [Test]
        public void RotateBackward_FullSize_ReturnsSameMapping()
        {
            // Given: A pool with five rows.
            var result = CreatePool(5); var pool = result.Pool; var rows = result.Rows;

            // When: Backward rotation equals pool size.
            pool.RotateBackward(5);

            // Then: Logical mapping stays unchanged.
            for (int i = 0; i < 5; i++)
                Assert.That(pool.GetRow(i), Is.SameAs(rows[i]), $"offset {i}");
        }

        // ── Round-trip ────────────────────────────────────────────────────────

        [Test]
        public void RotateForwardThenBackward_SameCount_RestoresOriginalOrder()
        {
            // Given: A pool with six rows.
            var result = CreatePool(6); var pool = result.Pool; var rows = result.Rows;

            // When: Forward and backward rotations of equal count are applied.
            pool.RotateForward(3);
            pool.RotateBackward(3);

            // Then: Original order is restored.
            for (int i = 0; i < 6; i++)
                Assert.That(pool.GetRow(i), Is.SameAs(rows[i]), $"offset {i}");
        }

        [Test]
        public void MultipleSmallRotationsEquivalentToOneBigRotation()
        {
            // Given: Two pools sharing identical row instances.
            var rows = new List<OpenDataGridRow>();
            for (int i = 0; i < 6; i++) rows.Add(new OpenDataGridRow());

            var poolA = new RowPool<OpenDataGridRow>();
            poolA.Initialize(6, idx => rows[idx]);

            var poolB = new RowPool<OpenDataGridRow>();
            poolB.Initialize(6, idx => rows[idx]);

            // When: One pool rotates once by total count, the other rotates repeatedly.
            poolA.RotateForward(4); // one big step
            poolB.RotateForward(1);
            poolB.RotateForward(1);
            poolB.RotateForward(1);
            poolB.RotateForward(1); // four small steps

            // Then: Both pools expose identical logical mapping.
            for (int i = 0; i < 6; i++)
                Assert.That(poolA.GetRow(i), Is.SameAs(poolB.GetRow(i)), $"offset {i}");
        }

        // ── ForEach ───────────────────────────────────────────────────────────

        [Test]
        public void ForEach_VisitsAllRowsInLogicalOrder()
        {
            // Given: A pool in initial state.
            var result = CreatePool(4); var pool = result.Pool; var rows = result.Rows;
            var visited = new List<OpenDataGridRow>();

            // When: ForEach enumerates all rows.
            pool.ForEach((row, _) => visited.Add(row));

            // Then: Rows are visited in logical order.
            CollectionAssert.AreEqual(rows, visited);
        }

        [Test]
        public void ForEach_AfterRotation_VisitsInRotatedOrder()
        {
            // Given: A pool rotated forward by one.
            var result = CreatePool(3); var pool = result.Pool; var rows = result.Rows;
            pool.RotateForward(1);
            var visited = new List<OpenDataGridRow>();

            // When: ForEach enumerates rows after rotation.
            pool.ForEach((row, _) => visited.Add(row));

            // Then: Enumeration follows rotated logical order.
            Assert.That(visited[0], Is.SameAs(rows[1]));
            Assert.That(visited[1], Is.SameAs(rows[2]));
            Assert.That(visited[2], Is.SameAs(rows[0]));
        }

        // ── No-allocations invariant ──────────────────────────────────────────

        [Test]
        public void RowIdentity_ThroughManyRotations_NoNewRowsCreated()
        {
            // Given: Initial set of rows from a created pool.
            var result = CreatePool(8); var pool = result.Pool; var rows = result.Rows;
            var originalSet = new HashSet<OpenDataGridRow>(rows);

            // When: Many rotations are performed.
            for (int step = 0; step < 100; step++)
                pool.RotateForward(1);

            var allRows = new HashSet<OpenDataGridRow>();
            pool.ForEach((r, _) => allRows.Add(r));

            // Then: The pool still contains exactly original row instances.
            Assert.That(allRows.SetEquals(originalSet), Is.True,
                "No new rows should be created during rotation.");
        }
    }
}
