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

            return OffsetCm + ((rawSample * UnitsPerSampleNumeratorCm) / (float)UnitsPerSampleDenominator);
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
