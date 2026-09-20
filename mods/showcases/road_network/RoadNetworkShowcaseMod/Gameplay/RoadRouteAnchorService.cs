using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteAnchorService
    {
        private readonly Dictionary<string, object> _globals;

        public RoadRouteAnchorService(Dictionary<string, object> globals)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public bool TryResolve(string agentTypeId, in Vector3 originWorldCm, in Vector3 goalWorldCm, out RoadRouteAnchors anchors)
        {
            anchors = default;
            if (!_globals.TryGetValue(CoreServiceKeys.LoadedGraphRuntime.Name, out object? runtimeObj) ||
                runtimeObj is not LoadedGraphRuntime runtime ||
                !_globals.TryGetValue(CoreServiceKeys.PathingConfig.Name, out object? configObj) ||
                configObj is not PathingConfig config)
            {
                return false;
            }

            int projectionRadiusCm = ResolveProjectionRadiusCm(config, agentTypeId);
            if (projectionRadiusCm <= 0 ||
                !TryResolveAnchor(runtime, originWorldCm, projectionRadiusCm, out RoadRouteAnchor originAnchor) ||
                !TryResolveAnchor(runtime, goalWorldCm, projectionRadiusCm, out RoadRouteAnchor goalAnchor))
            {
                return false;
            }

            anchors = new RoadRouteAnchors(originAnchor, goalAnchor);
            return true;
        }

        private static bool TryResolveAnchor(LoadedGraphRuntime runtime, in Vector3 worldCm, int projectionRadiusCm, out RoadRouteAnchor anchor)
        {
            anchor = default;
            int worldXcm = (int)MathF.Round(worldCm.X, MidpointRounding.AwayFromZero);
            int worldYcm = (int)MathF.Round(worldCm.Z, MidpointRounding.AwayFromZero);
            if (!runtime.TryFindNearestNode(new WorldCmInt2(worldXcm, worldYcm), projectionRadiusCm, out int nodeId, out int distanceSqCm))
            {
                return false;
            }

            NodeGraph graph = runtime.CurrentGraph;
            if ((uint)nodeId >= (uint)graph.NodeCount)
            {
                return false;
            }

            anchor = new RoadRouteAnchor(nodeId, graph.PosXcm[nodeId], graph.PosYcm[nodeId], distanceSqCm);
            return true;
        }

        private static int ResolveProjectionRadiusCm(PathingConfig config, string agentTypeId)
        {
            if (config.AgentTypes == null || config.AgentTypes.Count == 0)
            {
                return 0;
            }

            PathingAgentTypeConfig selected = config.AgentTypes[0];
            for (int i = 0; i < config.AgentTypes.Count; i++)
            {
                PathingAgentTypeConfig candidate = config.AgentTypes[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.Id, agentTypeId, StringComparison.OrdinalIgnoreCase))
                {
                    selected = candidate;
                    break;
                }
            }

            return Math.Max(0, selected.NodeGraph?.ProjectionMaxRadiusCm ?? 0);
        }
    }

    internal readonly struct RoadRouteAnchors
    {
        public readonly RoadRouteAnchor Origin;
        public readonly RoadRouteAnchor Goal;

        public RoadRouteAnchors(RoadRouteAnchor origin, RoadRouteAnchor goal)
        {
            Origin = origin;
            Goal = goal;
        }
    }

    internal readonly struct RoadRouteAnchor
    {
        public readonly int NodeId;
        public readonly int WorldXcm;
        public readonly int WorldYcm;
        public readonly int DistanceSqCm;

        public RoadRouteAnchor(int nodeId, int worldXcm, int worldYcm, int distanceSqCm)
        {
            NodeId = nodeId;
            WorldXcm = worldXcm;
            WorldYcm = worldYcm;
            DistanceSqCm = distanceSqCm;
        }
    }
}
