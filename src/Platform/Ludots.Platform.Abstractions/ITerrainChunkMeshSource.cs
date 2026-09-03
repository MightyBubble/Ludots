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

        /// <summary>chunk (0,0) 参考角的世界 X（米）；chunk 索引 c 的代理位置 = OriginX + c·SpacingX。</summary>
        float ChunkOriginXMeters { get; }

        /// <summary>chunk (0,0) 参考角的世界 Z（米）；chunk 索引 r 的代理位置 = OriginZ + r·SpacingY。</summary>
        float ChunkOriginYMeters { get; }

        long GetChunkKey(int chunkX, int chunkY);

        void BuildChunk(int chunkX, int chunkY, bool simplifiedCliffs, float heightScale, VertexMapChunkMeshData dst);
    }
}
