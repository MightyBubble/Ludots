using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Performers;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PerformerParamBlackboardTests
    {
        private World _world = null!;

        [SetUp]
        public void SetUp() => _world = World.Create();

        [TearDown]
        public void TearDown() => World.Destroy(_world);

        [Test]
        public void PerformerParams_SettersStoreValuesAcrossAllLanes()
        {
            var entity = _world.Create(
                new PerformerFloatParams(),
                new PerformerIntParams(),
                new PerformerVectorParams());

            ref var fp = ref _world.Get<PerformerFloatParams>(entity);
            fp.Set(10, 1.25f);
            ref var ip = ref _world.Get<PerformerIntParams>(entity);
            ip.Set(20, 7);
            ref var vp = ref _world.Get<PerformerVectorParams>(entity);
            vp.Set(30, new Vector4(1f, 2f, 3f, 4f));

            Assert.Multiple(() =>
            {
                Assert.That(_world.Get<PerformerFloatParams>(entity).TryGet(10, out float fv), Is.True);
                Assert.That(fv, Is.EqualTo(1.25f));
                Assert.That(_world.Get<PerformerIntParams>(entity).TryGet(20, out int iv), Is.True);
                Assert.That(iv, Is.EqualTo(7));
                Assert.That(_world.Get<PerformerVectorParams>(entity).TryGet(30, out Vector4 vv), Is.True);
                Assert.That(vv, Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
            });
        }

        [Test]
        public void PerformerParamResolver_ResolveFloatWalksParentChainAndPrefersNearestOverride()
        {
            var parent = _world.Create(
                new PerformerFloatParams(),
                new PerformerFloatDefaults(),
                new PerformerParent { Parent = Entity.Null });
            ref var parentFp = ref _world.Get<PerformerFloatParams>(parent);
            parentFp.Set(100, 1.5f);

            var child = _world.Create(
                new PerformerFloatParams(),
                new PerformerFloatDefaults(),
                new PerformerParent { Parent = parent });
            ref var childFp = ref _world.Get<PerformerFloatParams>(child);
            childFp.Set(100, 2.5f);

            var grandchild = _world.Create(
                new PerformerFloatParams(),
                new PerformerFloatDefaults(),
                new PerformerParent { Parent = child });

            Assert.Multiple(() =>
            {
                Assert.That(PerformerParamResolver.ResolveFloat(_world, grandchild, 100, -1f), Is.EqualTo(2.5f));
                Assert.That(PerformerParamResolver.ResolveFloat(_world, grandchild, 999, 9f), Is.EqualTo(9f));
            });
        }

        [Test]
        public void PerformerParamResolver_ResolvePrefersOverrideBeforeDefaultBeforeParent()
        {
            var parent = _world.Create(
                new PerformerIntParams(),
                new PerformerIntDefaults(),
                new PerformerParent { Parent = Entity.Null });

            var child = _world.Create(
                new PerformerIntParams(),
                new PerformerIntDefaults(),
                new PerformerParent { Parent = parent });
            ref var childDefaults = ref _world.Get<PerformerIntDefaults>(child);
            childDefaults.Set(100, 2);

            Assert.That(PerformerParamResolver.ResolveInt(_world, child, 100, -1), Is.EqualTo(2),
                "Child default should be found.");

            ref var childOverrides = ref _world.Get<PerformerIntParams>(child);
            childOverrides.Set(100, 3);
            Assert.That(PerformerParamResolver.ResolveInt(_world, child, 100, -1), Is.EqualTo(3),
                "Override should shadow default.");
        }

        [Test]
        public void PerformerParamResolver_ParentOverrideShadowsChildDefault()
        {
            var parent = _world.Create(
                new PerformerIntParams(),
                new PerformerIntDefaults(),
                new PerformerParent { Parent = Entity.Null });
            ref var parentOverrides = ref _world.Get<PerformerIntParams>(parent);
            parentOverrides.Set(100, 7);

            var child = _world.Create(
                new PerformerIntParams(),
                new PerformerIntDefaults(),
                new PerformerParent { Parent = parent });
            ref var childDefaults = ref _world.Get<PerformerIntDefaults>(child);
            childDefaults.Set(100, 2);

            Assert.That(
                PerformerParamResolver.ResolveInt(_world, child, 100, -1),
                Is.EqualTo(2),
                "Child default should be checked before walking to parent.");
        }
    }
}