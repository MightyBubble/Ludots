using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;

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

        public bool TryProjectNearestRoadPoint(string agentTypeId, in Vector3 worldCm, out RoadGraphEdgeProjection projection)
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
            GraphQueryResult query = runtime.CurrentSpatialIndex.QueryRadius(position, projectionRadiusCm, _candidateNodeIds);
            if (query.Count <= 0)
            {
                return false;
            }

            NodeGraph graph = runtime.CurrentGraph;
            ReadOnlySpan<int> posX = graph.PosXcm;
            ReadOnlySpan<int> posY = graph.PosYcm;
            float bestDistanceSq = float.MaxValue;
            int bestFromNodeId = -1;
            int bestToNodeId = -1;
            int bestProjectedXcm = 0;
            int bestProjectedYcm = 0;
            float bestT = 0f;

            for (int i = 0; i < query.Count; i++)
            {
                int fromNodeId = _candidateNodeIds[i];
                if (!graph.TryGetOutgoingEdges(fromNodeId, out NodeGraph.EdgeRange edgeRange))
                {
                    continue;
                }

                for (int edgeIndex = edgeRange.Start; edgeIndex < edgeRange.EndExclusive; edgeIndex++)
                {
                    int toNodeId = graph.EdgeTo[edgeIndex];
                    int fromXcm = posX[fromNodeId];
                    int fromYcm = posY[fromNodeId];
                    int toXcm = posX[toNodeId];
                    int toYcm = posY[toNodeId];

                    ProjectPointOnSegment(
                        position.X,
                        position.Y,
                        fromXcm,
                        fromYcm,
                        toXcm,
                        toYcm,
                        out int projectedXcm,
                        out int projectedYcm,
                        out float t);

                    float dx = position.X - projectedXcm;
                    float dy = position.Y - projectedYcm;
                    float distanceSq = (dx * dx) + (dy * dy);
                    if (distanceSq >= bestDistanceSq)
                    {
                        continue;
                    }

                    bestDistanceSq = distanceSq;
                    bestFromNodeId = fromNodeId;
                    bestToNodeId = toNodeId;
                    bestProjectedXcm = projectedXcm;
                    bestProjectedYcm = projectedYcm;
                    bestT = t;
                }
            }

            if (bestFromNodeId < 0 || bestToNodeId < 0)
            {
                return false;
            }

            projection = new RoadGraphEdgeProjection(
                bestFromNodeId,
                bestToNodeId,
                bestProjectedXcm,
                bestProjectedYcm,
                bestDistanceSq,
                bestT);
            return true;
        }

        private static void ProjectPointOnSegment(
            int pointXcm,
            int pointYcm,
            int fromXcm,
            int fromYcm,
            int toXcm,
            int toYcm,
            out int projectedXcm,
            out int projectedYcm,
            out float t)
        {
            float dx = toXcm - fromXcm;
            float dy = toYcm - fromYcm;
            float lengthSq = (dx * dx) + (dy * dy);
            if (lengthSq <= 0.0001f)
            {
                projectedXcm = fromXcm;
                projectedYcm = fromYcm;
                t = 0f;
                return;
            }

            t = Math.Clamp(((pointXcm - fromXcm) * dx + (pointYcm - fromYcm) * dy) / lengthSq, 0f, 1f);
            projectedXcm = (int)MathF.Round(fromXcm + (dx * t), MidpointRounding.AwayFromZero);
            projectedYcm = (int)MathF.Round(fromYcm + (dy * t), MidpointRounding.AwayFromZero);
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

    internal readonly struct RoadGraphEdgeProjection
    {
        public readonly int FromNodeId;
        public readonly int ToNodeId;
        public readonly int ProjectedXcm;
        public readonly int ProjectedYcm;
        public readonly float DistanceSqCm;
        public readonly float SegmentT;

        public RoadGraphEdgeProjection(
            int fromNodeId,
            int toNodeId,
            int projectedXcm,
            int projectedYcm,
            float distanceSqCm,
            float segmentT)
        {
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            ProjectedXcm = projectedXcm;
            ProjectedYcm = projectedYcm;
            DistanceSqCm = distanceSqCm;
            SegmentT = segmentT;
        }
    }
}
