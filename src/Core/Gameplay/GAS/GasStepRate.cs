using System;

namespace Ludots.Core.Gameplay.GAS;

public static class GasStepRate
{
    public static int RequirePositive(int stepRateHz, string consumer)
    {
        if (stepRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepRateHz),
                stepRateHz,
                $"{consumer} requires a positive GAS step rate.");
        }

        return stepRateHz;
    }
}
