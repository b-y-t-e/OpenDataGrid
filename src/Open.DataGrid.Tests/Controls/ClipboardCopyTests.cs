using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using NUnit.Framework;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Models;

namespace Open.DataGrid.Tests.Controls
{
    /// <summary>Ctrl+C copies the focused cell (Cell mode) or row (Row mode); Ctrl+Shift+C adds the headers.</summary>
    [TestFixture, Apartment(ApartmentState.STA), Category("Integration")]
    public class ClipboardCopyTests
    {
        private sealed class Item
        {
            public string Name { get; set; }
            public int Qty { get; set; }
            public DateTime Date { get; set; }
            public bool Active { get; set; }
        }

        private struct Rig
        {
            public OpenDataGrid Grid;
            public Window Window;
            public List<string> Copied;
        }

        private static Rig CreateRig(NavigationMode mode)
        {
            var data = new List<Item>
            {
                new Item { Name = "Apple", Qty = 3, Date = new DateTime(2026, 1, 2), Active = true },
                new Item { Name = "Pear\tgreen", Qty = 7, Date = new DateTime(2026, 3, 4), Active = false },
            };

            var grid = new OpenDataGrid { NavigationMode = mode };
            grid.Columns.Add(new OpenDataGridLabelColumn { Header = "Name", BindingPath = "Name", Width = 100 });
            grid.Columns.Add(new OpenDataGridIntegerColumn { Header = "Qty", BindingPath = "Qty", Width = 60 });
            grid.Columns.Add(new OpenDataGridDateTimeColumn { Header = "Date", BindingPath = "Date", Width = 100 });
            grid.Columns.Add(new OpenDataGridBooleanColumn { Header = "Active", BindingPath = "Active", Width = 60 });
            grid.DataSource = data;

            var copied = new List<string>();
            grid.ClipboardWriter = copied.Add;

            var window = new Window
            {
                Width = 500,
                Height = 300,
                Content = grid,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
            };
            window.Show();
            window.UpdateLayout();
            return new Rig { Grid = grid, Window = window, Copied = copied };
        }

        [Test]
        public void CtrlC_InCellMode_CopiesTheFocusedCellAsDisplayed()
        {
            // Given
            var rig = CreateRig(NavigationMode.Cell);
            rig.Grid.ScrollToItem(0, 2);

            // When
            bool handled = rig.Grid.TryHandleCopyKey(Key.C, ModifierKeys.Control);

            // Then
            Assert.That(handled, Is.True);
            Assert.That(rig.Copied, Is.EqualTo(new[] { "2026-01-02" }));
            rig.Window.Close();
        }

        [Test]
        public void CtrlShiftC_InCellMode_PrependsTheColumnHeader()
        {
            // Given
            var rig = CreateRig(NavigationMode.Cell);
            rig.Grid.ScrollToItem(0, 1);

            // When
            rig.Grid.TryHandleCopyKey(Key.C, ModifierKeys.Control | ModifierKeys.Shift);

            // Then
            Assert.That(rig.Copied, Is.EqualTo(new[] { "Qty" + Environment.NewLine + "3" }));
            rig.Window.Close();
        }

        [Test]
        public void CtrlC_InRowMode_CopiesAllVisibleColumnsTabSeparated()
        {
            // Given
            var rig = CreateRig(NavigationMode.Row);
            rig.Grid.ScrollToItem(0);

            // When
            rig.Grid.TryHandleCopyKey(Key.C, ModifierKeys.Control);

            // Then
            Assert.That(rig.Copied, Is.EqualTo(new[] { "Apple\t3\t2026-01-02\tTrue" }));
            rig.Window.Close();
        }

        [Test]
        public void CtrlShiftC_InRowMode_AddsAHeaderLine()
        {
            // Given
            var rig = CreateRig(NavigationMode.Row);
            rig.Grid.ScrollToItem(0);

            // When
            rig.Grid.TryHandleCopyKey(Key.C, ModifierKeys.Control | ModifierKeys.Shift);

            // Then
            Assert.That(rig.Copied, Is.EqualTo(new[] { "Name\tQty\tDate\tActive" + Environment.NewLine + "Apple\t3\t2026-01-02\tTrue" }));
            rig.Window.Close();
        }

        [Test]
        public void ValueContainingATab_IsQuoted()
        {
            // Given
            var rig = CreateRig(NavigationMode.Cell);
            rig.Grid.ScrollToItem(1, 0);

            // When
            var text = rig.Grid.GetCopyText();

            // Then
            Assert.That(text, Is.EqualTo("\"Pear\tgreen\""));
            rig.Window.Close();
        }

        [Test]
        public void HiddenColumns_AreNotCopied()
        {
            // Given
            var rig = CreateRig(NavigationMode.Row);
            rig.Grid.HiddenColumns = "Qty,Date";
            rig.Window.UpdateLayout();
            rig.Grid.ScrollToItem(0);

            // When
            var text = rig.Grid.GetCopyText(includeHeaders: true);

            // Then
            Assert.That(text, Is.EqualTo("Name\tActive" + Environment.NewLine + "Apple\tTrue"));
            rig.Window.Close();
        }

        [TestCase(false, Key.C, ModifierKeys.Control)]
        [TestCase(true, Key.C, ModifierKeys.None)]
        [TestCase(true, Key.C, ModifierKeys.Control | ModifierKeys.Alt)]
        [TestCase(true, Key.V, ModifierKeys.Control)]
        public void OtherKeysOrCopyDisabled_DoNothing(bool allowCopy, Key key, ModifierKeys modifiers)
        {
            // Given
            var rig = CreateRig(NavigationMode.Cell);
            rig.Grid.AllowCopy = allowCopy;
            rig.Grid.ScrollToItem(0, 0);

            // When
            bool handled = rig.Grid.TryHandleCopyKey(key, modifiers);

            // Then
            Assert.That(handled, Is.False);
            Assert.That(rig.Copied, Is.Empty);
            rig.Window.Close();
        }

        [Test]
        public void ClipboardLocked_CopyReturnsFalse_AndDoesNotThrow()
        {
            // Given
            var rig = CreateRig(NavigationMode.Cell);
            rig.Grid.ScrollToItem(0, 0);
            rig.Grid.ClipboardWriter = _ => throw new System.Runtime.InteropServices.COMException("busy");

            // When
            bool copied = rig.Grid.CopyToClipboard();

            // Then
            Assert.That(copied, Is.False);
            rig.Window.Close();
        }
    }
}
