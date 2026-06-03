namespace Open.DataGrid.Models
{
    /// <summary>
    /// Controls which visual elements are shown while the grid is computing
    /// filters, sorting, or rebuilding rows.
    /// </summary>
    public enum LoadingIndicatorMode
    {
        /// <summary>Show both the subtle overlay and the animated progress bar (default).</summary>
        All,
        /// <summary>Show only the thin animated progress bar at the top of the canvas.</summary>
        ProgressBarOnly,
        /// <summary>Show only the subtle semi-transparent overlay; no progress bar.</summary>
        OverlayOnly,
        /// <summary>Show nothing — grid renders intermediate states without any indicator.</summary>
        None
    }
}
