using System;
using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Explicit triangulation parameters for layered-span constrained triangulation.
    /// No defaults: every field must be provided and validated.
    /// </summary>
    public readonly struct LayeredSpanTriangulationSpec
    {
        /// <summary>
        /// Demonstrable local |delta| bound (cm) for exact Int128 orientation/incircle predicates
        /// after translating to a predicate origin. Deltas beyond this fail explicitly.
        /// Proven: with |d| &lt;= 2^30, each 4-point incircle expansion term fits in signed Int128.
        /// </summary>
        public const int DemonstrableLocalAbsDeltaCm = 1 << 30;

        public LayeredSpanTriangulationSpec(
            LayeredSpanHeightRounding heightRounding,
            int maxLawsonFlipCount,
            int targetMinXcm,
            int targetMinZcm,
            int targetMaxXcm,
            int targetMaxZcm,
            int cellSizeXcm,
            int cellSizeZcm)
        {
            if (heightRounding != LayeredSpanHeightRounding.FloorTowardNegativeInfinity &&
                heightRounding != LayeredSpanHeightRounding.RoundHalfAwayFromZero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(heightRounding),
                    heightRounding,
                    "heightRounding must be a known LayeredSpanHeightRounding value.");
            }

            if (maxLawsonFlipCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxLawsonFlipCount),
                    maxLawsonFlipCount,
                    "maxLawsonFlipCount must be nonnegative.");
            }

            if (cellSizeXcm <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSizeXcm),
                    cellSizeXcm,
                    "cellSizeXcm must be positive.");
            }

            if (cellSizeZcm <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSizeZcm),
                    cellSizeZcm,
                    "cellSizeZcm must be positive.");
            }

            long widthX = (long)targetMaxXcm - targetMinXcm;
            if (widthX <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetMaxXcm),
                    targetMaxXcm,
                    "targetMaxXcm must be strictly greater than targetMinXcm (LayeredSpanTriangulationSpec.target width X).");
            }

            long widthZ = (long)targetMaxZcm - targetMinZcm;
            if (widthZ <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetMaxZcm),
                    targetMaxZcm,
                    "targetMaxZcm must be strictly greater than targetMinZcm (LayeredSpanTriangulationSpec.target width Z).");
            }

            if (widthX % cellSizeXcm != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetMaxXcm),
                    targetMaxXcm,
                    "target X extent must be an exact multiple of cellSizeXcm (LayeredSpanTriangulationSpec.targetXcm alignment).");
            }

            if (widthZ % cellSizeZcm != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetMaxZcm),
                    targetMaxZcm,
                    "target Z extent must be an exact multiple of cellSizeZcm (LayeredSpanTriangulationSpec.targetZcm alignment).");
            }

            // NavBorderPortal U/V are tile-local centimetres (signed short). Reject overflow explicitly.
            NavBorderPortalCoordinateContract.RequireTileExtentFitsPortalCoordinates(
                checked((int)widthX),
                checked((int)widthZ),
                "LayeredSpanTriangulationSpec");

            // Predicate origin at target min; any vertex inside/on the target rectangle must keep
            // local |delta| within the demonstrable Int128 orientation/incircle bound.
            if (widthX > DemonstrableLocalAbsDeltaCm)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetMaxXcm),
                    targetMaxXcm,
                    "target X width exceeds LayeredSpanTriangulationSpec.DemonstrableLocalAbsDeltaCm.");
            }

            if (widthZ > DemonstrableLocalAbsDeltaCm)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetMaxZcm),
                    targetMaxZcm,
                    "target Z width exceeds LayeredSpanTriangulationSpec.DemonstrableLocalAbsDeltaCm.");
            }

            HeightRounding = heightRounding;
            MaxLawsonFlipCount = maxLawsonFlipCount;
            TargetMinXcm = targetMinXcm;
            TargetMinZcm = targetMinZcm;
            TargetMaxXcm = targetMaxXcm;
            TargetMaxZcm = targetMaxZcm;
            CellSizeXcm = cellSizeXcm;
            CellSizeZcm = cellSizeZcm;
        }

        public LayeredSpanHeightRounding HeightRounding { get; }

        /// <summary>
        /// Finite Lawson flip work bound. Exceeding this fails explicitly (no silent stop).
        /// </summary>
        public int MaxLawsonFlipCount { get; }

        public int TargetMinXcm { get; }
        public int TargetMinZcm { get; }
        public int TargetMaxXcm { get; }
        public int TargetMaxZcm { get; }
        public int CellSizeXcm { get; }
        public int CellSizeZcm { get; }

        public int CellCountX => (int)(((long)TargetMaxXcm - TargetMinXcm) / CellSizeXcm);

        public int CellCountZ => (int)(((long)TargetMaxZcm - TargetMinZcm) / CellSizeZcm);
    }
}
