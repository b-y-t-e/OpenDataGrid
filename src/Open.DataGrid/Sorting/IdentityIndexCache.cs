using System;
using System.Collections.Generic;

namespace Open.DataGrid.Sorting
{
    /// <summary>
    /// Shares read-only identity index arrays (<c>[0, 1, ..., n-1]</c>) between <see cref="SortEngine"/>
    /// instances with the same row count — e.g. two grids bound to the same 1M-row source share one
    /// 4 MB array instead of allocating one each. Arrays are held weakly, so they are reclaimed once no
    /// engine uses them.
    /// </summary>
    /// <remarks>
    /// Returned arrays MUST NOT be mutated. <see cref="SortEngine"/> only ever writes into arrays it
    /// allocates itself (sort/filter paths copy first), and all other consumers only read
    /// <see cref="SortEngine.SortedIndices"/>.
    /// </remarks>
    internal static class IdentityIndexCache
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<int, WeakReference<int[]>> Cache =
            new Dictionary<int, WeakReference<int[]>>();

        public static int[] Get(int count)
        {
            if (count <= 0) return Array.Empty<int>();

            lock (Sync)
            {
                if (Cache.TryGetValue(count, out var weak) && weak.TryGetTarget(out var cached))
                    return cached;

                var indices = new int[count];
                for (int i = 0; i < count; i++)
                    indices[i] = i;

                PruneDeadEntries();
                Cache[count] = new WeakReference<int[]>(indices);
                return indices;
            }
        }

        // Keeps the dictionary from accumulating dead entries when row counts change frequently
        // (e.g. an ObservableCollection growing one item at a time).
        private static void PruneDeadEntries()
        {
            List<int> dead = null;
            foreach (var pair in Cache)
                if (!pair.Value.TryGetTarget(out _))
                    (dead ?? (dead = new List<int>())).Add(pair.Key);
            if (dead != null)
                foreach (var key in dead)
                    Cache.Remove(key);
        }
    }
}
