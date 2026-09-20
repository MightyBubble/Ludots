using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.Persistence
{
    public sealed class SaveContainerCodec
    {
        private const uint Magic = 0x5354444C; // LDTS little-endian
        private const ushort FormatVersion = 1;
        private const int SectionHashLength = 32;
        private const int HeaderSize = 4 + 2 + 2 + 4 + 4 + 4 +
            SectionHashLength + SectionHashLength + SectionHashLength;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public byte[] Encode(WorldSaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Header == null) throw new ArgumentException("Save snapshot header is required.", nameof(snapshot));
            if (snapshot.Domains == null) throw new ArgumentException("Save snapshot domains are required.", nameof(snapshot));
            if (snapshot.WorldBytes == null) throw new ArgumentException("Save snapshot world bytes are required.", nameof(snapshot));

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot.Header, JsonOptions);
            byte[] domainBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot.Domains, JsonOptions);
            byte[] worldBytes = snapshot.WorldBytes;
            checked
            {
                int totalLength = HeaderSize + headerBytes.Length + domainBytes.Length + worldBytes.Length;
                byte[] bytes = new byte[totalLength];
                Span<byte> target = bytes;
                BinaryPrimitives.WriteUInt32LittleEndian(target[..4], Magic);
                BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(4, 2), FormatVersion);
                BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(6, 2), 0);
                BinaryPrimitives.WriteInt32LittleEndian(target.Slice(8, 4), headerBytes.Length);
                BinaryPrimitives.WriteInt32LittleEndian(target.Slice(12, 4), domainBytes.Length);
                BinaryPrimitives.WriteInt32LittleEndian(target.Slice(16, 4), worldBytes.Length);
                WriteHash(headerBytes, target.Slice(20, SectionHashLength));
                WriteHash(domainBytes, target.Slice(52, SectionHashLength));
                WriteHash(worldBytes, target.Slice(84, SectionHashLength));
                headerBytes.CopyTo(target[HeaderSize..]);
                domainBytes.CopyTo(target.Slice(HeaderSize + headerBytes.Length));
                worldBytes.CopyTo(target.Slice(HeaderSize + headerBytes.Length + domainBytes.Length));
                return bytes;
            }
        }

        public SaveContextHeader ReadHeader(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            ContainerSections sections = ReadSections(bytes);
            ValidateSectionHash(
                bytes.AsSpan(sections.HeaderOffset, sections.HeaderLength),
                sections.HeaderHash,
                "header.json");
            try
            {
                SaveContextHeader? header = JsonSerializer.Deserialize<SaveContextHeader>(
                    bytes.AsSpan(sections.HeaderOffset, sections.HeaderLength),
                    JsonOptions);
                return header ?? throw new SaveContextException("Save header.json is empty.");
            }
            catch (JsonException ex)
            {
                throw new SaveContextException($"Save header.json is invalid: {ex.Message}");
            }
        }

        public WorldSaveSnapshot Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            ContainerSections sections = ReadSections(bytes);
            SaveContextHeader header = ReadHeader(bytes);
            ValidateSectionHash(
                bytes.AsSpan(sections.DomainOffset, sections.DomainLength),
                sections.DomainHash,
                "domains.json");
            ValidateSectionHash(
                bytes.AsSpan(sections.WorldOffset, sections.WorldLength),
                sections.WorldHash,
                "world.bin");
            JsonObject domains = ReadDomains(bytes, sections);
            byte[] worldBytes = bytes.AsSpan(sections.WorldOffset, sections.WorldLength).ToArray();
            return new WorldSaveSnapshot(header, domains, worldBytes);
        }

        private static JsonObject ReadDomains(byte[] bytes, ContainerSections sections)
        {
            try
            {
                JsonNode? domains = JsonNode.Parse(bytes.AsSpan(sections.DomainOffset, sections.DomainLength));
                return domains as JsonObject ??
                    throw new SaveContextException("Save domains.json must be an object.");
            }
            catch (JsonException ex)
            {
                throw new SaveContextException($"Save domains.json is invalid: {ex.Message}");
            }
        }

        private static ContainerSections ReadSections(byte[] bytes)
        {
            if (bytes.Length < HeaderSize)
            {
                throw new SaveContextException("Save container is shorter than the frame header.");
            }

            ReadOnlySpan<byte> source = bytes;
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(source[..4]);
            if (magic != Magic)
            {
                throw new SaveContextException("Save container magic is invalid.");
            }

            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, 2));
            if (version != FormatVersion)
            {
                throw new SaveContextException(
                    $"Save container formatVersion mismatch: expected {FormatVersion}, actual {version}.");
            }

            ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6, 2));
            if (reserved != 0)
            {
                throw new SaveContextException("Save container reserved flags are not supported.");
            }

            int headerLength = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(8, 4));
            int domainLength = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(12, 4));
            int worldLength = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(16, 4));
            byte[] headerHash = source.Slice(20, SectionHashLength).ToArray();
            byte[] domainHash = source.Slice(52, SectionHashLength).ToArray();
            byte[] worldHash = source.Slice(84, SectionHashLength).ToArray();
            if (headerLength <= 0 || domainLength <= 0 || worldLength < 0)
            {
                throw new SaveContextException("Save container section lengths are invalid.");
            }

            int headerOffset = HeaderSize;
            int domainOffset;
            int worldOffset;
            int expectedLength;
            try
            {
                checked
                {
                    domainOffset = headerOffset + headerLength;
                    worldOffset = domainOffset + domainLength;
                    expectedLength = worldOffset + worldLength;
                }
            }
            catch (OverflowException)
            {
                throw new SaveContextException("Save container section lengths overflow.");
            }

            if (expectedLength != bytes.Length)
            {
                throw new SaveContextException("Save container length does not match framed section lengths.");
            }

            return new ContainerSections(
                headerOffset,
                headerLength,
                domainOffset,
                domainLength,
                worldOffset,
                worldLength,
                headerHash,
                domainHash,
                worldHash);
        }

        private static void WriteHash(ReadOnlySpan<byte> bytes, Span<byte> destination)
        {
            byte[] hash = SHA256.HashData(bytes);
            hash.CopyTo(destination);
        }

        private static void ValidateSectionHash(ReadOnlySpan<byte> bytes, byte[] expectedHash, string sectionName)
        {
            byte[] actualHash = SHA256.HashData(bytes);
            if (!actualHash.AsSpan().SequenceEqual(expectedHash))
            {
                throw new SaveContextException($"Save {sectionName} hash mismatch.");
            }
        }

        private readonly struct ContainerSections
        {
            public ContainerSections(
                int headerOffset,
                int headerLength,
                int domainOffset,
                int domainLength,
                int worldOffset,
                int worldLength,
                byte[] headerHash,
                byte[] domainHash,
                byte[] worldHash)
            {
                HeaderOffset = headerOffset;
                HeaderLength = headerLength;
                DomainOffset = domainOffset;
                DomainLength = domainLength;
                WorldOffset = worldOffset;
                WorldLength = worldLength;
                HeaderHash = headerHash;
                DomainHash = domainHash;
                WorldHash = worldHash;
            }

            public int HeaderOffset { get; }
            public int HeaderLength { get; }
            public int DomainOffset { get; }
            public int DomainLength { get; }
            public int WorldOffset { get; }
            public int WorldLength { get; }
            public byte[] HeaderHash { get; }
            public byte[] DomainHash { get; }
            public byte[] WorldHash { get; }
        }
    }
}
