using System.Globalization;
using System.Text;

namespace Open.DataGrid.Filtering
{
    internal static class DiacriticsHelper
    {
        internal static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;
                AppendAsciiEquivalent(builder, c);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static void AppendAsciiEquivalent(StringBuilder builder, char c)
        {
            switch (c)
            {
                case 'ą': builder.Append('a'); break;
                case 'Ą': builder.Append('A'); break;
                case 'ć': builder.Append('c'); break;
                case 'Ć': builder.Append('C'); break;
                case 'ę': builder.Append('e'); break;
                case 'Ę': builder.Append('E'); break;
                case 'ł': builder.Append('l'); break;
                case 'Ł': builder.Append('L'); break;
                case 'ń': builder.Append('n'); break;
                case 'Ń': builder.Append('N'); break;
                case 'ó': builder.Append('o'); break;
                case 'Ó': builder.Append('O'); break;
                case 'ś': builder.Append('s'); break;
                case 'Ś': builder.Append('S'); break;
                case 'ź':
                case 'ż':
                    builder.Append('z');
                    break;
                case 'Ź':
                case 'Ż':
                    builder.Append('Z');
                    break;
                case 'đ': builder.Append('d'); break;
                case 'Đ': builder.Append('D'); break;
                case 'ð': builder.Append('d'); break;
                case 'Ð': builder.Append('D'); break;
                case 'þ': builder.Append("th"); break;
                case 'Þ': builder.Append("Th"); break;
                case 'æ': builder.Append("ae"); break;
                case 'Æ': builder.Append("AE"); break;
                case 'œ': builder.Append("oe"); break;
                case 'Œ': builder.Append("OE"); break;
                case 'ß': builder.Append("ss"); break;
                default:
                    builder.Append(c);
                    break;
            }
        }
    }
}
