using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Immutable agent height / slope / same-surface tolerance for layered-span walkability.
    /// Slope threshold is a cold/config Q1M integer; this hot layer never derives floats or cosines.
    /// </summary>
    public readonly struct LayeredSpanWalkabilitySpec
    {
        public const int UpDotQ1M = 1_000_000;

        public LayeredSpanWalkabilitySpec(
            int agentHeightCm,
            int minWalkableUpDotQ1M,
            int sameSurfaceToleranceCm)
        {
            if (agentHeightCm <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(agentHeightCm),
                    agentHeightCm,
                    "agentHeightCm must be positive.");
            }

            if (minWalkableUpDotQ1M < 1 || minWalkableUpDotQ1M > UpDotQ1M)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minWalkableUpDotQ1M),
                    minWalkableUpDotQ1M,
                    "minWalkableUpDotQ1M must be in [1, 1_000_000].");
            }

            if (sameSurfaceToleranceCm < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sameSurfaceToleranceCm),
                    sameSurfaceToleranceCm,
                    "sameSurfaceToleranceCm must be nonnegative.");
            }

            AgentHeightCm = agentHeightCm;
            MinWalkableUpDotQ1M = minWalkableUpDotQ1M;
            SameSurfaceToleranceCm = sameSurfaceToleranceCm;
        }

        public int AgentHeightCm { get; }

        public int MinWalkableUpDotQ1M { get; }

        public int SameSurfaceToleranceCm { get; }
    }
}
