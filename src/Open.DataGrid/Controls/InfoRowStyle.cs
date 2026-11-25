using System.Windows;
using System.Windows.Media;

namespace Open.DataGrid.Controls
{
    /// <summary>
    /// Snapshot of the grid's info-row settings passed to every pooled <see cref="OpenDataGridRow"/>.
    /// Rebuilt (together with the pool) whenever one of the <c>InfoRow*</c> DPs changes.
    /// </summary>
    internal sealed class InfoRowStyle
    {
        /// <summary>First segment of <c>InfoRowBindingPath</c> — the property name raised by INotifyPropertyChanged.</summary>
        public string     RootPropertyName    { get; set; }
        public Brush      Foreground          { get; set; }
        /// <summary><c>null</c> = use the owner row's background (zebra / row color).</summary>
        public Brush      Background          { get; set; }
        public Brush      AccentBrush         { get; set; }
        public FontStyle  FontStyle           { get; set; }
        public bool       ScrollsHorizontally { get; set; }
    }
}
