using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Immutable climb limit for four-neighbor layered-span walk links (integer centimeters).
    /// </summary>
    public readonly struct LayeredSpanWalkLinkSpec
    {
        public LayeredSpanWalkLinkSpec(int maxClimbCm)
        {
            if (maxClimbCm < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxClimbCm),
                    maxClimbCm,
                    "maxClimbCm must be nonnegative.");
            }

            MaxClimbCm = maxClimbCm;
        }

        public int MaxClimbCm { get; }
    }
}
