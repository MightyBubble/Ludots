using System;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class EntityCollectionStoreTests
    {
        [Test]
        public void Replace_IndexesByOwnerAndKey_AndCopiesRowsInDeterministicOrder()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            Entity first = world.Create();
            Entity second = world.Create();
            Entity otherOwner = world.Create();
            var registry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
            var store = new EntityCollectionStore(registry, initialCollectionCapacity: 2, initialRowCapacity: 4);

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.EntityInfoExplicit,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.Display,
                owner,
                first,
                "Query Result",
                "2 entities");

            EntityCollectionHandle handle = store.Replace(owner, descriptor, new[] { first, second });

            Assert.That(handle.IsValid, Is.True);
            Assert.That(store.TryGet(owner, EntityCollectionKeys.EntityInfoExplicit, out EntityCollectionHandle resolved), Is.True);
            Assert.That(resolved.Slot, Is.EqualTo(handle.Slot));
            Assert.That(store.TryGet(otherOwner, EntityCollectionKeys.EntityInfoExplicit, out _), Is.False);

            Span<Entity> copied = stackalloc Entity[2];
            Assert.That(store.CopyEntities(resolved, 0, copied), Is.EqualTo(2));
            Assert.That(copied[0], Is.EqualTo(first));
            Assert.That(copied[1], Is.EqualTo(second));

            Assert.That(store.TryGetView(resolved, out EntityCollectionView view), Is.True);
            Assert.That(view.Owner, Is.EqualTo(owner));
            Assert.That(view.Key, Is.EqualTo(EntityCollectionKeys.EntityInfoExplicit));
            Assert.That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.Explicit));
            Assert.That(view.Role, Is.EqualTo(EntityCollectionRoleKind.Display));
            Assert.That(view.ContextEntity, Is.EqualTo(owner));
            Assert.That(view.PrimaryEntity, Is.EqualTo(first));
            Assert.That(view.Count, Is.EqualTo(2));
            Assert.That(view.Title, Is.EqualTo("Query Result"));
            Assert.That(view.Summary, Is.EqualTo("2 entities"));
        }

        [Test]
        public void Replace_SameContentPreservesRevision_ContentOrDescriptorChangeBumpsRevision()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            Entity first = world.Create();
            Entity second = world.Create();
            Entity third = world.Create();
            var store = new EntityCollectionStore(new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal));
            var descriptor = EntityCollectionDescriptor.Create(
                "tests.revision",
                EntityCollectionSourceKind.Debug,
                EntityCollectionRoleKind.Debug,
                owner,
                first,
                "Debug",
                "same");

            EntityCollectionHandle firstHandle = store.Replace(owner, descriptor, new[] { first, second });
            EntityCollectionHandle secondHandle = store.Replace(owner, descriptor, new[] { first, second });
            EntityCollectionHandle thirdHandle = store.Replace(owner, descriptor, new[] { first, third });
            var changedDescriptor = EntityCollectionDescriptor.Create(
                "tests.revision",
                EntityCollectionSourceKind.Debug,
                EntityCollectionRoleKind.Debug,
                owner,
                first,
                "Debug",
                "changed");
            EntityCollectionHandle fourthHandle = store.Replace(owner, changedDescriptor, new[] { first, third });

            Assert.That(secondHandle.Revision, Is.EqualTo(firstHandle.Revision));
            Assert.That(thirdHandle.Revision, Is.GreaterThan(secondHandle.Revision));
            Assert.That(fourthHandle.Revision, Is.GreaterThan(thirdHandle.Revision));
        }

        [Test]
        public void CopyWindow_ReturnsRowsRolesAndFlags_WithoutAllocatingAfterWarmup()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var rows = new Entity[128];
            var roleIds = new int[128];
            var flags = new EntityCollectionRowFlags[128];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = world.Create();
                roleIds[i] = i % 3;
                flags[i] = i == 17 ? EntityCollectionRowFlags.Primary : EntityCollectionRowFlags.None;
            }

            var store = new EntityCollectionStore(new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal));
            var descriptor = EntityCollectionDescriptor.Create(
                "tests.window",
                EntityCollectionSourceKind.Debug,
                EntityCollectionRoleKind.Display,
                owner,
                rows[17],
                "Window",
                "128 entities");
            EntityCollectionHandle handle = store.Replace(owner, descriptor, rows, roleIds, flags);

            Span<Entity> entityWindow = stackalloc Entity[8];
            Span<int> ordinalWindow = stackalloc int[8];
            Span<int> roleWindow = stackalloc int[8];
            Span<EntityCollectionRowFlags> flagWindow = stackalloc EntityCollectionRowFlags[8];
            Assert.That(store.CopyWindow(handle, 16, entityWindow, ordinalWindow, roleWindow, flagWindow), Is.EqualTo(8));
            Assert.That(entityWindow[0], Is.EqualTo(rows[16]));
            Assert.That(ordinalWindow[1], Is.EqualTo(17));
            Assert.That(roleWindow[2], Is.EqualTo(18 % 3));
            Assert.That(flagWindow[1], Is.EqualTo(EntityCollectionRowFlags.Primary));

            long allocated = long.MaxValue;
            for (int window = 0; window < 2; window++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1024; i++)
                {
                    store.CopyWindow(handle, 32, entityWindow, ordinalWindow, roleWindow, flagWindow);
                }

                allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            Assert.That(allocated, Is.EqualTo(0));
        }
    }
}
