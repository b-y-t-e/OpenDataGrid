using GalaSoft.MvvmLight;

namespace Open.DataGrid.Demo.Models
{
    public class SampleProductInfo : ObservableObject
    {
        private string _symbol;
        public string Symbol { get => _symbol; set => Set(ref _symbol, value); }

        private int _qty;
        public int Qty { get => _qty; set => Set(ref _qty, value); }
    }
}
