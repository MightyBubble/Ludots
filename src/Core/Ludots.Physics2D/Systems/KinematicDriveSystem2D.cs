using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// Applies externally submitted kinematic target poses at the start of every physics step
    /// (issue #732). For each kinematic body:
    /// - with a pending target: PreviousPosition2D ← current, Position2D ← target (verbatim,
    ///   no fixed-point drift), Rotation2D ← target, Velocity2D.Linear ← Δpose/dt;
    /// - without a pending target: the body holds its pose and Velocity2D is zeroed.
    /// Angular velocity is never derived (out of scope for #732).
    /// Also enforces the explicit kinematicBodyCapacity budget and rejects pending targets
    /// that do not resolve to a live kinematic body.
    /// </summary>
    public sealed class KinematicDriveSystem2D : BaseSystem<World, float>
    {
        private static readonly QueryDescription _bodiesQuery = new QueryDescription()
            .WithAll<Position2D, Velocity2D, Mass2D>();

        private readonly KinematicTargetPoseBuffer2D _poseBuffer;

        public KinematicDriveSystem2D(World world, KinematicTargetPoseBuffer2D poseBuffer) : base(world)
        {
            _poseBuffer = poseBuffer ?? throw new ArgumentNullException(nameof(poseBuffer));
        }

        public override void Update(in float deltaTime)
        {
            if (_poseBuffer.PendingCount > 0 && !(deltaTime > 0f))
            {
                throw new InvalidOperationException(
                    "KinematicDriveSystem2D requires a positive fixed step delta to derive kinematic velocity from target poses.");
            }

            var job = new DriveJob
            {
                PoseBuffer = _poseBuffer,
                InverseDt = deltaTime > 0f ? Fix64.OneValue / Fix64.FromFloat(deltaTime) : Fix64.Zero
            };
            foreach (ref var chunk in World.Query(in _bodiesQuery))
            {
                job.Execute(ref chunk);
            }

            if (job.KinematicBodyCount > _poseBuffer.Capacity)
            {
                throw new InvalidOperationException(
                    $"Kinematic body count {job.KinematicBodyCount} exceeds kinematicBodyCapacity={_poseBuffer.Capacity}. Raise 'Physics2D/kinematic.json' kinematicBodyCapacity.");
            }

            if (job.AppliedTargetCount != _poseBuffer.PendingCount)
            {
                ThrowUnappliedTargets();
            }

            _poseBuffer.Clear();
        }

        private void ThrowUnappliedTargets()
        {
            foreach (Entity entity in _poseBuffer.PendingEntities)
            {
                if (!World.IsAlive(entity))
                {
                    throw new InvalidOperationException(
                        $"SetKinematicTargetPose targeted entity {entity.Id}, which is not alive.");
                }

                if (!World.TryGet(entity, out Mass2D mass) || !mass.IsKinematic)
                {
                    throw new InvalidOperationException(
                        $"SetKinematicTargetPose targeted entity {entity.Id}, which is not a kinematic body; only kinematic bodies may be pose-driven.");
                }

                if (!World.Has<Position2D>(entity) || !World.Has<Velocity2D>(entity))
                {
                    throw new InvalidOperationException(
                        $"SetKinematicTargetPose targeted kinematic entity {entity.Id} without Position2D/Velocity2D physics state.");
                }
            }

            throw new InvalidOperationException(
                "KinematicDriveSystem2D could not apply every pending kinematic target pose but found no offending entity; this indicates an internal pipeline defect.");
        }

        private struct DriveJob
        {
            public KinematicTargetPoseBuffer2D PoseBuffer;
            public Fix64 InverseDt;
            public int KinematicBodyCount;
            public int AppliedTargetCount;

            public void Execute(ref Chunk chunk)
            {
                if (chunk.Count <= 0)
                {
                    return;
                }

                chunk.GetSpan<Position2D, Velocity2D, Mass2D>(out var positions, out var velocities, out var masses);
                bool hasPreviousPosition = chunk.Has<PreviousPosition2D>();
                Span<PreviousPosition2D> previousPositions = hasPreviousPosition ? chunk.GetSpan<PreviousPosition2D>() : default;
                bool hasRotation = chunk.Has<Rotation2D>();
                Span<Rotation2D> rotations = hasRotation ? chunk.GetSpan<Rotation2D>() : default;
                ref Entity entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    ref Mass2D mass = ref masses[index];
                    if (!mass.IsKinematic)
                    {
                        continue;
                    }

                    KinematicBodyCount++;

                    ref Position2D position = ref positions[index];
                    ref Velocity2D velocity = ref velocities[index];

                    if (hasPreviousPosition)
                    {
                        previousPositions[index].Value = position.Value;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (PoseBuffer.TryGetPending(entity, out KinematicTargetPose2D pose))
                    {
                        velocity.Linear = (pose.PositionCm - position.Value) * InverseDt;
                        velocity.Angular = Fix64.Zero;
                        position.Value = pose.PositionCm;

                        if (hasRotation)
                        {
                            rotations[index].Value = pose.RotationRad;
                        }
                        else if (pose.RotationRad != Fix64.Zero)
                        {
                            throw new InvalidOperationException(
                                $"SetKinematicTargetPose submitted rotation {pose.RotationRad.ToFloat()} for kinematic entity {entity.Id} without a Rotation2D component.");
                        }

                        AppliedTargetCount++;
                    }
                    else
                    {
                        velocity.Linear = Fix64Vec2.Zero;
                        velocity.Angular = Fix64.Zero;
                    }
                }
            }
        }
    }
}
