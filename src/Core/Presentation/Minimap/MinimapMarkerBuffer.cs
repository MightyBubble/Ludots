using System;
using System.Numerics;
using System.Threading;

namespace Ludots.Core.Presentation.Minimap
{
    public static class MinimapMarkerFlags
    {
        public const uint HasOrientation = 1u;
    }

    public sealed class MinimapMarkerBuffer
    {
        private readonly int[] _stableIds;
        private readonly float[] _worldXcm;
        private readonly float[] _worldYcm;
        private readonly Vector4[] _colors;
        private readonly float[] _sizePx;
        private readonly float[] _orientationRad;
        private readonly float[] _orientationLengthPx;
        private readonly uint[] _flags;
        private readonly MinimapMarkerRenderBucketKey[] _styleBucketKeys;
        private int _count;
        private int _droppedSinceClear;
        private int _droppedTotal;

        public MinimapMarkerBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
            _stableIds = new int[capacity];
            _worldXcm = new float[capacity];
            _worldYcm = new float[capacity];
            _colors = new Vector4[capacity];
            _sizePx = new float[capacity];
            _orientationRad = new float[capacity];
            _orientationLengthPx = new float[capacity];
            _flags = new uint[capacity];
            _styleBucketKeys = new MinimapMarkerRenderBucketKey[capacity];
        }

        public int Capacity { get; }

        public int Count => Math.Min(Math.Max(Volatile.Read(ref _count), 0), Capacity);

        public int DroppedSinceClear => Volatile.Read(ref _droppedSinceClear);

        public int DroppedTotal => Volatile.Read(ref _droppedTotal);

        public void BeginFrame()
        {
            _count = 0;
            _droppedSinceClear = 0;
        }

        public bool TryAdd(
            int stableId,
            float worldXcm,
            float worldYcm,
            in Vector4 color,
            float sizePx,
            uint flags = 0u,
            float orientationRad = 0f,
            float orientationLengthPx = 0f)
        {
            int index = _count;
            if ((uint)index >= (uint)Capacity)
            {
                _droppedSinceClear++;
                _droppedTotal++;
                return false;
            }

            _count = index + 1;
            Write(index, stableId, worldXcm, worldYcm, in color, sizePx, flags, orientationRad, orientationLengthPx);
            return true;
        }

        public bool TryAddThreadSafe(
            int stableId,
            float worldXcm,
            float worldYcm,
            in Vector4 color,
            float sizePx,
            uint flags = 0u,
            float orientationRad = 0f,
            float orientationLengthPx = 0f)
        {
            int index = Interlocked.Increment(ref _count) - 1;
            if ((uint)index >= (uint)Capacity)
            {
                Interlocked.Increment(ref _droppedSinceClear);
                Interlocked.Increment(ref _droppedTotal);
                return false;
            }

            Write(index, stableId, worldXcm, worldYcm, in color, sizePx, flags, orientationRad, orientationLengthPx);
            return true;
        }

        public int ReserveThreadSafe(int requestedCount, out int acceptedCount)
        {
            if (requestedCount <= 0)
            {
                acceptedCount = 0;
                return Count;
            }

            int start = Interlocked.Add(ref _count, requestedCount) - requestedCount;
            if ((uint)start >= (uint)Capacity)
            {
                Interlocked.Add(ref _droppedSinceClear, requestedCount);
                Interlocked.Add(ref _droppedTotal, requestedCount);
                acceptedCount = 0;
                return start;
            }

            acceptedCount = Math.Min(requestedCount, Capacity - start);
            int dropped = requestedCount - acceptedCount;
            if (dropped > 0)
            {
                Interlocked.Add(ref _droppedSinceClear, dropped);
                Interlocked.Add(ref _droppedTotal, dropped);
            }

            return start;
        }

        public void WriteReserved(
            int index,
            int stableId,
            float worldXcm,
            float worldYcm,
            in Vector4 color,
            float sizePx,
            uint flags = 0u,
            float orientationRad = 0f,
            float orientationLengthPx = 0f)
        {
            if ((uint)index >= (uint)Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Write(index, stableId, worldXcm, worldYcm, in color, sizePx, flags, orientationRad, orientationLengthPx);
        }

        public int GetStableId(int index) => _stableIds[index];

        public float GetWorldXcm(int index) => _worldXcm[index];

        public float GetWorldYcm(int index) => _worldYcm[index];

        public Vector4 GetColor(int index) => _colors[index];

        public float GetSizePx(int index) => _sizePx[index];

        public float GetOrientationRad(int index) => _orientationRad[index];

        public float GetOrientationLengthPx(int index) => _orientationLengthPx[index];

        public uint GetFlags(int index) => _flags[index];

        public MinimapMarkerRenderBucketKey GetStyleBucketKey(int index) => _styleBucketKeys[index];

        public ReadOnlySpan<int> StableIds => _stableIds.AsSpan(0, Count);

        public ReadOnlySpan<float> WorldXcm => _worldXcm.AsSpan(0, Count);

        public ReadOnlySpan<float> WorldYcm => _worldYcm.AsSpan(0, Count);

        public ReadOnlySpan<Vector4> Colors => _colors.AsSpan(0, Count);

        public ReadOnlySpan<float> SizePx => _sizePx.AsSpan(0, Count);

        public ReadOnlySpan<float> OrientationRad => _orientationRad.AsSpan(0, Count);

        public ReadOnlySpan<float> OrientationLengthPx => _orientationLengthPx.AsSpan(0, Count);

        public ReadOnlySpan<uint> Flags => _flags.AsSpan(0, Count);

        public ReadOnlySpan<MinimapMarkerRenderBucketKey> StyleBucketKeys => _styleBucketKeys.AsSpan(0, Count);

        private void Write(
            int index,
            int stableId,
            float worldXcm,
            float worldYcm,
            in Vector4 color,
            float sizePx,
            uint flags,
            float orientationRad,
            float orientationLengthPx)
        {
            _stableIds[index] = stableId <= 0 ? index + 1 : stableId;
            _worldXcm[index] = worldXcm;
            _worldYcm[index] = worldYcm;
            _colors[index] = color;
            _sizePx[index] = sizePx;
            _orientationRad[index] = orientationRad;
            _orientationLengthPx[index] = orientationLengthPx;
            _flags[index] = flags;
            _styleBucketKeys[index] = MinimapScreenMarkerBuffer.CreateStyleKey(
                in color,
                sizePx,
                flags,
                orientationLengthPx);
        }
    }
}
