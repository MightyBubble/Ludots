using System;
using Arch.Core;

namespace Ludots.Core.Knowledge
{
    public sealed class KnowledgeProjectionStore
    {
        private const float LoadFactor = 0.72f;

        private bool[] _active;
        private Entity[] _viewers;
        private int[] _viewerIds;
        private int[] _viewerWorldIds;
        private int[] _viewerVersions;
        private Entity[] _targets;
        private int[] _targetIds;
        private int[] _targetWorldIds;
        private int[] _targetVersions;
        private KnowledgePresence[] _presences;
        private KnowledgePositionAccess[] _positions;
        private KnowledgeIdMask256[] _attributeMasks;
        private KnowledgeIdMask256[] _relationshipTypeMasks;
        private KnowledgeIdMask256[] _tagMasks;
        private Entity[] _sources;
        private int[] _observedTicks;
        private int[] _expiryTicks;
        private int[] _confidencePermilles;
        private uint[] _revisions;

        private int[] _bucketHeads;
        private int[] _entryNext;
        private int[] _entryViewerIds;
        private int[] _entryViewerWorldIds;
        private int[] _entryViewerVersions;
        private int[] _entryTargetIds;
        private int[] _entryTargetWorldIds;
        private int[] _entryTargetVersions;
        private int[] _entrySlots;

        private int _slotCount;
        private int _entryCount;
        private int _activeCount;

        public KnowledgeProjectionStore(int initialCapacity = 64)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _active = new bool[initialCapacity];
            _viewers = new Entity[initialCapacity];
            _viewerIds = new int[initialCapacity];
            _viewerWorldIds = new int[initialCapacity];
            _viewerVersions = new int[initialCapacity];
            _targets = new Entity[initialCapacity];
            _targetIds = new int[initialCapacity];
            _targetWorldIds = new int[initialCapacity];
            _targetVersions = new int[initialCapacity];
            _presences = new KnowledgePresence[initialCapacity];
            _positions = new KnowledgePositionAccess[initialCapacity];
            _attributeMasks = new KnowledgeIdMask256[initialCapacity];
            _relationshipTypeMasks = new KnowledgeIdMask256[initialCapacity];
            _tagMasks = new KnowledgeIdMask256[initialCapacity];
            _sources = new Entity[initialCapacity];
            _observedTicks = new int[initialCapacity];
            _expiryTicks = new int[initialCapacity];
            _confidencePermilles = new int[initialCapacity];
            _revisions = new uint[initialCapacity];

            int bucketCount = NextPowerOfTwo(Math.Max(16, initialCapacity * 2));
            _bucketHeads = new int[bucketCount];
            Array.Fill(_bucketHeads, -1);
            _entryNext = new int[initialCapacity];
            _entryViewerIds = new int[initialCapacity];
            _entryViewerWorldIds = new int[initialCapacity];
            _entryViewerVersions = new int[initialCapacity];
            _entryTargetIds = new int[initialCapacity];
            _entryTargetWorldIds = new int[initialCapacity];
            _entryTargetVersions = new int[initialCapacity];
            _entrySlots = new int[initialCapacity];
        }

        public int RecordCount => _activeCount;

        public uint Upsert(Entity viewer, Entity target, in KnowledgeDisclosureRecord record)
        {
            ValidateViewerAndTarget(viewer, target);

            int slot = GetOrCreateSlot(viewer, target);
            bool wasActive = _active[slot];
            bool changed = !wasActive ||
                           _presences[slot] != record.Presence ||
                           _positions[slot] != record.Position ||
                           _attributeMasks[slot] != record.AttributeMask ||
                           _relationshipTypeMasks[slot] != record.RelationshipTypeMask ||
                           _tagMasks[slot] != record.TagMask ||
                           _sources[slot] != record.Source ||
                           _observedTicks[slot] != record.ObservedTick ||
                           _expiryTicks[slot] != record.ExpiryTick ||
                           _confidencePermilles[slot] != record.ConfidencePermille;

            if (!wasActive)
            {
                _activeCount++;
            }

            _active[slot] = true;
            _viewers[slot] = viewer;
            _viewerIds[slot] = viewer.Id;
            _viewerWorldIds[slot] = viewer.WorldId;
            _viewerVersions[slot] = viewer.Version;
            _targets[slot] = target;
            _targetIds[slot] = target.Id;
            _targetWorldIds[slot] = target.WorldId;
            _targetVersions[slot] = target.Version;
            _presences[slot] = record.Presence;
            _positions[slot] = record.Position;
            _attributeMasks[slot] = record.AttributeMask;
            _relationshipTypeMasks[slot] = record.RelationshipTypeMask;
            _tagMasks[slot] = record.TagMask;
            _sources[slot] = record.Source;
            _observedTicks[slot] = record.ObservedTick;
            _expiryTicks[slot] = record.ExpiryTick;
            _confidencePermilles[slot] = record.ConfidencePermille;

            if (changed)
            {
                _revisions[slot]++;
                if (_revisions[slot] == 0)
                {
                    _revisions[slot] = 1;
                }
            }
            else if (_revisions[slot] == 0)
            {
                _revisions[slot] = 1;
            }

            return _revisions[slot];
        }

        public bool Remove(Entity viewer, Entity target)
        {
            if (!TryFindSlot(viewer, target, out int slot) || !_active[slot])
            {
                return false;
            }

            DeactivateSlot(slot);
            return true;
        }

        public int Expire(int currentTick)
        {
            int expired = 0;
            for (int slot = 0; slot < _slotCount; slot++)
            {
                if (_active[slot] &&
                    _expiryTicks[slot] > 0 &&
                    currentTick >= _expiryTicks[slot])
                {
                    DeactivateSlot(slot);
                    expired++;
                }
            }

            return expired;
        }

        public bool TryGet(Entity viewer, Entity target, int currentTick, out KnowledgeDisclosureRecord record)
        {
            record = default;
            if (!TryFindActiveSlot(viewer, target, currentTick, out int slot))
            {
                return false;
            }

            record = CreateRecord(slot);
            return true;
        }

        public int CopyTargets(Entity viewer, int currentTick, Span<Entity> targets)
        {
            if (targets.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            for (int slot = 0; slot < _slotCount && written < targets.Length; slot++)
            {
                if (SlotMatchesViewer(slot, viewer) && IsProjectable(slot, currentTick))
                {
                    targets[written++] = _targets[slot];
                }
            }

            return written;
        }

        public int CopyRecords(Entity viewer, int currentTick, Span<Entity> targets, Span<KnowledgeDisclosureRecord> records)
        {
            if (targets.IsEmpty || records.IsEmpty)
            {
                return 0;
            }

            int limit = Math.Min(targets.Length, records.Length);
            int written = 0;
            for (int slot = 0; slot < _slotCount && written < limit; slot++)
            {
                if (SlotMatchesViewer(slot, viewer) && IsProjectable(slot, currentTick))
                {
                    targets[written] = _targets[slot];
                    records[written] = CreateRecord(slot);
                    written++;
                }
            }

            return written;
        }

        private KnowledgeDisclosureRecord CreateRecord(int slot)
        {
            return new KnowledgeDisclosureRecord(
                _presences[slot],
                _positions[slot],
                _attributeMasks[slot],
                _relationshipTypeMasks[slot],
                _tagMasks[slot],
                _sources[slot],
                _observedTicks[slot],
                _expiryTicks[slot],
                _confidencePermilles[slot],
                _revisions[slot]);
        }

        private void DeactivateSlot(int slot)
        {
            _active[slot] = false;
            _activeCount--;
            _revisions[slot]++;
            if (_revisions[slot] == 0)
            {
                _revisions[slot] = 1;
            }
        }

        private bool TryFindActiveSlot(Entity viewer, Entity target, int currentTick, out int slot)
        {
            if (!TryFindSlot(viewer, target, out slot))
            {
                return false;
            }

            return IsProjectable(slot, currentTick);
        }

        private bool IsProjectable(int slot, int currentTick)
        {
            return _active[slot] &&
                   (_expiryTicks[slot] <= 0 || currentTick < _expiryTicks[slot]);
        }

        private bool SlotMatchesViewer(int slot, Entity viewer)
        {
            return _active[slot] &&
                   _viewerIds[slot] == viewer.Id &&
                   _viewerWorldIds[slot] == viewer.WorldId &&
                   _viewerVersions[slot] == viewer.Version;
        }

        private int GetOrCreateSlot(Entity viewer, Entity target)
        {
            if (TryFindSlot(viewer, target, out int existing))
            {
                return existing;
            }

            EnsureSlotCapacity(_slotCount + 1);
            EnsureEntryCapacity(_entryCount + 1);
            if ((_entryCount + 1) > (int)(_bucketHeads.Length * LoadFactor))
            {
                Rehash(_bucketHeads.Length * 2);
            }

            int slot = _slotCount++;
            int entry = _entryCount++;
            _entryViewerIds[entry] = viewer.Id;
            _entryViewerWorldIds[entry] = viewer.WorldId;
            _entryViewerVersions[entry] = viewer.Version;
            _entryTargetIds[entry] = target.Id;
            _entryTargetWorldIds[entry] = target.WorldId;
            _entryTargetVersions[entry] = target.Version;
            _entrySlots[entry] = slot;
            int bucket = BucketIndex(viewer, target, _bucketHeads.Length);
            _entryNext[entry] = _bucketHeads[bucket];
            _bucketHeads[bucket] = entry;
            return slot;
        }

        private bool TryFindSlot(Entity viewer, Entity target, out int slot)
        {
            int bucket = BucketIndex(viewer, target, _bucketHeads.Length);
            for (int entry = _bucketHeads[bucket]; entry >= 0; entry = _entryNext[entry])
            {
                if (_entryViewerIds[entry] == viewer.Id &&
                    _entryViewerWorldIds[entry] == viewer.WorldId &&
                    _entryViewerVersions[entry] == viewer.Version &&
                    _entryTargetIds[entry] == target.Id &&
                    _entryTargetWorldIds[entry] == target.WorldId &&
                    _entryTargetVersions[entry] == target.Version)
                {
                    slot = _entrySlots[entry];
                    return true;
                }
            }

            slot = -1;
            return false;
        }

        private void EnsureSlotCapacity(int required)
        {
            if (required <= _active.Length)
            {
                return;
            }

            int next = _active.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _active, next);
            Array.Resize(ref _viewers, next);
            Array.Resize(ref _viewerIds, next);
            Array.Resize(ref _viewerWorldIds, next);
            Array.Resize(ref _viewerVersions, next);
            Array.Resize(ref _targets, next);
            Array.Resize(ref _targetIds, next);
            Array.Resize(ref _targetWorldIds, next);
            Array.Resize(ref _targetVersions, next);
            Array.Resize(ref _presences, next);
            Array.Resize(ref _positions, next);
            Array.Resize(ref _attributeMasks, next);
            Array.Resize(ref _relationshipTypeMasks, next);
            Array.Resize(ref _tagMasks, next);
            Array.Resize(ref _sources, next);
            Array.Resize(ref _observedTicks, next);
            Array.Resize(ref _expiryTicks, next);
            Array.Resize(ref _confidencePermilles, next);
            Array.Resize(ref _revisions, next);
        }

        private void EnsureEntryCapacity(int required)
        {
            if (required <= _entryNext.Length)
            {
                return;
            }

            int next = _entryNext.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _entryNext, next);
            Array.Resize(ref _entryViewerIds, next);
            Array.Resize(ref _entryViewerWorldIds, next);
            Array.Resize(ref _entryViewerVersions, next);
            Array.Resize(ref _entryTargetIds, next);
            Array.Resize(ref _entryTargetWorldIds, next);
            Array.Resize(ref _entryTargetVersions, next);
            Array.Resize(ref _entrySlots, next);
        }

        private void Rehash(int bucketCount)
        {
            int nextBucketCount = NextPowerOfTwo(Math.Max(16, bucketCount));
            Array.Resize(ref _bucketHeads, nextBucketCount);
            Array.Fill(_bucketHeads, -1);
            for (int entry = 0; entry < _entryCount; entry++)
            {
                int bucket = BucketIndex(
                    _entryViewerIds[entry],
                    _entryViewerWorldIds[entry],
                    _entryViewerVersions[entry],
                    _entryTargetIds[entry],
                    _entryTargetWorldIds[entry],
                    _entryTargetVersions[entry],
                    _bucketHeads.Length);
                _entryNext[entry] = _bucketHeads[bucket];
                _bucketHeads[bucket] = entry;
            }
        }

        private static void ValidateViewerAndTarget(Entity viewer, Entity target)
        {
            if (viewer == Entity.Null)
            {
                throw new ArgumentException("Knowledge viewer entity is required.", nameof(viewer));
            }

            if (target == Entity.Null)
            {
                throw new ArgumentException("Knowledge target entity is required.", nameof(target));
            }
        }

        private static int BucketIndex(Entity viewer, Entity target, int bucketCount)
        {
            return BucketIndex(
                viewer.Id,
                viewer.WorldId,
                viewer.Version,
                target.Id,
                target.WorldId,
                target.Version,
                bucketCount);
        }

        private static int BucketIndex(
            int viewerId,
            int viewerWorldId,
            int viewerVersion,
            int targetId,
            int targetWorldId,
            int targetVersion,
            int bucketCount)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)viewerId) * 16777619u;
                hash = (hash ^ (uint)viewerWorldId) * 16777619u;
                hash = (hash ^ (uint)viewerVersion) * 16777619u;
                hash = (hash ^ (uint)targetId) * 16777619u;
                hash = (hash ^ (uint)targetWorldId) * 16777619u;
                hash = (hash ^ (uint)targetVersion) * 16777619u;
                return (int)(hash & (uint)(bucketCount - 1));
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
        }
    }
}
