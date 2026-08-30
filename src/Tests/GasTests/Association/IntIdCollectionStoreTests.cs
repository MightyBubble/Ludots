using System;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Registry;
using Ludots.Core.TypedCollections;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class IntIdCollectionStoreTests
    {
        [Test]
        public void Replace_IndexesByOwnerAndKey_AndExposesIdsInDeterministicOrder()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            Entity otherOwner = world.Create();
            var registry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var store = new IntIdCollectionStore(registry, initialCollectionCapacity: 2, initialRowCapacity: 4);

            var descriptor = IntIdCollectionDescriptor.Create(
                "tests.intid.explicit",
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.Display,
                "Template Ids",
                "2 ids");

            IntIdCollectionHandle handle = store.Replace(owner, descriptor, new[] { 11, 22 });

            Assert.That(handle.IsValid, Is.True);
            Assert.That(store.TryGet(owner, "tests.intid.explicit", out IntIdCollectionHandle resolved), Is.True);
            Assert.That(resolved.Slot, Is.EqualTo(handle.Slot));
            Assert.That(store.TryGet(otherOwner, "tests.intid.explicit", out _), Is.False);

            Assert.That(store.TryGetIdAt(resolved, 0, out int first), Is.True);
            Assert.That(first, Is.EqualTo(11));
            Assert.That(store.TryGetIdAt(resolved, 1, out int second), Is.True);
            Assert.That(second, Is.EqualTo(22));
            Assert.That(store.TryGetIdAt(resolved, 2, out _), Is.False);

            Span<int> copied = stackalloc int[2];
            Assert.That(store.CopyIds(resolved, 0, copied), Is.EqualTo(2));
            Assert.That(copied[0], Is.EqualTo(11));
            Assert.That(copied[1], Is.EqualTo(22));

            Assert.That(store.TryGetView(resolved, out IntIdCollectionView view), Is.True);
            Assert.That(view.Owner, Is.EqualTo(owner));
            Assert.That(view.Key, Is.EqualTo("tests.intid.explicit"));
            Assert.That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.Explicit));
            Assert.That(view.Role, Is.EqualTo(EntityCollectionRoleKind.Display));
            Assert.That(view.Count, Is.EqualTo(2));
            Assert.That(view.Title, Is.EqualTo("Template Ids"));
            Assert.That(view.Summary, Is.EqualTo("2 ids"));
        }

        [Test]
        public void Replace_SameContentPreservesRevision_ContentOrDescriptorChangeBumpsRevision()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var store = new IntIdCollectionStore(new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            var descriptor = IntIdCollectionDescriptor.Create(
                "tests.intid.revision",
                EntityCollectionSourceKind.Debug,
                EntityCollectionRoleKind.Debug,
                "Debug",
                "same");

            IntIdCollectionHandle firstHandle = store.Replace(owner, descriptor, new[] { 1, 2 });
            IntIdCollectionHandle secondHandle = store.Replace(owner, descriptor, new[] { 1, 2 });
            IntIdCollectionHandle thirdHandle = store.Replace(owner, descriptor, new[] { 1, 3 });
            var changedDescriptor = IntIdCollectionDescriptor.Create(
                "tests.intid.revision",
                EntityCollectionSourceKind.Debug,
                EntityCollectionRoleKind.Debug,
                "Debug",
                "changed");
            IntIdCollectionHandle fourthHandle = store.Replace(owner, changedDescriptor, new[] { 1, 3 });

            Assert.That(secondHandle.Revision, Is.EqualTo(firstHandle.Revision));
            Assert.That(thirdHandle.Revision, Is.GreaterThan(secondHandle.Revision));
            Assert.That(fourthHandle.Revision, Is.GreaterThan(thirdHandle.Revision));
        }

        [Test]
        public void Replace_KeyIdOverload_RepeatedCallsDoNotGrowKeyRegistry()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var registry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var store = new IntIdCollectionStore(registry, initialCollectionCapacity: 2, initialRowCapacity: 4);
            int keyId = registry.Register("tests.intid.keyid.repeat");
            var descriptor = IntIdCollectionDescriptor.Create(
                "tests.intid.keyid.repeat",
                EntityCollectionSourceKind.Debug,
                EntityCollectionRoleKind.Display,
                "Repeat",
                "same");

            for (int i = 0; i < 100; i++)
            {
                store.Replace(owner, keyId, descriptor, new[] { 42 });
            }

            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(store.CollectionCount, Is.EqualTo(1));
        }

        [Test]
        public void Replace_KeyIdOverload_SharesKeyNamespaceWithEntityCollectionStore()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var registry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var entityStore = new EntityCollectionStore(registry);
            var intIdStore = new IntIdCollectionStore(registry);
            int keyId = registry.Register("tests.shared.key");

            entityStore.Replace(
                owner,
                keyId,
                EntityCollectionDescriptor.Create(
                    "tests.shared.key",
                    EntityCollectionSourceKind.Explicit,
                    EntityCollectionRoleKind.Display),
                Array.Empty<Entity>());
            intIdStore.Replace(
                owner,
                keyId,
                IntIdCollectionDescriptor.Create(
                    "tests.shared.key",
                    EntityCollectionSourceKind.GasGraphResult,
                    EntityCollectionRoleKind.Display,
                    "Ids",
                    "1"),
                new[] { 7 });

            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(intIdStore.TryGet(owner, keyId, out IntIdCollectionHandle handle), Is.True);
            Assert.That(intIdStore.TryGetIdAt(handle, 0, out int id), Is.True);
            Assert.That(id, Is.EqualTo(7));
        }

        [Test]
        public void Remove_ClearsLookup()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            var store = new IntIdCollectionStore(new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            var descriptor = IntIdCollectionDescriptor.Create(
                "tests.intid.remove",
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.Display);

            store.Replace(owner, descriptor, new[] { 9 });
            Assert.That(store.Remove(owner, "tests.intid.remove"), Is.True);
            Assert.That(store.TryGet(owner, "tests.intid.remove", out _), Is.False);
            Assert.That(store.CollectionCount, Is.EqualTo(0));
        }
    }
}
