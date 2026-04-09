using System.Collections.Generic;

namespace VisualTerrainEditorMod.Runtime;

internal sealed class VisualTerrainEditorMapManifest
{
    public int Version { get; set; } = 2;

    public string MapId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ChunkDirectory { get; set; } = string.Empty;

    public int BoundsLeftCm { get; set; }

    public int BoundsTopCm { get; set; }

    public int BoundsWidthCm { get; set; }

    public int BoundsHeightCm { get; set; }

    public int ChunkColumns { get; set; }

    public int ChunkRows { get; set; }

    public int SamplesPerChunkColumn { get; set; }

    public int SamplesPerChunkRow { get; set; }

    public int SampleColumns { get; set; }

    public int SampleRows { get; set; }

    public int RenderColumnsPerChunk { get; set; }

    public int RenderRowsPerChunk { get; set; }

    public int RenderColumns { get; set; }

    public int RenderRows { get; set; }

    public float DefaultHeight01 { get; set; }

    public string BindingKind { get; set; } = string.Empty;

    public int LogicalColumns { get; set; }

    public int LogicalRows { get; set; }

    public int EditedChunkCount { get; set; }

    public List<VisualTerrainEditorChunkManifestEntry> Chunks { get; set; } = new();
}

internal sealed class VisualTerrainEditorChunkManifestEntry
{
    public int ChunkX { get; set; }

    public int ChunkY { get; set; }

    public string File { get; set; } = string.Empty;
}
