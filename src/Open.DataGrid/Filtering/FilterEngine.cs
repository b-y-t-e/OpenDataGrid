using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Open.DataGrid.Filtering
{
    /// <summary>
    /// Manages a set of named filter predicates and produces a filtered index array.
    /// All registered filters are AND-combined: a row passes only if every predicate returns true.
    /// Thread-safe: Register/Unregister run on the UI thread; Apply may run on a background thread.
    /// </summary>
    public sealed class FilterEngine
    {
        private readonly Dictionary<object, Func<object, bool>> _filters
            = new Dictionary<object, Func<object, bool>>();
        private readonly object _lock = new object();
        // Volatile counter avoids taking a lock on the hot-path HasActiveFilters read.
        private volatile int _activeCount;

        public bool HasActiveFilters => _activeCount > 0;

        public void RegisterFilter(object key, Func<object, bool> predicate)
        {
            lock (_lock)
            {
                bool existed = _filters.ContainsKey(key);
                _filters[key] = predicate;
                if (!existed) _activeCount++;
            }
        }

        public void UnregisterFilter(object key)
        {
            lock (_lock)
            {
                if (_filters.Remove(key))
                    _activeCount--;
            }
        }

        /// <summary>
        /// Returns int[] of data indices (into dataSource) that pass ALL registered filters.
        /// Pre-allocates the full count then truncates — avoids List&lt;int&gt; resizing overhead.
        /// Safe to call from a background thread: takes a snapshot of predicates under lock,
        /// then iterates the snapshot without holding the lock.
        /// Throws <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/>
        /// is cancelled mid-iteration — caller is responsible for cleanup.
        /// </summary>
        public int[] Apply(IList dataSource, CancellationToken cancellationToken = default)
        {
            if (dataSource == null) return new int[0];

            // Snapshot predicate list under lock so UI-thread Register/Unregister cannot race.
            Func<object, bool>[] predicates;
            lock (_lock)
            {
                predicates = new Func<object, bool>[_filters.Count];
                _filters.Values.CopyTo(predicates, 0);
            }

            int total = dataSource.Count;
            var result = new int[total];
            int count = 0;

            for (int i = 0; i < total; i++)
            {
                // Check every 512 rows — fast enough response, zero overhead on hot path.
                if ((i & 511) == 0) cancellationToken.ThrowIfCancellationRequested();

                object item = dataSource[i];
                bool passes = true;
                foreach (var predicate in predicates)
                {
                    if (!predicate(item))
                    {
                        passes = false;
                        break;
                    }
                }
                if (passes)
                    result[count++] = i;
            }

            if (count < total)
                Array.Resize(ref result, count);

            return result;
        }
    }
}
