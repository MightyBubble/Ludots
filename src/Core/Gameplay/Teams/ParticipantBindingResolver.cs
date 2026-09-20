using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace Ludots.Core.Gameplay.Teams
{
    public readonly record struct ResolvedLocalSeatPossession(
        string SeatId,
        int PlayerId,
        Entity RepEntity,
        string? ControlSchemeId);

    public sealed class ParticipantBindingResult
    {
        public ParticipantBindingResult(
            TeamEntityLookup teams,
            PlayerEntityLookup players,
            IReadOnlyList<ResolvedLocalSeatPossession> localSeats,
            TeamRelationshipSnapshot? teamRelationships = null)
        {
            Teams = teams ?? throw new ArgumentNullException(nameof(teams));
            Players = players ?? throw new ArgumentNullException(nameof(players));
            LocalSeats = localSeats ?? Array.Empty<ResolvedLocalSeatPossession>();
            TeamRelationships = teamRelationships;
        }

        public TeamEntityLookup Teams { get; }
        public PlayerEntityLookup Players { get; }
        public IReadOnlyList<ResolvedLocalSeatPossession> LocalSeats { get; }
        public TeamRelationshipSnapshot? TeamRelationships { get; }
    }

    public static class ParticipantBindingResolver
    {
        /// <summary>
        /// Binds map participants and materializes the control-plane topology.
        /// <paramref name="stanceCatalog"/> selects the stance-bridging semantics (RFC-0065 DEC-3):
        /// when configured, every map attitude must match a registered stance type and is double-written
        /// as a relationship edge next to the TeamManager matrix; when null, no stance edges are built
        /// (explicit data absence = pure legacy TeamManager behavior).
        /// </summary>
        public static ParticipantBindingResult Resolve(
            MapSession session,
            World world,
            MapLoadEntityIndex entityIndex,
            RelationshipRuntime? relationships,
            RelationshipTypeRegistry? relationshipTypes,
            OwnershipResolver? ownership = null,
            DomainStanceConfig? stanceCatalog = null)
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

            IReadOnlyList<ResolvedLocalSeatPossession> localSeats = ResolveLocalSeats(
                mapId,
                session.LaunchContext,
                playerLookup);

            ResolveRelationships(mapId, mapConfig, teamLookup, playerLookup, relationships, relationshipTypes, stanceCatalog);
            BuildControlPlaneEdges(session, world, mapId, mapConfig, teamLookup, playerLookup, relationships, relationshipTypes, ownership);

            bool hasParticipantBindings = mapConfig.Teams.Count > 0 || mapConfig.Players.Count > 0;
            return new ParticipantBindingResult(
                teamLookup,
                playerLookup,
                localSeats,
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

            PublishLocalSeats(globals, participants.LocalSeats);
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

            if (globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) &&
                seatsObj is ClientLocalSeatRegistry seats)
            {
                seats.Clear();
            }

            if (globals.TryGetValue(CoreServiceKeys.LogicViewRegistry.Name, out object? viewsObj) &&
                viewsObj is LogicViewRegistry views)
            {
                views.Clear();
            }
        }

        private static IReadOnlyList<ResolvedLocalSeatPossession> ResolveLocalSeats(
            string mapId,
            MapLaunchContext? launchContext,
            PlayerEntityLookup playerLookup)
        {
            if (launchContext?.HasLocalSeats != true)
            {
                return Array.Empty<ResolvedLocalSeatPossession>();
            }

            var resolved = new ResolvedLocalSeatPossession[launchContext.LocalSeats.Count];
            for (int i = 0; i < launchContext.LocalSeats.Count; i++)
            {
                LocalSeatLaunchBinding seat = launchContext.LocalSeats[i];
                if (!playerLookup.TryGet(seat.PlayerId, out Entity rep))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' launch context LocalSeats[{i}] playerId {seat.PlayerId} references an unbound player.");
                }

                resolved[i] = new ResolvedLocalSeatPossession(seat.SeatId, seat.PlayerId, rep, seat.ControlSchemeId);
            }

            return resolved;
        }

        private static void PublishLocalSeats(
            IDictionary<string, object> globals,
            IReadOnlyList<ResolvedLocalSeatPossession> localSeats)
        {
            if (!globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) ||
                seatsObj is not ClientLocalSeatRegistry seats)
            {
                throw new InvalidOperationException(
                    $"{CoreServiceKeys.ClientLocalSeatRegistry.Name} must be registered before publishing focused participants.");
            }

            if (!globals.TryGetValue(CoreServiceKeys.LogicViewRegistry.Name, out object? viewsObj) ||
                viewsObj is not LogicViewRegistry views)
            {
                throw new InvalidOperationException(
                    $"{CoreServiceKeys.LogicViewRegistry.Name} must be registered before publishing focused participants.");
            }

            seats.Clear();
            views.Clear();
            string? declaredPresentLayout = ClientLocalSeatAccess.ResolveDeclaredPresentLayout(globals);
            PresentBinding.ValidateDeclaredLayout(declaredPresentLayout);
            var built = new ClientLocalSeat[localSeats.Count];
            for (int i = 0; i < localSeats.Count; i++)
            {
                ResolvedLocalSeatPossession possession = localSeats[i];
                var seat = new ClientLocalSeat(possession.SeatId, possession.ControlSchemeId)
                {
                    PossessedPlayerId = possession.PlayerId,
                    PossessedRep = possession.RepEntity,
                };
                string viewId = views.EnsureDefaultView(possession.RepEntity);
                if (TryResolvePresentResolutionPx(globals, out System.Numerics.Vector2 presentResolutionPx))
                {
                    seat.PresentBinding = PresentBinding.FromDeclaredLayout(
                        declaredPresentLayout,
                        viewId,
                        i,
                        localSeats.Count,
                        presentResolutionPx);
                }

                built[i] = seat;
            }

            seats.ReplaceAll(built);
            ConfigureLogicViewCameras(globals, views);
            ActivateSoleSeatControlScheme(globals, seats);
            PublishSeatInputChannels(globals, seats);
        }

        /// <summary>
        /// Multi-seat scheme routing: with more than one seat, every seat declaring a
        /// controlSchemeId activates it on its own input channel (handler context stack +
        /// authoritative input snapshot owned per seat). The sole seat keeps the engine-global
        /// interpretation chain and never reaches this path's channel building. A declared
        /// scheme that is uninstalled or refused by the mod allowed-set fails fast inside
        /// <see cref="ClientLocalSeatInputRuntime.PublishSeats"/> with the same semantics as
        /// the sole-seat activation chain.
        /// </summary>
        private static void PublishSeatInputChannels(
            IDictionary<string, object> globals,
            ClientLocalSeatRegistry seats)
        {
            if (globals.TryGetValue(CoreServiceKeys.ClientLocalSeatInputRuntime.Name, out object? runtimeObj) &&
                runtimeObj is ClientLocalSeatInputRuntime seatInput)
            {
                seatInput.PublishSeats(seats);
                return;
            }

            if (seats.Count > 1)
            {
                for (int i = 0; i < seats.SeatIds.Count; i++)
                {
                    ClientLocalSeat seat = seats.Require(seats.SeatIds[i]);
                    if (string.IsNullOrWhiteSpace(seat.ControlSchemeId))
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"ClientLocalSeat '{seat.SeatId}' declares control scheme '{seat.ControlSchemeId}' " +
                        $"but the ClientLocalSeatInputRuntime service is not registered.");
                }
            }
        }

        /// <summary>
        /// The sole seat's declared ControlSchemeId is the per-entry launch truth and activates the
        /// global ControlSchemeRuntime (sole consumption path) as a runtime-only switch: the
        /// map-entry truth is transient and never rewrites the persisted preference store, so the
        /// player's own choice survives for later maps without a declaration. Seats without a
        /// declaration keep the runtime's initial/preference activation. Multi-seat tables never
        /// touch this global runtime; their declared schemes activate per seat in
        /// <see cref="PublishSeatInputChannels"/>. A declared scheme with the runtime
        /// unregistered, uninstalled, or refused by the allowed-set is a configuration error and
        /// fails fast instead of falling back to the initial scheme.
        /// </summary>
        private static void ActivateSoleSeatControlScheme(
            IDictionary<string, object> globals,
            ClientLocalSeatRegistry seats)
        {
            if (seats.Count != 1)
            {
                return;
            }

            ClientLocalSeat seat = seats.Require(seats.SeatIds[0]);
            if (string.IsNullOrWhiteSpace(seat.ControlSchemeId))
            {
                return;
            }

            if (!globals.TryGetValue(CoreServiceKeys.ControlSchemeRuntime.Name, out object? schemeObj) ||
                schemeObj is not ControlSchemeRuntime schemes)
            {
                throw new InvalidOperationException(
                    $"ClientLocalSeat '{seat.SeatId}' declares control scheme '{seat.ControlSchemeId}' but the ControlSchemeRuntime service is not registered.");
            }

            string schemeId = seat.ControlSchemeId!.Trim();
            if (!schemes.SchemeIdRegistry.TryGetId(schemeId, out int compiledSchemeId))
            {
                throw new InvalidOperationException(
                    $"ClientLocalSeat '{seat.SeatId}' declares control scheme '{schemeId}' which is not installed.");
            }

            if (!schemes.TrySwitchRuntimeOnly(compiledSchemeId))
            {
                throw new InvalidOperationException(
                    $"ClientLocalSeat '{seat.SeatId}' declares control scheme '{schemeId}' which the mod allowed-set refuses.");
            }
        }

        private static bool TryResolvePresentResolutionPx(
            IDictionary<string, object> globals,
            out System.Numerics.Vector2 presentResolutionPx)
        {
            presentResolutionPx = default;
            if (!globals.TryGetValue(CoreServiceKeys.ViewController.Name, out object? viewObj) ||
                viewObj is not Presentation.Camera.IViewController view)
            {
                return false;
            }

            if (view.Resolution.X <= 0f || view.Resolution.Y <= 0f)
            {
                throw new InvalidOperationException(
                    "ViewController.Resolution must be positive before publishing PresentBinding.");
            }

            presentResolutionPx = view.Resolution;
            return true;
        }

        private static void ConfigureLogicViewCameras(IDictionary<string, object> globals, LogicViewRegistry views)
        {
            Gameplay.Camera.VirtualCameraRegistry? virtualCameras = null;
            if (globals.TryGetValue(CoreServiceKeys.VirtualCameraRegistry.Name, out object? vcamObj) &&
                vcamObj is Gameplay.Camera.VirtualCameraRegistry registry)
            {
                virtualCameras = registry;
            }

            Gameplay.Camera.CameraImpulseRuntime? impulseRuntime = null;
            if (globals.TryGetValue(CoreServiceKeys.CameraImpulseRuntime.Name, out object? impulseObj) &&
                impulseObj is Gameplay.Camera.CameraImpulseRuntime impulse)
            {
                impulseRuntime = impulse;
            }

            Gameplay.Camera.PlatformManagedCameraDriverRegistry? platformDrivers = null;
            if (globals.TryGetValue(CoreServiceKeys.PlatformManagedCameraDriverRegistry.Name, out object? driversObj) &&
                driversObj is Gameplay.Camera.PlatformManagedCameraDriverRegistry drivers)
            {
                platformDrivers = drivers;
            }

            var cameras = new System.Collections.Generic.List<Gameplay.Camera.CameraManager>(views.Count);
            views.CopyCameras(cameras);
            for (int i = 0; i < cameras.Count; i++)
            {
                Gameplay.Camera.CameraManager camera = cameras[i];
                if (virtualCameras != null && camera.VirtualCameraBrain == null)
                {
                    camera.SetVirtualCameraRegistry(virtualCameras);
                }

                if (impulseRuntime != null)
                {
                    camera.SetImpulseRuntime(impulseRuntime);
                }

                if (platformDrivers != null)
                {
                    camera.SetPlatformManagedCameraDriverRegistry(platformDrivers);
                }

                // Runtime (bounds / heightmap / view) is completed by GameEngine.EnsureCameraRuntimeConfigured.
            }
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
            RelationshipTypeRegistry? relationshipTypes,
            DomainStanceConfig? stanceCatalog)
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

                // RFC-0065 DEC-3 bridge: double-write the attitude as a teamRep→teamRep stance edge so
                // DomainStanceQuery and the legacy TeamManager matrix stay consistent until CTRL-3 retires the latter.
                if (stanceCatalog != null)
                {
                    int stanceTypeId = ResolveStanceType(
                        relationshipTypes!,
                        stanceCatalog,
                        mapId,
                        $"ParticipantRelationships.Teams[{i}]",
                        binding.Attitude);
                    EnsureRelationship(relationships!, teamA, teamB, stanceTypeId, symmetric: binding.Symmetric);
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

                if (stanceCatalog != null && !string.IsNullOrEmpty(binding.Attitude))
                {
                    int stanceTypeId = ResolveStanceType(
                        relationshipTypes!,
                        stanceCatalog,
                        mapId,
                        $"ParticipantRelationships.PlayerTeams[{i}]",
                        binding.Attitude);
                    EnsureRelationship(relationships!, player, team, stanceTypeId, symmetric: binding.Symmetric);
                }
            }
        }

        /// <summary>
        /// Resolves a map attitude string against the data-declared stance catalog (no code-level mapping):
        /// the attitude must literally match one of the registered stance names, otherwise fail fast.
        /// </summary>
        private static int ResolveStanceType(
            RelationshipTypeRegistry registry,
            DomainStanceConfig stanceCatalog,
            string mapId,
            string context,
            string attitude)
        {
            for (int i = 0; i < stanceCatalog.StanceTypes.Count; i++)
            {
                if (string.Equals(stanceCatalog.StanceTypes[i], attitude, StringComparison.Ordinal))
                {
                    return registry.GetId(attitude);
                }
            }

            throw new InvalidOperationException(
                $"Map '{mapId}' {context}.Attitude '{attitude}' does not match any registered stance type " +
                $"[{string.Join(", ", stanceCatalog.StanceTypes)}]. Stance names are relationship catalog data (RFC-0065 DEC-3); " +
                "align the map attitude with the catalog stance names or extend the catalog stance section.");
        }

        /// <summary>
        /// RFC-0065 CTRL-2: materializes the control-plane topology at participant binding time —
        /// <c>MemberOf(playerRep → teamRep)</c> for every bound player and <c>Owns(playerRep → unit)</c>
        /// for every map-owned non-rep entity carrying <see cref="PlayerOwner"/>.
        /// </summary>
        private static void BuildControlPlaneEdges(
            MapSession session,
            World world,
            string mapId,
            MapConfig mapConfig,
            TeamEntityLookup teams,
            PlayerEntityLookup players,
            RelationshipRuntime? relationships,
            RelationshipTypeRegistry? relationshipTypes,
            OwnershipResolver? ownership)
        {
            if (mapConfig.Teams.Count == 0 && mapConfig.Players.Count == 0)
            {
                return;
            }

            if (relationships == null || relationshipTypes == null || ownership == null)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' declares participant bindings but the relationship control plane (RelationshipRuntime/RelationshipTypeRegistry/OwnershipResolver) is unavailable.");
            }

            int memberOfTypeId = relationshipTypes.GetId("MemberOf");
            for (int i = 0; i < mapConfig.Players.Count; i++)
            {
                PlayerBindingData binding = mapConfig.Players[i]!;
                Entity playerRep = players.Get(binding.PlayerId);
                Entity teamRep = teams.Get(binding.TeamId);
                relationships.EnsureLink(playerRep, teamRep, memberOfTypeId);
            }

            var stanceMembers = new List<(Entity Entity, int TeamId)>();
            var stanceMemberQuery = new QueryDescription()
                .WithAll<Team, MapEntity>()
                .WithNone<PlayerIdentity, TeamIdentity>();
            world.Query(in stanceMemberQuery, (Entity entity, ref Team team, ref MapEntity mapEntity) =>
            {
                if (mapEntity.MapId == session.MapId && team.Id > 0)
                {
                    stanceMembers.Add((entity, team.Id));
                }
            });

            for (int i = 0; i < stanceMembers.Count; i++)
            {
                (Entity member, int teamId) = stanceMembers[i];
                if (!world.IsAlive(member))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' team member entity {member.Id} became invalid while building control-plane membership edges.");
                }

                if (!teams.TryGet(teamId, out Entity teamRep) || !world.IsAlive(teamRep))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' entity {member.Id} authors Team {teamId}, but no live team representative is bound.");
                }

                relationships.EnsureLink(member, teamRep, memberOfTypeId);
            }

            OwnershipEdgeBuilder.LinkMapOwnedEntities(world, ownership, players, session.MapId);
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
