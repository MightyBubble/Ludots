using System;
using System.Collections.Generic;
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
        private readonly Dictionary<int, int> _retainedIndexByStableId = new();
        private int _count;
        private int _transientCount;

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
            if (stableId > 0 && _retainedIndexByStableId.TryGetValue(stableId, out int existingIndex))
            {
                Set(existingIndex, stableId, in p0, in p1, in p2, in p3, width, in fillColor, in borderColor, borderWidth, style);
                return true;
            }

            if (_count >= _stableIds.Length)
            {
                return false;
            }

            int index = _count++;
            Set(index, stableId, in p0, in p1, in p2, in p3, width, in fillColor, in borderColor, borderWidth, style);
            if (stableId > 0)
            {
                _retainedIndexByStableId[stableId] = index;
            }
            else
            {
                _transientCount++;
            }

            return true;
        }

        private void Set(
            int index,
            int stableId,
            in Vector3 p0,
            in Vector3 p1,
            in Vector3 p2,
            in Vector3 p3,
            float width,
            in Vector4 fillColor,
            in Vector4 borderColor,
            float borderWidth,
            byte style)
        {
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
        }

        public void Remove(int stableId)
        {
            if (stableId <= 0 || !_retainedIndexByStableId.TryGetValue(stableId, out int index))
            {
                return;
            }

            int lastIndex = _count - 1;
            if (index != lastIndex)
            {
                Copy(lastIndex, index);
                int movedStableId = _stableIds[index];
                if (movedStableId > 0)
                {
                    _retainedIndexByStableId[movedStableId] = index;
                }
            }

            _count = lastIndex;
            _retainedIndexByStableId.Remove(stableId);
        }

        public void ClearTransient()
        {
            if (_transientCount == 0)
            {
                return;
            }

            for (int index = _count - 1; index >= 0; index--)
            {
                if (_stableIds[index] > 0)
                {
                    continue;
                }

                RemoveAt(index);
            }

            _transientCount = 0;
        }

        private void RemoveAt(int index)
        {
            int lastIndex = _count - 1;
            if (index != lastIndex)
            {
                Copy(lastIndex, index);
                int movedStableId = _stableIds[index];
                if (movedStableId > 0)
                {
                    _retainedIndexByStableId[movedStableId] = index;
                }
            }

            _count = lastIndex;
        }

        private void Copy(int source, int destination)
        {
            _stableIds[destination] = _stableIds[source];
            _p0x[destination] = _p0x[source];
            _p0y[destination] = _p0y[source];
            _p0z[destination] = _p0z[source];
            _p1x[destination] = _p1x[source];
            _p1y[destination] = _p1y[source];
            _p1z[destination] = _p1z[source];
            _p2x[destination] = _p2x[source];
            _p2y[destination] = _p2y[source];
            _p2z[destination] = _p2z[source];
            _p3x[destination] = _p3x[source];
            _p3y[destination] = _p3y[source];
            _p3z[destination] = _p3z[source];
            _width[destination] = _width[source];
            _borderWidth[destination] = _borderWidth[source];
            _fillR[destination] = _fillR[source];
            _fillG[destination] = _fillG[source];
            _fillB[destination] = _fillB[source];
            _fillA[destination] = _fillA[source];
            _borderR[destination] = _borderR[source];
            _borderG[destination] = _borderG[source];
            _borderB[destination] = _borderB[source];
            _borderA[destination] = _borderA[source];
            _style[destination] = _style[source];
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
            _transientCount = 0;
            _retainedIndexByStableId.Clear();
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
