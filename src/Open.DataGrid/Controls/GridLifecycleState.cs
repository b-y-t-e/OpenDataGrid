namespace Open.DataGrid.Controls
{
    internal enum GridLifecycleState
    {
        /// <summary>Initial state — OnApplyTemplate has not yet successfully wired template parts.</summary>
        NoTemplate,

        /// <summary>Template parts are present but initialization has not completed (no data, canvas 0, or re-init in progress).</summary>
        TemplateReady,

        /// <summary>Canvas height is 0 while data is present — row engine deferred until viewport is available.</summary>
        WaitingForViewport,

        /// <summary>Async filter compute is running in the background; FinishInitialize has not been called yet.</summary>
        Initializing,

        /// <summary>FinishInitialize completed successfully — grid is fully operational.</summary>
        Ready,

        /// <summary>OnGridUnloaded fired; subscriptions detached, async work cancelled.</summary>
        Unloaded,
    }
}
