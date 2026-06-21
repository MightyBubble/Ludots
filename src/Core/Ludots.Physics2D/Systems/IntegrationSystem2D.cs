using System;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class IntegrationSystem2D : BaseSystem<World, float>
    {
        private static readonly QueryDescription _dynamicQuery = new QueryDescription()
            .WithAll<Position2D, Velocity2D, Mass2D>()
            .WithNone<SleepingTag>();

        private static readonly QueryDescription _needsPrevPosQuery = new QueryDescription()
            .WithAll<Position2D, Velocity2D, Mass2D>()
            .WithNone<SleepingTag, PreviousPosition2D>();

        private readonly CommandBuffer _commandBuffer = new();
        private readonly Physics2DSolverConfig _config;

        public IntegrationSystem2D(World world, Physics2DSolverConfig config) : base(world)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override void Update(in float deltaTime)
        {
            InitializeMissingPrevPos();

            var job = new IntegrationJob
            {
                World = World,
                FixedDt = Fix64.FromFloat(deltaTime),
                DefaultBaseDamping = _config.DefaultBaseDampingFix64,
                MinVelocitySq = _config.MinVelocitySqFix64
            };
            World.InlineEntityQuery<IntegrationJob, Position2D, Velocity2D, Mass2D>(
                in _dynamicQuery,
                ref job);
        }

        private void InitializeMissingPrevPos()
        {
            var job = new InitializePrevPosJob { CommandBuffer = _commandBuffer };
            World.InlineEntityQuery<InitializePrevPosJob, Position2D>(in _needsPrevPosQuery, ref job);

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private struct IntegrationJob : IForEachWithEntity<Position2D, Velocity2D, Mass2D>
        {
            public World World;
            public Fix64 FixedDt;
            public Fix64 DefaultBaseDamping;
            public Fix64 MinVelocitySq;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref Position2D position, ref Velocity2D velocity, ref Mass2D mass)
            {
                if (mass.IsStatic) return;

                if (World.TryGet(entity, out NavDesiredVelocity2D desiredVelocity))
                {
                    velocity.Linear = desiredVelocity.ValueCmPerSec;
                }

                if (World.TryGet(entity, out ForceInput2D input))
                {
                    velocity.Linear = velocity.Linear + input.Force * FixedDt;
                    World.Set(entity, new ForceInput2D { Force = Fix64Vec2.Zero });
                }

                if (World.TryGet<PreviousPosition2D>(entity, out _))
                {
                    World.Set(entity, new PreviousPosition2D { Value = position.Value });
                }

                position.Value = position.Value + velocity.Linear * FixedDt;

                if (World.TryGet<Rotation2D>(entity, out var rotation))
                {
                    rotation.Value = rotation.Value + velocity.Angular * FixedDt;
                    World.Set(entity, rotation);
                }

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

                if (velocity.Linear.LengthSquared() < MinVelocitySq)
                {
                    velocity.Linear = Fix64Vec2.Zero;
                }

                if (velocity.Angular * velocity.Angular < MinVelocitySq)
                {
                    velocity.Angular = Fix64.Zero;
                }
            }
        }

        private struct InitializePrevPosJob : IForEachWithEntity<Position2D>
        {
            public CommandBuffer CommandBuffer;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref Position2D position)
            {
                CommandBuffer.Add(entity, new PreviousPosition2D { Value = position.Value });
            }
        }
    }
}
