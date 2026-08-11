using System;
using System.IO;

namespace Ludots.Core.Navigation.NavMesh
{
    public static class NavTileBinary
    {
        private const uint Magic = 0x4C49544E;
        public const ushort FormatVersion = 3;
        private const int HeaderBytesBeforeCounts =
            4 + 2 + 2 + 4 + 4 + 4 + 4 + 8 + 8 + 4 + 4;
        private const int ChecksumFieldOffset = 4 + 2 + 2 + 4 + 4 + 4 + 4 + 8;

        /// <summary>
        /// Exact serialized byte size of <paramref name="tile"/> using only its active counts.
        /// </summary>
        public static int GetSerializedSize(NavTile tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            return HeaderBytesBeforeCounts
                   + 4 + (tile.VertexCount * 12)
                   + 4 + (tile.TriangleCount * 12)
                   + 4 + (tile.TriangleCount * 12)
                   + 4 + tile.TriangleCount
                   + 4 + (tile.PortalCount * (1 + 2 + 2 + 2 + 2 + 4 + 4 + 4 + 4 + 4 + 4 + 4));
        }

        public static void Write(Stream stream, NavTile tile)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (tile == null) throw new ArgumentNullException(nameof(tile));

            int size = GetSerializedSize(tile);
            byte[] data = new byte[size];
            Write(data.AsSpan(), tile);
            stream.Write(data, 0, data.Length);
        }

        /// <summary>
        /// Allocation-free serialization into <paramref name="destination"/>. A short destination
        /// fails before any output is written; the checksum field is computed and patched in place.
        /// Returns the number of bytes written.
        /// </summary>
        public static int Write(Span<byte> destination, NavTile tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            int size = GetSerializedSize(tile);
            if (destination.Length < size)
            {
                throw new ArgumentException(
                    $"NavTileBinary destination span length {destination.Length} is below required {size}.",
                    nameof(destination));
            }

            int o = 0;
            WriteUInt32LE(destination, ref o, Magic);
            WriteUInt16LE(destination, ref o, FormatVersion);
            WriteUInt16LE(destination, ref o, 0);
            WriteInt32LE(destination, ref o, tile.TileId.ChunkX);
            WriteInt32LE(destination, ref o, tile.TileId.ChunkY);
            WriteInt32LE(destination, ref o, tile.TileId.Layer);
            WriteUInt32LE(destination, ref o, tile.TileVersion);
            WriteUInt64LE(destination, ref o, tile.BuildConfigHash);
            int checksumOffset = o;
            WriteUInt64LE(destination, ref o, 0UL);
            WriteInt32LE(destination, ref o, tile.OriginXcm);
            WriteInt32LE(destination, ref o, tile.OriginZcm);

            WriteInt32LE(destination, ref o, tile.VertexCount);
            ReadOnlySpan<int> activeVx = tile.ActiveVertexXcm;
            ReadOnlySpan<int> activeVy = tile.ActiveVertexYcm;
            ReadOnlySpan<int> activeVz = tile.ActiveVertexZcm;
            for (int i = 0; i < activeVx.Length; i++)
            {
                WriteInt32LE(destination, ref o, activeVx[i]);
                WriteInt32LE(destination, ref o, activeVy[i]);
                WriteInt32LE(destination, ref o, activeVz[i]);
            }

            WriteInt32LE(destination, ref o, tile.TriangleCount);
            ReadOnlySpan<int> activeTriA = tile.ActiveTriA;
            ReadOnlySpan<int> activeTriB = tile.ActiveTriB;
            ReadOnlySpan<int> activeTriC = tile.ActiveTriC;
            for (int i = 0; i < activeTriA.Length; i++)
            {
                WriteInt32LE(destination, ref o, activeTriA[i]);
                WriteInt32LE(destination, ref o, activeTriB[i]);
                WriteInt32LE(destination, ref o, activeTriC[i]);
            }

            WriteInt32LE(destination, ref o, tile.TriangleCount);
            ReadOnlySpan<int> activeN0 = tile.ActiveN0;
            ReadOnlySpan<int> activeN1 = tile.ActiveN1;
            ReadOnlySpan<int> activeN2 = tile.ActiveN2;
            for (int i = 0; i < activeN0.Length; i++)
            {
                WriteInt32LE(destination, ref o, activeN0[i]);
                WriteInt32LE(destination, ref o, activeN1[i]);
                WriteInt32LE(destination, ref o, activeN2[i]);
            }

            WriteInt32LE(destination, ref o, tile.TriangleCount);
            ReadOnlySpan<byte> activeAreas = tile.ActiveTriAreaIds;
            if (activeAreas.Length != tile.TriangleCount)
            {
                throw new InvalidDataException("NavTile triArea length mismatch.");
            }

            for (int i = 0; i < activeAreas.Length; i++)
            {
                destination[o++] = activeAreas[i];
            }

            WriteInt32LE(destination, ref o, tile.PortalCount);
            ReadOnlySpan<NavBorderPortal> activePortals = tile.ActivePortals;
            for (int i = 0; i < activePortals.Length; i++)
            {
                NavBorderPortal p = activePortals[i];
                destination[o++] = (byte)p.Side;
                WriteInt16LE(destination, ref o, p.U0);
                WriteInt16LE(destination, ref o, p.V0);
                WriteInt16LE(destination, ref o, p.U1);
                WriteInt16LE(destination, ref o, p.V1);
                WriteInt32LE(destination, ref o, p.LeftXcm);
                WriteInt32LE(destination, ref o, p.LeftYcm);
                WriteInt32LE(destination, ref o, p.LeftZcm);
                WriteInt32LE(destination, ref o, p.RightXcm);
                WriteInt32LE(destination, ref o, p.RightYcm);
                WriteInt32LE(destination, ref o, p.RightZcm);
                WriteInt32LE(destination, ref o, p.ClearanceCm);
            }

            if (o != size)
            {
                throw new InvalidOperationException($"NavTileBinary write size mismatch: wrote {o}, expected {size}.");
            }

            if (checksumOffset != ChecksumFieldOffset)
            {
                throw new InvalidOperationException("NavTileBinary checksum offset contract mismatch.");
            }

            ulong checksum = Fnv1a64(destination.Slice(0, size), ChecksumFieldOffset, 8);
            WriteUInt64LEAt(destination, ChecksumFieldOffset, checksum);
            return size;
        }

        /// <summary>
        /// Computes the tile checksum into <paramref name="scratch"/> without mutating the tile.
        /// The scratch must be at least <see cref="GetSerializedSize"/> bytes; short scratch fails.
        /// </summary>
        public static ulong ComputeChecksum(NavTile tile, Span<byte> scratch)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            int size = GetSerializedSize(tile);
            if (scratch.Length < size)
            {
                throw new ArgumentException(
                    $"NavTileBinary checksum scratch length {scratch.Length} is below required {size}.",
                    nameof(scratch));
            }

            Write(scratch, tile);
            return ReadUInt64LE(scratch, ChecksumFieldOffset);
        }

        /// <summary>
        /// Computes and assigns the tile checksum. The scratch must be at least
        /// <see cref="GetSerializedSize"/> bytes; short scratch fails before the tile is mutated.
        /// </summary>
        public static void AssignChecksum(NavTile tile, Span<byte> scratch)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            tile.SetChecksum(0UL);
            ulong checksum = ComputeChecksum(tile, scratch);
            tile.SetChecksum(checksum);
        }

        public static NavTile Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var data = ms.ToArray();
            if (data.Length < 4 + 2) throw new InvalidDataException("NavTileBin too small.");

            using var br = new BinaryReader(new MemoryStream(data));
            uint magic = br.ReadUInt32();
            if (magic != Magic) throw new InvalidDataException("NavTileBin magic mismatch.");
            ushort ver = br.ReadUInt16();
            if (ver != FormatVersion) throw new InvalidDataException($"NavTileBin version mismatch: {ver}.");
            _ = br.ReadUInt16();
            int chunkX = br.ReadInt32();
            int chunkY = br.ReadInt32();
            int layer = br.ReadInt32();
            uint tileVersion = br.ReadUInt32();
            ulong buildHash = br.ReadUInt64();
            ulong checksum = br.ReadUInt64();
            int originXcm = br.ReadInt32();
            int originZcm = br.ReadInt32();

            ulong computed = Fnv1a64(data, checksumOffset: ChecksumFieldOffset, checksumLength: 8);
            if (computed != checksum) throw new InvalidDataException("NavTileBin checksum mismatch.");

            int vCount = br.ReadInt32();
            var vx = new int[vCount];
            var vy = new int[vCount];
            var vz = new int[vCount];
            for (int i = 0; i < vCount; i++)
            {
                vx[i] = br.ReadInt32();
                vy[i] = br.ReadInt32();
                vz[i] = br.ReadInt32();
            }

            int tCount = br.ReadInt32();
            var ta = new int[tCount];
            var tb = new int[tCount];
            var tc = new int[tCount];
            for (int i = 0; i < tCount; i++)
            {
                ta[i] = br.ReadInt32();
                tb[i] = br.ReadInt32();
                tc[i] = br.ReadInt32();
            }

            int nCount = br.ReadInt32();
            if (nCount != tCount) throw new InvalidDataException("NavTileBin neighbor count mismatch.");
            var n0 = new int[tCount];
            var n1 = new int[tCount];
            var n2 = new int[tCount];
            for (int i = 0; i < tCount; i++)
            {
                n0[i] = br.ReadInt32();
                n1[i] = br.ReadInt32();
                n2[i] = br.ReadInt32();
            }

            int aCount = br.ReadInt32();
            if (aCount != tCount) throw new InvalidDataException("NavTileBin triArea count mismatch.");
            var triAreas = br.ReadBytes(tCount);
            if (triAreas.Length != tCount) throw new EndOfStreamException("NavTileBin triArea truncated.");

            int pCount = br.ReadInt32();
            var portals = new NavBorderPortal[pCount];
            for (int i = 0; i < pCount; i++)
            {
                var side = (NavPortalSide)br.ReadByte();
                short u0 = br.ReadInt16();
                short v0 = br.ReadInt16();
                short u1 = br.ReadInt16();
                short v1 = br.ReadInt16();
                int lx = br.ReadInt32();
                int ly = br.ReadInt32();
                int lz = br.ReadInt32();
                int rx = br.ReadInt32();
                int ry = br.ReadInt32();
                int rz = br.ReadInt32();
                int cl = br.ReadInt32();
                portals[i] = new NavBorderPortal(side, u0, v0, u1, v1, lx, ly, lz, rx, ry, rz, cl);
            }

            return new NavTile(new NavTileId(chunkX, chunkY, layer), tileVersion, buildHash, checksum, originXcm, originZcm, vx, vy, vz, ta, tb, tc, n0, n1, n2, triAreas, portals);
        }

        private static ulong Fnv1a64(byte[] data, int checksumOffset, int checksumLength)
            => Fnv1a64(data.AsSpan(), checksumOffset, checksumLength);

        private static ulong Fnv1a64(ReadOnlySpan<byte> data, int checksumOffset, int checksumLength)
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < data.Length; i++)
            {
                if (i >= checksumOffset && i < checksumOffset + checksumLength) continue;
                h ^= data[i];
                h *= 1099511628211UL;
            }

            return h;
        }

        private static void WriteUInt32LE(Span<byte> destination, ref int offset, uint value)
        {
            destination[offset++] = (byte)(value & 0xFF);
            destination[offset++] = (byte)((value >> 8) & 0xFF);
            destination[offset++] = (byte)((value >> 16) & 0xFF);
            destination[offset++] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteUInt16LE(Span<byte> destination, ref int offset, ushort value)
        {
            destination[offset++] = (byte)(value & 0xFF);
            destination[offset++] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteInt16LE(Span<byte> destination, ref int offset, short value)
            => WriteUInt16LE(destination, ref offset, unchecked((ushort)value));

        private static void WriteInt32LE(Span<byte> destination, ref int offset, int value)
            => WriteUInt32LE(destination, ref offset, unchecked((uint)value));

        private static void WriteUInt64LE(Span<byte> destination, ref int offset, ulong value)
        {
            WriteUInt64LEAt(destination, offset, value);
            offset += 8;
        }

        private static void WriteUInt64LEAt(Span<byte> destination, int offset, ulong value)
        {
            destination[offset + 0] = (byte)(value & 0xFF);
            destination[offset + 1] = (byte)((value >> 8) & 0xFF);
            destination[offset + 2] = (byte)((value >> 16) & 0xFF);
            destination[offset + 3] = (byte)((value >> 24) & 0xFF);
            destination[offset + 4] = (byte)((value >> 32) & 0xFF);
            destination[offset + 5] = (byte)((value >> 40) & 0xFF);
            destination[offset + 6] = (byte)((value >> 48) & 0xFF);
            destination[offset + 7] = (byte)((value >> 56) & 0xFF);
        }

        private static ulong ReadUInt64LE(ReadOnlySpan<byte> data, int offset)
        {
            return data[offset]
                   | ((ulong)data[offset + 1] << 8)
                   | ((ulong)data[offset + 2] << 16)
                   | ((ulong)data[offset + 3] << 24)
                   | ((ulong)data[offset + 4] << 32)
                   | ((ulong)data[offset + 5] << 40)
                   | ((ulong)data[offset + 6] << 48)
                   | ((ulong)data[offset + 7] << 56);
        }
    }
}
