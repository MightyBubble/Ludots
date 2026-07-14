using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    internal static class WorldMoveCmStepHelper
    {
        public static bool StepTowards(
            ref Fix64Vec2 current,
            in Fix64Vec2 target,
            Fix64 stepCm,
            Fix64 stopRadiusCm)
        {
            if (stepCm <= Fix64.Zero)
            {
                return false;
            }

            var delta = target - current;
            Fix64 distance = delta.Length();

            if (distance <= stopRadiusCm || distance <= stepCm)
            {
                current = target;
                return true;
            }

            current += delta * (stepCm / distance);
            return false;
        }
    }
}
