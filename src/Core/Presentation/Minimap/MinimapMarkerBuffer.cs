using System;
using System.Numerics;
using System.Threading;

namespace Ludots.Core.Presentation.Minimap
{
    public sealed class MinimapMarkerBuffer
    {
        private readonly int[] _stableIds;
        private readonly float[] _worldXcm;
        private readonly float[] _worldZcm;
        private readonly Vector4[] _colors;
        private readonly float[] _sizePx;
        private readonly uint[] _flags;
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
            _worldZcm = new float[capacity];
            _colors = new Vector4[capacity];
            _sizePx = new float[capacity];
            _flags = new uint[capacity];
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

        public bool TryAdd(int stableId, float worldXcm, float worldZcm, in Vector4 color, float sizePx, uint flags = 0u)
        {
            int index = _count;
            if ((uint)index >= (uint)Capacity)
            {
                _droppedSinceClear++;
                _droppedTotal++;
                return false;
            }

            _count = index + 1;
            Write(index, stableId, worldXcm, worldZcm, in color, sizePx, flags);
            return true;
        }

        public bool TryAddThreadSafe(int stableId, float worldXcm, float worldZcm, in Vector4 color, float sizePx, uint flags = 0u)
        {
            int index = Interlocked.Increment(ref _count) - 1;
            if ((uint)index >= (uint)Capacity)
            {
                Interlocked.Increment(ref _droppedSinceClear);
                Interlocked.Increment(ref _droppedTotal);
                return false;
            }

            Write(index, stableId, worldXcm, worldZcm, in color, sizePx, flags);
            return true;
        }

        public int GetStableId(int index) => _stableIds[index];

        public float GetWorldXcm(int index) => _worldXcm[index];

        public float GetWorldZcm(int index) => _worldZcm[index];

        public Vector4 GetColor(int index) => _colors[index];

        public float GetSizePx(int index) => _sizePx[index];

        public uint GetFlags(int index) => _flags[index];

        private void Write(int index, int stableId, float worldXcm, float worldZcm, in Vector4 color, float sizePx, uint flags)
        {
            _stableIds[index] = stableId <= 0 ? index + 1 : stableId;
            _worldXcm[index] = worldXcm;
            _worldZcm[index] = worldZcm;
            _colors[index] = color;
            _sizePx[index] = sizePx;
            _flags[index] = flags;
        }
    }
}
