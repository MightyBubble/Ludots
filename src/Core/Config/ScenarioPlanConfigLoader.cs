using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Config
{
    /// <summary>
    /// Loads ScenarioPlan entries through ConfigCatalog ArrayById merge into <see cref="DataRegistry{T}"/>.
    /// </summary>
    public sealed class ScenarioPlanConfigLoader
    {
        public const string RelativePath = "Scenarios/scenario_plans.json";

        private static readonly string[] AllowedTopLevelFields =
        {
            "id",
            "mapId",
            "seed",
            "layout",
            "placements",
            "teams",
            "players",
            "initialRelationships",
        };

        private static readonly string[] ForbiddenTopLevelFields =
        {
            "map",
            "terrain",
            "boards",
            "entities",
            "nav",
            "navigation",
            "pathing",
            "collision",
            "structureCollisionAsset",
            "defaultCamera",
            "triggerTypes",
            "startupLocalPlayerId",
            "metadata",
            "ruleset",
            "profiles",
            "templates",
            "performers",
            "entityTemplates",
            "performerDefinitions",
            "agentProfiles",
            "abilityDefinitions",
            "effectDefinitions",
            "relationshipTypes",
        };

        private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

        private readonly ConfigPipeline _pipeline;

        public ScenarioPlanConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public DataRegistry<ScenarioPlan> Load(
            ConfigCatalog catalog,
            ConfigConflictReport? report = null,
            string relativePath = RelativePath)
        {
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                catalog,
                relativePath,
                ConfigMergePolicy.ArrayById,
                defaultIdField: "id");

            IReadOnlyList<MergedConfigEntry> merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var registry = new DataRegistry<ScenarioPlan>(_pipeline);

            for (int i = 0; i < merged.Count; i++)
            {
                MergedConfigEntry item = merged[i];
                ValidateRawTopLevel(item.Node, item.Id, relativePath);
                ValidateRawValueRequirements(item.Node, item.Id, relativePath);

                ScenarioPlan plan = item.Node.Deserialize<ScenarioPlan>(SerializerOptions)
                    ?? throw new InvalidOperationException(
                        $"Failed to deserialize ScenarioPlan '{item.Id}' from {relativePath}.");

                ValidatePlan(plan, item.Id, relativePath);
                registry.Register(plan);
            }

            return registry;
        }

        private static void ValidateRawTopLevel(JsonObject obj, string catalogId, string relativePath)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                string key = property.Key;
                if (IsForbiddenTopLevelField(key))
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{catalogId}' in {relativePath} declares forbidden field '{key}'. " +
                        "Map identity fields (terrain/boards/nav/pathing/collision/structureCollisionAsset) belong to Map; " +
                        "template/performer definition fields belong to Ruleset/Profile, not ScenarioPlan.");
                }

                if (!IsAllowedTopLevelField(key))
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{catalogId}' in {relativePath} contains unknown top-level field '{key}'.");
                }
            }
        }

        private static void ValidateRawValueRequirements(JsonObject obj, string catalogId, string relativePath)
        {
            if (obj["placements"] is not JsonArray placements)
            {
                return;
            }

            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i] is not JsonObject placement)
                {
                    continue;
                }

                ValidateRawPerformerParamOverrides(
                    placement["performerParamOverrides"],
                    $"ScenarioPlan '{catalogId}' placements[{i}].performerParamOverrides",
                    relativePath);
            }
        }

        private static void ValidateRawPerformerParamOverrides(
            JsonNode? node,
            string context,
            string relativePath)
        {
            if (node == null)
            {
                return;
            }

            if (node is not JsonArray overrides)
            {
                throw new InvalidOperationException($"{context} in {relativePath} must be an array.");
            }

            for (int i = 0; i < overrides.Count; i++)
            {
                string itemContext = $"{context}[{i}]";
                JsonObject obj = overrides[i] as JsonObject
                    ?? throw new InvalidOperationException($"{itemContext} in {relativePath} requires an object payload.");

                if (obj.ContainsKey("value"))
                {
                    throw new InvalidOperationException(
                        $"{itemContext} in {relativePath} uses removed field 'value'. Use lane-specific fields.");
                }

                string laneText = RequireRawString(obj["lane"], $"{itemContext}.lane", relativePath);
                switch (laneText)
                {
                    case nameof(ParamLane.Float):
                        RequireRawFloat(obj["floatValue"], $"{itemContext}.floatValue", relativePath);
                        RejectRawField(obj, "intValue", itemContext, nameof(ParamLane.Float), relativePath);
                        RejectRawField(obj, "vectorValue", itemContext, nameof(ParamLane.Float), relativePath);
                        break;
                    case nameof(ParamLane.Int):
                        RequireRawInt(obj["intValue"], $"{itemContext}.intValue", relativePath);
                        RejectRawField(obj, "floatValue", itemContext, nameof(ParamLane.Int), relativePath);
                        RejectRawField(obj, "vectorValue", itemContext, nameof(ParamLane.Int), relativePath);
                        break;
                    case nameof(ParamLane.Vector):
                        RequireRawVector4(obj["vectorValue"], $"{itemContext}.vectorValue", relativePath);
                        RejectRawField(obj, "floatValue", itemContext, nameof(ParamLane.Vector), relativePath);
                        RejectRawField(obj, "intValue", itemContext, nameof(ParamLane.Vector), relativePath);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"{itemContext}.lane in {relativePath} has unsupported param lane '{laneText}'.");
                }
            }
        }

        private static void ValidatePlan(ScenarioPlan plan, string catalogId, string relativePath)
        {
            string id = RequireCanonicalId(plan.Id, $"ScenarioPlan id in {relativePath}");
            if (!string.Equals(id, catalogId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ScenarioPlan id mismatch in {relativePath}: catalog entry '{catalogId}' vs item id '{id}'.");
            }

            plan.MapId = RequireCanonicalId(plan.MapId, $"ScenarioPlan '{id}' mapId in {relativePath}");

            if (plan.Placements == null)
            {
                plan.Placements = new List<ScenarioPlanPlacement>();
            }

            var placementIds = new HashSet<string>(StringComparer.Ordinal);
            var placementsById = new Dictionary<string, ScenarioPlanPlacement>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Placements.Count; i++)
            {
                ScenarioPlanPlacement? placement = plan.Placements[i]
                    ?? throw new InvalidOperationException(
                        $"ScenarioPlan '{id}' in {relativePath}: placements[{i}] requires an object payload.");

                placement.Id = RequireCanonicalId(
                    placement.Id,
                    $"ScenarioPlan '{id}' placements[{i}].id in {relativePath}");
                placement.TemplateId = RequireCanonicalId(
                    placement.TemplateId,
                    $"ScenarioPlan '{id}' placements[{i}].templateId in {relativePath}");

                if (!placementIds.Add(placement.Id))
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{id}' in {relativePath}: duplicate placement id '{placement.Id}'.");
                }

                placementsById.Add(placement.Id, placement);

                if (placement.TeamId.HasValue)
                {
                    RequirePositive(
                        placement.TeamId.Value,
                        $"ScenarioPlan '{id}' placements[{i}].teamId in {relativePath}");
                }

                if (placement.PlayerOwnerId.HasValue)
                {
                    RequirePositive(
                        placement.PlayerOwnerId.Value,
                        $"ScenarioPlan '{id}' placements[{i}].playerOwnerId in {relativePath}");
                }

                ValidateComponentPatches(placement, id, i, relativePath);
                ValidatePerformerParamOverrides(placement, id, i, relativePath);
            }

            if (plan.Teams == null)
            {
                plan.Teams = new List<ScenarioPlanTeamOwnership>();
            }

            var teamIds = new HashSet<int>();
            for (int i = 0; i < plan.Teams.Count; i++)
            {
                ScenarioPlanTeamOwnership? team = plan.Teams[i]
                    ?? throw new InvalidOperationException(
                        $"ScenarioPlan '{id}' in {relativePath}: teams[{i}] requires an object payload.");

                RequirePositive(team.TeamId, $"ScenarioPlan '{id}' teams[{i}].teamId in {relativePath}");
                if (!teamIds.Add(team.TeamId))
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{id}' in {relativePath}: duplicate team id {team.TeamId}.");
                }

                team.RepresentativePlacementId = RequireCanonicalId(
                    team.RepresentativePlacementId,
                    $"ScenarioPlan '{id}' teams[{i}].representativePlacementId in {relativePath}");

                ScenarioPlanPlacement representative = RequirePlacement(
                    placementsById,
                    team.RepresentativePlacementId,
                    $"ScenarioPlan '{id}' teams[{i}].representativePlacementId in {relativePath}");
                if (representative.TeamId.HasValue && representative.TeamId.Value != team.TeamId)
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{id}' in {relativePath}: team {team.TeamId} representative placement '{team.RepresentativePlacementId}' declares teamId {representative.TeamId.Value}.");
                }
            }

            if (plan.Players == null)
            {
                plan.Players = new List<ScenarioPlanPlayerOwnership>();
            }

            var playerIds = new HashSet<int>();
            for (int i = 0; i < plan.Players.Count; i++)
            {
                ScenarioPlanPlayerOwnership? player = plan.Players[i]
                    ?? throw new InvalidOperationException(
                        $"ScenarioPlan '{id}' in {relativePath}: players[{i}] requires an object payload.");

                RequirePositive(player.PlayerId, $"ScenarioPlan '{id}' players[{i}].playerId in {relativePath}");
                RequirePositive(player.TeamId, $"ScenarioPlan '{id}' players[{i}].teamId in {relativePath}");
                if (!playerIds.Add(player.PlayerId))
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{id}' in {relativePath}: duplicate player id {player.PlayerId}.");
                }

                if (!teamIds.Contains(player.TeamId))
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{id}' in {relativePath}: player {player.PlayerId} references unknown team id {player.TeamId}.");
                }

                player.RepresentativePlacementId = RequireCanonicalId(
                    player.RepresentativePlacementId,
                    $"ScenarioPlan '{id}' players[{i}].representativePlacementId in {relativePath}");

                ScenarioPlanPlacement representative = RequirePlacement(
                    placementsById,
                    player.RepresentativePlacementId,
                    $"ScenarioPlan '{id}' players[{i}].representativePlacementId in {relativePath}");
                if (representative.PlayerOwnerId.HasValue && representative.PlayerOwnerId.Value != player.PlayerId)
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{id}' in {relativePath}: player {player.PlayerId} representative placement '{player.RepresentativePlacementId}' declares playerOwnerId {representative.PlayerOwnerId.Value}.");
                }

                if (representative.TeamId.HasValue && representative.TeamId.Value != player.TeamId)
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{id}' in {relativePath}: player {player.PlayerId} representative placement '{player.RepresentativePlacementId}' declares teamId {representative.TeamId.Value}.");
                }
            }

            ValidatePlacementOwnershipReferences(plan, teamIds, playerIds, id, relativePath);
            ValidateInitialRelationships(plan.InitialRelationships, teamIds, playerIds, id, relativePath);
        }

        private static void ValidateComponentPatches(
            ScenarioPlanPlacement placement,
            string planId,
            int placementIndex,
            string relativePath)
        {
            if (placement.ComponentPatches == null)
            {
                placement.ComponentPatches = new List<ScenarioPlanComponentPatch>();
                return;
            }

            for (int i = 0; i < placement.ComponentPatches.Count; i++)
            {
                ScenarioPlanComponentPatch? patch = placement.ComponentPatches[i]
                    ?? throw new InvalidOperationException(
                        $"ScenarioPlan '{planId}' placements[{placementIndex}].componentPatches[{i}] in {relativePath} requires an object payload.");

                patch.ComponentName = RequireCanonicalId(
                    patch.ComponentName,
                    $"ScenarioPlan '{planId}' placements[{placementIndex}].componentPatches[{i}].componentName in {relativePath}");

                if (patch.Data == null)
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{planId}' placements[{placementIndex}].componentPatches[{i}] in {relativePath} requires non-null data.");
                }
            }
        }

        private static void ValidatePerformerParamOverrides(
            ScenarioPlanPlacement placement,
            string planId,
            int placementIndex,
            string relativePath)
        {
            if (placement.PerformerParamOverrides == null)
            {
                placement.PerformerParamOverrides = new List<ParamOverrideData>();
                return;
            }

            for (int i = 0; i < placement.PerformerParamOverrides.Count; i++)
            {
                ParamOverrideData? item = placement.PerformerParamOverrides[i]
                    ?? throw new InvalidOperationException(
                        $"ScenarioPlan '{planId}' placements[{placementIndex}].performerParamOverrides[{i}] in {relativePath} requires an object payload.");

                item.ParamKey = RequireCanonicalId(
                    item.ParamKey,
                    $"ScenarioPlan '{planId}' placements[{placementIndex}].performerParamOverrides[{i}].paramKey in {relativePath}");

                if (!item.Lane.HasValue)
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{planId}' placements[{placementIndex}].performerParamOverrides[{i}].lane in {relativePath} requires an explicit param lane.");
                }
            }
        }

        private static void ValidatePlacementOwnershipReferences(
            ScenarioPlan plan,
            HashSet<int> teamIds,
            HashSet<int> playerIds,
            string planId,
            string relativePath)
        {
            for (int i = 0; i < plan.Placements.Count; i++)
            {
                ScenarioPlanPlacement placement = plan.Placements[i];
                if (placement.TeamId.HasValue && !teamIds.Contains(placement.TeamId.Value))
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{planId}' placements[{i}].teamId in {relativePath} references unknown team id {placement.TeamId.Value}.");
                }

                if (placement.PlayerOwnerId.HasValue && !playerIds.Contains(placement.PlayerOwnerId.Value))
                {
                    throw new InvalidOperationException(
                        $"ScenarioPlan '{planId}' placements[{i}].playerOwnerId in {relativePath} references unknown player id {placement.PlayerOwnerId.Value}.");
                }
            }
        }

        private static void ValidateInitialRelationships(
            ParticipantRelationshipConfig? relationships,
            HashSet<int> teamIds,
            HashSet<int> playerIds,
            string planId,
            string relativePath)
        {
            if (relationships == null)
            {
                return;
            }

            relationships.Teams ??= new List<TeamRelationshipBindingData>();
            relationships.Players ??= new List<PlayerRelationshipBindingData>();
            relationships.PlayerTeams ??= new List<PlayerTeamRelationshipBindingData>();

            for (int i = 0; i < relationships.Teams.Count; i++)
            {
                TeamRelationshipBindingData? binding = relationships.Teams[i]
                    ?? throw new InvalidOperationException(
                        $"ScenarioPlan '{planId}' initialRelationships.teams[{i}] in {relativePath} requires an object payload.");

                binding.TypeId = RequireCanonicalId(
                    binding.TypeId,
                    $"ScenarioPlan '{planId}' initialRelationships.teams[{i}].typeId in {relativePath}");
                RequireKnownTeamId(teamIds, binding.TeamA, $"ScenarioPlan '{planId}' initialRelationships.teams[{i}].teamA in {relativePath}");
                RequireKnownTeamId(teamIds, binding.TeamB, $"ScenarioPlan '{planId}' initialRelationships.teams[{i}].teamB in {relativePath}");
            }

            for (int i = 0; i < relationships.Players.Count; i++)
            {
                PlayerRelationshipBindingData? binding = relationships.Players[i]
                    ?? throw new InvalidOperationException(
                        $"ScenarioPlan '{planId}' initialRelationships.players[{i}] in {relativePath} requires an object payload.");

                binding.TypeId = RequireCanonicalId(
                    binding.TypeId,
                    $"ScenarioPlan '{planId}' initialRelationships.players[{i}].typeId in {relativePath}");
                RequireKnownPlayerId(playerIds, binding.PlayerA, $"ScenarioPlan '{planId}' initialRelationships.players[{i}].playerA in {relativePath}");
                RequireKnownPlayerId(playerIds, binding.PlayerB, $"ScenarioPlan '{planId}' initialRelationships.players[{i}].playerB in {relativePath}");
            }

            for (int i = 0; i < relationships.PlayerTeams.Count; i++)
            {
                PlayerTeamRelationshipBindingData? binding = relationships.PlayerTeams[i]
                    ?? throw new InvalidOperationException(
                        $"ScenarioPlan '{planId}' initialRelationships.playerTeams[{i}] in {relativePath} requires an object payload.");

                binding.TypeId = RequireCanonicalId(
                    binding.TypeId,
                    $"ScenarioPlan '{planId}' initialRelationships.playerTeams[{i}].typeId in {relativePath}");
                RequireKnownPlayerId(playerIds, binding.PlayerId, $"ScenarioPlan '{planId}' initialRelationships.playerTeams[{i}].playerId in {relativePath}");
                RequireKnownTeamId(teamIds, binding.TeamId, $"ScenarioPlan '{planId}' initialRelationships.playerTeams[{i}].teamId in {relativePath}");
            }
        }

        private static ScenarioPlanPlacement RequirePlacement(
            Dictionary<string, ScenarioPlanPlacement> placementsById,
            string placementId,
            string context)
        {
            if (!placementsById.TryGetValue(placementId, out ScenarioPlanPlacement? placement))
            {
                throw new InvalidOperationException($"{context} references unknown placement id '{placementId}'.");
            }

            return placement;
        }

        private static void RequireKnownTeamId(HashSet<int> teamIds, int teamId, string context)
        {
            RequirePositive(teamId, context);
            if (!teamIds.Contains(teamId))
            {
                throw new InvalidOperationException($"{context} references unknown team id {teamId}.");
            }
        }

        private static void RequireKnownPlayerId(HashSet<int> playerIds, int playerId, string context)
        {
            RequirePositive(playerId, context);
            if (!playerIds.Contains(playerId))
            {
                throw new InvalidOperationException($"{context} references unknown player id {playerId}.");
            }
        }

        private static void RequirePositive(int value, string context)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException($"{context} must be a positive id.");
            }
        }

        private static string RequireRawString(JsonNode? node, string context, string relativePath)
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} in {relativePath} requires a non-empty string value.");
            }

            if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} in {relativePath} must not include leading or trailing whitespace.");
            }

            return text;
        }

        private static void RequireRawFloat(JsonNode? node, string context, string relativePath)
        {
            if (node is not JsonValue value || !value.TryGetValue<float>(out _))
            {
                throw new InvalidOperationException($"{context} in {relativePath} requires a numeric value.");
            }
        }

        private static void RequireRawInt(JsonNode? node, string context, string relativePath)
        {
            if (node is not JsonValue value || !value.TryGetValue<int>(out _))
            {
                throw new InvalidOperationException($"{context} in {relativePath} requires an integer value.");
            }
        }

        private static void RequireRawVector4(JsonNode? node, string context, string relativePath)
        {
            if (node is not JsonArray values || values.Count != 4)
            {
                throw new InvalidOperationException($"{context} in {relativePath} requires four numeric values.");
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is not JsonValue value || !value.TryGetValue<float>(out _))
                {
                    throw new InvalidOperationException($"{context}[{i}] in {relativePath} requires a numeric value.");
                }
            }
        }

        private static void RejectRawField(
            JsonObject obj,
            string fieldName,
            string context,
            string lane,
            string relativePath)
        {
            if (obj.ContainsKey(fieldName))
            {
                throw new InvalidOperationException(
                    $"{context} in {relativePath} lane '{lane}' must not declare '{fieldName}'.");
            }
        }

        private static string RequireCanonicalId(string? value, string context)
        {
            if (value == null)
            {
                throw new InvalidOperationException($"{context} is required.");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} must not be blank.");
            }

            string trimmed = value.Trim();
            if (!string.Equals(value, trimmed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} '{value}' must not include leading or trailing whitespace.");
            }

            return value;
        }

        private static bool IsAllowedTopLevelField(string key)
        {
            for (int i = 0; i < AllowedTopLevelFields.Length; i++)
            {
                if (string.Equals(key, AllowedTopLevelFields[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsForbiddenTopLevelField(string key)
        {
            for (int i = 0; i < ForbiddenTopLevelFields.Length; i++)
            {
                if (string.Equals(key, ForbiddenTopLevelFields[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static JsonSerializerOptions CreateSerializerOptions()
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
            options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            return options;
        }
    }
}
