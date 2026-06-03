using System.Windows;

namespace Open.DataGrid.Filtering
{
    internal interface IColumnFilterController
    {
        FrameworkElement Editor { get; }
        void Attach();
        void Detach();
    }
}
