using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Relationships;
using Ludots.Core.Engine;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Quests;
using Ludots.Core.Gameplay.Relationships;
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
            "quests",
            "relationships",
            "tasks",
            "rng",
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
    public void NarrativeParticipantRestoresVariablesAndActiveDialogue()
    {
        using GameEngine engine = CreateInitializedEngine();
        var definitions = new NarrativeDefinitionRegistry();
        definitions.Register(new NarrativeVariableDefinition
        {
            Id = "trust",
            Kind = NarrativeValueKind.Int,
            DefaultInt = 1
        });
        definitions.Register(new NarrativeDialogueDefinition
        {
            Id = "briefing",
            StartNodeId = "hello",
            Nodes =
            {
                new NarrativeDialogueNodeDefinition
                {
                    Id = "hello",
                    SpeakerName = "Guide",
                    Text = "Trust is {trust}",
                    AutoAdvanceSeconds = 5f,
                    OnEnter =
                    {
                        new NarrativeActionDefinition
                        {
                            Kind = NarrativeActionKind.SetVariable,
                            VariableId = "trust",
                            ValueKind = NarrativeValueKind.Int,
                            IntValue = 7
                        }
                    }
                }
            }
        });

        QuestRuntimeService questRuntime = engine.GetService(CoreServiceKeys.QuestRuntimeService);
        var source = new NarrativeDirector(engine, definitions, questRuntime);
        source.StartDialogue("briefing");
        source.Update(1.25f);

        var target = new NarrativeDirector(engine, definitions, questRuntime);
        ISaveParticipant participant = CoreSaveParticipants.CreateNarrativeParticipant(source);
        ISaveParticipant targetParticipant = CoreSaveParticipants.CreateNarrativeParticipant(target);

        targetParticipant.RestoreState(participant.CaptureState());

        Assert.That(target.GetVariable("trust").IntValue, Is.EqualTo(7));
        Assert.That(target.HasActiveDialogue, Is.True);
        Assert.That(target.TryGetActiveDialogueView(out NarrativeDialogueView view), Is.True);
        Assert.That(view.NodeId, Is.EqualTo("hello"));
        Assert.That(view.ElapsedSeconds, Is.EqualTo(1.25f).Within(0.001f));
    }

    [Test]
    public void QuestParticipantRestoresSignalsAndRebuildsIndexFromWorld()
    {
        var definitions = new QuestDefinitionRegistry();
        definitions.Register("trial", new QuestDefinition
        {
            DisplayName = "Trial",
            Stages =
            {
                new QuestStageDefinition { Id = "start", Title = "Start" },
                new QuestStageDefinition
                {
                    Id = "done",
                    Title = "Done",
                    RequiredSignals = { "closed" }
                }
            }
        });

        using World sourceWorld = World.Create();
        var sourceRuntime = new QuestRuntimeService(sourceWorld, definitions);
        sourceRuntime.StartQuest("trial");
        sourceRuntime.EmitSignal("opened");

        using World targetWorld = World.Create();
        var targetRuntime = new QuestRuntimeService(targetWorld, definitions);
        targetWorld.Create(new QuestInstanceCm
        {
            DefinitionId = definitions.GetId("trial"),
            State = QuestState.Active,
            StageIndex = 1,
            Revision = 3
        });

        ISaveParticipant participant = CoreSaveParticipants.CreateQuestParticipant(sourceRuntime);
        ISaveParticipant targetParticipant = CoreSaveParticipants.CreateQuestParticipant(targetRuntime);

        targetParticipant.RestoreState(participant.CaptureState());

        Assert.That(targetRuntime.Signals.TryGetValue("opened", out int count), Is.True);
        Assert.That(count, Is.EqualTo(1));
        Assert.That(targetRuntime.TryGetQuestState("trial", out QuestState state, out string stageId), Is.True);
        Assert.That(state, Is.EqualTo(QuestState.Active));
        Assert.That(stageId, Is.EqualTo("done"));
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
