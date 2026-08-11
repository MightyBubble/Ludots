using Arch.Core;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs
{
    /// <summary>
    /// Context passed to all EQS tests, carrying world references and query origin.
    /// </summary>
    public readonly struct EqsContext
    {
        public readonly WorldCmInt2 Origin;
        public readonly World World;
        public readonly ISpatialQueryService? SpatialQueries;
        public readonly InfluenceFieldRegistry? InfluenceFields;

        public EqsContext(
            WorldCmInt2 origin,
            World world,
            ISpatialQueryService? spatialQueries = null,
            InfluenceFieldRegistry? influenceFields = null)
        {
            Origin = origin;
            World = world;
            SpatialQueries = spatialQueries;
            InfluenceFields = influenceFields;
        }
    }
}
