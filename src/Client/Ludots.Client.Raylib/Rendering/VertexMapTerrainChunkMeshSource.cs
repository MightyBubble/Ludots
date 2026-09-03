using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed class VertexMapTerrainChunkMeshSource : ITerrainChunkMeshSource
    {
        private VertexMapChunkMeshBuilder? _builder;
        private VertexMap? _builderMap;
        private VertexMapHexLayout _builderLayout;

        public VertexMapTerrainChunkMeshSource(VertexMap? map, VertexMapHexLayout layout)
        {
            Map = map;
            Layout = layout;
        }

        public VertexMap? Map { get; set; }

        public VertexMapHexLayout Layout { get; set; }

        public int WidthInChunks => Map?.WidthInChunks ?? 0;

        public int HeightInChunks => Map?.HeightInChunks ?? 0;

        public float ChunkSpacingXMeters => Layout.ColPitchMeters * VertexChunk.ChunkSize;

        public float ChunkSpacingYMeters => Layout.RowPitchMeters * VertexChunk.ChunkSize;

        public float ChunkOriginXMeters => Layout.OriginXMeters;

        public float ChunkOriginYMeters => Layout.OriginZMeters;

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

            if (_builder == null || !ReferenceEquals(_builderMap, map) || _builderLayout != Layout)
            {
                _builder = new VertexMapChunkMeshBuilder(map, Layout);
                _builderMap = map;
                _builderLayout = Layout;
            }

            _builder.BuildChunk(chunkX, chunkY, heightScale, simplifiedCliffs, dst);
        }
    }
}
