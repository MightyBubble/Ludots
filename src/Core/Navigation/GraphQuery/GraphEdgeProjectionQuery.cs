using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.GraphQuery
{
    public readonly struct GraphEdgeProjection
    {
        public readonly int FromNodeId;
        public readonly int ToNodeId;
        public readonly int ProjectedXcm;
        public readonly int ProjectedYcm;
        public readonly float DistanceSqCm;
        public readonly float SegmentT;

        public GraphEdgeProjection(
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

    public static class GraphEdgeProjectionQuery
    {
        public static bool TryProjectNearestEdge(
            NodeGraph graph,
            INodeGraphSpatialIndex index,
            WorldCmInt2 position,
            int radiusCm,
            Span<int> candidateScratch,
            out GraphEdgeProjection projection)
        {
            projection = default;
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (radiusCm <= 0 || graph.NodeCount <= 0 || candidateScratch.Length == 0)
            {
                return false;
            }

            GraphQueryResult query = index.QueryRadius(position, radiusCm, candidateScratch);
            if (query.Count <= 0)
            {
                return false;
            }

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
                int fromNodeId = candidateScratch[i];
                if (!graph.TryGetOutgoingEdges(fromNodeId, out NodeGraph.EdgeRange edgeRange))
                {
                    continue;
                }

                for (int edgeIndex = edgeRange.Start; edgeIndex < edgeRange.EndExclusive; edgeIndex++)
                {
                    int toNodeId = graph.EdgeTo[edgeIndex];
                    ProjectPointOnSegment(
                        position.X,
                        position.Y,
                        posX[fromNodeId],
                        posY[fromNodeId],
                        posX[toNodeId],
                        posY[toNodeId],
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

            projection = new GraphEdgeProjection(
                bestFromNodeId,
                bestToNodeId,
                bestProjectedXcm,
                bestProjectedYcm,
                bestDistanceSq,
                bestT);
            return true;
        }

        public static void ProjectPointOnSegment(
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
    }
}
