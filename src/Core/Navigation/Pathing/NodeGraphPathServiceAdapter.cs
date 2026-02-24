using System;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class NodeGraphPathServiceAdapter : IPathService
    {
        private readonly NodeGraph _graph;
        private readonly PathStore _store;
        private readonly DefaultTraversalPolicy _policy;

        private NodeGraphPathScratch _scratch;
        private int[] _nodeIdsScratch = Array.Empty<int>();
        private int[] _xScratch = Array.Empty<int>();
        private int[] _yScratch = Array.Empty<int>();

        public NodeGraphPathServiceAdapter(NodeGraph graph, PathStore store)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _policy = new DefaultTraversalPolicy();
            _scratch = new NodeGraphPathScratch();
        }

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            if (request.Domain != PathDomain.NodeGraph)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 2);
                return false;
            }

            if (request.Start.Kind != PathEndpointKind.NodeId || request.Goal.Kind != PathEndpointKind.NodeId)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 3);
                return false;
            }

            int maxExpanded = request.Budget.MaxExpanded > 0 ? request.Budget.MaxExpanded : int.MaxValue;
            int maxPoints = request.Budget.MaxPoints > 0 ? request.Budget.MaxPoints : _store.MaxPointsPerPath;

            EnsureCapacity(ref _nodeIdsScratch, Math.Min(_graph.NodeCount, maxPoints));
            var nodesSpan = _nodeIdsScratch.AsSpan();

            var scratch = _scratch;
            var policy = _policy;
            var r = NodeGraphPathService.FindPathAStar(_graph, request.Start.NodeId, request.Goal.NodeId, nodesSpan, ref scratch, ref policy, maxExpanded);
            _scratch = scratch;

            if (r.Status == GraphPathStatus.Success)
            {
                int count = Math.Min(r.NodeCount, maxPoints);
                if (!_store.TryAllocate(count, out var handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, r.Expanded, errorCode: 4);
                    return true;
                }

                EnsureCapacity(ref _xScratch, count);
                EnsureCapacity(ref _yScratch, count);

                var xs = _graph.PosXcm;
                var ys = _graph.PosYcm;
                for (int i = 0; i < count; i++)
                {
                    int nodeId = _nodeIdsScratch[i];
                    _xScratch[i] = xs[nodeId];
                    _yScratch[i] = ys[nodeId];
                }

                _store.TryWrite(in handle, _xScratch, _yScratch, count);
                result = new PathResult(request.RequestId, request.Actor, PathStatus.Found, handle, r.Expanded, errorCode: 0);
                return true;
            }

            var status = r.Status switch
            {
                GraphPathStatus.NotFound => PathStatus.NoPath,
                GraphPathStatus.OverBudget => PathStatus.BudgetExceeded,
                GraphPathStatus.InvalidInput => PathStatus.InvalidRequest,
                _ => PathStatus.Error
            };

            result = new PathResult(request.RequestId, request.Actor, status, default, r.Expanded, errorCode: (int)r.Status);
            return true;
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
        }

        private static void EnsureCapacity<T>(ref T[] array, int required)
        {
            if (array.Length >= required) return;
            int next = array.Length == 0 ? 4 : array.Length * 2;
            if (next < required) next = required;
            Array.Resize(ref array, next);
        }
    }
}

