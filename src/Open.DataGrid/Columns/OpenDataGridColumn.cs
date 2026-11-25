using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Open.DataGrid.Accessors;
using Open.DataGrid.Filtering;
using Open.DataGrid.Models;
using Open.DataGrid.Rendering;

namespace Open.DataGrid.Columns
{
    /// <summary>
    /// Abstract base for all OpenDataGrid columns. Inherits from Freezable so it participates
    /// in the WPF inheritance context and inherits DataContext from the parent grid.
    /// </summary>
    public abstract class OpenDataGridColumn : Freezable
    {
        /// <summary>
        /// Sentinel returned by <see cref="ExtractEditValue"/> when the element is null or of wrong
        /// type and there is nothing to commit. Distinct from <c>null</c>, which is a valid value
        /// (e.g. <c>bool?</c> indeterminate). Callers must check for this before writing to the model.
        /// </summary>
        public static readonly object NoValue = new object();

        #region Dependency Properties

        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(nameof(Header), typeof(string), typeof(OpenDataGridColumn),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty BindingPathProperty =
            DependencyProperty.Register(nameof(BindingPath), typeof(string), typeof(OpenDataGridColumn),
                new PropertyMetadata(null));

        public static readonly DependencyProperty WidthProperty =
            DependencyProperty.Register(nameof(Width), typeof(double), typeof(OpenDataGridColumn),
                new PropertyMetadata(100.0));

        public static readonly DependencyProperty MinWidthProperty =
            DependencyProperty.Register(nameof(MinWidth), typeof(double), typeof(OpenDataGridColumn),
                new PropertyMetadata(20.0));

        public static readonly DependencyProperty MaxWidthProperty =
            DependencyProperty.Register(nameof(MaxWidth), typeof(double), typeof(OpenDataGridColumn),
                new PropertyMetadata(double.MaxValue));

        public static readonly DependencyProperty IsReadOnlyProperty =
            DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(OpenDataGridColumn),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsSortableProperty =
            DependencyProperty.Register(nameof(IsSortable), typeof(bool), typeof(OpenDataGridColumn),
                new PropertyMetadata(true));

        public static readonly DependencyProperty IsVisibleProperty =
            DependencyProperty.Register(nameof(IsVisible), typeof(bool), typeof(OpenDataGridColumn),
                new PropertyMetadata(true));

        public static readonly DependencyProperty CellBackgroundBindingPathProperty =
            DependencyProperty.Register(nameof(CellBackgroundBindingPath), typeof(string), typeof(OpenDataGridColumn),
                new PropertyMetadata(null));

        public static readonly DependencyProperty BackgroundProperty =
            DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(OpenDataGridColumn),
                new PropertyMetadata(null, OnBackgroundChanged));

        public static readonly DependencyProperty BackgroundOverridesDataProperty =
            DependencyProperty.Register(nameof(BackgroundOverridesData), typeof(bool), typeof(OpenDataGridColumn),
                new PropertyMetadata(true));

        public static readonly DependencyProperty ForegroundProperty =
            DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(OpenDataGridColumn),
                new PropertyMetadata(null));

        public static readonly DependencyProperty SortDirectionProperty =
            DependencyProperty.Register(nameof(SortDirection), typeof(SortDirection), typeof(OpenDataGridColumn),
                new PropertyMetadata(SortDirection.None));

        public static readonly DependencyProperty FilteringEnabledProperty =
            DependencyProperty.Register(nameof(FilteringEnabled), typeof(bool), typeof(OpenDataGridColumn),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IgnoreDiacriticsProperty =
            DependencyProperty.Register(nameof(IgnoreDiacritics), typeof(bool), typeof(OpenDataGridColumn),
                new PropertyMetadata(true));

        public static readonly DependencyProperty FilterStringFormatProperty =
            DependencyProperty.Register(nameof(FilterStringFormat), typeof(string), typeof(OpenDataGridColumn),
                new PropertyMetadata(null));

        public static readonly DependencyProperty TextTrimmingProperty =
            DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(OpenDataGridColumn),
                new PropertyMetadata(TextTrimming.None));

        public static readonly DependencyProperty HorizontalContentAlignmentProperty =
            DependencyProperty.Register(nameof(HorizontalContentAlignment), typeof(HorizontalAlignment), typeof(OpenDataGridColumn),
                new PropertyMetadata(HorizontalAlignment.Left));

        public static readonly DependencyProperty HeaderHorizontalAlignmentProperty =
            DependencyProperty.Register(nameof(HeaderHorizontalAlignment), typeof(HorizontalAlignment?), typeof(OpenDataGridColumn),
                new PropertyMetadata(null));

        public static readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register(nameof(FontSize), typeof(double?), typeof(OpenDataGridColumn),
                new PropertyMetadata(null));

        #endregion

        #region Properties

        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public string BindingPath
        {
            get => (string)GetValue(BindingPathProperty);
            set => SetValue(BindingPathProperty, value);
        }

        public double Width
        {
            get => (double)GetValue(WidthProperty);
            set => SetValue(WidthProperty, value);
        }

        public double MinWidth
        {
            get => (double)GetValue(MinWidthProperty);
            set => SetValue(MinWidthProperty, value);
        }

        public double MaxWidth
        {
            get => (double)GetValue(MaxWidthProperty);
            set => SetValue(MaxWidthProperty, value);
        }

        public bool IsReadOnly
        {
            get => (bool)GetValue(IsReadOnlyProperty);
            set => SetValue(IsReadOnlyProperty, value);
        }

        public bool IsSortable
        {
            get => (bool)GetValue(IsSortableProperty);
            set => SetValue(IsSortableProperty, value);
        }

        public bool IsVisible
        {
            get => (bool)GetValue(IsVisibleProperty);
            set => SetValue(IsVisibleProperty, value);
        }

        /// <summary>Property path on the data item returning a Brush for cell background.</summary>
        public string CellBackgroundBindingPath
        {
            get => (string)GetValue(CellBackgroundBindingPathProperty);
            set => SetValue(CellBackgroundBindingPathProperty, value);
        }

        /// <summary>
        /// Static background brush for all cells in this column.
        /// When <see cref="BackgroundOverridesData"/> is true (default), this brush takes priority over CellBackgroundBindingPath.
        /// When false, data-bound brush wins and Background acts as fallback.
        /// </summary>
        public Brush Background
        {
            get => (Brush)GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        /// <summary>
        /// When true (default), Column.Background takes priority over CellBackgroundBindingPath data.
        /// When false, data-bound brush overrides Column.Background (Background acts as fallback only).
        /// </summary>
        public bool BackgroundOverridesData
        {
            get => (bool)GetValue(BackgroundOverridesDataProperty);
            set => SetValue(BackgroundOverridesDataProperty, value);
        }

        /// <summary>Static foreground brush for all cells in this column. Null = inherit from Grid.Foreground or WPF default.</summary>
        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public SortDirection SortDirection
        {
            get => (SortDirection)GetValue(SortDirectionProperty);
            set => SetValue(SortDirectionProperty, value);
        }

        /// <summary>When true, a filter editor is shown in the filter row for this column.</summary>
        public bool FilteringEnabled
        {
            get => (bool)GetValue(FilteringEnabledProperty);
            set => SetValue(FilteringEnabledProperty, value);
        }

        /// <summary>When true (default), the filter editor ignores diacritics when matching.</summary>
        public bool IgnoreDiacritics
        {
            get => (bool)GetValue(IgnoreDiacriticsProperty);
            set => SetValue(IgnoreDiacriticsProperty, value);
        }

        /// <summary>
        /// Optional format string used when converting values to string for filtering
        /// (e.g. "yyyy-MM-dd" for DateTime, "0.00" for decimals).
        /// Null = use default ToString().
        /// </summary>
        public string FilterStringFormat
        {
            get => (string)GetValue(FilterStringFormatProperty);
            set => SetValue(FilterStringFormatProperty, value);
        }

        /// <summary>Controls text trimming in display cells. Default: None.</summary>
        public TextTrimming TextTrimming
        {
            get => (TextTrimming)GetValue(TextTrimmingProperty);
            set => SetValue(TextTrimmingProperty, value);
        }

        /// <summary>Horizontal alignment of content in display cells. Numeric columns default to Right, Boolean to Center, others to Left.</summary>
        public HorizontalAlignment HorizontalContentAlignment
        {
            get => (HorizontalAlignment)GetValue(HorizontalContentAlignmentProperty);
            set => SetValue(HorizontalContentAlignmentProperty, value);
        }

        /// <summary>Per-column override of header text alignment. Null (default) = inherit from Grid.HeaderHorizontalAlignment.</summary>
        public HorizontalAlignment? HeaderHorizontalAlignment
        {
            get => (HorizontalAlignment?)GetValue(HeaderHorizontalAlignmentProperty);
            set => SetValue(HeaderHorizontalAlignmentProperty, value);
        }

        /// <summary>Per-column override of cell font size. Null (default) = inherit from Grid.CellFontSize.</summary>
        public double? FontSize
        {
            get => (double?)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        #endregion

        #region Alternate background cache

        // Cache for the shifted alternate-row variant of Background.
        // Invalidated whenever Background DP changes.
        private Brush  _cachedAlternateBg;
        private double _cachedShift = -1;
        private Brush  _cachedBase;

        /// <summary>
        /// Returns a shifted variant of <see cref="Background"/> for alternate rows.
        /// Result is cached per (Background, shift) pair — zero allocation on subsequent calls.
        /// Returns null when Background is null or shift is 0.
        /// </summary>
        internal Brush GetAlternateBackground(double shift)
        {
            var baseBg = Background;
            if (baseBg == null || shift <= 0.0) return null;

            if (!ReferenceEquals(_cachedBase, baseBg) || _cachedShift != shift)
            {
                _cachedBase        = baseBg;
                _cachedShift       = shift;
                _cachedAlternateBg = BrushShift.Apply(baseBg, shift);
            }
            return _cachedAlternateBg;
        }

        /// <summary>
        /// Invoked by the parent grid whenever this column's <see cref="Background"/> changes
        /// so visible cells can be repainted immediately. Set by the grid in
        /// <see cref="Controls.OpenDataGrid.BuildPropertyAccessors"/>; cleared when unset.
        /// </summary>
        internal Action BackgroundInvalidated;

        private static void OnBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var col = (OpenDataGridColumn)d;
            col._cachedBase = null;           // invalidate alternate cache
            col.BackgroundInvalidated?.Invoke();
        }

        #endregion

        #region Shared Helpers

        /// <summary>Reads a named property value from a data item using the shared accessor cache.</summary>
        protected static object GetPropertyValue(object item, string path)
            => PropertyAccessorCache.GetValue(item, path);

        #endregion

        #region Abstract Members

        /// <summary>Creates the visual element used in display (non-edit) mode.</summary>
        public abstract FrameworkElement CreateDisplayElement();

        /// <summary>Creates the visual element used in edit mode. Returns null if not editable.</summary>
        public abstract FrameworkElement CreateEditElement();

        /// <summary>Sets the display element's visual representation from the raw value.</summary>
        public abstract void ApplyDisplayValue(FrameworkElement element, object value);

        /// <summary>
        /// Text copied to the clipboard (Ctrl+C) for a cell holding <paramref name="value"/> —
        /// the same text the cell displays. A column without <see cref="BindingPath"/> receives
        /// the whole data item and copies nothing unless it overrides this.
        /// </summary>
        public virtual string GetClipboardText(object value)
            => string.IsNullOrEmpty(BindingPath) ? string.Empty : value?.ToString() ?? string.Empty;

        /// <summary>
        /// A formatter equivalent to <see cref="GetClipboardText"/> that captures the column's settings
        /// now (on the UI thread) and is therefore safe to run on a background thread — used to build
        /// large clipboard copies off the UI thread. Returns <c>null</c> for column types defined
        /// outside this library (their <see cref="GetClipboardText"/> may read dependency properties),
        /// in which case the copy is built on the UI thread.
        /// </summary>
        internal Func<object, string> CreateThreadSafeClipboardFormatter()
            => GetType().Assembly == typeof(OpenDataGridColumn).Assembly ? CreateClipboardFormatter() : null;

        /// <summary>Background-safe <see cref="GetClipboardText"/>; built-in columns override it to capture their settings.</summary>
        internal virtual Func<object, string> CreateClipboardFormatter()
        {
            bool hasPath = !string.IsNullOrEmpty(BindingPath);
            var culture  = System.Globalization.CultureInfo.CurrentCulture;
            return value => hasPath && value != null ? Convert.ToString(value, culture) ?? string.Empty : string.Empty;
        }

        /// <summary>Sets the edit element's content from the raw value.</summary>
        public abstract void ApplyEditValue(FrameworkElement element, object value);

        /// <summary>Extracts the edited value from the edit element.</summary>
        public abstract object ExtractEditValue(FrameworkElement element);

        /// <summary>
        /// Returns true if the given key press should trigger entering edit mode.
        /// Numeric columns should return false for letter keys.
        /// </summary>
        public abstract bool CanBeginEdit(Key key);

        /// <summary>Returns a Comparison delegate used for sorting rows by this column.</summary>
        public abstract Comparison<object> GetComparison();

        /// <summary>
        /// Creates a filter controller for this column's cell in the filter row.
        /// Returns null when the column type does not support in-row filtering.
        /// Override in each concrete column subclass that supports filtering.
        /// External subclasses that do not override receive null (no filter cell).
        /// </summary>
        internal virtual IColumnFilterController CreateFilterController(Controls.OpenDataGrid grid) => null;

        /// <summary>
        /// Called when edit mode is entered via a character key press (not F2/Enter/click).
        /// Override to replace the edit element's content with the typed character.
        /// Default: no-op — ApplyEditValue's SelectAll remains in effect.
        /// </summary>
        public virtual void BeginCharacterEdit(FrameworkElement editElement, Key key) { }

        /// <summary>
        /// When true, navigation keys (Up, Down, Left, Right, PageUp, PageDown) pressed during edit mode
        /// are absorbed without committing or navigating. Use for edit elements where those keys have
        /// no caret/cursor meaning (e.g. CheckBox). Default: false.
        /// </summary>
        public virtual bool AbsorbNavigationKeysInEdit => false;

        /// <summary>
        /// Returns true if the caret in the edit element is at a boundary in the direction of the given key,
        /// indicating that Left/Right should commit and navigate to the adjacent column instead of moving the caret.
        /// Default implementation handles TextBox: Left = caret at position 0, Right = caret at end of text.
        /// </summary>
        public virtual bool IsAtEdge(FrameworkElement editElement, Key key)
        {
            if (editElement is System.Windows.Controls.TextBox tb)
            {
                if (key == Key.Left)  return tb.SelectionStart == 0 && tb.SelectionLength == 0;
                if (key == Key.Right) return tb.SelectionStart == tb.Text.Length && tb.SelectionLength == 0;
            }
            return false;
        }

        #endregion

        protected override Freezable CreateInstanceCore()
        {
            throw new NotSupportedException("OpenDataGridColumn subclasses must override CreateInstanceCore.");
        }
    }
}
