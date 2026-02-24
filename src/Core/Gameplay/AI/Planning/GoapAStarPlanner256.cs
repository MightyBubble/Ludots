using System;
using System.Numerics;
using Ludots.Core.Collections;
using Ludots.Core.Gameplay.AI.WorldState;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public sealed class GoapAStarPlanner256
    {
        private readonly int _maxNodes;
        private readonly WorldStateBits256[] _states;
        private readonly int[] _parent;
        private readonly int[] _parentAction;
        private readonly int[] _g;
        private readonly int[] _hashSlots;
        private readonly int _hashMask;
        private readonly PriorityQueue<int> _open;
        private int _nodeCount;

        public GoapAStarPlanner256(int maxNodes = 4096)
        {
            _maxNodes = maxNodes < 256 ? 256 : maxNodes;
            _states = new WorldStateBits256[_maxNodes];
            _parent = new int[_maxNodes];
            _parentAction = new int[_maxNodes];
            _g = new int[_maxNodes];

            int hashSize = 1;
            while (hashSize < _maxNodes * 2) hashSize <<= 1;
            _hashSlots = new int[hashSize];
            _hashMask = hashSize - 1;

            _open = new PriorityQueue<int>(_maxNodes);
        }

        public bool TryPlan(
            in WorldStateBits256 start,
            in WorldStateCondition256 goal,
            ActionLibraryCompiled256 library,
            Span<int> outActionIds,
            out int outLength,
            int heuristicWeight = 1)
        {
            Reset();

            int startIndex = AddNode(start, parent: -1, parentAction: -1, g: 0);
            if (startIndex < 0)
            {
                outLength = 0;
                return false;
            }

            _open.Enqueue(startIndex, Heuristic(in start, in goal, heuristicWeight));

            while (_open.TryDequeue(out int current, out _))
            {
                ref readonly var state = ref _states[current];
                if (goal.Match(in state))
                {
                    return Reconstruct(current, outActionIds, out outLength);
                }

                var candidates = new ActionCandidateIndex256.CandidateEnumerator(library.CandidateIndex, in state);
                while (candidates.MoveNext())
                {
                    int actionId = candidates.Current;
                    if (!library.IsApplicable(actionId, in state)) continue;

                    var next = library.ApplyPost(actionId, in state);
                    int nextG = _g[current] + library.Cost[actionId];

                    if (TryFindNode(next, out int existing))
                    {
                        if (nextG >= _g[existing]) continue;
                        _g[existing] = nextG;
                        _parent[existing] = current;
                        _parentAction[existing] = actionId;
                        float f = nextG + Heuristic(in next, in goal, heuristicWeight);
                        _open.Enqueue(existing, f);
                        continue;
                    }

                    int nextIndex = AddNode(next, parent: current, parentAction: actionId, g: nextG);
                    if (nextIndex < 0) continue;
                    float priority = nextG + Heuristic(in next, in goal, heuristicWeight);
                    _open.Enqueue(nextIndex, priority);
                }
            }

            outLength = 0;
            return false;
        }

        private void Reset()
        {
            _open.Clear();
            Array.Clear(_hashSlots, 0, _hashSlots.Length);
            _nodeCount = 0;
        }

        private bool Reconstruct(int node, Span<int> outActionIds, out int outLength)
        {
            int len = 0;
            int cur = node;
            while (cur >= 0)
            {
                int a = _parentAction[cur];
                if (a >= 0)
                {
                    if (len >= outActionIds.Length)
                    {
                        outLength = 0;
                        return false;
                    }
                    outActionIds[len++] = a;
                }
                cur = _parent[cur];
            }

            int i0 = 0;
            int i1 = len - 1;
            while (i0 < i1)
            {
                int tmp = outActionIds[i0];
                outActionIds[i0] = outActionIds[i1];
                outActionIds[i1] = tmp;
                i0++;
                i1--;
            }
            outLength = len;
            return true;
        }

        private int AddNode(in WorldStateBits256 state, int parent, int parentAction, int g)
        {
            if (_nodeCount >= _maxNodes) return -1;
            int index = _nodeCount++;
            _states[index] = state;
            _parent[index] = parent;
            _parentAction[index] = parentAction;
            _g[index] = g;
            InsertHash(state, index);
            return index;
        }

        private static int Heuristic(in WorldStateBits256 state, in WorldStateCondition256 goal, int weight)
        {
            ulong d0 = (state.U0 ^ goal.Values.U0) & goal.Mask.U0;
            ulong d1 = (state.U1 ^ goal.Values.U1) & goal.Mask.U1;
            ulong d2 = (state.U2 ^ goal.Values.U2) & goal.Mask.U2;
            ulong d3 = (state.U3 ^ goal.Values.U3) & goal.Mask.U3;
            int diff = BitOperations.PopCount(d0) + BitOperations.PopCount(d1) + BitOperations.PopCount(d2) + BitOperations.PopCount(d3);
            return diff * (weight <= 0 ? 1 : weight);
        }

        private bool TryFindNode(in WorldStateBits256 state, out int nodeIndex)
        {
            int h = state.GetHashCode();
            int slot = h & _hashMask;
            for (int probe = 0; probe < _hashSlots.Length; probe++)
            {
                int stored = _hashSlots[slot];
                if (stored == 0)
                {
                    nodeIndex = -1;
                    return false;
                }
                int idx = stored - 1;
                if (_states[idx].Equals(state))
                {
                    nodeIndex = idx;
                    return true;
                }
                slot = (slot + 1) & _hashMask;
            }
            nodeIndex = -1;
            return false;
        }

        private void InsertHash(in WorldStateBits256 state, int nodeIndex)
        {
            int h = state.GetHashCode();
            int slot = h & _hashMask;
            for (int probe = 0; probe < _hashSlots.Length; probe++)
            {
                if (_hashSlots[slot] == 0)
                {
                    _hashSlots[slot] = nodeIndex + 1;
                    return;
                }
                slot = (slot + 1) & _hashMask;
            }
        }
    }
}
