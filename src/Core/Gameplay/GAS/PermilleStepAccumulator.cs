using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS
{
    public static class PermilleStepAccumulator
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Consume(ref int accumulatorPermille, int inputPermille, int thresholdPermille)
        {
            if (inputPermille < 0) throw new ArgumentOutOfRangeException(nameof(inputPermille));
            if (thresholdPermille < 1) throw new ArgumentOutOfRangeException(nameof(thresholdPermille));
            if (inputPermille == 0) return 0;

            accumulatorPermille += inputPermille;
            int steps = accumulatorPermille / thresholdPermille;
            accumulatorPermille -= steps * thresholdPermille;
            return steps;
        }
    }
}
