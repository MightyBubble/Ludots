using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.Placement
{
    /// <summary>
    /// Resolves cast/placement target position for effect phase graph execution.
    /// </summary>
    public static class PlacementPhaseTargetPosResolver
    {
        public static IntVector2 Resolve(
            World world,
            in EffectContext context,
            in EffectConfigParams mergedParams)
        {
            if (!EffectTargetPointResolver.TryResolve(
                    world,
                    in context,
                    in mergedParams,
                    out Fix64Vec2 positionCm))
            {
                return default;
            }

            var rounded = positionCm.RoundToInt();
            return new IntVector2(rounded.x, rounded.y);
        }
    }
}
