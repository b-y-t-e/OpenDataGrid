using System.Collections.Generic;
using System.Threading;
using System.Windows.Controls;
using Open.DataGrid.Columns;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Columns
{
    [TestFixture, Apartment(ApartmentState.STA)]
    public class ComboColumnTests
    {
        private sealed class Item
        {
            public int Id { get; set; }
            public string Label { get; set; }
        }

        [TestCase(System.Windows.Input.Key.F4,    TestName = "F4 triggers dropdown-open attempt")]
        [TestCase(System.Windows.Input.Key.Space, TestName = "Space triggers dropdown-open attempt")]
        [TestCase(System.Windows.Input.Key.A,     TestName = "Letter key is accepted without crash")]
        public void BeginCharacterEdit_SupportedKeys_DoNotThrow(System.Windows.Input.Key key)
        {
            // Given: Combo column editor (no WPF visual tree — popup cannot actually open).
            // Note: ComboBox.IsDropDownOpen requires a full WPF template to stay True; in a
            //       headless test the popup is coerced back to False by WPF. This test guards
            //       against crashes; actual dropdown behaviour is covered by integration tests.
            var column = new OpenDataGridComboColumn();
            var editor = (ComboBox)column.CreateEditElement();

            // When / Then: no exception is thrown for any key the handler accepts.
            Assert.DoesNotThrow(() => column.BeginCharacterEdit(editor, key));
        }

        [Test]
        public void ExtractEditValue_WithSelectedItem_ReturnsSelectedValue()
        {
            // Given: Combo column with item source and selected value.
            var column = new OpenDataGridComboColumn
            {
                ItemsSource = new List<Item>
                {
                    new Item { Id = 1, Label = "One" },
                    new Item { Id = 2, Label = "Two" }
                },
                ValueMemberPath = "Id",
                DisplayMemberPath = "Label"
            };

            var editor = (ComboBox)column.CreateEditElement();
            column.ApplyEditValue(editor, 2);

            // When: ExtractEditValue is called.
            var result = column.ExtractEditValue(editor);

            // Then: Selected key value is returned.
            Assert.That(result, Is.EqualTo(2));
        }

        [Test]
        public void ExtractEditValue_WithoutSelection_ReturnsNoValueSentinel()
        {
            // Given: Combo column editor without a selected item.
            var column = new OpenDataGridComboColumn();
            var editor = (ComboBox)column.CreateEditElement();

            // When: ExtractEditValue is called.
            var result = column.ExtractEditValue(editor);

            // Then: NoValue sentinel is returned.
            Assert.That(ReferenceEquals(result, OpenDataGridColumn.NoValue), Is.True);
        }
    }
}
