using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using NUnit.Framework;
using Open.DataGrid.Controls;
using Open.DataGrid.Models;
using Open.DataGrid.Rendering;

namespace Open.DataGrid.Tests.Rendering
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ThemeTests
    {
        // ── Palette sanity ───────────────────────────────────────────────────────

        [Test]
        public void Palette_Dark_AllBrushesNonNull()
        {
            // Given / When
            var palette = ThemePalette.Dark;

            // Then — no field should be null (would cause NullReferenceException at runtime)
            Assert.That(palette.RowBackground,         Is.Not.Null, nameof(palette.RowBackground));
            Assert.That(palette.AlternateRowBackground, Is.Not.Null, nameof(palette.AlternateRowBackground));
            Assert.That(palette.Foreground,            Is.Not.Null, nameof(palette.Foreground));
            Assert.That(palette.HeaderBackground,      Is.Not.Null, nameof(palette.HeaderBackground));
            Assert.That(palette.HeaderForeground,      Is.Not.Null, nameof(palette.HeaderForeground));
            Assert.That(palette.ColumnSeparator,       Is.Not.Null, nameof(palette.ColumnSeparator));
            Assert.That(palette.GridLines,             Is.Not.Null, nameof(palette.GridLines));
            Assert.That(palette.SelectionBrush,        Is.Not.Null, nameof(palette.SelectionBrush));
            Assert.That(palette.ControlBackground,     Is.Not.Null, nameof(palette.ControlBackground));
            Assert.That(palette.ChromeBorder,          Is.Not.Null, nameof(palette.ChromeBorder));
            Assert.That(palette.HeaderAreaBackground,  Is.Not.Null, nameof(palette.HeaderAreaBackground));
            Assert.That(palette.FilterRowBackground,   Is.Not.Null, nameof(palette.FilterRowBackground));
            Assert.That(palette.LoadingOverlay,        Is.Not.Null, nameof(palette.LoadingOverlay));
            Assert.That(palette.DropIndicator,         Is.Not.Null, nameof(palette.DropIndicator));
            Assert.That(palette.ScrollBarTrack,        Is.Not.Null, nameof(palette.ScrollBarTrack));
            Assert.That(palette.ScrollBarThumb,        Is.Not.Null, nameof(palette.ScrollBarThumb));
            Assert.That(palette.ScrollBarThumbHover,   Is.Not.Null, nameof(palette.ScrollBarThumbHover));
            Assert.That(palette.ScrollBarThumbDrag,    Is.Not.Null, nameof(palette.ScrollBarThumbDrag));
        }

        [Test]
        public void Palette_Light_AllBrushesNonNull()
        {
            // Given / When
            var palette = ThemePalette.Light;

            // Then
            Assert.That(palette.RowBackground,         Is.Not.Null);
            Assert.That(palette.SelectionBrush,        Is.Not.Null);
            Assert.That(palette.HeaderAreaBackground,  Is.Not.Null);
            Assert.That(palette.FilterRowBackground,   Is.Not.Null);
            Assert.That(palette.LoadingOverlay,        Is.Not.Null);
        }

        [Test]
        public void Palette_AllBrushes_AreFrozen()
        {
            // Given / When
            var dark  = ThemePalette.Dark;
            var light = ThemePalette.Light;

            // Then — frozen brushes are thread-safe and zero-allocation
            Assert.That(dark.RowBackground.IsFrozen,    Is.True, "Dark RowBackground must be frozen");
            Assert.That(dark.SelectionBrush.IsFrozen,   Is.True, "Dark SelectionBrush must be frozen");
            Assert.That(light.RowBackground.IsFrozen,   Is.True, "Light RowBackground must be frozen");
            Assert.That(light.ChromeBorder.IsFrozen,    Is.True, "Light ChromeBorder must be frozen");
        }

        [Test]
        public void Palette_Dark_RowBackground_IsDark()
        {
            // Given
            var brush = ThemePalette.Dark.RowBackground as SolidColorBrush;

            // Then — row background must be dark (luminance well below mid-point)
            Assert.That(brush, Is.Not.Null, "Dark RowBackground must be a SolidColorBrush");
            double lum = Luminance(brush.Color);
            Assert.That(lum, Is.LessThan(80), $"Dark RowBackground luminance ({lum:F0}) should be below 80");
        }

        [Test]
        public void BrushShift_DarkBackground_ProducesLightForeground()
        {
            // Given — the dark row background
            var darkBg = ThemePalette.Dark.RowBackground;

            // When — auto-contrast foreground is computed (same logic as CellForegroundResolver uses)
            var fg = BrushShift.ComputeForegroundBrush(darkBg) as SolidColorBrush;

            // Then — foreground should be light (high luminance)
            Assert.That(fg, Is.Not.Null);
            double lum = Luminance(fg.Color);
            Assert.That(lum, Is.GreaterThan(150), $"Foreground luminance ({lum:F0}) should be above 150 for dark background");
        }

        // ── Theme DP behaviour ───────────────────────────────────────────────────

        [Test]
        public void Theme_Default_IsLight()
        {
            // Given / When
            var grid = new OpenDataGrid();

            // Then
            Assert.That(grid.Theme, Is.EqualTo(GridTheme.Light));
        }

        [Test]
        public void Theme_SetToDark_AppliesDarkRowBackground()
        {
            // Given
            var grid = new OpenDataGrid();
            var expectedBg = ThemePalette.Dark.RowBackground;

            // When
            grid.Theme = GridTheme.Dark;

            // Then
            Assert.That(grid.RowBackground, Is.SameAs(expectedBg));
        }

        [Test]
        public void Theme_SetToDark_AppliesDarkHeaderBackground()
        {
            // Given
            var grid = new OpenDataGrid();

            // When
            grid.Theme = GridTheme.Dark;

            // Then
            Assert.That(grid.HeaderBackground, Is.SameAs(ThemePalette.Dark.HeaderBackground));
        }

        [Test]
        public void Theme_SetToDark_AppliesDarkGridLines()
        {
            // Given
            var grid = new OpenDataGrid();

            // When
            grid.Theme = GridTheme.Dark;

            // Then
            Assert.That(grid.GridLinesBrush, Is.SameAs(ThemePalette.Dark.GridLines));
        }

        [Test]
        public void Theme_RevertToLight_ClearsThemeDPs()
        {
            // Given — start dark, record what RowBackground default is
            var grid = new OpenDataGrid();
            grid.Theme = GridTheme.Dark;
            Assert.That(grid.RowBackground, Is.SameAs(ThemePalette.Dark.RowBackground));

            // When
            grid.Theme = GridTheme.Light;

            // Then — RowBackground should revert to its metadata default (White)
            Assert.That(grid.RowBackground, Is.SameAs(Brushes.White),
                "RowBackground should revert to White when Theme returns to Light");
        }

        [Test]
        public void Theme_Dark_DoesNotOverrideUserExplicitRowBackground()
        {
            // Given — user sets RowBackground explicitly before the theme is applied
            var grid = new OpenDataGrid();
            grid.RowBackground = Brushes.Pink;  // explicit user value

            // When
            grid.Theme = GridTheme.Dark;

            // Then — explicit user value wins
            Assert.That(grid.RowBackground, Is.SameAs(Brushes.Pink),
                "Theme must not overwrite an explicit user-set RowBackground");
        }

        [Test]
        public void Theme_Dark_DoesNotOverrideUserExplicitHeaderBackground()
        {
            // Given
            var grid = new OpenDataGrid();
            var userBrush = new SolidColorBrush(Colors.Purple);
            grid.HeaderBackground = userBrush;

            // When
            grid.Theme = GridTheme.Dark;

            // Then
            Assert.That(grid.HeaderBackground, Is.SameAs(userBrush),
                "Theme must not overwrite an explicit user-set HeaderBackground");
        }

        [Test]
        public void Theme_UserOverride_SurvivesLightToDarkAndBack()
        {
            // Given
            var grid = new OpenDataGrid();
            grid.RowBackground = Brushes.HotPink;  // user sets before first theme change

            // When
            grid.Theme = GridTheme.Dark;
            grid.Theme = GridTheme.Light;

            // Then — user override persists across theme round-trips
            Assert.That(grid.RowBackground, Is.SameAs(Brushes.HotPink));
        }

        [Test]
        public void Theme_System_ResolvesViaDelegateInjection()
        {
            // Given — inject a stub that reports dark
            var grid = new OpenDataGrid();
            grid._isDarkSystemTheme = () => true;

            // When
            grid.Theme = GridTheme.System;

            // Then — dark palette values should be applied
            Assert.That(grid.RowBackground, Is.SameAs(ThemePalette.Dark.RowBackground),
                "System theme with IsDark()=true should apply dark palette");
        }

        [Test]
        public void Theme_System_Light_ResolvesViaDelegateInjection()
        {
            // Given — inject a stub that reports light
            var grid = new OpenDataGrid();
            grid._isDarkSystemTheme = () => false;

            // When
            grid.Theme = GridTheme.System;

            // Then — no dark overrides; RowBackground stays at its default
            Assert.That(grid.RowBackground, Is.SameAs(Brushes.White),
                "System theme with IsDark()=false should leave RowBackground as default White");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static double Luminance(Color c)
            => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
    }
}
