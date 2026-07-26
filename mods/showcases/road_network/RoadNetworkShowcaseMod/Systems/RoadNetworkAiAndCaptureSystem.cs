using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadNetworkAiAndCaptureSystem : ISystem<float>
    {
        private static readonly QueryDescription FortQuery = new QueryDescription()
            .WithAll<WorldPositionCm, Team, RoadFortTag, RoadFortControlState>();

        private static readonly QueryDescription AiColumnQuery = new QueryDescription()
            .WithAll<WorldPositionCm, Team, RoadColumnTag, RoadAiControlledTag, OrderBuffer>();

        private static readonly QueryDescription ColumnQuery = new QueryDescription()
            .WithAll<WorldPositionCm, Team, RoadColumnTag>();

        private readonly World _world;
        private readonly RoadMoveOrderExpander _expander;
        private readonly int _moveToOrderTypeId;
        private float _aiDecisionCooldown;

        public RoadNetworkAiAndCaptureSystem(
            World world,
            Dictionary<string, object> globals,
            OrderQueue incomingOrders)
        {
            _world = world;
            _expander = new RoadMoveOrderExpander(world, globals, incomingOrders, RoadNetworkShowcaseIds.PathPlannerAgentTypeId, statusKey: string.Empty);
            _moveToOrderTypeId = RoadNetworkOrderTypeIds.Require(globals).MoveTo;
            _aiDecisionCooldown = 0.5f;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            UpdateFortCaptures(dt);
            UpdateAiDispatch(dt);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void UpdateFortCaptures(float dt)
        {
            foreach (ref var fortChunk in _world.Query(in FortQuery))
            {
                Span<WorldPositionCm> fortPositions = fortChunk.GetSpan<WorldPositionCm>();
                Span<Team> fortTeams = fortChunk.GetSpan<Team>();
                Span<RoadFortControlState> fortStates = fortChunk.GetSpan<RoadFortControlState>();

                for (int i = 0; i < fortChunk.Count; i++)
                {
                    ref WorldPositionCm fortPosition = ref fortPositions[i];
                    ref Team fortTeam = ref fortTeams[i];
                    ref RoadFortControlState fortState = ref fortStates[i];

                    int dominatingTeam = 0;
                    bool contested = false;
                    int captureRadiusCm = Math.Max(200, fortState.CaptureRadiusCm);
                    Fix64 radiusSq = Fix64.FromInt(captureRadiusCm * captureRadiusCm);

                    foreach (ref var columnChunk in _world.Query(in ColumnQuery))
                    {
                        Span<WorldPositionCm> columnPositions = columnChunk.GetSpan<WorldPositionCm>();
                        Span<Team> columnTeams = columnChunk.GetSpan<Team>();

                        for (int columnIndex = 0; columnIndex < columnChunk.Count; columnIndex++)
                        {
                            Fix64Vec2 delta = columnPositions[columnIndex].Value - fortPosition.Value;
                            if (delta.LengthSquared() > radiusSq)
                            {
                                continue;
                            }

                            int teamId = columnTeams[columnIndex].Id;
                            if (dominatingTeam == 0)
                            {
                                dominatingTeam = teamId;
                                continue;
                            }

                            if (dominatingTeam != teamId)
                            {
                                contested = true;
                                break;
                            }
                        }

                        if (contested)
                        {
                            break;
                        }
                    }

                    if (contested ||
                        dominatingTeam == 0 ||
                        dominatingTeam == fortState.ControllerTeamId)
                    {
                        fortState.CapturingTeamId = 0;
                        fortState.CaptureProgressSeconds = MathF.Max(0f, fortState.CaptureProgressSeconds - (dt * 1.5f));
                        continue;
                    }

                    if (fortState.CapturingTeamId != dominatingTeam)
                    {
                        fortState.CapturingTeamId = dominatingTeam;
                        fortState.CaptureProgressSeconds = 0f;
                    }

                    fortState.CaptureProgressSeconds += dt;
                    float captureDuration = fortState.CaptureDurationSeconds > 0f ? fortState.CaptureDurationSeconds : 3f;
                    if (fortState.CaptureProgressSeconds < captureDuration)
                    {
                        continue;
                    }

                    fortState.ControllerTeamId = dominatingTeam;
                    fortState.CapturingTeamId = 0;
                    fortState.CaptureProgressSeconds = 0f;
                    fortTeam.Id = dominatingTeam;
                }
            }
        }

        private void UpdateAiDispatch(float dt)
        {
            _aiDecisionCooldown -= dt;
            if (_aiDecisionCooldown > 0f)
            {
                return;
            }

            _aiDecisionCooldown = 1.25f;

            foreach (ref var columnChunk in _world.Query(in AiColumnQuery))
            {
                Span<WorldPositionCm> columnPositions = columnChunk.GetSpan<WorldPositionCm>();
                Span<Team> columnTeams = columnChunk.GetSpan<Team>();
                Span<OrderBuffer> orderBuffers = columnChunk.GetSpan<OrderBuffer>();

                for (int i = 0; i < columnChunk.Count; i++)
                {
                    Entity actor = columnChunk.Entity(i);
                    ref OrderBuffer buffer = ref orderBuffers[i];
                    if (buffer.HasActive || buffer.HasQueued)
                    {
                        continue;
                    }

                    if (!TryFindBestTarget(columnTeams[i].Id, columnPositions[i].Value, out Fix64Vec2 target))
                    {
                        continue;
                    }

                    Order order = new Order
                    {
                        OrderTypeId = _moveToOrderTypeId,
                        PlayerId = columnTeams[i].Id,
                        Actor = actor,
                        SubmitMode = OrderSubmitMode.Immediate
                    };
                    order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
                    order.Args.Spatial.Mode = OrderCollectionMode.Single;
                    order.Args.Spatial.WorldCm = new Vector3(target.X.ToFloat(), 0f, target.Y.ToFloat());

                    _expander.TrySubmit(in order);
                }
            }
        }

        private bool TryFindBestTarget(int teamId, Fix64Vec2 origin, out Fix64Vec2 target)
        {
            target = default;
            bool found = false;
            Fix64 bestDistanceSq = default;

            foreach (ref var fortChunk in _world.Query(in FortQuery))
            {
                Span<WorldPositionCm> fortPositions = fortChunk.GetSpan<WorldPositionCm>();
                Span<RoadFortControlState> fortStates = fortChunk.GetSpan<RoadFortControlState>();

                for (int i = 0; i < fortChunk.Count; i++)
                {
                    if (fortStates[i].ControllerTeamId == teamId)
                    {
                        continue;
                    }

                    Fix64Vec2 delta = fortPositions[i].Value - origin;
                    Fix64 distanceSq = delta.LengthSquared();
                    if (found && distanceSq >= bestDistanceSq)
                    {
                        continue;
                    }

                    bestDistanceSq = distanceSq;
                    target = fortPositions[i].Value;
                    found = true;
                }
            }

            return found;
        }

    }
}
