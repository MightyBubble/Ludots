using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class ExtensionAttributeTests
    {
        private World _world;
        private Entity _entity;
        private ExtensionAttributeRegistry _registry;
        
        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _entity = _world.Create();
            _registry = new ExtensionAttributeRegistry();
        }
        
        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }
        
        [Test]
        public void TestExtensionAttributeRegistry_Register()
        {
            // Arrange
            string fullName = "Mod.MyMod.Attributes.CustomAttr";
            
            // Act
            int id1 = _registry.Register(fullName);
            int id2 = _registry.Register(fullName); // 重复注册应该返回相同ID
            
            // Assert
            That(id1, Is.EqualTo(id2));
            That(id1, Is.GreaterThanOrEqualTo(10001).And.LessThanOrEqualTo(20000)); // ID范围检查
            That(_registry.TryGetId(fullName, out var retrievedId), Is.True);
            That(retrievedId, Is.EqualTo(id1));
            
            Console.WriteLine($"[ExtensionAttributeTests] TestExtensionAttributeRegistry_Register: Registered '{fullName}' -> {id1}");
        }
        
        [Test]
        public void TestExtensionAttributeRegistry_TryGetName()
        {
            // Arrange
            string fullName = "Mod.MyMod.Attributes.AnotherAttr";
            int id = _registry.Register(fullName);
            
            // Act
            bool found = _registry.TryGetName(id, out var retrievedName);
            
            // Assert
            That(found, Is.True);
            That(retrievedName, Is.EqualTo(fullName));
            
            Console.WriteLine($"[ExtensionAttributeTests] TestExtensionAttributeRegistry_TryGetName: ID {id} -> '{retrievedName}'");
        }
        
        [Test]
        public void TestExtensionAttributeBuffer_SetGetValue()
        {
            // Arrange
            _world.Add(_entity, new ExtensionAttributeBuffer());
            ref var buffer = ref _world.Get<ExtensionAttributeBuffer>(_entity);
            int attrId = 10001;
            float value = 42.5f;
            
            // Act
            buffer.SetValue(attrId, value);
            
            // Assert
            That(buffer.TryGetValue(attrId, out var retrievedValue), Is.True);
            That(retrievedValue, Is.EqualTo(value));
            
            Console.WriteLine($"[ExtensionAttributeTests] TestExtensionAttributeBuffer_SetGetValue: Set/Get value {value} for attr {attrId}");
        }
        
        [Test]
        public void TestExtensionAttributeBuffer_SetGetBaseValue()
        {
            // Arrange
            _world.Add(_entity, new ExtensionAttributeBuffer());
            ref var buffer = ref _world.Get<ExtensionAttributeBuffer>(_entity);
            int attrId = 10002;
            float baseValue = 100f;
            float currentValue = 150f;
            
            // Act
            buffer.SetBaseValue(attrId, baseValue);
            buffer.SetValue(attrId, currentValue);
            
            // Assert
            That(buffer.TryGetBaseValue(attrId, out var retrievedBase), Is.True);
            That(retrievedBase, Is.EqualTo(baseValue));
            That(buffer.TryGetValue(attrId, out var retrievedCurrent), Is.True);
            That(retrievedCurrent, Is.EqualTo(currentValue));
            
            Console.WriteLine($"[ExtensionAttributeTests] TestExtensionAttributeBuffer_SetGetBaseValue: Base={retrievedBase}, Current={retrievedCurrent}");
        }
        
        [Test]
        public void TestExtensionAttributeBuffer_RemoveAttribute()
        {
            // Arrange
            _world.Add(_entity, new ExtensionAttributeBuffer());
            ref var buffer = ref _world.Get<ExtensionAttributeBuffer>(_entity);
            int attrId = 10003;
            buffer.SetValue(attrId, 50f);
            
            // Act
            bool removed = buffer.RemoveAttribute(attrId);
            
            // Assert
            That(removed, Is.True);
            That(buffer.TryGetValue(attrId, out _), Is.False);
            
            Console.WriteLine($"[ExtensionAttributeTests] TestExtensionAttributeBuffer_RemoveAttribute: Attribute {attrId} removed");
        }
    }
}
