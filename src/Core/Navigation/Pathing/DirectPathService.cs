using System;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class DirectPathService : IPathService
    {
        private readonly PathStore _store;

        public DirectPathService(PathStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public bool TrySolve(in PathRequest request, out PathResult result)
        {
            if (request.Domain != PathDomain.Auto ||
                request.Start.Kind != PathEndpointKind.WorldCm ||
                request.Goal.Kind != PathEndpointKind.WorldCm)
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.InvalidRequest, default, 0, errorCode: 41);
                return false;
            }

            if (!_store.TryAllocate(2, out PathHandle handle))
            {
                result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, 0, errorCode: 42);
                return true;
            }

            Span<int> xcm = stackalloc int[2];
            Span<int> ycm = stackalloc int[2];
            xcm[0] = request.Start.Xcm;
            ycm[0] = request.Start.Ycm;
            xcm[1] = request.Goal.Xcm;
            ycm[1] = request.Goal.Ycm;
            if (!_store.TryWrite(in handle, xcm, ycm, 2))
            {
                if (_store.IsAlive(in handle))
                {
                    _store.Release(in handle);
                }

                result = new PathResult(request.RequestId, request.Actor, PathStatus.Error, default, 0, errorCode: 43);
                return false;
            }

            result = new PathResult(
                request.RequestId,
                request.Actor,
                PathStatus.Found,
                handle,
                expanded: 0,
                errorCode: 0,
                resolvedDomain: PathDomain.Auto);
            return true;
        }

        public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
        }
    }
}
