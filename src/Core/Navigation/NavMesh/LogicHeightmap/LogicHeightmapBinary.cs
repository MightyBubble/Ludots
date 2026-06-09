using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ludots.Core.Navigation.NavMesh.LogicHeightmap
{
    public static class LogicHeightmapBinary
    {
        private const string Magic = "LHTM";
        private const int Version = 1;

        public static LogicHeightmap Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            string magic = Encoding.ASCII.GetString(ReadExact(br, 4));
            if (!string.Equals(magic, Magic, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid logic heightmap binary magic.");
            }

            int version = br.ReadInt32();
            if (version != Version)
            {
                throw new InvalidDataException($"Unsupported logic heightmap binary version: {version}.");
            }

            int widthChunks = br.ReadInt32();
            int heightChunks = br.ReadInt32();
            int chunkSize = br.ReadInt32();
            var gridKind = (LogicHeightmapGridKind)br.ReadInt32();
            int cellSizeXCm = br.ReadInt32();
            int cellSizeZCm = br.ReadInt32();
            int storedChunks = br.ReadInt32();

            if (chunkSize != LogicHeightmapChunk.ChunkSize)
            {
                throw new InvalidDataException($"Unsupported logic heightmap chunk size: {chunkSize}.");
            }

            if (storedChunks < 0)
            {
                throw new InvalidDataException("Logic heightmap stored chunk count cannot be negative.");
            }

            var map = new LogicHeightmap();
            map.Initialize(widthChunks, heightChunks, gridKind, cellSizeXCm, cellSizeZCm);

            var heightCm = new int[LogicHeightmapChunk.TotalCells];
            var waterHeightCm = new int[LogicHeightmapChunk.TotalCells];
            var areaIds = new byte[LogicHeightmapChunk.TotalCells];
            var flagBytes = new byte[(LogicHeightmapChunk.TotalCells / 64) * sizeof(ulong)];
            var rampBytes = new byte[flagBytes.Length];

            for (int i = 0; i < storedChunks; i++)
            {
                int cx = br.ReadInt32();
                int cy = br.ReadInt32();
                ReadInt32Array(br, heightCm);
                ReadInt32Array(br, waterHeightCm);
                ReadExactInto(br, areaIds);
                ReadExactInto(br, flagBytes);
                ReadExactInto(br, rampBytes);

                var chunk = new LogicHeightmapChunk();
                chunk.LoadRaw(heightCm, waterHeightCm, areaIds, flagBytes, rampBytes);
                map.SetChunk(cx, cy, chunk);
            }

            return map;
        }

        public static void Write(Stream stream, LogicHeightmap map)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (map == null) throw new ArgumentNullException(nameof(map));

            using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            bw.Write(Encoding.ASCII.GetBytes(Magic));
            bw.Write(Version);
            bw.Write(map.WidthInChunks);
            bw.Write(map.HeightInChunks);
            bw.Write(LogicHeightmapChunk.ChunkSize);
            bw.Write((int)map.GridKind);
            bw.Write(map.CellSizeXCm);
            bw.Write(map.CellSizeZCm);
            bw.Write(map.ChunkCount);

            var heightCm = new int[LogicHeightmapChunk.TotalCells];
            var waterHeightCm = new int[LogicHeightmapChunk.TotalCells];
            var areaIds = new byte[LogicHeightmapChunk.TotalCells];
            var flagBytes = new byte[(LogicHeightmapChunk.TotalCells / 64) * sizeof(ulong)];
            var rampBytes = new byte[flagBytes.Length];

            foreach (var item in map.Chunks)
            {
                Array.Clear(heightCm);
                Array.Clear(waterHeightCm);
                Array.Clear(areaIds);
                Array.Clear(flagBytes);
                Array.Clear(rampBytes);

                item.Chunk.CopyRawTo(heightCm, waterHeightCm, areaIds, flagBytes, rampBytes);
                bw.Write(item.ChunkX);
                bw.Write(item.ChunkY);
                WriteInt32Array(bw, heightCm);
                WriteInt32Array(bw, waterHeightCm);
                bw.Write(areaIds);
                bw.Write(flagBytes);
                bw.Write(rampBytes);
            }
        }

        public static void WriteChunked(
            Stream stream,
            int widthInChunks,
            int heightInChunks,
            LogicHeightmapGridKind gridKind,
            int cellSizeXCm,
            int cellSizeZCm,
            Func<int, int, LogicHeightmapChunk> loadChunk)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (widthInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(widthInChunks));
            if (heightInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(heightInChunks));
            if (cellSizeXCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeXCm));
            if (cellSizeZCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeZCm));
            if (loadChunk == null) throw new ArgumentNullException(nameof(loadChunk));

            using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            bw.Write(Encoding.ASCII.GetBytes(Magic));
            bw.Write(Version);
            bw.Write(widthInChunks);
            bw.Write(heightInChunks);
            bw.Write(LogicHeightmapChunk.ChunkSize);
            bw.Write((int)gridKind);
            bw.Write(cellSizeXCm);
            bw.Write(cellSizeZCm);
            bw.Write(checked(widthInChunks * heightInChunks));

            var heightCm = new int[LogicHeightmapChunk.TotalCells];
            var waterHeightCm = new int[LogicHeightmapChunk.TotalCells];
            var areaIds = new byte[LogicHeightmapChunk.TotalCells];
            var flagBytes = new byte[(LogicHeightmapChunk.TotalCells / 64) * sizeof(ulong)];
            var rampBytes = new byte[flagBytes.Length];

            for (int cy = 0; cy < heightInChunks; cy++)
            {
                for (int cx = 0; cx < widthInChunks; cx++)
                {
                    Array.Clear(heightCm);
                    Array.Clear(waterHeightCm);
                    Array.Clear(areaIds);
                    Array.Clear(flagBytes);
                    Array.Clear(rampBytes);

                    LogicHeightmapChunk chunk = loadChunk(cx, cy) ?? throw new InvalidDataException($"Logic heightmap chunk provider returned null for {cx},{cy}.");
                    chunk.CopyRawTo(heightCm, waterHeightCm, areaIds, flagBytes, rampBytes);
                    bw.Write(cx);
                    bw.Write(cy);
                    WriteInt32Array(bw, heightCm);
                    WriteInt32Array(bw, waterHeightCm);
                    bw.Write(areaIds);
                    bw.Write(flagBytes);
                    bw.Write(rampBytes);
                }
            }
        }

        public static void WriteChunkedSubset(
            Stream stream,
            int widthInChunks,
            int heightInChunks,
            LogicHeightmapGridKind gridKind,
            int cellSizeXCm,
            int cellSizeZCm,
            IReadOnlyCollection<(int ChunkX, int ChunkY)> storedChunks,
            Func<int, int, LogicHeightmapChunk> loadChunk)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (widthInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(widthInChunks));
            if (heightInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(heightInChunks));
            if (cellSizeXCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeXCm));
            if (cellSizeZCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeZCm));
            if (storedChunks == null) throw new ArgumentNullException(nameof(storedChunks));
            if (loadChunk == null) throw new ArgumentNullException(nameof(loadChunk));

            var orderedChunks = new List<(int ChunkX, int ChunkY)>(storedChunks.Count);
            var seen = new HashSet<long>();
            foreach (var item in storedChunks)
            {
                if ((uint)item.ChunkX >= (uint)widthInChunks || (uint)item.ChunkY >= (uint)heightInChunks)
                {
                    throw new ArgumentOutOfRangeException(nameof(storedChunks), $"Stored chunk is out of bounds: {item.ChunkX},{item.ChunkY}.");
                }

                long key = ((long)item.ChunkX << 32) | (uint)item.ChunkY;
                if (!seen.Add(key))
                {
                    throw new InvalidDataException($"Logic heightmap stored chunk list contains duplicate chunk: {item.ChunkX},{item.ChunkY}.");
                }

                orderedChunks.Add(item);
            }

            orderedChunks.Sort(static (a, b) =>
            {
                int y = a.ChunkY.CompareTo(b.ChunkY);
                return y != 0 ? y : a.ChunkX.CompareTo(b.ChunkX);
            });

            using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            bw.Write(Encoding.ASCII.GetBytes(Magic));
            bw.Write(Version);
            bw.Write(widthInChunks);
            bw.Write(heightInChunks);
            bw.Write(LogicHeightmapChunk.ChunkSize);
            bw.Write((int)gridKind);
            bw.Write(cellSizeXCm);
            bw.Write(cellSizeZCm);
            bw.Write(orderedChunks.Count);

            var heightCm = new int[LogicHeightmapChunk.TotalCells];
            var waterHeightCm = new int[LogicHeightmapChunk.TotalCells];
            var areaIds = new byte[LogicHeightmapChunk.TotalCells];
            var flagBytes = new byte[(LogicHeightmapChunk.TotalCells / 64) * sizeof(ulong)];
            var rampBytes = new byte[flagBytes.Length];

            foreach (var item in orderedChunks)
            {
                Array.Clear(heightCm);
                Array.Clear(waterHeightCm);
                Array.Clear(areaIds);
                Array.Clear(flagBytes);
                Array.Clear(rampBytes);

                LogicHeightmapChunk chunk = loadChunk(item.ChunkX, item.ChunkY) ?? throw new InvalidDataException($"Logic heightmap chunk provider returned null for {item.ChunkX},{item.ChunkY}.");
                chunk.CopyRawTo(heightCm, waterHeightCm, areaIds, flagBytes, rampBytes);
                bw.Write(item.ChunkX);
                bw.Write(item.ChunkY);
                WriteInt32Array(bw, heightCm);
                WriteInt32Array(bw, waterHeightCm);
                bw.Write(areaIds);
                bw.Write(flagBytes);
                bw.Write(rampBytes);
            }
        }

        private static void ReadInt32Array(BinaryReader br, int[] dst)
        {
            byte[] raw = ReadExact(br, checked(dst.Length * sizeof(int)));
            Buffer.BlockCopy(raw, 0, dst, 0, raw.Length);
        }

        private static void WriteInt32Array(BinaryWriter bw, int[] src)
        {
            byte[] raw = new byte[checked(src.Length * sizeof(int))];
            Buffer.BlockCopy(src, 0, raw, 0, raw.Length);
            bw.Write(raw);
        }

        private static void ReadExactInto(BinaryReader br, byte[] dst)
        {
            int offset = 0;
            while (offset < dst.Length)
            {
                int read = br.Read(dst, offset, dst.Length - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        private static byte[] ReadExact(BinaryReader br, int byteCount)
        {
            byte[] bytes = br.ReadBytes(byteCount);
            if (bytes.Length != byteCount) throw new EndOfStreamException();
            return bytes;
        }
    }
}
