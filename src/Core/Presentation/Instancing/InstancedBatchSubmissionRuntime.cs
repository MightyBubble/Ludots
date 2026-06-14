using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Presentation.Instancing
{
    public sealed class InstancedBatchSubmissionRuntime
    {
        private readonly Dictionary<SubmissionKey, SubmissionState> _states = new();
        private readonly List<SubmissionKey> _scratch = new(256);

        public bool ShouldSubmit(Entity performer, int performerStableId, int batchAssetId, int groupIndex, int totalInstances, out int start, out int count, int budget)
        {
            if (totalInstances <= 0)
            {
                start = 0;
                count = 0;
                return false;
            }

            var key = new SubmissionKey(performer, performerStableId, batchAssetId, groupIndex);
            if (!_states.TryGetValue(key, out SubmissionState state))
            {
                state = new SubmissionState();
            }

            if (state.Completed && state.TotalInstances == totalInstances)
            {
                start = 0;
                count = 0;
                return false;
            }

            if (state.TotalInstances != totalInstances)
            {
                state = new SubmissionState();
            }

            int resolvedBudget = budget <= 0 ? totalInstances : Math.Min(budget, totalInstances);
            start = state.NextStart;
            count = Math.Min(resolvedBudget, totalInstances - start);
            state.NextStart += count;
            state.TotalInstances = totalInstances;
            state.Completed = state.NextStart >= totalInstances;
            _states[key] = state;
            return count > 0;
        }

        public void MarkDirty(Entity performer, int performerStableId, int batchAssetId)
        {
            _scratch.Clear();
            foreach (SubmissionKey key in _states.Keys)
            {
                if (key.Performer == performer &&
                    key.PerformerStableId == performerStableId &&
                    key.BatchAssetId == batchAssetId)
                {
                    _scratch.Add(key);
                }
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                _states.Remove(_scratch[i]);
            }
        }

        public void Remove(Entity performer, int performerStableId)
        {
            _scratch.Clear();
            foreach (SubmissionKey key in _states.Keys)
            {
                if (key.Performer == performer && key.PerformerStableId == performerStableId)
                {
                    _scratch.Add(key);
                }
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                _states.Remove(_scratch[i]);
            }
        }

        private readonly struct SubmissionKey : IEquatable<SubmissionKey>
        {
            public readonly Entity Performer;
            public readonly int PerformerStableId;
            public readonly int BatchAssetId;
            public readonly int GroupIndex;

            public SubmissionKey(Entity performer, int performerStableId, int batchAssetId, int groupIndex)
            {
                Performer = performer;
                PerformerStableId = performerStableId;
                BatchAssetId = batchAssetId;
                GroupIndex = groupIndex;
            }

            public bool Equals(SubmissionKey other)
            {
                return Performer == other.Performer &&
                       PerformerStableId == other.PerformerStableId &&
                       BatchAssetId == other.BatchAssetId &&
                       GroupIndex == other.GroupIndex;
            }

            public override bool Equals(object? obj) => obj is SubmissionKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Performer, PerformerStableId, BatchAssetId, GroupIndex);
        }

        private struct SubmissionState
        {
            public int NextStart;
            public int TotalInstances;
            public bool Completed;
        }
    }
}
