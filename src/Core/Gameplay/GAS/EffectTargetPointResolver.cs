using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// SSOT for resolving cast/placement target points from effect context and caller params.
    /// </summary>
    public static class EffectTargetPointResolver
    {
        public static bool TryResolve(
            World world,
            in EffectContext context,
            in EffectConfigParams mergedParams,
            out Fix64Vec2 positionCm)
        {
            if (TryGetPreservedTargetPoint(in mergedParams, out WorldCmInt2 point))
            {
                positionCm = Fix64Vec2.FromInt(point.X, point.Y);
                return true;
            }

            if (world.IsAlive(context.Target) && world.Has<WorldPositionCm>(context.Target))
            {
                positionCm = world.Get<WorldPositionCm>(context.Target).Value;
                return true;
            }

            if (world.IsAlive(context.TargetContext) && world.Has<WorldPositionCm>(context.TargetContext))
            {
                positionCm = world.Get<WorldPositionCm>(context.TargetContext).Value;
                return true;
            }

            if (world.IsAlive(context.Source) &&
                world.Has<AbilityExecInstance>(context.Source))
            {
                ref readonly var exec = ref world.Get<AbilityExecInstance>(context.Source);
                if (exec.HasTargetPos != 0)
                {
                    positionCm = exec.TargetPosCm;
                    return true;
                }
            }

            positionCm = default;
            return false;
        }

        public static bool TryResolveOrigin(
            World world,
            in EffectContext context,
            in EffectConfigParams mergedParams,
            out Fix64Vec2 positionCm)
        {
            if (TryGetPreservedTargetOrigin(in mergedParams, out WorldCmInt2 point))
            {
                positionCm = Fix64Vec2.FromInt(point.X, point.Y);
                return true;
            }

            if (world.IsAlive(context.Source) &&
                world.Has<AbilityExecInstance>(context.Source))
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

        private static bool TryGetPreservedTargetOrigin(in EffectConfigParams mergedParams, out WorldCmInt2 point)
        {
            if (mergedParams.TryGetFloat(EffectParamKeys.TargetOriginX, out float x) &&
                mergedParams.TryGetFloat(EffectParamKeys.TargetOriginY, out float y))
            {
                point = new WorldCmInt2((int)x, (int)y);
                return true;
            }

            point = default;
            return false;
        }

        private static bool TryGetPreservedTargetPoint(in EffectConfigParams mergedParams, out WorldCmInt2 point)
        {
            if (mergedParams.TryGetFloat(EffectParamKeys.TargetPosX, out float x) &&
                mergedParams.TryGetFloat(EffectParamKeys.TargetPosY, out float y))
            {
                point = new WorldCmInt2((int)x, (int)y);
                return true;
            }

            point = default;
            return false;
        }
    }
}
