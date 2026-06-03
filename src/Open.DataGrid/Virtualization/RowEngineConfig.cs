using System;
using System.Collections;
using System.Windows;
using System.Windows.Media;

namespace Open.DataGrid.Virtualization
{
    /// <summary>
    /// Groups all initialization parameters for <see cref="RowVirtualizationEngine"/>,
    /// replacing the 15-parameter overload with a single readable object.
    /// </summary>
    public sealed class RowEngineConfig
    {
        public IGridCanvas                  Canvas              { get; set; }
        public Func<int, IRowSlot>          SlotFactory         { get; set; }
        public IList                        DataSource          { get; set; }
        public int[]                        SortedIndices       { get; set; }
        public Func<object, object>[]       PropertyAccessors   { get; set; }
        public Func<object, Brush>[]        CellBrushAccessors  { get; set; }
        public Func<int, object, Brush>     RowColorResolver    { get; set; }
        public Func<object, bool>           RowReadOnlyAccessor { get; set; }
        public double                       RowHeight           { get; set; }
        public double                       CanvasHeight        { get; set; }
        public double                       HOffset             { get; set; }
        public int                          FirstVisibleCol     { get; set; }
        public int                          LastVisibleCol      { get; set; }
        public double[]                     ColumnWidths        { get; set; }
        public double[]                     ColumnOffsets       { get; set; }
        /// <summary>
        /// When <c>true</c> and <see cref="SortedIndices"/> is empty, the engine renders a single
        /// empty placeholder row instead of leaving the canvas blank.
        /// </summary>
        public bool                         IsPlaceholderMode   { get; set; }

        /// <summary>Returns <c>true</c> when the given data item is in the checked set.</summary>
        public Func<object, bool>           IsCheckedAccessor   { get; set; }

        public Brush                        CheckedRowForeground { get; set; }
        public Brush                        CheckedRowBackground { get; set; }
        public FontWeight?                  CheckedRowFontWeight { get; set; }
    }
}
