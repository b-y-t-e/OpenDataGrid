using System.Threading;
using System.Windows.Media;
using Open.DataGrid.Rendering;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Rendering
{
    /// <summary>
    /// Tests for <see cref="CellForegroundResolver.Resolve"/>.
    ///
    /// Priority hierarchy:
    ///   1. Highlighted (selected/focused) → auto-contrast brush derived from the resolved background
    ///   2. Normal → Column.Foreground (always wins) ?? Grid.Foreground with contrast check ?? null
    ///
    /// Symmetric counterpart to <see cref="CellBackgroundResolverTests"/>.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class CellForegroundResolverTests
    {
        private static readonly Brush ColumnFg = Brushes.DarkGreen;
        private static readonly Brush GridFg   = Brushes.DimGray;

        // ── Highlighted cell — auto-contrast ─────────────────────────────────

        [Test]
        public void Resolve_HighlightedCell_DarkBackground_ReturnsLightForeground()
        {
            // Given: highlighted cell with a dark background (black → luminance 0).
            var darkBg = Brushes.Black;

            // When
            var result = CellForegroundResolver.Resolve(darkBg, isHighlighted: true, ColumnFg, GridFg);

            // Then: contrast foreground for dark bg must be lighter than the bg itself.
            var fg = ((SolidColorBrush)result).Color;
            Assert.That(fg.R, Is.GreaterThan(darkBg.Color.R), "R channel should be lighter");
            Assert.That(fg.G, Is.GreaterThan(darkBg.Color.G), "G channel should be lighter");
            Assert.That(fg.B, Is.GreaterThan(darkBg.Color.B), "B channel should be lighter");
        }

        [Test]
        public void Resolve_HighlightedCell_LightBackground_ReturnsDarkForeground()
        {
            // Given: highlighted cell with a light background (white → luminance 255).
            var lightBg = Brushes.White;

            // When
            var result = CellForegroundResolver.Resolve(lightBg, isHighlighted: true, ColumnFg, GridFg);

            // Then: contrast foreground for light bg must be darker than the bg itself.
            var fg = ((SolidColorBrush)result).Color;
            Assert.That(fg.R, Is.LessThan(lightBg.Color.R), "R channel should be darker");
            Assert.That(fg.G, Is.LessThan(lightBg.Color.G), "G channel should be darker");
            Assert.That(fg.B, Is.LessThan(lightBg.Color.B), "B channel should be darker");
        }

        [Test]
        public void Resolve_HighlightedCell_NullBackground_ReturnsNonNullBrush()
        {
            // Given: highlighted cell, null background (non-SolidColorBrush or missing).
            // BrushShift falls back to Colors.White → produces a darkened foreground.

            // When
            var result = CellForegroundResolver.Resolve(
                resolvedBackground: null,
                isHighlighted: true,
                columnForeground: null,
                gridForeground: null);

            // Then: even without a background brush, a non-null foreground brush is returned.
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void Resolve_HighlightedCell_IgnoresColumnAndGridForeground()
        {
            // Given: highlighted cell. Column and grid foregrounds are set but must not be used.
            var darkBg = new SolidColorBrush(Color.FromRgb(20, 20, 20));

            // When
            var result = CellForegroundResolver.Resolve(darkBg, isHighlighted: true, ColumnFg, GridFg);

            // Then: result is NOT one of the configured foreground brushes — it is derived from bg.
            Assert.That(result, Is.Not.SameAs(ColumnFg));
            Assert.That(result, Is.Not.SameAs(GridFg));
        }

        // ── Normal cell — Column.Foreground ?? Grid.Foreground ?? null ────────

        [Test]
        public void Resolve_NormalCell_ColumnForegroundSet_ReturnsColumnForeground()
        {
            // Given: non-highlighted cell with both column and grid foreground set.

            // When
            var result = CellForegroundResolver.Resolve(
                resolvedBackground: Brushes.White,
                isHighlighted: false,
                columnForeground: ColumnFg,
                gridForeground: GridFg);

            // Then: column foreground takes priority over grid foreground.
            Assert.That(result, Is.SameAs(ColumnFg));
        }

        [Test]
        public void Resolve_NormalCell_NoColumnForeground_ReturnsGridForeground()
        {
            // Given: non-highlighted cell without a column-level foreground override.

            // When
            var result = CellForegroundResolver.Resolve(
                resolvedBackground: Brushes.White,
                isHighlighted: false,
                columnForeground: null,
                gridForeground: GridFg);

            // Then: grid-level foreground is used as fallback.
            Assert.That(result, Is.SameAs(GridFg));
        }

        [Test]
        public void Resolve_NormalCell_NoForegroundsConfigured_ReturnsNull()
        {
            // Given: non-highlighted cell with neither column nor grid foreground set.

            // When
            var result = CellForegroundResolver.Resolve(
                resolvedBackground: Brushes.White,
                isHighlighted: false,
                columnForeground: null,
                gridForeground: null);

            // Then: null is returned so the caller can ClearValue(ForegroundProperty)
            // and let WPF's default inheritance apply.
            Assert.That(result, Is.Null);
        }

        // ── Auto-contrast in normal cell — dark mode light-background rows ─────

        [Test]
        public void Resolve_NormalCell_LightGridFg_LightBackground_AutoContrasts()
        {
            // Given: dark mode — grid foreground is light (#E0E0E0, lum ~224) and
            // the row has a light background (White, lum 255). Both sides > 120 → poor contrast.
            var lightGridFg = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            var lightBg     = Brushes.White;

            // When
            var result = CellForegroundResolver.Resolve(lightBg, isHighlighted: false, null, lightGridFg);

            // Then: auto-contrast is applied — result must be darker than lightGridFg (not the same brush).
            Assert.That(result, Is.Not.SameAs(lightGridFg), "Should auto-contrast, not return gridFg directly");
            var fg = ((SolidColorBrush)result).Color;
            Assert.That(fg.R, Is.LessThan(lightBg.Color.R), "R should be darker than the light background");
        }

        [Test]
        public void Resolve_NormalCell_LightGridFg_DarkBackground_UsesGridFg()
        {
            // Given: dark mode — grid foreground is light (#E0E0E0, lum ~224) and
            // the row has a dark background (#1E1E1E, lum ~30). Opposite sides → good contrast.
            var lightGridFg = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            var darkBg      = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

            // When
            var result = CellForegroundResolver.Resolve(darkBg, isHighlighted: false, null, lightGridFg);

            // Then: gridFg is returned as-is — light text on dark bg is already readable.
            Assert.That(result, Is.SameAs(lightGridFg));
        }

        [Test]
        public void Resolve_NormalCell_ColumnFgSet_AlwaysReturnsColumnFg()
        {
            // Given: column foreground is set, even though gridFg+bg would cause poor contrast.
            var lightGridFg = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            var lightBg     = Brushes.White;
            var columnFg    = Brushes.DarkRed;

            // When
            var result = CellForegroundResolver.Resolve(lightBg, isHighlighted: false, columnFg, lightGridFg);

            // Then: explicit column foreground always wins, no auto-contrast override.
            Assert.That(result, Is.SameAs(columnFg));
        }
    }
}
