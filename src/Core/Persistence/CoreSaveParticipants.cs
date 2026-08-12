using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Quests;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Persistence
{
    public static class CoreSaveParticipants
    {
        public static void RegisterCore(GameEngine engine, SaveParticipantRegistry registry)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            registry.Register(CreateClockParticipant(engine.GetService(CoreServiceKeys.Clock)));
            registry.Register(CreateGameSessionParticipant(engine.GameSession));
            registry.Register(new EmptySaveParticipant("inventory"));
            registry.Register(CreateMapSessionsParticipant(engine.MapSessions));
            registry.Register(CreateQuestParticipant(engine.GetService(CoreServiceKeys.QuestRuntimeService)));
            registry.Register(CreateNarrativeParticipant(engine.GetService(CoreServiceKeys.NarrativeDirector)));
            registry.Register(CreateRelationshipParticipant(engine.GetService(CoreServiceKeys.RelationshipRuntime)));
            registry.Register(CreateTeamParticipant());
            registry.Register(CreateTimeFlowParticipant(engine.GetService(CoreServiceKeys.TimeFlow)));
        }

        public static ISaveParticipant CreateGameSessionParticipant(GameSession session)
        {
            return new GameSessionSaveParticipant(session);
        }

        public static ISaveParticipant CreateClockParticipant(IClock clock)
        {
            return new ClockSaveParticipant(clock);
        }

        public static ISaveParticipant CreateTimeFlowParticipant(TimeFlowService service)
        {
            return new TimeFlowSaveParticipant(service);
        }

        public static ISaveParticipant CreateTeamParticipant()
        {
            return new TeamSaveParticipant();
        }

        public static ISaveParticipant CreateMapSessionsParticipant(MapSessionManager manager)
        {
            return new MapSessionsSaveParticipant(manager);
        }

        public static ISaveParticipant CreateNarrativeParticipant(NarrativeDirector director)
        {
            return new NarrativeSaveParticipant(director);
        }

        public static ISaveParticipant CreateQuestParticipant(QuestRuntimeService runtime)
        {
            return new QuestSaveParticipant(runtime);
        }

        public static ISaveParticipant CreateRelationshipParticipant(RelationshipRuntime runtime)
        {
            return new RelationshipSaveParticipant(runtime);
        }

        private sealed class GameSessionSaveParticipant : ISaveParticipant
        {
            private readonly GameSession _session;

            public GameSessionSaveParticipant(GameSession session)
            {
                _session = session ?? throw new ArgumentNullException(nameof(session));
            }

            public string DomainKey => "gameSession";

            public JsonNode CaptureState()
            {
                GameSessionSnapshot snapshot = _session.CaptureSnapshot();
                var players = new JsonArray();
                for (int i = 0; i < snapshot.Players.Count; i++)
                {
                    PlayerSnapshot player = snapshot.Players[i];
                    players.Add(new JsonObject
                    {
                        ["id"] = player.Id,
                        ["teamId"] = player.TeamId,
                        ["camera"] = WriteCamera(player.Camera)
                    });
                }

                var globals = new JsonObject();
                foreach (KeyValuePair<string, object> pair in snapshot.Globals)
                {
                    globals[pair.Key] = WriteGlobal(pair.Key, pair.Value);
                }

                return new JsonObject
                {
                    ["currentTick"] = snapshot.CurrentTick,
                    ["players"] = players,
                    ["globals"] = globals
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                JsonObject root = state.AsObject();
                if (root.ContainsKey("localPlayerId"))
                {
                    throw new SaveContextException(
                        "GameSession save domain no longer accepts 'localPlayerId'. Local possession is ClientLocalSeatRegistry / launchContext.localSeats[].");
                }

                if (root.ContainsKey("camera"))
                {
                    throw new SaveContextException(
                        "GameSession save domain no longer accepts root 'camera'. Camera authority is LogicViewRegistry / PresentBinding.");
                }

                var players = new List<PlayerSnapshot>();
                JsonArray playerArray = RequireArray(root, "players");
                for (int i = 0; i < playerArray.Count; i++)
                {
                    JsonObject player = RequireObject(playerArray[i], $"players[{i}]");
                    players.Add(new PlayerSnapshot(
                        RequireInt(player, "id"),
                        RequireInt(player, "teamId"),
                        ReadCamera(RequireObject(player["camera"], $"players[{i}].camera"))));
                }

                var globals = new Dictionary<string, object>(StringComparer.Ordinal);
                JsonObject globalsObject = RequireObject(root["globals"], "globals");
                foreach (KeyValuePair<string, JsonNode?> pair in globalsObject)
                {
                    globals[pair.Key] = ReadGlobal(pair.Key, pair.Value);
                }

                var snapshot = new GameSessionSnapshot(
                    RequireInt(root, "currentTick"),
                    players,
                    globals);
                _session.RestoreSnapshot(snapshot);
            }
        }

        private sealed class ClockSaveParticipant : ISaveParticipant
        {
            private readonly DiscreteClock _clock;

            public ClockSaveParticipant(IClock clock)
            {
                _clock = clock as DiscreteClock ??
                    throw new SaveContextException("Core save clock participant requires DiscreteClock.");
            }

            public string DomainKey => "clock";

            public JsonNode CaptureState()
            {
                DiscreteClockSnapshot snapshot = _clock.CaptureSnapshot();
                var domains = new JsonArray();
                for (int i = 0; i < snapshot.Domains.Count; i++)
                {
                    DiscreteClockDomainSnapshot domain = snapshot.Domains[i];
                    domains.Add(new JsonObject
                    {
                        ["name"] = domain.Domain.ToString(),
                        ["tick"] = domain.Tick
                    });
                }

                return new JsonObject
                {
                    ["domains"] = domains
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                JsonObject root = state.AsObject();
                JsonArray domainArray = RequireArray(root, "domains");
                var domains = new List<DiscreteClockDomainSnapshot>(domainArray.Count);
                for (int i = 0; i < domainArray.Count; i++)
                {
                    JsonObject domain = RequireObject(domainArray[i], $"domains[{i}]");
                    string name = RequireString(domain, "name");
                    if (!Enum.TryParse(name, ignoreCase: false, out ClockDomainId domainId) ||
                        !string.Equals(domainId.ToString(), name, StringComparison.Ordinal))
                    {
                        throw new SaveContextException(
                            $"Clock save domain '{name}' at domains[{i}] is invalid.");
                    }

                    int tick = RequireInt(domain, "tick");
                    if (tick < 0)
                    {
                        throw new SaveContextException(
                            $"Clock save tick at domains[{i}] must be non-negative.");
                    }

                    domains.Add(new DiscreteClockDomainSnapshot(domainId, tick));
                }

                try
                {
                    _clock.RestoreSnapshot(new DiscreteClockSnapshot(domains));
                }
                catch (ArgumentException ex)
                {
                    throw new SaveContextException($"Clock save state is invalid: {ex.Message}");
                }
            }
        }

        private sealed class TimeFlowSaveParticipant : ISaveParticipant
        {
            private readonly TimeFlowService _service;

            public TimeFlowSaveParticipant(TimeFlowService service)
            {
                _service = service ?? throw new ArgumentNullException(nameof(service));
            }

            public string DomainKey => "timeFlow";

            public JsonNode CaptureState()
            {
                TimeFlowSnapshot snapshot = _service.CaptureSnapshot();
                var domains = new JsonArray();
                for (int i = 0; i < snapshot.Domains.Count; i++)
                {
                    TimeFlowDomainSnapshot domain = snapshot.Domains[i];
                    domains.Add(new JsonObject
                    {
                        ["name"] = domain.Name,
                        ["parentName"] = domain.ParentName,
                        ["baseScalePermille"] = domain.BaseScalePermille
                    });
                }

                var tokens = new JsonArray();
                for (int i = 0; i < snapshot.ActiveTokens.Count; i++)
                {
                    TimeFlowTokenSnapshot token = snapshot.ActiveTokens[i];
                    tokens.Add(new JsonObject
                    {
                        ["domainName"] = token.DomainName,
                        ["kind"] = token.Kind,
                        ["scalePermille"] = token.ScalePermille,
                        ["owner"] = token.Owner,
                        ["reason"] = token.Reason
                    });
                }

                return new JsonObject
                {
                    ["domains"] = domains,
                    ["activeTokens"] = tokens
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                JsonObject root = state.AsObject();
                JsonArray domainArray = RequireArray(root, "domains");
                var domains = new List<TimeFlowDomainSnapshot>(domainArray.Count);
                for (int i = 0; i < domainArray.Count; i++)
                {
                    JsonObject domain = RequireObject(domainArray[i], $"domains[{i}]");
                    domains.Add(new TimeFlowDomainSnapshot(
                        RequireString(domain, "name"),
                        RequireString(domain, "parentName"),
                        RequireInt(domain, "baseScalePermille")));
                }

                JsonArray tokenArray = RequireArray(root, "activeTokens");
                var tokens = new List<TimeFlowTokenSnapshot>(tokenArray.Count);
                for (int i = 0; i < tokenArray.Count; i++)
                {
                    JsonObject token = RequireObject(tokenArray[i], $"activeTokens[{i}]");
                    tokens.Add(new TimeFlowTokenSnapshot(
                        RequireString(token, "domainName"),
                        RequireString(token, "kind"),
                        RequireInt(token, "scalePermille"),
                        RequireString(token, "owner"),
                        RequireString(token, "reason")));
                }

                _service.RestoreSnapshot(new TimeFlowSnapshot(domains, tokens));
            }
        }

        private sealed class TeamSaveParticipant : ISaveParticipant
        {
            public string DomainKey => "teams";

            public JsonNode CaptureState()
            {
                TeamRelationshipSnapshot snapshot = TeamManager.CaptureSnapshot();
                var relationships = new JsonArray();
                foreach (KeyValuePair<long, TeamRelationship> pair in snapshot.Relationships)
                {
                    relationships.Add(new JsonObject
                    {
                        ["teamA"] = (int)(pair.Key >> 32),
                        ["teamB"] = (int)pair.Key,
                        ["relationship"] = pair.Value.ToString()
                    });
                }

                return new JsonObject
                {
                    ["defaultRelationship"] = snapshot.DefaultRelationship.ToString(),
                    ["relationships"] = relationships
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                JsonObject root = state.AsObject();
                string defaultRelationshipText = RequireString(root, "defaultRelationship");
                if (!TeamManager.TryParseRelationship(defaultRelationshipText, out TeamRelationship defaultRelationship))
                {
                    throw new SaveContextException(
                        $"Team save defaultRelationship '{defaultRelationshipText}' is invalid.");
                }

                JsonArray relationshipArray = RequireArray(root, "relationships");
                var relationships = new Dictionary<long, TeamRelationship>(relationshipArray.Count);
                for (int i = 0; i < relationshipArray.Count; i++)
                {
                    JsonObject item = RequireObject(relationshipArray[i], $"relationships[{i}]");
                    string relationshipText = RequireString(item, "relationship");
                    if (!TeamManager.TryParseRelationship(relationshipText, out TeamRelationship relationship))
                    {
                        throw new SaveContextException(
                            $"Team save relationship '{relationshipText}' at relationships[{i}] is invalid.");
                    }

                    int teamA = RequireInt(item, "teamA");
                    int teamB = RequireInt(item, "teamB");
                    long key = ((long)teamA << 32) | (uint)teamB;
                    relationships.Add(key, relationship);
                }

                TeamManager.RestoreSnapshot(new TeamRelationshipSnapshot(defaultRelationship, relationships));
            }
        }

        private sealed class MapSessionsSaveParticipant : ISaveParticipant
        {
            private readonly MapSessionManager _manager;

            public MapSessionsSaveParticipant(MapSessionManager manager)
            {
                _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            }

            public string DomainKey => "mapSessions";

            public JsonNode CaptureState()
            {
                MapSessionManagerSnapshot snapshot = _manager.CaptureSnapshot();
                var sessions = new JsonArray();
                for (int i = 0; i < snapshot.Sessions.Count; i++)
                {
                    MapSessionEntrySnapshot session = snapshot.Sessions[i];
                    var sessionObject = new JsonObject
                    {
                        ["mapId"] = session.MapId,
                        ["state"] = session.State.ToString()
                    };
                    JsonObject? launchContext = WriteLaunchContext(session.LaunchContext);
                    if (launchContext != null)
                    {
                        sessionObject["launchContext"] = launchContext;
                    }

                    sessions.Add(sessionObject);
                }

                var focusStack = new JsonArray();
                for (int i = 0; i < snapshot.FocusStack.Count; i++)
                {
                    focusStack.Add(snapshot.FocusStack[i]);
                }

                return new JsonObject
                {
                    ["sessions"] = sessions,
                    ["focusStack"] = focusStack
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                JsonObject root = state.AsObject();
                JsonArray sessionArray = RequireArray(root, "sessions");
                var sessions = new List<MapSessionEntrySnapshot>(sessionArray.Count);
                for (int i = 0; i < sessionArray.Count; i++)
                {
                    JsonObject session = RequireObject(sessionArray[i], $"sessions[{i}]");
                    string stateText = RequireString(session, "state");
                    if (!Enum.TryParse(stateText, ignoreCase: false, out MapSessionState sessionState) ||
                        !string.Equals(sessionState.ToString(), stateText, StringComparison.Ordinal))
                    {
                        throw new SaveContextException(
                            $"Map session state '{stateText}' at sessions[{i}] is invalid.");
                    }

                    sessions.Add(new MapSessionEntrySnapshot(
                        RequireString(session, "mapId"),
                        sessionState,
                        ReadLaunchContext(session["launchContext"])));
                }

                JsonArray focusStackArray = RequireArray(root, "focusStack");
                var focusStack = new string[focusStackArray.Count];
                for (int i = 0; i < focusStackArray.Count; i++)
                {
                    focusStack[i] = RequireStringValue(focusStackArray[i], $"focusStack[{i}]");
                }

                _manager.RestoreSnapshot(new MapSessionManagerSnapshot(sessions, focusStack));
            }
        }

        private sealed class NarrativeSaveParticipant : ISaveParticipant
        {
            private readonly NarrativeDirector _director;

            public NarrativeSaveParticipant(NarrativeDirector director)
            {
                _director = director ?? throw new ArgumentNullException(nameof(director));
            }

            public string DomainKey => "narrative";

            public JsonNode CaptureState()
            {
                NarrativeDirectorSnapshot snapshot = _director.CaptureSnapshot();
                var variables = new JsonObject();
                foreach (KeyValuePair<string, NarrativeValue> pair in snapshot.Variables)
                {
                    variables[pair.Key] = WriteNarrativeValue(pair.Value);
                }

                var bindings = new JsonArray();
                for (int i = 0; i < snapshot.Bindings.Count; i++)
                {
                    NarrativeEntityBindingSnapshot binding = snapshot.Bindings[i];
                    bindings.Add(new JsonObject
                    {
                        ["alias"] = binding.Alias,
                        ["entity"] = WriteEntity(binding.Entity)
                    });
                }

                return new JsonObject
                {
                    ["variables"] = variables,
                    ["bindings"] = bindings,
                    ["activeDialogue"] = WriteNarrativeDialogue(snapshot.ActiveDialogue),
                    ["activeCinematic"] = WriteNarrativeCinematic(snapshot.ActiveCinematic)
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                JsonObject root = state.AsObject();
                var variables = new Dictionary<string, NarrativeValue>(StringComparer.OrdinalIgnoreCase);
                JsonObject variableObject = RequireObject(root["variables"], "variables");
                foreach (KeyValuePair<string, JsonNode?> pair in variableObject)
                {
                    variables[pair.Key] = ReadNarrativeValue(RequireObject(pair.Value, $"variables.{pair.Key}"));
                }

                JsonArray bindingArray = RequireArray(root, "bindings");
                var bindings = new List<NarrativeEntityBindingSnapshot>(bindingArray.Count);
                for (int i = 0; i < bindingArray.Count; i++)
                {
                    JsonObject binding = RequireObject(bindingArray[i], $"bindings[{i}]");
                    bindings.Add(new NarrativeEntityBindingSnapshot(
                        RequireString(binding, "alias"),
                        ReadEntity(RequireObject(binding["entity"], $"bindings[{i}].entity"))));
                }

                _director.RestoreSnapshot(new NarrativeDirectorSnapshot(
                    variables,
                    bindings,
                    ReadNarrativeDialogue(root["activeDialogue"]),
                    ReadNarrativeCinematic(root["activeCinematic"])));
            }
        }

        private sealed class QuestSaveParticipant : ISaveParticipant
        {
            private readonly QuestRuntimeService _runtime;

            public QuestSaveParticipant(QuestRuntimeService runtime)
            {
                _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            }

            public string DomainKey => "quests";

            public JsonNode CaptureState()
            {
                QuestRuntimeSnapshot snapshot = _runtime.CaptureSnapshot();
                var signals = new JsonObject();
                foreach (KeyValuePair<string, int> pair in snapshot.Signals)
                {
                    signals[pair.Key] = pair.Value;
                }

                return new JsonObject
                {
                    ["signals"] = signals
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                JsonObject root = state.AsObject();
                var signals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                JsonObject signalObject = RequireObject(root["signals"], "signals");
                foreach (KeyValuePair<string, JsonNode?> pair in signalObject)
                {
                    signals[pair.Key] = RequireIntValue(pair.Value, $"signals.{pair.Key}");
                }

                try
                {
                    _runtime.RestoreSnapshot(new QuestRuntimeSnapshot(signals));
                }
                catch (InvalidOperationException ex)
                {
                    throw new SaveContextException($"Quest save state is invalid: {ex.Message}");
                }
            }
        }

        private sealed class RelationshipSaveParticipant : ISaveParticipant
        {
            private readonly RelationshipRuntime _runtime;

            public RelationshipSaveParticipant(RelationshipRuntime runtime)
            {
                _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            }

            public string DomainKey => "relationships";

            public JsonNode CaptureState()
            {
                return new JsonObject();
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                state.AsObject();
                try
                {
                    _runtime.RebuildEntityIndexFromWorld();
                }
                catch (InvalidOperationException ex)
                {
                    throw new SaveContextException($"Relationship save state is invalid: {ex.Message}");
                }
            }
        }

        private sealed class EmptySaveParticipant : ISaveParticipant
        {
            public EmptySaveParticipant(string domainKey)
            {
                DomainKey = domainKey;
            }

            public string DomainKey { get; }

            public JsonNode CaptureState()
            {
                return new JsonObject();
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));
            }
        }

        private static JsonObject WriteCamera(in CameraStateSnapshot camera)
        {
            return new JsonObject
            {
                ["targetX"] = camera.TargetCm.X,
                ["targetY"] = camera.TargetCm.Y,
                ["targetHeightCm"] = camera.TargetHeightCm,
                ["yaw"] = camera.Yaw,
                ["pitch"] = camera.Pitch,
                ["distanceCm"] = camera.DistanceCm,
                ["fovYDeg"] = camera.FovYDeg,
                ["rigKind"] = camera.RigKind.ToString(),
                ["zoomLevel"] = camera.ZoomLevel,
                ["isFollowing"] = camera.IsFollowing
            };
        }

        private static CameraStateSnapshot ReadCamera(JsonObject camera)
        {
            return new CameraStateSnapshot
            {
                TargetCm = new System.Numerics.Vector2(RequireSingle(camera, "targetX"), RequireSingle(camera, "targetY")),
                TargetHeightCm = RequireSingle(camera, "targetHeightCm"),
                Yaw = RequireSingle(camera, "yaw"),
                Pitch = RequireSingle(camera, "pitch"),
                DistanceCm = RequireSingle(camera, "distanceCm"),
                FovYDeg = RequireSingle(camera, "fovYDeg"),
                RigKind = Enum.Parse<CameraRigKind>(RequireString(camera, "rigKind")),
                ZoomLevel = RequireInt(camera, "zoomLevel"),
                IsFollowing = RequireBool(camera, "isFollowing")
            };
        }

        private static JsonNode WriteGlobal(string key, object value)
        {
            return value switch
            {
                string text => JsonValue.Create(text)!,
                bool boolean => JsonValue.Create(boolean)!,
                int integer => JsonValue.Create(integer)!,
                long longInteger => JsonValue.Create(longInteger)!,
                float single => JsonValue.Create(single)!,
                double number => JsonValue.Create(number)!,
                _ => throw new SaveContextException(
                    $"GameSession global '{key}' has unsupported save value type '{value.GetType().FullName}'.")
            };
        }

        private static object ReadGlobal(string key, JsonNode? node)
        {
            if (node == null)
            {
                throw new SaveContextException($"GameSession global '{key}' is null.");
            }

            JsonValueKind kind = node.GetValueKind();
            return kind switch
            {
                JsonValueKind.String => node.GetValue<string>(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => ReadNumber(node),
                _ => throw new SaveContextException(
                    $"GameSession global '{key}' has unsupported JSON kind '{kind}'.")
            };
        }

        private static object ReadNumber(JsonNode node)
        {
            if (node is not JsonValue value)
            {
                throw new SaveContextException("GameSession global number must be a JSON value.");
            }

            if (value.TryGetValue<int>(out int integer)) return integer;
            if (value.TryGetValue<long>(out long longInteger)) return longInteger;
            if (value.TryGetValue<float>(out float single)) return single;
            if (value.TryGetValue<double>(out double number)) return number;

            throw new SaveContextException("GameSession global number has unsupported numeric storage.");
        }

        private static JsonObject WriteNarrativeValue(NarrativeValue value)
        {
            return new JsonObject
            {
                ["kind"] = value.Kind.ToString(),
                ["intValue"] = value.IntValue,
                ["floatValue"] = value.FloatValue,
                ["boolValue"] = value.BoolValue,
                ["stringValue"] = value.StringValue
            };
        }

        private static NarrativeValue ReadNarrativeValue(JsonObject value)
        {
            string kindText = RequireString(value, "kind");
            if (!Enum.TryParse(kindText, ignoreCase: false, out NarrativeValueKind kind) ||
                !string.Equals(kind.ToString(), kindText, StringComparison.Ordinal))
            {
                throw new SaveContextException($"Narrative value kind '{kindText}' is invalid.");
            }

            return new NarrativeValue(
                kind,
                RequireInt(value, "intValue"),
                RequireSingle(value, "floatValue"),
                RequireBool(value, "boolValue"),
                RequireString(value, "stringValue"));
        }

        private static JsonObject WriteEntity(Arch.Core.Entity entity)
        {
            return new JsonObject
            {
                ["id"] = entity.Id,
                ["worldId"] = entity.WorldId,
                ["version"] = entity.Version
            };
        }

        private static Arch.Core.Entity ReadEntity(JsonObject entity)
        {
            return EntityUtil.Reconstruct(
                RequireInt(entity, "id"),
                RequireInt(entity, "worldId"),
                RequireInt(entity, "version"));
        }

        private static JsonNode? WriteNarrativeDialogue(NarrativeDialogueSnapshot dialogue)
        {
            if (dialogue == null)
            {
                return null;
            }

            return new JsonObject
            {
                ["dialogueId"] = dialogue.DialogueId,
                ["nodeId"] = dialogue.NodeId,
                ["elapsedSeconds"] = dialogue.ElapsedSeconds
            };
        }

        private static NarrativeDialogueSnapshot ReadNarrativeDialogue(JsonNode? node)
        {
            if (node == null || node.GetValueKind() == JsonValueKind.Null)
            {
                return null;
            }

            JsonObject dialogue = RequireObject(node, "activeDialogue");
            return new NarrativeDialogueSnapshot(
                RequireString(dialogue, "dialogueId"),
                RequireString(dialogue, "nodeId"),
                RequireSingle(dialogue, "elapsedSeconds"));
        }

        private static JsonNode? WriteNarrativeCinematic(NarrativeCinematicSnapshot cinematic)
        {
            if (cinematic == null)
            {
                return null;
            }

            return new JsonObject
            {
                ["cinematicId"] = cinematic.CinematicId,
                ["stepIndex"] = cinematic.StepIndex,
                ["elapsedSeconds"] = cinematic.ElapsedSeconds,
                ["advanceRequested"] = cinematic.AdvanceRequested
            };
        }

        private static NarrativeCinematicSnapshot ReadNarrativeCinematic(JsonNode? node)
        {
            if (node == null || node.GetValueKind() == JsonValueKind.Null)
            {
                return null;
            }

            JsonObject cinematic = RequireObject(node, "activeCinematic");
            return new NarrativeCinematicSnapshot(
                RequireString(cinematic, "cinematicId"),
                RequireInt(cinematic, "stepIndex"),
                RequireSingle(cinematic, "elapsedSeconds"),
                RequireBool(cinematic, "advanceRequested"));
        }

        private static JsonObject RequireObject(JsonNode? node, string field)
        {
            return node as JsonObject ?? throw new SaveContextException($"Save domain field '{field}' must be an object.");
        }

        private static JsonArray RequireArray(JsonObject root, string field)
        {
            return root[field] as JsonArray ?? throw new SaveContextException($"Save domain field '{field}' must be an array.");
        }

        private static int RequireInt(JsonObject root, string field)
        {
            JsonNode? node = root[field];
            if (node == null)
            {
                throw new SaveContextException($"Save domain field '{field}' is missing.");
            }

            return node.GetValue<int>();
        }

        private static float RequireSingle(JsonObject root, string field)
        {
            JsonNode? node = root[field];
            if (node == null)
            {
                throw new SaveContextException($"Save domain field '{field}' is missing.");
            }

            return node.GetValue<float>();
        }

        private static bool RequireBool(JsonObject root, string field)
        {
            JsonNode? node = root[field];
            if (node == null)
            {
                throw new SaveContextException($"Save domain field '{field}' is missing.");
            }

            return node.GetValue<bool>();
        }

        private static string RequireString(JsonObject root, string field)
        {
            JsonNode? node = root[field];
            if (node == null)
            {
                throw new SaveContextException($"Save domain field '{field}' is missing.");
            }

            return node.GetValue<string>();
        }

        private static int RequireIntValue(JsonNode? node, string field)
        {
            if (node == null)
            {
                throw new SaveContextException($"Save domain field '{field}' is missing.");
            }

            return node.GetValue<int>();
        }

        private static string RequireStringValue(JsonNode? node, string field)
        {
            if (node == null)
            {
                throw new SaveContextException($"Save domain field '{field}' is missing.");
            }

            return node.GetValue<string>();
        }

        private static JsonObject? WriteLaunchContext(MapLaunchContext? launchContext)
        {
            if (launchContext == null || launchContext.IsEmpty)
            {
                return null;
            }

            var root = new JsonObject();
            if (launchContext.HasLocalSeats)
            {
                var seats = new JsonArray();
                for (int i = 0; i < launchContext.LocalSeats.Count; i++)
                {
                    LocalSeatLaunchBinding seat = launchContext.LocalSeats[i];
                    var seatObj = new JsonObject
                    {
                        ["seatId"] = seat.SeatId,
                        ["playerId"] = seat.PlayerId,
                    };
                    if (!string.IsNullOrWhiteSpace(seat.ControlSchemeId))
                    {
                        seatObj["controlSchemeId"] = seat.ControlSchemeId;
                    }

                    seats.Add(seatObj);
                }

                root["localSeats"] = seats;
            }

            if (launchContext.Metadata != null && launchContext.Metadata.Count > 0)
            {
                var metadata = new JsonObject();
                foreach (KeyValuePair<string, object> pair in launchContext.Metadata)
                {
                    metadata[pair.Key] = WriteGlobal(pair.Key, pair.Value);
                }

                root["metadata"] = metadata;
            }

            return root;
        }

        private static MapLaunchContext? ReadLaunchContext(JsonNode? node)
        {
            if (node == null)
            {
                return null;
            }

            JsonObject root = node as JsonObject ??
                throw new SaveContextException("Map session launchContext must be an object.");

            if (root.ContainsKey("localPlayerId"))
            {
                throw new SaveContextException(
                    "Map session launchContext.localPlayerId is removed; use launchContext.localSeats[].");
            }

            LocalSeatLaunchBinding[] seats = Array.Empty<LocalSeatLaunchBinding>();
            if (root["localSeats"] is JsonArray seatArray)
            {
                seats = new LocalSeatLaunchBinding[seatArray.Count];
                for (int i = 0; i < seatArray.Count; i++)
                {
                    JsonObject seatObj = seatArray[i] as JsonObject ??
                        throw new SaveContextException($"launchContext.localSeats[{i}] must be an object.");
                    string seatId = seatObj["seatId"]?.GetValue<string>()
                        ?? throw new SaveContextException($"launchContext.localSeats[{i}].seatId is required.");
                    int playerId = seatObj["playerId"]?.GetValue<int>()
                        ?? throw new SaveContextException($"launchContext.localSeats[{i}].playerId is required.");
                    string? controlSchemeId = seatObj["controlSchemeId"]?.GetValue<string>();
                    seats[i] = new LocalSeatLaunchBinding(seatId, playerId, controlSchemeId);
                }
            }

            IReadOnlyDictionary<string, object>? metadata = null;
            if (root["metadata"] is JsonObject metadataObject)
            {
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, JsonNode?> pair in metadataObject)
                {
                    values[pair.Key] = ReadGlobal(pair.Key, pair.Value);
                }

                metadata = values;
            }

            return MapLaunchContext.Create(seats, metadata);
        }
    }
}
