using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Accessors;

namespace Open.DataGrid.Columns
{
    /// <summary>
    /// Column that shows a CheckBox representing whether the row is in <see cref="Controls.OpenDataGrid.CheckedItems"/>.
    ///
    /// Two modes:
    ///   - No <see cref="OpenDataGridColumn.BindingPath"/>: checked state comes from <c>CheckedItems</c> only.
    ///     <c>CheckedItems</c> must be non-null for toggle to work.
    ///   - With <c>BindingPath</c> (bool property): the CheckBox visual reads the property value.
    ///     Toggle writes the property directly. When <c>CheckedItems</c> is also set, toggle keeps both
    ///     in sync (dual-source mode). When <c>CheckedItems</c> is null, only the model field is written
    ///     (pure-bound mode) — row-level checked styling is not available in that case.
    ///
    /// The column is always display-only — <see cref="CanBeginEdit"/> always returns <c>false</c>.
    /// Toggle happens via <see cref="Controls.OpenDataGrid.ToggleCheckedKey"/> (disabled by default — set to e.g. <c>Space</c> to enable) or LPM click.
    ///
    /// <para>
    /// <b>Bound-mode contract — TWO SOURCES OF TRUTH.</b>
    /// In bound mode the CheckBox visual is driven by the model property (<c>BindingPath</c>) while
    /// row-level checked styling (<c>CheckedRowForeground</c>/<c>Background</c>/<c>FontWeight</c>) and
    /// <see cref="Controls.OpenDataGrid.MarkedItems"/> are driven by <c>CheckedItems</c> / the
    /// internal <c>_checkedSet</c>. The grid does <b>not</b> auto-synchronize between them — sync
    /// happens only when toggling through <see cref="Controls.OpenDataGrid.ToggleChecked"/>
    /// (mouse click / configured key).
    /// </para>
    /// <para>
    /// External mutations cause visible inconsistency:
    /// setting <c>data.BoolValue = true</c> directly flips the CheckBox but leaves the row
    /// unstyled and excluded from <c>MarkedItems</c>. Adding to <c>CheckedItems</c> directly
    /// styles the row but the CheckBox stays unchecked (re-read from the unchanged property).
    /// </para>
    /// <para>
    /// VM contract: when mixing bound mode with VM-driven mutations, the VM must update
    /// <c>data.BoolValue</c> AND <c>CheckedItems</c> together. To avoid this burden, prefer
    /// either bound mode <em>without</em> VM mutating <c>CheckedItems</c> directly, or unbound
    /// mode (no <c>BindingPath</c>) where <c>CheckedItems</c> is the only source of truth.
    /// </para>
    /// </summary>
    public class OpenDataGridCheckColumn : OpenDataGridColumn
    {
        private Controls.OpenDataGrid _owningGrid;

        static OpenDataGridCheckColumn()
        {
            HorizontalContentAlignmentProperty.OverrideMetadata(
                typeof(OpenDataGridCheckColumn),
                new PropertyMetadata(HorizontalAlignment.Center));
        }

        /// <summary>Called by the grid during initialization to provide access to CheckedItems state.</summary>
        internal void AttachGrid(Controls.OpenDataGrid grid) => _owningGrid = grid;

        public override FrameworkElement CreateDisplayElement()
        {
            return new CheckBox
            {
                Style = InternalStyles.CheckBox,
                HorizontalAlignment = HorizontalContentAlignment,
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
                IsHitTestVisible = false
            };
        }

        public override FrameworkElement CreateEditElement() => null;

        public override void ApplyDisplayValue(FrameworkElement element, object value)
        {
            if (!(element is CheckBox cb)) return;

            if (!string.IsNullOrEmpty(BindingPath))
                cb.IsChecked = ToBool(value);
            else
                cb.IsChecked = _owningGrid?.IsItemChecked(value) ?? false;
        }

        internal static bool ToBool(object value)
        {
            if (value is bool b) return b;
            var nb = value as bool?;
            if (nb != null) return nb.Value;
            return false;
        }

        public override void ApplyEditValue(FrameworkElement element, object value) { }

        public override object ExtractEditValue(FrameworkElement element) => OpenDataGridColumn.NoValue;

        public override bool CanBeginEdit(Key key) => false;

        public override Comparison<object> GetComparison()
        {
            var path = BindingPath;
            if (!string.IsNullOrEmpty(path))
            {
                var getter = PropertyAccessorCache.CreateGetter(path);
                return (a, b) =>
                {
                    bool ba = ToBool(getter(a));
                    bool bb = ToBool(getter(b));
                    return ba.CompareTo(bb);
                };
            }

            var grid = _owningGrid;
            if (grid != null)
                return (a, b) => grid.IsItemChecked(a).CompareTo(grid.IsItemChecked(b));

            return (a, b) => 0;
        }

        protected override Freezable CreateInstanceCore() => new OpenDataGridCheckColumn();
    }
}
