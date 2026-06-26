namespace Ludots.Core.Presentation.Terrain
{
    public interface IVisualHeightmapMipRenderSource : IVisualHeightmapRenderSource
    {
        int MaxRenderMipLevel { get; }

        bool TryGetChunk(int chunkX, int chunkY, int mipLevel, out VisualHeightmapRenderChunk chunk);
    }
}
