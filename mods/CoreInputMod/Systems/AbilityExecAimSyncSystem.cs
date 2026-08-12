using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;

namespace CoreInputMod.Systems
{
    /// <summary>
    /// Input-side bridge that continuously refreshes active execution target
    /// points for opt-in locally controlled abilities.
    /// </summary>
    internal sealed class AbilityExecAimSyncSystem : BaseSystem<World, float>
    {
        private readonly InputInteractionContextAccessor _context;

        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<AbilityExecInstance, AbilityExecAimSync>();

        public AbilityExecAimSyncSystem(World world, InputInteractionContextAccessor context) : base(world)
        {
            _context = context;
        }

        public override void Update(in float dt)
        {
            if (!_context.TryGetSolePossessedPlayerId(out int playerId))
            {
                return;
            }

            Entity actor = _context.GetControlledActor(playerId);
            if (actor == Entity.Null || !World.IsAlive(actor) || !_context.TryGetGroundWorldCm(out var worldCm))
            {
                return;
            }

            _context.TryGetAbilityDefinitionRegistry(out AbilityDefinitionRegistry? abilityDefinitions);

            World.Query(in Query, (Entity entity, ref AbilityExecInstance exec, ref AbilityExecAimSync sync) =>
            {
                if (!entity.Equals(actor) || exec.AbilitySlot != sync.AbilitySlot)
                {
                    return;
                }

                Fix64Vec2 targetCm = Fix64Vec2.FromInt(worldCm.X, worldCm.Y);
                if (abilityDefinitions != null &&
                    abilityDefinitions.TryGet(exec.AbilityId, out AbilityDefinition definition) &&
                    definition.HasTargeting &&
                    definition.Targeting.CastRangeCm > 0f &&
                    World.TryGet(entity, out WorldPositionCm position))
                {
                    Fix64Vec2 originCm = position.Value;
                    PlacementValidation.ClampToRange(
                        in originCm,
                        ref targetCm,
                        Fix64.FromFloat(definition.Targeting.CastRangeCm),
                        out _);
                }

                exec.TargetPosCm = targetCm;
                exec.HasTargetPos = 1;

                if (sync.SyncFacing != 0 && World.TryGet(entity, out WorldPositionCm facingPosition))
                {
                    Fix64Vec2 delta = exec.TargetPosCm - facingPosition.Value;
                    if (delta.X != Fix64.Zero || delta.Y != Fix64.Zero)
                    {
                        float facingRad = WorldPlane2D.FacingRadFromDirection(in delta);
                        Upsert(entity, new FacingDirection { AngleRad = facingRad });
                        UpsertBlackboardFacing(entity, facingRad);
                    }
                }

                if (World.TryGet(entity, out BlackboardSpatialBuffer spatial))
                {
                    spatial.SetPoint(
                        OrderBlackboardKeys.Cast_TargetPosition,
                        new System.Numerics.Vector3(targetCm.X.ToFloat(), 0f, targetCm.Y.ToFloat()));
                    World.Set(entity, spatial);
                }
            });
        }

        private void Upsert<T>(Entity entity, in T component)
        {
            if (World.Has<T>(entity))
            {
                World.Set(entity, component);
            }
            else
            {
                World.Add(entity, component);
            }
        }

        private void UpsertBlackboardFacing(Entity entity, float facingRad)
        {
            BlackboardFloatBuffer floats = World.TryGet(entity, out BlackboardFloatBuffer existing)
                ? existing
                : default;
            floats.Set(
                OrderBlackboardKeys.Cast_Facing,
                WorldPlane2D.NormalizeDegreesPositive(WorldPlane2D.RadToDegValue(facingRad)));
            if (World.Has<BlackboardFloatBuffer>(entity))
            {
                World.Set(entity, floats);
            }
            else
            {
                World.Add(entity, floats);
            }
        }
    }
}
