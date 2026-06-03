using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Open.DataGrid.Models;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// Tracks in-flight async work (filter compute, sort, collection rebuild) and
    /// shows/hides the loading indicators accordingly. Uses a reference-counted
    /// counter (<c>_pendingWorkCount</c>) so nested Begin/End pairs nest correctly.
    /// An epoch value (<see cref="CurrentEpoch"/>) lets async callbacks detect that
    /// the grid was unloaded between the time they were launched and when they complete.
    ///
    /// All mutations must happen on the UI thread.
    /// </summary>
    internal sealed class GridWorkCoordinator
    {
        // -----------------------------------------------------------------------
        // WorkToken
        // -----------------------------------------------------------------------

        /// <summary>
        /// A one-shot lease returned by <see cref="BeginWork"/>.
        /// Disposing ends the pending-work scope via <see cref="EndPendingWork(int)"/>.
        /// Double-Dispose is a safe no-op.
        /// </summary>
        internal sealed class WorkToken : IDisposable
        {
            private readonly GridWorkCoordinator _coordinator;
            private bool _disposed;

            private readonly int _epoch;

            internal WorkToken(GridWorkCoordinator coordinator, int epoch)
            {
                _coordinator = coordinator;
                _epoch       = epoch;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _coordinator.EndPendingWork(_epoch);
            }
        }

        private int _pendingWorkCount;
        private int _epoch;
        private readonly HashSet<object> _pendingFilters = new HashSet<object>();

        private FrameworkElement _loadingOverlay;
        private FrameworkElement _loadingBar;

        private readonly System.Func<LoadingIndicatorMode> _getMode;

        /// <param name="getMode">
        /// Delegate that returns the current <see cref="LoadingIndicatorMode"/> DP value.
        /// Called each time the loading indicators are toggled.
        /// </param>
        public GridWorkCoordinator(System.Func<LoadingIndicatorMode> getMode)
        {
            _getMode = getMode;
        }

        // -----------------------------------------------------------------------
        // Epoch
        // -----------------------------------------------------------------------

        /// <summary>
        /// Current epoch value — a staleness token for async callbacks.
        /// Canonical usage: capture <em>before</em> <see cref="BeginPendingWork"/>,
        /// pass the captured value to <see cref="EndPendingWork(int)"/> in the callback.
        /// <code>
        ///   int epoch = _workCoordinator.CurrentEpoch;
        ///   _workCoordinator.BeginPendingWork();
        ///   Task.Run(() => { ...; Dispatcher.BeginInvoke(() => {
        ///       try { ... } finally { _workCoordinator.EndPendingWork(epoch); }
        ///   }); });
        /// </code>
        /// Exception: sort epochs are captured <em>after</em> Sort() returns —
        /// see <c>_sortPendingEpoch</c> in <c>OpenDataGrid</c>.
        /// </summary>
        public int CurrentEpoch => _epoch;

        /// <summary>For tests: true when at least one filter key has an outstanding pending signal.</summary>
        internal bool HasPendingFilterSignals => _pendingFilters.Count > 0;

        // -----------------------------------------------------------------------
        // Template parts
        // -----------------------------------------------------------------------

        /// <summary>
        /// Called from <c>OnApplyTemplate</c> to wire up the loading indicator template parts.
        /// May be called multiple times (template re-application); stale references are replaced.
        /// </summary>
        public void SetTemplateParts(FrameworkElement overlay, FrameworkElement bar)
        {
            _loadingOverlay = overlay;
            _loadingBar     = bar;
        }

        // -----------------------------------------------------------------------
        // Pending work counter
        // -----------------------------------------------------------------------

        /// <summary>
        /// Begins a pending-work scope and returns a <see cref="WorkToken"/> whose
        /// <see cref="WorkToken.Dispose"/> ends it. Preferred over the manual
        /// <see cref="BeginPendingWork"/> / <see cref="EndPendingWork(int)"/> pair for
        /// async paths where a <c>finally</c> block owns the cleanup.
        /// </summary>
        public WorkToken BeginWork()
        {
            var token = new WorkToken(this, _epoch);
            BeginPendingWork();
            return token;
        }

        /// <summary>
        /// Increments the pending-work counter; shows the loading indicator when the
        /// counter crosses from 0 to 1. Use <see cref="BeginWork"/> for async paths.
        /// For sort paths that hand off cleanup to <c>OnSortCompleted</c> /
        /// <c>OnSortCancelled</c>, call this directly and capture <see cref="CurrentEpoch"/>
        /// into <c>_sortPendingEpoch</c> after <c>Sort()</c> returns.
        /// </summary>
        public void BeginPendingWork()
        {
            if (++_pendingWorkCount == 1)
                ShowLoadingIndicators(true);
        }

        /// <summary>
        /// Decrements the pending-work counter; hides the loading indicator when it
        /// drops to 0. Use this overload for synchronous callers that always run on
        /// the UI thread.
        /// </summary>
        public void EndPendingWork() => EndPendingWork(_epoch);

        /// <summary>
        /// Epoch-aware variant for async callbacks.
        /// If <paramref name="epoch"/> differs from <see cref="CurrentEpoch"/> the call
        /// is a true no-op — the callback is stale from before the last
        /// <see cref="Reset"/> call.
        /// </summary>
        public void EndPendingWork(int epoch)
        {
            if (epoch != _epoch) return;      // stale: grid was unloaded after this work began
            if (_pendingWorkCount <= 0) return;
            if (--_pendingWorkCount == 0)
                ShowLoadingIndicators(false);
        }

        // -----------------------------------------------------------------------
        // Filter-pending set
        // -----------------------------------------------------------------------

        /// <summary>
        /// Called by filter controls when they attach to the grid and have not yet
        /// computed and registered their predicate. Adds <paramref name="source"/> to
        /// the pending-filter set; shows the overlay while at least one is pending.
        /// </summary>
        public void NotifyFilterPending(object source)
        {
            if (_pendingFilters.Add(source) && _pendingFilters.Count == 1)
                BeginPendingWork();
        }

        /// <summary>
        /// Called by filter controls after they have registered (or decided not to
        /// register) their predicate. Removes <paramref name="source"/> from the pending
        /// set; when the set becomes empty the overlay is released.
        /// </summary>
        public void NotifyFilterReady(object source)
        {
            if (!_pendingFilters.Remove(source)) return;
            if (_pendingFilters.Count == 0)
                EndPendingWork();
        }

        // -----------------------------------------------------------------------
        // Unload reset
        // -----------------------------------------------------------------------

        /// <summary>
        /// Invalidates all in-flight async <see cref="EndPendingWork(int)"/> calls by
        /// bumping the epoch, then resets all counters and hides the indicators.
        /// Call from <c>OnGridUnloaded</c>.
        /// </summary>
        public void Reset()
        {
            _epoch++;
            _pendingFilters.Clear();
            _pendingWorkCount = 0;
            ShowLoadingIndicators(false);
        }

        // -----------------------------------------------------------------------
        // Private helpers
        // -----------------------------------------------------------------------

        private void ShowLoadingIndicators(bool show)
        {
            var mode = _getMode();
            bool showOverlay = show && (mode == LoadingIndicatorMode.All || mode == LoadingIndicatorMode.OverlayOnly);
            bool showBar     = show && (mode == LoadingIndicatorMode.All || mode == LoadingIndicatorMode.ProgressBarOnly);

            if (_loadingOverlay != null)
                _loadingOverlay.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;
            if (_loadingBar != null)
                _loadingBar.Visibility = showBar ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
