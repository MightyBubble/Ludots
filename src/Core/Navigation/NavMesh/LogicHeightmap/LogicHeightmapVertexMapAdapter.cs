using System;
using System.IO;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Navigation.NavMesh.LogicHeightmap
{
    public static class LogicHeightmapVertexMapAdapter
    {
        public static LogicHeightmap FromVertexMap(VertexMap map, in NavBuildConfig config)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            int widthSamples = checked(map.WidthInChunks * VertexChunk.ChunkSize);
            int heightSamples = checked(map.HeightInChunks * VertexChunk.ChunkSize);
            int heightUnitCm = GetHeightUnitCm(config);

            var logic = new LogicHeightmap();
            logic.Initialize(
                map.WidthInChunks,
                map.HeightInChunks,
                LogicHeightmapGridKind.HexVertex,
                cellSizeXCm: 100,
                cellSizeZCm: 100);

            for (int y = 0; y < heightSamples; y++)
            {
                for (int x = 0; x < widthSamples; x++)
                {
                    logic.SetHeightCm(x, y, map.GetHeight(x, y) * heightUnitCm);
                    logic.SetWaterHeightCm(x, y, map.GetWaterHeight(x, y) * heightUnitCm);
                    logic.SetAreaId(x, y, (byte)Math.Min(255, (int)map.GetBiome(x, y)));
                    logic.SetRamp(x, y, map.IsRamp(x, y));
                    logic.SetBlocked(x, y, map.IsBlocked(x, y));
                }
            }

            return logic;
        }

        public static void WriteVertexMap(
            Stream stream,
            Stream vertexMapStream,
            in NavBuildConfig config)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (vertexMapStream == null) throw new ArgumentNullException(nameof(vertexMapStream));

            using var reader = VertexMapBinary.ChunkReader.Open(vertexMapStream);
            int heightUnitCm = GetHeightUnitCm(config);
            var sourceChunk = new VertexChunk();

            LogicHeightmapBinary.WriteChunked(
                stream,
                reader.WidthInChunks,
                reader.HeightInChunks,
                LogicHeightmapGridKind.HexVertex,
                cellSizeXCm: 100,
                cellSizeZCm: 100,
                (expectedChunkX, expectedChunkY) =>
                {
                    if (!reader.TryReadNextChunk(out int chunkX, out int chunkY, sourceChunk))
                    {
                        throw new InvalidDataException($"VertexMap ended before chunk {expectedChunkX},{expectedChunkY}.");
                    }

                    if (chunkX != expectedChunkX || chunkY != expectedChunkY)
                    {
                        throw new InvalidDataException($"VertexMap chunk order mismatch. Expected {expectedChunkX},{expectedChunkY}, got {chunkX},{chunkY}.");
                    }

                    return BuildLogicChunk(sourceChunk, heightUnitCm);
                });
        }

        public static VertexMap ToVertexMap(LogicHeightmap logic, in NavBuildConfig config)
        {
            if (logic == null) throw new ArgumentNullException(nameof(logic));

            var map = new VertexMap();
            map.Initialize(logic.WidthInChunks, logic.HeightInChunks);

            int widthSamples = checked(logic.WidthInChunks * LogicHeightmapChunk.ChunkSize);
            int heightSamples = checked(logic.HeightInChunks * LogicHeightmapChunk.ChunkSize);
            int heightUnitCm = GetHeightUnitCm(config);

            for (int y = 0; y < heightSamples; y++)
            {
                for (int x = 0; x < widthSamples; x++)
                {
                    map.SetHeight(x, y, QuantizeHeightUnit(logic.GetHeightCm(x, y), heightUnitCm));
                    map.SetWaterHeight(x, y, QuantizeHeightUnit(logic.GetWaterHeightCm(x, y), heightUnitCm));
                    map.SetBiome(x, y, (byte)Math.Min(15, (int)logic.GetAreaId(x, y)));
                    map.SetRamp(x, y, logic.IsRamp(x, y));
                    map.SetBlocked(x, y, logic.IsBlocked(x, y));
                }
            }

            return map;
        }

        public static VertexMap ToVertexMapTileWindow(
            LogicHeightmap logic,
            int chunkX,
            int chunkY,
            in NavBuildConfig config)
        {
            if (logic == null) throw new ArgumentNullException(nameof(logic));
            if (!logic.IsValidChunk(chunkX, chunkY)) throw new ArgumentOutOfRangeException(nameof(chunkX));

            var map = new VertexMap();
            map.Initialize(logic.WidthInChunks, logic.HeightInChunks);

            int firstChunkX = Math.Max(0, chunkX - 1);
            int firstChunkY = Math.Max(0, chunkY - 1);
            int lastChunkX = Math.Min(logic.WidthInChunks - 1, chunkX + 1);
            int lastChunkY = Math.Min(logic.HeightInChunks - 1, chunkY + 1);
            int heightUnitCm = GetHeightUnitCm(config);

            for (int cy = firstChunkY; cy <= lastChunkY; cy++)
            {
                for (int cx = firstChunkX; cx <= lastChunkX; cx++)
                {
                    CopyChunk(logic, map, cx, cy, heightUnitCm);
                }
            }

            return map;
        }

        public static int GetHeightUnitCm(in NavBuildConfig config)
        {
            int heightUnitCm = (int)MathF.Round(config.HeightScaleMeters * 100f);
            return Math.Max(1, heightUnitCm);
        }

        private static void CopyChunk(LogicHeightmap logic, VertexMap map, int chunkX, int chunkY, int heightUnitCm)
        {
            int startX = chunkX * LogicHeightmapChunk.ChunkSize;
            int startY = chunkY * LogicHeightmapChunk.ChunkSize;
            int endX = startX + LogicHeightmapChunk.ChunkSize;
            int endY = startY + LogicHeightmapChunk.ChunkSize;

            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    map.SetHeight(x, y, QuantizeHeightUnit(logic.GetHeightCm(x, y), heightUnitCm));
                    map.SetWaterHeight(x, y, QuantizeHeightUnit(logic.GetWaterHeightCm(x, y), heightUnitCm));
                    map.SetBiome(x, y, (byte)Math.Min(15, (int)logic.GetAreaId(x, y)));
                    map.SetRamp(x, y, logic.IsRamp(x, y));
                    map.SetBlocked(x, y, logic.IsBlocked(x, y));
                }
            }
        }

        private static LogicHeightmapChunk BuildLogicChunk(VertexChunk source, int heightUnitCm)
        {
            var chunk = new LogicHeightmapChunk();
            for (int ly = 0; ly < VertexChunk.ChunkSize; ly++)
            {
                for (int lx = 0; lx < VertexChunk.ChunkSize; lx++)
                {
                    chunk.SetHeightCm(lx, ly, source.GetHeight(lx, ly) * heightUnitCm);
                    chunk.SetWaterHeightCm(lx, ly, source.GetWaterHeight(lx, ly) * heightUnitCm);
                    chunk.SetAreaId(lx, ly, (byte)Math.Min(255, (int)source.GetBiome(lx, ly)));
                    chunk.SetRamp(lx, ly, source.GetRamp(lx, ly));
                    chunk.SetBlocked(lx, ly, source.GetFlag(lx, ly));
                }
            }

            return chunk;
        }

        private static byte QuantizeHeightUnit(int heightCm, int heightUnitCm)
        {
            int value = (int)MathF.Round(heightCm / (float)heightUnitCm);
            return (byte)Math.Clamp(value, 0, 15);
        }
    }
}
