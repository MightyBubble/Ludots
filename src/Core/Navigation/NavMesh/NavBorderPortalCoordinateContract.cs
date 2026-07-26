using System;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// Shared .ntil / <see cref="NavBorderPortal"/> U/V storage contract.
    /// U/V are tile-local centimetres stored as signed 16-bit values.
    /// Overflow must fail-fast (never clamp or wrap).
    /// </summary>
    public static class NavBorderPortalCoordinateContract
    {
        public const int MinCoordinateInclusive = short.MinValue;
        public const int MaxCoordinateInclusive = short.MaxValue;

        public static void RequireTileExtentFitsPortalCoordinates(int tileWidthCm, int tileHeightCm, string owner)
        {
            if (string.IsNullOrWhiteSpace(owner))
            {
                throw new ArgumentException("Owner path is required.", nameof(owner));
            }

            if (tileWidthCm <= 0 || tileWidthCm > MaxCoordinateInclusive)
            {
                throw new InvalidOperationException(
                    $"{owner}: tileWidthCm={tileWidthCm} must be in (0, {MaxCoordinateInclusive}] for NavBorderPortal short U/V capacity.");
            }

            if (tileHeightCm <= 0 || tileHeightCm > MaxCoordinateInclusive)
            {
                throw new InvalidOperationException(
                    $"{owner}: tileHeightCm={tileHeightCm} must be in (0, {MaxCoordinateInclusive}] for NavBorderPortal short U/V capacity.");
            }
        }

        public static short RequirePortalCoordinate(int value, string owner)
        {
            if (string.IsNullOrWhiteSpace(owner))
            {
                throw new ArgumentException("Owner path is required.", nameof(owner));
            }

            if (value < MinCoordinateInclusive || value > MaxCoordinateInclusive)
            {
                throw new InvalidOperationException(
                    $"{owner}: portal coordinate {value} exceeds NavBorderPortal short U/V capacity " +
                    $"[{MinCoordinateInclusive}, {MaxCoordinateInclusive}].");
            }

            return (short)value;
        }
    }
}
