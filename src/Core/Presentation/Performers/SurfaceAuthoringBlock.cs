using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Performers
{
    public enum PerformerSurfaceKind : byte
    {
        SplineRibbon = 0,
        ClosedArea = 1,
        RawProceduralMesh = 2,
    }

    public enum PerformerSurfaceValueSourceKind : byte
    {
        Constant = 0,
        GraphProgram = 1,
    }

    public enum PerformerSurfaceChunkOwnership : byte
    {
        PerChunk = 0,
    }

    public sealed class PerformerSurfaceValueSource
    {
        public PerformerSurfaceValueSourceKind Kind;
        public string Id = string.Empty;
        public int GraphProgramId;
    }

    public sealed class PerformerSurfaceGeometrySource
    {
        public PerformerSurfaceValueSource? ControlPointSource;
        public PerformerSurfaceValueSource? WidthSource;
        public PerformerSurfaceValueSource? FlowDirectionSource;
        public string SegmentationPolicy = string.Empty;
        public PerformerSurfaceValueSource? BoundaryPointSource;
        public string TriangulationPolicy = string.Empty;
        public PerformerSurfaceValueSource? MeshPayloadSource;
    }

    public sealed class PerformerSurfaceChunkBakePolicy
    {
        public bool Enabled = true;
        public PerformerSurfaceChunkOwnership Ownership = PerformerSurfaceChunkOwnership.PerChunk;
        public string ChunkInfluencePolicy = string.Empty;
        public string RebakePolicy = string.Empty;
        public ProceduralMeshUsageHint UsageHint = ProceduralMeshUsageHint.Static;
    }

    public sealed class PerformerSurfaceMaterialSet
    {
        public string PrimaryMaterialId = string.Empty;
        public string SecondaryMaterialId = string.Empty;
        public bool AllowInstanceOverride;
    }

    public sealed class PerformerSurfaceGroundingPolicy
    {
        public string Mode = string.Empty;
    }

    public sealed class SurfaceAuthoringBlock
    {
        public PerformerSurfaceKind Kind;
        public string ProfileId = string.Empty;
        public PerformerSurfaceGeometrySource GeometrySource = new();
        public PerformerSurfaceChunkBakePolicy ChunkBake = new();
        public PerformerSurfaceMaterialSet MaterialSet = new();
        public string LodProfileId = string.Empty;
        public PerformerSurfaceGroundingPolicy Grounding = new();
        public string BoundsPolicy = string.Empty;
    }
}
