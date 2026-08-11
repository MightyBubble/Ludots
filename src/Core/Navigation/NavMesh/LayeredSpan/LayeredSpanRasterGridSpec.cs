using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Immutable XZ raster grid for layered-span column residency (integer centimeters).
    /// Columns are closed squares: [origin + i*cell, origin + (i+1)*cell] on each axis.
    /// </summary>
    public readonly struct LayeredSpanRasterGridSpec
    {
        public LayeredSpanRasterGridSpec(
            int originXcm,
            int originZcm,
            int cellSizeCm,
            int columnCountX,
            int columnCountZ)
        {
            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm), cellSizeCm, "Cell size must be positive.");
            }

            if (columnCountX <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columnCountX), columnCountX, "Column count X must be positive.");
            }

            if (columnCountZ <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columnCountZ), columnCountZ, "Column count Z must be positive.");
            }

            // Fail fast on overflow before scratch/raster work begins.
            int columnCount = checked(columnCountX * columnCountZ);
            _ = checked(originXcm + checked(columnCountX * cellSizeCm));
            _ = checked(originZcm + checked(columnCountZ * cellSizeCm));

            OriginXcm = originXcm;
            OriginZcm = originZcm;
            CellSizeCm = cellSizeCm;
            ColumnCountX = columnCountX;
            ColumnCountZ = columnCountZ;
            ColumnCount = columnCount;
        }

        public int OriginXcm { get; }

        public int OriginZcm { get; }

        public int CellSizeCm { get; }

        public int ColumnCountX { get; }

        public int ColumnCountZ { get; }

        public int ColumnCount { get; }

        public int ColumnMinXcm(int columnX) => checked(OriginXcm + checked(columnX * CellSizeCm));

        public int ColumnMaxXcm(int columnX) => checked(ColumnMinXcm(columnX) + CellSizeCm);

        public int ColumnMinZcm(int columnZ) => checked(OriginZcm + checked(columnZ * CellSizeCm));

        public int ColumnMaxZcm(int columnZ) => checked(ColumnMinZcm(columnZ) + CellSizeCm);
    }
}
