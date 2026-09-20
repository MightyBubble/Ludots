using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphQuery;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadGraphEdgeProjectionService
    {
        private readonly Dictionary<string, object> _globals;
        private int[] _candidateNodeIds = Array.Empty<int>();

        public RoadGraphEdgeProjectionService(Dictionary<string, object> globals)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public bool TryProjectNearestRoadPoint(string agentTypeId, in Vector3 worldCm, out GraphEdgeProjection projection)
        {
            projection = default;
            if (!_globals.TryGetValue(CoreServiceKeys.LoadedGraphRuntime.Name, out object? runtimeObj) ||
                runtimeObj is not LoadedGraphRuntime runtime ||
                !_globals.TryGetValue(CoreServiceKeys.PathingConfig.Name, out object? configObj) ||
                configObj is not PathingConfig config)
            {
                return false;
            }

            int projectionRadiusCm = ResolveProjectionRadiusCm(config, agentTypeId);
            if (projectionRadiusCm <= 0 || runtime.CurrentGraph.NodeCount <= 0)
            {
                return false;
            }

            EnsureCandidateCapacity(runtime.CurrentGraph.NodeCount);
            var position = new WorldCmInt2(
                (int)MathF.Round(worldCm.X, MidpointRounding.AwayFromZero),
                (int)MathF.Round(worldCm.Z, MidpointRounding.AwayFromZero));
            return GraphEdgeProjectionQuery.TryProjectNearestEdge(
                runtime.CurrentGraph,
                runtime.CurrentSpatialIndex,
                position,
                projectionRadiusCm,
                _candidateNodeIds,
                out projection);
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
                if (candidate != null &&
                    string.Equals(candidate.Id, agentTypeId, StringComparison.OrdinalIgnoreCase))
                {
                    selected = candidate;
                    break;
                }
            }

            return Math.Max(0, selected.NodeGraph?.ProjectionMaxRadiusCm ?? 0);
        }

        private void EnsureCandidateCapacity(int required)
        {
            if (_candidateNodeIds.Length >= required)
            {
                return;
            }

            int next = _candidateNodeIds.Length == 0 ? 64 : _candidateNodeIds.Length * 2;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _candidateNodeIds, next);
        }
    }

}
