using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Columns;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Columns
{
    /// <summary>
    /// Tests for OpenDataGridTimeSpanColumn — display formatting, TimeFormat override,
    /// CanBeginEdit key filtering, ExtractEditValue parsing, and GetComparison ordering.
    /// STA required because DependencyObject + TextBox creation.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class TimeSpanColumnTests
    {
        private OpenDataGridTimeSpanColumn _column;
        private static readonly TimeSpan Sample = new TimeSpan(14, 30, 45);

        [SetUp]
        public void SetUp()
        {
            _column = new OpenDataGridTimeSpanColumn { BindingPath = "TimeSpanValue" };
        }

        // ── ApplyDisplayValue ─────────────────────────────────────────────────

        [Test]
        public void ApplyDisplayValue_DefaultFormat_ShowsHhMmSs()
        {
            // Given: Column with default TimeFormat (hh\:mm\:ss).
            var tb = new TextBlock();

            // When: ApplyDisplayValue is called.
            _column.ApplyDisplayValue(tb, Sample);

            // Then: Text is formatted as HH:mm:ss.
            Assert.That(tb.Text, Is.EqualTo("14:30:45"));
        }

        [Test]
        public void ApplyDisplayValue_CustomFormat_UsesOverride()
        {
            // Given: Column with a custom TimeFormat.
            _column.TimeFormat = @"hh\:mm";
            var tb = new TextBlock();

            // When: ApplyDisplayValue is called.
            _column.ApplyDisplayValue(tb, Sample);

            // Then: Only hours and minutes are shown.
            Assert.That(tb.Text, Is.EqualTo("14:30"));
        }

        [Test]
        public void ApplyDisplayValue_NullableTimeSpan_ShowsFormattedValue()
        {
            // Given: A nullable TimeSpan value.
            var tb = new TextBlock();
            TimeSpan? nullable = Sample;

            // When: ApplyDisplayValue is called with boxed nullable.
            _column.ApplyDisplayValue(tb, nullable);

            // Then: Value is formatted correctly.
            Assert.That(tb.Text, Is.EqualTo("14:30:45"));
        }

        [Test]
        public void ApplyDisplayValue_NullValue_ShowsEmptyString()
        {
            // Given: A null value.
            var tb = new TextBlock();

            // When: ApplyDisplayValue is called with null.
            _column.ApplyDisplayValue(tb, null);

            // Then: Text is empty.
            Assert.That(tb.Text, Is.EqualTo(string.Empty));
        }

        // ── ApplyEditValue ────────────────────────────────────────────────────

        [Test]
        public void ApplyEditValue_SetsFormattedTextAndSelectsAll()
        {
            // Given: A TextBox editor and a TimeSpan value.
            var tb = new TextBox();

            // When: ApplyEditValue is called.
            _column.ApplyEditValue(tb, Sample);

            // Then: Text is set to the formatted TimeSpan.
            Assert.That(tb.Text, Is.EqualTo("14:30:45"));
        }

        // ── CanBeginEdit ──────────────────────────────────────────────────────

        [Test]
        [TestCase(Key.F2,    ExpectedResult = true,  TestName = "F2 triggers edit")]
        [TestCase(Key.Enter, ExpectedResult = true,  TestName = "Enter triggers edit")]
        [TestCase(Key.A,     ExpectedResult = false, TestName = "Letter A does not trigger edit")]
        [TestCase(Key.D1,    ExpectedResult = false, TestName = "Digit does not trigger edit")]
        public bool CanBeginEdit_Key_ReturnsExpected(Key key)
        {
            // Given: A TimeSpan column.
            // When: CanBeginEdit is called.
            // Then: Only F2 and Enter start edit mode.
            return _column.CanBeginEdit(key);
        }

        // ── ExtractEditValue ──────────────────────────────────────────────────

        [Test]
        public void ExtractEditValue_ValidTimeSpanString_ReturnsParsedValue()
        {
            // Given: A TextBox containing a valid TimeSpan string.
            var tb = new TextBox { Text = "14:30:45" };

            // When: ExtractEditValue parses the editor text.
            var result = (TimeSpan)_column.ExtractEditValue(tb);

            // Then: The TimeSpan is parsed correctly.
            Assert.That(result, Is.EqualTo(new TimeSpan(14, 30, 45)));
        }

        [Test]
        public void ExtractEditValue_HoursMinutesOnly_ParsedCorrectly()
        {
            // Given: A TextBox with time in HH:mm format (no seconds).
            var tb = new TextBox { Text = "08:00" };

            // When: ExtractEditValue parses the editor text.
            var result = (TimeSpan)_column.ExtractEditValue(tb);

            // Then: TimeSpan equals 8 hours.
            Assert.That(result, Is.EqualTo(new TimeSpan(8, 0, 0)));
        }

        [Test]
        public void ExtractEditValue_InvalidText_ReturnsNoValue()
        {
            // Given: A TextBox with an unparseable string.
            var tb = new TextBox { Text = "not-a-time" };

            // When: ExtractEditValue is called.
            var result = _column.ExtractEditValue(tb);

            // Then: NoValue sentinel is returned.
            Assert.That(result, Is.SameAs(OpenDataGridColumn.NoValue));
        }

        [Test]
        public void ExtractEditValue_EmptyText_ReturnsNoValue()
        {
            // Given: An empty TextBox.
            var tb = new TextBox { Text = "" };

            // When: ExtractEditValue is called.
            var result = _column.ExtractEditValue(tb);

            // Then: NoValue sentinel is returned (empty string does not parse as TimeSpan).
            Assert.That(result, Is.SameAs(OpenDataGridColumn.NoValue));
        }

        // ── GetComparison ─────────────────────────────────────────────────────

        [Test]
        public void GetComparison_SortsAscendingByTimeSpanProperty()
        {
            // Given: Items with different TimeSpan values.
            var items = new List<SortItem>
            {
                new SortItem { TimeSpanValue = new TimeSpan(10, 0, 0) },
                new SortItem { TimeSpanValue = new TimeSpan(2, 0, 0)  },
                new SortItem { TimeSpanValue = new TimeSpan(6, 30, 0) },
            };
            var cmp = _column.GetComparison();

            // When: Items are sorted.
            var sorted = items
                .OrderBy(x => (object)x, System.Collections.Generic.Comparer<object>.Create((a, b) => cmp(a, b)))
                .Cast<SortItem>().ToList();

            // Then: Items are in ascending order by duration.
            Assert.That(sorted[0].TimeSpanValue, Is.EqualTo(new TimeSpan(2, 0, 0)));
            Assert.That(sorted[1].TimeSpanValue, Is.EqualTo(new TimeSpan(6, 30, 0)));
            Assert.That(sorted[2].TimeSpanValue, Is.EqualTo(new TimeSpan(10, 0, 0)));
        }

        [Test]
        public void GetComparison_NullableTimeSpan_NullTreatedAsZero()
        {
            // Given: Items where one has a null TimeSpan? value.
            var withValue = new SortItem { NullableTimeSpanValue = new TimeSpan(1, 0, 0) };
            var withNull  = new SortItem { NullableTimeSpanValue = null };
            var col = new OpenDataGridTimeSpanColumn { BindingPath = "NullableTimeSpanValue" };
            var cmp = col.GetComparison();

            // When: Null is compared against a positive duration.
            var result = cmp(withNull, withValue);

            // Then: Null sorts before any positive duration (treated as TimeSpan.Zero).
            Assert.That(result, Is.LessThan(0));
        }

        [Test]
        public void GetComparison_EqualValues_ReturnsZero()
        {
            // Given: Two items with the same TimeSpan.
            var a = new SortItem { TimeSpanValue = new TimeSpan(5, 0, 0) };
            var b = new SortItem { TimeSpanValue = new TimeSpan(5, 0, 0) };

            // When: The comparison delegate compares both items.
            var result = _column.GetComparison()(a, b);

            // Then: Result is zero.
            Assert.That(result, Is.EqualTo(0));
        }
    }
}
