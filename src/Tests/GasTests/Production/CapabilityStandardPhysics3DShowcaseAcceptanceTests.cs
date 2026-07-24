using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CapabilityStandardPhysics3DShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Physics3D;
using Ludots.Core.Scripting;
using Ludots.Launcher.Backend;
using Ludots.Platform.Abstractions;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Surface;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
[Category("acceptance")]
public sealed class CapabilityStandardPhysics3DShowcaseAcceptanceTests
{
    private const string BindingName = "capability_standard_physics3d_showcase";
    private const string PresetId = "capability_standard_physics3d_showcase_raylib";
    private const string ShowcaseModId = "CapabilityStandardPhysics3DShowcaseMod";
    private const string PhysicsModId = "Physics3DMod";
    private const string MapId = "capability_standard_physics3d_showcase";
    private const string CameraId = "Camera.Profile.Physics3DLab";
    private const string PanelElementId = "capability-standard-physics3d-panel";
    private const int FixedHz = 30;
    private const double FixedStepBudgetMilliseconds = 1000d / FixedHz;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Test]
    public void RootMod_ResolvesFormalLauncherDependenciesAndOneToOneThirtyHzClock()
    {
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);

        Assert.That(plan.AdapterId, Is.EqualTo(LauncherPlatformIds.Raylib));
        Assert.That(plan.Selectors, Is.EqualTo(new[] { $"preset:{PresetId}" }));
        Assert.That(plan.RootModIds, Is.EqualTo(new[] { ShowcaseModId }));
        Assert.That(plan.OrderedModIds, Does.Contain("LudotsCoreMod"));
        Assert.That(plan.OrderedModIds, Does.Contain("CoreInputMod"));
        Assert.That(plan.OrderedModIds, Does.Contain("CameraProfilesMod"));
        Assert.That(plan.OrderedModIds, Does.Contain(PhysicsModId));
        Assert.That(plan.OrderedModIds, Does.Contain(ShowcaseModId));
        var orderedModIds = plan.OrderedModIds.ToList();
        Assert.That(
            orderedModIds.IndexOf(PhysicsModId),
            Is.LessThan(orderedModIds.IndexOf(ShowcaseModId)),
            "The authoritative Physics3D capability must load before the player-facing lab.");

        AssertModDependencies(repoRoot);
        AssertPhysicsModRuntimeDependencies(repoRoot);
        AssertEntryAssets(repoRoot);
        AssertClockConfig(Path.Combine(repoRoot, "assets", "Configs", "Engine", "clock.json"), includeStepCap: false);
        AssertClockConfig(
            Path.Combine(repoRoot, "mods", "capabilities", "physics3d", "Physics3DMod", "assets", "Configs", "Physics3D", "world.json"),
            includeStepCap: true);
        AssertClockConfig(
            Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "capability_standard",
                ShowcaseModId,
                "assets",
                "Configs",
                "Physics3D",
                "world.json"),
            includeStepCap: true);
    }

    [Test]
    [Category("scale")]
    public void NewPlayer_CanBrowseNineSamplesControlTimeAndRunTenThousandBodiesWithinThirtyHzBudget()
    {
        string repoRoot = FindRepoRoot();
        LauncherLaunchPlan plan = ResolveLaunchPlan(repoRoot);
        using GameEngine engine = CreateEngine(repoRoot, plan);
        var trace = new List<string>();

        Assert.That(Ludots.Core.Engine.Time.FixedDeltaTime, Is.EqualTo(1f / FixedHz).Within(1e-6f));
        engine.LoadEntryMap(MapId);
        Tick(engine);

        var runtime = engine.GetService(CoreServiceKeys.BenchmarkSceneController) as Physics3DShowcaseRuntime
            ?? throw new InvalidOperationException("Physics3D showcase runtime was not installed by the resolved launch plan.");
        IPhysics3DWorld world = engine.GetService(Physics3DServiceKeys.World)
            ?? throw new InvalidOperationException("Physics3D world was not installed for the entry map.");
        Physics3DSimulationSystem simulation = engine.GetService(Physics3DServiceKeys.SimulationSystem)
            ?? throw new InvalidOperationException("Physics3D simulation system was not installed for the entry map.");
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            throw new InvalidOperationException("Acceptance UI surface host is missing.");
        }

        Assert.That(runtime.IsActive, Is.True);
        Assert.That(runtime.SuppressHostDiagnosticUi, Is.False, "The player-facing lab panel must remain visible in Raylib.");
        Assert.That(runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.Stacking));
        Assert.That(world.FixedDeltaSeconds, Is.EqualTo(1f / FixedHz).Within(1e-6f));
        Assert.That(surfaceHost.Scene, Is.Not.Null);
        Assert.That(surfaceHost.Scene!.FindByElementId(PanelElementId), Is.Not.Null);
        Assert.That(
            surfaceHost.Scene.HitTest(1500f, 800f),
            Is.Null,
            "The lab's blank screen area must remain available to world interaction.");
        trace.Add(JsonSerializer.Serialize(new
        {
            step = "entry",
            map = MapId,
            scene = runtime.ActiveScene.ToString(),
            fixedHz = FixedHz,
            panel = "visible",
            worldInputOutsidePanel = "pass-through"
        }));

        foreach (Physics3DShowcaseScene scene in Enum.GetValues<Physics3DShowcaseScene>())
        {
            Click(surfaceHost, SceneButtonId(scene));
            TickUntil(engine, () => runtime.ActiveScene == scene, maximumFrames: 128);
            Assert.That(runtime.ActiveScene, Is.EqualTo(scene));
            Assert.That(runtime.BodyCount, Is.GreaterThan(0), $"{scene} must show physical content to the player.");

            if (scene == Physics3DShowcaseScene.Determinism)
            {
                RunUntilReplayCompletes(engine, runtime, surfaceHost);
                Assert.That(
                    runtime.ReplayStatus,
                    Is.EqualTo(Physics3DShowcaseReplayStatus.Passed),
                    $"cursor={runtime.ReplayCursor}, expected={runtime.ReplayExpectedHash:X16}, actual={runtime.ReplayActualHash:X16}");
            }
            else if (simulation.Enabled)
            {
                for (int frame = 0; frame < 128 && runtime.SceneStep == 0; frame++)
                {
                    Tick(engine);
                }

                Assert.That(
                    runtime.SceneStep,
                    Is.GreaterThan(0),
                    $"Scene {scene} did not advance. simulation.Enabled={simulation.Enabled}, " +
                    $"lastSteps={simulation.PhysicsStepsLastUpdate}, totalSteps={simulation.TotalPhysicsSteps}, " +
                    $"engineTick={engine.GameSession.CurrentTick}.");
            }

            if (scene == Physics3DShowcaseScene.Queries)
            {
                int totalQueryHits = 0;
                for (int i = 0; i < 7; i++)
                {
                    totalQueryHits += runtime.GetQueryHitCount(i);
                }

                Assert.That(totalQueryHits, Is.GreaterThan(0));
            }
            else if (scene == Physics3DShowcaseScene.Joints)
            {
                Assert.That(runtime.ConstraintCount, Is.GreaterThan(0));
            }

            trace.Add(JsonSerializer.Serialize(new
            {
                step = "scene",
                scene = scene.ToString(),
                bodies = runtime.BodyCount,
                constraints = runtime.ConstraintCount,
                replay = runtime.ReplayStatus.ToString()
            }));
        }

        ResumeIfPaused(engine, surfaceHost, simulation);
        Click(surfaceHost, "physics3d-action-pause");
        TickUntil(engine, () => !simulation.Enabled, maximumFrames: 128);
        Assert.That(simulation.Enabled, Is.False);
        long stepsAfterPause = simulation.TotalPhysicsSteps;
        for (int i = 0; i < 8; i++)
        {
            Tick(engine);
        }
        Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(stepsAfterPause));

        Click(surfaceHost, "physics3d-action-single-step");
        TickUntil(engine, () => simulation.TotalPhysicsSteps == stepsAfterPause + 1, maximumFrames: 128);
        Assert.That(simulation.Enabled, Is.False);
        Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(stepsAfterPause + 1));
        trace.Add(JsonSerializer.Serialize(new
        {
            step = "pause-single-step",
            pausedSteps = stepsAfterPause,
            afterSingleStep = simulation.TotalPhysicsSteps,
            delta = simulation.TotalPhysicsSteps - stepsAfterPause
        }));

        Click(surfaceHost, "physics3d-action-pause");
        TickUntil(engine, () => simulation.Enabled, maximumFrames: 128);
        Assert.That(simulation.Enabled, Is.True);

        Click(surfaceHost, "physics3d-benchmark-10000");
        TickUntil(
            engine,
            () => runtime.ActiveScene == Physics3DShowcaseScene.Benchmark && runtime.DynamicBodyCount == 10_000,
            maximumFrames: 128);
        Assert.That(runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.Benchmark));
        Assert.That(runtime.DynamicBodyCount, Is.EqualTo(10_000));
        Assert.That(runtime.BodyCount, Is.EqualTo(10_001));
        Assert.That(world.ActiveMobileBodyCount, Is.EqualTo(10_000));
        int visibleBodyLimit = runtime.ActiveConfig.VisibleBodyLimit;
        TickUntil(engine, () => runtime.VisibleBodyCount == visibleBodyLimit, maximumFrames: 128);
        Assert.That(runtime.VisibleBodyCount, Is.EqualTo(visibleBodyLimit));

        for (int i = 0; i < 10; i++)
        {
            TickUntilNextPhysicsStep(engine, simulation);
        }

        var samples = new double[60];
        var endToEnd = new double[60];
        for (int i = 0; i < samples.Length; i++)
        {
            endToEnd[i] = TickUntilNextPhysicsStep(engine, simulation);
            samples[i] = simulation.PhysicsUpdateMillisecondsLastUpdate;
        }

        double physicsP50 = Percentile(samples, 0.50d);
        double physicsP95 = Percentile(samples, 0.95d);
        double physicsP99 = Percentile(samples, 0.99d);
        double endToEndP95 = Percentile(endToEnd, 0.95d);
        Assert.That(
            physicsP95,
            Is.LessThanOrEqualTo(FixedStepBudgetMilliseconds),
            $"10K authoritative Physics3D P95 {physicsP95:0.###}ms exceeds the 30Hz step budget {FixedStepBudgetMilliseconds:0.###}ms.");
        trace.Add(JsonSerializer.Serialize(new
        {
            step = "benchmark-10k",
            authoritativeBodies = runtime.DynamicBodyCount,
            drawnBodies = runtime.VisibleBodyCount,
            physicsP50Ms = physicsP50,
            physicsP95Ms = physicsP95,
            physicsP99Ms = physicsP99,
            endToEndP95Ms = endToEndP95,
            budgetMs = FixedStepBudgetMilliseconds
        }));

        engine.UnloadMap(MapId);
        Assert.That(runtime.IsActive, Is.False);
        TickUntil(engine, () => surfaceHost.Scene?.FindByElementId(PanelElementId) == null, maximumFrames: 128);
        Assert.That(surfaceHost.Scene?.FindByElementId(PanelElementId), Is.Null);
        trace.Add(JsonSerializer.Serialize(new
        {
            step = "leave-map",
            map = MapId,
            panel = "released"
        }));

        WriteAcceptanceArtifacts(
            repoRoot,
            trace,
            physicsP50,
            physicsP95,
            physicsP99,
            endToEndP95,
            visibleBodyLimit);
    }

    private static LauncherLaunchPlan ResolveLaunchPlan(string repoRoot)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"ludots-physics3d-showcase-launcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
            string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
            File.WriteAllText(preferencesPath, "{}", Utf8NoBom);
            File.WriteAllText(userConfigPath, "{}", Utf8NoBom);
            var launcher = new LauncherService(
                repoRoot,
                Path.Combine(repoRoot, "launcher.config.json"),
                Path.Combine(repoRoot, "launcher.presets.json"),
                preferencesPath,
                userConfigPath);
            return launcher.Resolve(
                new[] { $"preset:{PresetId}" },
                LauncherPlatformIds.Raylib,
                LauncherBuildMode.Never).Plan;
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static GameEngine CreateEngine(string repoRoot, LauncherLaunchPlan plan)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            plan.Mods.Select(static mod => mod.RootPath).ToList(),
            Path.Combine(repoRoot, "assets"));
        InstallInput(engine);
        AcceptanceUiHostInstaller.Install(engine, 1600f, 900f);
        engine.Start();
        return engine;
    }

    private static void InstallInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static void AssertModDependencies(string repoRoot)
    {
        string modJson = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            ShowcaseModId,
            "mod.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(modJson, Encoding.UTF8));
        string[] dependencies = document.RootElement
            .GetProperty("dependencies")
            .EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();
        Assert.That(
            dependencies,
            Is.EquivalentTo(new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", PhysicsModId }));
    }

    private static void AssertPhysicsModRuntimeDependencies(string repoRoot)
    {
        string modRoot = Path.Combine(repoRoot, "mods", "capabilities", "physics3d", PhysicsModId);
        string projectPath = Path.Combine(modRoot, $"{PhysicsModId}.csproj");
        XDocument project = XDocument.Load(projectPath);
        string? copyLocalLockFileAssemblies = project
            .Descendants()
            .FirstOrDefault(static element => element.Name.LocalName == "CopyLocalLockFileAssemblies")
            ?.Value;
        Assert.That(
            copyLocalLockFileAssemblies,
            Is.EqualTo("true").IgnoreCase,
            "Physics3DMod must publish package runtime dependencies into its formal bin/net8.0 Mod output.");

        string outputDirectory = Path.Combine(modRoot, "bin", "net8.0");
        Assert.That(File.Exists(Path.Combine(outputDirectory, "BepuPhysics.dll")), Is.True);
        Assert.That(File.Exists(Path.Combine(outputDirectory, "BepuUtilities.dll")), Is.True);

        string dependencyManifest = File.ReadAllText(
            Path.Combine(outputDirectory, $"{PhysicsModId}.deps.json"),
            Encoding.UTF8);
        Assert.That(dependencyManifest, Does.Contain("BepuPhysics/2.4.0"));
        Assert.That(dependencyManifest, Does.Contain("BepuUtilities/2.4.0"));
    }

    private static void AssertEntryAssets(string repoRoot)
    {
        string assetRoot = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            ShowcaseModId,
            "assets");
        using (JsonDocument game = JsonDocument.Parse(File.ReadAllText(Path.Combine(assetRoot, "game.json"), Encoding.UTF8)))
        {
            Assert.That(game.RootElement.GetProperty("startupMapId").GetString(), Is.EqualTo(MapId));
            Assert.That(game.RootElement.GetProperty("targetFps").GetInt32(), Is.Zero);
        }

        using (JsonDocument map = JsonDocument.Parse(File.ReadAllText(Path.Combine(assetRoot, "Maps", $"{MapId}.json"), Encoding.UTF8)))
        {
            Assert.That(map.RootElement.GetProperty("Id").GetString(), Is.EqualTo(MapId));
            Assert.That(
                map.RootElement.GetProperty("DefaultCamera").GetProperty("VirtualCameraId").GetString(),
                Is.EqualTo(CameraId));
            Assert.That(
                map.RootElement.GetProperty("DefaultCamera").GetProperty("DistanceCm").GetInt32(),
                Is.EqualTo(7_000));
        }

        using (JsonDocument cameras = JsonDocument.Parse(File.ReadAllText(
                   Path.Combine(assetRoot, "Configs", "Camera", "virtual_cameras.json"),
                   Encoding.UTF8)))
        {
            JsonElement camera = cameras.RootElement.EnumerateArray().Single();
            Assert.That(camera.GetProperty("id").GetString(), Is.EqualTo(CameraId));
            Assert.That(camera.GetProperty("distanceCm").GetInt32(), Is.EqualTo(7_000));
            Assert.That(camera.GetProperty("panMode").GetString(), Is.EqualTo("Keyboard"));
            Assert.That(camera.GetProperty("enableGrabDrag").GetBoolean(), Is.True);
        }

        using (JsonDocument config = JsonDocument.Parse(File.ReadAllText(
                   Path.Combine(assetRoot, "CapabilityStandardPhysics3DShowcaseConfig.json"),
                   Encoding.UTF8)))
        {
            JsonElement root = config.RootElement;
            Assert.That(root.GetProperty("initialScene").GetString(), Is.EqualTo("Stacking"));
            Assert.That(root.GetProperty("maximumBodies").GetInt32(), Is.EqualTo(11_000));
            Assert.That(root.GetProperty("pyramidRows").GetInt32(), Is.EqualTo(10));
            Assert.That(root.GetProperty("pyramidCenterXCm").GetInt32(), Is.EqualTo(-800));
            Assert.That(root.GetProperty("spherePyramidRows").GetInt32(), Is.EqualTo(7));
            Assert.That(root.GetProperty("spherePyramidCenterXCm").GetInt32(), Is.EqualTo(1_000));
            Assert.That(root.GetProperty("capsulePyramidRows").GetInt32(), Is.EqualTo(6));
            Assert.That(root.GetProperty("capsulePyramidBaseColumns").GetInt32(), Is.EqualTo(9));
            Assert.That(root.GetProperty("capsulePyramidCenterXCm").GetInt32(), Is.EqualTo(2_800));
            Assert.That(root.GetProperty("stackingRailThicknessCm").GetInt32(), Is.EqualTo(24));
            Assert.That(root.GetProperty("stackingRailHeightCm").GetInt32(), Is.EqualTo(60));
            Assert.That(root.GetProperty("stackingRailClearanceCm").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("replaySteps").GetInt32(), Is.EqualTo(54));
            Assert.That(root.GetProperty("replayGridSize").GetInt32(), Is.EqualTo(6));
            Assert.That(root.GetProperty("replayBodySpacingCm").GetInt32(), Is.EqualTo(180));
            Assert.That(root.GetProperty("replayCenterXCm").GetInt32(), Is.EqualTo(1_000));
            Assert.That(root.GetProperty("replayBaseHeightCm").GetInt32(), Is.EqualTo(1_800));
            Assert.That(root.GetProperty("replayLaneOffsetCm").GetInt32(), Is.EqualTo(1_100));
            Assert.That(root.GetProperty("benchmarkColumns").GetInt32(), Is.EqualTo(25));
            Assert.That(root.GetProperty("benchmarkDepth").GetInt32(), Is.EqualTo(20));
            Assert.That(root.GetProperty("benchmarkSpacingCm").GetInt32(), Is.GreaterThan(root.GetProperty("bodySizeCm").GetInt32()));
            Assert.That(root.GetProperty("benchmarkBaseHeightCm").GetInt32(), Is.EqualTo(1_200));
            Assert.That(root.GetProperty("benchmarkRecycleHeightCm").GetInt32(), Is.EqualTo(120));
            Assert.That(root.GetProperty("benchmarkTravelHalfWidthCm").GetInt32(), Is.EqualTo(1_800));
            Assert.That(root.GetProperty("benchmarkSpeedCmPerSecond").GetInt32(), Is.EqualTo(500));
            Assert.That(
                root.GetProperty("benchmarkPresets").EnumerateArray().Select(static value => value.GetInt32()).ToArray(),
                Is.EqualTo(new[] { 1_000, 2_000, 5_000, 10_000 }));
        }

        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(assetRoot, "Configs", "config_catalog.json"),
            Encoding.UTF8));
        string[] catalogPaths = catalog.RootElement
            .EnumerateArray()
            .Select(static entry => entry.GetProperty("Path").GetString() ?? string.Empty)
            .ToArray();
        Assert.That(
            catalogPaths,
            Is.EqualTo(new[]
            {
                "CapabilityStandardPhysics3DShowcaseConfig.json",
                "Physics3D/world.json",
                "Camera/virtual_cameras.json"
            }));
    }

    private static void AssertClockConfig(string path, bool includeStepCap)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        JsonElement root = document.RootElement;
        string fixedHzProperty = root.TryGetProperty("FixedHz", out _)
            ? "FixedHz"
            : "FixedStepHz";
        Assert.That(root.GetProperty(fixedHzProperty).GetInt32(), Is.EqualTo(FixedHz), path);
        if (includeStepCap)
        {
            Assert.That(root.GetProperty("MaximumPhysicsStepsPerSourceTick").GetInt32(), Is.EqualTo(1), path);
        }
    }

    private static void RunUntilReplayCompletes(
        GameEngine engine,
        Physics3DShowcaseRuntime runtime,
        IUiSurfaceHost surfaceHost)
    {
        int maximumFrames = ((runtime.ActiveConfig.ReplaySteps * 2) + 8) * 16;
        bool comparisonStarted = false;
        for (int i = 0;
             i < maximumFrames &&
             runtime.ReplayStatus is not Physics3DShowcaseReplayStatus.Passed and
             not Physics3DShowcaseReplayStatus.Failed;
             i++)
        {
            if (runtime.ReplayStatus == Physics3DShowcaseReplayStatus.ReadyToReplay)
            {
                TickUntil(
                    engine,
                    () => surfaceHost.Scene?.FindByElementId("physics3d-replay-start") != null,
                    maximumFrames: 32);
                Click(surfaceHost, "physics3d-replay-start");
                comparisonStarted = true;
            }

            Tick(engine);
        }

        Assert.That(comparisonStarted, Is.True, "The player-facing replay comparison action was never exposed.");
    }

    private static void ResumeIfPaused(
        GameEngine engine,
        IUiSurfaceHost surfaceHost,
        Physics3DSimulationSystem simulation)
    {
        if (simulation.Enabled)
        {
            return;
        }

        Click(surfaceHost, "physics3d-action-pause");
        TickUntil(engine, () => simulation.Enabled, maximumFrames: 128);
        Assert.That(simulation.Enabled, Is.True);
    }

    private static void Click(IUiSurfaceHost surfaceHost, string elementId)
    {
        UiScene scene = surfaceHost.Scene
            ?? throw new InvalidOperationException("Physics3D showcase UI scene is not mounted.");
        UiNode node = scene.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"Physics3D showcase UI element '{elementId}' is missing.");
        UiEventResult result = scene.Dispatch(new UiPointerEvent(
            UiPointerEventType.Click,
            PointerId: 1,
            X: node.LayoutRect.X + (node.LayoutRect.Width * 0.5f),
            Y: node.LayoutRect.Y + (node.LayoutRect.Height * 0.5f),
            TargetNodeId: node.Id));
        Assert.That(result.Handled, Is.True, $"UI element '{elementId}' did not handle its click.");
    }

    private static void Tick(GameEngine engine)
    {
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.Tick(1f / FixedHz);
    }

    private static void TickUntil(
        GameEngine engine,
        Func<bool> condition,
        int maximumFrames)
    {
        for (int frame = 0; frame < maximumFrames; frame++)
        {
            if (condition())
            {
                return;
            }

            Tick(engine);
        }

        Assert.That(condition(), Is.True, $"Condition did not become true within {maximumFrames} rendered frames.");
    }

    private static double TickUntilNextPhysicsStep(GameEngine engine, Physics3DSimulationSystem simulation)
    {
        long physicsStepsBefore = simulation.TotalPhysicsSteps;
        long timestamp = Stopwatch.GetTimestamp();
        for (int frame = 0; frame < 128; frame++)
        {
            Tick(engine);
            if (simulation.TotalPhysicsSteps <= physicsStepsBefore)
            {
                continue;
            }

            Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(physicsStepsBefore + 1));
            Assert.That(simulation.PhysicsStepsLastUpdate, Is.EqualTo(1));
            return Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
        }

        Assert.Fail("Physics3D did not complete the next authoritative step within 128 rendered frames.");
        return double.NaN;
    }

    private static string SceneButtonId(Physics3DShowcaseScene scene) =>
        $"physics3d-scene-{scene.ToString().ToLowerInvariant()}";

    private static double Percentile(double[] samples, double percentile)
    {
        if (samples.Length == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        if (!(percentile > 0d && percentile <= 1d))
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        double[] sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        int index = Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private static void WriteAcceptanceArtifacts(
        string repoRoot,
        IReadOnlyList<string> trace,
        double physicsP50,
        double physicsP95,
        double physicsP99,
        double endToEndP95,
        int visibleBodyLimit)
    {
        string artifactDirectory = Path.Combine(repoRoot, "artifacts", "acceptance", "physics3d-showcase");
        Directory.CreateDirectory(artifactDirectory);
        File.WriteAllText(
            Path.Combine(artifactDirectory, "trace.jsonl"),
            string.Join(Environment.NewLine, trace) + Environment.NewLine,
            Utf8NoBom);
        File.WriteAllText(
            Path.Combine(artifactDirectory, "path.mmd"),
            BuildPathDiagram(),
            Utf8NoBom);
        File.WriteAllText(
            Path.Combine(artifactDirectory, "battle-report.md"),
            BuildBattleReport(physicsP50, physicsP95, physicsP99, endToEndP95, visibleBodyLimit),
            Utf8NoBom);
    }

    private static string BuildBattleReport(
        double physicsP50,
        double physicsP95,
        double physicsP99,
        double endToEndP95,
        int visibleBodyLimit)
    {
        var report = new StringBuilder();
        report.AppendLine("# Physics3D Sample Lab 验收战报");
        report.AppendLine();
        report.AppendLine("## 1. 概述");
        report.AppendLine();
        report.AppendLine("玩家从正式 Raylib 启动预设进入 3D 物理实验室，默认看到堆叠场景和完整控制面板。主循环与物理世界均为 30Hz，每次主循环只推进一次权威物理步。九个场景、暂停、单步和 10K 规模入口均通过真实界面按钮操作。");
        report.AppendLine();
        report.AppendLine("## 2. 结构");
        report.AppendLine();
        report.AppendLine("- 启动入口：`capability_standard_physics3d_showcase_raylib`");
        report.AppendLine("- 默认地图：`capability_standard_physics3d_showcase`");
        report.AppendLine("- 场景数量：9");
        report.AppendLine("- 压力档位：1K / 2K / 5K / 10K");
        report.AppendLine($"- 10K 时权威刚体：10,000；表现层采样上限：{visibleBodyLimit:N0}");
        report.AppendLine();
        report.AppendLine("## 3. 详情");
        report.AppendLine();
        report.AppendLine($"- 10K 物理步 P50：{physicsP50:0.###} ms");
        report.AppendLine($"- 10K 物理步 P95：{physicsP95:0.###} ms");
        report.AppendLine($"- 10K 物理步 P99：{physicsP99:0.###} ms");
        report.AppendLine($"- 10K 完整引擎 Tick P95：{endToEndP95:0.###} ms");
        report.AppendLine($"- 30Hz 单步预算：{FixedStepBudgetMilliseconds:0.###} ms");
        report.AppendLine($"- 预算判定：{(physicsP95 <= FixedStepBudgetMilliseconds ? "PASS" : "FAIL")}");
        report.AppendLine();
        report.AppendLine("## 4. 场景");
        report.AppendLine();
        report.AppendLine("```gherkin");
        report.AppendLine("Feature: 新玩家在一个实验室里理解并操作 3D 物理能力");
        report.AppendLine();
        report.AppendLine("  Scenario: 第一次进入就能看懂并浏览全部样例");
        report.AppendLine("    Given 玩家从 Physics3D Sample Lab 的 Raylib 正式入口启动游戏");
        report.AppendLine("    When 玩家进入默认地图");
        report.AppendLine("    Then 玩家首先看到 Stacking 场景、30 Hz 状态和九个场景按钮");
        report.AppendLine("    And 玩家依次点击 Bodies、Shapes、Stacking、Continuous、Queries、Contacts、Joints、Replay、Benchmark");
        report.AppendLine("    Then 每次点击都立即切换到有可见物理内容的新样例");
        report.AppendLine();
        report.AppendLine("  Scenario: 玩家暂停世界并只前进一步");
        report.AppendLine("    Given 物理世界正在以 30 Hz 运行");
        report.AppendLine("    When 玩家点击 Pause 后再点击 Single Step");
        report.AppendLine("    Then 暂停期间世界不前进");
        report.AppendLine("    And Single Step 恰好推进一个权威物理步且仍保持暂停");
        report.AppendLine();
        report.AppendLine("  Scenario: 玩家查看服务器规模的 10K 刚体压力场");
        report.AppendLine("    Given 玩家选择 Benchmark 场景");
        report.AppendLine("    When 玩家点击 10K");
        report.AppendLine("    Then 权威物理世界中存在且保持活跃的动态刚体恰好为 10000 个");
        report.AppendLine($"    And 画面只抽样最多 {visibleBodyLimit} 个刚体但不会减少权威模拟数量");
        report.AppendLine($"    And 连续测量的物理步 P95 不超过 {FixedStepBudgetMilliseconds:0.###} 毫秒");
        report.AppendLine();
        report.AppendLine("  Scenario: 玩家离开实验室后界面不残留");
        report.AppendLine("    Given Physics3D Sample Lab 面板正在显示");
        report.AppendLine("    When 玩家离开当前地图");
        report.AppendLine("    Then 面板租约被释放且实验室面板从界面树移除");
        report.AppendLine("```");
        report.AppendLine();
        report.AppendLine("## 5. 边界");
        report.AppendLine();
        report.AppendLine("- 本次 10K 结果验证的是单服务器权威 3D 刚体世界，不等价于已经验证 150 名玩家的网络收发、兴趣管理或状态同步成本。");
        report.AppendLine("- 表现层有固定采样上限；压力数字来自权威物理世界，不以画面中绘制数量冒充模拟数量。");
        report.AppendLine("- 固定步只支持当前明确配置的 30Hz 一对一推进；20Hz 主循环不在支持范围内。");
        report.AppendLine();
        report.AppendLine("## 6. UAT");
        report.AppendLine();
        report.AppendLine("- Launcher 预设与依赖闭包：PASS");
        report.AppendLine("- 默认 Stacking 首屏与九场景入口：PASS");
        report.AppendLine("- 空白屏幕区域不阻断世界交互：PASS");
        report.AppendLine("- Pause / Single Step：PASS");
        report.AppendLine("- 确定性记录、重建、回放：PASS");
        report.AppendLine("- 10K 精确权威刚体数量与 30Hz 物理预算：PASS");
        report.AppendLine("- MapUnloaded 后 Surface 释放：PASS");
        return report.ToString();
    }

    private static string BuildPathDiagram() =>
        "flowchart LR\n" +
        "    A[\"从 Raylib 正式预设启动\"] --> B[\"进入默认 Stacking 场景\"]\n" +
        "    B --> C[\"浏览九个物理样例\"]\n" +
        "    C --> D[\"暂停并单步观察\"]\n" +
        "    D --> E[\"确定性记录、重建、回放通过\"]\n" +
        "    E --> F[\"选择 10K 权威刚体\"]\n" +
        "    F --> G[\"采集 30Hz 物理预算\"]\n" +
        "    G --> H[\"离开地图并释放面板\"]\n";

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "launcher.config.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "mods")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Ludots repository root.");
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public System.Numerics.Vector2 GetMousePosition() => System.Numerics.Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
