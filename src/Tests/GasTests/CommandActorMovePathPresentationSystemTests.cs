using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class CommandActorMovePathPresentationSystemTests
    {
        private const int MoveToOrderTypeId = 101;
        private const int MassNavigationMoveOrderTypeId = 172;
        private const string MoveToOrderKey = "moveTo";
        private const string MassNavigationMoveOrderKey = "massNavigation.move";

        [Test]
        public void UpdateViewedSelection_PublishesMovePathEventsForActiveAndQueuedMoveOrders()
        {
            using var fixture = CommandActorMovePathFixture.Create(MoveToOrderKey);
            Entity actor = fixture.CreateSelectedActor(WorldPositionCm.FromCm(0, 0));

            ref OrderBuffer orders = ref fixture.World.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(CreateMoveOrder(actor, 300, 0, orderId: 1), priority: 60);
            Assert.That(orders.Enqueue(CreateMoveOrder(actor, 300, 200, orderId: 2), priority: 60, expireStep: -1, insertStep: 1), Is.True);

            fixture.System.Update(0.016f);

            PresentationEvent[] lineBegun = fixture.EventsOf(PresentationEventKind.MovePathBegun, CommandActorMovePathPresentationSystem.LineEventKey);
            PresentationEvent[] lineUpdated = fixture.EventsOf(PresentationEventKind.MovePathUpdated, CommandActorMovePathPresentationSystem.LineEventKey);
            PresentationEvent[] waypointBegun = fixture.EventsOf(PresentationEventKind.MovePathBegun, CommandActorMovePathPresentationSystem.WaypointEventKey);
            PresentationEvent[] waypointUpdated = fixture.EventsOf(PresentationEventKind.MovePathUpdated, CommandActorMovePathPresentationSystem.WaypointEventKey);

            Assert.That(lineBegun.Length, Is.EqualTo(2));
            Assert.That(lineUpdated.Length, Is.EqualTo(2));
            Assert.That(waypointBegun.Length, Is.EqualTo(2));
            Assert.That(waypointUpdated.Length, Is.EqualTo(2));
            Assert.That(lineUpdated[0].Source, Is.EqualTo(actor));
            Assert.That(lineUpdated[0].Target, Is.EqualTo(actor));
            Assert.That(lineUpdated[0].PayloadA, Is.GreaterThan(0));
            Assert.That(lineUpdated[0].FloatA, Is.EqualTo(WorldUnits.CmToM(300f)).Within(0.001f));
            Assert.That(lineUpdated[0].FloatB, Is.EqualTo(WorldUnits.CmToM(28f)).Within(0.001f));
            Assert.That(lineUpdated[1].Position.X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(lineUpdated[1].Position.Z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(lineUpdated[1].FloatA, Is.EqualTo(WorldUnits.CmToM(200f)).Within(0.001f));
            Assert.That(waypointUpdated[0].FloatA, Is.EqualTo(WorldUnits.CmToM(26f)).Within(0.001f));
        }

        [Test]
        public void UpdateViewedSelection_UsesConfiguredMoveOrderTypeKeys()
        {
            using var fixture = CommandActorMovePathFixture.Create(MoveToOrderKey, MassNavigationMoveOrderKey);
            Entity actor = fixture.CreateSelectedActor(WorldPositionCm.FromCm(0, 0));

            ref OrderBuffer orders = ref fixture.World.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(CreateMoveOrder(actor, MassNavigationMoveOrderTypeId, 450, 250, orderId: 7), priority: 70);

            fixture.System.Update(0.016f);

            PresentationEvent[] lineUpdated = fixture.EventsOf(PresentationEventKind.MovePathUpdated, CommandActorMovePathPresentationSystem.LineEventKey);
            PresentationEvent[] waypointUpdated = fixture.EventsOf(PresentationEventKind.MovePathUpdated, CommandActorMovePathPresentationSystem.WaypointEventKey);
            Assert.That(lineUpdated.Length, Is.EqualTo(1));
            Assert.That(waypointUpdated.Length, Is.EqualTo(1));
            Assert.That(lineUpdated[0].FloatA, Is.EqualTo(WorldUnits.CmToM(MathF.Sqrt((450f * 450f) + (250f * 250f)))).Within(0.001f));
            Assert.That(waypointUpdated[0].Position.X, Is.EqualTo(4.5f).Within(0.001f));
            Assert.That(waypointUpdated[0].Position.Z, Is.EqualTo(2.5f).Within(0.001f));
        }

        [Test]
        public void UpdateViewedSelection_AuthoredRoutePastCapacityPublishesCappedEvents()
        {
            using var fixture = CommandActorMovePathFixture.Create(MoveToOrderKey);
            Entity actor = fixture.CreateSelectedActor(WorldPositionCm.FromCm(0, 0));

            ref OrderBuffer orders = ref fixture.World.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(CreateRouteOrder(fixture.World, actor, OrderSpatial.MaxPoints), priority: 60);

            Assert.DoesNotThrow(() => fixture.System.Update(0.016f));
            Assert.That(
                fixture.EventsOf(PresentationEventKind.MovePathUpdated, CommandActorMovePathPresentationSystem.LineEventKey).Length,
                Is.EqualTo(OrderSpatial.MaxPoints));
            Assert.That(
                fixture.EventsOf(PresentationEventKind.MovePathUpdated, CommandActorMovePathPresentationSystem.WaypointEventKey).Length,
                Is.EqualTo(OrderSpatial.MaxPoints));
        }

        [Test]
        public void PerformerRules_ConsumeMovePathEvents_AsScopedGroundOverlayPerformers()
        {
            using var fixture = CommandActorMovePathFixture.Create(MoveToOrderKey);
            Entity actor = fixture.CreateSelectedActor(WorldPositionCm.FromCm(0, 0));

            ref OrderBuffer orders = ref fixture.World.Get<OrderBuffer>(actor);
            orders.SetActiveDirect(CreateMoveOrder(actor, 300, 0, orderId: 1), priority: 60);

            fixture.System.Update(0.016f);
            fixture.Performers.Tick();

            Entity line = fixture.Performers.FindPerformer("test.move_path.line", actor);
            Entity waypoint = fixture.Performers.FindPerformer("test.move_path.waypoint", actor);
            Assert.That(PerformerParamResolver.ResolveFloat(fixture.World, line, WellKnownPerformerParamKeys.OverlayLength), Is.EqualTo(3f).Within(0.001f));
            Assert.That(PerformerParamResolver.ResolveFloat(fixture.World, line, WellKnownPerformerParamKeys.OverlayWidth), Is.EqualTo(0.28f).Within(0.001f));
            Assert.That(PerformerParamResolver.ResolveFloat(fixture.World, waypoint, WellKnownPerformerParamKeys.OverlayRadius), Is.EqualTo(0.26f).Within(0.001f));

            fixture.Events.Clear();
            fixture.Performers.Requests.Clear();
            ref OrderBuffer currentOrders = ref fixture.World.Get<OrderBuffer>(actor);
            currentOrders.Clear();

            fixture.System.Update(0.016f);
            Assert.That(
                fixture.EventsOf(PresentationEventKind.MovePathEnded, CommandActorMovePathPresentationSystem.LineEventKey).Length,
                Is.EqualTo(1));
            Assert.That(
                fixture.EventsOf(PresentationEventKind.MovePathEnded, CommandActorMovePathPresentationSystem.WaypointEventKey).Length,
                Is.EqualTo(1));
            fixture.Performers.Tick();

            Assert.That(fixture.Performers.TryFindPerformer("test.move_path.line", actor, out _), Is.False);
            Assert.That(fixture.Performers.TryFindPerformer("test.move_path.waypoint", actor, out _), Is.False);
        }

        private static Order CreateMoveOrder(Entity actor, int xcm, int ycm, int orderId)
        {
            return CreateMoveOrder(actor, MoveToOrderTypeId, xcm, ycm, orderId);
        }

        private static Order CreateMoveOrder(Entity actor, int orderTypeId, int xcm, int ycm, int orderId)
        {
            return new Order
            {
                OrderId = orderId,
                OrderTypeId = orderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(xcm, 0f, ycm)
                    }
                }
            };
        }

        private static Order CreateRouteOrder(World world, Entity actor, int pointCount)
        {
            var order = new Order
            {
                OrderId = 99,
                OrderTypeId = MoveToOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.List,
                    }
                }
            };

            var pointXcm = new int[pointCount];
            var pointYcm = new int[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                pointXcm[i] = (i + 1) * 100;
            }

            OrderSpatialPayloadOps.SetPath(world, actor, ref order, pointXcm, pointYcm, pointCount);

            return order;
        }

        private sealed class CommandActorMovePathFixture : IDisposable
        {
            private readonly Entity _viewer;
            private readonly EntityCollectionStore _collections;

            private CommandActorMovePathFixture(
                World world,
                Entity viewer,
                EntityCollectionStore collections,
                PresentationEventStream events,
                CommandActorMovePathPresentationSystem system,
                PerformerMovePathFixture performers)
            {
                World = world;
                _viewer = viewer;
                _collections = collections;
                Events = events;
                System = system;
                Performers = performers;
            }

            public World World { get; }
            public PresentationEventStream Events { get; }
            public CommandActorMovePathPresentationSystem System { get; }
            public PerformerMovePathFixture Performers { get; }

            public static CommandActorMovePathFixture Create(params string[] previewOrderTypeKeys)
            {
                World world = World.Create();
                var commandSourceConfig = new CommandSourceAcquisitionConfig
                {
                    TargetFilter = new CommandSourceTargetFilterConfig { RelationFilter = "All" },
                    MovePathPreviewOrderTypeKeys = previewOrderTypeKeys,
                };
                var collectionKeys = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var collections = new EntityCollectionStore(collectionKeys);
                Entity viewer = world.Create();

                var events = new PresentationEventStream(512);
                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.PresentationEventStream.Name] = events,
                    [CoreServiceKeys.GameSession.Name] = new GameSession(),
                    [CoreServiceKeys.GameConfig.Name] = new GameConfig { CommandSource = commandSourceConfig },
                    [CoreServiceKeys.OrderTypeRegistry.Name] = CreateOrderTypeRegistry(),
                    [CoreServiceKeys.EntityCollectionStore.Name] = collections,
                    [CoreServiceKeys.EntityCollectionKeyRegistry.Name] = collectionKeys,
                };
                var system = new CommandActorMovePathPresentationSystem(
                    world,
                    globals,
                    (out Entity owner) =>
                    {
                        owner = viewer;
                        return world.IsAlive(viewer);
                    });
                return new CommandActorMovePathFixture(
                    world,
                    viewer,
                    collections,
                    events,
                    system,
                    new PerformerMovePathFixture(world, events));
            }

            public Entity CreateSelectedActor(WorldPositionCm position)
            {
                Entity actor = World.Create(position, OrderBuffer.CreateEmpty(), new OrderSpatialPayloadBuffer());
                var descriptor = EntityCollectionDescriptor.Create(
                    EntityCollectionKeys.CommandSource,
                    EntityCollectionSourceKind.Explicit,
                    EntityCollectionRoleKind.CommandSource,
                    contextEntity: _viewer,
                    primaryEntity: actor,
                    title: "Selected move path command source",
                    summary: "Test-owned command source collection.");
                _collections.Replace(_viewer, in descriptor, new[] { actor }, _viewer);
                return actor;
            }

            public PresentationEvent[] EventsOf(PresentationEventKind kind, string key)
            {
                int keyId = TagRegistry.GetId(key);
                Assert.That(keyId, Is.GreaterThan(0), $"Event key '{key}' was not registered.");
                return Events.GetSpan().ToArray()
                    .Where(evt => evt.Kind == kind && evt.KeyId == keyId)
                    .ToArray();
            }

            public void Dispose()
            {
                World.Dispose();
            }

            private static OrderTypeRegistry CreateOrderTypeRegistry()
            {
                var orderTypes = new OrderTypeRegistry();
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = MoveToOrderKey,
                    OrderTypeId = MoveToOrderTypeId,
                    Label = "Move",
                    Priority = 60,
                    AllowQueuedMode = true,
                    QueuedModeMaxSize = 8
                });
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = MassNavigationMoveOrderKey,
                    OrderTypeId = MassNavigationMoveOrderTypeId,
                    Label = "Mass Move",
                    Priority = 60,
                    AllowQueuedMode = true,
                    QueuedModeMaxSize = 8
                });
                return orderTypes;
            }
        }

        private sealed class PerformerMovePathFixture
        {
            private readonly World _world;
            private readonly PerformerDefinitionRegistry _definitions = new();
            private readonly PerformerEntityRuntime _runtime;
            private readonly PerformerCommandBuffer _commands = new(256);
            private readonly PerformerRuleSystem _rules;
            private readonly PerformerRuntimeSystem _runtimeSystem;
            private readonly PerformerEmitSystem _emitSystem;

            public PerformerMovePathFixture(World world, PresentationEventStream events)
            {
                _world = world;
                Events = events;
                Requests = new PresentationRequestBuffer(256);
                _runtime = new PerformerEntityRuntime(world);
                RegisterDefinitions();

                var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null);
                _rules = new PerformerRuleSystem(
                    world,
                    events,
                    _commands,
                    _definitions,
                    _runtime,
                    new GraphProgramRegistry(),
                    graphApi,
                    new Dictionary<string, object>());
                _runtimeSystem = new PerformerRuntimeSystem(
                    world,
                    _commands,
                    events,
                    new TransientMarkerBuffer(),
                    Requests,
                    _runtime,
                    new PresentationStableIdAllocator(),
                    _definitions);
                _emitSystem = new PerformerEmitSystem(
                    world,
                    _runtime,
                    _definitions,
                    Requests,
                    new Dictionary<string, object>());
            }

            public PresentationEventStream Events { get; }
            public PresentationRequestBuffer Requests { get; }

            public void Tick()
            {
                _rules.Update(0.016f);
                _runtimeSystem.Update(0.016f);
                _emitSystem.Update(0.016f);
                Events.Clear();
            }

            public Entity FindPerformer(string performerId, Entity owner)
            {
                Assert.That(TryFindPerformer(performerId, owner, out Entity performer), Is.True, $"Performer '{performerId}' was not created.");
                return performer;
            }

            public bool TryFindPerformer(string performerId, Entity owner, out Entity performer)
            {
                Entity found = Entity.Null;
                int defId = _definitions.GetId(performerId);
                var query = new QueryDescription().WithAll<PerformerState>();
                _world.Query(in query, (Entity entity, ref PerformerState state) =>
                {
                    if (state.DefId == defId && state.OwnerEntity == owner)
                    {
                        found = entity;
                    }
                });
                performer = found;
                return performer != Entity.Null;
            }

            private void RegisterDefinitions()
            {
                int lineId = _definitions.GetOrRegisterId("test.move_path.line");
                int waypointId = _definitions.GetOrRegisterId("test.move_path.waypoint");
                _definitions.Register("test.move_path.line", new PerformerDefinition
                {
                    DefaultLifetime = -1f,
                    ParamDefaults =
                    [
                        new ParamDefault { ParamKey = WellKnownPerformerParamKeys.OverlayLength, Lane = ParamLane.Float, FloatValue = 1f },
                        new ParamDefault { ParamKey = WellKnownPerformerParamKeys.OverlayWidth, Lane = ParamLane.Float, FloatValue = 1f },
                    ],
                    Behaviors = SingleGroundOverlayBehavior((int)GroundOverlayShape.Line),
                });
                _definitions.Register("test.move_path.waypoint", new PerformerDefinition
                {
                    DefaultLifetime = -1f,
                    ParamDefaults =
                    [
                        new ParamDefault { ParamKey = WellKnownPerformerParamKeys.OverlayRadius, Lane = ParamLane.Float, FloatValue = 1f },
                    ],
                    Behaviors = SingleGroundOverlayBehavior((int)GroundOverlayShape.Circle),
                });
                _definitions.Register("test.move_path.rules", new PerformerDefinition
                {
                    Rules =
                    [
                        CreateRule(CommandActorMovePathPresentationSystem.LineEventKey, lineId),
                        FloatParamRule(CommandActorMovePathPresentationSystem.LineEventKey, lineId, WellKnownPerformerParamKeys.OverlayLength, PerformerCommandValueSource.EventFloatA),
                        FloatParamRule(CommandActorMovePathPresentationSystem.LineEventKey, lineId, WellKnownPerformerParamKeys.OverlayWidth, PerformerCommandValueSource.EventFloatB),
                        FloatParamRule(CommandActorMovePathPresentationSystem.LineEventKey, lineId, WellKnownPerformerParamKeys.OverlayRotation, PerformerCommandValueSource.EventFloatC),
                        EndRule(CommandActorMovePathPresentationSystem.LineEventKey, lineId),
                        CreateRule(CommandActorMovePathPresentationSystem.WaypointEventKey, waypointId),
                        FloatParamRule(CommandActorMovePathPresentationSystem.WaypointEventKey, waypointId, WellKnownPerformerParamKeys.OverlayRadius, PerformerCommandValueSource.EventFloatA),
                        EndRule(CommandActorMovePathPresentationSystem.WaypointEventKey, waypointId),
                    ]
                });
            }

            private static PerformerRule CreateRule(string key, int definitionId)
            {
                return new PerformerRule
                {
                    Event = new EventFilter { Kind = PresentationEventKind.MovePathBegun, KeyId = TagRegistry.Register(key) },
                    Command = new PerformerCommand
                    {
                        CommandKind = PerformerCommandKind.CreatePerformer,
                        PerformerDefinitionId = definitionId,
                        ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        UseEventPosition = true,
                    }
                };
            }

            private static PerformerRule FloatParamRule(string key, int definitionId, int paramKey, PerformerCommandValueSource source)
            {
                return new PerformerRule
                {
                    Event = new EventFilter { Kind = PresentationEventKind.MovePathUpdated, KeyId = TagRegistry.Register(key) },
                    Command = new PerformerCommand
                    {
                        CommandKind = PerformerCommandKind.SetParam,
                        PerformerDefinitionId = definitionId,
                        ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                        UseEventPosition = true,
                        ParamKey = paramKey,
                        ParamLane = ParamLane.Float,
                        ValueSource = source,
                    }
                };
            }

            private static PerformerRule EndRule(string key, int definitionId)
            {
                return new PerformerRule
                {
                    Event = new EventFilter { Kind = PresentationEventKind.MovePathEnded, KeyId = TagRegistry.Register(key) },
                    Command = new PerformerCommand
                    {
                        CommandKind = PerformerCommandKind.DestroyScopedPerformer,
                        PerformerDefinitionId = definitionId,
                        ScopeSource = PerformerCommandScopeSource.EventPayloadA,
                    }
                };
            }

            private static BehaviorSlot[] SingleGroundOverlayBehavior(int shape)
            {
                return
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.GroundOverlay,
                            AssetId = shape,
                            RenderPath = VisualRenderPath.None,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                        }
                    }
                ];
            }
        }
    }
}
