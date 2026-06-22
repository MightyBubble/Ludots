using System.Collections.Generic;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics;

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
        private readonly int _navMoveTagId;

        public DisplacementRuntimeSystem(World world) : base(world)
        {
            _navMoveTagId = TagRegistry.Register("Ability.Nav.Move");
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
                    if (!World.IsAlive(disp.TargetEntity) || !World.Has<WorldPositionCm>(disp.TargetEntity))
                    {
                        RestoreNavigationOverride(ref disp);
                        _toDestroy.Add(entity);
                        continue;
                    }

                    if (disp.RemainingTicks <= 0 || disp.RemainingDistanceCm <= Fix64.Zero)
                    {
                        RestoreNavigationOverride(ref disp);
                        _toDestroy.Add(entity);
                        continue;
                    }

                    ApplyNavigationOverride(ref disp);

                    Fix64 stepCm = Fix64.FromInt(disp.TotalDistanceCm) / Fix64.FromInt(disp.TotalDurationTicks);
                    if (stepCm > disp.RemainingDistanceCm)
                    {
                        stepCm = disp.RemainingDistanceCm;
                    }

                    Fix64Vec2 direction = ComputeDirection(in disp, World);

                    ref var pos = ref World.Get<WorldPositionCm>(disp.TargetEntity);
                    pos.Value = pos.Value + direction * stepCm;

                    disp.RemainingDistanceCm -= stepCm;
                    disp.RemainingTicks--;

                    if (disp.RemainingTicks <= 0 || disp.RemainingDistanceCm <= Fix64.Zero)
                    {
                        RestoreNavigationOverride(ref disp);
                        _toDestroy.Add(entity);
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

        private void ApplyNavigationOverride(ref DisplacementState disp)
        {
            if (!disp.OverrideNavigation || !World.IsAlive(disp.TargetEntity))
            {
                return;
            }

            var target = disp.TargetEntity;
            if (!disp.NavigationOverrideCaptured)
            {
                if (_navMoveTagId > 0 && World.Has<GameplayTagContainer>(target))
                {
                    ref readonly var tags = ref World.Get<GameplayTagContainer>(target);
                    disp.SavedHadNavMoveTag = tags.HasTag(_navMoveTagId);
                }

                disp.NavigationOverrideCaptured = true;
            }

            if (_navMoveTagId > 0 && World.Has<GameplayTagContainer>(target))
            {
                ref var tags = ref World.Get<GameplayTagContainer>(target);
                if (tags.HasTag(_navMoveTagId))
                {
                    tags.RemoveTag(_navMoveTagId);
                }
            }

            if (World.Has<ForceInput2D>(target))
            {
                ref var forceInput = ref World.Get<ForceInput2D>(target);
                forceInput.Force = Fix64Vec2.Zero;
            }
        }

        private void RestoreNavigationOverride(ref DisplacementState disp)
        {
            if (!disp.OverrideNavigation || !disp.NavigationOverrideCaptured || !World.IsAlive(disp.TargetEntity))
            {
                return;
            }

            var target = disp.TargetEntity;
            if (disp.SavedHadNavMoveTag && _navMoveTagId > 0 && World.Has<GameplayTagContainer>(target) && World.Has<AbilityExecInstance>(target))
            {
                ref var tags = ref World.Get<GameplayTagContainer>(target);
                if (!tags.HasTag(_navMoveTagId))
                {
                    tags.AddTag(_navMoveTagId);
                }
            }
        }

        private static Fix64Vec2 ComputeDirection(in DisplacementState disp, World world)
        {
            switch (disp.DirectionMode)
            {
                case DisplacementDirectionMode.ToTarget:
                {
                    if (!world.Has<WorldPositionCm>(disp.TargetEntity))
                    {
                        return Fix64Vec2.UnitX;
                    }

                    var moverPos = world.Get<WorldPositionCm>(disp.TargetEntity).Value;
                    Fix64Vec2 destination;
                    if (disp.HasTargetPoint)
                    {
                        destination = disp.TargetPointCm;
                    }
                    else if (world.IsAlive(disp.DirectionTargetEntity) && world.Has<WorldPositionCm>(disp.DirectionTargetEntity))
                    {
                        destination = world.Get<WorldPositionCm>(disp.DirectionTargetEntity).Value;
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
                    if (!world.IsAlive(disp.SourceEntity) || !world.Has<WorldPositionCm>(disp.SourceEntity))
                    {
                        return Fix64Vec2.UnitX;
                    }
                    if (!world.Has<WorldPositionCm>(disp.TargetEntity))
                    {
                        return Fix64Vec2.UnitX;
                    }

                    var targetPos = world.Get<WorldPositionCm>(disp.TargetEntity).Value;
                    var sourcePos = world.Get<WorldPositionCm>(disp.SourceEntity).Value;
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
                    if (!world.IsAlive(disp.SourceEntity) || !world.Has<WorldPositionCm>(disp.SourceEntity))
                    {
                        return Fix64Vec2.UnitX;
                    }
                    if (!world.Has<WorldPositionCm>(disp.TargetEntity))
                    {
                        return Fix64Vec2.UnitX;
                    }

                    var targetPos = world.Get<WorldPositionCm>(disp.TargetEntity).Value;
                    var sourcePos = world.Get<WorldPositionCm>(disp.SourceEntity).Value;
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

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }
    }
}
