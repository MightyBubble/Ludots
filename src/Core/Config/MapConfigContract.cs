using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config
{
    public static class MapConfigContract
    {
        public static void RejectUnsupportedKeys(JsonNode fragment, string context)
        {
            if (fragment is not JsonObject root)
            {
                return;
            }

            RejectLegacyKey(root, "WidthInTiles", "widthInMacroTiles", context);
            RejectLegacyKey(root, "HeightInTiles", "heightInMacroTiles", context);
            RejectEntityPositionKeys(root, context);

            if (!TryGetPropertyCaseInsensitive(root, "boards", out JsonNode boardsNode) ||
                boardsNode is not JsonArray boards)
            {
                return;
            }

            for (int i = 0; i < boards.Count; i++)
            {
                if (boards[i] is not JsonObject board)
                {
                    continue;
                }

                RejectLegacyKey(board, "WidthInTiles", "widthInMacroTiles", $"{context}.boards[{i}]");
                RejectLegacyKey(board, "HeightInTiles", "heightInMacroTiles", $"{context}.boards[{i}]");
            }
        }

        public static void ValidateMerged(MapConfig config, string context)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            HashSet<string> entityIds = ValidateEntityInstanceIds(config, context);
            HashSet<int> teamIds = ValidateTeamIds(config, context, entityIds);
            HashSet<int> playerIds = ValidatePlayerIds(config, context, entityIds, teamIds);
            ValidateParticipantRelationships(config, context, teamIds, playerIds);
        }

        private static HashSet<string> ValidateEntityInstanceIds(MapConfig config, string context)
        {
            if (config.Entities == null)
            {
                throw new InvalidOperationException($"{context} requires an explicit entities collection.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Entities.Count; i++)
            {
                EntitySpawnData entity = config.Entities[i]
                    ?? throw new InvalidOperationException($"{context} Entities[{i}] requires an object payload.");
                string instanceId = entity.InstanceId;
                if (instanceId == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(instanceId))
                {
                    throw new InvalidOperationException($"{context} Entities[{i}].InstanceId requires a non-empty value when authored.");
                }

                string trimmed = instanceId.Trim();
                if (!string.Equals(instanceId, trimmed, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{context} Entities[{i}].InstanceId '{instanceId}' must be trimmed.");
                }

                if (!ids.Add(trimmed))
                {
                    throw new InvalidOperationException($"{context} contains duplicate entity InstanceId '{trimmed}'.");
                }
            }

            return ids;
        }

        private static HashSet<int> ValidateTeamIds(MapConfig config, string context, HashSet<string> entityIds)
        {
            if (config.Teams == null)
            {
                throw new InvalidOperationException($"{context} requires an explicit teams collection.");
            }

            var ids = new HashSet<int>();
            var representatives = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Teams.Count; i++)
            {
                TeamBindingData team = config.Teams[i]
                    ?? throw new InvalidOperationException($"{context} Teams[{i}] requires an object payload.");
                if (team.TeamId <= 0)
                {
                    throw new InvalidOperationException($"{context} Teams[{i}].TeamId must be a positive id.");
                }

                if (!ids.Add(team.TeamId))
                {
                    throw new InvalidOperationException($"{context} contains duplicate TeamId {team.TeamId}.");
                }

                string representative = RequireCanonicalInstanceId(
                    team.RepresentativeInstanceId,
                    $"{context} Teams[{i}].RepresentativeInstanceId");
                RequireAuthoredEntity(entityIds, representative, $"{context} Teams[{i}].RepresentativeInstanceId");
                if (!representatives.Add(representative))
                {
                    throw new InvalidOperationException($"{context} contains duplicate team representative InstanceId '{representative}'.");
                }
            }

            return ids;
        }

        private static HashSet<int> ValidatePlayerIds(
            MapConfig config,
            string context,
            HashSet<string> entityIds,
            HashSet<int> teamIds)
        {
            if (config.Players == null)
            {
                throw new InvalidOperationException($"{context} requires an explicit players collection.");
            }

            var ids = new HashSet<int>();
            var representatives = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Players.Count; i++)
            {
                PlayerBindingData player = config.Players[i]
                    ?? throw new InvalidOperationException($"{context} Players[{i}] requires an object payload.");
                if (player.PlayerId <= 0)
                {
                    throw new InvalidOperationException($"{context} Players[{i}].PlayerId must be a positive id.");
                }

                if (!ids.Add(player.PlayerId))
                {
                    throw new InvalidOperationException($"{context} contains duplicate PlayerId {player.PlayerId}.");
                }

                if (player.TeamId <= 0)
                {
                    throw new InvalidOperationException($"{context} Players[{i}].TeamId must be a positive id.");
                }

                if (!teamIds.Contains(player.TeamId))
                {
                    throw new InvalidOperationException($"{context} Players[{i}] references unknown TeamId {player.TeamId}.");
                }

                string representative = RequireCanonicalInstanceId(
                    player.RepresentativeInstanceId,
                    $"{context} Players[{i}].RepresentativeInstanceId");
                RequireAuthoredEntity(entityIds, representative, $"{context} Players[{i}].RepresentativeInstanceId");
                if (!representatives.Add(representative))
                {
                    throw new InvalidOperationException($"{context} contains duplicate player representative InstanceId '{representative}'.");
                }
            }

            return ids;
        }

        private static void ValidateParticipantRelationships(
            MapConfig config,
            string context,
            HashSet<int> teamIds,
            HashSet<int> playerIds)
        {
            ParticipantRelationshipConfig relationships = config.ParticipantRelationships;
            if (relationships == null)
            {
                return;
            }

            if (relationships.Teams != null)
            {
                for (int i = 0; i < relationships.Teams.Count; i++)
                {
                    TeamRelationshipBindingData binding = relationships.Teams[i]
                        ?? throw new InvalidOperationException($"{context} ParticipantRelationships.Teams[{i}] requires an object payload.");
                    RequireKnownTeamId(teamIds, binding.TeamA, $"{context} ParticipantRelationships.Teams[{i}].TeamA");
                    RequireKnownTeamId(teamIds, binding.TeamB, $"{context} ParticipantRelationships.Teams[{i}].TeamB");
                    RequireCanonicalId(binding.TypeId, $"{context} ParticipantRelationships.Teams[{i}].TypeId");
                }
            }

            if (relationships.Players != null)
            {
                for (int i = 0; i < relationships.Players.Count; i++)
                {
                    PlayerRelationshipBindingData binding = relationships.Players[i]
                        ?? throw new InvalidOperationException($"{context} ParticipantRelationships.Players[{i}] requires an object payload.");
                    RequireKnownPlayerId(playerIds, binding.PlayerA, $"{context} ParticipantRelationships.Players[{i}].PlayerA");
                    RequireKnownPlayerId(playerIds, binding.PlayerB, $"{context} ParticipantRelationships.Players[{i}].PlayerB");
                    RequireCanonicalId(binding.TypeId, $"{context} ParticipantRelationships.Players[{i}].TypeId");
                }
            }

            if (relationships.PlayerTeams != null)
            {
                for (int i = 0; i < relationships.PlayerTeams.Count; i++)
                {
                    PlayerTeamRelationshipBindingData binding = relationships.PlayerTeams[i]
                        ?? throw new InvalidOperationException($"{context} ParticipantRelationships.PlayerTeams[{i}] requires an object payload.");
                    RequireKnownPlayerId(playerIds, binding.PlayerId, $"{context} ParticipantRelationships.PlayerTeams[{i}].PlayerId");
                    RequireKnownTeamId(teamIds, binding.TeamId, $"{context} ParticipantRelationships.PlayerTeams[{i}].TeamId");
                    RequireCanonicalId(binding.TypeId, $"{context} ParticipantRelationships.PlayerTeams[{i}].TypeId");
                }
            }
        }

        private static void RequireKnownTeamId(HashSet<int> teamIds, int teamId, string context)
        {
            if (teamId <= 0)
            {
                throw new InvalidOperationException($"{context} must be a positive id.");
            }

            if (!teamIds.Contains(teamId))
            {
                throw new InvalidOperationException($"{context} references unknown TeamId {teamId}.");
            }
        }

        private static void RequireKnownPlayerId(HashSet<int> playerIds, int playerId, string context)
        {
            if (playerId <= 0)
            {
                throw new InvalidOperationException($"{context} must be a positive id.");
            }

            if (!playerIds.Contains(playerId))
            {
                throw new InvalidOperationException($"{context} references unknown PlayerId {playerId}.");
            }
        }

        private static void RequireAuthoredEntity(HashSet<string> entityIds, string instanceId, string context)
        {
            if (!entityIds.Contains(instanceId))
            {
                throw new InvalidOperationException($"{context} references unknown entity InstanceId '{instanceId}'.");
            }
        }

        private static string RequireCanonicalInstanceId(string value, string context)
        {
            return RequireCanonicalId(value, context);
        }

        private static string RequireCanonicalId(string value, string context)
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
                throw new InvalidOperationException($"{context} '{value}' must be trimmed.");
            }

            return value;
        }

        private static void RejectEntityPositionKeys(JsonObject root, string context)
        {
            if (!TryGetPropertyCaseInsensitive(root, "entities", out JsonNode entitiesNode) ||
                entitiesNode is not JsonArray entities)
            {
                return;
            }

            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] is not JsonObject entity)
                {
                    continue;
                }

                foreach (KeyValuePair<string, JsonNode> kvp in entity)
                {
                    if (string.Equals(kvp.Key, "position", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Map config '{context}.entities[{i}]' uses unsupported entity key '{kvp.Key}'. " +
                            "Use Overrides.WorldPositionCm as the map placement SSOT.");
                    }
                }
            }
        }

        private static void RejectLegacyKey(JsonObject obj, string legacyName, string replacementName, string context)
        {
            foreach (KeyValuePair<string, JsonNode> kvp in obj)
            {
                if (string.Equals(kvp.Key, legacyName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Map config '{context}' uses legacy key '{kvp.Key}'. Use '{replacementName}' instead.");
                }
            }
        }

        private static bool TryGetPropertyCaseInsensitive(JsonObject obj, string name, out JsonNode node)
        {
            foreach (KeyValuePair<string, JsonNode> kvp in obj)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    node = kvp.Value;
                    return true;
                }
            }

            node = null;
            return false;
        }
    }
}
