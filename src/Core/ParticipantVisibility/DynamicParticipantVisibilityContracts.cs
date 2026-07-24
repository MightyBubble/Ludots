using System;
using Arch.Core;
using Arch.Core.Utils;
using Ludots.Core.EntityCollections;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;

namespace Ludots.Core.ParticipantVisibility
{
    public enum DynamicParticipantViewerKind : byte
    {
        Map = 0,
        Player = 1,
        Team = 2,
    }

    public enum DynamicParticipantSourceKind : byte
    {
        Viewer = 0,
        Entity = 1,
        Target = 2,
    }

    public enum DynamicParticipantOwnerMatchPolicy : byte
    {
        MatchViewer = 0,
        Public = 1,
    }

    [Flags]
    public enum DynamicParticipantQueryFlags : byte
    {
        None = 0,
        RequireSelectable = 1 << 0,
        ExcludePlayerIdentity = 1 << 1,
        ExcludeTeamIdentity = 1 << 2,
        RequireMapMatch = 1 << 3,
    }

    public sealed class DynamicParticipantQueryClause
    {
        public string[] AllComponents { get; set; } = Array.Empty<string>();
        public string[] NoneComponents { get; set; } = Array.Empty<string>();

        public static DynamicParticipantQueryClause Create(
            string[]? allComponents,
            string[]? noneComponents = null)
        {
            return new DynamicParticipantQueryClause
            {
                AllComponents = NormalizeComponentNames(allComponents),
                NoneComponents = NormalizeComponentNames(noneComponents),
            };
        }

        private static string[] NormalizeComponentNames(string[]? values)
        {
            if (values == null || values.Length == 0)
            {
                return Array.Empty<string>();
            }

            string[] normalized = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Dynamic participant query component names must be non-empty.");
                }

                normalized[i] = value.Trim();
            }

            return normalized;
        }
    }

    public sealed class DynamicParticipantQuerySpec
    {
        public DynamicParticipantViewerKind ViewerKind { get; set; }
        public string ViewerRef { get; set; } = string.Empty;
        public string CollectionKey { get; set; } = string.Empty;
        public EntityCollectionRoleKind CollectionRole { get; set; } = EntityCollectionRoleKind.Display;
        public DynamicParticipantQueryClause Query { get; set; } = new();
        public DynamicParticipantQueryFlags Flags { get; set; }
        public DynamicParticipantOwnerMatchPolicy OwnerMatchPolicy { get; set; } = DynamicParticipantOwnerMatchPolicy.MatchViewer;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public KnowledgePresence Presence { get; set; }
        public KnowledgePositionAccess Position { get; set; }
        public string SourceRef { get; set; } = "viewer";
        public DynamicParticipantSourceKind SourceKind { get; set; } = DynamicParticipantSourceKind.Viewer;
        public string? RequiredTag { get; set; }
        public int[] AttributeIds { get; set; } = Array.Empty<int>();
        public string[] Attributes { get; set; } = Array.Empty<string>();
        public int[] RelationshipTypeIds { get; set; } = Array.Empty<int>();
        public string[] RelationshipTypes { get; set; } = Array.Empty<string>();
        public int[] TagIds { get; set; } = Array.Empty<int>();
        public string[] Tags { get; set; } = Array.Empty<string>();
        public KnowledgeIdMask256 AttributeMask { get; set; }
        public KnowledgeIdMask256 RelationshipTypeMask { get; set; }
        public KnowledgeIdMask256 TagMask { get; set; }
        public int ExpiryTick { get; set; }
        public int ConfidencePermille { get; set; } = 1000;

        public static DynamicParticipantQuerySpec Create(
            DynamicParticipantViewerKind viewerKind,
            string viewerRef,
            string collectionKey,
            DynamicParticipantQueryClause query,
            DynamicParticipantQueryFlags flags,
            KnowledgePresence presence,
            KnowledgePositionAccess position,
            EntityCollectionRoleKind collectionRole = EntityCollectionRoleKind.Display,
            string title = "",
            string summary = "",
            string sourceRef = "viewer",
            DynamicParticipantSourceKind sourceKind = DynamicParticipantSourceKind.Viewer,
            string? requiredTag = null,
            int[]? attributeIds = null,
            string[]? attributes = null,
            int[]? relationshipTypeIds = null,
            string[]? relationshipTypes = null,
            int[]? tagIds = null,
            string[]? tags = null,
            in KnowledgeIdMask256 attributeMask = default,
            in KnowledgeIdMask256 relationshipTypeMask = default,
            in KnowledgeIdMask256 tagMask = default,
            int expiryTick = 0,
            int confidencePermille = 1000,
            DynamicParticipantOwnerMatchPolicy ownerMatchPolicy = DynamicParticipantOwnerMatchPolicy.MatchViewer)
        {
            if (string.IsNullOrWhiteSpace(viewerRef))
            {
                throw new ArgumentException("Dynamic participant viewer ref is required.", nameof(viewerRef));
            }

            if (string.IsNullOrWhiteSpace(collectionKey))
            {
                throw new ArgumentException("Dynamic participant collection key is required.", nameof(collectionKey));
            }

            return new DynamicParticipantQuerySpec
            {
                ViewerKind = viewerKind,
                ViewerRef = viewerRef.Trim(),
                CollectionKey = collectionKey.Trim(),
                CollectionRole = collectionRole,
                Query = query,
                Flags = flags,
                OwnerMatchPolicy = ownerMatchPolicy,
                Title = title ?? string.Empty,
                Summary = summary ?? string.Empty,
                Presence = presence,
                Position = position,
                SourceRef = string.IsNullOrWhiteSpace(sourceRef) ? "viewer" : sourceRef.Trim(),
                SourceKind = sourceKind,
                RequiredTag = string.IsNullOrWhiteSpace(requiredTag) ? null : requiredTag.Trim(),
                AttributeIds = attributeIds ?? Array.Empty<int>(),
                Attributes = attributes ?? Array.Empty<string>(),
                RelationshipTypeIds = relationshipTypeIds ?? Array.Empty<int>(),
                RelationshipTypes = relationshipTypes ?? Array.Empty<string>(),
                TagIds = tagIds ?? Array.Empty<int>(),
                Tags = tags ?? Array.Empty<string>(),
                AttributeMask = attributeMask,
                RelationshipTypeMask = relationshipTypeMask,
                TagMask = tagMask,
                ExpiryTick = expiryTick,
                ConfidencePermille = confidencePermille,
            };
        }
    }

    public readonly record struct DynamicParticipantVisibilityBinding(
        Entity Viewer,
        Entity Source,
        MapId MapId,
        EntityCollectionDescriptor CollectionDescriptor,
        QueryDescription Query,
        DynamicParticipantQueryFlags Flags,
        DynamicParticipantOwnerMatchPolicy OwnerMatchPolicy,
        DynamicParticipantSourceKind SourceKind,
        int RequiredTagId,
        KnowledgePresence Presence,
        KnowledgePositionAccess Position,
        KnowledgeIdMask256 AttributeMask,
        KnowledgeIdMask256 RelationshipTypeMask,
        KnowledgeIdMask256 TagMask,
        int ExpiryTick,
        int ConfidencePermille)
    {
        public static DynamicParticipantVisibilityBinding Create(
            Entity viewer,
            Entity source,
            in MapId mapId,
            in EntityCollectionDescriptor collectionDescriptor,
            ReadOnlySpan<ComponentType> allComponents,
            ReadOnlySpan<ComponentType> noneComponents,
            DynamicParticipantQueryFlags flags,
            DynamicParticipantSourceKind sourceKind,
            int requiredTagId,
            KnowledgePresence presence,
            KnowledgePositionAccess position,
            in KnowledgeIdMask256 attributeMask,
            in KnowledgeIdMask256 relationshipTypeMask,
            in KnowledgeIdMask256 tagMask,
            int expiryTick = 0,
            int confidencePermille = 1000,
            DynamicParticipantOwnerMatchPolicy ownerMatchPolicy = DynamicParticipantOwnerMatchPolicy.MatchViewer)
        {
            if (viewer == Entity.Null)
            {
                throw new ArgumentException("Dynamic participant visibility viewer is required.", nameof(viewer));
            }

            if ((uint)confidencePermille > 1000u)
            {
                throw new ArgumentOutOfRangeException(nameof(confidencePermille), "Knowledge confidence must be in permille range 0..1000.");
            }

            if (ownerMatchPolicy is not DynamicParticipantOwnerMatchPolicy.MatchViewer and not DynamicParticipantOwnerMatchPolicy.Public)
            {
                throw new ArgumentOutOfRangeException(nameof(ownerMatchPolicy), "Unknown dynamic participant owner-match policy.");
            }

            var query = new QueryDescription(
                all: allComponents.IsEmpty ? Signature.Null : new Signature(allComponents),
                none: noneComponents.IsEmpty ? Signature.Null : new Signature(noneComponents));
            return new DynamicParticipantVisibilityBinding(
                viewer,
                source,
                mapId,
                collectionDescriptor,
                query,
                flags,
                ownerMatchPolicy,
                sourceKind,
                requiredTagId,
                presence,
                position,
                attributeMask,
                relationshipTypeMask,
                tagMask,
                expiryTick,
                confidencePermille);
        }
    }
}
