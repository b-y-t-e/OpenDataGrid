using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Open.DataGrid.Columns;
using Open.DataGrid.Rendering;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// Lightweight cell container. Holds display and edit elements for one column slot.
    /// Does NOT use WPF data binding for values — values are set directly for performance.
    ///
    /// Background is resolved by <see cref="CellBackgroundResolver"/> from cached layer state.
    /// Call order during Bind: UpdateCellBackground (no refresh) → UpdateRowState (triggers refresh). SetFocused triggers refresh independently.
    /// </summary>
    public class OpenDataGridCell : ContentControl
    {
        private FrameworkElement _displayElement;
        private FrameworkElement _editElement;

        // ── Background layer cache ────────────────────────────────────────────
        private Brush  _dataBrush;
        private Brush  _rowResolvedBackground;
        private Brush  _selectionBrush;
        private Brush  _gridForeground;
        private bool   _isRowSelected;
        private bool   _isAlternateRow;
        private double _alternateRowShift;

        // ── Checked-row styling ───────────────────────────────────────────────
        private bool         _isRowChecked;
        private Brush        _checkedRowForeground;
        private Brush        _checkedRowBackground;
        private FontWeight?  _checkedRowFontWeight;

        // ── Default font (stored so checked-weight override can be restored) ─
        private FontFamily   _defaultFontFamily;
        private double?      _defaultFontSize;
        private FontWeight?  _defaultFontWeight;

        // Focus/selection visual cache — all invalidated together when _selectionBrush changes
        private Brush           _selectionForeground;        // explicit override; null = auto-contrast
        private SolidColorBrush _cachedFocusBrush;
        private Brush           _cachedFocusForeground;
        private Brush           _cachedSelectionForeground;
        private Brush           _cachedFocusSelectionBrush;  // for cache invalidation

        public OpenDataGridColumn Column { get; private set; }
        public int ColumnIndex { get; private set; }
        public bool IsInEditMode { get; private set; }
        public bool IsFocusedCell { get; private set; }

        // Stores the raw value while in display mode so we can recover it on cancel
        private object _currentValue;
        private object _preEditValue;
        private TextChangedEventHandler _textBoxChangedHandler;
        private RoutedEventHandler _toggleChangedHandler;
        private SelectionChangedEventHandler _comboSelectionChangedHandler;

        internal event EventHandler EditValueChanged;

        /// <summary>The last value set via UpdateValue. Exposed internal for unit testing.</summary>
        internal object CurrentValue => _currentValue;

        internal object PreEditValue => _preEditValue;

        /// <summary>The active edit element (non-null only while IsInEditMode).</summary>
        internal FrameworkElement EditElement => _editElement;

        // Shared across all instances — default ContentControl template is <ContentPresenter/> only
        // and does not render Background/BorderBrush/BorderThickness. One frozen template suffices.
        private static readonly ControlTemplate SharedTemplate = BuildSharedTemplate();

        private static ControlTemplate BuildSharedTemplate()
        {
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetBinding(Border.BackgroundProperty,
                new Binding { RelativeSource = RelativeSource.TemplatedParent, Path = new PropertyPath(BackgroundProperty) });
            borderFactory.SetBinding(Border.BorderBrushProperty,
                new Binding { RelativeSource = RelativeSource.TemplatedParent, Path = new PropertyPath(BorderBrushProperty) });
            borderFactory.SetBinding(Border.BorderThicknessProperty,
                new Binding { RelativeSource = RelativeSource.TemplatedParent, Path = new PropertyPath(BorderThicknessProperty) });

            var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            borderFactory.AppendChild(cpFactory);

            var template = new ControlTemplate(typeof(OpenDataGridCell)) { VisualTree = borderFactory };
            template.Seal();
            return template;
        }

        public OpenDataGridCell(OpenDataGridColumn column, int columnIndex)
        {
            Column = column ?? throw new ArgumentNullException(nameof(column));
            ColumnIndex = columnIndex;

            Style = InternalStyles.ContentControl;
            Padding = new Thickness(0);
            BorderThickness = new Thickness(0);
            Focusable = false;
            Template = SharedTemplate;

            _displayElement = column.CreateDisplayElement();
            Content = _displayElement;
        }

        // ── Value ─────────────────────────────────────────────────────────────

        /// <summary>Sets value on the display element directly (fast path, no binding).</summary>
        public void UpdateValue(object value)
        {
            _currentValue = value;
            if (!IsInEditMode)
                Column.ApplyDisplayValue(_displayElement, value);
        }

        // ── Background setters ────────────────────────────────────────────────

        /// <summary>
        /// Stores the selection brush, alternate-row shift, and grid-level foreground used to compute
        /// focus/selection visuals and cell foreground.
        /// Call once per Initialize — does NOT trigger a visual refresh (no data bound yet at that point).
        /// </summary>
        public void UpdateSelectionBrush(Brush selectionBrush, double alternateRowShift, Brush gridForeground = null, Brush selectionForeground = null)
        {
            _selectionBrush     = selectionBrush;
            _alternateRowShift  = alternateRowShift;
            _gridForeground     = gridForeground;
            _selectionForeground = selectionForeground;
        }

        /// <summary>
        /// Updates row-level state and triggers a visual refresh.
        /// Always call this last — after UpdateCellBackground — so ApplyBackground runs once with complete state.
        /// </summary>
        public void UpdateRowState(Brush rowResolvedBackground, bool isRowSelected, bool isAlternateRow)
        {
            _rowResolvedBackground = rowResolvedBackground;
            _isRowSelected         = isRowSelected;
            _isAlternateRow        = isAlternateRow;
            ApplyBackground();
        }

        /// <summary>
        /// Stores the data-bound cell brush (from CellBackgroundBindingPath).
        /// Does NOT trigger a visual refresh — always followed by UpdateRowState which does.
        /// </summary>
        public void UpdateCellBackground(Brush dataBrush)
        {
            _dataBrush = dataBrush;
        }

        /// <summary>Shows or hides focused-cell styling. Always triggers a visual refresh.</summary>
        public void SetFocused(bool focused)
        {
            IsFocusedCell = focused;
            ApplyBackground();
        }


        // ── Core background + foreground resolver ─────────────────────────────

        private void ApplyBackground()
        {
            Brush resolvedBg;
            bool  isHighlighted;

            if (IsFocusedCell)
            {
                EnsureSelectionVisuals();
                Background = _cachedFocusBrush;
                BorderThickness = new Thickness(0);
                BorderBrush     = null;
                resolvedBg    = _cachedFocusBrush;
                isHighlighted = true;
            }
            else
            {
                BorderThickness = new Thickness(0);
                BorderBrush     = null;

                // Checked background sits between selection and column layers.
                // Only applied when row is checked but NOT selected (selection always wins over checked).
                if (_isRowChecked && !_isRowSelected && _checkedRowBackground != null)
                {
                    resolvedBg    = _checkedRowBackground;
                    Background    = resolvedBg;
                    isHighlighted = false;
                }
                else
                {
                    var ctx = new CellBackgroundContext(
                        focusCellBrush:            null,
                        selectionBrush:            _selectionBrush,
                        columnBackground:          Column.Background,
                        backgroundOverridesData:   Column.BackgroundOverridesData,
                        dataCellBrush:             _dataBrush,
                        rowResolvedBackground:     _rowResolvedBackground,
                        isRowSelected:             _isRowSelected,
                        isCellFocused:             false,
                        isAlternateRow:            _isAlternateRow,
                        alternateRowShift:         _alternateRowShift,
                        columnAlternateBackground: Column.GetAlternateBackground(_alternateRowShift));

                    resolvedBg    = CellBackgroundResolver.Resolve(in ctx);
                    Background    = resolvedBg;
                    isHighlighted = _isRowSelected;
                }
            }

            ApplyForeground(resolvedBg, isHighlighted);
        }

        private void ApplyForeground(Brush resolvedBg, bool isHighlighted)
        {
            if (isHighlighted)
            {
                EnsureSelectionVisuals();
                Foreground = IsFocusedCell ? _cachedFocusForeground : _cachedSelectionForeground;
                return;
            }

            // Checked foreground sits below selection/focus but above column/grid defaults.
            if (_isRowChecked && _checkedRowForeground != null)
            {
                Foreground = _checkedRowForeground;
                return;
            }

            var fg = CellForegroundResolver.Resolve(resolvedBg, isHighlighted: false, Column.Foreground, _gridForeground);
            if (fg != null)
                Foreground = fg;
            else
                ClearValue(ForegroundProperty);
        }

        private void EnsureSelectionVisuals()
        {
            if (!ReferenceEquals(_cachedFocusSelectionBrush, _selectionBrush) || _cachedFocusBrush == null)
            {
                _cachedFocusSelectionBrush  = _selectionBrush;
                _cachedFocusBrush           = BrushShift.ComputeFocusBrush(_selectionBrush);
                _cachedFocusForeground      = _selectionForeground ?? BrushShift.ComputeForegroundBrush(_cachedFocusBrush);
                _cachedSelectionForeground  = _selectionForeground ?? BrushShift.ComputeForegroundBrush(_selectionBrush);
            }
        }

        // ── Font ──────────────────────────────────────────────────────────────

        internal void UpdateDefaultFont(FontFamily fontFamily, double? fontSize, FontWeight? fontWeight)
        {
            _defaultFontFamily = fontFamily;
            _defaultFontSize   = fontSize;
            _defaultFontWeight = fontWeight;
            ApplyFont();
        }

        internal void UpdateCheckedState(bool isChecked)
        {
            if (_isRowChecked == isChecked) return;
            _isRowChecked = isChecked;
            ApplyBackground();
            ApplyFont();
        }

        internal void UpdateCheckedRowStyle(Brush foreground, Brush background, FontWeight? fontWeight)
        {
            _checkedRowForeground = foreground;
            _checkedRowBackground = background;
            _checkedRowFontWeight = fontWeight;
            if (_isRowChecked)
            {
                ApplyBackground();
                ApplyFont();
            }
        }

        internal void InvalidateFont() => ApplyFont();

        private void ApplyFont()
        {
            var effectiveFamily = _defaultFontFamily;
            var effectiveSize   = Column.FontSize ?? _defaultFontSize;
            var effectiveWeight = (_isRowChecked && _checkedRowFontWeight.HasValue)
                ? _checkedRowFontWeight
                : _defaultFontWeight;

            if (effectiveFamily != null) SetValue(FontFamilyProperty, effectiveFamily);
            else ClearValue(FontFamilyProperty);

            if (effectiveSize.HasValue) SetValue(FontSizeProperty, effectiveSize.Value);
            else ClearValue(FontSizeProperty);

            if (effectiveWeight.HasValue) SetValue(FontWeightProperty, effectiveWeight.Value);
            else ClearValue(FontWeightProperty);
        }

        // ── Edit mode ─────────────────────────────────────────────────────────

        /// <summary>Switches the cell into edit mode. Returns false if not editable.</summary>
        public bool BeginEdit()
        {
            if (IsInEditMode) return true;
            if (_editElement == null)
                _editElement = Column.CreateEditElement();
            if (_editElement == null) return false;

            _preEditValue = _currentValue;
            Column.ApplyEditValue(_editElement, _currentValue);
            Content = _editElement;
            IsInEditMode = true;

            if (_editElement is System.Windows.Controls.TextBox tb)
            {
                _textBoxChangedHandler = (s, e) => EditValueChanged?.Invoke(this, EventArgs.Empty);
                tb.TextChanged += _textBoxChangedHandler;
            }
            else if (_editElement is ToggleButton toggle)
            {
                _toggleChangedHandler = (s, e) => EditValueChanged?.Invoke(this, EventArgs.Empty);
                toggle.Checked += _toggleChangedHandler;
                toggle.Unchecked += _toggleChangedHandler;
                toggle.Indeterminate += _toggleChangedHandler;
            }
            else if (_editElement is ComboBox combo)
            {
                _comboSelectionChangedHandler = (s, e) => EditValueChanged?.Invoke(this, EventArgs.Empty);
                combo.SelectionChanged += _comboSelectionChangedHandler;
            }

            return true;
        }

        /// <summary>Commits the edit value back into the data item and switches to display mode.</summary>
        public object CommitEdit()
        {
            if (!IsInEditMode) return OpenDataGridColumn.NoValue;
            try
            {
                var value = Column.ExtractEditValue(_editElement);
                if (!ReferenceEquals(value, OpenDataGridColumn.NoValue))
                    _currentValue = value;
                return value;
            }
            finally { EndEdit(); }
        }

        /// <summary>Cancels the edit and restores the previous value.</summary>
        public void CancelEdit()
        {
            if (!IsInEditMode) return;
            _currentValue = _preEditValue;
            EndEdit();
        }

        private void EndEdit()
        {
            if (_textBoxChangedHandler != null && _editElement is System.Windows.Controls.TextBox tb)
                tb.TextChanged -= _textBoxChangedHandler;
            _textBoxChangedHandler = null;

            if (_toggleChangedHandler != null && _editElement is ToggleButton toggle)
            {
                toggle.Checked -= _toggleChangedHandler;
                toggle.Unchecked -= _toggleChangedHandler;
                toggle.Indeterminate -= _toggleChangedHandler;
            }
            _toggleChangedHandler = null;

            if (_comboSelectionChangedHandler != null && _editElement is ComboBox combo)
                combo.SelectionChanged -= _comboSelectionChangedHandler;
            _comboSelectionChangedHandler = null;

            Column.ApplyDisplayValue(_displayElement, _currentValue);
            Content = _displayElement;
            IsInEditMode = false;
            ApplyBackground(); // restore correct background/border regardless of focus state
        }
    }
}
