namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// 地形 chunk 网格数据源合同；Core 侧以 VertexMap 适配实现，引擎画廊可直接程序化生成。
    /// </summary>
    public interface ITerrainChunkMeshSource
    {
        int WidthInChunks { get; }

        int HeightInChunks { get; }

        float ChunkSpacingXMeters { get; }

        float ChunkSpacingYMeters { get; }

        long GetChunkKey(int chunkX, int chunkY);

        void BuildChunk(int chunkX, int chunkY, bool simplifiedCliffs, float heightScale, VertexMapChunkMeshData dst);
    }
}
