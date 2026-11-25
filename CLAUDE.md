# CLAUDE.md — OpenDataGrid

## Project Overview

This repository contains a custom WPF control `OpenDataGrid` written in .NET Framework 4.6.2.
The goal is a high-performance, configurable grid without using the built-in `DataGrid`, `ListView`, `VirtualizingStackPanel`, or `VirtualizingPanel`.

```
OpenDataGrid.slnx
src/
├── Open.DataGrid/          # Control library
├── Open.DataGrid.Demo/     # Test application (MVVMLight, 1,000,000 rows)
└── Open.DataGrid.Tests/    # Unit tests (NUnit 3, 451 unit + 75 integration = 526 total)
```

---

## Absolute Prohibitions

- **Never** use `DataGrid`, `ListView`, `VirtualizingStackPanel`, `VirtualizingPanel`, or any other built-in WPF virtualization mechanism.
- **Never** set cell values via `Binding` (WPF binding) during scrolling — use `PropertyInfo.GetValue` / `column.ApplyDisplayValue(element, value)` directly. Bindings on the hot path destroy performance with 1M rows.
- **Never** create new `OpenDataGridRow` objects during scrolling — the pool exists precisely to avoid this.

---

## Virtualization Architecture (most important part)

### Ring Buffer (RowPool)
- Only `visibleRowCount + 2 * 4` instances of `OpenDataGridRow` exist in memory — regardless of data size.
- Scroll = rotate `_headIndex` pointer in O(1) + rebind only changed rows.
- Full refresh (`FullRefresh`) only when delta > pool size.

### Canvas + ScrollBar
- `PART_Canvas` — viewport for rows; `Canvas.SetTop` positions each row.
- `PART_VerticalScrollBar` / `PART_HorizontalScrollBar` — standalone, no ScrollViewer.
- `ScrollBar.Value` = index of the first visible row (per-row scroll).

### Critical Invariant: visibleRowCount
`visibleRowCount = ceil(canvasHeight / rowHeight) + 1`

The extra `+1` ensures a partially visible row at the bottom. **Consequence**: the slot at index `visibleRowCount − 1` always starts at `canvasHeight` or beyond — it is invisible. The last fully visible slot has index `visibleRowCount − 2`.

All formulas based on `visibleRowCount` must account for this:
- `ScrollBar.Maximum = total − visibleRowCount + 1` (not `total − visibleRowCount`)
- `ScrollTo` clamp: `min(total − visibleRowCount + 1, index)`
- `EnsureRowVisible`: `lastFullyVisible = firstVisible + visibleRowCount − 2`, scroll target = `sortedIndex − visibleRowCount + 2`

An off-by-one error in any of the above causes the last data row to be invisible when scrolled to the end.

### Property Access
- `_propertyGetters[i]` — `Func<object, object>` built from **Fasterflect IL delegates** (`DelegateForGetPropertyValue`), ~10× faster than `PropertyInfo.GetValue`. Central cache: `src/Open.DataGrid/Accessors/PropertyAccessorCache.cs`.
- `_propertySetters[i]` — `Action<object, object>` for writing after editing.
- Cache is rebuilt in `BuildPropertyAccessors()` on every `DataSource` change.
- `PropertyAccessorCache` maintains two levels: global `ConcurrentDictionary<(Type, Name), MemberGetter>` + per-getter 1-entry type cache (`CreateGetter`) for heterogeneous collections. Also used by filters and columns.

### Sorting
- `int[] sortedIndices` — maps visual position → index in `DataSource`. Data is never moved.
- Sort runs on a background thread (`Task.Run`), then refreshes the grid via `Dispatcher.BeginInvoke`.
- `SortEngine.FindSortedPosition(dataIndex)` — maps a data index → visual position (for remapping selection after sort).
- `SortEngine.SortCompleted` event — synchronizes the grid after background sort; use it in tests (`SpinWait.SpinUntil`) instead of sleep.
- `SortEngine` supports cancellation — `SortEngineCancellationTests` covers these paths.

### Filtering
- `FilterEngine` (`src/Open.DataGrid/Filtering/FilterEngine.cs`) — predicate registry.
- `int[] FilterEngine.Apply(IList)` — single pass over data, returns indices of matching rows (AND logic).
- The filtered `int[]` goes to `SortEngine.Initialize(int[], int maxDataIndex)` — sort operates only on the filtered set.
- `_dataToSorted` is dimensioned to the full `DataSource.Count`; filtered-out rows have value `-1`.
- `RowVirtualizationEngine._totalRows` = `SortedIndices.Length` (not `DataSource.Count`) — this automatically gives a correct scrollbar and navigation.
- `RegisterFilter(key, predicate)` / `UnregisterFilter(key)` are **internal** methods — accessible only to `OpenDataGridTextFilter` / `OpenDataGridComboFilter`. A custom filter control requires extending this API. Multiple filters = AND.

### Deferred Render and Loading Indicator

The grid shows a loading indicator (overlay + bar) and defers data rendering while:
- a filter control signals `NotifyFilterPending` (predicate not yet computed)
- an async filter compute is in progress (`DataSource.Count > 10,000` and a filter is active)
- a background sort is running
- a batch rebuild after `ObservableCollection.CollectionChanged` is coalescing

**The mechanism** is managed by `GridWorkCoordinator` (see below). The grid's public methods `BeginPendingWork()` / `EndPendingWork()` and `NotifyFilterPending(this)` / `NotifyFilterReady(this)` delegate to the coordinator. Always use `finally { EndPendingWork(); }`.

**Async filter compute**: `ApplyFilters()` and `TryInitialize()` — when `HasActiveFilters && Count > FilterAsyncThreshold(10k)` → `Task.Run` with a generation token `_filterGeneration`; stale results (gen != current) are discarded.

**Coalescing `OnCollectionChanged`**: flag `_collectionRebuildScheduled` — 100 Adds = 1 rebuild.

**DP `LoadingIndicatorMode`** (type `Models.LoadingIndicatorMode`, default `OverlayOnly`):
- `All` — overlay (`PART_LoadingOverlay`, Border `#30FFFFFF`) + bar (`PART_LoadingBar`, ProgressBar 3px indeterminate)
- `ProgressBarOnly` — bar only
- `OverlayOnly` — overlay only (default)
- `None` — show nothing

**DP `LoadingMessage`** (string, localized PL/EN/UA) — unused in the default template, available for customization.

---

## Coordinator Classes (Controls/)

After refactoring, `OpenDataGrid` delegates key responsibilities to helper classes. All live in `src/Open.DataGrid/Controls/`.

### `GridLifecycleController` + `GridLifecycleState`
An explicit state machine for the grid lifecycle. Replaces three scattered boolean flags:

| Old flag | Controller property |
|---|---|
| `_initialized` | `IsInitialized` / `CanHandleInput` |
| `_wasUnloaded` | `IsUnloaded` |
| `_rowEngineNeedsReinit` | `NeedsViewport` |

| State | Meaning | Entry |
|---|---|---|
| `NoTemplate` | Initial state — `OnApplyTemplate` not yet called | constructor |
| `TemplateReady` | Template parts wired, initialization not complete (no data, canvas=0, or reinit in progress) | `OnTemplateApplied`, `BeginReinitialize`, `OnInitializationComplete` when no canvas/data, `OnGridLoaded` |
| `WaitingForViewport` | Canvas height is 0 (grid hidden), data exists — row engine deferred | `OnInitializationComplete(canvas=false, data=true)`, `OnViewportLost` |
| `Initializing` | Async filter compute running in background — `FinishInitialize` will arrive via `Dispatcher.BeginInvoke` | `OnAsyncInitStarted` |
| `Ready` | Grid fully operational — data rendered, navigation and selection active | `OnInitializationComplete(canvas=true, data=true)` |
| `Unloaded` | Grid left visual tree — subscriptions detached, async cancelled | `OnGridUnloaded` |

Flow diagram:
```
NoTemplate
  └─OnTemplateApplied──► TemplateReady ◄──BeginReinitialize── Ready
                              │                                   │
                    OnAsyncInitStarted                     OnViewportLost
                              │                                   │
                         Initializing                    WaitingForViewport
                              │                                   │
                    OnInitializationComplete         OnCanvasSizeChanged→TryInitialize
                         (canvas+data)                            │
                              └──────────────► Ready ◄───────────┘

Any state ──OnGridUnloaded──► Unloaded ──OnGridLoaded──► TemplateReady
```

Key invariant: `OnInitializationComplete` and `OnTemplateApplied` are no-ops when state is `Unloaded` — this prevents a situation where a `DataSource` binding or WPF re-template during re-mount would clear the `IsUnloaded` flag before `OnGridLoaded`, causing the `CollectionChanged` re-attach to be skipped.
Every state transition is logged in DEBUG through a single point (`Transition` in `GridLifecycleController.cs`).

### `SelectionFocusSynchronizer`
Central hub for selection and focus logic. Holds state: `_cellFocusApplied`, `InternalSelectionChange`, `PendingSelectedItem`. Implements `ApplyCellFocus`, `SelectByDataIndex`, `OnCanvasMouseDown`, `SelectedColumn` resolution on first focus, and the `_internalSelectionChange` guard. Previously this code was scattered in the (now non-existent) `OpenDataGrid.Selection.cs`.

### `GridWorkCoordinator`
Manages in-flight async work tracking: reference-counted `_pendingWorkCount`, epoch-based staleness detection, and overlay/progress bar visibility. RAII pattern via `WorkToken` — the token's destructor automatically calls `EndPendingWork`. Guards against overlay flicker on Unloaded/Loaded.

### `HeaderPresenter`
Encapsulates header cell creation and layout, column resize thumbs (`Cursors.SizeWE`), DPD subscriptions on `SortDirection` and `IsVisible` of columns, and the visible column list cache. Together with `HiddenColumnsTracker` forms the column management layer.

### `HiddenColumnsTracker`
Tracks which columns were hidden by the `HiddenColumns` DP and applies visibility changes incrementally — without touching columns set manually via `IsVisible`. Uses `HiddenColumnsByHeader` to select the matching mode (by `Header` or `BindingPath`).

**DPD subscriptions — note:** Every new `DependencyPropertyDescriptor.AddValueChanged` on columns **must** be registered in `HeaderPresenter` (the `_dpdSubscriptions` list) — otherwise a memory leak occurs (DPD holds a strong reference to the handler). Released in `BuildHeaders` / `RebuildColumnVisibilitySubscriptions`.

---

## Filter Controls

Both controls inherit directly from built-in WPF controls and communicate with the grid via `RegisterFilter` / `UnregisterFilter` (internal).

### `OpenDataGridTextFilter` (TextBox)
- DP `Grid` (`OpenDataGrid`) — reference to the grid.
- DP `FilterFields` (`string`) — field names separated by `,` or `;`. Empty = all `string` properties.
- DP `DebounceDelay` (`int`, default 300ms) — delay after the last keystroke before applying the filter.
- DP `SelectAllOnFocus` (`bool`, default `false`) — selects all text on focus; `false` = cursor at end.
- DP `FocusGridOnConfirm` (`bool`, default `true`) — Enter / ArrowDown moves focus to the grid.
- DP `EnterCommand` (`ICommand`, default `null`) — when bound, Enter executes the command with `Grid.SelectedItem` as the parameter and does **not** move focus to the grid (treated as `FocusGridOnConfirm = false` for Enter).
- Logic: text split by spaces → each token must appear in at least one of the fields (`IndexOf`, `OrdinalIgnoreCase`). "samsung fridge" = AND.
- Getter cache via `PropertyAccessorCache.TryCreateStringGetter` per object type.
- On `Loaded`: if `Text` is non-empty → `ApplyFilter()` immediately (no 300ms debounce); if empty → `NotifyFilterReady`.
- `OnGridChanged`: calls `NotifyFilterPending` on the new grid and `NotifyFilterReady` on the old one.

### `OpenDataGridComboFilter` (ComboBox)
- DP `Grid` (`OpenDataGrid`) — reference to the grid.
- DP `FilterField` (`string`, single field, required) — without a value: combo disabled, no filter.
- Dropdown: `""` (no filter) + distinct values from `DataSource` sorted alphabetically (`ToString()`).
- Listens to `DataSource` changes via `DependencyPropertyDescriptor` — rebuilds the list automatically.
- Filter: exact match `string.Equals(val, selected, Ordinal)`.
- Selecting `""` → `UnregisterFilter`.

### XAML Usage
```xml
<!-- TextFilter across multiple fields simultaneously -->
<edg:OpenDataGridTextFilter Grid="{Binding ElementName=TheGrid}"
                             FilterFields="Name,Description"
                             Width="220"/>

<!-- ComboFilter — distinct values from the Category field -->
<edg:OpenDataGridComboFilter Grid="{Binding ElementName=TheGrid}"
                              FilterField="Category"
                              Width="160"/>
```

Both filters on the same grid work together (AND). A single `OpenDataGrid` can have any number of filters.

---

## Column Hierarchy

All columns inherit from `OpenDataGridColumn : Freezable`.

| Class | Data type | Editing |
|---|---|---|
| `OpenDataGridLabelColumn` | string/object | Never |
| `OpenDataGridDecimalColumn` | decimal | TextBox with validation, `CanBeginEdit` = digits/decimal/minus |
| `OpenDataGridIntegerColumn` | int | TextBox with validation, `CanBeginEdit` = digits/minus |
| `OpenDataGridDateTimeColumn` | DateTime | TextBox with date parsing |
| `OpenDataGridBooleanColumn` | bool | CheckBox, `CanBeginEdit` = Space |
| `OpenDataGridComboColumn` | int/object (key) | ComboBox, value→text lookup cache |
| `OpenDataGridTimeSpanColumn` | TimeSpan | Read-only; `TimeFormat` DP |
| `OpenDataGridCheckColumn` | bool / CheckedItems | Read-only; toggle by click or `ToggleCheckedKey`; two modes: unbound (state = `CheckedItems`, required) and bound (`BindingPath` → bool field). Bound mode also works without `CheckedItems` (pure-bound) — toggle then only writes the model field; no checked-row styling. |
| `OpenDataGridCustomColumn` | any | `CellTemplate` / `CellEditingTemplate` |

Shared base `OpenDataGridNumericColumnBase` for `Decimal` and `Integer` — contains `Minimum`, `Maximum`, and shared validation logic.

### `IsVisible` and `HiddenColumns`
`OpenDataGrid.HiddenColumns` is a `string` — comma- or semicolon-separated list of column names to hide.
The matching mode is controlled by DP `HiddenColumnsByHeader` (`bool`, default `true`):
- `true` (default) — match by column `Header`
- `false` — match by column `BindingPath`

Columns without a matching key (e.g., `CustomColumn` without a `Header` when `HiddenColumnsByHeader = true`) always remain visible — hide them directly via `IsVisible`.
Changing `HiddenColumns` or `HiddenColumnsByHeader` triggers `ApplyHiddenColumnsToColumns` (via `HiddenColumnsTracker`), which sets `col.IsVisible` and rebuilds headers. When the number of visible columns drops to zero, `_focusedColIndex` is reset to `-1`.

### Freezable as Column Base
Columns must inherit from `Freezable` (not `DependencyObject`) to inherit `DataContext` from the grid.
`OpenDataGrid` adds columns to the logical tree via `LogicalChildren` override.

### Implementing a New Column
You must implement:
1. `CreateDisplayElement()` — element in display mode
2. `CreateEditElement()` — element in edit mode (or `null` if always read-only)
3. `ApplyDisplayValue(element, value)` — set the value directly, **no bindings**
4. `ApplyEditValue(element, value)` — load the value into the editor
5. `ExtractEditValue(element)` — read the value from the editor
6. `CanBeginEdit(Key)` — whether pressing a key should trigger edit mode
7. `GetComparison()` — comparator for sorting
8. `CreateInstanceCore()` — required by `Freezable`

---

## Edit Mode — Read-only Cascade

```
IsEffectivelyReadOnly(row, column) =
    Grid.IsReadOnly
    || Column.IsReadOnly
    || RowReadOnlyBindingPath != null && (bool)GetProperty(dataItem, RowReadOnlyBindingPath)
```

Check this cascade before entering edit mode in `OpenDataGrid.TryBeginEdit()` (`OpenDataGrid.Editing.cs`).

### `LiveEdit` (DP, default `true`)
When `true`, every editor change immediately saves to the model via `OnLiveEditValueChanged`. Two effects:
- `CommitEdit` **skips** the second write (to avoid a duplicate `PropertyChanged`).
- `CancelEdit` must restore `PreEditValue` back to the model.

When `false`, the write only happens on `CommitEdit` (Enter/focus lost).

### Auto-commit on Focus Loss
`OnLostKeyboardFocus` (`OpenDataGrid.Navigation.cs`) calls `CommitEdit(restoreFocus: false)` when focus leaves the grid entirely. Tabbing to another control = commit.

### `IsPotentialCharacterEditKey` + `BeginCharacterEdit`
"Type-to-edit": `NavigationEngine` passes the key to `TryBeginEdit`, which via `Dispatcher.BeginInvoke(DispatcherPriority.Input)` inserts the first character into the editor. Numeric/combo columns override `CanBeginEdit` to block letters.

---

## Coloring

Priority logic is centralized in `src/Open.DataGrid/Rendering/CellBackgroundResolver.cs`.

| Priority | Layer | Source |
|---|---|---|
| 1 | **Focused cell brush** | `Cell.SetFocused` — lighter/darker variant of `SelectionBrush` (via `BrushShift.ComputeFocusBrush`) |
| 2 | **Row SelectionBrush** | when `IsRowSelected` — always visible, even in columns with `Column.Background` |
| 3 | **Column.Background** | `Column.Background` DP, when `BackgroundOverridesData == true` (default) |
| 4 | **CellBackgroundBindingPath** | path to `Brush` on the data object (per cell) |
| 5 | **Column.Background** (fallback) | when `BackgroundOverridesData == false` and data returned no brush |
| 6 | **Row resolved background** | `rowColorResolver` — `AlternateRowBackground` / `AlternateRowShift` / `RowBackgroundSelector` / `RowBackgroundBindingPath` / `RowBackground` / White |

**Alternate row modifier:** layers 3–5 on rows at an odd visual position (`logicalIndex % 2 != 0` — position after sort/filter, not the `DataSource` index) are additionally shifted by `BrushShift.Apply(brush, AlternateRowShift)` when `AlternateRowShift > 0`. For `Column.Background` the result is cached in `Column.GetAlternateBackground(shift)` (zero allocations on the hot path). Layers 1, 2, and 6 are not shifted.

**Text color:** `CellForegroundResolver` (`src/Open.DataGrid/Rendering/CellForegroundResolver.cs`) — pure function with a hierarchy: selected cell → `SelectionForeground` (if set) ?? auto-contrast from background (via `BrushShift`), normal → `Column.Foreground` ?? `Grid.Foreground` ?? null (WPF default). Symmetric architecture to `CellBackgroundResolver`.

Rendering helper classes:
- `src/Open.DataGrid/Rendering/BrushShift.cs` — `Apply(brush, shift)`, `ComputeFocusBrush(selectionBrush)`
- `src/Open.DataGrid/Rendering/CellBackgroundContext.cs` — readonly struct with all layers
- `src/Open.DataGrid/Rendering/CellBackgroundResolver.cs` — pure static `Resolve(in CellBackgroundContext)`
- `src/Open.DataGrid/Rendering/CellForegroundResolver.cs` — pure static foreground function

---

## Keyboard Navigation

`NavigationEngine` supports two modes:

- **Row** — Up/Down/PageUp/PageDown/Home(Ctrl)/End(Ctrl)
- **Cell** — additionally Left/Right/Tab/Shift+Tab, F2 = enter edit mode

Smart edit entry — `column.CanBeginEdit(key)` decides whether a key triggers editing:
- Letters do **not** enter edit mode for `Decimal` / `Integer` fields
- `LabelColumn.CanBeginEdit` always returns `false`

After committing an edit (Enter) the cursor moves down one row.
Escape = cancel without saving.

`NavigationEngine` exposes 4 events handled by `OpenDataGrid`: `NavigationRequested`, `EditRequested`, `EditCommitRequested`, `EditCancelRequested` — the grid subscribes to them in `InitializeNavEngine()`.

---

## Demo Application

The `Open.DataGrid.Demo` project uses **GalaSoft MVVMLight** (`MvvmLightLibs 5.4.1.1`).

- `MainViewModel` generates 1,000,000 rows of `SampleRow` (40 properties of various types) on a background thread.
- Every 10th row: `IsRowReadOnly = true` → row not editable.
- Every 100th row: `RowColor = Brushes.LightYellow` → yellow background.
- `SampleRow` has properties for all column types.

---

## Code Conventions

- **Everything in English** — code, comments, commit messages, PR descriptions, documentation. No exceptions.
- Language: C# 7.3, .NET Framework 4.6.2
- Naming: PascalCase for types and properties, `_camelCase` for private fields
- Dependency Properties: `XxxProperty` constant + CLR wrapper, standard WPF pattern
- No `async void` except in event handlers
- No unnecessary abstractions — if the solution is simple, keep it simple

---

## `OpenDataGrid` File Structure (partial classes)

`OpenDataGrid` is split into 5 partial files + coordinator classes — find code in the appropriate file instead of grepping:

| File | Contents |
|---|---|
| `OpenDataGrid.cs` | DPs, fields, `OnApplyTemplate`, `TryInitialize`/`FinishInitialize`, `BuildSortEngineSync`, `ApplyFilters`/`FinishApplyFilters`, `BuildPropertyAccessors`, sorting, column helpers; delegates to coordinators |
| `OpenDataGrid.ColumnsLayout.cs` | Forwarders to `HeaderPresenter`: `BuildHeaders`, `LayoutHeaders`, `ApplyHiddenColumns`, `RebuildColumnVisibilitySubscriptions`, visible column lists |
| `OpenDataGrid.Scrolling.cs` | `OnVerticalScroll`, `OnHorizontalScroll`, `OnMouseWheel`, `OnCanvasSizeChanged`, `UpdateColumnRange` |
| `OpenDataGrid.Navigation.cs` | Focus/keyboard handlers, `EnsureRowVisible`, `EnsureColumnVisible`, `ScrollToItem`, `SelectColumn` |
| `OpenDataGrid.Editing.cs` | `TryBeginEdit`, `CommitEdit`, `CancelEdit`, `LiveEdit` |
| `GridLifecycleState.cs` | Lifecycle state enum: `NoTemplate`, `TemplateReady`, `WaitingForViewport`, `Initializing`, `Ready`, `Unloaded` |
| `GridLifecycleController.cs` | State machine; guards `CanHandleInput`, `IsInitialized`, `IsUnloaded`, `NeedsViewport`; named transitions; DEBUG log |
| `SelectionFocusSynchronizer.cs` | `OnCanvasMouseDown`, `SelectByDataIndex`, `ApplyCellFocus`, `InternalSelectionChange` guard, `PendingSelectedItem` |
| `GridWorkCoordinator.cs` | `BeginPendingWork`/`EndPendingWork`, `NotifyFilterPending`/`NotifyFilterReady`, `WorkToken` RAII, overlay visibility |
| `HeaderPresenter.cs` | Header cell creation, column resize thumbs, DPD subscriptions, column list cache |
| `HiddenColumnsTracker.cs` | `HiddenColumns` DP apply logic, incremental visibility updates |

> **Note:** `OpenDataGrid.Selection.cs` **does not exist** — that logic lives in `SelectionFocusSynchronizer`.

---

## Selection — Mechanisms

### `PendingSelectedItem` (in `SelectionFocusSynchronizer`)
If `SelectedItem` arrives from a binding **before** `DataSource` or before `GridLifecycleController.IsInitialized` returns `true`, the value is stored. `TryInitialize` restores it after initialization completes. This makes the order of DP assignment in XAML irrelevant.

### `AutoScrollToSelectedItem` (DP, default `true`)
When the VM changes `SelectedItem`, the grid calls `EnsureRowVisible` before `SelectByDataIndex`. Set to `false` to disable auto-scroll (e.g. when the VM controls scroll position itself).

### `RequireSelection` (DP, default `true`)
When `true`, the grid enforces the invariant: `SelectedItem != null` whenever visible rows exist (`sortedIndices.Length > 0`). Applies to:
- VM sets `SelectedItem = null` → grid automatically selects the first visible row.
- Internal paths (filter, sort, CollectionChanged) when `SelectedItem == null` → `ClearSelectionAndFallbackToFirst()`.
- VM points to an item outside the active filter → `ApplySelectedItemFromViewModel` falls back to the first visible.
When `false` — the VM has full control, null is accepted.

### `InternalSelectionChange` (guard in `SelectionFocusSynchronizer`)
`SelectByDataIndex` (called by UI: click, keyboard) sets `InternalSelectionChange = true` around `SelectedItem = ...`. `OnSelectedItemChanged` with this flag exits immediately — a change from the UI does **not** trigger `EnsureRowVisible`. UI scroll is handled by `NavigationEngine` directly.

### `SelectedColumn` (DP, default `null`)
The name (`Header` or `BindingPath`) of the column where the default cell selection should land on **first** keyboard focus in `NavigationMode.Cell`.
- Case-insensitive matching; first by `Header`, then by `BindingPath`.
- No match or empty value → falls back to the first visible column (index 0).
- Works only once — when `_cellFocusApplied == false` (no prior selection). Subsequent focus entries do not reset the position.
- Ignored in `NavigationMode.Row`.

### `ApplyCellFocus` (in `SelectionFocusSynchronizer`)
Single path for applying cell focus: updates `_focusedRowSortedIndex`, `_focusedColIndex`, `SelectedItem`, `NavigationEngine` position, focus render on the row, and the bindable `FocusedColumnIndex` / `FocusedColumnHeader`. The `ensureVisible` parameter controls calling `EnsureRowVisible` / `EnsureColumnVisible`.
Used by: mouse click (`ensureVisible: false`), keyboard navigation (`ensureVisible: true`), default selection on focus (`ensureVisible: true`).

### `FocusedColumnIndex` / `FocusedColumnHeader` (bindable DPs, VM read-only)
Updated by `ApplyCellFocus`. Allow the VM to observe the current cell focus position without polling the grid.

### `EnterCommand` (DP, `ICommand`, default `null`)
When set, pressing Enter outside edit mode executes the command with `SelectedItem` as the parameter instead of starting an edit. F2 still enters edit mode. Logic in `OpenDataGrid.Navigation.cs` (`TryExecuteEnterCommand`).

### `RestoreDefaultColumnOnFocus` (DP, default `false`)
When `true`, every time the grid receives keyboard focus it moves the column to `SelectedColumn` — preserving the current row. Differs from the one-shot `ApplyDefaultCellSelectionIfNeeded` (which only fires when `_cellFocusApplied == false`).

### `ScrollToItem(int dataIndex, int columnIndex = 0)` — public API
Programmatic scroll and selection via `SortEngine.FindSortedPosition(dataIndex)` → `ApplyCellFocus(ensureVisible: true)`. Works regardless of current sorting. Implemented in `OpenDataGrid.Navigation.cs`.

### `SelectColumn(string nameOrPath)` — public API
Moves cell focus to the specified column (match by `Header` or `BindingPath`, case-insensitive). Works in `NavigationMode.Cell` only. **Different from DP `SelectedColumn`**, which concerns only the default column on first focus.

---

## Theming / Dark Mode

### `Theme` DP (`GridTheme` enum, default `Light`)

| Value | Behaviour |
|---|---|
| `Light` | Default — identical to the appearance before this feature. Sets nothing; all existing brush defaults apply. |
| `Dark` | Professional dark palette (VS Code-inspired: `#1E1E1E` rows, `#2D2D30` header, `#E0E0E0` text, `#094771` selection). |
| `System` | Reads `HKCU\...\Themes\Personalize\AppsUseLightTheme` and reacts **live** to `SystemEvents.UserPreferenceChanged`. |

### Override rule — user values always win

The theme is a *default layer*, not an override. `ApplyTheme` tracks which DPs it wrote (`_themeWritten` dictionary). It only writes to a DP when:
1. The local value is `DependencyProperty.UnsetValue` (user never set it), **or**
2. The current local value equals the value the theme previously wrote (re-theming its own value).

This means: set `HeaderBackground = Brushes.Green` in XAML → switching to Dark leaves it green.

### Managed DPs (theme writes these when Dark)

`Control.Background`, `Control.BorderBrush`, `Control.Foreground`, `HeaderBackground`, `HeaderForeground`,
`ColumnSeparatorBrush`, `AlternateRowBackground`, `RowBackground`, `SelectionBrush`, `GridLinesBrush`,
`HeaderAreaBackground`, `FilterRowBackground`, `ChromeBorderBrush`, `LoadingOverlayBackground`.

### Chrome DPs (new, consumed by ControlTemplate)

| DP | Default (Light) | Dark value | Used in template |
|---|---|---|---|
| `HeaderAreaBackground` | `#F0F0F0` | `#2D2D30` | header strip `Background` |
| `FilterRowBackground` | `#FAFAFA` | `#252526` | filter strip `Background` |
| `ChromeBorderBrush` | `#CCCCCC` | `#3F3F46` | header/filter bottom border; outer `BorderBrush` |
| `LoadingOverlayBackground` | `#30FFFFFF` | `#40000000` | `PART_LoadingOverlay` `Background` |

### ScrollBars

Custom minimal style (`ODG.ScrollBarStyle` in `Generic.xaml`) — no arrows, rounded thumb.
Colours via `DynamicResource` keys (`ODG.ScrollBar.Track`, `ODG.ScrollBar.Thumb`, `ODG.ScrollBar.ThumbHover`, `ODG.ScrollBar.ThumbDrag`).
Light defaults are defined as static resources in `Generic.xaml`; Dark values are pushed into `this.Resources` per-instance by `ApplyTheme`.

### Palette source

`src/Open.DataGrid/Rendering/ThemePalette.cs` — two static frozen-brush instances `ThemePalette.Light` and `ThemePalette.Dark`.
System theme detection: `src/Open.DataGrid/Rendering/SystemTheme.cs` (`IsDark()` reads registry).

### Live System refresh

`OnThemeChanged` subscribes to `Microsoft.Win32.SystemEvents.UserPreferenceChanged` (category `General`) **only when `Theme == System`**.
Handler marshals to the UI dispatcher and calls `ApplyTheme()`. Subscription is released in `OnGridUnloaded` and re-attached in `OnGridLoaded` to prevent memory leaks (static event).

### XAML usage

```xml
<!-- Explicit dark mode -->
<edg:OpenDataGrid Theme="Dark" .../>

<!-- Follow Windows (live) -->
<edg:OpenDataGrid Theme="{Binding CurrentTheme}" .../>
```

---

## Dependency Properties — Overview

Beyond the sections above, key grid DPs:

| DP | Type | Notes |
|---|---|---|
| `RowHeight` | `double` | affects `visibleRowCount` — change requires `InitializeRowEngine()` |
| `HeaderHeight` | `double` | header row height |
| `ShowGridLines` / `GridLinesBrush` | `bool` / `Brush` | grid drawing via `SetGridLinePen` on rows |
| `VerticalGridLinesBrush` | `Brush` (default `null`) | column separator color; `null` = reuse `GridLinesBrush` |
| `SelectionForeground` | `Brush` (default `null`) | text color of selected cells; `null` = auto-contrast from `SelectionBrush` |
| `CheckedItems` | `IList` | collection of checked rows (VM provides ObservableCollection); required for `ToggleChecked` to work |
| `ToggleCheckedKey` | `Key` (default `Key.None`) | key to toggle checked state; `Key.None` = disabled |
| `ClearNumericCellOnDelete` | `bool` (default `false`) | Delete outside edit mode zeroes a non-read-only Integer/Decimal cell (clamped to `Minimum`/`Maximum`) |
| `CheckedRowForeground` / `CheckedRowBackground` / `CheckedRowFontWeight` | `Brush` / `Brush` / `FontWeight?` | visual style for rows in `_checkedSet` |
| `HeaderFontFamily/Size/Weight` | — | header typography; `HeaderFontSize` is nullable (WPF default fallback) |
| `CellFontFamily/Size/Weight` | — | cell typography; `CellFontSize`/`CellFontWeight` are nullable |
| `Column.MinWidth/MaxWidth` | `double` | clamping on header resize thumb |
| `Column.Foreground` | `Brush` | cell text color in the column |
| `Column.BackgroundOverridesData` | `bool` (default `true`) | priority of `Column.Background` vs `CellBackgroundBindingPath` |
| `Theme` | `GridTheme` (default `Light`) | visual theme: `Light` / `Dark` / `System`; see **Theming** section |
| `HeaderAreaBackground` | `Brush` | header strip bg; default `#F0F0F0`; template-binding |
| `FilterRowBackground` | `Brush` | filter strip bg; default `#FAFAFA`; template-binding |
| `ChromeBorderBrush` | `Brush` | chrome separator/border colour; default `#CCCCCC`; template-binding |
| `LoadingOverlayBackground` | `Brush` | loading overlay; default `#30FFFFFF`; template-binding |

## Rendering Layer API

When modifying rendering code — to avoid rewriting what already exists:

**`OpenDataGridRow`** (`src/Open.DataGrid/Controls/OpenDataGridRow.cs`):
`Initialize`, `Bind`/`Unbind`, `UpdateSelection`, `SetCellFocus`, `SetGridFocus`, `UpdateDefaultCellFont`, `GetCellValue`, `IsCellLoaded`, `UpdateColumnLayout`, `RefreshVisibleCellValues`, `SetGridLinePen`

**`OpenDataGridCell`** (`src/Open.DataGrid/Controls/OpenDataGridCell.cs`):
`UpdateValue`, `UpdateSelectionBrush`, `UpdateRowState`, `UpdateCellBackground`, `SetFocused`, `BeginEdit`/`CommitEdit`/`CancelEdit`, `IsInEditMode`, `EditValueChanged` event, `PreEditValue`

**Virtualization abstraction layer:**
`IGridCanvas` / `IRowSlot` — interfaces allowing `RowVirtualizationEngine` to be tested without WPF (test implementations: `TestGridCanvas`, `TestRowSlot` in `Open.DataGrid.Tests/Helpers/`).
`WpfGridCanvas` — production `IGridCanvas` implementation wrapping `Canvas`.

---

## Unit Tests

The `Open.DataGrid.Tests` project (NUnit 3, STA) — **451 unit tests + 75 integration tests = 526 total across 42 files**, covering logic isolated from UI:

| What is tested | Test class | Count |
|---|---|---|
| Lifecycle states, guards, Unloaded invariant | `GridLifecycleControllerTests` | 43 |
| Prefix-sum, binary search of visible columns | `ColumnVirtualizationEngineTests` | 16 |
| Ring buffer — rotation, no allocations during scroll | `RowPoolTests` | 12 |
| Scroll viewport math | `VirtualViewportTests` | 10 |
| Index sorting, direction toggling, `FindSortedPosition` | `SortEngineTests` | 18 |
| SortEngine with a filtered index set | `SortEngineFilteredTests` | 6 |
| Background sort cancellation | `SortEngineCancellationTests` | 5 |
| Background sort race conditions | `SortEngineRaceConditionTests` | 9 |
| FilterEngine — AND logic, register/unregister, multi-filter | `FilterEngineTests` | 9 |
| FilterEngine thread safety | `FilterEngineThreadSafetyTests` | 2 |
| Navigation state: Init, UpdateStats, SetCurrentPosition | `NavigationEngineTests` | 14 |
| `CanBeginEdit`, `GetComparison`, `ExtractEditValue` | `DecimalColumnTests` | 9 |
| `CanBeginEdit`, `GetComparison`, `ExtractEditValue` | `IntegerColumnTests` | 4 |
| `CanBeginEdit`, `GetComparison`, `ExtractEditValue` | `LabelColumnTests` | 7 |
| `CanBeginEdit`, `GetComparison`, `ExtractEditValue` | `BooleanColumnTests` | 5 |
| `CanBeginEdit`, `GetComparison`, `ExtractEditValue` | `ComboColumnTests` | 4 |
| `CanBeginEdit`, `GetComparison`, `ExtractEditValue` | `DateTimeColumnTests` | 14 |
| `CanBeginEdit`, `GetComparison`, `ExtractEditValue` | `TimeSpanColumnTests` | 13 |
| `CanBeginEdit`, `GetComparison`, two checked modes | `CheckColumnTests` | 13 |
| HiddenColumns DP logic | `HiddenColumnsTests` | 14 |
| HiddenColumnsTracker incremental changes | `HiddenColumnsTrackerTests` | 14 |
| BrushShift — Apply, ComputeFocusBrush | `BrushShiftTests` | 9 |
| Cell background layer priority | `CellBackgroundResolverTests` | 17 |
| Grid lines visibility | `GridLinesVisibilityTests` | 4 |
| GridWorkCoordinator — refcount, epoch, WorkToken RAII, overlay mode routing | `GridWorkCoordinatorTests` | 13 |
| Pending selection, AutoScrollToSelectedItem, guard | `SelectedItemTests` | 9 |
| RequireSelection invariant (VM null, filter, sort, CollectionChanged, pending) | `SelectionInvariantTests` | 15 |
| Column focus stability | `FocusColumnStabilityTests` | 5 |
| Placeholder row (no data) | `PlaceholderRowTests` | 6 |
| RestoreDefaultColumnOnFocus | `RestoreDefaultColumnOnFocusTests` | 3 |
| Selection edge cases (ReadOnly, filter, None) | `SelectionEdgeCaseTests` | 5 |
| CheckedItems — ToggleChecked, MarkedItems, duplicates, two sources of truth | `CheckedItemsTests` | 31 |
| TextFilter clearing | `TextFilterClearTests` | 8 |
| Horizontal scroll rebinding | `HScrollValueRebindTests` | 4 |
| RowBackgroundSelector | `RowBackgroundSelectorTests` | 5 |
| PropertyAccessorCache — compiled delegates | `PropertyAccessorCacheTests` | 7 |
| Column reorder: AllowColumnReorder, PerformReorder, focus remap | `HeaderPresenterReorderTests` | 9 |
| Header interactions before data is loaded | `LoadingStateInteractionTests` | 4 |
| DiacriticsHelper — PL/Unicode normalisation, edge cases | `DiacriticsHelperTests` | 22 |
| CellForegroundResolver — highlight contrast, column/grid fallback | `CellForegroundResolverTests` | 7 |
| Theme palette sanity, DP apply/revert, user override, System stub | `ThemeTests` | 15 |

**Testability boundary** — UI classes requiring a visual tree are tested in `SelectedItemTests` by hosting in a `Window` (tests marked `[Category("Integration")]`). `NavigationEngine.HandleKeyDown` requires `PresentationSource` — not unit tested.

**Test patterns:**
- Each test method should have `// Given / When / Then` comment sections.
- `[Apartment(ApartmentState.STA)]` — required for all classes with `DependencyObject`
- Decimal parsing: use `CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator` (PL uses `,`)
- `SpinWait.SpinUntil` to wait for `SortEngine.SortCompleted` (background `Task.Run`)
- Tests with `Window`: `window.Show(); window.UpdateLayout();` — sufficient for `TryInitialize` to complete the full path
- `[Category("Integration")]` on Window tests — can be skipped in headless CI (`--filter Category!=Integration`)
- Name helper variables from factory struct helpers (e.g. `CreatePool`, `CreateEngine`) as `result`

---

## Building and Running

Requires **VS Developer Command Prompt** (or MSBuild on PATH):
`C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe`

```bat
:: Build
msbuild OpenDataGrid.slnx -restore

:: Run demo (after building)
src\Open.DataGrid.Demo\bin\Debug\net462\Open.DataGrid.Demo.exe

:: Tests
vstest.console.exe src\Open.DataGrid.Tests\bin\Debug\net462\Open.DataGrid.Tests.dll

:: Tests without Integration
vstest.console.exe src\Open.DataGrid.Tests\bin\Debug\net462\Open.DataGrid.Tests.dll /TestCaseFilter:"TestCategory!=Integration"
```

**After every significant change, run the tests and make sure they all pass before committing.**

---

## Known Limitations / TODO

- Sorting with 1M rows (~200–400ms) — acceptable, runs in background
- No scroll animation (per-row scroll, not per-pixel) — intentional performance trade-off
- `OpenDataGridRow.UpdateColumnLayout` is called on every horizontal scroll change — if the column count is very large, consider debouncing
- Editing value written via Fasterflect setter — for `INotifyPropertyChanged` scenarios the model is responsible for notifying about the change
- ✅ Column resizing with mouse — implemented (`HeaderPresenter`, `Thumb` with `Cursors.SizeWE`)
- ✅ Unit tests — implemented (451 unit + 75 integration = 526 total, 42 files in `Open.DataGrid.Tests`)
- ✅ Loading indicator + deferred render — `LoadingIndicatorMode`, async filter, coalescing `CollectionChanged`, `NotifyFilterPending/Ready`
- ✅ Dark Mode / Theme (Light / Dark / System) — `Theme` DP (`GridTheme` enum); live System follows Windows `AppsUseLightTheme` via `SystemEvents`; palette in `ThemePalette`; user-set brush values always override theme; 15 unit tests
- ✅ `OpenDataGridBooleanColumn` — implemented
- ✅ Architecture refactor — `SelectionFocusSynchronizer`, `GridWorkCoordinator`, `HeaderPresenter`, `HiddenColumnsTracker`
- ✅ Lifecycle state machine — `GridLifecycleController` + `GridLifecycleState` replace `_initialized`, `_wasUnloaded`, `_rowEngineNeedsReinit` flags; 43 unit tests
- Required: ability to set default row height at the control level (accounting for the impact on virtualization calculation cost)
- ✅ Column reordering via drag & drop — `AllowColumnReorder` DP (default `true`); header drag with drop indicator; public API `ReorderColumn(int, int)`; 9 unit tests
- ✅ `OpenDataGridCheckColumn` + `CheckedItems` — two modes (unbound/bound), `ToggleChecked`, `MarkedItems`, `IsItemChecked`, `CheckedRowForeground/Background/FontWeight`; `ToggleCheckedKey` (default `Key.None`); 31+13 tests
- Required: integration tests for `NavigationEngine.HandleKeyDown` (requires WPF window/PresentationSource)
