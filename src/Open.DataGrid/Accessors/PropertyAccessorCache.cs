using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Windows.Media;
using Fasterflect;

namespace Open.DataGrid.Accessors
{
    /// <summary>
    /// Shared cache for fast property getters/setters built from Fasterflect delegates.
    /// Provides wrappers used by grid and filtering controls.
    /// </summary>
    internal static class PropertyAccessorCache
    {
        private struct AccessorKey : IEquatable<AccessorKey>
        {
            public readonly Type Type;
            public readonly string PropertyName;

            public AccessorKey(Type type, string propertyName)
            {
                Type = type;
                PropertyName = propertyName;
            }

            public bool Equals(AccessorKey other)
                => Type == other.Type && string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal);

            public override bool Equals(object obj)
                => obj is AccessorKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Type != null ? Type.GetHashCode() : 0) * 397) ^ (PropertyName != null ? PropertyName.GetHashCode() : 0);
                }
            }
        }

        private static readonly ConcurrentDictionary<AccessorKey, MemberGetter> GetterCache
            = new ConcurrentDictionary<AccessorKey, MemberGetter>();

        private static readonly ConcurrentDictionary<AccessorKey, MemberSetter> SetterCache
            = new ConcurrentDictionary<AccessorKey, MemberSetter>();

        /// <summary>
        /// Builds a getter for a known type. Returns false when property does not exist.
        /// Used for filter controls where the type is known up-front.
        /// Supports both flat property names and nested dot-notation paths.
        /// </summary>
        internal static bool TryCreateGetter(Type type, string propertyName, out Func<object, object> getter)
        {
            getter = null;
            if (type == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            var segments = SplitPropertyPath(propertyName);
            if (segments.Length > 1)
            {
                MemberGetter[] chain;
                Type finalType;
                if (!TryBuildGetterChain(type, segments, out chain, out finalType)) return false;

                getter = item => InvokeGetterChain(item, chain);
                return true;
            }

            MemberGetter memberGetter;
            if (!TryGetMemberGetter(type, propertyName, out memberGetter)) return false;

            getter = item => item == null ? null : memberGetter(item);
            return true;
        }

        /// <summary>
        /// Builds a string-typed getter. Returns false when property does not exist or is not string.
        /// Used for filter controls where the type is known up-front.
        /// Supports both flat property names and nested dot-notation paths.
        /// </summary>
        internal static bool TryCreateStringGetter(Type type, string propertyName, out Func<object, string> getter)
        {
            getter = null;
            if (type == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            var segments = SplitPropertyPath(propertyName);
            if (segments.Length > 1)
            {
                MemberGetter[] chain;
                Type finalType;
                if (!TryBuildGetterChain(type, segments, out chain, out finalType) || finalType != typeof(string))
                    return false;

                getter = item => InvokeGetterChain(item, chain) as string;
                return true;
            }

            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.PropertyType != typeof(string)) return false;

            MemberGetter memberGetter;
            if (!TryGetMemberGetter(type, propertyName, out memberGetter)) return false;

            getter = item => item == null ? null : memberGetter(item) as string;
            return true;
        }

        /// <summary>
        /// Returns a polymorphic getter with a 1-entry type cache.
        /// Works across heterogeneous collections: rebuilds the cached delegate when the item type changes.
        /// Used in the scroll hot path — flat paths stay on the original single-delegate fast path.
        /// Nested paths are resolved through a cached chain of Fasterflect delegates.
        /// </summary>
        internal static Func<object, object> CreateGetter(string propertyName)
        {
            var segments = SplitPropertyPath(propertyName);
            if (segments.Length > 1)
            {
                Type cachedNestedType = null;
                MemberGetter[] cachedChain = null;
                bool hasNestedGetter = false;

                return item =>
                {
                    if (item == null) return null;

                    var type = item.GetType();
                    if (!ReferenceEquals(type, cachedNestedType))
                    {
                        cachedNestedType = type;
                        hasNestedGetter = TryBuildGetterChain(type, segments, out cachedChain, out _);
                    }

                    return hasNestedGetter ? InvokeGetterChain(item, cachedChain) : null;
                };
            }

            Type cachedType = null;
            MemberGetter cachedGetter = null;
            bool hasGetter = false;

            return item =>
            {
                if (item == null) return null;

                var type = item.GetType();
                if (!ReferenceEquals(type, cachedType))
                {
                    cachedType = type;
                    hasGetter = TryGetMemberGetter(type, propertyName, out cachedGetter);
                }

                return hasGetter ? cachedGetter(item) : null;
            };
        }

        internal static Func<object, Brush> CreateBrushGetter(string propertyName)
        {
            var getter = CreateGetter(propertyName);
            return item => getter(item) as Brush;
        }

        internal static Func<object, bool> CreateBoolGetter(string propertyName)
        {
            var getter = CreateGetter(propertyName);
            return item => getter(item) is bool b && b;
        }

        /// <summary>
        /// Returns a polymorphic setter with a 1-entry type cache.
        /// Used in the edit hot path — no overhead beyond the Fasterflect IL delegate call.
        /// </summary>
        internal static Action<object, object> CreateSetter(string propertyName)
        {
            var segments = SplitPropertyPath(propertyName);
            if (segments.Length > 1)
            {
                Type cachedNestedType = null;
                NestedSetterChain cachedNestedChain = null;
                bool hasNestedSetter = false;

                return (item, value) =>
                {
                    if (item == null) return;

                    var type = item.GetType();
                    if (!ReferenceEquals(type, cachedNestedType))
                    {
                        cachedNestedType = type;
                        hasNestedSetter = TryBuildSetterChain(type, segments, out cachedNestedChain);
                    }

                    if (!hasNestedSetter) return;

                    InvokeNestedSetter(item, value, cachedNestedChain);
                };
            }

            Type cachedType = null;
            Action<object, object> cachedSetter = null;
            bool hasSetter = false;

            return (item, value) =>
            {
                if (item == null) return;

                var type = item.GetType();
                if (!ReferenceEquals(type, cachedType))
                {
                    cachedType = type;
                    hasSetter = TryGetPropertySetter(type, propertyName, out cachedSetter);
                }

                if (hasSetter) cachedSetter(item, value);
            };
        }

        internal static object GetValue(object item, string propertyName)
        {
            if (item == null || string.IsNullOrEmpty(propertyName)) return null;
            var dot = propertyName.IndexOf('.');
            if (dot >= 0)
            {
                var first = propertyName.Substring(0, dot);
                var rest  = propertyName.Substring(dot + 1);
                return GetValue(GetValue(item, first), rest);
            }
            Func<object, object> getter;
            return TryCreateGetter(item.GetType(), propertyName, out getter) ? getter(item) : null;
        }

        private static string[] SplitPropertyPath(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName)) return new string[0];

            var parts = propertyName.Split('.');
            for (int i = 0; i < parts.Length; i++)
                parts[i] = parts[i].Trim();
            return parts;
        }

        private sealed class NestedSetterChain
        {
            public MemberGetter[] OwnerChain;
            public Action<object, object>[] OwnerSetters;
            public bool[] OwnerValueTypes;
            public Action<object, object> LeafSetter;
        }

        private static object InvokeGetterChain(object item, MemberGetter[] chain)
        {
            if (item == null || chain == null) return null;

            object current = item;
            for (int i = 0; i < chain.Length; i++)
            {
                if (current == null) return null;
                current = chain[i](current);
            }

            return current;
        }

        private static void InvokeNestedSetter(object item, object value, NestedSetterChain chain)
        {
            if (item == null || chain == null || chain.LeafSetter == null) return;

            var owners = new object[chain.OwnerChain.Length + 1];
            owners[0] = item;

            object current = item;
            for (int i = 0; i < chain.OwnerChain.Length; i++)
            {
                current = chain.OwnerChain[i](current);
                if (current == null) return;
                owners[i + 1] = current;
            }

            chain.LeafSetter(owners[owners.Length - 1], value);

            for (int i = chain.OwnerValueTypes.Length - 1; i >= 0; i--)
            {
                if (!chain.OwnerValueTypes[i]) continue;

                var setter = chain.OwnerSetters[i];
                var parent = owners[i];
                if (setter == null || parent == null) return;

                setter(parent, owners[i + 1]);
            }
        }

        private static bool TryBuildGetterChain(Type rootType, string[] segments, out MemberGetter[] chain, out Type finalType)
        {
            chain = null;
            finalType = null;
            if (rootType == null || segments == null || segments.Length == 0) return false;

            var getters = new MemberGetter[segments.Length];
            var currentType = rootType;
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrWhiteSpace(segment)) return false;

                var property = currentType.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
                if (property == null) return false;

                MemberGetter getter;
                if (!TryGetMemberGetter(currentType, segment, out getter)) return false;

                getters[i] = getter;
                currentType = property.PropertyType;
            }

            chain = getters;
            finalType = currentType;
            return true;
        }

        private static bool TryBuildSetterChain(Type rootType, string[] segments, out NestedSetterChain chain)
        {
            chain = null;
            if (rootType == null || segments == null || segments.Length < 2) return false;

            var ownerChain = new MemberGetter[segments.Length - 1];
            var ownerSetters = new Action<object, object>[segments.Length - 1];
            var ownerValueTypes = new bool[segments.Length - 1];
            var currentType = rootType;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrWhiteSpace(segment)) return false;

                var property = currentType.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
                if (property == null) return false;

                MemberGetter getter;
                if (!TryGetMemberGetter(currentType, segment, out getter)) return false;

                ownerChain[i] = getter;
                ownerValueTypes[i] = property.PropertyType.IsValueType;
                if (ownerValueTypes[i] && !TryGetPropertySetter(currentType, segment, out ownerSetters[i]))
                    return false;

                currentType = property.PropertyType;
            }

            Action<object, object> leafSetter;
            if (!TryGetPropertySetter(currentType, segments[segments.Length - 1], out leafSetter))
                return false;

            chain = new NestedSetterChain
            {
                OwnerChain = ownerChain,
                OwnerSetters = ownerSetters,
                OwnerValueTypes = ownerValueTypes,
                LeafSetter = leafSetter,
            };

            return true;
        }

        private static bool TryGetPropertySetter(Type type, string propertyName, out Action<object, object> setter)
        {
            setter = null;
            if (type == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            if (type.IsValueType)
            {
                var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite) return false;

                setter = (owner, value) => property.SetValue(owner, value, null);
                return true;
            }

            MemberSetter memberSetter;
            if (!TryGetMemberSetter(type, propertyName, out memberSetter)) return false;

            setter = (owner, value) => memberSetter(owner, value);
            return true;
        }

        private static bool TryBuildGetterChain(Type rootType, string[] segments, int count, out MemberGetter[] chain, out Type finalType)
        {
            chain = null;
            finalType = null;
            if (rootType == null || segments == null || count <= 0 || count > segments.Length) return false;

            var getters = new MemberGetter[count];
            var currentType = rootType;
            for (int i = 0; i < count; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrWhiteSpace(segment)) return false;

                var property = currentType.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
                if (property == null) return false;

                MemberGetter getter;
                if (!TryGetMemberGetter(currentType, segment, out getter)) return false;

                getters[i] = getter;
                currentType = property.PropertyType;
            }

            chain = getters;
            finalType = currentType;
            return true;
        }

        private static bool TryGetMemberGetter(Type type, string propertyName, out MemberGetter getter)
        {
            getter = null;
            if (type == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            var key = new AccessorKey(type, propertyName);
            try
            {
                getter = GetterCache.GetOrAdd(key, k => k.Type.DelegateForGetPropertyValue(k.PropertyName));
                return getter != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetMemberSetter(Type type, string propertyName, out MemberSetter setter)
        {
            setter = null;
            if (type == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            var key = new AccessorKey(type, propertyName);
            try
            {
                setter = SetterCache.GetOrAdd(key, k => k.Type.DelegateForSetPropertyValue(k.PropertyName));
                return setter != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
