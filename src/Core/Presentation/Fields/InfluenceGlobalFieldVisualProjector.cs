using System;
using System.Collections.Generic;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Fields
{
    /// <summary>
    /// Projects InfluenceFieldRegistry into GlobalFieldVisualBuffer (Influence kind).
    /// Float values are quantized to byte relative to normalizePeak. 0-alloc after warmup.
    /// </summary>
    public sealed class InfluenceGlobalFieldVisualProjector
    {
        private FieldCellValue2D<float>[] _sourceCells = Array.Empty<FieldCellValue2D<float>>();
        private GlobalFieldVisualCell[] _visualCells = Array.Empty<GlobalFieldVisualCell>();
        private readonly IntRect[] _dirtyRects = new IntRect[1];
        private readonly Dictionary<string, int> _scopeKeyByField = new(StringComparer.Ordinal);
        private readonly Dictionary<GlobalFieldVisualId, IntRect> _boundsById = new();
        private int _nextScopeKeyId = 1;

        public float NormalizePeak { get; set; } = 10f;

        public int LastProjectedFieldCount { get; private set; }
        public int LastProjectedCellCount { get; private set; }

        public void Project(InfluenceFieldRegistry registry, GlobalFieldVisualBuffer buffer)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (NormalizePeak <= 0f)
            {
                throw new InvalidOperationException("InfluenceGlobalFieldVisualProjector.NormalizePeak must be > 0.");
            }

            LastProjectedFieldCount = 0;
            LastProjectedCellCount = 0;

            foreach (KeyValuePair<string, InfluenceField> pair in registry.Fields)
            {
                InfluenceField field = pair.Value;
                int capacity = Math.Max(1, field.CellCount);
                EnsureSourceCapacity(capacity);
                EnsureVisualCapacity(capacity);

                int cellCount = field.CopyNonDefaultCells(_sourceCells.AsSpan(0, capacity));
                IntRect cellBounds = BuildVisualCells(_sourceCells.AsSpan(0, cellCount), _visualCells.AsSpan(0, cellCount));
                if (!HasArea(cellBounds))
                {
                    continue;
                }

                int scopeKeyId = ResolveScopeKeyId(pair.Key);
                var id = new GlobalFieldVisualId(
                    GlobalFieldVisualKind.Influence,
                    scopeKeyId,
                    layerKeyId: 0,
                    surfaceKeyId: 0);

                IntRect bounds = ResolveStableBounds(id, cellBounds);
                _dirtyRects[0] = bounds;
                var descriptor = new GlobalFieldVisualDescriptor(
                    id,
                    field.CellSizeCm,
                    WorldCmInt2.Zero,
                    bounds,
                    GlobalFieldVisualValueKind.Byte);

                buffer.Upsert(
                    descriptor,
                    _visualCells.AsSpan(0, cellCount),
                    _dirtyRects.AsSpan(0, 1));

                LastProjectedFieldCount++;
                LastProjectedCellCount += cellCount;
            }
        }

        private IntRect BuildVisualCells(
            ReadOnlySpan<FieldCellValue2D<float>> source,
            Span<GlobalFieldVisualCell> destination)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            for (int i = 0; i < source.Length; i++)
            {
                FieldCellValue2D<float> cell = source[i];
                byte quantized = Quantize(cell.Value);
                destination[i] = new GlobalFieldVisualCell(cell.Cell, quantized);
                if (cell.Cell.X < minX) minX = cell.Cell.X;
                if (cell.Cell.Y < minY) minY = cell.Cell.Y;
                if (cell.Cell.X > maxX) maxX = cell.Cell.X;
                if (cell.Cell.Y > maxY) maxY = cell.Cell.Y;
            }

            if (source.Length == 0)
            {
                return default;
            }

            return new IntRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private byte Quantize(float value)
        {
            float normalized = Math.Clamp(value / NormalizePeak, 0f, 1f);
            return (byte)Math.Clamp((int)Math.Round(normalized * 255.0), 0, 255);
        }

        private IntRect ResolveStableBounds(GlobalFieldVisualId id, IntRect cellBounds)
        {
            if (_boundsById.TryGetValue(id, out IntRect existing) && HasArea(existing))
            {
                IntRect union = Union(existing, cellBounds);
                _boundsById[id] = union;
                return union;
            }

            _boundsById[id] = cellBounds;
            return cellBounds;
        }

        private int ResolveScopeKeyId(string fieldKey)
        {
            if (_scopeKeyByField.TryGetValue(fieldKey, out int existing))
            {
                return existing;
            }

            int id = _nextScopeKeyId++;
            _scopeKeyByField.Add(fieldKey, id);
            return id;
        }

        private void EnsureSourceCapacity(int capacity)
        {
            if (_sourceCells.Length < capacity)
            {
                _sourceCells = new FieldCellValue2D<float>[capacity];
            }
        }

        private void EnsureVisualCapacity(int capacity)
        {
            if (_visualCells.Length < capacity)
            {
                _visualCells = new GlobalFieldVisualCell[capacity];
            }
        }

        private static bool HasArea(IntRect rect) => rect.Width > 0 && rect.Height > 0;

        private static IntRect Union(IntRect a, IntRect b)
        {
            int minX = Math.Min(a.X, b.X);
            int minY = Math.Min(a.Y, b.Y);
            int maxX = Math.Max(a.X + a.Width, b.X + b.Width);
            int maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new IntRect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
