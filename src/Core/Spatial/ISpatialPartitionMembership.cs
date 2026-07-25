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

        void Remove(Entity entity);
    }
}
