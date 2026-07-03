using System.Collections.Generic;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

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
        private readonly CommandBuffer _commandBuffer = new();

        public DisplacementRuntimeSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            _toDestroy.Clear();

            foreach (ref var chunk in World.Query(in _query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var displacements = chunk.GetSpan<DisplacementState>();
                foreach (var index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref DisplacementState disp = ref displacements[index];
                    if (!World.IsAlive(disp.TargetEntity) || !HasDisplacementPosition(disp.TargetEntity))
                    {
                        ClearMovementSuppression(ref disp);
                        _toDestroy.Add(entity);
                        continue;
                    }

                    if (disp.RemainingTicks <= 0 || disp.RemainingDistanceCm <= Fix64.Zero)
                    {
                        ClearMovementSuppression(ref disp);
                        _toDestroy.Add(entity);
                        continue;
                    }

                    Fix64 stepCm = Fix64.FromInt(disp.TotalDistanceCm) / Fix64.FromInt(disp.TotalDurationTicks);
                    if (stepCm > disp.RemainingDistanceCm)
                    {
                        stepCm = disp.RemainingDistanceCm;
                    }

                    Fix64Vec2 direction = ComputeDirection(in disp, World);
                    ApplyPositionStep(disp.TargetEntity, direction * stepCm);

                    disp.RemainingDistanceCm -= stepCm;
                    disp.RemainingTicks--;

                    if (disp.RemainingTicks <= 0 || disp.RemainingDistanceCm <= Fix64.Zero)
                    {
                        ClearMovementSuppression(ref disp);
                        _toDestroy.Add(entity);
                    }
                    else
                    {
                        ApplyMovementSuppression(ref disp);
                    }
                }
            }

            for (int i = 0; i < _toDestroy.Count; i++)
            {
                if (World.IsAlive(_toDestroy[i]))
                {
                    _commandBuffer.Destroy(_toDestroy[i]);
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private bool HasDisplacementPosition(Entity target)
        {
            return World.Has<Position2D>(target) || World.Has<WorldPositionCm>(target);
        }

        private void ApplyMovementSuppression(ref DisplacementState disp)
        {
            if (!disp.OverrideNavigation || !World.IsAlive(disp.TargetEntity))
            {
                return;
            }

            var target = disp.TargetEntity;
            if (!disp.MovementSuppressionApplied && !World.Has<MovementSuppressed2D>(target))
            {
                _commandBuffer.Add(target, new MovementSuppressed2D());
            }

            disp.MovementSuppressionApplied = true;
        }

        private void ClearMovementSuppression(ref DisplacementState disp)
        {
            if (!disp.OverrideNavigation || !World.IsAlive(disp.TargetEntity))
            {
                return;
            }

            var target = disp.TargetEntity;
            if (disp.MovementSuppressionApplied && World.Has<MovementSuppressed2D>(target))
            {
                _commandBuffer.Remove<MovementSuppressed2D>(in target);
            }

            disp.MovementSuppressionApplied = false;
        }

        private void ApplyPositionStep(Entity target, Fix64Vec2 step)
        {
            if (World.Has<Position2D>(target))
            {
                ref var physicsPosition = ref World.Get<Position2D>(target);
                physicsPosition.Value = physicsPosition.Value + step;
            }

            if (World.Has<WorldPositionCm>(target))
            {
                ref var worldPosition = ref World.Get<WorldPositionCm>(target);
                worldPosition.Value = worldPosition.Value + step;
            }
        }

        private static Fix64Vec2 ComputeDirection(in DisplacementState disp, World world)
        {
            switch (disp.DirectionMode)
            {
                case DisplacementDirectionMode.ToTarget:
                {
                    if (!TryGetDisplacementPosition(world, disp.TargetEntity, out Fix64Vec2 moverPos))
                    {
                        return Fix64Vec2.UnitX;
                    }

                    Fix64Vec2 destination;
                    if (disp.HasTargetPoint)
                    {
                        destination = disp.TargetPointCm;
                    }
                    else if (world.IsAlive(disp.DirectionTargetEntity) && TryGetDisplacementPosition(world, disp.DirectionTargetEntity, out Fix64Vec2 directionTargetPos))
                    {
                        destination = directionTargetPos;
                    }
                    else
                    {
                        return Fix64Vec2.UnitX;
                    }

                    var delta = destination - moverPos;
                    var lenSq = delta.LengthSquared();
                    if (lenSq <= Fix64.OneValue)
                    {
                        return Fix64Vec2.Zero;
                    }

                    return delta.Normalized();
                }

                case DisplacementDirectionMode.AwayFromSource:
                {
                    if (!world.IsAlive(disp.SourceEntity) || !TryGetDisplacementPosition(world, disp.SourceEntity, out Fix64Vec2 sourcePos))
                    {
                        return Fix64Vec2.UnitX;
                    }
                    if (!TryGetDisplacementPosition(world, disp.TargetEntity, out Fix64Vec2 targetPos))
                    {
                        return Fix64Vec2.UnitX;
                    }

                    var delta = targetPos - sourcePos;
                    var lenSq = delta.LengthSquared();
                    if (lenSq <= Fix64.OneValue)
                    {
                        return Fix64Vec2.UnitX;
                    }
                    return delta.Normalized();
                }

                case DisplacementDirectionMode.TowardSource:
                {
                    if (!world.IsAlive(disp.SourceEntity) || !TryGetDisplacementPosition(world, disp.SourceEntity, out Fix64Vec2 sourcePos))
                    {
                        return Fix64Vec2.UnitX;
                    }
                    if (!TryGetDisplacementPosition(world, disp.TargetEntity, out Fix64Vec2 targetPos))
                    {
                        return Fix64Vec2.UnitX;
                    }

                    var delta = sourcePos - targetPos;
                    var lenSq = delta.LengthSquared();
                    if (lenSq <= Fix64.OneValue)
                    {
                        return Fix64Vec2.UnitX;
                    }
                    return delta.Normalized();
                }

                case DisplacementDirectionMode.Fixed:
                {
                    return WorldPlane2D.Fix64DirectionFromFacingRad(disp.FixedDirectionRad);
                }

                default:
                    return Fix64Vec2.UnitX;
            }
        }

        private static bool TryGetDisplacementPosition(World world, Entity entity, out Fix64Vec2 position)
        {
            if (world.Has<Position2D>(entity))
            {
                position = world.Get<Position2D>(entity).Value;
                return true;
            }

            if (world.Has<WorldPositionCm>(entity))
            {
                position = world.Get<WorldPositionCm>(entity).Value;
                return true;
            }

            position = Fix64Vec2.Zero;
            return false;
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }
    }
}
