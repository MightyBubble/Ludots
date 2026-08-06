using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace GasTests
{
    public class RelationTests
    {
        private World _world;
        private Entity _parent;
        private Entity _child;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _parent = _world.Create();
            _child = _world.Create();
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        [Test]
        public void TestRelationOps_SetAndRemoveParent()
        {
            RelationOps.SetParent(_world, _child, _parent);
            That(_world.Has<ChildOf>(_child), Is.True);
            That(_world.Has<ChildrenBuffer>(_parent), Is.True);

            ref var children = ref _world.Get<ChildrenBuffer>(_parent);
            That(children.Count, Is.EqualTo(1));
            That(children.Contains(in _child), Is.True);

            RelationOps.RemoveParent(_world, _child);
            That(_world.Has<ChildOf>(_child), Is.False);
            ref var after = ref _world.Get<ChildrenBuffer>(_parent);
            That(after.Count, Is.EqualTo(0));
        }

        [Test]
        public void SetParent_WhenDestinationIsFull_DoesNotDetachCurrentParent()
        {
            RelationOps.SetParent(_world, _child, _parent);
            ChildrenBuffer fullChildren = default;
            for (int i = 0; i < GasConstants.MAX_CHILDREN_BUFFER_CAPACITY; i++)
            {
                Entity existingChild = _world.Create();
                That(fullChildren.Add(in existingChild), Is.True);
            }
            Entity fullParent = _world.Create(fullChildren);

            InvalidOperationException error = Throws<InvalidOperationException>(
                () => RelationOps.SetParent(_world, _child, fullParent))!;

            That(error.Message, Does.StartWith(RelationOps.ChildrenCapacityExceededError));
            That(_world.Get<ChildOf>(_child).Parent, Is.EqualTo(_parent));
            That(_world.Get<ChildrenBuffer>(_parent).Contains(in _child), Is.True);
            That(_world.Get<ChildrenBuffer>(fullParent).Count, Is.EqualTo(GasConstants.MAX_CHILDREN_BUFFER_CAPACITY));
        }
    }
}
