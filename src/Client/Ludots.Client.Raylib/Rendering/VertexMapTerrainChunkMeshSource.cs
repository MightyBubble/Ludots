using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class VertexMapTerrainChunkMeshSource : ITerrainChunkMeshSource
    {
        private VertexMapChunkMeshBuilder? _builder;

        public VertexMapTerrainChunkMeshSource(VertexMap? map)
        {
            Map = map;
        }

        public VertexMap? Map { get; set; }

        public int WidthInChunks => Map?.WidthInChunks ?? 0;

        public int HeightInChunks => Map?.HeightInChunks ?? 0;

        public float ChunkSpacingXMeters => HexCoordinates.HexWidth * VertexChunk.ChunkSize;

        public float ChunkSpacingYMeters => HexCoordinates.RowSpacing * VertexChunk.ChunkSize;

        public long GetChunkKey(int chunkX, int chunkY)
        {
            return HexCoordinates.GetChunkKey(chunkX, chunkY);
        }

        public void BuildChunk(int chunkX, int chunkY, bool simplifiedCliffs, float heightScale, VertexMapChunkMeshData dst)
        {
            var map = Map;
            if (map == null)
            {
                dst.Terrain.Clear();
                dst.Water.Clear();
                return;
            }

            _builder ??= new VertexMapChunkMeshBuilder(map);
            _builder.BuildChunk(chunkX, chunkY, 0f, 0f, heightScale, simplifiedCliffs, dst);
        }
    }
}
