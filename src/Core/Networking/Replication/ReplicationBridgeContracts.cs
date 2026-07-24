namespace Ludots.Core.Networking.Replication
{
    public enum ReplicationBridgeResult : byte
    {
        Success = 0,
        InvalidInput = 1,
        EntityUnavailable = 2,
        SchemaMissing = 3,
        SchemaNotRegistered = 4,
        ProjectionFailed = 5,
        ResyncRequired = 6,
        CapacityContractViolated = 7,
        EpochMismatch = 8,
        SnapshotOutOfOrder = 9,
        InvalidPacket = 10,
        EcsStateMismatch = 11,
        SchemaApplyRejected = 12,
        TornDown = 13,
    }

    internal static class ReplicationBridgeResultMapper
    {
        public static ReplicationBridgeResult FromBuild(ReplicationBuildResult result)
        {
            return result switch
            {
                ReplicationBuildResult.Success => ReplicationBridgeResult.Success,
                ReplicationBuildResult.InvalidInput => ReplicationBridgeResult.InvalidInput,
                ReplicationBuildResult.EpochMismatch => ReplicationBridgeResult.EpochMismatch,
                ReplicationBuildResult.SnapshotOutOfOrder => ReplicationBridgeResult.SnapshotOutOfOrder,
                ReplicationBuildResult.BaselineUnavailable => ReplicationBridgeResult.ResyncRequired,
                ReplicationBuildResult.PacketCapacityExceeded => ReplicationBridgeResult.CapacityContractViolated,
                ReplicationBuildResult.DisclosureLogCapacityExceeded => ReplicationBridgeResult.CapacityContractViolated,
                _ => ReplicationBridgeResult.InvalidInput,
            };
        }

        public static ReplicationBridgeResult FromApply(ReplicationApplyResult result)
        {
            return result switch
            {
                ReplicationApplyResult.Success => ReplicationBridgeResult.Success,
                ReplicationApplyResult.InvalidPacket => ReplicationBridgeResult.InvalidPacket,
                ReplicationApplyResult.EpochMismatch => ReplicationBridgeResult.EpochMismatch,
                ReplicationApplyResult.BaselineMismatch => ReplicationBridgeResult.ResyncRequired,
                ReplicationApplyResult.SnapshotOutOfOrder => ReplicationBridgeResult.SnapshotOutOfOrder,
                ReplicationApplyResult.CapacityExceeded => ReplicationBridgeResult.CapacityContractViolated,
                _ => ReplicationBridgeResult.InvalidPacket,
            };
        }
    }
}
