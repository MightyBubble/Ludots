using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Rendering
{
    public enum GlobalFieldVisualKind : byte
    {
        Fog = 1,
        Weather = 2,
        Water = 3,
        Flow = 4,
        Heat = 5,
        Influence = 6,
    }

    public enum GlobalFieldVisualValueKind : byte
    {
        Byte = 0,
        Float = 1,
        Vector2 = 2,
        Vector3 = 3,
        Vector4 = 4,
    }

    public readonly struct GlobalFieldVisualId : IEquatable<GlobalFieldVisualId>
    {
        public GlobalFieldVisualId(GlobalFieldVisualKind kind, int scopeKeyId, int layerKeyId, int surfaceKeyId)
        {
            if (kind == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (scopeKeyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeKeyId));
            }

            if (layerKeyId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(layerKeyId));
            }

            if (surfaceKeyId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(surfaceKeyId));
            }

            Kind = kind;
            ScopeKeyId = scopeKeyId;
            LayerKeyId = layerKeyId;
            SurfaceKeyId = surfaceKeyId;
        }

        public readonly GlobalFieldVisualKind Kind;
        public readonly int ScopeKeyId;
        public readonly int LayerKeyId;
        public readonly int SurfaceKeyId;

        public bool Equals(GlobalFieldVisualId other) =>
            Kind == other.Kind &&
            ScopeKeyId == other.ScopeKeyId &&
            LayerKeyId == other.LayerKeyId &&
            SurfaceKeyId == other.SurfaceKeyId;

        public override bool Equals(object? obj) => obj is GlobalFieldVisualId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Kind, ScopeKeyId, LayerKeyId, SurfaceKeyId);
        public static bool operator ==(GlobalFieldVisualId left, GlobalFieldVisualId right) => left.Equals(right);
        public static bool operator !=(GlobalFieldVisualId left, GlobalFieldVisualId right) => !left.Equals(right);
        public override string ToString() => $"{Kind}:{ScopeKeyId}:{LayerKeyId}:{SurfaceKeyId}";
    }

    public readonly struct GlobalFieldVisualDescriptor
    {
        public GlobalFieldVisualDescriptor(
            GlobalFieldVisualId id,
            int cellSizeCm,
            WorldCmInt2 originWorldCm,
            IntRect boundsCells,
            GlobalFieldVisualValueKind valueKind,
            int paletteId = 0,
            int materialId = 0)
        {
            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            }

            if (boundsCells.Width < 0 || boundsCells.Height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(boundsCells));
            }

            Id = id;
            CellSizeCm = cellSizeCm;
            OriginWorldCm = originWorldCm;
            BoundsCells = boundsCells;
            ValueKind = valueKind;
            PaletteId = paletteId;
            MaterialId = materialId;
        }

        public readonly GlobalFieldVisualId Id;
        public readonly int CellSizeCm;
        public readonly WorldCmInt2 OriginWorldCm;
        public readonly IntRect BoundsCells;
        public readonly GlobalFieldVisualValueKind ValueKind;
        public readonly int PaletteId;
        public readonly int MaterialId;
    }

    public readonly struct GlobalFieldVisualCell
    {
        public GlobalFieldVisualCell(FieldCell2D cell, byte byteValue)
            : this(cell, byteValue, Vector4.Zero)
        {
        }

        public GlobalFieldVisualCell(FieldCell2D cell, Vector4 floatValue)
            : this(cell, 0, floatValue)
        {
        }

        private GlobalFieldVisualCell(FieldCell2D cell, byte byteValue, Vector4 floatValue)
        {
            Cell = cell;
            ByteValue = byteValue;
            FloatValue = floatValue;
        }

        public readonly FieldCell2D Cell;
        public readonly byte ByteValue;
        public readonly Vector4 FloatValue;
    }

    public struct GlobalFieldVisualRecord
    {
        public GlobalFieldVisualDescriptor Descriptor;
        public int Revision;
        public bool IsActive;
        public int CellStart;
        public int CellCount;
        public int DirtyRectStart;
        public int DirtyRectCount;
    }

    public sealed class GlobalFieldVisualBuffer
    {
        private readonly GlobalFieldVisualRecord[] _records;
        private readonly GlobalFieldVisualCell[] _cells;
        private readonly IntRect[] _dirtyRects;
        private readonly Dictionary<GlobalFieldVisualId, int> _recordIndexById;
        private int _recordCount;
        private int _activeRecordCount;
        private int _cellCount;
        private int _dirtyRectCount;
        private bool _frameOpen;

        public GlobalFieldVisualBuffer(int recordCapacity = 64, int cellCapacity = 65536, int dirtyRectCapacity = 1024)
        {
            if (recordCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(recordCapacity));
            }

            if (cellCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellCapacity));
            }

            if (dirtyRectCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dirtyRectCapacity));
            }

            _records = new GlobalFieldVisualRecord[recordCapacity];
            _cells = new GlobalFieldVisualCell[cellCapacity];
            _dirtyRects = new IntRect[dirtyRectCapacity];
            _recordIndexById = new Dictionary<GlobalFieldVisualId, int>(recordCapacity);
        }

        public int RecordCount => _recordCount;
        public int ActiveRecordCount => _activeRecordCount;
        public int CellCount => _cellCount;
        public int DirtyRectCount => _dirtyRectCount;
        public int RecordCapacity => _records.Length;
        public int CellCapacity => _cells.Length;
        public int DirtyRectCapacity => _dirtyRects.Length;
        public int ProjectionRevision { get; private set; }

        public void BeginFrame()
        {
            ProjectionRevision = ProjectionRevision == int.MaxValue ? 1 : ProjectionRevision + 1;
            _activeRecordCount = 0;
            _cellCount = 0;
            _dirtyRectCount = 0;
            _frameOpen = true;

            for (int i = 0; i < _recordCount; i++)
            {
                _records[i].IsActive = false;
                _records[i].CellStart = 0;
                _records[i].CellCount = 0;
                _records[i].DirtyRectStart = 0;
                _records[i].DirtyRectCount = 0;
            }
        }

        public int Upsert(
            in GlobalFieldVisualDescriptor descriptor,
            ReadOnlySpan<GlobalFieldVisualCell> cells,
            ReadOnlySpan<IntRect> dirtyRects)
        {
            if (!_frameOpen)
            {
                throw new InvalidOperationException("GlobalFieldVisualBuffer.BeginFrame must be called before writing field visuals.");
            }

            if (_cellCount + cells.Length > _cells.Length)
            {
                throw new InvalidOperationException(
                    $"GlobalFieldVisualBuffer cell capacity exceeded: required={_cellCount + cells.Length}, capacity={_cells.Length}.");
            }

            if (_dirtyRectCount + dirtyRects.Length > _dirtyRects.Length)
            {
                throw new InvalidOperationException(
                    $"GlobalFieldVisualBuffer dirty-rect capacity exceeded: required={_dirtyRectCount + dirtyRects.Length}, capacity={_dirtyRects.Length}.");
            }

            if (!_recordIndexById.TryGetValue(descriptor.Id, out int recordIndex))
            {
                if (_recordCount >= _records.Length)
                {
                    throw new InvalidOperationException(
                        $"GlobalFieldVisualBuffer record capacity exceeded: required={_recordCount + 1}, capacity={_records.Length}.");
                }

                recordIndex = _recordCount++;
                _recordIndexById.Add(descriptor.Id, recordIndex);
            }
            else if (_records[recordIndex].IsActive)
            {
                throw new InvalidOperationException($"Global field visual '{descriptor.Id}' was written more than once in the same frame.");
            }

            int cellStart = _cellCount;
            cells.CopyTo(_cells.AsSpan(cellStart, cells.Length));
            _cellCount += cells.Length;

            int dirtyRectStart = _dirtyRectCount;
            dirtyRects.CopyTo(_dirtyRects.AsSpan(dirtyRectStart, dirtyRects.Length));
            _dirtyRectCount += dirtyRects.Length;

            _records[recordIndex] = new GlobalFieldVisualRecord
            {
                Descriptor = descriptor,
                Revision = ProjectionRevision,
                IsActive = true,
                CellStart = cellStart,
                CellCount = cells.Length,
                DirtyRectStart = dirtyRectStart,
                DirtyRectCount = dirtyRects.Length,
            };
            _activeRecordCount++;
            return recordIndex;
        }

        public ReadOnlySpan<GlobalFieldVisualRecord> GetRecords() => new(_records, 0, _recordCount);

        public ReadOnlySpan<GlobalFieldVisualCell> GetCells(in GlobalFieldVisualRecord record)
        {
            ValidateRange(record.CellStart, record.CellCount, _cellCount, "cell");
            return new ReadOnlySpan<GlobalFieldVisualCell>(_cells, record.CellStart, record.CellCount);
        }

        public ReadOnlySpan<IntRect> GetDirtyRects(in GlobalFieldVisualRecord record)
        {
            ValidateRange(record.DirtyRectStart, record.DirtyRectCount, _dirtyRectCount, "dirty-rect");
            return new ReadOnlySpan<IntRect>(_dirtyRects, record.DirtyRectStart, record.DirtyRectCount);
        }

        public void Clear()
        {
            _recordIndexById.Clear();
            _recordCount = 0;
            _activeRecordCount = 0;
            _cellCount = 0;
            _dirtyRectCount = 0;
            _frameOpen = false;
        }

        private static void ValidateRange(int start, int count, int total, string label)
        {
            if (start < 0 || count < 0 || start + count > total)
            {
                throw new ArgumentOutOfRangeException(label, $"{label} range is outside the current GlobalFieldVisualBuffer frame.");
            }
        }
    }
}
