using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                    cachedGetters = BuildGettersForType(type, fieldNames);
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
        private static List<Func<object, string>> BuildGettersForType(Type itemType, string[] fieldNames)
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
            else
            {
                // All string properties
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
