using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ludots.Core.Navigation.NavMesh.LogicHeightmap
{
    public sealed class LogicHeightmapFileReader : IDisposable
    {
        private const string Magic = "LHTM";
        private const int Version = 1;
        private static readonly int FlagBytes = (LogicHeightmapChunk.TotalCells / 64) * sizeof(ulong);
        private static readonly int ChunkPayloadBytes =
            (LogicHeightmapChunk.TotalCells * sizeof(int)) +
            (LogicHeightmapChunk.TotalCells * sizeof(int)) +
            LogicHeightmapChunk.TotalCells +
            FlagBytes +
            FlagBytes;
        private const int ChunkHeaderBytes = sizeof(int) * 2;
        private static readonly int ChunkRecordBytes = ChunkHeaderBytes + ChunkPayloadBytes;

        private readonly FileStream _stream;
        private readonly BinaryReader _reader;
        private readonly Dictionary<long, long> _chunkPayloadOffsets = new();
        private readonly object _lock = new();
        private bool _denseRowMajor;
        private long _denseFirstPayloadOffset;

        private LogicHeightmapFileReader(FileStream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
            ReadHeaderAndIndex();
        }

        public int WidthInChunks { get; private set; }

        public int HeightInChunks { get; private set; }

        public LogicHeightmapGridKind GridKind { get; private set; }

        public int CellSizeXCm { get; private set; }

        public int CellSizeZCm { get; private set; }

        public int StoredChunkCount { get; private set; }

        public static LogicHeightmapFileReader Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("LogicHeightmap path is required.", nameof(path));
            return new LogicHeightmapFileReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        public LogicHeightmap ReadTileWindow(int centerChunkX, int centerChunkY, int radiusChunks = 1)
        {
            if (radiusChunks < 0) throw new ArgumentOutOfRangeException(nameof(radiusChunks));
            if (!IsValidChunk(centerChunkX, centerChunkY)) throw new ArgumentOutOfRangeException(nameof(centerChunkX));

            var map = new LogicHeightmap();
            map.Initialize(WidthInChunks, HeightInChunks, GridKind, CellSizeXCm, CellSizeZCm);

            int firstChunkX = Math.Max(0, centerChunkX - radiusChunks);
            int firstChunkY = Math.Max(0, centerChunkY - radiusChunks);
            int lastChunkX = Math.Min(WidthInChunks - 1, centerChunkX + radiusChunks);
            int lastChunkY = Math.Min(HeightInChunks - 1, centerChunkY + radiusChunks);

            lock (_lock)
            {
                for (int cy = firstChunkY; cy <= lastChunkY; cy++)
                {
                    for (int cx = firstChunkX; cx <= lastChunkX; cx++)
                    {
                        long key = GetChunkKey(cx, cy);
                        long payloadOffset;
                        if (_denseRowMajor)
                        {
                            payloadOffset = GetDensePayloadOffset(cx, cy);
                        }
                        else if (!_chunkPayloadOffsets.TryGetValue(key, out payloadOffset))
                        {
                            continue;
                        }

                        _stream.Position = payloadOffset;
                        map.SetChunk(cx, cy, ReadChunkPayload());
                    }
                }
            }

            return map;
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
        }

        private void ReadHeaderAndIndex()
        {
            string magic = Encoding.ASCII.GetString(ReadExact(4));
            if (!string.Equals(magic, Magic, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid logic heightmap binary magic.");
            }

            int version = _reader.ReadInt32();
            if (version != Version)
            {
                throw new InvalidDataException($"Unsupported logic heightmap binary version: {version}.");
            }

            WidthInChunks = _reader.ReadInt32();
            HeightInChunks = _reader.ReadInt32();
            int chunkSize = _reader.ReadInt32();
            GridKind = (LogicHeightmapGridKind)_reader.ReadInt32();
            CellSizeXCm = _reader.ReadInt32();
            CellSizeZCm = _reader.ReadInt32();
            StoredChunkCount = _reader.ReadInt32();

            if (WidthInChunks <= 0 || HeightInChunks <= 0)
            {
                throw new InvalidDataException("Logic heightmap dimensions must be positive.");
            }

            if (chunkSize != LogicHeightmapChunk.ChunkSize)
            {
                throw new InvalidDataException($"Unsupported logic heightmap chunk size: {chunkSize}.");
            }

            if (StoredChunkCount < 0)
            {
                throw new InvalidDataException("Logic heightmap stored chunk count cannot be negative.");
            }

            long firstRecordOffset = _stream.Position;
            if (TryUseDenseRowMajorIndex(firstRecordOffset))
            {
                return;
            }

            _stream.Position = firstRecordOffset;
            for (int i = 0; i < StoredChunkCount; i++)
            {
                int cx = _reader.ReadInt32();
                int cy = _reader.ReadInt32();
                if (!IsValidChunk(cx, cy))
                {
                    throw new InvalidDataException($"Logic heightmap chunk is out of bounds: {cx},{cy}.");
                }

                long key = GetChunkKey(cx, cy);
                if (_chunkPayloadOffsets.ContainsKey(key))
                {
                    throw new InvalidDataException($"Logic heightmap contains duplicate chunk: {cx},{cy}.");
                }

                _chunkPayloadOffsets[key] = _stream.Position;
                _stream.Position = checked(_stream.Position + ChunkPayloadBytes);
            }
        }

        private bool TryUseDenseRowMajorIndex(long firstRecordOffset)
        {
            if (StoredChunkCount != checked((long)WidthInChunks * HeightInChunks) || StoredChunkCount <= 0)
            {
                return false;
            }

            long expectedBytes = checked(firstRecordOffset + ((long)StoredChunkCount * ChunkRecordBytes));
            if (_stream.Length < expectedBytes)
            {
                return false;
            }

            _stream.Position = firstRecordOffset;
            int firstX = _reader.ReadInt32();
            int firstY = _reader.ReadInt32();
            if (firstX != 0 || firstY != 0)
            {
                return false;
            }

            if (StoredChunkCount > 1)
            {
                _stream.Position = checked(firstRecordOffset + ChunkRecordBytes);
                int secondX = _reader.ReadInt32();
                int secondY = _reader.ReadInt32();
                int expectedSecondX = WidthInChunks > 1 ? 1 : 0;
                int expectedSecondY = WidthInChunks > 1 ? 0 : 1;
                if (secondX != expectedSecondX || secondY != expectedSecondY)
                {
                    return false;
                }
            }

            _denseRowMajor = true;
            _denseFirstPayloadOffset = firstRecordOffset + ChunkHeaderBytes;
            _stream.Position = expectedBytes;
            return true;
        }

        private long GetDensePayloadOffset(int chunkX, int chunkY)
        {
            long recordIndex = checked(((long)chunkY * WidthInChunks) + chunkX);
            return checked(_denseFirstPayloadOffset + (recordIndex * ChunkRecordBytes));
        }

        private LogicHeightmapChunk ReadChunkPayload()
        {
            var heightCm = new int[LogicHeightmapChunk.TotalCells];
            var waterHeightCm = new int[LogicHeightmapChunk.TotalCells];
            var areaIds = new byte[LogicHeightmapChunk.TotalCells];
            var flagBytes = new byte[FlagBytes];
            var rampBytes = new byte[FlagBytes];

            ReadInt32Array(heightCm);
            ReadInt32Array(waterHeightCm);
            ReadExactInto(areaIds);
            ReadExactInto(flagBytes);
            ReadExactInto(rampBytes);

            var chunk = new LogicHeightmapChunk();
            chunk.LoadRaw(heightCm, waterHeightCm, areaIds, flagBytes, rampBytes);
            return chunk;
        }

        private bool IsValidChunk(int chunkX, int chunkY)
        {
            return chunkX >= 0 && chunkX < WidthInChunks && chunkY >= 0 && chunkY < HeightInChunks;
        }

        private void ReadInt32Array(int[] dst)
        {
            byte[] raw = ReadExact(checked(dst.Length * sizeof(int)));
            Buffer.BlockCopy(raw, 0, dst, 0, raw.Length);
        }

        private void ReadExactInto(byte[] dst)
        {
            int offset = 0;
            while (offset < dst.Length)
            {
                int read = _reader.Read(dst, offset, dst.Length - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        private byte[] ReadExact(int byteCount)
        {
            byte[] bytes = _reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount) throw new EndOfStreamException();
            return bytes;
        }

        private static long GetChunkKey(int chunkX, int chunkY)
        {
            return ((long)chunkX << 32) | (uint)chunkY;
        }
    }
}
