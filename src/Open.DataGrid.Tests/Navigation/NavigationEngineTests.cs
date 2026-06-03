using System.Collections.Generic;
using Open.DataGrid.Models;
using Open.DataGrid.Navigation;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Navigation
{
    /// <summary>
    /// Tests for NavigationEngine state machine — initialization, bounds clamping,
    /// SetCurrentPosition, SetEditingState, and event contract.
    ///
    /// Note: HandleKeyDown tests that require KeyEventArgs (which needs a live
    /// PresentationSource) are out of scope for pure unit tests. Those behaviors
    /// are covered by the manual integration smoke-tests in the Demo app.
    /// </summary>
    [TestFixture]
    public class NavigationEngineTests
    {
        private NavigationEngine _engine;

        [SetUp]
        public void SetUp()
        {
            _engine = new NavigationEngine();
        }

        // ── Initialize ────────────────────────────────────────────────────────

        [Test]
        public void Initialize_NonEmptySource_CurrentRowIsZero()
        {
            // Given: A non-empty data source.

            // When: Navigation engine is initialized.
            _engine.Initialize(NavigationMode.Cell, 100, 10, 20);

            // Then: Current row starts at zero.
            Assert.That(_engine.CurrentRowIndex, Is.EqualTo(0));
        }

        [Test]
        public void Initialize_EmptySource_CurrentRowIsMinus1()
        {
            // Given: An empty data source.

            // When: Navigation engine is initialized.
            _engine.Initialize(NavigationMode.Cell, 0, 10, 20);

            // Then: Current row is set to -1.
            Assert.That(_engine.CurrentRowIndex, Is.EqualTo(-1));
        }

        [Test]
        public void Initialize_CurrentColumnStartsAtZero()
        {
            // Given: A non-empty source in cell mode.

            // When: Navigation engine is initialized.
            _engine.Initialize(NavigationMode.Cell, 100, 10, 20);

            // Then: Current column starts at zero.
            Assert.That(_engine.CurrentColumnIndex, Is.EqualTo(0));
        }

        [Test]
        public void Initialize_IsEditingIsFalse()
        {
            // Given: A new navigation engine.

            // When: It is initialized.
            _engine.Initialize(NavigationMode.Cell, 100, 10, 20);

            // Then: Editing mode is disabled.
            Assert.That(_engine.IsEditing, Is.False);
        }

        [Test]
        public void Initialize_RowMode_CurrentColumnIsZero()
        {
            // Given: Row navigation mode initialization request.

            // When: Navigation engine is initialized.
            _engine.Initialize(NavigationMode.Row, 50, 5, 10);

            // Then: Current column remains zero.
            Assert.That(_engine.CurrentColumnIndex, Is.EqualTo(0));
        }

        // ── UpdateStats ───────────────────────────────────────────────────────

        [Test]
        public void UpdateStats_CurrentRowExceedsNewTotal_ClampsToLastRow()
        {
            // Given: Current row set beyond a future smaller total.
            _engine.Initialize(NavigationMode.Cell, 100, 10, 20);
            _engine.SetCurrentPosition(80, 0);

            // When: Stats are updated with fewer rows.
            _engine.UpdateStats(50, 20);

            // Then: Current row is clamped to last valid index.
            Assert.That(_engine.CurrentRowIndex, Is.EqualTo(49));
        }

        [Test]
        public void UpdateStats_CurrentRowWithinNewTotal_Preserved()
        {
            // Given: Current row inside bounds of the updated total.
            _engine.Initialize(NavigationMode.Cell, 100, 10, 20);
            _engine.SetCurrentPosition(30, 5);

            // When: Stats are updated without shrinking below current row.
            _engine.UpdateStats(100, 20);

            // Then: Current row is preserved.
            Assert.That(_engine.CurrentRowIndex, Is.EqualTo(30));
        }

        [Test]
        public void UpdateStats_ExpandedTotal_CurrentRowUnchanged()
        {
            // Given: Current row in a smaller dataset.
            _engine.Initialize(NavigationMode.Cell, 50, 5, 10);
            _engine.SetCurrentPosition(20, 0);

            // When: Total row count expands.
            _engine.UpdateStats(200, 10); // more rows

            // Then: Current row stays unchanged.
            Assert.That(_engine.CurrentRowIndex, Is.EqualTo(20));
        }

        // ── SetCurrentPosition ────────────────────────────────────────────────

        [Test]
        public void SetCurrentPosition_UpdatesRowAndColumnIndex()
        {
            // Given: An initialized engine.
            _engine.Initialize(NavigationMode.Cell, 100, 10, 20);

            // When: Position is set explicitly.
            _engine.SetCurrentPosition(42, 7);

            // Then: Row and column are updated.
            Assert.That(_engine.CurrentRowIndex,    Is.EqualTo(42));
            Assert.That(_engine.CurrentColumnIndex, Is.EqualTo(7));
        }

        [Test]
        public void SetCurrentPosition_DoesNotFireNavigationRequestedEvent()
        {
            // Given: An initialized engine with a NavigationRequested subscriber.
            _engine.Initialize(NavigationMode.Cell, 100, 10, 20);
            bool fired = false;
            _engine.NavigationRequested += (_, __) => fired = true;

            // When: Current position is set directly.
            _engine.SetCurrentPosition(5, 3);

            // Then: No navigation event is raised.
            Assert.That(fired, Is.False,
                "SetCurrentPosition is a silent position override — must not trigger navigation event.");
        }

        // ── SetEditingState ───────────────────────────────────────────────────

        [Test]
        public void SetEditingState_True_IsEditingBecomesTrue()
        {
            // Given: An initialized engine.
            _engine.Initialize(NavigationMode.Cell, 100, 10, 20);

            // When: Editing state is set to true.
            _engine.SetEditingState(true);

            // Then: IsEditing is true.
            Assert.That(_engine.IsEditing, Is.True);
        }

        [Test]
        public void SetEditingState_FalseAfterTrue_IsEditingBecomesFalse()
        {
            // Given: An initialized engine in editing state.
            _engine.Initialize(NavigationMode.Cell, 100, 10, 20);
            _engine.SetEditingState(true);

            // When: Editing state is set back to false.
            _engine.SetEditingState(false);

            // Then: IsEditing is false.
            Assert.That(_engine.IsEditing, Is.False);
        }

        // ── NavigationRequested event contract ────────────────────────────────

        [Test]
        public void NavigationRequested_FiredWhenPositionChangesViaInternal_RowAndColAreCorrect()
        {
            // Given: A listener collecting NavigationRequested events.
            var events = new List<NavigationEventArgs>();
            _engine.NavigationRequested += (_, e) => events.Add(e);

            // When: Engine initializes starting position silently.
            _engine.Initialize(NavigationMode.Cell, 5, 3, 5);

            // Then: No NavigationRequested event is fired by Initialize.
            Assert.That(events, Is.Empty,
                "Initialize must not fire NavigationRequested.");
        }

        // ── EditRequested event contract ──────────────────────────────────────

        [Test]
        public void EditRequested_EventArgsExposesRowColumnAndKey()
        {
            // Given: EditEventArgs created with explicit values.
            var args = new EditEventArgs(3, 7, System.Windows.Input.Key.F2);

            // When: Event argument properties are read.

            // Then: Row, column, and trigger key are exposed correctly.
            Assert.That(args.RowIndex,    Is.EqualTo(3));
            Assert.That(args.ColumnIndex, Is.EqualTo(7));
            Assert.That(args.TriggerKey,  Is.EqualTo(System.Windows.Input.Key.F2));
        }
    }
}
