using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;

namespace VisualTerrainEditorMod.Runtime;

internal sealed class VisualTerrainAssetDescriptor
{
    public VisualTerrainAssetDescriptor(
        string id,
        string displayName,
        WorldAabbCm bounds,
        int chunkColumns,
        int chunkRows,
        int samplesPerChunkColumn,
        int samplesPerChunkRow,
        int renderColumnsPerChunk,
        int renderRowsPerChunk,
        float defaultHeight01,
        VisualTerrainBindingDescriptor? binding = null,
        VisualHeightmapStorageLayout storageLayout = VisualHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters,
        VisualHeightmapInterpolationMode interpolationMode = VisualHeightmapInterpolationMode.TriangleHeightfield,
        VisualHeightSampleScale? sampleScale = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(displayName));
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }

        if (chunkColumns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkColumns));
        }

        if (chunkRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkRows));
        }

        if (bounds.Width % chunkColumns != 0 || bounds.Height % chunkRows != 0)
        {
            throw new ArgumentException("Bounds must be evenly divisible by chunk grid dimensions.", nameof(bounds));
        }

        if (samplesPerChunkColumn < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(samplesPerChunkColumn));
        }

        if (samplesPerChunkRow < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(samplesPerChunkRow));
        }

        if (renderColumnsPerChunk < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(renderColumnsPerChunk));
        }

        if (renderRowsPerChunk < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(renderRowsPerChunk));
        }

        if (defaultHeight01 is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultHeight01));
        }

        Id = id;
        DisplayName = displayName;
        Bounds = bounds;
        ChunkColumns = chunkColumns;
        ChunkRows = chunkRows;
        SamplesPerChunkColumn = samplesPerChunkColumn;
        SamplesPerChunkRow = samplesPerChunkRow;
        RenderColumnsPerChunk = renderColumnsPerChunk;
        RenderRowsPerChunk = renderRowsPerChunk;
        DefaultHeight01 = defaultHeight01;
        Binding = binding ?? VisualTerrainBindingDescriptor.None;
        StorageLayout = storageLayout;
        InterpolationMode = interpolationMode;
        SampleScale = sampleScale ?? VisualHeightSampleScale.IdentityCentimeters;
        SampleScale.Validate();
    }

    public string Id { get; }

    public string DisplayName { get; }

    public WorldAabbCm Bounds { get; }

    public int ChunkColumns { get; }

    public int ChunkRows { get; }

    public int SamplesPerChunkColumn { get; }

    public int SamplesPerChunkRow { get; }

    public int RenderColumnsPerChunk { get; }

    public int RenderRowsPerChunk { get; }

    public float DefaultHeight01 { get; }

    public VisualTerrainBindingDescriptor Binding { get; }

    public VisualHeightmapStorageLayout StorageLayout { get; }

    public VisualHeightmapInterpolationMode InterpolationMode { get; }

    public VisualHeightSampleScale SampleScale { get; }

    public int DefaultLayerIndex => 0;

    public int ChunkCount => checked(ChunkColumns * ChunkRows);

    public int ChunkWorldWidthCm => Bounds.Width / ChunkColumns;

    public int ChunkWorldHeightCm => Bounds.Height / ChunkRows;

    public int SampleColumns => checked(ChunkColumns * (SamplesPerChunkColumn - 1) + 1);

    public int SampleRows => checked(ChunkRows * (SamplesPerChunkRow - 1) + 1);

    public int RenderColumns => checked(ChunkColumns * (RenderColumnsPerChunk - 1) + 1);

    public int RenderRows => checked(ChunkRows * (RenderRowsPerChunk - 1) + 1);

    public int RuntimeVertexCapacityPerChunk
        => checked(RenderColumnsPerChunk * RenderRowsPerChunk);

    public int RuntimeIndexCapacityPerChunk
        => checked((RenderColumnsPerChunk - 1) * (RenderRowsPerChunk - 1) * 6);

    public int RuntimeVertexCapacity => RuntimeVertexCapacityPerChunk;

    public ChunkedVisualHeightmapDescriptor CreateHeightmapDescriptor()
    {
        return ChunkedVisualHeightmapDescriptor.CreateSingleLayer(
            Bounds,
            ChunkColumns,
            ChunkRows,
            SamplesPerChunkColumn,
            SamplesPerChunkRow,
            layerName: "eroded",
            interpolationMode: InterpolationMode,
            storageLayout: StorageLayout,
            sampleScale: SampleScale);
    }
}
