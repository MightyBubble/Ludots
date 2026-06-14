using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
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
            Entity actor = _context.GetControlledActor();
            if (actor == Entity.Null || !World.IsAlive(actor) || !_context.TryGetGroundWorldCm(out var worldCm))
            {
                return;
            }

            World.Query(in Query, (Entity entity, ref AbilityExecInstance exec, ref AbilityExecAimSync sync) =>
            {
                if (!entity.Equals(actor) || exec.AbilitySlot != sync.AbilitySlot)
                {
                    return;
                }

                exec.TargetPosCm = Fix64Vec2.FromInt(worldCm.X, worldCm.Y);
                exec.HasTargetPos = 1;

                if (sync.SyncFacing != 0 && World.TryGet(entity, out WorldPositionCm position))
                {
                    Fix64Vec2 delta = exec.TargetPosCm - position.Value;
                    if (delta.X != Fix64.Zero || delta.Y != Fix64.Zero)
                    {
                        Upsert(entity, new FacingDirection { AngleRad = WorldPlane2D.FacingRadFromDirection(in delta) });
                    }
                }

                if (World.TryGet(entity, out BlackboardSpatialBuffer spatial))
                {
                    spatial.SetPoint(
                        OrderBlackboardKeys.Cast_TargetPosition,
                        new System.Numerics.Vector3(worldCm.X, 0f, worldCm.Y));
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
    }
}
