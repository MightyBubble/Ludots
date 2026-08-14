using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using GraphAiShowcaseCommon;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class GraphAiShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;

    private static readonly GraphShowcaseCase LevelBlueprint = new(
        "GraphLevelBlueprintShowcaseMod",
        "mods/showcases/graph_level_blueprint/GraphLevelBlueprintShowcaseMod",
        "graph_level_blueprint_showcase",
        "GraphAiShowcase.LevelBlueprint.Runtime",
        "LevelBlueprint",
        "graph_level_blueprint_raylib",
        9);

    private static readonly GraphShowcaseCase StanceFsm = new(
        "GraphStanceFsmShowcaseMod",
        "mods/showcases/graph_stance_fsm/GraphStanceFsmShowcaseMod",
        "graph_stance_fsm_showcase",
        "GraphAiShowcase.StanceFsm.Runtime",
        "StanceFsm",
        "graph_stance_fsm_raylib",
        8);

    private static readonly GraphShowcaseCase ComplexBt = new(
        "GraphComplexBtShowcaseMod",
        "mods/showcases/graph_complex_bt/GraphComplexBtShowcaseMod",
        "graph_complex_bt_showcase",
        "GraphAiShowcase.ComplexBt.Runtime",
        "ComplexBt",
        "graph_complex_bt_raylib",
        9);

    [Test]
    public void PlayerOpensLevelBlueprint_ThenGraphAdvancesTheLevelFlow()
    {
        using GameEngine engine = CreateEngine(LevelBlueprint.ModName);
        engine.Start();
        engine.LoadMap(MapLoadRequest.FromMapId(LevelBlueprint.MapId, MapLaunchContext.Create(1)));

        GraphAiShowcaseRuntime runtime = ResolveRuntime(engine, LevelBlueprint.RuntimeKey);
        Entity cursor = GetMapEntity(engine, "graph-level-flow-cursor");
        Entity exit = GetMapEntity(engine, "graph-level-open-exit");
        Entity doorGate = GetMapEntity(engine, "graph-level-door-gate");
        Entity patrolSpawn = GetMapEntity(engine, "graph-level-patrol-spawn-zone");
        Entity objectiveBeacon = GetMapEntity(engine, "graph-level-objective-beacon");
        Entity exitGate = GetMapEntity(engine, "graph-level-exit-gate");
        AssertDynamicPresentationEntity(engine, cursor);
        AssertDynamicPresentationEntity(engine, exit);
        AssertDynamicPresentationEntity(engine, doorGate);
        AssertDynamicPresentationEntity(engine, patrolSpawn);
        AssertDynamicPresentationEntity(engine, objectiveBeacon);
        AssertDynamicPresentationEntity(engine, exitGate);
        WorldCmInt2 cursorStart = GetPosition(engine, cursor);
        WorldCmInt2 exitStart = GetPosition(engine, exit);
        WorldCmInt2 doorGateStart = GetPosition(engine, doorGate);
        WorldCmInt2 patrolSpawnStart = GetPosition(engine, patrolSpawn);
        WorldCmInt2 objectiveBeaconStart = GetPosition(engine, objectiveBeacon);
        WorldCmInt2 exitGateStart = GetPosition(engine, exitGate);
        Vector3 cursorVisualStart = GetVisualPosition(engine, cursor);
        Vector3 exitVisualStart = GetVisualPosition(engine, exit);
        Vector3 doorGateVisualStart = GetVisualPosition(engine, doorGate);
        Vector3 exitGateVisualStart = GetVisualPosition(engine, exitGate);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Contain("level_blueprint"));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Entities, Has.Count.EqualTo(LevelBlueprint.ExpectedMapEntityCount));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Teams, Has.Count.EqualTo(1));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Players, Has.Count.EqualTo(1));
        AssertSnapshot(runtime.Snapshot, LevelBlueprint, expectedProgram: "level_blueprint_opening");

        TickUntilSnapshotTick(engine, runtime, 3, maxFrames: 240);
        Assert.That(runtime.Snapshot.StateLabel, Is.EqualTo("Spawn Patrol"));
        Assert.That(runtime.Snapshot.IntentLabel, Is.EqualTo("Spawn Patrol"));
        Tick(engine, 60);
        Assert.That(DistanceCm(GetPosition(engine, cursor), cursorStart), Is.GreaterThan(500f));
        Assert.That(DistanceCm(GetPosition(engine, doorGate), doorGateStart), Is.GreaterThan(250f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, cursor), cursorVisualStart), Is.GreaterThan(4.5f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, doorGate), doorGateVisualStart), Is.GreaterThan(2.0f));
        AssertHotPath(runtime.Snapshot);

        TickUntilSnapshotTick(engine, runtime, 6, maxFrames: 420);
        Assert.That(runtime.Snapshot.StateLabel, Is.EqualTo("Secure Objective"));
        Assert.That(runtime.Snapshot.IntentLabel, Is.EqualTo("Mark Objective"));
        Tick(engine, 60);
        Assert.That(DistanceCm(GetPosition(engine, patrolSpawn), patrolSpawnStart), Is.GreaterThan(120f));
        Assert.That(DistanceCm(GetPosition(engine, objectiveBeacon), objectiveBeaconStart), Is.GreaterThan(180f));

        TickUntilSnapshotTick(engine, runtime, 9, maxFrames: 660);
        Assert.That(runtime.Snapshot.StateLabel, Is.EqualTo("Open Exit"));
        Assert.That(runtime.Snapshot.IntentLabel, Is.EqualTo("Unlock Exit"));
        Tick(engine, 60);
        Assert.That(GetPosition(engine, exit).X, Is.GreaterThan(exitStart.X + 300));
        Assert.That(GetPosition(engine, exitGate).X, Is.GreaterThan(exitGateStart.X + 300));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, exit), exitVisualStart), Is.GreaterThan(3.0f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, exitGate), exitGateVisualStart), Is.GreaterThan(3.0f));
        Assert.That(runtime.Snapshot.Actors, Is.Empty);
    }

    [Test]
    public void PlayerOpensRtsStanceFsm_ThenSquadsResolveDifferentGraphStances()
    {
        using GameEngine engine = CreateEngine(StanceFsm.ModName);
        engine.Start();
        engine.LoadMap(MapLoadRequest.FromMapId(StanceFsm.MapId, MapLaunchContext.Create(1)));

        GraphAiShowcaseRuntime runtime = ResolveRuntime(engine, StanceFsm.RuntimeKey);
        Entity sentry = GetMapEntity(engine, "graph-stance-forward-sentry");
        Entity guard = GetMapEntity(engine, "graph-stance-line-guard");
        Entity raider = GetMapEntity(engine, "graph-stance-damaged-raider");
        Entity observer = GetMapEntity(engine, "graph-stance-silent-observer");
        AssertDynamicPresentationEntity(engine, sentry);
        AssertDynamicPresentationEntity(engine, guard);
        AssertDynamicPresentationEntity(engine, raider);
        AssertDynamicPresentationEntity(engine, observer);
        WorldCmInt2 sentryStart = GetPosition(engine, sentry);
        WorldCmInt2 guardStart = GetPosition(engine, guard);
        WorldCmInt2 raiderStart = GetPosition(engine, raider);
        WorldCmInt2 observerStart = GetPosition(engine, observer);
        Vector3 sentryVisualStart = GetVisualPosition(engine, sentry);
        Vector3 guardVisualStart = GetVisualPosition(engine, guard);
        Vector3 raiderVisualStart = GetVisualPosition(engine, raider);
        Vector3 observerVisualStart = GetVisualPosition(engine, observer);
        TickUntilSnapshotTick(engine, runtime, 1, maxFrames: 60);
        Tick(engine, 120);
        GraphAiShowcaseSnapshot snapshot = runtime.Snapshot;
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Contain("stance_fsm"));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Entities, Has.Count.EqualTo(StanceFsm.ExpectedMapEntityCount));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Teams, Has.Count.EqualTo(1));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Players, Has.Count.EqualTo(1));
        AssertSnapshot(snapshot, StanceFsm, expectedProgram: "rts_stance_fsm");
        Assert.That(snapshot.Actors, Has.Length.EqualTo(4));
        Assert.That(snapshot.Actors.Select(actor => actor.StateLabel), Is.EquivalentTo(new[]
        {
            "Attack Anything",
            "Defend",
            "Return Fire",
            "Hold Fire",
        }));
        Assert.That(snapshot.Actors.Select(actor => actor.IntentLabel), Is.EquivalentTo(new[]
        {
            "Engage Threat",
            "Hold Lane",
            "Recover",
            "Observe",
        }));
        Assert.That(snapshot.Actors.Select(actor => actor.ActionLabel), Is.EquivalentTo(new[]
        {
            "attack red threat",
            "hold blue defense line",
            "retreat to green cover",
            "observe from watch point",
        }));

        GraphAiActorSnapshot damaged = snapshot.Actors.Single(actor => actor.Name == "Damaged Raider");
        Assert.That(damaged.StateLabel, Is.EqualTo("Return Fire"));
        Assert.That(damaged.IntentLabel, Is.EqualTo("Recover"));
        Assert.That(DistanceCm(GetPosition(engine, sentry), sentryStart), Is.GreaterThan(450f));
        Assert.That(DistanceCm(GetPosition(engine, guard), guardStart), Is.GreaterThan(160f));
        Assert.That(DistanceCm(GetPosition(engine, raider), raiderStart), Is.GreaterThan(420f));
        Assert.That(DistanceCm(GetPosition(engine, observer), observerStart), Is.GreaterThan(20f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, sentry), sentryVisualStart), Is.GreaterThan(4.0f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, guard), guardVisualStart), Is.GreaterThan(1.0f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, raider), raiderVisualStart), Is.GreaterThan(4.0f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, observer), observerVisualStart), Is.GreaterThan(0.2f));
        AssertHotPath(snapshot);
    }

    [Test]
    public void PlayerOpensComplexBt_ThenTasksRunCompleteAndReenterTheTree()
    {
        using GameEngine engine = CreateEngine(ComplexBt.ModName);
        engine.Start();
        engine.LoadMap(MapLoadRequest.FromMapId(ComplexBt.MapId, MapLaunchContext.Create(1)));

        GraphAiShowcaseRuntime runtime = ResolveRuntime(engine, ComplexBt.RuntimeKey);
        Entity scout = GetMapEntity(engine, "graph-bt-point-scout");
        Entity commander = GetMapEntity(engine, "graph-bt-field-commander");
        Entity breacher = GetMapEntity(engine, "graph-bt-wounded-breacher");
        Entity patrol = GetMapEntity(engine, "graph-bt-patrol-pair");
        AssertDynamicPresentationEntity(engine, scout);
        AssertDynamicPresentationEntity(engine, commander);
        AssertDynamicPresentationEntity(engine, breacher);
        AssertDynamicPresentationEntity(engine, patrol);
        WorldCmInt2 scoutStart = GetPosition(engine, scout);
        WorldCmInt2 commanderStart = GetPosition(engine, commander);
        WorldCmInt2 breacherStart = GetPosition(engine, breacher);
        WorldCmInt2 patrolStart = GetPosition(engine, patrol);
        Vector3 scoutVisualStart = GetVisualPosition(engine, scout);
        Vector3 commanderVisualStart = GetVisualPosition(engine, commander);
        Vector3 breacherVisualStart = GetVisualPosition(engine, breacher);
        Vector3 patrolVisualStart = GetVisualPosition(engine, patrol);
        TickUntilSnapshotTick(engine, runtime, 1, maxFrames: 60);
        GraphAiShowcaseSnapshot first = runtime.Snapshot;
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Contain("complex_bt"));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Entities, Has.Count.EqualTo(ComplexBt.ExpectedMapEntityCount));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Teams, Has.Count.EqualTo(1));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Players, Has.Count.EqualTo(1));
        AssertSnapshot(first, ComplexBt, expectedProgram: "complex_bt_selector");
        Assert.That(first.Actors, Has.Length.EqualTo(4));
        Assert.That(first.Actors.All(actor => actor.TaskId > 0), Is.True);
        Assert.That(first.Actors.All(actor => actor.TaskRemainingTicks > 0), Is.True);
        Assert.That(first.Actors.Select(actor => actor.TaskLabel), Is.EquivalentTo(new[]
        {
            "Suppress Target",
            "Call Reinforcement",
            "Select Cover",
            "Scout Sweep",
        }));
        Assert.That(first.Actors.Select(actor => actor.ActionLabel), Is.EquivalentTo(new[]
        {
            "suppress red target",
            "call yellow reinforcement",
            "select green cover",
            "sweep cyan route",
        }));

        int patrolRemaining = first.Actors.Single(actor => actor.Name == "Patrol Pair").TaskRemainingTicks;
        TickUntilSnapshotTick(engine, runtime, first.Tick + 1, maxFrames: 90);
        GraphAiActorSnapshot patrolCountdown = runtime.Snapshot.Actors.Single(actor => actor.Name == "Patrol Pair");
        Assert.That(patrolCountdown.TaskRemainingTicks, Is.EqualTo(patrolRemaining - 1));
        Assert.That(DistanceCm(GetPosition(engine, scout), scoutStart), Is.GreaterThan(80f));
        Assert.That(DistanceCm(GetPosition(engine, commander), commanderStart), Is.GreaterThan(80f));
        Assert.That(DistanceCm(GetPosition(engine, breacher), breacherStart), Is.GreaterThan(80f));
        Assert.That(DistanceCm(GetPosition(engine, patrol), patrolStart), Is.GreaterThan(80f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, scout), scoutVisualStart), Is.GreaterThan(0.8f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, commander), commanderVisualStart), Is.GreaterThan(0.8f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, breacher), breacherVisualStart), Is.GreaterThan(0.8f));
        Assert.That(Vector3.Distance(GetVisualPosition(engine, patrol), patrolVisualStart), Is.GreaterThan(0.8f));
        AssertHotPath(runtime.Snapshot);

        TickUntilSnapshotTick(engine, runtime, first.Tick + 3, maxFrames: 240);
        GraphAiShowcaseSnapshot reentered = runtime.Snapshot;
        Assert.That(reentered.CompletedTasks, Is.GreaterThan(0));
        GraphAiActorSnapshot patrolReentered = reentered.Actors.Single(actor => actor.Name == "Patrol Pair");
        Assert.That(patrolReentered.TaskLabel, Is.EqualTo("Reposition Squad"));
        Assert.That(patrolReentered.TaskRemainingTicks, Is.GreaterThan(0));
    }

    [Test]
    public void GraphAiShowcases_AreStandaloneLaunchableEntries()
    {
        string repoRoot = FindRepoRoot();
        var cases = new[] { LevelBlueprint, StanceFsm, ComplexBt };

        string launcherConfig = File.ReadAllText(Path.Combine(repoRoot, "launcher.config.json"));
        string launcherPresets = File.ReadAllText(Path.Combine(repoRoot, "launcher.presets.json"));
        string registry = File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json"));

        foreach (GraphShowcaseCase item in cases)
        {
            string absoluteModPath = Path.Combine(repoRoot, item.ModPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(Path.Combine(absoluteModPath, "mod.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(absoluteModPath, "assets", "game.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(absoluteModPath, "assets", "Maps", item.MapId + ".json")), Is.True);
            Assert.That(File.Exists(Path.Combine(absoluteModPath, "assets", "GraphAiShowcase", "showcase.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(absoluteModPath, "assets", "Entities", "templates.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(absoluteModPath, "assets", "Presentation", "performers.json")), Is.True);
            Assert.That(
                File.Exists(Path.Combine(absoluteModPath, "bin", "net8.0", "GraphAiShowcaseCommon.dll")),
                Is.True,
                $"{item.ModName} must copy GraphAiShowcaseCommon.dll for launcher runtime loading.");

            AssertModOnlyDependsOnCore(absoluteModPath);
            AssertNoOldShowcaseSourceReferences(absoluteModPath);
            AssertPlayerReadableWorldTargets(absoluteModPath, item);

            string binding = item.MapId;
            Assert.That(launcherConfig, Does.Contain($"\"name\": \"{binding}\""));
            Assert.That(launcherPresets, Does.Contain($"\"id\": \"{item.PresetId}\""));
            Assert.That(registry, Does.Contain($"\"id\": \"{item.RegistryId}\""));
            Assert.That(registry, Does.Contain($"\"binding\": \"{binding}\""));
            Assert.That(registry, Does.Contain($"\"preset\": \"{item.PresetId}\""));
        }
    }

    private static GameEngine CreateEngine(string graphShowcaseMod)
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", graphShowcaseMod }),
            Path.Combine(repoRoot, "assets"));
        InstallDummyInput(engine);
        return engine;
    }

    private static void AssertSnapshot(GraphAiShowcaseSnapshot snapshot, GraphShowcaseCase expected, string expectedProgram)
    {
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Mode, Is.EqualTo(expected.Mode));
            Assert.That(snapshot.GraphProgramId, Is.EqualTo(expectedProgram));
            Assert.That(snapshot.GraphInstructionCount, Is.GreaterThan(10));
            Assert.That(snapshot.StateLabel, Does.Not.StartWith("#"));
            Assert.That(snapshot.IntentLabel, Does.Not.StartWith("#"));
            Assert.That(snapshot.Boundary, Is.Not.Empty);
        });
    }

    private static void AssertHotPath(GraphAiShowcaseSnapshot snapshot)
    {
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.HotPath.EntityCount, Is.EqualTo(50_000));
            Assert.That(snapshot.HotPath.LastGraphExecutions, Is.EqualTo(50_000));
            Assert.That(snapshot.HotPath.TotalGraphExecutions, Is.GreaterThanOrEqualTo(50_000));
            Assert.That(snapshot.HotPath.LastElapsedMicroseconds, Is.GreaterThan(0));
            Assert.That(snapshot.HotPath.LastAllocatedBytes, Is.EqualTo(0));
            Assert.That(snapshot.HotPath.LastGen0Collections, Is.EqualTo(0));
            Assert.That(snapshot.HotPath.IntentChecksum, Is.Not.EqualTo(0));
        });
    }

    private static GraphAiShowcaseRuntime ResolveRuntime(GameEngine engine, string runtimeKey)
    {
        return engine.GlobalContext.TryGetValue(runtimeKey, out object? runtimeObj) &&
               runtimeObj is GraphAiShowcaseRuntime runtime
            ? runtime
            : throw new InvalidOperationException($"Graph AI runtime '{runtimeKey}' missing.");
    }

    private static void AssertModOnlyDependsOnCore(string absoluteModPath)
    {
        using FileStream stream = File.OpenRead(Path.Combine(absoluteModPath, "mod.json"));
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement dependencies = document.RootElement.GetProperty("dependencies");
        Assert.That(dependencies.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[] { "LudotsCoreMod" }));
    }

    private static void AssertNoOldShowcaseSourceReferences(string absoluteModPath)
    {
        string[] forbidden =
        {
            "CombatStanceShowcase",
            "RtsStarCraft",
            "CncTriNation",
            "RtsDemo",
            "RtsShowcase",
            "RtsTraining",
        };

        foreach (string file in Directory.EnumerateFiles(absoluteModPath, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (string token in forbidden)
            {
                Assert.That(text, Does.Not.Contain(token), $"{file} must not reference old RTS/C&C/stance showcase source.");
            }
        }
    }

    private static void AssertPlayerReadableWorldTargets(string absoluteModPath, GraphShowcaseCase item)
    {
        GraphAiShowcaseConfig config = LoadShowcaseConfig(absoluteModPath);
        HashSet<string> mapInstanceIds = LoadMapInstanceIds(absoluteModPath, item.MapId);

        if (item.Mode == "LevelBlueprint")
        {
            Assert.That(config.LevelFlow.Steps, Has.Count.EqualTo(4));
            foreach (GraphAiLevelStepConfig step in config.LevelFlow.Steps)
            {
                Assert.That(step.Label, Is.Not.Empty);
                Assert.That(step.ActionLabel, Is.Not.Empty);
                Assert.That(mapInstanceIds, Does.Contain(step.InstanceId));
                Assert.That(mapInstanceIds, Does.Contain(step.TargetInstanceId));
            }

            return;
        }

        if (item.Mode == "StanceFsm")
        {
            AssertMotionTargets(config.WorldTargets.StanceByState, new[] { 0, 1, 2, 3 }, mapInstanceIds);
            return;
        }

        if (item.Mode == "ComplexBt")
        {
            AssertMotionTargets(config.WorldTargets.BehaviorByTask, new[] { 1, 2, 3, 4, 5 }, mapInstanceIds);
            return;
        }

        Assert.Fail($"Unknown graph showcase mode '{item.Mode}'.");
    }

    private static void AssertMotionTargets(
        IReadOnlyCollection<GraphAiMotionTargetConfig> targets,
        int[] expectedKeys,
        HashSet<string> mapInstanceIds)
    {
        Assert.That(targets.Select(target => target.Key), Is.EquivalentTo(expectedKeys));
        foreach (GraphAiMotionTargetConfig target in targets)
        {
            Assert.That(target.ActionLabel, Is.Not.Empty);
            Assert.That(target.SpeedCmPerSecond, Is.GreaterThan(0));
            Assert.That(mapInstanceIds, Does.Contain(target.InstanceId));
        }
    }

    private static GraphAiShowcaseConfig LoadShowcaseConfig(string absoluteModPath)
    {
        string configPath = Path.Combine(absoluteModPath, "assets", "GraphAiShowcase", "showcase.json");
        using FileStream stream = File.OpenRead(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<GraphAiShowcaseConfig>(stream, options)
            ?? throw new InvalidOperationException($"Graph AI showcase config '{configPath}' is empty.");
    }

    private static HashSet<string> LoadMapInstanceIds(string absoluteModPath, string mapId)
    {
        string mapPath = Path.Combine(absoluteModPath, "assets", "Maps", mapId + ".json");
        using FileStream stream = File.OpenRead(mapPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entity in document.RootElement.GetProperty("Entities").EnumerateArray())
        {
            instanceIds.Add(entity.GetProperty("InstanceId").GetString() ?? string.Empty);
        }

        return instanceIds;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        GasClockStepPolicy stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
        for (int i = 0; i < frames; i++)
        {
            if (stepPolicy.Mode == GasStepMode.Manual)
            {
                stepPolicy.RequestStep(1);
            }

            engine.Tick(DeltaTime);
        }
    }

    private static void TickUntilSnapshotTick(GameEngine engine, GraphAiShowcaseRuntime runtime, int targetTick, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (runtime.Snapshot.Tick >= targetTick)
            {
                return;
            }

            Tick(engine, 1);
        }

        Assert.Fail($"Graph AI showcase snapshot reached tick {runtime.Snapshot.Tick}, expected at least {targetTick}.");
    }

    private static Entity GetMapEntity(GameEngine engine, string instanceId)
    {
        if (engine.CurrentMapSession == null)
        {
            throw new InvalidOperationException($"No map session while resolving graph showcase entity '{instanceId}'.");
        }

        if (!engine.CurrentMapSession.EntityIndex.TryGet(instanceId, out Entity entity))
        {
            throw new InvalidOperationException($"Graph showcase entity '{instanceId}' was not loaded.");
        }

        return entity;
    }

    private static WorldCmInt2 GetPosition(GameEngine engine, Entity entity)
    {
        return engine.World.Get<WorldPositionCm>(entity).ToWorldCmInt2();
    }

    private static Vector3 GetVisualPosition(GameEngine engine, Entity entity)
    {
        return engine.World.Get<VisualTransform>(entity).Position;
    }

    private static void AssertDynamicPresentationEntity(GameEngine engine, Entity entity)
    {
        Assert.Multiple(() =>
        {
            Assert.That(engine.World.Has<WorldPositionCm>(entity), Is.True);
            Assert.That(engine.World.Has<PreviousWorldPositionCm>(entity), Is.True);
            Assert.That(engine.World.Has<VisualTransform>(entity), Is.True);
            Assert.That(engine.World.Has<PresentationStaticTransform>(entity), Is.False);
        });
    }

    private static float DistanceCm(WorldCmInt2 a, WorldCmInt2 b)
    {
        int dx = a.X - b.X;
        int dy = a.Y - b.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static void InstallDummyInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record GraphShowcaseCase(
        string ModName,
        string ModPath,
        string MapId,
        string RuntimeKey,
        string Mode,
        string PresetId,
        int ExpectedMapEntityCount)
    {
        public string RegistryId => MapId.Replace("_showcase", string.Empty);
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
