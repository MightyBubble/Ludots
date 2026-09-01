using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Side-effect-free result for a spatial membership operation that will run at commit time.
    /// </summary>
    public enum SpatialMembershipValidationResult : byte
    {
        Success = 0,
        EntityUnavailable = 1,
        InvalidState = 2,
        PositionOutOfBounds = 3,
        InvalidTarget = 4,
    }

    /// <summary>
    /// The effective spatial state an operation will leave after commit.
    /// </summary>
    public enum SpatialMembershipTargetKind : byte
    {
        Invalid = 0,
        NoMembership = 1,
        Position = 2,
    }

    /// <summary>
    /// Fixed-size preview of an entity's effective post-commit spatial state.
    /// </summary>
    public readonly struct SpatialMembershipTarget
    {
        private SpatialMembershipTarget(SpatialMembershipTargetKind kind, in WorldCmInt2 positionCm)
        {
            Kind = kind;
            PositionCm = positionCm;
        }

        public SpatialMembershipTargetKind Kind { get; }
        public WorldCmInt2 PositionCm { get; }

        public static SpatialMembershipTarget NoMembership =>
            new(SpatialMembershipTargetKind.NoMembership, default);

        public static SpatialMembershipTarget At(in WorldCmInt2 positionCm) =>
            new(SpatialMembershipTargetKind.Position, in positionCm);
    }

    /// <summary>
    /// Owns the spatial-partition membership derived from an entity's current ECS state.
    /// Structural callers must invoke it outside ECS chunk iteration.
    /// </summary>
    public interface ISpatialPartitionMembership
    {
        /// <summary>
        /// Validates synchronization from the entity's current ECS state without changing ECS or the partition.
        /// </summary>
        SpatialMembershipValidationResult ValidateSynchronize(Entity entity);

        /// <summary>
        /// Validates a projected post-commit target. <see cref="Entity.Null"/> is valid for a pending create.
        /// </summary>
        SpatialMembershipValidationResult ValidateSynchronize(
            Entity entity,
            in SpatialMembershipTarget target);

        /// <summary>
        /// Validates deactivation without changing ECS or the partition.
        /// </summary>
        SpatialMembershipValidationResult ValidateDeactivate(Entity entity);

        /// <summary>
        /// Validates terminal removal without changing ECS or the partition.
        /// </summary>
        SpatialMembershipValidationResult ValidateRemove(Entity entity);

        void Synchronize(Entity entity);

        /// <summary>
        /// Removes the entity from the partition while retaining <see cref="SpatialCellRef"/>
        /// in <see cref="SpatialMembershipState.Deactivated"/>. Regular Update must not reactivate it;
        /// only <see cref="Synchronize"/> restores Active membership.
        /// </summary>
        void Deactivate(Entity entity);

        /// <summary>
        /// Terminal membership removal: drops partition entry and removes <see cref="SpatialCellRef"/>.
        /// </summary>
        void Remove(Entity entity);
    }
}
