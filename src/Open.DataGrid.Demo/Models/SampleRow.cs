using System;
using System.Windows.Media;
using GalaSoft.MvvmLight;

namespace Open.DataGrid.Demo.Models
{
    /// <summary>
    /// Test model with 40 properties of various types for performance testing.
    /// All properties raise PropertyChanged. Wartosc is computed via Calculate(),
    /// called automatically when Price or Quantity changes.
    /// </summary>
    public class SampleRow : ObservableObject
    {
        // --- String columns (10) ---
        private string _name;
        public string Name { get => _name; set => Set(ref _name, value); }

        private string _description;
        public string Description { get => _description; set => Set(ref _description, value); }

        private string _category;
        public string Category { get => _category; set => Set(ref _category, value); }

        private string _status;
        public string Status { get => _status; set => Set(ref _status, value); }

        private string _code;
        public string Code { get => _code; set => Set(ref _code, value); }

        private string _reference;
        public string Reference { get => _reference; set => Set(ref _reference, value); }

        private string _notes;
        public string Notes { get => _notes; set => Set(ref _notes, value); }

        private string _tag1;
        public string Tag1 { get => _tag1; set => Set(ref _tag1, value); }

        private string _tag2;
        public string Tag2 { get => _tag2; set => Set(ref _tag2, value); }

        private string _tag3;
        public string Tag3 { get => _tag3; set => Set(ref _tag3, value); }

        // --- Decimal columns (8) ---
        private decimal _price;
        public decimal Price
        {
            get => _price;
            set { if (Set(ref _price, value)) Calculate(); }
        }

        private decimal _amount;
        public decimal Amount { get => _amount; set => Set(ref _amount, value); }

        private decimal _tax;
        public decimal Tax { get => _tax; set => Set(ref _tax, value); }

        private decimal _discount;
        public decimal Discount { get => _discount; set => Set(ref _discount, value); }

        private decimal _netAmount;
        public decimal NetAmount { get => _netAmount; set => Set(ref _netAmount, value); }

        private decimal _grossAmount;
        public decimal GrossAmount { get => _grossAmount; set => Set(ref _grossAmount, value); }

        private decimal _rate;
        public decimal Rate { get => _rate; set => Set(ref _rate, value); }

        private decimal _balance;
        public decimal Balance { get => _balance; set => Set(ref _balance, value); }

        // --- Integer columns (8) ---
        private int _quantity;
        public int Quantity
        {
            get => _quantity;
            set { if (Set(ref _quantity, value)) Calculate(); }
        }

        private int _priority;
        public int Priority { get => _priority; set => Set(ref _priority, value); }

        private int _rating;
        public int Rating { get => _rating; set => Set(ref _rating, value); }

        private int _stockLevel;
        public int StockLevel { get => _stockLevel; set => Set(ref _stockLevel, value); }

        private int _orderCount;
        public int OrderCount { get => _orderCount; set => Set(ref _orderCount, value); }

        private int _returnCount;
        public int ReturnCount { get => _returnCount; set => Set(ref _returnCount, value); }

        private int _daysSince;
        public int DaysSince { get => _daysSince; set => Set(ref _daysSince, value); }

        private int _sequenceNo;
        public int SequenceNo { get => _sequenceNo; set => Set(ref _sequenceNo, value); }

        // --- DateTime columns (6) ---
        private DateTime _createdDate;
        public DateTime CreatedDate { get => _createdDate; set => Set(ref _createdDate, value); }

        private DateTime _modifiedDate;
        public DateTime ModifiedDate { get => _modifiedDate; set => Set(ref _modifiedDate, value); }

        private DateTime _dueDate;
        public DateTime DueDate { get => _dueDate; set => Set(ref _dueDate, value); }

        private DateTime _shipDate;
        public DateTime ShipDate { get => _shipDate; set => Set(ref _shipDate, value); }

        private DateTime _invoiceDate;
        public DateTime InvoiceDate { get => _invoiceDate; set => Set(ref _invoiceDate, value); }

        private DateTime _expiryDate;
        public DateTime ExpiryDate { get => _expiryDate; set => Set(ref _expiryDate, value); }

        // --- TimeSpan columns (2) ---
        private TimeSpan _workDuration;
        public TimeSpan WorkDuration { get => _workDuration; set => Set(ref _workDuration, value); }

        private TimeSpan _processingTime;
        public TimeSpan ProcessingTime { get => _processingTime; set => Set(ref _processingTime, value); }

        // --- Combo columns (4) ---
        private int _countryId;
        public int CountryId { get => _countryId; set => Set(ref _countryId, value); }

        private int _departmentId;
        public int DepartmentId { get => _departmentId; set => Set(ref _departmentId, value); }

        private int _typeId;
        public int TypeId { get => _typeId; set => Set(ref _typeId, value); }

        private int _currencyId;
        public int CurrencyId { get => _currencyId; set => Set(ref _currencyId, value); }

        // --- Computed ---
        private decimal _wartosc;

        /// <summary>Computed: Quantity × Price. Recalculated via Calculate() when either changes.</summary>
        public decimal Wartosc { get => _wartosc; private set => Set(ref _wartosc, value); }

        // --- Control columns (4) ---
        private bool _isRowReadOnly;
        /// <summary>If true, this row is read-only (demo of RowReadOnlyBindingPath).</summary>
        public bool IsRowReadOnly { get => _isRowReadOnly; set => Set(ref _isRowReadOnly, value); }

        private Brush _rowColor;
        /// <summary>Optional per-row background brush (demo of RowBackgroundBindingPath).</summary>
        public Brush RowColor { get => _rowColor; set => Set(ref _rowColor, value); }

        private string _customData;
        /// <summary>Misc string displayed in a custom column.</summary>
        public string CustomData { get => _customData; set => Set(ref _customData, value); }

        private bool _isActive;
        /// <summary>Bool flag shown in a boolean column.</summary>
        public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

        private bool? _isApproved;
        /// <summary>Nullable bool flag shown in a three-state boolean column.</summary>
        public bool? IsApproved { get => _isApproved; set => Set(ref _isApproved, value); }

        private SampleProductInfo _towar;
        /// <summary>Nested object used to demonstrate dot-notation BindingPath support.</summary>
        public SampleProductInfo Towar { get => _towar; set => Set(ref _towar, value); }

        private void Calculate()
        {
            Wartosc = (decimal)_quantity * _price;
        }
    }
}
