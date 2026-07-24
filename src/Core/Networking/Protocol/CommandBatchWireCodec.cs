using System;
using Ludots.Core.Networking.Replication;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Semantic command-batch codec. Actors are NetworkEntityHandle only; PlayerId is never on the wire.
    /// </summary>
    public static class CommandBatchWireCodec
    {
        public static int GetPayloadSize(int entryCount)
        {
            if (entryCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entryCount));
            }

            return checked(NetworkCommandBatchHeader.SizeInBytes + (entryCount * NetworkCommandWireEntry.SizeInBytes));
        }

        public static NetworkWireCodecStatus TryEncode(
            in NetworkCommandBatchHeader header,
            ReadOnlySpan<NetworkCommandWireEntry> entries,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (header.EntryCount != entries.Length)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if ((uint)entries.Length > ushort.MaxValue)
            {
                return NetworkWireCodecStatus.Overflow;
            }

            int required = GetPayloadSize(entries.Length);
            if (destination.Length < required)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                NetworkWireCodecStatus entryStatus = ValidateEntry(in entries[i]);
                if (entryStatus != NetworkWireCodecStatus.Success)
                {
                    return entryStatus;
                }
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, header.SessionEpoch) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, header.ClientBatchSequence) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, header.TargetTick) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, header.AcknowledgedCommittedTick) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, header.EntryCount) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, 0))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                NetworkWireCodecStatus write = TryWriteEntry(destination, ref offset, in entries[i]);
                if (write != NetworkWireCodecStatus.Success)
                {
                    return write;
                }
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecode(
            ReadOnlySpan<byte> source,
            Span<NetworkCommandWireEntry> entries,
            out NetworkCommandBatchHeader header,
            out int entryCount)
        {
            header = default;
            entryCount = 0;
            if (source.Length < NetworkCommandBatchHeader.SizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong sessionEpoch) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong clientSequence) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int targetTick) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int acknowledgedCommittedTick) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort declaredCount) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out _))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            long required = NetworkCommandBatchHeader.SizeInBytes +
                ((long)declaredCount * NetworkCommandWireEntry.SizeInBytes);
            if (required > source.Length)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (required < source.Length)
            {
                return NetworkWireCodecStatus.TrailingBytes;
            }

            if (declaredCount > entries.Length)
            {
                return NetworkWireCodecStatus.CapacityExhausted;
            }

            for (int i = 0; i < declaredCount; i++)
            {
                NetworkWireCodecStatus read = TryReadEntry(source, ref offset, out NetworkCommandWireEntry entry);
                if (read != NetworkWireCodecStatus.Success)
                {
                    return read;
                }

                entries[i] = entry;
            }

            NetworkWireCodecStatus end = NetworkWireBinary.EnsureExactEnd(source, offset);
            if (end != NetworkWireCodecStatus.Success)
            {
                return end;
            }

            header = new NetworkCommandBatchHeader(
                sessionEpoch,
                clientSequence,
                targetTick,
                acknowledgedCommittedTick,
                declaredCount);
            entryCount = declaredCount;
            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus ValidateEntry(in NetworkCommandWireEntry entry)
        {
            if (!entry.Actor.IsValid)
            {
                return NetworkWireCodecStatus.InvalidHandle;
            }

            if (entry.OrderTypeId <= 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            NetworkCommandTargetPayload target = entry.Target;
            return ValidateTarget(in target);
        }

        private static NetworkWireCodecStatus ValidateTarget(in NetworkCommandTargetPayload target)
        {
            if (!IsKnownTargetKind(target.Kind))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (target.Kind is NetworkCommandTargetKind.NetworkEntity or NetworkCommandTargetKind.WorldPositionAndEntity)
            {
                if (!NetworkEntityHandle.TryCreate(target.TargetSlot, target.TargetGeneration, out _))
                {
                    return NetworkWireCodecStatus.InvalidHandle;
                }
            }

            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus TryWriteEntry(
            Span<byte> destination,
            ref int offset,
            in NetworkCommandWireEntry entry)
        {
            if (!NetworkWireBinary.TryWriteHandle(destination, ref offset, entry.Actor) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, entry.OrderTypeId))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            NetworkCommandTargetPayload target = entry.Target;
            return TryWriteTarget(destination, ref offset, in target);
        }

        private static NetworkWireCodecStatus TryWriteTarget(
            Span<byte> destination,
            ref int offset,
            in NetworkCommandTargetPayload target)
        {
            if (!NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)target.Kind) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, target.PositionXCm) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, target.PositionYCm) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, target.PositionZCm) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, target.TargetSlot) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, target.TargetGeneration) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, target.Arg0) ||
                !NetworkWireBinary.TryWriteInt32(destination, ref offset, target.Arg1))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus TryReadEntry(
            ReadOnlySpan<byte> source,
            ref int offset,
            out NetworkCommandWireEntry entry)
        {
            entry = default;
            NetworkWireCodecStatus handleStatus = NetworkWireBinary.TryReadHandle(source, ref offset, out NetworkEntityHandle actor);
            if (handleStatus != NetworkWireCodecStatus.Success)
            {
                return handleStatus;
            }

            if (!NetworkWireBinary.TryReadInt32(source, ref offset, out int orderTypeId))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            NetworkWireCodecStatus targetStatus = TryReadTarget(source, ref offset, out NetworkCommandTargetPayload target);
            if (targetStatus != NetworkWireCodecStatus.Success)
            {
                return targetStatus;
            }

            if (orderTypeId <= 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            entry = new NetworkCommandWireEntry(actor, orderTypeId, in target);
            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus TryReadTarget(
            ReadOnlySpan<byte> source,
            ref int offset,
            out NetworkCommandTargetPayload target)
        {
            target = default;
            if (!NetworkWireBinary.TryReadByte(source, ref offset, out byte kindByte) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out _) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int x) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int y) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int z) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int slot) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint generation) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int arg0) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int arg1))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (!IsKnownTargetKind((NetworkCommandTargetKind)kindByte))
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            var kind = (NetworkCommandTargetKind)kindByte;
            if (kind is NetworkCommandTargetKind.NetworkEntity or NetworkCommandTargetKind.WorldPositionAndEntity)
            {
                if (!NetworkEntityHandle.TryCreate(slot, generation, out _))
                {
                    return NetworkWireCodecStatus.InvalidHandle;
                }
            }

            target = new NetworkCommandTargetPayload(kind, x, y, z, slot, generation, arg0, arg1);
            return NetworkWireCodecStatus.Success;
        }

        private static bool IsKnownTargetKind(NetworkCommandTargetKind kind) =>
            kind is >= NetworkCommandTargetKind.None and <= NetworkCommandTargetKind.WorldPositionAndEntity;
    }
}
