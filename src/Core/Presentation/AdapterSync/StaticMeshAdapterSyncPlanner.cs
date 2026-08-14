using System;
using System.Collections;
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
        private readonly Dictionary<int, BindingEntry> _bindingsByStableId = new();
        private readonly ActiveBindingView _activeBindings;
        private readonly Dictionary<StaticMeshLaneKey, LaneState> _lanes = new();
        private readonly List<int> _pendingRemovals = new();
        private readonly List<StaticMeshAdapterSyncOp> _operations = new();
        private int _syncFrame;
        private int _lastProjectionGeneration = -1;

        public StaticMeshAdapterSyncPlanner()
        {
            _activeBindings = new ActiveBindingView(_bindingsByStableId);
        }

        public IReadOnlyDictionary<int, StaticMeshAdapterBindingState> ActiveBindings => _activeBindings;

        public IReadOnlyList<StaticMeshAdapterSyncOp> Operations => _operations;

        public int LastCreateCount { get; private set; }

        public int LastUpdateCount { get; private set; }

        public int LastRemoveCount { get; private set; }

        public int LastProjectionResyncCount { get; private set; }

        public void Reset()
        {
            _bindingsByStableId.Clear();
            _lanes.Clear();
            _pendingRemovals.Clear();
            _operations.Clear();
            _syncFrame = 0;
            _lastProjectionGeneration = -1;
            LastCreateCount = 0;
            LastUpdateCount = 0;
            LastRemoveCount = 0;
            LastProjectionResyncCount = 0;
        }

        public bool TryGetBinding(int stableId, out StaticMeshAdapterBindingState binding)
        {
            if (_bindingsByStableId.TryGetValue(stableId, out var entry))
            {
                binding = entry.Binding;
                return true;
            }

            binding = default;
            return false;
        }

        public void Sync(PrimitiveDrawBuffer? snapshot)
        {
            Sync(snapshot != null ? snapshot.GetSpan() : ReadOnlySpan<PrimitiveDrawItem>.Empty, snapshot?.ProjectionGeneration ?? 0);
        }

        public void SyncDeltas(ReadOnlySpan<PrimitiveDrawItem> changedItems, ReadOnlySpan<int> removedStableIds)
        {
            SyncDeltas(changedItems, removedStableIds, projectionGeneration: _lastProjectionGeneration < 0 ? 0 : _lastProjectionGeneration);
        }

        public void SyncDeltas(ReadOnlySpan<PrimitiveDrawItem> changedItems, ReadOnlySpan<int> removedStableIds, int projectionGeneration)
        {
            _operations.Clear();
            _pendingRemovals.Clear();
            LastCreateCount = 0;
            LastUpdateCount = 0;
            LastRemoveCount = 0;
            LastProjectionResyncCount = 0;
            bool projectionGenerationChanged = UpdateProjectionGeneration(projectionGeneration);

            for (int i = 0; i < removedStableIds.Length; i++)
            {
                int stableId = removedStableIds[i];
                if (stableId > 0)
                {
                    RemoveBinding(stableId);
                }
            }

            for (int i = 0; i < changedItems.Length; i++)
            {
                ref readonly PrimitiveDrawItem item = ref changedItems[i];
                if (!StaticMeshLaneKey.Supports(item))
                {
                    continue;
                }

                ValidateStableId(item.StableId);
                SyncDeltaItem(item.StableId, item, projectionGeneration, projectionGenerationChanged);
            }

            if (projectionGenerationChanged)
            {
                EmitProjectionResyncOperations(projectionGeneration);
            }
        }

        public void Sync(ReadOnlySpan<PrimitiveDrawItem> snapshot)
        {
            Sync(snapshot, projectionGeneration: _lastProjectionGeneration < 0 ? 0 : _lastProjectionGeneration);
        }

        public void Sync(ReadOnlySpan<PrimitiveDrawItem> snapshot, int projectionGeneration)
        {
            _operations.Clear();
            _pendingRemovals.Clear();
            LastCreateCount = 0;
            LastUpdateCount = 0;
            LastRemoveCount = 0;
            LastProjectionResyncCount = 0;
            bool projectionGenerationChanged = UpdateProjectionGeneration(projectionGeneration);
            AdvanceSyncFrame();
            int supportedCount = 0;

            for (int i = 0; i < snapshot.Length; i++)
            {
                ref readonly var item = ref snapshot[i];
                if (!StaticMeshLaneKey.Supports(item))
                {
                    continue;
                }

                ValidateStableId(item.StableId);
                SyncItem(item.StableId, item, projectionGeneration, projectionGenerationChanged);
                supportedCount++;
            }

            if (supportedCount == _bindingsByStableId.Count)
            {
                return;
            }

            foreach (var pair in _bindingsByStableId)
            {
                if (pair.Value.SeenFrame != _syncFrame)
                {
                    _pendingRemovals.Add(pair.Key);
                }
            }

            for (int i = 0; i < _pendingRemovals.Count; i++)
            {
                RemoveBinding(_pendingRemovals[i]);
            }
        }

        private void AdvanceSyncFrame()
        {
            if (_syncFrame == int.MaxValue)
            {
                foreach (var pair in _bindingsByStableId)
                {
                    pair.Value.SeenFrame = 0;
                }

                _syncFrame = 0;
            }

            _syncFrame++;
        }

        private bool UpdateProjectionGeneration(int projectionGeneration)
        {
            if (_lastProjectionGeneration == projectionGeneration)
            {
                return false;
            }

            bool changed = _lastProjectionGeneration >= 0;
            _lastProjectionGeneration = projectionGeneration;
            return changed;
        }

        private void SyncItem(int stableId, in PrimitiveDrawItem item, int projectionGeneration, bool projectionGenerationChanged)
        {
            StaticMeshLaneKey lane = StaticMeshLaneKey.FromItem(item);
            if (!_bindingsByStableId.TryGetValue(stableId, out var entry))
            {
                CreateBinding(stableId, lane, item, projectionGeneration);
                return;
            }

            if (entry.SeenFrame == _syncFrame)
            {
                throw new InvalidOperationException(
                    $"Static lane snapshot contains duplicate PresentationStableId {stableId}.");
            }

            entry.SeenFrame = _syncFrame;
            StaticMeshAdapterBindingState current = entry.Binding;
            if (!current.Lane.Equals(lane))
            {
                RemoveBinding(stableId);
                CreateBinding(stableId, lane, item, projectionGeneration);
                return;
            }

            PrimitiveDrawItem currentItem = current.Item;
            if (ItemEquals(in currentItem, in item))
            {
                if (projectionGenerationChanged && current.ProjectionGeneration != projectionGeneration)
                {
                    var resynced = current.WithProjectionGeneration(projectionGeneration);
                    entry.Binding = resynced;
                    _operations.Add(new StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind.Resync, resynced));
                    LastProjectionResyncCount++;
                }

                return;
            }

            var updated = current.WithItem(item, projectionGeneration);
            entry.Binding = updated;
            if (projectionGenerationChanged)
            {
                _operations.Add(new StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind.Resync, updated));
                LastProjectionResyncCount++;
                return;
            }

            _operations.Add(new StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind.Update, updated));
            LastUpdateCount++;
        }

        private void SyncDeltaItem(int stableId, in PrimitiveDrawItem item, int projectionGeneration, bool projectionGenerationChanged)
        {
            StaticMeshLaneKey lane = StaticMeshLaneKey.FromItem(item);
            if (!_bindingsByStableId.TryGetValue(stableId, out var entry))
            {
                CreateBinding(stableId, lane, item, projectionGeneration);
                return;
            }

            StaticMeshAdapterBindingState current = entry.Binding;
            if (!current.Lane.Equals(lane))
            {
                RemoveBinding(stableId);
                CreateBinding(stableId, lane, item, projectionGeneration);
                return;
            }

            PrimitiveDrawItem currentItem = current.Item;
            if (ItemEquals(in currentItem, in item))
            {
                return;
            }

            var updated = current.WithItem(item, projectionGeneration);
            entry.Binding = updated;
            if (projectionGenerationChanged)
            {
                _operations.Add(new StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind.Resync, updated));
                LastProjectionResyncCount++;
                return;
            }

            _operations.Add(new StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind.Update, updated));
            LastUpdateCount++;
        }

        private void CreateBinding(int stableId, in StaticMeshLaneKey lane, in PrimitiveDrawItem item, int projectionGeneration)
        {
            LaneState state = GetOrCreateLaneState(lane);
            (int slot, int generation) = state.Allocate();
            var binding = new StaticMeshAdapterBindingState(stableId, lane, slot, generation, item, projectionGeneration);
            _bindingsByStableId[stableId] = new BindingEntry(binding, _syncFrame);
            _operations.Add(new StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind.Create, binding));
            LastCreateCount++;
        }

        private void EmitProjectionResyncOperations(int projectionGeneration)
        {
            foreach (var pair in _bindingsByStableId)
            {
                BindingEntry entry = pair.Value;
                StaticMeshAdapterBindingState current = entry.Binding;
                if (current.ProjectionGeneration == projectionGeneration)
                {
                    continue;
                }

                var resynced = current.WithProjectionGeneration(projectionGeneration);
                entry.Binding = resynced;
                _operations.Add(new StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind.Resync, resynced));
                LastProjectionResyncCount++;
            }
        }

        private void RemoveBinding(int stableId)
        {
            if (!_bindingsByStableId.TryGetValue(stableId, out var entry))
            {
                return;
            }

            StaticMeshAdapterBindingState binding = entry.Binding;
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
                && a.OwnerStableId == b.OwnerStableId
                && a.StableId == b.StableId
                && a.MaterialId == b.MaterialId
                && a.TemplateId == b.TemplateId
                && a.RenderPath == b.RenderPath
                && a.AssetKind == b.AssetKind
                && a.MaterialCustomData.Equals(b.MaterialCustomData)
                && a.Mobility == b.Mobility
                && a.Flags == b.Flags
                && a.Animator.Equals(b.Animator)
                && a.AnimationOverlay.Equals(b.AnimationOverlay)
                && a.Visibility == b.Visibility;
        }

        private sealed class BindingEntry
        {
            public BindingEntry(in StaticMeshAdapterBindingState binding, int seenFrame)
            {
                Binding = binding;
                SeenFrame = seenFrame;
            }

            public StaticMeshAdapterBindingState Binding;
            public int SeenFrame;
        }

        private sealed class ActiveBindingView : IReadOnlyDictionary<int, StaticMeshAdapterBindingState>
        {
            private readonly Dictionary<int, BindingEntry> _source;

            public ActiveBindingView(Dictionary<int, BindingEntry> source)
            {
                _source = source;
            }

            public int Count => _source.Count;

            public IEnumerable<int> Keys => _source.Keys;

            public IEnumerable<StaticMeshAdapterBindingState> Values
            {
                get
                {
                    foreach (var pair in _source)
                    {
                        yield return pair.Value.Binding;
                    }
                }
            }

            public StaticMeshAdapterBindingState this[int key] => _source[key].Binding;

            public bool ContainsKey(int key)
            {
                return _source.ContainsKey(key);
            }

            public bool TryGetValue(int key, out StaticMeshAdapterBindingState value)
            {
                if (_source.TryGetValue(key, out var entry))
                {
                    value = entry.Binding;
                    return true;
                }

                value = default;
                return false;
            }

            public IEnumerator<KeyValuePair<int, StaticMeshAdapterBindingState>> GetEnumerator()
            {
                foreach (var pair in _source)
                {
                    yield return new KeyValuePair<int, StaticMeshAdapterBindingState>(pair.Key, pair.Value.Binding);
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
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
