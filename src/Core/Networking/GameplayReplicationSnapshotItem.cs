namespace Ludots.Core.Networking
{
    public struct GameplayReplicationSnapshotItem
    {
        public int ReplicationEntityId;
        public int PresentationStableId;
        public int TeamId;
        public int PlayerId;
        public long PositionXRaw;
        public long PositionYRaw;
        public float FacingAngleRad;
        public GameplayReplicationSnapshotFlags Flags;
    }
}
