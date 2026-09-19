using System;
using Ludots.Core.Config;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// World extent authored in centimeters (#1567: the only world-size authority).
    /// Produces <see cref="WorldSizeSpec"/>; macro-tile counts are derived IO details.
    /// </summary>
    public readonly struct WorldExtentSpec : IEquatable<WorldExtentSpec>
    {
        public readonly int WidthCm;
        public readonly int HeightCm;
        public readonly int CellCm;

        public WorldExtentSpec(int widthCm, int heightCm, int cellCm)
        {
            if (widthCm <= 0) throw new ArgumentOutOfRangeException(nameof(widthCm));
            if (heightCm <= 0) throw new ArgumentOutOfRangeException(nameof(heightCm));
            if (cellCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellCm));
            if (widthCm % cellCm != 0 || heightCm % cellCm != 0)
            {
                throw new ArgumentException(
                    $"World extent {widthCm}x{heightCm}cm must be an exact multiple of CellCm {cellCm}.");
            }

            WidthCm = widthCm;
            HeightCm = heightCm;
            CellCm = cellCm;
        }

        public int WidthInCells => WidthCm / CellCm;
        public int HeightInCells => HeightCm / CellCm;

        /// <summary>Macro-tile count for the lazily allocated IO grid; pads partial tiles up.</summary>
        public int WidthInMacroTiles => (WidthInCells + SpatialScaleDefaults.MacroTileCells - 1) / SpatialScaleDefaults.MacroTileCells;

        /// <summary>Macro-tile count for the lazily allocated IO grid; pads partial tiles up.</summary>
        public int HeightInMacroTiles => (HeightInCells + SpatialScaleDefaults.MacroTileCells - 1) / SpatialScaleDefaults.MacroTileCells;

        public static WorldExtentSpec FromWorld(WorldConfig world) =>
            new(world.WidthCm, world.HeightCm, world.CellSizeCm);

        public WorldSizeSpec ToWorldSizeSpec()
        {
            return new WorldSizeSpec(
                new WorldAabbCm(-WidthCm / 2, -HeightCm / 2, WidthCm, HeightCm),
                CellCm);
        }

        public bool Equals(WorldExtentSpec other)
            => WidthCm == other.WidthCm &&
               HeightCm == other.HeightCm &&
               CellCm == other.CellCm;

        public override bool Equals(object obj) => obj is WorldExtentSpec other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(WidthCm, HeightCm, CellCm);
        public static bool operator ==(WorldExtentSpec left, WorldExtentSpec right) => left.Equals(right);
        public static bool operator !=(WorldExtentSpec left, WorldExtentSpec right) => !left.Equals(right);
        public override string ToString()
            => $"{WidthCm}x{HeightCm}cm, Cell={CellCm}cm";
    }
}
