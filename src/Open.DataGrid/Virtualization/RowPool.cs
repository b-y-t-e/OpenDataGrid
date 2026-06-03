using System;

namespace Open.DataGrid.Virtualization
{
    /// <summary>
    /// Generic ring buffer of row slots.
    /// Rotation is O(1) pointer arithmetic — no allocations during scrolling.
    /// </summary>
    public sealed class RowPool<T>
    {
        private T[] _rows;
        private int _headIndex;
        private int _size;

        public int Size => _size;

        public void Initialize(int size, Func<int, T> factory)
        {
            _size = size;
            _headIndex = 0;
            _rows = new T[size];
            for (int i = 0; i < size; i++)
                _rows[i] = factory(i);
        }

        /// <summary>Returns the row at logical pool offset (0 = top-most visible).</summary>
        public T GetRow(int poolOffset)
        {
            return _rows[(_headIndex + poolOffset + _size) % _size];
        }

        /// <summary>Rotates the ring forward (scrolling down).</summary>
        public void RotateForward(int count)
        {
            _headIndex = (_headIndex + count) % _size;
        }

        /// <summary>Rotates the ring backward (scrolling up).</summary>
        public void RotateBackward(int count)
        {
            _headIndex = (_headIndex - count + _size) % _size;
        }

        public void ForEach(Action<T, int> action)
        {
            for (int i = 0; i < _size; i++)
                action(GetRow(i), i);
        }
    }
}
