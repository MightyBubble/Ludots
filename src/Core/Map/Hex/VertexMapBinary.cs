using System;
using System.IO;
using System.Text;

namespace Ludots.Core.Map.Hex
{
    public static class VertexMapBinary
    {
        private const string Magic = "VTXM";
        private const int Version = 2;

        public static VertexMap Read(Stream stream)
        {
            var map = new VertexMap();
            Read(stream, map);
            return map;
        }

        public static void Read(Stream stream, VertexMap map)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (map == null) throw new ArgumentNullException(nameof(map));

            using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var magicBytes = br.ReadBytes(4);
            if (magicBytes.Length != 4) throw new EndOfStreamException();
            string magic = Encoding.ASCII.GetString(magicBytes);
            if (!string.Equals(magic, Magic, StringComparison.Ordinal)) throw new InvalidDataException("Invalid VertexMap binary magic.");

            int version = br.ReadInt32();
            if (version != Version) throw new InvalidDataException($"Unsupported VertexMap binary version: {version}.");

            int widthChunks = br.ReadInt32();
            int heightChunks = br.ReadInt32();
            int chunkSize = br.ReadInt32();
            br.ReadInt32();

            if (widthChunks <= 0 || heightChunks <= 0) throw new InvalidDataException("Invalid chunk dimensions.");
            if (chunkSize != VertexChunk.ChunkSize) throw new InvalidDataException($"Unsupported chunk size: {chunkSize}.");

            map.Initialize(widthChunks, heightChunks);

            byte[] packed = new byte[VertexChunk.TotalCells];
            byte[] layer2 = new byte[VertexChunk.TotalCells];
            byte[] flags = new byte[(VertexChunk.TotalCells / 64) * sizeof(ulong)];
            byte[] ramps = new byte[(VertexChunk.TotalCells / 64) * sizeof(ulong)];
            byte[] factions = new byte[VertexChunk.TotalCells];
            byte[] extraFlags0 = new byte[(VertexChunk.TotalCells / 64) * sizeof(ulong)];
            byte[] extraFlags1 = new byte[(VertexChunk.TotalCells / 64) * sizeof(ulong)];
            byte[] extraFlags2 = new byte[(VertexChunk.TotalCells / 64) * sizeof(ulong)];
            byte[] extraBytes0 = new byte[VertexChunk.TotalCells];
            byte[] cliffStraighten = new byte[VertexChunk.DerivedCliffStraightenBytes];

            for (int cy = 0; cy < heightChunks; cy++)
            {
                for (int cx = 0; cx < widthChunks; cx++)
                {
                    ReadExact(br, packed);
                    ReadExact(br, layer2);
                    ReadExact(br, flags);
                    ReadExact(br, ramps);
                    ReadExact(br, factions);
                    ReadExact(br, extraFlags0);
                    ReadExact(br, extraFlags1);
                    ReadExact(br, extraFlags2);
                    ReadExact(br, extraBytes0);
                    ReadExact(br, cliffStraighten);

                    int q = cx << VertexChunk.ChunkSizeShift;
                    int r = cy << VertexChunk.ChunkSizeShift;
                    var chunk = map.GetChunk(q, r, createIfMissing: true);
                    if (chunk == null) throw new InvalidDataException("Chunk out of bounds.");
                    chunk.LoadRaw(packed, layer2, flags, ramps, factions, extraFlags0, extraFlags1, extraFlags2, extraBytes0, cliffStraighten);
                }
            }
        }

        private static void ReadExact(BinaryReader br, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = br.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        public static void Write(Stream stream, VertexMap map)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (map == null) throw new ArgumentNullException(nameof(map));

            using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            bw.Write(Encoding.ASCII.GetBytes(Magic));
            bw.Write(Version);
            bw.Write(map.WidthInChunks);
            bw.Write(map.HeightInChunks);
            bw.Write(VertexChunk.ChunkSize);
            bw.Write(0);

            byte[] packed = new byte[VertexChunk.TotalCells];
            byte[] layer2 = new byte[VertexChunk.TotalCells];
            byte[] flags = new byte[(VertexChunk.TotalCells / 64) * sizeof(ulong)];
            byte[] ramps = new byte[(VertexChunk.TotalCells / 64) * sizeof(ulong)];
            byte[] factions = new byte[VertexChunk.TotalCells];
            byte[] extraFlags0 = new byte[(VertexChunk.TotalCells / 64) * sizeof(ulong)];
            byte[] extraFlags1 = new byte[(VertexChunk.TotalCells / 64) * sizeof(ulong)];
            byte[] extraFlags2 = new byte[(VertexChunk.TotalCells / 64) * sizeof(ulong)];
            byte[] extraBytes0 = new byte[VertexChunk.TotalCells];
            byte[] cliffStraighten = new byte[VertexChunk.DerivedCliffStraightenBytes];

            for (int cy = 0; cy < map.HeightInChunks; cy++)
            {
                for (int cx = 0; cx < map.WidthInChunks; cx++)
                {
                    Array.Clear(packed, 0, packed.Length);
                    Array.Clear(layer2, 0, layer2.Length);
                    Array.Clear(flags, 0, flags.Length);
                    Array.Clear(ramps, 0, ramps.Length);
                    Array.Clear(factions, 0, factions.Length);
                    Array.Clear(extraFlags0, 0, extraFlags0.Length);
                    Array.Clear(extraFlags1, 0, extraFlags1.Length);
                    Array.Clear(extraFlags2, 0, extraFlags2.Length);
                    Array.Clear(extraBytes0, 0, extraBytes0.Length);
                    Array.Clear(cliffStraighten, 0, cliffStraighten.Length);

                    int q = cx << VertexChunk.ChunkSizeShift;
                    int r = cy << VertexChunk.ChunkSizeShift;
                    var chunk = map.GetChunk(q, r, createIfMissing: false);
                    if (chunk != null)
                    {
                        chunk.CopyRawTo(packed, layer2, flags, ramps, factions, extraFlags0, extraFlags1, extraFlags2, extraBytes0, cliffStraighten);
                    }

                    bw.Write(packed);
                    bw.Write(layer2);
                    bw.Write(flags);
                    bw.Write(ramps);
                    bw.Write(factions);
                    bw.Write(extraFlags0);
                    bw.Write(extraFlags1);
                    bw.Write(extraFlags2);
                    bw.Write(extraBytes0);
                    bw.Write(cliffStraighten);
                }
            }
        }
    }
}
