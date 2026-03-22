using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadRouteFollowSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, OrderBuffer, WorldPositionCm, Position2D>();

        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly int _roadMoveFollowOrderTypeId;
        private readonly float _defaultSpeedCmPerSec;
        private readonly int _moveSpeedAttributeId;
        private readonly RoadRouteProfileCatalog _profiles;
        private readonly RoadRouteSelectionStrategy _selection = new();
        private readonly RoadRouteWalkStrategy _walk = new();
        private readonly RoadRouteTimeoutPolicy _timeout = new();
        private readonly RoadRouteArrivalPolicy _arrival = new();
        private readonly RoadRouteRefreshService _refresh;
        private readonly Dictionary<string, object> _globals;

        public RoadRouteFollowSystem(
            World world,
            Dictionary<string, object> globals,
            OrderTypeRegistry orderTypeRegistry,
            OrderQueue orderQueue,
            float defaultSpeedCmPerSec = 600f,
            float stopRadiusCm = 40f) : base(world)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _orderTypeRegistry = orderTypeRegistry ?? throw new ArgumentNullException(nameof(orderTypeRegistry));
            _roadMoveFollowOrderTypeId = ResolveOrderTypeId(globals, RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey);
            _defaultSpeedCmPerSec = Math.Max(0f, defaultSpeedCmPerSec);
            _moveSpeedAttributeId = AttributeRegistry.Register("MoveSpeed");
            _profiles = new RoadRouteProfileCatalog(world);
            _refresh = new RoadRouteRefreshService(world, globals, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
        }

        public override void Update(in float dt)
        {
            if (_roadMoveFollowOrderTypeId <= 0)
            {
                return;
            }

            foreach (ref var chunk in World.Query(in Query))
            {
                var positions = chunk.GetSpan<Position2D>();
                var worldPositions = chunk.GetSpan<WorldPositionCm>();
                var buffers = chunk.GetSpan<OrderBuffer>();
                ref var entityFirst = ref chunk.Entity(0);

                foreach (var index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref var buffer = ref buffers[index];

                    if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTypeId != _roadMoveFollowOrderTypeId)
                    {
                        _walk.Clear(World, entity);
                        ResetRuntimeState(entity);
                        continue;
                    }

                    ref Order activeOrder = ref buffer.ActiveOrder.Order;
                    Fix64Vec2 position = positions[index].Value;
                    RoadRouteExecutionProfile execution = _profiles.ResolveExecution(entity);
                    ref var routeState = ref EnsureBoundRuntimeState(entity, in activeOrder, preserveTimeoutCount: false);

                    if (!_selection.TrySelect(
                            in activeOrder,
                            position,
                            RoadRouteRuntimeBinding.ResolveStartWaypointIndex(in routeState, in activeOrder),
                            execution.WaypointRadiusCm,
                            out RoadRouteSelection selection))
                    {
                        if (!TryContinueOrComplete(entity, ref activeOrder, ref routeState, position, in execution))
                        {
                            CompleteRoadOrder(entity);
                        }
                        continue;
                    }

                    if (selection.Completed)
                    {
                        if (!TryContinueOrComplete(entity, ref activeOrder, ref routeState, position, in execution))
                        {
                            CompleteRoadOrder(entity);
                        }
                        continue;
                    }

                    routeState.CurrentWaypointIndex = selection.WaypointIndex;
                    float speedCmPerSec = ResolveMoveSpeed(entity) * Math.Max(0.1f, execution.SpeedMultiplier);
                    if (!_walk.TryApply(World, entity, selection.Target, speedCmPerSec, execution.WaypointRadiusCm))
                    {
                        _walk.Clear(World, entity);
                        ResetRuntimeState(entity);
                        continue;
                    }

                    if (!_timeout.Update(ref routeState, position, routeState.CurrentWaypointIndex, dt, execution.MinProgressCm, execution.StallTimeoutSeconds))
                    {
                        worldPositions[index].Value = position;
                        continue;
                    }

                    routeState.TimeoutCount++;
                    if (routeState.TimeoutCount > execution.MaxTimeoutRecoveries ||
                        !TryRefreshActiveRoute(entity, ref activeOrder, out string refreshStatus))
                    {
                        WriteStatus($"Road route abandoned after {routeState.TimeoutCount} timeout recovery attempt(s).");
                        CompleteRoadOrder(entity);
                        continue;
                    }

                    WriteStatus($"Road route refreshed after timeout {routeState.TimeoutCount}. {refreshStatus}");
                    RoadRouteRuntimeBinding.Rebind(ref routeState, in activeOrder, preserveTimeoutCount: true);
                    _timeout.Reset(ref routeState, position, routeState.CurrentWaypointIndex);
                }
            }
        }

        private float ResolveMoveSpeed(Entity entity)
        {
            if (_moveSpeedAttributeId != AttributeRegistry.InvalidId &&
                World.TryGet(entity, out AttributeBuffer attributes))
            {
                float configured = attributes.GetCurrent(_moveSpeedAttributeId);
                if (configured > 0f)
                {
                    return configured;
                }
            }

            return _defaultSpeedCmPerSec;
        }

        private bool TryRefreshActiveRoute(Entity entity, ref Order activeOrder, out string status)
        {
            status = string.Empty;
            if (!RoadRouteFinalTargetResolver.TryResolve(in activeOrder, out Vector3 destinationWorldCm))
            {
                return false;
            }

            int existingOrderId = activeOrder.OrderId;
            if (!_refresh.TryRefresh(entity, activeOrder.PlayerId, destinationWorldCm, out Order refreshed, out status))
            {
                return false;
            }

            refreshed.OrderId = existingOrderId;
            activeOrder = refreshed;
            return true;
        }

        private bool TryContinueOrComplete(Entity entity, ref Order activeOrder, ref RoadRouteRuntimeState routeState, Fix64Vec2 position, in RoadRouteExecutionProfile execution)
        {
            if (_arrival.HasReachedFinalTarget(in activeOrder, position, execution.FinalArrivalRadiusCm))
            {
                return false;
            }

            if (!TryRefreshActiveRoute(entity, ref activeOrder, out string refreshStatus))
            {
                WriteStatus("Road route ended before reaching the final destination.");
                return false;
            }

            WriteStatus(refreshStatus);
            RoadRouteRuntimeBinding.Rebind(ref routeState, in activeOrder, preserveTimeoutCount: true);
            _timeout.Reset(ref routeState, position, routeState.CurrentWaypointIndex);
            return true;
        }

        private void CompleteRoadOrder(Entity entity)
        {
            _walk.Clear(World, entity);
            ResetRuntimeState(entity);
            OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
        }

        private ref RoadRouteRuntimeState EnsureRuntimeState(Entity entity)
        {
            if (!World.Has<RoadRouteRuntimeState>(entity))
            {
                World.Add(entity, default(RoadRouteRuntimeState));
            }

            return ref World.Get<RoadRouteRuntimeState>(entity);
        }

        private ref RoadRouteRuntimeState EnsureBoundRuntimeState(Entity entity, in Order activeOrder, bool preserveTimeoutCount)
        {
            ref var state = ref EnsureRuntimeState(entity);
            if (!RoadRouteRuntimeBinding.Matches(in state, in activeOrder))
            {
                RoadRouteRuntimeBinding.Rebind(ref state, in activeOrder, preserveTimeoutCount);
            }

            return ref state;
        }

        private void ResetRuntimeState(Entity entity)
        {
            if (!World.Has<RoadRouteRuntimeState>(entity))
            {
                return;
            }

            ref var state = ref World.Get<RoadRouteRuntimeState>(entity);
            _timeout.Clear(ref state);
        }

        private void WriteStatus(string message)
        {
            _globals[RoadMoveOrderExpander.LastSubmitStatusKey] = message;
        }

        private static int ResolveOrderTypeId(IReadOnlyDictionary<string, object> globals, string key)
        {
            if (!globals.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) ||
                configObj is not GameConfig config ||
                !config.Constants.OrderTypeIds.TryGetValue(key, out int orderTypeId) ||
                orderTypeId <= 0)
            {
                throw new InvalidOperationException($"RoadNetworkShowcaseMod requires game.json constants.orderTypeIds.{key} to be configured.");
            }

            return orderTypeId;
        }
    }
}
