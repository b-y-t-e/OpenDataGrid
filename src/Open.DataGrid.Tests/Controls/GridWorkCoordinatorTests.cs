using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Open.DataGrid.Controls;
using Open.DataGrid.Models;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Controls
{
    /// <summary>
    /// Unit tests for <see cref="GridWorkCoordinator"/>.
    ///
    /// Covers the reference-counted pending-work counter, epoch-based staleness detection,
    /// WorkToken RAII disposal and the filter-pending signal set.
    /// Visibility of loading indicators is tested indirectly via <c>Visibility</c> on
    /// injected <see cref="Border"/> stubs — no real WPF template is needed.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class GridWorkCoordinatorTests
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        private Border _overlay;
        private Border _bar;
        private GridWorkCoordinator _coord;

        private void Init(LoadingIndicatorMode mode = LoadingIndicatorMode.OverlayOnly)
        {
            _overlay = new Border();
            _bar     = new Border();
            _coord   = new GridWorkCoordinator(() => mode);
            _coord.SetTemplateParts(_overlay, _bar);
        }

        // ── Pending-work counter ───────────────────────────────────────────────

        [Test]
        public void BeginPendingWork_FirstCall_ShowsOverlay()
        {
            // Given: fresh coordinator with overlay/bar stubs.
            Init();

            // When: first pending-work scope begins.
            _coord.BeginPendingWork();

            // Then: overlay becomes visible when counter crosses 0 → 1.
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void EndPendingWork_BalancingBegin_HidesOverlay()
        {
            // Given: one pending scope in flight.
            Init();
            _coord.BeginPendingWork();

            // When: the matching End is called.
            _coord.EndPendingWork();

            // Then: overlay is hidden once the counter drops back to 0.
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void NestedBeginEnd_OverlayStaysVisibleUntilLastEnd()
        {
            // Given: two nested pending scopes.
            Init();
            _coord.BeginPendingWork();
            _coord.BeginPendingWork();

            // When: first End — still one scope in flight.
            _coord.EndPendingWork();

            // Then: overlay stays visible because counter is still 1.
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Visible));

            // When: second (final) End.
            _coord.EndPendingWork();

            // Then: now the overlay hides.
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void EndPendingWork_WithStaleEpoch_IsNoOp()
        {
            // Given: a pending scope started at the current epoch.
            Init();
            int staleEpoch = _coord.CurrentEpoch;
            _coord.BeginPendingWork();

            // When: the grid is unloaded (Reset bumps the epoch).
            _coord.Reset();

            // Then: overlay is already hidden by Reset.
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed));

            // When: the async callback tries to end using the stale epoch.
            _coord.EndPendingWork(staleEpoch);

            // Then: still hidden — stale call had no effect (no double-hide / exception).
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        // ── WorkToken RAII ────────────────────────────────────────────────────

        [Test]
        public void WorkToken_Dispose_EndsOnePendingWork()
        {
            // Given: a WorkToken obtained from BeginWork().
            Init();
            var token = _coord.BeginWork();
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Visible), "sanity check — overlay up");

            // When: token is disposed.
            token.Dispose();

            // Then: counter returns to 0 and overlay hides.
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void WorkToken_DoubleDispose_IsNoOp()
        {
            // Given: a WorkToken for one scope.
            Init();
            var token = _coord.BeginWork();

            // When: token is disposed twice (e.g. in a finally + using combo).
            token.Dispose();
            TestDelegate act = () => token.Dispose();

            // Then: second dispose does not throw and does not corrupt the counter.
            Assert.DoesNotThrow(act);
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        // ── Filter-pending set ────────────────────────────────────────────────

        [Test]
        public void NotifyFilterPending_AddsToSet_ShowsOverlay()
        {
            // Given: fresh coordinator, no pending filters.
            Init();
            var filterKey = new object();

            // When: a filter control signals it hasn't computed its predicate yet.
            _coord.NotifyFilterPending(filterKey);

            // Then: overlay is shown and the key is tracked.
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(_coord.HasPendingFilterSignals, Is.True);
        }

        [Test]
        public void NotifyFilterReady_RemovesFromSet_HidesOverlayWhenSetEmpty()
        {
            // Given: one filter is pending.
            Init();
            var filterKey = new object();
            _coord.NotifyFilterPending(filterKey);

            // When: the filter signals it is ready.
            _coord.NotifyFilterReady(filterKey);

            // Then: overlay hides and the pending set is empty.
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(_coord.HasPendingFilterSignals, Is.False);
        }

        [Test]
        public void NotifyFilterReady_UnknownKey_IsNoOp()
        {
            // Given: coordinator with overlay driven to Collapsed (Begin then End).
            // Note: Border.Visibility defaults to Visible in WPF; we must first put the
            // coordinator through a Begin/End cycle to establish the Collapsed baseline.
            Init();
            _coord.BeginPendingWork();
            _coord.EndPendingWork();
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed), "baseline");

            // When: NotifyFilterReady is called with a key that was never pending.
            TestDelegate act = () => _coord.NotifyFilterReady(new object());

            // Then: no exception, overlay stays collapsed.
            Assert.DoesNotThrow(act);
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        // ── LoadingIndicatorMode routing ──────────────────────────────────────

        [Test]
        public void BeginPendingWork_ModeAll_ShowsBothOverlayAndBar()
        {
            // Given: mode is All — both overlay and progress bar should appear.
            Init(LoadingIndicatorMode.All);

            // When
            _coord.BeginPendingWork();

            // Then
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(_bar.Visibility,     Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void BeginPendingWork_ModeProgressBarOnly_ShowsBarNotOverlay()
        {
            // Given: mode is ProgressBarOnly.
            Init(LoadingIndicatorMode.ProgressBarOnly);

            // When
            _coord.BeginPendingWork();

            // Then: only bar is shown, overlay stays hidden.
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(_bar.Visibility,     Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void BeginPendingWork_ModeNone_ShowsNeither()
        {
            // Given: mode is None — indicators disabled.
            Init(LoadingIndicatorMode.None);

            // When
            _coord.BeginPendingWork();

            // Then: both stay collapsed.
            Assert.That(_overlay.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(_bar.Visibility,     Is.EqualTo(Visibility.Collapsed));
        }
    }
}
