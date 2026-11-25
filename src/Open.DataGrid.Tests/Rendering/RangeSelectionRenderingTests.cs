using System.Windows.Media;
using NUnit.Framework;
using Open.DataGrid.Models;
using Open.DataGrid.Rendering;

namespace Open.DataGrid.Tests.Rendering
{
    /// <summary><see cref="CellRange"/> model and the range layer of the cell colour resolvers.</summary>
    [TestFixture]
    public class RangeSelectionRenderingTests
    {
        private static readonly Brush Focus     = Brushes.DarkBlue;
        private static readonly Brush Range     = Brushes.LightBlue;
        private static readonly Brush Selection = Brushes.Blue;
        private static readonly Brush Column    = Brushes.Yellow;
        private static readonly Brush Row       = Brushes.White;

        private static CellBackgroundContext Ctx(bool focused, bool inRange, bool rowSelected, Brush rangeBrush = null)
            => new CellBackgroundContext(
                focusCellBrush: focused ? Focus : null,
                selectionBrush: Selection,
                columnBackground: Column,
                backgroundOverridesData: true,
                dataCellBrush: null,
                rowResolvedBackground: Row,
                isRowSelected: rowSelected,
                isCellFocused: focused,
                isAlternateRow: false,
                alternateRowShift: 0,
                columnAlternateBackground: null,
                isInRange: inRange,
                rangeBrush: rangeBrush ?? Range);

        [Test]
        public void CellRange_NormalizesCorners()
        {
            // Given / When
            var range = new CellRange(5, 3, 1, 0);

            // Then
            Assert.That(range.FirstRow, Is.EqualTo(1));
            Assert.That(range.LastRow, Is.EqualTo(5));
            Assert.That(range.FirstColumn, Is.EqualTo(0));
            Assert.That(range.LastColumn, Is.EqualTo(3));
            Assert.That(range.RowCount, Is.EqualTo(5));
            Assert.That(range.ColumnCount, Is.EqualTo(4));
        }

        [TestCase(1, 0, true)]
        [TestCase(5, 3, true)]
        [TestCase(3, 2, true)]
        [TestCase(0, 0, false)]
        [TestCase(6, 3, false)]
        [TestCase(3, 4, false)]
        public void CellRange_Contains_IsInclusive(int row, int col, bool expected)
        {
            // Given
            var range = new CellRange(1, 0, 5, 3);

            // When
            bool result = range.Contains(row, col);

            // Then
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void CellRange_EqualityAndSingleCell()
        {
            // Given / When
            var a = new CellRange(2, 2, 2, 2);
            var b = new CellRange(2, 2, 2, 2);

            // Then
            Assert.That(a == b, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.IsSingleCell, Is.True);
            Assert.That(new CellRange(2, 2, 3, 2).IsSingleCell, Is.False);
        }

        [Test]
        public void Resolver_FocusedCell_WinsOverRange()
        {
            // Given
            var ctx = Ctx(focused: true, inRange: true, rowSelected: true);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then
            Assert.That(result, Is.SameAs(Focus));
        }

        [Test]
        public void Resolver_Range_WinsOverRowSelection_AndColumnBackground()
        {
            // Given
            var selectedRow = Ctx(focused: false, inRange: true, rowSelected: true);
            var plainRow    = Ctx(focused: false, inRange: true, rowSelected: false);

            // When / Then
            Assert.That(CellBackgroundResolver.Resolve(in selectedRow), Is.SameAs(Range));
            Assert.That(CellBackgroundResolver.Resolve(in plainRow), Is.SameAs(Range));
        }

        [Test]
        public void Resolver_OutsideTheRange_IsUnchanged()
        {
            // Given
            var ctx = Ctx(focused: false, inRange: false, rowSelected: true);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then
            Assert.That(result, Is.SameAs(Selection));
        }

        [Test]
        public void ForegroundOfARangeCell_ContrastsWithTheRangeBrush()
        {
            // Given: a dark and a light range brush
            var dark  = new SolidColorBrush(Color.FromRgb(0x10, 0x30, 0x60));
            var light = new SolidColorBrush(Color.FromRgb(0xCC, 0xE4, 0xF7));

            // When
            var onDark  = (SolidColorBrush)CellForegroundResolver.Resolve(dark,  isHighlighted: true, columnForeground: Brushes.Red, gridForeground: null);
            var onLight = (SolidColorBrush)CellForegroundResolver.Resolve(light, isHighlighted: true, columnForeground: Brushes.Red, gridForeground: null);

            // Then
            double Lum(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            Assert.That(Lum(onDark.Color), Is.GreaterThan(Lum(dark.Color)));
            Assert.That(Lum(onLight.Color), Is.LessThan(Lum(light.Color)));
        }
    }
}
