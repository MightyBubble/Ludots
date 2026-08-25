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
    }
}
