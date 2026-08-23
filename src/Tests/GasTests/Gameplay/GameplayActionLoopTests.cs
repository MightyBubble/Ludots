using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.ActionLoops;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Teams;
using NUnit.Framework;
using AuthoringRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Tests.GAS.Gameplay;

[TestFixture]
[NonParallelizable]
public sealed class GameplayActionLoopTests
{
    [Test]
    public void ResourceTransport_RoutesThroughOrderQueue_AndCreditsOnlyAtSink()
    {
        using World world = World.Create();
        int resourceAttributeId = AttributeRegistry.Register("ActionLoop.Test.Resource");
        OrderTypeRegistry orderTypes = CreateOrderTypes();
        var admissionResults = new OrderAdmissionResultBuffer(64, 64);
        var orders = new OrderQueue(64, admissionResults);
        var system = new ResourceTransportSystem(world, orders, orderTypes, OpenGate.Instance, new Ludots.Core.Gameplay.GAS.TagOps(new DirtyEntityQueue(8), new TagRuleRegistry()));

        AttributeBuffer sinkAttributes = default;
        sinkAttributes.SetCurrent(resourceAttributeId, 40f);
        Entity sink = world.Create(
            new ResourceSinkProfile
            {
                ResourceAttributeId = resourceAttributeId,
                DockOffsetXCm = 600,
                DockOffsetYCm = -200,
            },
            sinkAttributes,
            new DirtyFlags(),
            WorldPositionCm.FromCm(0, 0),
            new PlayerOwner { PlayerId = 1 });
        Entity source = world.Create(
            new ResourceSourceProfile { ResourceAttributeId = resourceAttributeId },
            WorldPositionCm.FromCm(1_000, 0));
        Entity actor = world.Create(
            new ResourceTransportProfile
            {
                GatherOrderTypeId = 172,
                MoveOrderTypeId = 101,
                ResourceAttributeId = resourceAttributeId,
                CargoAmount = 20f,
                LoadDurationTicks = 1,
                ArrivalRadiusCm = 100,
            },
            new ResourceTransportState(),
            ActiveOrder(orderId: 7, orderTypeId: 172, target: source),
            WorldPositionCm.FromCm(0, 0),
            new PlayerOwner { PlayerId = 1 });

        system.Update(1f / 30f);
        Assert.That(orders.TryDequeue(out Order outbound), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(outbound.Actor, Is.EqualTo(actor));
            Assert.That(outbound.OrderTypeId, Is.EqualTo(101));
            Assert.That(world.Get<AttributeBuffer>(sink).GetCurrent(resourceAttributeId), Is.EqualTo(40f));
        });

        world.Set(actor, ActiveOrder(outbound.OrderId, outbound.OrderTypeId, outbound.Target));
        system.Update(1f / 30f);
        world.Set(actor, new OrderBuffer { ActiveIndex = -1 });
        world.Set(actor, WorldPositionCm.FromCm(1_000, 0));
        system.Update(1f / 30f);
        system.Update(1f / 30f);

        Assert.That(orders.TryDequeue(out Order inbound), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(inbound.Args.Spatial.WorldCm.X, Is.EqualTo(600f));
            Assert.That(inbound.Args.Spatial.WorldCm.Z, Is.EqualTo(-200f));
            Assert.That(world.Get<AttributeBuffer>(sink).GetCurrent(resourceAttributeId), Is.EqualTo(40f));
        });

        world.Set(actor, ActiveOrder(inbound.OrderId, inbound.OrderTypeId, inbound.Target));
        system.Update(1f / 30f);
        world.Set(actor, new OrderBuffer { ActiveIndex = -1 });
        world.Set(actor, WorldPositionCm.FromCm(0, 0));
        InvalidOperationException outsideDock = Assert.Throws<InvalidOperationException>(
            () => system.Update(1f / 30f))!;
        Assert.Multiple(() =>
        {
            Assert.That(outsideDock.Message, Does.Contain("resource sink"));
            Assert.That(world.Get<AttributeBuffer>(sink).GetCurrent(resourceAttributeId), Is.EqualTo(40f));
        });

        world.Set(actor, WorldPositionCm.FromCm(600, -200));
        system.Update(1f / 30f);

        Assert.Multiple(() =>
        {
            Assert.That(world.Get<AttributeBuffer>(sink).GetCurrent(resourceAttributeId), Is.EqualTo(60f));
            Assert.That(world.Get<ResourceTransportState>(actor).Phase, Is.EqualTo(ResourceTransportPhase.Idle));
        });
    }

    [Test]
    public void DirectAttack_UsesRelationshipPolicy_AndPublishesConfiguredEffect()
    {
        TeamRelationshipSnapshot relationships = TeamManager.CaptureSnapshot();
        try
        {
            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            using World world = World.Create();
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var admissionResults = new OrderAdmissionResultBuffer(64, 64);
        var orders = new OrderQueue(64, admissionResults);
            var effects = new EffectRequestQueue();
            var system = new DirectAttackSystem(world, orders, orderTypes, effects, OpenGate.Instance);

            Entity target = world.Create(
                new Team { Id = 2 },
                new AttributeBuffer(),
                WorldPositionCm.FromCm(500, 0));
            Entity actor = world.Create(
                new DirectAttackProfile
                {
                    AttackOrderTypeId = 102,
                    MoveOrderTypeId = 101,
                    EffectTemplateId = 77,
                    TargetRelation = RelationshipFilter.Hostile,
                    RangeCm = 650,
                    CooldownTicks = 30,
                },
                new DirectAttackState(),
                ActiveOrder(orderId: 8, orderTypeId: 102, target),
                new Team { Id = 1 },
                new PlayerOwner { PlayerId = 1 },
                WorldPositionCm.FromCm(0, 0));

            system.Update(1f / 30f);
            system.Update(1f / 30f);

            Assert.Multiple(() =>
            {
                Assert.That(effects.Count, Is.EqualTo(1));
                Assert.That(effects[0].Source, Is.EqualTo(actor));
                Assert.That(effects[0].Target, Is.EqualTo(target));
                Assert.That(effects[0].TemplateId, Is.EqualTo(77));
                Assert.That(world.Get<DirectAttackState>(actor).CooldownTicks, Is.EqualTo(30));
            });
        }
        finally
        {
            TeamManager.RestoreSnapshot(relationships);
        }
    }

    [Test]
    public void DirectAttack_CompletesItsPursuitOrder_AsSoonAsTheTargetEntersRange()
    {
        TeamRelationshipSnapshot relationships = TeamManager.CaptureSnapshot();
        try
        {
            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            using World world = World.Create();
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var admissionResults = new OrderAdmissionResultBuffer(64, 64);
            var orders = new OrderQueue(64, admissionResults);
            var effects = new EffectRequestQueue();
            var system = new DirectAttackSystem(world, orders, orderTypes, effects, OpenGate.Instance);

            Entity target = world.Create(
                new Team { Id = 2 },
                new AttributeBuffer(),
                WorldPositionCm.FromCm(1_000, 0));
            Entity actor = world.Create(
                new DirectAttackProfile
                {
                    AttackOrderTypeId = 102,
                    MoveOrderTypeId = 101,
                    EffectTemplateId = 77,
                    TargetRelation = RelationshipFilter.Hostile,
                    RangeCm = 650,
                    CooldownTicks = 30,
                },
                new DirectAttackState
                {
                    Phase = DirectAttackPhase.Pursuing,
                    Target = target,
                    ExpectedMoveOrderId = 9,
                    ExpectedMoveObserved = 1,
                },
                ActiveOrder(orderId: 9, orderTypeId: 101, target),
                new Team { Id = 1 },
                new PlayerOwner { PlayerId = 1 },
                WorldPositionCm.FromCm(400, 0));

            system.Update(1f / 30f);

            Assert.Multiple(() =>
            {
                Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
                Assert.That(world.Get<DirectAttackState>(actor).Phase, Is.EqualTo(DirectAttackPhase.Engaging));
                Assert.That(effects.Count, Is.Zero);
            });

            system.Update(1f / 30f);
            Assert.That(effects.Count, Is.EqualTo(1));
        }
        finally
        {
            TeamManager.RestoreSnapshot(relationships);
        }
    }

    [Test]
    public void DirectAttack_AttackOrderMovesIntoRangeWithoutReachingTheTargetCenter()
    {
        TeamRelationshipSnapshot relationships = TeamManager.CaptureSnapshot();
        try
        {
            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            using World world = World.Create();
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var admissionResults = new OrderAdmissionResultBuffer(64, 64);
            var orders = new OrderQueue(64, admissionResults);
            var effects = new EffectRequestQueue();
            var attackSystem = new DirectAttackSystem(world, orders, orderTypes, effects, OpenGate.Instance);
            var moveSystem = new MoveToWorldCmOrderSystem(world, orderTypes, moveToOrderTypeId: 101);

            const int attackRangeCm = 650;
            const int targetXCm = 2_000;
            int moveSpeedAttributeId = AttributeRegistry.Register("MoveSpeed");
            AttributeBuffer actorAttributes = default;
            actorAttributes.SetCurrent(moveSpeedAttributeId, 800f);
            Entity target = world.Create(
                new Team { Id = 2 },
                new AttributeBuffer(),
                WorldPositionCm.FromCm(targetXCm, 0));
            Entity actor = world.Create(
                new DirectAttackProfile
                {
                    AttackOrderTypeId = 102,
                    MoveOrderTypeId = 101,
                    EffectTemplateId = 77,
                    TargetRelation = RelationshipFilter.Hostile,
                    RangeCm = attackRangeCm,
                    CooldownTicks = 30,
                },
                new DirectAttackState(),
                ActiveOrder(orderId: 8, orderTypeId: 102, target),
                new Team { Id = 1 },
                new PlayerOwner { PlayerId = 1 },
                WorldPositionCm.FromCm(0, 0),
                actorAttributes);

            attackSystem.Update(1f / 30f);
            Assert.That(orders.TryDequeue(out Order pursuit), Is.True);
            world.Set(actor, new OrderBuffer
            {
                ActiveIndex = 0,
                ActiveOrder = new QueuedOrder { Order = pursuit },
            });

            for (int frame = 0; frame < 120 && effects.Count == 0; frame++)
            {
                moveSystem.Update(1f / 30f);
                attackSystem.Update(1f / 30f);
            }

            var finalPosition = world.Get<WorldPositionCm>(actor).ToWorldCmInt2();
            int finalDistanceCm = Math.Abs(targetXCm - finalPosition.X);
            Assert.Multiple(() =>
            {
                Assert.That(effects.Count, Is.EqualTo(1));
                Assert.That(finalDistanceCm, Is.LessThanOrEqualTo(attackRangeCm));
                Assert.That(finalDistanceCm, Is.GreaterThanOrEqualTo(attackRangeCm - 30));
                Assert.That(finalPosition.X, Is.Not.EqualTo(targetXCm));
            });
        }
        finally
        {
            TeamManager.RestoreSnapshot(relationships);
        }
    }

    [Test]
    public void DirectAttack_ExplicitEngagementPointRoutesPursuitAwayFromTargetCenter()
    {
        TeamRelationshipSnapshot relationships = TeamManager.CaptureSnapshot();
        try
        {
            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            using World world = World.Create();
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var admissionResults = new OrderAdmissionResultBuffer(64, 64);
            var orders = new OrderQueue(64, admissionResults);
            var effects = new EffectRequestQueue();
            var attackSystem = new DirectAttackSystem(world, orders, orderTypes, effects, OpenGate.Instance);

            Entity target = world.Create(
                new Team { Id = 2 },
                new AttributeBuffer(),
                WorldPositionCm.FromCm(1_000, 0));
            Entity actorA = CreateDirectAttackActor(
                world,
                target,
                orderId: 80,
                actorPosition: WorldPositionCm.FromCm(0, -120),
                engagementPoint: new Vector3(420f, 0f, -120f));
            Entity actorB = CreateDirectAttackActor(
                world,
                target,
                orderId: 81,
                actorPosition: WorldPositionCm.FromCm(0, 120),
                engagementPoint: new Vector3(420f, 0f, 120f));

            attackSystem.Update(1f / 30f);

            Assert.That(orders.TryDequeue(out Order pursuitA), Is.True);
            Assert.That(orders.TryDequeue(out Order pursuitB), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(pursuitA.Target, Is.EqualTo(target));
                Assert.That(pursuitB.Target, Is.EqualTo(target));
                Assert.That(new[] { pursuitA.Actor, pursuitB.Actor }, Is.EquivalentTo(new[] { actorA, actorB }));
                Assert.That(
                    new[] { pursuitA.Args.Spatial.WorldCm, pursuitB.Args.Spatial.WorldCm },
                    Is.EquivalentTo(new[]
                    {
                        new Vector3(420f, 0f, -120f),
                        new Vector3(420f, 0f, 120f),
                    }));
                Assert.That(pursuitA.Args.Spatial.WorldCm, Is.Not.EqualTo(pursuitB.Args.Spatial.WorldCm));
                Assert.That(effects.Count, Is.Zero);
            });
        }
        finally
        {
            TeamManager.RestoreSnapshot(relationships);
        }
    }

    [Test]
    public void DirectAttack_ExplicitEngagementPointBeginsEngagingWhenTargetAlreadyReachable()
    {
        TeamRelationshipSnapshot relationships = TeamManager.CaptureSnapshot();
        try
        {
            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            using World world = World.Create();
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var admissionResults = new OrderAdmissionResultBuffer(64, 64);
            var orders = new OrderQueue(64, admissionResults);
            var effects = new EffectRequestQueue();
            var attackSystem = new DirectAttackSystem(world, orders, orderTypes, effects, OpenGate.Instance);

            Entity target = world.Create(
                new Team { Id = 2 },
                new AttributeBuffer(),
                WorldPositionCm.FromCm(1_000, 0));
            Entity actor = CreateDirectAttackActor(
                world,
                target,
                orderId: 82,
                actorPosition: WorldPositionCm.FromCm(450, 0),
                engagementPoint: new Vector3(500f, 0f, 120f));

            attackSystem.Update(1f / 30f);

            Assert.That(orders.TryDequeue(out _), Is.False);
            DirectAttackState state = world.Get<DirectAttackState>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(effects.Count, Is.Zero);
                Assert.That(state.Phase, Is.EqualTo(DirectAttackPhase.Engaging));
                Assert.That(state.HasExplicitEngagementPoint, Is.Zero);
            });
        }
        finally
        {
            TeamManager.RestoreSnapshot(relationships);
        }
    }

    [Test]
    public void ComponentRegistry_AuthorsSemanticActionLoopProfiles()
    {
        using World world = World.Create();
        int resourceAttributeId = AttributeRegistry.Register("ActionLoop.Authoring.Resource");
        int effectTemplateId = EffectTemplateIdRegistry.Register("Effect.ActionLoop.Authoring.Damage");
        Entity carrier = world.Create();
        Entity sink = world.Create();
        Entity attacker = world.Create();

        AuthoringRegistry.Apply(
            carrier,
            "ResourceTransportProfile",
            JsonNode.Parse("""
                {
                  "GatherOrderTypeId": 172,
                  "MoveOrderTypeId": 101,
                  "ResourceAttribute": "ActionLoop.Authoring.Resource",
                  "CargoAmount": 20,
                  "LoadDurationTicks": 60,
                  "ArrivalRadiusCm": 100
                }
                """)!);
        AuthoringRegistry.Apply(
            sink,
            "ResourceSinkProfile",
            JsonNode.Parse("""
                {
                  "ResourceAttribute": "ActionLoop.Authoring.Resource",
                  "DockOffsetXCm": 600,
                  "DockOffsetYCm": -200
                }
                """)!);
        AuthoringRegistry.Apply(
            attacker,
            "DirectAttackProfile",
            JsonNode.Parse("""
                {
                  "AttackOrderTypeId": 102,
                  "MoveOrderTypeId": 101,
                  "EffectTemplate": "Effect.ActionLoop.Authoring.Damage",
                  "TargetRelation": "Hostile",
                  "RangeCm": 650,
                  "CooldownTicks": 30
                }
                """)!);

        ResourceTransportProfile resource = world.Get<ResourceTransportProfile>(carrier);
        ResourceSinkProfile resourceSink = world.Get<ResourceSinkProfile>(sink);
        DirectAttackProfile attack = world.Get<DirectAttackProfile>(attacker);
        Assert.Multiple(() =>
        {
            Assert.That(resource.ResourceAttributeId, Is.EqualTo(resourceAttributeId));
            Assert.That(resource.CargoAmount, Is.EqualTo(20f));
            Assert.That(resourceSink.ResourceAttributeId, Is.EqualTo(resourceAttributeId));
            Assert.That(resourceSink.DockOffsetXCm, Is.EqualTo(600));
            Assert.That(resourceSink.DockOffsetYCm, Is.EqualTo(-200));
            Assert.That(attack.EffectTemplateId, Is.EqualTo(effectTemplateId));
            Assert.That(attack.TargetRelation, Is.EqualTo(RelationshipFilter.Hostile));
        });
    }

    [TestCase("{ \"ResourceAttribute\": \"ActionLoop.Authoring.RequiredDock\", \"DockOffsetYCm\": 0 }")]
    [TestCase("{ \"ResourceAttribute\": \"ActionLoop.Authoring.RequiredDock\", \"DockOffsetXCm\": 0 }")]
    [TestCase("{ \"ResourceAttribute\": \"ActionLoop.Authoring.RequiredDock\", \"DockOffsetXCm\": 0.5, \"DockOffsetYCm\": 0 }")]
    public void ResourceSinkProfile_RequiresExplicitIntegerDockOffset(string json)
    {
        using World world = World.Create();
        Entity sink = world.Create();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AuthoringRegistry.Apply(sink, "ResourceSinkProfile", JsonNode.Parse(json)!))!;

        Assert.That(error.Message, Does.Contain(nameof(ResourceSinkProfile)));
    }

    [Test]
    public void StableIdleTicks_AreAllocationFree()
    {
        using World world = World.Create();
        int resourceAttributeId = AttributeRegistry.Register("ActionLoop.Allocation.Resource");
        OrderTypeRegistry orderTypes = CreateOrderTypes();
        var admissionResults = new OrderAdmissionResultBuffer(64, 64);
        var orders = new OrderQueue(64, admissionResults);
        var effects = new EffectRequestQueue();
        var resourceSystem = new ResourceTransportSystem(world, orders, orderTypes, OpenGate.Instance, new Ludots.Core.Gameplay.GAS.TagOps(new DirtyEntityQueue(8), new TagRuleRegistry()));
        var attackSystem = new DirectAttackSystem(world, orders, orderTypes, effects, OpenGate.Instance);
        _ = world.Create(
            new ResourceTransportProfile
            {
                GatherOrderTypeId = 172,
                MoveOrderTypeId = 101,
                ResourceAttributeId = resourceAttributeId,
                CargoAmount = 1f,
                LoadDurationTicks = 1,
                ArrivalRadiusCm = 1,
            },
            new ResourceTransportState(),
            new OrderBuffer { ActiveIndex = -1 },
            WorldPositionCm.FromCm(0, 0),
            new PlayerOwner { PlayerId = 1 });
        _ = world.Create(
            new DirectAttackProfile
            {
                AttackOrderTypeId = 102,
                MoveOrderTypeId = 101,
                EffectTemplateId = 1,
                TargetRelation = RelationshipFilter.Hostile,
                RangeCm = 1,
                CooldownTicks = 1,
            },
            new DirectAttackState(),
            new OrderBuffer { ActiveIndex = -1 },
            new Team { Id = 1 },
            new PlayerOwner { PlayerId = 1 },
            WorldPositionCm.FromCm(0, 0));

        for (int i = 0; i < 16; i++)
        {
            resourceSystem.Update(1f / 30f);
            attackSystem.Update(1f / 30f);
        }

        long allocated = MeasureAllocations(resourceSystem, attackSystem);
        allocated = Math.Min(allocated, MeasureAllocations(resourceSystem, attackSystem));
        Assert.That(allocated, Is.Zero);
    }

    private static long MeasureAllocations(
        ResourceTransportSystem resourceSystem,
        DirectAttackSystem attackSystem)
    {
        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            resourceSystem.Update(1f / 30f);
            attackSystem.Update(1f / 30f);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static Entity CreateDirectAttackActor(
        World world,
        Entity target,
        int orderId,
        WorldPositionCm actorPosition,
        Vector3 engagementPoint)
    {
        return world.Create(
            new DirectAttackProfile
            {
                AttackOrderTypeId = 102,
                MoveOrderTypeId = 101,
                EffectTemplateId = 77,
                TargetRelation = RelationshipFilter.Hostile,
                RangeCm = 650,
                CooldownTicks = 30,
            },
            new DirectAttackState(),
            ActiveOrder(
                orderId,
                102,
                target,
                OrderArgs.CreateSingleWorldCm(engagementPoint)),
            new Team { Id = 1 },
            new PlayerOwner { PlayerId = 1 },
            actorPosition);
    }

    private static OrderBuffer ActiveOrder(
        int orderId,
        int orderTypeId,
        Entity target,
        OrderArgs args = default) => new()
    {
        ActiveIndex = 0,
        ActiveOrder = new QueuedOrder
        {
            Order = new Order
            {
                OrderId = orderId,
                OrderTypeId = orderTypeId,
                PlayerId = 1,
                Target = target,
                Args = args,
            },
        },
    };

    private static OrderTypeRegistry CreateOrderTypes()
    {
        var registry = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
        registry.Register(CreateOrderType("moveTo", 101));
        registry.Register(CreateOrderType("attackTarget", 102));
        registry.Register(CreateOrderType("gather", 172));
        return registry;
    }

    private static OrderTypeConfig CreateOrderType(string key, int id) => new()
    {
        Key = key,
        OrderTypeId = id,
        MaxQueueSize = 1,
        SameTypePolicy = SameTypePolicy.Replace,
        QueueFullPolicy = QueueFullPolicy.RejectNew,
        Priority = 100,
        BufferWindowMs = 0,
        PendingBufferWindowMs = 0,
        CanInterruptSelf = true,
        QueuedModeMaxSize = 1,
        AllowQueuedMode = false,
        ClearQueueOnActivate = true,
        SpatialBlackboardKey = -1,
        EntityBlackboardKey = -1,
        IntArg0BlackboardKey = -1,
    };

    private sealed class OpenGate : IGameplayActionLoopGate
    {
        public static OpenGate Instance { get; } = new();

        public bool CanAdvanceGameplay => true;
    }
}
