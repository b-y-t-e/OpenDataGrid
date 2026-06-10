using System.Windows.Media;

namespace Open.DataGrid.Rendering
{
    /// <summary>
    /// Frozen brush palette for a single theme.
    /// All brushes are frozen on construction — safe to share across threads and zero-allocation on repeated access.
    /// </summary>
    public sealed class ThemePalette
    {
        // ── Row area ─────────────────────────────────────────────────────────────────
        public readonly Brush RowBackground;
        public readonly Brush AlternateRowBackground;

        // ── Text ─────────────────────────────────────────────────────────────────────
        public readonly Brush Foreground;

        // ── Header / filter cells (painted by HeaderPresenter / FilterRowPresenter) ──
        public readonly Brush HeaderBackground;
        public readonly Brush HeaderForeground;
        public readonly Brush ColumnSeparator;

        // ── Grid lines ───────────────────────────────────────────────────────────────
        public readonly Brush GridLines;

        // ── Selection ────────────────────────────────────────────────────────────────
        public readonly Brush SelectionBrush;

        // ── Outer chrome (Control.Background, Control.BorderBrush) ──────────────────
        public readonly Brush ControlBackground;
        public readonly Brush ChromeBorder;

        // ── Template-binding chrome DPs ───────────────────────────────────────────────
        public readonly Brush HeaderAreaBackground;   // header strip behind header cells
        public readonly Brush FilterRowBackground;    // filter strip background
        public readonly Brush LoadingOverlay;         // semi-transparent overlay during load

        // ── Drop indicator (column reorder) ──────────────────────────────────────────
        public readonly Brush DropIndicator;

        // ── ScrollBar ────────────────────────────────────────────────────────────────
        public readonly Brush ScrollBarTrack;
        public readonly Brush ScrollBarThumb;
        public readonly Brush ScrollBarThumbHover;
        public readonly Brush ScrollBarThumbDrag;

        private ThemePalette(
            Color rowBg,
            Color altRowBg,
            Color foreground,
            Color headerBg,
            Color headerFg,
            Color columnSep,
            Color gridLines,
            Color selection,
            Color controlBg,
            Color chromeBorder,
            Color headerAreaBg,
            Color filterRowBg,
            Color loadingOverlay,
            Color dropIndicator,
            Color scrollTrack,
            Color scrollThumb,
            Color scrollThumbHover,
            Color scrollThumbDrag)
        {
            RowBackground         = Freeze(rowBg);
            AlternateRowBackground = Freeze(altRowBg);
            Foreground            = Freeze(foreground);
            HeaderBackground      = Freeze(headerBg);
            HeaderForeground      = Freeze(headerFg);
            ColumnSeparator       = Freeze(columnSep);
            GridLines             = Freeze(gridLines);
            SelectionBrush        = Freeze(selection);
            ControlBackground     = Freeze(controlBg);
            ChromeBorder          = Freeze(chromeBorder);
            HeaderAreaBackground  = Freeze(headerAreaBg);
            FilterRowBackground   = Freeze(filterRowBg);
            LoadingOverlay        = Freeze(loadingOverlay);
            DropIndicator         = Freeze(dropIndicator);
            ScrollBarTrack        = Freeze(scrollTrack);
            ScrollBarThumb        = Freeze(scrollThumb);
            ScrollBarThumbHover   = Freeze(scrollThumbHover);
            ScrollBarThumbDrag    = Freeze(scrollThumbDrag);
        }

        private static SolidColorBrush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static Color Hex(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
        private static Color HexA(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

        // ── Light palette ─────────────────────────────────────────────────────────────
        // Exactly matches today's hardcoded defaults — no visual change in Light mode.
        public static readonly ThemePalette Light = new ThemePalette(
            rowBg:            Hex(0xFF, 0xFF, 0xFF),  // White
            altRowBg:         Hex(0xFF, 0xFF, 0xFF),  // same (grid applies AlternateRowShift on top)
            foreground:       Hex(0x00, 0x00, 0x00),  // Black (WPF default)
            headerBg:         Hex(0xF0, 0xF0, 0xF0),  // light gray
            headerFg:         Hex(0x00, 0x00, 0x00),  // Black
            columnSep:        Hex(0xCC, 0xCC, 0xCC),  // #CCCCCC
            gridLines:        Hex(0x77, 0x77, 0x77),  // #777777
            selection:        Hex(0x33, 0x99, 0xFF),  // Windows accent-like blue
            controlBg:        Hex(0xFF, 0xFF, 0xFF),  // White
            chromeBorder:     Hex(0xCC, 0xCC, 0xCC),  // #CCCCCC
            headerAreaBg:     Hex(0xF0, 0xF0, 0xF0),  // #F0F0F0
            filterRowBg:      Hex(0xFA, 0xFA, 0xFA),  // #FAFAFA
            loadingOverlay:   HexA(0x30, 0xFF, 0xFF, 0xFF), // #30FFFFFF
            dropIndicator:    Hex(0x1E, 0x90, 0xFF),  // DodgerBlue
            scrollTrack:      Hex(0xF0, 0xF0, 0xF0),
            scrollThumb:      Hex(0xCD, 0xCD, 0xCD),
            scrollThumbHover: Hex(0xA6, 0xA6, 0xA6),
            scrollThumbDrag:  Hex(0x60, 0x60, 0x60)
        );

        // ── Dark palette ──────────────────────────────────────────────────────────────
        // VS Code-inspired: raised dark surfaces, off-white text, blue selection, no pure black/white.
        public static readonly ThemePalette Dark = new ThemePalette(
            rowBg:            Hex(0x1E, 0x1E, 0x1E),  // VS Code editor bg
            altRowBg:         Hex(0x25, 0x25, 0x26),  // slightly elevated
            foreground:       Hex(0xE0, 0xE0, 0xE0),  // off-white
            headerBg:         Hex(0x2D, 0x2D, 0x30),  // VS Code title bar
            headerFg:         Hex(0xF0, 0xF0, 0xF0),  // near-white
            columnSep:        Hex(0x3F, 0x3F, 0x46),  // VS Code panel borders
            gridLines:        Hex(0x3F, 0x3F, 0x46),
            selection:        Hex(0x09, 0x47, 0x71),  // VS Code selection blue
            controlBg:        Hex(0x1E, 0x1E, 0x1E),
            chromeBorder:     Hex(0x3F, 0x3F, 0x46),
            headerAreaBg:     Hex(0x2D, 0x2D, 0x30),
            filterRowBg:      Hex(0x25, 0x25, 0x26),
            loadingOverlay:   HexA(0x40, 0x00, 0x00, 0x00), // #40000000 semi-black
            dropIndicator:    Hex(0x37, 0x94, 0xFF),  // VS Code accent blue
            scrollTrack:      Hex(0x1E, 0x1E, 0x1E),  // same as row bg — blends in
            scrollThumb:      Hex(0x4D, 0x4D, 0x4D),
            scrollThumbHover: Hex(0x5A, 0x5A, 0x5A),
            scrollThumbDrag:  Hex(0x7A, 0x7A, 0x7A)
        );
    }
}
