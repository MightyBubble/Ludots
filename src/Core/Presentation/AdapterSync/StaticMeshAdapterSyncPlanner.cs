using System;
using System.Collections.Generic;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.AdapterSync
{
    /// <summary>
    /// Diffs adapter-facing visual snapshots into platform-neutral create/update/remove ops
    /// for persistent static mesh lanes. Core remains frame-snapshot; adapters own dirty sync.
    /// </summary>
    public sealed class StaticMeshAdapterSyncPlanner
    {
        private readonly Dictionary<int, StaticMeshAdapterBindingState> _bindingsByStableId = new();
        private readonly Dictionary<StaticMeshLaneKey, LaneState> _lanes = new();
        private readonly HashSet<int> _seenStableIds = new();
        private readonly List<int> _pendingRemovals = new();
        private readonly List<StaticMeshAdapterSyncOp> _operations = new();

        public IReadOnlyDictionary<int, StaticMeshAdapterBindingState> ActiveBindings => _bindingsByStableId;

        public IReadOnlyList<StaticMeshAdapterSyncOp> Operations => _operations;

        public int LastCreateCount { get; private set; }

        public int LastUpdateCount { get; private set; }

        public int LastRemoveCount { get; private set; }

        public void Reset()
        {
            _bindingsByStableId.Clear();
            _lanes.Clear();
            _seenStableIds.Clear();
            _pendingRemovals.Clear();
            _operations.Clear();
            LastCreateCount = 0;
            LastUpdateCount = 0;
            LastRemoveCount = 0;
        }

        public bool TryGetBinding(int stableId, out StaticMeshAdapterBindingState binding)
        {
            return _bindingsByStableId.TryGetValue(stableId, out binding);
        }

        public void Sync(PrimitiveDrawBuffer? snapshot)
        {
            Sync(snapshot != null ? snapshot.GetSpan() : ReadOnlySpan<PrimitiveDrawItem>.Empty);
        }

        public void Sync(ReadOnlySpan<PrimitiveDrawItem> snapshot)
        {
            _operations.Clear();
            _seenStableIds.Clear();
            _pendingRemovals.Clear();
            LastCreateCount = 0;
            LastUpdateCount = 0;
            LastRemoveCount = 0;

            for (int i = 0; i < snapshot.Length; i++)
            {
                ref readonly var item = ref snapshot[i];
                if (!StaticMeshLaneKey.Supports(item))
                {
                    continue;
                }

                ValidateStableId(item.StableId);
                if (!_seenStableIds.Add(item.StableId))
                {
                    throw new InvalidOperationException(
                        $"Static lane snapshot contains duplicate PresentationStableId {item.StableId}.");
                }

                SyncItem(item.StableId, item);
            }

            foreach (var pair in _bindingsByStableId)
            {
                if (!_seenStableIds.Contains(pair.Key))
                {
                    _pendingRemovals.Add(pair.Key);
                }
            }

            for (int i = 0; i < _pendingRemovals.Count; i++)
            {
                RemoveBinding(_pendingRemovals[i]);
            }
        }

        private void SyncItem(int stableId, in PrimitiveDrawItem item)
        {
            StaticMeshLaneKey lane = StaticMeshLaneKey.FromItem(item);
            if (!_bindingsByStableId.TryGetValue(stableId, out var current))
            {
                CreateBinding(stableId, lane, item);
                return;
            }

            if (!current.Lane.Equals(lane))
            {
                RemoveBinding(stableId);
                CreateBinding(stableId, lane, item);
                return;
            }

            PrimitiveDrawItem currentItem = current.Item;
            if (ItemEquals(in currentItem, in item))
            {
                return;
            }

            var updated = current.WithItem(item);
            _bindingsByStableId[stableId] = updated;
            _operations.Add(new StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind.Update, updated));
            LastUpdateCount++;
        }

        private void CreateBinding(int stableId, in StaticMeshLaneKey lane, in PrimitiveDrawItem item)
        {
            LaneState state = GetOrCreateLaneState(lane);
            (int slot, int generation) = state.Allocate();
            var binding = new StaticMeshAdapterBindingState(stableId, lane, slot, generation, item);
            _bindingsByStableId[stableId] = binding;
            _operations.Add(new StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind.Create, binding));
            LastCreateCount++;
        }

        private void RemoveBinding(int stableId)
        {
            if (!_bindingsByStableId.TryGetValue(stableId, out var binding))
            {
                return;
            }

            GetOrCreateLaneState(binding.Lane).Release(binding.Slot);
            _bindingsByStableId.Remove(stableId);
            _operations.Add(new StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind.Remove, binding));
            LastRemoveCount++;
        }

        private LaneState GetOrCreateLaneState(in StaticMeshLaneKey lane)
        {
            if (!_lanes.TryGetValue(lane, out var state))
            {
                state = new LaneState();
                _lanes.Add(lane, state);
            }

            return state;
        }

        private static void ValidateStableId(int stableId)
        {
            if (stableId <= 0)
            {
                throw new InvalidOperationException(
                    $"Persistent static lane sync requires a positive PresentationStableId. Got {stableId}.");
            }
        }

        private static bool ItemEquals(in PrimitiveDrawItem a, in PrimitiveDrawItem b)
        {
            return a.MeshAssetId == b.MeshAssetId
                && a.Position.Equals(b.Position)
                && a.Rotation.Equals(b.Rotation)
                && a.Scale.Equals(b.Scale)
                && a.Color.Equals(b.Color)
                && a.StableId == b.StableId
                && a.MaterialId == b.MaterialId
                && a.TemplateId == b.TemplateId
                && a.RenderPath == b.RenderPath
                && a.Mobility == b.Mobility
                && a.Flags == b.Flags
                && a.Animator.Equals(b.Animator)
                && a.Visibility == b.Visibility;
        }

        private sealed class LaneState
        {
            private readonly List<int> _slotGenerations = new();
            private readonly Stack<int> _freeSlots = new();

            public (int Slot, int Generation) Allocate()
            {
                if (_freeSlots.Count > 0)
                {
                    int slot = _freeSlots.Pop();
                    int generation = _slotGenerations[slot] + 1;
                    _slotGenerations[slot] = generation;
                    return (slot, generation);
                }

                int newSlot = _slotGenerations.Count;
                _slotGenerations.Add(1);
                return (newSlot, 1);
            }

            public void Release(int slot)
            {
                if (slot < 0 || slot >= _slotGenerations.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, "Slot is outside the lane generation table.");
                }

                _freeSlots.Push(slot);
            }
        }
    }
}
