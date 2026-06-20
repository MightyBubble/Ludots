using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using ParticipantViewCapabilityMod.Runtime;

namespace CapabilityStandardParticipantViewsMod;

internal static class ParticipantViewKnowledgeShowcaseInstaller
{
    private const string MetadataSectionKey = "participantViewKnowledge";

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Install(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        MapSession? session = engine.CurrentMapSession;
        if (session == null || !ParticipantViewCapabilityIds.IsParticipantViewMap(session.MapConfig))
        {
            return;
        }

        ParticipantViewKnowledgeMetadata metadata = ReadRequiredMetadata(session);
        RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("CapabilityStandardParticipantViewsMod requires RelationshipRuntime.");
        RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new InvalidOperationException("CapabilityStandardParticipantViewsMod requires RelationshipTypeRegistry.");
        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("CapabilityStandardParticipantViewsMod requires EntityCollectionStore.");

        var store = new KnowledgeProjectionStore(initialCapacity: Math.Max(64, metadata.Projections.Length * 16));
        var grants = new KnowledgeRelationCollectionGrantStore(initialGrantCapacity: Math.Max(8, metadata.Grants.Length + 1));
        var projector = new KnowledgeRelationCollectionProjector(relationships, collections, grants, store);

        InstallCollections(session, collections, metadata.Collections);
        InstallRelations(session, relationships, relationshipTypes, metadata.Relations);
        InstallGrants(relationshipTypes, collections, grants, metadata.Grants);
        InstallDirectProjections(engine.World, session, relationshipTypes, store, metadata.Projections);

        engine.SetService(CoreServiceKeys.KnowledgeProjectionStore, store);
        engine.SetService(CoreServiceKeys.KnowledgeRelationCollectionGrantStore, grants);
        engine.SetService(CoreServiceKeys.KnowledgeRelationCollectionProjector, projector);
        engine.SetService(CoreServiceKeys.KnowledgeProjectionResolver, new KnowledgeProjectionResolver(store, projector));
    }

    private static ParticipantViewKnowledgeMetadata ReadRequiredMetadata(MapSession session)
    {
        if (!session.MapConfig.Metadata.TryGetValue(MetadataSectionKey, out JsonNode? sectionNode) ||
            sectionNode == null)
        {
            throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' must declare metadata.{MetadataSectionKey} for the participant visibility showcase.");
        }

        ParticipantViewKnowledgeMetadata? metadata = JsonSerializer.Deserialize<ParticipantViewKnowledgeMetadata>(
            sectionNode.ToJsonString(),
            MetadataJsonOptions);
        if (metadata == null)
        {
            throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' metadata.{MetadataSectionKey} could not be deserialized.");
        }

        return metadata;
    }

    private static void InstallCollections(
        MapSession session,
        EntityCollectionStore collections,
        KnowledgeCollectionSpec[] specs)
    {
        for (int i = 0; i < specs.Length; i++)
        {
            KnowledgeCollectionSpec spec = specs[i];
            Entity owner = ResolveEntityRef(session, spec.Owner, $"metadata.{MetadataSectionKey}.collections[{i}].owner");
            Entity[] targets = ResolveEntityRefs(session, spec.Entities, $"metadata.{MetadataSectionKey}.collections[{i}].entities");
            collections.Replace(
                owner,
                EntityCollectionDescriptor.Create(
                    RequireNonEmpty(spec.Key, $"metadata.{MetadataSectionKey}.collections[{i}].key"),
                    EntityCollectionSourceKind.RelationDerived,
                    EntityCollectionRoleKind.Display,
                    title: spec.Title ?? string.Empty,
                    summary: spec.Summary ?? string.Empty),
                targets);
        }
    }

    private static void InstallRelations(
        MapSession session,
        RelationshipRuntime relationships,
        RelationshipTypeRegistry relationshipTypes,
        KnowledgeRelationSpec[] specs)
    {
        for (int i = 0; i < specs.Length; i++)
        {
            KnowledgeRelationSpec spec = specs[i];
            int relationshipTypeId = relationshipTypes.Register(
                RequireNonEmpty(spec.RelationshipType, $"metadata.{MetadataSectionKey}.relations[{i}].relationshipType"));
            Entity target = ResolveEntityRef(session, spec.Target, $"metadata.{MetadataSectionKey}.relations[{i}].target");
            string context = $"metadata.{MetadataSectionKey}.relations[{i}].viewers";
            for (int viewerIndex = 0; viewerIndex < spec.Viewers.Length; viewerIndex++)
            {
                Entity viewer = ResolveEntityRef(session, spec.Viewers[viewerIndex], $"{context}[{viewerIndex}]");
                relationships.EnsureLink(viewer, target, relationshipTypeId);
            }
        }
    }

    private static void InstallGrants(
        RelationshipTypeRegistry relationshipTypes,
        EntityCollectionStore collections,
        KnowledgeRelationCollectionGrantStore grants,
        KnowledgeGrantSpec[] specs)
    {
        for (int i = 0; i < specs.Length; i++)
        {
            KnowledgeGrantSpec spec = specs[i];
            int relationshipTypeId = relationshipTypes.Register(
                RequireNonEmpty(spec.RelationshipType, $"metadata.{MetadataSectionKey}.grants[{i}].relationshipType"));
            int collectionKeyId = collections.KeyRegistry.Register(
                RequireNonEmpty(spec.CollectionKey, $"metadata.{MetadataSectionKey}.grants[{i}].collectionKey"));
            grants.Upsert(new KnowledgeRelationCollectionGrant(
                relationshipTypeId,
                collectionKeyId,
                CreateRecord(
                    spec.Presence,
                    spec.Position,
                    Entity.Null,
                    BuildMask(spec.AttributeIds, spec.Attributes, ResolveAttributeId, $"metadata.{MetadataSectionKey}.grants[{i}].attributes"),
                    BuildMask(spec.RelationshipTypeIds, spec.RelationshipTypes, name => relationshipTypes.Register(name), $"metadata.{MetadataSectionKey}.grants[{i}].relationshipTypes"),
                    BuildMask(spec.TagIds, spec.Tags, ResolveTagId, $"metadata.{MetadataSectionKey}.grants[{i}].tags"),
                    spec.ObservedTick,
                    spec.ExpiryTick,
                    spec.ConfidencePermille)));
        }
    }

    private static void InstallDirectProjections(
        World world,
        MapSession session,
        RelationshipTypeRegistry relationshipTypes,
        KnowledgeProjectionStore store,
        KnowledgeProjectionSpec[] specs)
    {
        for (int i = 0; i < specs.Length; i++)
        {
            KnowledgeProjectionSpec spec = specs[i];
            Entity[] targets = ResolveTargets(world, session, spec.Targets, i);
            KnowledgeIdMask256 attributeMask = BuildMask(
                spec.AttributeIds,
                spec.Attributes,
                ResolveAttributeId,
                $"metadata.{MetadataSectionKey}.projections[{i}].attributes");
            KnowledgeIdMask256 relationshipMask = BuildMask(
                spec.RelationshipTypeIds,
                spec.RelationshipTypes,
                name => relationshipTypes.Register(name),
                $"metadata.{MetadataSectionKey}.projections[{i}].relationshipTypes");
            KnowledgeIdMask256 tagMask = BuildMask(
                spec.TagIds,
                spec.Tags,
                ResolveTagId,
                $"metadata.{MetadataSectionKey}.projections[{i}].tags");

            for (int viewerIndex = 0; viewerIndex < spec.Viewers.Length; viewerIndex++)
            {
                Entity viewer = ResolveEntityRef(
                    session,
                    spec.Viewers[viewerIndex],
                    $"metadata.{MetadataSectionKey}.projections[{i}].viewers[{viewerIndex}]");
                Entity source = ResolveSource(session, spec.Source, viewer, i);
                KnowledgeDisclosureRecord record = CreateRecord(
                    spec.Presence,
                    spec.Position,
                    source,
                    attributeMask,
                    relationshipMask,
                    tagMask,
                    spec.ObservedTick,
                    spec.ExpiryTick,
                    spec.ConfidencePermille);

                for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                {
                    store.Upsert(viewer, targets[targetIndex], record);
                }
            }
        }
    }

    private static Entity[] ResolveTargets(World world, MapSession session, KnowledgeTargetSelector selector, int projectionIndex)
    {
        string context = $"metadata.{MetadataSectionKey}.projections[{projectionIndex}].targets";
        string kind = RequireNonEmpty(selector.Kind, $"{context}.kind");
        return kind switch
        {
            "instances" => ResolveEntityRefs(session, selector.Entities, $"{context}.entities"),
            "playerMembers" => ParticipantViewProjection.ResolvePlayerMembers(
                world,
                session,
                RequirePositive(selector.PlayerId, $"{context}.playerId")),
            "teamMembers" => ParticipantViewProjection.ResolveTeamMembers(
                world,
                session,
                RequirePositive(selector.TeamId, $"{context}.teamId")),
            "mapMembers" => ParticipantViewProjection.ResolveMapMembers(world, session),
            _ => throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' {context}.kind '{kind}' is invalid. Expected instances, playerMembers, teamMembers, or mapMembers.")
        };
    }

    private static Entity ResolveSource(MapSession session, string? sourceRef, Entity viewer, int projectionIndex)
    {
        if (string.IsNullOrWhiteSpace(sourceRef) || string.Equals(sourceRef, "viewer", StringComparison.Ordinal))
        {
            return viewer;
        }

        return ResolveEntityRef(
            session,
            sourceRef,
            $"metadata.{MetadataSectionKey}.projections[{projectionIndex}].source");
    }

    private static Entity[] ResolveEntityRefs(MapSession session, string[] refs, string context)
    {
        var entities = new Entity[refs.Length];
        for (int i = 0; i < refs.Length; i++)
        {
            entities[i] = ResolveEntityRef(session, refs[i], $"{context}[{i}]");
        }

        return entities;
    }

    private static Entity ResolveEntityRef(MapSession session, string entityRef, string context)
    {
        string value = RequireNonEmpty(entityRef, context);
        int separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' {context} must use 'player:<id>', 'team:<id>', or 'entity:<instanceId>'.");
        }

        string kind = value[..separator];
        string payload = value[(separator + 1)..];
        return kind switch
        {
            "player" => ResolvePlayer(session, ParseInt(payload, context)),
            "team" => ResolveTeam(session, ParseInt(payload, context)),
            "entity" => ResolveInstance(session, payload, context),
            _ => throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' {context} kind '{kind}' is invalid. Expected player, team, or entity.")
        };
    }

    private static Entity ResolvePlayer(MapSession session, int playerId)
    {
        if (!session.PlayerEntityLookup.TryGet(playerId, out Entity entity))
        {
            throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' metadata.{MetadataSectionKey} references unresolved player:{playerId}.");
        }

        return entity;
    }

    private static Entity ResolveTeam(MapSession session, int teamId)
    {
        if (!session.TeamEntityLookup.TryGet(teamId, out Entity entity))
        {
            throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' metadata.{MetadataSectionKey} references unresolved team:{teamId}.");
        }

        return entity;
    }

    private static Entity ResolveInstance(MapSession session, string instanceId, string context)
    {
        string normalized = RequireNonEmpty(instanceId, context);
        if (!session.EntityIndex.TryGet(normalized, out Entity entity))
        {
            throw new InvalidOperationException(
                $"Map '{session.MapId.Value}' {context} references unresolved entity InstanceId '{normalized}'.");
        }

        return entity;
    }

    private static KnowledgeDisclosureRecord CreateRecord(
        KnowledgePresence presence,
        KnowledgePositionAccess position,
        Entity source,
        in KnowledgeIdMask256 attributeMask,
        in KnowledgeIdMask256 relationshipMask,
        in KnowledgeIdMask256 tagMask,
        int observedTick,
        int expiryTick,
        int confidencePermille)
    {
        return new KnowledgeDisclosureRecord(
            presence,
            position,
            attributeMask,
            relationshipMask,
            tagMask,
            source,
            observedTick <= 0 ? 1 : observedTick,
            expiryTick,
            confidencePermille <= 0 ? 1000 : confidencePermille,
            revision: 0);
    }

    private static KnowledgeIdMask256 BuildMask(
        int[] ids,
        string[] names,
        Func<string, int> resolveName,
        string context)
    {
        KnowledgeIdMask256 mask = KnowledgeIdMask256.Empty;
        for (int i = 0; i < ids.Length; i++)
        {
            mask = mask.WithId(ids[i]);
        }

        for (int i = 0; i < names.Length; i++)
        {
            int resolved = resolveName(RequireNonEmpty(names[i], $"{context}[{i}]"));
            mask = mask.WithId(resolved);
        }

        return mask;
    }

    private static int ResolveAttributeId(string attributeName)
    {
        int attributeId = AttributeRegistry.GetId(attributeName);
        if (attributeId == AttributeRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                $"Knowledge showcase attribute '{attributeName}' is not registered. Author the attribute in templates or use attributeIds.");
        }

        return attributeId;
    }

    private static int ResolveTagId(string tagName)
    {
        int tagId = TagRegistry.GetId(tagName);
        if (tagId == TagRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                $"Knowledge showcase tag '{tagName}' is not registered. Author the tag before metadata.{MetadataSectionKey} resolves it.");
        }

        return tagId;
    }

    private static string RequireNonEmpty(string? value, string context)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{context} requires a non-empty value.");
        }

        string trimmed = value.Trim();
        if (!string.Equals(trimmed, value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{context} must be trimmed.");
        }

        return trimmed;
    }

    private static int RequirePositive(int value, string context)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"{context} must be positive.");
        }

        return value;
    }

    private static int ParseInt(string value, string context)
    {
        if (!int.TryParse(value, out int parsed))
        {
            throw new InvalidOperationException($"{context} must contain a valid integer id.");
        }

        return parsed;
    }

    private sealed class ParticipantViewKnowledgeMetadata
    {
        public KnowledgeProjectionSpec[] Projections { get; set; } = Array.Empty<KnowledgeProjectionSpec>();
        public KnowledgeRelationSpec[] Relations { get; set; } = Array.Empty<KnowledgeRelationSpec>();
        public KnowledgeCollectionSpec[] Collections { get; set; } = Array.Empty<KnowledgeCollectionSpec>();
        public KnowledgeGrantSpec[] Grants { get; set; } = Array.Empty<KnowledgeGrantSpec>();
    }

    private sealed class KnowledgeProjectionSpec
    {
        public string[] Viewers { get; set; } = Array.Empty<string>();
        public KnowledgeTargetSelector Targets { get; set; } = new();
        public KnowledgePresence Presence { get; set; }
        public KnowledgePositionAccess Position { get; set; }
        public string? Source { get; set; }
        public int[] AttributeIds { get; set; } = Array.Empty<int>();
        public string[] Attributes { get; set; } = Array.Empty<string>();
        public int[] RelationshipTypeIds { get; set; } = Array.Empty<int>();
        public string[] RelationshipTypes { get; set; } = Array.Empty<string>();
        public int[] TagIds { get; set; } = Array.Empty<int>();
        public string[] Tags { get; set; } = Array.Empty<string>();
        public int ObservedTick { get; set; } = 1;
        public int ExpiryTick { get; set; }
        public int ConfidencePermille { get; set; } = 1000;
    }

    private sealed class KnowledgeTargetSelector
    {
        public string Kind { get; set; } = string.Empty;
        public int PlayerId { get; set; }
        public int TeamId { get; set; }
        public string[] Entities { get; set; } = Array.Empty<string>();
    }

    private sealed class KnowledgeRelationSpec
    {
        public string[] Viewers { get; set; } = Array.Empty<string>();
        public string Target { get; set; } = string.Empty;
        public string RelationshipType { get; set; } = string.Empty;
    }

    private sealed class KnowledgeCollectionSpec
    {
        public string Owner { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string[] Entities { get; set; } = Array.Empty<string>();
        public string? Title { get; set; }
        public string? Summary { get; set; }
    }

    private sealed class KnowledgeGrantSpec
    {
        public string RelationshipType { get; set; } = string.Empty;
        public string CollectionKey { get; set; } = string.Empty;
        public KnowledgePresence Presence { get; set; }
        public KnowledgePositionAccess Position { get; set; }
        public int[] AttributeIds { get; set; } = Array.Empty<int>();
        public string[] Attributes { get; set; } = Array.Empty<string>();
        public int[] RelationshipTypeIds { get; set; } = Array.Empty<int>();
        public string[] RelationshipTypes { get; set; } = Array.Empty<string>();
        public int[] TagIds { get; set; } = Array.Empty<int>();
        public string[] Tags { get; set; } = Array.Empty<string>();
        public int ObservedTick { get; set; } = 1;
        public int ExpiryTick { get; set; }
        public int ConfidencePermille { get; set; } = 1000;
    }
}
