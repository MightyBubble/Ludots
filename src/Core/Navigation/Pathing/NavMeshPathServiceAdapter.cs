using System;
using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class NavMeshPathServiceAdapter : IPathService, IPathDataRevisionProvider
    {
        private const int DefaultNavMeshSearchBudget = 16_384;

        private readonly NavQueryService _navMesh;
        private readonly PathStore _store;

        public NavMeshPathServiceAdapter(NavQueryService navMesh, PathStore store)
        {
            _navMesh = navMesh ?? throw new ArgumentNullException(nameof(navMesh));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public int DataRevision => _navMesh.DataRevision;

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

            int maxPoints = ResolveMaxOutputPoints(in request);
            int maxPortals = ResolveNavMeshSearchBudget(in request);

            var r = _navMesh.TryFindPath(
                startXcm: request.Start.Xcm,
                startZcm: request.Start.Ycm,
                goalXcm: request.Goal.Xcm,
                goalZcm: request.Goal.Ycm,
                maxPortals: maxPortals);

            if (r.Status == NavPathStatus.Ok)
            {
                if (maxPoints <= 0)
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, errorCode: 4);
                    return true;
                }

                int count = Math.Min(r.PathXcm.Length, maxPoints);
                if (!_store.TryAllocate(count, out var handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, errorCode: 4);
                    return true;
                }

                PathOutputSampler.WritePreservingEndpoints(_store, in handle, r.PathXcm, r.PathZcm, count);
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

        private int ResolveMaxOutputPoints(in PathRequest request)
        {
            int requested = request.Budget.MaxPoints > 0 ? request.Budget.MaxPoints : _store.MaxPointsPerPath;
            return Math.Min(requested, _store.MaxPointsPerPath);
        }

        private static int ResolveNavMeshSearchBudget(in PathRequest request)
        {
            return request.Budget.MaxExpanded > 0
                ? request.Budget.MaxExpanded
                : DefaultNavMeshSearchBudget;
        }
    }
}

