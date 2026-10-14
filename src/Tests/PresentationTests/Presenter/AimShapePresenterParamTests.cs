using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Tests.TestCommon;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class AimShapePresenterParamTests
    {
        [Test]
        public void TwoCones_ReadDifferentHalfAnglesFromTheSameBlackboardBinding()
        {
            using var world = World.Create();
            int key = OrderBlackboardKeyRegistry.Resolve("aim.shape.param");
            var wide = default(BlackboardFloatBuffer);
            wide.Set(key, 40f);
            var narrow = default(BlackboardFloatBuffer);
            narrow.Set(key, 55f);
            Entity ownerWide = world.Create(wide);
            Entity ownerNarrow = world.Create(narrow);

            var instances = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("aim.cone", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = WellKnownPresenterParamKeys.OverlayAngle,
                        Value = ValueRef.FromOwnerBlackboardFloat(key),
                    },
                ],
            });
            instances.BindDefinitions(definitions);
            PresenterDefinition definition = definitions.Get(defId);
            Entity widePresenter = instances.Create(
                defId, ownerWide, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 801, Entity.Null, definition);
            Entity narrowPresenter = instances.Create(
                defId, ownerNarrow, 10, PresentationAnchorKind.Entity, Vector3.Zero, stableId: 802, Entity.Null, definition);
            world.Add(widePresenter, new PresenterBootstrapPending());
            world.Add(narrowPresenter, new PresenterBootstrapPending());

            using var system = new PresenterBehaviorSystem(
                world,
                instances,
                definitions,
                new PresentationEventStream(PresentationTestConstants.EventStreamCapacity),
                new PresentationOwnerChangeBuffer(8),
                new SoundRequestBuffer());
            system.Update(0.016f);

            Assert.That(instances.ResolveFloat(widePresenter, WellKnownPresenterParamKeys.OverlayAngle), Is.EqualTo(40f));
            Assert.That(instances.ResolveFloat(narrowPresenter, WellKnownPresenterParamKeys.OverlayAngle), Is.EqualTo(55f));
        }
    }
}
