using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Rendering
{
    /// <summary>
    /// Per-frame zero-allocation spline ribbon buffer.
    /// Uses structure-of-arrays layout so large road batches can be written and read
    /// without per-item allocations or transient object materialization.
    /// </summary>
    public sealed class RoadSplineBuffer
    {
        private readonly int[] _stableIds;
        private readonly float[] _p0x;
        private readonly float[] _p0y;
        private readonly float[] _p0z;
        private readonly float[] _p1x;
        private readonly float[] _p1y;
        private readonly float[] _p1z;
        private readonly float[] _p2x;
        private readonly float[] _p2y;
        private readonly float[] _p2z;
        private readonly float[] _p3x;
        private readonly float[] _p3y;
        private readonly float[] _p3z;
        private readonly float[] _width;
        private readonly float[] _borderWidth;
        private readonly float[] _fillR;
        private readonly float[] _fillG;
        private readonly float[] _fillB;
        private readonly float[] _fillA;
        private readonly float[] _borderR;
        private readonly float[] _borderG;
        private readonly float[] _borderB;
        private readonly float[] _borderA;
        private readonly byte[] _style;
        private int _count;

        public int Count => _count;
        public int Capacity => _stableIds.Length;

        public RoadSplineBuffer(int capacity = 2048)
        {
            if (capacity <= 0)
            {
                capacity = 2048;
            }

            _stableIds = new int[capacity];
            _p0x = new float[capacity];
            _p0y = new float[capacity];
            _p0z = new float[capacity];
            _p1x = new float[capacity];
            _p1y = new float[capacity];
            _p1z = new float[capacity];
            _p2x = new float[capacity];
            _p2y = new float[capacity];
            _p2z = new float[capacity];
            _p3x = new float[capacity];
            _p3y = new float[capacity];
            _p3z = new float[capacity];
            _width = new float[capacity];
            _borderWidth = new float[capacity];
            _fillR = new float[capacity];
            _fillG = new float[capacity];
            _fillB = new float[capacity];
            _fillA = new float[capacity];
            _borderR = new float[capacity];
            _borderG = new float[capacity];
            _borderB = new float[capacity];
            _borderA = new float[capacity];
            _style = new byte[capacity];
        }

        public bool TryAdd(
            int stableId,
            in Vector3 p0,
            in Vector3 p1,
            in Vector3 p2,
            in Vector3 p3,
            float width,
            in Vector4 fillColor,
            in Vector4 borderColor,
            float borderWidth,
            byte style = 0)
        {
            if (_count >= _stableIds.Length)
            {
                return false;
            }

            int index = _count++;
            _stableIds[index] = stableId;
            _p0x[index] = p0.X;
            _p0y[index] = p0.Y;
            _p0z[index] = p0.Z;
            _p1x[index] = p1.X;
            _p1y[index] = p1.Y;
            _p1z[index] = p1.Z;
            _p2x[index] = p2.X;
            _p2y[index] = p2.Y;
            _p2z[index] = p2.Z;
            _p3x[index] = p3.X;
            _p3y[index] = p3.Y;
            _p3z[index] = p3.Z;
            _width[index] = width;
            _borderWidth[index] = borderWidth;
            _fillR[index] = fillColor.X;
            _fillG[index] = fillColor.Y;
            _fillB[index] = fillColor.Z;
            _fillA[index] = fillColor.W;
            _borderR[index] = borderColor.X;
            _borderG[index] = borderColor.Y;
            _borderB[index] = borderColor.Z;
            _borderA[index] = borderColor.W;
            _style[index] = style;
            return true;
        }

        public bool TryAddLine(
            int stableId,
            in Vector3 start,
            in Vector3 end,
            float width,
            in Vector4 fillColor,
            in Vector4 borderColor,
            float borderWidth,
            byte style = 0)
        {
            Vector3 c1 = Vector3.Lerp(start, end, 1f / 3f);
            Vector3 c2 = Vector3.Lerp(start, end, 2f / 3f);
            return TryAdd(stableId, start, c1, c2, end, width, fillColor, borderColor, borderWidth, style);
        }

        public void Clear()
        {
            _count = 0;
        }

        public ReadOnlySpan<int> StableIds => _stableIds.AsSpan(0, _count);
        public ReadOnlySpan<float> P0X => _p0x.AsSpan(0, _count);
        public ReadOnlySpan<float> P0Y => _p0y.AsSpan(0, _count);
        public ReadOnlySpan<float> P0Z => _p0z.AsSpan(0, _count);
        public ReadOnlySpan<float> P1X => _p1x.AsSpan(0, _count);
        public ReadOnlySpan<float> P1Y => _p1y.AsSpan(0, _count);
        public ReadOnlySpan<float> P1Z => _p1z.AsSpan(0, _count);
        public ReadOnlySpan<float> P2X => _p2x.AsSpan(0, _count);
        public ReadOnlySpan<float> P2Y => _p2y.AsSpan(0, _count);
        public ReadOnlySpan<float> P2Z => _p2z.AsSpan(0, _count);
        public ReadOnlySpan<float> P3X => _p3x.AsSpan(0, _count);
        public ReadOnlySpan<float> P3Y => _p3y.AsSpan(0, _count);
        public ReadOnlySpan<float> P3Z => _p3z.AsSpan(0, _count);
        public ReadOnlySpan<float> Width => _width.AsSpan(0, _count);
        public ReadOnlySpan<float> BorderWidth => _borderWidth.AsSpan(0, _count);
        public ReadOnlySpan<float> FillR => _fillR.AsSpan(0, _count);
        public ReadOnlySpan<float> FillG => _fillG.AsSpan(0, _count);
        public ReadOnlySpan<float> FillB => _fillB.AsSpan(0, _count);
        public ReadOnlySpan<float> FillA => _fillA.AsSpan(0, _count);
        public ReadOnlySpan<float> BorderR => _borderR.AsSpan(0, _count);
        public ReadOnlySpan<float> BorderG => _borderG.AsSpan(0, _count);
        public ReadOnlySpan<float> BorderB => _borderB.AsSpan(0, _count);
        public ReadOnlySpan<float> BorderA => _borderA.AsSpan(0, _count);
        public ReadOnlySpan<byte> Style => _style.AsSpan(0, _count);
    }
}
