using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Models;
using Open.DataGrid.Tests.Helpers;

namespace Open.DataGrid.Tests.Controls
{
    /// <summary>
    /// Excel-like cell range selection (Cell mode): mouse gestures through the internal pointer API,
    /// Shift/Ctrl keys, clearing triggers, rendering and copying of the selected block.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Category("Integration")]
    public class RangeSelectionTests
    {
        private const int NameCol = 0, QtyCol = 1, ActiveCol = 2, KindCol = 3;

        private sealed class Kind
        {
            public int Id { get; set; }
            public string Text { get; set; }
        }

        private struct Rig
        {
            public OpenDataGrid Grid;
            public Window Window;
            public List<SortItem> Data;
            public List<string> Copied;
        }

        private static Rig CreateRig(NavigationMode mode = NavigationMode.Cell, int rows = 50)
        {
            var data = Enumerable.Range(0, rows)
                .Select(i => new SortItem { Name = "N" + i, IntValue = i, BoolValue = i % 2 == 0 })
                .ToList();
            var kinds = new List<Kind> { new Kind { Id = 0, Text = "zero" }, new Kind { Id = 1, Text = "one" } };

            var grid = new OpenDataGrid { NavigationMode = mode, RowHeight = 20 };
            grid.Columns.Add(new OpenDataGridLabelColumn { Header = "Name", BindingPath = "Name", Width = 80 });
            grid.Columns.Add(new OpenDataGridIntegerColumn { Header = "Qty", BindingPath = "IntValue", Width = 80 });
            grid.Columns.Add(new OpenDataGridCheckColumn { Header = "Active", BindingPath = "BoolValue", Width = 80 });
            grid.Columns.Add(new OpenDataGridComboColumn
            {
                Header = "Kind", BindingPath = "IntValue", Width = 80,
                DisplayMemberPath = "Text", ValueMemberPath = "Id", ItemsSource = kinds,
            });
            grid.DataSource = data;

            var copied = new List<string>();
            grid.ClipboardWriter = copied.Add;

            var window = new Window
            {
                Width = 500, Height = 300, Content = grid,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            };
            window.Show();
            window.UpdateLayout();
            grid.ScrollToItem(0, 0);
            Flush(window);
            return new Rig { Grid = grid, Window = window, Data = data, Copied = copied };
        }

        private static void Flush(Window window)
            => window.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

        private static void Drag(OpenDataGrid grid, int fromRow, int fromCol, int toRow, int toCol, ModifierKeys modifiers = ModifierKeys.None)
        {
            grid.BeginRangePointer(fromRow, fromCol, modifiers);
            grid.UpdateRangePointer(toRow, toCol);
            grid.EndRangePointer();
        }

        private static void Click(OpenDataGrid grid, int row, int col, ModifierKeys modifiers = ModifierKeys.None)
        {
            grid.BeginRangePointer(row, col, modifiers);
            grid.EndRangePointer();
        }

        private static OpenDataGridRow VisibleRow(OpenDataGrid grid, int dataIndex)
            => grid._canvas.Children.OfType<OpenDataGridRow>()
                .First(r => r.Visibility == Visibility.Visible && !r.IsInfoRow && r.DataIndex == dataIndex);

        private static string NL => Environment.NewLine;

        // ------------------------------------------------------------------ mouse

        [Test]
        public void Drag_SelectsTheBlock_AndKeepsTheActiveCellAtTheAnchor()
        {
            // Given
            var rig = CreateRig();

            // When
            Drag(rig.Grid, 1, NameCol, 3, QtyCol);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.EqualTo(new[] { new CellRange(1, 0, 3, 1) }));
            Assert.That(rig.Grid.FocusedRowSortedIndex, Is.EqualTo(1));
            Assert.That(rig.Grid.FocusedColIndex, Is.EqualTo(NameCol));
            Assert.That(rig.Grid.SelectedItem, Is.SameAs(rig.Data[1]));
            rig.Window.Close();
        }

        [Test]
        public void Drag_BackToTheAnchorCell_LeavesNoRange()
        {
            // Given
            var rig = CreateRig();
            rig.Grid.BeginRangePointer(2, 0, ModifierKeys.None);
            rig.Grid.UpdateRangePointer(4, 1);

            // When
            rig.Grid.UpdateRangePointer(2, 0);
            rig.Grid.EndRangePointer();

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.Empty);
            rig.Window.Close();
        }

        [Test]
        public void CtrlClick_AddsTheActiveCellAndTheClickedCell_AndMovesTheActiveCell()
        {
            // Given
            var rig = CreateRig();
            Click(rig.Grid, 0, NameCol);

            // When
            Click(rig.Grid, 4, QtyCol, ModifierKeys.Control);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.EqualTo(new[] { new CellRange(0, 0, 0, 0), new CellRange(4, 1, 4, 1) }));
            Assert.That(rig.Grid.FocusedRowSortedIndex, Is.EqualTo(4));
            Assert.That(rig.Grid.FocusedColIndex, Is.EqualTo(QtyCol));
            rig.Window.Close();
        }

        [Test]
        public void CtrlDrag_AddsASecondBlock()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 0, 0, 1, 1);

            // When
            Drag(rig.Grid, 5, 2, 6, 3, ModifierKeys.Control);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.EqualTo(new[] { new CellRange(0, 0, 1, 1), new CellRange(5, 2, 6, 3) }));
            rig.Window.Close();
        }

        [Test]
        public void ShiftClick_SelectsFromTheActiveCell_WithoutMovingIt()
        {
            // Given
            var rig = CreateRig();
            Click(rig.Grid, 2, QtyCol);

            // When
            Click(rig.Grid, 5, KindCol, ModifierKeys.Shift);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.EqualTo(new[] { new CellRange(2, 1, 5, 3) }));
            Assert.That(rig.Grid.FocusedRowSortedIndex, Is.EqualTo(2));
            Assert.That(rig.Grid.FocusedColIndex, Is.EqualTo(QtyCol));
            rig.Window.Close();
        }

        [Test]
        public void PlainClick_ClearsTheRanges()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 0, 0, 3, 3);

            // When
            Click(rig.Grid, 6, 1);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.Empty);
            Assert.That(rig.Grid.FocusedRowSortedIndex, Is.EqualTo(6));
            rig.Window.Close();
        }

        [Test]
        public void PlainClickOnACheckColumn_TogglesOnRelease()
        {
            // Given
            var rig = CreateRig();
            bool before = rig.Data[3].BoolValue;

            // When
            rig.Grid.BeginRangePointer(3, ActiveCol, ModifierKeys.None);
            bool afterPress = rig.Data[3].BoolValue;
            rig.Grid.EndRangePointer();

            // Then
            Assert.That(afterPress, Is.EqualTo(before), "the toggle waits for the release");
            Assert.That(rig.Data[3].BoolValue, Is.EqualTo(!before));
            rig.Window.Close();
        }

        [Test]
        public void DragStartingOnACheckColumn_DoesNotToggle()
        {
            // Given
            var rig = CreateRig();
            bool before = rig.Data[3].BoolValue;

            // When
            Drag(rig.Grid, 3, ActiveCol, 5, ActiveCol);

            // Then
            Assert.That(rig.Data[3].BoolValue, Is.EqualTo(before));
            rig.Window.Close();
        }

        [Test]
        public void CtrlOrShiftClickOnACheckColumn_DoesNotToggle()
        {
            // Given
            var rig = CreateRig();
            bool before = rig.Data[3].BoolValue;

            // When
            Click(rig.Grid, 3, ActiveCol, ModifierKeys.Control);
            Click(rig.Grid, 3, ActiveCol, ModifierKeys.Shift);

            // Then
            Assert.That(rig.Data[3].BoolValue, Is.EqualTo(before));
            rig.Window.Close();
        }

        [Test]
        public void ClickOnAComboColumn_OpensTheEditor_ButADragDoesNot()
        {
            // Given
            var rig = CreateRig();

            // When
            Drag(rig.Grid, 2, KindCol, 4, KindCol);
            bool editingAfterDrag = rig.Grid._editingCell != null;
            Click(rig.Grid, 2, KindCol);

            // Then
            Assert.That(editingAfterDrag, Is.False);
            Assert.That(rig.Grid._editingCell, Is.Not.Null);
            Assert.That(rig.Grid._editingCell.Column, Is.InstanceOf<OpenDataGridComboColumn>());
            rig.Window.Close();
        }

        [Test]
        public void PointerAboveTheViewport_MapsToTheFirstVisibleRow_AndAutoScrollsUp()
        {
            // Given: scrolled down, a drag in progress
            var rig = CreateRig();
            rig.Grid.ScrollToItem(40, 0);
            Flush(rig.Window);
            int firstVisibleBefore = rig.Grid._rowEngine.GetFirstVisibleIndex();
            rig.Grid.BeginRangePointer(40, 0, ModifierKeys.None);

            // When
            rig.Grid._rangeSelection.AutoScrollStep(new Point(10, -30));

            // Then
            Assert.That(rig.Grid._rowEngine.GetFirstVisibleIndex(), Is.LessThan(firstVisibleBefore));
            Assert.That(rig.Grid.SelectedRanges.Count, Is.EqualTo(1));
            Assert.That(rig.Grid.SelectedRanges[0].FirstRow, Is.EqualTo(rig.Grid._rowEngine.GetFirstVisibleIndex()));
            Assert.That(rig.Grid.SelectedRanges[0].LastRow, Is.EqualTo(40));
            rig.Grid.EndRangePointer();
            rig.Window.Close();
        }

        // ------------------------------------------------------------------ keyboard

        [Test]
        public void ShiftArrows_ExtendFromTheActiveCell()
        {
            // Given
            var rig = CreateRig();
            Click(rig.Grid, 2, 1);

            // When
            rig.Grid.TryHandleRangeKey(Key.Down, ModifierKeys.Shift);
            rig.Grid.TryHandleRangeKey(Key.Down, ModifierKeys.Shift);
            rig.Grid.TryHandleRangeKey(Key.Right, ModifierKeys.Shift);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.EqualTo(new[] { new CellRange(2, 1, 4, 2) }));
            Assert.That(rig.Grid.FocusedRowSortedIndex, Is.EqualTo(2));
            Assert.That(rig.Grid.FocusedColIndex, Is.EqualTo(1));
            rig.Window.Close();
        }

        [Test]
        public void ShiftEnd_And_CtrlShiftEnd_ExtendToTheLastColumnAndRow()
        {
            // Given
            var rig = CreateRig();
            Click(rig.Grid, 2, 1);

            // When
            rig.Grid.TryHandleRangeKey(Key.End, ModifierKeys.Shift);
            rig.Grid.TryHandleRangeKey(Key.End, ModifierKeys.Shift | ModifierKeys.Control);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.EqualTo(new[] { new CellRange(2, 1, 49, 3) }));
            rig.Window.Close();
        }

        [Test]
        public void ShiftPageDown_ScrollsTheMovingCornerIntoView()
        {
            // Given
            var rig = CreateRig();
            Click(rig.Grid, 0, 0);

            // When
            for (int i = 0; i < 5; i++) rig.Grid.TryHandleRangeKey(Key.PageDown, ModifierKeys.Shift);
            Flush(rig.Window);

            // Then
            int last = rig.Grid.SelectedRanges[0].LastRow;
            Assert.That(last, Is.GreaterThan(20));
            Assert.That(rig.Grid._rowEngine.IsLineFullyVisible(last), Is.True);
            rig.Window.Close();
        }

        [Test]
        public void CtrlA_SelectsEverything()
        {
            // Given
            var rig = CreateRig();

            // When
            bool handled = rig.Grid.TryHandleRangeKey(Key.A, ModifierKeys.Control);

            // Then
            Assert.That(handled, Is.True);
            Assert.That(rig.Grid.SelectedRanges, Is.EqualTo(new[] { new CellRange(0, 0, 49, 3) }));
            rig.Window.Close();
        }

        [Test]
        public void PlainNavigationKey_ClearsTheRanges_AndStillNavigates()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 0, 0, 3, 3);

            // When
            bool consumed = rig.Grid.TryHandleRangeKey(Key.Down, ModifierKeys.None);

            // Then
            Assert.That(consumed, Is.False, "the key continues to the navigation engine");
            Assert.That(rig.Grid.SelectedRanges, Is.Empty);
            rig.Window.Close();
        }

        [Test]
        public void Escape_ClearsTheRanges()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 0, 0, 3, 3);

            // When
            bool consumed = rig.Grid.TryHandleRangeKey(Key.Escape, ModifierKeys.None);

            // Then
            Assert.That(consumed, Is.True);
            Assert.That(rig.Grid.SelectedRanges, Is.Empty);
            rig.Window.Close();
        }

        // ------------------------------------------------------------------ clearing triggers

        [Test]
        public void Sort_ClearsTheRanges()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 0, 0, 3, 3);
            bool completed = false;
            rig.Grid._sortEngine.SortCompleted += (s, e) => completed = true;

            // When
            rig.Grid._sortEngine.Sort(rig.Grid.GetVisibleColumnList()[QtyCol], rig.Grid.DataSource);
            SpinWait.SpinUntil(() => completed, 3000);
            Flush(rig.Window);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.Empty);
            rig.Window.Close();
        }

        [Test]
        public void Filter_ClearsTheRanges()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 0, 0, 3, 3);

            // When
            rig.Grid.RegisterFilter("t", item => ((SortItem)item).IntValue < 10);
            Flush(rig.Window);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.Empty);
            rig.Grid.UnregisterFilter("t");
            rig.Window.Close();
        }

        [Test]
        public void HidingAColumn_ClearsTheRanges()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 0, 0, 3, 3);

            // When
            rig.Grid.HiddenColumns = "Qty";
            Flush(rig.Window);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.Empty);
            rig.Window.Close();
        }

        [Test]
        public void SwitchingToRowMode_OrDisablingRangeSelection_ClearsTheRanges()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 0, 0, 3, 3);

            // When
            rig.Grid.AllowRangeSelection = false;
            bool clearedByAllow = rig.Grid.SelectedRanges.Count == 0;
            rig.Grid.AllowRangeSelection = true;
            Drag(rig.Grid, 0, 0, 3, 3);
            rig.Grid.NavigationMode = NavigationMode.Row;

            // Then
            Assert.That(clearedByAllow, Is.True);
            Assert.That(rig.Grid.SelectedRanges, Is.Empty);
            rig.Window.Close();
        }

        [Test]
        public void SingleCellRangeOnTheActiveCell_IsTheSameAsNoRange()
        {
            // Given: the active cell is (0, 0)
            var rig = CreateRig();

            // When
            rig.Grid.SelectRange(0, 0, 0, 0);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.Empty);
            rig.Window.Close();
        }

        [Test]
        public void RangeSelectionChanged_IsRaised()
        {
            // Given
            var rig = CreateRig();
            int raised = 0;
            rig.Grid.RangeSelectionChanged += (s, e) => raised++;

            // When
            rig.Grid.SelectRange(0, 0, 2, 2);
            rig.Grid.ClearRangeSelection();

            // Then
            Assert.That(raised, Is.EqualTo(2));
            rig.Window.Close();
        }

        [Test]
        public void RangeSelectionChanged_IsNotRaised_WhenTheRangesDoNotChange()
        {
            // Given: one block selected
            var rig = CreateRig();
            rig.Grid.SelectRange(0, 0, 2, 2);
            int raised = 0;
            rig.Grid.RangeSelectionChanged += (s, e) => raised++;

            // When: the same block again, Ctrl+A twice, and a single-cell range on the active cell with no ranges
            rig.Grid.SelectRange(0, 0, 2, 2);
            rig.Grid.TryHandleRangeKey(Key.A, ModifierKeys.Control);
            rig.Grid.TryHandleRangeKey(Key.A, ModifierKeys.Control);
            rig.Grid.ClearRangeSelection();
            rig.Grid.SelectRange(0, 0, 0, 0);

            // Then: only Ctrl+A (once) and the clear changed anything
            Assert.That(raised, Is.EqualTo(2));
            rig.Window.Close();
        }

        [Test]
        public void ProgrammaticFocusChange_ClearsTheRanges_SoShiftExtendsFromTheNewActiveCell()
        {
            // Given: a dragged block
            var rig = CreateRig();
            Drag(rig.Grid, 2, 1, 4, 2);

            // When: the view model moves the selection, then Shift+Down
            rig.Grid.SelectedItem = rig.Data[30];
            Flush(rig.Window);
            int activeCol = rig.Grid.FocusedColIndex;
            bool clearedByFocusMove = rig.Grid.SelectedRanges.Count == 0;
            rig.Grid.TryHandleRangeKey(Key.Down, ModifierKeys.Shift);

            // Then
            Assert.That(clearedByFocusMove, Is.True);
            Assert.That(rig.Grid.SelectedRanges, Is.EqualTo(new[] { new CellRange(30, activeCol, 31, activeCol) }));
            rig.Window.Close();
        }

        // ------------------------------------------------------------------ Row mode

        [Test]
        public void RowMode_IgnoresRangeGesturesAndKeys()
        {
            // Given
            var rig = CreateRig(NavigationMode.Row);

            // When
            Drag(rig.Grid, 0, 0, 3, 3);
            rig.Grid.SelectRange(0, 0, 2, 2);
            bool shiftHandled = rig.Grid.TryHandleRangeKey(Key.Down, ModifierKeys.Shift);
            bool ctrlAHandled = rig.Grid.TryHandleRangeKey(Key.A, ModifierKeys.Control);

            // Then
            Assert.That(rig.Grid.SelectedRanges, Is.Empty);
            Assert.That(shiftHandled, Is.False);
            Assert.That(ctrlAHandled, Is.False);
            rig.Window.Close();
        }

        [Test]
        public void RowMode_CtrlC_StillCopiesTheWholeRow()
        {
            // Given
            var rig = CreateRig(NavigationMode.Row);
            rig.Grid.ScrollToItem(1);

            // When
            rig.Grid.TryHandleCopyKey(Key.C, ModifierKeys.Control);

            // Then
            Assert.That(rig.Copied, Is.EqualTo(new[] { "N1\t1\tFalse\tone" }));
            rig.Window.Close();
        }

        // ------------------------------------------------------------------ rendering

        [Test]
        public void CellsInTheRange_AreHighlighted_TheActiveCellKeepsTheFocusBrush()
        {
            // Given
            var rig = CreateRig();

            // When
            Drag(rig.Grid, 1, 0, 2, 1);

            // Then
            var rangeBrush = (SolidColorBrush)rig.Grid._rangeSelection.ResolveRangeBrush();
            var inRange  = VisibleRow(rig.Grid, 2).GetCell(1);
            var outside  = VisibleRow(rig.Grid, 2).GetCell(2);
            var active   = VisibleRow(rig.Grid, 1).GetCell(0);
            Assert.That(inRange.IsInRange, Is.True);
            Assert.That(((SolidColorBrush)inRange.Background).Color, Is.EqualTo(rangeBrush.Color));
            Assert.That(outside.IsInRange, Is.False);
            Assert.That(active.IsFocusedCell, Is.True);
            Assert.That(((SolidColorBrush)active.Background).Color, Is.Not.EqualTo(rangeBrush.Color));
            rig.Window.Close();
        }

        [Test]
        public void ClearingTheRanges_RemovesTheHighlight()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 1, 0, 2, 1);

            // When
            rig.Grid.ClearRangeSelection();

            // Then
            Assert.That(VisibleRow(rig.Grid, 2).GetCell(1).IsInRange, Is.False);
            rig.Window.Close();
        }

        [Test]
        public void ThemeSwitchWithInfoRows_KeepsTheRanges_AndRestylesTheHighlight()
        {
            // Given: info rows under every data row and a selected block
            var rig = CreateRig();
            rig.Grid.InfoRowBindingPath = "Name";
            Flush(rig.Window);
            rig.Grid.SelectRange(1, 0, 2, 1);
            var lightRangeBrush = (SolidColorBrush)rig.Grid._rangeSelection.ResolveRangeBrush();

            // When
            rig.Grid.Theme = GridTheme.Dark;
            Flush(rig.Window);

            // Then
            var darkRangeBrush = (SolidColorBrush)rig.Grid._rangeSelection.ResolveRangeBrush();
            var inRange = VisibleRow(rig.Grid, 2).GetCell(1);
            Assert.That(rig.Grid.SelectedRanges, Is.EqualTo(new[] { new CellRange(1, 0, 2, 1) }));
            Assert.That(inRange.IsInRange, Is.True);
            Assert.That(darkRangeBrush.Color, Is.Not.EqualTo(lightRangeBrush.Color));
            Assert.That(((SolidColorBrush)inRange.Background).Color, Is.EqualTo(darkRangeBrush.Color));
            rig.Window.Close();
        }

        [Test]
        public void RowsScrolledIntoView_GetTheHighlight()
        {
            // Given: a range far below the viewport
            var rig = CreateRig();
            rig.Grid.SelectRange(40, 1, 45, 1);

            // When: scrolling only (the active cell does not move, so the ranges stay)
            rig.Grid._vScrollBar.Value = 40;
            Flush(rig.Window);

            // Then
            Assert.That(VisibleRow(rig.Grid, 43).GetCell(1).IsInRange, Is.True);
            Assert.That(VisibleRow(rig.Grid, 43).GetCell(0).IsInRange, Is.False);
            rig.Window.Close();
        }

        [Test]
        public void AutoRangeBrush_IsLighterThanTheSelection_AndRangeSelectionBrushOverridesIt()
        {
            // Given
            var rig = CreateRig();
            rig.Grid.SelectionBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7));
            var auto = ((SolidColorBrush)rig.Grid._rangeSelection.ResolveRangeBrush()).Color;

            // When
            rig.Grid.RangeSelectionBrush = Brushes.Orange;
            Drag(rig.Grid, 1, 0, 2, 1);

            // Then
            double Lum(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            Assert.That(Lum(auto), Is.GreaterThan(Lum(Color.FromRgb(0x00, 0x78, 0xD7))));
            Assert.That(VisibleRow(rig.Grid, 2).GetCell(1).Background, Is.SameAs(Brushes.Orange));
            rig.Window.Close();
        }

        // ------------------------------------------------------------------ copy

        [Test]
        public void CtrlC_CopiesTheSelectedBlock()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 1, NameCol, 2, QtyCol);

            // When
            rig.Grid.TryHandleCopyKey(Key.C, ModifierKeys.Control);

            // Then
            Assert.That(rig.Copied, Is.EqualTo(new[] { "N1\t1" + NL + "N2\t2" }));
            rig.Window.Close();
        }

        [Test]
        public void CtrlShiftC_AddsTheHeadersOfTheBlockColumns()
        {
            // Given
            var rig = CreateRig();
            Drag(rig.Grid, 1, QtyCol, 2, KindCol);

            // When
            rig.Grid.TryHandleCopyKey(Key.C, ModifierKeys.Control | ModifierKeys.Shift);

            // Then
            Assert.That(rig.Copied, Is.EqualTo(new[] { "Qty\tActive\tKind" + NL + "1\tFalse\tone" + NL + "2\tTrue\t2" }));
            rig.Window.Close();
        }

        [Test]
        public void SeveralRanges_CopyTheirBoundingBox_WithEmptyFieldsForUnselectedCells()
        {
            // Given: (1,Name) and (3,Qty) — the bounding box is rows 1..3 × columns Name..Qty
            var rig = CreateRig();
            rig.Grid.SelectRange(1, NameCol, 1, NameCol);
            rig.Grid.SelectRange(3, QtyCol, 3, QtyCol, add: true);

            // When
            var text = rig.Grid.GetCopyText();

            // Then
            Assert.That(text, Is.EqualTo("N1\t" + NL + "\t" + NL + "\t3"));
            rig.Window.Close();
        }

        [Test]
        public void LargeCopy_IsBuiltInTheBackground_ThenWritten()
        {
            // Given
            var rig = CreateRig();
            rig.Grid.AsyncCopyCellThreshold = 5;
            rig.Grid.SelectRange(0, NameCol, 9, QtyCol);
            var expected = rig.Grid.GetCopyText();

            // When
            bool started = rig.Grid.CopyToClipboard();
            int writtenImmediately = rig.Copied.Count;
            rig.Grid.PendingCopyTask.Wait(3000);
            Flush(rig.Window);

            // Then
            Assert.That(started, Is.True);
            Assert.That(writtenImmediately, Is.EqualTo(0), "written on the UI thread after the background build");
            Assert.That(rig.Copied, Is.EqualTo(new[] { expected }));
            rig.Window.Close();
        }

        [Test]
        public void NewerCopy_SupersedesALargeCopyStillBeingBuilt()
        {
            // Given: a large background copy started
            var rig = CreateRig();
            rig.Grid.AsyncCopyCellThreshold = 5;
            rig.Grid.SelectRange(0, NameCol, 9, QtyCol);
            rig.Grid.CopyToClipboard();
            var first = rig.Grid.PendingCopyTask;

            // When: the user copies a single cell before the first copy is written
            rig.Grid.ClearRangeSelection();
            rig.Grid.CopyToClipboard();
            first.Wait(3000);
            Flush(rig.Window);

            // Then
            Assert.That(rig.Copied, Is.EqualTo(new[] { "N0" }));
            rig.Window.Close();
        }
    }
}
