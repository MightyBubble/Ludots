using System;

namespace Ludots.Core.Networking
{
    [Flags]
    public enum GameplayReplicationSnapshotFlags : ushort
    {
        None = 0,
        HasFacing = 1 << 0,
        HasTeam = 1 << 1,
        HasPlayerOwner = 1 << 2,
        HasPresentationStableId = 1 << 3,
    }
}
