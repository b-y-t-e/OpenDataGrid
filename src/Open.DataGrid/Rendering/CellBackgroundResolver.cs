using System.Windows.Media;

namespace Open.DataGrid.Rendering
{
    /// <summary>
    /// Centralized, pure function for resolving the final cell background brush.
    ///
    /// Priority hierarchy (highest to lowest):
    ///   1. Focused cell brush    — when IsCellFocused
    ///   1a. Range brush          — when the cell lies inside a selected cell range
    ///   2. Row SelectionBrush   — when IsRowSelected
    ///   3. Column.Background    — when set and BackgroundOverridesData == true
    ///   4. CellBackgroundPath   — data-bound brush per cell
    ///   5. Column.Background    — fallback when BackgroundOverridesData == false
    ///   6. Row resolved bg      — result of rowColorResolver (alternate/selector/path/default)
    ///
    /// Layers 3–5 are additionally shifted by AlternateRowShift when IsAlternateRow and shift > 0:
    ///   - Column.Background uses the pre-computed ColumnAlternateBackground (zero allocation on hot path).
    ///   - DataCellBrush is shifted ad-hoc via BrushShift.Apply (rare path).
    /// </summary>
    internal static class CellBackgroundResolver
    {
        /// <summary>Resolves the final background brush for one cell given all background layers.</summary>
        public static Brush Resolve(in CellBackgroundContext ctx)
        {
            // Layer 1: focused cell wins over everything
            if (ctx.IsCellFocused && ctx.FocusCellBrush != null)
                return ctx.FocusCellBrush;

            // Layer 1a: selected cell range wins over the row selection and everything below
            if (ctx.IsInRange && ctx.RangeBrush != null)
                return ctx.RangeBrush;

            // Layer 2: row selection wins over column/data backgrounds
            if (ctx.IsRowSelected && ctx.SelectionBrush != null)
                return ctx.SelectionBrush;

            // Layers 3–5: resolve cell-level brush (column vs data)
            var cellLayer = ResolveCellLayer(in ctx);
            if (cellLayer != null)
                return cellLayer;

            // Layer 6: row resolved background (already contains alternate shift for row-level colors)
            return ctx.RowResolvedBackground;
        }

        private static Brush ResolveCellLayer(in CellBackgroundContext ctx)
        {
            Brush baseBg;
            bool basedOnColumn;

            if (ctx.ColumnBackground != null && ctx.BackgroundOverridesData)
            {
                baseBg        = ctx.ColumnBackground;
                basedOnColumn = true;
            }
            else if (ctx.DataCellBrush != null)
            {
                baseBg        = ctx.DataCellBrush;
                basedOnColumn = false;
            }
            else if (ctx.ColumnBackground != null)
            {
                baseBg        = ctx.ColumnBackground;
                basedOnColumn = true;
            }
            else
            {
                return null;
            }

            // Apply alternate-row shift when applicable
            if (ctx.IsAlternateRow && ctx.AlternateRowShift > 0.0)
            {
                if (basedOnColumn && ctx.ColumnAlternateBackground != null)
                    return ctx.ColumnAlternateBackground; // zero-alloc: pre-computed cached brush

                return BrushShift.Apply(baseBg, ctx.AlternateRowShift); // ad-hoc for data brush
            }

            return baseBg;
        }
    }
}
