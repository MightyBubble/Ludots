using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Ludots.Core.Networking.Session
{
    /// <summary>
    /// Fixed 32-byte content identity (SHA-256 digest of caller-supplied canonical bytes).
    /// </summary>
    public readonly struct ContentFingerprint : IEquatable<ContentFingerprint>
    {
        public const int ByteLength = 32;
        public const int HexLength = ByteLength * 2;

        private readonly ulong _w0;
        private readonly ulong _w1;
        private readonly ulong _w2;
        private readonly ulong _w3;

        private ContentFingerprint(ulong w0, ulong w1, ulong w2, ulong w3)
        {
            _w0 = w0;
            _w1 = w1;
            _w2 = w2;
            _w3 = w3;
        }

        public static ContentFingerprint Empty => default;

        public bool IsEmpty => _w0 == 0 && _w1 == 0 && _w2 == 0 && _w3 == 0;

        public static ContentFingerprint FromBytes(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != ByteLength)
            {
                throw new ArgumentException($"Content fingerprint requires exactly {ByteLength} bytes.", nameof(bytes));
            }

            return new ContentFingerprint(
                ReadUInt64(bytes, 0),
                ReadUInt64(bytes, 8),
                ReadUInt64(bytes, 16),
                ReadUInt64(bytes, 24));
        }

        public void CopyTo(Span<byte> destination)
        {
            if (destination.Length < ByteLength)
            {
                throw new ArgumentException($"Destination must be at least {ByteLength} bytes.", nameof(destination));
            }

            WriteUInt64(destination, 0, _w0);
            WriteUInt64(destination, 8, _w1);
            WriteUInt64(destination, 16, _w2);
            WriteUInt64(destination, 24, _w3);
        }

        public bool TryFormatHex(Span<char> destination, out int charsWritten)
        {
            if (destination.Length < HexLength)
            {
                charsWritten = 0;
                return false;
            }

            Span<byte> bytes = stackalloc byte[ByteLength];
            CopyTo(bytes);
            for (int i = 0; i < ByteLength; i++)
            {
                byte value = bytes[i];
                destination[i * 2] = ToHexNibble(value >> 4);
                destination[(i * 2) + 1] = ToHexNibble(value & 0xF);
            }

            charsWritten = HexLength;
            return true;
        }

        public string ToHexString()
        {
            Span<char> chars = stackalloc char[HexLength];
            TryFormatHex(chars, out _);
            return new string(chars);
        }

        public static bool TryParseHex(ReadOnlySpan<char> hex, out ContentFingerprint fingerprint)
        {
            if (hex.Length != HexLength)
            {
                fingerprint = default;
                return false;
            }

            Span<byte> bytes = stackalloc byte[ByteLength];
            for (int i = 0; i < ByteLength; i++)
            {
                int hi = FromHexNibble(hex[i * 2]);
                int lo = FromHexNibble(hex[(i * 2) + 1]);
                if (hi < 0 || lo < 0)
                {
                    fingerprint = default;
                    return false;
                }

                bytes[i] = (byte)((hi << 4) | lo);
            }

            fingerprint = FromBytes(bytes);
            return true;
        }

        public bool Equals(ContentFingerprint other) =>
            _w0 == other._w0 &&
            _w1 == other._w1 &&
            _w2 == other._w2 &&
            _w3 == other._w3;

        public override bool Equals(object? obj) => obj is ContentFingerprint other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_w0, _w1, _w2, _w3);

        public static bool operator ==(ContentFingerprint left, ContentFingerprint right) => left.Equals(right);

        public static bool operator !=(ContentFingerprint left, ContentFingerprint right) => !left.Equals(right);

        public override string ToString() => ToHexString();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset) =>
            bytes[offset]
            | ((ulong)bytes[offset + 1] << 8)
            | ((ulong)bytes[offset + 2] << 16)
            | ((ulong)bytes[offset + 3] << 24)
            | ((ulong)bytes[offset + 4] << 32)
            | ((ulong)bytes[offset + 5] << 40)
            | ((ulong)bytes[offset + 6] << 48)
            | ((ulong)bytes[offset + 7] << 56);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteUInt64(Span<byte> destination, int offset, ulong value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
            destination[offset + 4] = (byte)(value >> 32);
            destination[offset + 5] = (byte)(value >> 40);
            destination[offset + 6] = (byte)(value >> 48);
            destination[offset + 7] = (byte)(value >> 56);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char ToHexNibble(int value) => (char)(value < 10 ? ('0' + value) : ('a' + (value - 10)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FromHexNibble(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            if (c >= 'A' && c <= 'F')
            {
                return c - 'A' + 10;
            }

            return -1;
        }
    }

    public static class ContentFingerprintBuilder
    {
        /// <summary>
        /// Builds a fingerprint from caller-owned canonical bytes via SHA-256. Does not define canonicalization.
        /// </summary>
        public static ContentFingerprint FromCanonicalBytes(ReadOnlySpan<byte> canonicalBytes)
        {
            Span<byte> digest = stackalloc byte[ContentFingerprint.ByteLength];
            int written = SHA256.HashData(canonicalBytes, digest);
            if (written != ContentFingerprint.ByteLength)
            {
                throw new InvalidOperationException($"SHA-256 produced {written} bytes; expected {ContentFingerprint.ByteLength}.");
            }

            return ContentFingerprint.FromBytes(digest);
        }
    }
}
