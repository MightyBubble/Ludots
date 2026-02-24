using System;

namespace Ludots.Core.Navigation.Pathing
{
    public interface IPathService
    {
        bool TrySolve(in PathRequest request, out PathResult result);
        bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count);
    }
}

