using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Open.DataGrid.Columns;
using Open.DataGrid.Controls;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Selection
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class CheckedItemsTests
    {
        // ------------------------------------------------------------------ helpers

        private static OpenDataGrid CreateGrid(bool addCheckColumn = true, bool bindCheckColumn = false)
        {
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            if (addCheckColumn)
            {
                var col = new OpenDataGridCheckColumn { Width = 50 };
                if (bindCheckColumn) col.BindingPath = "BoolValue";
                grid.Columns.Add(col);
            }
            return grid;
        }

        private static List<SortItem> MakeData(int count)
        {
            var list = new List<SortItem>(count);
            for (int i = 0; i < count; i++)
                list.Add(new SortItem { Name = $"Item{i}", IntValue = i });
            return list;
        }

        // ------------------------------------------------------------------ ToggleChecked API

        [Test]
        public void ToggleChecked_AddsItemToCheckedItems_WhenNotChecked()
        {
            // Given
            var checkedList = new ObservableCollection<SortItem>();
            var data = MakeData(5);
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When
            grid.ToggleChecked(data[2]);

            // Then
            Assert.That(checkedList, Has.Count.EqualTo(1));
            Assert.That(checkedList[0], Is.SameAs(data[2]));
        }

        [Test]
        public void ToggleChecked_RemovesItemFromCheckedItems_WhenAlreadyChecked()
        {
            // Given
            var data = MakeData(5);
            var checkedList = new ObservableCollection<SortItem> { data[1] };
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When
            grid.ToggleChecked(data[1]);

            // Then
            Assert.That(checkedList, Is.Empty);
        }

        [Test]
        public void ToggleChecked_IsItemChecked_ReflectsState()
        {
            // Given
            var checkedList = new ObservableCollection<SortItem>();
            var data = MakeData(3);
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When
            grid.ToggleChecked(data[0]);

            // Then
            Assert.That(grid.IsItemChecked(data[0]), Is.True);
            Assert.That(grid.IsItemChecked(data[1]), Is.False);
            Assert.That(grid.IsItemChecked(data[2]), Is.False);
        }

        [Test]
        public void ToggleChecked_DoesNothing_WhenCheckedItemsIsNull()
        {
            // Given
            var data = MakeData(3);
            var grid = CreateGrid();
            grid.DataSource = data;
            // CheckedItems is null by default

            // When / Then — should not throw
            Assert.DoesNotThrow(() => grid.ToggleChecked(data[0]));
            Assert.That(grid.IsItemChecked(data[0]), Is.False);
        }

        [Test]
        public void IsItemChecked_ReturnsFalse_WhenItemIsNull()
        {
            // Given
            var grid = CreateGrid();
            grid.CheckedItems = new ObservableCollection<SortItem>();

            // When / Then
            Assert.That(grid.IsItemChecked(null), Is.False);
        }

        // ------------------------------------------------------------------ SelectedItem is independent

        [Test]
        public void SelectedItem_Change_DoesNotAffectCheckedItems()
        {
            // Given
            var checkedList = new ObservableCollection<SortItem>();
            var data = MakeData(5);
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;
            grid.ToggleChecked(data[0]);
            Assert.That(checkedList, Has.Count.EqualTo(1));

            // When
            grid.SelectedItem = data[3];

            // Then
            Assert.That(checkedList, Has.Count.EqualTo(1));
            Assert.That(grid.IsItemChecked(data[0]), Is.True);
        }

        // ------------------------------------------------------------------ VM modifies CheckedItems

        [Test]
        public void VMAddsToCheckedItems_SyncsInternalSet()
        {
            // Given
            var data = MakeData(5);
            var checkedList = new ObservableCollection<SortItem>();
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;
            Assert.That(grid.IsItemChecked(data[2]), Is.False);

            // When — VM adds item
            checkedList.Add(data[2]);

            // Then
            Assert.That(grid.IsItemChecked(data[2]), Is.True);
        }

        [Test]
        public void VMRemovesFromCheckedItems_SyncsInternalSet()
        {
            // Given
            var data = MakeData(5);
            var checkedList = new ObservableCollection<SortItem> { data[1], data[3] };
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;
            Assert.That(grid.IsItemChecked(data[1]), Is.True);

            // When
            checkedList.Remove(data[1]);

            // Then
            Assert.That(grid.IsItemChecked(data[1]), Is.False);
            Assert.That(grid.IsItemChecked(data[3]), Is.True);
        }

        [Test]
        public void VMClearsCheckedItems_SyncsInternalSet()
        {
            // Given
            var data = MakeData(5);
            var checkedList = new ObservableCollection<SortItem> { data[0], data[1], data[2] };
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;
            Assert.That(grid.IsItemChecked(data[0]), Is.True);

            // When
            checkedList.Clear();

            // Then
            Assert.That(grid.IsItemChecked(data[0]), Is.False);
            Assert.That(grid.IsItemChecked(data[1]), Is.False);
        }

        // ------------------------------------------------------------------ CheckedItems DP swap

        [Test]
        public void SwappingCheckedItemsDP_RebuildsInternalSet()
        {
            // Given
            var data = MakeData(5);
            var list1 = new ObservableCollection<SortItem> { data[0] };
            var list2 = new ObservableCollection<SortItem> { data[3], data[4] };
            var grid = CreateGrid();
            grid.CheckedItems = list1;
            grid.DataSource = data;
            Assert.That(grid.IsItemChecked(data[0]), Is.True);

            // When
            grid.CheckedItems = list2;

            // Then
            Assert.That(grid.IsItemChecked(data[0]), Is.False);
            Assert.That(grid.IsItemChecked(data[3]), Is.True);
            Assert.That(grid.IsItemChecked(data[4]), Is.True);
        }

        [Test]
        public void OldCheckedItems_NoLongerTriggers_SyncAfterSwap()
        {
            // Given
            var data = MakeData(5);
            var list1 = new ObservableCollection<SortItem>();
            var list2 = new ObservableCollection<SortItem>();
            var grid = CreateGrid();
            grid.CheckedItems = list1;
            grid.DataSource = data;

            // When — swap, then modify old list
            grid.CheckedItems = list2;
            list1.Add(data[0]);

            // Then — old list change must NOT affect grid state
            Assert.That(grid.IsItemChecked(data[0]), Is.False);
        }

        // ------------------------------------------------------------------ Re-entrancy guard

        [Test]
        public void ToggleChecked_GridHandlerDoesNotReprocess_DuringInternalChange()
        {
            // Given — simulate that the grid's own CollectionChanged handler would cause a loop
            // if _internalCheckedChange guard is missing. The guard must prevent double-processing.
            var data = MakeData(5);
            var checkedList = new ObservableCollection<SortItem>();
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            int handlerCallCount = 0;
            checkedList.CollectionChanged += (s, e) => handlerCallCount++;

            // When — single toggle
            grid.ToggleChecked(data[0]);

            // Then — CollectionChanged fired exactly once (the Add); the grid's internal handler
            // must have been skipped (via _internalCheckedChange guard) so it didn't duplicate work.
            Assert.That(handlerCallCount, Is.EqualTo(1));
            Assert.That(grid.IsItemChecked(data[0]), Is.True);
            Assert.That(checkedList, Has.Count.EqualTo(1));
        }

        // ------------------------------------------------------------------ BindingPath mode

        [Test]
        public void ToggleChecked_WithBindingPath_UpdatesBoolFieldOnItem()
        {
            // Given
            var data = MakeData(3);
            data[1].BoolValue = false;
            var checkedList = new ObservableCollection<SortItem>();
            var grid = CreateGrid(addCheckColumn: true, bindCheckColumn: true);
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When
            grid.ToggleChecked(data[1]);

            // Then — item bool field updated
            Assert.That(data[1].BoolValue, Is.True);
            Assert.That(checkedList, Has.Count.EqualTo(1));
        }

        [Test]
        public void ToggleChecked_WithoutBindingPath_DoesNotTouchItemFields()
        {
            // Given
            var data = MakeData(3);
            data[0].BoolValue = false;
            var checkedList = new ObservableCollection<SortItem>();
            var grid = CreateGrid(addCheckColumn: true, bindCheckColumn: false);
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When
            grid.ToggleChecked(data[0]);

            // Then — BoolValue on item NOT touched
            Assert.That(data[0].BoolValue, Is.False);
            Assert.That(checkedList, Has.Count.EqualTo(1));
        }

        // ------------------------------------------------------------------ sourceColumn isolation (issue 1 regression)

        [Test]
        public void ToggleChecked_UnboundSourceColumn_DoesNotWriteBoundColumnField()
        {
            // Given — grid with both an unbound and a bound CheckColumn
            var data = MakeData(3);
            data[0].BoolValue = false;
            var checkedList = new ObservableCollection<SortItem>();
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            var unboundCol = new OpenDataGridCheckColumn { Width = 50 };                  // no BindingPath
            var boundCol   = new OpenDataGridCheckColumn { BindingPath = "BoolValue", Width = 50 };
            grid.Columns.Add(unboundCol);
            grid.Columns.Add(boundCol);
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When — toggle via the UNBOUND column
            grid.ToggleChecked(data[0], unboundCol);

            // Then — BoolValue must NOT be touched; unbound column state lives in CheckedItems only
            Assert.That(data[0].BoolValue, Is.False);
            Assert.That(checkedList, Has.Count.EqualTo(1));
        }

        [Test]
        public void ToggleChecked_BoundSourceColumn_WritesOnlyThatColumnField()
        {
            // Given — two bound CheckColumns mapping to different bool fields; only the activated
            // one should be written
            var data = MakeData(3);
            data[0].BoolValue        = false;
            data[0].NullableBoolValue = false;
            var checkedList = new ObservableCollection<SortItem>();
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            var colA = new OpenDataGridCheckColumn { BindingPath = "BoolValue",         Width = 50 };
            var colB = new OpenDataGridCheckColumn { BindingPath = "NullableBoolValue",  Width = 50 };
            grid.Columns.Add(colA);
            grid.Columns.Add(colB);
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When — toggle via colA only
            grid.ToggleChecked(data[0], colA);

            // Then — colA's field updated, colB's field unchanged
            Assert.That(data[0].BoolValue,         Is.True);
            Assert.That(data[0].NullableBoolValue,  Is.EqualTo((bool?)false));
            Assert.That(checkedList, Has.Count.EqualTo(1));
        }

        [Test]
        public void ToggleChecked_NullSourceColumn_WritesAllBoundColumnFields()
        {
            // Given — row-level keyboard toggle (sourceColumn == null) must write all bound columns
            var data = MakeData(3);
            data[0].BoolValue        = false;
            data[0].NullableBoolValue = false;
            var checkedList = new ObservableCollection<SortItem>();
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.Columns.Add(new OpenDataGridCheckColumn { BindingPath = "BoolValue",        Width = 50 });
            grid.Columns.Add(new OpenDataGridCheckColumn { BindingPath = "NullableBoolValue", Width = 50 });
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When — no sourceColumn = row-level toggle (keyboard)
            grid.ToggleChecked(data[0], sourceColumn: null);

            // Then — both bound fields updated
            Assert.That(data[0].BoolValue,        Is.True);
            Assert.That(data[0].NullableBoolValue, Is.EqualTo((bool?)true));
            Assert.That(checkedList, Has.Count.EqualTo(1));
        }

        // ------------------------------------------------------------------ Hidden column sync (issue: GetVisibleColumnList in SyncCheckedStateToBindingPath)

        [Test]
        public void ToggleChecked_HiddenBoundCheckColumn_StillWritesBindingPath()
        {
            // Given — bound CheckColumn hidden from view
            var data = MakeData(3);
            data[0].BoolValue = false;
            var checkedList = new ObservableCollection<SortItem>();
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            var boundCol = new OpenDataGridCheckColumn { BindingPath = "BoolValue", Width = 50 };
            boundCol.IsVisible = false; // hidden before DataSource is bound
            grid.Columns.Add(boundCol);
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When
            grid.ToggleChecked(data[0]);

            // Then — bool field must be written regardless of column visibility
            Assert.That(data[0].BoolValue, Is.True);
            Assert.That(checkedList, Has.Count.EqualTo(1));
        }

        // ------------------------------------------------------------------ DataSource replacement (issue 3)

        [Test]
        public void DataSourceReplacement_PrunesCheckedSetToNewItems()
        {
            // Given
            var data1 = MakeData(5);
            var data2 = MakeData(5); // completely new objects
            var checkedList = new ObservableCollection<SortItem> { data1[0], data1[1] };
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data1;
            Assert.That(grid.IsItemChecked(data1[0]), Is.True);

            // When — DataSource replaced with entirely new data
            grid.DataSource = data2;

            // Then — internal set no longer contains stale references from data1
            Assert.That(grid.IsItemChecked(data1[0]), Is.False);
            Assert.That(grid.IsItemChecked(data1[1]), Is.False);
            // CheckedItems is still the VM's collection — not touched by the grid
            Assert.That(checkedList, Has.Count.EqualTo(2));
        }

        // ------------------------------------------------------------------ MarkedItems

        [Test]
        public void VMAddsForeingnItem_DoesNotAppearInMarkedItems()
        {
            // Given
            var data = MakeData(5);
            var foreignItem = new SortItem { Name = "Foreign" }; // not in DataSource
            var checkedList = new ObservableCollection<SortItem>();
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When — VM adds a foreign object to CheckedItems directly
            checkedList.Add(foreignItem);

            // Then — foreign item must not appear in MarkedItems (not in DataSource)
            var marked = grid.MarkedItems;
            Assert.That(marked, Has.None.SameAs(foreignItem));
            // Also must not be considered checked
            Assert.That(grid.IsItemChecked(foreignItem), Is.False);
        }

        [Test]
        public void EmptyDataSource_ClearsSelectedItem()
        {
            // Given — grid with data and a selected item
            var data = MakeData(5);
            var grid = CreateGrid();
            grid.DataSource = data;
            grid.SelectedItem = data[2];
            Assert.That(grid.SelectedItem, Is.Not.Null);

            // When — DataSource set to empty list
            grid.DataSource = new List<SortItem>();

            // Then — SelectedItem cleared (no rows to select)
            Assert.That(grid.SelectedItem, Is.Null);
        }

        [Test]
        public void MarkedItems_StaleSelectedItem_ExcludedAfterDataSourceSwap()
        {
            // Given — item selected in data1, then DataSource replaced with data2
            var data1 = MakeData(5);
            var data2 = MakeData(5); // new objects
            var checkedList = new ObservableCollection<SortItem>();
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data1;
            grid.SelectedItem = data1[3];

            // When — DataSource replaced; SelectedItem still holds old reference
            grid.DataSource = data2;

            // Then — stale SelectedItem must NOT appear in MarkedItems
            var marked = grid.MarkedItems;
            Assert.That(marked, Has.None.SameAs(data1[3]));
        }

        [Test]
        public void MarkedItems_AfterDataSourceSwap_DoesNotReturnStaleItems()
        {
            // Given
            var data1 = MakeData(5);
            var data2 = MakeData(5); // completely new objects
            var checkedList = new ObservableCollection<SortItem> { data1[0], data1[1], data1[2] };
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data1;

            // When — DataSource replaced; checkedList still holds old references
            grid.DataSource = data2;

            // Then — MarkedItems must not contain objects from data1
            var marked = grid.MarkedItems;
            Assert.That(marked, Has.None.SameAs(data1[0]));
            Assert.That(marked, Has.None.SameAs(data1[1]));
            Assert.That(marked, Has.None.SameAs(data1[2]));
        }

        [Test]
        public void MarkedItems_OnlySelected_WhenNothingChecked()
        {
            // Given
            var checkedList = new ObservableCollection<SortItem>();
            var data = MakeData(5);
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;
            grid.SelectedItem = data[2];

            // When / Then
            var marked = grid.MarkedItems;
            Assert.That(marked, Has.Count.EqualTo(1));
            Assert.That(marked[0], Is.SameAs(data[2]));
        }

        [Test]
        public void MarkedItems_DoesNotDuplicateSelectedWhenAlsoChecked()
        {
            // Given
            var checkedList = new ObservableCollection<SortItem>();
            var data = MakeData(5);
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;
            grid.ToggleChecked(data[1]);
            grid.SelectedItem = data[1]; // same item as checked

            // When / Then — data[1] appears exactly once
            var marked = grid.MarkedItems;
            Assert.That(marked, Has.Count.EqualTo(1));
            Assert.That(marked[0], Is.SameAs(data[1]));
        }

        [Test]
        public void MarkedItems_PreservesCheckedItemsOrder_AndAppendsSelectedLast()
        {
            // Given
            var data = MakeData(5);
            var checkedList = new ObservableCollection<SortItem> { data[3], data[1], data[0] };
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;
            grid.SelectedItem = data[4]; // selected, not checked

            // When
            var marked = grid.MarkedItems;

            // Then — order from CheckedItems preserved; selected appended
            Assert.That(marked, Has.Count.EqualTo(4));
            Assert.That(marked[0], Is.SameAs(data[3]));
            Assert.That(marked[1], Is.SameAs(data[1]));
            Assert.That(marked[2], Is.SameAs(data[0]));
            Assert.That(marked[3], Is.SameAs(data[4]));
        }

        // ------------------------------------------------------------------ MarkedItems distinct contract

        [Test]
        public void MarkedItems_DuplicatesInCheckedItems_AreDeduplicated()
        {
            // Given: VM accidentally adds the same item twice to CheckedItems.
            var data = MakeData(3);
            var checkedList = new ObservableCollection<SortItem> { data[0], data[1], data[0] };
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When
            var marked = grid.MarkedItems;

            // Then: result honors the "distinct union" contract — data[0] appears once.
            Assert.That(marked, Has.Count.EqualTo(2));
            Assert.That(marked[0], Is.SameAs(data[0]));
            Assert.That(marked[1], Is.SameAs(data[1]));
        }

        // ------------------------------------------------------------------ CheckedItems collection replacement (OnCheckedItemsChanged filter)

        [Test]
        public void ReplaceCheckedItemsCollection_WithForeignItems_FiltersOutNonDataSourceMembers()
        {
            // Given: grid with DataSource [a, b, c] and an initial CheckedItems with [a].
            var data = MakeData(3);
            var foreign = new SortItem { Name = "foreign" };
            var initial = new ObservableCollection<SortItem> { data[0] };
            var grid = CreateGrid();
            grid.CheckedItems = initial;
            grid.DataSource = data;

            // When: VM replaces the whole CheckedItems collection — new list contains a valid item
            //       and a foreign item that is not in DataSource.
            var replacement = new ObservableCollection<SortItem> { data[0], foreign };
            grid.CheckedItems = replacement;

            // Then: only data[0] enters _checkedSet; foreign is rejected.
            Assert.That(grid.IsItemChecked(data[0]), Is.True,  "data[0] is in DataSource — must be checked");
            Assert.That(grid.IsItemChecked(foreign),  Is.False, "foreign is not in DataSource — must be rejected");

            var marked = grid.MarkedItems;
            Assert.That(marked, Has.None.SameAs(foreign), "MarkedItems must not contain foreign item");
            Assert.That(marked, Has.Some.SameAs(data[0]),  "MarkedItems must contain data[0]");
        }

        // ------------------------------------------------------------------ Two sources of truth (documented behavior)

        [Test]
        public void BoundCheckColumn_ModelBoolTrue_ButNotInCheckedItems_IsNotMarked()
        {
            // Given — bound mode: model BoolValue=true on data[1], but CheckedItems is empty.
            // This documents the "two sources of truth" contract: the CheckBox visual reads from
            // BindingPath, while MarkedItems / IsItemChecked read from CheckedItems. They are NOT
            // auto-synchronized — the grid only syncs them through ToggleChecked.
            var data = MakeData(5);
            data[1].BoolValue = true;
            var checkedList = new ObservableCollection<SortItem>();
            var grid = CreateGrid(addCheckColumn: true, bindCheckColumn: true);
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When / Then — model bool true alone does NOT register the item as checked
            Assert.That(grid.IsItemChecked(data[1]), Is.False);
            Assert.That(grid.MarkedItems, Has.None.SameAs(data[1]));
        }

        [Test]
        public void BoundCheckColumn_VMMutatesModelBoolDirectly_DoesNotSyncCheckedItems()
        {
            // Given — bound mode, item initially unchecked
            var data = MakeData(5);
            data[2].BoolValue = false;
            var checkedList = new ObservableCollection<SortItem>();
            var grid = CreateGrid(addCheckColumn: true, bindCheckColumn: true);
            grid.CheckedItems = checkedList;
            grid.DataSource = data;

            // When — VM flips the model property directly (bypassing ToggleChecked)
            data[2].BoolValue = true;

            // Then — grid's checked-state set is NOT auto-updated. This is documented behavior:
            // VM is responsible for keeping data.BoolValue and CheckedItems in sync.
            Assert.That(grid.IsItemChecked(data[2]), Is.False);
            Assert.That(checkedList, Is.Empty);
        }

        // ------------------------------------------------------------------ Pure-bound mode (CheckedItems == null)

        [Test]
        public void ToggleChecked_BoundMode_WithoutCheckedItems_TogglesModelField()
        {
            // Given — bound CheckColumn, no CheckedItems
            var data = MakeData(3);
            data[1].BoolValue = false;
            var col = new OpenDataGridCheckColumn { BindingPath = "BoolValue", Width = 50 };
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.Columns.Add(col);
            grid.DataSource = data; // CheckedItems stays null

            // When — toggle false→true
            grid.ToggleChecked(data[1], col);

            // Then
            Assert.That(data[1].BoolValue, Is.True);

            // When — toggle true→false
            grid.ToggleChecked(data[1], col);

            // Then
            Assert.That(data[1].BoolValue, Is.False);
        }

        [Test]
        public void ToggleChecked_BoundMode_WithoutCheckedItems_NullSourceColumn_WritesAllBoundFields()
        {
            // Given — two bound CheckColumns, no CheckedItems; keyboard toggle (sourceColumn=null)
            // must write all bound columns (consistent with ToggleChecked_NullSourceColumn_WritesAllBoundColumnFields)
            var data = MakeData(3);
            data[0].BoolValue         = false;
            data[0].NullableBoolValue = false;
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.Columns.Add(new OpenDataGridCheckColumn { BindingPath = "BoolValue",         Width = 50 });
            grid.Columns.Add(new OpenDataGridCheckColumn { BindingPath = "NullableBoolValue",  Width = 50 });
            grid.DataSource = data;

            // When
            grid.ToggleChecked(data[0], sourceColumn: null);

            // Then — both bound fields updated
            Assert.That(data[0].BoolValue,         Is.True);
            Assert.That(data[0].NullableBoolValue,  Is.EqualTo((bool?)true));
        }

        [Test]
        public void ToggleChecked_BoundMode_WithoutCheckedItems_UnboundSourceColumn_IsNoOp()
        {
            // Given — unbound sourceColumn passed but CheckedItems==null → no writes, no crash
            var data = MakeData(3);
            data[0].BoolValue = false;
            var unboundCol = new OpenDataGridCheckColumn { Width = 50 }; // no BindingPath
            var grid = new OpenDataGrid();
            grid.Columns.Add(new OpenDataGridLabelColumn { BindingPath = "Name", Width = 200 });
            grid.Columns.Add(unboundCol);
            grid.DataSource = data;

            // When
            grid.ToggleChecked(data[0], unboundCol);

            // Then — no-op; field unchanged
            Assert.That(data[0].BoolValue, Is.False);
        }

        [Test]
        public void ToggleChecked_NoCheckedItems_NoBoundColumn_IsNoOp()
        {
            // Given — CheckedItems==null AND no bound CheckColumn → no-op (regression guard)
            var data = MakeData(3);
            data[0].BoolValue = false;
            var grid = CreateGrid(addCheckColumn: true, bindCheckColumn: false); // unbound CheckColumn
            grid.DataSource = data; // CheckedItems stays null

            // When
            grid.ToggleChecked(data[0]);

            // Then — no-op; CheckedItems still null, field unchanged
            Assert.That(grid.CheckedItems, Is.Null);
            Assert.That(data[0].BoolValue, Is.False);
        }

        // ------------------------------------------------------------------ Duplicate removal regression

        [Test]
        public void ToggleChecked_RemovesAllDuplicates_FromCheckedItems()
        {
            // Given — VM has added the same item to CheckedItems twice (duplicate state).
            // A single Remove() call would leave one occurrence behind, breaking the invariant
            // that _checkedSet (says unchecked) ↔ CheckedItems (still contains item).
            var data = MakeData(5);
            var checkedList = new ObservableCollection<SortItem> { data[1], data[1] };
            var grid = CreateGrid();
            grid.CheckedItems = checkedList;
            grid.DataSource = data;
            Assert.That(grid.IsItemChecked(data[1]), Is.True);

            // When — toggle off
            grid.ToggleChecked(data[1]);

            // Then — every occurrence removed; invariant restored
            Assert.That(checkedList, Is.Empty);
            Assert.That(grid.IsItemChecked(data[1]), Is.False);
            Assert.That(grid.MarkedItems, Has.None.SameAs(data[1]));
        }
    }
}
