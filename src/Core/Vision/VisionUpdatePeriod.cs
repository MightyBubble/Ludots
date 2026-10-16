using System;

namespace Ludots.Core.Vision
{
    public static class VisionUpdatePeriod
    {
        public static int Ticks(int simHz, int updateHz)
        {
            if (simHz < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(simHz));
            }

            if (updateHz < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(updateHz));
            }

            if (updateHz >= simHz)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Floor(simHz / (double)updateHz + 0.5));
        }

        public static int FromFixedDelta(float fixedDeltaSeconds, int updateHz)
        {
            if (!(fixedDeltaSeconds > 0f) || float.IsNaN(fixedDeltaSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
            }

            int simHz = (int)MathF.Round(1f / fixedDeltaSeconds);
            return Ticks(Math.Max(1, simHz), updateHz);
        }
    }
}
