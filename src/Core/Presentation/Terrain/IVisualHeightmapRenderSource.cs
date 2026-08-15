using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Terrain
{
    /// <summary>
    /// Cross-adapter render contract for map-owned visual terrain. Adapters render chunks
    /// from this source; they must not invent height semantics outside IVisualHeightmap.
    /// </summary>
    public interface IVisualHeightmapRenderSource
    {
        WorldAabbCm Bounds { get; }

        int ChunkColumns { get; }

        int ChunkRows { get; }

        int SamplesPerChunkColumn { get; }

        int SamplesPerChunkRow { get; }

        int DefaultLayerIndex { get; }

        int Revision { get; }

        VisualHeightmapRenderProfile RenderProfile { get; }

        bool TryGetChunk(int chunkX, int chunkY, out VisualHeightmapRenderChunk chunk);
    }
}
