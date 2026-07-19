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
    }
}
