using System.Collections.Generic;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// Drives active displacement effects (dash, knockback, pull) each tick.
    /// Runs in <see cref="Engine.GameEngine.SystemGroup.EffectProcessing"/> alongside projectile/spawn systems.
    /// Uses deferred destruction to avoid structural changes inside query lambdas.
    /// All math uses Fix64/Fix64Vec2 for determinism.
    /// Targets bound as MassNavigation agents go through the pose-authority
    /// displacement window: the window is requested at effect start, committed at the next
    /// fixed-step boundary, and handed back to Nav when the effect ends or its speed drops
    /// below the authored handback threshold. Agents without an explicit
    /// <see cref="MovementParticipation"/> opt-in fail fast instead of double-writing.
    /// </summary>
    public sealed class DisplacementRuntimeSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription().WithAll<DisplacementState>();
        private readonly List<Entity> _toDestroy = new();
        private readonly CommandBuffer _commandBuffer = new();
        private readonly PoseAuthorityArbiter _poseAuthorityArbiter;

        public DisplacementRuntimeSystem(World world, PoseAuthorityArbiter poseAuthorityArbiter) : base(world)
        {
            _poseAuthorityArbiter = poseAuthorityArbiter
                ?? throw new System.ArgumentNullException(nameof(poseAuthorityArbiter));
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
                        // 位移中死亡是常规玩法事件：取消窗口（幂等——仲裁器的死亡检测
                        // 可能已先一步取消）并合法终止效果。
                        if (disp.PoseWindowRequested)
                        {
                            _poseAuthorityArbiter.CancelWindow(World, disp.TargetEntity);
                            disp.PoseWindowRequested = false;
                        }

                        ClearMovementSuppression(ref disp);
                        _toDestroy.Add(entity);
                        continue;
                    }

                    if (disp.RemainingTicks <= 0 || disp.RemainingDistanceCm <= Fix64.Zero)
                    {
                        if (disp.PoseWindowRequested)
                        {
                            throw new System.InvalidOperationException(
                                $"Displacement state for entity {disp.TargetEntity.Id} exhausted its budget outside the normal completion path while holding a pose-authority window.");
                        }

                        ClearMovementSuppression(ref disp);
                        _toDestroy.Add(entity);
                        continue;
                    }

                    bool isNavAgent = World.Has<MassNavigationAgentIndex>(disp.TargetEntity);
                    MovementParticipation participation = default;
                    if (isNavAgent)
                    {
                        if (!World.Has<MovementParticipation>(disp.TargetEntity))
                        {
                            throw new System.InvalidOperationException(
                                $"Displacement targets MassNavigation agent entity {disp.TargetEntity.Id} without a MovementParticipation declaration; silent double-writes of WorldPositionCm are forbidden.");
                        }

                        participation = World.Get<MovementParticipation>(disp.TargetEntity);
                        if (!participation.DisplacementAllowed)
                        {
                            throw new System.InvalidOperationException(
                                $"Displacement targets MassNavigation agent entity {disp.TargetEntity.Id} whose MovementParticipation.displacement.allowed is false.");
                        }

                        if (!disp.PoseWindowRequested)
                        {
                            _poseAuthorityArbiter.RequestDisplacementAuthority(World, disp.TargetEntity);
                            disp.PoseWindowRequested = true;
                            disp.WindowRefreshRequested = false;
                            // Motion starts once the window commits at the next fixed-step boundary;
                            // writing WorldPositionCm now would double-write against SyncEntities.
                            continue;
                        }

                        if (World.Get<PoseAuthority>(disp.TargetEntity).Value != PoseAuthorityKind.Displacement)
                        {
                            // 区分两种"写权不在位移方"：仲裁器仍持有窗口/待办 = 结算系统缺席，
                            // 是装配错误必须抛；仲裁器已无记录 = 窗口被生命周期事件（卸载/重建）
                            // 外部取消，效果随之合法终止。
                            if (_poseAuthorityArbiter.HasWindowOrPending(disp.TargetEntity))
                            {
                                throw new System.InvalidOperationException(
                                    $"Displacement window for entity {disp.TargetEntity.Id} was requested but never committed; PoseAuthorityCommitSystem must run at the fixed-step boundary.");
                            }

                            disp.PoseWindowRequested = false;
                            ClearMovementSuppression(ref disp);
                            _toDestroy.Add(entity);
                            continue;
                        }

                        // 叠加位移的替换合同：新位移段重置窗口时钟，
                        // maxDurationMs 约束单段位移而非连锁累计。
                        if (disp.WindowRefreshRequested)
                        {
                            _poseAuthorityArbiter.RefreshWindow(World, disp.TargetEntity);
                            disp.WindowRefreshRequested = false;
                        }
                    }

                    Fix64 stepCm = Fix64.FromInt(disp.TotalDistanceCm) / Fix64.FromInt(disp.TotalDurationTicks);
                    if (stepCm > disp.RemainingDistanceCm)
                    {
                        stepCm = disp.RemainingDistanceCm;
                    }

                    // 交还阈值比较必须留在定点域：把每秒阈值折算成本 tick 的定点步长下限，
                    // 避免 float 除法的平台舍入差异翻转结构性分支。
                    if (isNavAgent && dt > 0f &&
                        stepCm < Fix64.FromFloat(participation.DisplacementHandbackSpeedThresholdCmPerSec) * Fix64.FromFloat(dt))
                    {
                        EndPoseWindow(ref disp);
                        _toDestroy.Add(entity);
                        continue;
                    }

                    Fix64Vec2 direction = ComputeDirection(in disp, World);
                    ApplyPositionStep(disp.TargetEntity, direction * stepCm);

                    disp.RemainingDistanceCm -= stepCm;
                    disp.RemainingTicks--;

                    if (disp.RemainingTicks <= 0 || disp.RemainingDistanceCm <= Fix64.Zero)
                    {
                        if (isNavAgent)
                        {
                            EndPoseWindow(ref disp);
                        }
                        else
                        {
                            ClearMovementSuppression(ref disp);
                        }

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

        /// <summary>窗口正常结束：在固定步边界把写权交还 Nav，并撤销移动抑制。</summary>
        private void EndPoseWindow(ref DisplacementState disp)
        {
            _poseAuthorityArbiter.RequestDisplacementHandback(World, disp.TargetEntity);
            disp.PoseWindowRequested = false;
            ClearMovementSuppression(ref disp);
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
            // physicsPresence=Kinematic 的参与单位：Position2D 由
            // KinematicTargetPoseBuffer2D → KinematicDriveSystem2D 唯一驱动（massnav→kinematic
            // 桥喂送已提交 WorldPositionCm，物理步内应用并推导 Velocity2D）。位移窗口只写
            // WorldPositionCm；直写 Position2D 会绕过速度推导、在物理步之外瞬移刚体（双写违规）。
            bool kinematicPoseDriven =
                World.TryGet(target, out MovementParticipation participation) &&
                participation.PhysicsPresence == PhysicsPresenceKind.Kinematic;

            if (!kinematicPoseDriven && World.Has<Position2D>(target))
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
