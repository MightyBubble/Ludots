using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CoreInputMod.Systems
{
    public sealed class CommandActorMovePathPresentationSystem : ISystem<float>
    {
        public delegate bool CommandSourceOwnerProvider(out Entity owner);

        public const string DebugSummaryKey = "CoreInputMod.CommandActorMovePath.DebugSummary";

        public const string LineEventKey = "core_input.move_path.line";
        public const string WaypointEventKey = "core_input.move_path.waypoint";

        private const int MaxCommandActors = 4;
        private const int MaxScopesPerActor = OrderSpatial.MaxPoints + 1;
        private const int LineScopeBase = 52000;
        private const int WaypointScopeBase = 52900;
        private const float OverlayY = 0.035f;
        private const float PrimaryLineWidthCm = 28f;
        private const float SecondaryLineWidthCm = 18f;
        private const float WaypointRadiusCm = 26f;

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly CommandSourceOwnerProvider _commandSourceOwnerProvider;
        private readonly Dictionary<ActorKey, ActorScopeState> _activeScopesByActor = new();
        private readonly List<ActorKey> _actorRemovalScratch = new(16);
        private Entity[] _commandActors = new Entity[16];
        private int[] _moveOrderTypeIds = Array.Empty<int>();
        private bool _moveOrderTypeIdsResolved;
        private PresentationEventStream? _events;
        private GameSession? _session;
        private int _lineEventKeyId;
        private int _waypointEventKeyId;
        private string _lastProjectionSummary = "projection=uninitialized";

        private readonly struct ActorKey : IEquatable<ActorKey>
        {
            private readonly int _id;
            private readonly int _worldId;
            private readonly int _version;

            public ActorKey(Entity entity)
            {
                _id = entity.Id;
                _worldId = entity.WorldId;
                _version = entity.Version;
            }

            public bool Equals(ActorKey other)
            {
                return _id == other._id && _worldId == other._worldId && _version == other._version;
            }

            public override bool Equals(object? obj)
            {
                return obj is ActorKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_id, _worldId, _version);
            }
        }

        private struct ActorScopeState
        {
            public Entity Actor;
            public int LineCount;
            public int WaypointCount;
            public int TouchedFrame;
        }

        public CommandActorMovePathPresentationSystem(
            World world,
            Dictionary<string, object> globals,
            CommandSourceOwnerProvider commandSourceOwnerProvider)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _commandSourceOwnerProvider = commandSourceOwnerProvider ?? throw new ArgumentNullException(nameof(commandSourceOwnerProvider));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            Entity collectionOwner = ResolveCommandSourceOwner();
            const string collectionKey = EntityCollectionKeys.CommandSource;
            Entity collectionContext = Entity.Null;
            Entity primaryViewed = TryResolveCommandSourceView(collectionOwner, out EntityCollectionView commandSourceView)
                ? ResolveCollectionViewSummary(in commandSourceView, out collectionContext)
                : Entity.Null;

            int emittedLines = 0;
            int emittedWaypoints = 0;

            if (!TryResolveRuntime())
            {
                PublishDebugState(
                    commandActorCount: 0,
                    emittedLines: 0,
                    emittedWaypoints: 0,
                    collectionOwner,
                    collectionKey,
                    collectionContext,
                    primaryViewed);
                return;
            }

            int frameId = _session?.CurrentTick ?? Environment.TickCount;
            int viewedCount = GetCommandSourceCount(collectionOwner);
            EnsureCommandActorCapacity(viewedCount);
            int count = CopyCommandSourceActors(collectionOwner, _commandActors);
            if (count > 0)
            {
                int emittedEntities = 0;
                for (int i = 0; i < count && emittedEntities < MaxCommandActors; i++)
                {
                    Entity actor = _commandActors[i];
                    if (!_world.IsAlive(actor) || !_world.Has<OrderBuffer>(actor))
                    {
                        continue;
                    }

                    EmitActorPath(actor, emittedEntities == 0, frameId, out int actorLines, out int actorWaypoints);
                    emittedLines += actorLines;
                    emittedWaypoints += actorWaypoints;
                    emittedEntities++;
                }
            }

            EndUntouchedScopes(frameId);
            _lastProjectionSummary = $"projection=events line={emittedLines} waypoint={emittedWaypoints}";
            PublishDebugState(
                commandActorCount: count,
                emittedLines,
                emittedWaypoints,
                collectionOwner,
                collectionKey,
                collectionContext,
                primaryViewed);
        }

        private Entity ResolveCommandSourceOwner()
        {
            return _commandSourceOwnerProvider(out Entity owner) &&
                   owner != Entity.Null &&
                   _world.IsAlive(owner)
                ? owner
                : Entity.Null;
        }

        private bool TryResolveCommandSourceView(Entity owner, out EntityCollectionView view)
        {
            view = default;
            return owner != Entity.Null &&
                   _globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var collectionsObj) &&
                   collectionsObj is EntityCollectionStore collections &&
                   EntityCollectionContextRuntime.TryDescribeView(
                       collections,
                       owner,
                       EntityCollectionKeys.CommandSource,
                       out view);
        }

        private int GetCommandSourceCount(Entity owner)
        {
            return owner != Entity.Null &&
                   _globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var collectionsObj) &&
                   collectionsObj is EntityCollectionStore collections
                ? EntityCollectionContextRuntime.GetCount(collections, owner, EntityCollectionKeys.CommandSource)
                : 0;
        }

        private int CopyCommandSourceActors(Entity owner, Span<Entity> destination)
        {
            return owner != Entity.Null &&
                   _globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out var collectionsObj) &&
                   collectionsObj is EntityCollectionStore collections
                ? EntityCollectionContextRuntime.Copy(collections, owner, EntityCollectionKeys.CommandSource, destination)
                : 0;
        }

        private static Entity ResolveCollectionViewSummary(
            in EntityCollectionView view,
            out Entity contextEntity)
        {
            contextEntity = view.ContextEntity;
            return view.PrimaryEntity;
        }

        private bool TryResolveRuntime()
        {
            if (_events == null)
            {
                if (!_globals.TryGetValue(CoreServiceKeys.PresentationEventStream.Name, out var eventsObj) ||
                    eventsObj is not PresentationEventStream events)
                {
                    _lastProjectionSummary = "projection=missing:PresentationEventStream";
                    return false;
                }

                _events = events;
            }

            if (!_moveOrderTypeIdsResolved && !TryResolveMoveOrderTypeIds(out _))
            {
                _lastProjectionSummary = "projection=missing:moveOrderType";
                return false;
            }

            if (_lineEventKeyId <= 0)
            {
                _lineEventKeyId = ResolveEventKeyId(LineEventKey);
            }

            if (_waypointEventKeyId <= 0)
            {
                _waypointEventKeyId = ResolveEventKeyId(WaypointEventKey);
            }

            if (_session == null &&
                _globals.TryGetValue(CoreServiceKeys.GameSession.Name, out var sessionObj) &&
                sessionObj is GameSession session)
            {
                _session = session;
            }

            return true;
        }

        private void EmitActorPath(Entity actor, bool isPrimary, int frameId, out int emittedLines, out int emittedWaypoints)
        {
            emittedLines = 0;
            emittedWaypoints = 0;
            if (!OrderWorldSpatialResolver.TryGetEntityWorldCm(_world, actor, out var originWorldCm))
            {
                ClearActorScopes(actor, frameId);
                return;
            }

            ref var buffer = ref _world.Get<OrderBuffer>(actor);
            if (buffer.HasActive &&
                IsMoveOrderType(buffer.ActiveOrder.Order.OrderTypeId, _moveOrderTypeIds) &&
                TryEmitOrderPath(actor, in buffer.ActiveOrder.Order, originWorldCm, isPrimary, frameId, ref emittedLines, ref emittedWaypoints, consumeFromCurrentIndex: true, out var activeDestination))
            {
                originWorldCm = activeDestination;
            }
            else if (buffer.HasActive &&
                     IsMoveOrderType(buffer.ActiveOrder.Order.OrderTypeId, _moveOrderTypeIds) &&
                     OrderWorldSpatialResolver.TryResolveMoveDestination(_world, in buffer.ActiveOrder.Order, out activeDestination))
            {
                EmitSegment(actor, originWorldCm, activeDestination, isPrimary, frameId, emittedLines++);
                EmitWaypoint(actor, activeDestination, isPrimary, frameId, emittedWaypoints++);
                originWorldCm = activeDestination;
            }

            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                Order queued = buffer.GetQueued(i).Order;
                if (!IsMoveOrderType(queued.OrderTypeId, _moveOrderTypeIds))
                {
                    continue;
                }

                if (TryEmitOrderPath(actor, in queued, originWorldCm, isPrimary, frameId, ref emittedLines, ref emittedWaypoints, consumeFromCurrentIndex: false, out var queuedDestination))
                {
                    originWorldCm = queuedDestination;
                    continue;
                }

                if (!OrderWorldSpatialResolver.TryResolveMoveDestination(_world, in queued, out queuedDestination))
                {
                    continue;
                }

                EmitSegment(actor, originWorldCm, queuedDestination, isPrimary, frameId, emittedLines++);
                EmitWaypoint(actor, queuedDestination, isPrimary, frameId, emittedWaypoints++);
                originWorldCm = queuedDestination;
            }

            TouchActorScopes(actor, emittedLines, emittedWaypoints, frameId);
        }

        private bool TryEmitOrderPath(
            Entity actor,
            in Order order,
            Vector3 originWorldCm,
            bool isPrimary,
            int frameId,
            ref int emittedLines,
            ref int emittedWaypoints,
            bool consumeFromCurrentIndex,
            out Vector3 finalDestination)
        {
            finalDestination = default;
            if (order.Args.Spatial.Mode != OrderCollectionMode.List)
            {
                return false;
            }

            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(_world, in order);
            if (pointCount <= 0)
            {
                return false;
            }

            int startIndex = consumeFromCurrentIndex
                ? ResolveActiveRouteStartIndex(order.Actor, in order, pointCount)
                : 0;
            Vector3 previous = originWorldCm;
            bool emitted = false;
            for (int pointIndex = startIndex; pointIndex < pointCount; pointIndex++)
            {
                if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(_world, in order, pointIndex, out var pointWorldCm))
                {
                    continue;
                }

                EmitSegment(actor, previous, pointWorldCm, isPrimary, frameId, emittedLines++);
                EmitWaypoint(actor, pointWorldCm, isPrimary, frameId, emittedWaypoints++);
                previous = pointWorldCm;
                finalDestination = pointWorldCm;
                emitted = true;
                if (emittedLines >= MaxScopesPerActor || emittedWaypoints >= MaxScopesPerActor)
                {
                    break;
                }
            }

            return emitted;
        }

        private int ResolveActiveRouteStartIndex(Entity entity, in Order order, int pointCount)
        {
            if (pointCount <= 0)
            {
                return 0;
            }

            if (_world.IsAlive(entity) && _world.Has<OrderBuffer>(entity))
            {
                OrderBuffer buffer = _world.Get<OrderBuffer>(entity);
                if (buffer.HasActive &&
                    buffer.ActiveOrder.Order.OrderId == order.OrderId &&
                    buffer.ActiveOrder.Order.OrderTypeId == order.OrderTypeId)
                {
                    return Math.Clamp(buffer.ActiveRuntimeInt0, 0, pointCount - 1);
                }
            }

            return 0;
        }

        private void EmitSegment(Entity actor, Vector3 startWorldCm, Vector3 endWorldCm, bool isPrimary, int frameId, int segmentIndex)
        {
            if (segmentIndex >= MaxScopesPerActor)
            {
                return;
            }

            float dxCm = endWorldCm.X - startWorldCm.X;
            float dzCm = endWorldCm.Z - startWorldCm.Z;
            float lengthCm = MathF.Sqrt((dxCm * dxCm) + (dzCm * dzCm));
            if (!float.IsFinite(lengthCm) || lengthCm <= 0.01f)
            {
                return;
            }

            float widthMeters = WorldUnits.CmToM(isPrimary ? PrimaryLineWidthCm : SecondaryLineWidthCm);
            float rotationDegrees = WorldPlane2D.RadToDegValue(WorldPlane2D.FacingRadFromDirection(dxCm, dzCm));
            PublishMovePathEvent(
                PresentationEventKind.MovePathBegun,
                _lineEventKeyId,
                actor,
                BuildScopeId(actor, LineScopeBase + segmentIndex),
                startWorldCm,
                WorldUnits.CmToM(lengthCm),
                widthMeters,
                rotationDegrees,
                isPrimary ? 1f : 0f);
            PublishMovePathEvent(
                PresentationEventKind.MovePathUpdated,
                _lineEventKeyId,
                actor,
                BuildScopeId(actor, LineScopeBase + segmentIndex),
                startWorldCm,
                WorldUnits.CmToM(lengthCm),
                widthMeters,
                rotationDegrees,
                isPrimary ? 1f : 0f);
        }

        private void EmitWaypoint(Entity actor, Vector3 pointWorldCm, bool isPrimary, int frameId, int waypointIndex)
        {
            if (waypointIndex >= MaxScopesPerActor)
            {
                return;
            }

            PublishMovePathEvent(
                PresentationEventKind.MovePathBegun,
                _waypointEventKeyId,
                actor,
                BuildScopeId(actor, WaypointScopeBase + waypointIndex),
                pointWorldCm,
                WorldUnits.CmToM(WaypointRadiusCm),
                0f,
                0f,
                isPrimary ? 1f : 0f);
            PublishMovePathEvent(
                PresentationEventKind.MovePathUpdated,
                _waypointEventKeyId,
                actor,
                BuildScopeId(actor, WaypointScopeBase + waypointIndex),
                pointWorldCm,
                WorldUnits.CmToM(WaypointRadiusCm),
                0f,
                0f,
                isPrimary ? 1f : 0f);
        }

        private void TouchActorScopes(Entity actor, int lineCount, int waypointCount, int frameId)
        {
            var key = new ActorKey(actor);
            if (lineCount == 0 && waypointCount == 0)
            {
                ClearActorScopes(actor, frameId);
                return;
            }

            if (_activeScopesByActor.TryGetValue(key, out ActorScopeState previous))
            {
                PublishEndedScopeRange(previous.Actor, LineScopeBase, lineCount, previous.LineCount, _lineEventKeyId);
                PublishEndedScopeRange(previous.Actor, WaypointScopeBase, waypointCount, previous.WaypointCount, _waypointEventKeyId);
            }

            _activeScopesByActor[key] = new ActorScopeState
            {
                Actor = actor,
                LineCount = lineCount,
                WaypointCount = waypointCount,
                TouchedFrame = frameId,
            };
        }

        private void ClearActorScopes(Entity actor, int frameId)
        {
            var key = new ActorKey(actor);
            if (!_activeScopesByActor.TryGetValue(key, out ActorScopeState state))
            {
                return;
            }

            PublishEndedScopes(state.Actor, state.LineCount, state.WaypointCount);
            _activeScopesByActor.Remove(key);
        }

        private void EndUntouchedScopes(int frameId)
        {
            _actorRemovalScratch.Clear();
            foreach (KeyValuePair<ActorKey, ActorScopeState> pair in _activeScopesByActor)
            {
                if (pair.Value.TouchedFrame == frameId)
                {
                    continue;
                }

                PublishEndedScopes(pair.Value.Actor, pair.Value.LineCount, pair.Value.WaypointCount);
                _actorRemovalScratch.Add(pair.Key);
            }

            for (int i = 0; i < _actorRemovalScratch.Count; i++)
            {
                _activeScopesByActor.Remove(_actorRemovalScratch[i]);
            }
        }

        private void PublishEndedScopes(Entity actor, int lineCount, int waypointCount)
        {
            PublishEndedScopeRange(actor, LineScopeBase, 0, lineCount, _lineEventKeyId);
            PublishEndedScopeRange(actor, WaypointScopeBase, 0, waypointCount, _waypointEventKeyId);
        }

        private void PublishEndedScopeRange(Entity actor, int scopeBase, int startIndexInclusive, int endIndexExclusive, int eventKeyId)
        {
            for (int i = startIndexInclusive; i < endIndexExclusive; i++)
            {
                PublishMovePathEvent(
                    PresentationEventKind.MovePathEnded,
                    eventKeyId,
                    actor,
                    BuildScopeId(actor, scopeBase + i),
                    Vector3.Zero,
                    0f,
                    0f,
                    0f,
                    0f);
            }
        }

        private void PublishMovePathEvent(
            PresentationEventKind kind,
            int keyId,
            Entity actor,
            int scopeId,
            Vector3 worldCm,
            float floatA,
            float floatB,
            float floatC,
            float magnitude)
        {
            var evt = new PresentationEvent
            {
                LogicTickStamp = _session?.CurrentTick ?? 0,
                Kind = kind,
                KeyId = keyId,
                Source = actor,
                Target = actor,
                Viewer = actor,
                PayloadA = scopeId,
                PayloadB = 0,
                Magnitude = magnitude,
                FloatA = floatA,
                FloatB = floatB,
                FloatC = floatC,
                FloatD = magnitude,
                Position = ToVisualMeters(worldCm),
            };

            if (!_events!.TryAdd(in evt))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing command actor move path event.");
            }
        }

        private void PublishDebugState(
            int commandActorCount,
            int emittedLines,
            int emittedWaypoints,
            Entity collectionOwner,
            string collectionKey,
            Entity collectionContext,
            Entity primaryViewed)
        {
            string summary = BuildDebugSummary(
                commandActorCount,
                emittedLines,
                emittedWaypoints,
                collectionOwner,
                collectionKey,
                collectionContext,
                primaryViewed);
            _globals[DebugSummaryKey] = summary;
        }

        private string BuildDebugSummary(
            int commandActorCount,
            int emittedLines,
            int emittedWaypoints,
            Entity collectionOwner,
            string collectionKey,
            Entity collectionContext,
            Entity primaryViewed)
        {
            Entity inspected = primaryViewed;

            bool hasOrderBuffer = inspected != Entity.Null && _world.IsAlive(inspected) && _world.Has<OrderBuffer>(inspected);
            bool hasPosition2D = inspected != Entity.Null && _world.IsAlive(inspected) && _world.Has<Position2D>(inspected);
            int activeMoveCount = 0;
            int queuedMoveCount = 0;
            string positionSummary = "pos2D=(none)";
            string worldSummary = "world=(none)";
            if (hasOrderBuffer && TryResolveMoveOrderTypeIds(out int[] moveOrderTypeIds))
            {
                ref var buffer = ref _world.Get<OrderBuffer>(inspected);
                if (buffer.HasActive && IsMoveOrderType(buffer.ActiveOrder.Order.OrderTypeId, moveOrderTypeIds))
                {
                    activeMoveCount = 1;
                }

                for (int i = 0; i < buffer.QueuedCount; i++)
                {
                    if (IsMoveOrderType(buffer.GetQueued(i).Order.OrderTypeId, moveOrderTypeIds))
                    {
                        queuedMoveCount++;
                    }
                }
            }

            if (inspected != Entity.Null && _world.IsAlive(inspected))
            {
                if (_world.TryGet(inspected, out Position2D position2D))
                {
                    positionSummary = $"pos2D=({position2D.Value.X.ToFloat():0.#},{position2D.Value.Y.ToFloat():0.#})";
                }

                if (_world.TryGet(inspected, out WorldPositionCm worldPosition))
                {
                    Vector2 world = worldPosition.Value.ToVector2();
                    worldSummary = $"world=({world.X:0.#},{world.Y:0.#})";
                }
            }

            return
                $"{_lastProjectionSummary} " +
                $"owner={DescribeEntity(collectionOwner)} collection={collectionKey} context={DescribeEntity(collectionContext)} " +
                $"commandActorCount={commandActorCount} primary={DescribeEntity(primaryViewed)} " +
                $"inspect={DescribeEntity(inspected)} orderBuf={hasOrderBuffer} activeMove={activeMoveCount} queuedMove={queuedMoveCount} " +
                $"pos2D={hasPosition2D} {positionSummary} {worldSummary} " +
                $"events+line={emittedLines} events+waypoint={emittedWaypoints}";
        }

        private bool TryResolveMoveOrderTypeIds(out int[] moveOrderTypeIds)
        {
            if (_moveOrderTypeIdsResolved)
            {
                moveOrderTypeIds = _moveOrderTypeIds;
                return moveOrderTypeIds.Length > 0;
            }

            moveOrderTypeIds = Array.Empty<int>();
            if (!_globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out var configObj) ||
                configObj is not GameConfig config)
            {
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.OrderTypeRegistry.Name, out var orderTypesObj) ||
                orderTypesObj is not OrderTypeRegistry orderTypes)
            {
                return false;
            }

            CommandSourceAcquisitionConfig? commandSourceConfig = config.CommandSource;
            if (commandSourceConfig == null)
            {
                throw new InvalidOperationException(
                    "game.commandSource must be configured before CommandActorMovePathPresentationSystem resolves move path preview order types.");
            }

            moveOrderTypeIds = ResolveMoveOrderTypeIds(commandSourceConfig, orderTypes);
            _moveOrderTypeIds = moveOrderTypeIds;
            _moveOrderTypeIdsResolved = true;
            return moveOrderTypeIds.Length > 0;
        }

        private static int[] ResolveMoveOrderTypeIds(CommandSourceAcquisitionConfig config, OrderTypeRegistry orderTypes)
        {
            string[] orderTypeKeys = config.MovePathPreviewOrderTypeKeys;
            if (orderTypeKeys.Length == 0)
            {
                throw new InvalidOperationException(
                    "commandSource.movePathPreviewOrderTypeKeys must explicitly list order types that CommandActorMovePathPresentationSystem may preview.");
            }

            int[] ids = new int[orderTypeKeys.Length];
            int count = 0;
            for (int i = 0; i < orderTypeKeys.Length; i++)
            {
                string orderTypeKey = orderTypeKeys[i];
                if (string.IsNullOrWhiteSpace(orderTypeKey))
                {
                    throw new InvalidOperationException(
                        "commandSource.movePathPreviewOrderTypeKeys must not contain blank order type keys.");
                }

                if (!orderTypes.TryGetId(orderTypeKey, out int orderTypeId) ||
                    orderTypeId <= 0)
                {
                    throw new InvalidOperationException(
                        $"commandSource.movePathPreviewOrderTypeKeys references unknown OrderTypeRegistry key '{orderTypeKey}'.");
                }

                if (Contains(ids.AsSpan(0, count), orderTypeId))
                {
                    throw new InvalidOperationException(
                        $"commandSource.movePathPreviewOrderTypeKeys resolves duplicate order type id {orderTypeId} from key '{orderTypeKey}'.");
                }

                ids[count++] = orderTypeId;
            }

            if (count != ids.Length)
            {
                Array.Resize(ref ids, count);
            }

            return ids;
        }

        private static bool IsMoveOrderType(int orderTypeId, ReadOnlySpan<int> moveOrderTypeIds)
        {
            for (int i = 0; i < moveOrderTypeIds.Length; i++)
            {
                if (moveOrderTypeIds[i] == orderTypeId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(ReadOnlySpan<int> values, int value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private string DescribeEntity(Entity entity)
        {
            if (entity == Entity.Null)
            {
                return "(none)";
            }

            if (!_world.IsAlive(entity))
            {
                return $"#{entity.Id}(dead)";
            }

            if (_world.TryGet(entity, out Name name) && !string.IsNullOrWhiteSpace(name.Value))
            {
                return $"{name.Value}#{entity.Id}";
            }

            return $"#{entity.Id}";
        }

        private static int ResolveEventKeyId(string key)
        {
            int existing = TagRegistry.GetId(key);
            return existing > 0 ? existing : TagRegistry.Register(key);
        }

        private static Vector3 ToVisualMeters(Vector3 worldCm)
        {
            return new Vector3(WorldUnits.CmToM(worldCm.X), OverlayY, WorldUnits.CmToM(worldCm.Z));
        }

        private static int BuildScopeId(Entity owner, int offset)
        {
            unchecked
            {
                int scope = (owner.Id * 100000) + offset;
                return scope <= 0 ? offset : scope;
            }
        }

        private void EnsureCommandActorCapacity(int required)
        {
            if (required <= _commandActors.Length)
            {
                return;
            }

            int next = _commandActors.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _commandActors, next);
        }
    }
}
