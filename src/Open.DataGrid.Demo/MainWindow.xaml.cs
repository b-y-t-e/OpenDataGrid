using System;
using System.Linq;
using System.Windows;
using Open.DataGrid.Columns;
using Open.DataGrid.Models;

namespace Open.DataGrid.Demo
{
    public partial class MainWindow : Window
    {
        private static readonly Random _random = new Random();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void RbRow_Checked(object sender, RoutedEventArgs e)
        {
            if (TheGrid != null) TheGrid.NavigationMode = NavigationMode.Row;
        }

        private void RbCell_Checked(object sender, RoutedEventArgs e)
        {
            if (TheGrid != null) TheGrid.NavigationMode = NavigationMode.Cell;
        }

        private void RandomColumn_Click(object sender, RoutedEventArgs e)
        {
            var headers = TheGrid.Columns
                .Where(c => c.IsVisible && !string.IsNullOrEmpty(c.Header))
                .Select(c => c.Header)
                .ToList();

            if (headers.Count == 0) return;

            string picked = headers[_random.Next(headers.Count)];
            TheGrid.SelectedColumn = picked;
            TheGrid.NavigationMode = NavigationMode.Cell;
            rbCell.IsChecked = true;
            TheGrid.SelectColumn(picked);
            SelectedColumnRun.Text = picked;
        }
    }
}
