using System;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.Knowledge
{
    public readonly struct KnowledgeRelationCollectionGrant
    {
        public readonly int RelationshipTypeId;
        public readonly int CollectionKeyId;
        public readonly KnowledgeDisclosureRecord Disclosure;

        public KnowledgeRelationCollectionGrant(
            int relationshipTypeId,
            int collectionKeyId,
            in KnowledgeDisclosureRecord disclosure)
        {
            if (relationshipTypeId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(relationshipTypeId));
            }

            if (collectionKeyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(collectionKeyId));
            }

            RelationshipTypeId = relationshipTypeId;
            CollectionKeyId = collectionKeyId;
            Disclosure = disclosure;
        }
    }

    public sealed class KnowledgeRelationCollectionGrantStore
    {
        private int[] _relationshipTypeIds;
        private int[] _collectionKeyIds;
        private KnowledgeDisclosureRecord[] _disclosures;
        private int _count;

        public KnowledgeRelationCollectionGrantStore(int initialGrantCapacity = 16)
        {
            if (initialGrantCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialGrantCapacity));
            }

            _relationshipTypeIds = new int[initialGrantCapacity];
            _collectionKeyIds = new int[initialGrantCapacity];
            _disclosures = new KnowledgeDisclosureRecord[initialGrantCapacity];
        }

        public int Count => _count;

        public void Upsert(in KnowledgeRelationCollectionGrant grant)
        {
            int slot = FindSlot(grant.RelationshipTypeId, grant.CollectionKeyId);
            if (slot < 0)
            {
                EnsureCapacity(_count + 1);
                slot = _count++;
            }

            _relationshipTypeIds[slot] = grant.RelationshipTypeId;
            _collectionKeyIds[slot] = grant.CollectionKeyId;
            _disclosures[slot] = grant.Disclosure;
        }

        public bool TryGet(int relationshipTypeId, int collectionKeyId, out KnowledgeRelationCollectionGrant grant)
        {
            grant = default;
            if (relationshipTypeId < 0 || collectionKeyId <= 0)
            {
                return false;
            }

            int slot = FindSlot(relationshipTypeId, collectionKeyId);
            if (slot < 0)
            {
                return false;
            }

            grant = CreateGrant(slot);
            return true;
        }

        public bool TryGetAt(int index, out KnowledgeRelationCollectionGrant grant)
        {
            grant = default;
            if ((uint)index >= (uint)_count)
            {
                return false;
            }

            grant = CreateGrant(index);
            return true;
        }

        public int CopyForRelationshipType(int relationshipTypeId, Span<KnowledgeRelationCollectionGrant> destination)
        {
            if (relationshipTypeId < 0 || destination.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            for (int i = 0; i < _count && written < destination.Length; i++)
            {
                if (_relationshipTypeIds[i] != relationshipTypeId)
                {
                    continue;
                }

                destination[written++] = CreateGrant(i);
            }

            return written;
        }

        private KnowledgeRelationCollectionGrant CreateGrant(int slot)
        {
            return new KnowledgeRelationCollectionGrant(
                _relationshipTypeIds[slot],
                _collectionKeyIds[slot],
                _disclosures[slot]);
        }

        private int FindSlot(int relationshipTypeId, int collectionKeyId)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_relationshipTypeIds[i] == relationshipTypeId &&
                    _collectionKeyIds[i] == collectionKeyId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _relationshipTypeIds.Length)
            {
                return;
            }

            int next = _relationshipTypeIds.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _relationshipTypeIds, next);
            Array.Resize(ref _collectionKeyIds, next);
            Array.Resize(ref _disclosures, next);
        }
    }

    public sealed class KnowledgeRelationCollectionProjector
    {
        private readonly RelationshipRuntime _relationships;
        private readonly EntityCollectionStore _collections;
        private readonly KnowledgeRelationCollectionGrantStore _grants;
        private readonly KnowledgeProjectionStore _projection;

        public KnowledgeRelationCollectionProjector(
            RelationshipRuntime relationships,
            EntityCollectionStore collections,
            KnowledgeRelationCollectionGrantStore grants,
            KnowledgeProjectionStore projection)
        {
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _grants = grants ?? throw new ArgumentNullException(nameof(grants));
            _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        }

        public int ProjectOutgoing(
            Entity viewer,
            int relationshipTypeId,
            int currentTick,
            Span<Entity> sourceBuffer,
            Span<Entity> targetBuffer)
        {
            if (viewer == Entity.Null || relationshipTypeId < 0 || sourceBuffer.IsEmpty || targetBuffer.IsEmpty)
            {
                return 0;
            }

            int sourceCount = _relationships.CollectOutgoing(viewer, relationshipTypeId, sourceBuffer);
            int projected = 0;
            for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
            {
                projected += ProjectSource(viewer, sourceBuffer[sourceIndex], relationshipTypeId, currentTick, targetBuffer);
            }

            return projected;
        }

        private int ProjectSource(
            Entity viewer,
            Entity source,
            int relationshipTypeId,
            int currentTick,
            Span<Entity> targetBuffer)
        {
            if (viewer == Entity.Null || source == Entity.Null || relationshipTypeId < 0 || targetBuffer.IsEmpty)
            {
                return 0;
            }

            int projected = 0;
            for (int grantIndex = 0; grantIndex < _grants.Count; grantIndex++)
            {
                if (!_grants.TryGetAt(grantIndex, out KnowledgeRelationCollectionGrant grant) ||
                    grant.RelationshipTypeId != relationshipTypeId)
                {
                    continue;
                }

                int targetCount = _collections.CopyEntities(source, grant.CollectionKeyId, targetBuffer);
                KnowledgeDisclosureRecord record = CreateProjectedRecord(source, currentTick, grant.Disclosure);
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    Entity target = targetBuffer[targetIndex];
                    if (target == Entity.Null)
                    {
                        continue;
                    }

                    _projection.Upsert(viewer, target, record);
                    projected++;
                }
            }

            return projected;
        }

        private static KnowledgeDisclosureRecord CreateProjectedRecord(
            Entity source,
            int currentTick,
            in KnowledgeDisclosureRecord disclosure)
        {
            int observedTick = disclosure.ObservedTick == 0 ? currentTick : disclosure.ObservedTick;
            return new KnowledgeDisclosureRecord(
                disclosure.Presence,
                disclosure.Position,
                disclosure.AttributeMask,
                disclosure.RelationshipTypeMask,
                disclosure.TagMask,
                source,
                observedTick,
                disclosure.ExpiryTick,
                disclosure.ConfidencePermille,
                revision: 0);
        }
    }
}
