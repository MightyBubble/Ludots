using System;

namespace Ludots.Core.Navigation.Pathing
{
    public sealed class PathStore
    {
        private readonly int _maxPaths;
        private readonly int _maxPointsPerPath;

        private readonly int[] _pathXcm;
        private readonly int[] _pathYcm;
        private readonly int[] _counts;
        private readonly uint[] _generations;

        private readonly int[] _free;
        private int _freeCount;

        public int MaxPaths => _maxPaths;
        public int MaxPointsPerPath => _maxPointsPerPath;

        public PathStore(int maxPaths, int maxPointsPerPath)
        {
            if (maxPaths <= 0) throw new ArgumentOutOfRangeException(nameof(maxPaths));
            if (maxPointsPerPath <= 0) throw new ArgumentOutOfRangeException(nameof(maxPointsPerPath));

            _maxPaths = maxPaths;
            _maxPointsPerPath = maxPointsPerPath;

            _pathXcm = new int[maxPaths * maxPointsPerPath];
            _pathYcm = new int[maxPaths * maxPointsPerPath];
            _counts = new int[maxPaths];
            _generations = new uint[maxPaths];

            _free = new int[maxPaths];
            for (int i = 0; i < maxPaths; i++)
            {
                _free[i] = maxPaths - 1 - i;
                _generations[i] = 1;
            }
            _freeCount = maxPaths;
        }

        public bool TryAllocate(int pointCapacity, out PathHandle handle)
        {
            if (pointCapacity < 0 || pointCapacity > _maxPointsPerPath || _freeCount <= 0)
            {
                handle = default;
                return false;
            }

            int index = _free[--_freeCount];
            _counts[index] = 0;
            handle = new PathHandle(index, _generations[index]);
            return true;
        }

        public void Release(in PathHandle handle)
        {
            if (!IsAlive(handle)) throw new InvalidOperationException("PATH.ERR.InvalidHandle");
            int index = handle.Index;
            _counts[index] = 0;
            _generations[index]++;
            _free[_freeCount++] = index;
        }

        public bool IsAlive(in PathHandle handle)
        {
            int index = handle.Index;
            if ((uint)index >= (uint)_maxPaths) return false;
            return _generations[index] == handle.Generation;
        }

        public bool TryWrite(in PathHandle handle, ReadOnlySpan<int> xcm, ReadOnlySpan<int> ycm, int count)
        {
            if (!IsAlive(handle)) return false;
            if ((uint)count > (uint)_maxPointsPerPath) return false;
            if (xcm.Length < count || ycm.Length < count) return false;

            int baseOffset = handle.Index * _maxPointsPerPath;
            xcm.Slice(0, count).CopyTo(_pathXcm.AsSpan(baseOffset, count));
            ycm.Slice(0, count).CopyTo(_pathYcm.AsSpan(baseOffset, count));
            _counts[handle.Index] = count;
            return true;
        }

        public bool TryCopy(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
        {
            if (!IsAlive(handle))
            {
                count = 0;
                return false;
            }

            int stored = _counts[handle.Index];
            if (xcmOut.Length < stored || ycmOut.Length < stored)
            {
                count = 0;
                return false;
            }

            int baseOffset = handle.Index * _maxPointsPerPath;
            _pathXcm.AsSpan(baseOffset, stored).CopyTo(xcmOut);
            _pathYcm.AsSpan(baseOffset, stored).CopyTo(ycmOut);
            count = stored;
            return true;
        }
    }
}

