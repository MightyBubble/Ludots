using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.Terrain;

namespace Ludots.Core.Presentation.Terrain
{
    internal sealed class LogicTerrainVisualHeightmapAdapter :
        RegularGridVisualHeightmapRuntimeBase,
        IVisualHeightmapRenderSource
    {
        private const int HeightLevelToCm = 100;
        private readonly LogicTerrainField _terrain;
        private readonly short[] _heightSamplesCm;
        private readonly VisualHeightmapLayerDefinition[] _layers;
        private readonly int _sampleColumns;
        private readonly int _sampleRows;

        public LogicTerrainVisualHeightmapAdapter(LogicTerrainField terrain, int revision = 0)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            if (_terrain.Topology != LogicTerrainTopology.Grid)
            {
                throw new InvalidOperationException(
                    $"LogicTerrain visual adapter only supports Grid topology, got '{_terrain.Topology}'.");
            }

            _sampleColumns = checked(_terrain.WidthCells + 1);
            _sampleRows = checked(_terrain.HeightCells + 1);
            _heightSamplesCm = new short[checked(_sampleColumns * _sampleRows)];
            _layers = new[]
            {
                new VisualHeightmapLayerDefinition(0, "LogicTerrain", 0, _heightSamplesCm.Length)
            };
            Revision = revision;
            RebuildSamples();
        }

        WorldAabbCm IVisualHeightmapRenderSource.Bounds => Bounds;

        protected override int SampleColumns => _sampleColumns;

        protected override int SampleRows => _sampleRows;

        protected override VisualHeightmapLayerDefinition[] Layers => _layers;

        protected override int DefaultLayerIndex => 0;

        int IVisualHeightmapRenderSource.DefaultLayerIndex => DefaultLayerIndex;

        protected override VisualHeightmapInterpolationMode InterpolationMode => VisualHeightmapInterpolationMode.TriangleHeightfield;

        public int ChunkColumns => _terrain.WidthChunks;

        public int ChunkRows => _terrain.HeightChunks;

        public int SamplesPerChunkColumn => _terrain.ChunkSizeCells + 1;

        public int SamplesPerChunkRow => _terrain.ChunkSizeCells + 1;

        public int Revision { get; }

        public bool TryGetChunk(int chunkX, int chunkY, out VisualHeightmapRenderChunk chunk)
        {
            if ((uint)chunkX >= (uint)_terrain.WidthChunks ||
                (uint)chunkY >= (uint)_terrain.HeightChunks)
            {
                chunk = default;
                return false;
            }

            int sampleX = chunkX * _terrain.ChunkSizeCells;
            int sampleY = chunkY * _terrain.ChunkSizeCells;
            int sampleEndX = Math.Min(_sampleColumns - 1, sampleX + _terrain.ChunkSizeCells);
            int sampleEndY = Math.Min(_sampleRows - 1, sampleY + _terrain.ChunkSizeCells);
            int sampleColumns = sampleEndX - sampleX + 1;
            int sampleRows = sampleEndY - sampleY + 1;
            int left = sampleX * _terrain.HorizontalStepCm;
            int top = sampleY * _terrain.VerticalStepCm;
            int right = sampleEndX * _terrain.HorizontalStepCm;
            int bottom = sampleEndY * _terrain.VerticalStepCm;

            chunk = new VisualHeightmapRenderChunk(
                chunkX,
                chunkY,
                new WorldAabbCm(left, top, right - left, bottom - top),
                sampleColumns,
                sampleRows,
                _terrain.HorizontalStepCm,
                _terrain.VerticalStepCm,
                _heightSamplesCm,
                ReadOnlyMemory<ushort>.Empty,
                VisualHeightSampleScale.IdentityCentimeters,
                VisualHeightmapStorageLayout.RowMajorInt16Centimeters,
                _sampleColumns,
                (sampleY * _sampleColumns) + sampleX,
                Revision);
            return true;
        }

        protected override bool TryReadSampleCm(int layerSampleOffset, int globalSampleX, int globalSampleY, out float heightCm)
        {
            heightCm = default;
            if ((uint)globalSampleX >= (uint)_sampleColumns ||
                (uint)globalSampleY >= (uint)_sampleRows)
            {
                return false;
            }

            heightCm = _heightSamplesCm[layerSampleOffset + (globalSampleY * _sampleColumns) + globalSampleX];
            return true;
        }

        protected override WorldAabbCm Bounds => ResolveBounds(_terrain);

        private void RebuildSamples()
        {
            for (int y = 0; y < _sampleRows; y++)
            {
                int cellY = Math.Min(y, _terrain.HeightCells - 1);
                for (int x = 0; x < _sampleColumns; x++)
                {
                    int cellX = Math.Min(x, _terrain.WidthCells - 1);
                    LogicTerrainCell cell = _terrain.GetCell(cellX, cellY);
                    _heightSamplesCm[(y * _sampleColumns) + x] = checked((short)(cell.HeightLevel * HeightLevelToCm));
                }
            }
        }

        private static WorldAabbCm ResolveBounds(LogicTerrainField terrain)
            => new(
                0,
                0,
                checked(terrain.WidthCells * terrain.HorizontalStepCm),
                checked(terrain.HeightCells * terrain.VerticalStepCm));
    }
}
