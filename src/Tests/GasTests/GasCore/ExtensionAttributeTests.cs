using Ludots.Core.Gameplay.GAS;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class ExtensionAttributeTests
    {
        private ExtensionAttributeRegistry _registry;
        
        [SetUp]
        public void Setup()
        {
            _registry = new ExtensionAttributeRegistry();
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
        
    }
}
