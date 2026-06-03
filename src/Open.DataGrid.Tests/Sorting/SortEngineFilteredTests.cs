using System.Collections.Generic;
using System.Threading;
using Open.DataGrid.Columns;
using Open.DataGrid.Models;
using Open.DataGrid.Sorting;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Sorting
{
    /// <summary>
    /// Tests for SortEngine with a pre-filtered index set.
    /// Verifies that sorting operates only on filtered indices and
    /// FindSortedPosition returns -1 for filtered-out rows.
    /// STA required because OpenDataGridColumn (Freezable) is a DependencyObject.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class SortEngineFilteredTests
    {
        private SortEngine _engine;
        private List<SortItem> _data;
        private OpenDataGridLabelColumn _nameColumn;

        [SetUp]
        public void SetUp()
        {
            // Data: index 0=Charlie, 1=Alice, 2=Bob, 3=Dave
            _data = new List<SortItem>
            {
                new SortItem { Name = "Charlie", IntValue = 3 },  // 0
                new SortItem { Name = "Alice",   IntValue = 1 },  // 1
                new SortItem { Name = "Bob",     IntValue = 2 },  // 2
                new SortItem { Name = "Dave",    IntValue = 4 },  // 3
            };
            _engine = new SortEngine();
            _nameColumn = new OpenDataGridLabelColumn { BindingPath = "Name" };
        }

        [Test]
        public void Initialize_WithFilteredIndices_SortedIndicesContainsOnlyFiltered()
        {
            // Given — filter to indices 0 and 2 (Charlie, Bob)
            var filtered = new[] { 0, 2 };

            // When
            _engine.Initialize(filtered, _data.Count);

            // Then
            Assert.That(_engine.SortedIndices, Is.EquivalentTo(new[] { 0, 2 }));
        }

        [Test]
        public void Initialize_WithFilteredIndices_FindSortedPosition_ReturnsMinusOneForFilteredOut()
        {
            // Given
            var filtered = new[] { 0, 2 };
            _engine.Initialize(filtered, _data.Count);

            // When / Then
            Assert.That(_engine.FindSortedPosition(1), Is.EqualTo(-1)); // Alice filtered out
            Assert.That(_engine.FindSortedPosition(3), Is.EqualTo(-1)); // Dave filtered out
        }

        [Test]
        public void Initialize_WithFilteredIndices_FindSortedPosition_ReturnsCorrectPositionForVisible()
        {
            // Given — filtered to [0, 2]
            var filtered = new[] { 0, 2 };
            _engine.Initialize(filtered, _data.Count);

            // When / Then
            Assert.That(_engine.FindSortedPosition(0), Is.EqualTo(0)); // Charlie at position 0
            Assert.That(_engine.FindSortedPosition(2), Is.EqualTo(1)); // Bob at position 1
        }

        [Test]
        public void Sort_WithFilteredIndices_SortsOnlyFilteredRows()
        {
            // Given — filter to Charlie(0), Bob(2); sort ascending by Name
            var filtered = new[] { 0, 2 };
            _engine.Initialize(filtered, _data.Count);

            bool done = false;
            _engine.SortCompleted += (_, __) => done = true;

            // When
            _engine.Sort(_nameColumn, _data);
            SpinWait.SpinUntil(() => done, System.TimeSpan.FromSeconds(5));

            // Then — alphabetically Bob < Charlie, so sorted result is [2, 0]
            Assert.That(done, Is.True, "SortCompleted must fire");
            Assert.That(_engine.SortedIndices, Is.EqualTo(new[] { 2, 0 }));
        }

        [Test]
        public void Sort_WithFilteredIndices_FilteredOutRowsStillReturnMinusOne()
        {
            // Given
            var filtered = new[] { 0, 2 };
            _engine.Initialize(filtered, _data.Count);

            bool done = false;
            _engine.SortCompleted += (_, __) => done = true;
            _engine.Sort(_nameColumn, _data);
            SpinWait.SpinUntil(() => done, System.TimeSpan.FromSeconds(5));

            // When / Then
            Assert.That(_engine.FindSortedPosition(1), Is.EqualTo(-1)); // Alice still filtered out
            Assert.That(_engine.FindSortedPosition(3), Is.EqualTo(-1)); // Dave still filtered out
        }

        [Test]
        public void Initialize_EmptyFilteredSet_SortedIndicesIsEmpty()
        {
            // Given / When
            _engine.Initialize(new int[0], _data.Count);

            // Then
            Assert.That(_engine.SortedIndices, Is.Empty);
            Assert.That(_engine.FindSortedPosition(0), Is.EqualTo(-1));
        }
    }
}
