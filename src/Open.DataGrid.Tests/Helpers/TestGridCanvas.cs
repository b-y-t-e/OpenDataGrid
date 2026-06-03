using System.Collections.Generic;
using Open.DataGrid.Virtualization;

namespace Open.DataGrid.Tests.Helpers
{
    /// <summary>
    /// Pure-C# test double for <see cref="IGridCanvas"/>.
    /// Records which slots were positioned so viewport tests can make geometric assertions.
    /// </summary>
    public sealed class TestGridCanvas : IGridCanvas
    {
        public struct SlotPosition
        {
            public double Top;
            public double Width;
            public double Height;
        }

        public double Width { get; set; } = 1000.0;

        public readonly List<IRowSlot> Slots = new List<IRowSlot>();

        public readonly Dictionary<IRowSlot, SlotPosition> Positions
            = new Dictionary<IRowSlot, SlotPosition>();

        public void Clear()
        {
            Slots.Clear();
            Positions.Clear();
        }

        public void AddSlot(IRowSlot slot) => Slots.Add(slot);

        public void PositionSlot(IRowSlot slot, double top, double width, double height)
            => Positions[slot] = new SlotPosition { Top = top, Width = width, Height = height };
    }
}
