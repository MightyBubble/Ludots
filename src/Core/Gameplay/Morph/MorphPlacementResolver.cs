using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.Morph
{
    internal static class MorphPlacementResolver
    {
        public static bool TryResolve(
            World world,
            in RuntimeEntityMorphRequest request,
            MorphPlacementMode placement,
            out Fix64Vec2 positionCm,
            out float facingAngleRad,
            out bool hasFacing)
        {
            positionCm = default;
            facingAngleRad = 0f;
            hasFacing = false;

            if (request.HasPlacementOverride != 0)
            {
                positionCm = request.PlacementOverrideCm;
                if (request.HasFacingOverride != 0)
                {
                    facingAngleRad = request.FacingOverrideRad;
                    hasFacing = true;
                }

                return true;
            }

            switch (placement)
            {
                case MorphPlacementMode.AtSource:
                    return TryResolveEntityPosition(world, request.Source, out positionCm, out facingAngleRad, out hasFacing);
                case MorphPlacementMode.AtTargetPoint:
                    return TryResolveTargetPoint(world, in request, out positionCm);
                case MorphPlacementMode.PreservedExplicit:
                    return false;
                default:
                    throw new InvalidOperationException($"Unsupported morph placement mode '{placement}'.");
            }
        }

        private static bool TryResolveTargetPoint(World world, in RuntimeEntityMorphRequest request, out Fix64Vec2 positionCm)
        {
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
                EffectTargetPointResolveOptions.MorphAtTargetPoint,
                out positionCm);
        }

        private static bool TryResolveEntityPosition(
            World world,
            Entity entity,
            out Fix64Vec2 positionCm,
            out float facingAngleRad,
            out bool hasFacing)
        {
            positionCm = default;
            facingAngleRad = 0f;
            hasFacing = false;

            if (!world.IsAlive(entity) || !world.Has<WorldPositionCm>(entity))
            {
                return false;
            }

            positionCm = world.Get<WorldPositionCm>(entity).Value;
            if (world.Has<FacingDirection>(entity))
            {
                facingAngleRad = world.Get<FacingDirection>(entity).AngleRad;
                hasFacing = true;
            }

            return true;
        }
    }
}
