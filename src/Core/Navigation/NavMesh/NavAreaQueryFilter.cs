using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// Polygon flag bits cannot name an area id, so an impassable area is rejected here
    /// instead of through DtQueryDefaultFilter exclude flags.
    /// </summary>
    internal sealed class NavAreaQueryFilter : IDtQueryFilter
    {
        private readonly float[] _areaCost;
        private readonly bool[] _passable;
        private readonly int _includeFlags;

        public NavAreaQueryFilter(int includeFlags, float[] areaCost, bool[] passable)
        {
            _includeFlags = includeFlags;
            _areaCost = areaCost;
            _passable = passable;
        }

        public bool PassFilter(long refs, DtMeshTile tile, DtPoly poly)
        {
            if ((poly.flags & _includeFlags) == 0)
            {
                return false;
            }

            int area = poly.GetArea();
            return (uint)area >= (uint)_passable.Length || _passable[area];
        }

        public float GetCost(RcVec3f pa, RcVec3f pb, long prevRef, DtMeshTile prevTile, DtPoly prevPoly, long curRef,
            DtMeshTile curTile, DtPoly curPoly, long nextRef, DtMeshTile nextTile, DtPoly nextPoly)
        {
            return RcVec3f.Distance(pa, pb) * _areaCost[curPoly.GetArea()];
        }
    }
}
