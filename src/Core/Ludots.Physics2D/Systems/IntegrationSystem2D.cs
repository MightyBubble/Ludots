using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// 2D 积分系统 — 全定点数确定性物理积分。
    /// 
    /// 职责：欧拉积分 + 组合阻尼（固有 × 场阻尼）。
    /// 执行时机：在 ApplyImpulsesSystem 和 FieldDetectorSystem 之后。
    /// </summary>
    public sealed class IntegrationSystem2D : BaseSystem<World, float>
    {
        private static readonly Fix64 DefaultBaseDamping = PhysicsMaterial2D.Default.BaseDamping;
        private static readonly Fix64 MinVelocitySq = Fix64.FromFloat(0.0001f);

        private static readonly QueryDescription _dynamicQuery = new QueryDescription()
            .WithAll<Position2D, Velocity2D, Mass2D>()
            .WithNone<SleepingTag>();

        private static readonly QueryDescription _needsPrevPosQuery = new QueryDescription()
            .WithAll<Position2D, Velocity2D, Mass2D>()
            .WithNone<SleepingTag, PreviousPosition2D>();

        private readonly CommandBuffer _commandBuffer = new();

        public IntegrationSystem2D(World world) : base(world)
        {
        }

        public override void Update(in float deltaTime)
        {
            var dt = Fix64.FromFloat(deltaTime);

            InitializeMissingPrevPos();

            var fixedDt = dt;

            World.Query(in _dynamicQuery, (Entity entity,
                ref Position2D position,
                ref Velocity2D velocity,
                ref Mass2D mass) =>
            {
                if (mass.IsStatic) return;

                if (World.TryGet(entity, out ForceInput2D input))
                {
                    velocity.Linear = velocity.Linear + input.Force * fixedDt;
                    World.Set(entity, new ForceInput2D { Force = Fix64Vec2.Zero });
                }

                // 存储前一帧位置（渲染插值用）
                if (World.TryGet<PreviousPosition2D>(entity, out var prevPos))
                {
                    World.Set(entity, new PreviousPosition2D { Value = position.Value });
                }

                // 欧拉积分: position += velocity * dt
                position.Value = position.Value + velocity.Linear * fixedDt;

                if (World.TryGet<Rotation2D>(entity, out var rotation))
                {
                    rotation.Value = rotation.Value + velocity.Angular * fixedDt;
                    World.Set(entity, rotation);
                }

                // 组合阻尼: baseDamping × fieldDamping（全定点数，无转换）
                Fix64 baseDamping = DefaultBaseDamping;
                if (World.TryGet(entity, out PhysicsMaterial2D material))
                {
                    baseDamping = material.BaseDamping;
                }

                Fix64 fieldDamping = Fix64.OneValue;
                if (World.TryGet(entity, out AppliedDamping appliedDamping))
                {
                    fieldDamping = appliedDamping.TotalFieldDamping;
                }

                Fix64 finalDamping = baseDamping * fieldDamping;
                velocity.Linear = velocity.Linear * finalDamping;
                velocity.Angular = velocity.Angular * finalDamping;

                // 速度阈值归零
                if (velocity.Linear.LengthSquared() < MinVelocitySq)
                {
                    velocity.Linear = Fix64Vec2.Zero;
                }

                if (velocity.Angular * velocity.Angular < MinVelocitySq)
                {
                    velocity.Angular = Fix64.Zero;
                }
            });
        }

        private void InitializeMissingPrevPos()
        {
            World.Query(in _needsPrevPosQuery, (Entity entity, ref Position2D position) =>
            {
                _commandBuffer.Add(entity, new PreviousPosition2D { Value = position.Value });
            });

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }
    }
}
