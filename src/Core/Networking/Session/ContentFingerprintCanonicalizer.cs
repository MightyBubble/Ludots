using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Ludots.Core.Networking.Session
{
    public readonly record struct ContentFingerprintContent(
        string LogicalPath,
        ReadOnlyMemory<byte> Bytes);

    public static class ContentFingerprintCanonicalizer
    {
        private const string Domain = "ludots-network-content-v2";
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

        public static ContentFingerprint FromContent(
            ProtocolVersion protocolVersion,
            IReadOnlyList<ContentFingerprintContent> content)
        {
            if (!protocolVersion.IsWellFormed)
            {
                throw new ArgumentException("Protocol version must be well-formed.", nameof(protocolVersion));
            }

            ArgumentNullException.ThrowIfNull(content);
            if (content.Count == 0)
            {
                throw new ArgumentException("Content fingerprint input must not be empty.", nameof(content));
            }

            var normalized = new NormalizedContent[content.Count];
            for (int i = 0; i < content.Count; i++)
            {
                ContentFingerprintContent entry = content[i];
                normalized[i] = new NormalizedContent(
                    NormalizeLogicalPath(entry.LogicalPath, i),
                    entry.Bytes);
            }

            Array.Sort(normalized, static (left, right) =>
                StringComparer.Ordinal.Compare(left.LogicalPath, right.LogicalPath));
            for (int i = 1; i < normalized.Length; i++)
            {
                if (string.Equals(
                        normalized[i - 1].LogicalPath,
                        normalized[i].LogicalPath,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Content fingerprint input contains duplicate logical path '{normalized[i].LogicalPath}'.",
                        nameof(content));
                }
            }

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendTextFrame(hasher, Domain);
            AppendUInt16(hasher, protocolVersion.Major);
            AppendUInt16(hasher, protocolVersion.Minor);
            AppendUInt32(hasher, checked((uint)normalized.Length));
            for (int i = 0; i < normalized.Length; i++)
            {
                ref readonly NormalizedContent entry = ref normalized[i];
                AppendTextFrame(hasher, entry.LogicalPath);
                AppendUInt64(hasher, checked((ulong)entry.Bytes.Length));
                hasher.AppendData(entry.Bytes.Span);
            }

            return ContentFingerprint.FromBytes(hasher.GetHashAndReset());
        }

        private static string NormalizeLogicalPath(string logicalPath, int inputIndex)
        {
            if (string.IsNullOrWhiteSpace(logicalPath))
            {
                throw new ArgumentException(
                    $"Content fingerprint logical path at index {inputIndex} is required.",
                    nameof(logicalPath));
            }

            string normalized = logicalPath.Replace('\\', '/').Normalize(NormalizationForm.FormC);
            if (normalized[0] == '/' || normalized[^1] == '/')
            {
                throw new ArgumentException(
                    $"Content fingerprint logical path must be relative: '{logicalPath}'.",
                    nameof(logicalPath));
            }

            int segmentStart = 0;
            for (int i = 0; i <= normalized.Length; i++)
            {
                if (i < normalized.Length && normalized[i] != '/')
                {
                    if (normalized[i] == ':' || char.IsControl(normalized[i]))
                    {
                        throw new ArgumentException(
                            $"Content fingerprint logical path contains a reserved character: '{logicalPath}'.",
                            nameof(logicalPath));
                    }

                    continue;
                }

                int segmentLength = i - segmentStart;
                if (segmentLength == 0 ||
                    (segmentLength == 1 && normalized[segmentStart] == '.') ||
                    (segmentLength == 2 &&
                        normalized[segmentStart] == '.' &&
                        normalized[segmentStart + 1] == '.'))
                {
                    throw new ArgumentException(
                        $"Content fingerprint logical path is not canonical: '{logicalPath}'.",
                        nameof(logicalPath));
                }

                segmentStart = i + 1;
            }

            return normalized;
        }

        private static void AppendTextFrame(IncrementalHash hasher, string value)
        {
            int byteCount = Utf8.GetByteCount(value);
            AppendUInt32(hasher, checked((uint)byteCount));
            Span<byte> buffer = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];
            int written = Utf8.GetBytes(value, buffer);
            hasher.AppendData(buffer[..written]);
        }

        private static void AppendUInt16(IncrementalHash hasher, ushort value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
            hasher.AppendData(buffer);
        }

        private static void AppendUInt32(IncrementalHash hasher, uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            hasher.AppendData(buffer);
        }

        private static void AppendUInt64(IncrementalHash hasher, ulong value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
            hasher.AppendData(buffer);
        }

        private readonly record struct NormalizedContent(
            string LogicalPath,
            ReadOnlyMemory<byte> Bytes);
    }
}
