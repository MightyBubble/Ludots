using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public struct TrailMeshSample
    {
        public Vector3 Base;
        public Vector3 Tip;
        public float Age01;
    }

    /// <summary>
    /// Retained trail-mesh snapshot buffer. Presentation systems upsert one flat sample
    /// strip per trail each frame (index 0 = newest head sample); the renderer reads
    /// strips and rebuilds triangle bands. Identity is the owner presenter stableId.
    /// </summary>
    public sealed class TrailMeshBuffer
    {
        public const int MaxSamplesPerTrail = 32;

        private readonly int[] _stableIds;
        private readonly int[] _sampleCounts;
        private readonly float[] _headR;
        private readonly float[] _headG;
        private readonly float[] _headB;
        private readonly float[] _headA;
        private readonly float[] _tailR;
        private readonly float[] _tailG;
        private readonly float[] _tailB;
        private readonly float[] _tailA;
        private readonly TrailMeshSample[] _samples;
        private readonly Dictionary<int, int> _indexByStableId = new();
        private int _count;

        public int Count => _count;
        public int Capacity => _stableIds.Length;

        public TrailMeshBuffer(int capacity = 64)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _stableIds = new int[capacity];
            _sampleCounts = new int[capacity];
            _headR = new float[capacity];
            _headG = new float[capacity];
            _headB = new float[capacity];
            _headA = new float[capacity];
            _tailR = new float[capacity];
            _tailG = new float[capacity];
            _tailB = new float[capacity];
            _tailA = new float[capacity];
            _samples = new TrailMeshSample[capacity * MaxSamplesPerTrail];
        }

        public bool Upsert(
            int stableId,
            ReadOnlySpan<TrailMeshSample> samples,
            in Vector4 headColor,
            in Vector4 tailColor)
        {
            if (stableId <= 0)
            {
                throw new ArgumentException($"TrailMeshBuffer requires a positive stableId, got {stableId}.", nameof(stableId));
            }

            if (samples.Length == 0 || samples.Length > MaxSamplesPerTrail)
            {
                throw new ArgumentException($"TrailMeshBuffer accepts 1..{MaxSamplesPerTrail} samples per trail, got {samples.Length}.", nameof(samples));
            }

            if (!_indexByStableId.TryGetValue(stableId, out int index))
            {
                if (_count >= _stableIds.Length)
                {
                    return false;
                }

                index = _count++;
                _indexByStableId[stableId] = index;
            }

            _stableIds[index] = stableId;
            _sampleCounts[index] = samples.Length;
            _headR[index] = headColor.X;
            _headG[index] = headColor.Y;
            _headB[index] = headColor.Z;
            _headA[index] = headColor.W;
            _tailR[index] = tailColor.X;
            _tailG[index] = tailColor.Y;
            _tailB[index] = tailColor.Z;
            _tailA[index] = tailColor.W;
            samples.CopyTo(_samples.AsSpan(index * MaxSamplesPerTrail));
            return true;
        }

        public void Remove(int stableId)
        {
            if (stableId <= 0 || !_indexByStableId.TryGetValue(stableId, out int index))
            {
                return;
            }

            int lastIndex = _count - 1;
            if (index != lastIndex)
            {
                MoveHeader(lastIndex, index);
                Array.Copy(_samples, lastIndex * MaxSamplesPerTrail, _samples, index * MaxSamplesPerTrail, MaxSamplesPerTrail);
                _indexByStableId[_stableIds[index]] = index;
            }

            _count = lastIndex;
            _indexByStableId.Remove(stableId);
        }

        public void Clear()
        {
            _count = 0;
            _indexByStableId.Clear();
        }

        public ReadOnlySpan<TrailMeshSample> GetSamples(int itemIndex)
        {
            if ((uint)itemIndex >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(itemIndex));
            }

            return _samples.AsSpan(itemIndex * MaxSamplesPerTrail, _sampleCounts[itemIndex]);
        }

        public Vector4 GetHeadColor(int itemIndex)
        {
            if ((uint)itemIndex >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(itemIndex));
            }

            return new Vector4(_headR[itemIndex], _headG[itemIndex], _headB[itemIndex], _headA[itemIndex]);
        }

        public Vector4 GetTailColor(int itemIndex)
        {
            if ((uint)itemIndex >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(itemIndex));
            }

            return new Vector4(_tailR[itemIndex], _tailG[itemIndex], _tailB[itemIndex], _tailA[itemIndex]);
        }

        public int GetStableId(int itemIndex)
        {
            if ((uint)itemIndex >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(itemIndex));
            }

            return _stableIds[itemIndex];
        }

        private void MoveHeader(int source, int destination)
        {
            _stableIds[destination] = _stableIds[source];
            _sampleCounts[destination] = _sampleCounts[source];
            _headR[destination] = _headR[source];
            _headG[destination] = _headG[source];
            _headB[destination] = _headB[source];
            _headA[destination] = _headA[source];
            _tailR[destination] = _tailR[source];
            _tailG[destination] = _tailG[source];
            _tailB[destination] = _tailB[source];
            _tailA[destination] = _tailA[source];
        }
    }
}
