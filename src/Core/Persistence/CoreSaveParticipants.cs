using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Randomization;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Calendar;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.Sequencer;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Tasks;
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
            registry.Register(CreateActivityParticipant(engine.GetService(CoreServiceKeys.ActivityRuntimeService)));
            registry.Register(CreateTaskParticipant(engine.GetService(CoreServiceKeys.TaskRuntimeService)));
            registry.Register(CreateDialogueParticipant(engine.GetService(CoreServiceKeys.DialogueRuntime)));
            registry.Register(CreateSequencerParticipant(engine.GetService(CoreServiceKeys.SequencerRuntime)));
            registry.Register(CreateRelationshipParticipant(engine.GetService(CoreServiceKeys.RelationshipRuntime)));
            registry.Register(CreateRngParticipant(engine.GetService(CoreServiceKeys.RngStreamService)));
            registry.Register(CreateTeamParticipant());
            registry.Register(CreateTimeFlowParticipant(engine.GetService(CoreServiceKeys.TimeFlow)));
            registry.Register(CreateCalendarParticipant(engine.GetService(CoreServiceKeys.CalendarRuntime)));
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

        public static ISaveParticipant CreateCalendarParticipant(CalendarRuntime runtime)
        {
            return new CalendarSaveParticipant(runtime);
        }

        public static ISaveParticipant CreateTeamParticipant()
        {
            return new TeamSaveParticipant();
        }

        public static ISaveParticipant CreateMapSessionsParticipant(MapSessionManager manager)
        {
            return new MapSessionsSaveParticipant(manager);
        }

        public static ISaveParticipant CreateDialogueParticipant(DialogueRuntime runtime)
        {
            return new DialogueSaveParticipant(runtime);
        }

        public static ISaveParticipant CreateSequencerParticipant(SequencerRuntime runtime)
        {
            return new SequencerSaveParticipant(runtime);
        }

        public static ISaveParticipant CreateActivityParticipant(ActivityRuntimeService runtime)
        {
            return new ActivitySaveParticipant(runtime);
        }

        public static ISaveParticipant CreateTaskParticipant(TaskRuntimeService runtime)
        {
            return new TaskSaveParticipant(runtime);
        }

        public static ISaveParticipant CreateRelationshipParticipant(RelationshipRuntime runtime)
        {
            return new RelationshipSaveParticipant(runtime);
        }

        public static ISaveParticipant CreateRngParticipant(IRngStreamService streams)
        {
            return new RngSaveParticipant(streams);
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

        private sealed class CalendarSaveParticipant : ISaveParticipant
        {
            private readonly CalendarRuntime _runtime;

            public CalendarSaveParticipant(CalendarRuntime runtime)
            {
                _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            }

            public string DomainKey => "calendar";

            public JsonNode CaptureState()
            {
                CalendarWorldSnapshot snapshot = _runtime.CaptureSnapshot();
                return new JsonObject
                {
                    ["enabled"] = snapshot.Enabled,
                    ["dayIndex"] = snapshot.DayIndex,
                    ["ticksIntoDay"] = snapshot.TicksIntoDay,
                    ["activeCalendarId"] = snapshot.ActiveCalendarId
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                JsonObject root = state.AsObject();
                try
                {
                    _runtime.RestoreSnapshot(new CalendarWorldSnapshot(
                        RequireBool(root, "enabled"),
                        RequireInt(root, "dayIndex"),
                        RequireInt(root, "ticksIntoDay"),
                        RequireString(root, "activeCalendarId")));
                }
                catch (InvalidOperationException ex)
                {
                    throw new SaveContextException($"Calendar save state is invalid: {ex.Message}");
                }
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
                        ["state"] = session.State.ToString(),
                        ["variables"] = WriteMapVariables(session.Variables)
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
                        ReadLaunchContext(session["launchContext"]),
                        ReadMapVariables(RequireObject(session["variables"], $"sessions[{i}].variables"), $"sessions[{i}].variables")));
                }

                JsonArray focusStackArray = RequireArray(root, "focusStack");
                var focusStack = new string[focusStackArray.Count];
                for (int i = 0; i < focusStackArray.Count; i++)
                {
                    focusStack[i] = RequireStringValue(focusStackArray[i], $"focusStack[{i}]");
                }

                try
                {
                    _manager.RestoreSnapshot(new MapSessionManagerSnapshot(sessions, focusStack));
                }
                catch (InvalidOperationException ex)
                {
                    throw new SaveContextException($"Map sessions save state is invalid: {ex.Message}");
                }
            }
        }

        private sealed class DialogueSaveParticipant : ISaveParticipant
        {
            private readonly DialogueRuntime _runtime;

            public DialogueSaveParticipant(DialogueRuntime runtime)
            {
                _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            }

            public string DomainKey => "dialogue";

            public JsonNode CaptureState()
            {
                DialogueRuntimeSnapshot snapshot = _runtime.CaptureSnapshot();
                var bindings = new JsonArray();
                for (int i = 0; i < snapshot.Bindings.Count; i++)
                {
                    DialogueBindingSnapshot binding = snapshot.Bindings[i];
                    bindings.Add(new JsonObject
                    {
                        ["alias"] = binding.Alias,
                        ["entity"] = WriteEntity(binding.Entity)
                    });
                }

                return new JsonObject
                {
                    ["bindings"] = bindings,
                    ["activeDialogue"] = WriteDialogueSession(snapshot.ActiveDialogue)
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));
                JsonObject root = state.AsObject();
                JsonArray bindingArray = RequireArray(root, "bindings");
                var bindings = new List<DialogueBindingSnapshot>(bindingArray.Count);
                for (int i = 0; i < bindingArray.Count; i++)
                {
                    JsonObject binding = RequireObject(bindingArray[i], $"bindings[{i}]");
                    bindings.Add(new DialogueBindingSnapshot(
                        RequireString(binding, "alias"),
                        ReadEntity(RequireObject(binding["entity"], $"bindings[{i}].entity"))));
                }

                _runtime.RestoreSnapshot(new DialogueRuntimeSnapshot(
                    bindings,
                    ReadDialogueSession(root["activeDialogue"])));
            }
        }

        private sealed class SequencerSaveParticipant : ISaveParticipant
        {
            private readonly SequencerRuntime _runtime;

            public SequencerSaveParticipant(SequencerRuntime runtime)
            {
                _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            }

            public string DomainKey => "sequencer";

            public JsonNode CaptureState()
            {
                SequencerSessionSnapshot? snapshot = _runtime.CaptureSnapshot();
                if (snapshot == null)
                {
                    return new JsonObject { ["active"] = null };
                }

                var fired = new JsonArray();
                for (int i = 0; i < snapshot.FiredSignalTrackIndices.Count; i++)
                {
                    fired.Add(snapshot.FiredSignalTrackIndices[i]);
                }

                return new JsonObject
                {
                    ["active"] = new JsonObject
                    {
                        ["sequenceId"] = snapshot.SequenceId,
                        ["time"] = snapshot.Time,
                        ["rate"] = snapshot.Rate,
                        ["paused"] = snapshot.Paused,
                        ["firedSignals"] = fired
                    }
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));
                JsonObject root = state.AsObject();
                if (root["active"] is not JsonObject active)
                {
                    _runtime.RestoreSnapshot(null);
                    return;
                }

                JsonArray firedArray = RequireArray(active, "firedSignals");
                var fired = new List<int>(firedArray.Count);
                for (int i = 0; i < firedArray.Count; i++)
                {
                    fired.Add(RequireIntValue(firedArray[i], $"firedSignals[{i}]"));
                }

                _runtime.RestoreSnapshot(new SequencerSessionSnapshot(
                    RequireString(active, "sequenceId"),
                    RequireSingle(active, "time"),
                    RequireSingle(active, "rate"),
                    RequireBool(active, "paused"),
                    fired));
            }
        }

        private sealed class ActivitySaveParticipant : ISaveParticipant
        {
            private readonly ActivityRuntimeService _runtime;

            public ActivitySaveParticipant(ActivityRuntimeService runtime)
            {
                _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            }

            public string DomainKey => "activities";

            public JsonNode CaptureState()
            {
                ActivityRuntimeSnapshot snapshot = _runtime.CaptureSnapshot();
                return new JsonObject
                {
                    ["nextInstanceId"] = snapshot.NextInstanceId
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                JsonObject root = state.AsObject();
                int nextInstanceId = RequireInt(root, "nextInstanceId");
                try
                {
                    _runtime.RestoreSnapshot(new ActivityRuntimeSnapshot(nextInstanceId));
                }
                catch (InvalidOperationException ex)
                {
                    throw new SaveContextException($"Activity save state is invalid: {ex.Message}");
                }
            }
        }

        private sealed class TaskSaveParticipant : ISaveParticipant
        {
            private readonly TaskRuntimeService _runtime;

            public TaskSaveParticipant(TaskRuntimeService runtime)
            {
                _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            }

            public string DomainKey => "tasks";

            public JsonNode CaptureState()
            {
                TaskRuntimeSnapshot snapshot = _runtime.CaptureSnapshot();
                var signals = new JsonObject();
                foreach (KeyValuePair<string, int> pair in snapshot.Signals)
                {
                    signals[pair.Key] = pair.Value;
                }

                var accumulators = new JsonObject();
                foreach (KeyValuePair<string, int> pair in snapshot.Accumulators)
                {
                    accumulators[pair.Key] = pair.Value;
                }

                return new JsonObject
                {
                    ["signals"] = signals,
                    ["accumulators"] = accumulators,
                    ["nextInstanceId"] = snapshot.NextInstanceId
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

                var accumulators = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                JsonObject accumulatorObject = RequireObject(root["accumulators"], "accumulators");
                foreach (KeyValuePair<string, JsonNode?> pair in accumulatorObject)
                {
                    accumulators[pair.Key] = RequireIntValue(pair.Value, $"accumulators.{pair.Key}");
                }

                int nextInstanceId = RequireInt(root, "nextInstanceId");
                try
                {
                    _runtime.RestoreSnapshot(new TaskRuntimeSnapshot(signals, accumulators, nextInstanceId));
                }
                catch (InvalidOperationException ex)
                {
                    throw new SaveContextException($"Task save state is invalid: {ex.Message}");
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

        private sealed class RngSaveParticipant : ISaveParticipant
        {
            private readonly IRngStreamService _streams;

            public RngSaveParticipant(IRngStreamService streams)
            {
                _streams = streams ?? throw new ArgumentNullException(nameof(streams));
            }

            public string DomainKey => "rng";

            public JsonNode CaptureState()
            {
                var ids = new List<string>(_streams.DeclaredStreamIds);
                ids.Sort(StringComparer.Ordinal);

                var streams = new JsonArray();
                for (int i = 0; i < ids.Count; i++)
                {
                    RngStream stream = _streams.GetStream(ids[i]);
                    RngStreamSnapshot snapshot = stream.CaptureSnapshot();
                    streams.Add(new JsonObject
                    {
                        ["stream"] = snapshot.StreamId,
                        ["seed"] = stream.DeclaredSeed,
                        ["state"] = snapshot.State,
                        ["position"] = snapshot.Position
                    });
                }

                return new JsonObject
                {
                    ["streams"] = streams
                };
            }

            public void RestoreState(JsonNode state)
            {
                if (state == null) throw new ArgumentNullException(nameof(state));

                JsonObject root = state.AsObject();
                JsonArray streamArray = RequireArray(root, "streams");
                var snapshots = new Dictionary<string, RngStreamSnapshot>(streamArray.Count, StringComparer.Ordinal);
                var seeds = new Dictionary<string, uint>(streamArray.Count, StringComparer.Ordinal);
                for (int i = 0; i < streamArray.Count; i++)
                {
                    JsonObject entry = RequireObject(streamArray[i], $"streams[{i}]");
                    string streamId = RequireString(entry, "stream");
                    if (!snapshots.TryAdd(streamId, new RngStreamSnapshot(
                            RequireUInt(entry, "state", $"streams[{i}]"),
                            RequireLong(entry, "position", $"streams[{i}]"),
                            streamId)))
                    {
                        throw new SaveContextException($"Rng save stream '{streamId}' is duplicated.");
                    }

                    seeds.Add(streamId, RequireUInt(entry, "seed", $"streams[{i}]"));
                }

                foreach (string streamId in snapshots.Keys)
                {
                    if (!_streams.DeclaredStreamIds.Contains(streamId))
                    {
                        throw new SaveContextException(
                            $"Rng save stream '{streamId}' is not declared in this session.");
                    }
                }

                foreach (string streamId in _streams.DeclaredStreamIds)
                {
                    if (!snapshots.ContainsKey(streamId))
                    {
                        throw new SaveContextException(
                            $"Rng save is missing declared stream '{streamId}'.");
                    }
                }

                foreach (KeyValuePair<string, RngStreamSnapshot> pair in snapshots)
                {
                    RngStream stream = _streams.GetStream(pair.Key);
                    if (stream.DeclaredSeed != seeds[pair.Key])
                    {
                        throw new SaveContextException(
                            $"Rng stream '{pair.Key}' declared seed does not match the save.");
                    }
                }

                foreach (KeyValuePair<string, RngStreamSnapshot> pair in snapshots)
                {
                    _streams.GetStream(pair.Key).RestoreSnapshot(pair.Value);
                }
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

        private static JsonNode? WriteDialogueSession(DialogueSessionSnapshot? dialogue)
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

        private static DialogueSessionSnapshot? ReadDialogueSession(JsonNode? node)
        {
            if (node == null || node.GetValueKind() == System.Text.Json.JsonValueKind.Null)
            {
                return null;
            }

            JsonObject obj = RequireObject(node, "activeDialogue");
            return new DialogueSessionSnapshot(
                RequireString(obj, "dialogueId"),
                RequireString(obj, "nodeId"),
                RequireSingle(obj, "elapsedSeconds"));
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

        private static uint RequireUInt(JsonObject root, string field, string path)
        {
            JsonNode? node = root[field];
            if (node == null)
            {
                throw new SaveContextException($"Save domain field '{path}.{field}' is missing.");
            }

            return node.GetValue<uint>();
        }

        private static long RequireLong(JsonObject root, string field, string path)
        {
            JsonNode? node = root[field];
            if (node == null)
            {
                throw new SaveContextException($"Save domain field '{path}.{field}' is missing.");
            }

            return node.GetValue<long>();
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

        private static JsonObject WriteMapVariables(MapVariableStoreSnapshot? snapshot)
        {
            if (snapshot == null)
            {
                throw new SaveContextException("Map session snapshot carries no variable store state.");
            }

            var variables = new JsonObject();
            for (int i = 0; i < snapshot.Variables.Count; i++)
            {
                MapVariableValueSnapshot entry = snapshot.Variables[i];
                var slotValue = new JsonObject
                {
                    ["type"] = entry.Type.ToString()
                };
                slotValue["value"] = entry.Type == MapVariableType.Int
                    ? JsonValue.Create(entry.IntValue)!
                    : JsonValue.Create(entry.FloatValue)!;
                variables[entry.Name] = slotValue;
            }

            return variables;
        }

        private static MapVariableStoreSnapshot ReadMapVariables(JsonObject variables, string field)
        {
            var entries = new List<MapVariableValueSnapshot>(variables.Count);
            foreach (KeyValuePair<string, JsonNode?> pair in variables)
            {
                JsonObject entry = RequireObject(pair.Value, $"{field}.{pair.Key}");
                string typeText = RequireString(entry, "type");
                if (!Enum.TryParse(typeText, ignoreCase: false, out MapVariableType type) ||
                    !string.Equals(type.ToString(), typeText, StringComparison.Ordinal))
                {
                    throw new SaveContextException($"Map variable '{pair.Key}' type '{typeText}' at {field} is invalid.");
                }

                double raw = RequireDouble(entry, "value", $"{field}.{pair.Key}.value");
                int intValue = 0;
                float floatValue = 0f;
                if (type == MapVariableType.Int)
                {
                    if (Math.Floor(raw) != raw || raw > int.MaxValue || raw < int.MinValue)
                    {
                        throw new SaveContextException($"Map variable '{pair.Key}' int value {raw} at {field} is invalid.");
                    }

                    intValue = (int)raw;
                }
                else
                {
                    floatValue = (float)raw;
                }

                entries.Add(new MapVariableValueSnapshot(pair.Key, type, intValue, floatValue));
            }

            return new MapVariableStoreSnapshot(entries);
        }

        private static double RequireDouble(JsonObject root, string field, string label)
        {
            JsonNode? node = root[field];
            if (node == null)
            {
                throw new SaveContextException($"Save domain field '{label}' is missing.");
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out int integer)) return integer;
                if (value.TryGetValue<long>(out long longInteger)) return longInteger;
                if (value.TryGetValue<float>(out float single)) return single;
                if (value.TryGetValue<double>(out double number)) return number;
            }

            throw new SaveContextException($"Save domain field '{label}' must be a number.");
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
