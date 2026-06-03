using System.Threading;
using Open.DataGrid.Filtering;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Filtering
{
    /// <summary>
    /// Tests for <see cref="DiacriticsHelper.RemoveDiacritics"/>.
    ///
    /// The helper is used by <c>OpenDataGridTextFilter</c> to normalise both the search
    /// query and cell values, enabling accent-insensitive search (e.g. "lodowka" finds
    /// "lodówka"). Tests cover the Polish diacritic set, Western-European ligatures,
    /// edge cases (null, empty, ASCII-only) and case preservation.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class DiacriticsHelperTests
    {
        // ── Polish lowercase ──────────────────────────────────────────────────

        [TestCase("ą", "a", TestName = "ą → a")]
        [TestCase("ć", "c", TestName = "ć → c")]
        [TestCase("ę", "e", TestName = "ę → e")]
        [TestCase("ł", "l", TestName = "ł → l")]
        [TestCase("ń", "n", TestName = "ń → n")]
        [TestCase("ó", "o", TestName = "ó → o")]
        [TestCase("ś", "s", TestName = "ś → s")]
        [TestCase("ź", "z", TestName = "ź → z")]
        [TestCase("ż", "z", TestName = "ż → z")]
        public void RemoveDiacritics_PolishLowercase_ReturnsMappedAscii(string input, string expected)
        {
            // Given: single Polish character.
            // When
            var result = DiacriticsHelper.RemoveDiacritics(input);
            // Then
            Assert.That(result, Is.EqualTo(expected));
        }

        // ── Polish uppercase ──────────────────────────────────────────────────

        [TestCase("Ą", "A", TestName = "Ą → A")]
        [TestCase("Ć", "C", TestName = "Ć → C")]
        [TestCase("Ę", "E", TestName = "Ę → E")]
        [TestCase("Ł", "L", TestName = "Ł → L")]
        [TestCase("Ń", "N", TestName = "Ń → N")]
        [TestCase("Ó", "O", TestName = "Ó → O")]
        [TestCase("Ś", "S", TestName = "Ś → S")]
        [TestCase("Ź", "Z", TestName = "Ź → Z")]
        [TestCase("Ż", "Z", TestName = "Ż → Z")]
        public void RemoveDiacritics_PolishUppercase_ReturnsMappedAscii(string input, string expected)
        {
            // Given: single uppercase Polish character.
            // When
            var result = DiacriticsHelper.RemoveDiacritics(input);
            // Then
            Assert.That(result, Is.EqualTo(expected));
        }

        // ── Western-European ligatures and special chars ──────────────────────

        [TestCase("æ",  "ae",  TestName = "æ → ae")]
        [TestCase("Æ",  "AE",  TestName = "Æ → AE")]
        [TestCase("œ",  "oe",  TestName = "œ → oe")]
        [TestCase("Œ",  "OE",  TestName = "Œ → OE")]
        [TestCase("ß",  "ss",  TestName = "ß → ss")]
        [TestCase("þ",  "th",  TestName = "þ → th")]
        [TestCase("Þ",  "Th",  TestName = "Þ → Th")]
        [TestCase("đ",  "d",   TestName = "đ → d")]
        [TestCase("Đ",  "D",   TestName = "Đ → D")]
        [TestCase("ð",  "d",   TestName = "ð → d")]
        [TestCase("Ð",  "D",   TestName = "Ð → D")]
        public void RemoveDiacritics_WesternSpecialChars_ReturnsMappedAscii(string input, string expected)
        {
            // Given: a Western-European special character.
            // When
            var result = DiacriticsHelper.RemoveDiacritics(input);
            // Then
            Assert.That(result, Is.EqualTo(expected));
        }

        // ── Word-level tests ──────────────────────────────────────────────────

        [Test]
        public void RemoveDiacritics_MixedPolishWord_ReturnsAsciiEquivalent()
        {
            // Given: typical Polish word used in filter searches.
            // When
            var result = DiacriticsHelper.RemoveDiacritics("lodówka");
            // Then
            Assert.That(result, Is.EqualTo("lodowka"));
        }

        [Test]
        public void RemoveDiacritics_UppercaseWord_PreservesCase()
        {
            // Given: uppercase Polish word.
            // When
            var result = DiacriticsHelper.RemoveDiacritics("ŁÓDŹ");
            // Then
            Assert.That(result, Is.EqualTo("LODZ"));
        }

        [Test]
        public void RemoveDiacritics_MixedCase_PreservesCase()
        {
            // Given: mixed-case string with diacritics.
            // When
            var result = DiacriticsHelper.RemoveDiacritics("Żółta Łódź");
            // Then
            Assert.That(result, Is.EqualTo("Zolta Lodz"));
        }

        // ── Edge cases ────────────────────────────────────────────────────────

        [Test]
        public void RemoveDiacritics_AsciiOnlyInput_ReturnsUnchanged()
        {
            // Given: plain ASCII text — nothing to normalise.
            // When
            var result = DiacriticsHelper.RemoveDiacritics("hello world");
            // Then
            Assert.That(result, Is.EqualTo("hello world"));
        }

        [Test]
        public void RemoveDiacritics_EmptyString_ReturnsEmpty()
        {
            // Given: empty string.
            // When
            var result = DiacriticsHelper.RemoveDiacritics(string.Empty);
            // Then
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void RemoveDiacritics_NullInput_ReturnsNull()
        {
            // Given: null (guard for callers that may pass null before text is typed).
            // When
            var result = DiacriticsHelper.RemoveDiacritics(null);
            // Then
            Assert.That(result, Is.Null);
        }

        [Test]
        public void RemoveDiacritics_AccentedVowels_AreMappedAndRestStraysUnchanged()
        {
            // Given: string mixing accented chars with digits and punctuation.
            // When
            var result = DiacriticsHelper.RemoveDiacritics("café 42!");
            // Then: only the accented 'é' is normalised; digits and punctuation survive.
            Assert.That(result, Is.EqualTo("cafe 42!"));
        }
    }
}
