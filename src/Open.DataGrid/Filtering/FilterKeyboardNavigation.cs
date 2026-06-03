using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Open.DataGrid.Controls;

namespace Open.DataGrid.Filtering
{
    internal static class FilterKeyboardNavigation
    {
        public static void AttachTo(FrameworkElement editor, OpenDataGrid grid)
        {
            if (editor == null || grid == null) return;
            editor.PreviewKeyDown += (s, e) =>
            {
                if (e.Handled) return;
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
                        shouldFocus = IsCaretAtStart(e.OriginalSource);
                        break;
                    case Key.Right:
                        shouldFocus = IsCaretAtEnd(e.OriginalSource);
                        break;
                    default:
                        shouldFocus = false;
                        break;
                }
                if (!shouldFocus) return;
                grid.Focus();
                e.Handled = true;
            };
        }

        private static bool IsCaretAtStart(object src)
        {
            var tb = src as TextBox;
            if (tb == null) return true;
            return tb.SelectionLength == 0 && tb.CaretIndex == 0;
        }

        private static bool IsCaretAtEnd(object src)
        {
            var tb = src as TextBox;
            if (tb == null) return true;
            return tb.SelectionLength == 0 && tb.CaretIndex == (tb.Text != null ? tb.Text.Length : 0);
        }
    }
}
