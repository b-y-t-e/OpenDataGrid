using System.Threading;
using System.Windows;
using System.Windows.Media;
using Open.DataGrid.Rendering;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Rendering
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class BrushShiftTests
    {
        // --------------- Apply ---------------

        [Test]
        public void Apply_LightColor_Darkens()
        {
            // Given
            var white = new SolidColorBrush(Colors.White);

            // When
            var result = BrushShift.Apply(white, 0.2) as SolidColorBrush;

            // Then — all channels must be lower than 255 (darker)
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Color.R, Is.LessThan(255));
            Assert.That(result.Color.G, Is.LessThan(255));
            Assert.That(result.Color.B, Is.LessThan(255));
        }

        [Test]
        public void Apply_DarkColor_Lightens()
        {
            // Given
            var black = new SolidColorBrush(Colors.Black);

            // When
            var result = BrushShift.Apply(black, 0.2) as SolidColorBrush;

            // Then — all channels must be higher than 0 (lighter)
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Color.R, Is.GreaterThan(0));
            Assert.That(result.Color.G, Is.GreaterThan(0));
            Assert.That(result.Color.B, Is.GreaterThan(0));
        }

        [Test]
        public void Apply_NonSolidBrush_ReturnsSourceUnchanged()
        {
            // Given
            var gradient = new LinearGradientBrush();

            // When
            var result = BrushShift.Apply(gradient, 0.2);

            // Then
            Assert.That(result, Is.SameAs(gradient));
        }

        [Test]
        public void Apply_FreezesResult()
        {
            // Given
            var brush = new SolidColorBrush(Colors.CornflowerBlue);

            // When
            var result = BrushShift.Apply(brush, 0.1);

            // Then
            Assert.That(result.IsFrozen, Is.True);
        }

        [Test]
        public void Apply_SameInputTwice_ProducesIdenticalColors()
        {
            // Given
            var brush = new SolidColorBrush(Colors.SteelBlue);

            // When
            var r1 = BrushShift.Apply(brush, 0.15) as SolidColorBrush;
            var r2 = BrushShift.Apply(brush, 0.15) as SolidColorBrush;

            // Then
            Assert.That(r1.Color, Is.EqualTo(r2.Color));
        }

        [Test]
        public void Apply_ZeroShift_ProducesSameColor()
        {
            // Given
            var brush = new SolidColorBrush(Colors.CornflowerBlue);

            // When
            var result = BrushShift.Apply(brush, 0.0) as SolidColorBrush;

            // Then — kolor identyczny z oryginałem (shift=0 nie zmienia składowych)
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Color, Is.EqualTo(brush.Color));
        }

        // --------------- ComputeFocusBrush ---------------

        [Test]
        public void ComputeFocusBrush_LightSelectionBrush_Darkens()
        {
            // Given — a light blue brush
            var light = new SolidColorBrush(Color.FromRgb(200, 220, 255));

            // When
            var result = BrushShift.ComputeFocusBrush(light);

            // Then — focus brush should be darker (each channel <= original)
            Assert.That(result.Color.R, Is.LessThanOrEqualTo(200));
            Assert.That(result.Color.G, Is.LessThanOrEqualTo(220));
            Assert.That(result.Color.B, Is.LessThanOrEqualTo(255));
        }

        [Test]
        public void ComputeFocusBrush_DarkSelectionBrush_Lightens()
        {
            // Given — a dark brush
            var dark = new SolidColorBrush(Color.FromRgb(20, 30, 50));

            // When
            var result = BrushShift.ComputeFocusBrush(dark);

            // Then — focus brush should be lighter (each channel >= original)
            Assert.That(result.Color.R, Is.GreaterThanOrEqualTo(20));
            Assert.That(result.Color.G, Is.GreaterThanOrEqualTo(30));
            Assert.That(result.Color.B, Is.GreaterThanOrEqualTo(50));
        }

        [Test]
        public void ComputeFocusBrush_FreezesResult()
        {
            // Given
            var brush = new SolidColorBrush(SystemColors.HighlightColor);

            // When
            var result = BrushShift.ComputeFocusBrush(brush);

            // Then
            Assert.That(result.IsFrozen, Is.True);
        }
    }
}
