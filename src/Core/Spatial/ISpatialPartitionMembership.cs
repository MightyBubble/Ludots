using Arch.Core;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Owns the spatial-partition membership derived from an entity's current ECS state.
    /// Structural callers must invoke it outside ECS chunk iteration.
    /// </summary>
    public interface ISpatialPartitionMembership
    {
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
