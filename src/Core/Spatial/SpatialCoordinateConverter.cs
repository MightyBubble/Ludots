using System;
using System.Numerics;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public sealed class SpatialCoordinateConverter : ISpatialCoordinateConverter
    {
        public int GridCellSizeCm { get; }

        public SpatialCoordinateConverter(WorldSizeSpec spec)
        {
            GridCellSizeCm = spec.GridCellSizeCm;
        }

        public SpatialCoordinateConverter(int gridCellSizeCm = 100)
        {
            if (gridCellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(gridCellSizeCm));
            GridCellSizeCm = gridCellSizeCm;
        }

        public WorldCmInt2 GridToWorld(in IntVector2 grid)
        {
            int half = GridCellSizeCm / 2;
            return new WorldCmInt2(grid.X * GridCellSizeCm + half, grid.Y * GridCellSizeCm + half);
        }

        public IntVector2 WorldToGrid(in WorldCmInt2 world)
        {
            return new IntVector2(MathUtil.FloorDiv(world.X, GridCellSizeCm), MathUtil.FloorDiv(world.Y, GridCellSizeCm));
        }

        public WorldCmInt2 HexToWorld(in HexCoordinates hex)
        {
            Vector3 p = hex.ToWorldPositionCm();
            int xCm = (int)MathF.Round(p.X);
            int yCm = (int)MathF.Round(p.Z);
            return new WorldCmInt2(xCm, yCm);
        }

        public HexCoordinates WorldToHex(in WorldCmInt2 world)
        {
            return HexCoordinates.FromWorldPositionCm(new Vector3(world.X, 0f, world.Y));
        }

    }
}
