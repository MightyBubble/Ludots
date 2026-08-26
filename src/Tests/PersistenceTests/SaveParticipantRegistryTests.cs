using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Relationships;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.Story;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class SaveParticipantRegistryTests
{
    [Test]
    public void RegistryCapturesAndRestoresRegisteredDomainState()
    {
        var participant = new RecordingParticipant("sample", JsonValue.Create(42)!);
        var registry = new SaveParticipantRegistry();
        registry.Register(participant);

        JsonObject domains = registry.CaptureDomains();
        participant.NextState = JsonValue.Create(7)!;

        registry.RestoreDomains(domains);

        Assert.That(participant.RestoredState?.GetValue<int>(), Is.EqualTo(42));
    }

    [Test]
    public void RegistryRejectsDuplicateDomainKeys()
    {
        var registry = new SaveParticipantRegistry();
        registry.Register(new RecordingParticipant("sample", JsonValue.Create(1)!));

        var error = Assert.Throws<SaveContextException>(
            () => registry.Register(new RecordingParticipant("sample", JsonValue.Create(2)!)));

        Assert.That(error!.Message, Does.Contain("sample"));
        Assert.That(error.Message, Does.Contain("duplicate"));
    }

    [Test]
    public void RestoreFailsFastWhenSaveContainsUnknownDomain()
    {
        var registry = new SaveParticipantRegistry();
        registry.Register(new RecordingParticipant("known", JsonValue.Create(1)!));

        var domains = new JsonObject
        {
            ["known"] = JsonValue.Create(1),
            ["unknown"] = JsonValue.Create(2)
        };

        var error = Assert.Throws<SaveContextException>(() => registry.RestoreDomains(domains));

        Assert.That(error!.Message, Does.Contain("unknown"));
        Assert.That(error.Message, Does.Contain("domain"));
    }

    [Test]
    public void CoreParticipantRegistrationCoversRequiredSaveDomains()
    {
        using GameEngine engine = CreateInitializedEngine();
        var registry = new SaveParticipantRegistry();

        CoreSaveParticipants.RegisterCore(engine, registry);

        string[] domains = registry.Participants
            .Select(participant => participant.DomainKey)
            .OrderBy(domain => domain, StringComparer.Ordinal)
            .ToArray();

        Assert.That(domains, Is.EqualTo(new[]
        {
            "activities",
            "clock",
            "gameSession",
            "inventory",
            "mapSessions",
            "narrative",
            "relationships",
            "rng",
            "tasks",
            "teams",
            "timeFlow"
        }));
    }

    [Test]
    public void EngineExposesSaveParticipantRegistryAsCoreService()
    {
        using GameEngine engine = CreateInitializedEngine();

        SaveParticipantRegistry registry = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.SaveParticipants);

        Assert.That(registry, Is.Not.Null);
        Assert.That(
            registry.Participants.Select(participant => participant.DomainKey),
            Does.Contain("gameSession"));
    }

    [Test]
    public void ClockParticipantRestoresDomainTicks()
    {
        var source = new DiscreteClock();
        source.Advance(ClockDomainId.FixedFrame, 7);
        source.Advance(ClockDomainId.Step, 3);

        var target = new DiscreteClock();
        target.Advance(ClockDomainId.FixedFrame, 99);

        ISaveParticipant participant = CoreSaveParticipants.CreateClockParticipant(source);
        ISaveParticipant targetParticipant = CoreSaveParticipants.CreateClockParticipant(target);

        targetParticipant.RestoreState(participant.CaptureState());

        Assert.That(target.Now(ClockDomainId.FixedFrame), Is.EqualTo(7));
        Assert.That(target.Now(ClockDomainId.Step), Is.EqualTo(3));
    }

    [Test]
    public void GameSessionParticipantRestoresTickGlobalsAndPlayers()
    {
        var source = new GameSession();
        var sourcePlayer = new Player(1, NullInputSource.Instance) { TeamId = 2 };
        sourcePlayer.Camera.TargetCm = new(1200, 3400);
        sourcePlayer.Camera.Yaw = 77;
        source.AddPlayer(sourcePlayer);
        source.Globals["score"] = 12;
        source.Globals["label"] = "alpha";
        source.FixedUpdate();
        source.FixedUpdate();

        var target = new GameSession();
        target.AddPlayer(new Player(9, NullInputSource.Instance) { TeamId = 4 });

        ISaveParticipant participant = CoreSaveParticipants.CreateGameSessionParticipant(source);
        ISaveParticipant targetParticipant = CoreSaveParticipants.CreateGameSessionParticipant(target);

        JsonNode captured = participant.CaptureState();
        Assert.That(captured.AsObject().ContainsKey("localPlayerId"), Is.False);
        Assert.That(captured.AsObject().ContainsKey("camera"), Is.False);

        targetParticipant.RestoreState(captured);

        Assert.That(target.CurrentTick, Is.EqualTo(2));
        Assert.That(target.Players.Select(player => player.Id).ToArray(), Is.EqualTo(new[] { 1 }));
        Assert.That(target.Players[0].TeamId, Is.EqualTo(2));
        Assert.That(target.Globals["score"], Is.EqualTo(12));
        Assert.That(target.Globals["label"], Is.EqualTo("alpha"));
        Assert.That(target.Players[0].Camera.TargetCm, Is.EqualTo(sourcePlayer.Camera.TargetCm));
        Assert.That(target.Players[0].Camera.Yaw, Is.EqualTo(77));
    }

    [Test]
    public void GameSessionParticipant_RejectsLegacyLocalPlayerIdField()
    {
        var target = new GameSession();
        ISaveParticipant targetParticipant = CoreSaveParticipants.CreateGameSessionParticipant(target);
        var legacy = new JsonObject
        {
            ["currentTick"] = 0,
            ["localPlayerId"] = 1,
            ["players"] = new JsonArray(),
            ["globals"] = new JsonObject(),
        };

        Assert.Throws<SaveContextException>(() => targetParticipant.RestoreState(legacy));
    }

    [Test]
    public void GameSessionParticipant_RejectsLegacyRootCameraField()
    {
        var target = new GameSession();
        ISaveParticipant targetParticipant = CoreSaveParticipants.CreateGameSessionParticipant(target);
        var legacy = new JsonObject
        {
            ["currentTick"] = 0,
            ["players"] = new JsonArray(),
            ["globals"] = new JsonObject(),
            ["camera"] = new JsonObject
            {
                ["targetX"] = 0,
                ["targetY"] = 0,
                ["targetHeightCm"] = 0,
                ["yaw"] = 0,
                ["pitch"] = 0,
                ["distanceCm"] = 1000,
                ["fovYDeg"] = 60,
                ["rigKind"] = "Orbit",
                ["zoomLevel"] = 0,
                ["isFollowing"] = false,
            },
        };

        Assert.Throws<SaveContextException>(() => targetParticipant.RestoreState(legacy));
    }

    [Test]
    public void TimeFlowParticipantRestoresDomainHierarchyScaleAndPauseTokens()
    {
        var source = new TimeFlowService();
        source.EnsureDomain("simulation.bullets", TimeFlowDomainIds.Simulation, 1500);
        source.AcquireScaleToken(TimeFlowDomainIds.Simulation, 500, owner: "test", reason: "simulation scale token");
        source.AcquirePauseToken("simulation.bullets", owner: "test", reason: "modal");

        var target = new TimeFlowService();
        target.EnsureDomain("simulation.bullets", TimeFlowDomainIds.Simulation, 1000);

        ISaveParticipant participant = CoreSaveParticipants.CreateTimeFlowParticipant(source);
        ISaveParticipant targetParticipant = CoreSaveParticipants.CreateTimeFlowParticipant(target);

        targetParticipant.RestoreState(participant.CaptureState());

        Assert.That(target.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(500));
        Assert.That(target.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(500));
        Assert.That(target.GetEffectiveScalePermille("simulation.bullets"), Is.EqualTo(0));
        Assert.That(target.IsPaused("simulation.bullets"), Is.True);
    }

    [Test]
    public void TeamParticipantRestoresDefaultAndAsymmetricRelationships()
    {
        TeamRelationshipSnapshot original = TeamManager.CaptureSnapshot();
        try
        {
            TeamManager.Clear();
            TeamManager.DefaultRelationship = TeamRelationship.Hostile;
            TeamManager.SetRelationship(1, 2, TeamRelationship.Friendly);
            TeamManager.SetRelationship(2, 1, TeamRelationship.Neutral);

            ISaveParticipant participant = CoreSaveParticipants.CreateTeamParticipant();
            JsonNode state = participant.CaptureState();

            TeamManager.Clear();
            TeamManager.DefaultRelationship = TeamRelationship.Neutral;
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);

            participant.RestoreState(state);

            Assert.That(TeamManager.DefaultRelationship, Is.EqualTo(TeamRelationship.Hostile));
            Assert.That(TeamManager.GetRelationship(1, 2), Is.EqualTo(TeamRelationship.Friendly));
            Assert.That(TeamManager.GetRelationship(2, 1), Is.EqualTo(TeamRelationship.Neutral));
        }
        finally
        {
            TeamManager.RestoreSnapshot(original);
        }
    }

    [Test]
    public void MapSessionParticipantRestoresFocusStackAndSessionStates()
    {
        var source = new MapSessionManager();
        source.CreateSession(new MapId("outer"), new Ludots.Core.Config.MapConfig());
        source.PushFocused(new MapId("outer"));
        source.CreateSession(new MapId("inner"), new Ludots.Core.Config.MapConfig());
        source.PushFocused(new MapId("inner"));

        var target = new MapSessionManager();
        target.CreateSession(new MapId("outer"), new Ludots.Core.Config.MapConfig());
        target.PushFocused(new MapId("outer"));
        target.CreateSession(new MapId("inner"), new Ludots.Core.Config.MapConfig());

        ISaveParticipant participant = CoreSaveParticipants.CreateMapSessionsParticipant(source);
        ISaveParticipant targetParticipant = CoreSaveParticipants.CreateMapSessionsParticipant(target);

        targetParticipant.RestoreState(participant.CaptureState());

        Assert.That(target.FocusedSession?.MapId.Value, Is.EqualTo("inner"));
        Assert.That(target.HasPendingReturn, Is.True);
        Assert.That(target.GetSession(new MapId("outer"))?.State, Is.EqualTo(MapSessionState.Suspended));
        Assert.That(target.GetSession(new MapId("inner"))?.State, Is.EqualTo(MapSessionState.Active));
    }

    [Test]
    public void MapSessionParticipant_RoundTripsLaunchContextLocalSeatsAndMetadata()
    {
        var source = new MapSessionManager();
        MapSession sourceSession = source.CreateSession(new MapId("seats"), new Ludots.Core.Config.MapConfig());
        sourceSession.LaunchContext = MapLaunchContext.Create(
            new[]
            {
                new LocalSeatLaunchBinding("seat.0", 1, "scheme.wasd"),
                new LocalSeatLaunchBinding("seat.1", 2, null),
            },
            new Dictionary<string, object> { ["difficulty"] = "hard" });
        source.PushFocused(new MapId("seats"));

        var target = new MapSessionManager();
        target.CreateSession(new MapId("seats"), new Ludots.Core.Config.MapConfig());

        ISaveParticipant participant = CoreSaveParticipants.CreateMapSessionsParticipant(source);
        ISaveParticipant targetParticipant = CoreSaveParticipants.CreateMapSessionsParticipant(target);

        JsonNode captured = participant.CaptureState();
        JsonNode? seatZero = captured["sessions"]![0]!["launchContext"]!["localSeats"]![0];
        JsonNode? seatOne = captured["sessions"]![0]!["launchContext"]!["localSeats"]![1];
        Assert.That(seatZero!["controlSchemeId"]!.GetValue<string>(), Is.EqualTo("scheme.wasd"));
        Assert.That(seatZero["seatId"]!.GetValue<string>(), Is.EqualTo("seat.0"));
        Assert.That(seatZero["playerId"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(seatOne is JsonObject { Count: > 0 } seatOneObject && seatOneObject.ContainsKey("controlSchemeId"), Is.False,
            "an undeclared controlSchemeId is omitted instead of serialized as an empty default.");

        targetParticipant.RestoreState(captured);

        MapLaunchContext? restored = target.GetSession(new MapId("seats"))!.LaunchContext;
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.LocalSeats.Count, Is.EqualTo(2));
        Assert.That(restored.LocalSeats[0].SeatId, Is.EqualTo("seat.0"));
        Assert.That(restored.LocalSeats[0].PlayerId, Is.EqualTo(1));
        Assert.That(restored.LocalSeats[0].ControlSchemeId, Is.EqualTo("scheme.wasd"));
        Assert.That(restored.LocalSeats[1].SeatId, Is.EqualTo("seat.1"));
        Assert.That(restored.LocalSeats[1].PlayerId, Is.EqualTo(2));
        Assert.That(restored.LocalSeats[1].ControlSchemeId, Is.Null);
        Assert.That(restored.Metadata!, Is.Not.Null);
        Assert.That(restored.Metadata!["difficulty"], Is.EqualTo("hard"));
    }

    [Test]
    public void MapSessionParticipant_RejectsLegacyLaunchContextLocalPlayerId()
    {
        var target = new MapSessionManager();
        target.CreateSession(new MapId("outer"), new Ludots.Core.Config.MapConfig());
        ISaveParticipant targetParticipant = CoreSaveParticipants.CreateMapSessionsParticipant(target);
        var legacy = new JsonObject
        {
            ["sessions"] = new JsonArray
            {
                new JsonObject
                {
                    ["mapId"] = "outer",
                    ["state"] = MapSessionState.Active.ToString(),
                    ["launchContext"] = new JsonObject
                    {
                        ["localPlayerId"] = 1,
                    },
                },
            },
            ["focusStack"] = new JsonArray { "outer" },
        };

        Assert.Throws<SaveContextException>(() => targetParticipant.RestoreState(legacy));
    }

    [Test]
    public void DialogueParticipantRestoresActiveDialogueSession()
    {
        using GameEngine engine = CreateInitializedEngine();
        DialogueRuntime runtime = engine.GetService(CoreServiceKeys.DialogueRuntime)
            ?? throw new InvalidOperationException("DialogueRuntime missing.");
        DialogueDefinitionRegistry dialogues = engine.GetService(CoreServiceKeys.DialogueDefinitions)
            ?? throw new InvalidOperationException("Dialogue definitions missing.");
        StoryDefinitionRegistry story = engine.GetService(CoreServiceKeys.StoryDefinitions)
            ?? throw new InvalidOperationException("Story definitions missing.");

        story.Register(new StoryLineDefinition
        {
            Id = "line.test.hello",
            SpeakerId = "speaker.guide",
            TextToken = "story.test.hello"
        });
        story.Register(new StoryPresentationProfileDefinition
        {
            Id = "story.dialogue_overlay",
            Backend = StoryPresentationBackend.ScreenOverlay,
            SurfaceKind = "OverlayDialogue",
            Anchor = "BottomCenter"
        });
        dialogues.Register(new DialogueDefinition
        {
            Id = "dialogue.test.briefing",
            EntryNode = "hello",
            Nodes =
            {
                new DialogueNodeDefinition
                {
                    Id = "hello",
                    LineId = "line.test.hello",
                    PresentationProfile = "story.dialogue_overlay",
                    AutoAdvanceSeconds = 5f
                }
            }
        });

        runtime.RestoreSnapshot(new DialogueRuntimeSnapshot(
            Array.Empty<DialogueBindingSnapshot>(),
            new DialogueSessionSnapshot("dialogue.test.briefing", "hello", 1.25f)));
        ISaveParticipant participant = CoreSaveParticipants.CreateDialogueParticipant(runtime);
        var captured = participant.CaptureState();

        runtime.ResetState();
        Assert.That(runtime.HasActiveDialogue, Is.False);
        participant.RestoreState(captured);

        Assert.That(runtime.HasActiveDialogue, Is.True);
        DialogueRuntimeSnapshot restored = runtime.CaptureSnapshot();
        Assert.That(restored.ActiveDialogue, Is.Not.Null);
        Assert.That(restored.ActiveDialogue!.NodeId, Is.EqualTo("hello"));
        Assert.That(restored.ActiveDialogue.ElapsedSeconds, Is.EqualTo(1.25f).Within(0.001f));
    }

    [Test]
    public void RetiredQuestSaveDomain_IsRejectedWithReadableError()
    {
        var registry = new SaveParticipantRegistry();
        registry.Register(new SaveParticipantStub("tasks"));

        var domains = new JsonObject
        {
            ["tasks"] = new JsonObject(),
            ["quests"] = new JsonObject
            {
                ["signals"] = new JsonObject()
            }
        };

        SaveContextException error = Assert.Throws<SaveContextException>(() => registry.RestoreDomains(domains));
        Assert.That(error.Message, Does.Contain("quests"));
        Assert.That(error.Message, Does.Contain("retired"));
    }

    private sealed class SaveParticipantStub : ISaveParticipant
    {
        public SaveParticipantStub(string domainKey)
        {
            DomainKey = domainKey;
        }

        public string DomainKey { get; }

        public JsonNode CaptureState() => new JsonObject();

        public void RestoreState(JsonNode state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
        }
    }

    [Test]
    public void RelationshipParticipantRebuildsIndexFromWorld()
    {
        using World world = World.Create();
        var types = new RelationshipTypeRegistry();
        var metrics = new RelationshipMetricRegistry();
        int typeId = types.Register("Tests.Relationship.RestoreParticipant");
        Entity source = world.Create();
        Entity target = world.Create();
        var edgeSet = default(RelationshipEdgeSet);
        edgeSet.Set(typeId, RelationshipEdge.CreateDefault(metrics));
        source.AddRelationship(target, edgeSet);
        var runtime = new RelationshipRuntime(
            world,
            types,
            metrics,
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(world));
        Entity relationEntity = world.Create(new RelationshipInstanceCm
        {
            Source = source,
            Target = target,
            TypeId = typeId,
            Revision = 9
        });

        ISaveParticipant participant = CoreSaveParticipants.CreateRelationshipParticipant(runtime);

        participant.RestoreState(new JsonObject());

        Assert.That(runtime.TryResolveRelationshipEntity(source, target, typeId, out Entity resolved), Is.True);
        Assert.That(resolved, Is.EqualTo(relationEntity));
    }

    [Test]
    public void RelationshipParticipantRejectsProjectionWithoutMatchingEdge()
    {
        using World world = World.Create();
        var types = new RelationshipTypeRegistry();
        int typeId = types.Register("Tests.Relationship.OrphanParticipant");
        Entity source = world.Create();
        Entity target = world.Create();
        var runtime = new RelationshipRuntime(
            world,
            types,
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(world));
        world.Create(new RelationshipInstanceCm
        {
            Source = source,
            Target = target,
            TypeId = typeId,
            Revision = 1
        });

        ISaveParticipant participant = CoreSaveParticipants.CreateRelationshipParticipant(runtime);

        var error = Assert.Throws<SaveContextException>(() => participant.RestoreState(new JsonObject()));

        Assert.That(error!.Message, Does.Contain("Relationship save state is invalid"));
        Assert.That(error.Message, Does.Contain("no matching relationship edge"));
    }

    private sealed class RecordingParticipant : ISaveParticipant
    {
        public RecordingParticipant(string domainKey, JsonNode nextState)
        {
            DomainKey = domainKey;
            NextState = nextState;
        }

        public string DomainKey { get; }
        public JsonNode NextState { get; set; }
        public JsonNode? RestoredState { get; private set; }

        public JsonNode CaptureState()
        {
            return NextState.DeepClone();
        }

        public void RestoreState(JsonNode state)
        {
            RestoredState = state.DeepClone();
        }
    }

    private sealed class NullInputSource : IInputSource
    {
        public static readonly NullInputSource Instance = new();

        public PlayerInputFrame GetInput(int tick)
        {
            return new PlayerInputFrame { Tick = tick };
        }
    }

    private static GameEngine CreateInitializedEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
            Path.Combine(repoRoot, "assets"));
        engine.LoadStartupMap();
        return engine;
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string gitPath = Path.Combine(dir, ".git");
            if ((Directory.Exists(gitPath) || File.Exists(gitPath)) &&
                Directory.Exists(Path.Combine(dir, "src")) &&
                Directory.Exists(Path.Combine(dir, "mods")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Repository root not found from test directory.");
    }
}
