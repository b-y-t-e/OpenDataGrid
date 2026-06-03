namespace Open.DataGrid.Virtualization
{
    /// <summary>
    /// Abstraction over the WPF Canvas used to host and position row slots.
    /// Production code: <see cref="WpfGridCanvas"/>. Tests: <c>TestGridCanvas</c>.
    /// </summary>
    public interface IGridCanvas
    {
        /// <summary>Current drawable width (used to size rows). May return 0 before layout.</summary>
        double Width { get; }

        /// <summary>Removes all slot visuals (called before rebuilding the pool).</summary>
        void Clear();

        /// <summary>Adds a slot to the canvas at left=0.</summary>
        void AddSlot(IRowSlot slot);

        /// <summary>Sets the top position and size of a slot.</summary>
        void PositionSlot(IRowSlot slot, double top, double width, double height);
    }
}
