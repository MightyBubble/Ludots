using System;
using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class BlackboardEntityRefLifetimeTests
    {
        [Test]
        public void Destroy_ClearsMatchingSlotsOnFlush_AndLeavesOtherSlots()
        {
            using World world = World.Create();
            var system = new BlackboardEntityRefLifetimeSystem(world);
            Entity victim = world.Create();
            Entity survivor = world.Create();
            Entity owner = world.Create(new BlackboardEntityBuffer());
            ref BlackboardEntityBuffer buffer = ref world.Get<BlackboardEntityBuffer>(owner);
            buffer.Set(1, victim);
            buffer.Set(2, survivor);

            world.Destroy(victim);
            Assert.That(buffer.TryGet(1, out Entity beforeFlush), Is.True);
            Assert.That(beforeFlush, Is.EqualTo(victim));

            system.Flush();
            Assert.That(buffer.TryGet(1, out Entity cleared), Is.True);
            Assert.That(cleared, Is.EqualTo(Entity.Null));
            Assert.That(buffer.HasKey(1), Is.True);
            Assert.That(buffer.TryGet(2, out Entity kept), Is.True);
            Assert.That(kept, Is.EqualTo(survivor));
        }

        [Test]
        public void MassDestroy_CoalescedFlush_IsFasterThanPerDestroyScan_AndAllocatesNothing()
        {
            const int count = 10_000;
            (double oldMs, long oldAlloc) = MeasurePerDestroyScan(count);
            (double destroyMs, double flushMs, long flushAlloc) = MeasureCoalesced(count);
            TestContext.Out.WriteLine(
                $"blackboard entity-ref lifetime {count} deaths x {count} boards: " +
                $"per-destroy scan {oldMs:F2} ms alloc {oldAlloc}; " +
                $"coalesced destroy+enqueue {destroyMs:F2} ms; flush {flushMs:F2} ms alloc {flushAlloc}");
            Assert.That(flushAlloc, Is.EqualTo(0));
            Assert.That(flushMs, Is.LessThan(oldMs));
        }

        private static (double Milliseconds, long AllocatedBytes) MeasurePerDestroyScan(int count)
        {
            using World world = World.Create();
            var targets = new Entity[count];
            var query = new QueryDescription().WithAll<BlackboardEntityBuffer>();
            for (int i = 0; i < count; i++)
            {
                targets[i] = world.Create();
                var buffer = new BlackboardEntityBuffer();
                buffer.Set(1, targets[i]);
                world.Create(buffer);
            }

            var warmup = new PerDestroyScan(targets[0]);
            world.InlineEntityQuery<PerDestroyScan, BlackboardEntityBuffer>(in query, ref warmup);
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                var scan = new PerDestroyScan(targets[i]);
                world.InlineEntityQuery<PerDestroyScan, BlackboardEntityBuffer>(in query, ref scan);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return (milliseconds, allocated);
        }

        private static (double DestroyMilliseconds, double FlushMilliseconds, long FlushAllocatedBytes) MeasureCoalesced(int count)
        {
            using World world = World.Create();
            var system = new BlackboardEntityRefLifetimeSystem(world);
            for (int i = 0; i < count; i++)
            {
                world.Destroy(world.Create());
            }

            system.Flush();

            var targets = new Entity[count];
            var owners = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                targets[i] = world.Create();
                var buffer = new BlackboardEntityBuffer();
                buffer.Set(7, targets[i]);
                owners[i] = world.Create(buffer);
            }

            // The first flush that sees the blackboard archetype fills the query's match list.
            // Time and allocation are measured on the next flush, after that list already has capacity.
            Entity warmVictim = world.Create();
            var warmBuffer = new BlackboardEntityBuffer();
            warmBuffer.Set(7, warmVictim);
            world.Create(warmBuffer);
            world.Destroy(warmVictim);
            system.Flush();

            long destroyStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                world.Destroy(targets[i]);
            }

            double destroyMs = Stopwatch.GetElapsedTime(destroyStart).TotalMilliseconds;
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long flushStart = Stopwatch.GetTimestamp();
            system.Flush();
            double flushMs = Stopwatch.GetElapsedTime(flushStart).TotalMilliseconds;
            long flushAlloc = GC.GetAllocatedBytesForCurrentThread() - before;

            for (int i = 0; i < count; i++)
            {
                ref BlackboardEntityBuffer buffer = ref world.Get<BlackboardEntityBuffer>(owners[i]);
                Assert.That(buffer.TryGet(7, out Entity value), Is.True);
                Assert.That(value, Is.EqualTo(Entity.Null));
                Assert.That(buffer.HasKey(7), Is.True);
            }

            long steadyBefore = GC.GetAllocatedBytesForCurrentThread();
            system.Update(0f);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - steadyBefore, Is.EqualTo(0));
            return (destroyMs, flushMs, flushAlloc);
        }

        private readonly struct PerDestroyScan : IForEachWithEntity<BlackboardEntityBuffer>
        {
            private readonly Entity _dead;

            public PerDestroyScan(Entity dead) => _dead = dead;

            public unsafe void Update(Entity entity, ref BlackboardEntityBuffer buffer)
            {
                int count = buffer.Count;
                for (int i = 0; i < count; i++)
                {
                    Entity slot = EntityUtil.Reconstruct(buffer.EntityIds[i], buffer.WorldIds[i], buffer.Versions[i]);
                    if (slot != _dead)
                    {
                        continue;
                    }

                    buffer.EntityIds[i] = Entity.Null.Id;
                    buffer.WorldIds[i] = Entity.Null.WorldId;
                    buffer.Versions[i] = Entity.Null.Version;
                }
            }
        }
    }
}
