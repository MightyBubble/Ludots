using System;
using System.IO;

namespace Ludots.Core.Navigation.NavMesh
{
    public static class NavTileBinary
    {
        private const uint Magic = 0x4C49544E;
        public const ushort FormatVersion = 2;

        public static void Write(Stream stream, NavTile tile)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (tile == null) throw new ArgumentNullException(nameof(tile));

            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Magic);
                bw.Write(FormatVersion);
                bw.Write((ushort)0);
                bw.Write(tile.TileId.ChunkX);
                bw.Write(tile.TileId.ChunkY);
                bw.Write(tile.TileId.Layer);
                bw.Write(tile.TileVersion);
                bw.Write(tile.BuildConfigHash);
                bw.Write(0UL);
                bw.Write(tile.OriginXcm);
                bw.Write(tile.OriginZcm);

                bw.Write(tile.VertexCount);
                for (int i = 0; i < tile.VertexCount; i++)
                {
                    bw.Write(tile.VertexXcm[i]);
                    bw.Write(tile.VertexYcm[i]);
                    bw.Write(tile.VertexZcm[i]);
                }

                bw.Write(tile.TriangleCount);
                for (int i = 0; i < tile.TriangleCount; i++)
                {
                    bw.Write(tile.TriA[i]);
                    bw.Write(tile.TriB[i]);
                    bw.Write(tile.TriC[i]);
                }

                bw.Write(tile.TriangleCount);
                for (int i = 0; i < tile.TriangleCount; i++)
                {
                    bw.Write(tile.N0[i]);
                    bw.Write(tile.N1[i]);
                    bw.Write(tile.N2[i]);
                }

                bw.Write(tile.TriangleCount);
                if (tile.TriAreaIds.Length != tile.TriangleCount) throw new InvalidDataException("NavTile triArea length mismatch.");
                bw.Write(tile.TriAreaIds);

                bw.Write(tile.Portals.Length);
                for (int i = 0; i < tile.Portals.Length; i++)
                {
                    var p = tile.Portals[i];
                    bw.Write((byte)p.Side);
                    bw.Write(p.U0);
                    bw.Write(p.V0);
                    bw.Write(p.U1);
                    bw.Write(p.V1);
                    bw.Write(p.LeftXcm);
                    bw.Write(p.LeftZcm);
                    bw.Write(p.RightXcm);
                    bw.Write(p.RightZcm);
                    bw.Write(p.ClearanceCm);
                }
            }

            var data = ms.ToArray();
            ulong checksum = Fnv1a64(data, checksumOffset: 4 + 2 + 2 + 4 + 4 + 4 + 4 + 8, checksumLength: 8);
            WriteUInt64LE(data, checksumOffset: 4 + 2 + 2 + 4 + 4 + 4 + 4 + 8, value: checksum);
            stream.Write(data, 0, data.Length);
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

            ulong computed = Fnv1a64(data, checksumOffset: 4 + 2 + 2 + 4 + 4 + 4 + 4 + 8, checksumLength: 8);
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
                int lz = br.ReadInt32();
                int rx = br.ReadInt32();
                int rz = br.ReadInt32();
                int cl = br.ReadInt32();
                portals[i] = new NavBorderPortal(side, u0, v0, u1, v1, lx, lz, rx, rz, cl);
            }

            return new NavTile(new NavTileId(chunkX, chunkY, layer), tileVersion, buildHash, checksum, originXcm, originZcm, vx, vy, vz, ta, tb, tc, n0, n1, n2, triAreas, portals);
        }

        private static ulong Fnv1a64(byte[] data, int checksumOffset, int checksumLength)
        {
            ulong h = 1469598103934665603UL;
            int i = 0;
            for (; i < data.Length; i++)
            {
                if (i >= checksumOffset && i < checksumOffset + checksumLength) continue;
                h ^= data[i];
                h *= 1099511628211UL;
            }
            return h;
        }

        private static void WriteUInt64LE(byte[] data, int checksumOffset, ulong value)
        {
            data[checksumOffset + 0] = (byte)(value & 0xFF);
            data[checksumOffset + 1] = (byte)((value >> 8) & 0xFF);
            data[checksumOffset + 2] = (byte)((value >> 16) & 0xFF);
            data[checksumOffset + 3] = (byte)((value >> 24) & 0xFF);
            data[checksumOffset + 4] = (byte)((value >> 32) & 0xFF);
            data[checksumOffset + 5] = (byte)((value >> 40) & 0xFF);
            data[checksumOffset + 6] = (byte)((value >> 48) & 0xFF);
            data[checksumOffset + 7] = (byte)((value >> 56) & 0xFF);
        }
    }
}
