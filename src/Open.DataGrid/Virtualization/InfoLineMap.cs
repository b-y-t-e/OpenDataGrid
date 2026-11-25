using System;
using System.Collections;

namespace Open.DataGrid.Virtualization
{
    /// <summary>
    /// Maps sorted (navigation) indices to "lines" — a line is either a data row or the info row rendered
    /// directly below it. <c>_lineToSorted[line]</c> = sorted index (data line) or <c>~sorted</c> (info line).
    /// Both maps are <c>null</c> when no item has info text — lines == sorted positions (zero overhead).
    /// </summary>
    public sealed class InfoLineMap
    {
        private int[] _lineToSorted;
        private int[] _sortedToLine;
        private int   _sortedCount;

        // Per-data-index "has info text" flags, reused across sort / filter rebuilds so the getter
        // is invoked over the data source only once per source / accessor / structural change.
        private IList                _infoFlagsSource;
        private Func<object, string> _infoFlagsAccessor;
        private bool[]               _infoFlagsByData;
        private int                  _infoFlagsCount;

        /// <summary><c>true</c> when at least one info row is present (line space != sorted space).</summary>
        public bool HasInfoRows => _lineToSorted != null;

        /// <summary>Total number of lines (data rows + info rows).</summary>
        public int TotalLines => _lineToSorted?.Length ?? _sortedCount;

        /// <summary>Number of data rows (sorted positions) covered by the map.</summary>
        public int SortedCount => _sortedCount;

        /// <summary>
        /// Builds the map for <paramref name="sortedIndices"/>. Pass a <c>null</c> accessor to disable info rows.
        /// </summary>
        public void Build(IList dataSource, int[] sortedIndices, Func<object, string> infoTextAccessor)
        {
            _lineToSorted = null;
            _sortedToLine = null;
            int n = sortedIndices?.Length ?? 0;
            _sortedCount = n;
            if (infoTextAccessor == null || n == 0 || dataSource == null) return;

            EnsureInfoFlags(dataSource, infoTextAccessor);
            if (_infoFlagsCount == 0) return;

            bool[] hasInfo = MarkSortedWithInfo(sortedIndices, out int infoCount);
            if (infoCount == 0) return;

            var lineToSorted = new int[n + infoCount];
            var sortedToLine = new int[n];
            int line = 0;
            for (int s = 0; s < n; s++)
            {
                sortedToLine[s] = line;
                lineToSorted[line++] = s;
                if (hasInfo[s]) lineToSorted[line++] = ~s;
            }
            _lineToSorted = lineToSorted;
            _sortedToLine = sortedToLine;
        }

        /// <summary>Drops the cached info flags — call when items were added / removed / replaced or info texts changed.</summary>
        public void InvalidateInfoCache() => _infoFlagsSource = null;

        /// <summary>Maps a sorted (navigation) index to the line of its data row.</summary>
        public int SortedToLine(int sortedIndex)
        {
            var map = _sortedToLine;
            if (map == null || sortedIndex < 0 || sortedIndex >= map.Length) return sortedIndex;
            return map[sortedIndex];
        }

        /// <summary>Maps a line to the sorted index of the data row it belongs to (info line → owner row).</summary>
        public int LineToSorted(int line)
        {
            var map = _lineToSorted;
            if (map == null || line < 0 || line >= map.Length) return line;
            int v = map[line];
            return v >= 0 ? v : ~v;
        }

        /// <summary><c>true</c> when <paramref name="line"/> renders an info row.</summary>
        public bool IsInfoLine(int line)
        {
            var map = _lineToSorted;
            return map != null && line >= 0 && line < map.Length && map[line] < 0;
        }

        /// <summary><c>true</c> when the line right after <paramref name="line"/> is an info row.</summary>
        public bool HasInfoLineAfter(int line) => IsInfoLine(line + 1);

        /// <summary>Last line occupied by the data row (its info line when present, else the data line).</summary>
        public int GetLastLineOfSorted(int sortedIndex)
        {
            int line = SortedToLine(sortedIndex);
            return HasInfoLineAfter(line) ? line + 1 : line;
        }

        /// <summary>
        /// Line that keeps a previous top anchor in place after a rebuild. When the anchor was the info row
        /// of <paramref name="ownerOrDataSorted"/> and that info is gone, the next data row becomes the anchor.
        /// </summary>
        public int FirstLineForAnchor(int ownerOrDataSorted, bool anchorWasInfo)
        {
            int line = SortedToLine(ownerOrDataSorted);
            if (!anchorWasInfo) return line;
            if (HasInfoLineAfter(line)) return line + 1;
            int next = ownerOrDataSorted + 1;
            return next < _sortedCount ? SortedToLine(next) : line;
        }

        private bool[] MarkSortedWithInfo(int[] sortedIndices, out int infoCount)
        {
            bool[] flags = _infoFlagsByData;
            var hasInfo = new bool[sortedIndices.Length];
            infoCount = 0;
            for (int s = 0; s < sortedIndices.Length; s++)
            {
                int di = sortedIndices[s];
                if (di < 0 || di >= flags.Length || !flags[di]) continue;
                hasInfo[s] = true;
                infoCount++;
            }
            return hasInfo;
        }

        private void EnsureInfoFlags(IList dataSource, Func<object, string> infoTextAccessor)
        {
            int count = dataSource.Count;
            if (ReferenceEquals(_infoFlagsSource, dataSource)
                && ReferenceEquals(_infoFlagsAccessor, infoTextAccessor)
                && _infoFlagsByData.Length == count) return;

            var flags = new bool[count];
            int infoCount = 0;
            for (int di = 0; di < count; di++)
            {
                if (string.IsNullOrEmpty(infoTextAccessor(dataSource[di]))) continue;
                flags[di] = true;
                infoCount++;
            }
            _infoFlagsSource   = dataSource;
            _infoFlagsAccessor = infoTextAccessor;
            _infoFlagsByData   = flags;
            _infoFlagsCount    = infoCount;
        }
    }
}
