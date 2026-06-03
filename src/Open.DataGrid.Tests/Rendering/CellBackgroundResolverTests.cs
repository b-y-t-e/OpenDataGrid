using System.Threading;
using System.Windows.Media;
using Open.DataGrid.Rendering;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Rendering
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class CellBackgroundResolverTests
    {
        // Brushes used as test sentinels
        private static readonly Brush Focus      = Brushes.Gold;
        private static readonly Brush Selection  = Brushes.Blue;
        private static readonly Brush ColumnBg   = Brushes.LightBlue;
        private static readonly Brush DataBrush  = Brushes.Red;
        private static readonly Brush RowBg      = Brushes.LightGray;
        private static readonly Brush AltColBg   = Brushes.DarkBlue; // pre-computed alternate for column

        /// <summary>Helper: builds a context with all layers given, defaults IsAlternateRow=false, shift=0.</summary>
        private static CellBackgroundContext Full(
            bool isCellFocused   = false,
            bool isRowSelected   = false,
            bool overridesData   = true,
            bool isAlternateRow  = false,
            double shift         = 0.0,
            Brush colAlt         = null)
            => new CellBackgroundContext(
                focusCellBrush:          Focus,
                selectionBrush:          Selection,
                columnBackground:        ColumnBg,
                backgroundOverridesData: overridesData,
                dataCellBrush:           DataBrush,
                rowResolvedBackground:   RowBg,
                isRowSelected:           isRowSelected,
                isCellFocused:           isCellFocused,
                isAlternateRow:          isAlternateRow,
                alternateRowShift:       shift,
                columnAlternateBackground: colAlt);

        // --------------- Layer 1: Focused cell wins over everything ---------------

        [Test]
        public void FocusedCell_Wins_OverAllOtherLayers()
        {
            // Given — all layers set, cell is focused
            var ctx = Full(isCellFocused: true, isRowSelected: true);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then
            Assert.That(result, Is.SameAs(Focus));
        }

        [Test]
        public void FocusedCell_NullFocusBrush_FallsThrough_ToSelection()
        {
            // Given — focused but no focus brush
            var ctx = new CellBackgroundContext(
                focusCellBrush: null, selectionBrush: Selection,
                columnBackground: null, backgroundOverridesData: true,
                dataCellBrush: null, rowResolvedBackground: null,
                isRowSelected: true, isCellFocused: true,
                isAlternateRow: false, alternateRowShift: 0.0,
                columnAlternateBackground: null);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then — falls through to selection (layer 2)
            Assert.That(result, Is.SameAs(Selection));
        }

        // --------------- Layer 2: Row selection wins over column/data ---------------

        [Test]
        public void SelectedRow_Wins_OverColumnBackground_WhenOverridesTrue()
        {
            // Given — row selected, column overrides
            var ctx = Full(isRowSelected: true, overridesData: true);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then
            Assert.That(result, Is.SameAs(Selection));
        }

        [Test]
        public void SelectedRow_Wins_OverDataBrush()
        {
            // Given — row selected, no column bg
            var ctx = new CellBackgroundContext(
                focusCellBrush: null, selectionBrush: Selection,
                columnBackground: null, backgroundOverridesData: true,
                dataCellBrush: DataBrush, rowResolvedBackground: RowBg,
                isRowSelected: true, isCellFocused: false,
                isAlternateRow: false, alternateRowShift: 0.0,
                columnAlternateBackground: null);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then
            Assert.That(result, Is.SameAs(Selection));
        }

        [Test]
        public void SelectedRow_NullSelectionBrush_FallsThrough_ToColumnBg()
        {
            // Given — row selected but no selection brush
            var ctx = new CellBackgroundContext(
                focusCellBrush: null, selectionBrush: null,
                columnBackground: ColumnBg, backgroundOverridesData: true,
                dataCellBrush: DataBrush, rowResolvedBackground: RowBg,
                isRowSelected: true, isCellFocused: false,
                isAlternateRow: false, alternateRowShift: 0.0,
                columnAlternateBackground: null);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then — falls through to column bg (layer 3)
            Assert.That(result, Is.SameAs(ColumnBg));
        }

        // --------------- Layer 3: Column.Background (BackgroundOverridesData=true) ---------------

        [Test]
        public void ColumnBackground_Overrides_DataBrush_WhenOverridesTrue()
        {
            // Given — not focused, not selected, column overrides
            var ctx = Full(overridesData: true);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then
            Assert.That(result, Is.SameAs(ColumnBg));
        }

        // --------------- Layer 4: Data brush ---------------

        [Test]
        public void DataBrush_Overrides_ColumnBackground_WhenOverridesFalse()
        {
            // Given — BackgroundOverridesData = false
            var ctx = Full(overridesData: false);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then — data wins
            Assert.That(result, Is.SameAs(DataBrush));
        }

        // --------------- Layer 5: Column.Background as fallback (BackgroundOverridesData=false) ---------------

        [Test]
        public void ColumnBackground_Fallback_WhenDataBrushNull_AndOverridesFalse()
        {
            // Given — BackgroundOverridesData=false, no data brush
            var ctx = new CellBackgroundContext(
                focusCellBrush: null, selectionBrush: Selection,
                columnBackground: ColumnBg, backgroundOverridesData: false,
                dataCellBrush: null, rowResolvedBackground: RowBg,
                isRowSelected: false, isCellFocused: false,
                isAlternateRow: false, alternateRowShift: 0.0,
                columnAlternateBackground: null);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then — column used as fallback
            Assert.That(result, Is.SameAs(ColumnBg));
        }

        // --------------- Layer 6: Row resolved background ---------------

        [Test]
        public void NoLayers_ReturnsRowResolvedBackground()
        {
            // Given — no column bg, no data brush
            var ctx = new CellBackgroundContext(
                focusCellBrush: null, selectionBrush: Selection,
                columnBackground: null, backgroundOverridesData: true,
                dataCellBrush: null, rowResolvedBackground: RowBg,
                isRowSelected: false, isCellFocused: false,
                isAlternateRow: false, alternateRowShift: 0.0,
                columnAlternateBackground: null);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then
            Assert.That(result, Is.SameAs(RowBg));
        }

        [Test]
        public void AllNull_ReturnsNull()
        {
            // Given — everything null / false
            var ctx = new CellBackgroundContext(
                focusCellBrush: null, selectionBrush: null,
                columnBackground: null, backgroundOverridesData: true,
                dataCellBrush: null, rowResolvedBackground: null,
                isRowSelected: false, isCellFocused: false,
                isAlternateRow: false, alternateRowShift: 0.0,
                columnAlternateBackground: null);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then
            Assert.That(result, Is.Null);
        }

        // --------------- Alternate row shift ---------------

        [Test]
        public void AlternateRow_UsesCachedAlternate_ForColumnBackground()
        {
            // Given — alternate row with pre-computed column alternate
            var ctx = Full(overridesData: true, isAlternateRow: true, shift: 0.1, colAlt: AltColBg);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then — uses cached alternate (zero-alloc path)
            Assert.That(result, Is.SameAs(AltColBg));
        }

        [Test]
        public void AlternateRow_ShiftsDataBrush_AdHoc()
        {
            // Given — alternate row, data brush, no column bg
            var dataBrush = new SolidColorBrush(Colors.White);
            var ctx = new CellBackgroundContext(
                focusCellBrush: null, selectionBrush: null,
                columnBackground: null, backgroundOverridesData: false,
                dataCellBrush: dataBrush, rowResolvedBackground: null,
                isRowSelected: false, isCellFocused: false,
                isAlternateRow: true, alternateRowShift: 0.1,
                columnAlternateBackground: null);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx) as SolidColorBrush;

            // Then — a new shifted brush, not identical to source
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Not.SameAs(dataBrush));
            Assert.That(result.Color, Is.Not.EqualTo(Colors.White)); // shifted away from white
        }

        [Test]
        public void AlternateRow_DoesNotShift_SelectionBrush()
        {
            // Given — row selected AND alternate
            var ctx = Full(isRowSelected: true, isAlternateRow: true, shift: 0.5);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then — selection brush returned unshifted
            Assert.That(result, Is.SameAs(Selection));
        }

        [Test]
        public void AlternateRow_DoesNotShift_FocusedBrush()
        {
            // Given — focused AND alternate
            var ctx = Full(isCellFocused: true, isAlternateRow: true, shift: 0.5);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then — focus brush returned unshifted
            Assert.That(result, Is.SameAs(Focus));
        }

        [Test]
        public void ColumnBackground_OnNonAlternateRow_ReturnsRaw()
        {
            // Given — NOT an alternate row, column alt provided (should be ignored)
            var ctx = Full(overridesData: true, isAlternateRow: false, shift: 0.1, colAlt: AltColBg);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then — raw column bg, not the alternate
            Assert.That(result, Is.SameAs(ColumnBg));
        }

        [Test]
        public void Shift_Zero_SkipsAlternate_ReturnsRawColumnBg()
        {
            // Given — alternate row BUT shift == 0
            var ctx = Full(overridesData: true, isAlternateRow: true, shift: 0.0, colAlt: AltColBg);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx);

            // Then — no shift applied, raw column bg returned
            Assert.That(result, Is.SameAs(ColumnBg));
        }

        [Test]
        public void DataBrush_WithShift_NoColumnBackground()
        {
            // Given — data brush present, no column bg, alternate row with shift
            var dataBrush = new SolidColorBrush(Colors.Red);
            var ctx = new CellBackgroundContext(
                focusCellBrush: null, selectionBrush: null,
                columnBackground: null, backgroundOverridesData: false,
                dataCellBrush: dataBrush, rowResolvedBackground: null,
                isRowSelected: false, isCellFocused: false,
                isAlternateRow: true, alternateRowShift: 0.1,
                columnAlternateBackground: null);

            // When
            var result = CellBackgroundResolver.Resolve(in ctx) as SolidColorBrush;

            // Then — shifted red
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Not.SameAs(dataBrush));
            Assert.That(result.Color, Is.Not.EqualTo(Colors.Red));
        }
    }
}
