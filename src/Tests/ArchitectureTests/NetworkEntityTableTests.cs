using System;
using Arch.Core;
using Ludots.Core.Networking.Replication;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NetworkEntityTableTests
    {
        [Test]
        public void Allocate_UsesFixedCapacityAndRejectsDuplicateEntity()
        {
            using World world = World.Create();
            Entity first = world.Create();
            Entity second = world.Create();
            Entity overflow = world.Create();
            var table = new NetworkEntityTable(capacity: 2);

            Assert.That(table.TryAllocate(first, out NetworkEntityHandle firstHandle), Is.True);
            Assert.That(table.TryAllocate(second, out NetworkEntityHandle secondHandle), Is.True);
            Assert.That(table.TryAllocate(first, out NetworkEntityHandle duplicateHandle), Is.False);
            Assert.That(table.TryAllocate(overflow, out NetworkEntityHandle overflowHandle), Is.False);

            Assert.That(firstHandle.IsValid, Is.True);
            Assert.That(secondHandle.IsValid, Is.True);
            Assert.That(duplicateHandle.IsValid, Is.False);
            Assert.That(overflowHandle.IsValid, Is.False);
            Assert.That(table.Count, Is.EqualTo(2));
            Assert.That(table.AvailableCapacity, Is.EqualTo(0));
        }

        [Test]
        public void Resolve_MapsFullArchEntityIdentityInBothDirections()
        {
            using World firstWorld = World.Create();
            using World secondWorld = World.Create();
            Entity first = firstWorld.Create();
            Entity second = secondWorld.Create();
            var table = new NetworkEntityTable(capacity: 2);

            Assert.That(first.Id, Is.EqualTo(second.Id));
            Assert.That(first.Version, Is.EqualTo(second.Version));
            Assert.That(first.WorldId, Is.Not.EqualTo(second.WorldId));
            Assert.That(table.TryAllocate(first, out NetworkEntityHandle firstHandle), Is.True);
            Assert.That(table.TryAllocate(second, out NetworkEntityHandle secondHandle), Is.True);

            Assert.That(table.TryResolve(firstHandle, out Entity resolvedFirst), Is.True);
            Assert.That(table.TryResolve(secondHandle, out Entity resolvedSecond), Is.True);
            Assert.That(table.TryResolve(first, out NetworkEntityHandle reverseFirst), Is.True);
            Assert.That(table.TryResolve(second, out NetworkEntityHandle reverseSecond), Is.True);

            Assert.That(resolvedFirst, Is.EqualTo(first));
            Assert.That(resolvedSecond, Is.EqualTo(second));
            Assert.That(reverseFirst, Is.EqualTo(firstHandle));
            Assert.That(reverseSecond, Is.EqualTo(secondHandle));
        }

        [Test]
        public void Release_RecyclesSlotWithNextGenerationAndRejectsStaleHandle()
        {
            using World world = World.Create();
            Entity first = world.Create();
            Entity second = world.Create();
            var table = new NetworkEntityTable(capacity: 1);

            Assert.That(table.TryAllocate(first, out NetworkEntityHandle staleHandle), Is.True);
            Assert.That(table.TryRelease(staleHandle), Is.True);
            Assert.That(table.TryResolve(staleHandle, out Entity staleEntity), Is.False);
            Assert.That(staleEntity, Is.EqualTo(Entity.Null));
            Assert.That(table.TryResolve(first, out NetworkEntityHandle releasedReverse), Is.False);
            Assert.That(releasedReverse.IsValid, Is.False);
            Assert.That(table.TryRelease(staleHandle), Is.False);

            Assert.That(table.TryAllocate(second, out NetworkEntityHandle currentHandle), Is.True);
            Assert.That(currentHandle.Slot, Is.EqualTo(staleHandle.Slot));
            Assert.That(currentHandle.Generation, Is.EqualTo(staleHandle.Generation + 1));
            Assert.That(table.TryResolve(staleHandle, out _), Is.False);
            Assert.That(table.TryResolve(currentHandle, out Entity currentEntity), Is.True);
            Assert.That(currentEntity, Is.EqualTo(second));
        }

        [Test]
        public void Release_WithWrongGenerationDoesNotMutateCurrentMapping()
        {
            using World world = World.Create();
            Entity entity = world.Create();
            var table = new NetworkEntityTable(capacity: 1);
            Assert.That(table.TryAllocate(entity, out NetworkEntityHandle current), Is.True);
            var wrongGeneration = new NetworkEntityHandle(current.Slot, current.Generation + 1);

            Assert.That(table.TryRelease(wrongGeneration), Is.False);
            Assert.That(table.TryResolve(current, out Entity resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(entity));
            Assert.That(table.Count, Is.EqualTo(1));
            Assert.That(table.AvailableCapacity, Is.EqualTo(0));
        }

        [Test]
        public void AllocateResolveRelease_IsZeroAllocAfterConstructionAndWarmup()
        {
            using World world = World.Create();
            Entity entity = world.Create();
            var table = new NetworkEntityTable(capacity: 1);

            for (int i = 0; i < 128; i++)
            {
                Assert.That(table.TryAllocate(entity, out NetworkEntityHandle warmupHandle), Is.True);
                Assert.That(table.TryResolve(warmupHandle, out _), Is.True);
                Assert.That(table.TryResolve(entity, out _), Is.True);
                Assert.That(table.TryRelease(warmupHandle), Is.True);
            }

            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool allSucceeded = true;
            for (int i = 0; i < 10_000; i++)
            {
                allSucceeded &= table.TryAllocate(entity, out NetworkEntityHandle handle);
                allSucceeded &= table.TryResolve(handle, out Entity resolved) && resolved == entity;
                allSucceeded &= table.TryResolve(entity, out NetworkEntityHandle reverse) && reverse == handle;
                allSucceeded &= table.TryRelease(handle);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allSucceeded, Is.True);
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(table.Count, Is.EqualTo(0));
            Assert.That(table.AvailableCapacity, Is.EqualTo(1));
        }
    }
}
