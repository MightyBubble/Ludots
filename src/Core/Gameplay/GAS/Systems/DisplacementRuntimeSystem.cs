using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// Drives active displacement effects (dash, knockback, pull) each tick.
    /// Runs in <see cref="Engine.GameEngine.SystemGroup.EffectProcessing"/> alongside projectile/spawn systems.
    /// Uses deferred destruction to avoid structural changes inside query lambdas.
    /// All math uses Fix64/Fix64Vec2 for determinism.
    /// </summary>
    public sealed class DisplacementRuntimeSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription().WithAll<DisplacementState>();
        private readonly List<Entity> _toDestroy = new();

        public DisplacementRuntimeSystem(World world) : base(world) { }

        public override void Update(in float dt)
        {
            _toDestroy.Clear();

            World.Query(in _query, (Entity e, ref DisplacementState disp) =>
            {
                if (!World.IsAlive(disp.TargetEntity))
                {
                    _toDestroy.Add(e);
                    return;
                }

                if (!World.Has<WorldPositionCm>(disp.TargetEntity))
                {
                    _toDestroy.Add(e);
                    return;
                }

                if (disp.RemainingTicks <= 0)
                {
                    _toDestroy.Add(e);
                    return;
                }

                // Compute step distance per tick
                Fix64 stepCm = Fix64.FromInt(disp.TotalDistanceCm) / Fix64.FromInt(disp.TotalDurationTicks);
                if (stepCm > disp.RemainingDistanceCm)
                    stepCm = disp.RemainingDistanceCm;

                // Compute displacement direction
                Fix64Vec2 direction = ComputeDirection(disp, World);

                // Apply position delta
                ref var pos = ref World.Get<WorldPositionCm>(disp.TargetEntity);
                pos.Value = pos.Value + direction * stepCm;

                disp.RemainingDistanceCm -= stepCm;
                disp.RemainingTicks--;

                if (disp.RemainingTicks <= 0 || disp.RemainingDistanceCm <= Fix64.Zero)
                {
                    _toDestroy.Add(e);
                }
            });

            // Deferred destruction
            for (int i = 0; i < _toDestroy.Count; i++)
            {
                if (World.IsAlive(_toDestroy[i]))
                    World.Destroy(_toDestroy[i]);
            }
        }

        private static Fix64Vec2 ComputeDirection(in DisplacementState disp, World world)
        {
            switch (disp.DirectionMode)
            {
                case DisplacementDirectionMode.ToTarget:
                {
                    if (!world.IsAlive(disp.TargetEntity) || !world.IsAlive(disp.SourceEntity))
                        return Fix64Vec2.UnitX;
                    if (!world.Has<WorldPositionCm>(disp.TargetEntity) || !world.Has<WorldPositionCm>(disp.SourceEntity))
                        return Fix64Vec2.UnitX;

                    // Dash toward original target position (source moves toward target)
                    // Note: In dash semantics, the "target" of the displacement IS the moving entity,
                    // and "source" is the entity that caused the displacement.
                    // Direction = from mover toward ability target
                    // For simplicity, we use the forward direction of the displacement entity
                    return Fix64Vec2.UnitX; // Caller should set a specific direction via Fixed mode for dashes
                }

                case DisplacementDirectionMode.AwayFromSource:
                {
                    if (!world.IsAlive(disp.SourceEntity) || !world.Has<WorldPositionCm>(disp.SourceEntity))
                        return Fix64Vec2.UnitX;
                    if (!world.Has<WorldPositionCm>(disp.TargetEntity))
                        return Fix64Vec2.UnitX;

                    var targetPos = world.Get<WorldPositionCm>(disp.TargetEntity).Value;
                    var sourcePos = world.Get<WorldPositionCm>(disp.SourceEntity).Value;
                    var delta = targetPos - sourcePos;
                    var lenSq = delta.LengthSquared();
                    if (lenSq <= Fix64.OneValue)
                        return Fix64Vec2.UnitX;
                    return delta.Normalized();
                }

                case DisplacementDirectionMode.TowardSource:
                {
                    if (!world.IsAlive(disp.SourceEntity) || !world.Has<WorldPositionCm>(disp.SourceEntity))
                        return Fix64Vec2.UnitX;
                    if (!world.Has<WorldPositionCm>(disp.TargetEntity))
                        return Fix64Vec2.UnitX;

                    var targetPos = world.Get<WorldPositionCm>(disp.TargetEntity).Value;
                    var sourcePos = world.Get<WorldPositionCm>(disp.SourceEntity).Value;
                    var delta = sourcePos - targetPos;
                    var lenSq = delta.LengthSquared();
                    if (lenSq <= Fix64.OneValue)
                        return Fix64Vec2.UnitX;
                    return delta.Normalized();
                }

                case DisplacementDirectionMode.Fixed:
                {
                    Fix64 cos = Fix64Math.Cos(disp.FixedDirectionRad);
                    Fix64 sin = Fix64Math.Sin(disp.FixedDirectionRad);
                    return new Fix64Vec2(cos, sin);
                }

                default:
                    return Fix64Vec2.UnitX;
            }
        }
    }
}
