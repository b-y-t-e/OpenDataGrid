using System.Windows;
using System.Windows.Controls;

namespace Open.DataGrid.Virtualization
{
    /// <summary>
    /// Production implementation of <see cref="IGridCanvas"/> backed by a WPF <see cref="Canvas"/>.
    /// Casting <see cref="IRowSlot"/> to <see cref="UIElement"/> is safe because production row
    /// slots are always <see cref="Controls.OpenDataGridRow"/> (a WPF Panel).
    /// </summary>
    public class WpfGridCanvas : IGridCanvas
    {
        private readonly Canvas _canvas;

        public WpfGridCanvas(Canvas canvas) => _canvas = canvas;

        public double Width => _canvas.ActualWidth;

        public void Clear() => _canvas.Children.Clear();

        public void AddSlot(IRowSlot slot)
        {
            Canvas.SetLeft((UIElement)slot, 0);
            _canvas.Children.Add((UIElement)slot);
        }

        public void PositionSlot(IRowSlot slot, double top, double width, double height)
        {
            Canvas.SetTop((UIElement)slot, top);
            var element = (FrameworkElement)slot;
            element.Width  = width;
            element.Height = height;
        }
    }
}
