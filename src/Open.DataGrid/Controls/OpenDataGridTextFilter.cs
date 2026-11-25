using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Open.DataGrid.Accessors;
using Open.DataGrid.Filtering;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// A TextBox that filters rows in a linked <see cref="OpenDataGrid"/>.
    /// Multiple instances linked to the same grid all apply simultaneously (AND logic).
    /// Entering "samsung lodówka" finds rows containing BOTH words in any order (case-insensitive).
    /// </summary>
    public class OpenDataGridTextFilter : TextBox
    {
        static OpenDataGridTextFilter()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(OpenDataGridTextFilter),
                new FrameworkPropertyMetadata(typeof(OpenDataGridTextFilter)));
        }

        #region Dependency Properties

        public static readonly DependencyProperty GridProperty =
            DependencyProperty.Register(
                nameof(Grid), typeof(OpenDataGrid), typeof(OpenDataGridTextFilter),
                new PropertyMetadata(null, OnGridChanged));

        /// <summary>The grid to filter. Set via ElementName binding in XAML.</summary>
        public OpenDataGrid Grid
        {
            get => (OpenDataGrid)GetValue(GridProperty);
            set => SetValue(GridProperty, value);
        }

        public static readonly DependencyProperty FilterFieldsProperty =
            DependencyProperty.Register(
                nameof(FilterFields), typeof(string), typeof(OpenDataGridTextFilter),
                new PropertyMetadata(string.Empty, OnFilterFieldsChanged));

        /// <summary>
        /// Comma- or semicolon-separated property names to search.
        /// Leave empty to search all string properties on the data item type.
        /// </summary>
        public string FilterFields
        {
            get => (string)GetValue(FilterFieldsProperty);
            set => SetValue(FilterFieldsProperty, value);
        }

        public static readonly DependencyProperty IgnoreDiacriticsProperty =
            DependencyProperty.Register(
                nameof(IgnoreDiacritics), typeof(bool), typeof(OpenDataGridTextFilter),
                new PropertyMetadata(true, OnIgnoreDiacriticsChanged));

        /// <summary>
        /// When true, search ignores diacritic marks and compares their common ASCII equivalents
        /// (for example "lodowka" matches the accented variant). Default is true.
        /// </summary>
        public bool IgnoreDiacritics
        {
            get => (bool)GetValue(IgnoreDiacriticsProperty);
            set => SetValue(IgnoreDiacriticsProperty, value);
        }

        public static readonly DependencyProperty DebounceDelayProperty =
            DependencyProperty.Register(
                nameof(DebounceDelay), typeof(int), typeof(OpenDataGridTextFilter),
                new PropertyMetadata(300));

        /// <summary>Milliseconds to wait after the last keystroke before applying the filter (default 300).</summary>
        public int DebounceDelay
        {
            get => (int)GetValue(DebounceDelayProperty);
            set => SetValue(DebounceDelayProperty, value);
        }

        public static readonly DependencyProperty SelectAllOnFocusProperty =
            DependencyProperty.Register(
                nameof(SelectAllOnFocus), typeof(bool), typeof(OpenDataGridTextFilter),
                new PropertyMetadata(false));

        /// <summary>When true, all text is selected when the filter receives focus. Default is false (caret moves to end).</summary>
        public bool SelectAllOnFocus
        {
            get => (bool)GetValue(SelectAllOnFocusProperty);
            set => SetValue(SelectAllOnFocusProperty, value);
        }

        public static readonly DependencyProperty FocusGridOnConfirmProperty =
            DependencyProperty.Register(
                nameof(FocusGridOnConfirm), typeof(bool), typeof(OpenDataGridTextFilter),
                new PropertyMetadata(true));

        /// <summary>
        /// When true (default), pressing ArrowDown or Enter moves keyboard focus to the linked grid.
        /// </summary>
        public bool FocusGridOnConfirm
        {
            get => (bool)GetValue(FocusGridOnConfirmProperty);
            set => SetValue(FocusGridOnConfirmProperty, value);
        }

        public static readonly DependencyProperty KeepFocusOnNavigationProperty =
            DependencyProperty.Register(
                nameof(KeepFocusOnNavigation), typeof(bool), typeof(OpenDataGridTextFilter),
                new PropertyMetadata(false));

        /// <summary>
        /// When true, Up/Down/PageUp/PageDown drive the linked grid's row selection while keyboard focus
        /// stays in this text box — the search-box-over-results-list pattern, where the user keeps typing
        /// and the arrows move the selection remotely. Takes precedence over <see cref="FocusGridOnConfirm"/>
        /// for those four keys, which never move focus while this is true.
        /// <para>
        /// Left/Right are deliberately excluded: they edit the text (caret movement). Enter is also excluded —
        /// it confirms via <see cref="EnterCommand"/> or moves focus per <see cref="FocusGridOnConfirm"/>.
        /// </para>
        /// Default is false.
        /// </summary>
        public bool KeepFocusOnNavigation
        {
            get => (bool)GetValue(KeepFocusOnNavigationProperty);
            set => SetValue(KeepFocusOnNavigationProperty, value);
        }

        public static readonly DependencyProperty HorizontalArrowsNavigateGridControlsProperty =
            DependencyProperty.Register(
                nameof(HorizontalArrowsNavigateGridControls), typeof(bool), typeof(OpenDataGridTextFilter),
                new PropertyMetadata(false));

        /// <summary>
        /// When true, pressing Left with the caret at the start, or Right with the caret at the end
        /// (in both cases with no text selected), moves focus to the previous/next <b>grid control</b>
        /// — an <see cref="OpenDataGrid"/>, <see cref="OpenDataGridTextFilter"/> or
        /// <see cref="OpenDataGridComboFilter"/>. Ordering follows TabIndex (ties broken by tree order),
        /// but every other control in the window is skipped, so the arrows never escape the grid's own
        /// controls the way Tab would.
        /// <para>
        /// Takes precedence over <see cref="FocusGridOnConfirm"/>, whose Left/Right handling fires on the
        /// very same caret-at-edge condition. Not gated by it: traversal is a property of this control,
        /// not of the grid link. At either end of the set the key is swallowed rather than escaping.
        /// </para>
        /// Default is false.
        /// </summary>
        public bool HorizontalArrowsNavigateGridControls
        {
            get => (bool)GetValue(HorizontalArrowsNavigateGridControlsProperty);
            set => SetValue(HorizontalArrowsNavigateGridControlsProperty, value);
        }

        public static readonly DependencyProperty EnterCommandProperty =
            DependencyProperty.Register(
                nameof(EnterCommand), typeof(ICommand), typeof(OpenDataGridTextFilter),
                new PropertyMetadata(null));

        /// <summary>
        /// Command executed when the user presses Enter. The command parameter is the grid's
        /// <see cref="OpenDataGrid.SelectedItem"/>. When set, Enter does not move focus to the
        /// grid (behaves as if <see cref="FocusGridOnConfirm"/> were false for the Enter key).
        /// </summary>
        public ICommand EnterCommand
        {
            get => (ICommand)GetValue(EnterCommandProperty);
            set => SetValue(EnterCommandProperty, value);
        }

        #endregion

        public OpenDataGridTextFilter()
        {
            Loaded += (_, __) =>
            {
                SetProperSelection();
                // Apply immediately on first load so the grid never shows unfiltered data
                // during the debounce window. Subsequent text changes still use debounce.
                ApplyFilterOrSignalReady();
            };
            Unloaded += (_, __) =>
            {
                _debounceTimer?.Stop();
                // Unregister the predicate to prevent a zombie filter when the control leaves
                // the visual tree for good (DataTemplate swap, parent disposed). NotifyFilterReady
                // releases any pending signal set earlier (e.g. from OnGridChanged) so the grid
                // does not keep its loading overlay stuck if this instance is never re-loaded.
                var grid = Grid;
                if (grid != null)
                {
                    grid.NotifyFilterReady(_filterKey);
                    grid.UnregisterFilter(_filterKey);
                }
            };
        }

        private DispatcherTimer _debounceTimer;

        // Stable anonymous key used with FilterEngine and pending-filter signalling.
        // Using a dedicated object (not 'this') ensures FilterEngine holds no reference to the
        // TextFilter instance, so the control can be collected when it leaves the visual tree.
        private readonly object _filterKey = new object();

        private static void OnGridChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var filter = (OpenDataGridTextFilter)d;

            // Stop any pending debounce so it cannot fire against the old grid after the switch.
            filter._debounceTimer?.Stop();

            if (e.OldValue is OpenDataGrid oldGrid)
            {
                oldGrid.UnregisterFilter(filter._filterKey);
                oldGrid.NotifyFilterReady(filter._filterKey);   // release any pending signal on the old grid
            }

            if (e.NewValue is OpenDataGrid newGrid)
                newGrid.NotifyFilterPending(filter._filterKey); // signal: filter is attached but predicate not yet computed

            // Re-apply current filter text to the new grid; immediate if text is non-empty.
            filter.ApplyFilterOrSignalReady();
        }

        private static void OnFilterFieldsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var filter = (OpenDataGridTextFilter)d;
            // BuildPredicate creates a fresh closure on each call, so just rescheduling
            // is sufficient to pick up the new field list.
            filter.ScheduleFilterUpdate();
        }

        private static void OnIgnoreDiacriticsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var filter = (OpenDataGridTextFilter)d;
            // Rebuild the predicate so cached search terms use the new normalization mode.
            filter.ScheduleFilterUpdate();
        }

        protected override void OnGotFocus(RoutedEventArgs e)
        {
            base.OnGotFocus(e);
            // Deferred: mouse click resets the caret after OnGotFocus, so we apply after MouseDown is processed.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                SetProperSelection();
            }));
        }

        private void SetProperSelection()
        {
            if (SelectAllOnFocus)
                SelectAll();
            else
                CaretIndex = Text?.Length ?? 0;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Handled) return;

            if (e.Key == Key.Enter)
            {
                var cmd = EnterCommand;
                if (cmd != null)
                {
                    var param = Grid?.SelectedItem;
                    if (cmd.CanExecute(param))
                        cmd.Execute(param);
                    e.Handled = true;
                    return;
                }
            }

            // Keep-focus navigation wins over the focus-transfer path below: these keys drive the
            // grid remotely and must never pull focus out of the text box.
            if (KeepFocusOnNavigation && IsGridNavigationKey(e.Key))
            {
                var navGrid = Grid;
                if (navGrid != null)
                {
                    ForwardKeyToGrid(navGrid, e);
                    e.Handled = true; // the arrows belong to the grid now, not to the caret
                    return;
                }
            }

            // Grid-control traversal is independent of the grid link, so it runs before the
            // FocusGridOnConfirm gate and wins over that path's Left/Right handling, which fires
            // on the very same caret-at-edge condition.
            if (HorizontalArrowsNavigateGridControls && TryMoveToAdjacentGridControl(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (!FocusGridOnConfirm) return;

            bool shouldFocus;
            switch (e.Key)
            {
                case Key.Down:
                case Key.Up:
                case Key.PageDown:
                case Key.PageUp:
                case Key.Enter:
                    shouldFocus = true;
                    break;
                case Key.Left:
                    shouldFocus = SelectionLength == 0 && CaretIndex == 0;
                    break;
                case Key.Right:
                    shouldFocus = SelectionLength == 0 && CaretIndex == Text.Length;
                    break;
                default:
                    shouldFocus = false;
                    break;
            }

            if (shouldFocus)
            {
                Grid?.Focus();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Moves focus to the adjacent grid control when a horizontal arrow is pressed at the matching
        /// text edge with nothing selected. Returns false when the caret still has text to travel,
        /// leaving the key to the caret.
        /// </summary>
        private bool TryMoveToAdjacentGridControl(Key key)
        {
            if (SelectionLength != 0) return false;

            int step;
            if (key == Key.Left && CaretIndex == 0)
                step = -1;
            else if (key == Key.Right && CaretIndex == (Text?.Length ?? 0))
                step = 1;
            else
                return false;

            var controls = CollectGridControlsInTabOrder();
            int self = controls.IndexOf(this);

            // Swallow the key regardless: at either end of the set (or when detached from a visual
            // tree) the arrows must still not escape to unrelated controls, and the caret is already
            // parked at the edge with nowhere to go.
            if (self < 0) return true;

            int target = self + step;
            if (target >= 0 && target < controls.Count)
                controls[target].Focus();

            return true;
        }

        /// <summary>
        /// Grid controls under the containing window, ordered the way WPF orders tab stops:
        /// TabIndex ascending, ties broken by tree order. Unrelated controls are excluded — that
        /// is what scopes the arrows to the grid's own controls.
        /// </summary>
        private List<FrameworkElement> CollectGridControlsInTabOrder()
        {
            var found = new List<FrameworkElement>();
            CollectGridControls(FindTraversalRoot(), found);

            // OrderBy is stable, so ThenBy on tree position mirrors WPF's tie-breaking rule.
            return found
                .Select((el, treeOrder) => new { el, treeOrder })
                .OrderBy(x => KeyboardNavigation.GetTabIndex(x.el))
                .ThenBy(x => x.treeOrder)
                .Select(x => x.el)
                .ToList();
        }

        private DependencyObject FindTraversalRoot()
        {
            DependencyObject root = Window.GetWindow(this);
            if (root != null) return root;

            // No window (e.g. hosted in a popup or a detached tree) — climb to the topmost visual.
            DependencyObject current = this;
            while (true)
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (parent == null) return current;
                current = parent;
            }
        }

        private static void CollectGridControls(DependencyObject node, List<FrameworkElement> acc)
        {
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);

                if (IsGridControl(child))
                {
                    // Never descend into a grid control: its rows, cells and filter-row editors
                    // are internals, not traversal targets.
                    if (child is FrameworkElement el && IsTraversable(el))
                        acc.Add(el);
                    continue;
                }

                CollectGridControls(child, acc);
            }
        }

        private static bool IsGridControl(DependencyObject d)
            => d is OpenDataGrid || d is OpenDataGridTextFilter || d is OpenDataGridComboFilter;

        private static bool IsTraversable(FrameworkElement el)
            => el.IsVisible && el.IsEnabled && el.Focusable;

        /// <summary>
        /// Keys that move the grid's row selection. Left/Right are excluded — they move the caret
        /// within the filter text. Enter is excluded — it confirms rather than navigates.
        /// </summary>
        private static bool IsGridNavigationKey(Key key)
        {
            switch (key)
            {
                case Key.Up:
                case Key.Down:
                case Key.PageUp:
                case Key.PageDown:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Re-raises the keystroke on the grid as PreviewKeyDown — the event the grid overrides
        /// to drive its NavigationEngine. The original KeyboardDevice is reused so modifier checks
        /// still see the real modifier state. The grid needs no keyboard focus: ApplyCellFocus
        /// updates selection and rendering without it.
        /// </summary>
        private static void ForwardKeyToGrid(OpenDataGrid grid, KeyEventArgs e)
        {
            var source = PresentationSource.FromVisual(grid);
            if (source == null) return; // grid not connected to a rendering surface — nothing to navigate

            grid.RaiseEvent(new KeyEventArgs(e.KeyboardDevice, source, e.Timestamp, e.Key)
            {
                RoutedEvent = PreviewKeyDownEvent
            });
        }

        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            base.OnTextChanged(e);

            // Clearing always applies immediately — no predicate to compute, no cost worth
            // deferring. Also guards against VM setting Text="" while the textbox still has
            // keyboard focus (e.g. barcode/command scenario), which would otherwise start a
            // 300 ms debounce and leave the old filter active until the timer fires.
            if (string.IsNullOrWhiteSpace(Text))
            {
                _debounceTimer?.Stop();
                ApplyFilter();
            }
            else if (IsKeyboardFocused)
            {
                ScheduleFilterUpdate();
            }
            else
            {
                ApplyFilter();
            }

            if (!IsKeyboardFocused)
                SetProperSelection();
        }

        private void ScheduleFilterUpdate()
        {
            if (_debounceTimer == null)
            {
                _debounceTimer = new DispatcherTimer(DispatcherPriority.Background);
                _debounceTimer.Tick += OnDebounceTimerTick;
            }

            _debounceTimer.Stop();
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceDelay);
            _debounceTimer.Start();
        }

        private void OnDebounceTimerTick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            ApplyFilter();
        }

        /// <summary>
        /// If <see cref="TextBox.Text"/> is non-empty, applies the filter immediately (bypasses debounce).
        /// If empty, signals the grid that this filter is ready (no predicate to register).
        /// Used on Loaded and on Grid DP change to avoid the initial 300 ms flash of unfiltered data.
        /// </summary>
        private void ApplyFilterOrSignalReady()
        {
            string text = Text?.Trim() ?? string.Empty;
            if (text.Length > 0)
                ApplyFilter();            // registers predicate + NotifyFilterReady inside
            else
                Grid?.NotifyFilterReady(_filterKey);  // nothing to filter; release overlay
        }

        private void ApplyFilter()
        {
            var grid = Grid;
            if (grid == null) return;

            string text = Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                grid.UnregisterFilter(_filterKey);
                grid.NotifyFilterReady(_filterKey);
                return;
            }

            var predicate = BuildPredicate(text, FilterFields ?? string.Empty, IgnoreDiacritics);
            grid.RegisterFilter(_filterKey, predicate);
            grid.NotifyFilterReady(_filterKey);
        }

        private static Func<object, bool> BuildPredicate(string filterText, string filterFields, bool ignoreDiacritics)
        {
            string[] terms = filterText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) return _ => true;
            if (ignoreDiacritics)
            {
                for (int i = 0; i < terms.Length; i++)
                    terms[i] = RemoveDiacritics(terms[i]);
            }
            // OrdinalIgnoreCase comparison handles case folding — no need to pre-lowercase terms.

            string[] fieldNames = string.IsNullOrWhiteSpace(filterFields)
                ? null
                : filterFields.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            // Per-closure getter cache — no reference to the TextFilter instance, so
            // FilterEngine can hold this predicate without preventing GC of the control.
            List<Func<object, string>> cachedGetters = null;
            Type cachedType = null;

            return item =>
            {
                if (item == null) return false;

                var type = item.GetType();
                if (type != cachedType)
                {
                    cachedType = type;
                    cachedGetters = BuildGettersForType(type, fieldNames, item);
                }

                var getters = cachedGetters;
                if (getters == null || getters.Count == 0) return true;

                for (int t = 0; t < terms.Length; t++)
                {
                    bool found = false;
                    for (int f = 0; f < getters.Count; f++)
                    {
                        string val = getters[f](item);
                        if (ignoreDiacritics && val != null)
                            val = RemoveDiacritics(val);

                        if (val != null && val.IndexOf(terms[t], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found) return false;
                }

                return true;
            };
        }

        // Only properties of type exactly `string` are scanned.
        // Explicit FilterFields entries may use nested paths ("A.B"); automatic scan still covers top-level string properties only.
        // For DataRowView with no FilterFields, all table columns are included (schema read from firstItem).
        // For IDictionary<string,object>, automatic scan is not possible — specify FilterFields explicitly.
        private static List<Func<object, string>> BuildGettersForType(Type itemType, string[] fieldNames, object firstItem = null)
        {
            var getters = new List<Func<object, string>>();

            if (fieldNames != null)
            {
                foreach (string name in fieldNames)
                {
                    string trimmed = name.Trim();
                    if (trimmed.Length == 0) continue;
                    Func<object, string> getter;
                    if (PropertyAccessorCache.TryCreateStringGetter(itemType, trimmed, out getter))
                        getters.Add(getter);
                }
            }
            else if (itemType == typeof(DataRowView))
            {
                var row = firstItem as DataRowView;
                if (row != null)
                {
                    foreach (DataColumn col in row.DataView.Table.Columns)
                    {
                        var colName = col.ColumnName;
                        Func<object, string> getter;
                        if (PropertyAccessorCache.TryCreateStringGetter(itemType, colName, out getter))
                            getters.Add(getter);
                    }
                }
            }
            else
            {
                // All string properties on POCO types
                foreach (var prop in itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.PropertyType != typeof(string)) continue;
                    Func<object, string> getter;
                    if (PropertyAccessorCache.TryCreateStringGetter(itemType, prop.Name, out getter))
                        getters.Add(getter);
                }
            }

            return getters;
        }

        private static string RemoveDiacritics(string text) => DiacriticsHelper.RemoveDiacritics(text);
    }
}
