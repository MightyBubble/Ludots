using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.AI.WorldState;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public sealed class ActionCandidateIndex256
    {
        public const int AlwaysBucket = 256;

        private readonly int[] _bucketOffsets;
        private readonly int[] _actionIds;
        private readonly short[] _keyAtom;

        public ActionCandidateIndex256(int[] bucketOffsets, int[] actionIds, short[] keyAtom)
        {
            _bucketOffsets = bucketOffsets;
            _actionIds = actionIds;
            _keyAtom = keyAtom;
        }

        public ReadOnlySpan<int> GetBucket(int atomId)
        {
            if ((uint)atomId > 256u) return ReadOnlySpan<int>.Empty;
            int start = _bucketOffsets[atomId];
            int end = _bucketOffsets[atomId + 1];
            return _actionIds.AsSpan(start, end - start);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short GetKeyAtom(int actionId)
        {
            if ((uint)actionId >= (uint)_keyAtom.Length) return -1;
            return _keyAtom[actionId];
        }

        public static ActionCandidateIndex256 Build(in WorldStateBits256[] preMask, in WorldStateBits256[] preValues)
        {
            int count = preMask.Length;
            var keyAtoms = new short[count];
            var bucketCounts = new int[AlwaysBucket + 1];

            for (int i = 0; i < count; i++)
            {
                short key = FindFirstRequiredTrueAtom(in preMask[i], in preValues[i]);
                keyAtoms[i] = key;
                int bucket = key >= 0 ? key : AlwaysBucket;
                bucketCounts[bucket]++;
            }

            var offsets = new int[AlwaysBucket + 2];
            int sum = 0;
            for (int b = 0; b < offsets.Length - 1; b++)
            {
                offsets[b] = sum;
                sum += bucketCounts[b];
            }
            offsets[offsets.Length - 1] = sum;

            var cursor = new int[AlwaysBucket + 1];
            Array.Copy(offsets, cursor, cursor.Length);
            var ids = new int[count];

            for (int i = 0; i < count; i++)
            {
                int bucket = keyAtoms[i] >= 0 ? keyAtoms[i] : AlwaysBucket;
                int write = cursor[bucket]++;
                ids[write] = i;
            }

            return new ActionCandidateIndex256(offsets, ids, keyAtoms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short FindFirstRequiredTrueAtom(in WorldStateBits256 mask, in WorldStateBits256 values)
        {
            ulong m0 = mask.U0 & values.U0;
            if (m0 != 0) return (short)BitOperations.TrailingZeroCount(m0);

            ulong m1 = mask.U1 & values.U1;
            if (m1 != 0) return (short)(64 + BitOperations.TrailingZeroCount(m1));

            ulong m2 = mask.U2 & values.U2;
            if (m2 != 0) return (short)(128 + BitOperations.TrailingZeroCount(m2));

            ulong m3 = mask.U3 & values.U3;
            if (m3 != 0) return (short)(192 + BitOperations.TrailingZeroCount(m3));

            return -1;
        }

        public ref struct CandidateEnumerator
        {
            private ReadOnlySpan<int> _always;
            private readonly ActionCandidateIndex256 _index;
            private WorldStateBits256 _state;
            private int _phase;
            private ReadOnlySpan<int> _currentBucket;
            private int _bucketIndex;
            private int _currentIndexInBucket;

            public CandidateEnumerator(ActionCandidateIndex256 index, in WorldStateBits256 state)
            {
                _index = index;
                _state = state;
                _always = index.GetBucket(AlwaysBucket);
                _phase = 0;
                _currentBucket = _always;
                _bucketIndex = -1;
                _currentIndexInBucket = -1;
                Current = -1;
            }

            public int Current { get; private set; }

            public bool MoveNext()
            {
                _currentIndexInBucket++;
                if (_currentIndexInBucket < _currentBucket.Length)
                {
                    Current = _currentBucket[_currentIndexInBucket];
                    return true;
                }

                if (_phase == 0)
                {
                    _phase = 1;
                    _bucketIndex = -1;
                }

                while (TryNextSetBitBucket(ref _state, ref _bucketIndex, out int atom))
                {
                    _currentBucket = _index.GetBucket(atom);
                    _currentIndexInBucket = 0;
                    if (_currentBucket.Length == 0) continue;
                    Current = _currentBucket[0];
                    return true;
                }

                return false;
            }

            private static bool TryNextSetBitBucket(ref WorldStateBits256 state, ref int cursor, out int atomId)
            {
                cursor++;
                while (cursor < 256)
                {
                    if (state.GetBit(cursor))
                    {
                        atomId = cursor;
                        return true;
                    }
                    cursor++;
                }
                atomId = -1;
                return false;
            }
        }
    }
}
