using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class DeferredTriggerTests
    {
        private World _world;
        private Entity _entity;
        private DeferredTriggerQueue _queue;
        
        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _entity = _world.Create();
            _queue = new DeferredTriggerQueue();
        }
        
        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }
        
        [Test]
        public void TestDeferredTriggerQueue_AttributeChanged()
        {
            // Arrange
            var trigger = new AttributeChangedTrigger
            {
                Target = _entity,
                AttributeId = 1,
                OldValue = 10f,
                NewValue = 20f
            };
            
            // Act
            _queue.EnqueueAttributeChanged(trigger);
            
            // Assert
            That(_queue.AttributeTriggerCount, Is.EqualTo(1));
            var retrieved = _queue.GetAttributeTrigger(0);
            That(retrieved.Target.Id, Is.EqualTo(_entity.Id));
            That(retrieved.AttributeId, Is.EqualTo(1));
            That(retrieved.OldValue, Is.EqualTo(10f));
            That(retrieved.NewValue, Is.EqualTo(20f));
            
            Console.WriteLine($"[DeferredTriggerTests] TestDeferredTriggerQueue_AttributeChanged: Attribute trigger enqueued correctly");
        }
        
        [Test]
        public void TestDeferredTriggerQueue_TagChanged()
        {
            // Arrange
            var trigger = new TagChangedTrigger
            {
                Target = _entity,
                TagId = 5,
                WasPresent = false,
                IsPresent = true
            };
            
            // Act
            _queue.EnqueueTagChanged(trigger);
            
            // Assert
            That(_queue.TagTriggerCount, Is.EqualTo(1));
            var retrieved = _queue.GetTagTrigger(0);
            That(retrieved.Target.Id, Is.EqualTo(_entity.Id));
            That(retrieved.TagId, Is.EqualTo(5));
            That(retrieved.WasPresent, Is.False);
            That(retrieved.IsPresent, Is.True);
            
            Console.WriteLine($"[DeferredTriggerTests] TestDeferredTriggerQueue_TagChanged: Tag trigger enqueued correctly");
        }
        
        [Test]
        public void TestDirtyFlags_MarkAttributeDirty()
        {
            // Arrange
            _world.Add(_entity, new DirtyFlags());
            ref var dirtyFlags = ref _world.Get<DirtyFlags>(_entity);
            
            // Act
            dirtyFlags.MarkAttributeDirty(10);
            dirtyFlags.MarkAttributeDirty(20);
            
            // Assert
            That(dirtyFlags.IsAttributeDirty(10), Is.True);
            That(dirtyFlags.IsAttributeDirty(20), Is.True);
            That(dirtyFlags.IsAttributeDirty(30), Is.False);
            
            Console.WriteLine($"[DeferredTriggerTests] TestDirtyFlags_MarkAttributeDirty: Dirty flags work correctly");
        }
        
        [Test]
        public void TestDirtyFlags_MarkTagDirty()
        {
            // Arrange
            _world.Add(_entity, new DirtyFlags());
            ref var dirtyFlags = ref _world.Get<DirtyFlags>(_entity);
            
            // Act
            dirtyFlags.MarkTagDirty(5);
            dirtyFlags.MarkTagDirty(100);
            
            // Assert
            That(dirtyFlags.IsTagDirty(5), Is.True);
            That(dirtyFlags.IsTagDirty(100), Is.True);
            That(dirtyFlags.IsTagDirty(200), Is.False);
            
            Console.WriteLine($"[DeferredTriggerTests] TestDirtyFlags_MarkTagDirty: Tag dirty flags work correctly");
        }
        
        [Test]
        public void TestDirtyFlags_Clear()
        {
            // Arrange
            _world.Add(_entity, new DirtyFlags());
            ref var dirtyFlags = ref _world.Get<DirtyFlags>(_entity);
            dirtyFlags.MarkAttributeDirty(10);
            dirtyFlags.MarkTagDirty(5);
            
            // Act
            dirtyFlags.Clear();
            
            // Assert
            That(dirtyFlags.IsAttributeDirty(10), Is.False);
            That(dirtyFlags.IsTagDirty(5), Is.False);
            
            Console.WriteLine($"[DeferredTriggerTests] TestDirtyFlags_Clear: Clear works correctly");
        }
    }
}
