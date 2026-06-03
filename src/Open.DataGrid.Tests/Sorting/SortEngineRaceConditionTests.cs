using System;
using System.Collections.Generic;
using System.Threading;
using Open.DataGrid.Columns;
using Open.DataGrid.Sorting;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Sorting
{
    /// <summary>
    /// Regression tests for SortEngine race conditions:
    /// - IndexOutOfRange when DataSource shrinks after Initialize() but before Sort()
    /// - Reverse-lookup bounds guard when maxDataIndex changes mid-flight
    /// - CancelPendingSort exposed as internal for OnGridUnloaded
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class SortEngineRaceConditionTests
    {
        private SortEngine _engine;
        private OpenDataGridLabelColumn _nameColumn;

        [SetUp]
        public void SetUp()
        {
            _engine = new SortEngine();
            _nameColumn = new OpenDataGridLabelColumn { BindingPath = "Name" };
        }

        // ── IndexOutOfRange guard ────────────────────────────────────────────

        [Test]
        public void Sort_DataSourceShrinksAfterInitialize_CompletesAndProducesSubset()
        {
            // Given: Initialize with 10 items
            var data = new List<SortItem>(10);
            for (int i = 0; i < 10; i++)
                data.Add(new SortItem { Name = $"Item{i}" });
            _engine.Initialize(data.Count);

            // When: DataSource shrinks to 5 items — simulating ObservableCollection removes that
            // arrive after Initialize() but before the 100ms coalesce rebuild fires.
            data.RemoveRange(5, 5);

            bool completed = false;
            _engine.SortCompleted += (_, __) => completed = true;

            // Then: Sort() completes without exception (no IndexOutOfRangeException, no hang)
            Assert.DoesNotThrow(() => _engine.Sort(_nameColumn, data));
            bool signalled = SpinWait.SpinUntil(() => completed, TimeSpan.FromSeconds(3));
            Assert.That(signalled, Is.True, "SortCompleted must fire — no overlay hang.");

            // Result contains only the remaining valid items (≤ 5)
            Assert.That(_engine.SortedIndices, Is.Not.Null);
            Assert.That(_engine.SortedIndices.Length, Is.LessThanOrEqualTo(5));
        }

        [Test]
        public void Sort_DataSourceGrowsAfterInitialize_CompletesNormally()
        {
            // Given: Initialize with 5 items
            var data = new List<SortItem>(5);
            for (int i = 0; i < 5; i++)
                data.Add(new SortItem { Name = $"Item{i}" });
            _engine.Initialize(data.Count);

            // When: DataSource grows — indices stay within [0..4], no stale entries
            data.Add(new SortItem { Name = "NewItem" });

            bool completed = false;
            _engine.SortCompleted += (_, __) => completed = true;

            Assert.DoesNotThrow(() => _engine.Sort(_nameColumn, data));
            bool signalled = SpinWait.SpinUntil(() => completed, TimeSpan.FromSeconds(3));
            Assert.That(signalled, Is.True, "SortCompleted must fire.");
        }

        [Test]
        public void Sort_AllItemsRemovedAfterInitialize_ProducesEmptySortedIndices()
        {
            // Given: Initialize with 5 items, then remove all — snapshot will have 0 valid entries
            var data = new List<SortItem>(5);
            for (int i = 0; i < 5; i++)
                data.Add(new SortItem { Name = $"Item{i}" });
            _engine.Initialize(data.Count);
            data.Clear();

            bool completed = false;
            _engine.SortCompleted += (_, __) => completed = true;

            Assert.DoesNotThrow(() => _engine.Sort(_nameColumn, data));
            bool signalled = SpinWait.SpinUntil(() => completed, TimeSpan.FromSeconds(3));
            Assert.That(signalled, Is.True, "SortCompleted must fire even for empty result.");

            // When all items are stale, the sort produces an empty index array
            Assert.That(_engine.SortedIndices, Is.Not.Null);
            Assert.That(_engine.SortedIndices.Length, Is.EqualTo(0));
        }

        [Test]
        public void Sort_AfterDataSourceShrinks_SubsequentSortDoesNotThrow()
        {
            // Given: sort with shrunken DataSource (stale indices filtered → _sortedIndices.Length < _totalRows)
            var data = new List<SortItem>(10);
            for (int i = 0; i < 10; i++)
                data.Add(new SortItem { Name = $"Item{i}" });
            _engine.Initialize(data.Count);
            data.RemoveRange(5, 5);

            bool first = false;
            EventHandler firstHandler = (_, __) => first = true;
            _engine.SortCompleted += firstHandler;
            _engine.Sort(_nameColumn, data);
            SpinWait.SpinUntil(() => first, TimeSpan.FromSeconds(3));
            _engine.SortCompleted -= firstHandler;
            Assert.That(first, Is.True, "First sort must complete.");

            // When: a second Sort() is called — Array.Copy must not throw ArgumentException
            // because _totalRows was not synced with the filtered _sortedIndices.Length
            bool second = false;
            _engine.SortCompleted += (_, __) => second = true;
            Assert.DoesNotThrow(() => _engine.Sort(_nameColumn, data));
            SpinWait.SpinUntil(() => second, TimeSpan.FromSeconds(3));
            Assert.That(second, Is.True, "Second sort must complete without ArgumentException.");
        }

        [Test]
        public void ClearSort_AfterDataSourceShrinks_DoesNotThrow()
        {
            // Given: same shrunken-DataSource scenario
            var data = new List<SortItem>(10);
            for (int i = 0; i < 10; i++)
                data.Add(new SortItem { Name = $"Item{i}" });
            _engine.Initialize(data.Count);
            data.RemoveRange(5, 5);

            bool done = false;
            _engine.SortCompleted += (_, __) => done = true;
            _engine.Sort(_nameColumn, data);
            SpinWait.SpinUntil(() => done, TimeSpan.FromSeconds(3));

            // When: ClearSort calls Initialize(_totalRows) — must use synced length
            Assert.DoesNotThrow(() => _engine.ClearSort());
        }

        // ── CancelPendingSort exposed for OnGridUnloaded ─────────────────────

        [Test]
        public void CancelPendingSort_WhenSortRunning_FiresSortCancelled()
        {
            // Given: a large sort in progress
            var largeData = new List<SortItem>(100_000);
            var rng = new Random(42);
            for (int i = 0; i < 100_000; i++)
                largeData.Add(new SortItem { Name = rng.Next().ToString() });
            _engine.Initialize(largeData.Count);

            bool cancelled = false;
            bool sortFinishedFirst = false;
            _engine.SortCancelled  += (_, __) => cancelled = true;
            _engine.SortCompleted  += (_, __) => sortFinishedFirst = true;

            _engine.Sort(_nameColumn, largeData);

            // When: CancelPendingSort is called (as OnGridUnloaded would do)
            _engine.CancelPendingSort();

            // Then: either cancelled or finished before we could cancel (timing-sensitive)
            if (!sortFinishedFirst)
                Assert.That(cancelled, Is.True, "CancelPendingSort must fire SortCancelled.");
        }

        [Test]
        public void CancelPendingSort_WhenNoSortRunning_IsNoOp()
        {
            // Given: no sort in progress
            bool cancelled = false;
            _engine.SortCancelled += (_, __) => cancelled = true;

            // When / Then: calling CancelPendingSort must not throw and must not fire event
            Assert.DoesNotThrow(() => _engine.CancelPendingSort());
            Assert.That(cancelled, Is.False);
        }

        // ── FindSortedPosition robustness ────────────────────────────────────

        [Test]
        public void FindSortedPosition_WithDataIndexBeyondLookupLength_ReturnsMinusOne()
        {
            // Given: Initialize with 5 items
            _engine.Initialize(5);

            // When: asking for a position beyond the array bounds
            // Then: returns -1 (no exception)
            int result = _engine.FindSortedPosition(100);
            Assert.That(result, Is.EqualTo(-1));
        }

        [Test]
        public void FindSortedPosition_WithNegativeDataIndex_ReturnsMinusOne()
        {
            // Given
            _engine.Initialize(5);

            // When / Then
            Assert.That(_engine.FindSortedPosition(-1), Is.EqualTo(-1));
        }
    }
}
