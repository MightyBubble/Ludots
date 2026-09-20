using Ludots.Core.Fields;

namespace Ludots.Core.Components
{
    /// <summary>
    /// Marks an entity as one materialized region of a discrete-id field layer.
    /// Region entities carry <see cref="MapEntity"/> and are cleaned up with the map.
    /// </summary>
    public struct RegionCm
    {
        public FieldLayerId LayerId;
        public int RegionId;
        public string RegionKey;
    }

    /// <summary>
    /// A hierarchy group entity materialized from a roster parent that owns no cells of
    /// its own (e.g. a coarse grouping above the finest layer). Groups exist through the
    /// ChildOf edges of their members, not through a grid of their own.
    /// </summary>
    public struct RegionGroupCm
    {
        public string GroupKey;
    }

    /// <summary>Cell count of a materialized region, tallied at materialization time.</summary>
    public struct RegionFootprintCm
    {
        public int CellCount;
    }

    /// <summary>
    /// Opt-in for field membership tracking: the entity reports membership on exactly
    /// this layer. Assets and spawn templates declare it; untagged entities cost nothing.
    /// </summary>
    public struct FieldTrackedCm
    {
        public FieldLayerId LayerId;
    }

    /// <summary>
    /// Membership cache maintained by the differential system; cell-unchanged entities
    /// are skipped without any set write or event.
    /// </summary>
    public struct RegionMembershipCm
    {
        public int LayerId;
        public int RegionId;
        public int LastCellX;
        public int LastCellY;
        public byte Initialized;

        /// <summary>
        /// Change stamp of the chunk under the entity when membership was last
        /// evaluated; a mismatch forces re-evaluation even for stationary entities,
        /// which is how runtime redraws reach units standing inside repainted cells.
        /// </summary>
        public long LastChunkStamp;
    }
}
