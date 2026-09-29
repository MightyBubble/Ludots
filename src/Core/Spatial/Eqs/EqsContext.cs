using Arch.Core;
using Ludots.Platform.Abstractions;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.Pathing;

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
        public readonly IPathService? PathService;
        public readonly PathStore? PathStore;
        public readonly WorldCmInt2? SourceWorldCm;
        public readonly Entity SourceEntity;

        public EqsContext(
            WorldCmInt2 origin,
            World world,
            ISpatialQueryService? spatialQueries = null,
            InfluenceFieldRegistry? influenceFields = null,
            IPathService? pathService = null,
            PathStore? pathStore = null,
            WorldCmInt2? sourceWorldCm = null,
            Entity sourceEntity = default)
        {
            Origin = origin;
            World = world;
            SpatialQueries = spatialQueries;
            InfluenceFields = influenceFields;
            PathService = pathService;
            PathStore = pathStore;
            SourceWorldCm = sourceWorldCm;
            SourceEntity = sourceEntity;
        }
    }
}
