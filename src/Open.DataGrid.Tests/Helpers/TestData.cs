using System;
using System.Collections.Generic;
using Open.DataGrid.Columns;

namespace Open.DataGrid.Tests.Helpers
{
    /// <summary>Simple POCO used as a row data item in sorting / navigation tests.</summary>
    public class SortItem
    {
        public string Name { get; set; }
        public int    IntValue { get; set; }
        public decimal DecimalValue { get; set; }
        public bool BoolValue { get; set; }
        public bool? NullableBoolValue { get; set; }
        public DateTime DateTimeValue { get; set; }
        public DateTime? NullableDateTimeValue { get; set; }
        public TimeSpan TimeSpanValue { get; set; }
        public TimeSpan? NullableTimeSpanValue { get; set; }
    }

    /// <summary>Factory helpers for creating test column lists without a real Grid.</summary>
    public static class TestColumns
    {
        /// <summary>Creates LabelColumns with the specified widths (STA required).</summary>
        public static IReadOnlyList<OpenDataGridColumn> WithWidths(params double[] widths)
        {
            var list = new List<OpenDataGridColumn>(widths.Length);
            foreach (var w in widths)
                list.Add(new OpenDataGridLabelColumn { Width = w });
            return list;
        }
    }
}
