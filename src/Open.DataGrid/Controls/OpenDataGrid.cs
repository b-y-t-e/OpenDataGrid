using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Open.DataGrid.Accessors;
using Open.DataGrid.Columns;
using Open.DataGrid.Filtering;
using Open.DataGrid.Models;
using Open.DataGrid.Navigation;
using Open.DataGrid.Rendering;
using Open.DataGrid.Sorting;
using Open.DataGrid.Virtualization;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// High-performance custom DataGrid.
    /// - Full row virtualization via ring-buffer (Canvas + standalone ScrollBars)
    /// - No DataGrid, ListView, VirtualizingStackPanel or VirtualizingPanel used
    /// - Direct property access (compiled delegates) for cell value updates
    /// - Fixed row height for O(1) scroll math
    /// </summary>
    [TemplatePart(Name = PartCanvas, Type = typeof(Canvas))]
    [TemplatePart(Name = PartHeaderCanvas, Type = typeof(Canvas))]
    [TemplatePart(Name = PartFilterRowContainer, Type = typeof(FrameworkElement))]
    [TemplatePart(Name = PartFilterRowCanvas, Type = typeof(Canvas))]
    [TemplatePart(Name = PartVerticalScrollBar, Type = typeof(ScrollBar))]
    [TemplatePart(Name = PartHorizontalScrollBar, Type = typeof(ScrollBar))]
    [TemplatePart(Name = PartLoadingOverlay, Type = typeof(FrameworkElement))]
    [TemplatePart(Name = PartLoadingBar, Type = typeof(ProgressBar))]
    public partial class OpenDataGrid : Control
    {
        private const string PartCanvas = "PART_Canvas";
        private const string PartHeaderCanvas = "PART_HeaderCanvas";
        private const string PartVerticalScrollBar = "PART_VerticalScrollBar";
        private const string PartHorizontalScrollBar = "PART_HorizontalScrollBar";
        private const string PartLoadingOverlay = "PART_LoadingOverlay";
        private const string PartLoadingBar = "PART_LoadingBar";
        private const string PartFilterRowContainer = "PART_FilterRowContainer";
        private const string PartFilterRowCanvas = "PART_FilterRowCanvas";
        private const int FilterAsyncThreshold = 10_000;
        private const int MouseWheelRowsPerNotch = 3;
        private const int HorizontalScrollSmallChange = 50;
        internal const double HorizontalOffsetEpsilon = 0.5;
        private const double InitFallbackViewportWidth = 1200;
        private const double InitFallbackCanvasHeight = 600;

        // Template parts
        internal Canvas _canvas;
        internal ScrollBar _vScrollBar;
        internal ScrollBar _hScrollBar;

        // Engines
        internal readonly RowVirtualizationEngine _rowEngine = new RowVirtualizationEngine();
        internal readonly ColumnVirtualizationEngine _colEngine = new ColumnVirtualizationEngine();
        internal readonly SortEngine _sortEngine = new SortEngine();
        internal readonly NavigationEngine _navEngine = new NavigationEngine();
        private readonly FilterEngine _filterEngine = new FilterEngine();

        // State
        private bool _suppressScrollEvents;
        private readonly GridLifecycleController _lifecycle = new GridLifecycleController();
        private Pen _gridLinePen;

        // Theme state
        // DPs that the theme engine wrote to — used to detect "user override" vs "theme value"
        // and to clean up when switching back to Light.
        private readonly HashSet<DependencyProperty> _themeWritten =
            new HashSet<DependencyProperty>();

        // Resource keys for DynamicResource-driven scrollbar colors.
        internal const string ScrollBarTrackKey      = "ODG.ScrollBar.Track";
        internal const string ScrollBarThumbKey      = "ODG.ScrollBar.Thumb";
        internal const string ScrollBarThumbHoverKey = "ODG.ScrollBar.ThumbHover";
        internal const string ScrollBarThumbDragKey  = "ODG.ScrollBar.ThumbDrag";

        // Injected system-theme resolver — default reads the registry; tests can substitute a stub.
        internal Func<bool> _isDarkSystemTheme = Rendering.SystemTheme.IsDark;
        internal double _hOffset;
        // Focused cell position — single source of truth lives in _navEngine.
        // These properties are the only way to read focused cell indices; all writes
        // go through _selection.ApplyCellFocus (or _navEngine.SetCurrentPosition directly in init paths).
        internal int  FocusedRowSortedIndex => _navEngine.CurrentRowIndex;
        internal int  FocusedColIndex       => _navEngine.CurrentColumnIndex;

        internal OpenDataGridCell _editingCell;
        private int _editingDataIndex = -1;
        private EventHandler _editValueChangedHandler;

        // Compiled property accessors: index matches Columns index
        private Func<object, object>[] _propertyGetters;
        private Action<object, object>[] _propertySetters;
        private Func<object, Brush>[] _cellBrushGetters;
        private Func<object, Brush>      _rowBrushGetter;
        private Func<int, object, Brush> _rowColorResolver;

        // CheckedItems state
        private readonly HashSet<object> _checkedSet = new HashSet<object>(ObjectReferenceEqualityComparer.Default);
        private bool _internalCheckedChange;
        private Func<object, bool>       _rowReadOnlyGetter;
        private int[]                    _checkColumnIndices = Array.Empty<int>();
        private Func<object, bool>       _boundCheckGetter; // non-null only in pure-bound mode (CheckedItems==null, BindingPath set)

        private bool _isForcingTemplateApply;

        // Coordinates in-flight async work: pending-work counter, epoch, filter-pending set,
        // and loading-indicator visibility. See GridWorkCoordinator for the full protocol.
        private readonly GridWorkCoordinator _workCoordinator;

        // Centralizes selection/focus synchronization. See SelectionFocusSynchronizer.
        internal readonly SelectionFocusSynchronizer _selection;

        // Manages header build/layout, column caches, DPD subscriptions, HiddenColumns.
        internal readonly HeaderPresenter _headerPresenter;

        // Manages the per-column filter row displayed under the header.
        internal FilterRowPresenter _filterRowPresenter;

        // Epoch captured alongside each BeginPendingWork that precedes a Sort/SortExplicit call.
        // Read inside OnSortCompleted (on UI thread via Dispatcher.BeginInvoke) to guard against stale sort events.
        // IMPORTANT: assigned AFTER Sort()/SortExplicit() returns (not before). SortCancelled fires
        // synchronously inside those calls, so at fire-time _sortPendingEpoch still holds the OLD sort's epoch —
        // exactly what OnSortCancelled's EndPendingWork needs. SortCompleted reads it later via Dispatcher.BeginInvoke,
        // after the assignment completes on the UI thread.
        private int _sortPendingEpoch;
        // Coalescing flag: true = a CollectionChanged rebuild is already scheduled.
        private bool _collectionRebuildScheduled;
        // Generation token for async filter compute; stale results (old gen) are discarded.
        // Written with Interlocked.Increment (atomic) — both UI and background threads read it.
        private int _filterGeneration;
        // Cancels the currently running background FilterEngine.Apply when a newer one starts.
        private System.Threading.CancellationTokenSource _filterCts;

        // -----------------------------------------------------------------------
        // Internal API for SelectionFocusSynchronizer / HeaderPresenter
        // Single-property accessors reduce direct field coupling in helper classes.
        // -----------------------------------------------------------------------

        /// <summary>Whether the grid has completed TryInitialize/FinishInitialize.</summary>
        internal bool IsGridInitialized => _lifecycle.IsInitialized;

        /// <summary>Total number of rows visible after filter + sort.</summary>
        internal int TotalVisibleRows => _sortEngine.SortedIndices?.Length ?? 0;

        /// <summary>For tests: true when any filter control has an outstanding NotifyFilterPending signal.</summary>
        internal bool HasPendingFilterSignals => _workCoordinator.HasPendingFilterSignals;

        /// <summary>
        /// True when <see cref="ShowEmptyPlaceholderRow"/> is enabled and there are no real data rows.
        /// The placeholder counts as 1 row for navigation but carries no data item and cannot be edited.
        /// </summary>
        internal bool IsPlaceholderActive =>
            ShowEmptyPlaceholderRow &&
            (_sortEngine.SortedIndices != null
                ? _sortEngine.SortedIndices.Length == 0
                : (DataSource?.Count ?? 0) == 0);

        /// <summary>
        /// Navigation-visible row count: 1 when only the placeholder is shown, otherwise <see cref="TotalVisibleRows"/>.
        /// Use this everywhere a row count is needed for nav/focus — never for data access.
        /// </summary>
        internal int EffectiveRowCount => IsPlaceholderActive ? 1 : TotalVisibleRows;

        internal void SetGridFocusVisual(bool focused) => _rowEngine.SetGridFocused(focused);

        #region Dependency Properties

        public static readonly DependencyProperty DataSourceProperty =
            DependencyProperty.Register(nameof(DataSource), typeof(IList), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnDataSourceChanged));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(OpenDataGrid),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(OpenDataGrid),
                new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty AutoScrollToSelectedItemProperty =
            DependencyProperty.Register(nameof(AutoScrollToSelectedItem), typeof(bool), typeof(OpenDataGrid),
                new PropertyMetadata(true));

        public static readonly DependencyProperty RequireSelectionProperty =
            DependencyProperty.Register(nameof(RequireSelection), typeof(bool), typeof(OpenDataGrid),
                new PropertyMetadata(true));

        /// <summary>
        /// When <c>true</c>, a single empty non-editable placeholder row is shown whenever the grid
        /// has no visible data (null/empty DataSource or all rows filtered out). The row is
        /// navigable (Left/Right in Cell mode, Row-focus border in Row mode) but editing is blocked.
        /// Default: <c>false</c>.
        /// </summary>
        public static readonly DependencyProperty ShowEmptyPlaceholderRowProperty =
            DependencyProperty.Register(nameof(ShowEmptyPlaceholderRow), typeof(bool), typeof(OpenDataGrid),
                new PropertyMetadata(false, (d, _) => ((OpenDataGrid)d).TryInitialize()));

        public static readonly DependencyProperty AllowColumnReorderProperty =
            DependencyProperty.Register(nameof(AllowColumnReorder), typeof(bool), typeof(OpenDataGrid),
                new PropertyMetadata(true));

        public static readonly DependencyProperty ColumnsProperty =
            DependencyProperty.Register(nameof(Columns), typeof(FreezableCollection<OpenDataGridColumn>), typeof(OpenDataGrid),
                new PropertyMetadata(null));

        public static readonly DependencyProperty HiddenColumnsProperty =
            DependencyProperty.Register(nameof(HiddenColumns), typeof(string), typeof(OpenDataGrid),
                new PropertyMetadata(string.Empty, OnHiddenColumnsChanged));

        public static readonly DependencyProperty HiddenColumnsByHeaderProperty =
            DependencyProperty.Register(nameof(HiddenColumnsByHeader), typeof(bool), typeof(OpenDataGrid),
                new PropertyMetadata(true, (d, _) => ((OpenDataGrid)d).ApplyHiddenColumns()));

        public static readonly DependencyProperty IsReadOnlyProperty =
            DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(OpenDataGrid),
                new PropertyMetadata(false));

        public static readonly DependencyProperty RowHeightProperty =
            DependencyProperty.Register(nameof(RowHeight), typeof(double), typeof(OpenDataGrid),
                new PropertyMetadata(28.0, (d, _) => ((OpenDataGrid)d).TryInitialize()));

        public static readonly DependencyProperty HeaderHeightProperty =
            DependencyProperty.Register(nameof(HeaderHeight), typeof(double), typeof(OpenDataGrid),
                new PropertyMetadata(32.0));

        public static readonly DependencyProperty HeaderHorizontalAlignmentProperty =
            DependencyProperty.Register(nameof(HeaderHorizontalAlignment), typeof(HorizontalAlignment), typeof(OpenDataGrid),
                new PropertyMetadata(HorizontalAlignment.Left, OnHeaderHorizontalAlignmentChanged));

        public static readonly DependencyProperty HeaderFontFamilyProperty =
            DependencyProperty.Register(nameof(HeaderFontFamily), typeof(FontFamily), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnHeaderFontDefaultsChanged));

        public static readonly DependencyProperty HeaderFontSizeProperty =
            DependencyProperty.Register(nameof(HeaderFontSize), typeof(double?), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnHeaderFontDefaultsChanged));

        public static readonly DependencyProperty HeaderFontWeightProperty =
            DependencyProperty.Register(nameof(HeaderFontWeight), typeof(FontWeight), typeof(OpenDataGrid),
                new PropertyMetadata(FontWeights.SemiBold, OnHeaderFontDefaultsChanged));

        public static readonly DependencyProperty HeaderBackgroundProperty =
            DependencyProperty.Register(nameof(HeaderBackground), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnHeaderFontDefaultsChanged));

        public static readonly DependencyProperty HeaderForegroundProperty =
            DependencyProperty.Register(nameof(HeaderForeground), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnHeaderFontDefaultsChanged));

        public static readonly DependencyProperty ColumnSeparatorBrushProperty =
            DependencyProperty.Register(nameof(ColumnSeparatorBrush), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnColumnSeparatorBrushChanged));

        public static readonly DependencyProperty CellFontFamilyProperty =
            DependencyProperty.Register(nameof(CellFontFamily), typeof(FontFamily), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnCellFontDefaultsChanged));

        public static readonly DependencyProperty CellFontSizeProperty =
            DependencyProperty.Register(nameof(CellFontSize), typeof(double?), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnCellFontDefaultsChanged));

        public static readonly DependencyProperty CellFontWeightProperty =
            DependencyProperty.Register(nameof(CellFontWeight), typeof(FontWeight?), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnCellFontDefaultsChanged));

        public static readonly DependencyProperty AlternateRowBackgroundProperty =
            DependencyProperty.Register(nameof(AlternateRowBackground), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(null));

        public static readonly DependencyProperty RowBackgroundProperty =
            DependencyProperty.Register(nameof(RowBackground), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(Brushes.White));

        public static readonly DependencyProperty AlternateRowShiftProperty =
            DependencyProperty.Register(nameof(AlternateRowShift), typeof(double), typeof(OpenDataGrid),
                new PropertyMetadata(0.2));

        public static readonly DependencyProperty SelectionBrushProperty =
            DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(SystemColors.HighlightBrush));

        public static readonly DependencyProperty SelectionForegroundProperty =
            DependencyProperty.Register(nameof(SelectionForeground), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(null));

        public static readonly DependencyProperty NavigationModeProperty =
            DependencyProperty.Register(nameof(NavigationMode), typeof(NavigationMode), typeof(OpenDataGrid),
                new PropertyMetadata(NavigationMode.Cell, OnNavigationModeChanged));

public static readonly DependencyProperty RowReadOnlyBindingPathProperty =
            DependencyProperty.Register(nameof(RowReadOnlyBindingPath), typeof(string), typeof(OpenDataGrid),
                new PropertyMetadata(null));

        public static readonly DependencyProperty RowBackgroundBindingPathProperty =
            DependencyProperty.Register(nameof(RowBackgroundBindingPath), typeof(string), typeof(OpenDataGrid),
                new PropertyMetadata(null));

        public static readonly DependencyProperty RowBackgroundSelectorProperty =
            DependencyProperty.Register(nameof(RowBackgroundSelector), typeof(Func<object, Brush>), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnRowBackgroundSelectorChanged));

        public static readonly DependencyProperty FocusedColumnIndexProperty =
            DependencyProperty.Register(nameof(FocusedColumnIndex), typeof(int), typeof(OpenDataGrid),
                new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty FocusedColumnHeaderProperty =
            DependencyProperty.Register(nameof(FocusedColumnHeader), typeof(string), typeof(OpenDataGrid),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SelectedColumnProperty =
            DependencyProperty.Register(nameof(SelectedColumn), typeof(string), typeof(OpenDataGrid),
                new PropertyMetadata(null));

        /// <summary>
        /// When <c>true</c> and <see cref="SelectedColumn"/> is set, every time the grid receives
        /// keyboard focus the focused column is reset to the column named by <see cref="SelectedColumn"/>.
        /// The focused row is preserved. Only active in <see cref="NavigationMode.Cell"/> mode.
        /// Default: <c>false</c>.
        /// </summary>
        public static readonly DependencyProperty RestoreDefaultColumnOnFocusProperty =
            DependencyProperty.Register(nameof(RestoreDefaultColumnOnFocus), typeof(bool), typeof(OpenDataGrid),
                new PropertyMetadata(false));

        /// <summary>
        /// Command executed when the user presses Enter while the grid is not in edit mode.
        /// The command parameter is <see cref="SelectedItem"/>.
        /// When set, Enter no longer starts cell editing — use F2 instead.
        /// </summary>
        public static readonly DependencyProperty EnterCommandProperty =
            DependencyProperty.Register(nameof(EnterCommand), typeof(System.Windows.Input.ICommand), typeof(OpenDataGrid),
                new PropertyMetadata(null));

        /// <summary>
        /// The list of checked rows, independent from <see cref="SelectedItem"/>.
        /// The VM typically provides an <c>ObservableCollection&lt;T&gt;</c>; the grid
        /// adds/removes items from it when the user toggles rows.
        /// Must be non-null for any toggle action (Space / LPM click / <see cref="ToggleChecked"/>) to take effect.
        /// </summary>
        public static readonly DependencyProperty CheckedItemsProperty =
            DependencyProperty.Register(nameof(CheckedItems), typeof(IList), typeof(OpenDataGrid),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnCheckedItemsChanged));

        /// <summary>
        /// Key that toggles the checked state of the focused row.
        /// Default: <see cref="Key.None"/> (disabled). Set explicitly to e.g. <see cref="Key.Space"/> to enable.
        /// </summary>
        public static readonly DependencyProperty ToggleCheckedKeyProperty =
            DependencyProperty.Register(nameof(ToggleCheckedKey), typeof(Key), typeof(OpenDataGrid),
                new PropertyMetadata(Key.None));

        /// <summary>Foreground brush for checked rows. Default: <see cref="Brushes.Red"/>.</summary>
        public static readonly DependencyProperty CheckedRowForegroundProperty =
            DependencyProperty.Register(nameof(CheckedRowForeground), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(Brushes.Red, OnCheckedRowStyleChanged));

        /// <summary>Background brush for checked rows. Default: <c>null</c> (no background override).</summary>
        public static readonly DependencyProperty CheckedRowBackgroundProperty =
            DependencyProperty.Register(nameof(CheckedRowBackground), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(null, OnCheckedRowStyleChanged));

        /// <summary>Font weight for checked rows. Default: <see cref="FontWeights.Bold"/>. Set <c>null</c> to inherit default weight.</summary>
        public static readonly DependencyProperty CheckedRowFontWeightProperty =
            DependencyProperty.Register(nameof(CheckedRowFontWeight), typeof(FontWeight?), typeof(OpenDataGrid),
                new PropertyMetadata(FontWeights.Bold, OnCheckedRowStyleChanged));

        private static readonly Brush DefaultGridLinesBrush = CreateFrozenBrush(0x77, 0x77, 0x77);

        private static Brush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public static readonly DependencyProperty GridLinesBrushProperty =
            DependencyProperty.Register(nameof(GridLinesBrush), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(DefaultGridLinesBrush, OnGridLinesChanged));

        public static readonly DependencyProperty GridLinesVisibilityProperty =
            DependencyProperty.Register(nameof(GridLinesVisibility), typeof(GridLinesVisibility), typeof(OpenDataGrid),
                new PropertyMetadata(GridLinesVisibility.Horizontal, OnGridLinesChanged));

        public bool LiveEdit { get; set; } = true;

        // ── Theme ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Visual theme for the grid.
        /// <list type="bullet">
        ///   <item><see cref="Models.GridTheme.Light"/> (default) — identical to the look before this feature; no changes to existing brushes.</item>
        ///   <item><see cref="Models.GridTheme.Dark"/> — professional dark palette (VS Code-inspired).</item>
        ///   <item><see cref="Models.GridTheme.System"/> — follows the Windows app color mode and reacts to changes at runtime.</item>
        /// </list>
        /// Explicitly set brush DPs (via XAML or code) always take priority over the theme.
        /// </summary>
        public static readonly DependencyProperty ThemeProperty =
            DependencyProperty.Register(nameof(Theme), typeof(Models.GridTheme), typeof(OpenDataGrid),
                new PropertyMetadata(Models.GridTheme.Light, OnThemeChanged));

        // Chrome DPs — consumed by ControlTemplate via TemplateBinding.
        // Defaults match today's hardcoded values → Theme=Light produces identical output.

        /// <summary>Background of the header strip (behind header cells). Default: #F0F0F0.</summary>
        public static readonly DependencyProperty HeaderAreaBackgroundProperty =
            DependencyProperty.Register(nameof(HeaderAreaBackground), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(Rendering.ThemePalette.Light.HeaderAreaBackground));

        /// <summary>Background of the per-column filter row strip. Default: #FAFAFA.</summary>
        public static readonly DependencyProperty FilterRowBackgroundProperty =
            DependencyProperty.Register(nameof(FilterRowBackground), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(Rendering.ThemePalette.Light.FilterRowBackground));

        /// <summary>Border colour used for chrome separators (header/filter bottom border, outer border). Default: #CCCCCC.</summary>
        public static readonly DependencyProperty ChromeBorderBrushProperty =
            DependencyProperty.Register(nameof(ChromeBorderBrush), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(Rendering.ThemePalette.Light.ChromeBorder));

        /// <summary>Background of the semi-transparent loading overlay. Default: #30FFFFFF.</summary>
        public static readonly DependencyProperty LoadingOverlayBackgroundProperty =
            DependencyProperty.Register(nameof(LoadingOverlayBackground), typeof(Brush), typeof(OpenDataGrid),
                new PropertyMetadata(Rendering.ThemePalette.Light.LoadingOverlay));

        public static readonly DependencyProperty LoadingMessageProperty =
            DependencyProperty.Register(nameof(LoadingMessage), typeof(string), typeof(OpenDataGrid),
                new PropertyMetadata(GetLocalizedLoadingMessage()));

        public static readonly DependencyProperty LoadingIndicatorModeProperty =
            DependencyProperty.Register(nameof(LoadingIndicatorMode), typeof(Models.LoadingIndicatorMode), typeof(OpenDataGrid),
                new PropertyMetadata(Models.LoadingIndicatorMode.OverlayOnly));

        public static readonly DependencyProperty FilterRowHeightProperty =
            DependencyProperty.Register(nameof(FilterRowHeight), typeof(double), typeof(OpenDataGrid),
                new PropertyMetadata(26.0));

        private static string GetLocalizedLoadingMessage()
        {
            switch (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            {
                case "pl": return "Ładowanie...";
                case "uk": return "Завантаження...";
                default:   return "Loading...";
            }
        }

        #endregion

        #region CLR Properties

        public IList DataSource { get => (IList)GetValue(DataSourceProperty); set => SetValue(DataSourceProperty, value); }
        public object SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
        public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
        public bool AutoScrollToSelectedItem { get => (bool)GetValue(AutoScrollToSelectedItemProperty); set => SetValue(AutoScrollToSelectedItemProperty, value); }
        public bool RequireSelection { get => (bool)GetValue(RequireSelectionProperty); set => SetValue(RequireSelectionProperty, value); }
        public bool ShowEmptyPlaceholderRow { get => (bool)GetValue(ShowEmptyPlaceholderRowProperty); set => SetValue(ShowEmptyPlaceholderRowProperty, value); }
        public bool AllowColumnReorder { get => (bool)GetValue(AllowColumnReorderProperty); set => SetValue(AllowColumnReorderProperty, value); }
        public FreezableCollection<OpenDataGridColumn> Columns { get => (FreezableCollection<OpenDataGridColumn>)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
        public string HiddenColumns { get => (string)GetValue(HiddenColumnsProperty); set => SetValue(HiddenColumnsProperty, value); }
        public bool HiddenColumnsByHeader { get => (bool)GetValue(HiddenColumnsByHeaderProperty); set => SetValue(HiddenColumnsByHeaderProperty, value); }
        public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
        public double RowHeight { get => (double)GetValue(RowHeightProperty); set => SetValue(RowHeightProperty, value); }
        public double HeaderHeight { get => (double)GetValue(HeaderHeightProperty); set => SetValue(HeaderHeightProperty, value); }
        public HorizontalAlignment HeaderHorizontalAlignment { get => (HorizontalAlignment)GetValue(HeaderHorizontalAlignmentProperty); set => SetValue(HeaderHorizontalAlignmentProperty, value); }
        public FontFamily HeaderFontFamily { get => (FontFamily)GetValue(HeaderFontFamilyProperty); set => SetValue(HeaderFontFamilyProperty, value); }
        public double? HeaderFontSize { get => (double?)GetValue(HeaderFontSizeProperty); set => SetValue(HeaderFontSizeProperty, value); }
        public FontWeight HeaderFontWeight { get => (FontWeight)GetValue(HeaderFontWeightProperty); set => SetValue(HeaderFontWeightProperty, value); }
        public Brush HeaderBackground { get => (Brush)GetValue(HeaderBackgroundProperty); set => SetValue(HeaderBackgroundProperty, value); }
        public Brush HeaderForeground { get => (Brush)GetValue(HeaderForegroundProperty); set => SetValue(HeaderForegroundProperty, value); }
        public Brush ColumnSeparatorBrush { get => (Brush)GetValue(ColumnSeparatorBrushProperty); set => SetValue(ColumnSeparatorBrushProperty, value); }
        public FontFamily CellFontFamily { get => (FontFamily)GetValue(CellFontFamilyProperty); set => SetValue(CellFontFamilyProperty, value); }
        public double? CellFontSize { get => (double?)GetValue(CellFontSizeProperty); set => SetValue(CellFontSizeProperty, value); }
        public FontWeight? CellFontWeight { get => (FontWeight?)GetValue(CellFontWeightProperty); set => SetValue(CellFontWeightProperty, value); }
        public Brush AlternateRowBackground { get => (Brush)GetValue(AlternateRowBackgroundProperty); set => SetValue(AlternateRowBackgroundProperty, value); }
        public Brush RowBackground { get => (Brush)GetValue(RowBackgroundProperty); set => SetValue(RowBackgroundProperty, value); }
        public double AlternateRowShift { get => (double)GetValue(AlternateRowShiftProperty); set => SetValue(AlternateRowShiftProperty, value); }
        public Brush SelectionBrush { get => (Brush)GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
        public Brush SelectionForeground { get => (Brush)GetValue(SelectionForegroundProperty); set => SetValue(SelectionForegroundProperty, value); }
        public NavigationMode NavigationMode { get => (NavigationMode)GetValue(NavigationModeProperty); set => SetValue(NavigationModeProperty, value); }
public string RowReadOnlyBindingPath { get => (string)GetValue(RowReadOnlyBindingPathProperty); set => SetValue(RowReadOnlyBindingPathProperty, value); }
        public string RowBackgroundBindingPath { get => (string)GetValue(RowBackgroundBindingPathProperty); set => SetValue(RowBackgroundBindingPathProperty, value); }
        public Func<object, Brush> RowBackgroundSelector { get => (Func<object, Brush>)GetValue(RowBackgroundSelectorProperty); set => SetValue(RowBackgroundSelectorProperty, value); }
        /// <summary>Index aktywnej kolumny (bindowalny).</summary>
        public int FocusedColumnIndex { get => (int)GetValue(FocusedColumnIndexProperty); set => SetValue(FocusedColumnIndexProperty, value); }
        /// <summary>Nagłówek aktywnej kolumny (bindowalny, read-only z perspektywy użytkownika).</summary>
        public string FocusedColumnHeader { get => (string)GetValue(FocusedColumnHeaderProperty); internal set => SetValue(FocusedColumnHeaderProperty, value); }

        /// <summary>
        /// Header albo BindingPath kolumny, na której pojawi się domyślna selekcja komórki
        /// przy pierwszym uzyskaniu keyboard focusu (tryb <see cref="NavigationMode.Cell"/>).
        /// Jeśli brak dopasowania lub puste — fallback na pierwszą widoczną kolumnę.
        /// </summary>
        public string SelectedColumn { get => (string)GetValue(SelectedColumnProperty); set => SetValue(SelectedColumnProperty, value); }
        public bool RestoreDefaultColumnOnFocus { get => (bool)GetValue(RestoreDefaultColumnOnFocusProperty); set => SetValue(RestoreDefaultColumnOnFocusProperty, value); }
        public System.Windows.Input.ICommand EnterCommand { get => (System.Windows.Input.ICommand)GetValue(EnterCommandProperty); set => SetValue(EnterCommandProperty, value); }
        public IList CheckedItems { get => (IList)GetValue(CheckedItemsProperty); set => SetValue(CheckedItemsProperty, value); }
        public Key ToggleCheckedKey { get => (Key)GetValue(ToggleCheckedKeyProperty); set => SetValue(ToggleCheckedKeyProperty, value); }
        public Brush CheckedRowForeground { get => (Brush)GetValue(CheckedRowForegroundProperty); set => SetValue(CheckedRowForegroundProperty, value); }
        public Brush CheckedRowBackground { get => (Brush)GetValue(CheckedRowBackgroundProperty); set => SetValue(CheckedRowBackgroundProperty, value); }
        public FontWeight? CheckedRowFontWeight { get => (FontWeight?)GetValue(CheckedRowFontWeightProperty); set => SetValue(CheckedRowFontWeightProperty, value); }
        public Brush GridLinesBrush { get => (Brush)GetValue(GridLinesBrushProperty); set => SetValue(GridLinesBrushProperty, value); }
        public GridLinesVisibility GridLinesVisibility { get => (GridLinesVisibility)GetValue(GridLinesVisibilityProperty); set => SetValue(GridLinesVisibilityProperty, value); }
        /// <summary>Text shown in the loading overlay while filter/sort/rebuild operations are in progress. Default: "Loading..."</summary>
        public string LoadingMessage { get => (string)GetValue(LoadingMessageProperty); set => SetValue(LoadingMessageProperty, value); }
        /// <summary>Controls which elements are visible during loading (filter/sort/rebuild). Default: OverlayOnly.</summary>
        public Models.LoadingIndicatorMode LoadingIndicatorMode { get => (Models.LoadingIndicatorMode)GetValue(LoadingIndicatorModeProperty); set => SetValue(LoadingIndicatorModeProperty, value); }
        /// <summary>Height of the per-column filter row shown under the header when any column has FilteringEnabled=true. Default: 26.</summary>
        public double FilterRowHeight { get => (double)GetValue(FilterRowHeightProperty); set => SetValue(FilterRowHeightProperty, value); }

        /// <summary>Visual theme: Light (default), Dark, or System (follows Windows app color mode, reacts live).</summary>
        public Models.GridTheme Theme { get => (Models.GridTheme)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }

        /// <summary>Background of the header strip. Managed by the theme; overridable.</summary>
        public Brush HeaderAreaBackground { get => (Brush)GetValue(HeaderAreaBackgroundProperty); set => SetValue(HeaderAreaBackgroundProperty, value); }
        /// <summary>Background of the filter row strip. Managed by the theme; overridable.</summary>
        public Brush FilterRowBackground { get => (Brush)GetValue(FilterRowBackgroundProperty); set => SetValue(FilterRowBackgroundProperty, value); }
        /// <summary>Chrome border colour (header bottom, filter bottom, outer border). Managed by the theme; overridable.</summary>
        public Brush ChromeBorderBrush { get => (Brush)GetValue(ChromeBorderBrushProperty); set => SetValue(ChromeBorderBrushProperty, value); }
        /// <summary>Loading overlay background. Managed by the theme; overridable.</summary>
        public Brush LoadingOverlayBackground { get => (Brush)GetValue(LoadingOverlayBackgroundProperty); set => SetValue(LoadingOverlayBackgroundProperty, value); }

        #endregion

        static OpenDataGrid()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(OpenDataGrid),
                new FrameworkPropertyMetadata(typeof(OpenDataGrid)));
            FocusVisualStyleProperty.OverrideMetadata(typeof(OpenDataGrid),
                new FrameworkPropertyMetadata(null));
        }

        public OpenDataGrid()
        {
            _workCoordinator = new GridWorkCoordinator(() => LoadingIndicatorMode);
            _selection = new SelectionFocusSynchronizer(this);
            _headerPresenter = new HeaderPresenter(this);
            _filterRowPresenter = new FilterRowPresenter(this);
            Columns = new FreezableCollection<OpenDataGridColumn>();
            Focusable = true;
            Unloaded += OnGridUnloaded;
            Loaded   += OnGridLoaded;
        }

        // Ensure columns participate in the logical tree so they inherit DataContext
        protected override System.Collections.IEnumerator LogicalChildren
        {
            get
            {
                var cols = Columns;
                if (cols != null)
                    foreach (var col in cols)
                        yield return col;
            }
        }

        #region Template

        public override void OnApplyTemplate()
        {
            if (_vScrollBar != null)
                _vScrollBar.Scroll -= OnVerticalScroll;
            if (_hScrollBar != null)
                _hScrollBar.Scroll -= OnHorizontalScroll;
            if (_canvas != null)
            {
                _canvas.SizeChanged -= OnCanvasSizeChanged;
                _canvas.MouseLeftButtonDown -= _selection.OnCanvasMouseDown;
            }

            _headerPresenter.DetachVisibilitySubscriptions();

            _sortEngine.SortCompleted -= OnSortCompleted;
            _sortEngine.SortCancelled -= OnSortCancelled;
            _navEngine.NavigationRequested -= OnNavigationRequested;
            _navEngine.EditRequested -= OnEditRequested;
            _navEngine.EditCommitRequested -= OnEditCommitRequested;
            _navEngine.EditCancelRequested -= OnEditCancelRequested;
            _navEngine.IsAtEdgeForNavigation = null;
            _navEngine.ShouldAbsorbNavigationKeys = null;

            base.OnApplyTemplate();

            _canvas = GetTemplateChild(PartCanvas) as Canvas;
            _headerPresenter.SetHeaderCanvas(GetTemplateChild(PartHeaderCanvas) as Canvas);
            _filterRowPresenter.SetTemplateParts(
                GetTemplateChild(PartFilterRowContainer) as FrameworkElement,
                GetTemplateChild(PartFilterRowCanvas) as Canvas);
            _vScrollBar = GetTemplateChild(PartVerticalScrollBar) as ScrollBar;
            _hScrollBar = GetTemplateChild(PartHorizontalScrollBar) as ScrollBar;
            _workCoordinator.SetTemplateParts(
                GetTemplateChild(PartLoadingOverlay) as FrameworkElement,
                GetTemplateChild(PartLoadingBar) as FrameworkElement);

            if (_vScrollBar != null)
                _vScrollBar.Scroll += OnVerticalScroll;
            if (_hScrollBar != null)
                _hScrollBar.Scroll += OnHorizontalScroll;
            if (_canvas != null)
            {
                _canvas.SizeChanged += OnCanvasSizeChanged;
                _canvas.MouseLeftButtonDown += _selection.OnCanvasMouseDown;
            }

            _sortEngine.SortCompleted += OnSortCompleted;
            _sortEngine.SortCancelled += OnSortCancelled;
            _navEngine.NavigationRequested += OnNavigationRequested;
            _navEngine.EditRequested += OnEditRequested;
            _navEngine.EditCommitRequested += OnEditCommitRequested;
            _navEngine.EditCancelRequested += OnEditCancelRequested;
            _navEngine.IsAtEdgeForNavigation = key => _editingCell?.Column.IsAtEdge(_editingCell.EditElement, key) ?? false;
            _navEngine.ShouldAbsorbNavigationKeys = () => _editingCell?.Column.AbsorbNavigationKeysInEdit ?? false;

            _lifecycle.OnTemplateApplied();
            TryInitialize();
        }

        /// <summary>
        /// Detaches <see cref="INotifyCollectionChanged.CollectionChanged"/> when the grid leaves
        /// the visual tree, preventing the DataSource from keeping the grid alive via a strong
        /// delegate reference. DPD column subscriptions and sort-engine events are <em>not</em>
        /// touched here — they are managed by <see cref="OnApplyTemplate"/> (sort events) and
        /// <see cref="BuildHeaders"/> / <see cref="RebuildColumnVisibilitySubscriptions"/> (DPD),
        /// so there is no risk of double-subscription on reload.
        /// </summary>
        private void OnGridUnloaded(object sender, RoutedEventArgs e)
        {
            _lifecycle.OnGridUnloaded();

            // Detach static SystemEvents subscription to prevent memory leaks
            // (SystemEvents holds a strong reference to event handlers).
            if (Theme == Models.GridTheme.System)
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            if (DataSource is System.Collections.Specialized.INotifyCollectionChanged ncc)
                ncc.CollectionChanged -= OnCollectionChanged;

            if (CheckedItems is INotifyCollectionChanged checkedNcc)
                checkedNcc.CollectionChanged -= OnCheckedItemsCollectionChanged;

            // Invalidate all in-flight async EndPendingWork calls by bumping the epoch.
            // Any pending Dispatcher.BeginInvoke / Dispatcher.Invoke callbacks that captured
            // the old epoch will call EndPendingWork(stale_epoch) and return immediately —
            // they are genuine no-ops, not accidental ones rescued by a ≤ 0 guard.
            _filterRowPresenter.Detach();
            _filterCts?.Cancel();
            _filterCts?.Dispose();
            _filterCts = null;
            // Cancel any in-progress background sort so it does not publish stale results after unload.
            _sortEngine.CancelPendingSort();
            _workCoordinator.Reset();
            // Reset so SelectedColumn DP is re-applied on first keyboard focus after reload.
            // Without this the grid would remember "focus was already established" from the
            // previous session and skip the default-column heuristic in ApplyDefaultCellSelectionIfNeeded.
            _selection.ResetCellFocusApplied();
        }

        /// <summary>
        /// Re-attaches subscriptions that were released in <see cref="OnGridUnloaded"/> when the
        /// grid is added back to the visual tree. Only runs after a prior Unloaded event.
        /// </summary>
        private void OnGridLoaded(object sender, RoutedEventArgs e)
        {
            if (!_lifecycle.IsUnloaded) return;
            _lifecycle.OnGridLoaded();

            // Re-attach SystemEvents subscription if grid was in System mode when unloaded.
            if (Theme == Models.GridTheme.System)
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // Re-attach CollectionChanged so we track data mutations again.
            if (DataSource is System.Collections.Specialized.INotifyCollectionChanged ncc)
                ncc.CollectionChanged += OnCollectionChanged;

            if (CheckedItems is INotifyCollectionChanged checkedNcc)
                checkedNcc.CollectionChanged += OnCheckedItemsCollectionChanged;

            // Re-initialize: rebuilds DPD subscriptions, resyncs scroll/selection/filter state.
            TryInitialize();
        }

        #endregion

        #region Pending Work Overlay

        internal void NotifyFilterPending(object source) => _workCoordinator.NotifyFilterPending(source);
        internal void NotifyFilterReady(object source)   => _workCoordinator.NotifyFilterReady(source);

        #endregion

        #region CheckedItems

        private static void OnCheckedRowStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;
            if (!grid.IsGridInitialized) return;
            grid._rowEngine.UpdateCheckedRowStyle(
                grid.CheckedRowForeground, grid.CheckedRowBackground, grid.CheckedRowFontWeight);
        }

        private static void OnCheckedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;

            if (e.OldValue is INotifyCollectionChanged oldNcc)
                oldNcc.CollectionChanged -= grid.OnCheckedItemsCollectionChanged;

            grid._checkedSet.Clear();

            var newList = e.NewValue as IList;
            if (newList != null)
            {
                foreach (var item in newList)
                    if (item != null && grid.DataSourceContains(item)) grid._checkedSet.Add(item);

                if (e.NewValue is INotifyCollectionChanged newNcc)
                    newNcc.CollectionChanged += grid.OnCheckedItemsCollectionChanged;
            }

            // Ensure CheckColumns have the grid reference even before full initialization.
            grid.AttachCheckColumns();

            if (grid.IsGridInitialized)
            {
                grid._rowEngine.RefreshAllCheckedStates(grid.IsItemChecked);
                grid.RefreshAllCheckColumnCells();
            }
        }

        private void OnCheckedItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_internalCheckedChange) return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        foreach (var item in e.NewItems)
                            if (item != null && DataSourceContains(item)) _checkedSet.Add(item);
                        if (IsGridInitialized)
                        {
                            foreach (var item in e.NewItems)
                            {
                                _rowEngine.UpdateCheckedStateForItem(item, true);
                                RefreshCheckColumnCellsForItem(item);
                            }
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (var item in e.OldItems)
                            if (item != null) _checkedSet.Remove(item);
                        if (IsGridInitialized)
                        {
                            foreach (var item in e.OldItems)
                            {
                                _rowEngine.UpdateCheckedStateForItem(item, false);
                                RefreshCheckColumnCellsForItem(item);
                            }
                        }
                    }
                    break;

                default:
                    // Reset / Replace — rebuild the set and refresh all check cells and states
                    _checkedSet.Clear();
                    var list = CheckedItems;
                    if (list != null)
                        foreach (var item in list)
                            if (item != null && DataSourceContains(item)) _checkedSet.Add(item);
                    if (IsGridInitialized)
                    {
                        _rowEngine.RefreshAllCheckedStates(IsItemChecked);
                        RefreshAllCheckColumnCells();
                    }
                    break;
            }
        }

        /// <summary>Returns true if <paramref name="item"/> is checked.
        /// In dual-source mode (CheckedItems set) reads from the internal HashSet.
        /// In pure-bound mode (CheckedItems null, BindingPath set) reads from the model field.</summary>
        internal bool IsItemChecked(object item)
        {
            if (item == null) return false;
            if (CheckedItems != null) return _checkedSet.Contains(item);
            return _boundCheckGetter?.Invoke(item) ?? false;
        }

        private bool DataSourceContains(object item)
        {
            var ds = DataSource;
            return ds != null && ds.Contains(item);
        }

        /// <summary>
        /// Returns the distinct union of <see cref="CheckedItems"/> and <see cref="SelectedItem"/>.
        /// Useful as the effective target set for bulk operations: if nothing is checked the selected
        /// row is still included; if items are checked they form the full target regardless of selection.
        /// Computed on-the-fly — call it immediately before the operation, do not cache the result.
        /// </summary>
        public IReadOnlyList<object> MarkedItems
        {
            get
            {
                // Build an explicit local set from the current DataSource so we filter against
                // what is actually present right now — not against _checkedSet consistency.
                var ds = DataSource;
                var dsSet = new HashSet<object>(ObjectReferenceEqualityComparer.Default);
                if (ds != null)
                    foreach (var item in ds)
                        if (item != null) dsSet.Add(item);

                var selected = SelectedItem;
                bool selectedIsValid = selected != null && dsSet.Contains(selected);

                if (_checkedSet.Count == 0)
                    return selectedIsValid ? new[] { selected } : (IReadOnlyList<object>)Array.Empty<object>();

                var result = new List<object>(_checkedSet.Count + 1);
                var seen = new HashSet<object>(ObjectReferenceEqualityComparer.Default);
                var list = CheckedItems;
                if (list != null)
                    foreach (var item in list)
                        if (item != null && dsSet.Contains(item) && seen.Add(item)) result.Add(item);

                if (selectedIsValid && seen.Add(selected))
                    result.Add(selected);

                return result;
            }
        }

        /// <summary>
        /// Toggles the checked state of <paramref name="item"/>.
        /// <para>
        /// When <paramref name="sourceColumn"/> is provided, only that column's
        /// <see cref="Columns.OpenDataGridColumn.BindingPath"/> is written (two-way sync for that
        /// specific column). When <paramref name="sourceColumn"/> is <c>null</c> (e.g. keyboard
        /// toggle without a focused CheckColumn), every bound CheckColumn's field is updated.
        /// Passing an unbound column (no BindingPath) skips the field write entirely.
        /// </para>
        /// <para>
        /// When <see cref="CheckedItems"/> is non-null, the item is added/removed from that collection
        /// and the internal HashSet (dual-source mode). Row-level checked styling is active.
        /// When <see cref="CheckedItems"/> is null and at least one bound CheckColumn exists,
        /// only the model field(s) are written (pure-bound mode). Row-level checked styling is inactive.
        /// When <see cref="CheckedItems"/> is null and no bound CheckColumn exists, this is a no-op.
        /// </para>
        /// </summary>
        internal void ToggleChecked(object item, Columns.OpenDataGridCheckColumn sourceColumn = null)
        {
            if (item == null) return;
            var list = CheckedItems;

            if (list == null)
            {
                // Pure-bound path: no CheckedItems/checkedSet — write model field(s) only.
                var stateColumn = sourceColumn ?? FindFirstBoundCheckColumn();
                if (stateColumn == null || string.IsNullOrEmpty(stateColumn.BindingPath)) return;
                var rawBool   = Accessors.PropertyAccessorCache.GetValue(item, stateColumn.BindingPath);
                bool current  = rawBool is bool bv ? bv : (rawBool as bool?) == true;
                SyncCheckedStateToBindingPath(item, !current, sourceColumn);
                if (IsGridInitialized)
                    _rowEngine.UpdateCheckedStateForItem(item, !current);
                RefreshCheckColumnCellsForItem(item);
                return;
            }

            _internalCheckedChange = true;
            try
            {
                bool wasChecked = _checkedSet.Contains(item);
                if (wasChecked)
                {
                    _checkedSet.Remove(item);
                    // Remove every occurrence — VM may have inserted duplicates and a single
                    // Remove() leaves the item visible in CheckedItems while _checkedSet says
                    // unchecked, breaking MarkedItems / IsItemChecked invariants.
                    // IList.Remove returns void, so check Contains to loop safely.
                    while (list.Contains(item)) list.Remove(item);
                }
                else
                {
                    _checkedSet.Add(item);
                    list.Add(item);
                }
                SyncCheckedStateToBindingPath(item, !wasChecked, sourceColumn);
            }
            finally
            {
                _internalCheckedChange = false;
            }

            if (IsGridInitialized)
                _rowEngine.UpdateCheckedStateForItem(item, _checkedSet.Contains(item));
            RefreshCheckColumnCellsForItem(item);
        }

        /// <summary>
        /// Writes the new checked value to the BindingPath field(s) on <paramref name="item"/>.
        /// When <paramref name="sourceColumn"/> is non-null, only that column's path is written.
        /// When null (keyboard row-level toggle), every bound CheckColumn is written.
        /// Columns without a BindingPath are skipped in both cases.
        /// </summary>
        private void SyncCheckedStateToBindingPath(object item, bool isChecked,
            Columns.OpenDataGridCheckColumn sourceColumn = null)
        {
            // Use all columns (not just visible) so hidden bound CheckColumns are still written.
            // _propertySetters is indexed by visible-column position and cannot be used here.
            var columns = GetAllColumnList();
            for (int i = 0; i < columns.Count; i++)
            {
                if (!(columns[i] is Columns.OpenDataGridCheckColumn cc)) continue;
                var path = cc.BindingPath;
                if (string.IsNullOrEmpty(path)) continue;

                // When a specific column triggered the toggle, only write to that column.
                // This prevents an unbound-column click from modifying bound-column fields.
                if (sourceColumn != null && !ReferenceEquals(cc, sourceColumn)) continue;

                Accessors.PropertyAccessorCache.CreateSetter(path)?.Invoke(item, isChecked);
            }
        }

        private void AttachCheckColumns()
        {
            foreach (var col in GetAllColumnList())
            {
                if (col is Columns.OpenDataGridCheckColumn cc)
                    cc.AttachGrid(this);
            }
        }

        private Columns.OpenDataGridCheckColumn FindFirstBoundCheckColumn()
        {
            var cols = GetAllColumnList();
            for (int i = 0; i < cols.Count; i++)
                if (cols[i] is Columns.OpenDataGridCheckColumn cc && !string.IsNullOrEmpty(cc.BindingPath))
                    return cc;
            return null;
        }

        internal bool HasAnyBoundCheckColumn() => FindFirstBoundCheckColumn() != null;

        private void RefreshCheckColumnCellsForItem(object item)
        {
            if (!IsGridInitialized || item == null) return;
            if (_checkColumnIndices.Length > 0)
                _rowEngine.RefreshSpecificCellsForItem(item, _checkColumnIndices, _propertyGetters);
        }

        private void RefreshAllCheckColumnCells()
        {
            if (_checkColumnIndices.Length > 0)
                _rowEngine.RefreshAllCellValuesForColumns(_checkColumnIndices, _propertyGetters);
        }

        #endregion

        #region Initialization

        private static void OnDataSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;

            if (e.OldValue is INotifyCollectionChanged oldCol)
                oldCol.CollectionChanged -= grid.OnCollectionChanged;

            // Attach CollectionChanged only when the grid is in the visual tree.
            // If the grid is currently Unloaded, OnGridLoaded re-attaches the subscription
            // to whatever DataSource is current at that point — subscribing here would cause
            // a double subscription once the grid is reloaded.
            if (!grid._lifecycle.IsUnloaded && e.NewValue is INotifyCollectionChanged newCol)
                newCol.CollectionChanged += grid.OnCollectionChanged;

            // Rebuild the internal checked-set from scratch as the intersection of the VM-owned
            // CheckedItems and the new DataSource. Using IntersectWith would miss items that were
            // added to CheckedItems before DataSource was set (pre-binding scenario) and would
            // fail to reject them properly when DataSource arrives. Full rebuild ensures correctness
            // in both directions: stale items from old DataSource are dropped, and pre-bound items
            // that now exist in the new DataSource are picked up.
            // The VM-owned CheckedItems collection is intentionally NOT modified here — that is
            // the VM's responsibility.
            var newData = e.NewValue as IList;
            grid._checkedSet.Clear();
            if (newData != null)
            {
                var newSet = new HashSet<object>(ObjectReferenceEqualityComparer.Default);
                foreach (var item in newData)
                    if (item != null) newSet.Add(item);

                var checkedItems = grid.CheckedItems;
                if (checkedItems != null)
                    foreach (var item in checkedItems)
                        if (item != null && newSet.Contains(item)) grid._checkedSet.Add(item);
            }

            // When DataSource becomes empty, clear stale SelectedItem immediately.
            // FinishInitialize handles this for the canvas-ready path, but when
            // the grid has no visual tree yet (canvasHeight=0), that path is not reached.
            if (newData == null || newData.Count == 0)
            {
                grid._selection.PendingSelectedItem = null;
                grid._selection.ClearSelectedItem();
            }

            grid.TryInitialize();
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;
            if (grid._selection.InternalSelectionChange) return;

            if (e.NewValue == null)
            {
                grid._selection.PendingSelectedItem = null;
                if (grid.RequireSelection && grid._lifecycle.IsInitialized
                    && grid._sortEngine.SortedIndices?.Length > 0)
                    grid.ClearSelectionAndFallbackToFirst();
                return;
            }

            if (grid.DataSource == null || !grid._lifecycle.IsInitialized)
            {
                grid._selection.PendingSelectedItem = e.NewValue;
                return;
            }

            if (grid.DataSource.IndexOf(e.NewValue) < 0)
            {
                // Item not in current DataSource — save as pending; will be resolved on next DataSource change.
                grid._selection.PendingSelectedItem = e.NewValue;
                return;
            }

            grid._selection.PendingSelectedItem = null;
            grid._selection.ApplySelectedItemFromViewModel(e.NewValue);
        }

        private void ApplySelectedItemFromViewModel(object item)   => _selection.ApplySelectedItemFromViewModel(item);
        private void ClearSelectionAndFallbackToFirst()            => _selection.ClearSelectionAndFallbackToFirst();
        private void SelectByDataIndex(int dataIndex)              => _selection.SelectByDataIndex(dataIndex);

        private bool TryConsumePendingSelection()
        {
            var pending = _selection.PendingSelectedItem;
            if (pending == null || DataSource == null) return false;
            int idx = DataSource.IndexOf(pending);
            // Always clear pending — it is valid only for the next FinishInitialize/FinishApplyFilters.
            // If the item is not in the current (filtered) view, the caller falls back to RequireSelection.
            _selection.PendingSelectedItem = null;
            if (idx < 0 || _sortEngine.FindSortedPosition(idx) < 0) return false;
            _selection.ApplySelectedItemFromViewModel(pending);
            return true;
        }
        internal void ApplyCellFocus(int row, int col, bool ensure) => _selection.ApplyCellFocus(row, col, ensure);
        private void EnsureRowVisible(int sortedIndex)             => _selection.EnsureRowVisible(sortedIndex);
        private int  ResolveDefaultCellColumnIndex()               => _selection.ResolveDefaultCellColumnIndex();

        private static void OnNavigationModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;
            if (!grid._lifecycle.CanHandleInput) return;

            var newMode = (NavigationMode)e.NewValue;
            grid._navEngine.UpdateMode(newMode);

            // Re-apply focus so the row engine reflects the new mode (cell border vs row border).
            if (grid.FocusedRowSortedIndex >= 0)
                grid.ApplyCellFocus(grid.FocusedRowSortedIndex, grid.FocusedColIndex, false);
        }

        private static void OnHeaderHorizontalAlignmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((OpenDataGrid)d).ApplyHeaderHorizontalAlignmentToExistingHeaders();
        }

        private static void OnHeaderFontDefaultsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;
            grid.ApplyHeaderFontDefaultsToExistingHeaders();
            // HeaderBackground change affects auto-derived separator color.
            grid.ApplyColumnSeparatorBrushToHeaders();
        }

        private static void OnColumnSeparatorBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((OpenDataGrid)d).ApplyColumnSeparatorBrushToHeaders();
        }

        internal Brush GetEffectiveColumnSeparatorBrush()
        {
            if (ColumnSeparatorBrush != null) return ColumnSeparatorBrush;
            var headerBg = HeaderBackground ?? Controls.HeaderPresenter.DefaultHeaderBackground;
            return Rendering.BrushShift.Apply(headerBg, 0.15);
        }

        private void ApplyColumnSeparatorBrushToHeaders()
        {
            _headerPresenter.UpdateColumnSeparatorBrush();
            _filterRowPresenter.UpdateColumnSeparatorBrush();
        }

        private static void OnCellFontDefaultsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;
            if (!grid._lifecycle.CanHandleInput) return;
            grid._rowEngine.UpdateDefaultCellFont(grid.CellFontFamily, grid.CellFontSize, grid.CellFontWeight);
        }

        internal void InvalidateColumnFont(OpenDataGridColumn col)
        {
            if (!_lifecycle.CanHandleInput) return;
            var visible = _headerPresenter.GetVisibleColumnList();
            for (int i = 0; i < visible.Count; i++)
            {
                if (ReferenceEquals(visible[i], col))
                {
                    _rowEngine.InvalidateColumnFont(i);
                    return;
                }
            }
        }

        private static void OnGridLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;
            grid._gridLinePen = grid.BuildGridLinePen();
            if (grid._lifecycle.CanHandleInput)
            {
                grid._rowEngine.SetGridLinePen(grid._gridLinePen);
                grid._rowEngine.SetGridLinesVisibility(grid.GridLinesVisibility);
            }
        }

        private Pen BuildGridLinePen()
        {
            if (GridLinesVisibility == GridLinesVisibility.None) return null;
            var brush = GridLinesBrush ?? DefaultGridLinesBrush;
            var pen = new Pen(brush, 1.0);
            pen.Freeze();
            return pen;
        }

        // ── Theme engine ─────────────────────────────────────────────────────────────

        private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;
            var prev = (Models.GridTheme)e.OldValue;
            var next = (Models.GridTheme)e.NewValue;

            // Manage SystemEvents subscription — subscribe only when Theme == System.
            // Guard with IsUnloaded: OnGridLoaded handles the subscribe when the grid re-enters the tree,
            // so subscribing here while Unloaded would create a duplicate when OnGridLoaded also subscribes.
            if (prev == Models.GridTheme.System)
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= grid.OnUserPreferenceChanged;
            if (next == Models.GridTheme.System && !grid._lifecycle.IsUnloaded)
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += grid.OnUserPreferenceChanged;

            grid.ApplyTheme();
        }

        private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            // Fired from a background thread; marshal to UI thread.
            if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
                Dispatcher.BeginInvoke(new Action(ApplyTheme), System.Windows.Threading.DispatcherPriority.Normal);
        }

        /// <summary>
        /// Applies the resolved theme (Light or Dark) to all managed DPs and chrome resources,
        /// honouring any explicit user overrides.
        /// </summary>
        private void ApplyTheme()
        {
            bool isDark = Theme == Models.GridTheme.Dark ||
                          (Theme == Models.GridTheme.System && _isDarkSystemTheme());

            var palette = isDark ? Rendering.ThemePalette.Dark : null; // null = revert to defaults

            // ── 1. DPs managed by the theme ──────────────────────────────────────────
            // Includes Control.Background, Control.BorderBrush, Control.Foreground and all grid brush DPs.

            if (palette == null)
            {
                // Light: clear all values the theme previously wrote.
                foreach (var dp in _themeWritten)
                    ClearValue(dp);
                _themeWritten.Clear();

                // Revert chrome dynamic resources.
                Resources.Remove(ScrollBarTrackKey);
                Resources.Remove(ScrollBarThumbKey);
                Resources.Remove(ScrollBarThumbHoverKey);
                Resources.Remove(ScrollBarThumbDragKey);

                // Revert drop-indicator to default.
                _headerPresenter.DropIndicatorBrush = null;
            }
            else
            {
                // Dark: write each palette value unless the user has an explicit local override.
                ApplyThemeDp(BackgroundProperty,             palette.ControlBackground);
                ApplyThemeDp(BorderBrushProperty,           palette.ChromeBorder);
                ApplyThemeDp(ForegroundProperty,            palette.Foreground);
                ApplyThemeDp(HeaderBackgroundProperty,      palette.HeaderBackground);
                ApplyThemeDp(HeaderForegroundProperty,      palette.HeaderForeground);
                ApplyThemeDp(ColumnSeparatorBrushProperty,  palette.ColumnSeparator);
                ApplyThemeDp(AlternateRowBackgroundProperty,palette.AlternateRowBackground);
                ApplyThemeDp(RowBackgroundProperty,         palette.RowBackground);
                ApplyThemeDp(SelectionBrushProperty,        palette.SelectionBrush);
                ApplyThemeDp(GridLinesBrushProperty,        palette.GridLines);
                ApplyThemeDp(HeaderAreaBackgroundProperty,  palette.HeaderAreaBackground);
                ApplyThemeDp(FilterRowBackgroundProperty,   palette.FilterRowBackground);
                ApplyThemeDp(ChromeBorderBrushProperty,     palette.ChromeBorder);
                ApplyThemeDp(LoadingOverlayBackgroundProperty, palette.LoadingOverlay);

                // Override scrollbar dynamic resources.
                Resources[ScrollBarTrackKey]      = palette.ScrollBarTrack;
                Resources[ScrollBarThumbKey]      = palette.ScrollBarThumb;
                Resources[ScrollBarThumbHoverKey] = palette.ScrollBarThumbHover;
                Resources[ScrollBarThumbDragKey]  = palette.ScrollBarThumbDrag;

                // Override drop-indicator accent.
                _headerPresenter.DropIndicatorBrush = palette.DropIndicator;
            }

            // ── 2. Propagate row-level changes not covered by existing DP callbacks ─
            // (RowBackground, AlternateRowBackground, SelectionBrush, Foreground have no callbacks)
            if (_lifecycle.CanHandleInput)
            {
                BuildRowColorResolver();
                _rowEngine.UpdateRowBrushes(
                    SelectionBrush,
                    RowBackground ?? Rendering.ThemePalette.Light.RowBackground,
                    _rowColorResolver,
                    Foreground,
                    SelectionForeground,
                    AlternateRowShift);
                // Refresh filter row cell backgrounds (reads RowBackground / HeaderBackground).
                _filterRowPresenter.UpdateRowBackground();
            }
        }

        /// <summary>
        /// Writes <paramref name="value"/> to <paramref name="dp"/> on behalf of the theme,
        /// but only when the user has NOT set an explicit local value.
        /// Also allows overwriting a value that the theme itself previously wrote.
        /// </summary>
        private void ApplyThemeDp(DependencyProperty dp, Brush value)
        {
            var local = ReadLocalValue(dp);
            bool isUnset      = ReferenceEquals(local, DependencyProperty.UnsetValue);
            bool isThemeOwned = _themeWritten.Contains(dp);

            if (isUnset || isThemeOwned)
            {
                SetValue(dp, value);
                _themeWritten.Add(dp);
            }
            // else: user has an explicit local value — leave it alone.
        }

        private static void OnHiddenColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;
            grid.ApplyHiddenColumns();
        }

        private static void OnRowBackgroundSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = (OpenDataGrid)d;
            if (grid._lifecycle.CanHandleInput)
                grid.BuildPropertyAccessors();
            // Intentionally no pool refresh — color is computed during row Bind().
        }

        private void TryInitialize()
        {
#if DEBUG
            Debug.WriteLine($"[EDG] TryInitialize: canvas={_canvas?.ActualHeight:F0} state={_lifecycle.State} DataSource={DataSource?.Count.ToString() ?? "null"} SelectedItem={SelectedItem?.GetHashCode().ToString() ?? "null"} pending={_selection.PendingSelectedItem?.GetHashCode().ToString() ?? "null"}");
#endif
            if (_canvas == null && !_isForcingTemplateApply)
            {
                _isForcingTemplateApply = true;
                try { ApplyTemplate(); }
                finally { _isForcingTemplateApply = false; }
            }
            if (_canvas == null) return;

            InvalidateColumnCaches(); // invalidate cache on every structural change
            RebuildColumnVisibilitySubscriptions();
            ApplyHiddenColumnsToColumns();

            // Transition out of Ready before inspecting columns — grid is no longer operational
            // during re-init. Without this, a column-removal that leaves 0 visible columns would
            // keep the state as Ready while no data is rendered.
            _lifecycle.BeginReinitialize();

            // Clear any stale sort arrows — sort set before DataSource arrived should not persist.
            foreach (var col in GetAllColumnList())
                col.SortDirection = Models.SortDirection.None;

            var columns = GetVisibleColumnList();
            if (columns.Count == 0) return;

            BuildPropertyAccessors();
            BuildColumnVirtualization();

            var dataSource = DataSource;
            int total = dataSource?.Count ?? 0;

            // Async path: large filtered dataset and canvas is ready — avoid blocking UI thread.
            if (_filterEngine.HasActiveFilters && dataSource != null && total > FilterAsyncThreshold
                && _canvas.ActualHeight > 0)
            {
                _lifecycle.OnAsyncInitStarted();
                var token = RefreshFilterCts();
                int gen   = System.Threading.Interlocked.Increment(ref _filterGeneration);
                var work  = _workCoordinator.BeginWork();
                System.Threading.Tasks.Task.Run(() =>
                {
                    int[] filtered;
                    try { filtered = _filterEngine.Apply(dataSource, token); }
                    catch (System.OperationCanceledException)
                    {
                        Dispatcher.BeginInvoke(new Action(() => work.Dispose()));
                        return;
                    }
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (gen != _filterGeneration) return;   // superseded
                            if (DataSource != dataSource) return;   // DS swapped mid-flight
                            // No IsInitialized check here — state is Initializing, not Ready.
                            // The generation counter is the sufficient guard against stale results.
                            _sortEngine.Initialize(filtered, dataSource.Count);
                            FinishInitialize(dataSource);
                        }
                        finally
                        {
                            work.Dispose();
                        }
                    }));
                });
                return;
            }

            // Sync path: no filter, small dataset, or canvas not yet ready (grid hidden).
            BuildSortEngineSync();
            FinishInitialize(dataSource);
        }

        /// <summary>
        /// Completes grid initialization after sort/filter indices are ready.
        /// Called from both the sync and async paths of <see cref="TryInitialize"/>.
        /// </summary>
        private void FinishInitialize(IList dataSource)
        {
            BuildHeaders();
            RebuildFilterRow();

            // Re-read canvas height — in the async path this fires after a Task.Run,
            // so the grid may have been resized or hidden in the meantime.
            double canvasHeight = _canvas?.ActualHeight ?? 0;

            if (canvasHeight > 0 && dataSource != null && dataSource.Count > 0)
            {
                InitializeRowEngine();
                InitializeNavEngine();
                _lifecycle.OnInitializationComplete(canvasReady: true, hasData: true);

                // Validate and restore selection after DataSource change:
                //   1. pending (if exists in new collection)
                //   2. current SelectedItem (if exists in new collection)
                //   3. first item in new collection (fallback — clears stale selection)
                var pending = _selection.PendingSelectedItem;
                _selection.PendingSelectedItem = null;

                object itemToSelect = null;
                if (pending != null && dataSource.IndexOf(pending) >= 0)
                    itemToSelect = pending;
                else if (SelectedItem != null && dataSource.IndexOf(SelectedItem) >= 0)
                    itemToSelect = SelectedItem;

                if (itemToSelect != null)
                {
                    ApplySelectedItemFromViewModel(itemToSelect);
                }
                else
                {
                    // Stale or no selection — clear and fall back to the nearest visible item.
                    // InitializeNavEngine() (above) preserved and clamped CurrentRowIndex from the
                    // previous DataSource, so FocusedRowSortedIndex already points to the nearest
                    // still-valid sorted position.  Falls back to index 0 on first load (no prior
                    // selection, CurrentRowIndex == -1) or when the new set has fewer rows.
                    // We intentionally stay away from raw dataSource[0] because with active filters
                    // the first data item may be excluded from the filtered set.
                    _selection.ClearSelectedItem();

                    if (RequireSelection)
                    {
                        var indices = _sortEngine.SortedIndices;
                        if (indices != null && indices.Length > 0)
                        {
                            int target    = Math.Max(0, Math.Min(FocusedRowSortedIndex, indices.Length - 1));
                            int dataIndex = indices[target];
                            if (dataIndex >= 0 && dataIndex < dataSource.Count)
                                ApplySelectedItemFromViewModel(dataSource[dataIndex]);
                        }
                    }
                }

                if (IsKeyboardFocusWithin)
                    _selection.ApplyDefaultCellSelectionIfNeeded();
            }
            else if (canvasHeight > 0)
            {
                // Canvas ready but DataSource is null or empty — clear stale rows.
                // RowVirtualizationEngine.Initialize with SortedIndices=[] calls _canvas.Clear(),
                // hides all slots (_totalRows=0) and GetMaxFirstVisibleIndex() returns 0 so
                // UpdateScrollBarRange correctly resets Maximum to 0.
                // When ShowEmptyPlaceholderRow is on, the row engine renders a single placeholder
                // row and the nav engine is initialized with 1 row so keyboard navigation works.
                InitializeRowEngine();
                bool treatedAsHavingData = IsPlaceholderActive;
                if (treatedAsHavingData)
                    InitializeNavEngine();
                _selection.ResetCellFocusApplied();
                _lifecycle.OnInitializationComplete(canvasReady: true, hasData: treatedAsHavingData);
            }
            else if (dataSource != null && dataSource.Count > 0)
            {
                // Canvas not ready yet (grid hidden) — defer row engine init until canvas has size.
                _lifecycle.OnInitializationComplete(canvasReady: false, hasData: true);
            }
            else
            {
                // Canvas not ready and no data — nothing to show yet.
                _lifecycle.OnInitializationComplete(canvasReady: false, hasData: false);
            }

            UpdateScrollBarRange();
            UpdateHScrollBar();
        }

        /// <summary>
        /// Called by <see cref="Columns.OpenDataGridColumn.BackgroundInvalidated"/> when a column's
        /// Background DP changes. Finds the column's current visual index and asks the row engine
        /// to repaint that column's cells across the entire pool.
        /// </summary>
        internal void InvalidateColumnBackground(Columns.OpenDataGridColumn col)
        {
            if (!_lifecycle.CanHandleInput) return;
            var visible = _headerPresenter.GetVisibleColumnList();
            for (int i = 0; i < visible.Count; i++)
            {
                if (ReferenceEquals(visible[i], col))
                {
                    _rowEngine.RefreshColumnBackground(i);
                    return;
                }
            }
        }

        internal void BuildPropertyAccessors()
        {
            var columns = GetVisibleColumnList();
            int count = columns.Count;
            _propertyGetters = new Func<object, object>[count];
            _propertySetters = new Action<object, object>[count];
            _cellBrushGetters = new Func<object, Brush>[count];

            for (int i = 0; i < count; i++)
            {
                var path = columns[i].BindingPath;
                var cellBrushPath = columns[i].CellBackgroundBindingPath;

                if (!string.IsNullOrEmpty(path))
                {
                    _propertyGetters[i] = PropertyAccessorCache.CreateGetter(path);
                    _propertySetters[i] = PropertyAccessorCache.CreateSetter(path);

                    if (!string.IsNullOrEmpty(cellBrushPath))
                        _cellBrushGetters[i] = PropertyAccessorCache.CreateBrushGetter(cellBrushPath);
                }
                else
                {
                    // No BindingPath: pass the whole data item (used by CustomColumn to set full DataContext)
                    _propertyGetters[i] = item => item;
                }
            }

            // Wire up per-column Background invalidation so cells repaint when Column.Background changes.
            for (int i = 0; i < count; i++)
            {
                var captured = columns[i];
                captured.BackgroundInvalidated = () => InvalidateColumnBackground(captured);
            }

            var selector     = RowBackgroundSelector;
            var rowBrushPath = RowBackgroundBindingPath;

            Func<object, Brush> pathGetter = null;
            if (!string.IsNullOrEmpty(rowBrushPath))
                pathGetter = PropertyAccessorCache.CreateBrushGetter(rowBrushPath);

            if (selector != null && pathGetter != null)
                _rowBrushGetter = item => selector(item) ?? pathGetter(item);
            else if (selector != null)
                _rowBrushGetter = selector;
            else
                _rowBrushGetter = pathGetter;

            var rowRoPart = RowReadOnlyBindingPath;
            if (!string.IsNullOrEmpty(rowRoPart))
                _rowReadOnlyGetter = PropertyAccessorCache.CreateBoolGetter(rowRoPart);
            else _rowReadOnlyGetter = null;

            BuildRowColorResolver();
            AttachCheckColumns();

            // Cache indices of CheckColumns so RefreshCheckColumnCellsForItem can update
            // only those cells instead of refreshing the entire visible column range.
            var checkIdx = new System.Collections.Generic.List<int>();
            for (int i = 0; i < count; i++)
                if (columns[i] is Columns.OpenDataGridCheckColumn) checkIdx.Add(i);
            _checkColumnIndices = checkIdx.ToArray();

            // Pure-bound checked accessor: used by IsItemChecked when CheckedItems == null.
            var firstBound = FindFirstBoundCheckColumn();
            if (firstBound != null)
            {
                var boundPath = firstBound.BindingPath;
                _boundCheckGetter = item => {
                    var v = Accessors.PropertyAccessorCache.GetValue(item, boundPath);
                    return v is bool b ? b : (v as bool?) == true;
                };
            }
            else
                _boundCheckGetter = null;
        }

        private void BuildRowColorResolver()
        {
            var selectorOrPath = _rowBrushGetter;
            var explicitAlt    = AlternateRowBackground;
            var shift          = AlternateRowShift;
            var rowBg          = RowBackground ?? Brushes.White;

            if (selectorOrPath == null)
            {
                // Static case: no per-row selector — precompute alternate once.
                var staticAlt = explicitAlt ?? (shift > 0.0 ? BrushShift.Apply(rowBg, shift) : null);
                _rowColorResolver = (dataIndex, _) =>
                    dataIndex % 2 != 0 && staticAlt != null ? staticAlt : rowBg;
            }
            else
            {
                // Dynamic case: per-row selector may return different base colors.
                // 1-entry cache avoids recreating SolidColorBrush when consecutive rows share the same base.
                Brush lastBase = null, lastAlt = null;
                _rowColorResolver = (dataIndex, item) =>
                {
                    var baseBrush = selectorOrPath(item) ?? rowBg;
                    if (dataIndex % 2 != 0)
                    {
                        if (explicitAlt != null) return explicitAlt;
                        if (shift <= 0.0) return baseBrush;
                        if (!ReferenceEquals(baseBrush, lastBase))
                        {
                            lastBase = baseBrush;
                            lastAlt  = BrushShift.Apply(baseBrush, shift);
                        }
                        return lastAlt ?? baseBrush;
                    }
                    return baseBrush;
                };
            }
        }


        internal void BuildColumnVirtualization()
        {
            _colEngine.Rebuild(GetVisibleColumnList());
            UpdateHScrollBar();
        }

        private void BuildSortEngineSync()
        {
            int total = DataSource?.Count ?? 0;
            if (_filterEngine.HasActiveFilters && DataSource != null)
            {
                int[] filtered = _filterEngine.Apply(DataSource);
                _sortEngine.Initialize(filtered, total);
            }
            else
            {
                _sortEngine.Initialize(total);
            }
        }

        internal void RegisterFilter(object key, Func<object, bool> predicate)
        {
            _filterEngine.RegisterFilter(key, predicate);
            ApplyFilters();
        }

        internal void UnregisterFilter(object key)
        {
            _filterEngine.UnregisterFilter(key);
            ApplyFilters();
        }

        /// <summary>
        /// Cancels and disposes the current filter <see cref="System.Threading.CancellationTokenSource"/>,
        /// creates a fresh one, and returns its token.
        /// Call this once per async filter launch to avoid CTS leaks and race conditions.
        /// </summary>
        private System.Threading.CancellationToken RefreshFilterCts()
        {
            _filterCts?.Cancel();
            _filterCts?.Dispose();
            _filterCts = new System.Threading.CancellationTokenSource();
            return _filterCts.Token;
        }

        private void ApplyFilters()
        {
            if (!_lifecycle.CanHandleInput || DataSource == null) return;

            var prevColumn    = _sortEngine.CurrentColumn;
            var prevDirection = _sortEngine.CurrentDirection;
            int total         = DataSource.Count;

            // Always bump generation so any in-flight async result is invalidated,
            // regardless of whether this call takes the sync or async path.
            int gen = ++_filterGeneration;

            if (_filterEngine.HasActiveFilters && total > FilterAsyncThreshold)
            {
                // Async path: large dataset — compute filtered indices on background thread.
                var dataSource = DataSource;          // capture current reference
                var token      = RefreshFilterCts();
                var work   = _workCoordinator.BeginWork();
                System.Threading.Tasks.Task.Run(() =>
                {
                    int[] filtered;
                    try { filtered = _filterEngine.Apply(dataSource, token); }
                    catch (System.OperationCanceledException)
                    {
                        Dispatcher.BeginInvoke(new Action(() => work.Dispose()));
                        return;
                    }
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (gen != _filterGeneration) return;          // superseded by newer call
                            if (!_lifecycle.IsInitialized || DataSource != dataSource) return;  // DS swapped
                            _sortEngine.Initialize(filtered, dataSource.Count);
                            FinishApplyFilters(prevColumn, prevDirection);
                        }
                        finally
                        {
                            work.Dispose();
                        }
                    }));
                });
            }
            else
            {
                // Sync path: no filter, or small dataset.
                // try-finally ensures FinishApplyFilters always runs (clears loading overlay, restores selection).
                try
                {
                    if (_filterEngine.HasActiveFilters)
                    {
                        int[] filtered = _filterEngine.Apply(DataSource);
                        _sortEngine.Initialize(filtered, total);
                    }
                    else
                    {
                        _sortEngine.Initialize(total);
                    }
                }
                finally
                {
                    FinishApplyFilters(prevColumn, prevDirection);
                }
            }
        }

        /// <summary>
        /// Completion path shared by sync and async <see cref="ApplyFilters"/> variants.
        /// Called after <c>_sortEngine</c> has been initialized with the (possibly filtered) index set.
        /// </summary>
        private void FinishApplyFilters(OpenDataGridColumn prevColumn, SortDirection prevDirection)
        {
            if (prevColumn != null && prevDirection != SortDirection.None)
            {
                // Re-sort with previous column/direction — OnSortCompleted refreshes the grid.
                // BeginPendingWork matches EndPendingWork inside OnSortCompleted.
                _workCoordinator.BeginPendingWork();
                _sortEngine.SortExplicit(prevColumn, prevDirection, DataSource);   // SortCancelled (if fired) reads _sortPendingEpoch before this line updates it
                _sortPendingEpoch = _workCoordinator.CurrentEpoch;
            }
            else
            {
                InitializeRowEngine();
                InitializeNavEngine();
                UpdateScrollBarRange();

                var sortedIndices = _sortEngine.SortedIndices;
                bool hasVisibleRows = sortedIndices != null && sortedIndices.Length > 0;

                if (SelectedItem != null)
                {
                    int dataIdx = DataSource.IndexOf(SelectedItem);
                    if (dataIdx < 0)
                    {
                        // Item was removed from collection — clear stale reference and fall back to first visible.
                        ClearSelectionAndFallbackToFirst();
                    }
                    else
                    {
                        bool selectedVisible = _sortEngine.FindSortedPosition(dataIdx) >= 0;
                        if (selectedVisible)
                        {
                            ApplySelectedItemFromViewModel(SelectedItem);
                        }
                        else
                        {
                            // Item is in DataSource but excluded by the active filter.
                            _selection.ClearSelectedItem();
                            if (hasVisibleRows && RequireSelection)
                                ApplySelectedItemFromViewModel(DataSource[sortedIndices[0]]);
                        }
                    }
                }
                else if (hasVisibleRows)
                {
                    _rowEngine.ScrollTo(Math.Min((int)_vScrollBar.Value, _rowEngine.GetMaxFirstVisibleIndex()));
                    if (!TryConsumePendingSelection() && RequireSelection)
                        ClearSelectionAndFallbackToFirst();
                }
            }
        }

        internal void InitializeRowEngine()
        {
            if (_canvas == null) return;
            var columns = GetVisibleColumnList();
            if (columns.Count == 0) return;

            int firstVis, lastVis;
            double viewportWidth = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : InitFallbackViewportWidth;
            _colEngine.GetVisibleColumnRange(_hOffset, viewportWidth, out firstVis, out lastVis);

            var widths  = _colEngine.GetColumnWidths();
            var offsets = _colEngine.GetColumnOffsets();

            var cols               = columns;
            var resolver           = _rowColorResolver;
            var selBrush           = SelectionBrush;
            var selForeground      = SelectionForeground;
            var gridForeground     = Foreground;
            var defaultRowBg       = RowBackground ?? Rendering.ThemePalette.Light.RowBackground;

            IRowSlot SlotFactory(int poolIndex)
            {
                var row = new OpenDataGridRow();
                row.UpdateDefaultCellFont(CellFontFamily, CellFontSize, CellFontWeight);
                row.Initialize(cols, widths, offsets, resolver, selBrush, AlternateRowShift, gridForeground, selForeground, defaultRowBg);
                row.PoolIndex = poolIndex;
                return row;
            }

            _rowEngine.Initialize(new Virtualization.RowEngineConfig
            {
                Canvas              = new WpfGridCanvas(_canvas),
                SlotFactory         = SlotFactory,
                DataSource          = DataSource,
                SortedIndices       = _sortEngine.SortedIndices,
                PropertyAccessors   = _propertyGetters,
                CellBrushAccessors  = _cellBrushGetters,
                RowColorResolver    = _rowColorResolver,
                RowReadOnlyAccessor = _rowReadOnlyGetter,
                RowHeight           = RowHeight,
                CanvasHeight        = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : InitFallbackCanvasHeight,
                HOffset             = _hOffset,
                FirstVisibleCol     = firstVis,
                LastVisibleCol      = lastVis,
                ColumnWidths        = widths,
                ColumnOffsets       = offsets,
                IsPlaceholderMode   = IsPlaceholderActive,
                IsCheckedAccessor   = IsItemChecked,
                CheckedRowForeground = CheckedRowForeground,
                CheckedRowBackground = CheckedRowBackground,
                CheckedRowFontWeight = CheckedRowFontWeight,
            });

            if (_gridLinePen == null) _gridLinePen = BuildGridLinePen();
            _rowEngine.SetGridLinePen(_gridLinePen);
            _rowEngine.SetGridLinesVisibility(GridLinesVisibility);
        }

        internal void InitializeNavEngine()
        {
            _navEngine.Initialize(NavigationMode, EffectiveRowCount,
                GetVisibleColumnList().Count, GetNavigationVisibleRows());
        }

        #endregion

        #region Sorting

        internal void OnHeaderCellClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is int colIndex)
            {
                var columns = GetVisibleColumnList();
                if (colIndex < 0 || colIndex >= columns.Count) return;
                var col = columns[colIndex];
                if (!col.IsSortable) return;
                if (DataSource == null || DataSource.Count == 0) return;
                CommitEdit();
                _workCoordinator.BeginPendingWork();  // matched by EndPendingWork in OnSortCompleted
                _sortEngine.Sort(col, DataSource);   // SortCancelled (if fired) reads _sortPendingEpoch before this line updates it
                _sortPendingEpoch = _workCoordinator.CurrentEpoch;
            }
        }

        private void OnSortCompleted(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                // Read _sortPendingEpoch here, on the UI thread, so it reflects the epoch that was
                // live when Sort/SortExplicit was called. If the grid was unloaded since then,
                // _workCoordinator.CurrentEpoch will have advanced and EndPendingWork(epoch) will be a true no-op.
                int epoch = _sortPendingEpoch;
                try
                {
                    if (!_lifecycle.CanHandleInput) return;
                    if (_canvas?.ActualHeight <= 0) { _lifecycle.OnViewportLost(); return; }

                    InitializeRowEngine();
                    _navEngine.UpdateStats(EffectiveRowCount, GetNavigationVisibleRows());
                    UpdateScrollBarRange();

                    _suppressScrollEvents = true;
                    _vScrollBar.Value = 0;
                    _suppressScrollEvents = false;

                    // Remap selection after sort.
                    // If the selected item was deleted from DataSource, or is present but filtered out,
                    // fall back to the first visible item — mirrors the no-sort path in FinishApplyFilters.
                    if (SelectedItem != null)
                    {
                        int dataIndex = DataSource?.IndexOf(SelectedItem) ?? -1;
                        int sortedIndex = dataIndex >= 0 ? _sortEngine.FindSortedPosition(dataIndex) : -1;
                        if (sortedIndex >= 0)
                            _selection.ApplyCellFocus(sortedIndex, FocusedColIndex, ensureVisible: false);
                        else
                            ClearSelectionAndFallbackToFirst();
                    }
                    else if (RequireSelection && _sortEngine.SortedIndices?.Length > 0)
                    {
                        if (!TryConsumePendingSelection())
                            ClearSelectionAndFallbackToFirst();
                    }
                }
                finally
                {
                    // Matches BeginPendingWork called before Sort/SortExplicit in:
                    // OnHeaderCellClick, FinishApplyFilters, OnCollectionChanged.
                    _workCoordinator.EndPendingWork(epoch);
                }
            }));
        }

        /// <summary>
        /// Called when a sort is cancelled because a newer Initialize/Sort superseded it.
        /// Balances the BeginPendingWork that was paired with the cancelled Sort call.
        /// SortCancelled fires on the UI thread (from CancelPendingSort), so no marshal needed.
        /// </summary>
        private void OnSortCancelled(object sender, EventArgs e)
        {
            // SortCancelled fires synchronously from SortEngine.CancelPendingSort which is called
            // from the UI thread — either directly in Initialize or from ExecuteSortCore (also UI).
            // No Dispatcher.Invoke needed.
            //
            // _sortPendingEpoch holds the epoch of the cancelled sort's BeginPendingWork because we
            // assign _sortPendingEpoch = _workCoordinator.CurrentEpoch only AFTER Sort()/SortExplicit() returns.
            // If the sort started before an Unloaded (stale epoch), EndPendingWork is a genuine no-op.
            _workCoordinator.EndPendingWork(_sortPendingEpoch);
        }

        #endregion

        #region Collection Change

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Coalesce multiple rapid events (e.g. 100 ObservableCollection.Add calls) into a single
            // rebuild. Only the first event in a burst schedules the work; subsequent events return early.
            if (_collectionRebuildScheduled) return;
            _collectionRebuildScheduled = true;
            var work = _workCoordinator.BeginWork();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _collectionRebuildScheduled = false;
                try
                {
                    if (!_lifecycle.CanHandleInput || _canvas == null || DataSource == null)
                    {
                        TryInitialize();
                        return;
                    }

                    // Guard: canvas has no size (grid hidden) — defer row engine init.
                    if (_canvas.ActualHeight <= 0)
                    {
                        _lifecycle.OnViewportLost();
                        return;
                    }

                    var prevColumn    = _sortEngine.CurrentColumn;
                    var prevDirection = _sortEngine.CurrentDirection;

                    BuildSortEngineSync();

                    if (prevColumn != null && prevDirection != Models.SortDirection.None)
                    {
                        // Re-sort with the same column and direction — OnSortCompleted refreshes the grid.
                        // _workCoordinator.BeginPendingWork matches EndPendingWork inside OnSortCompleted.
                        _workCoordinator.BeginPendingWork();
                        _sortEngine.SortExplicit(prevColumn, prevDirection, DataSource);   // SortCancelled (if fired) reads _sortPendingEpoch before this line updates it
                        _sortPendingEpoch = _workCoordinator.CurrentEpoch;
                    }
                    else
                    {
                        _navEngine.UpdateStats(EffectiveRowCount, GetNavigationVisibleRows());
                        InitializeRowEngine();
                        UpdateScrollBarRange();
                        // Restore viewport first — ApplySelectedItemFromViewModel will override only
                        // when AutoScrollToSelectedItem = true; without this, the engine stays at 0.
                        _rowEngine.ScrollTo((int)_vScrollBar.Value);

                        // Pending takes precedence: an item set via SelectedItem before it existed
                        // in the collection (e.g. set in XAML/VM constructor) becomes resolvable
                        // when CollectionChanged adds it.
                        if (!TryConsumePendingSelection())
                        {
                            if (SelectedItem != null)
                            {
                                int dataIdx = DataSource.IndexOf(SelectedItem);
                                if (dataIdx >= 0)
                                {
                                    bool selectedVisible = _sortEngine.FindSortedPosition(dataIdx) >= 0;
                                    ApplySelectedItemFromViewModel(SelectedItem);
                                    if (!selectedVisible)
                                        _selection.ResetCellFocusApplied();   // let focus re-establish on next keyboard focus
                                }
                                else
                                {
                                    ClearSelectionAndFallbackToFirst();
                                }
                            }
                            else if (RequireSelection && _sortEngine.SortedIndices?.Length > 0)
                            {
                                ClearSelectionAndFallbackToFirst();
                            }
                        }
                    }
                }
                finally
                {
                    work.Dispose();
                }
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        #endregion

    }
}
