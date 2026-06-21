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

    public sealed class KnowledgeRelationCollectionProjector
    {
        private readonly RelationshipRuntime _relationships;
        private readonly EntityCollectionStore _collections;
        private readonly RelationshipCatalogRuntime _catalog;
        private readonly KnowledgeProjectionStore _projection;

        public KnowledgeRelationCollectionProjector(
            RelationshipRuntime relationships,
            EntityCollectionStore collections,
            RelationshipCatalogRuntime catalog,
            KnowledgeProjectionStore projection)
        {
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
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

        public int ProjectOutgoing(
            Entity viewer,
            int currentTick,
            Span<Entity> sourceBuffer,
            Span<Entity> targetBuffer)
        {
            if (viewer == Entity.Null || sourceBuffer.IsEmpty || targetBuffer.IsEmpty)
            {
                return 0;
            }

            int projected = 0;
            for (int grantIndex = 0; grantIndex < _catalog.KnowledgeGrantCount; grantIndex++)
            {
                if (!_catalog.TryGetKnowledgeGrantAt(grantIndex, out KnowledgeRelationCollectionGrant grant))
                {
                    continue;
                }

                int sourceCount = _relationships.CollectOutgoing(viewer, grant.RelationshipTypeId, sourceBuffer);
                for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
                {
                    projected += ProjectSource(
                        viewer,
                        sourceBuffer[sourceIndex],
                        grant.RelationshipTypeId,
                        grant.CollectionKeyId,
                        currentTick,
                        in grant.Disclosure,
                        targetBuffer);
                }
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
            for (int grantIndex = 0; grantIndex < _catalog.KnowledgeGrantCount; grantIndex++)
            {
                if (!_catalog.TryGetKnowledgeGrantAt(grantIndex, out KnowledgeRelationCollectionGrant grant) ||
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

        private int ProjectSource(
            Entity viewer,
            Entity source,
            int relationshipTypeId,
            int collectionKeyId,
            int currentTick,
            in KnowledgeDisclosureRecord disclosure,
            Span<Entity> targetBuffer)
        {
            if (viewer == Entity.Null ||
                source == Entity.Null ||
                relationshipTypeId < 0 ||
                collectionKeyId <= 0 ||
                targetBuffer.IsEmpty)
            {
                return 0;
            }

            int targetCount = _collections.CopyEntities(source, collectionKeyId, targetBuffer);
            KnowledgeDisclosureRecord record = CreateProjectedRecord(source, currentTick, disclosure);
            int projected = 0;
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
