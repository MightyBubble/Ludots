using System;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Authoring-time world extent expressed in macro tiles and converted into <see cref="WorldSizeSpec"/>.
    /// </summary>
    public readonly struct WorldExtentSpec : IEquatable<WorldExtentSpec>
    {
        public readonly int WidthInMacroTiles;
        public readonly int HeightInMacroTiles;
        public readonly int CellCm;

        public WorldExtentSpec(int widthInMacroTiles, int heightInMacroTiles, int cellCm)
        {
            if (widthInMacroTiles <= 0) throw new ArgumentOutOfRangeException(nameof(widthInMacroTiles));
            if (heightInMacroTiles <= 0) throw new ArgumentOutOfRangeException(nameof(heightInMacroTiles));
            if (cellCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellCm));

            WidthInMacroTiles = widthInMacroTiles;
            HeightInMacroTiles = heightInMacroTiles;
            CellCm = cellCm;
        }

        public int WidthInCells => checked(WidthInMacroTiles * SpatialScaleDefaults.MacroTileCells);
        public int HeightInCells => checked(HeightInMacroTiles * SpatialScaleDefaults.MacroTileCells);
        public int WidthCm => checked(WidthInCells * CellCm);
        public int HeightCm => checked(HeightInCells * CellCm);

        public WorldSizeSpec ToWorldSizeSpec()
        {
            int widthCm = WidthCm;
            int heightCm = HeightCm;
            return new WorldSizeSpec(
                new WorldAabbCm(-widthCm / 2, -heightCm / 2, widthCm, heightCm),
                CellCm);
        }

        public bool Equals(WorldExtentSpec other)
            => WidthInMacroTiles == other.WidthInMacroTiles &&
               HeightInMacroTiles == other.HeightInMacroTiles &&
               CellCm == other.CellCm;

        public override bool Equals(object obj) => obj is WorldExtentSpec other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(WidthInMacroTiles, HeightInMacroTiles, CellCm);
        public static bool operator ==(WorldExtentSpec left, WorldExtentSpec right) => left.Equals(right);
        public static bool operator !=(WorldExtentSpec left, WorldExtentSpec right) => !left.Equals(right);
        public override string ToString()
            => $"{WidthInMacroTiles}x{HeightInMacroTiles} MacroTiles, Cell={CellCm}cm";
    }
}
