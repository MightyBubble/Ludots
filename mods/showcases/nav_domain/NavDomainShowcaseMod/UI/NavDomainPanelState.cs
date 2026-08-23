using Ludots.Core.Navigation.NavMesh.Bake;
using NavDomainShowcaseMod.Runtime;

namespace NavDomainShowcaseMod.UI;

internal sealed class NavDomainPanelState
{
    public NavDomainPanelState(
        float viewportWidth,
        float viewportHeight,
        string statusText,
        int chunkColumns,
        int chunkRows,
        int dirtyChunkCount,
        int paintedChunkCount,
        TerrainBrushMode brushMode,
        float brushRadiusMeters,
        NavBakeEstimateReport? estimate,
        NavBakeSessionOutcome? outcome)
    {
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        StatusText = statusText;
        ChunkColumns = chunkColumns;
        ChunkRows = chunkRows;
        DirtyChunkCount = dirtyChunkCount;
        PaintedChunkCount = paintedChunkCount;
        BrushMode = brushMode;
        BrushRadiusMeters = brushRadiusMeters;
        Estimate = estimate;
        Outcome = outcome;
    }

    public float ViewportWidth { get; }

    public float ViewportHeight { get; }

    public string StatusText { get; }

    public int ChunkColumns { get; }

    public int ChunkRows { get; }

    public int DirtyChunkCount { get; }

    public int PaintedChunkCount { get; }

    public TerrainBrushMode BrushMode { get; }

    public float BrushRadiusMeters { get; }

    public NavBakeEstimateReport? Estimate { get; }

    public NavBakeSessionOutcome? Outcome { get; }
}
