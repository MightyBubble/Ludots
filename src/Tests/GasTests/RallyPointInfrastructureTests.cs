using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class RallyPointInfrastructureTests
    {
        [Test]
        public void RallyBlackboardOps_PointEntityAndHexRoundTrip()
        {
            using World world = World.Create();
            Entity producer = world.Create(
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer());
            Entity rallyUnit = world.Create();

            RallyBlackboardOps.SetPoint(world, producer, new Vector3(1200f, 0f, 800f));
            Assert.That(RallyBlackboardOps.TryRead(world, producer, out RallyTargetSnapshot pointRally), Is.True);
            Assert.That(pointRally.Kind, Is.EqualTo(RallyTargetKind.Point));
            Assert.That(pointRally.WorldPositionCm.X, Is.EqualTo(1200f).Within(0.01f));

            RallyBlackboardOps.SetHex(world, producer, 3, -2);
            Assert.That(RallyBlackboardOps.TryRead(world, producer, out RallyTargetSnapshot hexRally), Is.True);
            Assert.That(hexRally.Kind, Is.EqualTo(RallyTargetKind.HexCell));
            Assert.That(hexRally.HexQ, Is.EqualTo(3));
            Assert.That(hexRally.HexR, Is.EqualTo(-2));
            Assert.That(hexRally.WorldPositionCm.Z, Is.Not.EqualTo(0f));

            RallyBlackboardOps.SetEntity(world, producer, rallyUnit);
            Assert.That(RallyBlackboardOps.TryRead(world, producer, out RallyTargetSnapshot entityRally), Is.True);
            Assert.That(entityRally.Kind, Is.EqualTo(RallyTargetKind.Entity));
            Assert.That(entityRally.TargetEntity, Is.EqualTo(rallyUnit));
        }

        [Test]
        public void SetRallyPointOrderSystem_CommitsPointRallyAndCompletes()
        {
            using World world = World.Create();
            const int setRallyPointOrderTypeId = 106;
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "setRallyPoint",
                OrderTypeId = setRallyPointOrderTypeId,
            });

            Entity producer = world.Create(OrderBuffer.CreateEmpty(), new BlackboardIntBuffer(), new BlackboardSpatialBuffer(), new BlackboardEntityBuffer());
            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(producer);
            buffer.SetActiveDirect(new Order
            {
                OrderTypeId = setRallyPointOrderTypeId,
                Target = Entity.Null,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(500f, 0f, 700f),
                    },
                },
            }, priority: 40);

            var system = new SetRallyPointOrderSystem(world, orderTypes, setRallyPointOrderTypeId);
            system.Update(default);

            Assert.That(buffer.HasActive, Is.False);
            Assert.That(RallyBlackboardOps.TryRead(world, producer, out RallyTargetSnapshot rally), Is.True);
            Assert.That(rally.Kind, Is.EqualTo(RallyTargetKind.Point));
            Assert.That(rally.WorldPositionCm.X, Is.EqualTo(500f).Within(0.01f));
        }

        [Test]
        public void SubmitOrderFromRallyHandler_SubmitsMoveOrderForPointRally()
        {
            using World world = World.Create();
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 101, AllowQueuedMode = true });
            orderTypes.Register(new OrderTypeConfig { Key = "castAbility", OrderTypeId = 100, IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex, AllowQueuedMode = true });

            Entity producer = world.Create(new BlackboardIntBuffer(), new BlackboardSpatialBuffer(), new BlackboardEntityBuffer());
            Entity spawned = world.Create(OrderBuffer.CreateEmpty());
            RallyBlackboardOps.SetPoint(world, producer, new Vector3(900f, 0f, 1200f));

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            var runtime = new BuiltinHandlerExecutionContext
            {
                OrderTypeRegistry = orderTypes,
                CurrentStep = 1,
                StepRateHz = 30,
            };

            var template = new EffectTemplateData
            {
                SubmitOrderFromRally = new SubmitOrderFromRallyDescriptor
                {
                    RallyHolderSlot = RelationEntitySlot.Source,
                    OrderActorSlot = RelationEntitySlot.Target,
                    PointMoveOrderTypeKey = "moveTo",
                    EntityOrderTypeKey = "castAbility",
                    EntityOrderIntArg0 = 1,
                    SubmitMode = OrderSubmitMode.Immediate,
                },
            };

            var context = new EffectContext
            {
                Source = producer,
                Target = spawned,
            };

            var mergedParams = new EffectConfigParams();
            registry.Invoke(
                BuiltinHandlerId.SubmitOrderFromRally,
                world,
                Entity.Null,
                ref context,
                in mergedParams,
                in template,
                runtime);

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(spawned);
            Assert.That(buffer.HasActive, Is.True);
            Assert.That(buffer.ActiveOrder.Order.OrderTypeId, Is.EqualTo(101));
            Assert.That(buffer.ActiveOrder.Order.Args.Spatial.WorldCm.X, Is.EqualTo(900f).Within(0.01f));
        }
    }
}
