using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using NUnit.Framework;
using CombatStanceBehaviorMod.Components;
using CombatStanceBehaviorMod.Runtime;
using CombatStanceBehaviorMod.Systems;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class CombatStanceBehaviorTests
{
    [Test]
    public void SetCombatStance_WritesStateAndCompletesOrder()
    {
        using var fixture = StanceFixture.Create();
        Entity actor = fixture.CreateActor(0, 0, CombatStances.HoldFire);
        fixture.Activate(actor, fixture.SetCombatStanceOrderTypeId, (ref Order order) =>
        {
            order.Args.I0 = CombatStances.AttackAnything;
            order.Args.I1 = 900;
            order.Args.I2 = 30;
        });

        fixture.System.Update(1f / 60f);

        Assert.That(fixture.World.Has<CombatStanceState>(actor), Is.True);
        var state = fixture.World.Get<CombatStanceState>(actor);
        Assert.That(state.Stance, Is.EqualTo(CombatStances.AttackAnything));
        Assert.That(state.LeashRadiusCm, Is.EqualTo(900));
        Assert.That(fixture.World.Get<OrderBuffer>(actor).HasActive, Is.False);
        Assert.That(fixture.Orders.Count, Is.EqualTo(0));
    }

    [Test]
    public void HoldFire_DoesNotSubmitAttack()
    {
        using var fixture = StanceFixture.Create();
        _ = fixture.CreateActor(0, 0, CombatStances.HoldFire);
        _ = fixture.CreateHostile(300, 0);

        fixture.System.Update(1f / 60f);

        Assert.That(fixture.Orders.Count, Is.EqualTo(0));
    }

    [Test]
    public void ReturnFire_AttacksRecentAttackerOnly()
    {
        using var fixture = StanceFixture.Create();
        Entity actor = fixture.CreateActor(0, 0, CombatStances.ReturnFire, leashRadiusCm: 900);
        Entity hostile = fixture.CreateHostile(300, 0);
        fixture.World.Add(actor, new RetaliationMemory { LastAttacker = hostile, LastAttackerStep = 0 });

        fixture.System.Update(1f / 60f);

        Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
        Assert.That(order.OrderTypeId, Is.EqualTo(fixture.AttackTargetOrderTypeId));
        Assert.That(order.Target, Is.EqualTo(hostile));
    }

    [Test]
    public void DamageTakenEvent_WritesRetaliationMemoryAndReturnFireAttacks()
    {
        using var fixture = StanceFixture.Create();
        Entity actor = fixture.CreateActor(0, 0, CombatStances.ReturnFire, leashRadiusCm: 900);
        Entity hostile = fixture.CreateHostile(300, 0);
        fixture.Events.Publish(new GameplayEvent
        {
            TagId = fixture.DamageTakenEventTagId,
            Source = hostile,
            Target = actor
        });
        fixture.Events.Update();

        fixture.System.Update(1f / 60f);
        fixture.World.Get<OrderBuffer>(actor).Clear();
        fixture.Clock.Advance(ClockDomainId.Step, 1);
        fixture.System.Update(1f / 60f);

        Assert.That(fixture.World.Has<RetaliationMemory>(actor), Is.True);
        Assert.That(fixture.World.Get<RetaliationMemory>(actor).LastAttacker, Is.EqualTo(hostile));
        Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
        Assert.That(order.OrderTypeId, Is.EqualTo(fixture.AttackTargetOrderTypeId));
        Assert.That(order.Target, Is.EqualTo(hostile));
    }

    [Test]
    public void Defend_AttacksHostileInLeash()
    {
        using var fixture = StanceFixture.Create();
        Entity actor = fixture.CreateActor(0, 0, CombatStances.Defend, leashRadiusCm: 900);
        Entity hostile = fixture.CreateHostile(300, 0);

        fixture.System.Update(1f / 60f);

        Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
        Assert.That(order.Actor, Is.EqualTo(actor));
        Assert.That(order.OrderTypeId, Is.EqualTo(fixture.AttackTargetOrderTypeId));
        Assert.That(order.Target, Is.EqualTo(hostile));
    }

    [Test]
    public void AttackAnything_UsesPriorityBucketThenNearest()
    {
        using var fixture = StanceFixture.Create();
        _ = fixture.CreateActor(0, 0, CombatStances.AttackAnything, leashRadiusCm: 1200);
        _ = fixture.CreateHostile(200, 0, priority: 1);
        Entity highPriority = fixture.CreateHostile(900, 0, priority: 5);

        fixture.System.Update(1f / 60f);

        Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
        Assert.That(order.Target, Is.EqualTo(highPriority));
    }

    [Test]
    public void AttackMove_SubmitsAttackThenResumesMove()
    {
        using var fixture = StanceFixture.Create();
        Entity actor = fixture.CreateActor(0, 0, CombatStances.Defend, leashRadiusCm: 800);
        Entity hostile = fixture.CreateHostile(300, 0);
        fixture.Activate(actor, fixture.AttackMoveOrderTypeId, (ref Order order) =>
        {
            order.Args.I0 = 800;
            WritePoint(ref order, 1000, 0);
        });

        fixture.System.Update(1f / 60f);

        Assert.That(fixture.Orders.TryDequeue(out var attack), Is.True);
        Assert.That(attack.OrderTypeId, Is.EqualTo(fixture.AttackTargetOrderTypeId));
        Assert.That(attack.Target, Is.EqualTo(hostile));

        fixture.World.Destroy(hostile);
        ref var buffer = ref fixture.World.Get<OrderBuffer>(actor);
        buffer.Clear();
        fixture.Clock.Advance(ClockDomainId.Step, 1);
        fixture.System.Update(1f / 60f);

        Assert.That(fixture.Orders.TryDequeue(out var resume), Is.True);
        Assert.That(resume.OrderTypeId, Is.EqualTo(fixture.MoveToOrderTypeId));
        Assert.That((int)resume.Args.Spatial.WorldCm.X, Is.EqualTo(1000));
        Assert.That((int)resume.Args.Spatial.WorldCm.Z, Is.EqualTo(0));
    }

    [Test]
    public void Guard_FollowsProtectedTargetAndAttacksThreat()
    {
        using var fixture = StanceFixture.Create();
        Entity actor = fixture.CreateActor(0, 0, CombatStances.Defend, leashRadiusCm: 900);
        Entity guarded = fixture.CreateFriendly(1000, 0);
        Entity hostile = fixture.CreateHostile(1050, 0);
        fixture.Activate(actor, fixture.GuardOrderTypeId, (ref Order order) =>
        {
            order.Target = guarded;
            order.Args.I0 = 150;
            order.Args.I1 = 900;
        });

        fixture.System.Update(1f / 60f);

        Assert.That(fixture.Orders.TryDequeue(out var attack), Is.True);
        Assert.That(attack.OrderTypeId, Is.EqualTo(fixture.AttackTargetOrderTypeId));
        Assert.That(attack.Target, Is.EqualTo(hostile));

        fixture.World.Destroy(hostile);
        ref var buffer = ref fixture.World.Get<OrderBuffer>(actor);
        buffer.Clear();
        fixture.Clock.Advance(ClockDomainId.Step, 1);
        fixture.System.Update(1f / 60f);

        Assert.That(fixture.Orders.TryDequeue(out var follow), Is.True);
        Assert.That(follow.OrderTypeId, Is.EqualTo(fixture.MoveToOrderTypeId));
        Assert.That((int)follow.Args.Spatial.WorldCm.X, Is.EqualTo(1000));
    }

    [Test]
    public void Scatter_SubmitsMoveOrderAndDoesNotCreateEffect()
    {
        using var fixture = StanceFixture.Create();
        var effects = new EffectRequestQueue();
        Entity actor = fixture.CreateActor(0, 0, CombatStances.HoldFire);
        fixture.Activate(actor, fixture.ScatterOrderTypeId, (ref Order order) => order.Args.I0 = 200);

        fixture.System.Update(1f / 60f);

        Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
        Assert.That(order.OrderTypeId, Is.EqualTo(fixture.MoveToOrderTypeId));
        Assert.That(effects.Count, Is.EqualTo(0));
    }

    private static void WritePoint(ref Order order, int x, int y)
    {
        order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
        order.Args.Spatial.Mode = OrderCollectionMode.Single;
        order.Args.Spatial.WorldCm = new Vector3(x, 0f, y);
    }

    private sealed class StanceFixture : IDisposable
    {
        private int _nextOrderId = 1;

        private StanceFixture(
            World world,
            DiscreteClock clock,
            OrderQueue orders,
            OrderTypeRegistry orderTypes,
            GameplayEventBus events,
            ChunkedGridSpatialPartitionWorld partition,
            WorldSizeSpec spec,
            SpatialQueryService spatial,
            RelationshipRuntime relationships,
            TeamEntityLookup teamLookup,
            int hostileRelationshipTypeId,
            CombatStanceOrderSystem system)
        {
            World = world;
            Clock = clock;
            Orders = orders;
            OrderTypes = orderTypes;
            Events = events;
            Partition = partition;
            Spec = spec;
            Spatial = spatial;
            Relationships = relationships;
            TeamLookup = teamLookup;
            HostileRelationshipTypeId = hostileRelationshipTypeId;
            System = system;
            AttackMoveOrderTypeId = orderTypes.GetId(StanceOrderKeys.AttackMove);
            AssaultMoveOrderTypeId = orderTypes.GetId(StanceOrderKeys.AssaultMove);
            GuardOrderTypeId = orderTypes.GetId(StanceOrderKeys.Guard);
            SetCombatStanceOrderTypeId = orderTypes.GetId(StanceOrderKeys.SetCombatStance);
            ScatterOrderTypeId = orderTypes.GetId(StanceOrderKeys.Scatter);
            MoveToOrderTypeId = orderTypes.GetId(StanceOrderKeys.MoveTo);
            AttackTargetOrderTypeId = orderTypes.GetId(StanceOrderKeys.AttackTarget);
            DamageTakenEventTagId = TagRegistry.GetId("Event.DamageTaken");
        }

        public World World { get; }
        public DiscreteClock Clock { get; }
        public OrderQueue Orders { get; }
        public OrderTypeRegistry OrderTypes { get; }
        public GameplayEventBus Events { get; }
        public ChunkedGridSpatialPartitionWorld Partition { get; }
        public WorldSizeSpec Spec { get; }
        public SpatialQueryService Spatial { get; }
        public RelationshipRuntime Relationships { get; }
        public TeamEntityLookup TeamLookup { get; }
        public int HostileRelationshipTypeId { get; }
        public CombatStanceOrderSystem System { get; }
        public int AttackMoveOrderTypeId { get; }
        public int AssaultMoveOrderTypeId { get; }
        public int GuardOrderTypeId { get; }
        public int SetCombatStanceOrderTypeId { get; }
        public int ScatterOrderTypeId { get; }
        public int MoveToOrderTypeId { get; }
        public int AttackTargetOrderTypeId { get; }
        public int DamageTakenEventTagId { get; }

        public static StanceFixture Create()
        {
            var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var events = new GameplayEventBus();
            var orderTypes = CreateOrderTypes();
            TagRegistry.Clear();
            TagRegistry.Register("Event.DamageTaken");
            var relationshipTypes = new RelationshipTypeRegistry();
            int hostileRelationshipTypeId = relationshipTypes.Register("CombatStance.Hostile", isSymmetric: true);
            var relationshipMetrics = new RelationshipMetricRegistry();
            var relationshipFlags = new RelationshipFlagRegistry();
            var relationshipBands = new RelationshipBandRegistry();
            var relationshipChanges = new RelationshipChangeBuffer();
            var relationships = new RelationshipRuntime(world, relationshipTypes, relationshipMetrics, relationshipFlags, relationshipBands, relationshipChanges, new RelationshipReverseIndex(world));
            var teamLookup = new TeamEntityLookup();
            var partition = new ChunkedGridSpatialPartitionWorld(64);
            var spec = new WorldSizeSpec(new WorldAabbCm(-5000, -5000, 10000, 10000), 100);
            var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
            spatial.SetPositionProvider(entity =>
            {
                if (!world.IsAlive(entity) || !world.Has<WorldPositionCm>(entity))
                {
                    return new WorldCmInt2(1_000_000_000, 1_000_000_000);
                }

                return world.Get<WorldPositionCm>(entity).ToWorldCmInt2();
            });
            var settings = new CombatStanceBehaviorSettings(arrivalRadiusCm: 50, defaultRetaliationTtlSteps: 180, hostileRelationshipTypeId);
            var system = new CombatStanceOrderSystem(world, clock, orders, orderTypes, spatial, settings, relationships, teamLookup, events);
            return new StanceFixture(world, clock, orders, orderTypes, events, partition, spec, spatial, relationships, teamLookup, hostileRelationshipTypeId, system);
        }

        public Entity CreateActor(int x, int y, int stance, int leashRadiusCm = 0)
        {
            var actor = World.Create(
                new OrderBuffer { ActiveIndex = -1 },
                new Team { Id = 1 },
                new CombatStanceState
                {
                    Stance = stance,
                    LeashRadiusCm = leashRadiusCm,
                    RetaliationTtlSteps = 180
                },
                WorldPositionCm.FromCm(x, y));
            RegisterTeamRepresentativeIfMissing(1, actor);
            Partition.Add(actor, x / Spec.GridCellSizeCm, y / Spec.GridCellSizeCm);
            return actor;
        }

        public Entity CreateFriendly(int x, int y)
        {
            var entity = World.Create(
                new OrderBuffer { ActiveIndex = -1 },
                new Team { Id = 1 },
                WorldPositionCm.FromCm(x, y));
            RegisterTeamRepresentativeIfMissing(1, entity);
            Partition.Add(entity, x / Spec.GridCellSizeCm, y / Spec.GridCellSizeCm);
            return entity;
        }

        public Entity CreateHostile(int x, int y, int priority = 0)
        {
            Entity entity = priority > 0
                ? World.Create(new Team { Id = 2 }, new UtilityAiTargetPriority { Bucket = priority }, WorldPositionCm.FromCm(x, y))
                : World.Create(new Team { Id = 2 }, WorldPositionCm.FromCm(x, y));
            RegisterTeamRepresentativeIfMissing(2, entity);
            EnsureHostileTeamRelationship();
            Partition.Add(entity, x / Spec.GridCellSizeCm, y / Spec.GridCellSizeCm);
            return entity;
        }

        private void RegisterTeamRepresentativeIfMissing(int teamId, Entity entity)
        {
            if (!TeamLookup.TryGet(teamId, out _))
            {
                TeamLookup.Register(teamId, entity);
            }
        }

        private void EnsureHostileTeamRelationship()
        {
            if (!TeamLookup.TryGet(1, out Entity friendlyTeam) ||
                !TeamLookup.TryGet(2, out Entity hostileTeam))
            {
                return;
            }

            Relationships.EnsureLink(friendlyTeam, hostileTeam, HostileRelationshipTypeId);
            Relationships.EnsureLink(hostileTeam, friendlyTeam, HostileRelationshipTypeId);
        }

        public void Activate(Entity actor, int orderTypeId, OrderMutator mutate)
        {
            var order = new Order
            {
                OrderId = _nextOrderId++,
                Actor = actor,
                OrderTypeId = orderTypeId,
                SubmitMode = OrderSubmitMode.Immediate,
                SubmitStep = Clock.Now(ClockDomainId.Step)
            };
            mutate(ref order);
            ref var buffer = ref World.Get<OrderBuffer>(actor);
            buffer.SetActiveDirect(in order, OrderTypes.Get(orderTypeId).Priority);
        }

        public void Dispose()
        {
            System.Dispose();
            World.Destroy(World);
        }

        private static OrderTypeRegistry CreateOrderTypes()
        {
            var registry = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            Register(registry, StanceOrderKeys.MoveTo, 101, 60);
            Register(registry, StanceOrderKeys.AttackTarget, 102, 75);
            Register(registry, StanceOrderKeys.AttackMove, 110, 80);
            Register(registry, StanceOrderKeys.AssaultMove, 111, 80);
            Register(registry, StanceOrderKeys.Guard, 112, 85);
            Register(registry, StanceOrderKeys.SetCombatStance, 113, 180);
            Register(registry, StanceOrderKeys.Scatter, 114, 140);
            return registry;
        }

        private static void Register(OrderTypeRegistry registry, string key, int id, int priority)
        {
            registry.Register(new OrderTypeConfig
            {
                Key = key,
                OrderTypeId = id,
                Priority = priority,
                BufferWindowMs = 0,
                PendingBufferWindowMs = 0,
                SameTypePolicy = SameTypePolicy.Replace,
                QueueFullPolicy = QueueFullPolicy.DropOldest,
                MaxQueueSize = 1,
                QueuedModeMaxSize = 1,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                EntityBlackboardKey = -1,
                SpatialBlackboardKey = -1,
            });
        }

        public delegate void OrderMutator(ref Order order);
    }
}
