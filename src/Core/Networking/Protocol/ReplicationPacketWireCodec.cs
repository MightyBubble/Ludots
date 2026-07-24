using System;
using Ludots.Core.Networking.Replication;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Full/delta replication packet codec over ReplicationPacketBuffer.
    /// </summary>
    public static class ReplicationPacketWireCodec
    {
        public const int HeaderSizeInBytes = 1 + 3 + 8 + 4 + 8 + 8 + 2 + 2 + 2 + 2;
        public const int UpsertSizeInBytes = 4 + 4 + 4 + 4 + 8 + 8 + 8 + 8;
        public const int RemovalSizeInBytes = 4 + 4;
        public const int DisclosureChangeSizeInBytes = 8 + 8 + 4 + 4 + 1 + 3;

        public static int GetPayloadSize(int upsertCount, int removalCount, int disclosureChangeCount)
        {
            if (upsertCount < 0 || removalCount < 0 || disclosureChangeCount < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            return checked(
                HeaderSizeInBytes
                + (upsertCount * UpsertSizeInBytes)
                + (removalCount * RemovalSizeInBytes)
                + (disclosureChangeCount * DisclosureChangeSizeInBytes));
        }

        public static NetworkWireCodecStatus TryEncode(
            ReplicationPacketBuffer packet,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (packet is null)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            ReplicationPacketHeader header = packet.Header;
            if (!IsKnownPacketKind(header.Kind))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            ReadOnlySpan<ReplicatedEntityState> upserts = packet.Upserts;
            ReadOnlySpan<NetworkEntityHandle> removals = packet.Removals;
            ReadOnlySpan<ReplicationDisclosureChange> disclosures = packet.DisclosureChanges;

            if ((uint)upserts.Length > ushort.MaxValue ||
                (uint)removals.Length > ushort.MaxValue ||
                (uint)disclosures.Length > ushort.MaxValue)
            {
                return NetworkWireCodecStatus.Overflow;
            }

            int required = GetPayloadSize(upserts.Length, removals.Length, disclosures.Length);
            if (destination.Length < required)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            for (int i = 0; i < upserts.Length; i++)
            {
                if (!upserts[i].Entity.IsValid || upserts[i].SchemaId <= 0)
                {
                    return upserts[i].SchemaId <= 0
                        ? NetworkWireCodecStatus.UnknownSchema
                        : NetworkWireCodecStatus.InvalidHandle;
                }
            }

            for (int i = 0; i < removals.Length; i++)
            {
                if (!removals[i].IsValid)
                {
                    return NetworkWireCodecStatus.InvalidHandle;
                }
            }

            for (int i = 0; i < disclosures.Length; i++)
            {
                if (!disclosures[i].Entity.IsValid || !IsKnownDisclosureKind(disclosures[i].Kind))
                {
                    return !disclosures[i].Entity.IsValid
                        ? NetworkWireCodecStatus.InvalidHandle
                        : NetworkWireCodecStatus.InvalidEnum;
                }
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)header.Kind) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, header.SessionEpoch) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, header.Tick) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, header.SnapshotId) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, header.BaselineSnapshotId) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, (ushort)upserts.Length) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, (ushort)removals.Length) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, (ushort)disclosures.Length) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, 0))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            for (int i = 0; i < upserts.Length; i++)
            {
                NetworkWireCodecStatus status = TryWriteUpsert(destination, ref offset, in upserts[i]);
                if (status != NetworkWireCodecStatus.Success)
                {
                    return status;
                }
            }

            for (int i = 0; i < removals.Length; i++)
            {
                if (!NetworkWireBinary.TryWriteHandle(destination, ref offset, removals[i]))
                {
                    return NetworkWireCodecStatus.BufferTooSmall;
                }
            }

            for (int i = 0; i < disclosures.Length; i++)
            {
                NetworkWireCodecStatus status = TryWriteDisclosure(destination, ref offset, in disclosures[i]);
                if (status != NetworkWireCodecStatus.Success)
                {
                    return status;
                }
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecode(
            ReadOnlySpan<byte> source,
            ReplicationPacketBuffer packet)
        {
            if (packet is null)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (source.Length < HeaderSizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadByte(source, ref offset, out byte kindByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong sessionEpoch) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint tick) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong snapshotId) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong baselineSnapshotId) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort upsertCount) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort removalCount) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort disclosureCount) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out _))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (!IsKnownPacketKindByte(kindByte))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            long required = HeaderSizeInBytes
                + ((long)upsertCount * UpsertSizeInBytes)
                + ((long)removalCount * RemovalSizeInBytes)
                + ((long)disclosureCount * DisclosureChangeSizeInBytes);
            if (required > source.Length)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (required < source.Length)
            {
                return NetworkWireCodecStatus.TrailingBytes;
            }

            if (upsertCount > packet.EntityCapacity ||
                removalCount > packet.EntityCapacity ||
                disclosureCount > packet.DisclosureCapacity)
            {
                return NetworkWireCodecStatus.CapacityExhausted;
            }

            var header = new ReplicationPacketHeader(
                (ReplicationPacketKind)kindByte,
                sessionEpoch,
                tick,
                snapshotId,
                baselineSnapshotId);
            packet.Reset(in header);

            for (int i = 0; i < upsertCount; i++)
            {
                NetworkWireCodecStatus status = TryReadUpsert(source, ref offset, out ReplicatedEntityState state);
                if (status != NetworkWireCodecStatus.Success)
                {
                    packet.Reset(default);
                    return status;
                }

                if (!packet.TryAddUpsert(in state))
                {
                    packet.Reset(default);
                    return NetworkWireCodecStatus.CapacityExhausted;
                }
            }

            for (int i = 0; i < removalCount; i++)
            {
                NetworkWireCodecStatus status = NetworkWireBinary.TryReadHandle(source, ref offset, out NetworkEntityHandle entity);
                if (status != NetworkWireCodecStatus.Success)
                {
                    packet.Reset(default);
                    return status;
                }

                if (!packet.TryAddRemoval(entity))
                {
                    packet.Reset(default);
                    return NetworkWireCodecStatus.CapacityExhausted;
                }
            }

            for (int i = 0; i < disclosureCount; i++)
            {
                NetworkWireCodecStatus status = TryReadDisclosure(source, ref offset, out ReplicationDisclosureChange change);
                if (status != NetworkWireCodecStatus.Success)
                {
                    packet.Reset(default);
                    return status;
                }

                if (!packet.TryAddDisclosureChange(in change))
                {
                    packet.Reset(default);
                    return NetworkWireCodecStatus.CapacityExhausted;
                }
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                packet.Reset(default);
                return end;
            }

            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus TryWriteUpsert(
            Span<byte> destination,
            ref int offset,
            in ReplicatedEntityState state)
        {
            ReplicationStateVector values = state.Values;
            if (!NetworkWireBinary.TryWriteHandle(destination, ref offset, state.Entity) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, state.SchemaId) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, state.Revision) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, unchecked((ulong)values.Value0)) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, unchecked((ulong)values.Value1)) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, unchecked((ulong)values.Value2)) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, unchecked((ulong)values.Value3)))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus TryReadUpsert(
            ReadOnlySpan<byte> source,
            ref int offset,
            out ReplicatedEntityState state)
        {
            state = default;
            NetworkWireCodecStatus handleStatus = NetworkWireBinary.TryReadHandle(source, ref offset, out NetworkEntityHandle entity);
            if (handleStatus != NetworkWireCodecStatus.Success)
            {
                return handleStatus;
            }

            if (!NetworkWireBinary.TryReadInt32(source, ref offset, out int schemaId) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint revision) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong v0) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong v1) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong v2) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong v3))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (schemaId <= 0)
            {
                return NetworkWireCodecStatus.UnknownSchema;
            }

            var values = new ReplicationStateVector(
                unchecked((long)v0),
                unchecked((long)v1),
                unchecked((long)v2),
                unchecked((long)v3));
            state = new ReplicatedEntityState(entity, schemaId, revision, in values);
            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus TryWriteDisclosure(
            Span<byte> destination,
            ref int offset,
            in ReplicationDisclosureChange change)
        {
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, change.Sequence) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, change.SnapshotId) ||
                !NetworkWireBinary.TryWriteHandle(destination, ref offset, change.Entity) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)change.Kind) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus TryReadDisclosure(
            ReadOnlySpan<byte> source,
            ref int offset,
            out ReplicationDisclosureChange change)
        {
            change = default;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong sequence) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong snapshotId))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            NetworkWireCodecStatus handleStatus = NetworkWireBinary.TryReadHandle(source, ref offset, out NetworkEntityHandle entity);
            if (handleStatus != NetworkWireCodecStatus.Success)
            {
                return handleStatus;
            }

            if (!NetworkWireBinary.TryReadByte(source, ref offset, out byte kindByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (!IsKnownDisclosureKindByte(kindByte))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            change = new ReplicationDisclosureChange(
                sequence,
                snapshotId,
                entity,
                (ReplicationDisclosureChangeKind)kindByte);
            return NetworkWireCodecStatus.Success;
        }

        private static bool IsKnownPacketKind(ReplicationPacketKind kind) => IsKnownPacketKindByte((byte)kind);

        private static bool IsKnownPacketKindByte(byte value) =>
            value is (byte)ReplicationPacketKind.Full or (byte)ReplicationPacketKind.Delta;

        private static bool IsKnownDisclosureKind(ReplicationDisclosureChangeKind kind) =>
            IsKnownDisclosureKindByte((byte)kind);

        private static bool IsKnownDisclosureKindByte(byte value) =>
            value is (byte)ReplicationDisclosureChangeKind.Reveal or (byte)ReplicationDisclosureChangeKind.Conceal;
    }
}
