using System;
using Arch.Core;
using Ludots.Core.Navigation.Pathing;

namespace Ludots.Core.Navigation.GraphQuery
{
    public sealed class GraphHybridRouteBuilder
    {
        private readonly IPathService _pathService;
        private readonly PathStore _pathStore;
        private readonly int[] _legXcm;
        private readonly int[] _legYcm;
        private int _nextRequestId = 1;

        public GraphHybridRouteBuilder(IPathService pathService, PathStore pathStore)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _pathStore = pathStore ?? throw new ArgumentNullException(nameof(pathStore));
            _legXcm = new int[pathStore.MaxPointsPerPath];
            _legYcm = new int[pathStore.MaxPointsPerPath];
        }

        public bool TryStitch(
            Entity actor,
            string agentTypeId,
            ReadOnlySpan<PathEndpoint> waypoints,
            Span<int> outXcm,
            Span<int> outYcm,
            out int count,
            out string failure)
        {
            count = 0;
            failure = string.Empty;

            if (waypoints.Length < 2)
            {
                failure = "Route stitching requires at least two waypoints.";
                return false;
            }

            for (int i = 0; i < waypoints.Length - 1; i++)
            {
                if (!TryAppendLeg(
                        actor,
                        agentTypeId,
                        waypoints[i],
                        waypoints[i + 1],
                        outXcm,
                        outYcm,
                        ref count,
                        out failure))
                {
                    return false;
                }
            }

            return count > 0;
        }

        private bool TryAppendLeg(
            Entity actor,
            string agentTypeId,
            in PathEndpoint start,
            in PathEndpoint goal,
            Span<int> outXcm,
            Span<int> outYcm,
            ref int writeCount,
            out string failure)
        {
            failure = string.Empty;
            var request = new PathRequest(
                _nextRequestId++,
                actor,
                PathDomain.Auto,
                agentTypeId,
                start,
                goal,
                new PathBudget(maxExpanded: 0, maxPoints: Math.Min(outXcm.Length, _pathStore.MaxPointsPerPath)));

            if (!_pathService.TrySolve(in request, out PathResult pathResult) ||
                pathResult.Status != PathStatus.Found ||
                !pathResult.Handle.IsValid)
            {
                failure = DescribeFailure(pathResult.Status, pathResult.ErrorCode);
                return false;
            }

            try
            {
                if (!_pathService.TryCopyPath(in pathResult.Handle, _legXcm, _legYcm, out int legCount) ||
                    legCount <= 0)
                {
                    failure = "Path copy failed.";
                    return false;
                }

                int startIndex = writeCount == 0 ? 0 : 1;
                if (writeCount + (legCount - startIndex) > outXcm.Length ||
                    writeCount + (legCount - startIndex) > outYcm.Length)
                {
                    failure = "Route exceeded output capacity.";
                    return false;
                }

                for (int i = startIndex; i < legCount; i++)
                {
                    outXcm[writeCount] = _legXcm[i];
                    outYcm[writeCount] = _legYcm[i];
                    writeCount++;
                }

                return true;
            }
            finally
            {
                if (_pathStore.IsAlive(pathResult.Handle))
                {
                    _pathStore.Release(pathResult.Handle);
                }
            }
        }

        private static string DescribeFailure(PathStatus status, int errorCode)
        {
            return status switch
            {
                PathStatus.NoPath => "No path was found near the requested destination.",
                PathStatus.NotReady => "Graph chunks are still streaming.",
                PathStatus.BudgetExceeded => "Graph solve hit its path budget.",
                PathStatus.InvalidRequest => $"The path service rejected the request (error {errorCode}).",
                PathStatus.Error => $"The path service returned an error (error {errorCode}).",
                _ => $"The route could not be resolved (error {errorCode})."
            };
        }
    }
}
