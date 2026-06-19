using System;
using Arch.Core;

namespace Ludots.Core.Knowledge
{
    public readonly struct KnowledgeProjection
    {
        public readonly Entity Viewer;
        public readonly Entity Target;
        public readonly Entity Source;
        public readonly KnowledgePresence Presence;
        public readonly KnowledgePositionAccess Position;
        public readonly KnowledgeIdMask256 AttributeMask;
        public readonly KnowledgeIdMask256 RelationshipTypeMask;
        public readonly KnowledgeIdMask256 TagMask;
        public readonly int ObservedTick;
        public readonly int ExpiryTick;
        public readonly int ConfidencePermille;
        public readonly uint Revision;

        public KnowledgeProjection(
            Entity viewer,
            Entity target,
            Entity source,
            KnowledgePresence presence,
            KnowledgePositionAccess position,
            in KnowledgeIdMask256 attributeMask,
            in KnowledgeIdMask256 relationshipTypeMask,
            in KnowledgeIdMask256 tagMask,
            int observedTick,
            int expiryTick,
            int confidencePermille,
            uint revision)
        {
            Viewer = viewer;
            Target = target;
            Source = source;
            Presence = presence;
            Position = position;
            AttributeMask = attributeMask;
            RelationshipTypeMask = relationshipTypeMask;
            TagMask = tagMask;
            ObservedTick = observedTick;
            ExpiryTick = expiryTick;
            ConfidencePermille = confidencePermille;
            Revision = revision;
        }

        public bool CanKnowEntity => Presence != KnowledgePresence.Unknown;

        public bool CanReadPosition(KnowledgePositionAccess required)
        {
            return required == KnowledgePositionAccess.None || Position >= required;
        }

        public bool CanReadAttribute(int attributeId) => AttributeMask.ContainsId(attributeId);

        public bool CanReadRelationship(int relationshipTypeId) => RelationshipTypeMask.ContainsId(relationshipTypeId);

        public bool CanReadTag(int tagId) => TagMask.ContainsId(tagId);
    }

    public sealed class KnowledgeProjectionResolver
    {
        private readonly KnowledgeProjectionStore _store;
        private readonly KnowledgeRelationCollectionProjector? _relationProjector;

        public KnowledgeProjectionResolver(KnowledgeProjectionStore store)
            : this(store, relationProjector: null)
        {
        }

        public KnowledgeProjectionResolver(
            KnowledgeProjectionStore store,
            KnowledgeRelationCollectionProjector? relationProjector)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _relationProjector = relationProjector;
        }

        public bool CanKnowEntity(Entity viewer, Entity target, int currentTick)
        {
            return TryResolve(viewer, target, currentTick, out KnowledgeProjection projection) &&
                   projection.CanKnowEntity;
        }

        public bool CanReadPosition(
            Entity viewer,
            Entity target,
            int currentTick,
            KnowledgePositionAccess required)
        {
            return TryResolve(viewer, target, currentTick, out KnowledgeProjection projection) &&
                   projection.CanReadPosition(required);
        }

        public bool CanReadAttribute(Entity viewer, Entity target, int currentTick, int attributeId)
        {
            return TryResolve(viewer, target, currentTick, out KnowledgeProjection projection) &&
                   projection.CanReadAttribute(attributeId);
        }

        public bool CanReadRelationship(Entity viewer, Entity target, int currentTick, int relationshipTypeId)
        {
            return TryResolve(viewer, target, currentTick, out KnowledgeProjection projection) &&
                   projection.CanReadRelationship(relationshipTypeId);
        }

        public bool CanReadTag(Entity viewer, Entity target, int currentTick, int tagId)
        {
            return TryResolve(viewer, target, currentTick, out KnowledgeProjection projection) &&
                   projection.CanReadTag(tagId);
        }

        public bool TryResolve(
            Entity viewer,
            Entity target,
            int currentTick,
            out KnowledgeProjection projection)
        {
            Span<Entity> scopes = stackalloc Entity[1] { viewer };
            return TryResolve(viewer, target, currentTick, scopes, out projection);
        }

        public bool TryResolve(
            Entity viewer,
            Entity target,
            int currentTick,
            ReadOnlySpan<Entity> viewerScopes,
            out KnowledgeProjection projection)
        {
            projection = default;
            if (viewer == Entity.Null || target == Entity.Null || viewerScopes.IsEmpty)
            {
                return false;
            }

            ProjectionAccumulator accumulator = default;
            for (int i = 0; i < viewerScopes.Length; i++)
            {
                Entity scope = viewerScopes[i];
                if (scope == Entity.Null)
                {
                    continue;
                }

                if (_store.TryGet(scope, target, currentTick, out KnowledgeDisclosureRecord record))
                {
                    accumulator.Add(record);
                }
            }

            return accumulator.TryCreate(viewer, target, out projection);
        }

        public bool TryResolve(
            Entity viewer,
            Entity target,
            int currentTick,
            ReadOnlySpan<Entity> viewerScopes,
            int relationGrantTypeId,
            Span<Entity> relationSourceBuffer,
            Span<Entity> relationTargetBuffer,
            out KnowledgeProjection projection)
        {
            if (_relationProjector != null &&
                relationGrantTypeId >= 0 &&
                !relationSourceBuffer.IsEmpty &&
                !relationTargetBuffer.IsEmpty)
            {
                _relationProjector.ProjectOutgoing(
                    viewer,
                    relationGrantTypeId,
                    currentTick,
                    relationSourceBuffer,
                    relationTargetBuffer);
            }

            return TryResolve(viewer, target, currentTick, viewerScopes, out projection);
        }

        private struct ProjectionAccumulator
        {
            private bool _hasAny;
            private Entity _source;
            private KnowledgePresence _presence;
            private KnowledgePositionAccess _position;
            private KnowledgeIdMask256 _attributeMask;
            private KnowledgeIdMask256 _relationshipTypeMask;
            private KnowledgeIdMask256 _tagMask;
            private int _observedTick;
            private int _expiryTick;
            private int _confidencePermille;
            private uint _revision;

            public void Add(in KnowledgeDisclosureRecord record)
            {
                if (!_hasAny)
                {
                    _hasAny = true;
                    _source = record.Source;
                    _presence = record.Presence;
                    _position = record.Position;
                    _attributeMask = record.AttributeMask;
                    _relationshipTypeMask = record.RelationshipTypeMask;
                    _tagMask = record.TagMask;
                    _observedTick = record.ObservedTick;
                    _expiryTick = record.ExpiryTick;
                    _confidencePermille = record.ConfidencePermille;
                    _revision = record.Revision;
                    return;
                }

                if (IsStrongerSource(record))
                {
                    _source = record.Source;
                }

                if (record.Presence > _presence)
                {
                    _presence = record.Presence;
                }

                if (record.Position > _position)
                {
                    _position = record.Position;
                }

                _attributeMask = _attributeMask.Union(record.AttributeMask);
                _relationshipTypeMask = _relationshipTypeMask.Union(record.RelationshipTypeMask);
                _tagMask = _tagMask.Union(record.TagMask);
                _observedTick = Math.Max(_observedTick, record.ObservedTick);
                _expiryTick = MergeExpiry(_expiryTick, record.ExpiryTick);
                _confidencePermille = Math.Max(_confidencePermille, record.ConfidencePermille);
                _revision ^= record.Revision;
            }

            public bool TryCreate(Entity viewer, Entity target, out KnowledgeProjection projection)
            {
                projection = default;
                if (!_hasAny || _presence == KnowledgePresence.Unknown)
                {
                    return false;
                }

                projection = new KnowledgeProjection(
                    viewer,
                    target,
                    _source,
                    _presence,
                    _position,
                    _attributeMask,
                    _relationshipTypeMask,
                    _tagMask,
                    _observedTick,
                    _expiryTick,
                    _confidencePermille,
                    _revision);
                return true;
            }

            private bool IsStrongerSource(in KnowledgeDisclosureRecord record)
            {
                return record.Presence > _presence ||
                       (record.Presence == _presence && record.Position > _position);
            }

            private static int MergeExpiry(int left, int right)
            {
                if (left <= 0 || right <= 0)
                {
                    return 0;
                }

                return Math.Max(left, right);
            }
        }
    }
}
