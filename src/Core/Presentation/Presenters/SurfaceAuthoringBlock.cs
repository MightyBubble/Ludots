using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Presenters
{
    public enum PresenterSurfaceKind : byte
    {
        SplineRibbon = 0,
        ClosedArea = 1,
        RawProceduralMesh = 2,
    }

    public enum PresenterSurfaceValueSourceKind : byte
    {
        Constant = 0,
        GraphProgram = 1,
    }

    public enum PresenterSurfaceChunkOwnership : byte
    {
        PerChunk = 0,
    }

    public sealed class PresenterSurfaceValueSource
    {
        public PresenterSurfaceValueSourceKind Kind;
        public string Id = string.Empty;
        public int GraphProgramId;
    }

    public sealed class PresenterSurfaceGeometrySource
    {
        public PresenterSurfaceValueSource? ControlPointSource;
        public PresenterSurfaceValueSource? WidthSource;
        public PresenterSurfaceValueSource? FlowDirectionSource;
        public string SegmentationPolicy = string.Empty;
        public PresenterSurfaceValueSource? BoundaryPointSource;
        public string TriangulationPolicy = string.Empty;
        public PresenterSurfaceValueSource? MeshPayloadSource;
    }

    public sealed class PresenterSurfaceChunkBakePolicy
    {
        public bool Enabled = true;
        public PresenterSurfaceChunkOwnership Ownership = PresenterSurfaceChunkOwnership.PerChunk;
        public string ChunkInfluencePolicy = string.Empty;
        public string RebakePolicy = string.Empty;
        public ProceduralMeshUsageHint UsageHint = ProceduralMeshUsageHint.Static;
    }

    public sealed class PresenterSurfaceMaterialSet
    {
        public string PrimaryMaterialId = string.Empty;
        public string SecondaryMaterialId = string.Empty;
        public bool AllowInstanceOverride;
    }

    public sealed class PresenterSurfaceGroundingPolicy
    {
        public string Mode = string.Empty;
    }

    public sealed class SurfaceAuthoringBlock
    {
        public PresenterSurfaceKind Kind;
        public string ProfileId = string.Empty;
        public PresenterSurfaceGeometrySource GeometrySource = new();
        public PresenterSurfaceChunkBakePolicy ChunkBake = new();
        public PresenterSurfaceMaterialSet MaterialSet = new();
        public string LodProfileId = string.Empty;
        public PresenterSurfaceGroundingPolicy Grounding = new();
        public string BoundsPolicy = string.Empty;
    }
}
