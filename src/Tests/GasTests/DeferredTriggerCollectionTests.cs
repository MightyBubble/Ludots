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
            var system = new DeferredTriggerCollectionSystem(world, queue, new TagOps());

            var e = world.Create();
            var attrs = new AttributeBuffer();
            attrs.SetBase(0, 20f);
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
            var system = new DeferredTriggerCollectionSystem(world, queue, new TagOps());

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
            var system = new DeferredTriggerCollectionSystem(world, queue, new TagOps());

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

        [Test]
        public void DeferredTriggerCollection_VisitsOnlyActiveDirtyEntitiesAfterBootstrap()
        {
            using var world = World.Create();
            var triggers = new DeferredTriggerQueue();
            var active = new DirtyEntityQueue(32);
            var tagOps = new TagOps(new TagRuleRegistry(), dirtyEntities: active);
            var system = new DeferredTriggerCollectionSystem(world, triggers, tagOps, active);
            var dirtyEntities = new Entity[10];

            for (int i = 0; i < 10_000; i++)
            {
                Entity entity = world.Create(new DirtyFlags(), new AttributeBuffer());
                if (i < dirtyEntities.Length)
                {
                    world.Get<DirtyFlags>(entity).MarkAttributeDirty(0);
                    active.Track(world, entity);
                    dirtyEntities[i] = entity;
                }
            }

            system.Update(0f);
            Assert.That(system.VisitedEntityCountLastUpdate, Is.EqualTo(10));

            for (int i = 0; i < dirtyEntities.Length; i++)
            {
                world.Get<DirtyFlags>(dirtyEntities[i]).MarkAttributeDirty(1);
                active.Track(world, dirtyEntities[i]);
                active.Track(world, dirtyEntities[i]);
            }

            system.Update(0f);

            Assert.That(system.VisitedEntityCountLastUpdate, Is.EqualTo(10));
            Assert.That(active.Count, Is.Zero);
        }

        [Test]
        public void DirtyEntityQueue_Full_ThrowsWithoutSilentlyDroppingDirtyEntity()
        {
            using var world = World.Create();
            var active = new DirtyEntityQueue(1);
            Entity first = world.Create(new DirtyFlags());
            Entity second = world.Create(new DirtyFlags());

            Assert.That(active.Track(world, first), Is.True);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => active.Track(world, second))!;

            Assert.That(ex.Message, Does.Contain(DirtyEntityQueue.CapacityExceededError));
            Assert.That(active.Count, Is.EqualTo(1));
            Assert.That(active.OverflowCount, Is.EqualTo(1));
            Assert.That(world.Get<DirtyFlags>(second).DeferredTriggerQueued, Is.Zero);
        }

        [Test]
        public void AttributeMutation_WhenDirtyQueueIsFull_RollsBackValueAndDirtyState()
        {
            using var world = World.Create();
            var active = new DirtyEntityQueue(1);
            var tagOps = new TagOps(new TagRuleRegistry(), dirtyEntities: active);
            Entity queued = world.Create(new DirtyFlags());
            var attributes = new AttributeBuffer();
            attributes.SetBase(0, 100f);
            attributes.SetCurrent(0, 100f);
            Entity target = world.Create(attributes, new DirtyFlags());
            active.Track(world, queued);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => AttributeMutationOps.SetCurrent(world, target, 0, 75f, tagOps))!;

            Assert.That(ex.Message, Does.Contain(DirtyEntityQueue.CapacityExceededError));
            Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(0), Is.EqualTo(100f));
            Assert.That(world.Get<DirtyFlags>(target).IsAnyAttributeDirty(), Is.False);
            Assert.That(world.Get<DirtyFlags>(target).DeferredTriggerQueued, Is.Zero);
        }

        [Test]
        public void AttributeModifiers_WhenDirtyQueueIsFull_RollBackAllTouchedValues()
        {
            using var world = World.Create();
            var active = new DirtyEntityQueue(1);
            var tagOps = new TagOps(new TagRuleRegistry(), dirtyEntities: active);
            Entity queued = world.Create(new DirtyFlags());
            var attributes = new AttributeBuffer();
            attributes.SetBase(0, 100f);
            attributes.SetCurrent(0, 100f);
            attributes.SetBase(1, 50f);
            attributes.SetCurrent(1, 50f);
            Entity target = world.Create(attributes, new DirtyFlags());
            var modifiers = default(EffectModifiers);
            modifiers.Add(0, ModifierOp.Add, -25f);
            modifiers.Add(1, ModifierOp.Add, 10f);
            active.Track(world, queued);

            Assert.Throws<InvalidOperationException>(
                () => AttributeMutationOps.ApplyModifiers(world, target, in modifiers, tagOps));

            ref AttributeBuffer after = ref world.Get<AttributeBuffer>(target);
            Assert.That(after.GetCurrent(0), Is.EqualTo(100f));
            Assert.That(after.GetCurrent(1), Is.EqualTo(50f));
            Assert.That(world.Get<DirtyFlags>(target).IsAnyAttributeDirty(), Is.False);
            Assert.That(world.Get<DirtyFlags>(target).DeferredTriggerQueued, Is.Zero);
        }

        [Test]
        public void DeferredTriggerActivePath_AfterWarmup_AllocatesZero()
        {
            using var world = World.Create();
            var triggers = new DeferredTriggerQueue();
            var active = new DirtyEntityQueue(4);
            var tagOps = new TagOps(new TagRuleRegistry(), dirtyEntities: active);
            var system = new DeferredTriggerCollectionSystem(world, triggers, tagOps, active);
            var attributes = new AttributeBuffer();
            attributes.SetCurrent(0, 1f);
            var snapshot = default(AttributeLastSnapshot);
            unsafe { snapshot.Values[0] = 1f; }
            Entity entity = world.Create(attributes, snapshot, new DirtyFlags());
            system.Update(0f);

            world.Get<AttributeBuffer>(entity).SetCurrent(0, 2f);
            world.Get<DirtyFlags>(entity).MarkAttributeDirty(0);
            active.Track(world, entity);
            system.Update(0f);
            triggers.Clear();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 100; i++)
            {
                world.Get<AttributeBuffer>(entity).SetCurrent(0, i + 2f);
                world.Get<DirtyFlags>(entity).MarkAttributeDirty(0);
                active.Track(world, entity);
                system.Update(0f);
                triggers.Clear();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.LessThanOrEqualTo(64));
        }
    }
}
