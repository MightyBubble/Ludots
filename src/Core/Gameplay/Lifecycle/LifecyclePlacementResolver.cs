using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.Lifecycle
{
    public static class LifecyclePlacementResolver
    {
        public static bool TryResolveAtTargetPoint(
            World world,
            in RuntimeEntityLifecycleRequest request,
            out Fix64Vec2 positionCm)
        {
            positionCm = default;

            if (request.HasPlacementOverride != 0)
            {
                positionCm = request.PlacementOverrideCm;
                return true;
            }

            var context = new EffectContext
            {
                Source = request.EffectContextSource,
                Target = request.EffectContextTarget,
                TargetContext = request.EffectContextTargetContext,
            };

            return EffectTargetPointResolver.TryResolve(
                world,
                in context,
                in request.EffectConfigParams,
                EffectTargetPointResolveOptions.DeployAtTargetPoint,
                out positionCm);
        }
    }
}
