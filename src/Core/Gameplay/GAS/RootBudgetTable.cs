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
        private readonly int[] _rollbackSlotMarks;
        private readonly int[] _rollbackSlots;
        private readonly int[] _rollbackKeys;
        private readonly int[] _rollbackCounts;
        private readonly int[] _rollbackStamps;
        private int _stamp;
        private int _rollbackEpoch;
        private int _rollbackCount;
        private bool _writeCheckpointActive;

        public RootBudgetTable(int capacity)
        {
            capacity = NextPowerOfTwo(capacity);
            _keys = new int[capacity];
            _counts = new int[capacity];
            _stamps = new int[capacity];
            _rollbackSlotMarks = new int[capacity];
            _rollbackSlots = new int[capacity];
            _rollbackKeys = new int[capacity];
            _rollbackCounts = new int[capacity];
            _rollbackStamps = new int[capacity];
            _stamp = 1;
            _rollbackEpoch = 1;
        }

        public int Capacity => _keys.Length;

        internal readonly struct WriteCheckpoint
        {
            internal WriteCheckpoint(int epoch)
            {
                Epoch = epoch;
            }

            internal int Epoch { get; }
        }

        internal WriteCheckpoint CaptureWriteCheckpoint()
        {
            if (_writeCheckpointActive)
            {
                throw new InvalidOperationException("GAS.ROOT_BUDGET.ERR.CheckpointAlreadyActive");
            }

            _rollbackEpoch++;
            if (_rollbackEpoch == 0)
            {
                Array.Clear(_rollbackSlotMarks, 0, _rollbackSlotMarks.Length);
                _rollbackEpoch = 1;
            }
            _rollbackCount = 0;
            _writeCheckpointActive = true;
            return new WriteCheckpoint(_rollbackEpoch);
        }

        internal void CommitWrites(in WriteCheckpoint checkpoint)
        {
            RequireActiveCheckpoint(in checkpoint);
            _rollbackCount = 0;
            _writeCheckpointActive = false;
        }

        internal void RollbackWrites(in WriteCheckpoint checkpoint)
        {
            RequireActiveCheckpoint(in checkpoint);
            for (int i = _rollbackCount - 1; i >= 0; i--)
            {
                int slot = _rollbackSlots[i];
                _keys[slot] = _rollbackKeys[i];
                _counts[slot] = _rollbackCounts[i];
                _stamps[slot] = _rollbackStamps[i];
            }
            _rollbackCount = 0;
            _writeCheckpointActive = false;
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
            if (_writeCheckpointActive)
            {
                throw new InvalidOperationException("GAS.ROOT_BUDGET.ERR.NextFrameDuringCheckpoint");
            }

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
            for (int probes = 0; probes < _keys.Length; probes++)
            {
                if (_stamps[idx] != _stamp)
                {
                    RecordOriginalSlot(idx);
                    _stamps[idx] = _stamp;
                    _keys[idx] = rootId;
                    _counts[idx] = 1;
                    return true;
                }

                if (_keys[idx] == rootId)
                {
                    int c = _counts[idx];
                    if (c >= limit) return false;
                    RecordOriginalSlot(idx);
                    _counts[idx] = c + 1;
                    return true;
                }

                idx = (idx + 1) & mask;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordOriginalSlot(int slot)
        {
            if (!_writeCheckpointActive || _rollbackSlotMarks[slot] == _rollbackEpoch)
            {
                return;
            }
            if (_rollbackCount >= _rollbackSlots.Length)
            {
                throw new InvalidOperationException("GAS.ROOT_BUDGET.ERR.RollbackCapacityExceeded");
            }

            _rollbackSlotMarks[slot] = _rollbackEpoch;
            int index = _rollbackCount++;
            _rollbackSlots[index] = slot;
            _rollbackKeys[index] = _keys[slot];
            _rollbackCounts[index] = _counts[slot];
            _rollbackStamps[index] = _stamps[slot];
        }

        private void RequireActiveCheckpoint(in WriteCheckpoint checkpoint)
        {
            if (!_writeCheckpointActive || checkpoint.Epoch != _rollbackEpoch)
            {
                throw new InvalidOperationException("GAS.ROOT_BUDGET.ERR.InvalidWriteCheckpoint");
            }
        }
    }
}
