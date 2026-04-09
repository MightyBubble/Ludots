using System;

namespace Ludots.Core.Presentation.Terrain
{
    public static class VisualHeightmapImport
    {
        public static short[] ConvertUInt16SamplesToInt16Centimeters(
            ReadOnlySpan<ushort> rawSamples,
            float centimetersPerUnit,
            float offsetCentimeters = 0f,
            bool allowSaturation = false)
        {
            if (!float.IsFinite(centimetersPerUnit) || centimetersPerUnit <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(centimetersPerUnit));
            }

            if (!float.IsFinite(offsetCentimeters))
            {
                throw new ArgumentOutOfRangeException(nameof(offsetCentimeters));
            }

            short[] samplesCm = new short[rawSamples.Length];
            for (int i = 0; i < rawSamples.Length; i++)
            {
                float sampleCm = offsetCentimeters + (rawSamples[i] * centimetersPerUnit);
                if (!float.IsFinite(sampleCm))
                {
                    throw new InvalidOperationException("Imported terrain sample evaluated to a non-finite height.");
                }

                int rounded = (int)MathF.Round(sampleCm, MidpointRounding.AwayFromZero);
                if (!allowSaturation &&
                    (rounded < short.MinValue || rounded > short.MaxValue))
                {
                    throw new InvalidOperationException("Imported terrain sample exceeded int16 centimeter storage.");
                }

                rounded = Math.Clamp(rounded, short.MinValue, short.MaxValue);
                samplesCm[i] = (short)rounded;
            }

            return samplesCm;
        }
    }
}
