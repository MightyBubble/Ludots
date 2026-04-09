using System.Collections.Generic;

namespace VisualTerrainEditorMod.Runtime;

internal sealed class VisualTerrainEditorMapManifest
{
    public int Version { get; set; } = 3;

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

    public int DefaultLayerIndex { get; set; }

    public string StorageLayout { get; set; } = string.Empty;

    public string InterpolationMode { get; set; } = string.Empty;

    public int SampleScaleOffsetCm { get; set; }

    public int SampleScaleUnitsPerSampleNumeratorCm { get; set; }

    public int SampleScaleUnitsPerSampleDenominator { get; set; }

    public string BindingKind { get; set; } = string.Empty;

    public int LogicalColumns { get; set; }

    public int LogicalRows { get; set; }

    public int EditedChunkCount { get; set; }

    public VisualTerrainEditorErosionManifest Erosion { get; set; } = new();

    public List<VisualTerrainEditorChunkManifestEntry> Chunks { get; set; } = new();
}

internal sealed class VisualTerrainEditorChunkManifestEntry
{
    public int ChunkX { get; set; }

    public int ChunkY { get; set; }

    public string File { get; set; } = string.Empty;
}

internal sealed class VisualTerrainEditorErosionManifest
{
    public float Scale { get; set; }

    public float Strength { get; set; }

    public float GullyWeight { get; set; }

    public float Detail { get; set; }

    public float RidgeRounding { get; set; }

    public float CreaseRounding { get; set; }

    public float InputRoundingMultiplier { get; set; }

    public float OctaveRoundingMultiplier { get; set; }

    public float InputOnset { get; set; }

    public float OctaveOnset { get; set; }

    public float RidgeMapInputOnset { get; set; }

    public float RidgeMapOctaveOnset { get; set; }

    public float AssumedSlopeValue { get; set; }

    public float AssumedSlopeMix { get; set; }

    public float CellScale { get; set; }

    public float Normalization { get; set; }

    public int Octaves { get; set; }

    public float Lacunarity { get; set; }

    public float Gain { get; set; }
}
