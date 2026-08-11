using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs.Tests
{
    /// <summary>
    /// Score by distance from origin (or a reference point). Optionally filters by min/max band.
    /// </summary>
    public sealed class DistanceTest : IEqsTest
    {
        private readonly bool _preferNear;
        private readonly int _minCm;
        private readonly int _maxCm;
        private readonly float _weight;
        private readonly WorldCmInt2? _reference;

        /// <param name="preferNear">true: closer = higher score; false: farther = higher score.</param>
        /// <param name="minCm">Filter out candidates closer than this (0 = no min).</param>
        /// <param name="maxCm">Filter out candidates farther than this (0 = no max).</param>
        /// <param name="weight">Score contribution weight.</param>
        /// <param name="reference">Optional reference point; defaults to query origin.</param>
        public DistanceTest(bool preferNear, int minCm = 0, int maxCm = 0, float weight = 1f, WorldCmInt2? reference = null)
        {
            _preferNear = preferNear;
            _minCm = minCm;
            _maxCm = maxCm;
            _weight = weight;
            _reference = reference;
        }

        public void Score(in EqsContext ctx, ref EqsItem item)
        {
            if (item.Filtered)
            {
                return;
            }

            WorldCmInt2 refPos = _reference ?? ctx.Origin;
            long dx = item.Position.X - refPos.X;
            long dy = item.Position.Y - refPos.Y;
            long distSq = dx * dx + dy * dy;
            double dist = Math.Sqrt(distSq);

            if (_minCm > 0 && dist < _minCm)
            {
                item.Filtered = true;
                return;
            }

            if (_maxCm > 0 && dist > _maxCm)
            {
                item.Filtered = true;
                return;
            }

            // Normalize to 0..1 using maxCm as scale (fallback to dist itself when no max).
            float scale = _maxCm > 0 ? _maxCm : (float)Math.Max(dist, 1.0);
            float normalized = (float)Math.Clamp(dist / scale, 0.0, 1.0);
            float scoreContribution = _preferNear ? (1f - normalized) : normalized;
            item.Score += scoreContribution * _weight;
        }
    }
}
