using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using Open.DataGrid.Columns;
using Open.DataGrid.Tests.Helpers;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Columns
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class BooleanColumnTests
    {
        private OpenDataGridBooleanColumn _column;

        [SetUp]
        public void SetUp()
        {
            _column = new OpenDataGridBooleanColumn
            {
                BindingPath = "BoolValue",
                IsThreeState = false
            };
        }

        [Test]
        [TestCase(System.Windows.Input.Key.Space,    ExpectedResult = true,  TestName = "Space triggers edit")]
        [TestCase(System.Windows.Input.Key.F2,       ExpectedResult = true,  TestName = "F2 triggers edit")]
        [TestCase(System.Windows.Input.Key.Enter,    ExpectedResult = true,  TestName = "Enter triggers edit")]
        [TestCase(System.Windows.Input.Key.D0,       ExpectedResult = true,  TestName = "Digit 0 triggers edit")]
        [TestCase(System.Windows.Input.Key.D1,       ExpectedResult = true,  TestName = "Digit 1 triggers edit")]
        [TestCase(System.Windows.Input.Key.NumPad0,  ExpectedResult = true,  TestName = "NumPad 0 triggers edit")]
        [TestCase(System.Windows.Input.Key.NumPad1,  ExpectedResult = true,  TestName = "NumPad 1 triggers edit")]
        [TestCase(System.Windows.Input.Key.A,        ExpectedResult = false, TestName = "Letter A does not trigger edit")]
        [TestCase(System.Windows.Input.Key.OemMinus, ExpectedResult = false, TestName = "Minus does not trigger edit")]
        public bool CanBeginEdit_Key_ReturnsExpected(System.Windows.Input.Key key)
        {
            // Given: A boolean column.
            // When: CanBeginEdit is called with a keyboard key.
            // Then: It returns whether this key should enter edit mode.
            return _column.CanBeginEdit(key);
        }

        [Test]
        public void ExtractEditValue_TwoState_ReturnsBoolean()
        {
            // Given: A two-state boolean column and checked checkbox editor.
            _column.IsThreeState = false;
            var editor = new CheckBox { IsThreeState = false, IsChecked = true };

            // When: The edited value is extracted.
            var result = _column.ExtractEditValue(editor);

            // Then: A non-null bool value is returned.
            Assert.That(result, Is.EqualTo(true));
            Assert.That(result, Is.TypeOf<bool>());
        }

        [Test]
        public void ExtractEditValue_ThreeState_ReturnsNullForIndeterminate()
        {
            // Given: A three-state boolean column and indeterminate editor value.
            _column.IsThreeState = true;
            var editor = new CheckBox { IsThreeState = true, IsChecked = null };

            // When: The edited value is extracted.
            var result = _column.ExtractEditValue(editor);

            // Then: Null is returned (nullable bool support).
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetComparison_SortsNullableBooleanInExpectedOrder()
        {
            // Given: Items containing null/false/true values for nullable bool.
            _column.BindingPath = "NullableBoolValue";
            _column.IsThreeState = true;
            var items = new List<SortItem>
            {
                new SortItem { NullableBoolValue = true },
                new SortItem { NullableBoolValue = null },
                new SortItem { NullableBoolValue = false }
            };
            var cmp = _column.GetComparison();

            // When: Items are sorted with column comparison.
            var sorted = items.OrderBy(x => (object)x, Comparer<object>.Create((a, b) => cmp(a, b)))
                .Cast<SortItem>()
                .ToList();

            // Then: Order is null, false, true.
            Assert.That(sorted[0].NullableBoolValue, Is.Null);
            Assert.That(sorted[1].NullableBoolValue, Is.False);
            Assert.That(sorted[2].NullableBoolValue, Is.True);
        }

        [Test]
        public void BeginCharacterEdit_SpaceCyclesThreeState()
        {
            // Given: Three-state boolean column and editor with false value.
            _column.IsThreeState = true;
            var editor = new CheckBox { IsThreeState = true, IsChecked = false };

            // When: Space is used for character edit three times.
            _column.BeginCharacterEdit(editor, System.Windows.Input.Key.Space);
            var first = editor.IsChecked;
            _column.BeginCharacterEdit(editor, System.Windows.Input.Key.Space);
            var second = editor.IsChecked;
            _column.BeginCharacterEdit(editor, System.Windows.Input.Key.Space);
            var third = editor.IsChecked;

            // Then: Value cycles false -> true -> null -> false.
            Assert.That(first, Is.True);
            Assert.That(second, Is.Null);
            Assert.That(third, Is.False);
        }
    }
}
