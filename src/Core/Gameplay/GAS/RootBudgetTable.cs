using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Open-addressing hash table that tracks per-root creation counts within a frame.
    /// Prevents cascade explosions by limiting how many effects a single root can create.
    /// Uses stamp-based clearing for O(1) NextFrame.
    /// 
    /// Thread-safety: NOT thread-safe. Intended for single-system use per frame.
    /// </summary>
    public sealed class RootBudgetTable
    {
        private readonly int[] _keys;
        private readonly int[] _counts;
        private readonly int[] _stamps;
        private int _stamp;

        public RootBudgetTable(int capacity)
        {
            capacity = NextPowerOfTwo(capacity);
            _keys = new int[capacity];
            _counts = new int[capacity];
            _stamps = new int[capacity];
            _stamp = 1;
        }

        private static int NextPowerOfTwo(int v)
        {
            if (v <= 0) return 1;
            v--;
            v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16;
            return v + 1;
        }

        /// <summary>
        /// Advance to the next frame, logically clearing all entries via stamp increment.
        /// </summary>
        public void NextFrame()
        {
            _stamp++;
            if (_stamp == 0)
            {
                Array.Clear(_stamps, 0, _stamps.Length);
                _stamp = 1;
            }
        }

        /// <summary>
        /// Try to consume one budget unit for the given rootId.
        /// Returns true if under the limit, false if the root has already hit the cap.
        /// rootId == 0 is always allowed (no root tracking).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryConsume(int rootId, int limit)
        {
            if (rootId == 0) return true;

            int mask = _keys.Length - 1;
            int idx = (unchecked(rootId * (int)0x9E3779B1)) & mask;
            while (true)
            {
                if (_stamps[idx] != _stamp)
                {
                    _stamps[idx] = _stamp;
                    _keys[idx] = rootId;
                    _counts[idx] = 1;
                    return true;
                }

                if (_keys[idx] == rootId)
                {
                    int c = _counts[idx];
                    if (c >= limit) return false;
                    _counts[idx] = c + 1;
                    return true;
                }

                idx = (idx + 1) & mask;
            }
        }
    }
}
