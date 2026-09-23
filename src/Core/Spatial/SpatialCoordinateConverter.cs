using System;
using System.Numerics;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Board-local topology coordinates to world cm.
    /// The origin is the world position of the corner where cells start.
    /// GridToWorld then steps half a cell to the cell center.
    /// </summary>
    public sealed class SpatialCoordinateConverter : ISpatialCoordinateConverter
    {
        public int GridCellSizeCm { get; }
        public int OriginXCm { get; }
        public int OriginYCm { get; }

        public SpatialCoordinateConverter(WorldSizeSpec spec)
        {
            GridCellSizeCm = spec.GridCellSizeCm;
        }

        public SpatialCoordinateConverter(int gridCellSizeCm = 100, int originXCm = 0, int originYCm = 0)
        {
            if (gridCellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(gridCellSizeCm));
            GridCellSizeCm = gridCellSizeCm;
            OriginXCm = originXCm;
            OriginYCm = originYCm;
        }

        public WorldCmInt2 GridToWorld(in IntVector2 grid)
        {
            int half = GridCellSizeCm / 2;
            return new WorldCmInt2(
                OriginXCm + grid.X * GridCellSizeCm + half,
                OriginYCm + grid.Y * GridCellSizeCm + half);
        }

        public IntVector2 WorldToGrid(in WorldCmInt2 world)
        {
            return new IntVector2(
                MathUtil.FloorDiv(world.X - OriginXCm, GridCellSizeCm),
                MathUtil.FloorDiv(world.Y - OriginYCm, GridCellSizeCm));
        }

        public WorldCmInt2 HexToWorld(in HexCoordinates hex)
        {
            Vector3 p = hex.ToWorldPositionCm();
            int xCm = (int)MathF.Round(p.X);
            int yCm = (int)MathF.Round(p.Z);
            return new WorldCmInt2(OriginXCm + xCm, OriginYCm + yCm);
        }

        public HexCoordinates WorldToHex(in WorldCmInt2 world)
        {
            return HexCoordinates.FromWorldPositionCm(new Vector3(world.X - OriginXCm, 0f, world.Y - OriginYCm));
        }
    }
}
