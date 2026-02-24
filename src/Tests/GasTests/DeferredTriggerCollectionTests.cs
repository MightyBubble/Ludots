using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class DeferredTriggerCollectionTests
    {
        [Test]
        public void DeferredTriggerQueue_Overflow_DefersToNextFrame()
        {
            var queue = new DeferredTriggerQueue();
            for (int i = 0; i < GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME + 1; i++)
            {
                queue.EnqueueAttributeChanged(new AttributeChangedTrigger { AttributeId = i });
            }

            That(queue.AttributeTriggerCount, Is.EqualTo(GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME));

            queue.Clear();

            That(queue.AttributeTriggerCount, Is.EqualTo(1));
            That(queue.GetAttributeTrigger(0).AttributeId, Is.EqualTo(GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME));
        }

        [Test]
        public void DeferredTrigger_AttributeChanged_UsesSnapshotOldValue()
        {
            using var world = World.Create();
            var queue = new DeferredTriggerQueue();
            var system = new DeferredTriggerCollectionSystem(world, queue);

            var e = world.Create();
            var attrs = new AttributeBuffer();
            attrs.SetCurrent(0, 20f);
            world.Add(e, attrs);

            var snap = default(AttributeLastSnapshot);
            unsafe { snap.Values[0] = 10f; }
            world.Add(e, snap);

            var dirty = default(DirtyFlags);
            dirty.MarkAttributeDirty(0);
            world.Add(e, dirty);

            system.Update(0.016f);

            That(queue.AttributeTriggerCount, Is.EqualTo(1));
            var trigger = queue.GetAttributeTrigger(0);
            That(trigger.AttributeId, Is.EqualTo(0));
            That(trigger.OldValue, Is.EqualTo(10f));
            That(trigger.NewValue, Is.EqualTo(20f));

            ref var snapRef = ref world.Get<AttributeLastSnapshot>(e);
            unsafe { That(snapRef.Values[0], Is.EqualTo(20f)); }
        }

        [Test]
        public void DeferredTrigger_TagChanged_UsesSnapshotWasPresent()
        {
            using var world = World.Create();
            var queue = new DeferredTriggerQueue();
            var system = new DeferredTriggerCollectionSystem(world, queue);

            var e = world.Create();
            var tags = new GameplayTagContainer();
            tags.AddTag(5);
            world.Add(e, tags);

            var snap = default(GameplayTagSnapshot);
            world.Add(e, snap);

            var dirty = default(DirtyFlags);
            dirty.MarkTagDirty(5);
            world.Add(e, dirty);

            system.Update(0.016f);

            That(queue.TagTriggerCount, Is.EqualTo(1));
            var trigger = queue.GetTagTrigger(0);
            That(trigger.TagId, Is.EqualTo(5));
            That(trigger.WasPresent, Is.False);
            That(trigger.IsPresent, Is.True);
        }

        [Test]
        public void DeferredTrigger_TagCountChanged_UsesSnapshotOldCount()
        {
            using var world = World.Create();
            var queue = new DeferredTriggerQueue();
            var system = new DeferredTriggerCollectionSystem(world, queue);

            var e = world.Create();
            var counts = new TagCountContainer();
            counts.AddCount(7, 3);
            world.Add(e, counts);

            var snap = default(TagCountSnapshot);
            snap.SetCount(7, 1);
            world.Add(e, snap);

            var dirty = default(DirtyFlags);
            dirty.MarkTagDirty(7);
            world.Add(e, dirty);

            system.Update(0.016f);

            That(queue.TagCountTriggerCount, Is.EqualTo(1));
            var trigger = queue.GetTagCountTrigger(0);
            That(trigger.TagId, Is.EqualTo(7));
            That(trigger.OldCount, Is.EqualTo((ushort)1));
            That(trigger.NewCount, Is.EqualTo((ushort)3));

            ref var snapRef = ref world.Get<TagCountSnapshot>(e);
            That(snapRef.GetCount(7), Is.EqualTo((ushort)3));
        }
    }
}
