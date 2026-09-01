using System;
using Ludots.Core.Navigation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    public static class LogicTerrainHeightmapExport
    {
        public static ContinuousHeightmapAsset ProjectToAsset(
            LogicTerrainField terrain,
            int heightStepCm,
            string layerName = "base")
        {
            if (terrain == null) throw new ArgumentNullException(nameof(terrain));
            if (terrain.Topology != LogicTerrainTopology.Grid)
            {
                throw new NotSupportedException($"Heightmap export only supports grid logic terrain, got {terrain.Topology}.");
            }
            if (heightStepCm <= 0) throw new ArgumentOutOfRangeException(nameof(heightStepCm));

            int sampleColumns = terrain.WidthCells;
            int sampleRows = terrain.HeightCells;
            short[] samples = new short[checked(sampleColumns * sampleRows)];
            int resolvedOriginXcm = ResolveOriginXcm(terrain);
            int resolvedOriginZcm = ResolveOriginZcm(terrain);

            for (int row = 0; row < sampleRows; row++)
            {
                for (int col = 0; col < sampleColumns; col++)
                {
                    LogicTerrainCell cell = terrain.GetCell(col, row);
                    samples[(row * sampleColumns) + col] = checked((short)(cell.HeightLevel * heightStepCm));
                }
            }

            var bounds = new WorldAabbCm(
                resolvedOriginXcm,
                resolvedOriginZcm,
                checked(sampleColumns * terrain.HorizontalStepCm),
                checked(sampleRows * terrain.VerticalStepCm));

            return ContinuousHeightmapAsset.CreateSingleLayer(
                bounds,
                sampleColumns,
                sampleRows,
                samples,
                layerName);
        }

        private static int ResolveOriginXcm(LogicTerrainField terrain)
            => terrain switch
            {
                MutableGridLogicTerrainField mutable => mutable.OriginXcm,
                FlatGridLogicTerrainField flat => flat.OriginXcm,
                SparseGridLogicTerrainField sparse => sparse.OriginXcm,
                _ => 0
            };

        private static int ResolveOriginZcm(LogicTerrainField terrain)
            => terrain switch
            {
                MutableGridLogicTerrainField mutable => mutable.OriginZcm,
                FlatGridLogicTerrainField flat => flat.OriginZcm,
                SparseGridLogicTerrainField sparse => sparse.OriginZcm,
                _ => 0
            };
    }
}
