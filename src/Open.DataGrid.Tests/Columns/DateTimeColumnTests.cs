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
    /// Tests for OpenDataGridDateTimeColumn — display formatting via DisplayMode and DateFormat override,
    /// CanBeginEdit key filtering, ExtractEditValue parsing with clamping, and GetComparison ordering.
    /// STA required because DependencyObject + TextBox creation.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class DateTimeColumnTests
    {
        private OpenDataGridDateTimeColumn _column;
        private static readonly DateTime Sample = new DateTime(2024, 6, 15, 14, 30, 45);

        [SetUp]
        public void SetUp()
        {
            _column = new OpenDataGridDateTimeColumn { BindingPath = "DateTimeValue" };
        }

        // ── ApplyDisplayValue — DisplayMode presets ───────────────────────────

        [Test]
        public void ApplyDisplayValue_DateMode_ShowsDateOnly()
        {
            // Given: Column with default DisplayMode (Date).
            var tb = new TextBlock();

            // When: ApplyDisplayValue is called with a DateTime.
            _column.ApplyDisplayValue(tb, Sample);

            // Then: Text shows only the date part.
            Assert.That(tb.Text, Is.EqualTo("2024-06-15"));
        }

        [Test]
        public void ApplyDisplayValue_DateTimeMode_ShowsDateAndTime()
        {
            // Given: Column with DisplayMode set to DateTime.
            _column.DisplayMode = DateTimeDisplayMode.DateTime;
            var tb = new TextBlock();

            // When: ApplyDisplayValue is called with a DateTime.
            _column.ApplyDisplayValue(tb, Sample);

            // Then: Text shows both date and time.
            Assert.That(tb.Text, Is.EqualTo("2024-06-15 14:30:45"));
        }

        [Test]
        public void ApplyDisplayValue_TimeMode_ShowsTimeOnly()
        {
            // Given: Column with DisplayMode set to Time.
            _column.DisplayMode = DateTimeDisplayMode.Time;
            var tb = new TextBlock();

            // When: ApplyDisplayValue is called with a DateTime.
            _column.ApplyDisplayValue(tb, Sample);

            // Then: Text shows only the time part.
            Assert.That(tb.Text, Is.EqualTo("14:30:45"));
        }

        [Test]
        public void ApplyDisplayValue_ExplicitDateFormat_OverridesDisplayMode()
        {
            // Given: Column with DateFormat explicitly set (should override DisplayMode preset).
            // Single-quoting "/" prevents culture date-separator substitution.
            _column.DisplayMode = DateTimeDisplayMode.DateTime;
            _column.DateFormat = "dd'/'MM'/'yy";
            var tb = new TextBlock();

            // When: ApplyDisplayValue is called.
            _column.ApplyDisplayValue(tb, Sample);

            // Then: The explicit DateFormat is used, not the DisplayMode preset.
            Assert.That(tb.Text, Is.EqualTo("15/06/24"));
        }

        [Test]
        public void ApplyDisplayValue_NullableDateTime_ShowsFormattedValue()
        {
            // Given: A nullable DateTime value (boxed DateTime? with a value).
            var tb = new TextBlock();
            DateTime? nullable = Sample;

            // When: ApplyDisplayValue is called with the boxed nullable.
            _column.ApplyDisplayValue(tb, nullable);

            // Then: The value is formatted correctly (pattern matching handles DateTime?).
            Assert.That(tb.Text, Is.EqualTo("2024-06-15"));
        }

        [Test]
        public void ApplyDisplayValue_NullValue_ShowsEmptyString()
        {
            // Given: A null value (nullable with no value).
            var tb = new TextBlock();

            // When: ApplyDisplayValue is called with null.
            _column.ApplyDisplayValue(tb, null);

            // Then: Text is empty.
            Assert.That(tb.Text, Is.EqualTo(string.Empty));
        }

        // ── ApplyEditValue ────────────────────────────────────────────────────

        [Test]
        public void ApplyEditValue_DateTimeMode_SetsFullFormatInEditor()
        {
            // Given: Column in DateTime mode and a TextBox editor.
            _column.DisplayMode = DateTimeDisplayMode.DateTime;
            var tb = new TextBox();

            // When: ApplyEditValue is called.
            _column.ApplyEditValue(tb, Sample);

            // Then: Editor text shows date and time for user editing.
            Assert.That(tb.Text, Is.EqualTo("2024-06-15 14:30:45"));
        }

        // ── CanBeginEdit ──────────────────────────────────────────────────────

        [Test]
        [TestCase(Key.F2,    ExpectedResult = true,  TestName = "F2 triggers edit")]
        [TestCase(Key.Enter, ExpectedResult = true,  TestName = "Enter triggers edit")]
        [TestCase(Key.A,     ExpectedResult = false, TestName = "Letter A does not trigger edit")]
        [TestCase(Key.D1,    ExpectedResult = false, TestName = "Digit does not trigger edit")]
        public bool CanBeginEdit_Key_ReturnsExpected(Key key)
        {
            // Given: A DateTime column.
            // When: CanBeginEdit is called.
            // Then: Only F2 and Enter start edit mode — dates require deliberate editing.
            return _column.CanBeginEdit(key);
        }

        // ── ExtractEditValue ──────────────────────────────────────────────────

        [Test]
        public void ExtractEditValue_ValidDateString_ReturnsParsedDateTime()
        {
            // Given: A TextBox containing a parseable date string.
            var tb = new TextBox { Text = "2024-06-15" };

            // When: ExtractEditValue parses the editor text.
            var result = (DateTime)_column.ExtractEditValue(tb);

            // Then: The date is parsed correctly.
            Assert.That(result.Date, Is.EqualTo(new DateTime(2024, 6, 15)));
        }

        [Test]
        public void ExtractEditValue_DateAboveMax_ClampsToMaxDate()
        {
            // Given: Column with MaxDate set and a text value beyond it.
            _column.MaxDate = new DateTime(2020, 1, 1);
            var tb = new TextBox { Text = "2025-01-01" };

            // When: ExtractEditValue parses and clamps.
            var result = (DateTime)_column.ExtractEditValue(tb);

            // Then: Result is clamped to MaxDate.
            Assert.That(result, Is.EqualTo(new DateTime(2020, 1, 1)));
        }

        [Test]
        public void ExtractEditValue_DateBelowMin_ClampsToMinDate()
        {
            // Given: Column with MinDate set and a text value before it.
            _column.MinDate = new DateTime(2023, 1, 1);
            var tb = new TextBox { Text = "2022-06-01" };

            // When: ExtractEditValue parses and clamps.
            var result = (DateTime)_column.ExtractEditValue(tb);

            // Then: Result is clamped to MinDate.
            Assert.That(result, Is.EqualTo(new DateTime(2023, 1, 1)));
        }

        [Test]
        public void ExtractEditValue_InvalidText_ReturnsNoValue()
        {
            // Given: A TextBox with an unparseable string.
            var tb = new TextBox { Text = "not-a-date" };

            // When: ExtractEditValue is called.
            var result = _column.ExtractEditValue(tb);

            // Then: NoValue sentinel is returned.
            Assert.That(result, Is.SameAs(OpenDataGridColumn.NoValue));
        }

        // ── GetComparison ─────────────────────────────────────────────────────

        [Test]
        public void GetComparison_SortsAscendingByDateProperty()
        {
            // Given: Items with different DateTime values.
            var items = new List<SortItem>
            {
                new SortItem { DateTimeValue = new DateTime(2024, 3, 1) },
                new SortItem { DateTimeValue = new DateTime(2023, 1, 1) },
                new SortItem { DateTimeValue = new DateTime(2024, 1, 1) },
            };
            var cmp = _column.GetComparison();

            // When: Items are sorted.
            var sorted = items
                .OrderBy(x => (object)x, System.Collections.Generic.Comparer<object>.Create((a, b) => cmp(a, b)))
                .Cast<SortItem>().ToList();

            // Then: Items are in ascending chronological order.
            Assert.That(sorted[0].DateTimeValue.Year, Is.EqualTo(2023));
            Assert.That(sorted[1].DateTimeValue, Is.EqualTo(new DateTime(2024, 1, 1)));
            Assert.That(sorted[2].DateTimeValue, Is.EqualTo(new DateTime(2024, 3, 1)));
        }

        [Test]
        public void GetComparison_NullableDateTime_NullTreatedAsMinValue()
        {
            // Given: Items where one has a null DateTime? value.
            var withDate = new SortItem { NullableDateTimeValue = new DateTime(2024, 1, 1) };
            var withNull = new SortItem { NullableDateTimeValue = null };
            var col = new OpenDataGridDateTimeColumn { BindingPath = "NullableDateTimeValue" };
            var cmp = col.GetComparison();

            // When: Null is compared against a real date.
            var result = cmp(withNull, withDate);

            // Then: Null sorts before any real date (treated as DateTime.MinValue).
            Assert.That(result, Is.LessThan(0));
        }
    }
}
