using System;
using System.Buffers.Binary;
using Ludots.Core.Networking.Replication;

namespace Ludots.Core.Networking.Protocol
{
    internal static class NetworkWireBinary
    {
        public static bool TryWriteUInt16(Span<byte> destination, ref int offset, ushort value)
        {
            if ((uint)(destination.Length - offset) < 2u)
            {
                return false;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2), value);
            offset += 2;
            return true;
        }

        public static bool TryWriteInt32(Span<byte> destination, ref int offset, int value)
        {
            if ((uint)(destination.Length - offset) < 4u)
            {
                return false;
            }

            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), value);
            offset += 4;
            return true;
        }

        public static bool TryWriteUInt32(Span<byte> destination, ref int offset, uint value)
        {
            if ((uint)(destination.Length - offset) < 4u)
            {
                return false;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), value);
            offset += 4;
            return true;
        }

        public static bool TryWriteUInt64(Span<byte> destination, ref int offset, ulong value)
        {
            if ((uint)(destination.Length - offset) < 8u)
            {
                return false;
            }

            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(offset, 8), value);
            offset += 8;
            return true;
        }

        public static bool TryWriteByte(Span<byte> destination, ref int offset, byte value)
        {
            if ((uint)offset >= (uint)destination.Length)
            {
                return false;
            }

            destination[offset++] = value;
            return true;
        }

        public static bool TryWriteBytes(Span<byte> destination, ref int offset, ReadOnlySpan<byte> source)
        {
            if ((uint)(destination.Length - offset) < (uint)source.Length)
            {
                return false;
            }

            source.CopyTo(destination.Slice(offset, source.Length));
            offset += source.Length;
            return true;
        }

        public static bool TryReadUInt16(ReadOnlySpan<byte> source, ref int offset, out ushort value)
        {
            if ((uint)(source.Length - offset) < 2u)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));
            offset += 2;
            return true;
        }

        public static bool TryReadInt32(ReadOnlySpan<byte> source, ref int offset, out int value)
        {
            if ((uint)(source.Length - offset) < 4u)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;
            return true;
        }

        public static bool TryReadUInt32(ReadOnlySpan<byte> source, ref int offset, out uint value)
        {
            if ((uint)(source.Length - offset) < 4u)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;
            return true;
        }

        public static bool TryReadUInt64(ReadOnlySpan<byte> source, ref int offset, out ulong value)
        {
            if ((uint)(source.Length - offset) < 8u)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8));
            offset += 8;
            return true;
        }

        public static bool TryReadByte(ReadOnlySpan<byte> source, ref int offset, out byte value)
        {
            if ((uint)offset >= (uint)source.Length)
            {
                value = 0;
                return false;
            }

            value = source[offset++];
            return true;
        }

        public static bool TryReadBytes(ReadOnlySpan<byte> source, ref int offset, Span<byte> destination)
        {
            if ((uint)(source.Length - offset) < (uint)destination.Length)
            {
                return false;
            }

            source.Slice(offset, destination.Length).CopyTo(destination);
            offset += destination.Length;
            return true;
        }

        public static bool TryWriteHandle(Span<byte> destination, ref int offset, NetworkEntityHandle handle)
        {
            return TryWriteInt32(destination, ref offset, handle.Slot)
                && TryWriteUInt32(destination, ref offset, handle.Generation);
        }

        public static NetworkWireCodecStatus TryReadHandle(
            ReadOnlySpan<byte> source,
            ref int offset,
            out NetworkEntityHandle handle)
        {
            if (!TryReadInt32(source, ref offset, out int slot) ||
                !TryReadUInt32(source, ref offset, out uint generation))
            {
                handle = default;
                return NetworkWireCodecStatus.MalformedLength;
            }

            if (!NetworkEntityHandle.TryCreate(slot, generation, out handle))
            {
                return NetworkWireCodecStatus.InvalidHandle;
            }

            return NetworkWireCodecStatus.Success;
        }

        public static NetworkWireCodecStatus EnsureExactEnd(ReadOnlySpan<byte> source, int offset)
        {
            if (offset < source.Length)
            {
                return NetworkWireCodecStatus.TrailingBytes;
            }

            if (offset > source.Length)
            {
                return NetworkWireCodecStatus.MalformedLength;
            }

            return NetworkWireCodecStatus.Success;
        }
    }
}
