using System;
using System.IO;
using System.Linq;

namespace Ludots.Core.Navigation.NavMesh.LogicHeightmap
{
    public sealed class LogicHeightmapSemanticSummary
    {
        private readonly byte[] _dominantAreaIds;
        private readonly bool[] _chunkHasWaterLike;
        private readonly bool[] _chunkHasBlocked;
        private readonly bool[] _chunkHasRamp;
        private readonly int[] _chunkHeightRangeCm;
        private readonly bool[] _chunkSampled;

        private LogicHeightmapSemanticSummary(
            bool available,
            string source,
            int widthInChunks,
            int heightInChunks,
            int sampledChunks,
            int sampledCells,
            int distinctAreaCount,
            string areaHistogram,
            int waterHeightCellCount,
            int waterAreaCellCount,
            int blockedCellCount,
            int rampCellCount,
            int minHeightCm,
            int maxHeightCm,
            int maxChunkHeightRangeCm,
            bool hasMountainRiverSignals,
            byte[] dominantAreaIds,
            bool[] chunkHasWaterLike,
            bool[] chunkHasBlocked,
            bool[] chunkHasRamp,
            int[] chunkHeightRangeCm,
            bool[] chunkSampled)
        {
            Available = available;
            Source = source ?? string.Empty;
            WidthInChunks = widthInChunks;
            HeightInChunks = heightInChunks;
            SampledChunks = sampledChunks;
            SampledCells = sampledCells;
            DistinctAreaCount = distinctAreaCount;
            AreaHistogram = areaHistogram ?? string.Empty;
            WaterHeightCellCount = waterHeightCellCount;
            WaterAreaCellCount = waterAreaCellCount;
            BlockedCellCount = blockedCellCount;
            RampCellCount = rampCellCount;
            MinHeightCm = minHeightCm;
            MaxHeightCm = maxHeightCm;
            MaxChunkHeightRangeCm = maxChunkHeightRangeCm;
            HasMountainRiverSignals = hasMountainRiverSignals;
            _dominantAreaIds = dominantAreaIds ?? Array.Empty<byte>();
            _chunkHasWaterLike = chunkHasWaterLike ?? Array.Empty<bool>();
            _chunkHasBlocked = chunkHasBlocked ?? Array.Empty<bool>();
            _chunkHasRamp = chunkHasRamp ?? Array.Empty<bool>();
            _chunkHeightRangeCm = chunkHeightRangeCm ?? Array.Empty<int>();
            _chunkSampled = chunkSampled ?? Array.Empty<bool>();
        }

        public bool Available { get; }

        public string Source { get; }

        public int WidthInChunks { get; }

        public int HeightInChunks { get; }

        public int SampledChunks { get; }

        public int SampledCells { get; }

        public int DistinctAreaCount { get; }

        public string AreaHistogram { get; }

        public int WaterHeightCellCount { get; }

        public int WaterAreaCellCount { get; }

        public int WaterLikeCellCount => WaterHeightCellCount + WaterAreaCellCount;

        public int BlockedCellCount { get; }

        public int RampCellCount { get; }

        public int MinHeightCm { get; }

        public int MaxHeightCm { get; }

        public int HeightRangeCm => Math.Max(0, MaxHeightCm - MinHeightCm);

        public int MaxChunkHeightRangeCm { get; }

        public bool HasMountainRiverSignals { get; }

        public string VisualizationSource => Available ? "logic_heightmap_sampled_view" : "unavailable";

        public static LogicHeightmapSemanticSummary Empty(string source)
        {
            return new LogicHeightmapSemanticSummary(
                available: false,
                source: source,
                widthInChunks: 0,
                heightInChunks: 0,
                sampledChunks: 0,
                sampledCells: 0,
                distinctAreaCount: 0,
                areaHistogram: string.Empty,
                waterHeightCellCount: 0,
                waterAreaCellCount: 0,
                blockedCellCount: 0,
                rampCellCount: 0,
                minHeightCm: 0,
                maxHeightCm: 0,
                maxChunkHeightRangeCm: 0,
                hasMountainRiverSignals: false,
                dominantAreaIds: Array.Empty<byte>(),
                chunkHasWaterLike: Array.Empty<bool>(),
                chunkHasBlocked: Array.Empty<bool>(),
                chunkHasRamp: Array.Empty<bool>(),
                chunkHeightRangeCm: Array.Empty<int>(),
                chunkSampled: Array.Empty<bool>());
        }

        public static LogicHeightmapSemanticSummary FromFile(string path, int maxSampledChunks = 4096)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Empty("missing_path");
            }

            using LogicHeightmapFileReader reader = LogicHeightmapFileReader.Open(path);
            return FromReader(reader, Path.GetFullPath(path), maxSampledChunks);
        }

        public bool ChunkSampled(int chunkX, int chunkY)
        {
            int index = GetChunkIndex(chunkX, chunkY);
            return index >= 0 && index < _chunkSampled.Length && _chunkSampled[index];
        }

        public byte GetDominantAreaId(int chunkX, int chunkY)
        {
            int index = GetChunkIndex(chunkX, chunkY);
            return index >= 0 && index < _dominantAreaIds.Length ? _dominantAreaIds[index] : (byte)0;
        }

        public bool ChunkHasWaterLike(int chunkX, int chunkY)
        {
            int index = GetChunkIndex(chunkX, chunkY);
            return index >= 0 && index < _chunkHasWaterLike.Length && _chunkHasWaterLike[index];
        }

        public bool ChunkHasBlocked(int chunkX, int chunkY)
        {
            int index = GetChunkIndex(chunkX, chunkY);
            return index >= 0 && index < _chunkHasBlocked.Length && _chunkHasBlocked[index];
        }

        public bool ChunkHasRamp(int chunkX, int chunkY)
        {
            int index = GetChunkIndex(chunkX, chunkY);
            return index >= 0 && index < _chunkHasRamp.Length && _chunkHasRamp[index];
        }

        public int GetChunkHeightRangeCm(int chunkX, int chunkY)
        {
            int index = GetChunkIndex(chunkX, chunkY);
            return index >= 0 && index < _chunkHeightRangeCm.Length ? _chunkHeightRangeCm[index] : 0;
        }

        private static LogicHeightmapSemanticSummary FromReader(LogicHeightmapFileReader reader, string source, int maxSampledChunks)
        {
            int chunkCount = checked(reader.WidthInChunks * reader.HeightInChunks);
            int chunkStride = 1;
            if (maxSampledChunks > 0 && chunkCount > maxSampledChunks)
            {
                chunkStride = (int)Math.Ceiling(Math.Sqrt(chunkCount / (double)maxSampledChunks));
            }

            var dominantAreaIds = new byte[chunkCount];
            var chunkHasWaterLike = new bool[chunkCount];
            var chunkHasBlocked = new bool[chunkCount];
            var chunkHasRamp = new bool[chunkCount];
            var chunkHeightRangeCm = new int[chunkCount];
            var chunkSampled = new bool[chunkCount];
            var areaHistogram = new long[256];

            int sampledChunks = 0;
            int sampledCells = 0;
            int waterHeightCellCount = 0;
            int waterAreaCellCount = 0;
            int blockedCellCount = 0;
            int rampCellCount = 0;
            int minHeight = int.MaxValue;
            int maxHeight = int.MinValue;
            int maxChunkRange = 0;

            for (int cy = 0; cy < reader.HeightInChunks; cy++)
            {
                for (int cx = 0; cx < reader.WidthInChunks; cx++)
                {
                    if (cx % chunkStride != 0 || cy % chunkStride != 0)
                    {
                        continue;
                    }

                    LogicHeightmap window = reader.ReadTileWindow(cx, cy, radiusChunks: 0);
                    int startX = cx * LogicHeightmapChunk.ChunkSize;
                    int startY = cy * LogicHeightmapChunk.ChunkSize;
                    int[] localAreas = new int[256];
                    int localMin = int.MaxValue;
                    int localMax = int.MinValue;
                    bool localWaterLike = false;
                    bool localBlocked = false;
                    bool localRamp = false;

                    for (int ly = 0; ly < LogicHeightmapChunk.ChunkSize; ly++)
                    {
                        for (int lx = 0; lx < LogicHeightmapChunk.ChunkSize; lx++)
                        {
                            int sampleX = startX + lx;
                            int sampleY = startY + ly;
                            int height = window.GetHeightCm(sampleX, sampleY);
                            int waterHeight = window.GetWaterHeightCm(sampleX, sampleY);
                            byte areaId = window.GetAreaId(sampleX, sampleY);
                            bool blocked = window.IsBlocked(sampleX, sampleY);
                            bool ramp = window.IsRamp(sampleX, sampleY);

                            sampledCells++;
                            if (height < minHeight) minHeight = height;
                            if (height > maxHeight) maxHeight = height;
                            if (height < localMin) localMin = height;
                            if (height > localMax) localMax = height;
                            areaHistogram[areaId]++;
                            localAreas[areaId]++;
                            if (waterHeight > 0)
                            {
                                waterHeightCellCount++;
                                localWaterLike = true;
                            }

                            if (areaId == 5)
                            {
                                waterAreaCellCount++;
                                localWaterLike = true;
                            }

                            if (blocked)
                            {
                                blockedCellCount++;
                                localBlocked = true;
                            }

                            if (ramp)
                            {
                                rampCellCount++;
                                localRamp = true;
                            }
                        }
                    }

                    int chunkIndex = cy * reader.WidthInChunks + cx;
                    dominantAreaIds[chunkIndex] = (byte)Array.IndexOf(localAreas, localAreas.Max());
                    chunkHasWaterLike[chunkIndex] = localWaterLike;
                    chunkHasBlocked[chunkIndex] = localBlocked;
                    chunkHasRamp[chunkIndex] = localRamp;
                    chunkSampled[chunkIndex] = true;
                    int localRange = Math.Max(0, localMax - localMin);
                    chunkHeightRangeCm[chunkIndex] = localRange;
                    if (localRange > maxChunkRange) maxChunkRange = localRange;
                    sampledChunks++;
                }
            }

            int distinctAreas = areaHistogram.Count(count => count > 0);
            string areaSummary = string.Join(",", areaHistogram
                .Select((count, areaId) => new { areaId, count })
                .Where(item => item.count > 0)
                .Select(item => $"{item.areaId}:{item.count}"));
            bool hasMountainRiverSignals =
                distinctAreas >= 3 &&
                (waterHeightCellCount > 0 || waterAreaCellCount > 0) &&
                maxChunkRange > 250;

            if (sampledCells == 0)
            {
                minHeight = 0;
                maxHeight = 0;
            }

            return new LogicHeightmapSemanticSummary(
                available: sampledChunks > 0,
                source: source,
                widthInChunks: reader.WidthInChunks,
                heightInChunks: reader.HeightInChunks,
                sampledChunks: sampledChunks,
                sampledCells: sampledCells,
                distinctAreaCount: distinctAreas,
                areaHistogram: areaSummary,
                waterHeightCellCount: waterHeightCellCount,
                waterAreaCellCount: waterAreaCellCount,
                blockedCellCount: blockedCellCount,
                rampCellCount: rampCellCount,
                minHeightCm: minHeight,
                maxHeightCm: maxHeight,
                maxChunkHeightRangeCm: maxChunkRange,
                hasMountainRiverSignals: hasMountainRiverSignals,
                dominantAreaIds: dominantAreaIds,
                chunkHasWaterLike: chunkHasWaterLike,
                chunkHasBlocked: chunkHasBlocked,
                chunkHasRamp: chunkHasRamp,
                chunkHeightRangeCm: chunkHeightRangeCm,
                chunkSampled: chunkSampled);
        }

        private int GetChunkIndex(int chunkX, int chunkY)
        {
            if (chunkX < 0 || chunkX >= WidthInChunks || chunkY < 0 || chunkY >= HeightInChunks)
            {
                return -1;
            }

            return chunkY * WidthInChunks + chunkX;
        }
    }
}
