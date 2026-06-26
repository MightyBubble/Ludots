using System;
using System.IO;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map.Fields
{
    public enum LogicTerrainTopology : byte
    {
        Grid = 0,
        Hex = 1
    }

    [Flags]
    public enum LogicTerrainSurfaceFlags : byte
    {
        None = 0,
        Water = 1 << 0,
        Ramp = 1 << 1,
        Blocked = 1 << 2
    }

    public readonly struct LogicTerrainCell : IEquatable<LogicTerrainCell>
    {
        public static readonly LogicTerrainCell Default = new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None);

        public LogicTerrainCell(
            byte heightLevel,
            byte waterHeightLevel,
            LogicTerrainSurfaceFlags surfaceFlags,
            byte areaId = 0)
        {
            HeightLevel = ClampHeight(heightLevel);
            WaterHeightLevel = ClampHeight(waterHeightLevel);
            SurfaceFlags = surfaceFlags;
            AreaId = areaId;
        }

        public byte HeightLevel { get; }

        public byte WaterHeightLevel { get; }

        public LogicTerrainSurfaceFlags SurfaceFlags { get; }

        public byte AreaId { get; }

        public bool IsRamp => (SurfaceFlags & LogicTerrainSurfaceFlags.Ramp) != 0;

        public bool IsBlocked => (SurfaceFlags & LogicTerrainSurfaceFlags.Blocked) != 0;

        public bool HasWater => (SurfaceFlags & LogicTerrainSurfaceFlags.Water) != 0 || WaterHeightLevel > 0;

        public bool Equals(LogicTerrainCell other)
            => HeightLevel == other.HeightLevel &&
               WaterHeightLevel == other.WaterHeightLevel &&
               SurfaceFlags == other.SurfaceFlags &&
               AreaId == other.AreaId;

        public override bool Equals(object? obj)
            => obj is LogicTerrainCell other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(HeightLevel, WaterHeightLevel, SurfaceFlags, AreaId);

        private static byte ClampHeight(byte value)
            => value > SpatialScaleDefaults.LogicTerrainMaxHeightLevel
                ? (byte)SpatialScaleDefaults.LogicTerrainMaxHeightLevel
                : value;
    }

    public abstract class LogicTerrainField
    {
        protected LogicTerrainField(int widthCells, int heightCells, int chunkSizeCells)
        {
            if (widthCells <= 0) throw new ArgumentOutOfRangeException(nameof(widthCells));
            if (heightCells <= 0) throw new ArgumentOutOfRangeException(nameof(heightCells));
            if (chunkSizeCells <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSizeCells));

            WidthCells = widthCells;
            HeightCells = heightCells;
            ChunkSizeCells = chunkSizeCells;
        }

        public abstract LogicTerrainTopology Topology { get; }

        public int WidthCells { get; }

        public int HeightCells { get; }

        public int ChunkSizeCells { get; }

        public int WidthChunks => (WidthCells + ChunkSizeCells - 1) / ChunkSizeCells;

        public int HeightChunks => (HeightCells + ChunkSizeCells - 1) / ChunkSizeCells;

        public abstract LogicTerrainCell GetCell(int col, int row);

        public virtual bool TryGetCliffStraightenEdge(int col, int row, int edgeIndex, out bool value)
        {
            value = false;
            return false;
        }

        public abstract void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters);

        public abstract int HorizontalStepCm { get; }

        public abstract int VerticalStepCm { get; }

        public bool IsInBounds(int col, int row)
            => (uint)col < (uint)WidthCells && (uint)row < (uint)HeightCells;

        public int TileWidthCells(int chunkX)
            => Math.Max(0, Math.Min(ChunkSizeCells, WidthCells - chunkX * ChunkSizeCells));

        public int TileHeightCells(int chunkY)
            => Math.Max(0, Math.Min(ChunkSizeCells, HeightCells - chunkY * ChunkSizeCells));
    }

    public sealed class VertexMapLogicTerrainField : LogicTerrainField
    {
        public VertexMapLogicTerrainField(VertexMap vertexMap)
            : base(
                (vertexMap ?? throw new ArgumentNullException(nameof(vertexMap))).WidthInChunks * VertexChunk.ChunkSize,
                vertexMap.HeightInChunks * VertexChunk.ChunkSize,
                VertexChunk.ChunkSize)
        {
            VertexMap = vertexMap;
        }

        public VertexMap VertexMap { get; }

        public override LogicTerrainTopology Topology => LogicTerrainTopology.Hex;

        public override int HorizontalStepCm => HexCoordinates.EdgeLengthCm;

        public override int VerticalStepCm => HexCoordinates.EdgeLengthCm;

        public override LogicTerrainCell GetCell(int col, int row)
        {
            if (!IsInBounds(col, row))
            {
                return default;
            }

            var chunk = VertexMap.GetChunk(col, row, false);
            if (chunk == null)
            {
                return default;
            }

            int lx = col & VertexChunk.ChunkSizeMask;
            int ly = row & VertexChunk.ChunkSizeMask;
            byte height = chunk.GetHeight(lx, ly);
            byte water = chunk.GetWaterHeight(lx, ly);
            var flags = LogicTerrainSurfaceFlags.None;
            if (chunk.GetRamp(lx, ly)) flags |= LogicTerrainSurfaceFlags.Ramp;
            if (chunk.GetFlag(lx, ly)) flags |= LogicTerrainSurfaceFlags.Blocked;
            if (water > height) flags |= LogicTerrainSurfaceFlags.Water;
            return new LogicTerrainCell(height, water, flags, chunk.GetExtraByte(lx, ly, 0));
        }

        public override bool TryGetCliffStraightenEdge(int col, int row, int edgeIndex, out bool value)
        {
            value = false;
            if (!IsInBounds(col, row))
            {
                return false;
            }

            var chunk = VertexMap.GetChunk(col, row, false);
            if (chunk == null)
            {
                return false;
            }

            value = chunk.GetCliffStraightenEdge(col & VertexChunk.ChunkSizeMask, row & VertexChunk.ChunkSizeMask, edgeIndex);
            return true;
        }

        public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
        {
            xMeters = HexCoordinates.HexWidth * (col + 0.5f * (row & 1));
            zMeters = HexCoordinates.RowSpacing * row;
        }
    }

    public sealed class FlatGridLogicTerrainField : LogicTerrainField
    {
        private readonly LogicTerrainCell _cell;

        public FlatGridLogicTerrainField(
            int widthCells,
            int heightCells,
            int cellSizeCm = SpatialScaleDefaults.CellCm,
            int chunkSizeCells = SpatialScaleDefaults.TerrainChunkCells,
            LogicTerrainCell cell = default)
            : base(widthCells, heightCells, chunkSizeCells)
        {
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            CellSizeCm = cellSizeCm;
            _cell = cell;
        }

        public override LogicTerrainTopology Topology => LogicTerrainTopology.Grid;

        public int CellSizeCm { get; }

        public override int HorizontalStepCm => CellSizeCm;

        public override int VerticalStepCm => CellSizeCm;

        public override LogicTerrainCell GetCell(int col, int row)
            => IsInBounds(col, row) ? _cell : default;

        public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
        {
            xMeters = col * SpatialScaleDefaults.CentimetersToMeters(CellSizeCm);
            zMeters = row * SpatialScaleDefaults.CentimetersToMeters(CellSizeCm);
        }
    }

    public sealed class MutableGridLogicTerrainField : LogicTerrainField
    {
        private BoardFieldStore<LogicTerrainCell> _store;

        public MutableGridLogicTerrainField(
            int widthCells,
            int heightCells,
            int cellSizeCm = SpatialScaleDefaults.CellCm,
            int chunkSizeCells = SpatialScaleDefaults.TerrainChunkCells)
            : base(widthCells, heightCells, chunkSizeCells)
        {
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            CellSizeCm = cellSizeCm;
            _store = CreateStore(LogicTerrainCell.Default);
        }

        public override LogicTerrainTopology Topology => LogicTerrainTopology.Grid;

        public int CellSizeCm { get; }

        public int ResidentChunkCount => _store.ResidentChunkCount;

        internal BoardFieldStore<LogicTerrainCell> Store => _store;

        public override int HorizontalStepCm => CellSizeCm;

        public override int VerticalStepCm => CellSizeCm;

        public void SetCell(int col, int row, LogicTerrainCell cell)
        {
            if (!IsInBounds(col, row)) throw new ArgumentOutOfRangeException();
            _store.SetCell(col, row, cell);
        }

        public void Fill(LogicTerrainCell cell)
        {
            _store = CreateStore(cell);
        }

        public override LogicTerrainCell GetCell(int col, int row)
            => IsInBounds(col, row) ? _store.GetCell(col, row) : default;

        public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
        {
            xMeters = col * SpatialScaleDefaults.CentimetersToMeters(CellSizeCm);
            zMeters = row * SpatialScaleDefaults.CentimetersToMeters(CellSizeCm);
        }

        private BoardFieldStore<LogicTerrainCell> CreateStore(LogicTerrainCell defaultCell)
            => new BoardFieldStore<LogicTerrainCell>(
                WidthCells,
                HeightCells,
                CellSizeCm,
                defaultCell,
                LogicTerrainChunkCodec.Instance,
                ChunkSizeCells);
    }

    public sealed class SparseGridLogicTerrainField : LogicTerrainField
    {
        private readonly BoardFieldStore<LogicTerrainCell> _store;

        public SparseGridLogicTerrainField(
            int widthCells,
            int heightCells,
            int cellSizeCm = SpatialScaleDefaults.CellCm,
            int chunkSizeCells = SpatialScaleDefaults.TerrainChunkCells,
            LogicTerrainCell defaultCell = default)
            : base(widthCells, heightCells, chunkSizeCells)
        {
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            CellSizeCm = cellSizeCm;
            _store = new BoardFieldStore<LogicTerrainCell>(
                widthCells,
                heightCells,
                cellSizeCm,
                defaultCell,
                LogicTerrainChunkCodec.Instance,
                chunkSizeCells);
        }

        public override LogicTerrainTopology Topology => LogicTerrainTopology.Grid;

        public int CellSizeCm { get; }

        public int ResidentChunkCount => _store.ResidentChunkCount;

        internal BoardFieldStore<LogicTerrainCell> Store => _store;

        public override int HorizontalStepCm => CellSizeCm;

        public override int VerticalStepCm => CellSizeCm;

        public bool IsChunkResident(int chunkX, int chunkY)
            => _store.IsChunkResident(chunkX, chunkY);

        public bool IsChunkDirty(int chunkX, int chunkY)
            => _store.IsChunkDirty(chunkX, chunkY);

        public void ClearChunkDirty(int chunkX, int chunkY)
            => _store.ClearChunkDirty(chunkX, chunkY);

        public void ClearDirty()
            => _store.ClearDirty();

        public void SetCell(int col, int row, LogicTerrainCell cell)
        {
            if (!IsInBounds(col, row)) throw new ArgumentOutOfRangeException();
            _store.SetCell(col, row, cell);
        }

        public override LogicTerrainCell GetCell(int col, int row)
        {
            if (!IsInBounds(col, row)) return default;
            return _store.GetCell(col, row);
        }

        public override void GetWorldPositionMeters(int col, int row, out float xMeters, out float zMeters)
        {
            xMeters = col * SpatialScaleDefaults.CentimetersToMeters(CellSizeCm);
            zMeters = row * SpatialScaleDefaults.CentimetersToMeters(CellSizeCm);
        }
    }

    internal sealed class LogicTerrainChunkCodec : IBoardFieldChunkCodec<LogicTerrainCell>
    {
        public static readonly LogicTerrainChunkCodec Instance = new LogicTerrainChunkCodec();

        private LogicTerrainChunkCodec()
        {
        }

        public BoardFieldChunk<LogicTerrainCell> CreateChunk(int cellCount, LogicTerrainCell defaultValue)
        {
            var chunk = new LogicTerrainChunk(cellCount);
            chunk.Fill(defaultValue);
            return chunk;
        }
    }

    internal sealed class LogicTerrainChunk : BoardFieldChunk<LogicTerrainCell>
    {
        private const int SurfaceFlagPlaneCount = 3;
        private const int WaterFlagPlane = 0;
        private const int RampFlagPlane = 1;
        private const int BlockedFlagPlane = 2;

        private readonly byte[] _heightWaterLevels;
        private readonly byte[] _areaIds;
        private readonly ulong[] _surfaceFlagPlanes;
        private readonly int _wordsPerPlane;

        public LogicTerrainChunk(int cellCount)
            : base(cellCount)
        {
            _heightWaterLevels = new byte[cellCount];
            _areaIds = new byte[cellCount];
            _wordsPerPlane = (cellCount + 63) >> 6;
            _surfaceFlagPlanes = new ulong[checked(_wordsPerPlane * SurfaceFlagPlaneCount)];
        }

        public override LogicTerrainCell GetCell(int index)
        {
            byte packed = _heightWaterLevels[index];
            var flags = LogicTerrainSurfaceFlags.None;
            if (GetFlag(index, WaterFlagPlane)) flags |= LogicTerrainSurfaceFlags.Water;
            if (GetFlag(index, RampFlagPlane)) flags |= LogicTerrainSurfaceFlags.Ramp;
            if (GetFlag(index, BlockedFlagPlane)) flags |= LogicTerrainSurfaceFlags.Blocked;

            return new LogicTerrainCell(
                (byte)(packed & 0x0F),
                (byte)((packed >> 4) & 0x0F),
                flags,
                _areaIds[index]);
        }

        public override void SetCell(int index, LogicTerrainCell value)
        {
            _heightWaterLevels[index] = PackHeightWater(value.HeightLevel, value.WaterHeightLevel);
            _areaIds[index] = value.AreaId;
            SetFlag(index, WaterFlagPlane, (value.SurfaceFlags & LogicTerrainSurfaceFlags.Water) != 0);
            SetFlag(index, RampFlagPlane, (value.SurfaceFlags & LogicTerrainSurfaceFlags.Ramp) != 0);
            SetFlag(index, BlockedFlagPlane, (value.SurfaceFlags & LogicTerrainSurfaceFlags.Blocked) != 0);
        }

        public override void Fill(LogicTerrainCell value)
        {
            Array.Fill(_heightWaterLevels, PackHeightWater(value.HeightLevel, value.WaterHeightLevel));
            Array.Fill(_areaIds, value.AreaId);
            FillFlagPlane(WaterFlagPlane, (value.SurfaceFlags & LogicTerrainSurfaceFlags.Water) != 0);
            FillFlagPlane(RampFlagPlane, (value.SurfaceFlags & LogicTerrainSurfaceFlags.Ramp) != 0);
            FillFlagPlane(BlockedFlagPlane, (value.SurfaceFlags & LogicTerrainSurfaceFlags.Blocked) != 0);
        }

        internal int PortablePayloadByteLength
            => checked(_heightWaterLevels.Length + _areaIds.Length + (_surfaceFlagPlanes.Length * sizeof(ulong)));

        internal void WritePortablePayload(BinaryWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            writer.Write(_heightWaterLevels);
            writer.Write(_areaIds);
            for (int i = 0; i < _surfaceFlagPlanes.Length; i++)
            {
                writer.Write(_surfaceFlagPlanes[i]);
            }
        }

        internal void ReadPortablePayload(BinaryReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            ReadExactly(reader, _heightWaterLevels, nameof(_heightWaterLevels));
            ReadExactly(reader, _areaIds, nameof(_areaIds));
            for (int i = 0; i < _surfaceFlagPlanes.Length; i++)
            {
                _surfaceFlagPlanes[i] = reader.ReadUInt64();
            }
        }

        private static byte PackHeightWater(byte heightLevel, byte waterHeightLevel)
            => (byte)((heightLevel & 0x0F) | ((waterHeightLevel & 0x0F) << 4));

        private bool GetFlag(int index, int plane)
        {
            int wordIndex = plane * _wordsPerPlane + (index >> 6);
            int bitIndex = index & 0x3F;
            return (_surfaceFlagPlanes[wordIndex] & (1UL << bitIndex)) != 0;
        }

        private void SetFlag(int index, int plane, bool value)
        {
            int wordIndex = plane * _wordsPerPlane + (index >> 6);
            int bitIndex = index & 0x3F;
            ulong mask = 1UL << bitIndex;
            if (value) _surfaceFlagPlanes[wordIndex] |= mask;
            else _surfaceFlagPlanes[wordIndex] &= ~mask;
        }

        private void FillFlagPlane(int plane, bool value)
        {
            int offset = plane * _wordsPerPlane;
            Array.Fill(_surfaceFlagPlanes, value ? ulong.MaxValue : 0UL, offset, _wordsPerPlane);
        }

        private static void ReadExactly(BinaryReader reader, byte[] destination, string layerName)
        {
            int offset = 0;
            while (offset < destination.Length)
            {
                int read = reader.Read(destination, offset, destination.Length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException($"LogicTerrain chunk payload truncated while reading {layerName}.");
                }

                offset += read;
            }
        }
    }

    public readonly struct LogicTerrainProjectionOptions
    {
        public LogicTerrainProjectionOptions(int heightStepCm, int layerIndex = -1)
        {
            if (heightStepCm <= 0) throw new ArgumentOutOfRangeException(nameof(heightStepCm));
            HeightStepCm = heightStepCm;
            LayerIndex = layerIndex;
        }

        public int HeightStepCm { get; }

        public int LayerIndex { get; }

        public static LogicTerrainProjectionOptions Default { get; } =
            new LogicTerrainProjectionOptions(SpatialScaleDefaults.CellCm);
    }

    public interface ILogicTerrainAreaSource
    {
        bool TryGetAreaId(int col, int row, int worldXCm, int worldYCm, out byte areaId);
    }

    public static class VisualHeightmapLogicTerrainProjection
    {
        public static MutableGridLogicTerrainField ProjectToGrid(
            IVisualHeightmap visualHeightmap,
            int widthCells,
            int heightCells,
            int cellSizeCm,
            LogicTerrainProjectionOptions options,
            ILogicTerrainAreaSource? areaSource = null)
        {
            if (visualHeightmap == null) throw new ArgumentNullException(nameof(visualHeightmap));

            var field = new MutableGridLogicTerrainField(widthCells, heightCells, cellSizeCm);
            for (int row = 0; row < heightCells; row++)
            {
                for (int col = 0; col < widthCells; col++)
                {
                    int xCm = col * cellSizeCm;
                    int yCm = row * cellSizeCm;
                    if (!visualHeightmap.TrySampleHeightCm(xCm, yCm, out float heightCm, options.LayerIndex))
                    {
                        throw new InvalidOperationException(
                            $"Visual heightmap projection failed at grid cell ({col},{row}).");
                    }

                    int level = (int)MathF.Round(heightCm / options.HeightStepCm);
                    level = Math.Clamp(level, 0, SpatialScaleDefaults.LogicTerrainMaxHeightLevel);
                    byte areaId = 0;
                    if (areaSource != null && areaSource.TryGetAreaId(col, row, xCm, yCm, out byte projectedAreaId))
                    {
                        areaId = projectedAreaId;
                    }

                    field.SetCell(col, row, new LogicTerrainCell((byte)level, 0, LogicTerrainSurfaceFlags.None, areaId));
                }
            }

            return field;
        }
    }
}
