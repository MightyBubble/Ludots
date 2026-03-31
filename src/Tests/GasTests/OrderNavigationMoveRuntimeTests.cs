using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Systems;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class OrderNavigationMoveRuntimeTests
    {
        private const int MoveToOrderTypeId = 101;

        [Test]
        public void NavOrderAgentBootstrapSystem_AddsNavPhysicsComponentsFromWorldPosition()
        {
            using var world = World.Create();
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");

            AttributeBuffer attributes = default;
            attributes.SetBase(moveSpeedId, 355f);

            Entity actor = world.Create(
                WorldPositionCm.FromCm(120, 340),
                attributes,
                OrderBuffer.CreateEmpty());
            ref var actorAttributes = ref world.Get<AttributeBuffer>(actor);
            actorAttributes.SetCurrent(moveSpeedId, 355f);

            var system = new NavOrderAgentBootstrapSystem(world);
            system.Update(0f);

            Assert.That(world.Has<NavAgent2D>(actor), Is.True);
            Assert.That(world.Has<Position2D>(actor), Is.True);
            Assert.That(world.Has<PreviousWorldPositionCm>(actor), Is.True);
            Assert.That(world.Has<PreviousPosition2D>(actor), Is.True);
            Assert.That(world.Has<Velocity2D>(actor), Is.True);
            Assert.That(world.Has<Mass2D>(actor), Is.True);
            Assert.That(world.Has<NavKinematics2D>(actor), Is.True);

            var position = world.Get<Position2D>(actor);
            Assert.That(position.Value, Is.EqualTo(Fix64Vec2.FromInt(120, 340)));

            var previousPosition = world.Get<PreviousPosition2D>(actor);
            Assert.That(previousPosition.Value, Is.EqualTo(Fix64Vec2.FromInt(120, 340)));

            var previousWorldPosition = world.Get<PreviousWorldPositionCm>(actor);
            Assert.That(previousWorldPosition.Value, Is.EqualTo(Fix64Vec2.FromInt(120, 340)));

            var kinematics = world.Get<NavKinematics2D>(actor);
            Assert.That(kinematics.MaxSpeedCmPerSec.ToFloat(), Is.EqualTo(355f).Within(0.01f));
        }

        [Test]
        public void NavOrderAgentBootstrapSystem_ProjectsMoveBlockedControlStateIntoZeroSpeed()
        {
            using var world = World.Create();
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");

            AttributeBuffer attributes = default;
            attributes.SetBase(moveSpeedId, 355f);

            Entity actor = world.Create(
                WorldPositionCm.FromCm(120, 340),
                attributes,
                GameplayControlState.CreateDefault(),
                OrderBuffer.CreateEmpty());
            ref var actorAttributes = ref world.Get<AttributeBuffer>(actor);
            actorAttributes.SetCurrent(moveSpeedId, 355f);
            ref var controlState = ref world.Get<GameplayControlState>(actor);
            controlState.MoveBlocked = 1;

            var system = new NavOrderAgentBootstrapSystem(world);
            system.Update(0f);

            var kinematics = world.Get<NavKinematics2D>(actor);
            Assert.That(kinematics.MaxSpeedCmPerSec.ToFloat(), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void MoveToWorldCmOrderSystem_NavAgents_WriteNavGoalInsteadOfSteppingWorldPosition()
        {
            using var world = World.Create();
            var registry = CreateMoveRegistry();

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new Position2D { Value = Fix64Vec2.Zero },
                new NavAgent2D(),
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(300),
                    MaxAccelCmPerSec2 = Fix64.FromInt(6000),
                    RadiusCm = Fix64.FromInt(40),
                    NeighborDistCm = Fix64.FromInt(400),
                    TimeHorizonSec = Fix64.FromInt(2),
                    MaxNeighbors = 16
                },
                OrderBuffer.CreateEmpty());

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(CreateMoveOrder(actor, 500, 0), priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, registry, MoveToOrderTypeId, defaultSpeedCmPerSec: 600f, stopRadiusCm: 40f);
            system.Update(0.1f);

            var worldPosition = world.Get<WorldPositionCm>(actor);
            Assert.That(worldPosition.Value, Is.EqualTo(Fix64Vec2.Zero));
            Assert.That(world.Has<NavGoal2D>(actor), Is.True);

            var goal = world.Get<NavGoal2D>(actor);
            Assert.That(goal.Kind, Is.EqualTo(NavGoalKind2D.Point));
            Assert.That(goal.TargetCm, Is.EqualTo(Fix64Vec2.FromInt(500, 0)));
            Assert.That(buffer.HasActive, Is.True);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_CompletesNavOrderWhenAlreadyInsideStopRadius()
        {
            using var world = World.Create();
            var registry = CreateMoveRegistry();

            Entity actor = world.Create(
                WorldPositionCm.FromCm(100, 0),
                new Position2D { Value = Fix64Vec2.FromInt(100, 0) },
                new NavAgent2D(),
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(300),
                    MaxAccelCmPerSec2 = Fix64.FromInt(6000),
                    RadiusCm = Fix64.FromInt(40),
                    NeighborDistCm = Fix64.FromInt(400),
                    TimeHorizonSec = Fix64.FromInt(2),
                    MaxNeighbors = 16
                },
                OrderBuffer.CreateEmpty(),
                new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = Fix64Vec2.FromInt(140, 0), RadiusCm = Fix64.FromInt(40) });

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(CreateMoveOrder(actor, 120, 0), priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, registry, MoveToOrderTypeId, defaultSpeedCmPerSec: 600f, stopRadiusCm: 40f);
            system.Update(0.1f);

            var goal = world.Get<NavGoal2D>(actor);
            Assert.That(goal.Kind, Is.EqualTo(NavGoalKind2D.None));
            Assert.That(buffer.HasActive, Is.False);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_ListRoute_AdvancesWaypointsWithoutCompletingEarly()
        {
            using var world = World.Create();
            var registry = CreateMoveRegistry();

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                OrderBuffer.CreateEmpty());

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(CreateRouteOrder(actor, (0, 0), (120, 0), (240, 80)), priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, registry, MoveToOrderTypeId, defaultSpeedCmPerSec: 600f, stopRadiusCm: 5f);
            system.Update(0.10f);

            Assert.That(buffer.HasActive, Is.True);
            Assert.That(buffer.ActiveOrder.Order.Args.Spatial.A0, Is.EqualTo(0));
            Assert.That(buffer.ActiveOrder.RuntimeInt0, Is.GreaterThanOrEqualTo(1));

            system.Update(0.10f);
            system.Update(0.10f);
            system.Update(0.10f);
            system.Update(0.10f);

            var finalPosition = world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            Assert.That(finalPosition.X, Is.EqualTo(240).Within(1));
            Assert.That(finalPosition.Y, Is.EqualTo(80).Within(1));
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_ListRouteNavAgents_EnableSmartStopSuppressionWhileRouteIsActive()
        {
            using var world = World.Create();
            var registry = CreateMoveRegistry();

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new Position2D { Value = Fix64Vec2.Zero },
                new NavAgent2D(),
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(300),
                    MaxAccelCmPerSec2 = Fix64.FromInt(6000),
                    RadiusCm = Fix64.FromInt(40),
                    NeighborDistCm = Fix64.FromInt(400),
                    TimeHorizonSec = Fix64.FromInt(2),
                    MaxNeighbors = 16
                },
                OrderBuffer.CreateEmpty());

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(CreateRouteOrder(actor, (0, 0), (120, 0), (240, 0)), priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, registry, MoveToOrderTypeId, defaultSpeedCmPerSec: 600f, stopRadiusCm: 5f);
            system.Update(0.1f);

            Assert.That(world.Get<NavAgent2D>(actor).SmartStopSuppressed, Is.EqualTo((byte)1));
            Assert.That(buffer.HasActive, Is.True);
        }

        [Test]
        public void MoveToWorldCmOrderSystem_MoveBlockedNavAgent_SkipsNavigationGoalAndKeepsOrderActive()
        {
            using var world = World.Create();
            var registry = CreateMoveRegistry();

            Entity actor = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new Position2D { Value = Fix64Vec2.Zero },
                new NavAgent2D(),
                new NavKinematics2D
                {
                    MaxSpeedCmPerSec = Fix64.FromInt(300),
                    MaxAccelCmPerSec2 = Fix64.FromInt(6000),
                    RadiusCm = Fix64.FromInt(40),
                    NeighborDistCm = Fix64.FromInt(400),
                    TimeHorizonSec = Fix64.FromInt(2),
                    MaxNeighbors = 16
                },
                Velocity2D.FromCmPerSec(120f, 0f),
                new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.FromInt(120, 0) },
                new ForceInput2D { Force = Fix64Vec2.FromInt(60, 0) },
                new GameplayControlState
                {
                    MoveBlocked = 1,
                    ActionBlocked = 0
                },
                OrderBuffer.CreateEmpty());

            ref var buffer = ref world.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(CreateMoveOrder(actor, 500, 0), priority: 60);

            var system = new MoveToWorldCmOrderSystem(world, registry, MoveToOrderTypeId, defaultSpeedCmPerSec: 600f, stopRadiusCm: 40f);
            system.Update(0.1f);

            var worldPosition = world.Get<WorldPositionCm>(actor);
            Assert.That(worldPosition.Value, Is.EqualTo(Fix64Vec2.Zero));
            Assert.That(world.Has<NavGoal2D>(actor), Is.False, "Move-blocked nav agents should not receive a new nav goal.");
            Assert.That(world.Get<Velocity2D>(actor).Linear, Is.EqualTo(Fix64Vec2.Zero));
            Assert.That(world.Get<NavDesiredVelocity2D>(actor).ValueCmPerSec, Is.EqualTo(Fix64Vec2.Zero));
            Assert.That(world.Get<ForceInput2D>(actor).Force, Is.EqualTo(Fix64Vec2.Zero));
            Assert.That(buffer.HasActive, Is.True);
        }

        private static OrderTypeRegistry CreateMoveRegistry()
        {
            var registry = new OrderTypeRegistry();
            registry.Register(new OrderTypeConfig
            {
                OrderTypeId = MoveToOrderTypeId,
                Label = "Move",
                Priority = 60,
                AllowQueuedMode = true,
                QueuedModeMaxSize = 8
            });
            return registry;
        }

        private static Order CreateMoveOrder(Entity actor, int targetXcm, int targetYcm)
        {
            return new Order
            {
                OrderId = 5,
                OrderTypeId = MoveToOrderTypeId,
                PlayerId = 1,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new System.Numerics.Vector3(targetXcm, 0f, targetYcm)
                    }
                }
            };
        }

        private static Order CreateRouteOrder(Entity actor, params (int xcm, int ycm)[] points)
        {
            var order = new Order
            {
                OrderId = 7,
                OrderTypeId = MoveToOrderTypeId,
                PlayerId = 1,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs()
            };

            order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            order.Args.Spatial.Mode = OrderCollectionMode.List;
            for (int i = 0; i < points.Length; i++)
            {
                order.Args.Spatial.AddPointWorldCm(points[i].xcm, 0, points[i].ycm);
            }

            return order;
        }
    }
}
