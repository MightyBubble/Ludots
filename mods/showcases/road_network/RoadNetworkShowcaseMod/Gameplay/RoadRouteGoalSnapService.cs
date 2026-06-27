using System.Numerics;
using Ludots.Core.Navigation.GraphQuery;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal sealed class RoadRouteGoalSnapService
    {
        public int SnapToGoalOnPath(in Vector3 goalWorldCm, Span<int> pathXcm, Span<int> pathYcm, int count)
        {
            return PolylineGoalSnapQuery.SnapGoalOntoPolyline(goalWorldCm.X, goalWorldCm.Z, pathXcm, pathYcm, count);
        }
    }
}
