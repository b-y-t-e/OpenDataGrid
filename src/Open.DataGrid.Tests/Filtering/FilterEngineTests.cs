using System.Collections.Generic;
using Open.DataGrid.Filtering;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Filtering
{
    [TestFixture]
    public class FilterEngineTests
    {
        private FilterEngine _engine;

        [SetUp]
        public void SetUp()
        {
            _engine = new FilterEngine();
        }

        // --------------- HasActiveFilters ---------------

        [Test]
        public void HasActiveFilters_WhenEmpty_ReturnsFalse()
        {
            // Given / When / Then
            Assert.That(_engine.HasActiveFilters, Is.False);
        }

        [Test]
        public void HasActiveFilters_AfterRegister_ReturnsTrue()
        {
            // Given
            _engine.RegisterFilter("key1", _ => true);

            // When / Then
            Assert.That(_engine.HasActiveFilters, Is.True);
        }

        [Test]
        public void HasActiveFilters_AfterUnregister_ReturnsFalse()
        {
            // Given
            _engine.RegisterFilter("key1", _ => true);

            // When
            _engine.UnregisterFilter("key1");

            // Then
            Assert.That(_engine.HasActiveFilters, Is.False);
        }

        // --------------- Apply — basic ---------------

        [Test]
        public void Apply_NoFilters_ReturnsAllIndices()
        {
            // Given
            var data = new List<Row> { Row.Make("a"), Row.Make("b"), Row.Make("c") };

            // When
            int[] result = _engine.Apply(data);

            // Then
            Assert.That(result, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void Apply_SingleFilter_ReturnMatchingIndices()
        {
            // Given
            var data = new List<Row> { Row.Make("samsung"), Row.Make("apple"), Row.Make("samsung tv") };
            _engine.RegisterFilter("f1", item => ((Row)item).Name.Contains("samsung"));

            // When
            int[] result = _engine.Apply(data);

            // Then
            Assert.That(result, Is.EqualTo(new[] { 0, 2 }));
        }

        [Test]
        public void Apply_SingleFilter_NoMatches_ReturnsEmpty()
        {
            // Given
            var data = new List<Row> { Row.Make("apple"), Row.Make("nokia") };
            _engine.RegisterFilter("f1", item => ((Row)item).Name.Contains("samsung"));

            // When
            int[] result = _engine.Apply(data);

            // Then
            Assert.That(result, Is.Empty);
        }

        // --------------- AND logic across terms ---------------

        [Test]
        public void Apply_MultipleTerms_AllMustMatch()
        {
            // Given — "samsung lodówka" split by space means item must contain BOTH words
            var data = new List<Row>
            {
                Row.Make("samsung lodówka"),   // 0 — matches both
                Row.Make("samsung pralka"),     // 1 — only samsung
                Row.Make("lodówka LG"),         // 2 — only lodówka
                Row.Make("nokia"),              // 3 — neither
            };
            _engine.RegisterFilter("f1", item =>
            {
                var name = ((Row)item).Name.ToLowerInvariant();
                return name.Contains("samsung") && name.Contains("lodówka");
            });

            // When
            int[] result = _engine.Apply(data);

            // Then
            Assert.That(result, Is.EqualTo(new[] { 0 }));
        }

        // --------------- AND logic across multiple filters ---------------

        [Test]
        public void Apply_MultipleFilters_AndCombined()
        {
            // Given
            var data = new List<Row>
            {
                Row.MakeFull(name: "samsung",  category: "lodówka"),  // 0 — passes both
                Row.MakeFull(name: "samsung",  category: "pralka"),   // 1 — only f1
                Row.MakeFull(name: "nokia",    category: "lodówka"),  // 2 — only f2
                Row.MakeFull(name: "nokia",    category: "tv"),       // 3 — neither
            };
            _engine.RegisterFilter("f1", item => ((Row)item).Name.Contains("samsung"));
            _engine.RegisterFilter("f2", item => ((Row)item).Category.Contains("lodówka"));

            // When
            int[] result = _engine.Apply(data);

            // Then
            Assert.That(result, Is.EqualTo(new[] { 0 }));
        }

        // --------------- Null data source ---------------

        [Test]
        public void Apply_NullDataSource_ReturnsEmpty()
        {
            // Given
            _engine.RegisterFilter("f1", _ => true);

            // When
            int[] result = _engine.Apply(null);

            // Then
            Assert.That(result, Is.Empty);
        }

        // --------------- Helper types ---------------

        private class Row
        {
            public string Name     { get; set; }
            public string Category { get; set; }

            public static Row Make(string name) => new Row { Name = name, Category = string.Empty };
            public static Row MakeFull(string name, string category) => new Row { Name = name, Category = category };
        }
    }
}
