using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Open.DataGrid.Filtering;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Filtering
{
    /// <summary>
    /// Verifies that FilterEngine is safe to use from multiple threads simultaneously.
    /// Register/Unregister run on the "UI thread" (main thread here), Apply on background threads.
    /// </summary>
    [TestFixture, Category("Stress")]
    public class FilterEngineThreadSafetyTests
    {
        // A simple IList backed by an array, safe to iterate from multiple threads (read-only).
        private sealed class ReadOnlyListWrapper : IList
        {
            private readonly object[] _items;
            public ReadOnlyListWrapper(object[] items) { _items = items; }
            public int Count => _items.Length;
            public object this[int index] { get => _items[index]; set => throw new NotSupportedException(); }
            public bool IsFixedSize => true;
            public bool IsReadOnly => true;
            public bool IsSynchronized => false;
            public object SyncRoot => this;
            public IEnumerator GetEnumerator() => _items.GetEnumerator();
            public bool Contains(object value) => Array.IndexOf(_items, value) >= 0;
            public int IndexOf(object value) => Array.IndexOf(_items, value);
            public void CopyTo(Array array, int index) => _items.CopyTo(array, index);
            public int Add(object value) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public void Insert(int index, object value) => throw new NotSupportedException();
            public void Remove(object value) => throw new NotSupportedException();
            public void RemoveAt(int index) => throw new NotSupportedException();
        }

        [Test]
        public void ConcurrentRegisterUnregisterAndApply_DoesNotThrow()
        {
            // Given
            var engine = new FilterEngine();
            const int itemCount = 500;
            var data = new ReadOnlyListWrapper(Enumerable.Range(0, itemCount).Select(i => (object)i).ToArray());

            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            // When: background threads repeatedly call Apply while the main loop Register/Unregisters
            const int applyThreads = 4;
            var applyTasks = Enumerable.Range(0, applyThreads).Select(_ => Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try { engine.Apply(data); }
                    catch (Exception ex) { exceptions.Add(ex); cts.Cancel(); }
                }
            })).ToArray();

            // UI thread side: 200 iterations of register/apply/unregister
            const int iterations = 200;
            for (int i = 0; i < iterations && !cts.IsCancellationRequested; i++)
            {
                try
                {
                    int capture = i;
                    engine.RegisterFilter("key", item => (int)item % (capture % 7 + 2) == 0);
                    Thread.Yield();   // give background threads a chance to call Apply
                    engine.UnregisterFilter("key");
                    Thread.Yield();
                }
                catch (Exception ex) { exceptions.Add(ex); cts.Cancel(); }
            }

            cts.Cancel();
            Task.WaitAll(applyTasks);

            // Then
            Assert.That(exceptions, Is.Empty,
                "Expected no exceptions during concurrent Register/Unregister/Apply. " +
                $"First: {exceptions.FirstOrDefault()?.Message}");
        }

        [Test]
        public void HasActiveFilters_AfterConcurrentRegisterUnregister_IsConsistent()
        {
            // Given
            var engine = new FilterEngine();
            const int threads = 8;
            const int opsPerThread = 100;
            var barrier = new Barrier(threads);

            // When: multiple threads Register and Unregister their own key simultaneously
            var tasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
            {
                string key = $"key{t}";
                barrier.SignalAndWait();
                for (int i = 0; i < opsPerThread; i++)
                {
                    engine.RegisterFilter(key, _ => true);
                    engine.UnregisterFilter(key);
                }
            })).ToArray();
            Task.WaitAll(tasks);

            // Then: after all threads finish, no filters remain
            Assert.That(engine.HasActiveFilters, Is.False);
        }
    }
}
