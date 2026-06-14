using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Requests
{
    public struct SurfaceSourceRequest
    {
        public int StableId;
        public int PerformerDefinitionId;
        public int ScopeId;
        public PerformerSurfaceKind SurfaceKind;
        public SurfaceAuthoringBlock Authoring;
        public Vector3 AnchorPosition;
        public LODLevel LodSeed;
    }
}
