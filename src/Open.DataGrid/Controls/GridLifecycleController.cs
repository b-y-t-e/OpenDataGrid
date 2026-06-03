using System.Diagnostics;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// Tracks the lifecycle state of <see cref="OpenDataGrid"/> and exposes named guards that
    /// replace the raw boolean flags (_initialized, _wasUnloaded, _rowEngineNeedsReinit).
    /// Every state transition is logged in DEBUG to a single point (Transition).
    ///
    /// Behavioral note: DP property-change handlers (e.g. OnNavigationModeChanged)
    /// early-return via CanHandleInput between Unload and Load. In the pre-refactor code
    /// _initialized remained true after Unload, so those handlers could still fire.
    /// The new behavior is defensively correct — applying visual updates to a
    /// detached grid was harmless but unnecessary.
    /// </summary>
    internal sealed class GridLifecycleController
    {
        public GridLifecycleState State { get; private set; } = GridLifecycleState.NoTemplate;

        // --- Guards used by OpenDataGrid instead of direct flag checks ---

        /// <summary>Grid is fully operational — data rendered, navigation and selection active.</summary>
        public bool CanHandleInput => State == GridLifecycleState.Ready;

        /// <summary>
        /// Replaces <c>_initialized</c>. Same body as <see cref="CanHandleInput"/> — distinct names
        /// carry semantic intent: CanHandleInput is a gate for input/render; IsInitialized is a
        /// logical predicate used in data-access guards where the distinction matters for readability.
        /// </summary>
        public bool IsInitialized => State == GridLifecycleState.Ready;

        /// <summary>Replaces <c>_wasUnloaded</c>.</summary>
        public bool IsUnloaded => State == GridLifecycleState.Unloaded;

        /// <summary>Replaces <c>_rowEngineNeedsReinit</c>.</summary>
        public bool NeedsViewport => State == GridLifecycleState.WaitingForViewport;

        // --- Transitions ---

        /// <summary>
        /// OnApplyTemplate obtained all template parts.
        /// No-op when Unloaded — OnApplyTemplate can fire during re-mount before the Loaded event;
        /// letting it clear the Unloaded state would cause OnGridLoaded to skip re-attaching CollectionChanged.
        /// </summary>
        public void OnTemplateApplied()
        {
            if (State != GridLifecycleState.Unloaded)
                Transition(GridLifecycleState.TemplateReady, nameof(OnTemplateApplied));
        }

        /// <summary>
        /// TryInitialize began a full reinitialize cycle — grid is no longer considered Ready
        /// until FinishInitialize succeeds. Backs the state off to TemplateReady (not Initializing;
        /// the async path uses <see cref="OnAsyncInitStarted"/> for that).
        /// No-op when Unloaded so that a DataSource binding firing while detached cannot
        /// accidentally clear the Unloaded flag.
        /// </summary>
        public void BeginReinitialize()
        {
            if (State != GridLifecycleState.Unloaded)
                Transition(GridLifecycleState.TemplateReady, nameof(BeginReinitialize));
        }

        /// <summary>TryInitialize took the async filter path — FinishInitialize will fire via Dispatcher.</summary>
        public void OnAsyncInitStarted()
            => Transition(GridLifecycleState.Initializing, nameof(OnAsyncInitStarted));

        /// <summary>
        /// FinishInitialize determined the outcome.
        /// <paramref name="canvasReady"/> — canvas height > 0.
        /// <paramref name="hasData"/> — DataSource is non-null and non-empty.
        /// Transitions: (true, true) → Ready; (false, true) → WaitingForViewport; otherwise → TemplateReady.
        /// No-op when Unloaded — only <see cref="OnGridLoaded"/> may exit the Unloaded state.
        /// </summary>
        public void OnInitializationComplete(bool canvasReady, bool hasData)
        {
            // TryInitialize can be triggered while Unloaded (e.g. DataSource binding fires before the grid
            // is back in the visual tree). Allowing a transition out of Unloaded here would clear IsUnloaded,
            // causing OnGridLoaded to skip re-attaching CollectionChanged.
            if (State == GridLifecycleState.Unloaded) return;

            GridLifecycleState next;
            if (canvasReady && hasData)
                next = GridLifecycleState.Ready;
            else if (!canvasReady && hasData)
                next = GridLifecycleState.WaitingForViewport;
            else
                next = GridLifecycleState.TemplateReady;

            Transition(next, nameof(OnInitializationComplete));
        }

        /// <summary>Canvas became unavailable (height dropped to 0) while grid was Ready — row engine must reinit when viewport returns.</summary>
        public void OnViewportLost()
        {
            if (State == GridLifecycleState.Ready || State == GridLifecycleState.Initializing)
                Transition(GridLifecycleState.WaitingForViewport, nameof(OnViewportLost));
        }

        /// <summary>OnGridUnloaded — grid left visual tree.</summary>
        public void OnGridUnloaded()
            => Transition(GridLifecycleState.Unloaded, nameof(OnGridUnloaded));

        /// <summary>OnGridLoaded after prior Unloaded — grid is back in visual tree, needs re-init.</summary>
        public void OnGridLoaded()
        {
            if (State == GridLifecycleState.Unloaded)
                Transition(GridLifecycleState.TemplateReady, nameof(OnGridLoaded));
        }

        // --- Internal ---

        private void Transition(GridLifecycleState next, string trigger)
        {
#if DEBUG
            if (State != next)
                Debug.WriteLine($"[EDG-LC] {State} → {next}  ({trigger})");
#endif
            State = next;
        }
    }
}
