using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace GasTests
{
    public class BlackboardTests
    {
        private World _world;
        private Entity _entity;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _entity = _world.Create(new BlackboardIntBuffer(), new BlackboardFloatBuffer());
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        [Test]
        public void TestBlackboardInt_SetGet()
        {
            ref var bb = ref _world.Get<BlackboardIntBuffer>(_entity);
            bb.Set(10, 123);
            That(bb.TryGet(10, out var v), Is.True);
            That(v, Is.EqualTo(123));
            bb.Set(10, 456);
            That(bb.TryGet(10, out v), Is.True);
            That(v, Is.EqualTo(456));
        }

        [Test]
        public void TestBlackboardFloat_SetGet()
        {
            ref var bb = ref _world.Get<BlackboardFloatBuffer>(_entity);
            bb.Set(20, 1.5f);
            That(bb.TryGet(20, out var v), Is.True);
            That(v, Is.EqualTo(1.5f));
        }
    }
}
