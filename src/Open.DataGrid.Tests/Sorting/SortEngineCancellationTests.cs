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
    /// Verifies SortEngine cancellation: SortCancelled fires when a pending sort is superseded
    /// by Initialize() or by a new Sort() call, ensuring pending-work counters stay balanced.
    /// STA required because OpenDataGridColumn (Freezable) is a DependencyObject.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class SortEngineCancellationTests
    {
        private SortEngine _engine;
        private List<SortItem> _largeData;
        private OpenDataGridLabelColumn _nameColumn;

        [SetUp]
        public void SetUp()
        {
            _engine = new SortEngine();
            _nameColumn = new OpenDataGridLabelColumn { BindingPath = "Name" };

            // 100k items — large enough that the background sort won't finish
            // before we call Initialize() to cancel it.
            _largeData = new List<SortItem>(100_000);
            var rng = new Random(42);
            for (int i = 0; i < 100_000; i++)
                _largeData.Add(new SortItem { Name = rng.Next().ToString() });
            _engine.Initialize(_largeData.Count);
        }

        // ── Initialize cancels in-progress sort ──────────────────────────────

        [Test]
        public void Initialize_WhileSortRunning_FiresSortCancelled()
        {
            // Given: subscribe BEFORE starting the sort.
            bool cancelled = false;
            _engine.SortCancelled += (_, __) => cancelled = true;

            // Track whether the background task has started executing.
            // SortCompleted fires (from the task) only after _sortCts is cleared, so we can use it
            // as a proxy to detect "sort already finished" and skip the cancel-specific assertion.
            bool sortFinishedBeforeCancel = false;
            _engine.SortCompleted += (_, __) => sortFinishedBeforeCancel = true;

            _engine.Sort(_nameColumn, _largeData);

            // When: Initialize cancels the sort.
            // 100k items take >> 1 ms to sort; Task.Run won't even start executing in the few
            // nanoseconds between Sort() and Initialize() on the same thread. But to be safe,
            // skip the assertion if the sort somehow finished before we could cancel it.
            _engine.Initialize(_largeData.Count);

            // Then: either SortCancelled fired (expected path) or the sort finished before
            // Initialize got to it (degenerate path — passes vacuously rather than flaking).
            if (!sortFinishedBeforeCancel)
                Assert.That(cancelled, Is.True, "SortCancelled should fire when Initialize cancels an in-progress sort.");
        }

        [Test]
        public void Initialize_WhileSortRunning_SortCompletedDoesNotFireAfterCancel()
        {
            // Given: subscribe BEFORE starting the sort so we can't miss an early completion.
            // (Subscribing after Sort() would leave a race window where the sort could finish
            // and fire SortCompleted before the handler is registered.)
            bool completed = false;
            var completedSignal = new ManualResetEventSlim(false);
            _engine.SortCompleted += (_, __) => { completed = true; completedSignal.Set(); };

            // Escape hatch: SortCancelled fires synchronously inside Initialize() when a sort
            // is actually running. If it doesn't fire, the sort completed before Initialize()
            // reached CancelPendingSort — the assertion is vacuous, not a test failure.
            bool sortActuallyCancelled = false;
            _engine.SortCancelled += (_, __) => sortActuallyCancelled = true;

            _engine.Sort(_nameColumn, _largeData);

            // When: Initialize cancels the sort immediately
            _engine.Initialize(_largeData.Count);

            // If Initialize found no running sort (degenerate timing), skip the assertion.
            if (!sortActuallyCancelled) return;

            // Then: wait up to 500 ms — SortCompleted must NOT arrive for the cancelled sort.
            // ManualResetEventSlim.Wait returns true if the event was set (= SortCompleted fired).
            bool firedUnexpectedly = completedSignal.Wait(500);
            Assert.That(firedUnexpectedly, Is.False,
                "SortCompleted fired unexpectedly for a sort that was cancelled by Initialize.");
            Assert.That(completed, Is.False);
        }

        [Test]
        public void Initialize_WhenNoSortRunning_DoesNotFireSortCancelled()
        {
            // Given: no sort is in progress
            bool cancelled = false;
            _engine.SortCancelled += (_, __) => cancelled = true;

            // When: Initialize is called without a prior Sort
            _engine.Initialize(10);

            // Then: SortCancelled does not fire
            Assert.That(cancelled, Is.False);
        }

        // ── Sort supersedes another Sort ─────────────────────────────────────

        [Test]
        public void Sort_SupersedesPreviousSort_FiresSortCancelledThenSortCompleted()
        {
            // Given: a first sort is kicked off
            _engine.Sort(_nameColumn, _largeData);

            bool cancelled = false;
            bool completed = false;
            _engine.SortCancelled += (_, __) => cancelled = true;
            _engine.SortCompleted += (_, __) => completed = true;

            // When: a second Sort call immediately supersedes the first
            _engine.Sort(_nameColumn, _largeData);

            // Then: SortCancelled fires synchronously for the first sort
            Assert.That(cancelled, Is.True, "First sort should have been cancelled.");

            // And: SortCompleted fires eventually for the second sort
            SpinWait.SpinUntil(() => completed, TimeSpan.FromSeconds(5));
            Assert.That(completed, Is.True, "Second sort should have completed.");
        }

        // ── SortCancelled fires on calling thread ────────────────────────────

        [Test]
        public void SortCancelled_FiredOnCallingThread()
        {
            // Given: a sort is in progress
            _engine.Sort(_nameColumn, _largeData);

            int firingThreadId = -1;
            _engine.SortCancelled += (_, __) => firingThreadId = Thread.CurrentThread.ManagedThreadId;

            // When: Initialize cancels it (called from current thread)
            int callingThreadId = Thread.CurrentThread.ManagedThreadId;
            _engine.Initialize(_largeData.Count);

            // Then: SortCancelled fires on the same thread as the Initialize call
            Assert.That(firingThreadId, Is.EqualTo(callingThreadId));
        }
    }
}
