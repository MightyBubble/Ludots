using System;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.MultiLayerGraph
{
    public static class HierarchicalPathService
    {
        public static GraphPathResult FindPathHierarchical<TFinePolicy, TCoarsePolicy>(
            MultiLayerGraph graph,
            int fineLayerIndex,
            int startFineNodeId,
            int goalFineNodeId,
            Span<int> outFinePathNodeIds,
            ref NodeGraphPathScratch fineScratch,
            ref NodeGraphPathScratch coarseScratch,
            ref TFinePolicy finePolicy,
            ref TCoarsePolicy coarsePolicy,
            int maxExpandedFine = int.MaxValue,
            int maxExpandedCoarse = int.MaxValue)
            where TFinePolicy : struct, INodeGraphTraversalPolicy
            where TCoarsePolicy : struct, INodeGraphTraversalPolicy
        {
            var fine = graph.GetLayer(fineLayerIndex);
            if (fineLayerIndex <= 0) return new GraphPathResult(GraphPathStatus.InvalidInput, 0, 0, 0);
            var coarse = graph.GetLayer(fineLayerIndex - 1);
            var mapping = graph.GetMappingFineToCoarse(fineLayerIndex);

            int coarseStart = mapping.GetCoarseOfFine(startFineNodeId);
            int coarseGoal = mapping.GetCoarseOfFine(goalFineNodeId);
            if ((uint)coarseStart >= (uint)coarse.NodeCount) return new GraphPathResult(GraphPathStatus.InvalidInput, 0, 0, 0);
            if ((uint)coarseGoal >= (uint)coarse.NodeCount) return new GraphPathResult(GraphPathStatus.InvalidInput, 0, 0, 0);

            Span<int> coarsePath = coarse.NodeCount <= 1024 ? stackalloc int[coarse.NodeCount] : new int[coarse.NodeCount];
            var coarseResult = NodeGraphPathService.FindPathAStar(coarse, coarseStart, coarseGoal, coarsePath, ref coarseScratch, ref coarsePolicy, maxExpandedCoarse);
            if (coarseResult.Status != GraphPathStatus.Success) return coarseResult;

            Span<int> segment = fine.NodeCount <= 2048 ? stackalloc int[fine.NodeCount] : new int[fine.NodeCount];

            int written = 0;
            int expandedTotal = coarseResult.Expanded;

            int currentFine = startFineNodeId;
            if (outFinePathNodeIds.Length == 0) return new GraphPathResult(GraphPathStatus.BufferTooSmall, 0, 1, expandedTotal);
            outFinePathNodeIds[0] = currentFine;
            written = 1;

            for (int i = 1; i < coarseResult.NodeCount; i++)
            {
                int targetCoarse = coarsePath[i];
                int targetFine = PickGatewayOrFallback(fine, mapping, targetCoarse, goalFineNodeId);
                if (targetFine < 0) return new GraphPathResult(GraphPathStatus.NotFound, 0, 0, expandedTotal);

                var segResult = NodeGraphPathService.FindPathAStar(fine, currentFine, targetFine, segment, ref fineScratch, ref finePolicy, maxExpandedFine);
                expandedTotal += segResult.Expanded;
                if (segResult.Status != GraphPathStatus.Success) return new GraphPathResult(segResult.Status, 0, segResult.RequiredNodeCount, expandedTotal);

                if (!Append(segment, segResult.NodeCount, outFinePathNodeIds, ref written, skipFirst: true))
                {
                    return new GraphPathResult(GraphPathStatus.BufferTooSmall, 0, written + (segResult.NodeCount - 1), expandedTotal);
                }

                currentFine = targetFine;
            }

            if (currentFine != goalFineNodeId)
            {
                var segResult = NodeGraphPathService.FindPathAStar(fine, currentFine, goalFineNodeId, segment, ref fineScratch, ref finePolicy, maxExpandedFine);
                expandedTotal += segResult.Expanded;
                if (segResult.Status != GraphPathStatus.Success) return new GraphPathResult(segResult.Status, 0, segResult.RequiredNodeCount, expandedTotal);
                if (!Append(segment, segResult.NodeCount, outFinePathNodeIds, ref written, skipFirst: true))
                {
                    return new GraphPathResult(GraphPathStatus.BufferTooSmall, 0, written + (segResult.NodeCount - 1), expandedTotal);
                }
            }

            return new GraphPathResult(GraphPathStatus.Success, written, written, expandedTotal);
        }

        private static int PickGatewayOrFallback(NodeGraph fine, InterLayerMapping mapping, int coarseNodeId, int preferFineNodeId)
        {
            var gateways = mapping.GetGatewaysOfCoarse(coarseNodeId);
            if (gateways.Count == 0) return -1;

            var xs = fine.PosXcm;
            var ys = fine.PosYcm;

            int px = xs[preferFineNodeId];
            int py = ys[preferFineNodeId];

            int best = gateways[0];
            int bestD2 = int.MaxValue;
            for (int i = 0; i < gateways.Count; i++)
            {
                int n = gateways[i];
                int dx = xs[n] - px;
                int dy = ys[n] - py;
                int d2 = dx * dx + dy * dy;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = n;
                }
            }

            return best;
        }

        private static bool Append(Span<int> segment, int count, Span<int> dest, ref int destCount, bool skipFirst)
        {
            int start = skipFirst ? 1 : 0;
            for (int i = start; i < count; i++)
            {
                if ((uint)destCount >= (uint)dest.Length) return false;
                dest[destCount++] = segment[i];
            }
            return true;
        }
    }
}

