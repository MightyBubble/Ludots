using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics2D
{
    public struct KinematicTargetPose2D
    {
        public Fix64Vec2 PositionCm;
        public Fix64 RotationRad;
    }

    /// <summary>
    /// The only sanctioned drive channel for kinematic bodies.
    /// External systems submit at most one target pose per entity per physics fixed step;
    /// KinematicDriveSystem2D consumes the buffer at the start of the step, applies the pose
    /// verbatim, and derives Velocity2D = Δpose/dt so restitution/friction/relative-velocity
    /// terms stay correct. External code must never write kinematic Velocity2D directly.
    /// Preallocated to the explicit kinematicBodyCapacity budget; exceeding it throws.
    /// </summary>
    public sealed class KinematicTargetPoseBuffer2D
    {
        private readonly Dictionary<Entity, KinematicTargetPose2D> _pending;

        public KinematicTargetPoseBuffer2D(int kinematicBodyCapacity)
        {
            if (kinematicBodyCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kinematicBodyCapacity),
                    kinematicBodyCapacity,
                    "kinematicBodyCapacity must be > 0.");
            }

            Capacity = kinematicBodyCapacity;
            _pending = new Dictionary<Entity, KinematicTargetPose2D>(kinematicBodyCapacity);
        }

        public int Capacity { get; }

        public int PendingCount => _pending.Count;

        public void SetKinematicTargetPose(Entity entity, Fix64Vec2 targetPositionCm, Fix64 targetRotationRad)
        {
            if (_pending.ContainsKey(entity))
            {
                throw new InvalidOperationException(
                    $"Kinematic target pose for entity {entity.Id} was already submitted this physics step; the contract is exactly one SetKinematicTargetPose per entity per fixed step.");
            }

            if (_pending.Count >= Capacity)
            {
                throw new InvalidOperationException(
                    $"Kinematic target pose buffer exhausted: kinematicBodyCapacity={Capacity} reached. Raise 'Physics2D/kinematic.json' kinematicBodyCapacity.");
            }

            _pending.Add(entity, new KinematicTargetPose2D
            {
                PositionCm = targetPositionCm,
                RotationRad = targetRotationRad
            });
        }

        public bool TryGetPending(Entity entity, out KinematicTargetPose2D pose)
        {
            return _pending.TryGetValue(entity, out pose);
        }

        public Dictionary<Entity, KinematicTargetPose2D>.KeyCollection PendingEntities => _pending.Keys;

        public void Clear()
        {
            _pending.Clear();
        }
    }
}
