using System;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Mathematics.FixedPoint;
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
                FixedDt = Fix64.FromFloat(deltaTime),
                DefaultBaseDamping = _config.DefaultBaseDampingFix64,
                MinVelocitySq = _config.MinVelocitySqFix64
            };
            foreach (ref var chunk in World.Query(in _dynamicQuery))
            {
                job.Execute(ref chunk);
            }
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

        private struct IntegrationJob
        {
            public Fix64 FixedDt;
            public Fix64 DefaultBaseDamping;
            public Fix64 MinVelocitySq;

            public void Execute(ref Chunk chunk)
            {
                if (chunk.Count <= 0)
                {
                    return;
                }

                chunk.GetSpan<Position2D, Velocity2D, Mass2D>(out var positions, out var velocities, out var masses);

                bool hasForceInput = chunk.Has<ForceInput2D>();
                Span<ForceInput2D> forceInputs = hasForceInput ? chunk.GetSpan<ForceInput2D>() : default;
                bool hasPreviousPosition = chunk.Has<PreviousPosition2D>();
                Span<PreviousPosition2D> previousPositions = hasPreviousPosition ? chunk.GetSpan<PreviousPosition2D>() : default;
                bool hasRotation = chunk.Has<Rotation2D>();
                Span<Rotation2D> rotations = hasRotation ? chunk.GetSpan<Rotation2D>() : default;
                bool hasMaterial = chunk.Has<PhysicsMaterial2D>();
                Span<PhysicsMaterial2D> materials = hasMaterial ? chunk.GetSpan<PhysicsMaterial2D>() : default;
                bool hasAppliedDamping = chunk.Has<AppliedDamping>();
                Span<AppliedDamping> appliedDampings = hasAppliedDamping ? chunk.GetSpan<AppliedDamping>() : default;

                foreach (int index in chunk)
                {
                    ref Position2D position = ref positions[index];
                    ref Velocity2D velocity = ref velocities[index];
                    ref Mass2D mass = ref masses[index];

                    // Only dynamic bodies integrate: static never moves, kinematic poses are
                    // applied verbatim by KinematicDriveSystem2D (no forces, damping, or clamping).
                    if (!mass.IsDynamic) continue;

                    if (hasForceInput)
                    {
                        ref ForceInput2D input = ref forceInputs[index];
                        velocity.Linear = velocity.Linear + input.Force * FixedDt;
                        input.Force = Fix64Vec2.Zero;
                    }

                    if (hasPreviousPosition)
                    {
                        previousPositions[index].Value = position.Value;
                    }

                    position.Value = position.Value + velocity.Linear * FixedDt;

                    if (hasRotation)
                    {
                        rotations[index].Value = rotations[index].Value + velocity.Angular * FixedDt;
                    }

                    Fix64 baseDamping = hasMaterial ? materials[index].BaseDamping : DefaultBaseDamping;
                    Fix64 fieldDamping = hasAppliedDamping ? appliedDampings[index].TotalFieldDamping : Fix64.OneValue;
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
