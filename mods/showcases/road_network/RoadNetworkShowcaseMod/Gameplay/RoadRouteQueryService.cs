using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteQueryService
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly string _agentTypeId;
        private readonly int[] _pathXcm;
        private readonly int[] _pathYcm;
        private readonly int[] _scratchXcm;
        private readonly int[] _scratchYcm;
        private readonly RoadRouteAnchorService _anchorService;
        private readonly RoadRouteGoalSnapService _goalSnapService;
        private readonly RoadGraphEdgeProjectionService _edgeProjectionService;
        private readonly RoadRouteProfileCatalog _profiles;
        private int _nextRequestId = 1;

        public RoadRouteQueryService(World world, Dictionary<string, object> globals, string agentTypeId, int maxPathPoints)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _agentTypeId = string.IsNullOrWhiteSpace(agentTypeId) ? RoadNetworkShowcaseIds.PathPlannerAgentTypeId : agentTypeId;
            _pathXcm = new int[maxPathPoints];
            _pathYcm = new int[maxPathPoints];
            _scratchXcm = new int[maxPathPoints];
            _scratchYcm = new int[maxPathPoints];
            _anchorService = new RoadRouteAnchorService(globals);
            _goalSnapService = new RoadRouteGoalSnapService();
            _edgeProjectionService = new RoadGraphEdgeProjectionService(globals);
            _profiles = new RoadRouteProfileCatalog(world);
        }

        public bool TryQuery(in Order order, int moveToOrderTypeId, out RoadRouteQueryResult result, out string status)
        {
            result = default;
            status = string.Empty;

            if (!TryResolvePathing(out IPathService? pathService, out PathStore? pathStore))
            {
                status = "Road command rejected: path service is unavailable.";
                return false;
            }
            IPathService resolvedPathService = pathService!;
            PathStore resolvedPathStore = pathStore!;

            if (!ResolveRouteOrigin(order.Actor, moveToOrderTypeId, out Vector3 originWorldCm) ||
                !OrderWorldSpatialResolver.TryResolveMoveDestination(in order, out Vector3 goalWorldCm))
            {
                status = "Road command rejected: could not resolve route origin or target.";
                return false;
            }

            RoadRoutePlannerProfile planner = _profiles.ResolvePlanner(order.Actor);
            RoadNetworkScenarioDefinition? scenario = ResolveScenario();
            Vector3 projectedOriginWorldCm = ProjectNearestRoadPointOrSelf(planner.AgentTypeId, originWorldCm);
            Vector3 projectedGoalWorldCm = ProjectNearestRoadPointOrSelf(planner.AgentTypeId, goalWorldCm);

            PrimeLoadedChunksForSolve(projectedOriginWorldCm, projectedGoalWorldCm, scenario, planner);

            bool anchored = _anchorService.TryResolve(planner.AgentTypeId, projectedOriginWorldCm, projectedGoalWorldCm, out RoadRouteAnchors anchors);
            PathEndpoint start = anchored
                ? PathEndpoint.FromNodeId(anchors.Origin.NodeId)
                : PathEndpoint.FromWorldCm((int)MathF.Round(projectedOriginWorldCm.X), (int)MathF.Round(projectedOriginWorldCm.Z));
            PathEndpoint goal = anchored
                ? PathEndpoint.FromNodeId(anchors.Goal.NodeId)
                : PathEndpoint.FromWorldCm((int)MathF.Round(projectedGoalWorldCm.X), (int)MathF.Round(projectedGoalWorldCm.Z));

            float bestScore = float.MaxValue;
            int bestCount = 0;
            string bestLabel = string.Empty;
            string lastFailure = "no road path was found near the clicked destination. Try clicking closer to a road or fort.";

            TryConsiderVariant(
                in order,
                resolvedPathService,
                resolvedPathStore,
                planner.AgentTypeId,
                start,
                goal,
                projectedGoalWorldCm,
                label: "Direct corridor",
                scoreBiasCm: planner.DirectBiasCm,
                viaCount: 0,
                via0: default,
                via1: default,
                ref bestScore,
                ref bestCount,
                ref bestLabel,
                ref lastFailure);

            if (scenario != null &&
                planner.AllowNorthVariant &&
                TryResolveNorthVariant(scenario, out byte northViaCount, out Vector3 northVia0, out Vector3 northVia1))
            {
                TryConsiderVariant(
                    in order,
                    resolvedPathService,
                    resolvedPathStore,
                    planner.AgentTypeId,
                    start,
                    goal,
                    projectedGoalWorldCm,
                    label: "North corridor",
                    scoreBiasCm: planner.NorthBiasCm,
                    viaCount: northViaCount,
                    via0: northVia0,
                    via1: northVia1,
                    ref bestScore,
                    ref bestCount,
                    ref bestLabel,
                    ref lastFailure);
            }

            if (scenario != null &&
                planner.AllowSouthVariant &&
                TryResolveSouthVariant(scenario, out byte southViaCount, out Vector3 southVia0, out Vector3 southVia1))
            {
                TryConsiderVariant(
                    in order,
                    resolvedPathService,
                    resolvedPathStore,
                    planner.AgentTypeId,
                    start,
                    goal,
                    projectedGoalWorldCm,
                    label: "South corridor",
                    scoreBiasCm: planner.SouthBiasCm,
                    viaCount: southViaCount,
                    via0: southVia0,
                    via1: southVia1,
                    ref bestScore,
                    ref bestCount,
                    ref bestLabel,
                    ref lastFailure);
            }

            if (bestCount <= 0)
            {
                status = $"Road command rejected: {lastFailure}";
                return false;
            }

            result = new RoadRouteQueryResult(_pathXcm, _pathYcm, bestCount, projectedGoalWorldCm);
            status = $"{planner.Label} selected {bestLabel} with {bestCount} sampled point(s).";
            return true;
        }

        private bool TryResolvePathing(out IPathService? pathService, out PathStore? pathStore)
        {
            pathService = null;
            pathStore = null;
            if (!_globals.TryGetValue(CoreServiceKeys.PathService.Name, out object? pathServiceObj) ||
                pathServiceObj is not IPathService resolvedPathService ||
                !_globals.TryGetValue(CoreServiceKeys.PathStore.Name, out object? pathStoreObj) ||
                pathStoreObj is not PathStore resolvedPathStore)
            {
                return false;
            }

            pathService = resolvedPathService;
            pathStore = resolvedPathStore;
            return true;
        }

        private Vector3 ProjectNearestRoadPointOrSelf(string agentTypeId, in Vector3 worldCm)
        {
            if (_edgeProjectionService.TryProjectNearestRoadPoint(agentTypeId, worldCm, out RoadGraphEdgeProjection projection))
            {
                return new Vector3(projection.ProjectedXcm, 0f, projection.ProjectedYcm);
            }

            return worldCm;
        }

        private RoadNetworkScenarioDefinition? ResolveScenario()
        {
            return _globals.TryGetValue(RoadNetworkShowcaseIds.ScenarioServiceKey, out object? scenarioObj)
                ? scenarioObj as RoadNetworkScenarioDefinition
                : null;
        }

        private bool ResolveRouteOrigin(Entity actor, int moveToOrderTypeId, out Vector3 originWorldCm)
        {
            if (OrderWorldSpatialResolver.TryResolveProjectedQueuedOrigin(_world, actor, moveToOrderTypeId, out originWorldCm))
            {
                return true;
            }

            return OrderWorldSpatialResolver.TryGetEntityWorldCm(_world, actor, out originWorldCm);
        }

        private void PrimeLoadedChunksForSolve(
            in Vector3 originWorldCm,
            in Vector3 goalWorldCm,
            RoadNetworkScenarioDefinition? scenario,
            in RoadRoutePlannerProfile planner)
        {
            if (!_globals.TryGetValue(RoadNetworkShowcaseIds.GraphLoadedChunksServiceKey, out object? loadedChunksObj) ||
                loadedChunksObj is not WorldGridLoadedChunks loadedChunks)
            {
                return;
            }

            int minXcm = (int)MathF.Round(Math.Min(originWorldCm.X, goalWorldCm.X), MidpointRounding.AwayFromZero);
            int maxXcm = (int)MathF.Round(Math.Max(originWorldCm.X, goalWorldCm.X), MidpointRounding.AwayFromZero);
            int minYcm = (int)MathF.Round(Math.Min(originWorldCm.Z, goalWorldCm.Z), MidpointRounding.AwayFromZero);
            int maxYcm = (int)MathF.Round(Math.Max(originWorldCm.Z, goalWorldCm.Z), MidpointRounding.AwayFromZero);

            if (scenario != null &&
                planner.AllowNorthVariant &&
                TryResolveNorthVariant(scenario, out byte northViaCount, out Vector3 northVia0, out Vector3 northVia1))
            {
                ExpandBounds(ref minXcm, ref maxXcm, ref minYcm, ref maxYcm, northVia0);
                if (northViaCount > 1)
                {
                    ExpandBounds(ref minXcm, ref maxXcm, ref minYcm, ref maxYcm, northVia1);
                }
            }

            if (scenario != null &&
                planner.AllowSouthVariant &&
                TryResolveSouthVariant(scenario, out byte southViaCount, out Vector3 southVia0, out Vector3 southVia1))
            {
                ExpandBounds(ref minXcm, ref maxXcm, ref minYcm, ref maxYcm, southVia0);
                if (southViaCount > 1)
                {
                    ExpandBounds(ref minXcm, ref maxXcm, ref minYcm, ref maxYcm, southVia1);
                }
            }

            int paddingCm = loadedChunks.ChunkSizeCm * 2;
            int centerXcm = (int)(((long)minXcm + maxXcm) / 2L);
            int centerYcm = (int)(((long)minYcm + maxYcm) / 2L);
            int radiusCm = Math.Max(maxXcm - minXcm, maxYcm - minYcm) / 2;
            radiusCm += paddingCm;

            loadedChunks.Update(centerXcm, centerYcm, radiusCm);
        }

        private void TryConsiderVariant(
            in Order order,
            IPathService pathService,
            PathStore pathStore,
            string agentTypeId,
            in PathEndpoint start,
            in PathEndpoint goal,
            in Vector3 projectedGoalWorldCm,
            string label,
            float scoreBiasCm,
            byte viaCount,
            in Vector3 via0,
            in Vector3 via1,
            ref float bestScore,
            ref int bestCount,
            ref string bestLabel,
            ref string lastFailure)
        {
            if (!TryBuildVariant(
                    in order,
                    pathService,
                    pathStore,
                    agentTypeId,
                    start,
                    goal,
                    projectedGoalWorldCm,
                    scoreBiasCm,
                    viaCount,
                    via0,
                    via1,
                    out float score,
                    out int count,
                    out string failure))
            {
                if (!string.IsNullOrWhiteSpace(failure))
                {
                    lastFailure = failure;
                }

                return;
            }

            if (count <= 0 || score >= bestScore)
            {
                return;
            }

            bestScore = score;
            bestCount = count;
            bestLabel = label;
            Array.Copy(_scratchXcm, _pathXcm, count);
            Array.Copy(_scratchYcm, _pathYcm, count);
        }

        private bool TryBuildVariant(
            in Order order,
            IPathService pathService,
            PathStore pathStore,
            string agentTypeId,
            in PathEndpoint start,
            in PathEndpoint goal,
            in Vector3 projectedGoalWorldCm,
            float scoreBiasCm,
            byte viaCount,
            in Vector3 via0,
            in Vector3 via1,
            out float score,
            out int count,
            out string failure)
        {
            score = float.MaxValue;
            count = 0;
            failure = string.Empty;

            PathEndpoint legStart = start;
            int writeCount = 0;
            if (viaCount > 0)
            {
                PathEndpoint viaEndpoint = ToWorldEndpoint(via0);
                if (!TryAppendLeg(pathService, pathStore, in order, agentTypeId, legStart, viaEndpoint, ref writeCount, out failure))
                {
                    return false;
                }

                legStart = viaEndpoint;
            }

            if (viaCount > 1)
            {
                PathEndpoint viaEndpoint = ToWorldEndpoint(via1);
                if (!TryAppendLeg(pathService, pathStore, in order, agentTypeId, legStart, viaEndpoint, ref writeCount, out failure))
                {
                    return false;
                }

                legStart = viaEndpoint;
            }

            if (!TryAppendLeg(pathService, pathStore, in order, agentTypeId, legStart, goal, ref writeCount, out failure))
            {
                return false;
            }

            int snappedCount = _goalSnapService.SnapToGoalOnPath(projectedGoalWorldCm, _scratchXcm, _scratchYcm, writeCount);
            if (snappedCount <= 0)
            {
                failure = "road-goal snap failed.";
                return false;
            }

            count = snappedCount;
            score = EstimatePathLengthCm(_scratchXcm, _scratchYcm, snappedCount) + scoreBiasCm;
            return true;
        }

        private bool TryAppendLeg(
            IPathService pathService,
            PathStore pathStore,
            in Order order,
            string agentTypeId,
            in PathEndpoint start,
            in PathEndpoint goal,
            ref int writeCount,
            out string failure)
        {
            failure = string.Empty;
            var request = new PathRequest(
                _nextRequestId++,
                order.Actor,
                PathDomain.Auto,
                agentTypeId,
                start,
                goal,
                new PathBudget(maxExpanded: 0, maxPoints: _scratchXcm.Length));

            if (!pathService.TrySolve(in request, out PathResult pathResult) ||
                pathResult.Status != PathStatus.Found ||
                !pathResult.Handle.IsValid)
            {
                failure = DescribeFailure(pathResult.Status, pathResult.ErrorCode);
                return false;
            }

            try
            {
                Span<int> legXcm = stackalloc int[OrderSpatial.MaxPoints];
                Span<int> legYcm = stackalloc int[OrderSpatial.MaxPoints];
                if (!pathService.TryCopyPath(in pathResult.Handle, legXcm, legYcm, out int legCount) ||
                    legCount <= 0)
                {
                    failure = "path copy failed.";
                    return false;
                }

                int startIndex = writeCount == 0 ? 0 : 1;
                if (writeCount + (legCount - startIndex) > _scratchXcm.Length)
                {
                    failure = "road route exceeded showcase path capacity.";
                    return false;
                }

                for (int i = startIndex; i < legCount; i++)
                {
                    _scratchXcm[writeCount] = legXcm[i];
                    _scratchYcm[writeCount] = legYcm[i];
                    writeCount++;
                }

                return true;
            }
            finally
            {
                if (pathStore.IsAlive(pathResult.Handle))
                {
                    pathStore.Release(pathResult.Handle);
                }
            }
        }

        private static PathEndpoint ToWorldEndpoint(in Vector3 worldCm)
        {
            return PathEndpoint.FromWorldCm(
                (int)MathF.Round(worldCm.X, MidpointRounding.AwayFromZero),
                (int)MathF.Round(worldCm.Z, MidpointRounding.AwayFromZero));
        }

        private static bool TryResolveNorthVariant(RoadNetworkScenarioDefinition scenario, out byte viaCount, out Vector3 via0, out Vector3 via1)
        {
            viaCount = 0;
            via0 = default;
            via1 = default;
            if (!scenario.TryGetLandmarkWorldCm(RoadNetworkScenarioDefinition.RoadLandmarkId.NorthPass, out via0) ||
                !scenario.TryGetLandmarkWorldCm(RoadNetworkScenarioDefinition.RoadLandmarkId.NorthWatch, out via1))
            {
                return false;
            }

            viaCount = 2;
            return true;
        }

        private static bool TryResolveSouthVariant(RoadNetworkScenarioDefinition scenario, out byte viaCount, out Vector3 via0, out Vector3 via1)
        {
            viaCount = 0;
            via0 = default;
            via1 = default;
            if (!scenario.TryGetLandmarkWorldCm(RoadNetworkScenarioDefinition.RoadLandmarkId.SouthFord, out via0) ||
                !scenario.TryGetLandmarkWorldCm(RoadNetworkScenarioDefinition.RoadLandmarkId.SouthWatch, out via1))
            {
                return false;
            }

            viaCount = 2;
            return true;
        }

        private static void ExpandBounds(ref int minXcm, ref int maxXcm, ref int minYcm, ref int maxYcm, in Vector3 pointWorldCm)
        {
            int xcm = (int)MathF.Round(pointWorldCm.X, MidpointRounding.AwayFromZero);
            int ycm = (int)MathF.Round(pointWorldCm.Z, MidpointRounding.AwayFromZero);
            minXcm = Math.Min(minXcm, xcm);
            maxXcm = Math.Max(maxXcm, xcm);
            minYcm = Math.Min(minYcm, ycm);
            maxYcm = Math.Max(maxYcm, ycm);
        }

        private static float EstimatePathLengthCm(ReadOnlySpan<int> pathXcm, ReadOnlySpan<int> pathYcm, int count)
        {
            float total = 0f;
            for (int i = 0; i < count - 1; i++)
            {
                float dx = pathXcm[i + 1] - pathXcm[i];
                float dy = pathYcm[i + 1] - pathYcm[i];
                total += MathF.Sqrt((dx * dx) + (dy * dy));
            }

            return total;
        }

        private static string DescribeFailure(PathStatus status, int errorCode)
        {
            return status switch
            {
                PathStatus.NoPath => "no road path was found near the clicked destination. Try clicking closer to a road or fort.",
                PathStatus.NotReady => "road chunks are still streaming. Try again after the camera settles.",
                PathStatus.BudgetExceeded => "road solve hit its path budget.",
                PathStatus.InvalidRequest => $"the path service rejected the road request (error {errorCode}).",
                PathStatus.Error => $"the path service returned an error (error {errorCode}).",
                _ => $"the road route could not be resolved (error {errorCode})."
            };
        }
    }

    internal readonly ref struct RoadRouteQueryResult
    {
        public readonly ReadOnlySpan<int> PathXcm;
        public readonly ReadOnlySpan<int> PathYcm;
        public readonly int Count;
        public readonly Vector3 FinalGoalWorldCm;

        public RoadRouteQueryResult(ReadOnlySpan<int> pathXcm, ReadOnlySpan<int> pathYcm, int count, Vector3 finalGoalWorldCm)
        {
            PathXcm = pathXcm;
            PathYcm = pathYcm;
            Count = count;
            FinalGoalWorldCm = finalGoalWorldCm;
        }
    }
}
