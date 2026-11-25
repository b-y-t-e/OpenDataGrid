using System;

namespace Open.DataGrid.Models
{
    /// <summary>
    /// A rectangular block of cells selected with the mouse (drag, Ctrl, Shift) or the keyboard
    /// (Shift+arrows, Ctrl+A). Rows are visual (sorted/filtered) positions, columns are visible-column
    /// indices; both bounds are inclusive and normalized (<c>First &lt;= Last</c>). Only coordinates are
    /// stored, so a range over a million rows costs the same as a single cell.
    /// </summary>
    public readonly struct CellRange : IEquatable<CellRange>
    {
        public int FirstRow { get; }
        public int LastRow { get; }
        public int FirstColumn { get; }
        public int LastColumn { get; }

        /// <summary>Creates a range between two corner cells given in any order.</summary>
        public CellRange(int row1, int column1, int row2, int column2)
        {
            FirstRow    = Math.Min(row1, row2);
            LastRow     = Math.Max(row1, row2);
            FirstColumn = Math.Min(column1, column2);
            LastColumn  = Math.Max(column1, column2);
        }

        public int RowCount => LastRow - FirstRow + 1;
        public int ColumnCount => LastColumn - FirstColumn + 1;

        /// <summary><c>true</c> when the range covers exactly one cell.</summary>
        public bool IsSingleCell => FirstRow == LastRow && FirstColumn == LastColumn;

        public bool Contains(int row, int column)
            => row >= FirstRow && row <= LastRow && column >= FirstColumn && column <= LastColumn;

        public bool Equals(CellRange other)
            => FirstRow == other.FirstRow && LastRow == other.LastRow
               && FirstColumn == other.FirstColumn && LastColumn == other.LastColumn;

        public override bool Equals(object obj) => obj is CellRange other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = FirstRow;
                hash = hash * 397 ^ LastRow;
                hash = hash * 397 ^ FirstColumn;
                hash = hash * 397 ^ LastColumn;
                return hash;
            }
        }

        public static bool operator ==(CellRange left, CellRange right) => left.Equals(right);
        public static bool operator !=(CellRange left, CellRange right) => !left.Equals(right);

        public override string ToString() => $"R{FirstRow}C{FirstColumn}:R{LastRow}C{LastColumn}";
    }
}
