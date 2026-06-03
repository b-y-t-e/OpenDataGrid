using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Open.DataGrid
{
    internal sealed class ObjectReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ObjectReferenceEqualityComparer Default = new ObjectReferenceEqualityComparer();

        bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);
        int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
