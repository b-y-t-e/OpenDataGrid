using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Accessors;

namespace Open.DataGrid.Columns
{
    /// <summary>
    /// Abstract base for numeric columns (Decimal, Integer).
    /// Provides shared TextBlock/TextBox creation, edit lifecycle, and sorting pattern.
    /// Subclasses implement type-specific formatting, validation, parsing, and clamping.
    /// </summary>
    public abstract class OpenDataGridNumericColumnBase : OpenDataGridColumn
    {
        static OpenDataGridNumericColumnBase()
        {
            HorizontalContentAlignmentProperty.OverrideMetadata(
                typeof(OpenDataGridNumericColumnBase),
                new PropertyMetadata(HorizontalAlignment.Right));
        }

        // ── Shared UI creation ────────────────────────────────────────────────

        public override FrameworkElement CreateDisplayElement() =>
            new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalContentAlignment,
                Margin = new Thickness(4, 0, 4, 0),
                TextTrimming = TextTrimming
            };

        public override FrameworkElement CreateEditElement()
        {
            var tb = new TextBox
            {
                Style = InternalStyles.TextBox,
                Background = System.Windows.Media.Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Right,
                BorderThickness = new Thickness(0)
            };
            tb.PreviewTextInput += (s, e) =>
            {
                var box = (TextBox)s;
                var candidate = box.Text.Insert(box.SelectionStart, e.Text);
                e.Handled = !ValidateInput(candidate);
            };
            return tb;
        }

        // ── Shared value application ──────────────────────────────────────────

        public override void ApplyDisplayValue(FrameworkElement element, object value)
        {
            if (element is TextBlock tb)
                tb.Text = FormatValue(value);
        }

        public override string GetClipboardText(object value) => FormatValue(value);

        public override void ApplyEditValue(FrameworkElement element, object value)
        {
            if (element is TextBox tb)
            {
                tb.Text = FormatValue(value);
                tb.SelectAll();
            }
        }

        // ── Shared edit entry ─────────────────────────────────────────────────

        public override void BeginCharacterEdit(FrameworkElement editElement, Key key)
        {
            if (editElement is TextBox tb)
            {
                var input = KeyToChar(key);
                if (string.IsNullOrEmpty(input)) return;
                tb.Text = input;
                tb.CaretIndex = tb.Text.Length;
            }
        }

        // ── Shared CanBeginEdit — digits, minus, F2, Enter ───────────────────

        public override bool CanBeginEdit(Key key)
        {
            return (key >= Key.D0 && key <= Key.D9)
                || (key >= Key.NumPad0 && key <= Key.NumPad9)
                || key == Key.OemMinus || key == Key.Subtract
                || key == Key.F2 || key == Key.Enter
                || IsDecimalSeparatorKey(key);
        }

        /// <summary>Override to allow decimal-separator keys (period, comma, numpad-decimal).</summary>
        protected virtual bool IsDecimalSeparatorKey(Key key) => false;

        // ── Shared sort comparison ────────────────────────────────────────────

        public override Comparison<object> GetComparison()
        {
            var getter = PropertyAccessorCache.CreateGetter(BindingPath);
            return (a, b) =>
            {
                var va = GetSortValue(getter(a));
                var vb = GetSortValue(getter(b));
                return va.CompareTo(vb);
            };
        }

        // ── Key-to-character mapping — base handles digits and minus ──────────

        /// <summary>
        /// Maps a key to the character string to place in the edit box when smart-entry fires.
        /// Override to add type-specific keys (e.g. decimal separator).
        /// </summary>
        protected virtual string KeyToChar(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return ((int)(key - Key.D0)).ToString();
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return ((int)(key - Key.NumPad0)).ToString();
            if (key == Key.OemMinus || key == Key.Subtract)
                return "-";
            return string.Empty;
        }

        internal override Filtering.IColumnFilterController CreateFilterController(Controls.OpenDataGrid grid)
            => new Filtering.Cells.NumericFilterController(this, grid, FilterStringFormat);

        // ── Abstract hooks ────────────────────────────────────────────────────

        /// <summary>Formats a raw value for display or edit. Return empty string for null.</summary>
        protected abstract string FormatValue(object value);

        /// <summary>Returns true when <paramref name="candidate"/> is a valid partial or complete input.</summary>
        protected abstract bool ValidateInput(string candidate);

        /// <summary>Returns a comparable representation of <paramref name="rawValue"/> for sorting.</summary>
        protected abstract IComparable GetSortValue(object rawValue);

        /// <summary>
        /// Returns the column's zero value (of the type matching <see cref="OpenDataGridColumn.BindingPath"/>),
        /// clamped to <c>Minimum</c>/<c>Maximum</c>. Used when clearing a cell with the Delete key.
        /// </summary>
        public abstract object GetClearedValue();
    }
}
