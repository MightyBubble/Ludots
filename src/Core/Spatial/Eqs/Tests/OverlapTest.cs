using System;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs.Tests
{
    /// <summary>
    /// Score by counting entities overlapping a shape cast at the candidate position.
    /// Reuses ISpatialQueryService cast functions (Radius/Cone/Rectangle/Line) — does not
    /// reimplement spatial queries.
    /// </summary>
    public sealed class OverlapTest : IEqsTest
    {
        private readonly OverlapShape _shape;
        private readonly int _extentCm;
        private readonly bool _preferMore;
        private readonly float _weight;
        private readonly int _normalizeCount;
        private readonly int _minCount;
        private readonly int _maxCount;
        private readonly bool _filterByCount;

        /// <param name="shape">Cast shape mapped to a spatial query function.</param>
        /// <param name="extentCm">Shape size (radius / cone range / rect half-extent / line length).</param>
        /// <param name="preferMore">true: more overlapping entities = higher score.</param>
        /// <param name="weight">Score contribution weight.</param>
        /// <param name="normalizeCount">Count used to normalize to 0..1 (expected max).</param>
        /// <param name="minCount">If filterByCount, filter candidates with fewer than this many hits.</param>
        /// <param name="maxCount">If filterByCount, filter candidates with more than this many hits.</param>
        /// <param name="filterByCount">Enable hard count filter.</param>
        public OverlapTest(
            OverlapShape shape,
            int extentCm,
            bool preferMore,
            float weight = 1f,
            int normalizeCount = 8,
            int minCount = 0,
            int maxCount = 0,
            bool filterByCount = false)
        {
            if (!Enum.IsDefined(typeof(OverlapShape), shape))
            {
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown OverlapShape.");
            }

            if (normalizeCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(normalizeCount), normalizeCount, "normalizeCount must be > 0.");
            }

            _shape = shape;
            _extentCm = extentCm;
            _preferMore = preferMore;
            _weight = weight;
            _normalizeCount = normalizeCount;
            _minCount = minCount;
            _maxCount = maxCount;
            _filterByCount = filterByCount;
        }

        public void Score(in EqsContext ctx, ref EqsItem item)
        {
            if (item.Filtered)
            {
                return;
            }

            if (ctx.SpatialQueries == null)
            {
                throw new InvalidOperationException(
                    "OverlapTest requires EqsContext.SpatialQueries; spatial query service was not provided.");
            }

            Span<Entity> hits = stackalloc Entity[64];
            int count = CastShape(ctx.SpatialQueries, ctx.Origin, item.Position, hits);

            if (_filterByCount)
            {
                if (_minCount > 0 && count < _minCount)
                {
                    item.Filtered = true;
                    return;
                }

                if (_maxCount > 0 && count > _maxCount)
                {
                    item.Filtered = true;
                    return;
                }
            }

            float normalized = Math.Clamp((float)count / _normalizeCount, 0f, 1f);
            float scoreContribution = _preferMore ? normalized : (1f - normalized);
            item.Score += scoreContribution * _weight;
        }

        private int CastShape(ISpatialQueryService queries, WorldCmInt2 origin, WorldCmInt2 pos, Span<Entity> buffer)
        {
            switch (_shape)
            {
                case OverlapShape.Radius:
                    return queries.QueryRadius(pos, _extentCm, buffer).Count;

                case OverlapShape.Cone:
                {
                    int dirDeg = HeadingDeg(pos, origin);
                    return queries.QueryCone(pos, dirDeg, 45, _extentCm, buffer).Count;
                }

                case OverlapShape.Rectangle:
                    return queries.QueryRectangle(pos, _extentCm, _extentCm, 0, buffer).Count;

                case OverlapShape.Line:
                {
                    int dirDeg = HeadingDeg(pos, origin);
                    return queries.QueryLine(pos, dirDeg, _extentCm, _extentCm / 4, buffer).Count;
                }

                default:
                    throw new InvalidOperationException($"Unhandled OverlapShape '{_shape}'.");
            }
        }

        private static int HeadingDeg(WorldCmInt2 from, WorldCmInt2 to)
        {
            double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
            int deg = (int)Math.Round(angle * 180.0 / Math.PI);
            return ((deg % 360) + 360) % 360;
        }
    }
}
