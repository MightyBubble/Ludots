using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Cold-path deterministic conversion from authoring <c>maxSlopeDeg</c> to
    /// <see cref="LayeredSpanWalkabilitySpec.MinWalkableUpDotQ1M"/>.
    /// Contract: authoring degrees must be an exact integer in [0, 89]; the Q1M cosine
    /// is the frozen table value for that degree (round-half-away-from-zero of
    /// cos(deg·π/180)·1_000_000). Warmed kernels never recompute cosine or use float/double.
    /// </summary>
    public static class LayeredSpanSlopeQ1M
    {
        public const int Scale = LayeredSpanWalkabilitySpec.UpDotQ1M;

        /// <summary>
        /// Frozen cos(deg) in Q1M for deg = 0..89. Index equals integer degrees.
        /// </summary>
        private static readonly int[] CosDegreesQ1M =
        {
            1000000, 999848, 999391, 998630, 997564, 996195, 994522, 992546, 990268, 987688,
            984808, 981627, 978148, 974370, 970296, 965926, 961262, 956305, 951057, 945519,
            939693, 933580, 927184, 920505, 913545, 906308, 898794, 891007, 882948, 874620,
            866025, 857167, 848048, 838671, 829038, 819152, 809017, 798636, 788011, 777146,
            766044, 754710, 743145, 731354, 719340, 707107, 694658, 681998, 669131, 656059,
            642788, 629320, 615661, 601815, 587785, 573576, 559193, 544639, 529919, 515038,
            500000, 484810, 469472, 453990, 438371, 422618, 406737, 390731, 374607, 358368,
            342020, 325568, 309017, 292372, 275637, 258819, 241922, 224951, 207912, 190809,
            173648, 156434, 139173, 121869, 104528, 87156, 69756, 52336, 34899, 17452
        };

        public static int CosDegrees(int maxSlopeDeg)
        {
            if ((uint)maxSlopeDeg >= (uint)CosDegreesQ1M.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxSlopeDeg),
                    maxSlopeDeg,
                    "maxSlopeDeg for LayeredSpanSlopeQ1M.CosDegrees must be an integer in [0, 89].");
            }

            return CosDegreesQ1M[maxSlopeDeg];
        }

        /// <summary>
        /// Compiles authoring <paramref name="maxSlopeDeg"/> into canonical Q1M up-dot.
        /// Float is accepted only to reject non-integer authoring values; no MathF.Cos is used.
        /// </summary>
        public static int CompileMinWalkableUpDotQ1M(float maxSlopeDeg, string owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (float.IsNaN(maxSlopeDeg) || float.IsInfinity(maxSlopeDeg))
            {
                throw new InvalidOperationException($"{owner} requires finite maxSlopeDeg.");
            }

            if (maxSlopeDeg < 0f || maxSlopeDeg >= 90f)
            {
                throw new InvalidOperationException($"{owner} requires maxSlopeDeg >= 0 and < 90.");
            }

            int degrees = (int)maxSlopeDeg;
            if ((float)degrees != maxSlopeDeg)
            {
                throw new InvalidOperationException(
                    $"{owner} requires maxSlopeDeg to be an exact integer degree for layered-span Q1M slope compilation; got {maxSlopeDeg}.");
            }

            int q1m = CosDegrees(degrees);
            if (q1m < 1 || q1m > Scale)
            {
                throw new InvalidOperationException(
                    $"{owner} compiled minWalkableUpDotQ1M ({q1m}) is outside [1, {Scale}].");
            }

            return q1m;
        }
    }
}
