
namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Cross-adapter render contract for map-owned visual terrain. Adapters render chunks
    /// from this source; they must not invent height semantics outside IContinuousHeightmap.
    /// </summary>
    public interface IContinuousHeightmapRenderSource
    {
        WorldAabbCm Bounds { get; }

        int ChunkColumns { get; }

        int ChunkRows { get; }

        int SamplesPerChunkColumn { get; }

        int SamplesPerChunkRow { get; }

        int DefaultLayerIndex { get; }

        int Revision { get; }

        ContinuousHeightmapRenderProfile RenderProfile { get; }

        bool TryGetChunk(int chunkX, int chunkY, out ContinuousHeightmapRenderChunk chunk);
    }
}
