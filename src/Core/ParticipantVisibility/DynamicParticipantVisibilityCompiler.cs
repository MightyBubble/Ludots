using System;
using Arch.Core;
using Arch.Core.Utils;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using CoreComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Core.ParticipantVisibility
{
    public static class DynamicParticipantVisibilityCompiler
    {
        public static DynamicParticipantVisibilityBinding[] Compile(
            MapSession session,
            DynamicParticipantQuerySpec[] specs,
            RelationshipTypeRegistry relationshipTypes)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(specs);
            ArgumentNullException.ThrowIfNull(relationshipTypes);

            var bindings = new DynamicParticipantVisibilityBinding[specs.Length];
            int count = 0;
            for (int i = 0; i < specs.Length; i++)
            {
                bindings[count++] = CompileOne(
                    session,
                    specs[i],
                    relationshipTypes,
                    $"metadata dynamic participant query[{i}]");
            }

            if (count == bindings.Length)
            {
                return bindings;
            }

            Array.Resize(ref bindings, count);
            return bindings;
        }

        private static DynamicParticipantVisibilityBinding CompileOne(
            MapSession session,
            DynamicParticipantQuerySpec spec,
            RelationshipTypeRegistry relationshipTypes,
            string context)
        {
            ArgumentNullException.ThrowIfNull(spec);
            Entity viewer = ResolveViewer(session, spec.ViewerKind, spec.ViewerRef, $"{context}.viewerRef");
            Entity source = ResolveSource(session, spec.SourceKind, spec.SourceRef, viewer, $"{context}.sourceRef");
            ComponentType[] all = ResolveComponentTypes(spec.Query.AllComponents, $"{context}.query.allComponents");
            ComponentType[] none = ResolveComponentTypes(spec.Query.NoneComponents, $"{context}.query.noneComponents");
            int requiredTagId = string.IsNullOrWhiteSpace(spec.RequiredTag)
                ? 0
                : ResolveRequiredTag(spec.RequiredTag, $"{context}.requiredTag");
            KnowledgeIdMask256 attributeMask = spec.AttributeMask.Union(BuildMask(
                spec.AttributeIds,
                spec.Attributes,
                ResolveAttributeId,
                $"{context}.attributes"));
            KnowledgeIdMask256 relationshipMask = spec.RelationshipTypeMask.Union(BuildMask(
                spec.RelationshipTypeIds,
                spec.RelationshipTypes,
                relationshipTypes.GetId,
                $"{context}.relationshipTypes"));
            KnowledgeIdMask256 tagMask = spec.TagMask.Union(BuildMask(
                spec.TagIds,
                spec.Tags,
                ResolveTagId,
                $"{context}.tags"));

            var descriptor = EntityCollectionDescriptor.Create(
                RequireNonEmpty(spec.CollectionKey, $"{context}.collectionKey"),
                EntityCollectionSourceKind.DynamicParticipant,
                spec.CollectionRole,
                contextEntity: viewer,
                primaryEntity: source,
                title: spec.Title,
                summary: spec.Summary);

            return DynamicParticipantVisibilityBinding.Create(
                viewer,
                source,
                session.MapId,
                descriptor,
                all,
                none,
                spec.Flags,
                spec.SourceKind,
                requiredTagId,
                spec.Presence,
                spec.Position,
                attributeMask,
                relationshipMask,
                tagMask,
                spec.ExpiryTick,
                spec.ConfidencePermille,
                spec.OwnerMatchPolicy);
        }

        private static Entity ResolveViewer(
            MapSession session,
            DynamicParticipantViewerKind kind,
            string viewerRef,
            string context)
        {
            string value = RequireNonEmpty(viewerRef, context);
            return kind switch
            {
                DynamicParticipantViewerKind.Player => ResolvePlayer(session, value, context),
                DynamicParticipantViewerKind.Team => ResolveTeam(session, value, context),
                DynamicParticipantViewerKind.Map => ResolveEntityRef(session, value, context),
                _ => throw new InvalidOperationException($"{context} uses unsupported dynamic participant viewer kind '{kind}'.")
            };
        }

        private static Entity ResolveSource(
            MapSession session,
            DynamicParticipantSourceKind kind,
            string sourceRef,
            Entity viewer,
            string context)
        {
            if (kind == DynamicParticipantSourceKind.Viewer ||
                string.IsNullOrWhiteSpace(sourceRef) ||
                string.Equals(sourceRef, "viewer", StringComparison.Ordinal))
            {
                return viewer;
            }

            if (kind == DynamicParticipantSourceKind.Target)
            {
                return Entity.Null;
            }

            return ResolveEntityRef(session, sourceRef, context);
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
                "player" => ResolvePlayer(session, payload, context),
                "team" => ResolveTeam(session, payload, context),
                "entity" => ResolveInstance(session, payload, context),
                _ => throw new InvalidOperationException(
                    $"Map '{session.MapId.Value}' {context} kind '{kind}' is invalid. Expected player, team, or entity.")
            };
        }

        private static Entity ResolvePlayer(MapSession session, string value, string context)
        {
            int playerId = ParsePositiveInt(value, context);
            if (!session.PlayerEntityLookup.TryGet(playerId, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"Map '{session.MapId.Value}' {context} references unresolved player:{playerId}.");
            }

            return entity;
        }

        private static Entity ResolveTeam(MapSession session, string value, string context)
        {
            int teamId = ParsePositiveInt(value, context);
            if (!session.TeamEntityLookup.TryGet(teamId, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"Map '{session.MapId.Value}' {context} references unresolved team:{teamId}.");
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

        private static ComponentType[] ResolveComponentTypes(string[] names, string context)
        {
            if (names == null || names.Length == 0)
            {
                return Array.Empty<ComponentType>();
            }

            var types = new ComponentType[names.Length];
            int count = 0;
            for (int i = 0; i < names.Length; i++)
            {
                string name = RequireNonEmpty(names[i], $"{context}[{i}]");
                if (!CoreComponentRegistry.TryGetComponentType(name, out ComponentType componentType))
                {
                    throw new InvalidOperationException($"{context}[{i}] references unknown component '{name}'.");
                }

                bool duplicate = false;
                for (int j = 0; j < count; j++)
                {
                    if (types[j].Equals(componentType))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    types[count++] = componentType;
                }
            }

            if (count == types.Length)
            {
                return types;
            }

            Array.Resize(ref types, count);
            return types;
        }

        private static int ResolveRequiredTag(string name, string context)
        {
            int tagId = TagRegistry.GetId(RequireNonEmpty(name, context));
            if (tagId == TagRegistry.InvalidId)
            {
                throw new InvalidOperationException($"{context} references unknown gameplay tag '{name}'.");
            }

            return tagId;
        }

        private static int ResolveAttributeId(string name)
        {
            int attributeId = AttributeRegistry.GetId(name);
            if (attributeId == AttributeRegistry.InvalidId)
            {
                throw new InvalidOperationException(
                    $"Dynamic participant visibility attribute '{name}' is not registered.");
            }

            return attributeId;
        }

        private static int ResolveTagId(string name)
        {
            int tagId = TagRegistry.GetId(name);
            if (tagId == TagRegistry.InvalidId)
            {
                throw new InvalidOperationException(
                    $"Dynamic participant visibility tag '{name}' is not registered.");
            }

            return tagId;
        }

        private static KnowledgeIdMask256 BuildMask(
            int[]? ids,
            string[]? names,
            Func<string, int> resolveName,
            string context)
        {
            KnowledgeIdMask256 mask = KnowledgeIdMask256.Empty;
            if (ids != null)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    mask = mask.WithId(ids[i]);
                }
            }

            if (names != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    int resolved = resolveName(RequireNonEmpty(names[i], $"{context}[{i}]"));
                    mask = mask.WithId(resolved);
                }
            }

            return mask;
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

        private static int ParsePositiveInt(string value, string context)
        {
            if (!int.TryParse(value, out int parsed) || parsed <= 0)
            {
                throw new InvalidOperationException($"{context} must contain a positive integer id.");
            }

            return parsed;
        }
    }
}
