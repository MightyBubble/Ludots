using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Requests
{
    public struct SurfaceSourceRequest
    {
        public int StableId;
        public int PresenterDefinitionId;
        public int ScopeId;
        public PresenterSurfaceKind SurfaceKind;
        public SurfaceAuthoringBlock Authoring;
        public Vector3 AnchorPosition;
        public LODLevel LodSeed;
    }
}
