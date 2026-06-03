using System;
using Open.DataGrid.Accessors;
using NUnit.Framework;

namespace Open.DataGrid.Tests.Accessors
{
    [TestFixture]
    public class PropertyAccessorCacheTests
    {
        private class Person
        {
            public string Name { get; set; }
            public int Age { get; set; }
        }

        private class OtherPerson
        {
            public string Name { get; set; }
        }

        private class Product
        {
            public ProductInfo Towar { get; set; }
        }

        private class ProductInfo
        {
            public string Symbol { get; set; }
            public int Quantity { get; set; }
        }

        private class OtherProduct
        {
            public OtherProductInfo Towar { get; set; }
        }

        private class OtherProductInfo
        {
            public string Symbol { get; set; }
        }

        private class ProductWithStructDetails
        {
            public ProductDetails Details { get; set; }
        }

        private class ProductDetails
        {
            public QuantityInfo QuantityInfo { get; set; }
        }

        private struct QuantityInfo
        {
            public int Quantity { get; set; }
        }

        [Test]
        public void TryCreateGetter_ValidProperty_ReturnsValue()
        {
            // Given: a valid public property.
            Func<object, object> getter;
            bool created = PropertyAccessorCache.TryCreateGetter(typeof(Person), "Name", out getter);

            // When: the getter is invoked.
            var value = getter(new Person { Name = "Alice" });

            // Then: creation succeeds and value is returned.
            Assert.That(created, Is.True);
            Assert.That(value, Is.EqualTo("Alice"));
        }

        [Test]
        public void TryCreateGetter_InvalidProperty_ReturnsFalse()
        {
            // Given: an invalid property name.

            // When: getter creation is attempted.
            Func<object, object> getter;
            bool created = PropertyAccessorCache.TryCreateGetter(typeof(Person), "Missing", out getter);

            // Then: creation fails without throwing.
            Assert.That(created, Is.False);
            Assert.That(getter, Is.Null);
        }

        [Test]
        public void CreateGetter_InvalidProperty_ReturnsNullWithoutThrow()
        {
            // Given: dynamic getter for a missing property.
            var getter = PropertyAccessorCache.CreateGetter("Missing");

            // When: getter is invoked.
            object value = null;
            Assert.DoesNotThrow(() => value = getter(new Person { Name = "Alice" }));

            // Then: no exception is thrown and null is returned.
            Assert.That(value, Is.Null);
        }

        [Test]
        public void CreateSetter_ValidProperty_UpdatesValue()
        {
            // Given: setter for a writable property.
            var setter = PropertyAccessorCache.CreateSetter("Name");
            var person = new Person { Name = "Before" };

            // When: setter is invoked.
            setter(person, "After");

            // Then: property value is updated.
            Assert.That(person.Name, Is.EqualTo("After"));
        }

        [Test]
        public void CreateSetter_InvalidProperty_DoesNotThrow()
        {
            // Given: setter for a missing property.
            var setter = PropertyAccessorCache.CreateSetter("Missing");
            var person = new Person { Name = "Alice" };

            // When: setter is invoked.
            Assert.DoesNotThrow(() => setter(person, "Bob"));

            // Then: original value remains unchanged.
            Assert.That(person.Name, Is.EqualTo("Alice"));
        }

        [Test]
        public void CreateGetter_WorksAcrossDifferentItemTypes()
        {
            // Given: one dynamic getter used against two types sharing same property name.
            var getter = PropertyAccessorCache.CreateGetter("Name");

            // When: getter reads values from different types.
            var valueA = getter(new Person { Name = "Alice" });
            var valueB = getter(new OtherPerson { Name = "Bob" });

            // Then: both values are resolved correctly.
            Assert.That(valueA, Is.EqualTo("Alice"));
            Assert.That(valueB, Is.EqualTo("Bob"));
        }

        [Test]
        public void TryCreateStringGetter_NonStringProperty_ReturnsFalse()
        {
            // Given: a non-string property.

            // When: string getter creation is attempted.
            Func<object, string> getter;
            bool created = PropertyAccessorCache.TryCreateStringGetter(typeof(Person), "Age", out getter);

            // Then: creation fails.
            Assert.That(created, Is.False);
            Assert.That(getter, Is.Null);
        }

        [Test]
        public void TryCreateGetter_NestedProperty_ReturnsValue()
        {
            // Given: a valid nested public property path.
            Func<object, object> getter;
            bool created = PropertyAccessorCache.TryCreateGetter(typeof(Product), "Towar.Symbol", out getter);

            // When: the getter is invoked.
            var value = getter(new Product { Towar = new ProductInfo { Symbol = "ABC" } });

            // Then: creation succeeds and nested value is returned.
            Assert.That(created, Is.True);
            Assert.That(value, Is.EqualTo("ABC"));
        }

        [Test]
        public void TryCreateStringGetter_NestedStringProperty_ReturnsValue()
        {
            // Given: a nested string property path.
            Func<object, string> getter;
            bool created = PropertyAccessorCache.TryCreateStringGetter(typeof(Product), "Towar.Symbol", out getter);

            // When: the getter is invoked.
            var value = getter(new Product { Towar = new ProductInfo { Symbol = "ABC" } });

            // Then: creation succeeds and nested string value is returned.
            Assert.That(created, Is.True);
            Assert.That(value, Is.EqualTo("ABC"));
        }

        [Test]
        public void CreateGetter_NestedProperty_WorksAcrossDifferentItemTypes()
        {
            // Given: one dynamic getter used against two types sharing the same nested path.
            var getter = PropertyAccessorCache.CreateGetter("Towar.Symbol");

            // When: getter reads values from different root types.
            var valueA = getter(new Product { Towar = new ProductInfo { Symbol = "AAA" } });
            var valueB = getter(new OtherProduct { Towar = new OtherProductInfo { Symbol = "BBB" } });

            // Then: both values are resolved correctly.
            Assert.That(valueA, Is.EqualTo("AAA"));
            Assert.That(valueB, Is.EqualTo("BBB"));
        }

        [Test]
        public void CreateGetter_NestedProperty_NullIntermediate_ReturnsNullWithoutThrow()
        {
            // Given: dynamic getter for a nested path whose owner object is null.
            var getter = PropertyAccessorCache.CreateGetter("Towar.Symbol");

            // When: getter is invoked.
            object value = null;
            Assert.DoesNotThrow(() => value = getter(new Product { Towar = null }));

            // Then: no exception is thrown and null is returned.
            Assert.That(value, Is.Null);
        }

        [Test]
        public void CreateSetter_NestedProperty_UpdatesValue()
        {
            // Given: setter for a writable nested property.
            var setter = PropertyAccessorCache.CreateSetter("Towar.Quantity");
            var product = new Product { Towar = new ProductInfo { Quantity = 1 } };

            // When: setter is invoked.
            setter(product, 7);

            // Then: nested property value is updated.
            Assert.That(product.Towar.Quantity, Is.EqualTo(7));
        }

        [Test]
        public void CreateSetter_NestedProperty_NullIntermediate_DoesNotThrow()
        {
            // Given: setter for a nested property whose owner object is null.
            var setter = PropertyAccessorCache.CreateSetter("Towar.Quantity");
            var product = new Product { Towar = null };

            // When: setter is invoked.
            Assert.DoesNotThrow(() => setter(product, 7));

            // Then: no object is created implicitly.
            Assert.That(product.Towar, Is.Null);
        }

        [Test]
        public void CreateSetter_NestedProperty_WithValueTypeIntermediate_UpdatesOriginalOwner()
        {
            // Given: setter for a nested property whose immediate owner is a struct.
            var setter = PropertyAccessorCache.CreateSetter("Details.QuantityInfo.Quantity");
            var product = new ProductWithStructDetails
            {
                Details = new ProductDetails
                {
                    QuantityInfo = new QuantityInfo { Quantity = 1 }
                }
            };

            // When: setter is invoked.
            setter(product, 7);

            // Then: the updated struct value is written back to the original owner.
            Assert.That(product.Details.QuantityInfo.Quantity, Is.EqualTo(7));
        }
    }
}
