using System;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Decodes imported raw height samples into canonical Core runtime centimeters.
    /// </summary>
    public readonly record struct ContinuousHeightSampleScale(
        int OffsetCm,
        int UnitsPerSampleNumeratorCm,
        int UnitsPerSampleDenominator)
    {
        public static ContinuousHeightSampleScale IdentityCentimeters { get; } = new(0, 1, 1);

        public float Decode(ushort rawSample)
        {
            if (UnitsPerSampleDenominator <= 0)
            {
                throw new InvalidOperationException("Visual height sample denominator must be positive.");
            }

            // 先 widening 成 long 再乘:raw(≤65535) × 分子在大比例资产下会溢出 int32。
            return OffsetCm + (float)((rawSample * (long)UnitsPerSampleNumeratorCm) / (double)UnitsPerSampleDenominator);
        }

        public void Validate()
        {
            if (UnitsPerSampleDenominator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(UnitsPerSampleDenominator));
            }
        }
    }
}
