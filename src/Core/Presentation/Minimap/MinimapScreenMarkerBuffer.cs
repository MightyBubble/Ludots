using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Minimap
{
    public readonly record struct MinimapMarkerRenderBucketKey(
        uint ColorKey,
        int SizePx,
        bool HasOrientation,
        int OrientationBucket,
        int OrientationLengthKey,
        int ShadowStrokeKey,
        int ColorStrokeKey,
        byte ShadowAlpha);

    public readonly record struct MinimapScreenMarkerBucket(
        MinimapMarkerRenderBucketKey Key,
        int Start,
        int Count);

    public sealed class MinimapScreenMarkerBuffer
    {
        public const int OrientationBucketCount = 64;

        private readonly int[] _stableIds;
        private readonly float[] _screenX;
        private readonly float[] _screenY;
        private readonly Vector4[] _colors;
        private readonly float[] _sizePx;
        private readonly float[] _orientationRad;
        private readonly float[] _orientationLengthPx;
        private readonly uint[] _flags;
        private readonly int[] _stagedStableIds;
        private readonly float[] _stagedScreenX;
        private readonly float[] _stagedScreenY;
        private readonly Vector4[] _stagedColors;
        private readonly float[] _stagedSizePx;
        private readonly float[] _stagedOrientationRad;
        private readonly float[] _stagedOrientationLengthPx;
        private readonly uint[] _stagedFlags;
        private readonly int[] _stagedBucketIndices;
        private readonly MinimapMarkerRenderBucketKey[] _bucketKeys;
        private readonly int[] _bucketCounts;
        private readonly int[] _bucketWritePositions;
        private readonly MinimapScreenMarkerBucket[] _buckets;
        private readonly MinimapMarkerRenderBucketKey[] _bucketLookupKeys;
        private readonly int[] _bucketLookupIndices;
        private readonly int[] _bucketLookupStamps;
        private readonly int[] _markerBucketIndices;
        private readonly int _bucketLookupMask;
        private int _count;
        private int _stagedCount;
        private int _bucketCount;
        private int _bucketedReservedCount;
        private int _bucketLookupStamp = 1;
        private int _fieldX;
        private int _fieldY;
        private int _fieldSize;
        private bool _bucketLayoutMaterialized;

        public MinimapScreenMarkerBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
            _stableIds = new int[capacity];
            _screenX = new float[capacity];
            _screenY = new float[capacity];
            _colors = new Vector4[capacity];
            _sizePx = new float[capacity];
            _orientationRad = new float[capacity];
            _orientationLengthPx = new float[capacity];
            _flags = new uint[capacity];
            _stagedStableIds = new int[capacity];
            _stagedScreenX = new float[capacity];
            _stagedScreenY = new float[capacity];
            _stagedColors = new Vector4[capacity];
            _stagedSizePx = new float[capacity];
            _stagedOrientationRad = new float[capacity];
            _stagedOrientationLengthPx = new float[capacity];
            _stagedFlags = new uint[capacity];
            _stagedBucketIndices = new int[capacity];
            _bucketKeys = new MinimapMarkerRenderBucketKey[capacity];
            _bucketCounts = new int[capacity];
            _bucketWritePositions = new int[capacity];
            _buckets = new MinimapScreenMarkerBucket[capacity];
            int lookupCapacity = ResolveLookupCapacity(capacity);
            _bucketLookupKeys = new MinimapMarkerRenderBucketKey[lookupCapacity];
            _bucketLookupIndices = new int[lookupCapacity];
            _bucketLookupStamps = new int[lookupCapacity];
            _markerBucketIndices = new int[capacity];
            _bucketLookupMask = lookupCapacity - 1;
        }

        public int Capacity { get; }

        public int Count => _count;

        public int BucketCount => _bucketCount;

        public int FieldX => _fieldX;

        public int FieldY => _fieldY;

        public int FieldSize => _fieldSize;

        public PresentationClipShape ClipShape { get; private set; }

        public int DroppedSinceClear { get; private set; }

        public int DroppedTotal { get; private set; }

        public void BeginFrame()
        {
            _count = 0;
            _stagedCount = 0;
            _bucketCount = 0;
            _bucketedReservedCount = 0;
            _bucketLayoutMaterialized = false;
            ClipShape = PresentationClipShape.None;
            AdvanceLookupStamp();
            DroppedSinceClear = 0;
        }

        public void BeginBucketedFrame()
        {
            BeginFrame();
        }

        public void SetFieldBounds(int x, int y, int size)
        {
            _fieldX = x;
            _fieldY = y;
            _fieldSize = Math.Max(0, size);
        }

        public void SetClipShape(PresentationClipShape clipShape)
        {
            ClipShape = clipShape.IsActive ? clipShape : PresentationClipShape.None;
        }

        public bool TryAdd(
            int stableId,
            float screenX,
            float screenY,
            in Vector4 color,
            float sizePx,
            uint flags = 0u,
            float orientationRad = 0f,
            float orientationLengthPx = 0f)
        {
            int index = _count;
            if ((uint)index >= (uint)Capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _count = index + 1;
            Write(index, stableId, screenX, screenY, in color, sizePx, flags, orientationRad, orientationLengthPx);
            TrackAppendBucket(index, in color, sizePx, flags, orientationRad, orientationLengthPx);
            return true;
        }

        public bool TryAddBucketKey(
            in Vector4 color,
            float sizePx,
            uint flags,
            float orientationRad,
            float orientationLengthPx,
            out int bucketIndex)
        {
            if (sizePx <= 0f || color.W <= 0f)
            {
                bucketIndex = -1;
                return false;
            }

            if ((uint)_bucketedReservedCount >= (uint)Capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                bucketIndex = -1;
                return false;
            }

            MinimapMarkerRenderBucketKey key = CreateBucketKey(in color, sizePx, flags, orientationRad, orientationLengthPx);
            if (TryFindBucketIndex(in key, out bucketIndex))
            {
                _bucketCounts[bucketIndex]++;
                _bucketedReservedCount++;
                _bucketLayoutMaterialized = false;
                return true;
            }

            if ((uint)_bucketCount >= (uint)_bucketKeys.Length)
            {
                bucketIndex = -1;
                return false;
            }

            bucketIndex = _bucketCount++;
            _bucketKeys[bucketIndex] = key;
            _bucketCounts[bucketIndex] = 1;
            AddBucketLookup(in key, bucketIndex);
            _bucketedReservedCount++;
            _bucketLayoutMaterialized = false;
            return true;
        }

        public bool TryAddBucketKey(
            in MinimapMarkerRenderBucketKey key,
            out int bucketIndex)
        {
            if ((uint)_bucketedReservedCount >= (uint)Capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                bucketIndex = -1;
                return false;
            }

            if (TryFindBucketIndex(in key, out bucketIndex))
            {
                _bucketCounts[bucketIndex]++;
                _bucketedReservedCount++;
                _bucketLayoutMaterialized = false;
                return true;
            }

            if ((uint)_bucketCount >= (uint)_bucketKeys.Length)
            {
                bucketIndex = -1;
                return false;
            }

            bucketIndex = _bucketCount++;
            _bucketKeys[bucketIndex] = key;
            _bucketCounts[bucketIndex] = 1;
            AddBucketLookup(in key, bucketIndex);
            _bucketedReservedCount++;
            _bucketLayoutMaterialized = false;
            return true;
        }

        public bool TryGetBucketIndex(
            in Vector4 color,
            float sizePx,
            uint flags,
            float orientationRad,
            float orientationLengthPx,
            out int bucketIndex)
        {
            if (sizePx <= 0f || color.W <= 0f)
            {
                bucketIndex = -1;
                return false;
            }

            MinimapMarkerRenderBucketKey key = CreateBucketKey(in color, sizePx, flags, orientationRad, orientationLengthPx);
            return TryFindBucketIndex(in key, out bucketIndex);
        }

        public bool TryGetBucketIndex(
            in MinimapMarkerRenderBucketKey key,
            out int bucketIndex)
        {
            return TryFindBucketIndex(in key, out bucketIndex);
        }

        public void MaterializeBuckets()
        {
            int start = 0;
            for (int i = 0; i < _bucketCount; i++)
            {
                int count = _bucketCounts[i];
                _buckets[i] = new MinimapScreenMarkerBucket(_bucketKeys[i], start, count);
                _bucketWritePositions[i] = start;
                start += count;
            }

            _count = 0;
            _bucketLayoutMaterialized = true;
        }

        public void BeginDirectBucketWrites()
        {
            MaterializeBuckets();
        }

        public void ResetBucketWritePositions()
        {
            EnsureBucketLayoutMaterialized();
            for (int i = 0; i < _bucketCount; i++)
            {
                _bucketWritePositions[i] = _buckets[i].Start;
            }

            _count = 0;
        }

        public bool TryStageBucketed(
            int stableId,
            float screenX,
            float screenY,
            in Vector4 color,
            float sizePx,
            uint flags = 0u,
            float orientationRad = 0f,
            float orientationLengthPx = 0f)
        {
            if (sizePx <= 0f || color.W <= 0f)
            {
                return false;
            }

            int stagedIndex = _stagedCount;
            if ((uint)stagedIndex >= (uint)Capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            MinimapMarkerRenderBucketKey key = CreateBucketKey(in color, sizePx, flags, orientationRad, orientationLengthPx);
            int bucketIndex;
            if (TryFindBucketIndex(in key, out bucketIndex))
            {
                _bucketCounts[bucketIndex]++;
            }
            else
            {
                if ((uint)_bucketCount >= (uint)_bucketKeys.Length)
                {
                    DroppedSinceClear++;
                    DroppedTotal++;
                    return false;
                }

                bucketIndex = _bucketCount++;
                _bucketKeys[bucketIndex] = key;
                _bucketCounts[bucketIndex] = 1;
                AddBucketLookup(in key, bucketIndex);
            }

            _stagedCount = stagedIndex + 1;
            _bucketedReservedCount = _stagedCount;
            _bucketLayoutMaterialized = false;
            WriteStaged(stagedIndex, stableId, screenX, screenY, in color, sizePx, flags, orientationRad, orientationLengthPx);
            _stagedBucketIndices[stagedIndex] = bucketIndex;
            return true;
        }

        public bool TryStageBucketKeyed(
            int stableId,
            float screenX,
            float screenY,
            in MinimapMarkerRenderBucketKey key)
        {
            return TryStageBucketKeyed(stableId, screenX, screenY, in key, out _);
        }

        public bool TryStageBucketKeyed(
            int stableId,
            float screenX,
            float screenY,
            in MinimapMarkerRenderBucketKey key,
            out int bucketIndex)
        {
            int stagedIndex = _stagedCount;
            if ((uint)stagedIndex >= (uint)Capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                bucketIndex = -1;
                return false;
            }

            if (TryFindBucketIndex(in key, out bucketIndex))
            {
                _bucketCounts[bucketIndex]++;
            }
            else
            {
                if ((uint)_bucketCount >= (uint)_bucketKeys.Length)
                {
                    DroppedSinceClear++;
                    DroppedTotal++;
                    return false;
                }

                bucketIndex = _bucketCount++;
                _bucketKeys[bucketIndex] = key;
                _bucketCounts[bucketIndex] = 1;
                AddBucketLookup(in key, bucketIndex);
            }

            _stagedCount = stagedIndex + 1;
            _bucketedReservedCount = _stagedCount;
            _bucketLayoutMaterialized = false;
            _stagedStableIds[stagedIndex] = stableId <= 0 ? stagedIndex + 1 : stableId;
            _stagedScreenX[stagedIndex] = screenX;
            _stagedScreenY[stagedIndex] = screenY;
            _stagedBucketIndices[stagedIndex] = bucketIndex;
            return true;
        }

        public bool TryStageKnownBucket(
            int stableId,
            float screenX,
            float screenY,
            int bucketIndex)
        {
            int stagedIndex = _stagedCount;
            if ((uint)stagedIndex >= (uint)Capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            if ((uint)bucketIndex >= (uint)_bucketCount)
            {
                return false;
            }

            _bucketCounts[bucketIndex]++;
            _stagedCount = stagedIndex + 1;
            _bucketedReservedCount = _stagedCount;
            _bucketLayoutMaterialized = false;
            _stagedStableIds[stagedIndex] = stableId <= 0 ? stagedIndex + 1 : stableId;
            _stagedScreenX[stagedIndex] = screenX;
            _stagedScreenY[stagedIndex] = screenY;
            _stagedBucketIndices[stagedIndex] = bucketIndex;
            return true;
        }

        public void MaterializeStagedBuckets()
        {
            int start = 0;
            for (int i = 0; i < _bucketCount; i++)
            {
                int count = _bucketCounts[i];
                _buckets[i] = new MinimapScreenMarkerBucket(_bucketKeys[i], start, count);
                _bucketWritePositions[i] = start;
                start += count;
            }

            for (int i = 0; i < _stagedCount; i++)
            {
                int bucketIndex = _stagedBucketIndices[i];
                if ((uint)bucketIndex >= (uint)_bucketCount)
                {
                    continue;
                }

                int index = _bucketWritePositions[bucketIndex]++;
                MinimapScreenMarkerBucket bucket = _buckets[bucketIndex];
                if ((uint)index >= (uint)Capacity ||
                    index >= bucket.Start + bucket.Count)
                {
                    DroppedSinceClear++;
                    DroppedTotal++;
                    continue;
                }

                Write(
                    index,
                    _stagedStableIds[i],
                    _stagedScreenX[i],
                    _stagedScreenY[i],
                    in _stagedColors[i],
                    _stagedSizePx[i],
                    _stagedFlags[i],
                    _stagedOrientationRad[i],
                    _stagedOrientationLengthPx[i]);
                SetMarkerBucket(index, bucketIndex);
            }

            _count = _stagedCount;
            _bucketLayoutMaterialized = true;
        }

        public void MaterializeStagedBucketKeys()
        {
            int start = 0;
            for (int i = 0; i < _bucketCount; i++)
            {
                int count = _bucketCounts[i];
                _buckets[i] = new MinimapScreenMarkerBucket(_bucketKeys[i], start, count);
                _bucketWritePositions[i] = start;
                start += count;
            }

            for (int i = 0; i < _stagedCount; i++)
            {
                int bucketIndex = _stagedBucketIndices[i];
                if ((uint)bucketIndex >= (uint)_bucketCount)
                {
                    continue;
                }

                int index = _bucketWritePositions[bucketIndex]++;
                MinimapScreenMarkerBucket bucket = _buckets[bucketIndex];
                if ((uint)index >= (uint)Capacity ||
                    index >= bucket.Start + bucket.Count)
                {
                    DroppedSinceClear++;
                    DroppedTotal++;
                    continue;
                }

                WriteBucketed(
                    index,
                    _stagedStableIds[i],
                    _stagedScreenX[i],
                    _stagedScreenY[i],
                    bucketIndex);
            }

            _count = _stagedCount;
            _bucketLayoutMaterialized = true;
        }

        public bool TryAddToBucket(
            int bucketIndex,
            int stableId,
            float screenX,
            float screenY,
            in Vector4 color,
            float sizePx,
            uint flags = 0u,
            float orientationRad = 0f,
            float orientationLengthPx = 0f)
        {
            return TryAddToBucket(
                bucketIndex,
                stableId,
                screenX,
                screenY,
                in color,
                sizePx,
                flags,
                orientationRad,
                orientationLengthPx,
                out _);
        }

        public bool TryAddToBucket(
            int bucketIndex,
            int stableId,
            float screenX,
            float screenY,
            in Vector4 color,
            float sizePx,
            uint flags,
            float orientationRad,
            float orientationLengthPx,
            out int markerIndex)
        {
            markerIndex = -1;
            if ((uint)bucketIndex >= (uint)_bucketCount)
            {
                return false;
            }

            EnsureBucketLayoutMaterialized();
            int index = _bucketWritePositions[bucketIndex]++;
            MinimapScreenMarkerBucket bucket = _buckets[bucketIndex];
            if (index >= bucket.Start + bucket.Count)
            {
                return false;
            }

            if ((uint)index >= (uint)Capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _count = Math.Max(_count, index + 1);
            Write(index, stableId, screenX, screenY, in color, sizePx, flags, orientationRad, orientationLengthPx);
            SetMarkerBucket(index, bucketIndex);
            markerIndex = index;
            return true;
        }

        public bool TryAddProjectedToBucket(
            int bucketIndex,
            int stableId,
            float screenX,
            float screenY,
            in MinimapMarkerRenderBucketKey key,
            out int markerIndex)
        {
            return TryAddToBucket(bucketIndex, stableId, screenX, screenY, in key, out markerIndex);
        }

        public bool TryAddProjectedToBucket(
            int bucketIndex,
            int stableId,
            float screenX,
            float screenY,
            out int markerIndex)
        {
            markerIndex = -1;
            if ((uint)bucketIndex >= (uint)_bucketCount)
            {
                return false;
            }

            EnsureBucketLayoutMaterialized();
            int index = _bucketWritePositions[bucketIndex]++;
            MinimapScreenMarkerBucket bucket = _buckets[bucketIndex];
            if (index >= bucket.Start + bucket.Count)
            {
                return false;
            }

            if ((uint)index >= (uint)Capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _count = Math.Max(_count, index + 1);
            WriteBucketed(index, stableId, screenX, screenY, bucketIndex);
            markerIndex = index;
            return true;
        }

        public bool TryAddToBucket(
            int bucketIndex,
            int stableId,
            float screenX,
            float screenY,
            in MinimapMarkerRenderBucketKey key,
            out int markerIndex)
        {
            markerIndex = -1;
            if ((uint)bucketIndex >= (uint)_bucketCount)
            {
                return false;
            }

            EnsureBucketLayoutMaterialized();
            int index = _bucketWritePositions[bucketIndex]++;
            MinimapScreenMarkerBucket bucket = _buckets[bucketIndex];
            if (index >= bucket.Start + bucket.Count)
            {
                return false;
            }

            if ((uint)index >= (uint)Capacity)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _count = Math.Max(_count, index + 1);
            WriteBucketed(index, stableId, screenX, screenY, bucketIndex);
            markerIndex = index;
            return true;
        }

        public bool TryAddProjectedToBucket(
            int bucketIndex,
            int stableId,
            float screenX,
            float screenY,
            in Vector4 color,
            float sizePx,
            uint flags,
            float orientationRad,
            float orientationLengthPx,
            out int markerIndex)
        {
            return TryAddToBucket(
                bucketIndex,
                stableId,
                screenX,
                screenY,
                in color,
                sizePx,
                flags,
                orientationRad,
                orientationLengthPx,
                out markerIndex);
        }

        public MinimapScreenMarkerBucket GetBucket(int index) => _buckets[index];

        public int GetBucketIndex(int markerIndex) => _markerBucketIndices[markerIndex];

        public bool TryGetMarkerBucketKey(int markerIndex, out MinimapMarkerRenderBucketKey key)
        {
            key = default;
            if ((uint)markerIndex >= (uint)_count)
            {
                return false;
            }

            int bucketIndex = _markerBucketIndices[markerIndex];
            if ((uint)bucketIndex >= (uint)_bucketCount)
            {
                return false;
            }

            key = _bucketKeys[bucketIndex];
            return true;
        }

        public int GetStableId(int index) => _stableIds[index];

        public float GetScreenX(int index) => _screenX[index];

        public float GetScreenY(int index) => _screenY[index];

        public ReadOnlySpan<float> ScreenX => _screenX.AsSpan(0, Count);

        public ReadOnlySpan<float> ScreenY => _screenY.AsSpan(0, Count);

        public Vector4 GetColor(int index)
        {
            return TryGetMarkerBucketKey(index, out MinimapMarkerRenderBucketKey key)
                ? UnpackColorKey(key.ColorKey)
                : _colors[index];
        }

        public float GetSizePx(int index)
        {
            return TryGetMarkerBucketKey(index, out MinimapMarkerRenderBucketKey key)
                ? key.SizePx
                : _sizePx[index];
        }

        public float GetOrientationRad(int index)
        {
            return TryGetMarkerBucketKey(index, out MinimapMarkerRenderBucketKey key) && key.HasOrientation
                ? WorldPlane2D.BucketToFacingRad(key.OrientationBucket, OrientationBucketCount)
                : _orientationRad[index];
        }

        public float GetOrientationLengthPx(int index)
        {
            return TryGetMarkerBucketKey(index, out MinimapMarkerRenderBucketKey key) && key.HasOrientation
                ? key.OrientationLengthKey / 16f
                : _orientationLengthPx[index];
        }

        public uint GetFlags(int index)
        {
            return TryGetMarkerBucketKey(index, out MinimapMarkerRenderBucketKey key)
                ? (key.HasOrientation ? MinimapMarkerFlags.HasOrientation : 0u)
                : _flags[index];
        }

        public static MinimapMarkerRenderBucketKey CreateBucketKey(
            in Vector4 color,
            float sizePx,
            uint flags,
            float orientationRad,
            float orientationLengthPx)
        {
            MinimapMarkerRenderBucketKey styleKey = CreateStyleKey(in color, sizePx, flags, orientationLengthPx);
            return WithOrientationBucket(styleKey, orientationRad);
        }

        public static MinimapMarkerRenderBucketKey CreateStyleKey(
            in Vector4 color,
            float sizePx,
            uint flags,
            float orientationLengthPx)
        {
            int resolvedSizePx = Math.Max(1, (int)MathF.Round(sizePx));
            float lengthPx = 0f;
            float shadowStroke = 0f;
            float colorStroke = 0f;
            bool hasOrientation = color.W > 0f &&
                TryResolveOrientationSprite(
                    resolvedSizePx,
                    flags,
                    orientationLengthPx,
                    out lengthPx,
                    out shadowStroke,
                    out colorStroke);

            return new MinimapMarkerRenderBucketKey(
                PackColorKey(color),
                resolvedSizePx,
                hasOrientation,
                0,
                hasOrientation ? Math.Max(1, (int)MathF.Round(lengthPx * 16f)) : 0,
                hasOrientation ? QuantizeStrokeWidth(shadowStroke) : 0,
                hasOrientation ? QuantizeStrokeWidth(colorStroke) : 0,
                hasOrientation ? (byte)Math.Clamp(color.W * 210f, 0f, 255f) : (byte)0);
        }

        public static MinimapMarkerRenderBucketKey WithOrientationBucket(
            in MinimapMarkerRenderBucketKey key,
            float orientationRad)
        {
            if (!key.HasOrientation)
            {
                return key;
            }

            return new MinimapMarkerRenderBucketKey(
                key.ColorKey,
                key.SizePx,
                true,
                WorldPlane2D.QuantizeFacingRadToBucket(orientationRad, OrientationBucketCount),
                key.OrientationLengthKey,
                key.ShadowStrokeKey,
                key.ColorStrokeKey,
                key.ShadowAlpha);
        }

        public static MinimapMarkerRenderBucketKey WithOrientationBucket(
            in MinimapMarkerRenderBucketKey key,
            int orientationBucket)
        {
            if (!key.HasOrientation)
            {
                return key;
            }

            int bucket = WorldPlane2D.NormalizeBucketIndex(orientationBucket, OrientationBucketCount);

            return new MinimapMarkerRenderBucketKey(
                key.ColorKey,
                key.SizePx,
                true,
                bucket,
                key.OrientationLengthKey,
                key.ShadowStrokeKey,
                key.ColorStrokeKey,
                key.ShadowAlpha);
        }

        public static uint PackColorKey(in Vector4 color)
        {
            byte a = (byte)Math.Clamp(color.W * 255f, 0f, 255f);
            byte r = (byte)Math.Clamp(color.X * 255f, 0f, 255f);
            byte g = (byte)Math.Clamp(color.Y * 255f, 0f, 255f);
            byte b = (byte)Math.Clamp(color.Z * 255f, 0f, 255f);
            return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        }

        private static bool TryResolveOrientationSprite(
            float sizePx,
            uint flags,
            float orientationLengthPx,
            out float lengthPx,
            out float shadowStroke,
            out float colorStroke)
        {
            lengthPx = 0f;
            shadowStroke = 0f;
            colorStroke = 0f;
            if ((flags & MinimapMarkerFlags.HasOrientation) == 0u ||
                orientationLengthPx <= 0f ||
                !float.IsFinite(orientationLengthPx))
            {
                return false;
            }

            lengthPx = MathF.Max(orientationLengthPx, sizePx * 0.55f);
            shadowStroke = MathF.Max(2f, sizePx * 0.30f);
            colorStroke = MathF.Max(1f, sizePx * 0.16f);
            return true;
        }

        private static int QuantizeStrokeWidth(float strokeWidth)
        {
            return Math.Max(1, (int)MathF.Round(strokeWidth * 16f));
        }

        private void TrackAppendBucket(
            int markerIndex,
            in Vector4 color,
            float sizePx,
            uint flags,
            float orientationRad,
            float orientationLengthPx)
        {
            if (sizePx <= 0f || color.W <= 0f)
            {
                return;
            }

            MinimapMarkerRenderBucketKey key = CreateBucketKey(in color, sizePx, flags, orientationRad, orientationLengthPx);
            int lastBucketIndex = _bucketCount - 1;
            if ((uint)lastBucketIndex < (uint)_bucketCount)
            {
                MinimapScreenMarkerBucket last = _buckets[lastBucketIndex];
                if (last.Key.Equals(key) &&
                    last.Start + last.Count == markerIndex)
                {
                    _buckets[lastBucketIndex] = new MinimapScreenMarkerBucket(key, last.Start, last.Count + 1);
                    _bucketKeys[lastBucketIndex] = key;
                    _bucketCounts[lastBucketIndex] = last.Count + 1;
                    SetMarkerBucket(markerIndex, lastBucketIndex);
                    return;
                }
            }

            if ((uint)_bucketCount >= (uint)_buckets.Length)
            {
                return;
            }

            int bucketIndex = _bucketCount++;
            _bucketKeys[bucketIndex] = key;
            _bucketCounts[bucketIndex] = 1;
            _buckets[bucketIndex] = new MinimapScreenMarkerBucket(key, markerIndex, 1);
            SetMarkerBucket(markerIndex, bucketIndex);
        }

        private void EnsureBucketLayoutMaterialized()
        {
            if (!_bucketLayoutMaterialized)
            {
                MaterializeBuckets();
            }
        }

        private static int ResolveLookupCapacity(int capacity)
        {
            int required = Math.Max(8, capacity * 2);
            int value = 1;
            while (value < required && value > 0)
            {
                value <<= 1;
            }

            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Minimap marker capacity is too large for bucket lookup.");
            }

            return value;
        }

        private void AdvanceLookupStamp()
        {
            _bucketLookupStamp++;
            if (_bucketLookupStamp != int.MaxValue)
            {
                return;
            }

            Array.Clear(_bucketLookupStamps);
            _bucketLookupStamp = 1;
        }

        private bool TryFindBucketIndex(in MinimapMarkerRenderBucketKey key, out int bucketIndex)
        {
            int slot = (int)(HashBucketKey(in key) & (uint)_bucketLookupMask);
            while (_bucketLookupStamps[slot] == _bucketLookupStamp)
            {
                if (KeysEqual(in _bucketLookupKeys[slot], in key))
                {
                    bucketIndex = _bucketLookupIndices[slot];
                    return true;
                }

                slot = (slot + 1) & _bucketLookupMask;
            }

            bucketIndex = -1;
            return false;
        }

        private void AddBucketLookup(in MinimapMarkerRenderBucketKey key, int bucketIndex)
        {
            int slot = (int)(HashBucketKey(in key) & (uint)_bucketLookupMask);
            while (_bucketLookupStamps[slot] == _bucketLookupStamp)
            {
                slot = (slot + 1) & _bucketLookupMask;
            }

            _bucketLookupStamps[slot] = _bucketLookupStamp;
            _bucketLookupKeys[slot] = key;
            _bucketLookupIndices[slot] = bucketIndex;
        }

        private static bool KeysEqual(in MinimapMarkerRenderBucketKey left, in MinimapMarkerRenderBucketKey right)
        {
            return left.ColorKey == right.ColorKey &&
                left.SizePx == right.SizePx &&
                left.HasOrientation == right.HasOrientation &&
                left.OrientationBucket == right.OrientationBucket &&
                left.OrientationLengthKey == right.OrientationLengthKey &&
                left.ShadowStrokeKey == right.ShadowStrokeKey &&
                left.ColorStrokeKey == right.ColorStrokeKey &&
                left.ShadowAlpha == right.ShadowAlpha;
        }

        private static uint HashBucketKey(in MinimapMarkerRenderBucketKey key)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = Mix(hash, key.ColorKey);
                hash = Mix(hash, (uint)key.SizePx);
                hash = Mix(hash, key.HasOrientation ? 1u : 0u);
                hash = Mix(hash, (uint)key.OrientationBucket);
                hash = Mix(hash, (uint)key.OrientationLengthKey);
                hash = Mix(hash, (uint)key.ShadowStrokeKey);
                hash = Mix(hash, (uint)key.ColorStrokeKey);
                hash = Mix(hash, key.ShadowAlpha);
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                hash *= 0x846ca68bu;
                hash ^= hash >> 16;
                return hash;
            }
        }

        private static uint Mix(uint hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                return hash * 16777619u;
            }
        }

        private void Write(
            int index,
            int stableId,
            float screenX,
            float screenY,
            in Vector4 color,
            float sizePx,
            uint flags,
            float orientationRad,
            float orientationLengthPx)
        {
            _stableIds[index] = stableId <= 0 ? index + 1 : stableId;
            _screenX[index] = screenX;
            _screenY[index] = screenY;
            _colors[index] = color;
            _sizePx[index] = sizePx;
            _orientationRad[index] = orientationRad;
            _orientationLengthPx[index] = orientationLengthPx;
            _flags[index] = flags;
            _markerBucketIndices[index] = -1;
        }

        private void Write(
            int index,
            int stableId,
            float screenX,
            float screenY,
            in MinimapMarkerRenderBucketKey key)
        {
            _stableIds[index] = stableId <= 0 ? index + 1 : stableId;
            _screenX[index] = screenX;
            _screenY[index] = screenY;
            _colors[index] = UnpackColorKey(key.ColorKey);
            _sizePx[index] = key.SizePx;
            _orientationRad[index] = key.HasOrientation ? WorldPlane2D.BucketToFacingRad(key.OrientationBucket, OrientationBucketCount) : 0f;
            _orientationLengthPx[index] = key.HasOrientation ? key.OrientationLengthKey / 16f : 0f;
            _flags[index] = key.HasOrientation ? MinimapMarkerFlags.HasOrientation : 0u;
            _markerBucketIndices[index] = -1;
        }

        private void WriteBucketed(
            int index,
            int stableId,
            float screenX,
            float screenY,
            int bucketIndex)
        {
            _stableIds[index] = stableId <= 0 ? index + 1 : stableId;
            _screenX[index] = screenX;
            _screenY[index] = screenY;
            SetMarkerBucket(index, bucketIndex);
        }

        private void SetMarkerBucket(int markerIndex, int bucketIndex)
        {
            _markerBucketIndices[markerIndex] = bucketIndex;
        }

        private static Vector4 UnpackColorKey(uint colorKey)
        {
            const float inv255 = 1f / 255f;
            float a = ((colorKey >> 24) & 0xFF) * inv255;
            float r = ((colorKey >> 16) & 0xFF) * inv255;
            float g = ((colorKey >> 8) & 0xFF) * inv255;
            float b = (colorKey & 0xFF) * inv255;
            return new Vector4(r, g, b, a);
        }

        private void WriteStaged(
            int index,
            int stableId,
            float screenX,
            float screenY,
            in Vector4 color,
            float sizePx,
            uint flags,
            float orientationRad,
            float orientationLengthPx)
        {
            _stagedStableIds[index] = stableId <= 0 ? index + 1 : stableId;
            _stagedScreenX[index] = screenX;
            _stagedScreenY[index] = screenY;
            _stagedColors[index] = color;
            _stagedSizePx[index] = sizePx;
            _stagedOrientationRad[index] = orientationRad;
            _stagedOrientationLengthPx[index] = orientationLengthPx;
            _stagedFlags[index] = flags;
        }

    }
}
