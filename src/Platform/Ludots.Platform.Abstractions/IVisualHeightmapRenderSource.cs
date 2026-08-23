
namespace Ludots.Platform.Abstractions
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

    public interface IVisualHeightmapRenderPresentation
    {
        float RenderDisplayHeightScale { get; }

        float RenderColorContrast { get; }

        bool RenderFlatOverview { get; }

        VisualHeightmapRenderColorMode RenderColorMode { get; }

        bool RenderUseAbsoluteHeightColorRange { get; }

        float RenderMinHeightCm { get; }

        float RenderMaxHeightCm { get; }

        int RenderPresentationRevision { get; }
    }
}
