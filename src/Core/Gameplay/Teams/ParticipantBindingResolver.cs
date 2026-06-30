using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace Ludots.Core.Gameplay.Teams
{
    public sealed class ParticipantBindingResult
    {
        public ParticipantBindingResult(
            TeamEntityLookup teams,
            PlayerEntityLookup players,
            int localPlayerId,
            Entity localPlayerEntity,
            TeamRelationshipSnapshot? teamRelationships = null)
        {
            Teams = teams ?? throw new ArgumentNullException(nameof(teams));
            Players = players ?? throw new ArgumentNullException(nameof(players));
            LocalPlayerId = localPlayerId;
            LocalPlayerEntity = localPlayerEntity;
            TeamRelationships = teamRelationships;
        }

        public TeamEntityLookup Teams { get; }
        public PlayerEntityLookup Players { get; }
        public int LocalPlayerId { get; }
        public Entity LocalPlayerEntity { get; }
        public TeamRelationshipSnapshot? TeamRelationships { get; }
    }

    public static class ParticipantBindingResolver
    {
        public static ParticipantBindingResult Resolve(
            MapSession session,
            World world,
            MapLoadEntityIndex entityIndex,
            RelationshipRuntime? relationships,
            RelationshipTypeRegistry? relationshipTypes)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(entityIndex);

            MapConfig mapConfig = session.MapConfig ?? throw new InvalidOperationException($"Map session '{session.MapId.Value}' has no MapConfig.");
            string mapId = session.MapId.Value;
            var teamLookup = new TeamEntityLookup();
            var playerLookup = new PlayerEntityLookup();
            var teamIds = new HashSet<int>();
            var playerIds = new HashSet<int>();
            var teamRepresentativeIds = new HashSet<string>(StringComparer.Ordinal);
            var playerRepresentativeIds = new HashSet<string>(StringComparer.Ordinal);

            ValidateCollection(mapConfig.Teams, $"Map '{mapId}' Teams");
            ValidateCollection(mapConfig.Players, $"Map '{mapId}' Players");

            for (int i = 0; i < mapConfig.Teams.Count; i++)
            {
                TeamBindingData binding = mapConfig.Teams[i] ?? throw new InvalidOperationException($"Map '{mapId}' Teams[{i}] requires an object payload.");
                if (binding.TeamId <= 0)
                {
                    throw new InvalidOperationException($"Map '{mapId}' Teams[{i}].TeamId must be positive.");
                }

                if (!teamIds.Add(binding.TeamId))
                {
                    throw new InvalidOperationException($"Map '{mapId}' Teams contains duplicate TeamId {binding.TeamId}.");
                }

                string instanceId = RequireRepresentativeInstanceId(mapId, $"Teams[{i}]", binding.RepresentativeInstanceId);
                if (!teamRepresentativeIds.Add(instanceId))
                {
                    throw new InvalidOperationException($"Map '{mapId}' Teams contains duplicate representative InstanceId '{instanceId}'.");
                }

                Entity entity = entityIndex.GetRequired(mapId, instanceId, $"Teams[{i}]");
                if (!world.IsAlive(entity))
                {
                    throw new InvalidOperationException($"Map '{mapId}' Teams[{i}] resolved dead entity InstanceId '{instanceId}'.");
                }

                Upsert(world, entity, new TeamIdentity { TeamId = binding.TeamId });
                teamLookup.Register(binding.TeamId, entity);
            }

            int localPlayerId = 0;
            Entity localPlayerEntity = Entity.Null;
            for (int i = 0; i < mapConfig.Players.Count; i++)
            {
                PlayerBindingData binding = mapConfig.Players[i] ?? throw new InvalidOperationException($"Map '{mapId}' Players[{i}] requires an object payload.");
                if (binding.PlayerId <= 0)
                {
                    throw new InvalidOperationException($"Map '{mapId}' Players[{i}].PlayerId must be positive.");
                }

                if (!playerIds.Add(binding.PlayerId))
                {
                    throw new InvalidOperationException($"Map '{mapId}' Players contains duplicate PlayerId {binding.PlayerId}.");
                }

                if (binding.TeamId <= 0)
                {
                    throw new InvalidOperationException($"Map '{mapId}' Players[{i}].TeamId must be positive.");
                }

                if (!teamLookup.TryGet(binding.TeamId, out _))
                {
                    throw new InvalidOperationException($"Map '{mapId}' Players[{i}] references unbound TeamId {binding.TeamId}.");
                }

                string instanceId = RequireRepresentativeInstanceId(mapId, $"Players[{i}]", binding.RepresentativeInstanceId);
                if (!playerRepresentativeIds.Add(instanceId))
                {
                    throw new InvalidOperationException($"Map '{mapId}' Players contains duplicate representative InstanceId '{instanceId}'.");
                }

                Entity entity = entityIndex.GetRequired(mapId, instanceId, $"Players[{i}]");
                if (!world.IsAlive(entity))
                {
                    throw new InvalidOperationException($"Map '{mapId}' Players[{i}] resolved dead entity InstanceId '{instanceId}'.");
                }

                Upsert(world, entity, new PlayerIdentity { PlayerId = binding.PlayerId });
                Upsert(world, entity, new PlayerOwner { PlayerId = binding.PlayerId });
                Upsert(world, entity, new Team { Id = binding.TeamId });
                playerLookup.Register(binding.PlayerId, entity);
            }

            MapLaunchContext? launchContext = session.LaunchContext;
            if (launchContext?.HasSelectedPlayer == true)
            {
                int selectedPlayerId = launchContext.SelectedPlayerId;
                if (!playerLookup.TryGet(selectedPlayerId, out Entity selectedEntity))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' launch context SelectedPlayerId {selectedPlayerId} references an unbound player.");
                }

                localPlayerId = selectedPlayerId;
                localPlayerEntity = selectedEntity;
            }

            ResolveRelationships(mapId, mapConfig, teamLookup, playerLookup, relationships, relationshipTypes);

            bool hasParticipantBindings = mapConfig.Teams.Count > 0 || mapConfig.Players.Count > 0;
            return new ParticipantBindingResult(
                teamLookup,
                playerLookup,
                localPlayerId,
                localPlayerEntity,
                hasParticipantBindings ? TeamManager.CaptureSnapshot() : null);
        }

        public static void PublishFocused(
            IDictionary<string, object> globals,
            ParticipantBindingResult participants)
        {
            ArgumentNullException.ThrowIfNull(globals);
            ArgumentNullException.ThrowIfNull(participants);

            PublishTeamLookup(globals, participants.Teams);
            PublishPlayerLookup(globals, participants.Players);
            if (participants.TeamRelationships != null)
            {
                TeamManager.RestoreSnapshot(participants.TeamRelationships);
            }

            if (participants.LocalPlayerId > 0 && participants.LocalPlayerEntity != Entity.Null)
            {
                globals[CoreServiceKeys.LocalPlayerId.Name] = participants.LocalPlayerId;
                globals[CoreServiceKeys.LocalPlayerEntity.Name] = participants.LocalPlayerEntity;
            }
            else if (participants.LocalPlayerEntity != Entity.Null)
            {
                globals.Remove(CoreServiceKeys.LocalPlayerId.Name);
                globals[CoreServiceKeys.LocalPlayerEntity.Name] = participants.LocalPlayerEntity;
            }
            else
            {
                globals.Remove(CoreServiceKeys.LocalPlayerId.Name);
                globals.Remove(CoreServiceKeys.LocalPlayerEntity.Name);
            }
        }

        public static void ClearFocused(IDictionary<string, object> globals)
        {
            ArgumentNullException.ThrowIfNull(globals);
            if (globals.TryGetValue(CoreServiceKeys.TeamEntityLookup.Name, out object teamObj) &&
                teamObj is TeamEntityLookup teamLookup)
            {
                teamLookup.Clear();
            }
            else
            {
                globals[CoreServiceKeys.TeamEntityLookup.Name] = new TeamEntityLookup();
            }

            if (globals.TryGetValue(CoreServiceKeys.PlayerEntityLookup.Name, out object playerObj) &&
                playerObj is PlayerEntityLookup playerLookup)
            {
                playerLookup.Clear();
            }
            else
            {
                globals[CoreServiceKeys.PlayerEntityLookup.Name] = new PlayerEntityLookup();
            }

            globals.Remove(CoreServiceKeys.LocalPlayerId.Name);
            globals.Remove(CoreServiceKeys.LocalPlayerEntity.Name);
        }

        private static void PublishTeamLookup(IDictionary<string, object> globals, TeamEntityLookup source)
        {
            if (globals.TryGetValue(CoreServiceKeys.TeamEntityLookup.Name, out object obj) &&
                obj is TeamEntityLookup focused)
            {
                focused.ReplaceWith(source);
                return;
            }

            globals[CoreServiceKeys.TeamEntityLookup.Name] = source;
        }

        private static void PublishPlayerLookup(IDictionary<string, object> globals, PlayerEntityLookup source)
        {
            if (globals.TryGetValue(CoreServiceKeys.PlayerEntityLookup.Name, out object obj) &&
                obj is PlayerEntityLookup focused)
            {
                focused.ReplaceWith(source);
                return;
            }

            globals[CoreServiceKeys.PlayerEntityLookup.Name] = source;
        }

        private static void ResolveRelationships(
            string mapId,
            MapConfig mapConfig,
            TeamEntityLookup teams,
            PlayerEntityLookup players,
            RelationshipRuntime? relationships,
            RelationshipTypeRegistry? relationshipTypes)
        {
            ParticipantRelationshipConfig config = mapConfig.ParticipantRelationships ?? new ParticipantRelationshipConfig();
            ValidateCollection(config.Teams, $"Map '{mapId}' ParticipantRelationships.Teams");
            ValidateCollection(config.Players, $"Map '{mapId}' ParticipantRelationships.Players");
            ValidateCollection(config.PlayerTeams, $"Map '{mapId}' ParticipantRelationships.PlayerTeams");

            bool hasEntityRelationships =
                config.Teams.Count > 0 ||
                config.Players.Count > 0 ||
                config.PlayerTeams.Count > 0;
            if (hasEntityRelationships && (relationships == null || relationshipTypes == null))
            {
                throw new InvalidOperationException($"Map '{mapId}' declares participant relationships but RelationshipRuntime is unavailable.");
            }

            if (mapConfig.Teams.Count > 0 || mapConfig.Players.Count > 0)
            {
                TeamManager.Clear();
            }

            for (int i = 0; i < config.Teams.Count; i++)
            {
                TeamRelationshipBindingData binding = config.Teams[i] ?? throw new InvalidOperationException($"Map '{mapId}' ParticipantRelationships.Teams[{i}] requires an object payload.");
                Entity teamA = RequireTeam(teams, binding.TeamA, mapId, $"ParticipantRelationships.Teams[{i}].TeamA");
                Entity teamB = RequireTeam(teams, binding.TeamB, mapId, $"ParticipantRelationships.Teams[{i}].TeamB");
                int typeId = ResolveRelationshipType(relationshipTypes!, mapId, $"ParticipantRelationships.Teams[{i}]", binding.TypeId);
                EnsureRelationship(relationships!, teamA, teamB, typeId, symmetric: binding.Symmetric);

                if (!TeamManager.TryParseRelationship(binding.Attitude, out TeamRelationship attitude))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' ParticipantRelationships.Teams[{i}].Attitude is invalid: '{binding.Attitude}'.");
                }

                if (binding.Symmetric)
                {
                    TeamManager.SetRelationshipSymmetric(binding.TeamA, binding.TeamB, attitude);
                }
                else
                {
                    TeamManager.SetRelationship(binding.TeamA, binding.TeamB, attitude);
                }
            }

            for (int i = 0; i < config.Players.Count; i++)
            {
                PlayerRelationshipBindingData binding = config.Players[i] ?? throw new InvalidOperationException($"Map '{mapId}' ParticipantRelationships.Players[{i}] requires an object payload.");
                Entity playerA = RequirePlayer(players, binding.PlayerA, mapId, $"ParticipantRelationships.Players[{i}].PlayerA");
                Entity playerB = RequirePlayer(players, binding.PlayerB, mapId, $"ParticipantRelationships.Players[{i}].PlayerB");
                int typeId = ResolveRelationshipType(relationshipTypes!, mapId, $"ParticipantRelationships.Players[{i}]", binding.TypeId);
                EnsureRelationship(relationships!, playerA, playerB, typeId, symmetric: binding.Symmetric);
            }

            for (int i = 0; i < config.PlayerTeams.Count; i++)
            {
                PlayerTeamRelationshipBindingData binding = config.PlayerTeams[i] ?? throw new InvalidOperationException($"Map '{mapId}' ParticipantRelationships.PlayerTeams[{i}] requires an object payload.");
                Entity player = RequirePlayer(players, binding.PlayerId, mapId, $"ParticipantRelationships.PlayerTeams[{i}].PlayerId");
                Entity team = RequireTeam(teams, binding.TeamId, mapId, $"ParticipantRelationships.PlayerTeams[{i}].TeamId");
                int typeId = ResolveRelationshipType(relationshipTypes!, mapId, $"ParticipantRelationships.PlayerTeams[{i}]", binding.TypeId);
                EnsureRelationship(relationships!, player, team, typeId, symmetric: binding.Symmetric);
            }
        }

        private static void EnsureRelationship(RelationshipRuntime relationships, Entity source, Entity target, int typeId, bool symmetric)
        {
            relationships.EnsureLink(source, target, typeId);
            if (symmetric)
            {
                relationships.EnsureLink(target, source, typeId);
            }
        }

        private static Entity RequireTeam(TeamEntityLookup lookup, int teamId, string mapId, string context)
        {
            if (teamId <= 0)
            {
                throw new InvalidOperationException($"Map '{mapId}' {context} must be positive.");
            }

            if (!lookup.TryGet(teamId, out Entity entity))
            {
                throw new InvalidOperationException($"Map '{mapId}' {context} references unbound TeamId {teamId}.");
            }

            return entity;
        }

        private static Entity RequirePlayer(PlayerEntityLookup lookup, int playerId, string mapId, string context)
        {
            if (playerId <= 0)
            {
                throw new InvalidOperationException($"Map '{mapId}' {context} must be positive.");
            }

            if (!lookup.TryGet(playerId, out Entity entity))
            {
                throw new InvalidOperationException($"Map '{mapId}' {context} references unbound PlayerId {playerId}.");
            }

            return entity;
        }

        private static int ResolveRelationshipType(RelationshipTypeRegistry registry, string mapId, string context, string typeId)
        {
            if (string.IsNullOrWhiteSpace(typeId))
            {
                throw new InvalidOperationException($"Map '{mapId}' {context}.TypeId requires a non-empty relationship type id.");
            }

            return registry.GetId(typeId);
        }

        private static string RequireRepresentativeInstanceId(string mapId, string context, string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new InvalidOperationException($"Map '{mapId}' {context}.RepresentativeInstanceId requires a non-empty value.");
            }

            string trimmed = instanceId.Trim();
            if (!string.Equals(trimmed, instanceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Map '{mapId}' {context}.RepresentativeInstanceId must be trimmed.");
            }

            return instanceId;
        }

        private static void ValidateCollection<T>(List<T>? values, string context)
        {
            if (values == null)
            {
                throw new InvalidOperationException($"{context} must be an explicit collection.");
            }
        }

        private static void Upsert<T>(World world, Entity entity, T component)
        {
            if (world.Has<T>(entity))
            {
                world.Set(entity, component);
            }
            else
            {
                world.Add(entity, component);
            }
        }
    }
}
