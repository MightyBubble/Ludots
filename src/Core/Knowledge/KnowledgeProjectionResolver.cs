using System;
using Arch.Core;
using Ludots.Core.Association;

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
        private readonly ScopeResolver? _scopeResolver;

        public KnowledgeProjectionResolver(KnowledgeProjectionStore store)
            : this(store, relationProjector: null, scopeResolver: null)
        {
        }

        public KnowledgeProjectionResolver(
            KnowledgeProjectionStore store,
            ScopeResolver scopeResolver)
            : this(store, relationProjector: null, scopeResolver)
        {
        }

        public KnowledgeProjectionResolver(
            KnowledgeProjectionStore store,
            KnowledgeRelationCollectionProjector? relationProjector)
            : this(store, relationProjector, scopeResolver: null)
        {
        }

        public KnowledgeProjectionResolver(
            KnowledgeProjectionStore store,
            KnowledgeRelationCollectionProjector? relationProjector,
            ScopeResolver? scopeResolver)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _relationProjector = relationProjector;
            _scopeResolver = scopeResolver;
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
            Span<Entity> scopeMembers = stackalloc Entity[1];
            var context = new RoleResolverContext(
                actor: viewer,
                subject: viewer,
                viewer: viewer);
            ScopeKey scope = ScopeKey.Self;
            return TryResolve(
                viewer,
                target,
                currentTick,
                in scope,
                in context,
                scopeMembers,
                out projection);
        }

        public bool TryResolve(
            Entity viewer,
            Entity target,
            int currentTick,
            in ScopeKey viewerScope,
            in RoleResolverContext context,
            Span<Entity> scopeMemberBuffer,
            out KnowledgeProjection projection)
        {
            projection = default;
            if (viewer == Entity.Null || target == Entity.Null)
            {
                return false;
            }

            int scopeMemberCount = ResolveScopeMembers(viewer, in viewerScope, in context, scopeMemberBuffer);
            if (scopeMemberCount <= 0)
            {
                return false;
            }

            ReadOnlySpan<Entity> resolvedScopes = scopeMemberBuffer.Slice(0, scopeMemberCount);
            return TryResolveResolvedScopes(viewer, target, currentTick, resolvedScopes, out projection);
        }

        private bool TryResolveResolvedScopes(
            Entity viewer,
            Entity target,
            int currentTick,
            ReadOnlySpan<Entity> resolvedScopes,
            out KnowledgeProjection projection)
        {
            ProjectionAccumulator accumulator = default;
            for (int i = 0; i < resolvedScopes.Length; i++)
            {
                Entity scope = resolvedScopes[i];
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
            in ScopeKey viewerScope,
            in RoleResolverContext context,
            Span<Entity> scopeMemberBuffer,
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

            return TryResolve(
                viewer,
                target,
                currentTick,
                in viewerScope,
                in context,
                scopeMemberBuffer,
                out projection);
        }

        public bool TryResolveWithRelationGrants(
            Entity viewer,
            Entity target,
            int currentTick,
            in ScopeKey viewerScope,
            in RoleResolverContext context,
            Span<Entity> scopeMemberBuffer,
            Span<Entity> relationSourceBuffer,
            Span<Entity> relationTargetBuffer,
            out KnowledgeProjection projection)
        {
            if (TryResolve(
                    viewer,
                    target,
                    currentTick,
                    in viewerScope,
                    in context,
                    scopeMemberBuffer,
                    out projection))
            {
                return true;
            }

            if (_relationProjector != null &&
                !relationSourceBuffer.IsEmpty &&
                !relationTargetBuffer.IsEmpty)
            {
                _relationProjector.ProjectOutgoing(
                    viewer,
                    currentTick,
                    relationSourceBuffer,
                    relationTargetBuffer);
            }

            return TryResolve(
                viewer,
                target,
                currentTick,
                in viewerScope,
                in context,
                scopeMemberBuffer,
                out projection);
        }

        private int ResolveScopeMembers(
            Entity viewer,
            in ScopeKey viewerScope,
            in RoleResolverContext context,
            Span<Entity> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            if (viewerScope.Kind == ScopeKind.Self)
            {
                destination[0] = viewer;
                return 1;
            }

            if (_scopeResolver == null)
            {
                throw new InvalidOperationException("KnowledgeProjectionResolver requires ScopeResolver for non-self ScopeKey resolution.");
            }

            return _scopeResolver.ResolveMembers(in viewerScope, in context, destination);
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

                if (PresenceRank(record.Presence) > PresenceRank(_presence))
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
                int recordRank = PresenceRank(record.Presence);
                int currentRank = PresenceRank(_presence);
                return recordRank > currentRank ||
                       (recordRank == currentRank && record.Position > _position);
            }

            private static int PresenceRank(KnowledgePresence presence)
            {
                return presence switch
                {
                    KnowledgePresence.LiveVisible => 3,
                    KnowledgePresence.HiddenWithSource => 2,
                    KnowledgePresence.Known => 1,
                    _ => 0,
                };
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
