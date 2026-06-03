using System;
using System.Collections.Generic;
using Open.DataGrid.Columns;

namespace Open.DataGrid.Virtualization
{
    /// <summary>
    /// Computes which columns are visible in the current horizontal scroll position.
    /// Uses prefix-sum array for O(log n) lookup.
    /// </summary>
    public sealed class ColumnVirtualizationEngine
    {
        private double[] _cumulative; // cumulative widths (prefix sums)
        private int _columnCount;
        private double _totalWidth;

        // Cached layout arrays — rebuilt once in Rebuild(), returned by reference to avoid
        // per-scroll allocations. Callers must not mutate these arrays.
        private double[] _cachedWidths  = Array.Empty<double>();
        private double[] _cachedOffsets = Array.Empty<double>();

        public void Rebuild(IReadOnlyList<OpenDataGridColumn> columns)
        {
            _columnCount = columns.Count;
            _cumulative = new double[_columnCount + 1];
            _cumulative[0] = 0;
            for (int i = 0; i < _columnCount; i++)
                _cumulative[i + 1] = _cumulative[i] + columns[i].Width;
            _totalWidth = _cumulative[_columnCount];

            _cachedWidths  = new double[_columnCount];
            _cachedOffsets = new double[_columnCount];
            for (int i = 0; i < _columnCount; i++)
            {
                _cachedWidths[i]  = _cumulative[i + 1] - _cumulative[i];
                _cachedOffsets[i] = _cumulative[i];
            }
        }

        public double TotalWidth => _totalWidth;

        /// <summary>Returns column left edge offset.</summary>
        public double GetColumnOffset(int index)
        {
            if (index < 0 || index >= _columnCount) return 0;
            return _cumulative[index];
        }

        /// <summary>Returns the cached column widths array. Do not mutate.</summary>
        public double[] GetColumnWidths() => _cachedWidths;

        /// <summary>Returns the cached column offsets (prefix sums) array. Do not mutate.</summary>
        public double[] GetColumnOffsets() => _cachedOffsets;

        /// <summary>Gets the visible column range for given horizontal scroll and viewport width.</summary>
        public void GetVisibleColumnRange(double hOffset, double viewportWidth,
            out int firstVisible, out int lastVisible)
        {
            firstVisible = BinarySearchLeft(hOffset);
            lastVisible = BinarySearchRight(hOffset + viewportWidth);
            firstVisible = Math.Max(0, firstVisible);
            lastVisible = Math.Min(_columnCount - 1, lastVisible);
        }

        private int BinarySearchLeft(double target)
        {
            int lo = 0, hi = _columnCount - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_cumulative[mid + 1] <= target) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private int BinarySearchRight(double target)
        {
            int lo = 0, hi = _columnCount - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_cumulative[mid] >= target) hi = mid - 1;
                else lo = mid;
            }
            return lo;
        }
    }
}
