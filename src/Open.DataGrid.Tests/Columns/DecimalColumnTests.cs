using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using Open.DataGrid.Columns;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Columns
{
    /// <summary>
    /// Tests for OpenDataGridDecimalColumn — CanBeginEdit key filtering,
    /// ExtractEditValue parsing with clamping, and GetComparison ordering.
    /// STA required because DependencyObject + TextBox creation.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class DecimalColumnTests
    {
        private OpenDataGridDecimalColumn _column;

        [SetUp]
        public void SetUp()
        {
            _column = new OpenDataGridDecimalColumn
            {
                BindingPath = "DecimalValue",
                DecimalPlaces = 2,
                Minimum = 0m,
                Maximum = 999.99m
            };
        }

        // ── CanBeginEdit ──────────────────────────────────────────────────────

        [Test]
        [TestCase(System.Windows.Input.Key.D0,        ExpectedResult = true,  TestName = "Digit 0 triggers edit")]
        [TestCase(System.Windows.Input.Key.D9,        ExpectedResult = true,  TestName = "Digit 9 triggers edit")]
        [TestCase(System.Windows.Input.Key.NumPad5,   ExpectedResult = true,  TestName = "NumPad 5 triggers edit")]
        [TestCase(System.Windows.Input.Key.OemMinus,  ExpectedResult = true,  TestName = "Minus triggers edit")]
        [TestCase(System.Windows.Input.Key.Subtract,  ExpectedResult = true,  TestName = "NumPad minus triggers edit")]
        [TestCase(System.Windows.Input.Key.Decimal,   ExpectedResult = true,  TestName = "NumPad decimal triggers edit")]
        [TestCase(System.Windows.Input.Key.OemPeriod, ExpectedResult = true,  TestName = "Period triggers edit")]
        [TestCase(System.Windows.Input.Key.OemComma,  ExpectedResult = true,  TestName = "Comma triggers edit")]
        [TestCase(System.Windows.Input.Key.F2,        ExpectedResult = true,  TestName = "F2 triggers edit")]
        [TestCase(System.Windows.Input.Key.Enter,     ExpectedResult = true,  TestName = "Enter triggers edit")]
        [TestCase(System.Windows.Input.Key.A,         ExpectedResult = false, TestName = "Letter A does not trigger edit")]
        [TestCase(System.Windows.Input.Key.Space,     ExpectedResult = false, TestName = "Space does not trigger edit")]
        [TestCase(System.Windows.Input.Key.F5,        ExpectedResult = false, TestName = "F5 does not trigger edit")]
        public bool CanBeginEdit_Key_ReturnsExpected(System.Windows.Input.Key key)
        {
            // Given: A decimal column configured for editing.
            // When: CanBeginEdit is called with a specific key.
            // Then: The method returns whether the key should start edit mode.
            return _column.CanBeginEdit(key);
        }

        // ── ExtractEditValue ──────────────────────────────────────────────────

        [Test]
        public void ExtractEditValue_ValidDecimalText_ReturnsParsedValue()
        {
            // Given: TextBox text with a culture-specific decimal separator.
            var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            var tb = new TextBox { Text = $"12{sep}50" };

            // When: ExtractEditValue parses the editor text.
            var result = (decimal)_column.ExtractEditValue(tb);

            // Then: The parsed decimal value is returned.
            Assert.That(result, Is.EqualTo(12.50m));
        }

        [Test]
        public void ExtractEditValue_ValueAboveMaximum_ClampsToMaximum()
        {
            // Given: TextBox text representing a value above column maximum.
            var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            var tb = new TextBox { Text = $"9999{sep}99" };

            // When: ExtractEditValue parses and clamps the value.
            var result = (decimal)_column.ExtractEditValue(tb);

            // Then: The result is clamped to Maximum.
            Assert.That(result, Is.EqualTo(999.99m));
        }

        [Test]
        public void ExtractEditValue_ValueBelowMinimum_ClampsToMinimum()
        {
            // Given: TextBox text representing a value below column minimum.
            var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            var tb = new TextBox { Text = $"-50{sep}00" };

            // When: ExtractEditValue parses and clamps the value.
            var result = (decimal)_column.ExtractEditValue(tb);

            // Then: The result is clamped to Minimum.
            Assert.That(result, Is.EqualTo(0m)); // minimum = 0
        }

        [Test]
        public void ExtractEditValue_InvalidText_ReturnsNoValue()
        {
            // Given: TextBox text that is not a valid decimal value.
            var tb = new TextBox { Text = "abc" };

            // When: ExtractEditValue is called.
            var result = _column.ExtractEditValue(tb);

            // Then: NoValue sentinel is returned — invalid input is not committed to the model.
            Assert.That(result, Is.SameAs(OpenDataGridColumn.NoValue));
        }

        [Test]
        public void ExtractEditValue_EmptyText_ReturnsClamped0()
        {
            // Given: An empty TextBox input.
            var tb = new TextBox { Text = "" };

            // When: ExtractEditValue is called.
            var result = _column.ExtractEditValue(tb);

            // Then: 0 is returned (clamped to [Minimum, Maximum]), same behaviour as IntegerColumn.
            Assert.That(result, Is.EqualTo(0m));
        }

        [Test]
        public void ExtractEditValue_TextWithThousandsSeparator_ParsedCorrectly()
        {
            // Given: A value formatted in the current culture (including separators).
            _column.Maximum = 9999m;
            var text = (1000.50m).ToString("F2", CultureInfo.CurrentCulture);
            var tb = new TextBox { Text = text };

            // When: ExtractEditValue parses the formatted text.
            var result = (decimal?)_column.ExtractEditValue(tb);

            // Then: The parsed value is not null and equals the expected decimal.
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(1000.50m));
        }

        // ── GetClearedValue ───────────────────────────────────────────────────

        [Test]
        public void GetClearedValue_WithinRange_ReturnsZeroDecimal()
        {
            // Given: A column whose range includes 0 (Minimum = 0).
            // When: The cleared value is requested.
            var result = _column.GetClearedValue();

            // Then: 0m is returned (typed as decimal, matching the property type).
            Assert.That(result, Is.EqualTo(0m));
            Assert.That(result, Is.TypeOf<decimal>());
        }

        [Test]
        public void GetClearedValue_MinimumAboveZero_ClampsToMinimum()
        {
            // Given: A column whose Minimum is above zero.
            _column.Minimum = 5m;

            // When: The cleared value is requested.
            var result = (decimal)_column.GetClearedValue();

            // Then: 0 is clamped up to Minimum.
            Assert.That(result, Is.EqualTo(5m));
        }

        // ── GetComparison ─────────────────────────────────────────────────────

        [Test]
        public void GetComparison_SortsAscendingByDecimalProperty()
        {
            // Given: Unsorted items with decimal values.
            var items = new List<SortItem>
            {
                new SortItem { DecimalValue = 30.5m },
                new SortItem { DecimalValue = 10.1m },
                new SortItem { DecimalValue = 20.9m }
            };
            var cmp = _column.GetComparison();

            // When: Items are sorted using the column comparison.
            var sorted = items.OrderBy(x => (object)x, System.Collections.Generic.Comparer<object>.Create((a, b) => cmp(a, b)))
                              .Cast<SortItem>()
                              .ToList();

            // Then: Items are ordered ascending by decimal property.
            Assert.That(sorted[0].DecimalValue, Is.EqualTo(10.1m));
            Assert.That(sorted[1].DecimalValue, Is.EqualTo(20.9m));
            Assert.That(sorted[2].DecimalValue, Is.EqualTo(30.5m));
        }

        [Test]
        public void GetComparison_EqualValues_ReturnsZero()
        {
            // Given: Two items with equal decimal values.
            var a = new SortItem { DecimalValue = 7.77m };
            var b = new SortItem { DecimalValue = 7.77m };

            // When: The comparison delegate compares both items.
            var result = _column.GetComparison()(a, b);

            // Then: Result equals zero for equal values.
            Assert.That(result, Is.EqualTo(0));
        }
    }
}
