using System;
using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class NavMeshPathServiceAdapter : IPathService
    {
        private readonly NavQueryService _navMesh;
        private readonly PathStore _store;

        public NavMeshPathServiceAdapter(NavQueryService navMesh, PathStore store)
        {
            _navMesh = navMesh ?? throw new ArgumentNullException(nameof(navMesh));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            if (request.Domain != PathDomain.NavMesh)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 2);
                return false;
            }

            if (request.Start.Kind != PathEndpointKind.WorldCm || request.Goal.Kind != PathEndpointKind.WorldCm)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 3);
                return false;
            }

            int maxPoints = request.Budget.MaxPoints > 0 ? request.Budget.MaxPoints : _store.MaxPointsPerPath;
            int maxPortals = Math.Max(0, maxPoints - 2);

            var r = _navMesh.TryFindPath(
                startXcm: request.Start.Xcm,
                startZcm: request.Start.Ycm,
                goalXcm: request.Goal.Xcm,
                goalZcm: request.Goal.Ycm,
                maxPortals: maxPortals);

            if (r.Status == NavPathStatus.Ok)
            {
                int count = Math.Min(r.PathXcm.Length, maxPoints);
                if (!_store.TryAllocate(count, out var handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, errorCode: 4);
                    return true;
                }

                _store.TryWrite(in handle, r.PathXcm, r.PathZcm, count);
                result = new PathResult(request.RequestId, request.Actor, PathStatus.Found, handle, expanded: 0, errorCode: 0);
                return true;
            }

            var status = r.Status switch
            {
                NavPathStatus.NotReachable => PathStatus.NoPath,
                NavPathStatus.NotReady => PathStatus.NotReady,
                NavPathStatus.InvalidInput => PathStatus.InvalidRequest,
                _ => PathStatus.Error
            };

            result = new PathResult(request.RequestId, request.Actor, status, default, expanded: 0, errorCode: (int)r.Status);
            return true;
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
        }
    }
}

