using System;
using Ludots.Core.Networking.Session;

namespace Ludots.Core.Networking.Protocol
{
    public static class RoomControlWireCodec
    {
        public const int ReadyIntentSizeInBytes = 8 + 1 + 3;
        public const int SnapshotHeaderSizeInBytes = 8 + 8 + 4 + 4 + 2 + 2 + 2 + 1 + 1;
        public const int SnapshotSeatSizeInBytes = 2 + 1 + 1 + 4 + 4;

        public static int GetSnapshotPayloadSize(int seatCount)
        {
            if ((uint)(seatCount - 1) >= ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(seatCount));
            }

            return checked(SnapshotHeaderSizeInBytes + (seatCount * SnapshotSeatSizeInBytes));
        }

        public static NetworkWireCodecStatus TryEncodeReadyIntent(
            in NetworkRoomReadyIntent intent,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (intent.SessionEpoch.IsEmpty)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (intent.ReadyState is not NetworkRoomReadyState.Unready and not NetworkRoomReadyState.Ready)
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (destination.Length < ReadyIntentSizeInBytes)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, intent.SessionEpoch.Value) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)intent.ReadyState) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecodeReadyIntent(
            ReadOnlySpan<byte> source,
            out NetworkRoomReadyIntent intent)
        {
            intent = default;
            if (source.Length < ReadyIntentSizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (source.Length > ReadyIntentSizeInBytes)
            {
                return NetworkWireCodecStatus.TrailingBytes;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong epoch) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte ready) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte reserved0) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte reserved1) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte reserved2))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (epoch == 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (ready > (byte)NetworkRoomReadyState.Ready)
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if ((reserved0 | reserved1 | reserved2) != 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            intent = new NetworkRoomReadyIntent(new SessionEpoch(epoch), (NetworkRoomReadyState)ready);
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryEncodeSnapshot(
            in NetworkRoomSnapshotHeader header,
            ReadOnlySpan<NetworkRoomSeatSnapshot> seats,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            NetworkWireCodecStatus validation = ValidateSnapshot(in header, seats);
            if (validation != NetworkWireCodecStatus.Success)
            {
                return validation;
            }

            int required = GetSnapshotPayloadSize(seats.Length);
            if (destination.Length < required)
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryWriteUInt64(destination, ref offset, header.SessionEpoch.Value) ||
                !NetworkWireBinary.TryWriteUInt64(destination, ref offset, header.Revision) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, header.CommittedTick) ||
                !NetworkWireBinary.TryWriteUInt32(destination, ref offset, header.CountdownRemainingTicks) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, header.SeatCount) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, header.ConnectedSeatCount) ||
                !NetworkWireBinary.TryWriteUInt16(destination, ref offset, header.ReadySeatCount) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)header.Phase) ||
                !NetworkWireBinary.TryWriteByte(destination, ref offset, 0))
            {
                return NetworkWireCodecStatus.BufferTooSmall;
            }

            for (int i = 0; i < seats.Length; i++)
            {
                NetworkRoomSeatSnapshot seat = seats[i];
                if (!NetworkWireBinary.TryWriteUInt16(destination, ref offset, checked((ushort)seat.Slot)) ||
                    !NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)seat.ConnectionState) ||
                    !NetworkWireBinary.TryWriteByte(destination, ref offset, (byte)seat.ReadyState) ||
                    !NetworkWireBinary.TryWriteUInt32(destination, ref offset, seat.Generation) ||
                    !NetworkWireBinary.TryWriteInt32(destination, ref offset, seat.PlayerId.Value))
                {
                    return NetworkWireCodecStatus.BufferTooSmall;
                }
            }

            bytesWritten = offset;
            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus TryDecodeSnapshot(
            ReadOnlySpan<byte> source,
            Span<NetworkRoomSeatSnapshot> seats,
            out NetworkRoomSnapshotHeader header,
            out int seatCount)
        {
            header = default;
            seatCount = 0;
            if (source.Length < SnapshotHeaderSizeInBytes)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            int offset = 0;
            if (!NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong epoch) ||
                !NetworkWireBinary.TryReadUInt64(source, ref offset, out ulong revision) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint committedTick) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint countdownRemainingTicks) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort encodedSeatCount) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort encodedConnectedCount) ||
                !NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort encodedReadyCount) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte phase) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte reserved))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (encodedSeatCount == 0 || epoch == 0 || revision == 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            if (phase > (byte)NetworkRoomPhase.Started)
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            if (reserved != 0)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            int required = GetSnapshotPayloadSize(encodedSeatCount);
            if (source.Length < required)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (source.Length > required)
            {
                return NetworkWireCodecStatus.TrailingBytes;
            }

            if (seats.Length < encodedSeatCount)
            {
                seatCount = encodedSeatCount;
                return NetworkWireCodecStatus.CapacityExhausted;
            }

            int connectedCount = 0;
            int readyCount = 0;
            for (int i = 0; i < encodedSeatCount; i++)
            {
                NetworkWireCodecStatus decoded = TryDecodeSeat(source, offset, i, out NetworkRoomSeatSnapshot seat);
                if (decoded != NetworkWireCodecStatus.Success)
                {
                    return decoded;
                }

                connectedCount += seat.ConnectionState == NetworkRoomSeatConnectionState.Connected ? 1 : 0;
                readyCount += seat.ReadyState == NetworkRoomReadyState.Ready ? 1 : 0;
                offset += SnapshotSeatSizeInBytes;
            }

            NetworkRoomPhase roomPhase = (NetworkRoomPhase)phase;
            if (connectedCount != encodedConnectedCount || readyCount != encodedReadyCount ||
                !IsPhaseConsistent(roomPhase, encodedSeatCount, connectedCount, readyCount, countdownRemainingTicks))
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            offset = SnapshotHeaderSizeInBytes;
            for (int i = 0; i < encodedSeatCount; i++)
            {
                _ = TryDecodeSeat(source, offset, i, out seats[i]);
                offset += SnapshotSeatSizeInBytes;
            }

            header = new NetworkRoomSnapshotHeader(
                new SessionEpoch(epoch),
                revision,
                committedTick,
                countdownRemainingTicks,
                encodedSeatCount,
                encodedConnectedCount,
                encodedReadyCount,
                roomPhase);
            seatCount = encodedSeatCount;
            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus ValidateSnapshot(
            in NetworkRoomSnapshotHeader header,
            ReadOnlySpan<NetworkRoomSeatSnapshot> seats)
        {
            if (header.SessionEpoch.IsEmpty || header.Revision == 0 || seats.Length == 0 ||
                seats.Length > ushort.MaxValue || header.SeatCount != seats.Length)
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            int connectedCount = 0;
            int readyCount = 0;
            for (int i = 0; i < seats.Length; i++)
            {
                NetworkRoomSeatSnapshot seat = seats[i];
                if (seat.Slot != i ||
                    seat.ConnectionState is < NetworkRoomSeatConnectionState.Empty or > NetworkRoomSeatConnectionState.AwaitingReconnect ||
                    seat.ReadyState is < NetworkRoomReadyState.Unready or > NetworkRoomReadyState.Ready ||
                    (seat.ConnectionState == NetworkRoomSeatConnectionState.Empty &&
                        (seat.Generation != 0 || seat.PlayerId.Value != 0 || seat.ReadyState != NetworkRoomReadyState.Unready)) ||
                    (seat.ConnectionState != NetworkRoomSeatConnectionState.Empty &&
                        (seat.Generation == 0 || seat.PlayerId.Value != i + 1)) ||
                    (seat.ConnectionState != NetworkRoomSeatConnectionState.Connected &&
                        seat.ReadyState != NetworkRoomReadyState.Unready))
                {
                    return NetworkWireCodecStatus.InvalidInput;
                }

                connectedCount += seat.ConnectionState == NetworkRoomSeatConnectionState.Connected ? 1 : 0;
                readyCount += seat.ReadyState == NetworkRoomReadyState.Ready ? 1 : 0;
            }

            if (header.ConnectedSeatCount != connectedCount || header.ReadySeatCount != readyCount ||
                !IsPhaseConsistent(header.Phase, seats.Length, connectedCount, readyCount, header.CountdownRemainingTicks))
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            return NetworkWireCodecStatus.Success;
        }

        private static NetworkWireCodecStatus TryDecodeSeat(
            ReadOnlySpan<byte> source,
            int offset,
            int expectedSlot,
            out NetworkRoomSeatSnapshot seat)
        {
            seat = default;
            if (!NetworkWireBinary.TryReadUInt16(source, ref offset, out ushort slot) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte connectionState) ||
                !NetworkWireBinary.TryReadByte(source, ref offset, out byte readyState) ||
                !NetworkWireBinary.TryReadUInt32(source, ref offset, out uint generation) ||
                !NetworkWireBinary.TryReadInt32(source, ref offset, out int playerId))
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (connectionState > (byte)NetworkRoomSeatConnectionState.AwaitingReconnect ||
                readyState > (byte)NetworkRoomReadyState.Ready)
            {
                return NetworkWireCodecStatus.InvalidEnum;
            }

            NetworkRoomSeatConnectionState state = (NetworkRoomSeatConnectionState)connectionState;
            NetworkRoomReadyState ready = (NetworkRoomReadyState)readyState;
            if (slot != expectedSlot ||
                (state == NetworkRoomSeatConnectionState.Empty &&
                    (generation != 0 || playerId != 0 || ready != NetworkRoomReadyState.Unready)) ||
                (state != NetworkRoomSeatConnectionState.Empty &&
                    (generation == 0 || playerId != expectedSlot + 1)) ||
                (state != NetworkRoomSeatConnectionState.Connected && ready != NetworkRoomReadyState.Unready))
            {
                return NetworkWireCodecStatus.InvalidInput;
            }

            PlayerId decodedPlayer = playerId == 0 ? default : new PlayerId(playerId);
            seat = new NetworkRoomSeatSnapshot(slot, state, ready, generation, decodedPlayer);
            return NetworkWireCodecStatus.Success;
        }

        private static bool IsPhaseConsistent(
            NetworkRoomPhase phase,
            int seatCount,
            int connectedCount,
            int readyCount,
            uint countdownRemainingTicks)
        {
            return phase switch
            {
                NetworkRoomPhase.WaitingForPlayers => connectedCount < seatCount && countdownRemainingTicks == 0,
                NetworkRoomPhase.WaitingForReady => connectedCount == seatCount && readyCount < seatCount && countdownRemainingTicks == 0,
                NetworkRoomPhase.Countdown => connectedCount == seatCount && readyCount == seatCount && countdownRemainingTicks > 0,
                NetworkRoomPhase.Started => countdownRemainingTicks == 0,
                _ => false,
            };
        }
    }
}
