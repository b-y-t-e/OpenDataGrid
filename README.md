# OpenDataGrid

A high-performance, fully custom WPF grid built from scratch — no `DataGrid`, `ListView`, `VirtualizingStackPanel`, or any built-in WPF virtualization mechanism. Handles **1,000,000 rows** at 60 fps using a row ring-buffer and direct property access through compiled IL delegates.

## Features

- **True virtualization** — only `visibleRows + 8` row objects exist in memory, regardless of data size
- **O(1) scroll** — ring-buffer rotation, zero allocations during scrolling
- **Async sorting and filtering** — background `Task.Run` with cancellation tokens and epoch-based staleness detection
- **Loading overlay** — semi-transparent overlay and/or progress bar during sorting, filtering, and collection rebuilds
- **Per-row and per-cell coloring** — binding path, selector function, or static brush with a priority hierarchy
- **Full keyboard navigation** — row and cell modes, F2 to edit, Tab to commit, Escape to cancel
- **In-place editing** — typed columns with validation, live-edit mode (saves on every keystroke)
- **Column resizing** — drag the resize thumb in the header
- **Column reordering** — header drag & drop (`AllowColumnReorder`), with public `ReorderColumn` API
- **Text and combo filters** — debounced multi-field filter, distinct-value filter; multiple filters combine as AND
- **Column hiding** — via `HiddenColumns` DP, matched by header or binding path
- **Row checking** (`CheckedItems`) — toggle by click or key (`ToggleCheckedKey`), checked-row styling, `MarkedItems` as union of checked and selected
- **436 unit tests** (+ 75 integration) across 41 files

## Requirements

- .NET Framework 4.6.2+
- WPF (Windows only)

## Quick Start

### 1. Add a reference

Add a project reference to `Open.DataGrid` or install the NuGet package into your WPF project.

### 2. Basic grid in XAML

```xml
<Window xmlns:edg="clr-namespace:Open.DataGrid;assembly=Open.DataGrid">

    <edg:OpenDataGrid x:Name="TheGrid"
                      DataSource="{Binding Items}"
                      SelectedItem="{Binding SelectedItem}"
                      NavigationMode="Cell"
                      RowHeight="24"
                      AlternateRowBackground="#F5F5F5">
        <edg:OpenDataGrid.Columns>
            <edg:OpenDataGridLabelColumn    Header="ID"       BindingPath="Id"       Width="60"/>
            <edg:OpenDataGridLabelColumn    Header="Name"     BindingPath="Name"     Width="180"/>
            <edg:OpenDataGridDecimalColumn  Header="Price"    BindingPath="Price"    Width="100"/>
            <edg:OpenDataGridIntegerColumn  Header="Quantity" BindingPath="Quantity" Width="80"/>
            <edg:OpenDataGridBooleanColumn  Header="Active"   BindingPath="IsActive" Width="70"/>
            <edg:OpenDataGridDateTimeColumn Header="Created"  BindingPath="CreatedAt" Format="yyyy-MM-dd" Width="120"/>
        </edg:OpenDataGrid.Columns>
    </edg:OpenDataGrid>

</Window>
```

### 3. Add filters

```xml
<StackPanel Orientation="Horizontal" Margin="0,0,0,4">
    <!-- Search across multiple fields simultaneously -->
    <edg:OpenDataGridTextFilter Grid="{Binding ElementName=TheGrid}"
                                FilterFields="Name,Description"
                                Width="220"
                                DebounceDelay="300"/>

    <!-- With EnterCommand: Enter executes the command instead of moving focus to the grid -->
    <edg:OpenDataGridTextFilter Grid="{Binding ElementName=TheGrid}"
                                FilterFields="Name"
                                EnterCommand="{Binding OpenItemCommand}"
                                Width="220"/>

    <!-- Filter by distinct values of the Category field -->
    <edg:OpenDataGridComboFilter Grid="{Binding ElementName=TheGrid}"
                                 FilterField="Category"
                                 Width="160"
                                 Margin="8,0,0,0"/>
</StackPanel>
```

Multiple filters on the same grid work together as AND.

`OpenDataGridTextFilter.EnterCommand` — when bound, Enter executes the command with `Grid.SelectedItem` as the parameter and does not move focus to the grid (regardless of `FocusGridOnConfirm`).

## Column Types

| Type | Data | Editing |
|------|------|---------|
| `OpenDataGridLabelColumn` | any (ToString) | Read-only |
| `OpenDataGridDecimalColumn` | `decimal` | TextBox — digits, decimal separator, minus |
| `OpenDataGridIntegerColumn` | `int` | TextBox — digits, minus |
| `OpenDataGridDateTimeColumn` | `DateTime` | TextBox with date parsing, `Format` DP |
| `OpenDataGridBooleanColumn` | `bool` | CheckBox, Space to toggle |
| `OpenDataGridComboColumn` | key (`int`/`object`) | ComboBox with lookup |
| `OpenDataGridTimeSpanColumn` | `TimeSpan` | Read-only; `TimeFormat` DP |
| `OpenDataGridCheckColumn` | `bool` / `CheckedItems` | Read-only; two modes: unbound (state = `CheckedItems`) and bound (`BindingPath`) |
| `OpenDataGridCustomColumn` | any | `CellTemplate` / `CellEditingTemplate` |

All columns share common DPs: `Header`, `BindingPath`, `Width`, `MinWidth`, `MaxWidth`, `IsReadOnly`, `IsSortable`, `IsVisible`, `Background`, `Foreground`, `BackgroundOverridesData`, `CellBackgroundBindingPath`, `SortDirection`.

## Key Dependency Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DataSource` | `IList` | — | Data collection (supports ObservableCollection) |
| `SelectedItem` | `object` | — | Two-way binding for the selected row |
| `NavigationMode` | `NavigationMode` | `Row` | `Row` or `Cell` |
| `RowHeight` | `double` | 24 | Fixed row height (affects virtualization) |
| `IsReadOnly` | `bool` | `false` | Disables all editing |
| `LiveEdit` | `bool` | `true` | Writes to the model on every keystroke |
| `HiddenColumns` | `string` | — | Comma- or semicolon-separated list of column names to hide |
| `HiddenColumnsByHeader` | `bool` | `true` | Match by Header (true) or BindingPath (false) |
| `AlternateRowBackground` | `Brush` | — | Background for every other row |
| `AlternateRowShift` | `double` | 0 | Brightness shift for alternating columns/cells |
| `SelectionBrush` | `Brush` | — | Row selection color |
| `ShowGridLines` | `bool` | `false` | Draw cell borders |
| `LoadingIndicatorMode` | `LoadingIndicatorMode` | `OverlayOnly` | `All`, `OverlayOnly`, `ProgressBarOnly`, `None` |
| `RowReadOnlyBindingPath` | `string` | — | Path to `bool` — per-row read-only |
| `RowBackgroundBindingPath` | `string` | — | Path to `Brush` — per-row color |
| `RowBackgroundSelector` | `Func<object,Brush>` | — | Row color selector function |
| `CellBackgroundBindingPath`* | `string` | — | Path to `Brush` — per-cell color |
| `SelectedColumn` | `string` | — | Default column focus on first keyboard focus (Cell mode) |
| `AutoScrollToSelectedItem` | `bool` | `true` | Scroll to SelectedItem when the VM changes it |
| `AllowColumnReorder` | `bool` | `false` | Allows column reordering via header drag & drop |
| `CheckedItems` | `IList` | — | Collection of checked rows (VM provides `ObservableCollection<T>`); required for toggle to work |
| `ToggleCheckedKey` | `Key` | `Key.None` | Key to toggle checked state; `Key.None` = disabled |
| `CheckedRowBackground` / `CheckedRowForeground` | `Brush` | — | Visual style for checked rows |
| `FocusedColumnIndex` | `int` | -1 | Bindable read: index of the currently focused column |
| `FocusedColumnHeader` | `string` | — | Bindable read: header of the currently focused column |
| `EnterCommand` | `ICommand` | — | Command executed on Enter (parameter: `SelectedItem`); when set, Enter does not start editing |

*`CellBackgroundBindingPath` is a column-level DP, not a grid-level one.

## Public Methods

```csharp
// Scroll to a specific data item and column (works with active sorting/filtering)
grid.ScrollToItem(dataIndex, columnIndex: 0);

// Move cell focus to a column by Header or BindingPath (Cell mode only)
grid.SelectColumn("Name");

// Reorder columns from code (requires AllowColumnReorder = true)
// fromVisibleIndex — current position; toVisibleIndex — insertion point (0..N)
grid.ReorderColumn(fromVisibleIndex: 1, toVisibleIndex: 0);

// Commit the active cell edit from code
grid.CommitEdit();
```

## Coloring Priority

Cell background is resolved in the following order (highest wins):

1. Focused cell highlight (auto-contrast from `SelectionBrush`)
2. `SelectionBrush` of the selected row
3. `Column.Background` when `BackgroundOverridesData = true`
4. Data property via `CellBackgroundBindingPath`
5. `Column.Background` when `BackgroundOverridesData = false`
6. Row background (`AlternateRowBackground` / `RowBackgroundSelector` / `RowBackgroundBindingPath` / `RowBackground`)

## Architecture

```
OpenDataGrid (Controls/)
├── GridLifecycleController    — lifecycle state machine (NoTemplate→Ready⇄WaitingForViewport, Unloaded)
├── RowVirtualizationEngine    — row ring-buffer, O(1) math
├── ColumnVirtualizationEngine — prefix-sum, binary-search of the visible range
├── SortEngine                 — sort on index array (data never moved), background Task.Run
├── FilterEngine               — predicate registry, AND logic, thread-safe
├── NavigationEngine           — keyboard FSM, Row/Cell modes
├── SelectionFocusSynchronizer — selection and focus state hub
├── GridWorkCoordinator        — async work tracking, WorkToken RAII, overlay visibility
├── HeaderPresenter            — header cells, resize thumbs, DPD subscriptions
└── HiddenColumnsTracker       — HiddenColumns DP logic, incremental visibility updates
```

Property access via **Fasterflect** — compiled IL delegates (~10× faster than `PropertyInfo.GetValue`), global cache in `PropertyAccessorCache`.

## Demo Application

`Open.DataGrid.Demo` generates **1,000,000 rows** of `SampleRow` (40 typed properties) on a background thread. Every 10th row is read-only; every 100th has a yellow background. Demonstrates all column types, filters, sorting, and the loading overlay.

## Building

Requires Visual Studio 2022 or MSBuild from a VS Developer Command Prompt:

```bat
msbuild OpenDataGrid.slnx -restore
```

Run the demo:
```bat
src\Open.DataGrid.Demo\bin\Debug\net462\Open.DataGrid.Demo.exe
```

Run tests:
```bat
vstest.console.exe src\Open.DataGrid.Tests\bin\Debug\net462\Open.DataGrid.Tests.dll
```

Skip integration tests (headless CI):
```bat
vstest.console.exe src\Open.DataGrid.Tests\bin\Debug\net462\Open.DataGrid.Tests.dll /TestCaseFilter:"TestCategory!=Integration"
```

## License

MIT
