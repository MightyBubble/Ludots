using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    public sealed class TriggerGraphExecutionSlotStoreTests
    {
        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidCapacityFails(int capacity)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new TriggerGraphExecutionSlotStore(capacity));

        [Test]
        public void ExhaustionAndReusePreserveOtherRunAndRejectOldHandle()
        {
            var store = new TriggerGraphExecutionSlotStore(2);
            var first = store.Rent();
            var second = store.Rent();
            using var world = World.Create();
            Entity target = world.Create();
            store.Ints(first)[0] = 41;
            store.Ints(second)[0] = 99;
            store.Floats(first)[1] = 3.5f;
            store.Bools(first)[2] = 1;
            store.Entities(first)[3] = target;
            store.Targets(first)[4] = target;
            store.CallStack(first)[5] = 12;
            store.PreviousInts(first)[0] = 40;
            store.PreviousFloats(first)[1] = 3;
            store.PreviousBools(first)[2] = 1;
            store.PreviousEntities(first)[3] = target;
            store.EntryPayload(first).SetInt("count", 41);
            store.EntryPayload(second).SetInt("count", 99);
            store.InvokeArgs(first).UpsertEntity("target", target);
            store.InvokeArgs(second).UpsertFloat("power", 5);

            Assert.That(Assert.Throws<InvalidOperationException>(() => store.Rent())!.Message,
                Does.Contain("CapacityExceeded"));
            Assert.That(store.InUseCount, Is.EqualTo(2));
            store.Return(first);
            var replacement = store.Rent();
            Assert.That(replacement.Index, Is.EqualTo(first.Index));
            Assert.That(replacement.Generation, Is.GreaterThan(first.Generation));
            Assert.Throws<InvalidOperationException>(() => store.Return(first));
            Assert.Throws<InvalidOperationException>(() => store.Ints(first).Clear());
            Assert.That(store.Ints(replacement).ToArray(), Is.All.Zero);
            Assert.That(store.Floats(replacement).ToArray(), Is.All.Zero);
            Assert.That(store.Bools(replacement).ToArray(), Is.All.Zero);
            Assert.That(store.Entities(replacement).ToArray(), Is.All.EqualTo(default(Entity)));
            Assert.That(store.Targets(replacement).ToArray(), Is.All.EqualTo(default(Entity)));
            Assert.That(store.CallStack(replacement).ToArray(), Is.All.Zero);
            Assert.That(store.PreviousInts(replacement).ToArray(), Is.All.Zero);
            Assert.That(store.PreviousFloats(replacement).ToArray(), Is.All.Zero);
            Assert.That(store.PreviousBools(replacement).ToArray(), Is.All.Zero);
            Assert.That(store.PreviousEntities(replacement).ToArray(), Is.All.EqualTo(default(Entity)));
            Assert.That(store.EntryPayload(replacement).Count, Is.Zero);
            Assert.That(store.InvokeArgs(replacement).Count, Is.Zero);
            Assert.That(store.Ints(second)[0], Is.EqualTo(99));
            Assert.That(store.EntryPayload(second).TryGetInt("count", out int count), Is.True);
            Assert.That(count, Is.EqualTo(99));
            Assert.That(store.InvokeArgs(second).TryGetFloat("power", out float power), Is.True);
            Assert.That(power, Is.EqualTo(5));
            store.Return(second);
            store.Return(replacement);
            Assert.That(store.InUseCount, Is.Zero);
            Assert.That(store.HighWaterMark, Is.EqualTo(2));
        }

        [Test]
        public void RepeatedRentPayloadAndReturnAllocateNothingAfterConstruction()
        {
            var store = new TriggerGraphExecutionSlotStore(2);
            for (int i = 0; i < 100; i++) Cycle(store);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) Cycle(store);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(bytes, Is.Zero);
        }

        private static void Cycle(TriggerGraphExecutionSlotStore store)
        {
            var slot = store.Rent();
            store.Ints(slot)[0] = 42;
            store.EntryPayload(slot).SetInt("count", 42);
            store.InvokeArgs(slot).UpsertFloat("power", 2);
            store.Return(slot);
        }
    }
}
