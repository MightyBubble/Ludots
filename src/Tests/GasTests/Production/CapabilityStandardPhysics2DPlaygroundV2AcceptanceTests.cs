using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Arch.Core;
using CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
public sealed class CapabilityStandardPhysics2DPlaygroundV2AcceptanceTests
{
    private const string ShowcaseModId = "CapabilityStandardPhysics2DPlaygroundV2Mod";
    private const string MapId = "capability_standard_physics2d_playground_v2";
    private const string PhysicsBodyTemplateId = "capability_standard_physics2d_playground_v2_physics_body";
    private const string PhysicsWallTemplateId = "capability_standard_physics2d_playground_v2_physics_wall";
    private const string NavAgentTemplateId = "capability_standard_physics2d_playground_v2_nav_agent";
    private const string NavObstacleTemplateId = "capability_standard_physics2d_playground_v2_nav_obstacle";
    private const string NavTargetTemplateId = "capability_standard_physics2d_playground_v2_nav_target";
    private const string BenchmarkBodyTemplateId = "capability_standard_physics2d_playground_v2_benchmark_body";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        ShowcaseModId
    };

    [Test]
    public void CapabilityStandardPhysics2DPlaygroundV2_ModePartitionsAndProductionPipeline_WriteAcceptance()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        AssertConfigAndTemplateBoundaries(repoRoot);

        using var engine = CapabilityStandardShowcaseTestHarness.CreateEngine(repoRoot, AcceptanceMods);
        Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo(MapId));
        Assert.That(engine.MergedConfig.Physics2D.Enabled, Is.True);
        Assert.That(engine.MergedConfig.Navigation2D.Enabled, Is.True);
        Assert.That(engine.GetService(CoreServiceKeys.Navigation2DRuntime), Is.Not.Null);

        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
        Physics2DSimulationSystem physics = CapabilityStandardShowcaseTestHarness.FindSystem<Physics2DSimulationSystem>(
            engine,
            SystemGroup.InputCollection);

        Assert.That(CountSystems<Physics2DSimulationSystem>(engine, SystemGroup.InputCollection), Is.EqualTo(1),
            "Playground v2 must use the production Physics2D system installed by game.json, without the legacy playground's manual simulation registration.");

        engine.LoadMap(MapId);
        CapabilityStandardPhysics2DPlaygroundV2InteractionSystem interaction =
            CapabilityStandardShowcaseTestHarness.FindSystem<CapabilityStandardPhysics2DPlaygroundV2InteractionSystem>(
                engine,
                SystemGroup.InputCollection);
        Assert.That(spawnQueue.Count, Is.EqualTo(5));

        var frameTimesMs = new List<double>(128);
        CapabilityStandardShowcaseTestHarness.TickUntil(engine, frameTimesMs, () => spawnQueue.Count == 0, maxFrames: 8);

        Entity physicsBody = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, PhysicsBodyTemplateId);
        Entity physicsWall = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, PhysicsWallTemplateId);
        Entity navAgent = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, NavAgentTemplateId);
        Entity navObstacle = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, NavObstacleTemplateId);
        Entity navTarget = CapabilityStandardShowcaseTestHarness.FindSingleByTemplate(engine, MapId, NavTargetTemplateId);

        AssertPhysicsOnlyPartition(engine, physicsBody);
        AssertPhysicsOnlyPartition(engine, physicsWall);
        Assert.That(engine.World.Has<OrderBuffer>(physicsBody), Is.False);
        Assert.That(engine.World.Has<OrderBuffer>(physicsWall), Is.False);

        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<NavAgent2D>(navAgent) &&
                  engine.World.Has<Position2D>(navAgent) &&
                  engine.World.Has<Velocity2D>(navAgent) &&
                  engine.World.Has<NavObstacle2D>(navObstacle) &&
                  engine.World.Has<Collider2D>(navObstacle) &&
                  engine.World.Has<Physics2DStaticBodyState>(navObstacle),
            maxFrames: 16);
        AssertNavPartition(engine, navAgent);
        AssertNavPartition(engine, navObstacle);
        AssertNavPartition(engine, navTarget);
        Assert.That(engine.World.Has<NavAgent2D>(navTarget), Is.False);
        Assert.That(engine.World.Has<Velocity2D>(navTarget), Is.False);
        Assert.That(engine.World.Has<Collider2D>(navTarget), Is.False);
        Assert.That(engine.World.Get<WorldPositionCm>(navTarget).Value.X, Is.EqualTo(Fix64.FromInt(980)));

        PerformerEntityRuntime performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
            ?? throw new InvalidOperationException("PerformerEntityRuntime missing.");
        AssertPresentationPayload(engine, physicsBody, "physics body");
        AssertPresentationPayload(engine, physicsWall, "physics wall");
        AssertPresentationPayload(engine, navAgent, "nav agent");
        AssertPresentationPayload(engine, navObstacle, "nav obstacle");
        AssertPresentationPayload(engine, navTarget, "nav target");
        Assert.That(performers.ActiveCount, Is.GreaterThanOrEqualTo(5),
            "Playground v2 must open with visible performer payloads for every first-screen entity.");

        var keyframes = new List<PlaygroundV2Keyframe>
        {
            Capture(engine, 0, physicsBody, navAgent, navObstacle)
        };

        CapabilityStandardPhysics2DPlaygroundV2InteractionSystem.SetMode(CapabilityStandardPhysics2DPlaygroundV2Mode.PhysicsOnly, engine);
        Assert.That(interaction.ApplyPhysicsImpulse(), Is.True);
        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, 1, frameTimesMs);
        keyframes.Add(Capture(engine, frameTimesMs.Count, physicsBody, navAgent, navObstacle));
        Assert.That(engine.World.Get<Velocity2D>(physicsBody).Linear.X, Is.GreaterThan(Fix64.FromInt(120)));
        Assert.That(engine.World.Has<NavDesiredVelocity2D>(physicsBody), Is.False);

        Fix64Vec2 beforeDisplacement = engine.World.Get<Position2D>(physicsBody).Value;
        Assert.That(interaction.ApplyPhysicsDisplacement(), Is.True);
        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<MovementSuppressed2D>(physicsBody) &&
                  engine.World.Get<Velocity2D>(physicsBody).Linear == Fix64Vec2.Zero,
            maxFrames: 12);
        keyframes.Add(Capture(engine, frameTimesMs.Count, physicsBody, navAgent, navObstacle));

        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimesMs,
            () => !HasActiveDisplacement(engine),
            maxFrames: 64);
        Fix64Vec2 afterDisplacement = engine.World.Get<Position2D>(physicsBody).Value;
        Assert.That(ToFloat(afterDisplacement.X), Is.EqualTo(ToFloat(beforeDisplacement.X + Fix64.FromInt(160))).Within(0.05f),
            "Physics-only displacement must not include residual locomotion drift while MovementSuppressed2D is active.");
        Assert.That(engine.World.Has<MovementSuppressed2D>(physicsBody), Is.False);
        Assert.That(engine.World.Has<NavAgent2D>(physicsBody), Is.False);
        keyframes.Add(Capture(engine, frameTimesMs.Count, physicsBody, navAgent, navObstacle));

        Fix64Vec2 navInitial = engine.World.Get<Position2D>(navAgent).Value;
        CapabilityStandardPhysics2DPlaygroundV2InteractionSystem.SetMode(CapabilityStandardPhysics2DPlaygroundV2Mode.Nav, engine);
        Assert.That(interaction.SubmitNavMove(), Is.True);

        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<NavDesiredVelocity2D>(navAgent) &&
                  engine.World.Get<NavDesiredVelocity2D>(navAgent).ValueCmPerSec.LengthSquared() > Fix64.Zero,
            maxFrames: 48);
        Assert.That(engine.World.Get<Velocity2D>(navAgent).Linear.X, Is.GreaterThan(Fix64.Zero));
        keyframes.Add(Capture(engine, frameTimesMs.Count, physicsBody, navAgent, navObstacle));

        CapabilityStandardShowcaseTestHarness.TickMeasured(engine, 24, frameTimesMs);
        Fix64Vec2 navFinal = engine.World.Get<Position2D>(navAgent).Value;
        Assert.That(navFinal.X, Is.GreaterThan(navInitial.X + Fix64.FromInt(50)),
            "Nav mode should drive only the Nav partition through desired velocity committed to Physics2D.");
        Assert.That(engine.World.Has<NavDesiredVelocity2D>(physicsBody), Is.False);
        Assert.That(engine.World.Get<WorldPositionCm>(navAgent).Value, Is.EqualTo(navFinal));
        keyframes.Add(Capture(engine, frameTimesMs.Count, physicsBody, navAgent, navObstacle));

        CapabilityStandardPhysics2DPlaygroundV2InteractionSystem.SetMode(CapabilityStandardPhysics2DPlaygroundV2Mode.PhysicsOnly, engine);
        Assert.That(interaction.SetBenchmarkSpawnCountForSlot(3), Is.EqualTo(30));
        Assert.That(interaction.SpawnBenchmarkBodiesAt(Fix64Vec2.FromInt(180, -520)), Is.True);
        Assert.That(spawnQueue.Count, Is.EqualTo(30),
            "Playground v2 must retain the old v1 benchmark burst spawn capability through RuntimeEntitySpawnQueue.");
        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimesMs,
            () => CountByTemplate(engine, BenchmarkBodyTemplateId) == 30,
            maxFrames: 12);
        Entity benchmarkBody = FindFirstByTemplate(engine, BenchmarkBodyTemplateId);
        AssertBenchmarkBody(engine, benchmarkBody);
        AssertPresentationPayload(engine, benchmarkBody, "benchmark body");
        Assert.That(performers.ActiveCount, Is.GreaterThanOrEqualTo(35),
            "Benchmark bodies should create visible performer payloads through the same entity-spawn bootstrap path.");
        keyframes.Add(Capture(engine, frameTimesMs.Count, physicsBody, navAgent, navObstacle));

        Fix64Vec2 beforeForcePulseVelocity = engine.World.Get<Velocity2D>(physicsBody).Linear;
        Assert.That(interaction.ApplyBenchmarkForcePulse(), Is.True);
        CapabilityStandardShowcaseTestHarness.TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Get<Velocity2D>(physicsBody).Linear.X > beforeForcePulseVelocity.X,
            maxFrames: 24);
        Fix64Vec2 afterForcePulseVelocity = engine.World.Get<Velocity2D>(physicsBody).Linear;
        Assert.That(afterForcePulseVelocity.X, Is.GreaterThan(beforeForcePulseVelocity.X),
            "The v1-style C force pulse must travel through GAS ApplyForce2D -> ForceInput2D -> production Physics2D integration.");
        Assert.That(engine.World.Get<ForceInput2D>(physicsBody).Force, Is.EqualTo(Fix64Vec2.Zero),
            "IntegrationSystem2D should consume the force input after applying it.");
        keyframes.Add(Capture(engine, frameTimesMs.Count, physicsBody, navAgent, navObstacle));

        Physics2DPerfStats stats = CapabilityStandardShowcaseTestHarness.ReadPhysicsPerfStats(engine.World);
        Assert.That(physics.PipelineStepNames.ToArray(), Does.Contain("NavToPhysicsVelocitySync"));
        WriteAcceptanceArtifacts(repoRoot, keyframes, frameTimesMs, stats);
    }

    private static void AssertPhysicsOnlyPartition(GameEngine engine, Entity entity)
    {
        Assert.That(engine.World.Has<Position2D>(entity), Is.True);
        Assert.That(engine.World.Has<Velocity2D>(entity), Is.True);
        Assert.That(engine.World.Has<Mass2D>(entity), Is.True);
        Assert.That(engine.World.Get<CapabilityStandardPhysics2DPlaygroundV2ModePartition>(entity).Mode,
            Is.EqualTo(CapabilityStandardPhysics2DPlaygroundV2Mode.PhysicsOnly));
        Assert.That(engine.World.Has<NavAgent2D>(entity), Is.False);
        Assert.That(engine.World.Has<NavDesiredVelocity2D>(entity), Is.False);
        Assert.That(engine.World.Has<NavObstacle2D>(entity), Is.False);
        Assert.That(engine.World.Has<NavGoal2D>(entity), Is.False);
    }

    private static void AssertNavPartition(GameEngine engine, Entity entity)
    {
        Assert.That(engine.World.Get<CapabilityStandardPhysics2DPlaygroundV2ModePartition>(entity).Mode,
            Is.EqualTo(CapabilityStandardPhysics2DPlaygroundV2Mode.Nav));
    }

    private static void AssertPresentationPayload(GameEngine engine, Entity entity, string label)
    {
        Assert.That(engine.World.Has<PresentationOwnerHasPerformerPayload>(entity), Is.True,
            $"{label} should bootstrap an entity-anchored performer payload for launcher visibility.");
        PresentationOwnerHasPerformerPayload payload = engine.World.Get<PresentationOwnerHasPerformerPayload>(entity);
        Assert.That(payload.RootCount, Is.GreaterThanOrEqualTo(1), $"{label} performer root missing.");
        Assert.That(payload.SingleRootPerformer, Is.Not.EqualTo(Entity.Null), $"{label} single root performer missing.");
    }

    private static void AssertBenchmarkBody(GameEngine engine, Entity entity)
    {
        Assert.That(engine.World.Has<Position2D>(entity), Is.True);
        Assert.That(engine.World.Has<PreviousPosition2D>(entity), Is.True);
        Assert.That(engine.World.Has<Velocity2D>(entity), Is.True);
        Assert.That(engine.World.Has<Mass2D>(entity), Is.True);
        Assert.That(engine.World.Has<Collider2D>(entity), Is.True);
        Assert.That(engine.World.Has<ForceInput2D>(entity), Is.True);
        Assert.That(engine.World.Get<Velocity2D>(entity).Linear.LengthSquared(), Is.GreaterThan(Fix64.Zero),
            "Benchmark receipt binding must seed deterministic initial velocity after RuntimeEntitySpawnSystem creates the body.");
        Assert.That(engine.World.Has<OrderBuffer>(entity), Is.False);
        Assert.That(engine.World.Has<NavAgent2D>(entity), Is.False);
        Assert.That(engine.World.Has<NavDesiredVelocity2D>(entity), Is.False);
        Assert.That(engine.World.Has<NavObstacle2D>(entity), Is.False);
        Assert.That(engine.World.Has<NavGoal2D>(entity), Is.False);
    }

    private static int CountSystems<T>(GameEngine engine, SystemGroup group)
        where T : class
    {
        var field = typeof(GameEngine).GetField("_systemGroups", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);

        var systemGroups = field!.GetValue(engine) as Dictionary<SystemGroup, List<Arch.System.ISystem<float>>>;
        Assert.That(systemGroups, Is.Not.Null);
        Assert.That(systemGroups!.TryGetValue(group, out List<Arch.System.ISystem<float>>? systems), Is.True);

        int count = 0;
        for (int i = 0; i < systems!.Count; i++)
        {
            if (systems[i] is T)
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasActiveDisplacement(GameEngine engine)
    {
        bool found = false;
        var query = new QueryDescription().WithAll<DisplacementState>();
        engine.World.Query(in query, (Entity _) =>
        {
            found = true;
        });
        return found;
    }

    private static PlaygroundV2Keyframe Capture(
        GameEngine engine,
        int frame,
        Entity physicsBody,
        Entity navAgent,
        Entity navObstacle)
    {
        Position2D physicsPosition = engine.World.Get<Position2D>(physicsBody);
        Velocity2D physicsVelocity = engine.World.Get<Velocity2D>(physicsBody);
        Position2D navPosition = engine.World.Has<Position2D>(navAgent)
            ? engine.World.Get<Position2D>(navAgent)
            : Position2D.Zero;
        Velocity2D navVelocity = engine.World.Has<Velocity2D>(navAgent)
            ? engine.World.Get<Velocity2D>(navAgent)
            : Velocity2D.Zero;
        Fix64Vec2 navDesired = engine.World.Has<NavDesiredVelocity2D>(navAgent)
            ? engine.World.Get<NavDesiredVelocity2D>(navAgent).ValueCmPerSec
            : Fix64Vec2.Zero;
        Entity benchmarkBody = TryFindFirstByTemplate(engine, BenchmarkBodyTemplateId);
        int benchmarkCount = CountByTemplate(engine, BenchmarkBodyTemplateId);
        Fix64Vec2 benchmarkVelocity = benchmarkBody != Entity.Null && engine.World.Has<Velocity2D>(benchmarkBody)
            ? engine.World.Get<Velocity2D>(benchmarkBody).Linear
            : Fix64Vec2.Zero;

        return new PlaygroundV2Keyframe(
            frame,
            CapabilityStandardPhysics2DPlaygroundV2State.ActiveMode.ToString(),
            ToFloat(physicsPosition.Value.X),
            ToFloat(physicsVelocity.Linear.X),
            engine.World.Has<MovementSuppressed2D>(physicsBody),
            engine.World.Has<NavAgent2D>(physicsBody),
            ToFloat(navPosition.Value.X),
            ToFloat(navVelocity.Linear.X),
            ToFloat(navDesired.X),
            engine.World.Has<NavObstacle2D>(navObstacle),
            engine.World.Has<Physics2DStaticBodyState>(navObstacle),
            benchmarkCount,
            ToFloat(benchmarkVelocity.X));
    }

    private static void AssertConfigAndTemplateBoundaries(string repoRoot)
    {
        string catalogPath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics2DPlaygroundV2Mod",
            "assets",
            "Configs",
            "config_catalog.json");
        string catalog = File.ReadAllText(catalogPath);
        Assert.That(catalog, Does.Contain("CapabilityStandardPhysics2DPlaygroundV2Config.json"));
        Assert.That(catalog, Does.Contain("Entities/templates.json"));
        Assert.That(catalog, Does.Contain("Presentation/performers.json"));
        Assert.That(catalog, Does.Not.Contain("Input/default_input.json"));

        string gameJsonPath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics2DPlaygroundV2Mod",
            "assets",
            "game.json");
        using (JsonDocument game = JsonDocument.Parse(File.ReadAllText(gameJsonPath)))
        {
            Assert.That(game.RootElement.GetProperty("physics2D").GetProperty("enabled").GetBoolean(), Is.True);
            Assert.That(game.RootElement.GetProperty("navigation2D").GetProperty("enabled").GetBoolean(), Is.True);
            JsonElement presentation = game.RootElement.GetProperty("presentation");
            Assert.That(presentation.GetProperty("performerInstanceCapacity").GetInt32(), Is.GreaterThanOrEqualTo(2048));
            Assert.That(presentation.GetProperty("visualProxyBufferCapacity").GetInt32(), Is.GreaterThanOrEqualTo(131072));
            Assert.That(presentation.GetProperty("presentationRequestCapacity").GetInt32(), Is.GreaterThanOrEqualTo(262144),
                "Playground v2 launcher must not overflow presentation requests while emitting first-screen and benchmark performer visuals.");
        }

        string templatePath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics2DPlaygroundV2Mod",
            "assets",
            "Entities",
            "templates.json");
        using JsonDocument templates = JsonDocument.Parse(File.ReadAllText(templatePath));
        foreach (JsonElement template in templates.RootElement.EnumerateArray())
        {
            string id = RequireString(template, "id");
            JsonElement components = template.GetProperty("components");
            if (id.Contains("_physics_", StringComparison.Ordinal))
            {
                Assert.That(components.TryGetProperty("OrderBuffer", out _), Is.False);
                Assert.That(components.TryGetProperty("NavKinematics2D", out _), Is.False);
                Assert.That(components.TryGetProperty("NavDesiredVelocity2D", out _), Is.False);
                Assert.That(components.TryGetProperty("NavObstacle2D", out _), Is.False);
            }

            if (string.Equals(id, NavAgentTemplateId, StringComparison.Ordinal))
            {
                Assert.That(components.TryGetProperty("OrderBuffer", out _), Is.True);
                Assert.That(components.TryGetProperty("Position2D", out _), Is.False);
                Assert.That(components.TryGetProperty("Velocity2D", out _), Is.False);
            }

            if (string.Equals(id, NavTargetTemplateId, StringComparison.Ordinal))
            {
                Assert.That(components.TryGetProperty("OrderBuffer", out _), Is.False);
                Assert.That(components.TryGetProperty("NavKinematics2D", out _), Is.False);
                Assert.That(components.TryGetProperty("NavDesiredVelocity2D", out _), Is.False);
                Assert.That(components.TryGetProperty("ManifestationObstacleIntent2D", out _), Is.False);
                Assert.That(components.TryGetProperty("CapabilityStandardPhysics2DPlaygroundV2.RigidBody", out _), Is.False);
            }

            if (string.Equals(id, BenchmarkBodyTemplateId, StringComparison.Ordinal))
            {
                Assert.That(components.TryGetProperty("CapabilityStandardPhysics2DPlaygroundV2.RigidBody", out _), Is.True);
                Assert.That(components.TryGetProperty("ForceInput2D", out _), Is.True);
                Assert.That(components.TryGetProperty("OrderBuffer", out _), Is.False);
                Assert.That(components.TryGetProperty("NavKinematics2D", out _), Is.False);
                Assert.That(components.TryGetProperty("NavDesiredVelocity2D", out _), Is.False);
                Assert.That(components.TryGetProperty("NavObstacle2D", out _), Is.False);
                Assert.That(components.TryGetProperty("ManifestationObstacleIntent2D", out _), Is.False);
            }
        }

        AssertPlaygroundBenchmarkInput(repoRoot);
        AssertFirstScreenVisualAuthoring(repoRoot);
    }

    private static void AssertPlaygroundBenchmarkInput(string repoRoot)
    {
        string inputPath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics2DPlaygroundV2Mod",
            "assets",
            "Input",
            "default_input.json");
        string input = File.ReadAllText(inputPath);
        Assert.That(input, Does.Contain("CapabilityStandardPhysics2DPlaygroundV2.BenchmarkForcePulse"));
        Assert.That(input, Does.Contain("\"<Keyboard>/C\""));
        Assert.That(input, Does.Contain("\"<Mouse>/Pos\""));
        Assert.That(input, Does.Contain("\"<Mouse>/RightButton\""));

        using JsonDocument document = JsonDocument.Parse(input);
        JsonElement bindings = document.RootElement.GetProperty("contexts")[0].GetProperty("bindings");
        AssertNoBenchmarkFunctionKeyBindings(bindings);
        AssertBenchmarkCountChord(bindings, "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount1", "<Keyboard>/q");
        AssertBenchmarkCountChord(bindings, "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount2", "<Keyboard>/w");
        AssertBenchmarkCountChord(bindings, "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount3", "<Keyboard>/e");
        AssertBenchmarkCountChord(bindings, "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount4", "<Keyboard>/r");
        AssertBenchmarkCountChord(bindings, "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount5", "<Keyboard>/t");
        AssertBenchmarkCountChord(bindings, "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount6", "<Keyboard>/y");
        AssertBenchmarkCountChord(bindings, "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount7", "<Keyboard>/u");
        AssertBenchmarkCountChord(bindings, "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount8", "<Keyboard>/o");
        AssertBenchmarkCountChord(bindings, "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount9", "<Keyboard>/p");
    }

    private static void AssertNoBenchmarkFunctionKeyBindings(JsonElement bindings)
    {
        foreach (JsonElement binding in bindings.EnumerateArray())
        {
            if (!binding.TryGetProperty("actionId", out JsonElement action) ||
                !action.GetString()!.StartsWith("CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawnCount", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.That(binding.TryGetProperty("path", out JsonElement path), Is.False,
                "Benchmark count slots must use non-conflicting ButtonChord bindings so F1/F2/F3 remain camera profile hotkeys.");
            if (!binding.TryGetProperty("compositeParts", out JsonElement parts))
            {
                continue;
            }

            foreach (JsonElement part in parts.EnumerateArray())
            {
                string? partPath = part.GetProperty("path").GetString();
                Assert.That(partPath, Does.Not.Match(@"^<Keyboard>/f[1-9]$").IgnoreCase,
                    "Benchmark count slots must not reclaim camera/minimap function keys.");
            }
        }
    }

    private static void AssertBenchmarkCountChord(JsonElement bindings, string actionId, string keyPath)
    {
        foreach (JsonElement binding in bindings.EnumerateArray())
        {
            if (!binding.TryGetProperty("actionId", out JsonElement action) ||
                !string.Equals(action.GetString(), actionId, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.That(RequireString(binding, "compositeType"), Is.EqualTo("ButtonChord"));
            JsonElement parts = binding.GetProperty("compositeParts");
            Assert.That(parts.GetArrayLength(), Is.EqualTo(2));
            Assert.That(RequireString(parts[0], "path"), Is.EqualTo("<Keyboard>/leftShift"));
            Assert.That(RequireString(parts[1], "path"), Is.EqualTo(keyPath));
            return;
        }

        Assert.Fail($"Missing benchmark count chord binding for {actionId}.");
    }

    private static void AssertFirstScreenVisualAuthoring(string repoRoot)
    {
        string mapPath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics2DPlaygroundV2Mod",
            "assets",
            "Maps",
            "capability_standard_physics2d_playground_v2.json");
        using (JsonDocument map = JsonDocument.Parse(File.ReadAllText(mapPath)))
        {
            JsonElement camera = map.RootElement.GetProperty("DefaultCamera");
            Assert.That(camera.GetProperty("DistanceCm").GetInt32(), Is.LessThanOrEqualTo(3200),
                "Playground v2 must launch close enough for its first-screen objects to be legible.");
            Assert.That(camera.GetProperty("TargetXCm").GetInt32(), Is.EqualTo(40));
            Assert.That(camera.GetProperty("TargetYCm").GetInt32(), Is.EqualTo(-120));
        }

        string performerPath = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics2DPlaygroundV2Mod",
            "assets",
            "Presentation",
            "performers.json");
        using JsonDocument performers = JsonDocument.Parse(File.ReadAllText(performerPath));
        AssertBootstrapAndVisual(performers.RootElement, PhysicsBodyTemplateId, "capability_standard_physics2d_playground_v2.physics_body");
        AssertBootstrapAndVisual(performers.RootElement, PhysicsWallTemplateId, "capability_standard_physics2d_playground_v2.physics_wall");
        AssertBootstrapAndVisual(performers.RootElement, NavAgentTemplateId, "capability_standard_physics2d_playground_v2.nav_agent");
        AssertBootstrapAndVisual(performers.RootElement, NavObstacleTemplateId, "capability_standard_physics2d_playground_v2.nav_obstacle");
        AssertBootstrapAndVisual(performers.RootElement, NavTargetTemplateId, "capability_standard_physics2d_playground_v2.nav_target");
        AssertBootstrapAndVisual(performers.RootElement, BenchmarkBodyTemplateId, "capability_standard_physics2d_playground_v2.benchmark_body");
    }

    private static void AssertBootstrapAndVisual(JsonElement performers, string templateId, string definitionId)
    {
        bool hasBootstrap = false;
        bool hasVisual = false;
        foreach (JsonElement performer in performers.EnumerateArray())
        {
            string id = RequireString(performer, "id");
            if (string.Equals(id, definitionId, StringComparison.Ordinal))
            {
                hasVisual = PerformerHasActiveAssetBinding(performer);
            }

            if (!performer.TryGetProperty("rules", out JsonElement rules))
            {
                continue;
            }

            foreach (JsonElement rule in rules.EnumerateArray())
            {
                if (!rule.TryGetProperty("event", out JsonElement evt) ||
                    !rule.TryGetProperty("command", out JsonElement command))
                {
                    continue;
                }

                string eventKind = RequireString(evt, "kind");
                string eventKey = RequireString(evt, "key");
                string commandKind = RequireString(command, "kind");
                string commandDefinition = command.TryGetProperty("definitionId", out JsonElement def)
                    ? def.GetString() ?? string.Empty
                    : string.Empty;
                hasBootstrap |= string.Equals(eventKind, "EntitySpawned", StringComparison.Ordinal) &&
                                string.Equals(eventKey, templateId, StringComparison.Ordinal) &&
                                string.Equals(commandKind, "CreatePerformer", StringComparison.Ordinal) &&
                                string.Equals(commandDefinition, definitionId, StringComparison.Ordinal);
            }
        }

        Assert.That(hasBootstrap, Is.True, $"{templateId} must have a direct EntitySpawned -> CreatePerformer bootstrap.");
        Assert.That(hasVisual, Is.True, $"{definitionId} must contain an active AssetBinding visual.");
    }

    private static bool PerformerHasActiveAssetBinding(JsonElement performer)
    {
        if (!performer.TryGetProperty("behaviors", out JsonElement behaviors))
        {
            return false;
        }

        foreach (JsonElement behavior in behaviors.EnumerateArray())
        {
            if (!behavior.TryGetProperty("activeByDefault", out JsonElement active) ||
                !active.GetBoolean())
            {
                continue;
            }

            if (string.Equals(RequireString(behavior, "kind"), "AssetBinding", StringComparison.Ordinal) &&
                behavior.TryGetProperty("assetBinding", out JsonElement binding) &&
                binding.TryGetProperty("assetId", out JsonElement asset) &&
                !string.IsNullOrWhiteSpace(asset.GetString()))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteAcceptanceArtifacts(
        string repoRoot,
        IReadOnlyList<PlaygroundV2Keyframe> keyframes,
        IReadOnlyList<double> frameTimesMs,
        in Physics2DPerfStats stats)
    {
        string artifactDir = Path.Combine(repoRoot, "artifacts", "showcases", "capability-standard-physics2d-playground-v2");
        Directory.CreateDirectory(artifactDir);
        string jsonlPath = Path.Combine(artifactDir, "keyframes.jsonl");
        string mdPath = Path.Combine(artifactDir, "acceptance.md");

        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
        using (var writer = new StreamWriter(jsonlPath, append: false, Encoding.UTF8))
        {
            for (int i = 0; i < keyframes.Count; i++)
            {
                writer.WriteLine(JsonSerializer.Serialize(keyframes[i], jsonOptions));
            }
        }

        double maxMs = 0d;
        double sumMs = 0d;
        for (int i = 0; i < frameTimesMs.Count; i++)
        {
            double value = frameTimesMs[i];
            maxMs = Math.Max(maxMs, value);
            sumMs += value;
        }

        double avgMs = frameTimesMs.Count > 0 ? sumMs / frameTimesMs.Count : 0d;
        PlaygroundV2Keyframe final = keyframes[^1];
        var builder = new StringBuilder();
        builder.AppendLine("# Capability Standard Physics2D Playground v2 Acceptance");
        builder.AppendLine();
        builder.AppendLine("| Check | Evidence |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine("| Startup boundary | `physics2D.enabled=true`, `navigation2D.enabled=true`, production `Navigation2DRuntime` present |");
        builder.AppendLine("| Runtime boundary | v2 installs interaction only; production `Physics2DSimulationSystem` count remains 1 |");
        builder.AppendLine("| Spawn path | `ConfigPipeline` catalog -> map focus event -> `RuntimeEntitySpawnQueue.EnqueueMany` -> `RuntimeEntitySpawnSystem` for 5 first-screen entities |");
        builder.AppendLine("| Launch visibility | every first-screen entity has direct performer bootstrap; default camera frames the playground instead of the global debug grid |");
        builder.AppendLine("| Physics-only mode | physics body has `Position2D/Velocity2D/Mass2D`, no `OrderBuffer`, no Nav components |");
        builder.AppendLine("| CC/knockback regression | displacement under `MovementSuppressed2D` clears velocity and lands at the configured displacement distance |");
        builder.AppendLine($"| Nav mode | nav final X `{Format(final.NavAgentX)}` cm, desired X `{Format(final.NavDesiredX)}` cm/s, obstacle nav `{final.NavObstacle}` physics `{final.PhysicsObstacle}` |");
        builder.AppendLine($"| v1 benchmark carryover | LeftShift+Q/W/E/R/T/Y/U/O/P count slots, right-click/runtime burst spawn, and C GAS force pulse retained; benchmark bodies `{final.BenchmarkBodyCount}`, sample Vx `{Format(final.BenchmarkBodyVelocityX)}` cm/s |");
        builder.AppendLine($"| Physics stats | Hz `{stats.PhysicsHz}`, potential pairs `{stats.PotentialPairs}`, contact pairs `{stats.ContactPairs}`, last update `{stats.PhysicsUpdateMs:F4}` ms |");
        builder.AppendLine($"| Test tick timings | frames `{frameTimesMs.Count}`, avg `{avgMs:F4}` ms, max `{maxMs:F4}` ms |");
        builder.AppendLine();
        builder.AppendLine("## Keyframes");
        builder.AppendLine();
        builder.AppendLine("| Frame | Mode | Physics X | Physics Vx | Suppressed | Physics Has Nav | Nav X | Nav Vx | Nav Desired X | Nav Obstacle | Physics Obstacle | Benchmark Bodies | Benchmark Vx |");
        builder.AppendLine("| ---: | --- | ---: | ---: | :---: | :---: | ---: | ---: | ---: | :---: | :---: | ---: | ---: |");
        for (int i = 0; i < keyframes.Count; i++)
        {
            PlaygroundV2Keyframe keyframe = keyframes[i];
            builder.AppendLine(
                $"| {keyframe.Frame} | {keyframe.Mode} | {Format(keyframe.PhysicsBodyX)} | {Format(keyframe.PhysicsBodyVelocityX)} | {keyframe.PhysicsBodySuppressed} | {keyframe.PhysicsBodyHasNav} | {Format(keyframe.NavAgentX)} | {Format(keyframe.NavVelocityX)} | {Format(keyframe.NavDesiredX)} | {keyframe.NavObstacle} | {keyframe.PhysicsObstacle} | {keyframe.BenchmarkBodyCount} | {Format(keyframe.BenchmarkBodyVelocityX)} |");
        }

        File.WriteAllText(mdPath, builder.ToString(), Encoding.UTF8);
    }

    private static Entity FindFirstByTemplate(GameEngine engine, string templateId)
    {
        Entity found = TryFindFirstByTemplate(engine, templateId);
        Assert.That(found, Is.Not.EqualTo(Entity.Null), $"Expected at least one entity for template '{templateId}'.");
        return found;
    }

    private static Entity TryFindFirstByTemplate(GameEngine engine, string templateId)
    {
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            return Entity.Null;
        }

        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
        engine.World.Query(in query, (Entity entity, ref EntityTemplateKeyRef keyRef) =>
        {
            if (found == Entity.Null && keyRef.TemplateKeyId == templateKeyId)
            {
                found = entity;
            }
        });

        return found;
    }

    private static int CountByTemplate(GameEngine engine, string templateId)
    {
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            return 0;
        }

        int count = 0;
        var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
        engine.World.Query(in query, (ref EntityTemplateKeyRef keyRef) =>
        {
            if (keyRef.TemplateKeyId == templateKeyId)
            {
                count++;
            }
        });

        return count;
    }

    private static string RequireString(JsonElement root, string propertyName)
    {
        string? value = root.GetProperty(propertyName).GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Config property '{propertyName}' is required.");
        }

        return value;
    }

    private static float ToFloat(Fix64 value)
    {
        return value.ToFloat();
    }

    private static string Format(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private readonly record struct PlaygroundV2Keyframe(
        int Frame,
        string Mode,
        float PhysicsBodyX,
        float PhysicsBodyVelocityX,
        bool PhysicsBodySuppressed,
        bool PhysicsBodyHasNav,
        float NavAgentX,
        float NavVelocityX,
        float NavDesiredX,
        bool NavObstacle,
        bool PhysicsObstacle,
        int BenchmarkBodyCount,
        float BenchmarkBodyVelocityX);
}
