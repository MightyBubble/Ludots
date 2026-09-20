using System;
using System.Linq;
using Arch.Core;
using Ludots.Core.EntityCollections;
using NUnit.Framework;

namespace Ludots.Tests.Gas.InteractionInput
{
    /// <summary>
    /// CollectionWrite primitive (graph-side owned-collection write): the op semantics
    /// replace/add/subtract execute here over the store; membership semantics match the
    /// retired pass-through writer's behavior.
    /// </summary>
    public sealed class CollectionWriteTests
    {
        private World _world = null!;
        private EntityCollectionStore _store = null!;
        private Entity _owner;
        private int _keyId;
        private Entity _a;
        private Entity _b;
        private Entity _c;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            var keys = new Ludots.Core.Registry.StringIntRegistry();
            _store = new EntityCollectionStore(keys);
            _owner = _world.Create();
            _a = _world.Create();
            _b = _world.Create();
            _c = _world.Create();
            _keyId = _store.KeyRegistry.Register("test.selected");
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        private void Write(CollectionWriteOp op, params Entity[] entities)
        {
            CollectionWrite.Apply(_store, _owner, _keyId, op, entities);
        }

        private Entity[] Members()
        {
            if (!_store.TryGet(_owner, _keyId, out EntityCollectionHandle handle) || !_store.TryGetView(handle, out EntityCollectionView view))
            {
                return Array.Empty<Entity>();
            }

            var buffer = new Entity[view.Count];
            int count = _store.CopyEntities(_owner, _keyId, buffer);
            return buffer.Take(count).ToArray();
        }

        [Test]
        public void ReplaceAddSubtract_WriteSemantics()
        {
            Write(CollectionWriteOp.Replace, _a, _b);
            Assert.That(Members(), Is.EquivalentTo(new[] { _a, _b }), "replace writes the incoming set");

            Write(CollectionWriteOp.Add, _c);
            Assert.That(Members(), Is.EquivalentTo(new[] { _a, _b, _c }), "add unions with current members");

            Write(CollectionWriteOp.Subtract, _a);
            Assert.That(Members(), Is.EquivalentTo(new[] { _b, _c }), "subtract removes incoming from current");

            Write(CollectionWriteOp.Add, _a);
            Write(CollectionWriteOp.Add, _a);
            Assert.That(Members().Count(e => e == _a), Is.EqualTo(1), "add is distinct — no duplicates");
        }

        [Test]
        public void UnknownKey_FailsFast()
        {
            Assert.Throws<InvalidOperationException>(
                () => CollectionWrite.Apply(_store, _owner, 9999, CollectionWriteOp.Replace, new[] { _a }),
                "unregistered collection key ids fail closed");
        }

        [Test]
        public void DeadOwner_FailsFast()
        {
            Assert.Throws<InvalidOperationException>(
                () => CollectionWrite.Apply(_store, Entity.Null, _keyId, CollectionWriteOp.Replace, new[] { _a }),
                "a live owner (the writing rep) is required");
        }

        [Test]
        public void InvalidOp_FailsFast()
        {
            Assert.Throws<InvalidOperationException>(
                () => CollectionWrite.Apply(_store, _owner, _keyId, (CollectionWriteOp)7, new[] { _a }),
                "op must be replace(0)/add(1)/subtract(2)");
        }

        [Test]
        public void EmptyReplace_ClearsCollection()
        {
            Write(CollectionWriteOp.Replace, _a);
            Write(CollectionWriteOp.Replace);
            Assert.That(Members(), Is.Empty, "replace with an empty set clears the collection");
        }

        [Test]
        public void SubtractOnMissingCollection_IsNoOpWithoutCollection()
        {
            Write(CollectionWriteOp.Subtract, _a);
            Assert.That(Members(), Is.Empty, "subtract with no current collection leaves nothing");
        }

        [Test]
        public void Replace_EmptySetThenAdd_Rebuilds()
        {
            Write(CollectionWriteOp.Replace, _a);
            Write(CollectionWriteOp.Replace);
            Write(CollectionWriteOp.Add, _b);
            Assert.That(Members(), Is.EquivalentTo(new[] { _b }), "clear then add seeds from empty");
        }
    }
}
