using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Open.DataGrid.Demo.Models;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;

namespace Open.DataGrid.Demo.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private IList<SampleRow> _rows;
        private SampleRow _selectedItem;
        private bool _isGridReadOnly;
        private string _statusText = "Ready";
        private bool _isLoading;
        private readonly Random _random = new Random();
        private int _nextRowIndex;

        public IList<SampleRow> Rows
        {
            get => _rows;
            private set => Set(ref _rows, value);
        }

        public SampleRow SelectedItem
        {
            get => _selectedItem;
            set
            {
                Set(ref _selectedItem, value);
                StatusText = value != null ? $"Selected: {value.Name} | Row {value.SequenceNo}" : "No selection";
            }
        }

        public bool IsGridReadOnly
        {
            get => _isGridReadOnly;
            set => Set(ref _isGridReadOnly, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => Set(ref _isLoading, value);
        }

        /// <summary>Collection of checked rows exposed to the grid via CheckedItems DP.</summary>
        public System.Collections.ObjectModel.ObservableCollection<SampleRow> CheckedRows { get; }
            = new System.Collections.ObjectModel.ObservableCollection<SampleRow>();

        // Combo sources
        public List<ComboItem> Countries { get; } = GenerateComboItems("Country", 200);
        public List<ComboItem> Departments { get; } = GenerateComboItems("Dept", 50);
        public List<ComboItem> Types { get; } = GenerateComboItems("Type", 30);
        public List<ComboItem> Currencies { get; } = GenerateComboItems("Currency", 20);

        // Commands
        public RelayCommand LoadDataCommand { get; }
        public RelayCommand Load500kCommand { get; }
        public RelayCommand Load100kCommand { get; }
        public RelayCommand Add1000Command { get; }
        public RelayCommand Remove1000Command { get; }
        public RelayCommand ToggleReadOnlyCommand { get; }
        public RelayCommand ClearSelectionCommand { get; }
        public RelayCommand SelectRandomRowCommand { get; }

        private static readonly Brush[] _rowColors =
        {
            null, null, null, null, null, null, null, null, null,
            Brushes.LightYellow,  // every 10th row gets a color
        };

        private static readonly Brush _readOnlyRowBrush = new SolidColorBrush(Color.FromRgb(255, 235, 235));

        /// <summary>
        /// VM-level row color selector — evaluated once per row during virtualization Bind().
        /// Returns MistyRose for expensive rows, LightCyan for cheap rows,
        /// null to fall back to the model's RowColor property.
        /// </summary>
        public Func<object, Brush> RowBackgroundSelector { get; } = item =>
        {
            var row = (SampleRow)item;
            if (row.Amount > 800m) return Brushes.MistyRose;
            if (row.Amount < 50m)  return Brushes.LightCyan;
            return null;
        };

        public MainViewModel()
        {
            LoadDataCommand    = new RelayCommand(async () => await LoadDataAsync(1_000_000));
            Load500kCommand    = new RelayCommand(async () => await LoadDataAsync(500_000));
            Load100kCommand    = new RelayCommand(async () => await LoadDataAsync(100_000));
            Add1000Command     = new RelayCommand(AddRandomRows);
            Remove1000Command  = new RelayCommand(RemoveRandomRows);
            ToggleReadOnlyCommand = new RelayCommand(() => IsGridReadOnly = !IsGridReadOnly);
            ClearSelectionCommand = new RelayCommand(() => SelectedItem = null);
            SelectRandomRowCommand = new RelayCommand(SelectRandomRow);

            // Load initial data
            _ = LoadDataAsync(1_000_000);
        }

        private async Task LoadDataAsync(int count)
        {
            IsLoading = true;
            StatusText = $"Loading {count:N0} rows...";

            // Clear checked rows before swapping the data source — old SampleRow references
            // would otherwise leak across loads and inflate CheckedRows.Count for the new dataset.
            CheckedRows.Clear();

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var rows = await Task.Run(() => GenerateRows(count));

            sw.Stop();
            _nextRowIndex = count;
            Rows = rows;
            IsLoading = false;
            StatusText = $"Loaded {count:N0} rows in {sw.ElapsedMilliseconds}ms";
        }

        private void SelectRandomRow()
        {
            var rows = Rows;
            if (rows == null || rows.Count == 0)
            {
                StatusText = "No data to select";
                return;
            }

            SelectedItem = rows[_random.Next(rows.Count)];
        }

        private void AddRandomRows()
        {
            const int count = 1000;
            var existing = _rows;
            if (existing == null) return;

            var newRows = GenerateRows(count, _nextRowIndex);
            _nextRowIndex += count;

            var merged = new List<SampleRow>(existing.Count + count);
            merged.AddRange(existing);
            merged.AddRange(newRows);
            Rows = merged;
            StatusText = $"Dodano {count} wierszy — razem: {merged.Count:N0}";
        }

        private void RemoveRandomRows()
        {
            const int count = 1000;
            var existing = _rows;
            if (existing == null || existing.Count == 0) return;

            int toRemove = Math.Min(count, existing.Count);
            var indices = new HashSet<int>();
            while (indices.Count < toRemove)
                indices.Add(_random.Next(existing.Count));

            var result = new List<SampleRow>(existing.Count - toRemove);
            for (int i = 0; i < existing.Count; i++)
                if (!indices.Contains(i))
                    result.Add(existing[i]);

            Rows = result;
            StatusText = $"Usunięto {toRemove} wierszy — razem: {result.Count:N0}";
        }

        private List<SampleRow> GenerateRows(int count) => GenerateRows(count, 0);

        private List<SampleRow> GenerateRows(int count, int startIndex)
        {
            var rng = startIndex == 0 ? new Random(42) : new Random();
            var rows = new List<SampleRow>(count);
            var baseDate = new DateTime(2020, 1, 1);

            for (int i = 0; i < count; i++)
            {
                int globalIndex = startIndex + i;
                bool isReadOnly = globalIndex % 10 == 0;
                Brush rowColor = globalIndex % 100 == 0 ? Brushes.LightYellow : null;
                if (isReadOnly) rowColor = _readOnlyRowBrush;

                rows.Add(new SampleRow
                {
                    // Strings
                    Name = $"Item {globalIndex:D7}",
                    Description = $"Description of item number {globalIndex}",
                    Category = Categories[globalIndex % Categories.Length],
                    Status = Statuses[globalIndex % Statuses.Length],
                    Code = $"CODE-{globalIndex % 9999:D4}",
                    Reference = $"REF-{globalIndex:D6}",
                    Notes = globalIndex % 5 == 0 ? $"Note for {globalIndex}" : string.Empty,
                    Tag1 = $"T{globalIndex % 100}",
                    Tag2 = $"G{globalIndex % 50}",
                    Tag3 = globalIndex % 3 == 0 ? "X" : string.Empty,

                    // Decimals
                    Price = Math.Round((decimal)(rng.NextDouble() * 9999.99), 2),
                    Amount = Math.Round((decimal)(rng.NextDouble() * 99999.99), 2),
                    Tax = Math.Round((decimal)(rng.NextDouble() * 1000), 2),
                    Discount = Math.Round((decimal)(rng.NextDouble() * 50), 2),
                    NetAmount = Math.Round((decimal)(rng.NextDouble() * 88888), 2),
                    GrossAmount = Math.Round((decimal)(rng.NextDouble() * 100000), 2),
                    Rate = Math.Round((decimal)(rng.NextDouble() * 5), 4),
                    Balance = Math.Round((decimal)(rng.NextDouble() * 50000 - 10000), 2),

                    // Integers
                    Quantity = rng.Next(0, 9999),
                    Priority = rng.Next(1, 10),
                    Rating = rng.Next(1, 5),
                    StockLevel = rng.Next(0, 999),
                    OrderCount = rng.Next(0, 500),
                    ReturnCount = rng.Next(0, 20),
                    DaysSince = rng.Next(0, 3650),
                    SequenceNo = globalIndex,

                    // DateTimes
                    CreatedDate = baseDate.AddDays(rng.Next(0, 1460)),
                    ModifiedDate = baseDate.AddDays(rng.Next(0, 1460)),
                    DueDate = baseDate.AddDays(rng.Next(-30, 365)),
                    ShipDate = baseDate.AddDays(rng.Next(0, 1000)),
                    InvoiceDate = baseDate.AddDays(rng.Next(0, 1460)),
                    ExpiryDate = baseDate.AddDays(rng.Next(100, 3000)),

                    // TimeSpans
                    WorkDuration    = TimeSpan.FromMinutes(rng.Next(30, 600)),
                    ProcessingTime  = TimeSpan.FromSeconds(rng.Next(10, 3600)),

                    // Combos
                    CountryId = rng.Next(0, 200),
                    DepartmentId = rng.Next(0, 50),
                    TypeId = rng.Next(0, 30),
                    CurrencyId = rng.Next(0, 20),

                    // Control
                    IsRowReadOnly = isReadOnly,
                    RowColor = rowColor,
                    CustomData = $"Custom-{globalIndex}",
                    IsActive = globalIndex % 7 != 0,
                    IsApproved = globalIndex % 9 == 0 ? (bool?)null : (globalIndex % 2 == 0),
                    Towar = new SampleProductInfo
                    {
                        Symbol = $"TOW-{globalIndex % 5000:D4}",
                        Qty = rng.Next(0, 999)
                    }
                });
            }
            return rows;
        }

        private static List<ComboItem> GenerateComboItems(string prefix, int count)
        {
            var list = new List<ComboItem>(count);
            for (int i = 0; i < count; i++)
                list.Add(new ComboItem(i, $"{prefix} {i:D3}"));
            return list;
        }

        private static readonly string[] Categories = { "Electronics", "Clothing", "Food", "Hardware", "Software", "Books", "Toys", "Sports", "Żywność", "Odzież", "Sprzęt" };
        private static readonly string[] Statuses = { "Active", "Inactive", "Pending", "Archived", "Draft", "Approved" };
    }
}
