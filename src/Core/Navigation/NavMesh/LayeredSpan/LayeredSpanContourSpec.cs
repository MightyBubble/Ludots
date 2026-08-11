using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Explicit contour extraction parameters for layered-span charts/rings.
    /// No defaults: every field must be provided and validated.
    /// </summary>
    public readonly struct LayeredSpanContourSpec
    {
        public LayeredSpanContourSpec(
            int maxSimplificationErrorCm,
            int targetMinXcm,
            int targetMinZcm,
            int targetMaxXcm,
            int targetMaxZcm)
        {
            if (maxSimplificationErrorCm < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxSimplificationErrorCm),
                    maxSimplificationErrorCm,
                    "maxSimplificationErrorCm must be nonnegative.");
            }

            if (targetMaxXcm <= targetMinXcm)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetMaxXcm),
                    targetMaxXcm,
                    "targetMaxXcm must be strictly greater than targetMinXcm.");
            }

            if (targetMaxZcm <= targetMinZcm)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetMaxZcm),
                    targetMaxZcm,
                    "targetMaxZcm must be strictly greater than targetMinZcm.");
            }

            MaxSimplificationErrorCm = maxSimplificationErrorCm;
            TargetMinXcm = targetMinXcm;
            TargetMinZcm = targetMinZcm;
            TargetMaxXcm = targetMaxXcm;
            TargetMaxZcm = targetMaxZcm;
        }

        /// <summary>
        /// Maximum allowed chord deviation in integer centimeters for optional simplification.
        /// Zero means only exact duplicate and collinear vertex removal.
        /// </summary>
        public int MaxSimplificationErrorCm { get; }

        /// <summary>Inclusive lower X of the target clip rectangle (world cm).</summary>
        public int TargetMinXcm { get; }

        /// <summary>Inclusive lower Z of the target clip rectangle (world cm).</summary>
        public int TargetMinZcm { get; }

        /// <summary>Exclusive-of-degenerate upper X of the target clip rectangle (world cm).</summary>
        public int TargetMaxXcm { get; }

        /// <summary>Exclusive-of-degenerate upper Z of the target clip rectangle (world cm).</summary>
        public int TargetMaxZcm { get; }
    }
}
