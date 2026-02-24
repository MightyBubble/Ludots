using System;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class PathServiceRouter : IPathService
    {
        private readonly IPathService _nodeGraph;
        private readonly IPathService _navMesh;
        private readonly IPathService _auto;
        private readonly PathStore _store;

        public PathServiceRouter(IPathService nodeGraph, IPathService navMesh, PathStore store)
        {
            _nodeGraph = nodeGraph ?? throw new ArgumentNullException(nameof(nodeGraph));
            _navMesh = navMesh ?? throw new ArgumentNullException(nameof(navMesh));
            _auto = null;
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public PathServiceRouter(IPathService nodeGraph, IPathService navMesh, IPathService auto, PathStore store)
        {
            _nodeGraph = nodeGraph ?? throw new ArgumentNullException(nameof(nodeGraph));
            _navMesh = navMesh ?? throw new ArgumentNullException(nameof(navMesh));
            _auto = auto ?? throw new ArgumentNullException(nameof(auto));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            return request.Domain switch
            {
                PathDomain.NodeGraph => _nodeGraph.TrySolve(in request, out result),
                PathDomain.NavMesh => _navMesh.TrySolve(in request, out result),
                PathDomain.Auto => _auto != null ? _auto.TrySolve(in request, out result) : FailInvalid(in request, out result),
                _ => FailInvalid(in request, out result)
            };
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
        }

        private static bool FailInvalid(in PathRequest request, out PathResult result)
        {
            result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 1);
            return false;
        }
    }
}
