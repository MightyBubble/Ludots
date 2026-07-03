using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// SSOT for resolving cast/placement target points from effect context and caller params.
    /// </summary>
    public static class EffectTargetPointResolver
    {
        public static bool TryResolvePreservedTargetPoint(in EffectConfigParams mergedParams, out Fix64Vec2 targetPointCm)
        {
            if (mergedParams.TryGetFloat(EffectParamKeys.TargetPosX, out float x) &&
                mergedParams.TryGetFloat(EffectParamKeys.TargetPosY, out float y))
            {
                targetPointCm = Fix64Vec2.FromInt((int)x, (int)y);
                return true;
            }

            targetPointCm = default;
            return false;
        }

        public static bool TryResolvePreservedTargetOrigin(in EffectConfigParams mergedParams, out Fix64Vec2 originPointCm)
        {
            if (mergedParams.TryGetFloat(EffectParamKeys.TargetOriginX, out float x) &&
                mergedParams.TryGetFloat(EffectParamKeys.TargetOriginY, out float y))
            {
                originPointCm = Fix64Vec2.FromInt((int)x, (int)y);
                return true;
            }

            originPointCm = default;
            return false;
        }

        public static bool TryResolve(
            World world,
            in EffectContext context,
            in EffectConfigParams mergedParams,
            EffectTargetPointResolveOptions options,
            out Fix64Vec2 positionCm)
        {
            if (TryResolvePreservedTargetPoint(in mergedParams, out positionCm))
            {
                return true;
            }

            if (world.IsAlive(context.TargetContext) && world.Has<WorldPositionCm>(context.TargetContext))
            {
                positionCm = world.Get<WorldPositionCm>(context.TargetContext).Value;
                return true;
            }

            if (world.IsAlive(context.Target) && world.Has<WorldPositionCm>(context.Target))
            {
                positionCm = world.Get<WorldPositionCm>(context.Target).Value;
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

            positionCm = default;
            return false;
        }

        public static bool TryResolve(
            World world,
            in EffectContext context,
            in EffectConfigParams mergedParams,
            out Fix64Vec2 positionCm)
        {
            return TryResolve(
                world,
                in context,
                in mergedParams,
                EffectTargetPointResolveOptions.CreateUnit,
                out positionCm);
        }

        public static bool TryResolveOrigin(
            World world,
            in EffectContext context,
            in EffectConfigParams mergedParams,
            out Fix64Vec2 positionCm)
        {
            if (TryResolvePreservedTargetOrigin(in mergedParams, out positionCm))
            {
                return true;
            }

            if (world.IsAlive(context.Source) && world.Has<AbilityExecInstance>(context.Source))
            {
                ref readonly var exec = ref world.Get<AbilityExecInstance>(context.Source);
                if (exec.HasTargetOriginPos != 0)
                {
                    positionCm = exec.TargetOriginPosCm;
                    return true;
                }
            }

            if (world.IsAlive(context.Source) && world.Has<WorldPositionCm>(context.Source))
            {
                positionCm = world.Get<WorldPositionCm>(context.Source).Value;
                return true;
            }

            positionCm = default;
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
        public static EffectTargetPointResolveOptions DeployAtTargetPoint => new() { AllowSourceWorldPositionFallback = false };
    }
}
