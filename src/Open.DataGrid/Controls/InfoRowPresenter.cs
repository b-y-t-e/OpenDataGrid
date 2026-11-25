using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Open.DataGrid.Rendering;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// Visual of an info row (accent bar + one line of text). Owns the info-row look so that
    /// <see cref="OpenDataGridRow"/> only switches between cell mode and this presenter.
    /// </summary>
    internal sealed class InfoRowPresenter : Border
    {
        private const double Indent = 14;
        private const double SelectionBlendAmount = 0.35;

        private readonly TextBlock _text;
        private InfoRowStyle _style;

        public InfoRowPresenter()
        {
            _text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                TextWrapping      = TextWrapping.NoWrap,
            };
            BorderThickness = new Thickness(3, 0, 0, 0);
            Padding         = new Thickness(8, 0, 6, 0);
            Margin          = new Thickness(Indent, 3, 0, 3);
            Child           = _text;
            Visibility      = Visibility.Collapsed;
        }

        public InfoRowStyle InfoStyle
        {
            get => _style;
            set
            {
                _style           = value;
                BorderBrush      = value?.AccentBrush;
                _text.Foreground = value?.Foreground;
                _text.FontStyle  = value?.FontStyle ?? FontStyles.Italic;
            }
        }

        public bool ScrollsHorizontally => _style?.ScrollsHorizontally == true;

        public void SetText(string text)
        {
            _text.Text = text ?? string.Empty;
            ToolTip    = string.IsNullOrEmpty(text) ? null : text;
        }

        public void ApplyFont(FontFamily fontFamily, double? fontSize)
        {
            if (fontFamily != null) _text.FontFamily = fontFamily;
            else _text.ClearValue(TextBlock.FontFamilyProperty);
            // Slightly smaller than cell text — subordinate to the data row it annotates.
            double baseSize = fontSize ?? SystemFonts.MessageFontSize;
            _text.FontSize = Math.Max(8, baseSize - 1);
        }

        /// <summary>Row background: configured info background or the owner's, lightly tinted when the owner is selected.</summary>
        public Brush ResolveBackground(Brush ownerBackground, bool isOwnerSelected, Brush selectionBrush)
        {
            var baseBg = _style?.Background ?? ownerBackground ?? Brushes.White;
            return isOwnerSelected && selectionBrush != null
                ? BrushShift.Blend(baseBg, selectionBrush, SelectionBlendAmount)
                : baseBg;
        }

        /// <summary>
        /// Lays the presenter out inside a row. Pinned (default) to the viewport's left edge; with
        /// <see cref="InfoRowStyle.ScrollsHorizontally"/> anchored to the column origin and moved by the h-scroll.
        /// </summary>
        public void ArrangeInRow(Size rowSize, double hOffset, double totalColumnsWidth)
        {
            double x = ScrollsHorizontally ? -hOffset : 0;
            Arrange(new Rect(x, 0, GetWidth(rowSize.Width, hOffset, totalColumnsWidth), rowSize.Height));
        }

        public void MeasureInRow(Size rowSize, double hOffset, double totalColumnsWidth)
            => Measure(new Size(GetWidth(rowSize.Width, hOffset, totalColumnsWidth), rowSize.Height));

        private double GetWidth(double viewportWidth, double hOffset, double totalColumnsWidth)
            => ScrollsHorizontally ? Math.Max(totalColumnsWidth, viewportWidth + hOffset) : viewportWidth;
    }
}
