using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS
{
    public static class EffectTargetPointResolver
    {
        public static bool TryResolvePreservedTargetPoint(in EffectConfigParams mergedParams, out Fix64Vec2 targetPointCm)
        {
            if (mergedParams.TryGetFloat(EffectParamKeys.TargetPosX, out float x) &&
                mergedParams.TryGetFloat(EffectParamKeys.TargetPosY, out float y))
            {
                targetPointCm = Fix64Vec2.FromFloat(x, y);
                return true;
            }

            targetPointCm = default;
            return false;
        }

        public static bool TryResolve(
            World world,
            in EffectContext context,
            in EffectConfigParams mergedParams,
            EffectTargetPointResolveOptions options,
            out Fix64Vec2 positionCm)
        {
            positionCm = default;

            if (world.IsAlive(context.TargetContext) && world.Has<WorldPositionCm>(context.TargetContext))
            {
                positionCm = world.Get<WorldPositionCm>(context.TargetContext).Value;
                return true;
            }

            if (TryResolvePreservedTargetPoint(in mergedParams, out positionCm))
            {
                return true;
            }

            if (world.IsAlive(context.Source) && world.Has<AbilityExecInstance>(context.Source))
            {
                ref readonly var exec = ref world.Get<AbilityExecInstance>(context.Source);
                if (exec.HasTargetPos != 0)
                {
                    positionCm = exec.TargetPosCm;
                    return true;
                }
            }

            if (options.AllowSourceWorldPositionFallback &&
                world.IsAlive(context.Source) &&
                world.Has<WorldPositionCm>(context.Source))
            {
                positionCm = world.Get<WorldPositionCm>(context.Source).Value;
                return true;
            }

            return false;
        }

        public static Fix64Vec2 ResolveOrThrow(
            World world,
            in EffectContext context,
            in EffectConfigParams mergedParams,
            EffectTargetPointResolveOptions options,
            string failureMessage)
        {
            if (!TryResolve(world, in context, in mergedParams, options, out Fix64Vec2 positionCm))
            {
                throw new InvalidOperationException(failureMessage);
            }

            return positionCm;
        }
    }

    public readonly struct EffectTargetPointResolveOptions
    {
        public bool AllowSourceWorldPositionFallback { get; init; }

        public static EffectTargetPointResolveOptions CreateUnit => new() { AllowSourceWorldPositionFallback = true };
        public static EffectTargetPointResolveOptions MorphAtTargetPoint => new() { AllowSourceWorldPositionFallback = false };
    }
}
