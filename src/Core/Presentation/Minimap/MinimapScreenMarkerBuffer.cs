using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Minimap
{
    public sealed class MinimapScreenMarkerBuffer
    {
        private readonly int[] _stableIds;
        private readonly float[] _screenX;
        private readonly float[] _screenY;
        private readonly Vector4[] _colors;
        private readonly float[] _sizePx;
        private readonly float[] _orientationRad;
        private readonly float[] _orientationLengthPx;
        private readonly uint[] _flags;
        private int _count;

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
        }

        public int Capacity { get; }

        public int Count => _count;

        public int DroppedSinceClear { get; private set; }

        public int DroppedTotal { get; private set; }

        public void BeginFrame()
        {
            _count = 0;
            DroppedSinceClear = 0;
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
            _stableIds[index] = stableId <= 0 ? index + 1 : stableId;
            _screenX[index] = screenX;
            _screenY[index] = screenY;
            _colors[index] = color;
            _sizePx[index] = sizePx;
            _orientationRad[index] = orientationRad;
            _orientationLengthPx[index] = orientationLengthPx;
            _flags[index] = flags;
            return true;
        }

        public int GetStableId(int index) => _stableIds[index];

        public float GetScreenX(int index) => _screenX[index];

        public float GetScreenY(int index) => _screenY[index];

        public Vector4 GetColor(int index) => _colors[index];

        public float GetSizePx(int index) => _sizePx[index];

        public float GetOrientationRad(int index) => _orientationRad[index];

        public float GetOrientationLengthPx(int index) => _orientationLengthPx[index];

        public uint GetFlags(int index) => _flags[index];
    }
}
