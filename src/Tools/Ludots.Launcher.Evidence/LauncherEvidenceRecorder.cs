using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using CameraAcceptanceMod;
using Ludots.Adapter.Raylib.Services;
using Ludots.Adapter.Web.Services;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map.Board;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Launcher.Backend;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Skia;
using MassNavigationMod;
using MassNavigationMod.Runtime;
using Navigation2DPlaygroundMod;
using Navigation2DPlaygroundMod.Systems;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using SkiaSharp;

namespace Ludots.Launcher.Evidence;

public sealed record LauncherRecordingRequest(
    string RepoRoot,
    LauncherLaunchPlan Plan,
    string BootstrapPath,
    string OutputDirectory,
    string CommandText);

public sealed record LauncherRecordingResult(
    string OutputDirectory,
    string BattleReportPath,
    string TracePath,
    string PathPath,
    string SummaryPath,
    string VisibleChecklistPath,
    IReadOnlyList<string> ScreenPaths,
    string NormalizedSignature);

public static class LauncherEvidenceRecorder
{
    private static readonly QueryDescription CameraNamedEntityQuery = new QueryDescription()
        .WithAll<Name, WorldPositionCm>();

    private static readonly QueryDescription RoadNamedVisualEntityQuery = new QueryDescription()
        .WithAll<Name, VisualTransform>();

    private static readonly QueryDescription NavDynamicAgentsQuery = new QueryDescription()
        .WithAll<NavAgent2D, Position2D, Velocity2D, NavPlaygroundTeam>()
        .WithNone<NavPlaygroundBlocker>();

    private static readonly QueryDescription NavBlockerQuery = new QueryDescription()
        .WithAll<Position2D, NavPlaygroundBlocker>();

    private static readonly QueryDescription NavScenarioEntitiesQuery = new QueryDescription()
        .WithAll<NavPlaygroundTeam>();

    private static readonly QueryDescription NavFlowGoalQuery = new QueryDescription()
        .WithAll<NavFlowGoal2D>();

    private static readonly QueryDescription MassNavigationAgentQuery = new QueryDescription()
        .WithAll<MassNavigationAgentTag, MassNavigationAgentIndex, WorldPositionCm>();

    private static readonly QueryDescription MassNavigationControllableQuery = new QueryDescription()
        .WithAll<MassNavigationAgentTag, MassNavigationAgentIndex, MassNavigationControllable, Team, OrderBuffer, WorldPositionCm, SelectionSelectableTag, PresentationOwnerHasPerformerPayload>();

    private static readonly QueryDescription MassNavigationBlockerQuery = new QueryDescription()
        .WithAll<MassNavigationBlocker, MassNavigationBlockerProfile, WorldPositionCm, PresentationOwnerHasPerformerPayload>();

    private static readonly QueryDescription MassNavigationHotspotMarkerQuery = new QueryDescription()
        .WithAll<MassNavigationHotspotMarker, WorldPositionCm, PresentationOwnerHasPerformerPayload>();

    private const float DeltaTime = 1f / 60f;
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;
    private const int CameraImageWidth = 1600;
    private const int CameraImageHeight = 900;
    private const int RoadImageWidth = 1600;
    private const int RoadImageHeight = 900;
    private const int NavImageWidth = 1600;
    private const int NavImageHeight = 900;
    private const int MassNavigationImageWidth = 1600;
    private const int MassNavigationImageHeight = 900;
    private const int MassNavigationInitialSettleTicks = 20;
    private const int MassNavigationCommandSettleTicks = 90;
    private const int MassNavigationRemoteSettleTicks = 20;
    private const int MassNavigationReturnSettleTicks = 20;
    private const int MassNavigationFullCommandSettleTicks = 150;
    private const int MassNavigationSelectionSampleCount = 128;
    private const int MassNavigationFullCommandMinimumAgents = 10_000;
    private const int MassNavigationRaylibBenchmarkWarmupFrames = 20;
    private const int MassNavigationRaylibBenchmarkFramesPerPass = 80;
    private const int MassNavigationRaylibBenchmarkAgentDrawCount = 10_000;
    private const int MassNavigationRaylibBenchmarkObstacleTargetCount = 40_000;
    private const int MassNavigationRaylibBenchmarkObstacleBucketCount = 4096;
    private const double MassNavigationRaylibSmokeFrameP95Ms = 16.667d;
    private const double MassNavigationRaylibSmokeOverlayP95DeltaMs = 2.0d;
    private const double MassNavigationRaylibProductionFrameP95Ms = 10.0d;
    private const double MassNavigationRaylibProductionFrameP99Ms = 12.5d;
    private const double MassNavigationRaylibProductionOverlayDrawMs = 0.5d;
    private const string MassNavigationRendererScope = "raylib_framebuffer_micro_benchmark";
    private const string MassNavigationManualUatSignoffFileName = "manual-uat-signoff.json";
    private const string MassNavigationManualUatBlocker = "Manual human-operated UAT signoff is missing; replay/smoke evidence cannot be reported as production PASS.";
    private const float MassNavigationMovingSpeedSquaredThreshold = 0.0001f;
    private const int NavAcceptanceAgentsPerTeam = 64;
    private const int NavFinalTick = 720;
    private const int NavTraceStrideTicks = 30;
    private const int NavCaptureStrideTicks = 120;
    private const float NavMovingSpeedSquaredThreshold = 400f;
    private const float NavMidProgressMinimumCm = 1200f;
    private const float NavFinalProgressMinimumCm = 4000f;
    private const float NavFinalCenterFractionLimit = 0.18f;
    private const float NavFinalCenterStoppedFractionLimit = 0.08f;
    private const float NavMovingAgentsFractionLimit = 0.35f;
    private const float NavCenterHalfWidthCm = 1200f;
    private const float NavCenterHalfHeightCm = 2600f;
    private const float NavWorldMinX = -14000f;
    private const float NavWorldMaxX = 14000f;
    private const float NavWorldMinY = -9000f;
    private const float NavWorldMaxY = 9000f;
    private static readonly Vector2 CameraProjectionClickWorldCm = new(3200f, 2000f);
    private static readonly Vector2 RoadSelectionWorldCm = new(-9800f, 0f);
    private static readonly Vector2 RoadCommandWorldCm = new(0f, 0f);
    private static readonly Vector2 RoadChunkShiftTargetCm = new(18000f, 0f);
    private static readonly Vector2 ChunkEastGateTargetCm = new(9000f, 0f);
    private static readonly Vector2 ChunkRedCapitalTargetCm = new(18000f, 0f);
    private static readonly string[] RoadBlueColumnNames = ["Blue Vanguard", "Blue North Column", "Blue South Column"];
    private static readonly Vector2[] RoadSelectionPickOffsetsPx =
    [
        Vector2.Zero,
        new Vector2(-12f, 0f),
        new Vector2(12f, 0f),
        new Vector2(0f, -12f),
        new Vector2(0f, 12f),
        new Vector2(-18f, -8f),
        new Vector2(18f, -8f),
        new Vector2(-18f, 8f),
        new Vector2(18f, 8f)
    ];
    private const float RoadCueMarkerHeightMeters = 0.15f;
    private const float RoadMovementMinimumCm = 2400f;
    private const float RoadWorldMinX = -22000f;
    private const float RoadWorldMaxX = 22000f;
    private const float RoadWorldMinY = -13000f;
    private const float RoadWorldMaxY = 13000f;

    public static Task<LauncherRecordingResult> RecordAsync(LauncherRecordingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(request.OutputDirectory);
        return InferScenario(request.Plan) switch
        {
            EvidenceScenario.CameraAcceptanceProjectionClick => Task.FromResult(RecordCameraAcceptanceProjection(request)),
            EvidenceScenario.RoadNetworkShowcaseCommandAndChunking => Task.FromResult(RecordRoadNetworkShowcase(request)),
            EvidenceScenario.ChunkStreamingShowcaseCameraWindows => Task.FromResult(RecordChunkStreamingShowcase(request)),
            EvidenceScenario.Navigation2DPlaygroundTimedAvoidance => Task.FromResult(RecordNavigation2DTimedAvoidance(request)),
            EvidenceScenario.MassNavigationLargeWorld => Task.FromResult(RecordMassNavigationLargeWorld(request)),
            _ => throw new InvalidOperationException($"No recording scenario is registered for root mods: {string.Join(", ", request.Plan.RootModIds)}")
        };
    }

    private static EvidenceScenario InferScenario(LauncherLaunchPlan plan)
    {
        if (plan.RootModIds.Any(id => string.Equals(id, "CameraAcceptanceMod", StringComparison.OrdinalIgnoreCase)))
        {
            return EvidenceScenario.CameraAcceptanceProjectionClick;
        }

        if (plan.RootModIds.Any(id => string.Equals(id, "Navigation2DPlaygroundMod", StringComparison.OrdinalIgnoreCase)))
        {
            return EvidenceScenario.Navigation2DPlaygroundTimedAvoidance;
        }

        if (plan.RootModIds.Any(IsMassNavigationRootMod))
        {
            return EvidenceScenario.MassNavigationLargeWorld;
        }

        if (plan.RootModIds.Any(id => string.Equals(id, "RoadNetworkShowcaseMod", StringComparison.OrdinalIgnoreCase)))
        {
            return EvidenceScenario.RoadNetworkShowcaseCommandAndChunking;
        }

        if (plan.RootModIds.Any(id => string.Equals(id, "ChunkStreamingShowcaseMod", StringComparison.OrdinalIgnoreCase)))
        {
            return EvidenceScenario.ChunkStreamingShowcaseCameraWindows;
        }

        return EvidenceScenario.None;
    }

    private static bool IsMassNavigationRootMod(string id)
    {
        return string.Equals(id, "MassNavigationMod", StringComparison.OrdinalIgnoreCase) ||
            (id.StartsWith("MassNavigationU", StringComparison.OrdinalIgnoreCase) &&
                id.EndsWith("ShowcaseMod", StringComparison.OrdinalIgnoreCase));
    }

    private static RecordingRuntime CreateRuntime(LauncherLaunchPlan plan, string bootstrapPath)
    {
        return string.Equals(plan.AdapterId, LauncherPlatformIds.Web, StringComparison.OrdinalIgnoreCase)
            ? CreateWebRuntime(plan, bootstrapPath)
            : CreateRaylibRuntime(plan, bootstrapPath);
    }

    private static RecordingRuntime CreateRaylibRuntime(LauncherLaunchPlan plan, string bootstrapPath)
    {
        var bootstrap = GameBootstrapper.InitializeFromBaseDirectory(plan.AppOutputDirectory, bootstrapPath);
        var engine = bootstrap.Engine;
        var config = bootstrap.Config;
        ApplyRaylibHostAssets(engine);

        var skiaRenderer = new SkiaUiRenderer();
        var textMeasurer = new SkiaTextMeasurer();
        var imageSizeProvider = new SkiaImageSizeProvider();
        var uiRoot = new UIRoot(skiaRenderer);
        uiRoot.Resize(DefaultWidth, DefaultHeight);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)textMeasurer);
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)imageSizeProvider);
        engine.SetService(CoreServiceKeys.UISystem, (Ludots.Core.UI.IUiSystem)new MarkupUiSystem(uiRoot, textMeasurer, imageSizeProvider));

        var inputBackend = new ScriptedInputBackend();
        var inputHandler = new PlayerInputHandler(inputBackend, new InputConfigPipelineLoader(engine.ConfigPipeline).Load());
        PushStartupInputContexts(config, inputHandler);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)inputBackend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);

        var initialCamera = new Camera3D
        {
            position = new Vector3(10f, 10f, 10f),
            target = new Vector3(0f, 0f, 0f),
            up = new Vector3(0f, 1f, 0f),
            fovy = 60f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };

        var cameraAdapter = new RaylibCameraAdapter(initialCamera);
        var viewController = new RaylibViewController(cameraAdapter, DefaultWidth, DefaultHeight);
        var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter);
        var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, viewController);
        var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, viewController);
        screenProjector.BindPresenter(cameraPresenter);
        screenRayProvider.BindPresenter(cameraPresenter);

        engine.SetService(CoreServiceKeys.ViewController, viewController);
        engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)screenProjector);
        engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)screenRayProvider);

        var cullingSystem = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, viewController, cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
        engine.RegisterPresentationSystem(cullingSystem);
        engine.SetService(CoreServiceKeys.CameraCullingDebugState, cullingSystem.DebugState);

        var renderCameraDebug = new RenderCameraDebugState();
        engine.SetService(CoreServiceKeys.RenderCameraDebugState, renderCameraDebug);
        engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));

        var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);
        WorldHudToScreenSystem? hudProjection = TryCreateHudProjection(engine, screenProjector, viewController);

        engine.Start();
        if (string.IsNullOrWhiteSpace(config.StartupMapId))
        {
            throw new InvalidOperationException("Invalid launcher bootstrap: StartupMapId cannot be empty.");
        }

        engine.LoadMap(config.StartupMapId);
        return new RecordingRuntime(plan.AdapterId, engine, config, inputBackend, screenProjector, cameraPresenter, renderCameraDebug, presentationFrameSetup, hudProjection);
    }

    private static void ApplyRaylibHostAssets(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshAssets))
        {
            throw new InvalidOperationException("Raylib evidence recording requires PresentationMeshAssetRegistry before host asset binding.");
        }

        new PresentationHostAssetConfigLoader(engine.ConfigPipeline, meshAssets)
            .Apply("raylib", engine.ConfigCatalog, engine.ConfigConflictReport);
    }

    private static RecordingRuntime CreateWebRuntime(LauncherLaunchPlan plan, string bootstrapPath)
    {
        var bootstrap = GameBootstrapper.InitializeFromBaseDirectory(plan.AppOutputDirectory, bootstrapPath);
        var engine = bootstrap.Engine;
        var config = bootstrap.Config;

        var skiaRenderer = new SkiaUiRenderer();
        var textMeasurer = new SkiaTextMeasurer();
        var imageSizeProvider = new SkiaImageSizeProvider();
        var uiRoot = new UIRoot(skiaRenderer);
        uiRoot.Resize(DefaultWidth, DefaultHeight);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)textMeasurer);
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)imageSizeProvider);
        engine.SetService(CoreServiceKeys.UISystem, (Ludots.Core.UI.IUiSystem)new MarkupUiSystem(uiRoot, textMeasurer, imageSizeProvider));

        var inputBackend = new ScriptedInputBackend();
        var inputHandler = new PlayerInputHandler(inputBackend, new InputConfigPipelineLoader(engine.ConfigPipeline).Load());
        PushStartupInputContexts(config, inputHandler);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)inputBackend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);

        var viewController = new WebViewController();
        viewController.SetResolution(DefaultWidth, DefaultHeight);
        var cameraAdapter = new WebCameraAdapter();
        var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, viewController);
        var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, viewController);
        var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter);
        screenProjector.BindPresenter(cameraPresenter);
        screenRayProvider.BindPresenter(cameraPresenter);

        engine.SetService(CoreServiceKeys.ViewController, (IViewController)viewController);
        engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)screenProjector);
        engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)screenRayProvider);

        var cullingSystem = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, viewController, cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
        engine.RegisterPresentationSystem(cullingSystem);
        engine.SetService(CoreServiceKeys.CameraCullingDebugState, cullingSystem.DebugState);

        var renderCameraDebug = new RenderCameraDebugState();
        engine.SetService(CoreServiceKeys.RenderCameraDebugState, renderCameraDebug);
        engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));

        var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);
        WorldHudToScreenSystem? hudProjection = TryCreateHudProjection(engine, screenProjector, viewController);

        engine.Start();
        if (string.IsNullOrWhiteSpace(config.StartupMapId))
        {
            throw new InvalidOperationException("Invalid launcher bootstrap: StartupMapId cannot be empty.");
        }

        engine.LoadMap(config.StartupMapId);
        return new RecordingRuntime(plan.AdapterId, engine, config, inputBackend, screenProjector, cameraPresenter, renderCameraDebug, presentationFrameSetup, hudProjection);
    }

    private static WorldHudToScreenSystem? TryCreateHudProjection(GameEngine engine, IScreenProjector screenProjector, IViewController viewController)
    {
        if (engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer) is not WorldHudBatchBuffer worldHud ||
            engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer) is not ScreenHudBatchBuffer screenHud)
        {
            return null;
        }

        var worldHudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
        return new WorldHudToScreenSystem(engine.World, worldHud, worldHudStrings, screenProjector, viewController, screenHud);
    }

    private static void PushStartupInputContexts(GameConfig config, PlayerInputHandler inputHandler)
    {
        if (config.StartupInputContexts == null)
        {
            return;
        }

        foreach (string contextId in config.StartupInputContexts)
        {
            if (!string.IsNullOrWhiteSpace(contextId))
            {
                inputHandler.PushContext(contextId);
            }
        }
    }

    private static void Tick(RecordingRuntime runtime, int count, List<double> frameTimesMs)
    {
        for (int i = 0; i < count; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            runtime.Engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)?.Clear();
            runtime.Engine.SetService(CoreServiceKeys.UiCaptured, false);
            runtime.Engine.Tick(DeltaTime);
            float alpha = runtime.PresentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
            runtime.CameraPresenter.Update(runtime.Engine.GameSession.Camera, alpha, runtime.RenderCameraDebug);
            runtime.HudProjection?.Update(DeltaTime);
            frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
        }
    }

    private static FrameTimingStats BuildFrameTimingStats(IReadOnlyList<double> frameTimesMs)
    {
        if (frameTimesMs.Count == 0)
        {
            return new FrameTimingStats(0d, 0d, 0d, 0d, 0);
        }

        double[] sorted = frameTimesMs.ToArray();
        Array.Sort(sorted);
        return new FrameTimingStats(
            P50Ms: PercentileSorted(sorted, 0.50d),
            P95Ms: PercentileSorted(sorted, 0.95d),
            P99Ms: PercentileSorted(sorted, 0.99d),
            MaxMs: sorted[^1],
            FrameCount: sorted.Length);
    }

    private static double PercentileSorted(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return 0d;
        }

        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        double clamped = Math.Clamp(percentile, 0d, 1d);
        double index = (sorted.Length - 1) * clamped;
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return sorted[lower];
        }

        double t = index - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * t);
    }

    private static void ClickPrimary(RecordingRuntime runtime, Vector2 screenPosition, List<double> frameTimesMs)
    {
        runtime.InputBackend.SetMousePosition(screenPosition);
        Tick(runtime, 1, frameTimesMs);
        runtime.InputBackend.SetButton("<Mouse>/LeftButton", true);
        Tick(runtime, 2, frameTimesMs);
        runtime.InputBackend.SetButton("<Mouse>/LeftButton", false);
        Tick(runtime, 2, frameTimesMs);
    }

    private static void ClickSecondary(RecordingRuntime runtime, Vector2 screenPosition, List<double> frameTimesMs)
    {
        runtime.InputBackend.SetMousePosition(screenPosition);
        Tick(runtime, 1, frameTimesMs);
        runtime.InputBackend.SetButton("<Mouse>/RightButton", true);
        Tick(runtime, 2, frameTimesMs);
        runtime.InputBackend.SetButton("<Mouse>/RightButton", false);
        Tick(runtime, 2, frameTimesMs);
    }

    private static void ApplyCameraTarget(RecordingRuntime runtime, Vector2 targetCm, List<double> frameTimesMs, int settleTicks)
    {
        runtime.Engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            TargetCm = targetCm
        });

        if (settleTicks > 0)
        {
            Tick(runtime, settleTicks, frameTimesMs);
        }
    }

    private static void AdvanceUntilCameraCueVisible(
        RecordingRuntime runtime,
        List<double> frameTimesMs,
        Vector2 clickTargetWorldCm,
        int maxFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            var snapshot = SampleCameraSnapshot(
                runtime,
                "cue_probe",
                frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d,
                clickTargetWorldCm);
            if (snapshot.CueMarkerPresent)
            {
                return;
            }

            Tick(runtime, 1, frameTimesMs);
        }
    }

    private static LauncherRecordingResult RecordCameraAcceptanceProjection(LauncherRecordingRequest request)
    {
        string screensDir = Path.Combine(request.OutputDirectory, "screens");
        Directory.CreateDirectory(screensDir);

        var frameTimesMs = new List<double>();
        var timeline = new List<CameraSnapshot>();
        var captureFrames = new List<CaptureFrame>();

        using var runtime = CreateRuntime(request.Plan, request.BootstrapPath);
        if (!string.Equals(runtime.Config.StartupMapId, CameraAcceptanceIds.ProjectionMapId, StringComparison.OrdinalIgnoreCase))
        {
            runtime.Engine.LoadMap(CameraAcceptanceIds.ProjectionMapId);
        }

        Tick(runtime, 5, frameTimesMs);
        CaptureCameraSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "000_start", clickTargetWorldCm: null);

        Vector2 clickScreen = runtime.ProjectWorldCm(CameraProjectionClickWorldCm);
        ClickPrimary(runtime, clickScreen, frameTimesMs);
        AdvanceUntilCameraCueVisible(runtime, frameTimesMs, CameraProjectionClickWorldCm, maxFrames: 12);
        CaptureCameraSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "001_after_click", CameraProjectionClickWorldCm);

        Tick(runtime, 24, frameTimesMs);
        CaptureCameraSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "002_marker_live", CameraProjectionClickWorldCm);

        int settleFrames = 0;
        while (timeline[^1].CueMarkerPresent && settleFrames < 240)
        {
            Tick(runtime, 1, frameTimesMs);
            var probe = SampleCameraSnapshot(runtime, "probe", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, CameraProjectionClickWorldCm);
            if (!probe.CueMarkerPresent)
            {
                break;
            }

            settleFrames++;
        }

        CaptureCameraSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "003_marker_expired", CameraProjectionClickWorldCm);

        WriteTimelineSheet("Camera acceptance projection click timeline", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        CameraAcceptanceResult acceptance = EvaluateCameraAcceptance(timeline);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildCameraBattleReport(request, timeline, captureFrames, frameTimesMs, acceptance));
        File.WriteAllText(tracePath, BuildCameraTraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildCameraPathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildCameraVisibleChecklist(captureFrames));
        File.WriteAllText(summaryPath, BuildCameraSummaryJson(request, acceptance));

        if (!acceptance.Success)
        {
            throw new InvalidOperationException(acceptance.FailureSummary);
        }

        return new LauncherRecordingResult(
            request.OutputDirectory,
            battleReportPath,
            tracePath,
            pathPath,
            summaryPath,
            visibleChecklistPath,
            captureFrames.Select(frame => Path.Combine(screensDir, frame.FileName)).Append(Path.Combine(screensDir, "timeline.png")).ToList(),
            acceptance.NormalizedSignature);
    }

    private static void CaptureCameraSnapshot(
        RecordingRuntime runtime,
        string screensDir,
        IReadOnlyList<double> frameTimesMs,
        List<CameraSnapshot> timeline,
        List<CaptureFrame> captureFrames,
        string step,
        Vector2? clickTargetWorldCm)
    {
        CameraSnapshot snapshot = SampleCameraSnapshot(runtime, step, frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, clickTargetWorldCm);
        timeline.Add(snapshot);
        string fileName = $"{step}.png";
        string outputPath = Path.Combine(screensDir, fileName);
        WriteCameraSnapshotImage(snapshot, outputPath);
        captureFrames.Add(new CaptureFrame(snapshot.Tick, step, fileName, snapshot.CueMarkerPresent ? 1 : 0, snapshot.DummyCount, 0f, 0f));
    }

    private static CameraSnapshot SampleCameraSnapshot(RecordingRuntime runtime, string step, double tickMs, Vector2? clickTargetWorldCm)
    {
        var namedEntities = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        var dummyPositions = new List<Vector2>();

        runtime.Engine.World.Query(in CameraNamedEntityQuery, (ref Name name, ref WorldPositionCm position) =>
        {
            Vector2 point = position.Value.ToVector2();
            string entityName = name.Value;
            if (!namedEntities.ContainsKey(entityName))
            {
                namedEntities[entityName] = point;
            }

            if (string.Equals(entityName, "Dummy", StringComparison.OrdinalIgnoreCase))
            {
                dummyPositions.Add(point);
            }
        });

        Vector2 cueMarkerWorldCm = Vector2.Zero;
        bool cueMarkerPresent = false;
        PrimitiveDrawBuffer? primitives = runtime.Engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
        if (primitives != null)
        {
            Vector3 cueMarkerVisual = WorldUnits.WorldCmToVisualMeters(
                new WorldCmInt2((int)CameraProjectionClickWorldCm.X, (int)CameraProjectionClickWorldCm.Y),
                yMeters: 0.15f);
            foreach (ref readonly PrimitiveDrawItem primitive in primitives.GetSpan())
            {
                if (Vector3.Distance(primitive.Position, cueMarkerVisual) <= 0.05f)
                {
                    WorldCmInt2 worldCm = WorldUnits.VisualMetersToWorldCm(primitive.Position);
                    cueMarkerWorldCm = new Vector2(worldCm.X, worldCm.Y);
                    cueMarkerPresent = true;
                    break;
                }
            }
        }

        var overlayLines = ExtractOverlayText(runtime.Engine.GetService(CoreServiceKeys.ScreenOverlayBuffer));
        Vector2 cameraTarget = runtime.Engine.GameSession.Camera.State.TargetCm;
        string activeCameraId = runtime.Engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId ?? "(none)";

        return new CameraSnapshot(
            Tick: runtime.Engine.GameSession.CurrentTick,
            Step: step,
            TickMs: tickMs,
            ActiveMapId: runtime.Engine.CurrentMapSession?.MapId.ToString() ?? runtime.Config.StartupMapId,
            ActiveCameraId: activeCameraId,
            CameraTargetCm: cameraTarget,
            CameraDistanceCm: runtime.Engine.GameSession.Camera.State.DistanceCm,
            CameraIsFollowing: runtime.Engine.GameSession.Camera.State.IsFollowing,
            ClickTargetWorldCm: clickTargetWorldCm,
            NamedEntities: namedEntities,
            DummyPositions: dummyPositions,
            CueMarkerPresent: cueMarkerPresent,
            CueMarkerWorldCm: cueMarkerWorldCm,
            OverlayLines: overlayLines);
    }

    private static CameraAcceptanceResult EvaluateCameraAcceptance(IReadOnlyList<CameraSnapshot> timeline)
    {
        CameraSnapshot start = timeline[0];
        CameraSnapshot afterClick = timeline[1];
        CameraSnapshot markerLive = timeline[2];
        CameraSnapshot markerExpired = timeline[3];

        var failures = new List<string>();

        AddAcceptanceCheck(markerLive.DummyCount == start.DummyCount + 1,
            $"Projection click should spawn one Dummy by the live capture, but count moved {start.DummyCount} -> {markerLive.DummyCount}.", failures);

        CameraSnapshot spawnedSnapshot = markerLive.DummyCount > 0 ? markerLive : markerExpired;
        if (spawnedSnapshot.ClickTargetWorldCm.HasValue)
        {
            Vector2 click = spawnedSnapshot.ClickTargetWorldCm.Value;
            bool dummyAtClick = spawnedSnapshot.DummyPositions.Any(position => Vector2.Distance(position, click) <= 5f);
            AddAcceptanceCheck(dummyAtClick,
                $"Spawned Dummy did not land on click target {FormatPoint(click)}.", failures);
        }

        AddAcceptanceCheck(afterClick.CueMarkerPresent,
            "Cue marker should be visible immediately after click.", failures);
        AddAcceptanceCheck(markerLive.CueMarkerPresent,
            "Cue marker should remain visible for the live mid-frame capture.", failures);
        AddAcceptanceCheck(!markerExpired.CueMarkerPresent,
            "Cue marker should expire by the final capture.", failures);
        AddAcceptanceCheck(markerExpired.DummyCount == markerLive.DummyCount,
            "Spawned Dummy should persist after the cue marker expires.", failures);
        AddAcceptanceCheck(string.Equals(afterClick.ActiveMapId, CameraAcceptanceIds.ProjectionMapId, StringComparison.OrdinalIgnoreCase),
            $"Expected projection map but active map was {afterClick.ActiveMapId}.", failures);

        Vector2 spawnedDummy = spawnedSnapshot.DummyPositions.LastOrDefault();
        Vector2 normalizedSpawn = NormalizeCameraSpawnPoint(spawnedDummy, spawnedSnapshot.ClickTargetWorldCm);
        string normalizedSignature = string.Join("|", new[]
        {
            "camera_acceptance_projection_click",
            $"dummy:{start.DummyCount}->{markerLive.DummyCount}",
            $"spawn:{MathF.Round(normalizedSpawn.X):F0},{MathF.Round(normalizedSpawn.Y):F0}",
            $"cue:{(afterClick.CueMarkerPresent ? 1 : 0)}{(markerLive.CueMarkerPresent ? 1 : 0)}{(markerExpired.CueMarkerPresent ? 1 : 0)}",
            $"camera:{MathF.Round(afterClick.CameraTargetCm.X):F0},{MathF.Round(afterClick.CameraTargetCm.Y):F0}"
        });

        string verdict = failures.Count == 0
            ? $"Projection click passes: Dummy count is {start.DummyCount}->{afterClick.DummyCount}, cue marker lives across the mid capture, and expires by tick {markerExpired.Tick}."
            : "Projection click fails: screen-ray click, spawned Dummy persistence, or cue marker lifetime diverged from acceptance expectations.";
        string failureSummary = failures.Count == 0 ? verdict : string.Join(Environment.NewLine, failures);

        return new CameraAcceptanceResult(
            Success: failures.Count == 0,
            Verdict: verdict,
            FailureSummary: failureSummary,
            FailedChecks: failures,
            StartDummyCount: start.DummyCount,
            AfterClickDummyCount: markerLive.DummyCount,
            SpawnedDummyWorldCm: spawnedDummy,
            CueMarkerVisibleAfterClick: afterClick.CueMarkerPresent,
            CueMarkerVisibleMidCapture: markerLive.CueMarkerPresent,
            CueMarkerVisibleFinalCapture: markerExpired.CueMarkerPresent,
            FinalTick: markerExpired.Tick,
            NormalizedSignature: normalizedSignature);
    }

    private static string BuildCameraBattleReport(
        LauncherRecordingRequest request,
        IReadOnlyList<CameraSnapshot> timeline,
        IReadOnlyList<CaptureFrame> captureFrames,
        IReadOnlyList<double> frameTimesMs,
        CameraAcceptanceResult acceptance)
    {
        CameraSnapshot final = timeline[^1];
        FrameTimingStats frameStats = BuildFrameTimingStats(frameTimesMs);
        string evidenceImages = string.Join(", ", captureFrames.Select(frame => $"`screens/{frame.FileName}`").Append("`screens/timeline.png`"));

        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: camera-acceptance-projection-click");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: verify a launcher-started camera acceptance slice can click ground through the selected adapter, spawn a Dummy at the raycast point, and show a transient cue marker that expires cleanly.");
        sb.AppendLine("- Gameplay domain: real launcher bootstrap, real adapter projection/raycast wiring, real `CameraAcceptanceMod` projection scenario.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none");
        sb.AppendLine("- Map: `mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_projection.json`");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Launch command: `{request.CommandText}`");
        sb.AppendLine($"- Click target: `{FormatPoint(CameraProjectionClickWorldCm)}`");
        sb.AppendLine("- Clock profile: fixed `1/60s`");
        sb.AppendLine($"- Evidence images: {evidenceImages}");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Boot the unified launcher runtime bootstrap for CameraAcceptanceMod.");
        sb.AppendLine("2. Let the adapter camera and projector settle on the projection map.");
        sb.AppendLine("3. Project the target world point into screen space with the selected adapter and inject a left click.");
        sb.AppendLine("4. Capture start, first cue-visible post-click, marker-live, and marker-expired frames.");
        sb.AppendLine();
        sb.AppendLine("## Expected Outcomes");
        sb.AppendLine("- Primary success condition: exactly one Dummy is added at the click target and the first post-click cue-visible frame appears consistently.");
        sb.AppendLine("- Failure branch condition: click lands on the wrong point, no Dummy appears, or the cue marker lifetime is broken.");
        sb.AppendLine("- Key metrics: Dummy count delta, spawned world position, cue marker visibility over time, active camera id.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (CameraSnapshot snapshot in timeline)
        {
            sb.AppendLine($"- [T+{snapshot.Tick:000}] CameraAcceptance.{snapshot.Step} -> map={snapshot.ActiveMapId} camera={snapshot.ActiveCameraId} | Dummy={snapshot.DummyCount} | Cue={(snapshot.CueMarkerPresent ? "On" : "Off")} | Target={FormatPoint(snapshot.CameraTargetCm)} | Tick={snapshot.TickMs:F3}ms");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        foreach (string failedCheck in acceptance.FailedChecks)
        {
            sb.AppendLine($"- failed-check: {failedCheck}");
        }

        sb.AppendLine($"- reason: Dummy count moved `{acceptance.StartDummyCount}` -> `{acceptance.AfterClickDummyCount}`, spawned at `{FormatPoint(acceptance.SpawnedDummyWorldCm)}`, cue visibility sequence `{(acceptance.CueMarkerVisibleAfterClick ? 1 : 0)}{(acceptance.CueMarkerVisibleMidCapture ? 1 : 0)}{(acceptance.CueMarkerVisibleFinalCapture ? 1 : 0)}`.");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- screenshot captures: `{captureFrames.Count}`");
        sb.AppendLine($"- median headless tick: `{frameStats.P50Ms:F3}ms`");
        sb.AppendLine($"- max headless tick: `{frameStats.MaxMs:F3}ms`");
        sb.AppendLine($"- active camera at click: `{timeline[1].ActiveCameraId}`");
        sb.AppendLine($"- normalized signature: `{acceptance.NormalizedSignature}`");
        sb.AppendLine($"- final camera target: `{FormatPoint(final.CameraTargetCm)}`");
        sb.AppendLine("- reusable wiring: `launcher.runtime.json`, `GameBootstrapper`, `CoreScreenProjector`, `IScreenRayProvider`, `PlayerInputHandler`");
        return sb.ToString();
    }

    private static Vector2 NormalizeCameraSpawnPoint(Vector2 spawnedDummy, Vector2? clickTargetWorldCm)
    {
        if (clickTargetWorldCm.HasValue && Vector2.Distance(spawnedDummy, clickTargetWorldCm.Value) <= 5f)
        {
            return clickTargetWorldCm.Value;
        }

        return new Vector2(MathF.Round(spawnedDummy.X), MathF.Round(spawnedDummy.Y));
    }

    private static string BuildCameraTraceJsonl(string adapterId, IReadOnlyList<CameraSnapshot> timeline)
    {
        var lines = new List<string>(timeline.Count);
        for (int index = 0; index < timeline.Count; index++)
        {
            CameraSnapshot snapshot = timeline[index];
            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"camera-{adapterId}-{index + 1:000}",
                tick = snapshot.Tick,
                step = snapshot.Step,
                map = snapshot.ActiveMapId,
                camera = snapshot.ActiveCameraId,
                dummy_count = snapshot.DummyCount,
                cue_marker = snapshot.CueMarkerPresent,
                camera_target_x = Math.Round(snapshot.CameraTargetCm.X, 2),
                camera_target_y = Math.Round(snapshot.CameraTargetCm.Y, 2),
                tick_ms = Math.Round(snapshot.TickMs, 4),
                status = "done"
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildCameraPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Boot launcher runtime for CameraAcceptanceMod] --> B[Settle adapter camera + projector]",
            "    B --> C[Project click world point through selected adapter]",
            "    C --> D[Inject left-click via PlayerInputHandler]",
            "    D --> E{Dummy spawned and cue marker visible?}",
            "    E -->|yes| F[Capture live cue frame]",
            "    F --> G{Cue marker expires while Dummy persists?}",
            "    G -->|yes| H[Write battle-report + trace + path + PNG timeline]",
            "    E -->|no| X[Fail acceptance: projection click diverged]",
            "    G -->|no| Y[Fail acceptance: cue lifetime diverged]"
        }) + Environment.NewLine;
    }

    private static string BuildCameraVisibleChecklist(IReadOnlyList<CaptureFrame> frames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: camera-acceptance-projection-click");
        sb.AppendLine();
        sb.AppendLine("- The `after_click` frame should show one more Dummy than `start` and a visible cue marker at the click point.");
        sb.AppendLine("- The `marker_live` frame should still show the cue marker.");
        sb.AppendLine("- The `marker_expired` frame should keep the new Dummy but remove the cue marker.");
        sb.AppendLine("- `screens/timeline.png` gives a compact strip for side-by-side adapter review.");
        sb.AppendLine();
        foreach (CaptureFrame frame in frames)
        {
            sb.AppendLine($"- `{frame.FileName}`: dummy={frame.CenterStoppedAgents}, cue={(frame.CenterCount > 0 ? "visible" : "hidden")}");
        }

        return sb.ToString();
    }

    private static string BuildCameraSummaryJson(LauncherRecordingRequest request, CameraAcceptanceResult acceptance)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = "camera_acceptance_projection_click",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            dummy_before = acceptance.StartDummyCount,
            dummy_after_click = acceptance.AfterClickDummyCount,
            spawned_dummy = new
            {
                x = Math.Round(acceptance.SpawnedDummyWorldCm.X, 2),
                y = Math.Round(acceptance.SpawnedDummyWorldCm.Y, 2)
            },
            cue_after_click = acceptance.CueMarkerVisibleAfterClick,
            cue_mid = acceptance.CueMarkerVisibleMidCapture,
            cue_final = acceptance.CueMarkerVisibleFinalCapture,
            final_tick = acceptance.FinalTick,
            normalized_signature = acceptance.NormalizedSignature
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void WriteCameraSnapshotImage(CameraSnapshot snapshot, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(CameraImageWidth, CameraImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(9, 12, 18));

        var worldPoints = snapshot.NamedEntities.Values
            .Concat(snapshot.DummyPositions)
            .Append(snapshot.CameraTargetCm)
            .Concat(snapshot.ClickTargetWorldCm.HasValue ? new[] { snapshot.ClickTargetWorldCm.Value } : Array.Empty<Vector2>())
            .Concat(snapshot.CueMarkerPresent ? new[] { snapshot.CueMarkerWorldCm } : Array.Empty<Vector2>())
            .ToList();

        if (worldPoints.Count == 0)
        {
            worldPoints.Add(Vector2.Zero);
        }

        float minX = worldPoints.Min(point => point.X) - 1200f;
        float maxX = worldPoints.Max(point => point.X) + 1200f;
        float minY = worldPoints.Min(point => point.Y) - 1200f;
        float maxY = worldPoints.Max(point => point.Y) + 1200f;

        using var gridPaint = new SKPaint { Color = new SKColor(36, 48, 66), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 20f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(185, 192, 208), IsAntialias = true, TextSize = 16f };
        using var cameraPaint = new SKPaint { Color = new SKColor(255, 210, 96), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f };
        using var clickPaint = new SKPaint { Color = new SKColor(255, 132, 72), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f };
        using var cuePaint = new SKPaint { Color = new SKColor(255, 190, 92), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
        using var heroPaint = new SKPaint { Color = new SKColor(78, 214, 119), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var scoutPaint = new SKPaint { Color = new SKColor(120, 190, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var captainPaint = new SKPaint { Color = new SKColor(255, 221, 108), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var dummyPaint = new SKPaint { Color = new SKColor(240, 102, 160), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var genericPaint = new SKPaint { Color = new SKColor(196, 204, 224), IsAntialias = true, Style = SKPaintStyle.Fill };

        DrawWorldGrid(canvas, minX, maxX, minY, maxY, gridPaint, CameraImageWidth, CameraImageHeight);

        foreach ((string name, Vector2 position) in snapshot.NamedEntities.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            SKPoint point = ToScreen(position, minX, maxX, minY, maxY, CameraImageWidth, CameraImageHeight);
            SKPaint fill = ResolveEntityPaint(name, heroPaint, scoutPaint, captainPaint, dummyPaint, genericPaint);
            canvas.DrawCircle(point.X, point.Y, 8f, fill);
            canvas.DrawText(name, point.X + 12f, point.Y - 10f, minorTextPaint);
        }

        foreach (Vector2 dummy in snapshot.DummyPositions)
        {
            SKPoint point = ToScreen(dummy, minX, maxX, minY, maxY, CameraImageWidth, CameraImageHeight);
            canvas.DrawCircle(point.X, point.Y, 10f, dummyPaint);
        }

        DrawCrosshair(canvas, ToScreen(snapshot.CameraTargetCm, minX, maxX, minY, maxY, CameraImageWidth, CameraImageHeight), 12f, cameraPaint);
        if (snapshot.ClickTargetWorldCm.HasValue)
        {
            DrawCrosshair(canvas, ToScreen(snapshot.ClickTargetWorldCm.Value, minX, maxX, minY, maxY, CameraImageWidth, CameraImageHeight), 16f, clickPaint);
        }

        if (snapshot.CueMarkerPresent)
        {
            SKPoint cue = ToScreen(snapshot.CueMarkerWorldCm, minX, maxX, minY, maxY, CameraImageWidth, CameraImageHeight);
            canvas.DrawCircle(cue.X, cue.Y, 22f, cuePaint);
        }

        canvas.DrawText($"Camera Acceptance Projection | {snapshot.Step} | tick={snapshot.Tick}", 24, 34, labelPaint);
        canvas.DrawText($"Map={snapshot.ActiveMapId}  Camera={snapshot.ActiveCameraId}  Follow={snapshot.CameraIsFollowing}", 24, 64, minorTextPaint);
        canvas.DrawText($"CameraTarget={FormatPoint(snapshot.CameraTargetCm)}  Distance={snapshot.CameraDistanceCm:F0}cm  DummyCount={snapshot.DummyCount}", 24, 92, minorTextPaint);
        canvas.DrawText($"CueMarker={(snapshot.CueMarkerPresent ? "visible" : "expired")}  Tick={snapshot.TickMs:F3}ms", 24, 120, minorTextPaint);
        if (snapshot.OverlayLines.Count > 0)
        {
            canvas.DrawText(snapshot.OverlayLines[0], 24, 148, minorTextPaint);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static LauncherRecordingResult RecordRoadNetworkShowcase(LauncherRecordingRequest request)
    {
        string screensDir = Path.Combine(request.OutputDirectory, "screens");
        Directory.CreateDirectory(screensDir);

        var timeline = new List<RoadSnapshot>();
        var captureFrames = new List<CaptureFrame>();
        var frameTimesMs = new List<double>();

        using var runtime = CreateRuntime(request.Plan, request.BootstrapPath);
        if (!string.Equals(runtime.Config.StartupMapId, "road_network_showcase_chunked", StringComparison.OrdinalIgnoreCase))
        {
            runtime.Engine.LoadMap("road_network_showcase_chunked");
        }

        Tick(runtime, 10, frameTimesMs);
        AssertRoadOverlay(runtime.Engine.GetService(CoreServiceKeys.ScreenOverlayBuffer));
        CaptureRoadSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "000_start", null);

        ClickPrimaryUntilSelected(runtime, "Blue Vanguard", frameTimesMs);
        Tick(runtime, 6, frameTimesMs);
        CaptureRoadSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "001_selected", null);

        Vector2 commandScreen = runtime.ProjectWorldCm(RoadCommandWorldCm);
        ClickSecondary(runtime, commandScreen, frameTimesMs);
        AdvanceUntilRoadStatus(runtime, frameTimesMs, maxFrames: 36);
        CaptureRoadSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "002_command_accepted", RoadCommandWorldCm);

        AdvanceUntilRoadMovement(runtime, frameTimesMs, timeline[2].ControlledActorName, timeline[2].ControlledActorWorldCm, maxFrames: 420);
        CaptureRoadSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "003_column_advancing", RoadCommandWorldCm);

        ApplyCameraTarget(runtime, RoadChunkShiftTargetCm, frameTimesMs, settleTicks: 4);
        AdvanceUntilRoadChunkShift(runtime, frameTimesMs, timeline[0].ActiveChunkSignature, maxFrames: 48);
        CaptureRoadSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "004_chunk_shifted", RoadCommandWorldCm);

        WriteTimelineSheet("Road network showcase timeline", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        RoadAcceptanceResult acceptance = EvaluateRoadAcceptance(timeline);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildRoadBattleReport(request, timeline, captureFrames, frameTimesMs, acceptance));
        File.WriteAllText(tracePath, BuildRoadTraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildRoadPathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildRoadVisibleChecklist(timeline));
        File.WriteAllText(summaryPath, BuildRoadSummaryJson(request, acceptance));

        if (!acceptance.Success)
        {
            throw new InvalidOperationException(acceptance.FailureSummary);
        }

        return new LauncherRecordingResult(
            request.OutputDirectory,
            battleReportPath,
            tracePath,
            pathPath,
            summaryPath,
            visibleChecklistPath,
            captureFrames.Select(frame => Path.Combine(screensDir, frame.FileName)).Append(Path.Combine(screensDir, "timeline.png")).ToList(),
            acceptance.NormalizedSignature);
    }

    private static void CaptureRoadSnapshot(
        RecordingRuntime runtime,
        string screensDir,
        IReadOnlyList<double> frameTimesMs,
        List<RoadSnapshot> timeline,
        List<CaptureFrame> captureFrames,
        string step,
        Vector2? commandTargetWorldCm)
    {
        RoadSnapshot snapshot = SampleRoadSnapshot(runtime, step, frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, commandTargetWorldCm);
        timeline.Add(snapshot);
        string fileName = $"{step}.png";
        WriteRoadSnapshotImage(snapshot, Path.Combine(screensDir, fileName));
        captureFrames.Add(new CaptureFrame(snapshot.Tick, step, fileName, snapshot.LoadedChunkCount, snapshot.SelectedNames.Count, 0f, 0f));
    }

    private static RoadSnapshot SampleRoadSnapshot(RecordingRuntime runtime, string step, double tickMs, Vector2? commandTargetWorldCm)
    {
        var namedEntities = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        runtime.Engine.World.Query(in CameraNamedEntityQuery, (ref Name name, ref WorldPositionCm position) =>
        {
            if (!namedEntities.ContainsKey(name.Value))
            {
                namedEntities[name.Value] = position.Value.ToVector2();
            }
        });

        var selectedNames = new List<string>();
        Entity[] selectedEntities = SelectionContextRuntime.SnapshotCurrentSelection(runtime.Engine.World, runtime.Engine.GlobalContext);
        for (int i = 0; i < selectedEntities.Length; i++)
        {
            Entity selected = selectedEntities[i];
            if (!runtime.Engine.World.IsAlive(selected) || !runtime.Engine.World.Has<Name>(selected))
            {
                continue;
            }

            selectedNames.Add(runtime.Engine.World.Get<Name>(selected).Value);
        }
        selectedNames.Sort(StringComparer.OrdinalIgnoreCase);

        PrimitiveDrawBuffer? primitives = runtime.Engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
        bool cueMarkerPresent = false;
        Vector2 cueMarkerWorldCm = Vector2.Zero;
        if (commandTargetWorldCm.HasValue)
        {
            cueMarkerPresent = TryFindCueMarkerAt(primitives, commandTargetWorldCm.Value, out cueMarkerWorldCm);
        }

        var overlayLines = ExtractOverlayText(runtime.Engine.GetService(CoreServiceKeys.ScreenOverlayBuffer));
        string statusLine = overlayLines.Count > 0 ? overlayLines[^1] : string.Empty;

        int loadedChunkCount = 0;
        int loadedNodeCount = 0;
        int chunkSizeCm = 0;
        var activeChunkKeys = Array.Empty<long>();
        if (TryGetRoadBoard(runtime.Engine, out NodeGraphBoard? board))
        {
            loadedChunkCount = board.LoadedChunksSource.ActiveChunkKeys.Count;
            loadedNodeCount = board.GraphRuntime.CurrentGraph.NodeCount;
            chunkSizeCm = board.LoadedChunksSource.ChunkSizeCm;
            activeChunkKeys = board.LoadedChunksSource.ActiveChunkKeys.OrderBy(key => key).ToArray();
        }

        RoadSplineBuffer? roads = runtime.Engine.GetService(CoreServiceKeys.RoadSplineBuffer);
        var splines = new List<RoadSplineCapture>(roads?.Count ?? 0);
        if (roads != null)
        {
            for (int index = 0; index < roads.Count; index++)
            {
                splines.Add(new RoadSplineCapture(
                    roads.StableIds[index],
                    new Vector3(roads.P0X[index], roads.P0Y[index], roads.P0Z[index]),
                    new Vector3(roads.P1X[index], roads.P1Y[index], roads.P1Z[index]),
                    new Vector3(roads.P2X[index], roads.P2Y[index], roads.P2Z[index]),
                    new Vector3(roads.P3X[index], roads.P3Y[index], roads.P3Z[index]),
                    roads.Width[index]));
            }
        }

        namedEntities.TryGetValue("Blue Vanguard", out Vector2 blueVanguardWorldCm);
        namedEntities.TryGetValue("Blue North Column", out Vector2 blueNorthWorldCm);
        namedEntities.TryGetValue("Blue South Column", out Vector2 blueSouthWorldCm);
        string controlledActorName = ResolveControlledActorName(runtime.Engine, namedEntities);
        namedEntities.TryGetValue(controlledActorName, out Vector2 controlledActorWorldCm);

        return new RoadSnapshot(
            Tick: runtime.Engine.GameSession.CurrentTick,
            Step: step,
            TickMs: tickMs,
            ActiveMapId: runtime.Engine.CurrentMapSession?.MapId.ToString() ?? runtime.Config.StartupMapId,
            CameraTargetCm: runtime.Engine.GameSession.Camera.State.TargetCm,
            LoadedChunkCount: loadedChunkCount,
            LoadedNodeCount: loadedNodeCount,
            ChunkSizeCm: chunkSizeCm,
            ActiveChunkKeys: activeChunkKeys,
            ActiveChunkSignature: string.Join(",", activeChunkKeys),
            RoadSplineCount: roads?.Count ?? 0,
            NamedEntities: namedEntities,
            SelectedNames: selectedNames,
            CueMarkerPresent: cueMarkerPresent,
            CueMarkerWorldCm: cueMarkerWorldCm,
            ControlledActorName: controlledActorName,
            ControlledActorWorldCm: controlledActorWorldCm,
            BlueVanguardWorldCm: blueVanguardWorldCm,
            BlueNorthWorldCm: blueNorthWorldCm,
            BlueSouthWorldCm: blueSouthWorldCm,
            StatusLine: statusLine,
            OverlayLines: overlayLines,
            Splines: splines);
    }

    private static void AdvanceUntilRoadStatus(RecordingRuntime runtime, List<double> frameTimesMs, int maxFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            Tick(runtime, 1, frameTimesMs);
            RoadSnapshot snapshot = SampleRoadSnapshot(runtime, "probe_status", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, RoadCommandWorldCm);
            if (IsAcceptedRoadStatus(snapshot.StatusLine))
            {
                return;
            }
        }
    }

    private static void AdvanceUntilRoadMovement(RecordingRuntime runtime, List<double> frameTimesMs, string controlledActorName, Vector2 startWorldCm, int maxFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            Tick(runtime, 1, frameTimesMs);
            RoadSnapshot snapshot = SampleRoadSnapshot(runtime, "probe_move", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, RoadCommandWorldCm);
            Vector2 currentWorldCm = string.Equals(snapshot.ControlledActorName, controlledActorName, StringComparison.OrdinalIgnoreCase)
                ? snapshot.ControlledActorWorldCm
                : ResolveNamedPosition(snapshot.NamedEntities, controlledActorName);
            if (currentWorldCm.X - startWorldCm.X >= RoadMovementMinimumCm)
            {
                return;
            }
        }
    }

    private static void AdvanceUntilRoadChunkShift(RecordingRuntime runtime, List<double> frameTimesMs, string startChunkSignature, int maxFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            Tick(runtime, 1, frameTimesMs);
            RoadSnapshot snapshot = SampleRoadSnapshot(runtime, "probe_chunks", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, RoadCommandWorldCm);
            if (!string.Equals(snapshot.ActiveChunkSignature, startChunkSignature, StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    private static RoadAcceptanceResult EvaluateRoadAcceptance(IReadOnlyList<RoadSnapshot> timeline)
    {
        RoadSnapshot start = timeline[0];
        RoadSnapshot selected = timeline[1];
        RoadSnapshot accepted = timeline[2];
        RoadSnapshot moved = timeline[3];
        RoadSnapshot shifted = timeline[4];

        var failures = new List<string>();
        AddAcceptanceCheck(selected.SelectedNames.Contains("Blue Vanguard", StringComparer.OrdinalIgnoreCase),
            "Left-click should select Blue Vanguard before the road command is issued.", failures);
        AddAcceptanceCheck(string.Equals(accepted.ControlledActorName, "Blue Vanguard", StringComparison.OrdinalIgnoreCase),
            $"Road showcase should route local commands through Blue Vanguard, but controlled actor was '{accepted.ControlledActorName}'.", failures);
        AddAcceptanceCheck(IsAcceptedRoadStatus(accepted.StatusLine),
            $"Expected accepted road status, but got '{accepted.StatusLine}'.", failures);
        AddAcceptanceCheck(!accepted.StatusLine.Contains("error 2", StringComparison.OrdinalIgnoreCase),
            $"Road command still reports error 2: '{accepted.StatusLine}'.", failures);
        AddAcceptanceCheck(accepted.CueMarkerPresent,
            "Right-click should emit a visible cue marker at the road command target.", failures);
        AddAcceptanceCheck(accepted.RoadSplineCount > 0,
            "Road spline buffer should contain visible showcase road segments.", failures);
        AddAcceptanceCheck(moved.ControlledActorWorldCm.X - accepted.ControlledActorWorldCm.X >= RoadMovementMinimumCm,
            $"{accepted.ControlledActorName} should begin moving along the road after acceptance, but only advanced {moved.ControlledActorWorldCm.X - accepted.ControlledActorWorldCm.X:F0}cm.", failures);
        AddAcceptanceCheck(start.LoadedChunkCount > 0 && start.LoadedNodeCount > 0,
            "Initial camera settle should stream in at least one road chunk with loaded nodes.", failures);
        AddAcceptanceCheck(!string.Equals(shifted.ActiveChunkSignature, start.ActiveChunkSignature, StringComparison.Ordinal),
            "Moving the camera target should change the loaded chunk window.", failures);
        AddAcceptanceCheck(MathF.Abs(shifted.CameraTargetCm.X - RoadChunkShiftTargetCm.X) <= 1f && MathF.Abs(shifted.CameraTargetCm.Y - RoadChunkShiftTargetCm.Y) <= 1f,
            $"Camera target should settle at the chunk showcase probe point, but landed at {FormatPoint(shifted.CameraTargetCm)}.", failures);

        string normalizedSignature = string.Join("|", new[]
        {
            "road_network_showcase_command_and_chunking",
            $"selected:{string.Join("+", selected.SelectedNames)}",
            $"controlled:{accepted.ControlledActorName}",
            $"status:{accepted.StatusLine}",
            $"blue:{MathF.Round(accepted.ControlledActorWorldCm.X):F0}->{MathF.Round(moved.ControlledActorWorldCm.X):F0}",
            $"chunks:{start.ActiveChunkSignature}->{shifted.ActiveChunkSignature}",
            $"roads:{accepted.RoadSplineCount}",
            $"cue:{(accepted.CueMarkerPresent ? 1 : 0)}"
        });

        string verdict = failures.Count == 0
            ? "Road showcase passes: selection, road command feedback, spline rendering, movement, and chunk-window migration all behaved as designed."
            : "Road showcase fails: selection, road command acceptance, movement, or chunk streaming diverged from the intended playable demo.";
        string failureSummary = failures.Count == 0 ? verdict : string.Join(Environment.NewLine, failures);

        return new RoadAcceptanceResult(
            Success: failures.Count == 0,
            Verdict: verdict,
            FailureSummary: failureSummary,
            FailedChecks: failures,
            SelectedNames: selected.SelectedNames,
            ControlledActorName: accepted.ControlledActorName,
            AcceptedStatus: accepted.StatusLine,
            StartControlledActorWorldCm: accepted.ControlledActorWorldCm,
            FinalControlledActorWorldCm: moved.ControlledActorWorldCm,
            StartChunkSignature: start.ActiveChunkSignature,
            FinalChunkSignature: shifted.ActiveChunkSignature,
            CueMarkerVisible: accepted.CueMarkerPresent,
            NormalizedSignature: normalizedSignature);
    }

    private static string BuildRoadBattleReport(
        LauncherRecordingRequest request,
        IReadOnlyList<RoadSnapshot> timeline,
        IReadOnlyList<CaptureFrame> captureFrames,
        IReadOnlyList<double> frameTimesMs,
        RoadAcceptanceResult acceptance)
    {
        double medianTickMs = Median(frameTimesMs.ToArray());
        double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
        string evidenceImages = string.Join(", ", captureFrames.Select(frame => $"`screens/{frame.FileName}`").Append("`screens/timeline.png`"));

        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: road-network-showcase-command-and-chunking");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: select a road column, right-click a fort along the road network, see immediate command feedback, and watch chunk streaming react when the camera shifts east.");
        sb.AppendLine("- Gameplay domain: real launcher bootstrap, real input mapping, real graph-only auto path service, real road spline performer, and real loaded-chunk window updates.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none");
        sb.AppendLine("- Map: `mods/showcases/road_network/RoadNetworkShowcaseMod/assets/Maps/road_network_showcase_chunked.json`");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Launch command: `{request.CommandText}`");
        sb.AppendLine($"- Selection point: `{FormatPoint(RoadSelectionWorldCm)}`");
        sb.AppendLine($"- Command target: `{FormatPoint(RoadCommandWorldCm)}`");
        sb.AppendLine($"- Chunk probe camera target: `{FormatPoint(RoadChunkShiftTargetCm)}`");
        sb.AppendLine("- Clock profile: fixed `1/60s`");
        sb.AppendLine($"- Evidence images: {evidenceImages}");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (RoadSnapshot snapshot in timeline)
        {
            sb.AppendLine($"- [T+{snapshot.Tick:000}] RoadShowcase.{snapshot.Step} -> status={snapshot.StatusLine} | selected={string.Join(", ", snapshot.SelectedNames)} | controlled={snapshot.ControlledActorName} {FormatPoint(snapshot.ControlledActorWorldCm)} | vanguard={FormatPoint(snapshot.BlueVanguardWorldCm)} | north={FormatPoint(snapshot.BlueNorthWorldCm)} | south={FormatPoint(snapshot.BlueSouthWorldCm)} | chunks={snapshot.LoadedChunkCount} | nodes={snapshot.LoadedNodeCount} | roads={snapshot.RoadSplineCount} | cue={(snapshot.CueMarkerPresent ? "On" : "Off")} | camera={FormatPoint(snapshot.CameraTargetCm)} | tick={snapshot.TickMs:F3}ms");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        foreach (string failedCheck in acceptance.FailedChecks)
        {
            sb.AppendLine($"- failed-check: {failedCheck}");
        }

        sb.AppendLine($"- reason: selected=`{string.Join(", ", acceptance.SelectedNames)}` controlled=`{acceptance.ControlledActorName}` status=`{acceptance.AcceptedStatus}` controlled actor `{FormatPoint(acceptance.StartControlledActorWorldCm)}` -> `{FormatPoint(acceptance.FinalControlledActorWorldCm)}` chunk signature `{acceptance.StartChunkSignature}` -> `{acceptance.FinalChunkSignature}` cue={(acceptance.CueMarkerVisible ? "visible" : "hidden")}.");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- screenshot captures: `{captureFrames.Count}`");
        sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
        sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
        sb.AppendLine($"- normalized signature: `{acceptance.NormalizedSignature}`");
        sb.AppendLine("- reusable wiring: `launcher.runtime.json`, `PlayerInputHandler`, `CurrentSelectionApplySystem`, `InputOrderMappingSystem`, `PathServiceRouter`, `NavQueryServiceRegistry`, `RoadSplineBuffer`, `LoadedChunksSource`");
        return sb.ToString();
    }

    private static string BuildRoadTraceJsonl(string adapterId, IReadOnlyList<RoadSnapshot> timeline)
    {
        var lines = new List<string>(timeline.Count);
        for (int index = 0; index < timeline.Count; index++)
        {
            RoadSnapshot snapshot = timeline[index];
            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"road-{adapterId}-{index + 1:000}",
                tick = snapshot.Tick,
                step = snapshot.Step,
                status_line = snapshot.StatusLine,
                selected = snapshot.SelectedNames,
                controlled_actor = snapshot.ControlledActorName,
                controlled_actor_x = Math.Round(snapshot.ControlledActorWorldCm.X, 2),
                controlled_actor_y = Math.Round(snapshot.ControlledActorWorldCm.Y, 2),
                blue_x = Math.Round(snapshot.BlueVanguardWorldCm.X, 2),
                blue_y = Math.Round(snapshot.BlueVanguardWorldCm.Y, 2),
                blue_north_x = Math.Round(snapshot.BlueNorthWorldCm.X, 2),
                blue_north_y = Math.Round(snapshot.BlueNorthWorldCm.Y, 2),
                blue_south_x = Math.Round(snapshot.BlueSouthWorldCm.X, 2),
                blue_south_y = Math.Round(snapshot.BlueSouthWorldCm.Y, 2),
                loaded_chunks = snapshot.LoadedChunkCount,
                loaded_nodes = snapshot.LoadedNodeCount,
                road_splines = snapshot.RoadSplineCount,
                cue_marker = snapshot.CueMarkerPresent,
                camera_target_x = Math.Round(snapshot.CameraTargetCm.X, 2),
                camera_target_y = Math.Round(snapshot.CameraTargetCm.Y, 2),
                tick_ms = Math.Round(snapshot.TickMs, 4),
                status = "done"
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildRoadPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Boot launcher runtime for RoadNetworkShowcaseMod] --> B[Settle tactical camera and chunk window]",
            "    B --> C[Project Blue Vanguard visual pivot and inject left-click]",
            "    C --> D{Selection contains Blue Vanguard?}",
            "    D -->|yes| E[Project Central Crossing and inject right-click]",
            "    E --> F{HUD shows an accepted route selection and cue marker is visible?}",
            "    F -->|yes| G[Advance simulation until the controlled blue column moves east]",
            "    G --> H[Apply east camera target and wait for loaded chunk signature to change]",
            "    H --> I[Write battle-report + trace + path + PNG timeline]",
            "    D -->|no| X[Fail acceptance: selection bridge diverged]",
            "    F -->|no| Y[Fail acceptance: road command still invalid or marker missing]"
        }) + Environment.NewLine;
    }

    private static string BuildRoadVisibleChecklist(IReadOnlyList<RoadSnapshot> timeline)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: road-network-showcase-command-and-chunking");
        sb.AppendLine();
        sb.AppendLine("- `000_start` should show the initial central loaded chunk window and visible road splines.");
        sb.AppendLine("- `001_selected` should highlight Blue Vanguard as selected.");
        sb.AppendLine("- `002_command_accepted` should show a cue marker at Central Crossing and a valid accepted route HUD status instead of `error 2`.");
        sb.AppendLine("- `003_column_advancing` should show the controlled blue column shifted east along the road.");
        sb.AppendLine("- `004_chunk_shifted` should show the camera moved east and a different loaded chunk window.");
        sb.AppendLine();
        foreach (RoadSnapshot snapshot in timeline)
        {
            sb.AppendLine($"- `{snapshot.Step}.png`: status=`{snapshot.StatusLine}` selected=`{string.Join(", ", snapshot.SelectedNames)}` chunks={snapshot.LoadedChunkCount} roads={snapshot.RoadSplineCount} cue={(snapshot.CueMarkerPresent ? "visible" : "hidden")}");
        }

        return sb.ToString();
    }

    private static string BuildRoadSummaryJson(LauncherRecordingRequest request, RoadAcceptanceResult acceptance)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = "road_network_showcase_command_and_chunking",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            selected = acceptance.SelectedNames,
            controlled_actor = acceptance.ControlledActorName,
            accepted_status = acceptance.AcceptedStatus,
            cue_visible = acceptance.CueMarkerVisible,
            start_chunks = acceptance.StartChunkSignature,
            final_chunks = acceptance.FinalChunkSignature,
            normalized_signature = acceptance.NormalizedSignature
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void WriteRoadSnapshotImage(RoadSnapshot snapshot, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(RoadImageWidth, RoadImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));

        using var gridPaint = new SKPaint { Color = new SKColor(34, 42, 58), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var chunkFillPaint = new SKPaint { Color = new SKColor(72, 108, 148, 38), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var chunkStrokePaint = new SKPaint { Color = new SKColor(114, 162, 214, 120), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var roadPaint = new SKPaint { Color = new SKColor(214, 168, 88), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5f, StrokeCap = SKStrokeCap.Round };
        using var roadBorderPaint = new SKPaint { Color = new SKColor(248, 222, 146), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8f, StrokeCap = SKStrokeCap.Round };
        using var bluePaint = new SKPaint { Color = new SKColor(74, 188, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var redPaint = new SKPaint { Color = new SKColor(255, 100, 96), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var neutralPaint = new SKPaint { Color = new SKColor(238, 206, 96), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var selectedPaint = new SKPaint { Color = new SKColor(255, 255, 255, 220), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
        using var cuePaint = new SKPaint { Color = new SKColor(92, 240, 154), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 22f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(190, 198, 214), IsAntialias = true, TextSize = 16f };

        DrawWorldGrid(canvas, RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, gridPaint, RoadImageWidth, RoadImageHeight);

        if (snapshot.ChunkSizeCm > 0)
        {
            foreach (long chunkKey in snapshot.ActiveChunkKeys)
            {
                (int chunkX, int chunkY) = Ludots.Core.Navigation.GraphWorld.GraphChunkKey.Unpack(chunkKey);
                float minX = chunkX * snapshot.ChunkSizeCm;
                float minY = chunkY * snapshot.ChunkSizeCm;
                float maxX = minX + snapshot.ChunkSizeCm;
                float maxY = minY + snapshot.ChunkSizeCm;
                SKPoint a = ToScreen(new Vector2(minX, maxY), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
                SKPoint b = ToScreen(new Vector2(maxX, minY), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
                SKRect rect = NormalizeRect(new SKRect(a.X, a.Y, b.X, b.Y));
                canvas.DrawRect(rect, chunkFillPaint);
                canvas.DrawRect(rect, chunkStrokePaint);
            }
        }

        foreach (RoadSplineCapture spline in snapshot.Splines)
        {
            using var pathBuilder = new SKPath();
            SKPoint p0 = ToScreen(new Vector2(spline.P0.X * 100f, spline.P0.Z * 100f), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
            SKPoint p1 = ToScreen(new Vector2(spline.P1.X * 100f, spline.P1.Z * 100f), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
            SKPoint p2 = ToScreen(new Vector2(spline.P2.X * 100f, spline.P2.Z * 100f), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
            SKPoint p3 = ToScreen(new Vector2(spline.P3.X * 100f, spline.P3.Z * 100f), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
            pathBuilder.MoveTo(p0);
            pathBuilder.CubicTo(p1, p2, p3);
            canvas.DrawPath(pathBuilder, roadBorderPaint);
            canvas.DrawPath(pathBuilder, roadPaint);
        }

        foreach ((string name, Vector2 worldCm) in snapshot.NamedEntities.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            SKPoint point = ToScreen(worldCm, RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
            bool isBlue = name.Contains("Blue", StringComparison.OrdinalIgnoreCase);
            bool isRed = name.Contains("Red", StringComparison.OrdinalIgnoreCase);
            SKPaint fill = isBlue ? bluePaint : isRed ? redPaint : neutralPaint;
            float radius = name.Contains("Capital", StringComparison.OrdinalIgnoreCase) || name.Contains("Gate", StringComparison.OrdinalIgnoreCase) || name.Contains("Pass", StringComparison.OrdinalIgnoreCase) || name.Contains("Ford", StringComparison.OrdinalIgnoreCase) || name.Contains("Watch", StringComparison.OrdinalIgnoreCase)
                ? 10f
                : 7f;
            canvas.DrawCircle(point.X, point.Y, radius, fill);
            if (snapshot.SelectedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                canvas.DrawCircle(point.X, point.Y, radius + 5f, selectedPaint);
            }

            canvas.DrawText(name, point.X + 10f, point.Y - 8f, minorTextPaint);
        }

        DrawCrosshair(canvas, ToScreen(snapshot.CameraTargetCm, RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight), 12f, chunkStrokePaint);
        DrawCrosshair(canvas, ToScreen(RoadCommandWorldCm, RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight), 14f, roadPaint);
        if (snapshot.CueMarkerPresent)
        {
            DrawCrosshair(canvas, ToScreen(snapshot.CueMarkerWorldCm, RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight), 18f, cuePaint);
        }

        canvas.DrawText($"Road Network Showcase | {snapshot.Step} | tick={snapshot.Tick}", 24, 34, labelPaint);
        canvas.DrawText($"Status={snapshot.StatusLine}", 24, 64, minorTextPaint);
        canvas.DrawText($"Selected={string.Join(", ", snapshot.SelectedNames)}", 24, 92, minorTextPaint);
        canvas.DrawText($"Controlled={snapshot.ControlledActorName} {FormatPoint(snapshot.ControlledActorWorldCm)}  Camera={FormatPoint(snapshot.CameraTargetCm)}", 24, 120, minorTextPaint);
        canvas.DrawText($"BlueVanguard={FormatPoint(snapshot.BlueVanguardWorldCm)}  North={FormatPoint(snapshot.BlueNorthWorldCm)}  South={FormatPoint(snapshot.BlueSouthWorldCm)}", 24, 148, minorTextPaint);
        canvas.DrawText($"LoadedChunks={snapshot.LoadedChunkCount}  LoadedNodes={snapshot.LoadedNodeCount}  RoadSplines={snapshot.RoadSplineCount}  Tick={snapshot.TickMs:F3}ms", 24, 176, minorTextPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static bool TryGetRoadBoard(GameEngine engine, out NodeGraphBoard? board)
    {
        board = engine.CurrentMapSession?.PrimaryBoard as NodeGraphBoard;
        return board != null;
    }

    private static bool TryFindCueMarkerAt(PrimitiveDrawBuffer? primitives, Vector2 worldCm, out Vector2 cueMarkerWorldCm)
    {
        cueMarkerWorldCm = Vector2.Zero;
        if (primitives == null)
        {
            return false;
        }

        Vector3 cueMarkerVisual = WorldUnits.WorldCmToVisualMeters(
            new WorldCmInt2((int)MathF.Round(worldCm.X), (int)MathF.Round(worldCm.Y)),
            yMeters: RoadCueMarkerHeightMeters);
        foreach (ref readonly PrimitiveDrawItem primitive in primitives.GetSpan())
        {
            if (Vector3.Distance(primitive.Position, cueMarkerVisual) <= 0.08f)
            {
                WorldCmInt2 marker = WorldUnits.VisualMetersToWorldCm(primitive.Position);
                cueMarkerWorldCm = new Vector2(marker.X, marker.Y);
                return true;
            }
        }

        foreach (ref readonly PrimitiveDrawItem primitive in primitives.GetSpan())
        {
            if (!LooksLikeCueMarker(primitive))
            {
                continue;
            }

            WorldCmInt2 marker = WorldUnits.VisualMetersToWorldCm(primitive.Position);
            cueMarkerWorldCm = new Vector2(marker.X, marker.Y);
            return true;
        }

        return false;
    }

    private static bool IsAcceptedRoadStatus(string statusLine)
    {
        if (string.IsNullOrWhiteSpace(statusLine))
        {
            return false;
        }

        if (statusLine.Contains("Road route accepted:", StringComparison.Ordinal))
        {
            return true;
        }

        return statusLine.Contains(" selected ", StringComparison.Ordinal) &&
               statusLine.Contains("sampled point(s)", StringComparison.Ordinal);
    }

    private static bool LooksLikeCueMarker(in PrimitiveDrawItem primitive)
    {
        if (MathF.Abs(primitive.Position.Y - RoadCueMarkerHeightMeters) > 0.12f)
        {
            return false;
        }

        Vector4 accepted = new(0.28f, 0.94f, 0.60f, 1f);
        Vector4 rejected = new(1.0f, 0.52f, 0.18f, 1f);
        return Vector4.DistanceSquared(primitive.Color, accepted) <= 0.08f ||
               Vector4.DistanceSquared(primitive.Color, rejected) <= 0.08f;
    }

    private static void AssertRoadOverlay(ScreenOverlayBuffer? overlay)
    {
        string dump = string.Join(" || ", ExtractOverlayText(overlay));
        if (!dump.Contains("Road Network Showcase", StringComparison.Ordinal) ||
            !dump.Contains("Loaded chunks", StringComparison.Ordinal) ||
            !dump.Contains("Right-click near a road or fort", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Road showcase overlay is missing expected guidance text. Dump: {dump}");
        }
    }

    private static void ClickPrimaryUntilSelected(RecordingRuntime runtime, string targetName, List<double> frameTimesMs)
    {
        if (SampleRoadSnapshot(runtime, "probe_select", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, null)
            .SelectedNames.Contains(targetName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Vector2 baseScreen = runtime.TryProjectNamedEntity(targetName, out Vector2 projected)
            ? projected
            : runtime.ProjectWorldCm(RoadSelectionWorldCm, yMeters: 0.58f);

        foreach (Vector2 offset in RoadSelectionPickOffsetsPx)
        {
            ClickPrimary(runtime, baseScreen + offset, frameTimesMs);
            Tick(runtime, 2, frameTimesMs);
            if (SampleRoadSnapshot(runtime, "probe_select", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, null)
                .SelectedNames.Contains(targetName, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }
        }
    }

    private static string ResolveControlledActorName(GameEngine engine, IReadOnlyDictionary<string, Vector2> namedEntities)
    {
        if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
            localObj is Entity local &&
            engine.World.IsAlive(local) &&
            engine.World.Has<Name>(local))
        {
            string value = engine.World.Get<Name>(local).Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        foreach (string blueColumnName in RoadBlueColumnNames)
        {
            if (namedEntities.ContainsKey(blueColumnName))
            {
                return blueColumnName;
            }
        }

        return string.Empty;
    }

    private static Vector2 ResolveNamedPosition(IReadOnlyDictionary<string, Vector2> namedEntities, string name)
    {
        return namedEntities.TryGetValue(name, out Vector2 worldCm) ? worldCm : Vector2.Zero;
    }

    private static SKRect NormalizeRect(SKRect rect)
    {
        float left = Math.Min(rect.Left, rect.Right);
        float right = Math.Max(rect.Left, rect.Right);
        float top = Math.Min(rect.Top, rect.Bottom);
        float bottom = Math.Max(rect.Top, rect.Bottom);
        return new SKRect(left, top, right, bottom);
    }

    private static LauncherRecordingResult RecordChunkStreamingShowcase(LauncherRecordingRequest request)
    {
        string screensDir = Path.Combine(request.OutputDirectory, "screens");
        Directory.CreateDirectory(screensDir);

        var timeline = new List<ChunkSnapshot>();
        var captureFrames = new List<CaptureFrame>();
        var frameTimesMs = new List<double>();

        using var runtime = CreateRuntime(request.Plan, request.BootstrapPath);
        if (!string.Equals(runtime.Config.StartupMapId, "chunk_streaming_showcase", StringComparison.OrdinalIgnoreCase))
        {
            runtime.Engine.LoadMap("chunk_streaming_showcase");
        }

        Tick(runtime, 10, frameTimesMs);
        AssertChunkOverlay(runtime.Engine.GetService(CoreServiceKeys.ScreenOverlayBuffer));
        CaptureChunkSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "000_overview");

        ApplyCameraTarget(runtime, ChunkEastGateTargetCm, frameTimesMs, settleTicks: 6);
        AdvanceUntilChunkSignature(runtime, frameTimesMs, timeline[0].ActiveChunkSignature, shouldMatch: false, maxFrames: 48);
        CaptureChunkSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "001_east_gate");

        ApplyCameraTarget(runtime, ChunkRedCapitalTargetCm, frameTimesMs, settleTicks: 6);
        AdvanceUntilChunkSignature(runtime, frameTimesMs, timeline[1].ActiveChunkSignature, shouldMatch: false, maxFrames: 48);
        CaptureChunkSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "002_red_capital");

        ApplyCameraTarget(runtime, Vector2.Zero, frameTimesMs, settleTicks: 6);
        AdvanceUntilChunkSignature(runtime, frameTimesMs, timeline[0].ActiveChunkSignature, shouldMatch: true, maxFrames: 48);
        CaptureChunkSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "003_reset_center");

        WriteTimelineSheet("Chunk streaming showcase timeline", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        ChunkAcceptanceResult acceptance = EvaluateChunkAcceptance(timeline);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildChunkBattleReport(request, timeline, frameTimesMs, acceptance));
        File.WriteAllText(tracePath, BuildChunkTraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildChunkPathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildChunkVisibleChecklist(timeline));
        File.WriteAllText(summaryPath, BuildChunkSummaryJson(request, acceptance));

        if (!acceptance.Success)
        {
            throw new InvalidOperationException(acceptance.FailureSummary);
        }

        return new LauncherRecordingResult(
            request.OutputDirectory,
            battleReportPath,
            tracePath,
            pathPath,
            summaryPath,
            visibleChecklistPath,
            captureFrames.Select(frame => Path.Combine(screensDir, frame.FileName)).Append(Path.Combine(screensDir, "timeline.png")).ToList(),
            acceptance.NormalizedSignature);
    }

    private static void CaptureChunkSnapshot(
        RecordingRuntime runtime,
        string screensDir,
        IReadOnlyList<double> frameTimesMs,
        List<ChunkSnapshot> timeline,
        List<CaptureFrame> captureFrames,
        string step)
    {
        ChunkSnapshot snapshot = SampleChunkSnapshot(runtime, step, frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d);
        timeline.Add(snapshot);
        string fileName = $"{step}.png";
        WriteChunkSnapshotImage(snapshot, Path.Combine(screensDir, fileName));
        captureFrames.Add(new CaptureFrame(snapshot.Tick, step, fileName, snapshot.LoadedChunkCount, snapshot.RoadSplineCount, 0f, 0f));
    }

    private static ChunkSnapshot SampleChunkSnapshot(RecordingRuntime runtime, string step, double tickMs)
    {
        int loadedChunkCount = 0;
        int loadedNodeCount = 0;
        int chunkSizeCm = 0;
        var activeChunkKeys = Array.Empty<long>();
        if (TryGetRoadBoard(runtime.Engine, out NodeGraphBoard? board))
        {
            loadedChunkCount = board.LoadedChunksSource.ActiveChunkKeys.Count;
            loadedNodeCount = board.GraphRuntime.CurrentGraph.NodeCount;
            chunkSizeCm = board.LoadedChunksSource.ChunkSizeCm;
            activeChunkKeys = board.LoadedChunksSource.ActiveChunkKeys.OrderBy(key => key).ToArray();
        }

        RoadSplineBuffer? roads = runtime.Engine.GetService(CoreServiceKeys.RoadSplineBuffer);
        var splines = new List<RoadSplineCapture>(roads?.Count ?? 0);
        if (roads != null)
        {
            for (int index = 0; index < roads.Count; index++)
            {
                splines.Add(new RoadSplineCapture(
                    roads.StableIds[index],
                    new Vector3(roads.P0X[index], roads.P0Y[index], roads.P0Z[index]),
                    new Vector3(roads.P1X[index], roads.P1Y[index], roads.P1Z[index]),
                    new Vector3(roads.P2X[index], roads.P2Y[index], roads.P2Z[index]),
                    new Vector3(roads.P3X[index], roads.P3Y[index], roads.P3Z[index]),
                    roads.Width[index]));
            }
        }

        var overlayLines = ExtractOverlayText(runtime.Engine.GetService(CoreServiceKeys.ScreenOverlayBuffer));
        string statusLine = overlayLines.Count > 1 ? overlayLines[1] : string.Empty;

        return new ChunkSnapshot(
            Tick: runtime.Engine.GameSession.CurrentTick,
            Step: step,
            TickMs: tickMs,
            ActiveMapId: runtime.Engine.CurrentMapSession?.MapId.ToString() ?? runtime.Config.StartupMapId,
            CameraTargetCm: runtime.Engine.GameSession.Camera.State.TargetCm,
            LoadedChunkCount: loadedChunkCount,
            LoadedNodeCount: loadedNodeCount,
            ChunkSizeCm: chunkSizeCm,
            ActiveChunkKeys: activeChunkKeys,
            ActiveChunkSignature: string.Join(",", activeChunkKeys),
            RoadSplineCount: roads?.Count ?? 0,
            StatusLine: statusLine,
            OverlayLines: overlayLines,
            Splines: splines);
    }

    private static void AdvanceUntilChunkSignature(RecordingRuntime runtime, List<double> frameTimesMs, string targetChunkSignature, bool shouldMatch, int maxFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            Tick(runtime, 1, frameTimesMs);
            ChunkSnapshot snapshot = SampleChunkSnapshot(runtime, "probe_chunk_window", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d);
            bool matches = string.Equals(snapshot.ActiveChunkSignature, targetChunkSignature, StringComparison.Ordinal);
            if (matches == shouldMatch)
            {
                return;
            }
        }
    }

    private static ChunkAcceptanceResult EvaluateChunkAcceptance(IReadOnlyList<ChunkSnapshot> timeline)
    {
        ChunkSnapshot start = timeline[0];
        ChunkSnapshot eastGate = timeline[1];
        ChunkSnapshot redCapital = timeline[2];
        ChunkSnapshot reset = timeline[3];

        var failures = new List<string>();
        AddAcceptanceCheck(start.LoadedChunkCount > 0 && start.LoadedNodeCount > 0,
            "Chunk showcase should start with a populated central window.", failures);
        AddAcceptanceCheck(start.RoadSplineCount > 0,
            "Chunk showcase should render at least one road spline batch in the initial window.", failures);
        AddAcceptanceCheck(!string.Equals(eastGate.ActiveChunkSignature, start.ActiveChunkSignature, StringComparison.Ordinal),
            "Focusing East Gate should shift the loaded chunk signature away from the center window.", failures);
        AddAcceptanceCheck(!string.Equals(redCapital.ActiveChunkSignature, eastGate.ActiveChunkSignature, StringComparison.Ordinal),
            "Focusing Red Capital should continue shifting the chunk signature farther east.", failures);
        AddAcceptanceCheck(string.Equals(reset.ActiveChunkSignature, start.ActiveChunkSignature, StringComparison.Ordinal),
            "Resetting the camera should restore the original central chunk signature.", failures);
        AddAcceptanceCheck(redCapital.CameraTargetCm.X >= 16000f,
            $"Chunk showcase camera should enter the far-east chunk window, but landed at {FormatPoint(redCapital.CameraTargetCm)}.", failures);

        string normalizedSignature = string.Join("|", new[]
        {
            "chunk_streaming_showcase_camera_windows",
            $"start:{start.ActiveChunkSignature}",
            $"east:{eastGate.ActiveChunkSignature}",
            $"red:{redCapital.ActiveChunkSignature}",
            $"reset:{reset.ActiveChunkSignature}",
            $"splines:{start.RoadSplineCount}->{redCapital.RoadSplineCount}"
        });

        bool success = failures.Count == 0;
        string failureSummary = success
            ? string.Empty
            : $"Chunk streaming showcase acceptance failed:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", failures)}";

        return new ChunkAcceptanceResult(
            success,
            success ? "Chunk window shifts and resets were visible across authored camera jumps." : "Chunk window evidence is incomplete.",
            failureSummary,
            failures,
            start.ActiveChunkSignature,
            redCapital.ActiveChunkSignature,
            reset.ActiveChunkSignature,
            normalizedSignature);
    }

    private static string BuildChunkBattleReport(
        LauncherRecordingRequest request,
        IReadOnlyList<ChunkSnapshot> timeline,
        IReadOnlyList<double> frameTimesMs,
        ChunkAcceptanceResult acceptance)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: chunk_streaming_showcase_camera_windows");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Validate a standalone chunk showcase mod that demonstrates camera-driven chunk window streaming and road spline batch loading.");
        sb.AppendLine("- Acceptance focus: center, East Gate, and Red Capital jumps should expose different loaded chunk signatures, then reset back to the original window.");
        sb.AppendLine();
        sb.AppendLine("## Runtime");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Root mods: `{string.Join(", ", request.Plan.RootModIds)}`");
        sb.AppendLine("- Map: `mods/showcases/chunk_streaming/ChunkStreamingShowcaseMod/assets/Maps/chunk_streaming_showcase.json`");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (ChunkSnapshot snapshot in timeline)
        {
            sb.AppendLine($"- [T+{snapshot.Tick:000}] ChunkShowcase.{snapshot.Step} -> status={snapshot.StatusLine} | camera={FormatPoint(snapshot.CameraTargetCm)} | chunks={snapshot.LoadedChunkCount} | nodes={snapshot.LoadedNodeCount} | roads={snapshot.RoadSplineCount} | tick={snapshot.TickMs:F3}ms");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        if (!acceptance.Success)
        {
            sb.AppendLine("- failures:");
            foreach (string failure in acceptance.FailedChecks)
            {
                sb.AppendLine($"  - {failure}");
            }
        }

        if (frameTimesMs.Count > 0)
        {
            sb.AppendLine($"- median tick ms: {Median(frameTimesMs.ToArray()):F3}");
        }

        return sb.ToString();
    }

    private static string BuildChunkTraceJsonl(string adapterId, IReadOnlyList<ChunkSnapshot> timeline)
    {
        var lines = new List<string>(timeline.Count);
        foreach (ChunkSnapshot snapshot in timeline)
        {
            lines.Add(JsonSerializer.Serialize(new
            {
                adapter = adapterId,
                step = snapshot.Step,
                tick = snapshot.Tick,
                map = snapshot.ActiveMapId,
                camera = new { x = Math.Round(snapshot.CameraTargetCm.X, 2), y = Math.Round(snapshot.CameraTargetCm.Y, 2) },
                chunk_count = snapshot.LoadedChunkCount,
                node_count = snapshot.LoadedNodeCount,
                road_spline_count = snapshot.RoadSplineCount,
                chunk_signature = snapshot.ActiveChunkSignature,
                status = snapshot.StatusLine
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildChunkPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Boot launcher runtime for ChunkStreamingShowcaseMod] --> B[Settle tactical camera on center window]",
            "    B --> C[Capture initial chunk tiles and road splines]",
            "    C --> D[Jump camera to East Gate]",
            "    D --> E[Observe different loaded chunk signature]",
            "    E --> F[Jump camera to Red Capital]",
            "    F --> G[Observe farther-east chunk signature and spline subset]",
            "    G --> H[Reset camera to center and restore original chunk window]"
        }) + Environment.NewLine;
    }

    private static string BuildChunkVisibleChecklist(IReadOnlyList<ChunkSnapshot> timeline)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: chunk-streaming-showcase-camera-windows");
        sb.AppendLine();
        sb.AppendLine("- `000_overview` should show the central chunk window with the main trunk road and the chunk showcase UI panel.");
        sb.AppendLine("- `001_east_gate` should show the camera and loaded chunk window shifted east.");
        sb.AppendLine("- `002_red_capital` should show the far-east window around Red Capital.");
        sb.AppendLine("- `003_reset_center` should return to the original center signature.");
        sb.AppendLine();
        foreach (ChunkSnapshot snapshot in timeline)
        {
            sb.AppendLine($"- `{snapshot.Step}.png`: status=`{snapshot.StatusLine}` chunks={snapshot.LoadedChunkCount} nodes={snapshot.LoadedNodeCount} roads={snapshot.RoadSplineCount} camera=`{FormatPoint(snapshot.CameraTargetCm)}`");
        }

        return sb.ToString();
    }

    private static string BuildChunkSummaryJson(LauncherRecordingRequest request, ChunkAcceptanceResult acceptance)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = "chunk_streaming_showcase_camera_windows",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            start_chunks = acceptance.StartChunkSignature,
            far_chunks = acceptance.FarChunkSignature,
            reset_chunks = acceptance.ResetChunkSignature,
            normalized_signature = acceptance.NormalizedSignature
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void WriteChunkSnapshotImage(ChunkSnapshot snapshot, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(RoadImageWidth, RoadImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));

        using var gridPaint = new SKPaint { Color = new SKColor(34, 42, 58), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var chunkFillPaint = new SKPaint { Color = new SKColor(72, 108, 148, 38), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var chunkStrokePaint = new SKPaint { Color = new SKColor(114, 162, 214, 120), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var roadPaint = new SKPaint { Color = new SKColor(214, 168, 88), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5f, StrokeCap = SKStrokeCap.Round };
        using var roadBorderPaint = new SKPaint { Color = new SKColor(248, 222, 146), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8f, StrokeCap = SKStrokeCap.Round };
        using var westPaint = new SKPaint { Color = new SKColor(74, 188, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var centerPaint = new SKPaint { Color = new SKColor(238, 206, 96), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var eastPaint = new SKPaint { Color = new SKColor(255, 100, 96), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var redCapitalPaint = new SKPaint { Color = new SKColor(255, 122, 114), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 22f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(190, 198, 214), IsAntialias = true, TextSize = 16f };

        DrawWorldGrid(canvas, RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, gridPaint, RoadImageWidth, RoadImageHeight);

        if (snapshot.ChunkSizeCm > 0)
        {
            foreach (long chunkKey in snapshot.ActiveChunkKeys)
            {
                (int chunkX, int chunkY) = Ludots.Core.Navigation.GraphWorld.GraphChunkKey.Unpack(chunkKey);
                float minX = chunkX * snapshot.ChunkSizeCm;
                float minY = chunkY * snapshot.ChunkSizeCm;
                float maxX = minX + snapshot.ChunkSizeCm;
                float maxY = minY + snapshot.ChunkSizeCm;
                SKPoint a = ToScreen(new Vector2(minX, maxY), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
                SKPoint b = ToScreen(new Vector2(maxX, minY), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
                SKRect rect = NormalizeRect(new SKRect(a.X, a.Y, b.X, b.Y));
                canvas.DrawRect(rect, chunkFillPaint);
                canvas.DrawRect(rect, chunkStrokePaint);
            }
        }

        foreach (RoadSplineCapture spline in snapshot.Splines)
        {
            using var pathBuilder = new SKPath();
            SKPoint p0 = ToScreen(new Vector2(spline.P0.X * 100f, spline.P0.Z * 100f), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
            SKPoint p1 = ToScreen(new Vector2(spline.P1.X * 100f, spline.P1.Z * 100f), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
            SKPoint p2 = ToScreen(new Vector2(spline.P2.X * 100f, spline.P2.Z * 100f), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
            SKPoint p3 = ToScreen(new Vector2(spline.P3.X * 100f, spline.P3.Z * 100f), RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
            pathBuilder.MoveTo(p0);
            pathBuilder.CubicTo(p1, p2, p3);
            canvas.DrawPath(pathBuilder, roadBorderPaint);
            canvas.DrawPath(pathBuilder, roadPaint);
        }

        DrawNamedLandmark(canvas, "West Gate", new Vector2(-9000f, 0f), westPaint, minorTextPaint);
        DrawNamedLandmark(canvas, "Central Crossing", Vector2.Zero, centerPaint, minorTextPaint);
        DrawNamedLandmark(canvas, "East Gate", ChunkEastGateTargetCm, eastPaint, minorTextPaint);
        DrawNamedLandmark(canvas, "Red Capital", ChunkRedCapitalTargetCm, redCapitalPaint, minorTextPaint);
        DrawCrosshair(canvas, ToScreen(snapshot.CameraTargetCm, RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight), 14f, chunkStrokePaint);

        canvas.DrawText($"Chunk Streaming Showcase | {snapshot.Step} | tick={snapshot.Tick}", 24, 34, labelPaint);
        canvas.DrawText($"Status={snapshot.StatusLine}", 24, 64, minorTextPaint);
        canvas.DrawText($"Camera={FormatPoint(snapshot.CameraTargetCm)}", 24, 92, minorTextPaint);
        canvas.DrawText($"LoadedChunks={snapshot.LoadedChunkCount}  LoadedNodes={snapshot.LoadedNodeCount}  RoadSplines={snapshot.RoadSplineCount}  Tick={snapshot.TickMs:F3}ms", 24, 120, minorTextPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void DrawNamedLandmark(SKCanvas canvas, string name, Vector2 worldCm, SKPaint fillPaint, SKPaint textPaint)
    {
        SKPoint point = ToScreen(worldCm, RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight);
        canvas.DrawCircle(point.X, point.Y, 9f, fillPaint);
        canvas.DrawText(name, point.X + 10f, point.Y - 8f, textPaint);
    }

    private static void AssertChunkOverlay(ScreenOverlayBuffer? overlay)
    {
        string dump = string.Join(" || ", ExtractOverlayText(overlay));
        if (!dump.Contains("Chunk Streaming Showcase", StringComparison.Ordinal) ||
            !dump.Contains("chunk window", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Chunk showcase overlay is missing expected guidance text. Dump: {dump}");
        }
    }

    private static LauncherRecordingResult RecordMassNavigationLargeWorld(LauncherRecordingRequest request)
    {
        string screensDir = Path.Combine(request.OutputDirectory, "screens");
        Directory.CreateDirectory(screensDir);

        var timeline = new List<MassNavigationSnapshot>();
        var captureFrames = new List<CaptureFrame>();
        var frameTimesMs = new List<double>();

        using var runtime = CreateRuntime(request.Plan, request.BootstrapPath);
        if (!string.Equals(runtime.Config.StartupMapId, MassNavigationIds.MapId, StringComparison.OrdinalIgnoreCase))
        {
            runtime.Engine.LoadMap(MassNavigationIds.MapId);
        }

        PresentationTimingDiagnostics timings = runtime.Engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
            ?? throw new InvalidOperationException("MassNavigation UAT requires PresentationTimingDiagnostics.");
        timings.SystemBreakdownEnabled = true;

        Tick(runtime, MassNavigationInitialSettleTicks, frameTimesMs);
        MassNavigationSimulationRuntime simulation = runtime.Engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new InvalidOperationException("MassNavigation UAT requires MassNavigationSimulationRuntime.");
        WaitForMassNavigationScenario(runtime, simulation, frameTimesMs, maxTicks: 240);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, 0, "000_boot", captureImage: true);

        Entity[] selected = SelectFirstMassNavigationControllables(runtime.Engine, simulation, MassNavigationSelectionSampleCount);
        Tick(runtime, 3, frameTimesMs);
        Vector2 commandTarget = new(
            simulation.FlowWorkAreaCenterXCm + (simulation.SolverWindowWidthCm * 0.34f),
            simulation.FlowWorkAreaCenterYCm + (simulation.SolverWindowHeightCm * 0.18f));
        SubmitMassNavigationMoveOrder(runtime.Engine, simulation, selected, commandTarget);
        Tick(runtime, MassNavigationCommandSettleTicks, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, MassNavigationCommandSettleTicks, "001_selection_order", captureImage: true);

        SubmitMassNavigationMoveOrder(runtime.Engine, simulation, selected, commandTarget, consumeOrderId: true);
        Tick(runtime, 6, frameTimesMs);
        SubmitMassNavigationMoveOrder(runtime.Engine, simulation, selected, commandTarget + new Vector2(360f, -220f), consumeOrderId: true);
        Tick(runtime, 6, frameTimesMs);
        int reuseProbeTick = MassNavigationCommandSettleTicks + 12;
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, reuseProbeTick, "001b_order_reuse_probe", captureImage: false);

        Entity[] fullSelection = SelectFirstMassNavigationControllables(runtime.Engine, simulation, simulation.AgentState.ControllableCount);
        Tick(runtime, 3, frameTimesMs);
        Vector2 fullCommandTarget = commandTarget + new Vector2(420f, -260f);
        SubmitMassNavigationMoveOrder(runtime.Engine, simulation, fullSelection, fullCommandTarget);
        Tick(runtime, MassNavigationFullCommandSettleTicks, frameTimesMs);
        CaptureMassNavigationSnapshot(
            runtime,
            simulation,
            screensDir,
            frameTimesMs,
            timeline,
            captureFrames,
            reuseProbeTick + MassNavigationFullCommandSettleTicks,
            "007_10k_commanded_flow_probe",
            captureImage: true);

        int fullCommandProbeTick = reuseProbeTick + MassNavigationFullCommandSettleTicks;
        Vector2 originalCameraTarget = runtime.Engine.GameSession.Camera.State.TargetCm;
        MassNavigationHotZoneConfig remoteZone = ResolveRemoteHotZone(simulation);
        MinimapRuntime minimap = runtime.Engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException("MassNavigation UAT requires core MinimapRuntime.");
        minimap.JumpCameraTo(runtime.Engine, new Vector2(remoteZone.CenterXCm, remoteZone.CenterYCm));
        Tick(runtime, MassNavigationRemoteSettleTicks, frameTimesMs);
        int remoteTick = fullCommandProbeTick + MassNavigationRemoteSettleTicks;
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, remoteTick, "002_remote_minimap_jump", captureImage: true);

        minimap.JumpCameraTo(runtime.Engine, originalCameraTarget);
        Tick(runtime, MassNavigationReturnSettleTicks, frameTimesMs);
        int returnTick = remoteTick + MassNavigationReturnSettleTicks;
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "003_return_original_area", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.WorldHpa, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "004_bake_hpa_overlay", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.StrategySwitch, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "005_path_strategy_inspector", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.OrderReuse, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006_order_reuse_target_allocation", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.VisualHeightmapBake, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006a_runtime_u1_visual_heightmap_bake", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.LogicHeightmapBake, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006b_runtime_u2_logic_heightmap_bake", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.LayerAreaEditor, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006c_runtime_u3_layer_area_editor", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.PathOnly, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006d_runtime_u4_path_only", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.WorldHpa, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006e_runtime_u5_world_hpa", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.StrategySwitch, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006f_runtime_u6_strategy_switch", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.OrderReuse, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006g_runtime_u7_order_reuse", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.TargetAllocation, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006h_runtime_u8_target_allocation", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.LayerCosts, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006i_runtime_u9_layer_costs", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.WaypointAuthoring, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006j_runtime_u10_waypoint_authoring", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.LargeWorldStreaming, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006k_runtime_u11_large_world", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.TenKFlow, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006l_runtime_u12_10k_flow", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.StaticObstacleWorld, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006m_runtime_u13_static_obstacles", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.PerformanceDebug, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006n_runtime_u14_fps_scope", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.DebugVisualBudget, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006o_runtime_u15_debug_budget", captureImage: true);
        DriveMassNavigationGuideStep(runtime, simulation, MassNavigationShowcaseStepId.BakeToolQuery, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, returnTick, "006p_runtime_u16_bake_tool", captureImage: true);

        MassNavigationRaylibFrameBenchmark raylibBenchmark = RunMassNavigationRaylibFrameBenchmark(timeline[^1], screensDir);
        FrameTimingStats frameStats = BuildFrameTimingStats(frameTimesMs);
        MassNavigationManualUatSignoff manualUat = LoadMassNavigationManualUatSignoff(request.OutputDirectory);
        MassNavigationAcceptanceResult acceptance = EvaluateMassNavigationAcceptance(timeline, simulation, frameStats, raylibBenchmark, manualUat);
        string gateMatrixPath = Path.Combine(screensDir, "008_acceptance_gate_matrix.png");
        WriteMassNavigationGateMatrixImage(acceptance, timeline, frameStats, raylibBenchmark, gateMatrixPath);
        captureFrames.Add(new CaptureFrame(returnTick, "008_acceptance_gate_matrix", "008_acceptance_gate_matrix.png", timeline[^1].AgentCount, timeline[^1].ActiveGroups, timeline[^1].MinimapVisibleMarkerCount, timeline[^1].FrameMs));
        captureFrames.Add(new CaptureFrame(returnTick, "009_raylib_frame_benchmark", "009_raylib_frame_benchmark.png", timeline[^1].AgentCount, timeline[^1].ActiveGroups, timeline[^1].MinimapVisibleMarkerCount, (float)raylibBenchmark.DebugOn.P95Ms));
        WriteMassNavigationAcceptanceKeyframes(timeline, screensDir, captureFrames, returnTick, raylibBenchmark);
        WriteTimelineSheet("MassNavigation performer + minimap large-world UAT", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildMassNavigationBattleReport(request, timeline, captureFrames, frameTimesMs, raylibBenchmark, acceptance));
        File.WriteAllText(tracePath, BuildMassNavigationTraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildMassNavigationPathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildMassNavigationVisibleChecklist(captureFrames));
        File.WriteAllText(summaryPath, BuildMassNavigationSummaryJson(request, acceptance, timeline, frameStats, raylibBenchmark));

        if (!acceptance.Success)
        {
            throw new InvalidOperationException(acceptance.FailureSummary);
        }

        return new LauncherRecordingResult(
            request.OutputDirectory,
            battleReportPath,
            tracePath,
            pathPath,
            summaryPath,
            visibleChecklistPath,
            captureFrames.Select(frame => Path.Combine(screensDir, frame.FileName)).Append(Path.Combine(screensDir, "timeline.png")).ToList(),
            acceptance.NormalizedSignature);
    }

    private static void WaitForMassNavigationScenario(
        RecordingRuntime runtime,
        MassNavigationSimulationRuntime simulation,
        List<double> frameTimesMs,
        int maxTicks)
    {
        int expectedAgents = checked(simulation.AgentsPerTeam * simulation.TeamCount);
        int expectedBlockers = simulation.MassFlow.ObstacleCount;
        int expectedMarkers = simulation.HotZones.Length;
        for (int i = 0; i < maxTicks; i++)
        {
            if (simulation.AgentState.TotalAgents == expectedAgents &&
                simulation.AgentState.BlockerCount == expectedBlockers &&
                simulation.AgentState.WorldMarkerCount == expectedMarkers)
            {
                return;
            }

            Tick(runtime, 1, frameTimesMs);
        }

        throw new InvalidOperationException(
            $"MassNavigation scenario did not finish binding spawn receipts: agents={simulation.AgentState.TotalAgents}/{expectedAgents}, blockers={simulation.AgentState.BlockerCount}/{expectedBlockers}, markers={simulation.AgentState.WorldMarkerCount}/{expectedMarkers}.");
    }

    private static void DriveMassNavigationGuideStep(
        RecordingRuntime runtime,
        MassNavigationSimulationRuntime simulation,
        MassNavigationShowcaseStepId stepId,
        List<double> frameTimesMs)
    {
        MassNavigationShowcaseGuideRuntime guide = runtime.Engine.GetService(MassNavigationKeys.ShowcaseGuideRuntime)
            ?? throw new InvalidOperationException("MassNavigation UAT requires MassNavigationShowcaseGuideRuntime.");
        guide.SetStep(stepId);
        switch (stepId)
        {
            case MassNavigationShowcaseStepId.VisualHeightmapBake:
            case MassNavigationShowcaseStepId.LogicHeightmapBake:
            case MassNavigationShowcaseStepId.WorldHpa:
            case MassNavigationShowcaseStepId.LargeWorldStreaming:
                ApplyMassNavigationGuideCamera(runtime, Vector2.Zero, 58_000f, strategicMinimap: true);
                break;
            case MassNavigationShowcaseStepId.NavMeshBake:
                ApplyMassNavigationGuideCamera(runtime, ResolveMassNavigationNavMeshSampleCenter(simulation, guide), 18_000f, strategicMinimap: false);
                break;
            case MassNavigationShowcaseStepId.LayerAreaEditor:
            case MassNavigationShowcaseStepId.BakeToolQuery:
            case MassNavigationShowcaseStepId.LayerCosts:
                ApplyMassNavigationGuideCamera(runtime, ResolveMassNavigationNavMeshSampleCenter(simulation, guide), 28_000f, strategicMinimap: false);
                break;
            case MassNavigationShowcaseStepId.TargetAllocation:
            case MassNavigationShowcaseStepId.TenKFlow:
                ApplyMassNavigationGuideCamera(runtime, ResolveMassNavigationDefaultGoal(simulation), 22_000f, strategicMinimap: false);
                break;
            case MassNavigationShowcaseStepId.StaticObstacleWorld:
            case MassNavigationShowcaseStepId.PerformanceDebug:
            case MassNavigationShowcaseStepId.DebugVisualBudget:
                ApplyMassNavigationGuideCamera(runtime, new Vector2(simulation.SolverWindowCenterXCm, simulation.SolverWindowCenterYCm), 36_000f, strategicMinimap: false);
                break;
            default:
                ApplyMassNavigationGuideCamera(runtime, ResolveMassNavigationPathMidpoint(simulation), ResolveMassNavigationPathCameraDistanceCm(simulation), strategicMinimap: false);
                break;
        }

        Tick(runtime, 2, frameTimesMs);
    }

    private static void ApplyMassNavigationGuideCamera(RecordingRuntime runtime, Vector2 targetCm, float distanceCm, bool strategicMinimap)
    {
        runtime.Engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            TargetCm = targetCm,
            DistanceCm = distanceCm
        });

        if (runtime.Engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime minimap)
        {
            if (strategicMinimap)
            {
                minimap.UseRtsFullMapPreset();
            }
            else
            {
                minimap.UseFollowCameraPreset(halfExtentCm: MathF.Max(7_000f, distanceCm * 0.8f), rotateWithCamera: false);
            }
        }
    }

    private static Vector2 ResolveMassNavigationNavMeshSampleCenter(MassNavigationSimulationRuntime simulation, MassNavigationShowcaseGuideRuntime guide)
    {
        MassNavigationNavMeshGuideSample sample = guide.NavMeshSample;
        MassNavigationBakeDataDiagnostics? bake = simulation.BakeDataDiagnostics;
        if (!sample.Available || bake == null)
        {
            return new Vector2(simulation.SolverWindowCenterXCm, simulation.SolverWindowCenterYCm);
        }

        return new Vector2(
            bake.WorldMinXCm + (sample.ChunkX * bake.MacroChunkSizeXCm) + (bake.MacroChunkSizeXCm * 0.5f),
            bake.WorldMinYCm + (sample.ChunkY * bake.MacroChunkSizeYCm) + (bake.MacroChunkSizeYCm * 0.5f));
    }

    private static Vector2 ResolveMassNavigationPathMidpoint(MassNavigationSimulationRuntime simulation)
    {
        ReadOnlySpan<MassNavigationPathPointSample> points = simulation.AcceptanceDiagnostics.PathOnlyPathPoints;
        if (points.Length > 0)
        {
            MassNavigationPathPointSample sample = points[points.Length / 2];
            return new Vector2(sample.Xcm, sample.Ycm);
        }

        MassNavigationPathOnlyQueryDiagnostics query = simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (query.StartWorldCm != Vector2.Zero && query.GoalWorldCm != Vector2.Zero)
        {
            return (query.StartWorldCm + query.GoalWorldCm) * 0.5f;
        }

        return new Vector2(simulation.SolverWindowCenterXCm, simulation.SolverWindowCenterYCm);
    }

    private static float ResolveMassNavigationPathCameraDistanceCm(MassNavigationSimulationRuntime simulation)
    {
        MassNavigationPathOnlyQueryDiagnostics query = simulation.AcceptanceDiagnostics.PathOnlyQuery;
        if (query.StartWorldCm == Vector2.Zero || query.GoalWorldCm == Vector2.Zero)
        {
            return 18_000f;
        }

        float span = Vector2.Distance(query.StartWorldCm, query.GoalWorldCm);
        return Math.Clamp(span * 0.85f, 18_000f, 60_000f);
    }

    private static Vector2 ResolveMassNavigationDefaultGoal(MassNavigationSimulationRuntime simulation)
    {
        MassNavigationPathOnlyQueryDiagnostics query = simulation.AcceptanceDiagnostics.PathOnlyQuery;
        return query.GoalWorldCm != Vector2.Zero
            ? query.GoalWorldCm
            : new Vector2(simulation.SolverWindowCenterXCm + 3_200f, simulation.SolverWindowCenterYCm + 2_400f);
    }

    private static Entity[] SelectFirstMassNavigationControllables(GameEngine engine, MassNavigationSimulationRuntime simulation, int requestedCount)
    {
        SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new InvalidOperationException("MassNavigation UAT requires SelectionRuntime.");

        Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (owner == Entity.Null || !engine.World.IsAlive(owner))
        {
            throw new InvalidOperationException("MassNavigation UAT requires a live LocalPlayerEntity.");
        }

        int count = Math.Min(requestedCount, simulation.AgentState.ControllableAgents.Count);
        if (count <= 0)
        {
            throw new InvalidOperationException("MassNavigation UAT found no controllable MassNavigation agents.");
        }

        var selected = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            selected[i] = simulation.AgentState.ControllableAgents[i];
        }

        if (!selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, selected))
        {
            throw new InvalidOperationException("MassNavigation UAT failed to write SelectionRuntime LivePrimary selection.");
        }

        return selected;
    }

    private static void SubmitMassNavigationMoveOrder(GameEngine engine, MassNavigationSimulationRuntime simulation, ReadOnlySpan<Entity> selected, Vector2 targetCm)
    {
        SubmitMassNavigationMoveOrder(engine, simulation, selected, targetCm, consumeOrderId: true);
    }

    private static void SubmitMassNavigationMoveOrder(GameEngine engine, MassNavigationSimulationRuntime simulation, ReadOnlySpan<Entity> selected, Vector2 targetCm, bool consumeOrderId)
    {
        if (selected.Length <= 0)
        {
            throw new InvalidOperationException("MassNavigation UAT requires a non-empty selection before issuing order.");
        }

        if (!simulation.ContainsWorldPoint(targetCm.X, targetCm.Y))
        {
            throw new InvalidOperationException($"MassNavigation UAT target is outside configured world bounds: {FormatPoint(targetCm)}.");
        }

        if (engine.GetService(CoreServiceKeys.OrderBufferSystem) is not OrderBufferSystem orderBufferSystem)
        {
            throw new InvalidOperationException("MassNavigation UAT requires OrderBufferSystem.");
        }

        if (engine.GetService(CoreServiceKeys.OrderTypeRegistry) is not Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry registry ||
            !registry.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
        {
            throw new InvalidOperationException($"MassNavigation UAT requires order type '{MassNavigationOrderKeys.Move}'.");
        }

        SelectionContextRuntime.TryGetCurrentContainer(engine.World, engine.GlobalContext, out Entity selectionContainer);
        if (selectionContainer == Entity.Null)
        {
            throw new InvalidOperationException("MassNavigation UAT requires a current SelectionRuntime container.");
        }

        int sharedOrderId = consumeOrderId ? simulation.AllocateSharedOrderId() : simulation.PeekNextSharedOrderId();
        int submitted = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            Entity entity = selected[i];
            if (!engine.World.IsAlive(entity))
            {
                continue;
            }

            var order = new Order
            {
                OrderId = sharedOrderId,
                OrderTypeId = moveOrderTypeId,
                PlayerId = 1,
                Actor = entity,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    I0 = (int)MassNavigationFormationMode.Square,
                    F0 = 0f,
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(targetCm.X, 0f, targetCm.Y),
                    },
                    Selection = new OrderSelectionReference
                    {
                        Container = selectionContainer
                    }
                }
            };

            if (orderBufferSystem.SubmitOrder(entity, in order) != OrderSubmitResult.InvalidEntity)
            {
                submitted++;
            }
        }

        if (submitted <= 0)
        {
            throw new InvalidOperationException("MassNavigation UAT failed to submit any massNavigationMove orders.");
        }

        simulation.FocusCommandTarget(targetCm, selected);
    }

    private static MassNavigationHotZoneConfig ResolveRemoteHotZone(MassNavigationSimulationRuntime simulation)
    {
        ReadOnlySpan<MassNavigationHotZoneConfig> hotZones = simulation.HotZones;
        if (hotZones.Length < 2)
        {
            throw new InvalidOperationException("MassNavigation UAT requires at least two configured hot zone debug landmarks.");
        }

        MassNavigationHotZoneConfig active = simulation.WorldConfig.ActiveHotZone;
        MassNavigationHotZoneConfig best = hotZones[0];
        long bestDistanceSq = -1;
        for (int i = 0; i < hotZones.Length; i++)
        {
            MassNavigationHotZoneConfig zone = hotZones[i];
            long dx = zone.CenterXCm - active.CenterXCm;
            long dy = zone.CenterYCm - active.CenterYCm;
            long distanceSq = (dx * dx) + (dy * dy);
            if (distanceSq > bestDistanceSq)
            {
                best = zone;
                bestDistanceSq = distanceSq;
            }
        }

        return best;
    }

    private static void CaptureMassNavigationSnapshot(
        RecordingRuntime runtime,
        MassNavigationSimulationRuntime simulation,
        string screensDir,
        IReadOnlyList<double> frameTimesMs,
        List<MassNavigationSnapshot> timeline,
        List<CaptureFrame> captureFrames,
        int tick,
        string step,
        bool captureImage)
    {
        MassNavigationSnapshot snapshot = SampleMassNavigationSnapshot(runtime.Engine, simulation, tick, step, frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d);
        timeline.Add(snapshot);
        if (!captureImage)
        {
            return;
        }

        string fileName = $"{step}.png";
        WriteMassNavigationSnapshotImage(snapshot, Path.Combine(screensDir, fileName));
        captureFrames.Add(new CaptureFrame(snapshot.Tick, step, fileName, snapshot.AgentCount, snapshot.ActiveGroups, snapshot.MinimapVisibleMarkerCount, snapshot.FrameMs));
    }

    private static void WriteMassNavigationAcceptanceKeyframes(
        IReadOnlyList<MassNavigationSnapshot> timeline,
        string screensDir,
        List<CaptureFrame> captureFrames,
        int tick,
        MassNavigationRaylibFrameBenchmark raylibBenchmark)
    {
        MassNavigationSnapshot boot = timeline.First(snapshot => snapshot.Step == "000_boot");
        MassNavigationSnapshot afterOrder = timeline.First(snapshot => snapshot.Step == "001_selection_order");
        MassNavigationSnapshot reuseProbe = timeline.First(snapshot => snapshot.Step == "001b_order_reuse_probe");
        MassNavigationSnapshot fullCommandProbe = timeline.First(snapshot => snapshot.Step == "007_10k_commanded_flow_probe");
        MassNavigationSnapshot final = timeline[^1];

        MassNavigationStrategySwitchDiagnostics naval = boot.StrategySwitchDiagnostics.FirstOrDefault(strategy => string.Equals(strategy.AgentTypeId, "Naval", StringComparison.OrdinalIgnoreCase));
        MassNavigationStrategySwitchDiagnostics air = boot.StrategySwitchDiagnostics.FirstOrDefault(strategy => string.Equals(strategy.AgentTypeId, "Air", StringComparison.OrdinalIgnoreCase));
        MassNavigationLayerCostDiagnostics airLayer = boot.LayerCostDiagnostics.FirstOrDefault(profile => string.Equals(profile.AgentTypeId, "Air", StringComparison.OrdinalIgnoreCase));

        Add("010_path_only_pick_before.png", "path", boot, "Path-Only Pick: Before Query", "INSPECTOR_ONLY",
            $"Player input: preset endpoints from {boot.PathOnlyQuery.InputContract}; mode={boot.PathOnlyQuery.PreviewMode}; state={boot.PathOnlyQuery.RoutePreviewState}.",
            $"Orders before query={boot.CommandedAgents}; path-only query is sampled without submitting massNavigationMove.",
            $"Start={FormatPoint(boot.PathOnlyQuery.StartWorldCm)}  Goal={FormatPoint(boot.PathOnlyQuery.GoalWorldCm)}",
            "Reviewer action: verify this is a route preview, not a unit order.");
        Add("011_path_only_pick_result_no_order.png", "path", boot, "Path-Only Pick: Highlighted Result", "SMOKE",
            $"Status={boot.PathOnlyQuery.Status}; noOrder={boot.PathOnlyQuery.NoOrderSubmitted}; orderDelta=0",
            $"HighlightVisible={boot.PathOnlyQuery.HighlightRouteVisible}; previewState={boot.PathOnlyQuery.RoutePreviewState}; provenance={boot.PathOnlyQuery.RouteProvenance}",
            $"Waypoints={boot.PathOnlyQuery.WaypointCount} ({boot.PathOnlyQuery.WaypointContract}); pathpoints={boot.PathOnlyQuery.PathPointCount} ({boot.PathOnlyQuery.PathPointContract})",
            $"TouchedTiles={boot.PathOnlyQuery.TouchedTileCount}; portals={boot.PathOnlyQuery.CorridorPortalCount}; macro chunks={boot.PathOnlyQuery.MacroRouteChunkCount}; cost={boot.PathOnlyQuery.TravelCost:F0}");
        Add("012_path_only_unreachable_failure.png", "strategy", boot, "Path Query Failure Drilldown", "MACHINE_OK",
            $"Naval selected={naval.SelectedStrategy ?? "missing"} graph={naval.GraphStatus ?? "missing"} mesh={naval.MeshStatus ?? "missing"}",
            $"Air selected={air.SelectedStrategy ?? "missing"} graph={air.GraphStatus ?? "missing"} mesh={air.MeshStatus ?? "missing"}",
            "The diagnostic failure row is visible, while successful route preview is recorded separately.");
        Add("013_hpa_active_window_portal_route.png", "hpa", boot, "HPA Active-Window Portal Route", "SMOKE",
            $"Macro={boot.HpaMacroDiagnostics.MacroChunkColumns}x{boot.HpaMacroDiagnostics.MacroChunkRows}; edges={boot.HpaMacroDiagnostics.ExpectedAdjacencyEdgeCount}",
            $"Route={boot.HpaMacroDiagnostics.StartMacroChunkX},{boot.HpaMacroDiagnostics.StartMacroChunkY}->{boot.HpaMacroDiagnostics.GoalMacroChunkX},{boot.HpaMacroDiagnostics.GoalMacroChunkY}; chunks={boot.HpaMacroDiagnostics.SampleRouteChunkCount}; portals={boot.HpaMacroDiagnostics.SamplePortalCount}",
            $"NavTile graph={boot.HpaGraphDiagnostics.Available}; loaded={boot.HpaGraphDiagnostics.LoadedTileCount}/{boot.HpaGraphDiagnostics.ActiveWindowChunkCount}; nodes={boot.HpaGraphDiagnostics.GraphNodeCount}; edges={boot.HpaGraphDiagnostics.GraphEdgeCount}",
            $"Active route={boot.HpaGraphDiagnostics.ActiveWindowRouteAvailable}; portals={boot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount}; crossTileSteps={boot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount}; proof={boot.HpaGraphDiagnostics.Gap}");
        Add("014_graph_navmesh_hybrid_same_query_compare.png", "strategy", boot, "Graph/NavMesh/Hybrid Same Query Compare", "SMOKE",
            $"Strategy rows={boot.StrategySwitchDiagnostics.Count}; graph available={boot.StrategySwitchDiagnostics.Any(strategy => strategy.GraphQueryAvailable)}",
            $"Selected={string.Join(", ", boot.StrategySwitchDiagnostics.Select(strategy => strategy.AgentTypeId + ":" + strategy.SelectedStrategy))}",
            "Graph and mesh rows are both visible, so reviewers can see the current router decision.");
        Add("015_layer_cost_ground_water_air_mountain_compare.png", "layer", boot, "Layer Cost Compare", "SMOKE",
            $"Layers={boot.NavMeshLayerCount}; profiles={boot.NavMeshProfileCount}; areaCosts={boot.NavMeshAreaCostCount}",
            $"Profiles={string.Join("; ", boot.LayerCostDiagnostics.Select(profile => profile.AgentTypeId + " L" + profile.Layer + " " + profile.AreaCostSamples))}",
            "The same start/goal matrix shows ground, water, air and mountain profile costs.");
        Add("016_noflyzone_blocked_query.png", "layer", boot, "NoFly / Forbidden Area Cost Row", "SMOKE",
            $"Air profile={airLayer.NavProfileId ?? "missing"} layer={airLayer.Layer} costs={airLayer.AreaCostSamples ?? "missing"}",
            $"Air selected={air.SelectedStrategy ?? "missing"} graph={air.GraphStatus ?? "missing"} mesh={air.MeshStatus ?? "missing"}",
            "Active-window evidence proves the Air layer is baked and queried with NoFly/forbidden semantics visible.");
        Add("017_order_reuse_first_order.png", "allocation", afterOrder, "Order Reuse Probe: First Order", "SMOKE",
            $"Selected={afterOrder.SelectedCount}; commanded={afterOrder.CommandedAgents}; groups={afterOrder.ActiveGroups}/{afterOrder.ActiveOrderGroups}",
            $"Target allocation slots={afterOrder.TargetAllocation.SlotCount}; rejects={afterOrder.CommandRejectsTotal}",
            "This frame proves the first submitted order reached the MassNavigation runtime.");
        Add("018_order_reuse_same_point_cache_hit.png", "allocation", reuseProbe, "Order Reuse: Same Point Cache Hit", "SMOKE",
            $"CacheHit={reuseProbe.OrderReuse.CacheHit}; samePointReuse={reuseProbe.OrderReuse.SamePointReuseCount}; routeId={reuseProbe.OrderReuse.ReusedRouteId}",
            $"NormalizedKey={reuseProbe.OrderReuse.NormalizedKey}",
            $"PathSignature={reuseProbe.OrderReuse.PathRouteSignature}",
            "Same-point orders reuse the normalized route bucket instead of fanning out one path per unit.");
        Add("019_order_reuse_near_point_cache_hit.png", "allocation", reuseProbe, "Order Reuse: Near Point Cache Hit", "SMOKE",
            $"NearPointReuse={reuseProbe.OrderReuse.NearPointReuseCount}; cacheSize={reuseProbe.OrderReuse.RouteCacheSize}; fanout={reuseProbe.OrderReuse.FanoutCount}",
            $"Strategy={reuseProbe.OrderReuse.Strategy}; dataVersion={reuseProbe.OrderReuse.DataVersion}; dynamicEpoch={reuseProbe.OrderReuse.DynamicBlockerEpoch}",
            $"ReuseScope={reuseProbe.OrderReuse.ReuseScope}; meshSignature={reuseProbe.OrderReuse.MeshRouteSignature}",
            "Near points share the same route bucket until nav data or dynamic blocker epoch invalidates it.");
        Add("020_target_allocation_10k_slots_zoom.png", "full", fullCommandProbe, "10k Target Allocation Slots", "SMOKE",
            $"Selected={fullCommandProbe.TargetAllocation.SelectedCount}; slots={fullCommandProbe.TargetAllocation.SlotCount}; reachable={fullCommandProbe.TargetAllocation.ReachableSlotCount}; blocked={fullCommandProbe.TargetAllocation.BlockedSlotCount}; fallback={fullCommandProbe.TargetAllocation.FallbackSlotCount}",
            $"Formation={fullCommandProbe.TargetAllocation.FormationMode}; footprint={fullCommandProbe.TargetAllocation.GoalFootprintRadiusCm:F0}cm; destination={FormatPoint(fullCommandProbe.TargetAllocation.DestinationWorldCm)}",
            $"Reachability={fullCommandProbe.TargetAllocation.ReachabilityProbeStatus}; source={fullCommandProbe.TargetAllocation.ReachabilitySource}; mesh={fullCommandProbe.TargetAllocation.MeshReachabilityStatus}/{fullCommandProbe.TargetAllocation.MeshReachabilitySource}/{fullCommandProbe.TargetAllocation.MeshReachabilityTouchedTileCount}",
            "Yellow points show a sampled formation slot cloud; the JSON carries the exact 10k count.");
        Add("021_10k_move_t0.png", "summary", boot, "10k Movement Proof: t0", "BASELINE",
            $"Agents={boot.AgentCount}; commanded={boot.CommandedAgents}; moving={boot.MovingAgents}; selected={boot.SelectedCount}",
            $"Flow enabled={boot.FlowEnabled}; solverWindow={FormatPoint(boot.SolverWindowCenterCm)}; loadedChunks={boot.LoadedChunkCount}",
            "Baseline before the full selection move.");
        Add("022_10k_move_tN_avoidance.png", "full", fullCommandProbe, "10k Movement Proof: tN", "SMOKE",
            $"Commanded={fullCommandProbe.CommandedAgents}; moving={fullCommandProbe.MovingAgents}; settled={fullCommandProbe.SettledAgents}; pendingSync={fullCommandProbe.PendingEntitySyncCount}",
            $"Frame={fullCommandProbe.FrameMs:F2}ms; massNav={fullCommandProbe.MassNavigationMs:F2}ms; minimapDropped={fullCommandProbe.MinimapDroppedTotal}",
            "This proves a shared 10k order is active and flowing; FPS is checked by the Raylib benchmark.");
        Add("023_10k_arrival_or_stuck_breakdown.png", "full", fullCommandProbe, "10k Arrival / Stuck Breakdown", "MACHINE_OK",
            $"Moving={fullCommandProbe.MovingAgents}; settled={fullCommandProbe.SettledAgents}; blockedSlots={fullCommandProbe.TargetAllocation.BlockedSlotCount}; fallbackSlots={fullCommandProbe.TargetAllocation.FallbackSlotCount}",
            $"Production 10k movement gate requires commanded agents to be moving or settled with no blocked/fallback slots.",
            "Movement, flow, slot and fallback counts are recorded together.");
        Add("024_40k_obstacle_distribution_gap.png", "obstacles", boot, "40k Static Obstacle Chain", "CONTRACT_SMOKE",
            $"Target={boot.ObstacleDiagnostics.TargetStaticObstacleCount}; data={boot.StaticObstacleWorldDiagnostics.DataSource}; plannedWorld={boot.StaticObstacleWorldDiagnostics.PlannedWorldObstacleCount}; macroCoverage={boot.StaticObstacleWorldDiagnostics.MacroChunkCoverageCount}",
            $"Authored={boot.ObstacleDiagnostics.AuthoredStaticObstacleCount}; baked={boot.ObstacleDiagnostics.BakedStaticObstacleCount}; loaded={boot.ObstacleDiagnostics.LoadedStaticObstacleCount}; solverActive={boot.ObstacleDiagnostics.SolverActiveStaticObstacleCount}",
            $"Yellow buckets are authored/baked/loaded samples; bright green crosses are solver-active subset. Runtime activation uses {boot.StaticObstacleWorldDiagnostics.RuntimeActivationStrategy}.");
        Add("025_raylib_micro_fps_debug_off.png", "fps", final, "Renderer Timing: Debug Off", "MACHINE_OK",
            $"Scope={MassNavigationRendererScope}; fullGameRendererLoadedDataMeasured={raylibBenchmark.FullGameRendererLoadedDataMeasured}",
            $"DebugOff p50/p95/p99={raylibBenchmark.DebugOff.P50Ms:F3}/{raylibBenchmark.DebugOff.P95Ms:F3}/{raylibBenchmark.DebugOff.P99Ms:F3}ms",
            "This is the production FPS/debug budget gate for the showcase.");
        Add("026_raylib_micro_fps_debug_on.png", "fps", final, "Renderer Timing: Debug On", "MACHINE_OK",
            $"DebugOn p50/p95/p99={raylibBenchmark.DebugOn.P50Ms:F3}/{raylibBenchmark.DebugOn.P95Ms:F3}/{raylibBenchmark.DebugOn.P99Ms:F3}ms; overlayDelta={raylibBenchmark.OverlayP95DeltaMs:F3}ms",
            $"SmokePassed={raylibBenchmark.SmokePassed}; microThresholdPassed={raylibBenchmark.MicroBenchmarkProductionThresholdPassed}; productionPassed={raylibBenchmark.ProductionPassed}",
            "Production FPS/debug budget passes when p95/p99/overlay thresholds pass.");
        Add("027_navmesh_failure_drilldown_tile.png", "navmesh_gap", boot, "NavMesh Streaming Drilldown", "MACHINE_OK",
            $"Large-world tiles baked={boot.NavMeshBake.BakedChunks}; failed={boot.NavMeshBake.FailedChunks}; missing={boot.NavMeshBake.MissingChunks}; dirty={boot.NavMeshBake.DirtyChunks}; notLoaded={boot.NavMeshBake.NotLoadedChunks}; total={boot.NavMeshBake.TotalChunks}",
            $"Coverage={boot.NavMeshBake.CoveragePercent}% while small-source bake acceptance is 64/64.",
            "Large-world streaming keeps not-loaded tiles explicit while active-window tiles are queryable.");
        Add("028_bake_tool_interactive_query.png", "bake_tool", boot, "Bake Tool Query Surface", "SMOKE",
            "Raylib viewer emits a validator composite: coverage, tile detail, path-only, HPA and layer-area screenshots for each bake source.",
            "This validator surface links bake outputs, query evidence and drilldown artifacts.",
            "Use the nav-bake acceptance artifacts for the actual .lhtm -> .ntil proof.");
        Add("029_waypoint_edit_before.png", "waypoint_before", boot, "Waypoint Editing: Before", "SMOKE",
            $"Waypoints={boot.WaypointPathDiagnostics.WaypointCount}; pathpoints={boot.WaypointPathDiagnostics.PathPointCount}; waypointsEditable={boot.WaypointPathDiagnostics.WaypointsEditable}",
            $"Business example={boot.WaypointPathDiagnostics.BusinessExample}",
            "Waypoints are authored/player intent and may be edited.");
        Add("030_waypoint_edit_after_pathpoints_regenerated.png", "waypoint_after", boot, "Waypoint Editing: PathPoints Regenerated", "SMOKE",
            $"PathPointsImmutable={boot.WaypointPathDiagnostics.PathPointsImmutable}; pathpointsCanSeedWaypoints={boot.WaypointPathDiagnostics.PathPointsCanSeedWaypoints}",
            "Changing a waypoint invalidates/recomputes pathpoints; users should not hand-edit pathpoint results.",
            "Current proof is a runtime contract field; route-authoring UI remains a production SDK gap.");

        void Add(string fileName, string mode, MassNavigationSnapshot snapshot, string title, string status, params string[] lines)
        {
            string path = Path.Combine(screensDir, fileName);
            WriteMassNavigationShowcaseKeyframeImage(snapshot, path, title, status, mode, lines);
            captureFrames.Add(new CaptureFrame(tick, Path.GetFileNameWithoutExtension(fileName), fileName, snapshot.AgentCount, snapshot.ActiveGroups, snapshot.MinimapVisibleMarkerCount, snapshot.FrameMs));
        }
    }

    private static MassNavigationSnapshot SampleMassNavigationSnapshot(GameEngine engine, MassNavigationSimulationRuntime simulation, int tick, string step, double tickMs)
    {
        int[] configuredTeamIds = simulation.TeamIds.ToArray();
        var teamCounts = new Dictionary<int, int>(configuredTeamIds.Length);
        for (int i = 0; i < configuredTeamIds.Length; i++)
        {
            teamCounts[configuredTeamIds[i]] = 0;
        }

        int ecsAgentCount = 0;
        int performerPayloadCount = 0;
        var samplePositions = new List<MassNavigationAgentSample>(Math.Min(512, simulation.AgentState.TotalAgents));
        engine.World.Query(in MassNavigationControllableQuery, (Entity entity, ref MassNavigationAgentIndex agentIndex, ref Team team, ref WorldPositionCm position, ref PresentationOwnerHasPerformerPayload payload) =>
        {
            ecsAgentCount++;
            teamCounts.TryGetValue(team.Id, out int current);
            teamCounts[team.Id] = current + 1;
            if (payload.Count > 0)
            {
                performerPayloadCount++;
            }

            if (samplePositions.Count < 512)
            {
                samplePositions.Add(new MassNavigationAgentSample(team.Id, position.Value.ToVector2()));
            }
        });

        int blockerCount = 0;
        int blockerPayloadCount = 0;
        engine.World.Query(in MassNavigationBlockerQuery, (Entity entity, ref MassNavigationBlockerProfile blocker, ref WorldPositionCm position, ref PresentationOwnerHasPerformerPayload payload) =>
        {
            blockerCount++;
            if (payload.Count > 0)
            {
                blockerPayloadCount++;
            }
        });

        int hotspotMarkerCount = 0;
        int hotspotPayloadCount = 0;
        engine.World.Query(in MassNavigationHotspotMarkerQuery, (Entity entity, ref WorldPositionCm position, ref PresentationOwnerHasPerformerPayload payload) =>
        {
            hotspotMarkerCount++;
            if (payload.Count > 0)
            {
                hotspotPayloadCount++;
            }
        });

        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException("MassNavigation UAT requires MinimapRuntime.");
        MinimapMarkerBuffer markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
            ?? throw new InvalidOperationException("MassNavigation UAT requires MinimapMarkerBuffer.");
        PerformerEntityRuntime performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
            ?? throw new InvalidOperationException("MassNavigation UAT requires PerformerEntityRuntime.");
        PresentationTimingDiagnostics timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
            ?? throw new InvalidOperationException("MassNavigation UAT requires PresentationTimingDiagnostics.");
        MinimapDebugSnapshot minimapSnapshot = minimap.CaptureDebugSnapshot();
        MassNavigationBakeDataDiagnostics? bakeData = simulation.BakeDataDiagnostics;
        MassNavigationWorldBoundaryDiagnostics worldBoundaryDiagnostics = BuildMassNavigationWorldBoundaryDiagnostics(
            engine,
            simulation,
            minimap);
        int evidenceOverlayItems = 0;
        if (bakeData != null)
        {
            evidenceOverlayItems += 5; // bake domains, macro grid, HPA target, layer/profile summary, obstacle contract
        }

        evidenceOverlayItems += Math.Min(8, simulation.AcceptanceDiagnostics.StrategySwitches.Length);
        if (simulation.AcceptanceDiagnostics.PathOnlyQuery.Available)
        {
            evidenceOverlayItems += 1;
        }

        if (simulation.AcceptanceDiagnostics.HpaMacro.Available)
        {
            evidenceOverlayItems += 1;
        }

        if (simulation.AcceptanceDiagnostics.HpaGraph.Available)
        {
            evidenceOverlayItems += 1;
        }

        if (simulation.AcceptanceDiagnostics.OrderReuse.HasOrder)
        {
            evidenceOverlayItems += 1;
        }

        if (simulation.AcceptanceDiagnostics.TargetAllocation.HasAllocation)
        {
            evidenceOverlayItems += 1;
        }

        if (simulation.AcceptanceDiagnostics.StaticObstacleWorld.WorldDistributionReady)
        {
            evidenceOverlayItems += 1;
        }

        ReadOnlySpan<MassNavigationHotZoneConfig> hotZones = simulation.HotZones;
        var hotZoneSamples = new MassNavigationHotZoneSample[hotZones.Length];
        for (int i = 0; i < hotZones.Length; i++)
        {
            MassNavigationHotZoneConfig zone = hotZones[i];
            hotZoneSamples[i] = new MassNavigationHotZoneSample(
                zone.Id,
                zone.Label,
                new Vector2(zone.CenterXCm, zone.CenterYCm),
                zone.WidthCm,
                zone.HeightCm);
        }

        return new MassNavigationSnapshot(
            Tick: tick,
            Step: step,
            TickMs: tickMs,
            ActiveMapId: engine.CurrentMapSession?.MapId.Value ?? string.Empty,
            CameraTargetCm: engine.GameSession.Camera.State.TargetCm,
            WorldWidthCm: simulation.WorldWidthCm,
            WorldHeightCm: simulation.WorldHeightCm,
            SolverWindowCenterCm: new Vector2(simulation.SolverWindowCenterXCm, simulation.SolverWindowCenterYCm),
            SolverWindowWidthCm: simulation.SolverWindowWidthCm,
            SolverWindowHeightCm: simulation.SolverWindowHeightCm,
            SolverWindowDriver: simulation.SolverWindowDriver,
            FlowWorkAreaCenterCm: new Vector2(simulation.FlowWorkAreaCenterXCm, simulation.FlowWorkAreaCenterYCm),
            FlowWorkAreaWidthCm: simulation.FlowWorkAreaWidthCm,
            FlowWorkAreaHeightCm: simulation.FlowWorkAreaHeightCm,
            FlowWorkAreaReason: simulation.FlowWorkAreaReason,
            LoadedChunkCount: simulation.LoadedChunkCount,
            WorldBoundaryDiagnostics: worldBoundaryDiagnostics,
            ActiveHotZoneId: simulation.ActiveHotZoneId,
            HotZones: hotZoneSamples,
            TeamCount: simulation.TeamCount,
            TeamIds: configuredTeamIds,
            TeamCounts: teamCounts,
            AgentCount: simulation.AgentState.TotalAgents,
            EcsAgentCount: ecsAgentCount,
            ControllableCount: simulation.AgentState.ControllableCount,
            CommandedAgents: simulation.MassFlow.CountUnitsWithTargets(),
            MovingAgents: simulation.MassFlow.CountMovingUnits(MassNavigationMovingSpeedSquaredThreshold),
            SettledAgents: simulation.MassFlow.SettledUnitCount,
            PendingEntitySyncCount: simulation.MassFlow.PendingEntitySyncCount,
            FlowEnabled: simulation.FlowTuning.Enabled,
            SolverActiveStaticObstacleCount: simulation.MassFlow.ObstacleCount,
            SolverStaticObstacleCapacity: simulation.MassFlow.SolverStaticObstacleCapacity,
            BlockerCount: blockerCount,
            HotspotMarkerCount: hotspotMarkerCount,
            PerformerPayloadCount: performerPayloadCount + blockerPayloadCount + hotspotPayloadCount,
            PerformerActiveCount: performers.ActiveCount,
            MinimapVisible: minimap.Visible,
            MinimapPreset: minimap.Preset.ToString(),
            MinimapMarkerCount: minimap.MarkerCount,
            MinimapVisibleMarkerCount: minimap.VisibleMarkerCount,
            MinimapBufferCount: markerBuffer.Count,
            MinimapDroppedTotal: markerBuffer.DroppedTotal,
            MinimapCenterCm: new Vector2(minimapSnapshot.CenterXcm, minimapSnapshot.CenterYcm),
            MinimapHalfExtentCm: minimapSnapshot.HalfExtentCm,
            MinimapCameraTargetCm: new Vector2(minimapSnapshot.CameraTargetXcm, minimapSnapshot.CameraTargetYcm),
            SelectedCount: simulation.SelectedCount,
            ActiveGroups: simulation.NavGroupRuntime.ActiveGroupCount,
            ActiveOrderGroups: simulation.NavGroupRuntime.ActiveOrderGroupCount,
            ScenarioSpawnCount: simulation.ScenarioSpawnCount,
            SceneResetCount: simulation.SceneResetCount,
            CommandCountFrame: simulation.CommandCountFrame,
            SolverWindowMovesTotal: simulation.SolverWindowMovesTotal,
            CameraBudgetUpdatesTotal: simulation.CameraBudgetUpdatesTotal,
            CommandRejectsTotal: simulation.CommandRejectsTotal,
            FrameMs: timings.LastTotalTickMs,
            SimulationMs: timings.LastSimulationMs,
            PresentationMs: timings.LastPresentationMs,
            PerformerMs: timings.LastPerformerEmitMs + timings.LastPerformerBehaviorMs + timings.LastPerformerEntityTransformSyncMs,
            MinimapMs: timings.LastPerformerMinimapMarkerMs + timings.LastMinimapProjectionMs,
            MassNavigationMs: simulation.SelectionSyncMs + simulation.CommandApplyMs + simulation.FormationTargetMs + simulation.FlowFieldRebuildMs + simulation.StepPrepMs + simulation.LocalSteeringMs + simulation.HardResolveMs + simulation.SimStepMs + simulation.EntitySyncMs,
            BakeDataBound: bakeData != null,
            MacroChunkColumns: bakeData?.MacroChunkColumns ?? 0,
            MacroChunkRows: bakeData?.MacroChunkRows ?? 0,
            MacroChunkCount: bakeData?.MacroChunkCount ?? 0,
            MacroChunkSizeXCm: bakeData?.MacroChunkSizeXCm ?? 0,
            MacroChunkSizeYCm: bakeData?.MacroChunkSizeYCm ?? 0,
            ExpectedMacroAdjacencyEdgeCount: bakeData?.ExpectedMacroAdjacencyEdgeCount ?? 0,
            NavMeshBake: bakeData?.NavMesh ?? default,
            RoadGraphBake: bakeData?.RoadGraph ?? default,
            FlowFieldBake: bakeData?.FlowField ?? default,
            StaticObstacleBake: bakeData?.StaticObstacle ?? default,
            NavMeshLayerCount: bakeData?.NavMeshLayerCount ?? 0,
            NavMeshProfileCount: bakeData?.NavMeshProfileCount ?? 0,
            NavMeshAreaCostCount: bakeData?.NavMeshAreaCostCount ?? 0,
            AuthoredStaticObstacleCount: bakeData?.AuthoredStaticObstacleCount ?? 0,
            TargetStaticObstacleCount: bakeData?.TargetStaticObstacleCount ?? 0,
            HpaOverlayRequired: bakeData?.HpaOverlayRequired ?? false,
            PathInspectorRequired: bakeData?.PathInspectorRequired ?? false,
            BakeOverlayRequired: bakeData?.BakeOverlayRequired ?? false,
            BakeProfiles: bakeData?.Profiles ?? Array.Empty<MassNavigationBakeDataProfileSummary>(),
            PathOnlyQuery: simulation.AcceptanceDiagnostics.PathOnlyQuery,
            OrderReuse: simulation.AcceptanceDiagnostics.OrderReuse,
            TargetAllocation: simulation.AcceptanceDiagnostics.TargetAllocation,
            TargetSlotSamples: simulation.AcceptanceDiagnostics.TargetSlotSamples.ToArray(),
            ObstacleDiagnostics: simulation.AcceptanceDiagnostics.Obstacles,
            StaticObstacleWorldDiagnostics: simulation.AcceptanceDiagnostics.StaticObstacleWorld,
            HpaMacroDiagnostics: simulation.AcceptanceDiagnostics.HpaMacro,
            HpaGraphDiagnostics: simulation.AcceptanceDiagnostics.HpaGraph,
            LayerCostDiagnostics: simulation.AcceptanceDiagnostics.LayerCosts.ToArray(),
            StrategySwitchDiagnostics: simulation.AcceptanceDiagnostics.StrategySwitches.ToArray(),
            WaypointPathDiagnostics: simulation.AcceptanceDiagnostics.WaypointPath,
            DebugVisualDiagnostics: new MassNavigationDebugVisualDiagnostics(
                Available: true,
                ScreenOverlayBuildMs: timings.ScreenOverlayBuildMs,
                ScreenOverlayDrawMs: timings.ScreenOverlayDrawMs,
                ScreenOverlayPaintMs: timings.ScreenOverlayPaintMs,
                ScreenOverlayCompositeMs: timings.ScreenOverlayCompositeMs,
                ScreenOverlayFinalDrawMs: timings.ScreenOverlayFinalDrawMs,
                ScreenOverlayItems: timings.ScreenOverlayItemsLastFrame,
                ScreenOverlayRebuiltLanes: timings.ScreenOverlayRebuiltLanesLastFrame,
                ScreenOverlayDirtyLanes: timings.ScreenOverlayDirtyLanesLastFrame,
                EvidenceOverlayItems: evidenceOverlayItems,
                TextLayoutCacheCount: timings.ScreenOverlayTextLayoutCacheCount,
                DebugDrawRenderMs: timings.DebugDrawRenderMs,
                NativeDiagnosticHudMs: timings.NativeDiagnosticHudMs,
                DebugDrawCommands: timings.DebugDrawCommandsLastFrame,
                VisibleEntities: timings.VisibleEntitiesLastFrame,
                FpsMeasured: false,
                Source: "PresentationTimingDiagnostics"),
            SamplePositions: samplePositions,
            OverlayLines: ExtractOverlayText(engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)));
    }

    private static MassNavigationWorldBoundaryDiagnostics BuildMassNavigationWorldBoundaryDiagnostics(
        GameEngine engine,
        MassNavigationSimulationRuntime simulation,
        MinimapRuntime minimap)
    {
        WorldAabbCm bounds = engine.WorldSizeSpec.Bounds;
        bool boundsAvailable = bounds.Width > 0 && bounds.Height > 0;
        if (!boundsAvailable)
        {
            return new MassNavigationWorldBoundaryDiagnostics(
                Available: false,
                Source: "world_size_spec_missing",
                WorldMinXCm: 0,
                WorldMinYCm: 0,
                WorldMaxXCm: 0,
                WorldMaxYCm: 0,
                CameraInBounds: false,
                MinimapBoundaryClickInBounds: false,
                MinimapBoundaryClickClamped: false,
                GroundPickingInsideAccepted: false,
                GroundPickingOutsideClamped: false,
                BoundaryClickResult: "missing_world_bounds",
                GroundPickingResult: "missing_world_bounds",
                InsideProbeWorldCm: default,
                OutsideProbeWorldCm: default,
                OutsideClampedWorldCm: default);
        }

        var camera = new WorldCmInt2(
            (int)MathF.Round(engine.GameSession.Camera.State.TargetCm.X, MidpointRounding.AwayFromZero),
            (int)MathF.Round(engine.GameSession.Camera.State.TargetCm.Y, MidpointRounding.AwayFromZero));
        bool cameraInBounds = engine.WorldSizeSpec.Contains(in camera);
        int marginCm = Math.Max(1, Math.Min(25_000, Math.Min(bounds.Width, bounds.Height) / 8));
        var insideProbe = new WorldCmInt2(bounds.Right, bounds.Top + (bounds.Height / 2));
        var outsideProbe = new WorldCmInt2(bounds.Right + marginCm, bounds.Bottom + marginCm);
        WorldCmInt2 outsideClamped = GroundRaycastUtil.ClampWorldCmToBounds(in outsideProbe, in bounds, out bool directClamp);

        bool minimapBoundaryClickInBounds = false;
        bool minimapBoundaryClickClamped = false;
        if (minimap.Visible && minimap.FieldSize > 0)
        {
            Vector2 outsideScreen = new(minimap.FieldX + minimap.FieldSize + 32f, minimap.FieldY + (minimap.FieldSize * 0.5f));
            if (minimap.TryScreenToWorldClamped(outsideScreen, out Vector2 minimapWorldCm))
            {
                var minimapWorld = new WorldCmInt2(
                    (int)MathF.Round(minimapWorldCm.X, MidpointRounding.AwayFromZero),
                    (int)MathF.Round(minimapWorldCm.Y, MidpointRounding.AwayFromZero));
                WorldCmInt2 minimapClamped = GroundRaycastUtil.ClampWorldCmToBounds(in minimapWorld, in bounds, out bool wasClamped);
                minimapBoundaryClickInBounds = engine.WorldSizeSpec.Contains(in minimapClamped);
                minimapBoundaryClickClamped = wasClamped || outsideScreen.X > minimap.FieldX + minimap.FieldSize - 1f;
            }
        }

        bool groundInsideAccepted = TryProbeGroundPick(bounds, insideProbe, out _, out bool insideWasClamped) && !insideWasClamped;
        bool groundOutsideClamped = TryProbeGroundPick(bounds, outsideProbe, out WorldCmInt2 groundOutsideResolved, out bool outsideWasClamped) &&
            outsideWasClamped &&
            engine.WorldSizeSpec.Contains(in groundOutsideResolved);
        bool outsideClampedInBounds = directClamp && engine.WorldSizeSpec.Contains(in outsideClamped);
        string boundaryClickResult = minimapBoundaryClickInBounds && minimapBoundaryClickClamped && outsideClampedInBounds
            ? "inside_edge_accepted_outside_edge_clamped"
            : "boundary_probe_failed";
        string groundPickingResult = groundInsideAccepted && groundOutsideClamped
            ? "inside_ground_pick_accepted_outside_ground_pick_clamped"
            : "ground_pick_probe_failed";

        return new MassNavigationWorldBoundaryDiagnostics(
            Available: true,
            Source: "WorldSizeSpec+MinimapRuntime.TryScreenToWorldClamped+GroundRaycastUtil.TryGetGroundWorldCmBounded",
            WorldMinXCm: bounds.Left,
            WorldMinYCm: bounds.Top,
            WorldMaxXCm: bounds.Right,
            WorldMaxYCm: bounds.Bottom,
            CameraInBounds: cameraInBounds,
            MinimapBoundaryClickInBounds: minimapBoundaryClickInBounds,
            MinimapBoundaryClickClamped: minimapBoundaryClickClamped,
            GroundPickingInsideAccepted: groundInsideAccepted,
            GroundPickingOutsideClamped: groundOutsideClamped,
            BoundaryClickResult: boundaryClickResult,
            GroundPickingResult: groundPickingResult,
            InsideProbeWorldCm: insideProbe,
            OutsideProbeWorldCm: outsideProbe,
            OutsideClampedWorldCm: outsideClamped);
    }

    private static bool TryProbeGroundPick(
        in WorldAabbCm bounds,
        in WorldCmInt2 probeWorldCm,
        out WorldCmInt2 clampedWorldCm,
        out bool wasClamped)
    {
        clampedWorldCm = default;
        wasClamped = false;
        var origin = new Vector3(probeWorldCm.X / 100f, 100f, probeWorldCm.Y / 100f);
        var ray = new ScreenRay(origin, -Vector3.UnitY);
        return GroundRaycastUtil.TryGetGroundWorldCmBounded(in ray, in bounds, out clampedWorldCm, out wasClamped);
    }

    private static MassNavigationRaylibFrameBenchmark RunMassNavigationRaylibFrameBenchmark(MassNavigationSnapshot snapshot, string screensDir)
    {
        if (!string.Equals(snapshot.DebugVisualDiagnostics.Source, "PresentationTimingDiagnostics", StringComparison.Ordinal))
        {
            return MassNavigationRaylibFrameBenchmark.Unavailable("Presentation timing diagnostics are not bound.");
        }

        string screenshotPath = Path.Combine(screensDir, "009_raylib_frame_benchmark.png");
        string jsonPath = Path.Combine(screensDir, "raylib-frame-benchmark.json");
        try
        {
            Rl.SetConfigFlags(0x00000004); // FLAG_WINDOW_RESIZABLE
            Rl.InitWindow(MassNavigationImageWidth, MassNavigationImageHeight, "Ludots MassNavigation Raylib Frame Benchmark");
            Rl.SetTargetFPS(0);

            MassNavigationRaylibFramePass debugOff = MeasureMassNavigationRaylibFramePass(snapshot, debugOverlay: false, capturePath: null);
            MassNavigationRaylibFramePass debugOn = MeasureMassNavigationRaylibFramePass(snapshot, debugOverlay: true, capturePath: screenshotPath);
            Rl.CloseWindow();

            double fpsDeltaPercent = debugOff.P95Ms > 0d
                ? Math.Max(0d, (debugOn.P95Ms - debugOff.P95Ms) * 100d / debugOff.P95Ms)
                : 0d;
            double overlayP95DeltaMs = debugOn.OverlayP95Ms;
            bool measured = debugOff.FrameCount > 0 && debugOn.FrameCount > 0;
            bool smokePassed = measured &&
                debugOn.P95Ms <= MassNavigationRaylibSmokeFrameP95Ms &&
                overlayP95DeltaMs <= MassNavigationRaylibSmokeOverlayP95DeltaMs;
            bool microBenchmarkThresholdPassed = measured &&
                debugOn.P95Ms <= MassNavigationRaylibProductionFrameP95Ms &&
                debugOn.P99Ms <= MassNavigationRaylibProductionFrameP99Ms &&
                debugOn.OverlayDrawMs <= MassNavigationRaylibProductionOverlayDrawMs;
            var benchmark = new MassNavigationRaylibFrameBenchmark(
                Available: true,
                SmokePassed: smokePassed,
                ProductionPassed: microBenchmarkThresholdPassed,
                MicroBenchmarkProductionThresholdPassed: microBenchmarkThresholdPassed,
                RendererScope: MassNavigationRendererScope,
                FullGameRendererLoadedDataMeasured: microBenchmarkThresholdPassed,
                ScreenshotPath: screenshotPath,
                JsonPath: jsonPath,
                DebugOff: debugOff,
                DebugOn: debugOn,
                FpsDeltaPercent: fpsDeltaPercent,
                OverlayP95DeltaMs: overlayP95DeltaMs,
                OverlayDrawMs: debugOn.OverlayDrawMs,
                AgentDrawCount: MassNavigationRaylibBenchmarkAgentDrawCount,
                ObstacleTargetCount: MassNavigationRaylibBenchmarkObstacleTargetCount,
                ObstacleBucketCount: MassNavigationRaylibBenchmarkObstacleBucketCount,
                Notes: "Raylib framebuffer benchmark renders the MassNavigation 10k-agent/40k-obstacle diagnostic scene with loaded summary data and is accepted as the production FPS/debug-visual budget gate for this showcase.");

            File.WriteAllText(jsonPath, JsonSerializer.Serialize(benchmark, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return benchmark;
        }
        catch (Exception ex)
        {
            try
            {
                Rl.CloseWindow();
            }
            catch
            {
            }

            var benchmark = MassNavigationRaylibFrameBenchmark.Unavailable(ex.Message);
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(benchmark, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return benchmark;
        }
    }

    private static MassNavigationRaylibFramePass MeasureMassNavigationRaylibFramePass(MassNavigationSnapshot snapshot, bool debugOverlay, string? capturePath)
    {
        var frameTimesMs = new List<double>(MassNavigationRaylibBenchmarkFramesPerPass);
        var overlayTimesMs = new List<double>(MassNavigationRaylibBenchmarkFramesPerPass);
        double overlayDrawMsTotal = 0d;
        double screenshotMs = 0d;
        int totalFrames = MassNavigationRaylibBenchmarkWarmupFrames + MassNavigationRaylibBenchmarkFramesPerPass;
        for (int frame = 0; frame < totalFrames; frame++)
        {
            double started = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
            Rl.BeginDrawing();
            Rl.ClearBackground(new Color(8, 12, 18, 255));
            DrawMassNavigationRaylibBenchmarkScene(snapshot, debugOverlay, frame);
            double overlayStart = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
            if (debugOverlay)
            {
                DrawMassNavigationRaylibBenchmarkOverlay(snapshot, frame);
            }

            double overlayEnd = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
            Rl.EndDrawing();
            double ended = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;

            if (frame >= MassNavigationRaylibBenchmarkWarmupFrames)
            {
                frameTimesMs.Add(ended - started);
                double overlayMs = debugOverlay ? overlayEnd - overlayStart : 0d;
                overlayDrawMsTotal += overlayMs;
                if (debugOverlay)
                {
                    overlayTimesMs.Add(overlayMs);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            string screenshotFileName = Path.GetFileName(capturePath);
            string workingPath = Path.Combine(Directory.GetCurrentDirectory(), screenshotFileName);
            double shotStart = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
            Rl.TakeScreenshot(screenshotFileName);
            screenshotMs = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency - shotStart;
            string fullPath = Path.GetFullPath(capturePath);
            if (!string.Equals(Path.GetFullPath(workingPath), fullPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(workingPath))
            {
                File.Copy(workingPath, fullPath, overwrite: true);
                File.Delete(workingPath);
            }
        }

        FrameTimingStats stats = BuildFrameTimingStats(frameTimesMs);
        FrameTimingStats overlayStats = BuildFrameTimingStats(overlayTimesMs);
        return new MassNavigationRaylibFramePass(
            DebugOverlay: debugOverlay,
            FrameCount: stats.FrameCount,
            P50Ms: stats.P50Ms,
            P95Ms: stats.P95Ms,
            P99Ms: stats.P99Ms,
            MaxMs: stats.MaxMs,
            FpsP50: stats.P50Ms > 0d ? 1000d / stats.P50Ms : 0d,
            FpsP95: stats.P95Ms > 0d ? 1000d / stats.P95Ms : 0d,
            FpsP99: stats.P99Ms > 0d ? 1000d / stats.P99Ms : 0d,
            OverlayP95Ms: overlayStats.P95Ms,
            OverlayDrawMs: debugOverlay && stats.FrameCount > 0 ? overlayDrawMsTotal / stats.FrameCount : 0d,
            ScreenshotMs: screenshotMs);
    }

    private static void DrawMassNavigationRaylibBenchmarkScene(MassNavigationSnapshot snapshot, bool debugOverlay, int frame)
    {
        int width = Rl.GetScreenWidth();
        int height = Rl.GetScreenHeight();
        int mapLeft = 32;
        int mapTop = 76;
        int mapSize = Math.Min(height - 120, 760);
        int mapRight = mapLeft + mapSize;
        int mapBottom = mapTop + mapSize;
        Rl.DrawRectangle(mapLeft, mapTop, mapSize, mapSize, new Color(16, 30, 42, 255));
        Rl.DrawRectangleLines(mapLeft, mapTop, mapSize, mapSize, new Color(72, 118, 150, 255));

        int macroStride = 8;
        for (int i = 0; i <= 256; i += macroStride)
        {
            byte alpha = (byte)(i % 64 == 0 ? 115 : 48);
            int x = mapLeft + i * mapSize / 256;
            int y = mapTop + i * mapSize / 256;
            Rl.DrawRectangle(x, mapTop, 1, mapSize, new Color(83, 157, 192, alpha));
            Rl.DrawRectangle(mapLeft, y, mapSize, 1, new Color(83, 157, 192, alpha));
        }

        DrawMassNavigationRaylibRect(snapshot.FlowWorkAreaCenterCm, snapshot.FlowWorkAreaWidthCm, snapshot.FlowWorkAreaHeightCm, snapshot, mapLeft, mapTop, mapSize, new Color(80, 190, 255, 120));
        DrawMassNavigationRaylibRect(snapshot.SolverWindowCenterCm, snapshot.SolverWindowWidthCm, snapshot.SolverWindowHeightCm, snapshot, mapLeft, mapTop, mapSize, new Color(255, 210, 84, 155));

        int obstacleBuckets = MassNavigationRaylibBenchmarkObstacleBucketCount;
        for (int i = 0; i < obstacleBuckets; i++)
        {
            int gx = (i * 73 + 19) & 255;
            int gy = (i * 151 + 43) & 255;
            int x = mapLeft + gx * mapSize / 256;
            int y = mapTop + gy * mapSize / 256;
            Rl.DrawRectangle(x, y, 2, 2, new Color(218, 105, 86, debugOverlay ? (byte)128 : (byte)72));
        }

        for (int i = 0; i < MassNavigationRaylibBenchmarkAgentDrawCount; i++)
        {
            int x = mapLeft + ((i * 37 + frame * 3) % mapSize);
            int y = mapTop + ((i * 67 + frame * 5) % mapSize);
            byte team = (byte)(i & 3);
            Color color = team switch
            {
                0 => new Color(86, 183, 255, 220),
                1 => new Color(255, 185, 86, 220),
                2 => new Color(118, 225, 144, 220),
                _ => new Color(196, 132, 255, 220)
            };
            Rl.DrawRectangle(x, y, 2, 2, color);
        }

        Rl.DrawText("MassNavigation Raylib Frame Benchmark", 32, 36, 26, new Color(236, 244, 252, 255));
        Rl.DrawText($"World={snapshot.WorldWidthCm / 100000f:F1}km x {snapshot.WorldHeightCm / 100000f:F1}km  agentsDrawn={MassNavigationRaylibBenchmarkAgentDrawCount} obstacleBuckets={obstacleBuckets} debugOverlay={debugOverlay}", 32, height - 32, 18, new Color(190, 205, 216, 255));
        Rl.DrawRectangle(mapRight + 30, mapTop, Math.Max(420, width - mapRight - 70), 252, new Color(7, 16, 23, 224));
        Rl.DrawRectangleLines(mapRight + 30, mapTop, Math.Max(420, width - mapRight - 70), 252, new Color(94, 136, 158, 255));
        Rl.DrawText($"Commanded={snapshot.CommandedAgents} Moving={snapshot.MovingAgents} Flow={snapshot.FlowEnabled}", mapRight + 50, mapTop + 34, 20, new Color(236, 244, 252, 255));
        Rl.DrawText($"Macro={snapshot.MacroChunkColumns}x{snapshot.MacroChunkRows} HPA edges={snapshot.ExpectedMacroAdjacencyEdgeCount}", mapRight + 50, mapTop + 68, 18, new Color(198, 212, 224, 255));
        Rl.DrawText($"NavMesh={snapshot.NavMeshBake.BakedChunks}/{snapshot.NavMeshBake.TotalChunks} notLoaded={snapshot.NavMeshBake.NotLoadedChunks}", mapRight + 50, mapTop + 98, 18, new Color(255, 194, 84, 255));
        Rl.DrawText($"Obstacles target/authored/baked/loaded={snapshot.TargetStaticObstacleCount}/{snapshot.AuthoredStaticObstacleCount}/{snapshot.ObstacleDiagnostics.BakedStaticObstacleCount}/{snapshot.ObstacleDiagnostics.LoadedStaticObstacleCount}", mapRight + 50, mapTop + 128, 18, new Color(198, 212, 224, 255));
        Rl.DrawText("Production gate: Raylib framebuffer timing for 10k agents, 40k obstacle buckets and debug overlay.", mapRight + 50, mapTop + 178, 18, new Color(118, 225, 144, 255));
        Rl.DrawText("Thresholds: p95<=10ms, p99<=12.5ms, overlay draw<=0.5ms with loaded summary data.", mapRight + 50, mapTop + 208, 18, new Color(118, 225, 144, 255));
    }

    private static void DrawMassNavigationRaylibBenchmarkOverlay(MassNavigationSnapshot snapshot, int frame)
    {
        int width = Rl.GetScreenWidth();
        int x = Math.Max(820, width - 650);
        int y = 372;
        Rl.DrawRectangle(x, y, 610, 248, new Color(10, 18, 28, 230));
        Rl.DrawRectangleLines(x, y, 610, 248, new Color(95, 126, 148, 255));
        Rl.DrawText("Debug Overlay A/B Probe", x + 18, y + 32, 22, new Color(236, 244, 252, 255));
        Rl.DrawText($"overlayItems={snapshot.DebugVisualDiagnostics.EvidenceOverlayItems} runtimeItems={snapshot.DebugVisualDiagnostics.ScreenOverlayItems}", x + 18, y + 68, 18, new Color(198, 212, 224, 255));
        Rl.DrawText($"pathOnly={snapshot.PathOnlyQuery.Status}/{snapshot.PathOnlyQuery.PathPointCount} reuse={snapshot.OrderReuse.CacheHit}/{snapshot.OrderReuse.FanoutCount}", x + 18, y + 98, 18, new Color(198, 212, 224, 255));
        Rl.DrawText($"layers={snapshot.NavMeshLayerCount} profiles={snapshot.NavMeshProfileCount} strategyRows={snapshot.StrategySwitchDiagnostics.Count}", x + 18, y + 128, 18, new Color(198, 212, 224, 255));
        Rl.DrawText($"frame={frame} zeroRuntimeOverlayWrites={snapshot.DebugVisualDiagnostics.ScreenOverlayItems == 0}", x + 18, y + 158, 18, new Color(118, 225, 144, 255));
        Rl.DrawText("Gate: smoke p95 <= 16.667ms, overlay p95 delta <= 2ms; micro threshold also requires p95<=10ms, p99<=12.5ms, overlay draw<=0.5ms.", x + 18, y + 204, 16, new Color(255, 210, 84, 255));
    }

    private static void DrawMassNavigationRaylibRect(Vector2 center, float widthCm, float heightCm, MassNavigationSnapshot snapshot, int mapLeft, int mapTop, int mapSize, Color color)
    {
        float minX = center.X - (widthCm * 0.5f);
        float maxX = center.X + (widthCm * 0.5f);
        float minY = center.Y - (heightCm * 0.5f);
        float maxY = center.Y + (heightCm * 0.5f);
        float worldMinX = -snapshot.WorldWidthCm * 0.5f;
        float worldMinY = -snapshot.WorldHeightCm * 0.5f;
        int x0 = mapLeft + (int)MathF.Round((minX - worldMinX) / snapshot.WorldWidthCm * mapSize);
        int x1 = mapLeft + (int)MathF.Round((maxX - worldMinX) / snapshot.WorldWidthCm * mapSize);
        int y0 = mapTop + (int)MathF.Round((minY - worldMinY) / snapshot.WorldHeightCm * mapSize);
        int y1 = mapTop + (int)MathF.Round((maxY - worldMinY) / snapshot.WorldHeightCm * mapSize);
        int x = Math.Min(x0, x1);
        int y = Math.Min(y0, y1);
        int w = Math.Max(1, Math.Abs(x1 - x0));
        int h = Math.Max(1, Math.Abs(y1 - y0));
        Rl.DrawRectangleLines(x, y, w, h, color);
    }

    private static MassNavigationAcceptanceResult EvaluateMassNavigationAcceptance(
        IReadOnlyList<MassNavigationSnapshot> timeline,
        MassNavigationSimulationRuntime simulation,
        FrameTimingStats frameStats,
        MassNavigationRaylibFrameBenchmark raylibBenchmark,
        MassNavigationManualUatSignoff manualUat)
    {
        var sceneFailures = new List<string>();
        var productionFailures = new List<string>();
        MassNavigationSnapshot boot = timeline.First(snapshot => snapshot.Step == "000_boot");
        MassNavigationSnapshot afterOrder = timeline.First(snapshot => snapshot.Step == "001_selection_order");
        MassNavigationSnapshot reuseProbe = timeline.First(snapshot => snapshot.Step == "001b_order_reuse_probe");
        MassNavigationSnapshot fullCommandProbe = timeline.First(snapshot => snapshot.Step == "007_10k_commanded_flow_probe");
        MassNavigationSnapshot remote = timeline.First(snapshot => snapshot.Step == "002_remote_minimap_jump");
        MassNavigationSnapshot returned = timeline.First(snapshot => snapshot.Step == "003_return_original_area");

        AddAcceptanceCheck(boot.ActiveMapId == MassNavigationIds.MapId, $"Expected MassNavigation map '{MassNavigationIds.MapId}', got '{boot.ActiveMapId}'.", sceneFailures);
        AddAcceptanceCheck(boot.WorldWidthCm == 6_400_000 && boot.WorldHeightCm == 6_400_000, $"Expected 64km x 64km config, got {boot.WorldWidthCm}x{boot.WorldHeightCm} cm.", sceneFailures);
        AddAcceptanceCheck(boot.TeamCount >= 4, $"Expected at least 4 configured teams, got {boot.TeamCount}.", sceneFailures);
        AddAcceptanceCheck(boot.AgentCount == simulation.AgentsPerTeam * simulation.TeamCount, $"Agent state count mismatch: {boot.AgentCount} vs configured {simulation.AgentsPerTeam * simulation.TeamCount}.", sceneFailures);
        AddAcceptanceCheck(boot.EcsAgentCount == boot.AgentCount, $"ECS controllable agent count mismatch: {boot.EcsAgentCount} vs runtime {boot.AgentCount}.", sceneFailures);
        AddAcceptanceCheck(boot.BlockerCount == simulation.MassFlow.ObstacleCount, $"Blocker count mismatch: {boot.BlockerCount} vs solver {simulation.MassFlow.ObstacleCount}.", sceneFailures);
        AddAcceptanceCheck(boot.HotspotMarkerCount == simulation.HotZones.Length, $"Hotspot marker count mismatch: {boot.HotspotMarkerCount} vs config {simulation.HotZones.Length}.", sceneFailures);
        AddAcceptanceCheck(boot.PerformerPayloadCount >= boot.AgentCount + boot.BlockerCount + boot.HotspotMarkerCount, $"Performer payloads missing: {boot.PerformerPayloadCount} for {boot.AgentCount + boot.BlockerCount + boot.HotspotMarkerCount} MassNavigation owners.", sceneFailures);
        AddAcceptanceCheck(boot.PerformerActiveCount >= boot.AgentCount + boot.BlockerCount + boot.HotspotMarkerCount, $"Performer active count too low: {boot.PerformerActiveCount}.", sceneFailures);
        AddAcceptanceCheck(boot.MinimapVisible, "Core minimap should be visible.", sceneFailures);
        AddAcceptanceCheck(string.Equals(boot.MinimapPreset, MinimapPreset.RtsFullMap.ToString(), StringComparison.Ordinal), $"Expected core minimap RtsFullMap preset, got {boot.MinimapPreset}.", sceneFailures);
        AddAcceptanceCheck(boot.MinimapBufferCount >= boot.AgentCount + boot.BlockerCount + boot.HotspotMarkerCount, $"Minimap marker buffer too low: {boot.MinimapBufferCount}.", sceneFailures);
        AddAcceptanceCheck(boot.MinimapDroppedTotal == 0, $"Minimap markers dropped: {boot.MinimapDroppedTotal}.", sceneFailures);
        AddAcceptanceCheck(afterOrder.SelectedCount > 0, "SelectionRuntime LivePrimary selection was not observed by MassNavigation.", sceneFailures);
        AddAcceptanceCheck(afterOrder.ActiveOrderGroups > 0 || afterOrder.ActiveGroups > 0, "massNavigationMove order did not create an active NavGroup.", sceneFailures);
        AddAcceptanceCheck(afterOrder.CommandRejectsTotal == 0, $"MassNavigation rejected commands unexpectedly: {afterOrder.CommandRejectsTotal}.", sceneFailures);
        AddAcceptanceCheck(Vector2.Distance(remote.CameraTargetCm, boot.CameraTargetCm) > 500_000f, $"Remote minimap jump did not move the camera far enough: boot={FormatPoint(boot.CameraTargetCm)} remote={FormatPoint(remote.CameraTargetCm)}.", sceneFailures);
        AddAcceptanceCheck(returned.AgentCount == boot.AgentCount, $"Returning to original area changed agent count: {boot.AgentCount} -> {returned.AgentCount}.", sceneFailures);
        AddAcceptanceCheck(returned.ScenarioSpawnCount == boot.ScenarioSpawnCount, $"Returning to original area re-ran scenario spawn: {boot.ScenarioSpawnCount} -> {returned.ScenarioSpawnCount}.", sceneFailures);
        AddAcceptanceCheck(returned.SceneResetCount == boot.SceneResetCount, $"Returning to original area reset the scene: {boot.SceneResetCount} -> {returned.SceneResetCount}.", sceneFailures);
        AddAcceptanceCheck(boot.BakeDataBound, "MassNavigation bake/data diagnostics was not bound.", sceneFailures);
        AddAcceptanceCheck(boot.MacroChunkColumns == 256 && boot.MacroChunkRows == 256, $"Expected 256x256 macro chunks, got {boot.MacroChunkColumns}x{boot.MacroChunkRows}.", sceneFailures);
        AddAcceptanceCheck(boot.LoadedChunkCount > 0, $"Expected S1 loaded chunk count to be recorded and positive, got {boot.LoadedChunkCount}.", sceneFailures);
        AddAcceptanceCheck(boot.WorldBoundaryDiagnostics.Available && boot.WorldBoundaryDiagnostics.CameraInBounds, $"S1 world boundary diagnostics must be available and keep camera in bounds; result={boot.WorldBoundaryDiagnostics.BoundaryClickResult}.", sceneFailures);
        AddAcceptanceCheck(
            string.Equals(boot.WorldBoundaryDiagnostics.BoundaryClickResult, "inside_edge_accepted_outside_edge_clamped", StringComparison.Ordinal) &&
            string.Equals(boot.WorldBoundaryDiagnostics.GroundPickingResult, "inside_ground_pick_accepted_outside_ground_pick_clamped", StringComparison.Ordinal),
            $"S1 boundary click/ground picking probe failed: boundary={boot.WorldBoundaryDiagnostics.BoundaryClickResult}, ground={boot.WorldBoundaryDiagnostics.GroundPickingResult}.",
            sceneFailures);
        AddAcceptanceCheck(boot.ExpectedMacroAdjacencyEdgeCount == 130_560, $"Expected 130560 HPA adjacency edges for a 256x256 grid, got {boot.ExpectedMacroAdjacencyEdgeCount}.", sceneFailures);
        AddAcceptanceCheck(boot.NavMeshLayerCount >= 4, $"Expected multi-layer navmesh config for ground/water/air/mountain, got {boot.NavMeshLayerCount}.", sceneFailures);
        AddAcceptanceCheck(boot.NavMeshProfileCount >= 5, $"Expected multiple navmesh profiles, got {boot.NavMeshProfileCount}.", sceneFailures);
        AddAcceptanceCheck(boot.BakeProfiles.Count >= 5, $"Expected pathing agent strategy profiles, got {boot.BakeProfiles.Count}.", sceneFailures);
        AddAcceptanceCheck(boot.TargetStaticObstacleCount >= 40_000, $"Expected static obstacle bake target >= 40000, got {boot.TargetStaticObstacleCount}.", sceneFailures);
        AddAcceptanceCheck(boot.StaticObstacleWorldDiagnostics.WorldDistributionReady && boot.StaticObstacleWorldDiagnostics.PlannedWorldObstacleCount >= 40_000, $"Expected deterministic 40k world obstacle distribution contract, got planned={boot.StaticObstacleWorldDiagnostics.PlannedWorldObstacleCount}.", sceneFailures);
        AddAcceptanceCheck(boot.HpaOverlayRequired && boot.PathInspectorRequired && boot.BakeOverlayRequired, "Bake/HPA/path inspector overlays must be required by MassNavigation config.", sceneFailures);
        AddAcceptanceCheck(boot.HpaMacroDiagnostics.Available && boot.HpaMacroDiagnostics.SampleRouteChunkCount > 0 && boot.HpaMacroDiagnostics.SamplePortalCount > 0, $"Expected HPA macro diagnostics with sample route/portal counts, got route={boot.HpaMacroDiagnostics.SampleRouteChunkCount} portals={boot.HpaMacroDiagnostics.SamplePortalCount}.", sceneFailures);
        AddAcceptanceCheck(boot.HpaGraphDiagnostics.Available && boot.HpaGraphDiagnostics.LoadedTileCount > 0 && boot.HpaGraphDiagnostics.GraphNodeCount > 0, $"Expected active-window HPA graph diagnostics from real NavTiles, got available={boot.HpaGraphDiagnostics.Available} loaded={boot.HpaGraphDiagnostics.LoadedTileCount} nodes={boot.HpaGraphDiagnostics.GraphNodeCount}.", sceneFailures);
        AddAcceptanceCheck(boot.PathOnlyQuery.Available && boot.PathOnlyQuery.NoOrderSubmitted && boot.PathOnlyQuery.PathPointCount > 0, "Path-only query diagnostics must be available without submitting a unit order.", sceneFailures);
        AddAcceptanceCheck(string.Equals(boot.PathOnlyQuery.PreviewMode, "path_preview", StringComparison.Ordinal) &&
            string.Equals(boot.PathOnlyQuery.InputContract, "pick_start_world_point_then_goal_world_point", StringComparison.Ordinal) &&
            string.Equals(boot.PathOnlyQuery.RoutePreviewState, "highlighted_route_ready", StringComparison.Ordinal) &&
            boot.PathOnlyQuery.HighlightRouteVisible &&
            string.Equals(boot.PathOnlyQuery.PathPointContract, "immutable_query_result", StringComparison.Ordinal) &&
            string.Equals(boot.PathOnlyQuery.WaypointContract, "editable_order_intent", StringComparison.Ordinal),
            "Path-only query must expose the player-facing Path Preview contract, highlighted route state, and waypoint/pathpoint semantics.",
            sceneFailures);
        AddAcceptanceCheck(boot.StrategySwitchDiagnostics.Count >= 5, $"Expected strategy switch diagnostics for configured agent profiles, got {boot.StrategySwitchDiagnostics.Count}.", sceneFailures);
        AddAcceptanceCheck(boot.StrategySwitchDiagnostics.Any(strategy => strategy.GraphQueryAvailable), "Expected at least one road graph strategy query to be available.", sceneFailures);
        AddAcceptanceCheck(boot.WaypointPathDiagnostics.WaypointsEditable && boot.WaypointPathDiagnostics.PathPointsImmutable && boot.WaypointPathDiagnostics.PathPointsCanSeedWaypoints, "Waypoint/pathpoint diagnostics must prove editable intent is separate from immutable path results.", sceneFailures);
        AddAcceptanceCheck(reuseProbe.OrderReuse.HasOrder, "Order reuse diagnostics did not observe submitted MassNavigation orders.", sceneFailures);
        AddAcceptanceCheck(reuseProbe.OrderReuse.CacheHit, "Near-point MassNavigation order did not hit the normalized route cache.", sceneFailures);
        AddAcceptanceCheck(reuseProbe.OrderReuse.FanoutCount >= MassNavigationSelectionSampleCount, $"Expected shared order fanout >= {MassNavigationSelectionSampleCount}, got {reuseProbe.OrderReuse.FanoutCount}.", sceneFailures);
        AddAcceptanceCheck(!string.IsNullOrWhiteSpace(reuseProbe.OrderReuse.PathRouteSignature) && !string.Equals(reuseProbe.OrderReuse.PathRouteSignature, "not_available", StringComparison.Ordinal), "Order reuse diagnostics must expose the path route signature used as route context.", sceneFailures);
        AddAcceptanceCheck(!string.IsNullOrWhiteSpace(reuseProbe.OrderReuse.MeshRouteSignature) && !string.Equals(reuseProbe.OrderReuse.MeshRouteSignature, "not_available", StringComparison.Ordinal), "Order reuse diagnostics must expose the active-window mesh route signature used as route context.", sceneFailures);
        AddAcceptanceCheck(string.Equals(reuseProbe.OrderReuse.ReuseScope, "near_point_order_bucket", StringComparison.Ordinal) || string.Equals(reuseProbe.OrderReuse.ReuseScope, "same_point_order_bucket", StringComparison.Ordinal), $"Order reuse scope must identify same/near reuse, got {reuseProbe.OrderReuse.ReuseScope}.", sceneFailures);
        AddAcceptanceCheck(reuseProbe.TargetAllocation.HasAllocation && reuseProbe.TargetAllocation.SlotCount >= MassNavigationSelectionSampleCount, $"Expected target slots for the selected group, got {reuseProbe.TargetAllocation.SlotCount}.", sceneFailures);
        AddAcceptanceCheck(fullCommandProbe.SelectedCount >= MassNavigationFullCommandMinimumAgents, $"Expected full-selection probe to select >= {MassNavigationFullCommandMinimumAgents} agents, got {fullCommandProbe.SelectedCount}.", sceneFailures);
        AddAcceptanceCheck(fullCommandProbe.CommandedAgents >= MassNavigationFullCommandMinimumAgents, $"Expected full-selection probe to command >= {MassNavigationFullCommandMinimumAgents} agents, got {fullCommandProbe.CommandedAgents}.", sceneFailures);
        AddAcceptanceCheck(fullCommandProbe.TargetAllocation.HasAllocation && fullCommandProbe.TargetAllocation.SlotCount >= MassNavigationFullCommandMinimumAgents, $"Expected target slots for full-selection probe, got {fullCommandProbe.TargetAllocation.SlotCount}.", sceneFailures);
        AddAcceptanceCheck(fullCommandProbe.TargetAllocation.ReachableSlotCount >= MassNavigationFullCommandMinimumAgents, $"Expected reachable target slots for full-selection probe, got {fullCommandProbe.TargetAllocation.ReachableSlotCount}.", sceneFailures);
        AddAcceptanceCheck(string.Equals(fullCommandProbe.TargetAllocation.ReachabilityProbeStatus, "Ok", StringComparison.Ordinal), $"Expected U8 reachability smoke status Ok, got {fullCommandProbe.TargetAllocation.ReachabilityProbeStatus}.", sceneFailures);
        AddAcceptanceCheck(fullCommandProbe.TargetAllocation.ReachabilitySource.Contains("path_only_route_reachability_smoke", StringComparison.Ordinal) || fullCommandProbe.TargetAllocation.ReachabilitySource.Contains("active_window_navmesh_query", StringComparison.Ordinal), $"Expected U8 reachability source to cite a path or mesh probe, got {fullCommandProbe.TargetAllocation.ReachabilitySource}.", sceneFailures);
        AddAcceptanceCheck(fullCommandProbe.TargetAllocation.AllocationRouteId > 0 && !string.IsNullOrWhiteSpace(fullCommandProbe.TargetAllocation.AllocationRouteReuseKey), "Expected U8 target allocation to carry the shared route reuse id and normalized key.", sceneFailures);
        AddAcceptanceCheck(!string.IsNullOrWhiteSpace(fullCommandProbe.TargetAllocation.MeshReachabilityStatus) && !string.IsNullOrWhiteSpace(fullCommandProbe.TargetAllocation.MeshReachabilitySource), "Expected U8 target allocation to expose mesh reachability provenance, even when the current profile is only an active-window smoke.", sceneFailures);
        AddAcceptanceCheck(returned.LayerCostDiagnostics.Count >= 5, $"Expected layer/cost diagnostics for ground/water/air/mountain profiles, got {returned.LayerCostDiagnostics.Count}.", sceneFailures);
        AddAcceptanceCheck(timeline.Any(HasMassNavigationRuntimeGuideOverlay), "Playable guided showcase runtime overlay was not captured with Showcase/Player input/Look for/Pass signal/Legend/Expected/Gate text; screenshots cannot replace the playable flow.", sceneFailures);
        foreach (string useCaseId in RequiredMassNavigationRuntimeUseCaseIds())
        {
            AddAcceptanceCheck(
                TimelineHasRuntimeGuideForUseCase(timeline, useCaseId),
                $"Playable guided showcase runtime overlay did not sample {useCaseId}; every U1-U16 case must exist as an in-game guided step, not only as an offline screenshot card.",
                sceneFailures);
        }

        int expectedNavMeshTiles = boot.MacroChunkCount * boot.NavMeshLayerCount * boot.NavMeshProfileCount;
        AddAcceptanceCheck(boot.NavMeshBake.TotalChunks == expectedNavMeshTiles && boot.NavMeshBake.BakedChunks > 0 && boot.NavMeshBake.FailedChunks == 0 && boot.NavMeshBake.MissingChunks == 0 && boot.NavMeshBake.DirtyChunks == 0 && boot.NavMeshBake.NotLoadedChunks == boot.NavMeshBake.TotalChunks - boot.NavMeshBake.BakedChunks, $"Production NavMesh streaming gate failed: baked={boot.NavMeshBake.BakedChunks} failed={boot.NavMeshBake.FailedChunks} missing={boot.NavMeshBake.MissingChunks} dirty={boot.NavMeshBake.DirtyChunks} notLoaded={boot.NavMeshBake.NotLoadedChunks} total={boot.NavMeshBake.TotalChunks} expected={expectedNavMeshTiles}.", productionFailures);
        AddAcceptanceCheck(boot.HpaGraphDiagnostics.Available && boot.HpaGraphDiagnostics.ActiveWindowRouteAvailable && boot.HpaGraphDiagnostics.GraphNodeCount > 0 && boot.HpaGraphDiagnostics.GraphEdgeCount > 0 && boot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount >= 2 && boot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount >= 1, $"Production HPA active-window route gate failed: available={boot.HpaGraphDiagnostics.Available} nodes={boot.HpaGraphDiagnostics.GraphNodeCount} edges={boot.HpaGraphDiagnostics.GraphEdgeCount} route={boot.HpaGraphDiagnostics.ActiveWindowRouteAvailable} portals={boot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount} crossTileSteps={boot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount}.", productionFailures);
        AddAcceptanceCheck(HasRequiredMultiLayerActiveWindowQueries(boot), "Production multi-layer NavMesh query gate failed: ground/water/air/mountain active-window rows were not all Ok with touched tiles.", productionFailures);
        AddAcceptanceCheck(boot.StrategySwitchDiagnostics.Any(strategy => strategy.GraphQueryAvailable && (string.Equals(strategy.GraphStatus, "Ok", StringComparison.OrdinalIgnoreCase) || string.Equals(strategy.GraphStatus, "OkViaPathServiceRouter", StringComparison.OrdinalIgnoreCase))) && boot.StrategySwitchDiagnostics.Any(strategy => strategy.MeshQueryAvailable && string.Equals(strategy.MeshStatus, "Ok", StringComparison.OrdinalIgnoreCase)), "Production RoadGraph/NavMesh/Hybrid strategy gate failed: expected graph and mesh route evidence in the strategy matrix.", productionFailures);
        AddAcceptanceCheck(boot.ObstacleDiagnostics.TargetStaticObstacleCount >= 40_000, $"Production obstacle target too low: {boot.ObstacleDiagnostics.TargetStaticObstacleCount}.", productionFailures);
        AddAcceptanceCheck(boot.ObstacleDiagnostics.AuthoredStaticObstacleCount >= boot.ObstacleDiagnostics.TargetStaticObstacleCount, $"Production obstacle authoring gate failed: authored={boot.ObstacleDiagnostics.AuthoredStaticObstacleCount} target={boot.ObstacleDiagnostics.TargetStaticObstacleCount}.", productionFailures);
        AddAcceptanceCheck(boot.ObstacleDiagnostics.BakedStaticObstacleCount >= boot.ObstacleDiagnostics.TargetStaticObstacleCount, $"Production obstacle bake gate failed: baked={boot.ObstacleDiagnostics.BakedStaticObstacleCount} target={boot.ObstacleDiagnostics.TargetStaticObstacleCount}.", productionFailures);
        AddAcceptanceCheck(boot.ObstacleDiagnostics.LoadedStaticObstacleCount >= boot.ObstacleDiagnostics.TargetStaticObstacleCount, $"Production obstacle load gate failed: loaded={boot.ObstacleDiagnostics.LoadedStaticObstacleCount} target={boot.ObstacleDiagnostics.TargetStaticObstacleCount}.", productionFailures);
        AddAcceptanceCheck(boot.StaticObstacleWorldDiagnostics.WorldDistributionReady && string.Equals(boot.StaticObstacleWorldDiagnostics.RuntimeActivationStrategy, "active_window_subset_to_mass_flow_solver", StringComparison.Ordinal) && boot.ObstacleDiagnostics.SolverActiveStaticObstacleCount > 0 && boot.ObstacleDiagnostics.SolverActiveStaticObstacleCount <= boot.ObstacleDiagnostics.SolverStaticObstacleCapacity, $"Production obstacle streaming gate failed: worldReady={boot.StaticObstacleWorldDiagnostics.WorldDistributionReady} runtimeActivation={boot.StaticObstacleWorldDiagnostics.RuntimeActivationStrategy} solverActive={boot.ObstacleDiagnostics.SolverActiveStaticObstacleCount} capacity={boot.ObstacleDiagnostics.SolverStaticObstacleCapacity}.", productionFailures);
        AddAcceptanceCheck(fullCommandProbe.CommandedAgents >= 10_000, $"Production 10k command gate failed: commandedAgents={fullCommandProbe.CommandedAgents}.", productionFailures);
        AddAcceptanceCheck(fullCommandProbe.MovingAgents + fullCommandProbe.SettledAgents >= 10_000, $"Production 10k movement gate failed: moving={fullCommandProbe.MovingAgents} settled={fullCommandProbe.SettledAgents}.", productionFailures);
        AddAcceptanceCheck(fullCommandProbe.TargetAllocation.SlotCount >= 10_000 && fullCommandProbe.TargetAllocation.ReachableSlotCount >= 10_000 && fullCommandProbe.TargetAllocation.BlockedSlotCount == 0 && fullCommandProbe.TargetAllocation.FallbackSlotCount == 0, $"Production target allocation gate failed: slots={fullCommandProbe.TargetAllocation.SlotCount} reachable={fullCommandProbe.TargetAllocation.ReachableSlotCount} blocked={fullCommandProbe.TargetAllocation.BlockedSlotCount} fallback={fullCommandProbe.TargetAllocation.FallbackSlotCount}.", productionFailures);
        AddAcceptanceCheck(fullCommandProbe.FlowEnabled, "Production flowfield gate failed: flow.enabled is false.", productionFailures);
        AddAcceptanceCheck(raylibBenchmark.Available, $"Raylib framebuffer benchmark did not run: {raylibBenchmark.Notes}", sceneFailures);
        AddAcceptanceCheck(raylibBenchmark.SmokePassed, $"Raylib framebuffer smoke benchmark failed: debugOn p95={raylibBenchmark.DebugOn.P95Ms:F3}ms p99={raylibBenchmark.DebugOn.P99Ms:F3}ms overlayDelta={raylibBenchmark.OverlayP95DeltaMs:F3}ms deltaPercent={raylibBenchmark.FpsDeltaPercent:F2}%.", sceneFailures);
        AddAcceptanceCheck(raylibBenchmark.ProductionPassed && raylibBenchmark.FullGameRendererLoadedDataMeasured, $"Production renderer FPS gate failed: rendererScope={raylibBenchmark.RendererScope}, fullGameRendererLoadedDataMeasured={raylibBenchmark.FullGameRendererLoadedDataMeasured}, Raylib micro debugOn p50/p95/p99={raylibBenchmark.DebugOn.P50Ms:F3}/{raylibBenchmark.DebugOn.P95Ms:F3}/{raylibBenchmark.DebugOn.P99Ms:F3}ms, debugOff p95={raylibBenchmark.DebugOff.P95Ms:F3}ms, overlayDelta={raylibBenchmark.OverlayP95DeltaMs:F3}ms, deltaPercent={raylibBenchmark.FpsDeltaPercent:F2}%.", productionFailures);
        AddAcceptanceCheck(raylibBenchmark.ProductionPassed && fullCommandProbe.DebugVisualDiagnostics.ScreenOverlayItems == 0, $"Production debug overlay cost gate failed: rendererScope={raylibBenchmark.RendererScope}, overlayDrawMs={raylibBenchmark.OverlayDrawMs:F3}, overlayDelta={raylibBenchmark.OverlayP95DeltaMs:F3}ms, runtimeOverlayItems={fullCommandProbe.DebugVisualDiagnostics.ScreenOverlayItems}; still requires full-scene alloc/frame and zero-write counters.", productionFailures);

        bool machineProductionEvidencePassed = productionFailures.Count == 0;
        if (!manualUat.Accepted)
        {
            productionFailures.Add(manualUat.Blocker);
        }

        string normalizedSignature = string.Join("|", new[]
        {
            "mass_navigation_large_world",
            $"agents:{boot.AgentCount}",
            $"commanded:{fullCommandProbe.CommandedAgents}",
            $"moving:{fullCommandProbe.MovingAgents}",
            $"teams:{boot.TeamCount}",
            $"macro:{boot.MacroChunkColumns}x{boot.MacroChunkRows}",
            $"navmesh:{boot.NavMeshBake.BakedChunks}/{boot.NavMeshBake.TotalChunks}/{boot.NavMeshBake.NotLoadedChunks}",
            $"pathOnly:{boot.PathOnlyQuery.Status}/{boot.PathOnlyQuery.PathPointCount}",
            $"reuse:{reuseProbe.OrderReuse.CacheHit}/{reuseProbe.OrderReuse.FanoutCount}/{reuseProbe.OrderReuse.RouteCacheSize}",
            $"slots:{reuseProbe.TargetAllocation.SlotCount}/{reuseProbe.TargetAllocation.SelectedCount}",
            $"prodMachine:{machineProductionEvidencePassed}",
            $"manualUat:{manualUat.Accepted}",
            $"performers:{boot.PerformerActiveCount}",
            $"markers:{boot.MinimapBufferCount}/{boot.MinimapDroppedTotal}",
            $"remote:{MathF.Round(remote.CameraTargetCm.X):F0},{MathF.Round(remote.CameraTargetCm.Y):F0}",
            $"spawns:{boot.ScenarioSpawnCount}->{returned.ScenarioSpawnCount}",
            $"resets:{boot.SceneResetCount}->{returned.SceneResetCount}"
        });

        bool sceneSmokeSuccess = sceneFailures.Count == 0;
        bool productionGateSuccess = machineProductionEvidencePassed && manualUat.Accepted;
        var allFailures = sceneFailures.Concat(productionFailures).ToArray();
        string verdict = sceneSmokeSuccess
            ? $"MassNavigation passes scene smoke with {boot.AgentCount} agents, {boot.PerformerActiveCount} performers and {boot.MinimapBufferCount} minimap markers; machine production evidence {(machineProductionEvidencePassed ? "passes" : "fails")}; manual UAT {(manualUat.Accepted ? "signed off" : "missing")}."
            : "MassNavigation large-world scene smoke failed.";

        return new MassNavigationAcceptanceResult(
            Success: sceneSmokeSuccess,
            SceneSmokeSuccess: sceneSmokeSuccess,
            ProductionGateSuccess: productionGateSuccess,
            MachineProductionEvidenceSuccess: machineProductionEvidencePassed,
            ManualUatAccepted: manualUat.Accepted,
            ManualUatEvidencePath: manualUat.EvidencePath,
            ManualUatBlocker: manualUat.Accepted ? string.Empty : manualUat.Blocker,
            Verdict: verdict,
            FailureSummary: allFailures.Length == 0 ? verdict : string.Join(Environment.NewLine, allFailures),
            SceneSmokeFailedChecks: sceneFailures,
            ProductionGateFailedChecks: productionFailures,
            FailedChecks: allFailures,
            NormalizedSignature: normalizedSignature);
    }

    private static string BuildMassNavigationBattleReport(
        LauncherRecordingRequest request,
        IReadOnlyList<MassNavigationSnapshot> timeline,
        IReadOnlyList<CaptureFrame> captureFrames,
        IReadOnlyList<double> frameTimesMs,
        MassNavigationRaylibFrameBenchmark raylibBenchmark,
        MassNavigationAcceptanceResult acceptance)
    {
        MassNavigationSnapshot boot = timeline[0];
        MassNavigationSnapshot final = timeline[^1];
        MassNavigationSnapshot reuseProbe = timeline.FirstOrDefault(snapshot => snapshot.Step == "001b_order_reuse_probe");
        if (reuseProbe.Step == null)
        {
            reuseProbe = final;
        }

        MassNavigationSnapshot fullCommandProbe = timeline.FirstOrDefault(snapshot => snapshot.Step == "007_10k_commanded_flow_probe");
        if (fullCommandProbe.Step == null)
        {
            fullCommandProbe = final;
        }

        double medianTickMs = Median(frameTimesMs.ToArray());
        double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
        FrameTimingStats frameStats = BuildFrameTimingStats(frameTimesMs);
        IReadOnlyList<MassNavigationUseCaseStatus> useCases = BuildMassNavigationUseCaseStatuses(timeline, frameStats, raylibBenchmark, acceptance.ManualUatAccepted);
        string evidenceImages = string.Join(", ", captureFrames.Select(frame => $"`screens/{frame.FileName}`").Append("`screens/timeline.png`"));

        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: mass-navigation-large-world");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: verify MassNavigation is the massflow SSOT and runs through performer + core minimap on a 64km RTS map.");
        sb.AppendLine("- Gameplay domain: real launcher bootstrap, template spawn receipts, SelectionRuntime, OrderBuffer, performer runtime and core MinimapRuntime.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Map: `mods/capabilities/navigation/MassNavigationMod/assets/Maps/mass_navigation.json`");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Launch command: `{request.CommandText}`");
        sb.AppendLine($"- Evidence images: {evidenceImages}");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Boot the real MassNavigation launcher preset and wait for MassNavigation spawn receipts to bind.");
        sb.AppendLine("2. Write LivePrimary selection through SelectionRuntime and submit a `massNavigationMove` order through OrderBufferSystem.");
        sb.AppendLine("3. Jump the core minimap camera to a remote 64km hot-zone landmark, then jump back to the original area.");
        sb.AppendLine("4. Fail if units are recreated/reset, performer payloads are missing, minimap markers drop, or core minimap is not the visible RTS full-map preset.");
        sb.AppendLine("5. Submit a full-controllable 10k shared move order and capture the commanded/flow probe separately from the small reuse probe.");
        sb.AppendLine("6. Capture bake/HPA/path strategy inspector frames so a reviewer can see data-bake health without opening a stale Web editor.");
        sb.AppendLine("7. Run the Raylib framebuffer micro-benchmark and capture the debug overlay A/B frame.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (MassNavigationSnapshot snapshot in timeline)
        {
            sb.AppendLine($"- [{snapshot.Step}] camera={FormatPoint(snapshot.CameraTargetCm)} agents={snapshot.AgentCount} commanded={snapshot.CommandedAgents} moving={snapshot.MovingAgents} teams={snapshot.TeamCount} selected={snapshot.SelectedCount} groups={snapshot.ActiveGroups}/{snapshot.ActiveOrderGroups} performers={snapshot.PerformerActiveCount} minimap={snapshot.MinimapVisibleMarkerCount}/{snapshot.MinimapMarkerCount} loadedChunks={snapshot.LoadedChunkCount} frame={snapshot.FrameMs:F3}ms sim={snapshot.SimulationMs:F3}ms pres={snapshot.PresentationMs:F3}ms mass_navigation={snapshot.MassNavigationMs:F3}ms");
        }

        sb.AppendLine();
        sb.AppendLine("## Acceptance Layers");
        sb.AppendLine($"- scene_smoke_success: `{acceptance.SceneSmokeSuccess}`");
        sb.AppendLine($"- production_gate_success: `{acceptance.ProductionGateSuccess}`");
        sb.AppendLine($"- machine_production_evidence_success: `{acceptance.MachineProductionEvidenceSuccess}`");
        sb.AppendLine($"- manual_uat_accepted: `{acceptance.ManualUatAccepted}`");
        sb.AppendLine("- `success` in `summary.json` is intentionally scoped to scene smoke. Production is reported separately and must not be inferred from scene smoke.");
        sb.AppendLine("- Replay/smoke evidence is not a human UAT signoff; production PASS requires the explicit manual UAT evidence file.");
        sb.AppendLine();
        sb.AppendLine("## Bake/Data Diagnostics");
        sb.AppendLine($"- bound: `{boot.BakeDataBound}`");
        sb.AppendLine($"- macro chunks: `{boot.MacroChunkColumns} x {boot.MacroChunkRows}` = `{boot.MacroChunkCount}`");
        sb.AppendLine($"- macro chunk size: `{boot.MacroChunkSizeXCm} x {boot.MacroChunkSizeYCm}` cm");
        sb.AppendLine($"- HPA adjacency edges: `{boot.ExpectedMacroAdjacencyEdgeCount}`");
        sb.AppendLine($"- navmesh tiles: baked=`{boot.NavMeshBake.BakedChunks}` failed=`{boot.NavMeshBake.FailedChunks}` missing=`{boot.NavMeshBake.MissingChunks}` dirty=`{boot.NavMeshBake.DirtyChunks}` notLoaded=`{boot.NavMeshBake.NotLoadedChunks}` total=`{boot.NavMeshBake.TotalChunks}` coverage=`{boot.NavMeshBake.CoveragePercent}%`");
        sb.AppendLine($"- roadGraph/flow/staticObstacle coverage: `{boot.RoadGraphBake.CoveragePercent}%` / `{boot.FlowFieldBake.CoveragePercent}%` / `{boot.StaticObstacleBake.CoveragePercent}%`");
        sb.AppendLine($"- navmesh config: layers=`{boot.NavMeshLayerCount}` profiles=`{boot.NavMeshProfileCount}` areaCosts=`{boot.NavMeshAreaCostCount}`");
        sb.AppendLine($"- HPA macro diagnostics: available=`{boot.HpaMacroDiagnostics.Available}` macro=`{boot.HpaMacroDiagnostics.MacroChunkColumns}x{boot.HpaMacroDiagnostics.MacroChunkRows}` route=`{boot.HpaMacroDiagnostics.StartMacroChunkX},{boot.HpaMacroDiagnostics.StartMacroChunkY}->{boot.HpaMacroDiagnostics.GoalMacroChunkX},{boot.HpaMacroDiagnostics.GoalMacroChunkY}` routeChunks=`{boot.HpaMacroDiagnostics.SampleRouteChunkCount}` portals=`{boot.HpaMacroDiagnostics.SamplePortalCount}` source=`{boot.HpaMacroDiagnostics.RouteSource}` activeWindowPortalGraph=`{boot.HpaGraphDiagnostics.ActiveWindowRouteAvailable}` gap=`{boot.HpaMacroDiagnostics.ProductionGap}`");
        sb.AppendLine($"- HPA active-window graph diagnostics: available=`{boot.HpaGraphDiagnostics.Available}` window=`{boot.HpaGraphDiagnostics.ActiveWindowMinChunkX},{boot.HpaGraphDiagnostics.ActiveWindowMinChunkY}->{boot.HpaGraphDiagnostics.ActiveWindowMaxChunkX},{boot.HpaGraphDiagnostics.ActiveWindowMaxChunkY}` loadedTiles=`{boot.HpaGraphDiagnostics.LoadedTileCount}/{boot.HpaGraphDiagnostics.ActiveWindowChunkCount}` portalNodes=`{boot.HpaGraphDiagnostics.GraphNodeCount}` graphEdges=`{boot.HpaGraphDiagnostics.GraphEdgeCount}` crossTileEdges=`{boot.HpaGraphDiagnostics.CrossTileEdgeCount}` routeAvailable=`{boot.HpaGraphDiagnostics.ActiveWindowRouteAvailable}` routePortals=`{boot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount}` routeCrossTileSteps=`{boot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount}` route=`{boot.HpaGraphDiagnostics.RouteStartChunkX},{boot.HpaGraphDiagnostics.RouteStartChunkY}:{boot.HpaGraphDiagnostics.RouteStartPortalIndex}->{boot.HpaGraphDiagnostics.RouteGoalChunkX},{boot.HpaGraphDiagnostics.RouteGoalChunkY}:{boot.HpaGraphDiagnostics.RouteGoalPortalIndex}` source=`{boot.HpaGraphDiagnostics.Source}` proof=`{boot.HpaGraphDiagnostics.Gap}`");
        sb.AppendLine($"- static obstacles: target=`{boot.ObstacleDiagnostics.TargetStaticObstacleCount}` authored=`{boot.ObstacleDiagnostics.AuthoredStaticObstacleCount}` baked=`{boot.ObstacleDiagnostics.BakedStaticObstacleCount}` loaded=`{boot.ObstacleDiagnostics.LoadedStaticObstacleCount}` solverActive=`{boot.ObstacleDiagnostics.SolverActiveStaticObstacleCount}` solverCapacity=`{boot.ObstacleDiagnostics.SolverStaticObstacleCapacity}`");
        sb.AppendLine($"- 40k obstacle world contract: dataSource=`{boot.StaticObstacleWorldDiagnostics.DataSource}` planned=`{boot.StaticObstacleWorldDiagnostics.PlannedWorldObstacleCount}` macroCoverage=`{boot.StaticObstacleWorldDiagnostics.MacroChunkCoverageCount}` activeWindow=`{boot.StaticObstacleWorldDiagnostics.ActiveWindowLoadedCount}/{boot.StaticObstacleWorldDiagnostics.SolverStaticObstacleCapacity}` seed=`{boot.StaticObstacleWorldDiagnostics.DeterministicSeed}` strategy=`{boot.StaticObstacleWorldDiagnostics.DistributionStrategy}` runtimeActivation=`{boot.StaticObstacleWorldDiagnostics.RuntimeActivationStrategy}` buckets=`{boot.StaticObstacleWorldDiagnostics.SampleChunkBuckets}`");
        sb.AppendLine($"- inspector frames: `screens/004_bake_hpa_overlay.png`, `screens/005_path_strategy_inspector.png`, `screens/006_order_reuse_target_allocation.png`, `screens/008_acceptance_gate_matrix.png`");
        sb.AppendLine("- playable guided showcase overlay: the real runtime overlay was sampled during evidence recording; it must contain `Showcase`, `Player input`, `Look for`, `Pass signal`, `Legend`, `Expected`, and `Gate`.");
        foreach (MassNavigationSnapshot guideSnapshot in timeline.Where(HasMassNavigationRuntimeGuideOverlay))
        {
            string overlaySummary = string.Join(" | ", guideSnapshot.OverlayLines
                .Where(line =>
                    line.Contains("Showcase", StringComparison.Ordinal) ||
                    line.Contains("Player input:", StringComparison.Ordinal) ||
                    line.Contains("Look for:", StringComparison.Ordinal) ||
                    line.Contains("Pass signal:", StringComparison.Ordinal) ||
                    line.Contains("Legend:", StringComparison.Ordinal) ||
                    line.Contains("Expected:", StringComparison.Ordinal) ||
                    line.Contains("Gate:", StringComparison.Ordinal))
                .Take(8));
            sb.AppendLine($"- runtime guide `{guideSnapshot.Step}`: {overlaySummary}");
        }

        sb.AppendLine($"- path preview: mode=`{boot.PathOnlyQuery.PreviewMode}` input=`{boot.PathOnlyQuery.InputContract}` state=`{boot.PathOnlyQuery.RoutePreviewState}` highlight=`{boot.PathOnlyQuery.HighlightRouteVisible}` status=`{boot.PathOnlyQuery.Status}` noOrderSubmitted=`{boot.PathOnlyQuery.NoOrderSubmitted}` source=`{boot.PathOnlyQuery.QuerySource}` provenance=`{boot.PathOnlyQuery.RouteProvenance}` strategy=`{boot.PathOnlyQuery.Strategy}` layer=`{boot.PathOnlyQuery.Layer}` waypoints=`{boot.PathOnlyQuery.WaypointCount}` pathpoints=`{boot.PathOnlyQuery.PathPointCount}` expanded=`{boot.PathOnlyQuery.ExpandedNodeCount}` error=`{boot.PathOnlyQuery.ErrorCode}` touchedTiles=`{boot.PathOnlyQuery.TouchedTileCount}` portals=`{boot.PathOnlyQuery.CorridorPortalCount}` travelCost=`{boot.PathOnlyQuery.TravelCost:F1}` macroRouteChunks=`{boot.PathOnlyQuery.MacroRouteChunkCount}`");
        sb.AppendLine($"- waypoint/pathpoint contract: waypointsEditable=`{boot.WaypointPathDiagnostics.WaypointsEditable}` pathpointsImmutable=`{boot.WaypointPathDiagnostics.PathPointsImmutable}` seedWaypoints=`{boot.WaypointPathDiagnostics.PathPointsCanSeedWaypoints}` source=`{boot.WaypointPathDiagnostics.Source}`");
        foreach (MassNavigationStrategySwitchDiagnostics strategy in boot.StrategySwitchDiagnostics)
        {
            sb.AppendLine($"- strategy switch `{strategy.AgentTypeId}`: requested=`{strategy.RequestedMode}` selected=`{strategy.SelectedStrategy}` graph=`{strategy.GraphStatus}/{strategy.GraphPathPointCount}/{strategy.GraphTravelCost:F0}` mesh=`{strategy.MeshStatus}/{strategy.MeshPathPointCount}/{strategy.MeshTravelCost:F0}` meshSource=`{strategy.MeshQuerySource}` meshRoute=`{strategy.MeshStartChunkX},{strategy.MeshStartChunkY}->{strategy.MeshGoalChunkX},{strategy.MeshGoalChunkY}` meshTouchedTiles=`{strategy.MeshTouchedTileCount}` routeId=`{strategy.RouteId}`");
        }

        foreach (MassNavigationLayerQueryMatrixRow row in BuildLayerQueryMatrix(boot))
        {
            sb.AppendLine($"- layer query `{row.AgentTypeId}`: layer=`{row.Layer}` profile=`{row.NavProfileId}` costs=`{row.AreaCostSamples}` requested=`{row.RequestedMode}` selected=`{row.SelectedStrategy}` graph=`{row.GraphStatus}` mesh=`{row.MeshStatus}`");
        }

        sb.AppendLine($"- order reuse: key=`{reuseProbe.OrderReuse.NormalizedKey}` cacheHit=`{reuseProbe.OrderReuse.CacheHit}` routeId=`{reuseProbe.OrderReuse.ReusedRouteId}` cacheSize=`{reuseProbe.OrderReuse.RouteCacheSize}` fanout=`{reuseProbe.OrderReuse.FanoutCount}` samePointReuse=`{reuseProbe.OrderReuse.SamePointReuseCount}` nearPointReuse=`{reuseProbe.OrderReuse.NearPointReuseCount}` scope=`{reuseProbe.OrderReuse.ReuseScope}` pathSignature=`{reuseProbe.OrderReuse.PathRouteSignature}` pathSource=`{reuseProbe.OrderReuse.PathRouteSource}` meshSignature=`{reuseProbe.OrderReuse.MeshRouteSignature}` meshStatus=`{reuseProbe.OrderReuse.MeshRouteStatus}` meshSource=`{reuseProbe.OrderReuse.MeshRouteSource}` strategy=`{reuseProbe.OrderReuse.Strategy}` productionProof=`{reuseProbe.OrderReuse.ProductionGap}`");
        sb.AppendLine($"- target allocation: selected=`{fullCommandProbe.TargetAllocation.SelectedCount}` slots=`{fullCommandProbe.TargetAllocation.SlotCount}` reachable=`{fullCommandProbe.TargetAllocation.ReachableSlotCount}` reachability=`{fullCommandProbe.TargetAllocation.ReachabilityProbeStatus}` source=`{fullCommandProbe.TargetAllocation.ReachabilitySource}` routeId=`{fullCommandProbe.TargetAllocation.AllocationRouteId}` routeKey=`{fullCommandProbe.TargetAllocation.AllocationRouteReuseKey}` mesh=`{fullCommandProbe.TargetAllocation.MeshReachabilityStatus}/{fullCommandProbe.TargetAllocation.MeshReachabilitySource}` meshTouchedTiles=`{fullCommandProbe.TargetAllocation.MeshReachabilityTouchedTileCount}` blocked=`{fullCommandProbe.TargetAllocation.BlockedSlotCount}` fallback=`{fullCommandProbe.TargetAllocation.FallbackSlotCount}` blockedReasons=`{fullCommandProbe.TargetAllocation.BlockedReasonSummary}` fallbackReasons=`{fullCommandProbe.TargetAllocation.FallbackReasonSummary}` formation=`{fullCommandProbe.TargetAllocation.FormationMode}` destination=`{FormatPoint(fullCommandProbe.TargetAllocation.DestinationWorldCm)}` productionProof=`{fullCommandProbe.TargetAllocation.ProductionGap}`");
        sb.AppendLine($"- 10k load fields: selected=`{fullCommandProbe.SelectedCount}` commanded=`{fullCommandProbe.CommandedAgents}` moving=`{fullCommandProbe.MovingAgents}` settled=`{fullCommandProbe.SettledAgents}` pendingEntitySync=`{fullCommandProbe.PendingEntitySyncCount}` flowEnabled=`{fullCommandProbe.FlowEnabled}`");
        sb.AppendLine($"- debug visual smoke: runtimeOverlayItems=`{final.DebugVisualDiagnostics.ScreenOverlayItems}` evidenceOverlayItems=`{final.DebugVisualDiagnostics.EvidenceOverlayItems}` overlayDrawMs=`{final.DebugVisualDiagnostics.ScreenOverlayDrawMs:F3}` rebuiltLanes=`{final.DebugVisualDiagnostics.ScreenOverlayRebuiltLanes}` debugDrawMs=`{final.DebugVisualDiagnostics.DebugDrawRenderMs:F3}` nativeHudMs=`{final.DebugVisualDiagnostics.NativeDiagnosticHudMs:F3}` source=`{final.DebugVisualDiagnostics.Source}`");
        sb.AppendLine($"- Raylib framebuffer benchmark: available=`{raylibBenchmark.Available}` smoke=`{raylibBenchmark.SmokePassed}` production=`{raylibBenchmark.ProductionPassed}` microProductionThreshold=`{raylibBenchmark.MicroBenchmarkProductionThresholdPassed}` scope=`{raylibBenchmark.RendererScope}` fullLoadedData=`{raylibBenchmark.FullGameRendererLoadedDataMeasured}` debugOffP95=`{raylibBenchmark.DebugOff.P95Ms:F3}` debugOnP95=`{raylibBenchmark.DebugOn.P95Ms:F3}` debugOnP99=`{raylibBenchmark.DebugOn.P99Ms:F3}` fpsP95=`{raylibBenchmark.DebugOn.FpsP95:F1}` overlayDeltaMs=`{raylibBenchmark.OverlayP95DeltaMs:F3}` delta=`{raylibBenchmark.FpsDeltaPercent:F2}%` overlayDrawMs=`{raylibBenchmark.OverlayDrawMs:F3}` screenshot=`screens/009_raylib_frame_benchmark.png`");

        sb.AppendLine();
        sb.AppendLine("## U1-U16 Use-Case Matrix");
        sb.AppendLine("| Case | Showcase | Production | Evidence | Acceptance proof |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (MassNavigationUseCaseStatus useCase in useCases)
        {
            sb.AppendLine($"| {useCase.Id} {useCase.Name} | {useCase.ShowcaseStatus} | {useCase.ProductionStatus} | {useCase.Evidence} | {useCase.AcceptanceProof} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")} (scene smoke)");
        sb.AppendLine($"- production_gate_success: {(acceptance.ProductionGateSuccess ? "yes" : "no")}");
        sb.AppendLine($"- machine_production_evidence_success: {(acceptance.MachineProductionEvidenceSuccess ? "yes" : "no")}");
        sb.AppendLine($"- manual_uat_accepted: {(acceptance.ManualUatAccepted ? "yes" : "no")}");
        if (!acceptance.ManualUatAccepted)
        {
            sb.AppendLine($"- manual-uat-blocker: {acceptance.ManualUatBlocker}");
        }
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        foreach (string failedCheck in acceptance.SceneSmokeFailedChecks)
        {
            sb.AppendLine($"- scene-smoke-failed-check: {failedCheck}");
        }

        foreach (string failedCheck in acceptance.ProductionGateFailedChecks)
        {
            sb.AppendLine($"- production-gate-failed-check: {failedCheck}");
        }

        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- world: `{boot.WorldWidthCm} x {boot.WorldHeightCm}` cm");
        sb.AppendLine($"- S1 world bounds: `{boot.WorldBoundaryDiagnostics.WorldMinXCm},{boot.WorldBoundaryDiagnostics.WorldMinYCm}` -> `{boot.WorldBoundaryDiagnostics.WorldMaxXCm},{boot.WorldBoundaryDiagnostics.WorldMaxYCm}` cm");
        sb.AppendLine($"- S1 loaded chunks: boot=`{boot.LoadedChunkCount}` final=`{final.LoadedChunkCount}`");
        sb.AppendLine($"- S1 boundary click result: `{boot.WorldBoundaryDiagnostics.BoundaryClickResult}` source=`{boot.WorldBoundaryDiagnostics.Source}`");
        sb.AppendLine($"- S1 ground picking result: `{boot.WorldBoundaryDiagnostics.GroundPickingResult}` insideProbe=`{boot.WorldBoundaryDiagnostics.InsideProbeWorldCm}` outsideProbe=`{boot.WorldBoundaryDiagnostics.OutsideProbeWorldCm}` clamped=`{boot.WorldBoundaryDiagnostics.OutsideClampedWorldCm}`");
        sb.AppendLine($"- agents: `{boot.AgentCount}`");
        sb.AppendLine($"- blockers: `{boot.BlockerCount}`");
        sb.AppendLine($"- hotspot markers: `{boot.HotspotMarkerCount}`");
        sb.AppendLine($"- performer active at boot: `{boot.PerformerActiveCount}`");
        sb.AppendLine($"- minimap markers at boot: `{boot.MinimapBufferCount}` droppedTotal=`{boot.MinimapDroppedTotal}`");
        sb.AppendLine($"- scenario spawn count boot/final: `{boot.ScenarioSpawnCount}` / `{final.ScenarioSpawnCount}`");
        sb.AppendLine($"- scene reset count boot/final: `{boot.SceneResetCount}` / `{final.SceneResetCount}`");
        sb.AppendLine($"- headless tick frame count: `{frameStats.FrameCount}`");
        sb.AppendLine($"- headless tick p50/p95/p99/max: `{frameStats.P50Ms:F3}` / `{frameStats.P95Ms:F3}` / `{frameStats.P99Ms:F3}` / `{frameStats.MaxMs:F3}` ms");
        sb.AppendLine($"- Raylib framebuffer p50/p95/p99: `{raylibBenchmark.DebugOn.P50Ms:F3}` / `{raylibBenchmark.DebugOn.P95Ms:F3}` / `{raylibBenchmark.DebugOn.P99Ms:F3}` ms");
        sb.AppendLine("- FPS note: Raylib framebuffer benchmark is the production FPS/debug-budget gate for this showcase; it renders the 10k-agent/40k-obstacle diagnostic scene with loaded summary data.");
        sb.AppendLine($"- normalized signature: `{acceptance.NormalizedSignature}`");
        sb.AppendLine("- reusable wiring: `RuntimeEntitySpawnQueue`, `RuntimeEntitySpawnReceiptQueue`, `SelectionRuntime`, `OrderBufferSystem`, `PerformerEntityRuntime`, `MinimapRuntime`, `PresentationTimingDiagnostics`, `NavBakeDiagnosticsLoader`, `MassNavigationBakeDataDiagnostics`");
        return sb.ToString();
    }

    private static string BuildMassNavigationTraceJsonl(string adapterId, IReadOnlyList<MassNavigationSnapshot> timeline)
    {
        var lines = new List<string>(timeline.Count);
        for (int index = 0; index < timeline.Count; index++)
        {
            MassNavigationSnapshot snapshot = timeline[index];
            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"mass_navigation-{adapterId}-{index + 1:000}",
                tick = snapshot.Tick,
                step = snapshot.Step,
                map = snapshot.ActiveMapId,
                camera_x_cm = Math.Round(snapshot.CameraTargetCm.X, 2),
                camera_y_cm = Math.Round(snapshot.CameraTargetCm.Y, 2),
                world_width_cm = snapshot.WorldWidthCm,
                world_height_cm = snapshot.WorldHeightCm,
                agents = snapshot.AgentCount,
                ecs_agents = snapshot.EcsAgentCount,
                commanded_agents = snapshot.CommandedAgents,
                moving_agents = snapshot.MovingAgents,
                settled_agents = snapshot.SettledAgents,
                pending_entity_sync = snapshot.PendingEntitySyncCount,
                flow_enabled = snapshot.FlowEnabled,
                teams = snapshot.TeamCount,
                selected = snapshot.SelectedCount,
                groups = snapshot.ActiveGroups,
                order_groups = snapshot.ActiveOrderGroups,
                blockers = snapshot.BlockerCount,
                hotspots = snapshot.HotspotMarkerCount,
                performers = snapshot.PerformerActiveCount,
                performer_payloads = snapshot.PerformerPayloadCount,
                minimap_visible = snapshot.MinimapVisible,
                minimap_preset = snapshot.MinimapPreset,
                minimap_markers = snapshot.MinimapMarkerCount,
                minimap_visible_markers = snapshot.MinimapVisibleMarkerCount,
                minimap_buffer_markers = snapshot.MinimapBufferCount,
                minimap_dropped_total = snapshot.MinimapDroppedTotal,
                loaded_chunks = snapshot.LoadedChunkCount,
                loaded_chunk_count = snapshot.LoadedChunkCount,
                boundary_click_result = snapshot.WorldBoundaryDiagnostics.BoundaryClickResult,
                ground_picking_result = snapshot.WorldBoundaryDiagnostics.GroundPickingResult,
                world_boundary = new
                {
                    snapshot.WorldBoundaryDiagnostics.Available,
                    snapshot.WorldBoundaryDiagnostics.Source,
                    min_x_cm = snapshot.WorldBoundaryDiagnostics.WorldMinXCm,
                    min_y_cm = snapshot.WorldBoundaryDiagnostics.WorldMinYCm,
                    max_x_cm = snapshot.WorldBoundaryDiagnostics.WorldMaxXCm,
                    max_y_cm = snapshot.WorldBoundaryDiagnostics.WorldMaxYCm,
                    snapshot.WorldBoundaryDiagnostics.CameraInBounds,
                    snapshot.WorldBoundaryDiagnostics.MinimapBoundaryClickInBounds,
                    snapshot.WorldBoundaryDiagnostics.MinimapBoundaryClickClamped,
                    snapshot.WorldBoundaryDiagnostics.GroundPickingInsideAccepted,
                    snapshot.WorldBoundaryDiagnostics.GroundPickingOutsideClamped,
                    inside_probe = new { x_cm = snapshot.WorldBoundaryDiagnostics.InsideProbeWorldCm.X, y_cm = snapshot.WorldBoundaryDiagnostics.InsideProbeWorldCm.Y },
                    outside_probe = new { x_cm = snapshot.WorldBoundaryDiagnostics.OutsideProbeWorldCm.X, y_cm = snapshot.WorldBoundaryDiagnostics.OutsideProbeWorldCm.Y },
                    outside_clamped = new { x_cm = snapshot.WorldBoundaryDiagnostics.OutsideClampedWorldCm.X, y_cm = snapshot.WorldBoundaryDiagnostics.OutsideClampedWorldCm.Y }
                },
                bake_data_bound = snapshot.BakeDataBound,
                macro_chunks = $"{snapshot.MacroChunkColumns}x{snapshot.MacroChunkRows}",
                hpa_edges = snapshot.ExpectedMacroAdjacencyEdgeCount,
                navmesh_baked = snapshot.NavMeshBake.BakedChunks,
                navmesh_failed = snapshot.NavMeshBake.FailedChunks,
                navmesh_missing = snapshot.NavMeshBake.MissingChunks,
                navmesh_dirty = snapshot.NavMeshBake.DirtyChunks,
                navmesh_not_loaded = snapshot.NavMeshBake.NotLoadedChunks,
                navmesh_total = snapshot.NavMeshBake.TotalChunks,
                navmesh_coverage = snapshot.NavMeshBake.CoveragePercent,
                pathing_profiles = snapshot.BakeProfiles.Count,
                path_only_status = snapshot.PathOnlyQuery.Status,
                path_only_no_order_submitted = snapshot.PathOnlyQuery.NoOrderSubmitted,
                path_only_waypoints = snapshot.PathOnlyQuery.WaypointCount,
                path_only_pathpoints = snapshot.PathOnlyQuery.PathPointCount,
                path_only_macro_route_chunks = snapshot.PathOnlyQuery.MacroRouteChunkCount,
                waypoint_editable = snapshot.WaypointPathDiagnostics.WaypointsEditable,
                pathpoint_immutable = snapshot.WaypointPathDiagnostics.PathPointsImmutable,
                pathpoints_can_seed_waypoints = snapshot.WaypointPathDiagnostics.PathPointsCanSeedWaypoints,
                strategy_switch_count = snapshot.StrategySwitchDiagnostics.Count,
                strategy_switch_selected = string.Join(",", snapshot.StrategySwitchDiagnostics.Select(item => $"{item.AgentTypeId}:{item.SelectedStrategy}")),
                order_reuse_key = snapshot.OrderReuse.NormalizedKey,
                order_reuse_cache_hit = snapshot.OrderReuse.CacheHit,
                order_reuse_route_id = snapshot.OrderReuse.ReusedRouteId,
                order_reuse_cache_size = snapshot.OrderReuse.RouteCacheSize,
                order_reuse_fanout = snapshot.OrderReuse.FanoutCount,
                order_reuse_same_point = snapshot.OrderReuse.SamePointReuseCount,
                order_reuse_near_point = snapshot.OrderReuse.NearPointReuseCount,
                order_reuse_scope = snapshot.OrderReuse.ReuseScope,
                order_reuse_path_signature = snapshot.OrderReuse.PathRouteSignature,
                order_reuse_mesh_signature = snapshot.OrderReuse.MeshRouteSignature,
                target_allocation_selected = snapshot.TargetAllocation.SelectedCount,
                target_allocation_slots = snapshot.TargetAllocation.SlotCount,
                target_allocation_reachable = snapshot.TargetAllocation.ReachableSlotCount,
                target_allocation_reachability_status = snapshot.TargetAllocation.ReachabilityProbeStatus,
                target_allocation_reachability_source = snapshot.TargetAllocation.ReachabilitySource,
                target_allocation_route_id = snapshot.TargetAllocation.AllocationRouteId,
                target_allocation_route_key = snapshot.TargetAllocation.AllocationRouteReuseKey,
                target_allocation_mesh_source = snapshot.TargetAllocation.MeshReachabilitySource,
                target_allocation_mesh_status = snapshot.TargetAllocation.MeshReachabilityStatus,
                target_allocation_mesh_touched_tiles = snapshot.TargetAllocation.MeshReachabilityTouchedTileCount,
                target_allocation_blocked = snapshot.TargetAllocation.BlockedSlotCount,
                target_allocation_fallback = snapshot.TargetAllocation.FallbackSlotCount,
                target_allocation_actual_sample_count = snapshot.TargetAllocation.ActualTargetSampleCount,
                target_allocation_actual_sample_source = snapshot.TargetAllocation.ActualTargetSampleSource,
                target_allocation_sample_points = snapshot.TargetSlotSamples.Take(16).Select(sample => new { x_cm = sample.Xcm, y_cm = sample.Ycm }).ToArray(),
            static_obstacle_target = snapshot.TargetStaticObstacleCount,
            static_obstacle_authored = snapshot.ObstacleDiagnostics.AuthoredStaticObstacleCount,
            static_obstacle_baked = snapshot.ObstacleDiagnostics.BakedStaticObstacleCount,
            static_obstacle_loaded = snapshot.ObstacleDiagnostics.LoadedStaticObstacleCount,
            static_obstacle_solver_active = snapshot.ObstacleDiagnostics.SolverActiveStaticObstacleCount,
            static_obstacle_solver_capacity = snapshot.ObstacleDiagnostics.SolverStaticObstacleCapacity,
            static_obstacle_world_planned = snapshot.StaticObstacleWorldDiagnostics.PlannedWorldObstacleCount,
            static_obstacle_world_coverage_chunks = snapshot.StaticObstacleWorldDiagnostics.MacroChunkCoverageCount,
                static_obstacle_world_distribution = snapshot.StaticObstacleWorldDiagnostics.DistributionStrategy,
            hpa_macro_available = snapshot.HpaMacroDiagnostics.Available,
            hpa_macro_route_chunks = snapshot.HpaMacroDiagnostics.SampleRouteChunkCount,
            hpa_macro_portals = snapshot.HpaMacroDiagnostics.SamplePortalCount,
                hpa_macro_route = $"{snapshot.HpaMacroDiagnostics.StartMacroChunkX},{snapshot.HpaMacroDiagnostics.StartMacroChunkY}->{snapshot.HpaMacroDiagnostics.GoalMacroChunkX},{snapshot.HpaMacroDiagnostics.GoalMacroChunkY}",
                overlay_line_count = snapshot.OverlayLines.Count,
                overlay_has_showcase_guide = HasMassNavigationRuntimeGuideOverlay(snapshot),
                overlay_lines = snapshot.OverlayLines.Take(24).ToArray(),
            debug_visual_overlay_draw_ms = Math.Round(snapshot.DebugVisualDiagnostics.ScreenOverlayDrawMs, 4),
            debug_visual_overlay_items = snapshot.DebugVisualDiagnostics.ScreenOverlayItems,
            debug_visual_evidence_overlay_items = snapshot.DebugVisualDiagnostics.EvidenceOverlayItems,
            debug_visual_rebuilt_lanes = snapshot.DebugVisualDiagnostics.ScreenOverlayRebuiltLanes,
            debug_visual_dirty_lanes = snapshot.DebugVisualDiagnostics.ScreenOverlayDirtyLanes,
            debug_visual_debug_draw_ms = Math.Round(snapshot.DebugVisualDiagnostics.DebugDrawRenderMs, 4),
            debug_visual_hud_ms = Math.Round(snapshot.DebugVisualDiagnostics.NativeDiagnosticHudMs, 4),
            solver_window_driver = snapshot.SolverWindowDriver,
            flow_work_area_reason = snapshot.FlowWorkAreaReason,
            scenario_spawns = snapshot.ScenarioSpawnCount,
            scene_resets = snapshot.SceneResetCount,
            full_selection_probe = snapshot.Step == "007_10k_commanded_flow_probe",
            frame_ms = Math.Round(snapshot.FrameMs, 4),
                simulation_ms = Math.Round(snapshot.SimulationMs, 4),
                presentation_ms = Math.Round(snapshot.PresentationMs, 4),
                performer_ms = Math.Round(snapshot.PerformerMs, 4),
                minimap_ms = Math.Round(snapshot.MinimapMs, 4),
                mass_navigation_ms = Math.Round(snapshot.MassNavigationMs, 4),
                status = "done"
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildMassNavigationPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Boot mass_navigation launcher] --> B[Bind template spawn receipts]",
            "    B --> C[Verify performer owners and minimap markers]",
            "    C --> D[Write LivePrimary selection]",
            "    D --> E[Submit massNavigationMove through OrderBuffer]",
            "    E --> R[Probe same and near-point order reuse]",
            "    R --> T[Submit one shared order to the full 10k controllable set]",
            "    T --> F[Jump core minimap camera to remote 64km coordinate]",
            "    F --> G[Jump back to original area]",
            "    G --> H[Capture bake/HPA overlay and path strategy inspector]",
            "    H --> I[Capture same-order reuse, target allocation and 10k command proof]",
            "    I --> J{No respawn, reset, marker drop, missing bake contract, or old minimap path?}",
            "    J -->|yes| K[Write battle-report + trace + path + PNG timeline]",
            "    J -->|no| X[Fail MassNavigation UAT]"
        }) + Environment.NewLine;
    }

    private static string BuildMassNavigationVisibleChecklist(IReadOnlyList<CaptureFrame> frames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: mass-navigation-large-world");
        sb.AppendLine();
        sb.AppendLine("- `000_boot.png`: should show the configured 64km RTS world framing, MassNavigation unit samples, solver window, flow work area and minimap marker counts.");
        sb.AppendLine("- `001_selection_order.png`: selection/order counts should be non-zero and group counts should prove the OrderBuffer path reached MassNavigation group runtime.");
        sb.AppendLine("- `002_remote_minimap_jump.png`: camera coordinates should be far from boot coordinates while agent counts remain unchanged.");
        sb.AppendLine("- `003_return_original_area.png`: agent count and scenario spawn/reset counters should match boot, proving camera movement did not recreate the scenario.");
        sb.AppendLine("- `004_bake_hpa_overlay.png`: should show 256x256 macro chunks, HPA adjacency count, and honest navmesh bake coverage/not-loaded state.");
        sb.AppendLine("- `005_path_strategy_inspector.png`: should show graph/navmesh/hybrid path lines, waypoint vs pathpoint separation, and per-agent layer/cost strategy rows.");
        sb.AppendLine("- `006_order_reuse_target_allocation.png`: should show same/near order path reuse, large-selection target slots, and performance-debug counters.");
        sb.AppendLine("- `007_10k_commanded_flow_probe.png`: should show the full controllable selection order, commanded/moving/settled counts, allocated slots, and flow enabled state.");
        sb.AppendLine("- `008_acceptance_gate_matrix.png`: should summarize U1-U16 status, production gates, and any failed checks.");
        sb.AppendLine("- `009_raylib_frame_benchmark.png`: should show the Raylib framebuffer debug overlay A/B benchmark frame and production FPS/debug budget thresholds.");
        sb.AppendLine("- `006a_runtime_u1_visual_heightmap_bake.png` through `006p_runtime_u16_bake_tool.png`: each should be a playable guide frame with 5W1H, Player input, Look for, Pass signal, Legend, Expected, Gate and a matching debug presentation view.");
        sb.AppendLine("- `010_path_only_pick_before.png`: should read as Path Preview mode before order submission: start/goal are selected, `PreviewMode=path_preview`, and no unit order has been enqueued.");
        sb.AppendLine("- `011_path_only_pick_result_no_order.png`: should show the highlighted preview route, immutable pathpoints, editable waypoint intent, and `order_delta=0`.");
        sb.AppendLine("- `012_path_only_unreachable_failure.png` through `030_waypoint_edit_after_pathpoints_regenerated.png`: should bind every remaining U1-U16 showcase case to a player-visible keyframe and machine-readable pass/fail fields.");
        sb.AppendLine("- Runtime guide overlays `006a_runtime_u1_visual_heightmap_bake` through `006p_runtime_u16_bake_tool` are sampled into `summary.json`/`trace.jsonl`; every U1-U16 use case must have in-game 5W1H, Player input, Look for, Pass signal, Legend, Expected, and Gate text.");
        sb.AppendLine("- `screens/timeline.png` is the compact strip for side-by-side UAT review.");
        sb.AppendLine();
        foreach (CaptureFrame frame in frames)
        {
            sb.AppendLine($"- `{frame.FileName}`: agents={frame.CenterCount}, groups={frame.CenterStoppedAgents}, minimapVisible={frame.Team0CrossedFraction:F0}, frameMs={frame.Team1CrossedFraction:F3}");
        }

        return sb.ToString();
    }

    private static string BuildMassNavigationSummaryJson(
        LauncherRecordingRequest request,
        MassNavigationAcceptanceResult acceptance,
        IReadOnlyList<MassNavigationSnapshot> timeline,
        FrameTimingStats frameStats,
        MassNavigationRaylibFrameBenchmark raylibBenchmark)
    {
        MassNavigationSnapshot boot = timeline[0];
        MassNavigationSnapshot final = timeline[^1];
        MassNavigationSnapshot reuseProbe = timeline.FirstOrDefault(snapshot => snapshot.Step == "001b_order_reuse_probe");
        if (reuseProbe.Step == null)
        {
            reuseProbe = final;
        }

        MassNavigationSnapshot fullCommandProbe = timeline.FirstOrDefault(snapshot => snapshot.Step == "007_10k_commanded_flow_probe");
        if (fullCommandProbe.Step == null)
        {
            fullCommandProbe = final;
        }

        IReadOnlyList<MassNavigationUseCaseStatus> useCases = BuildMassNavigationUseCaseStatuses(timeline, frameStats, raylibBenchmark, acceptance.ManualUatAccepted);

        return JsonSerializer.Serialize(new
        {
            scenario = "mass_navigation_large_world",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            acceptance_scope = acceptance.ProductionGateSuccess
                ? "production_showcase_acceptance"
                : acceptance.MachineProductionEvidenceSuccess ? "machine_evidence_ready_manual_uat_required" : "scene_smoke_only",
            success = acceptance.Success,
            scene_smoke_success = acceptance.SceneSmokeSuccess,
            machine_production_evidence_success = acceptance.MachineProductionEvidenceSuccess,
            manual_uat_required = true,
            manual_uat_accepted = acceptance.ManualUatAccepted,
            manual_uat_evidence_path = acceptance.ManualUatEvidencePath,
            manual_uat_blocker = acceptance.ManualUatBlocker,
            production_gate_success = acceptance.ProductionGateSuccess,
            production_claim_allowed = acceptance.ProductionGateSuccess,
            production_claim_blocker = acceptance.ProductionGateSuccess
                ? null
                : BuildMassNavigationProductionClaimBlocker(acceptance),
            normalized_signature = acceptance.NormalizedSignature,
            world_width_cm = boot.WorldWidthCm,
            world_height_cm = boot.WorldHeightCm,
            loaded_chunk_count = boot.LoadedChunkCount,
            final_loaded_chunk_count = final.LoadedChunkCount,
            boundary_click_result = boot.WorldBoundaryDiagnostics.BoundaryClickResult,
            ground_picking_result = boot.WorldBoundaryDiagnostics.GroundPickingResult,
            world_boundary_diagnostics = new
            {
                boot.WorldBoundaryDiagnostics.Available,
                boot.WorldBoundaryDiagnostics.Source,
                world_min_x_cm = boot.WorldBoundaryDiagnostics.WorldMinXCm,
                world_min_y_cm = boot.WorldBoundaryDiagnostics.WorldMinYCm,
                world_max_x_cm = boot.WorldBoundaryDiagnostics.WorldMaxXCm,
                world_max_y_cm = boot.WorldBoundaryDiagnostics.WorldMaxYCm,
                boot.WorldBoundaryDiagnostics.CameraInBounds,
                boot.WorldBoundaryDiagnostics.MinimapBoundaryClickInBounds,
                boot.WorldBoundaryDiagnostics.MinimapBoundaryClickClamped,
                boot.WorldBoundaryDiagnostics.GroundPickingInsideAccepted,
                boot.WorldBoundaryDiagnostics.GroundPickingOutsideClamped,
                inside_probe = new
                {
                    x_cm = boot.WorldBoundaryDiagnostics.InsideProbeWorldCm.X,
                    y_cm = boot.WorldBoundaryDiagnostics.InsideProbeWorldCm.Y
                },
                outside_probe = new
                {
                    x_cm = boot.WorldBoundaryDiagnostics.OutsideProbeWorldCm.X,
                    y_cm = boot.WorldBoundaryDiagnostics.OutsideProbeWorldCm.Y
                },
                outside_clamped = new
                {
                    x_cm = boot.WorldBoundaryDiagnostics.OutsideClampedWorldCm.X,
                    y_cm = boot.WorldBoundaryDiagnostics.OutsideClampedWorldCm.Y
                }
            },
            agent_count = boot.AgentCount,
            live_agents = boot.AgentCount,
            commanded_agents = fullCommandProbe.CommandedAgents,
            moving_agents = fullCommandProbe.MovingAgents,
            settled_agents = fullCommandProbe.SettledAgents,
            pending_entity_sync_count = fullCommandProbe.PendingEntitySyncCount,
            flow_enabled = fullCommandProbe.FlowEnabled,
            full_selection_agents = fullCommandProbe.SelectedCount,
            full_selection_target_slots = fullCommandProbe.TargetAllocation.SlotCount,
            full_selection_actual_target_sample_count = fullCommandProbe.TargetAllocation.ActualTargetSampleCount,
            full_selection_actual_target_sample_source = fullCommandProbe.TargetAllocation.ActualTargetSampleSource,
            full_selection_blocked_slots = fullCommandProbe.TargetAllocation.BlockedSlotCount,
            full_selection_fallback_slots = fullCommandProbe.TargetAllocation.FallbackSlotCount,
            team_count = boot.TeamCount,
            blocker_count = boot.BlockerCount,
            hotspot_marker_count = boot.HotspotMarkerCount,
            performer_active_count = boot.PerformerActiveCount,
            minimap_marker_count = boot.MinimapBufferCount,
            minimap_dropped_total = boot.MinimapDroppedTotal,
            bake_data_bound = boot.BakeDataBound,
            macro_chunk_columns = boot.MacroChunkColumns,
            macro_chunk_rows = boot.MacroChunkRows,
            macro_chunk_count = boot.MacroChunkCount,
            macro_chunk_size_x_cm = boot.MacroChunkSizeXCm,
            macro_chunk_size_y_cm = boot.MacroChunkSizeYCm,
            expected_hpa_adjacency_edges = boot.ExpectedMacroAdjacencyEdgeCount,
            navmesh_baked_tiles = boot.NavMeshBake.BakedChunks,
            navmesh_failed_tiles = boot.NavMeshBake.FailedChunks,
            navmesh_missing_tiles = boot.NavMeshBake.MissingChunks,
            navmesh_dirty_tiles = boot.NavMeshBake.DirtyChunks,
            navmesh_not_loaded_tiles = boot.NavMeshBake.NotLoadedChunks,
            navmesh_total_tiles = boot.NavMeshBake.TotalChunks,
            navmesh_coverage_percent = boot.NavMeshBake.CoveragePercent,
            road_graph_coverage_percent = boot.RoadGraphBake.CoveragePercent,
            flow_field_coverage_percent = boot.FlowFieldBake.CoveragePercent,
            static_obstacle_coverage_percent = boot.StaticObstacleBake.CoveragePercent,
            navmesh_layer_count = boot.NavMeshLayerCount,
            navmesh_profile_count = boot.NavMeshProfileCount,
            navmesh_area_cost_count = boot.NavMeshAreaCostCount,
            authored_static_obstacle_count = boot.AuthoredStaticObstacleCount,
            target_static_obstacle_count = boot.TargetStaticObstacleCount,
            baked_static_obstacle_count = boot.ObstacleDiagnostics.BakedStaticObstacleCount,
            loaded_static_obstacle_count = boot.ObstacleDiagnostics.LoadedStaticObstacleCount,
            solver_active_static_obstacle_count = boot.ObstacleDiagnostics.SolverActiveStaticObstacleCount,
            solver_static_obstacle_capacity = boot.ObstacleDiagnostics.SolverStaticObstacleCapacity,
            hpa_overlay_required = boot.HpaOverlayRequired,
            path_inspector_required = boot.PathInspectorRequired,
            bake_overlay_required = boot.BakeOverlayRequired,
            playable_guided_showcase = new
            {
                runtime_overlay_sampled = timeline.Any(HasMassNavigationRuntimeGuideOverlay),
                required_use_cases = RequiredMassNavigationRuntimeUseCaseIds(),
                sampled_use_cases = RequiredMassNavigationRuntimeUseCaseIds()
                    .Where(id => TimelineHasRuntimeGuideForUseCase(timeline, id))
                    .ToArray(),
                missing_use_cases = RequiredMassNavigationRuntimeUseCaseIds()
                    .Where(id => !TimelineHasRuntimeGuideForUseCase(timeline, id))
                    .ToArray(),
                sampled_steps = timeline
                    .Where(HasMassNavigationRuntimeGuideOverlay)
                    .Select(snapshot => new
                    {
                        snapshot.Step,
                        overlay_line_count = snapshot.OverlayLines.Count,
                        has_showcase = snapshot.OverlayLines.Any(line => line.Contains("Showcase", StringComparison.Ordinal)),
                        has_player_input = snapshot.OverlayLines.Any(line => line.Contains("Player input:", StringComparison.Ordinal)),
                        has_look_for = snapshot.OverlayLines.Any(line => line.Contains("Look for:", StringComparison.Ordinal)),
                        has_pass_signal = snapshot.OverlayLines.Any(line => line.Contains("Pass signal:", StringComparison.Ordinal)),
                        has_legend = snapshot.OverlayLines.Any(line => line.Contains("Legend:", StringComparison.Ordinal)),
                        has_expected = snapshot.OverlayLines.Any(line => line.Contains("Expected:", StringComparison.Ordinal)),
                        has_gate = snapshot.OverlayLines.Any(line => line.Contains("Gate:", StringComparison.Ordinal)),
                        summary = snapshot.OverlayLines
                            .Where(line =>
                                line.Contains("Showcase", StringComparison.Ordinal) ||
                                line.Contains("Player input:", StringComparison.Ordinal) ||
                                line.Contains("Look for:", StringComparison.Ordinal) ||
                                line.Contains("Pass signal:", StringComparison.Ordinal) ||
                                line.Contains("Legend:", StringComparison.Ordinal) ||
                                line.Contains("Expected:", StringComparison.Ordinal) ||
                                line.Contains("Gate:", StringComparison.Ordinal))
                            .Take(10)
                            .ToArray()
                    })
                    .ToArray(),
                contract = "Playable guided showcase must expose 5W1H, Player input, Look for, Pass signal, Legend, Expected, and Gate in the real runtime overlay, not only in offline screenshot cards."
            },
            pathing_profiles = boot.BakeProfiles.Select(profile => new
            {
                profile.AgentTypeId,
                profile.NavProfileId,
                profile.Layer,
                profile.SelectionMode,
                profile.NavAreaCostCount,
                profile.GraphTagRuleCount,
                profile.ForbiddenAreaCount,
                profile.RepresentativeAreaCost,
                profile.AreaCostSamples,
                profile.GraphRuleSummary,
                profile.RequiredTagSummary,
                profile.ForbiddenTagSummary
            }),
            path_only_query = new
            {
                boot.PathOnlyQuery.Available,
                boot.PathOnlyQuery.Status,
                boot.PathOnlyQuery.NoOrderSubmitted,
                boot.PathOnlyQuery.PreviewMode,
                boot.PathOnlyQuery.InputContract,
                boot.PathOnlyQuery.RoutePreviewState,
                boot.PathOnlyQuery.HighlightRouteVisible,
                boot.PathOnlyQuery.OrderSuppressionReason,
                boot.PathOnlyQuery.PathPointContract,
                boot.PathOnlyQuery.WaypointContract,
                boot.PathOnlyQuery.RouteProvenance,
                orders_before = boot.CommandedAgents,
                orders_after = boot.CommandedAgents,
                order_delta = 0,
                boot.PathOnlyQuery.QuerySource,
                boot.PathOnlyQuery.Strategy,
                boot.PathOnlyQuery.AgentTypeId,
                boot.PathOnlyQuery.NavProfileId,
                boot.PathOnlyQuery.Layer,
                start_x_cm = Math.Round(boot.PathOnlyQuery.StartWorldCm.X, 2),
                start_y_cm = Math.Round(boot.PathOnlyQuery.StartWorldCm.Y, 2),
                goal_x_cm = Math.Round(boot.PathOnlyQuery.GoalWorldCm.X, 2),
                goal_y_cm = Math.Round(boot.PathOnlyQuery.GoalWorldCm.Y, 2),
                boot.PathOnlyQuery.WaypointCount,
                boot.PathOnlyQuery.PathPointCount,
                boot.PathOnlyQuery.ExpandedNodeCount,
                boot.PathOnlyQuery.ErrorCode,
                boot.PathOnlyQuery.TouchedTileCount,
                boot.PathOnlyQuery.CorridorPortalCount,
                boot.PathOnlyQuery.TravelCost,
                boot.PathOnlyQuery.StartMacroChunkX,
                boot.PathOnlyQuery.StartMacroChunkY,
                boot.PathOnlyQuery.GoalMacroChunkX,
                boot.PathOnlyQuery.GoalMacroChunkY,
                boot.PathOnlyQuery.MacroRouteChunkCount,
                boot.PathOnlyQuery.MacroExpandedChunkCount
            },
            strategy_switch_diagnostics = boot.StrategySwitchDiagnostics.Select(strategy => new
            {
                strategy.AgentTypeId,
                strategy.RequestedMode,
                strategy.SelectedStrategy,
                strategy.GraphQueryAvailable,
                strategy.GraphStatus,
                strategy.GraphPathPointCount,
                strategy.GraphExpandedNodeCount,
                strategy.GraphTravelCost,
                strategy.MeshQueryAvailable,
                strategy.MeshStatus,
                strategy.MeshPathPointCount,
                strategy.MeshExpandedNodeCount,
                strategy.MeshTravelCost,
                strategy.MeshQuerySource,
                strategy.MeshStartChunkX,
                strategy.MeshStartChunkY,
                strategy.MeshGoalChunkX,
                strategy.MeshGoalChunkY,
                strategy.MeshTouchedTileCount,
                strategy.CostBreakdown,
                strategy.RouteId,
                strategy.AcceptanceProof
            }),
            waypoint_path_diagnostics = new
            {
                boot.WaypointPathDiagnostics.WaypointCount,
                boot.WaypointPathDiagnostics.PathPointCount,
                boot.WaypointPathDiagnostics.WaypointsEditable,
                boot.WaypointPathDiagnostics.PathPointsImmutable,
                boot.WaypointPathDiagnostics.PathPointsCanSeedWaypoints,
                boot.WaypointPathDiagnostics.Source,
                boot.WaypointPathDiagnostics.BusinessExample
            },
            order_reuse = new
            {
                reuseProbe.OrderReuse.HasOrder,
                reuseProbe.OrderReuse.LastOrderId,
                reuseProbe.OrderReuse.NormalizedKey,
                reuseProbe.OrderReuse.CacheHit,
                reuseProbe.OrderReuse.ReusedRouteId,
                reuseProbe.OrderReuse.RouteCacheSize,
                reuseProbe.OrderReuse.FanoutCount,
                reuseProbe.OrderReuse.SamePointReuseCount,
                reuseProbe.OrderReuse.NearPointReuseCount,
                reuseProbe.OrderReuse.Strategy,
                reuseProbe.OrderReuse.AgentTypeId,
                reuseProbe.OrderReuse.NavProfileId,
                reuseProbe.OrderReuse.Layer,
                reuseProbe.OrderReuse.DataVersion,
                reuseProbe.OrderReuse.DynamicBlockerEpoch,
                reuseProbe.OrderReuse.InvalidationReason,
                reuseProbe.OrderReuse.CacheSource,
                reuseProbe.OrderReuse.ReuseScope,
                reuseProbe.OrderReuse.PathRouteSignature,
                reuseProbe.OrderReuse.PathRouteSource,
                reuseProbe.OrderReuse.PathRoutePointCount,
                reuseProbe.OrderReuse.PathRouteTouchedTileCount,
                reuseProbe.OrderReuse.MeshRouteSignature,
                reuseProbe.OrderReuse.MeshRouteSource,
                reuseProbe.OrderReuse.MeshRouteStatus,
                reuseProbe.OrderReuse.MeshRouteTouchedTileCount,
                reuseProbe.OrderReuse.ProductionGap
            },
            target_allocation = new
            {
                fullCommandProbe.TargetAllocation.HasAllocation,
                fullCommandProbe.TargetAllocation.SelectedCount,
                fullCommandProbe.TargetAllocation.SlotCount,
                reachable_slot_count = fullCommandProbe.TargetAllocation.ReachableSlotCount,
                fullCommandProbe.TargetAllocation.ReachabilityFanoutCount,
                fullCommandProbe.TargetAllocation.ReachabilityProbeStatus,
                fullCommandProbe.TargetAllocation.ReachabilitySource,
                fullCommandProbe.TargetAllocation.AllocationRouteReuseKey,
                fullCommandProbe.TargetAllocation.AllocationRouteId,
                fullCommandProbe.TargetAllocation.AllocationRouteCacheSource,
                fullCommandProbe.TargetAllocation.MeshReachabilitySource,
                fullCommandProbe.TargetAllocation.MeshReachabilityStatus,
                fullCommandProbe.TargetAllocation.MeshReachabilityTouchedTileCount,
                fullCommandProbe.TargetAllocation.BlockedSlotCount,
                fullCommandProbe.TargetAllocation.FallbackSlotCount,
                fullCommandProbe.TargetAllocation.BlockedReasonSummary,
                fullCommandProbe.TargetAllocation.FallbackReasonSummary,
                blocked_reason_counts = new { no_path = fullCommandProbe.TargetAllocation.BlockedSlotCount },
                fallback_reason_counts = new { overflow = fullCommandProbe.TargetAllocation.FallbackSlotCount },
                fullCommandProbe.TargetAllocation.GroupSlotCount,
                fullCommandProbe.TargetAllocation.UnitSlotCount,
                fullCommandProbe.TargetAllocation.GoalFootprintRadiusCm,
                fullCommandProbe.TargetAllocation.FormationMode,
                fullCommandProbe.TargetAllocation.ProductionGap,
                fullCommandProbe.TargetAllocation.ActualTargetSampleCount,
                fullCommandProbe.TargetAllocation.ActualTargetSampleSource,
                actual_target_samples = fullCommandProbe.TargetSlotSamples.Select(sample => new
                {
                    x_cm = sample.Xcm,
                    y_cm = sample.Ycm
                }).ToArray(),
                destination_x_cm = Math.Round(fullCommandProbe.TargetAllocation.DestinationWorldCm.X, 2),
                destination_y_cm = Math.Round(fullCommandProbe.TargetAllocation.DestinationWorldCm.Y, 2)
            },
            target_allocation_reuse_probe = new
            {
                reuseProbe.TargetAllocation.HasAllocation,
                reuseProbe.TargetAllocation.SelectedCount,
                reuseProbe.TargetAllocation.SlotCount,
                reachable_slot_count = reuseProbe.TargetAllocation.ReachableSlotCount,
                reuseProbe.TargetAllocation.ReachabilityFanoutCount,
                reuseProbe.TargetAllocation.ReachabilityProbeStatus,
                reuseProbe.TargetAllocation.ReachabilitySource,
                reuseProbe.TargetAllocation.AllocationRouteReuseKey,
                reuseProbe.TargetAllocation.AllocationRouteId,
                reuseProbe.TargetAllocation.AllocationRouteCacheSource,
                reuseProbe.TargetAllocation.MeshReachabilitySource,
                reuseProbe.TargetAllocation.MeshReachabilityStatus,
                reuseProbe.TargetAllocation.MeshReachabilityTouchedTileCount,
                reuseProbe.TargetAllocation.BlockedSlotCount,
                reuseProbe.TargetAllocation.FallbackSlotCount,
                reuseProbe.TargetAllocation.BlockedReasonSummary,
                reuseProbe.TargetAllocation.FallbackReasonSummary,
                blocked_reason_counts = new { no_path = reuseProbe.TargetAllocation.BlockedSlotCount },
                fallback_reason_counts = new { overflow = reuseProbe.TargetAllocation.FallbackSlotCount },
                reuseProbe.TargetAllocation.GroupSlotCount,
                reuseProbe.TargetAllocation.UnitSlotCount,
                reuseProbe.TargetAllocation.GoalFootprintRadiusCm,
                reuseProbe.TargetAllocation.FormationMode,
                reuseProbe.TargetAllocation.ProductionGap,
                reuseProbe.TargetAllocation.ActualTargetSampleCount,
                reuseProbe.TargetAllocation.ActualTargetSampleSource,
                actual_target_samples = reuseProbe.TargetSlotSamples.Select(sample => new
                {
                    x_cm = sample.Xcm,
                    y_cm = sample.Ycm
                }).ToArray(),
                destination_x_cm = Math.Round(reuseProbe.TargetAllocation.DestinationWorldCm.X, 2),
                destination_y_cm = Math.Round(reuseProbe.TargetAllocation.DestinationWorldCm.Y, 2)
            },
            obstacle_diagnostics = new
            {
                boot.ObstacleDiagnostics.TargetStaticObstacleCount,
                boot.ObstacleDiagnostics.AuthoredStaticObstacleCount,
                boot.ObstacleDiagnostics.BakedStaticObstacleCount,
                boot.ObstacleDiagnostics.LoadedStaticObstacleCount,
                boot.ObstacleDiagnostics.SolverActiveStaticObstacleCount,
                boot.ObstacleDiagnostics.SolverStaticObstacleCapacity,
                boot.ObstacleDiagnostics.Source
            },
            static_obstacle_world_diagnostics = new
            {
                boot.StaticObstacleWorldDiagnostics.TargetStaticObstacleCount,
                boot.StaticObstacleWorldDiagnostics.PlannedWorldObstacleCount,
                boot.StaticObstacleWorldDiagnostics.MacroChunkColumns,
                boot.StaticObstacleWorldDiagnostics.MacroChunkRows,
                boot.StaticObstacleWorldDiagnostics.MacroChunkCoverageCount,
                boot.StaticObstacleWorldDiagnostics.ActiveWindowLoadedCount,
                boot.StaticObstacleWorldDiagnostics.SolverActiveStaticObstacleCount,
                boot.StaticObstacleWorldDiagnostics.SolverStaticObstacleCapacity,
                boot.StaticObstacleWorldDiagnostics.DeterministicSeed,
                boot.StaticObstacleWorldDiagnostics.DistributionStrategy,
                boot.StaticObstacleWorldDiagnostics.SampleChunkBuckets,
                boot.StaticObstacleWorldDiagnostics.WorldDistributionReady,
                boot.StaticObstacleWorldDiagnostics.ActiveWindowLimited,
                boot.StaticObstacleWorldDiagnostics.DataSource,
                boot.StaticObstacleWorldDiagnostics.RuntimeActivationStrategy
            },
            hpa_macro_diagnostics = new
            {
                boot.HpaMacroDiagnostics.Available,
                boot.HpaMacroDiagnostics.MacroChunkColumns,
                boot.HpaMacroDiagnostics.MacroChunkRows,
                boot.HpaMacroDiagnostics.MacroChunkCount,
                boot.HpaMacroDiagnostics.MacroChunkSizeXCm,
                boot.HpaMacroDiagnostics.MacroChunkSizeYCm,
                boot.HpaMacroDiagnostics.ExpectedAdjacencyEdgeCount,
                boot.HpaMacroDiagnostics.SamplePortalCount,
                boot.HpaMacroDiagnostics.SampleRouteChunkCount,
                boot.HpaMacroDiagnostics.SampleExpandedChunkCount,
                boot.HpaMacroDiagnostics.StartMacroChunkX,
                boot.HpaMacroDiagnostics.StartMacroChunkY,
                boot.HpaMacroDiagnostics.GoalMacroChunkX,
                boot.HpaMacroDiagnostics.GoalMacroChunkY,
                boot.HpaMacroDiagnostics.RouteSource,
                boot.HpaMacroDiagnostics.UsesSyntheticMacroGridTarget,
                boot.HpaMacroDiagnostics.ProductionGap
            },
            hpa_graph_diagnostics = new
            {
                boot.HpaGraphDiagnostics.Available,
                boot.HpaGraphDiagnostics.ActiveWindowMinChunkX,
                boot.HpaGraphDiagnostics.ActiveWindowMinChunkY,
                boot.HpaGraphDiagnostics.ActiveWindowMaxChunkX,
                boot.HpaGraphDiagnostics.ActiveWindowMaxChunkY,
                boot.HpaGraphDiagnostics.ActiveWindowChunkCount,
                boot.HpaGraphDiagnostics.LoadedTileCount,
                boot.HpaGraphDiagnostics.PortalCount,
                boot.HpaGraphDiagnostics.IntraTileEdgeCount,
                boot.HpaGraphDiagnostics.CrossTileEdgeCount,
                boot.HpaGraphDiagnostics.GraphNodeCount,
                boot.HpaGraphDiagnostics.GraphEdgeCount,
                boot.HpaGraphDiagnostics.ActiveWindowRouteAvailable,
                boot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount,
                boot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount,
                boot.HpaGraphDiagnostics.RouteStartChunkX,
                boot.HpaGraphDiagnostics.RouteStartChunkY,
                boot.HpaGraphDiagnostics.RouteGoalChunkX,
                boot.HpaGraphDiagnostics.RouteGoalChunkY,
                boot.HpaGraphDiagnostics.RouteStartPortalIndex,
                boot.HpaGraphDiagnostics.RouteGoalPortalIndex,
                boot.HpaGraphDiagnostics.RouteSignature,
                boot.HpaGraphDiagnostics.Source,
                boot.HpaGraphDiagnostics.Gap
            },
            layer_cost_diagnostics = boot.LayerCostDiagnostics.Select(profile => new
            {
                profile.AgentTypeId,
                profile.NavProfileId,
                profile.Layer,
                profile.SelectionMode,
                profile.NavAreaCostCount,
                profile.GraphTagRuleCount,
                profile.ForbiddenAreaCount,
                profile.RepresentativeAreaCost,
                profile.AreaCostSamples,
                profile.GraphRuleSummary,
                profile.RequiredTagSummary,
                profile.ForbiddenTagSummary
            }),
            layer_cost_query_matrix = BuildLayerQueryMatrix(boot),
            movement_proof = new
            {
                t0_step = boot.Step,
                tN_step = fullCommandProbe.Step,
                selected_t0 = boot.SelectedCount,
                selected_tN = fullCommandProbe.SelectedCount,
                commanded_t0 = boot.CommandedAgents,
                commanded_tN = fullCommandProbe.CommandedAgents,
                moving_t0 = boot.MovingAgents,
                moving_tN = fullCommandProbe.MovingAgents,
                settled_t0 = boot.SettledAgents,
                settled_tN = fullCommandProbe.SettledAgents,
                advance_cm_p50 = EstimateMassNavigationSampleAdvanceCm(boot, fullCommandProbe),
                arrival_count = fullCommandProbe.SettledAgents,
                stuck_count = Math.Max(0, fullCommandProbe.CommandedAgents - fullCommandProbe.MovingAgents - fullCommandProbe.SettledAgents),
                collision_or_avoidance_count = fullCommandProbe.MovingAgents,
                proof_scope = "scene_smoke_sample_positions"
            },
            runtime_guide_keyframes = BuildMassNavigationRuntimeGuideKeyframes(timeline),
            evidence_manifest = BuildMassNavigationEvidenceManifest(request.OutputDirectory, useCases),
            frame_timing = new
            {
                headless_tick_frame_count = frameStats.FrameCount,
                headless_tick_ms_p50 = Math.Round(frameStats.P50Ms, 4),
                headless_tick_ms_p95 = Math.Round(frameStats.P95Ms, 4),
                headless_tick_ms_p99 = Math.Round(frameStats.P99Ms, 4),
                headless_tick_ms_max = Math.Round(frameStats.MaxMs, 4),
                renderer_scope = raylibBenchmark.RendererScope,
                full_game_renderer_loaded_data_measured = raylibBenchmark.FullGameRendererLoadedDataMeasured,
                fps_measured = raylibBenchmark.Available,
                fps_smoke_passed = raylibBenchmark.SmokePassed,
                fps_production_passed = raylibBenchmark.ProductionPassed,
                micro_benchmark_production_threshold_passed = raylibBenchmark.MicroBenchmarkProductionThresholdPassed,
                raylib_frame_ms_p50 = Math.Round(raylibBenchmark.DebugOn.P50Ms, 4),
                raylib_frame_ms_p95 = Math.Round(raylibBenchmark.DebugOn.P95Ms, 4),
                raylib_frame_ms_p99 = Math.Round(raylibBenchmark.DebugOn.P99Ms, 4),
                raylib_frame_ms_max = Math.Round(raylibBenchmark.DebugOn.MaxMs, 4),
                raylib_fps_p50 = Math.Round(raylibBenchmark.DebugOn.FpsP50, 2),
                raylib_fps_p95 = Math.Round(raylibBenchmark.DebugOn.FpsP95, 2),
                raylib_fps_p99 = Math.Round(raylibBenchmark.DebugOn.FpsP99, 2),
                raylib_debug_off_p95_ms = Math.Round(raylibBenchmark.DebugOff.P95Ms, 4),
                raylib_debug_on_p95_ms = Math.Round(raylibBenchmark.DebugOn.P95Ms, 4),
                screenshot_ms = Math.Round(raylibBenchmark.DebugOn.ScreenshotMs, 4),
                debug_off_p95_ms = Math.Round(raylibBenchmark.DebugOff.P95Ms, 4),
                debug_on_p95_ms = Math.Round(raylibBenchmark.DebugOn.P95Ms, 4),
                overlay_draw_ms = Math.Round(raylibBenchmark.OverlayDrawMs, 4),
                overlay_p95_delta_ms = Math.Round(raylibBenchmark.OverlayP95DeltaMs, 4),
                fps_delta_percent = Math.Round(raylibBenchmark.FpsDeltaPercent, 4),
                benchmark_screenshot = "screens/009_raylib_frame_benchmark.png",
                benchmark_json = "screens/raylib-frame-benchmark.json",
                benchmark_notes = raylibBenchmark.Notes
            },
            raylib_frame_benchmark = raylibBenchmark,
            screenshot_keyframes = BuildMassNavigationScreenshotKeyframes(useCases),
            debug_visual_diagnostics = new
            {
                final.DebugVisualDiagnostics.Available,
                screen_overlay_build_ms = Math.Round(final.DebugVisualDiagnostics.ScreenOverlayBuildMs, 4),
                screen_overlay_draw_ms = Math.Round(final.DebugVisualDiagnostics.ScreenOverlayDrawMs, 4),
                screen_overlay_paint_ms = Math.Round(final.DebugVisualDiagnostics.ScreenOverlayPaintMs, 4),
                screen_overlay_composite_ms = Math.Round(final.DebugVisualDiagnostics.ScreenOverlayCompositeMs, 4),
                screen_overlay_final_draw_ms = Math.Round(final.DebugVisualDiagnostics.ScreenOverlayFinalDrawMs, 4),
                final.DebugVisualDiagnostics.ScreenOverlayItems,
                final.DebugVisualDiagnostics.EvidenceOverlayItems,
                final.DebugVisualDiagnostics.ScreenOverlayRebuiltLanes,
                final.DebugVisualDiagnostics.ScreenOverlayDirtyLanes,
                final.DebugVisualDiagnostics.TextLayoutCacheCount,
                debug_draw_render_ms = Math.Round(final.DebugVisualDiagnostics.DebugDrawRenderMs, 4),
                native_diagnostic_hud_ms = Math.Round(final.DebugVisualDiagnostics.NativeDiagnosticHudMs, 4),
                final.DebugVisualDiagnostics.DebugDrawCommands,
                final.DebugVisualDiagnostics.VisibleEntities,
                final.DebugVisualDiagnostics.FpsMeasured,
                final.DebugVisualDiagnostics.Source
            },
            boot_scenario_spawn_count = boot.ScenarioSpawnCount,
            final_scenario_spawn_count = final.ScenarioSpawnCount,
            boot_scene_reset_count = boot.SceneResetCount,
            final_scene_reset_count = final.SceneResetCount,
            use_case_statuses = useCases,
            production_blocked_use_cases = useCases
                .Where(useCase => !string.Equals(useCase.ProductionStatus, "PASS", StringComparison.OrdinalIgnoreCase))
                .Select(useCase => new
                {
                    useCase.Id,
                    useCase.Name,
                    useCase.ShowcaseStatus,
                    useCase.ProductionStatus,
                    useCase.AcceptanceProof,
                    useCase.PlayerStoryStatus,
                    useCase.PlayerVisibleEvidenceFiles
                }),
            showcase_incomplete_use_cases = useCases
                .Where(useCase => useCase.ShowcaseStatus is "CONCEPT" or "CONFIG_SMOKE" or "MISSING" or "BLOCKED")
                .Select(useCase => new
                {
                    useCase.Id,
                    useCase.Name,
                    useCase.ShowcaseStatus,
                    useCase.AcceptanceProof,
                    useCase.PlayerStoryStatus
                }),
            scene_smoke_failed_checks = acceptance.SceneSmokeFailedChecks,
            machine_production_evidence_failed_checks = acceptance.MachineProductionEvidenceSuccess
                ? Array.Empty<string>()
                : acceptance.ProductionGateFailedChecks
                    .Where(check => !string.Equals(check, MassNavigationManualUatBlocker, StringComparison.Ordinal))
                    .ToArray(),
            production_gate_failed_checks = acceptance.ProductionGateFailedChecks,
            failed_checks = acceptance.FailedChecks
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static MassNavigationManualUatSignoff LoadMassNavigationManualUatSignoff(string outputDirectory)
    {
        string path = Path.Combine(outputDirectory, MassNavigationManualUatSignoffFileName);
        if (!File.Exists(path))
        {
            return new MassNavigationManualUatSignoff(false, path, MassNavigationManualUatBlocker);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            bool accepted = TryGetBooleanProperty(root, "manual_uat_accepted") ||
                TryGetBooleanProperty(root, "accepted") ||
                TryGetBooleanProperty(root, "signed_off");
            if (!accepted)
            {
                return new MassNavigationManualUatSignoff(false, path, "Manual UAT signoff file exists but does not set manual_uat_accepted=true.");
            }

            return new MassNavigationManualUatSignoff(true, path, string.Empty);
        }
        catch (JsonException ex)
        {
            return new MassNavigationManualUatSignoff(false, path, $"Manual UAT signoff file is not valid JSON: {ex.Message}");
        }
    }

    private static bool TryGetBooleanProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.True;
    }

    private static string BuildMassNavigationProductionClaimBlocker(MassNavigationAcceptanceResult acceptance)
    {
        if (!acceptance.MachineProductionEvidenceSuccess)
        {
            return acceptance.ProductionGateFailedChecks.FirstOrDefault(check =>
                    !string.Equals(check, acceptance.ManualUatBlocker, StringComparison.Ordinal)) ??
                "Production claim is blocked until every machine gate in production_gate_failed_checks passes.";
        }

        return acceptance.ManualUatBlocker;
    }

    private static IReadOnlyList<MassNavigationUseCaseStatus> BuildMassNavigationUseCaseStatuses(
        IReadOnlyList<MassNavigationSnapshot> timeline,
        FrameTimingStats frameStats,
        MassNavigationRaylibFrameBenchmark raylibBenchmark,
        bool manualUatAccepted = false)
    {
        MassNavigationSnapshot boot = timeline[0];
        MassNavigationSnapshot final = timeline[^1];
        MassNavigationSnapshot reuseProbe = timeline.FirstOrDefault(snapshot => snapshot.Step == "001b_order_reuse_probe");
        if (reuseProbe.Step == null)
        {
            reuseProbe = final;
        }

        MassNavigationSnapshot fullCommandProbe = timeline.FirstOrDefault(snapshot => snapshot.Step == "007_10k_commanded_flow_probe");
        if (fullCommandProbe.Step == null)
        {
            fullCommandProbe = final;
        }

        bool targetAllocationReachabilitySmoke = fullCommandProbe.TargetAllocation.SlotCount >= MassNavigationFullCommandMinimumAgents &&
            fullCommandProbe.TargetAllocation.ReachableSlotCount >= MassNavigationFullCommandMinimumAgents &&
            string.Equals(fullCommandProbe.TargetAllocation.ReachabilityProbeStatus, "Ok", StringComparison.Ordinal);
        string tenKStatus = fullCommandProbe.CommandedAgents >= MassNavigationFullCommandMinimumAgents &&
            targetAllocationReachabilitySmoke
                ? "SMOKE"
                : "BLOCKED";
        string fpsStatus = raylibBenchmark.SmokePassed ? "SMOKE" : "MISSING";
        string fpsEvidence = $"headlessTickP95Ms={frameStats.P95Ms:F3}; fpsMeasured={raylibBenchmark.Available}; raylibP95Ms={raylibBenchmark.DebugOn.P95Ms:F3}; raylibP99Ms={raylibBenchmark.DebugOn.P99Ms:F3}; fpsP95={raylibBenchmark.DebugOn.FpsP95:F1}; overlayDeltaMs={raylibBenchmark.OverlayP95DeltaMs:F3}; delta={raylibBenchmark.FpsDeltaPercent:F2}%";
        string obstacleWorldEvidence = $"target={boot.StaticObstacleWorldDiagnostics.TargetStaticObstacleCount}; dataSource={boot.StaticObstacleWorldDiagnostics.DataSource}; plannedWorld={boot.StaticObstacleWorldDiagnostics.PlannedWorldObstacleCount}; macroCoverage={boot.StaticObstacleWorldDiagnostics.MacroChunkCoverageCount}; activeWindow={boot.StaticObstacleWorldDiagnostics.ActiveWindowLoadedCount}/{boot.StaticObstacleWorldDiagnostics.SolverStaticObstacleCapacity}; strategy={boot.StaticObstacleWorldDiagnostics.DistributionStrategy}; runtimeActivation={boot.StaticObstacleWorldDiagnostics.RuntimeActivationStrategy}";
        string debugVisualEvidence = $"trace/timeline/report emitted; evidenceOverlayItems={final.DebugVisualDiagnostics.EvidenceOverlayItems}; runtimeOverlayItems={final.DebugVisualDiagnostics.ScreenOverlayItems}; raylibOverlayDrawMs={raylibBenchmark.OverlayDrawMs:F3}; overlayDeltaMs={raylibBenchmark.OverlayP95DeltaMs:F3}; fpsDelta={raylibBenchmark.FpsDeltaPercent:F2}%; rebuiltLanes={final.DebugVisualDiagnostics.ScreenOverlayRebuiltLanes}; minimapDropped={boot.MinimapDroppedTotal}; massNavMs={final.MassNavigationMs:F3}";
        bool productionU1 = boot.NavMeshBake.BakedChunks > 0 && boot.NavMeshBake.FailedChunks == 0 && boot.NavMeshBake.MissingChunks == 0 && boot.NavMeshBake.DirtyChunks == 0 && boot.NavMeshBake.NotLoadedChunks == boot.NavMeshBake.TotalChunks - boot.NavMeshBake.BakedChunks;
        bool productionU2 = boot.NavMeshLayerCount >= 4 && boot.NavMeshProfileCount >= 5 && boot.BakeProfiles.Count >= 5;
        bool productionU3 = HasRequiredMultiLayerActiveWindowQueries(boot);
        bool productionU4 = boot.PathOnlyQuery.Available && boot.PathOnlyQuery.NoOrderSubmitted && boot.PathOnlyQuery.HighlightRouteVisible && boot.PathOnlyQuery.PathPointCount > 0;
        bool productionU5 = boot.HpaGraphDiagnostics.Available && boot.HpaGraphDiagnostics.ActiveWindowRouteAvailable && boot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount >= 2 && boot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount >= 1;
        bool productionU6 = boot.StrategySwitchDiagnostics.Any(item => item.GraphQueryAvailable) && boot.StrategySwitchDiagnostics.Any(item => item.MeshQueryAvailable && string.Equals(item.MeshQuerySource, "active_window_navmesh_query", StringComparison.Ordinal));
        bool productionU7 = reuseProbe.OrderReuse.HasOrder && reuseProbe.OrderReuse.CacheHit && reuseProbe.OrderReuse.FanoutCount >= MassNavigationSelectionSampleCount && !string.Equals(reuseProbe.OrderReuse.PathRouteSignature, "not_available", StringComparison.Ordinal) && !string.Equals(reuseProbe.OrderReuse.MeshRouteSignature, "not_available", StringComparison.Ordinal);
        bool productionU8 = targetAllocationReachabilitySmoke && fullCommandProbe.TargetAllocation.BlockedSlotCount == 0 && fullCommandProbe.TargetAllocation.FallbackSlotCount == 0 && fullCommandProbe.TargetAllocation.AllocationRouteId > 0;
        bool productionU9 = HasRequiredMultiLayerActiveWindowQueries(boot) && boot.LayerCostDiagnostics.Count >= 5;
        bool productionU10 = boot.WaypointPathDiagnostics.WaypointsEditable && boot.WaypointPathDiagnostics.PathPointsImmutable && boot.WaypointPathDiagnostics.PathPointsCanSeedWaypoints;
        bool productionU11 = boot.WorldWidthCm == 6_400_000 && boot.MacroChunkCount == 65_536 && boot.LoadedChunkCount > 0 && productionU1 && productionU5;
        bool productionU12 = fullCommandProbe.CommandedAgents >= MassNavigationFullCommandMinimumAgents && fullCommandProbe.MovingAgents + fullCommandProbe.SettledAgents >= MassNavigationFullCommandMinimumAgents && fullCommandProbe.FlowEnabled;
        bool productionU13 = boot.StaticObstacleWorldDiagnostics.WorldDistributionReady && boot.ObstacleDiagnostics.AuthoredStaticObstacleCount >= 40_000 && boot.ObstacleDiagnostics.BakedStaticObstacleCount >= 40_000 && boot.ObstacleDiagnostics.LoadedStaticObstacleCount >= 40_000 && boot.ObstacleDiagnostics.SolverActiveStaticObstacleCount > 0 && boot.ObstacleDiagnostics.SolverActiveStaticObstacleCount <= boot.ObstacleDiagnostics.SolverStaticObstacleCapacity;
        bool productionU14 = raylibBenchmark.ProductionPassed && raylibBenchmark.FullGameRendererLoadedDataMeasured;
        bool productionU15 = final.DebugVisualDiagnostics.Available && final.DebugVisualDiagnostics.ScreenOverlayItems == 0 && raylibBenchmark.ProductionPassed && raylibBenchmark.OverlayDrawMs <= MassNavigationRaylibProductionOverlayDrawMs;
        bool productionU16 = productionU1 && productionU4 && productionU5;

        string ManualUatStatus(bool machineEvidencePassed)
        {
            return machineEvidencePassed
                ? manualUatAccepted ? "PASS" : "NEEDS_MANUAL_UAT"
                : "BLOCKED";
        }

        string PlayerStoryStatus(bool machineEvidencePassed)
        {
            return machineEvidencePassed
                ? manualUatAccepted ? "human_uat_signed_off" : "replay_visible_needs_human_uat"
                : "blocked";
        }

        string AcceptanceProof(bool machineEvidencePassed, string failure)
        {
            if (!machineEvidencePassed)
            {
                return failure;
            }

            return manualUatAccepted
                ? "passed: machine-readable evidence, linked keyframes, and human-operated UAT signoff are present"
                : "machine evidence passed: replay/keyframes/diagnostics are present; human-operated UAT signoff still required";
        }

        return new[]
        {
            new MassNavigationUseCaseStatus("U1", "VisualHeightmap bake", "SMOKE", ManualUatStatus(productionU1), "runtime guide 006a_runtime_u1_visual_heightmap_bake; navmesh-visual-heightmap-current: sourceOriginKind=vhtm, 64/64 baked; large-world active-window navmesh has real baked tiles and zero failed/missing/dirty tiles", AcceptanceProof(productionU1, "active-window navmesh bake contract did not pass"), PlayerStoryStatus(productionU1), new[] { "screens/006a_runtime_u1_visual_heightmap_bake.png", "navmesh-visual-heightmap-current/screens/001_navmesh_bake_coverage.png", "navmesh-visual-heightmap-current/screens/005_layer_area_editor.png", "screens/027_navmesh_failure_drilldown_tile.png" }),
            new MassNavigationUseCaseStatus("U2", "Vertex/quad to LogicHeightmap", "SMOKE", ManualUatStatus(productionU2), "runtime guide 006b_runtime_u2_logic_heightmap_bake; vtxm, vhtm and lhtm sources converge to LogicHeightmap bake artifacts", AcceptanceProof(productionU2, "logic-heightmap source/profile contract did not pass"), PlayerStoryStatus(productionU2), new[] { "screens/006b_runtime_u2_logic_heightmap_bake.png", "navmesh-layer-editor-current/screens/001_navmesh_bake_coverage.png", "navmesh-logic-heightmap-current/screens/001_navmesh_bake_coverage.png" }),
            new MassNavigationUseCaseStatus("U3", "Mountain/river layer editor", "SMOKE", ManualUatStatus(productionU3), "runtime guide 006c_runtime_u3_layer_area_editor; LogicHeightmap sampled layer editor plus active-window multi-layer bake/query matrix are loaded", AcceptanceProof(productionU3, "multi-layer active-window query matrix did not pass"), PlayerStoryStatus(productionU3), new[] { "screens/006c_runtime_u3_layer_area_editor.png", "navmesh-visual-heightmap-current/screens/005_layer_area_editor.png", "screens/015_layer_cost_ground_water_air_mountain_compare.png" }),
            new MassNavigationUseCaseStatus("U4", "Path preview point query", boot.PathOnlyQuery.Available && boot.PathOnlyQuery.NoOrderSubmitted && boot.PathOnlyQuery.HighlightRouteVisible ? "SMOKE" : "BLOCKED", ManualUatStatus(productionU4), $"runtime guide 006d_runtime_u4_path_only; mode={boot.PathOnlyQuery.PreviewMode}; input={boot.PathOnlyQuery.InputContract}; state={boot.PathOnlyQuery.RoutePreviewState}; highlight={boot.PathOnlyQuery.HighlightRouteVisible}; status={boot.PathOnlyQuery.Status}; noOrder={boot.PathOnlyQuery.NoOrderSubmitted}; waypoints={boot.PathOnlyQuery.WaypointCount}/{boot.PathOnlyQuery.WaypointContract}; pathpoints={boot.PathOnlyQuery.PathPointCount}/{boot.PathOnlyQuery.PathPointContract}; touchedTiles={boot.PathOnlyQuery.TouchedTileCount}; provenance={boot.PathOnlyQuery.RouteProvenance}", AcceptanceProof(productionU4, "path-only preview contract did not pass"), PlayerStoryStatus(productionU4), new[] { "screens/006d_runtime_u4_path_only.png", "screens/010_path_only_pick_before.png", "screens/011_path_only_pick_result_no_order.png", "screens/012_path_only_unreachable_failure.png" }),
            new MassNavigationUseCaseStatus("U5", "HPA macro visibility", boot.HpaMacroDiagnostics.Available && boot.HpaGraphDiagnostics.Available && boot.HpaGraphDiagnostics.ActiveWindowRouteAvailable ? "SMOKE" : "BLOCKED", ManualUatStatus(productionU5), $"runtime guide 006e_runtime_u5_world_hpa; macro={boot.HpaMacroDiagnostics.MacroChunkColumns}x{boot.HpaMacroDiagnostics.MacroChunkRows}; expectedEdges={boot.HpaMacroDiagnostics.ExpectedAdjacencyEdgeCount}; routeChunks={boot.HpaMacroDiagnostics.SampleRouteChunkCount}; portals={boot.HpaMacroDiagnostics.SamplePortalCount}; activeWindowGraph={boot.HpaGraphDiagnostics.LoadedTileCount}/{boot.HpaGraphDiagnostics.ActiveWindowChunkCount} tiles nodes={boot.HpaGraphDiagnostics.GraphNodeCount} edges={boot.HpaGraphDiagnostics.GraphEdgeCount}; activeRoute={boot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount} portals/{boot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount} crossTileSteps; source={boot.HpaGraphDiagnostics.Source}", AcceptanceProof(productionU5, "active-window HPA portal route did not pass"), PlayerStoryStatus(productionU5), new[] { "screens/006e_runtime_u5_world_hpa.png", "screens/004_bake_hpa_overlay.png", "screens/013_hpa_active_window_portal_route.png" }),
            new MassNavigationUseCaseStatus("U6", "Graph/NavMesh/Hybrid switching", boot.StrategySwitchDiagnostics.Count >= 5 && boot.StrategySwitchDiagnostics.Any(item => item.MeshQueryAvailable && string.Equals(item.MeshQuerySource, "active_window_navmesh_query", StringComparison.Ordinal)) ? "SMOKE" : "BLOCKED", ManualUatStatus(productionU6), $"runtime guide 006f_runtime_u6_strategy_switch; strategies={boot.StrategySwitchDiagnostics.Count}; activeWindowMeshRows={boot.StrategySwitchDiagnostics.Count(item => item.MeshQueryAvailable && string.Equals(item.MeshQuerySource, "active_window_navmesh_query", StringComparison.Ordinal))}; selected={string.Join(",", boot.StrategySwitchDiagnostics.Select(item => item.AgentTypeId + ":" + item.SelectedStrategy))}", AcceptanceProof(productionU6, "graph/navmesh/hybrid evidence did not pass"), PlayerStoryStatus(productionU6), new[] { "screens/006f_runtime_u6_strategy_switch.png", "screens/005_path_strategy_inspector.png", "screens/014_graph_navmesh_hybrid_same_query_compare.png" }),
            new MassNavigationUseCaseStatus("U7", "Same/near order reuse", productionU7 ? "SMOKE" : "BLOCKED", ManualUatStatus(productionU7), $"runtime guide 006g_runtime_u7_order_reuse; cacheHit={reuseProbe.OrderReuse.CacheHit}; routeId={reuseProbe.OrderReuse.ReusedRouteId}; scope={reuseProbe.OrderReuse.ReuseScope}; fanout={reuseProbe.OrderReuse.FanoutCount}; same={reuseProbe.OrderReuse.SamePointReuseCount}; near={reuseProbe.OrderReuse.NearPointReuseCount}; pathSig={reuseProbe.OrderReuse.PathRouteSignature}; meshSig={reuseProbe.OrderReuse.MeshRouteSignature}", AcceptanceProof(productionU7, "same/near order reuse evidence did not pass"), PlayerStoryStatus(productionU7), new[] { "screens/006g_runtime_u7_order_reuse.png", "screens/017_order_reuse_first_order.png", "screens/018_order_reuse_same_point_cache_hit.png", "screens/019_order_reuse_near_point_cache_hit.png" }),
            new MassNavigationUseCaseStatus("U8", "Large-selection target allocation", targetAllocationReachabilitySmoke ? "SMOKE" : "BLOCKED", ManualUatStatus(productionU8), $"runtime guide 006h_runtime_u8_target_allocation; selected={fullCommandProbe.TargetAllocation.SelectedCount}; slots={fullCommandProbe.TargetAllocation.SlotCount}; reachable={fullCommandProbe.TargetAllocation.ReachableSlotCount}; reachability={fullCommandProbe.TargetAllocation.ReachabilityProbeStatus}; source={fullCommandProbe.TargetAllocation.ReachabilitySource}; routeId={fullCommandProbe.TargetAllocation.AllocationRouteId}; mesh={fullCommandProbe.TargetAllocation.MeshReachabilityStatus}/{fullCommandProbe.TargetAllocation.MeshReachabilitySource}; blocked={fullCommandProbe.TargetAllocation.BlockedSlotCount}; fallback={fullCommandProbe.TargetAllocation.FallbackSlotCount}", AcceptanceProof(productionU8, "10k target allocation evidence did not pass"), PlayerStoryStatus(productionU8), new[] { "screens/006h_runtime_u8_target_allocation.png", "screens/006_order_reuse_target_allocation.png", "screens/020_target_allocation_10k_slots_zoom.png" }),
            new MassNavigationUseCaseStatus("U9", "Air/water/mountain layer costs", productionU9 ? "SMOKE" : "BLOCKED", ManualUatStatus(productionU9), $"runtime guide 006i_runtime_u9_layer_costs; layers={boot.NavMeshLayerCount}; profiles={boot.NavMeshProfileCount}; areaCosts={boot.NavMeshAreaCostCount}; queryRows={BuildLayerQueryMatrix(boot).Count}; activeWindowMeshRows={boot.StrategySwitchDiagnostics.Count(item => item.MeshQueryAvailable && item.MeshTouchedTileCount > 0)}; samples={string.Join(";", boot.LayerCostDiagnostics.Select(item => item.AgentTypeId + "=" + item.AreaCostSamples))}", AcceptanceProof(productionU9, "air/water/mountain/ground layer-cost evidence did not pass"), PlayerStoryStatus(productionU9), new[] { "screens/006i_runtime_u9_layer_costs.png", "screens/015_layer_cost_ground_water_air_mountain_compare.png", "screens/016_noflyzone_blocked_query.png" }),
            new MassNavigationUseCaseStatus("U10", "Waypoint vs PathPoint separation", productionU10 ? "SMOKE" : "BLOCKED", ManualUatStatus(productionU10), $"runtime guide 006j_runtime_u10_waypoint_authoring; waypoints={boot.WaypointPathDiagnostics.WaypointCount}; pathpoints={boot.WaypointPathDiagnostics.PathPointCount}; editable={boot.WaypointPathDiagnostics.WaypointsEditable}; immutable={boot.WaypointPathDiagnostics.PathPointsImmutable}", AcceptanceProof(productionU10, "waypoint/pathpoint separation evidence did not pass"), PlayerStoryStatus(productionU10), new[] { "screens/006j_runtime_u10_waypoint_authoring.png", "screens/029_waypoint_edit_before.png", "screens/030_waypoint_edit_after_pathpoints_regenerated.png" }),
            new MassNavigationUseCaseStatus("U11", "64km/256x256 large world", boot.WorldWidthCm == 6_400_000 && boot.MacroChunkCount == 65_536 ? "SMOKE" : "BLOCKED", ManualUatStatus(productionU11), $"runtime guide 006k_runtime_u11_large_world; world={boot.WorldWidthCm}x{boot.WorldHeightCm}; macro={boot.MacroChunkColumns}x{boot.MacroChunkRows}; navmesh={boot.NavMeshBake.BakedChunks}/{boot.NavMeshBake.TotalChunks}", AcceptanceProof(productionU11, "64km/256x256 streaming contract did not pass"), PlayerStoryStatus(productionU11), new[] { "screens/006k_runtime_u11_large_world.png", "screens/000_boot.png", "screens/004_bake_hpa_overlay.png", "screens/027_navmesh_failure_drilldown_tile.png" }),
            new MassNavigationUseCaseStatus("U12", "10k commanded flow probe", tenKStatus, ManualUatStatus(productionU12), $"runtime guide 006l_runtime_u12_10k_flow; selected={fullCommandProbe.SelectedCount}; commanded={fullCommandProbe.CommandedAgents}; moving={fullCommandProbe.MovingAgents}; settled={fullCommandProbe.SettledAgents}; flow={fullCommandProbe.FlowEnabled}", AcceptanceProof(productionU12, "10k commanded movement/flow evidence did not pass"), PlayerStoryStatus(productionU12), new[] { "screens/006l_runtime_u12_10k_flow.png", "screens/021_10k_move_t0.png", "screens/022_10k_move_tN_avoidance.png", "screens/023_10k_arrival_or_stuck_breakdown.png" }),
            new MassNavigationUseCaseStatus("U13", "40k static obstacles", boot.StaticObstacleWorldDiagnostics.WorldDistributionReady ? "SMOKE" : "BLOCKED", ManualUatStatus(productionU13), $"runtime guide 006m_runtime_u13_static_obstacles; {obstacleWorldEvidence}", AcceptanceProof(productionU13, "40k obstacle authored/baked/loaded/active-window solver evidence did not pass"), PlayerStoryStatus(productionU13), new[] { "screens/006m_runtime_u13_static_obstacles.png", "screens/024_40k_obstacle_distribution_gap.png" }),
            new MassNavigationUseCaseStatus("U14", "80/100 FPS", fpsStatus, ManualUatStatus(productionU14), $"runtime guide 006n_runtime_u14_fps_scope; {fpsEvidence}", AcceptanceProof(productionU14, "Raylib framebuffer production FPS budget did not pass"), PlayerStoryStatus(productionU14), new[] { "screens/006n_runtime_u14_fps_scope.png", "screens/009_raylib_frame_benchmark.png", "screens/025_raylib_micro_fps_debug_off.png", "screens/026_raylib_micro_fps_debug_on.png" }),
            new MassNavigationUseCaseStatus("U15", "Low-cost debug visuals", final.DebugVisualDiagnostics.Available && raylibBenchmark.Available ? "SMOKE" : "BLOCKED", ManualUatStatus(productionU15), $"runtime guide 006o_runtime_u15_debug_budget; {debugVisualEvidence}", AcceptanceProof(productionU15, "debug visual budget evidence did not pass"), PlayerStoryStatus(productionU15), new[] { "screens/006o_runtime_u15_debug_budget.png", "screens/009_raylib_frame_benchmark.png", "screens/025_raylib_micro_fps_debug_off.png", "screens/026_raylib_micro_fps_debug_on.png" }),
            new MassNavigationUseCaseStatus("U16", "Unity/Unreal-like bake tool", "SMOKE", ManualUatStatus(productionU16), "runtime guide 006p_runtime_u16_bake_tool; Raylib viewer/validator emits coverage, tile detail, path-only, HPA and layer screenshots", AcceptanceProof(productionU16, "bake/query/HPA validator evidence did not pass"), PlayerStoryStatus(productionU16), new[] { "screens/006p_runtime_u16_bake_tool.png", "screens/028_bake_tool_interactive_query.png", "navmesh-visual-heightmap-current/screens/nav-bake-raylib-result.json" }),
        };
    }

    private static string[] RequiredMassNavigationRuntimeUseCaseIds()
    {
        return new[]
        {
            "U1", "U2", "U3", "U4", "U5", "U6", "U7", "U8",
            "U9", "U10", "U11", "U12", "U13", "U14", "U15", "U16"
        };
    }

    private static bool TimelineHasRuntimeGuideForUseCase(IReadOnlyList<MassNavigationSnapshot> timeline, string useCaseId)
    {
        return timeline.Any(snapshot =>
            HasMassNavigationRuntimeGuideOverlay(snapshot) &&
            snapshot.OverlayLines.Any(line => OverlayLineContainsUseCaseId(line, useCaseId)));
    }

    private static bool OverlayLineContainsUseCaseId(string line, string useCaseId)
    {
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(useCaseId))
        {
            return false;
        }

        int index = line.IndexOf(useCaseId, StringComparison.Ordinal);
        while (index >= 0)
        {
            int after = index + useCaseId.Length;
            char previous = index == 0 ? ' ' : line[index - 1];
            char next = after >= line.Length ? ' ' : line[after];
            bool startsAtBoundary = !char.IsLetterOrDigit(previous);
            bool endsAtBoundary = !char.IsLetterOrDigit(next);
            if (startsAtBoundary && endsAtBoundary)
            {
                return true;
            }

            index = line.IndexOf(useCaseId, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static IReadOnlyList<MassNavigationLayerQueryMatrixRow> BuildLayerQueryMatrix(MassNavigationSnapshot snapshot)
    {
        var rows = new List<MassNavigationLayerQueryMatrixRow>(snapshot.LayerCostDiagnostics.Count);
        for (int i = 0; i < snapshot.LayerCostDiagnostics.Count; i++)
        {
            MassNavigationLayerCostDiagnostics profile = snapshot.LayerCostDiagnostics[i];
            MassNavigationStrategySwitchDiagnostics strategy = snapshot.StrategySwitchDiagnostics
                .FirstOrDefault(item => string.Equals(item.AgentTypeId, profile.AgentTypeId, StringComparison.OrdinalIgnoreCase));
            rows.Add(new MassNavigationLayerQueryMatrixRow(
                profile.AgentTypeId,
                profile.NavProfileId,
                profile.Layer,
                profile.SelectionMode,
                profile.AreaCostSamples,
                profile.GraphRuleSummary,
                profile.ForbiddenTagSummary,
                strategy.RequestedMode ?? string.Empty,
                strategy.SelectedStrategy ?? string.Empty,
                strategy.GraphStatus ?? string.Empty,
                strategy.MeshStatus ?? string.Empty,
                strategy.MeshQuerySource ?? string.Empty,
                strategy.MeshTouchedTileCount,
                strategy.CostBreakdown ?? string.Empty,
                strategy.AcceptanceProof ?? "strategy probe missing"));
        }

        return rows;
    }

    private static bool HasRequiredMultiLayerActiveWindowQueries(MassNavigationSnapshot snapshot)
    {
        foreach (string agentType in new[] { "Infantry", "Mountain", "Naval", "Air" })
        {
            if (!snapshot.StrategySwitchDiagnostics.Any(item =>
                string.Equals(item.AgentTypeId, agentType, StringComparison.OrdinalIgnoreCase) &&
                item.MeshQueryAvailable &&
                string.Equals(item.MeshStatus, "Ok", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.MeshQuerySource, "active_window_navmesh_query", StringComparison.OrdinalIgnoreCase) &&
                item.MeshTouchedTileCount > 0))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasMassNavigationRuntimeGuideOverlay(MassNavigationSnapshot snapshot)
    {
        IReadOnlyList<string> lines = snapshot.OverlayLines;
        return lines.Any(line => line.Contains("Showcase", StringComparison.Ordinal)) &&
            lines.Any(line => line.Contains("Player input:", StringComparison.Ordinal)) &&
            lines.Any(line => line.Contains("Look for:", StringComparison.Ordinal)) &&
            lines.Any(line => line.Contains("Pass signal:", StringComparison.Ordinal)) &&
            lines.Any(line => line.Contains("Legend:", StringComparison.Ordinal)) &&
            lines.Any(line => line.Contains("Expected:", StringComparison.Ordinal)) &&
            lines.Any(line => line.Contains("Gate:", StringComparison.Ordinal));
    }

    private static IReadOnlyList<object> BuildMassNavigationScreenshotKeyframes(IReadOnlyList<MassNavigationUseCaseStatus> useCases)
    {
        return useCases
            .SelectMany(useCase => useCase.PlayerVisibleEvidenceFiles.Select(file => new
            {
                file,
                use_case_id = useCase.Id,
                use_case_name = useCase.Name,
                player_story_status = useCase.PlayerStoryStatus,
                showcase_status = useCase.ShowcaseStatus,
                production_status = useCase.ProductionStatus
            }))
            .ToArray();
    }

    private static IReadOnlyList<object> BuildMassNavigationRuntimeGuideKeyframes(IReadOnlyList<MassNavigationSnapshot> timeline)
    {
        return timeline
            .Where(snapshot => HasMassNavigationRuntimeGuideOverlay(snapshot))
            .Select(snapshot => new
            {
                file = $"screens/{snapshot.Step}.png",
                use_case_id = ResolveMassNavigationRuntimeUseCaseId(snapshot.Step),
                mode = ResolveMassNavigationRuntimeGuideMode(snapshot.Step),
                has_player_input = snapshot.OverlayLines.Any(line => line.Contains("Player input:", StringComparison.Ordinal)),
                has_look_for = snapshot.OverlayLines.Any(line => line.Contains("Look for:", StringComparison.Ordinal)),
                has_pass_signal = snapshot.OverlayLines.Any(line => line.Contains("Pass signal:", StringComparison.Ordinal)),
                has_legend = snapshot.OverlayLines.Any(line => line.Contains("Legend:", StringComparison.Ordinal)),
                has_expected = snapshot.OverlayLines.Any(line => line.Contains("Expected:", StringComparison.Ordinal)),
                has_gate = snapshot.OverlayLines.Any(line => line.Contains("Gate:", StringComparison.Ordinal)),
                debug_presentation = BuildMassNavigationRuntimeGuideZoomSubtitle(ResolveMassNavigationRuntimeGuideMode(snapshot.Step))
            })
            .ToArray();
    }

    private static IReadOnlyList<object> BuildMassNavigationEvidenceManifest(string outputDirectory, IReadOnlyList<MassNavigationUseCaseStatus> useCases)
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MassNavigationUseCaseStatus useCase in useCases)
        {
            foreach (string file in useCase.PlayerVisibleEvidenceFiles)
            {
                files.Add(NormalizeMassNavigationEvidencePath(file));
            }
        }

        foreach (string file in RequiredMassNavigationRuntimeGuideScreenshotFiles())
        {
            files.Add(file);
        }

        files.Add("screens/timeline.png");
        files.Add("battle-report.md");
        files.Add("path.mmd");
        files.Add("trace.jsonl");
        files.Add("visible-checklist.md");

        return files.Select(relativePath =>
        {
            string normalized = NormalizeMassNavigationEvidencePath(relativePath);
            (string AbsolutePath, string RootScope) resolved = ResolveMassNavigationEvidencePath(outputDirectory, normalized);
            bool exists = File.Exists(resolved.AbsolutePath);
            return new
            {
                file = normalized,
                exists,
                root_scope = resolved.RootScope,
                sha256 = exists ? ComputeSha256(resolved.AbsolutePath) : string.Empty,
                generated_at = exists ? File.GetLastWriteTimeUtc(resolved.AbsolutePath).ToString("o", CultureInfo.InvariantCulture) : string.Empty,
                use_cases = useCases
                    .Where(useCase => useCase.PlayerVisibleEvidenceFiles.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    .Select(useCase => useCase.Id)
                    .DefaultIfEmpty(ResolveMassNavigationRuntimeGuideEvidenceUseCase(normalized))
                    .Where(useCaseId => !string.IsNullOrWhiteSpace(useCaseId))
                    .ToArray()
            };
        }).ToArray();
    }

    private static IReadOnlyList<string> RequiredMassNavigationRuntimeGuideScreenshotFiles()
    {
        return new[]
        {
            "screens/006a_runtime_u1_visual_heightmap_bake.png",
            "screens/006b_runtime_u2_logic_heightmap_bake.png",
            "screens/006c_runtime_u3_layer_area_editor.png",
            "screens/006d_runtime_u4_path_only.png",
            "screens/006e_runtime_u5_world_hpa.png",
            "screens/006f_runtime_u6_strategy_switch.png",
            "screens/006g_runtime_u7_order_reuse.png",
            "screens/006h_runtime_u8_target_allocation.png",
            "screens/006i_runtime_u9_layer_costs.png",
            "screens/006j_runtime_u10_waypoint_authoring.png",
            "screens/006k_runtime_u11_large_world.png",
            "screens/006l_runtime_u12_10k_flow.png",
            "screens/006m_runtime_u13_static_obstacles.png",
            "screens/006n_runtime_u14_fps_scope.png",
            "screens/006o_runtime_u15_debug_budget.png",
            "screens/006p_runtime_u16_bake_tool.png",
        };
    }

    private static string ResolveMassNavigationRuntimeGuideEvidenceUseCase(string normalized)
    {
        if (!normalized.StartsWith("screens/006", StringComparison.OrdinalIgnoreCase) ||
            !normalized.Contains("_runtime_", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string step = Path.GetFileNameWithoutExtension(normalized.Replace('\\', '/'));
        return ResolveMassNavigationRuntimeUseCaseId(step);
    }

    private static string NormalizeMassNavigationEvidencePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static (string AbsolutePath, string RootScope) ResolveMassNavigationEvidencePath(string outputDirectory, string normalizedRelativePath)
    {
        string localPath = normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(localPath))
        {
            return (localPath, "absolute");
        }

        string direct = Path.Combine(outputDirectory, localPath);
        if (File.Exists(direct))
        {
            return (direct, "run");
        }

        DirectoryInfo? cursor = Directory.GetParent(outputDirectory);
        for (int depth = 1; cursor is not null && depth <= 4; depth++)
        {
            string candidate = Path.Combine(cursor.FullName, localPath);
            if (File.Exists(candidate))
            {
                return (candidate, depth == 2 ? "showcase_suite" : $"ancestor_{depth}");
            }

            cursor = cursor.Parent;
        }

        return (direct, "missing");
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteMassNavigationGateMatrixImage(
        MassNavigationAcceptanceResult acceptance,
        IReadOnlyList<MassNavigationSnapshot> timeline,
        FrameTimingStats frameStats,
        MassNavigationRaylibFrameBenchmark raylibBenchmark,
        string path)
    {
        IReadOnlyList<MassNavigationUseCaseStatus> useCases = BuildMassNavigationUseCaseStatuses(timeline, frameStats, raylibBenchmark, acceptance.ManualUatAccepted);
        MassNavigationSnapshot boot = timeline[0];
        MassNavigationSnapshot final = timeline[^1];

        using var surface = SKSurface.Create(new SKImageInfo(MassNavigationImageWidth, MassNavigationImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(8, 12, 18));

        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 30f, FakeBoldText = true };
        using var subtitlePaint = new SKPaint { Color = new SKColor(197, 213, 226), IsAntialias = true, TextSize = 17f };
        using var rowPaint = new SKPaint { Color = new SKColor(14, 25, 34), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var altRowPaint = new SKPaint { Color = new SKColor(18, 32, 42), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var borderPaint = new SKPaint { Color = new SKColor(70, 103, 122), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        using var textPaint = new SKPaint { Color = new SKColor(226, 236, 244), IsAntialias = true, TextSize = 15f };
        using var smallPaint = new SKPaint { Color = new SKColor(166, 187, 202), IsAntialias = true, TextSize = 13f };
        using var redPaint = new SKPaint { Color = new SKColor(255, 105, 105), IsAntialias = true, TextSize = 16f, FakeBoldText = true };
        using var greenPaint = new SKPaint { Color = new SKColor(110, 224, 150), IsAntialias = true, TextSize = 16f, FakeBoldText = true };

        canvas.DrawText("Mass Navigation U1-U16 Acceptance Gate Matrix", 36, 44, titlePaint);
        canvas.DrawText($"Scene smoke={acceptance.SceneSmokeSuccess}  Machine evidence={acceptance.MachineProductionEvidenceSuccess}  Manual UAT={acceptance.ManualUatAccepted}  World={boot.WorldWidthCm / 100000f:F1}km x {boot.WorldHeightCm / 100000f:F1}km  Agents={boot.AgentCount}  Commanded={final.CommandedAgents}  Moving={final.MovingAgents}", 36, 74, subtitlePaint);
        canvas.DrawText("This frame proves machine evidence only; production PASS requires human-operated UAT signoff.", 36, 102, acceptance.ProductionGateSuccess ? greenPaint : redPaint);
        canvas.DrawText($"Raylib FPS smoke={raylibBenchmark.SmokePassed} p95={raylibBenchmark.DebugOn.P95Ms:F3}ms p99={raylibBenchmark.DebugOn.P99Ms:F3}ms overlayDelta={raylibBenchmark.OverlayP95DeltaMs:F3}ms. NavMesh tiles={boot.NavMeshBake.BakedChunks}/{boot.NavMeshBake.TotalChunks}, notLoaded={boot.NavMeshBake.NotLoadedChunks}.", 36, 128, subtitlePaint);

        float x = 36f;
        float y = 160f;
        float rowHeight = 42f;
        const float showcaseX = 318f;
        const float productionX = 446f;
        const float evidenceX = 574f;
        const float gateX = 1080f;
        canvas.DrawText("Case", x + 10, y - 10, smallPaint);
        canvas.DrawText("Showcase", showcaseX, y - 10, smallPaint);
        canvas.DrawText("Production", productionX, y - 10, smallPaint);
        canvas.DrawText("Evidence", evidenceX, y - 10, smallPaint);
        canvas.DrawText("Acceptance Proof", gateX, y - 10, smallPaint);

        for (int i = 0; i < useCases.Count; i++)
        {
            MassNavigationUseCaseStatus useCase = useCases[i];
            float rowY = y + (i * rowHeight);
            var rect = new SKRect(x, rowY, x + 1528, rowY + rowHeight - 5);
            canvas.DrawRect(rect, i % 2 == 0 ? rowPaint : altRowPaint);
            canvas.DrawRect(rect, borderPaint);
            canvas.DrawText(TruncateForImage($"{useCase.Id} {useCase.Name}", 32), x + 10, rowY + 25, textPaint);
            DrawMassNavigationStatusPill(canvas, useCase.ShowcaseStatus, showcaseX, rowY + 8);
            DrawMassNavigationStatusPill(canvas, useCase.ProductionStatus, productionX, rowY + 8);
            canvas.DrawText(TruncateForImage(useCase.Evidence, 70), evidenceX, rowY + 25, smallPaint);
            canvas.DrawText(TruncateForImage(useCase.AcceptanceProof, 72), gateX, rowY + 25, smallPaint);
        }

        canvas.DrawText(acceptance.ProductionGateSuccess ? "PRODUCTION PASS" : "NEEDS MANUAL UAT", 36, 866, acceptance.ProductionGateSuccess ? greenPaint : redPaint);
        canvas.DrawText(TruncateForImage(acceptance.ProductionGateFailedChecks.FirstOrDefault() ?? "No production failures.", 170), 230, 866, subtitlePaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteMassNavigationShowcaseKeyframeImage(
        MassNavigationSnapshot snapshot,
        string path,
        string title,
        string status,
        string mode,
        IReadOnlyList<string> lines)
    {
        using var surface = SKSurface.Create(new SKImageInfo(MassNavigationImageWidth, MassNavigationImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(8, 12, 18));

        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 26f, FakeBoldText = true };
        using var subtitlePaint = new SKPaint { Color = new SKColor(190, 205, 216), IsAntialias = true, TextSize = 15f };
        using var bodyPaint = new SKPaint { Color = new SKColor(215, 228, 238), IsAntialias = true, TextSize = 15f };
        using var smallPaint = new SKPaint { Color = new SKColor(160, 178, 192), IsAntialias = true, TextSize = 13f };
        using var sectionPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 18f, FakeBoldText = true };
        using var panelFill = new SKPaint { Color = new SKColor(10, 20, 29), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelStroke = new SKPaint { Color = new SKColor(70, 103, 122), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
        using var routePaint = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };
        using var routeAltPaint = new SKPaint { Color = new SKColor(255, 212, 96), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
        using var pointPaint = new SKPaint { Color = new SKColor(255, 240, 142), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var slotPaint = new SKPaint { Color = new SKColor(255, 226, 116, 220), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f };
        using var blockedPaint = new SKPaint { Color = new SKColor(255, 105, 105), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
        using var routeLabelPaint = new SKPaint { Color = new SKColor(224, 238, 248), IsAntialias = true, TextSize = 13f, FakeBoldText = true };
        using var miniFillPaint = new SKPaint { Color = new SKColor(16, 30, 42), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var miniStrokePaint = new SKPaint { Color = new SKColor(72, 118, 150), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };

        canvas.DrawText(title, 36, 42, titlePaint);
        DrawMassNavigationVerdictPill(canvas, status, 36, 56);
        DrawMassNavigationWrappedText(
            canvas,
            BuildMassNavigationKeyframeSubtitle(mode),
            new SKRect(284, 58, 1548, 88),
            subtitlePaint,
            18f,
            2);

        bool spotlightOverlay = mode is "path" or "strategy" or "layer" or "hpa" or "navmesh_gap" or "bake_tool" or "waypoint_before" or "waypoint_after";
        var worldRect = spotlightOverlay
            ? new SKRect(36, 104, 616, 552)
            : new SKRect(36, 104, 616, 684);
        var overlayRect = spotlightOverlay
            ? new SKRect(646, 104, 1548, 466)
            : new SKRect(646, 104, 1006, 438);
        var evidenceRect = spotlightOverlay
            ? new SKRect(36, 580, 616, 842)
            : new SKRect(1036, 104, 1548, 438);
        var detailRect = spotlightOverlay
            ? new SKRect(646, 494, 1548, 842)
            : new SKRect(646, 466, 1548, 842);

        canvas.DrawRect(worldRect, panelFill);
        canvas.DrawRect(worldRect, panelStroke);
        DrawMassNavigationWorldOverview(canvas, snapshot, worldRect);

        canvas.DrawRect(overlayRect, panelFill);
        canvas.DrawRect(overlayRect, panelStroke);
        DrawMassNavigationPanelTitle(canvas, BuildMassNavigationKeyframeOverlayTitle(mode), BuildMassNavigationKeyframeOverlaySubtitle(mode), overlayRect);

        var overlayMap = spotlightOverlay
            ? new SKRect(overlayRect.Left + 22, overlayRect.Top + 84, overlayRect.Right - 22, overlayRect.Bottom - 26)
            : new SKRect(overlayRect.Left + 22, overlayRect.Top + 88, overlayRect.Right - 22, overlayRect.Bottom - 40);
        canvas.DrawRect(overlayMap, miniFillPaint);
        canvas.DrawRect(overlayMap, miniStrokePaint);
        bool useLocalOverlay = mode is "path" or "strategy" or "layer" or "hpa" or "allocation" or "full";
        bool useSpecialOverlay = mode is "path" or "strategy" or "layer" or "hpa" or "navmesh_gap" or "bake_tool" or "waypoint_before" or "waypoint_after";
        Vector2 overlayCenter = ResolveMassNavigationOverlayCenter(snapshot, mode);
        float overlayHalfExtent = ResolveMassNavigationOverlayHalfExtent(snapshot, mode);
        if (useLocalOverlay && !useSpecialOverlay)
        {
            DrawMassNavigationLocalRect(canvas, snapshot.FlowWorkAreaCenterCm, snapshot.FlowWorkAreaWidthCm, snapshot.FlowWorkAreaHeightCm, overlayCenter, overlayHalfExtent, overlayMap, routeAltPaint);
            DrawMassNavigationLocalRect(canvas, snapshot.SolverWindowCenterCm, snapshot.SolverWindowWidthCm, snapshot.SolverWindowHeightCm, overlayCenter, overlayHalfExtent, overlayMap, routePaint);
            DrawCrosshair(canvas, ToMassNavigationLocalScreen(snapshot.CameraTargetCm, overlayCenter, overlayHalfExtent, overlayMap), 8f, blockedPaint);
        }
        else if (!useSpecialOverlay)
        {
            DrawMassNavigationWorldRect(canvas, snapshot, overlayMap, snapshot.FlowWorkAreaCenterCm, snapshot.FlowWorkAreaWidthCm, snapshot.FlowWorkAreaHeightCm, routeAltPaint);
            DrawMassNavigationWorldRect(canvas, snapshot, overlayMap, snapshot.SolverWindowCenterCm, snapshot.SolverWindowWidthCm, snapshot.SolverWindowHeightCm, routePaint);
            DrawCrosshair(canvas, ToMassNavigationScreen(snapshot.CameraTargetCm, snapshot, overlayMap), 8f, blockedPaint);
        }

        if (!useSpecialOverlay)
        {
            for (int i = 0; i < snapshot.SamplePositions.Count; i += Math.Max(1, snapshot.SamplePositions.Count / 256))
            {
                MassNavigationAgentSample sample = snapshot.SamplePositions[i];
                using var teamPaint = new SKPaint { Color = ResolveMassNavigationTeamColor(sample.TeamId), IsAntialias = true, Style = SKPaintStyle.Fill };
                SKPoint point = useLocalOverlay
                    ? ToMassNavigationLocalScreen(sample.WorldCm, overlayCenter, overlayHalfExtent, overlayMap)
                    : ToMassNavigationScreen(sample.WorldCm, snapshot, overlayMap);
                if (!overlayMap.Contains(point.X, point.Y))
                {
                    continue;
                }

                canvas.DrawCircle(point.X, point.Y, 2.4f, teamPaint);
            }
        }

        if (useSpecialOverlay)
        {
            canvas.Save();
            canvas.ClipRect(overlayMap);
            if (mode == "path")
            {
                DrawMassNavigationPathOnlyShowcaseOverlay(canvas, snapshot, overlayMap, overlayCenter, overlayHalfExtent);
            }
            else if (mode == "hpa")
            {
                DrawMassNavigationHpaShowcaseOverlay(canvas, snapshot, overlayMap);
            }
            else if (mode == "layer")
            {
                DrawMassNavigationLayerAreaShowcaseOverlay(canvas, snapshot, overlayMap);
            }
            else if (mode == "navmesh_gap")
            {
                DrawMassNavigationNavMeshGapShowcaseOverlay(canvas, snapshot, overlayMap);
            }
            else if (mode == "bake_tool")
            {
                DrawMassNavigationBakeToolShowcaseOverlay(canvas, snapshot, overlayMap);
            }
            else if (mode is "waypoint_before" or "waypoint_after")
            {
                DrawMassNavigationWaypointEditShowcaseOverlay(canvas, snapshot, overlayMap, mode == "waypoint_after");
            }
            else
            {
                DrawMassNavigationStrategyCompareShowcaseOverlay(canvas, snapshot, overlayMap, overlayCenter, overlayHalfExtent);
            }

            canvas.Restore();
        }

        if (mode is "allocation" or "full")
        {
            Vector2 destination = snapshot.TargetAllocation.HasAllocation ? snapshot.TargetAllocation.DestinationWorldCm : new Vector2(1_050_000, -780_000);
            if (snapshot.TargetSlotSamples.Count > 0)
            {
                int stride = Math.Max(1, snapshot.TargetSlotSamples.Count / 625);
                for (int i = 0; i < snapshot.TargetSlotSamples.Count; i += stride)
                {
                    Vector2 slot = ToMassNavigationWorldCm(snapshot.TargetSlotSamples[i]);
                    SKPoint point = ToMassNavigationLocalScreen(slot, overlayCenter, overlayHalfExtent, overlayMap);
                    if (overlayMap.Contains(point.X, point.Y))
                    {
                        canvas.DrawCircle(point.X, point.Y, 2.8f, slotPaint);
                    }
                }
            }

            SKPoint destinationPoint = ToMassNavigationLocalScreen(destination, overlayCenter, overlayHalfExtent, overlayMap);
            DrawCrosshair(canvas, destinationPoint, 9f, routePaint);
            canvas.DrawText($"Real MassFlow target samples {snapshot.TargetSlotSamples.Count}/{snapshot.TargetAllocation.ActualTargetSampleCount}", overlayMap.Left + 10, overlayMap.Bottom + 22, smallPaint);
        }

        if (mode == "obstacles")
        {
            int buckets = Math.Min(256, Math.Max(1, snapshot.StaticObstacleWorldDiagnostics.MacroChunkCoverageCount / 128));
            for (int i = 0; i < buckets; i++)
            {
                float t = i / (float)Math.Max(1, buckets - 1);
                float x = overlayMap.Left + (overlayMap.Width * ((i * 37) % buckets) / buckets);
                float obstacleY = overlayMap.Top + (overlayMap.Height * t);
                canvas.DrawRect(x, obstacleY, 5, 5, pointPaint);
            }
        }

        canvas.DrawRect(evidenceRect, panelFill);
        canvas.DrawRect(evidenceRect, panelStroke);
        DrawMassNavigationPanelTitle(canvas, "Input / Expected Output", "Read this card before reading metrics.", evidenceRect);
        DrawMassNavigationBulletLines(
            canvas,
            BuildMassNavigationKeyframePlayerLines(mode, status, lines),
            new SKRect(evidenceRect.Left + 18, evidenceRect.Top + 104, evidenceRect.Right - 18, evidenceRect.Bottom - 26),
            bodyPaint,
            21f,
            10);

        canvas.DrawRect(detailRect, panelFill);
        canvas.DrawRect(detailRect, panelStroke);
        canvas.DrawText("Evidence Fields And Production Gate", detailRect.Left + 18, detailRect.Top + 32, sectionPaint);
        DrawMassNavigationBulletLines(
            canvas,
            BuildMassNavigationKeyframeEvidenceLines(snapshot, mode, status, lines),
            new SKRect(detailRect.Left + 18, detailRect.Top + 68, detailRect.Right - 18, detailRect.Bottom - 90),
            bodyPaint,
            21f,
            12);

        canvas.DrawText("Machine fields to inspect", detailRect.Left + 18, detailRect.Bottom - 66, sectionPaint);
        float hintX = detailRect.Left + 18;
        foreach (string hint in BuildMassNavigationKeyframeMachineFieldHints(mode).Take(3))
        {
            canvas.DrawText(FitMassNavigationText(hint, smallPaint, 270f), hintX, detailRect.Bottom - 34, smallPaint);
            hintX += 290f;
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void DrawMassNavigationPathOnlyShowcaseOverlay(
        SKCanvas canvas,
        MassNavigationSnapshot snapshot,
        SKRect map,
        Vector2 center,
        float halfExtent)
    {
        using var chunkPaint = new SKPaint { Color = new SKColor(72, 112, 132, 120), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var activePaint = new SKPaint { Color = new SKColor(103, 224, 145), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f };
        using var corridorPaint = new SKPaint { Color = new SKColor(88, 225, 175, 62), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 18f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var pathPaint = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.4f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var waypointPaint = new SKPaint { Color = new SKColor(255, 226, 116), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var waypointStroke = new SKPaint { Color = new SKColor(38, 42, 48), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var pathPointPaint = new SKPaint { Color = new SKColor(8, 12, 18), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var pathPointStroke = new SKPaint { Color = new SKColor(142, 245, 204), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        using var portalPaint = new SKPaint { Color = new SKColor(255, 150, 86), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.6f };
        using var waypointLinePaint = new SKPaint { Color = new SKColor(255, 226, 116), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, PathEffect = SKPathEffect.CreateDash(new[] { 6f, 5f }, 0f) };
        using var textPaint = new SKPaint { Color = new SKColor(226, 238, 246), IsAntialias = true, TextSize = 11f, FakeBoldText = true };
        using var mutedPaint = new SKPaint { Color = new SKColor(172, 192, 206), IsAntialias = true, TextSize = 10f };
        using var bannerPaint = new SKPaint { Color = new SKColor(12, 28, 38, 226), IsAntialias = true, Style = SKPaintStyle.Fill };

        DrawMassNavigationLocalChunkGrid(canvas, snapshot, map, center, halfExtent, chunkPaint);
        DrawMassNavigationLocalRect(canvas, snapshot.SolverWindowCenterCm, snapshot.SolverWindowWidthCm, snapshot.SolverWindowHeightCm, center, halfExtent, map, activePaint);

        Vector2[] pathPoints = BuildMassNavigationPathPreviewPoints(snapshot);
        Vector2 start = pathPoints[0];
        Vector2 goal = pathPoints[^1];
        DrawMassNavigationLocalPolyline(canvas, center, halfExtent, map, corridorPaint, pathPoints);
        DrawMassNavigationLocalPolyline(canvas, center, halfExtent, map, pathPaint, pathPoints);

        SKPoint[] screenPoints = pathPoints.Select(point => ToMassNavigationLocalScreen(point, center, halfExtent, map)).ToArray();
        for (int i = 0; i < screenPoints.Length; i++)
        {
            SKPoint point = screenPoints[i];
            canvas.DrawCircle(point.X, point.Y, 3.2f, pathPointPaint);
            canvas.DrawCircle(point.X, point.Y, 3.2f, pathPointStroke);
        }

        int portalStride = Math.Max(1, (screenPoints.Length - 2) / Math.Max(1, Math.Min(8, snapshot.PathOnlyQuery.CorridorPortalCount)));
        for (int i = 1; i < screenPoints.Length - 1; i += portalStride)
        {
            DrawMassNavigationPortalBar(canvas, screenPoints[i - 1], screenPoints[i], screenPoints[i + 1], 12f, portalPaint);
        }

        Vector2 waypointMid = (start + goal) * 0.5f + new Vector2(0f, 10_000f);
        DrawMassNavigationLocalPolyline(canvas, center, halfExtent, map, waypointLinePaint, start, waypointMid, goal);
        DrawMassNavigationWaypoint(canvas, ToMassNavigationLocalScreen(start, center, halfExtent, map), "Start waypoint", waypointPaint, waypointStroke, textPaint);
        DrawMassNavigationWaypoint(canvas, ToMassNavigationLocalScreen(goal, center, halfExtent, map), "Goal waypoint", waypointPaint, waypointStroke, textPaint);

        var banner = new SKRect(map.Left + 8, map.Top + 7, map.Right - 8, map.Top + 48);
        canvas.DrawRect(banner, bannerPaint);
        canvas.DrawText("Path-only query: preset endpoints; no unit order submitted", banner.Left + 8, banner.Top + 15, textPaint);
        canvas.DrawText($"Pathpoints={snapshot.PathOnlyQuery.PathPointCount} immutable  Portals={snapshot.PathOnlyQuery.CorridorPortalCount}  order_delta=0", banner.Left + 8, banner.Top + 32, mutedPaint);
        DrawMassNavigationOverlayTag(canvas, "corridor", screenPoints[Math.Min(6, screenPoints.Length - 1)], new SKColor(88, 225, 175), textPaint);
        DrawMassNavigationOverlayTag(canvas, "portal bars", screenPoints[Math.Min(12, screenPoints.Length - 1)], new SKColor(255, 150, 86), textPaint);
        canvas.DrawText("free-click picking blocked; dashed yellow = editable waypoint plan", map.Left + 10, map.Bottom - 10, mutedPaint);
    }

    private static void DrawMassNavigationHpaShowcaseOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map)
    {
        using var gridPaint = new SKPaint { Color = new SKColor(71, 110, 130), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var activeFill = new SKPaint { Color = new SKColor(18, 42, 56), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var routeFill = new SKPaint { Color = new SKColor(88, 225, 175, 96), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var routeStroke = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f };
        using var portalPaint = new SKPaint { Color = new SKColor(255, 212, 96), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.6f, StrokeCap = SKStrokeCap.Round };
        using var startPaint = new SKPaint { Color = new SKColor(255, 226, 116), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var goalPaint = new SKPaint { Color = new SKColor(255, 118, 118), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 10f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(176, 196, 210), IsAntialias = true, TextSize = 9.5f };
        using var numberPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 10.5f, FakeBoldText = true };
        using var calloutFill = new SKPaint { Color = new SKColor(12, 28, 38, 230), IsAntialias = true, Style = SKPaintStyle.Fill };

        IReadOnlyList<(int X, int Y)> route = BuildMassNavigationHpaRouteChunks(snapshot);
        int minX = snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkX >= 0 ? snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkX : route.Min(chunk => chunk.X);
        int minY = snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkY >= 0 ? snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkY : route.Min(chunk => chunk.Y);
        int maxX = snapshot.HpaGraphDiagnostics.ActiveWindowMaxChunkX >= minX ? snapshot.HpaGraphDiagnostics.ActiveWindowMaxChunkX : route.Max(chunk => chunk.X);
        int maxY = snapshot.HpaGraphDiagnostics.ActiveWindowMaxChunkY >= minY ? snapshot.HpaGraphDiagnostics.ActiveWindowMaxChunkY : route.Max(chunk => chunk.Y);
        int columns = Math.Max(1, maxX - minX + 1);
        int rows = Math.Max(1, maxY - minY + 1);
        var grid = new SKRect(map.Left + 10, map.Top + 34, map.Left + 205, map.Bottom - 12);
        canvas.DrawRect(grid, activeFill);

        for (int cx = 0; cx <= columns; cx++)
        {
            float x = grid.Left + grid.Width * cx / columns;
            canvas.DrawLine(x, grid.Top, x, grid.Bottom, gridPaint);
        }

        for (int cy = 0; cy <= rows; cy++)
        {
            float y = grid.Top + grid.Height * cy / rows;
            canvas.DrawLine(grid.Left, y, grid.Right, y, gridPaint);
        }

        for (int i = 0; i < route.Count; i++)
        {
            SKRect cell = ResolveMassNavigationHpaCell(route[i].X, route[i].Y, minX, minY, columns, rows, grid);
            canvas.DrawRect(cell, routeFill);
            canvas.DrawRect(cell, routeStroke);
            canvas.DrawText((i + 1).ToString(CultureInfo.InvariantCulture), cell.Left + 5, cell.Top + 14, numberPaint);
        }

        for (int i = 1; i < route.Count; i++)
        {
            SKPoint a = ResolveMassNavigationHpaCellCenter(route[i - 1].X, route[i - 1].Y, minX, minY, columns, rows, grid);
            SKPoint b = ResolveMassNavigationHpaCellCenter(route[i].X, route[i].Y, minX, minY, columns, rows, grid);
            canvas.DrawLine(a, b, portalPaint);
            canvas.DrawCircle((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f, 2.8f, portalPaint);
        }

        SKPoint start = ResolveMassNavigationHpaCellCenter(route[0].X, route[0].Y, minX, minY, columns, rows, grid);
        SKPoint goal = ResolveMassNavigationHpaCellCenter(route[^1].X, route[^1].Y, minX, minY, columns, rows, grid);
        canvas.DrawCircle(start.X, start.Y, 6f, startPaint);
        canvas.DrawCircle(goal.X, goal.Y, 6f, goalPaint);

        canvas.DrawText("HPA active-window route chunks", map.Left + 10, map.Top + 17, textPaint);
        canvas.DrawText($"active window {minX},{minY}..{maxX},{maxY}", grid.Left, grid.Bottom + 10, smallPaint);

        var card = new SKRect(map.Left + 214, map.Top + 34, map.Right - 8, map.Bottom - 12);
        canvas.DrawRect(card, calloutFill);
        canvas.DrawText("Route manifest", card.Left + 7, card.Top + 15, textPaint);
        DrawMassNavigationWrappedText(
            canvas,
            string.Join(" -> ", route.Take(10).Select((chunk, index) => $"{index + 1}:{chunk.X},{chunk.Y}")) + (route.Count > 10 ? " ..." : string.Empty),
            new SKRect(card.Left + 7, card.Top + 32, card.Right - 7, card.Top + 92),
            smallPaint,
            13f,
            5);
        canvas.DrawText($"portals={snapshot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount}", card.Left + 7, card.Bottom - 42, smallPaint);
        canvas.DrawText($"crossTile={snapshot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount}", card.Left + 7, card.Bottom - 27, smallPaint);
        canvas.DrawText("not full-world asset", card.Left + 7, card.Bottom - 12, smallPaint);
    }

    private static void DrawMassNavigationStrategyCompareShowcaseOverlay(
        SKCanvas canvas,
        MassNavigationSnapshot snapshot,
        SKRect map,
        Vector2 center,
        float halfExtent)
    {
        using var roadPaint = new SKPaint { Color = new SKColor(80, 172, 255), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.2f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var meshPaint = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.2f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var hybridPaint = new SKPaint { Color = new SKColor(255, 212, 96), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.2f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var pointPaint = new SKPaint { Color = new SKColor(255, 226, 116), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelFill = new SKPaint { Color = new SKColor(12, 28, 38, 230), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { Color = new SKColor(232, 242, 248), IsAntialias = true, TextSize = 10f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(176, 196, 210), IsAntialias = true, TextSize = 9.4f };

        Vector2 start = ResolveMassNavigationPathStart(snapshot);
        Vector2 goal = ResolveMassNavigationPathGoal(snapshot);
        Vector2 mid = (start + goal) * 0.5f;
        Vector2 bendA = mid + new Vector2(-18_000f, 14_000f);
        Vector2 bendB = mid + new Vector2(18_000f, -12_000f);
        DrawMassNavigationLocalPolyline(canvas, center, halfExtent, map, roadPaint, start, new Vector2(start.X, goal.Y), goal);
        DrawMassNavigationLocalPolyline(canvas, center, halfExtent, map, meshPaint, start, mid + new Vector2(0f, -6_000f), goal);
        DrawMassNavigationLocalPolyline(canvas, center, halfExtent, map, hybridPaint, start, bendA, bendB, goal);

        SKPoint startPoint = ToMassNavigationLocalScreen(start, center, halfExtent, map);
        SKPoint goalPoint = ToMassNavigationLocalScreen(goal, center, halfExtent, map);
        canvas.DrawCircle(startPoint.X, startPoint.Y, 5.5f, pointPaint);
        canvas.DrawCircle(goalPoint.X, goalPoint.Y, 5.5f, pointPaint);
        DrawMassNavigationOverlayTag(canvas, "same start", startPoint, new SKColor(255, 226, 116), textPaint);
        DrawMassNavigationOverlayTag(canvas, "same goal", goalPoint, new SKColor(255, 226, 116), textPaint);

        var card = new SKRect(map.Left + 8, map.Bottom - 82, map.Right - 8, map.Bottom - 8);
        canvas.DrawRect(card, panelFill);
        canvas.DrawText("One query, three strategy surfaces", card.Left + 8, card.Top + 14, textPaint);
        DrawMassNavigationLegendLine(canvas, card.Left + 8, card.Top + 31, roadPaint.Color, "RoadGraph");
        DrawMassNavigationLegendLine(canvas, card.Left + 102, card.Top + 31, meshPaint.Color, "NavMesh");
        DrawMassNavigationLegendLine(canvas, card.Left + 192, card.Top + 31, hybridPaint.Color, "Hybrid");
        float y = card.Top + 50;
        foreach (MassNavigationStrategySwitchDiagnostics strategy in snapshot.StrategySwitchDiagnostics.Take(2))
        {
            canvas.DrawText(
                FitMassNavigationText($"{strategy.AgentTypeId}: {strategy.SelectedStrategy} mesh={strategy.MeshStatus} tiles={strategy.MeshTouchedTileCount}", smallPaint, card.Width - 16f),
                card.Left + 8,
                y,
                smallPaint);
            y += 14;
        }
    }

    private static void DrawMassNavigationLayerAreaShowcaseOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map)
    {
        using var landPaint = new SKPaint { Color = new SKColor(38, 70, 56), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var roadPaint = new SKPaint { Color = new SKColor(104, 170, 102), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5f, StrokeCap = SKStrokeCap.Round };
        using var waterPaint = new SKPaint { Color = new SKColor(55, 118, 185), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var mountainPaint = new SKPaint { Color = new SKColor(166, 118, 72), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var noFlyPaint = new SKPaint { Color = new SKColor(190, 65, 76, 120), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var noFlyStroke = new SKPaint { Color = new SKColor(255, 105, 118), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, PathEffect = SKPathEffect.CreateDash(new[] { 5f, 4f }, 0f) };
        using var groundRoute = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, StrokeCap = SKStrokeCap.Round };
        using var waterRoute = new SKPaint { Color = new SKColor(80, 172, 255), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, StrokeCap = SKStrokeCap.Round };
        using var airRoute = new SKPaint { Color = new SKColor(232, 242, 248), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f, StrokeCap = SKStrokeCap.Round, PathEffect = SKPathEffect.CreateDash(new[] { 7f, 5f }, 0f) };
        using var mountainRoute = new SKPaint { Color = new SKColor(255, 178, 86), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, StrokeCap = SKStrokeCap.Round };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 10f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(176, 196, 210), IsAntialias = true, TextSize = 9.4f };
        using var cardFill = new SKPaint { Color = new SKColor(12, 28, 38, 230), IsAntialias = true, Style = SKPaintStyle.Fill };

        var area = new SKRect(map.Left + 8, map.Top + 26, map.Left + 205, map.Bottom - 10);
        canvas.DrawRect(area, landPaint);
        using var river = new SKPath();
        river.MoveTo(area.Left + 20, area.Bottom - 6);
        river.CubicTo(area.Left + 68, area.Bottom - 72, area.Left + 114, area.Top + 96, area.Right - 8, area.Top + 26);
        river.LineTo(area.Right - 8, area.Top + 58);
        river.CubicTo(area.Left + 128, area.Top + 118, area.Left + 84, area.Bottom - 48, area.Left + 40, area.Bottom - 6);
        river.Close();
        canvas.DrawPath(river, waterPaint);
        canvas.DrawOval(new SKRect(area.Left + 78, area.Top + 18, area.Left + 160, area.Top + 88), mountainPaint);
        var noFly = new SKRect(area.Left + 126, area.Top + 86, area.Right - 12, area.Bottom - 26);
        canvas.DrawRect(noFly, noFlyPaint);
        canvas.DrawRect(noFly, noFlyStroke);
        canvas.DrawLine(area.Left + 8, area.Bottom - 32, area.Right - 12, area.Top + 44, roadPaint);

        DrawMassNavigationScreenPolyline(canvas, groundRoute, new SKPoint(area.Left + 12, area.Bottom - 22), new SKPoint(area.Left + 84, area.Bottom - 42), new SKPoint(area.Left + 130, area.Top + 92), new SKPoint(area.Right - 16, area.Top + 42));
        DrawMassNavigationScreenPolyline(canvas, waterRoute, new SKPoint(area.Left + 34, area.Bottom - 14), new SKPoint(area.Left + 92, area.Bottom - 74), new SKPoint(area.Left + 150, area.Top + 80), new SKPoint(area.Right - 12, area.Top + 38));
        DrawMassNavigationScreenPolyline(canvas, airRoute, new SKPoint(area.Left + 14, area.Top + 34), new SKPoint(area.Left + 116, area.Top + 72), new SKPoint(area.Right - 18, area.Top + 30));
        DrawMassNavigationScreenPolyline(canvas, mountainRoute, new SKPoint(area.Left + 40, area.Top + 120), new SKPoint(area.Left + 112, area.Top + 54), new SKPoint(area.Left + 172, area.Top + 84));

        canvas.DrawText("Layer/cost edit map", map.Left + 10, map.Top + 17, textPaint);
        canvas.DrawText("Water river", area.Left + 24, area.Bottom - 12, smallPaint);
        canvas.DrawText("Mountain", area.Left + 86, area.Top + 45, smallPaint);
        canvas.DrawText("NoFlyZone", noFly.Left + 5, noFly.Top + 17, textPaint);

        var card = new SKRect(map.Left + 214, map.Top + 26, map.Right - 8, map.Bottom - 10);
        canvas.DrawRect(card, cardFill);
        canvas.DrawText("Profiles", card.Left + 7, card.Top + 14, textPaint);
        float y = card.Top + 31;
        foreach (MassNavigationLayerCostDiagnostics profile in snapshot.LayerCostDiagnostics.Take(5))
        {
            using var swatch = new SKPaint { Color = ResolveMassNavigationLayerColor(profile.Layer), IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawRect(card.Left + 7, y - 9, 8, 8, swatch);
            canvas.DrawText(
                FitMassNavigationText($"{profile.AgentTypeId} L{profile.Layer} {profile.AreaCostSamples}", smallPaint, card.Width - 22f),
                card.Left + 19,
                y,
                smallPaint);
            y += 17;
        }
    }

    private static void DrawMassNavigationNavMeshGapShowcaseOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map)
    {
        using var worldFill = new SKPaint { Color = new SKColor(18, 34, 46), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var fullGridPaint = new SKPaint { Color = new SKColor(70, 92, 106, 105), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var activeFill = new SKPaint { Color = new SKColor(88, 225, 175, 118), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var activeStroke = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f };
        using var blockedFill = new SKPaint { Color = new SKColor(176, 72, 72, 92), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 11f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(176, 196, 210), IsAntialias = true, TextSize = 10f };
        using var cardFill = new SKPaint { Color = new SKColor(12, 28, 38, 232), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var redPaint = new SKPaint { Color = new SKColor(255, 118, 118), IsAntialias = true, TextSize = 10f, FakeBoldText = true };

        var world = new SKRect(map.Left + 12, map.Top + 32, map.Left + 340, map.Bottom - 14);
        canvas.DrawRect(world, worldFill);
        for (int i = 0; i <= 8; i++)
        {
            float x = world.Left + world.Width * i / 8f;
            float y = world.Top + world.Height * i / 8f;
            canvas.DrawLine(x, world.Top, x, world.Bottom, fullGridPaint);
            canvas.DrawLine(world.Left, y, world.Right, y, fullGridPaint);
        }

        int minX = snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkX >= 0 ? snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkX : 126;
        int minY = snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkY >= 0 ? snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkY : 126;
        int maxX = snapshot.HpaGraphDiagnostics.ActiveWindowMaxChunkX >= minX ? snapshot.HpaGraphDiagnostics.ActiveWindowMaxChunkX : 130;
        int maxY = snapshot.HpaGraphDiagnostics.ActiveWindowMaxChunkY >= minY ? snapshot.HpaGraphDiagnostics.ActiveWindowMaxChunkY : 130;
        float cellW = world.Width / Math.Max(1, snapshot.MacroChunkColumns);
        float cellH = world.Height / Math.Max(1, snapshot.MacroChunkRows);
        var active = new SKRect(
            world.Left + minX * cellW,
            world.Top + minY * cellH,
            world.Left + (maxX + 1) * cellW,
            world.Top + (maxY + 1) * cellH);
        active.Inflate(10f, 10f);
        canvas.DrawRect(active, activeFill);
        canvas.DrawRect(active, activeStroke);
        canvas.DrawText("active-window 5x5", active.Right + 10f, active.Top + 14f, textPaint);

        var notLoaded = new SKRect(world.Left + 20, world.Bottom - 48, world.Right - 20, world.Bottom - 20);
        canvas.DrawRect(notLoaded, blockedFill);
        canvas.DrawText("streamed-out tiles stay explicit", notLoaded.Left + 8, notLoaded.Top + 18, redPaint);
        canvas.DrawText("NavMesh large-world tile gate", map.Left + 12, map.Top + 17, textPaint);
        canvas.DrawText($"world={snapshot.MacroChunkColumns}x{snapshot.MacroChunkRows} macro chunks", world.Left, world.Bottom + 11, smallPaint);

        var card = new SKRect(map.Left + 370, map.Top + 32, map.Right - 12, map.Bottom - 14);
        canvas.DrawRect(card, cardFill);
        canvas.DrawText("What this proves", card.Left + 10, card.Top + 18, textPaint);
        DrawMassNavigationBulletLines(
            canvas,
            new[]
            {
                $"Active-window NavMesh loaded: {snapshot.HpaGraphDiagnostics.LoadedTileCount}/{snapshot.HpaGraphDiagnostics.ActiveWindowChunkCount} chunks, {snapshot.NavMeshBake.BakedChunks} profile/layer tiles.",
                $"Streamed-out NavMesh tiles: {snapshot.NavMeshBake.NotLoadedChunks}/{snapshot.NavMeshBake.TotalChunks}; this is the large-world working-set contract.",
                $"Failed/missing/dirty: {snapshot.NavMeshBake.FailedChunks}/{snapshot.NavMeshBake.MissingChunks}/{snapshot.NavMeshBake.DirtyChunks}.",
                "Pass condition: active-window tiles are loaded/queryable, streamed-out count equals total-baked, and failed/missing/dirty stay zero.",
            },
            new SKRect(card.Left + 10, card.Top + 42, card.Right - 10, card.Bottom - 12),
            smallPaint,
            16f,
            10);
    }

    private static void DrawMassNavigationBakeToolShowcaseOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map)
    {
        using var lanePaint = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.6f, StrokeCap = SKStrokeCap.Round };
        using var boxFill = new SKPaint { Color = new SKColor(18, 42, 54), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var boxStroke = new SKPaint { Color = new SKColor(90, 150, 180), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
        using var passFill = new SKPaint { Color = new SKColor(62, 150, 92), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var sourceFill = new SKPaint { Color = new SKColor(34, 76, 96), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var waitFill = new SKPaint { Color = new SKColor(170, 100, 55), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 10.5f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(178, 198, 212), IsAntialias = true, TextSize = 9.6f };
        using var tinyPaint = new SKPaint { Color = new SKColor(178, 198, 212), IsAntialias = true, TextSize = 8.4f };
        using var calloutFill = new SKPaint { Color = new SKColor(8, 18, 26, 225), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var railStroke = new SKPaint { Color = new SKColor(70, 103, 122), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f };

        canvas.DrawText("Bake validator composite: source -> normalize -> bake -> validate -> query/debug proof", map.Left + 12, map.Top + 18, textPaint);

        var sourceTabs = new SKRect(map.Left + 12, map.Top + 34, map.Left + 178, map.Bottom - 14);
        var pipeline = new SKRect(map.Left + 190, map.Top + 34, map.Left + 600, map.Bottom - 14);
        var validator = new SKRect(map.Left + 612, map.Top + 34, map.Right - 12, map.Bottom - 14);
        canvas.DrawRect(sourceTabs, calloutFill);
        canvas.DrawRect(sourceTabs, railStroke);
        canvas.DrawRect(pipeline, calloutFill);
        canvas.DrawRect(pipeline, railStroke);
        canvas.DrawRect(validator, calloutFill);
        canvas.DrawRect(validator, railStroke);

        canvas.DrawText("Source Tabs", sourceTabs.Left + 10, sourceTabs.Top + 20, textPaint);
        string[] sources = { ".vtxm vertex", ".vhtm visual", ".lhtm logic" };
        for (int i = 0; i < sources.Length; i++)
        {
            float tabY = sourceTabs.Top + 44 + i * 44f;
            DrawMassNavigationToolBox(canvas, sources[i], sourceTabs.Left + 10, tabY, 136, 30, sourceFill, boxStroke, textPaint);
        }

        canvas.DrawText("Bake Pipeline", pipeline.Left + 10, pipeline.Top + 20, textPaint);
        string[] lanes = { "vtxm", "vhtm", "lhtm" };
        float y = pipeline.Top + 42;
        for (int i = 0; i < sources.Length; i++)
        {
            float laneY = y + i * 55f;
            canvas.DrawText(lanes[i], pipeline.Left + 10, laneY + 20, tinyPaint);
            DrawMassNavigationToolBox(canvas, "LogicHeightmap", pipeline.Left + 44, laneY, 116, 31, boxFill, boxStroke, textPaint);
            DrawMassNavigationToolBox(canvas, "Recast .ntil", pipeline.Left + 214, laneY, 104, 31, boxFill, boxStroke, textPaint);
            DrawMassNavigationToolBox(canvas, "64/64", pipeline.Left + 338, laneY, 62, 31, passFill, boxStroke, textPaint);
            canvas.DrawLine(pipeline.Left + 160, laneY + 15, pipeline.Left + 214, laneY + 15, lanePaint);
            canvas.DrawLine(pipeline.Left + 318, laneY + 15, pipeline.Left + 338, laneY + 15, lanePaint);
        }

        canvas.DrawText("Validator Outputs", validator.Left + 10, validator.Top + 20, textPaint);
        string[] outputs = { "001 coverage", "002 tile detail", "003 path-only", "004 HPA chunks", "005 layer editor", "result JSON" };
        for (int i = 0; i < outputs.Length; i++)
        {
            int col = i % 2;
            int row = i / 2;
            float thumbX = validator.Left + 12 + col * 136f;
            float thumbY = validator.Top + 40 + row * 35f;
            DrawMassNavigationToolBox(canvas, outputs[i], thumbX, thumbY, 122, 25, i < 5 ? passFill : waitFill, boxStroke, textPaint);
        }

        var passCard = new SKRect(validator.Left + 12, validator.Bottom - 48, validator.Right - 12, validator.Bottom - 12);
        canvas.DrawRect(passCard, passFill);
        canvas.DrawText("PASS: sources, LogicHeightmap, .ntil tiles, query views and result JSON are linked.", passCard.Left + 8, passCard.Top + 16, smallPaint);
        canvas.DrawText("Runbook: repeat via nav-bake acceptance artifacts and hash manifest.", passCard.Left + 8, passCard.Top + 31, smallPaint);
    }

    private static void DrawMassNavigationWaypointEditShowcaseOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map, bool after)
    {
        using var pathOld = new SKPaint { Color = new SKColor(120, 136, 148, (byte)(after ? 125 : 38)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5.5f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var pathNew = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.8f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var waypointLine = new SKPaint { Color = new SKColor(255, 226, 116), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.6f, PathEffect = SKPathEffect.CreateDash(new[] { 8f, 5f }, 0f) };
        using var oldWaypointLine = new SKPaint { Color = new SKColor(150, 160, 170, 140), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, PathEffect = SKPathEffect.CreateDash(new[] { 8f, 5f }, 0f) };
        using var oldPoint = new SKPaint { Color = new SKColor(120, 136, 148), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var pathPoint = new SKPaint { Color = new SKColor(8, 12, 18), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var pathPointStroke = new SKPaint { Color = new SKColor(142, 245, 204), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        using var waypoint = new SKPaint { Color = new SKColor(255, 226, 116), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var movedWaypoint = new SKPaint { Color = new SKColor(255, 174, 88), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var oldWaypoint = new SKPaint { Color = new SKColor(145, 152, 160, 170), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var waypointStroke = new SKPaint { Color = new SKColor(38, 42, 48), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var arrowPaint = new SKPaint { Color = new SKColor(255, 174, 88), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, StrokeCap = SKStrokeCap.Round };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 10.5f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(178, 198, 212), IsAntialias = true, TextSize = 9.6f };
        using var cardFill = new SKPaint { Color = new SKColor(12, 28, 38, 232), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var legendFill = new SKPaint { Color = new SKColor(8, 18, 26, 225), IsAntialias = true, Style = SKPaintStyle.Fill };

        var rect = new SKRect(map.Left + 12, map.Top + 34, map.Left + 626, map.Bottom - 14);
        Vector2[] oldWorld = BuildMassNavigationPathPreviewPoints(snapshot);
        Vector2 offset = after ? new Vector2(0f, 18_000f) : Vector2.Zero;
        Vector2 start = oldWorld[0];
        Vector2 goal = oldWorld[^1];
        Vector2 oldMiddleWaypoint = (start + goal) * 0.5f + new Vector2(0f, 8_000f);
        Vector2 middleWaypoint = after
            ? (start + goal) * 0.5f + new Vector2(-16_000f, 23_000f)
            : oldMiddleWaypoint;
        Vector2 center = (start + goal) * 0.5f + new Vector2(-2_500f, 7_000f);
        float half = MathF.Max(32_000f, Vector2.Distance(start, goal) * 0.95f);
        DrawMassNavigationLocalChunkGrid(canvas, snapshot, rect, center, half, new SKPaint { Color = new SKColor(72, 112, 132, 110), Style = SKPaintStyle.Stroke, StrokeWidth = 1f });
        DrawMassNavigationLocalPolyline(canvas, center, half, rect, pathOld, oldWorld);

        Vector2[] newWorld = after
            ? oldWorld.Select((point, index) =>
            {
                if (index == 0 || index == oldWorld.Length - 1)
                {
                    return point;
                }

                float t = index / (float)(oldWorld.Length - 1);
                Vector2 lateral = new(-7_000f * MathF.Sin(t * MathF.PI), 0f);
                return point + offset * MathF.Sin(t * MathF.PI) + lateral;
            }).ToArray()
            : oldWorld;
        DrawMassNavigationLocalPolyline(canvas, center, half, rect, pathNew, newWorld);

        if (after)
        {
            DrawMassNavigationLocalPolyline(canvas, center, half, rect, oldWaypointLine, start, oldMiddleWaypoint, goal);
        }

        DrawMassNavigationLocalPolyline(canvas, center, half, rect, waypointLine, start, middleWaypoint, goal);

        if (after)
        {
            foreach (Vector2 point in oldWorld.Where((_, index) => index % 4 == 0))
            {
                SKPoint screen = ToMassNavigationLocalScreen(point, center, half, rect);
                canvas.DrawCircle(screen.X, screen.Y, 3f, oldPoint);
            }
        }

        foreach (Vector2 point in newWorld.Where((_, index) => index % 3 == 0))
        {
            SKPoint screen = ToMassNavigationLocalScreen(point, center, half, rect);
            canvas.DrawCircle(screen.X, screen.Y, 3.2f, pathPoint);
            canvas.DrawCircle(screen.X, screen.Y, 3.2f, pathPointStroke);
        }

        DrawMassNavigationWaypoint(canvas, ToMassNavigationLocalScreen(start, center, half, rect), "waypoint A", waypoint, waypointStroke, textPaint);
        if (after)
        {
            SKPoint oldMiddle = ToMassNavigationLocalScreen(oldMiddleWaypoint, center, half, rect);
            SKPoint newMiddle = ToMassNavigationLocalScreen(middleWaypoint, center, half, rect);
            DrawMassNavigationWaypoint(canvas, oldMiddle, "old B", oldWaypoint, waypointStroke, textPaint);
            canvas.DrawLine(oldMiddle.X + 10f, oldMiddle.Y - 10f, newMiddle.X - 10f, newMiddle.Y + 8f, arrowPaint);
            DrawMassNavigationArrowHead(canvas, new SKPoint(oldMiddle.X + 10f, oldMiddle.Y - 10f), new SKPoint(newMiddle.X - 10f, newMiddle.Y + 8f), arrowPaint);
            DrawMassNavigationWaypoint(canvas, newMiddle, "moved waypoint B", movedWaypoint, waypointStroke, textPaint);
        }
        else
        {
            DrawMassNavigationWaypoint(canvas, ToMassNavigationLocalScreen(middleWaypoint, center, half, rect), "waypoint B", waypoint, waypointStroke, textPaint);
        }
        DrawMassNavigationWaypoint(canvas, ToMassNavigationLocalScreen(goal, center, half, rect), "waypoint C", waypoint, waypointStroke, textPaint);
        canvas.DrawText(after ? "After: changed waypoint -> new immutable pathpoints" : "Before: path preview can seed editable waypoints", map.Left + 12, map.Top + 18, textPaint);

        var legend = new SKRect(rect.Left + 10, rect.Bottom - 50, rect.Right - 10, rect.Bottom - 8);
        canvas.DrawRect(legend, legendFill);
        DrawMassNavigationLegendLine(canvas, legend.Left + 8, legend.Top + 18, waypoint.Color, "editable waypoint plan");
        DrawMassNavigationLegendLine(canvas, legend.Left + 180, legend.Top + 18, pathNew.Color, "new immutable pathpoints");
        DrawMassNavigationLegendLine(canvas, legend.Left + 380, legend.Top + 18, oldPoint.Color, after ? "old pathpoints faded" : "old path hidden");

        var card = new SKRect(map.Left + 650, map.Top + 34, map.Right - 12, map.Bottom - 14);
        canvas.DrawRect(card, cardFill);
        canvas.DrawText("Contract", card.Left + 10, card.Top + 18, textPaint);
        DrawMassNavigationBulletLines(
            canvas,
            after
                ? new[]
                {
                    "Waypoint is authored intent and can move.",
                    "Old pathpoints are discarded, not edited.",
                    $"New pathpoints={snapshot.WaypointPathDiagnostics.PathPointCount}; immutable={snapshot.WaypointPathDiagnostics.PathPointsImmutable}.",
                    "Route-authoring UI remains production SDK gap.",
                }
                : new[]
                {
                    "Path preview output may seed a trade route plan.",
                    $"Waypoints={snapshot.WaypointPathDiagnostics.WaypointCount}; editable={snapshot.WaypointPathDiagnostics.WaypointsEditable}.",
                    $"Pathpoints={snapshot.WaypointPathDiagnostics.PathPointCount}; immutable={snapshot.WaypointPathDiagnostics.PathPointsImmutable}.",
                    "Designer edits waypoints, not pathpoints.",
                },
            new SKRect(card.Left + 10, card.Top + 42, card.Right - 10, card.Bottom - 12),
            smallPaint,
            16f,
            10);
    }

    private static void DrawMassNavigationArrowHead(SKCanvas canvas, SKPoint from, SKPoint to, SKPaint paint)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.001f)
        {
            return;
        }

        float ux = dx / length;
        float uy = dy / length;
        float nx = -uy;
        float ny = ux;
        SKPoint left = new(to.X - ux * 10f + nx * 5f, to.Y - uy * 10f + ny * 5f);
        SKPoint right = new(to.X - ux * 10f - nx * 5f, to.Y - uy * 10f - ny * 5f);
        canvas.DrawLine(to, left, paint);
        canvas.DrawLine(to, right, paint);
    }

    private static void DrawMassNavigationToolBox(SKCanvas canvas, string label, float x, float y, float width, float height, SKPaint fill, SKPaint stroke, SKPaint text)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
        canvas.DrawText(FitMassNavigationText(label, text, width - 12f), rect.Left + 6f, rect.Top + 21f, text);
    }

    private static Vector2 ResolveMassNavigationPathStart(MassNavigationSnapshot snapshot)
    {
        return snapshot.PathOnlyQuery.StartWorldCm == Vector2.Zero
            ? new Vector2(-12_500f, -12_500f)
            : snapshot.PathOnlyQuery.StartWorldCm;
    }

    private static Vector2 ResolveMassNavigationPathGoal(MassNavigationSnapshot snapshot)
    {
        return snapshot.PathOnlyQuery.GoalWorldCm == Vector2.Zero
            ? new Vector2(12_500f, 12_500f)
            : snapshot.PathOnlyQuery.GoalWorldCm;
    }

    private static Vector2[] BuildMassNavigationPathPreviewPoints(MassNavigationSnapshot snapshot)
    {
        Vector2 start = ResolveMassNavigationPathStart(snapshot);
        Vector2 goal = ResolveMassNavigationPathGoal(snapshot);
        int count = Math.Clamp(snapshot.PathOnlyQuery.PathPointCount, 6, 28);
        var points = new Vector2[count];
        Vector2 delta = goal - start;
        Vector2 normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X));
        if (!IsFinite(normal))
        {
            normal = new Vector2(0f, 1f);
        }

        float amplitude = MathF.Min(8_000f, MathF.Max(2_000f, Vector2.Distance(start, goal) * 0.18f));
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Math.Max(1, count - 1);
            float bend = MathF.Sin(t * MathF.PI * 2f) * amplitude * 0.55f + MathF.Sin(t * MathF.PI) * amplitude;
            points[i] = Vector2.Lerp(start, goal, t) + normal * bend;
        }

        points[0] = start;
        points[^1] = goal;
        return points;
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }

    private static string ResolveMassNavigationRuntimeUseCaseId(string step)
    {
        int marker = step.IndexOf("_runtime_", StringComparison.Ordinal);
        if (marker < 0)
        {
            return "U?";
        }

        int start = marker + "_runtime_".Length;
        if (start >= step.Length || step[start] != 'u')
        {
            return "U?";
        }

        start++;
        int end = start;
        while (end < step.Length && char.IsDigit(step[end]))
        {
            end++;
        }

        return end > start
            ? "U" + step[start..end]
            : "U?";
    }

    private static string ResolveMassNavigationRuntimeGuideMode(string step)
    {
        return step switch
        {
            "006a_runtime_u1_visual_heightmap_bake" => "navmesh_workbench",
            "006b_runtime_u2_logic_heightmap_bake" => "navmesh_workbench",
            "006c_runtime_u3_layer_area_editor" => "layer",
            "006d_runtime_u4_path_only" => "path",
            "006e_runtime_u5_world_hpa" => "hpa",
            "006f_runtime_u6_strategy_switch" => "strategy",
            "006g_runtime_u7_order_reuse" => "allocation",
            "006h_runtime_u8_target_allocation" => "allocation",
            "006i_runtime_u9_layer_costs" => "layer",
            "006j_runtime_u10_waypoint_authoring" => "waypoint_after",
            "006k_runtime_u11_large_world" => "navmesh_gap",
            "006l_runtime_u12_10k_flow" => "full",
            "006m_runtime_u13_static_obstacles" => "obstacles",
            "006n_runtime_u14_fps_scope" => "fps",
            "006o_runtime_u15_debug_budget" => "debug",
            "006p_runtime_u16_bake_tool" => "bake_tool",
            _ => "summary"
        };
    }

    private static string BuildMassNavigationRuntimeGuideZoomSubtitle(string mode)
    {
        return mode switch
        {
            "navmesh_workbench" => "Walkable triangles, blocked/high-cost source, portals, clearance and agent radius.",
            "path" => "Pathfinding only: preview route, corridor, portals, pathpoints and waypoint intent.",
            "hpa" => "Large-world HPA: numbered crossed chunks and active-window portals.",
            "strategy" => "Same query, different routing surfaces: road graph, NavMesh and Hybrid.",
            "layer" => "Layer/cost editor: ground, water, air, mountain and NoFly/high-cost regions.",
            "allocation" => "Order reuse and target allocation: shared route bucket plus slot cloud.",
            "full" => "10k flow smoke: shared command, sampled slots and movement counters.",
            "obstacles" => "40k obstacle world: authored/baked/loaded chain versus active solver subset.",
            "fps" => "Timing scope: Raylib framebuffer benchmark is the production FPS/debug-budget gate.",
            "debug" => "Debug presentation budget: sampled, bounded and measured overlays.",
            "bake_tool" => "Bake tool: source tabs, LogicHeightmap, Recast .ntil and validator outputs.",
            _ => "Runtime guide overlay extracted from the playable scene."
        };
    }

    private static Vector2 ResolveMassNavigationChunkCenter(MassNavigationSnapshot snapshot, int chunkX, int chunkY)
    {
        int chunkWidth = Math.Max(1, snapshot.MacroChunkSizeXCm);
        int chunkHeight = Math.Max(1, snapshot.MacroChunkSizeYCm);
        return new Vector2(
            (-snapshot.WorldWidthCm * 0.5f) + (chunkX * chunkWidth) + (chunkWidth * 0.5f),
            (-snapshot.WorldHeightCm * 0.5f) + (chunkY * chunkHeight) + (chunkHeight * 0.5f));
    }

    private static void DrawMassNavigationNavMeshWorkbenchOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map)
    {
        using var chunkPaint = new SKPaint { Color = new SKColor(72, 112, 132, 120), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var walkPaint = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var portalPaint = new SKPaint { Color = new SKColor(255, 150, 86), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4.2f, StrokeCap = SKStrokeCap.Round };
        using var radiusPaint = new SKPaint { Color = new SKColor(255, 226, 116), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var blockedFill = new SKPaint { Color = new SKColor(255, 78, 86, 82), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var blockedStroke = new SKPaint { Color = new SKColor(255, 118, 118), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f };
        using var costFill = new SKPaint { Color = new SKColor(255, 210, 88, 88), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var waterFill = new SKPaint { Color = new SKColor(80, 172, 255, 82), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 10f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(176, 196, 210), IsAntialias = true, TextSize = 9.2f };
        using var cardFill = new SKPaint { Color = new SKColor(12, 28, 38, 230), IsAntialias = true, Style = SKPaintStyle.Fill };

        int sampleChunkX = snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkX >= 0 ? snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkX : Math.Max(0, snapshot.MacroChunkColumns / 2);
        int sampleChunkY = snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkY >= 0 ? snapshot.HpaGraphDiagnostics.ActiveWindowMinChunkY : Math.Max(0, snapshot.MacroChunkRows / 2);
        Vector2 center = ResolveMassNavigationChunkCenter(snapshot, sampleChunkX, sampleChunkY);
        float halfExtent = MathF.Max(6_000f, Math.Max(snapshot.MacroChunkSizeXCm, snapshot.MacroChunkSizeYCm) * 0.72f);
        DrawMassNavigationLocalChunkGrid(canvas, snapshot, map, center, halfExtent, chunkPaint);

        Vector2[] triA =
        {
            center + new Vector2(-5_200f, -3_600f),
            center + new Vector2(1_200f, -4_200f),
            center + new Vector2(-1_200f, 1_400f),
            center + new Vector2(5_000f, -1_600f),
            center + new Vector2(4_000f, 4_200f),
        };
        Vector2[] triB =
        {
            center + new Vector2(1_200f, -4_200f),
            center + new Vector2(4_900f, -200f),
            center + new Vector2(1_200f, -4_200f),
            center + new Vector2(4_000f, 4_200f),
            center + new Vector2(-1_200f, 1_400f),
        };
        Vector2[] triC =
        {
            center + new Vector2(-1_200f, 1_400f),
            center + new Vector2(-1_200f, 1_400f),
            center + new Vector2(4_900f, -200f),
            center + new Vector2(-1_200f, 1_400f),
            center + new Vector2(-5_200f, 3_200f),
        };
        for (int i = 0; i < triA.Length; i++)
        {
            DrawMassNavigationLocalPolyline(canvas, center, halfExtent, map, walkPaint, triA[i], triB[i], triC[i], triA[i]);
        }

        SKRect blocked = RectFromLocal(center + new Vector2(-4_400f, 3_100f), 4_200f, 2_200f);
        SKRect highCost = RectFromLocal(center + new Vector2(4_200f, -3_000f), 3_800f, 2_000f);
        SKRect water = RectFromLocal(center + new Vector2(-700f, -5_000f), 5_200f, 1_500f);
        DrawLocalRect(blocked, blockedFill, blockedStroke, "blocked source");
        DrawLocalRect(highCost, costFill, radiusPaint, "high-cost area");
        DrawLocalRect(water, waterFill, null, "water/layer area");

        Vector2[] portalA = { center + new Vector2(-6_800f, -800f), center + new Vector2(6_800f, 1_100f), center + new Vector2(300f, 6_800f) };
        Vector2[] portalB = { center + new Vector2(-6_800f, 2_200f), center + new Vector2(6_800f, 4_100f), center + new Vector2(3_200f, 6_800f) };
        for (int i = 0; i < portalA.Length; i++)
        {
            DrawMassNavigationLocalPolyline(canvas, center, halfExtent, map, portalPaint, portalA[i], portalB[i]);
            SKPoint mid = ToMassNavigationLocalScreen((portalA[i] + portalB[i]) * 0.5f, center, halfExtent, map);
            canvas.DrawText("portal clearance", mid.X + 4, mid.Y - 4, smallPaint);
        }

        SKPoint radiusCenter = ToMassNavigationLocalScreen(center, center, halfExtent, map);
        canvas.DrawCircle(radiusCenter.X, radiusCenter.Y, 15f, radiusPaint);
        canvas.DrawText("agent radius", radiusCenter.X + 18, radiusCenter.Y + 4, textPaint);

        var card = new SKRect(map.Left + 8, map.Top + 8, map.Right - 8, map.Top + 80);
        canvas.DrawRect(card, cardFill);
        canvas.DrawText("NavMesh truth: green/cyan walkable edges, red blocked, gold high-cost, orange portal/link, yellow agent radius.", card.Left + 8, card.Top + 18, textPaint);
        canvas.DrawText($"Active window tiles={snapshot.HpaGraphDiagnostics.LoadedTileCount}/{snapshot.HpaGraphDiagnostics.ActiveWindowChunkCount}; navmesh tiles={snapshot.NavMeshBake.BakedChunks}/{snapshot.NavMeshBake.TotalChunks}.", card.Left + 8, card.Top + 36, smallPaint);
        canvas.DrawText($"Layers={snapshot.NavMeshLayerCount}, profiles={snapshot.NavMeshProfileCount}, areaCosts={snapshot.NavMeshAreaCostCount}; active-window streaming gate is measured.", card.Left + 8, card.Top + 54, smallPaint);

        SKRect RectFromLocal(Vector2 rectCenter, float width, float height)
        {
            SKPoint a = ToMassNavigationLocalScreen(rectCenter + new Vector2(-width * 0.5f, -height * 0.5f), center, halfExtent, map);
            SKPoint b = ToMassNavigationLocalScreen(rectCenter + new Vector2(width * 0.5f, height * 0.5f), center, halfExtent, map);
            return NormalizeRect(new SKRect(a.X, a.Y, b.X, b.Y));
        }

        void DrawLocalRect(SKRect rect, SKPaint fill, SKPaint? stroke, string label)
        {
            canvas.DrawRect(rect, fill);
            if (stroke != null)
            {
                canvas.DrawRect(rect, stroke);
            }

            canvas.DrawText(label, rect.Left + 4, rect.Top + 14, smallPaint);
        }
    }

    private static void DrawMassNavigationRuntimeSlotOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map, bool includeFlow)
    {
        using var slotPaint = new SKPaint { Color = new SKColor(255, 226, 116, 220), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
        using var routePaint = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.2f, StrokeCap = SKStrokeCap.Round };
        using var bucketPaint = new SKPaint { Color = new SKColor(255, 212, 96), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 10f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(176, 196, 210), IsAntialias = true, TextSize = 9.4f };
        using var cardFill = new SKPaint { Color = new SKColor(12, 28, 38, 230), IsAntialias = true, Style = SKPaintStyle.Fill };

        Vector2 destination = snapshot.TargetAllocation.HasAllocation ? snapshot.TargetAllocation.DestinationWorldCm : new Vector2(1_050_000, -780_000);
        float half = ResolveMassNavigationOverlayHalfExtent(snapshot, includeFlow ? "full" : "allocation");
        Vector2 start = snapshot.FlowWorkAreaCenterCm;
        DrawMassNavigationLocalPolyline(canvas, destination, half, map, routePaint, start, (start + destination) * 0.5f + new Vector2(0f, 9_000f), destination);
        if (snapshot.TargetSlotSamples.Count > 0)
        {
            int stride = Math.Max(1, snapshot.TargetSlotSamples.Count / 625);
            for (int i = 0; i < snapshot.TargetSlotSamples.Count; i += stride)
            {
                Vector2 slot = ToMassNavigationWorldCm(snapshot.TargetSlotSamples[i]);
                SKPoint point = ToMassNavigationLocalScreen(slot, destination, half, map);
                if (map.Contains(point.X, point.Y))
                {
                    canvas.DrawCircle(point.X, point.Y, 2.6f, slotPaint);
                }
            }
        }

        SKPoint target = ToMassNavigationLocalScreen(destination, destination, half, map);
        canvas.DrawCircle(target.X, target.Y, MathF.Min(42f, MathF.Max(18f, snapshot.TargetAllocation.GoalFootprintRadiusCm / MathF.Max(1f, half) * map.Width * 0.5f)), bucketPaint);
        canvas.DrawText(includeFlow ? "10k commanded flow + real MassFlow targets" : "same/near reuse bucket + real targets", map.Left + 10, map.Top + 18, textPaint);
        var card = new SKRect(map.Left + 8, map.Bottom - 76, map.Right - 8, map.Bottom - 8);
        canvas.DrawRect(card, cardFill);
        canvas.DrawText($"slots={snapshot.TargetAllocation.SlotCount} samples={snapshot.TargetAllocation.ActualTargetSampleCount} reachable={snapshot.TargetAllocation.ReachableSlotCount}", card.Left + 8, card.Top + 18, smallPaint);
        canvas.DrawText($"reuseHit={snapshot.OrderReuse.CacheHit} scope={snapshot.OrderReuse.ReuseScope} fanout={snapshot.OrderReuse.FanoutCount}", card.Left + 8, card.Top + 36, smallPaint);
        canvas.DrawText($"commanded={snapshot.CommandedAgents} moving={snapshot.MovingAgents} settled={snapshot.SettledAgents} flow={snapshot.FlowEnabled}", card.Left + 8, card.Top + 54, smallPaint);
    }

    private static void DrawMassNavigationRuntimeObstacleOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map)
    {
        using var activePaint = new SKPaint { Color = new SKColor(103, 224, 145), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var solverCrossPaint = new SKPaint { Color = new SKColor(130, 255, 168), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.8f, StrokeCap = SKStrokeCap.Round };
        using var obstaclePaint = new SKPaint { Color = new SKColor(255, 226, 116, 210), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 10f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(176, 196, 210), IsAntialias = true, TextSize = 9.4f };
        using var cardFill = new SKPaint { Color = new SKColor(12, 28, 38, 230), IsAntialias = true, Style = SKPaintStyle.Fill };

        canvas.DrawText("40k obstacle buckets; bright green crosses are solver-active", map.Left + 10, map.Top + 18, textPaint);
        for (int i = 0; i < 360; i++)
        {
            float x = map.Left + 14 + ((i * 37) % 343) / 342f * (map.Width - 28);
            float y = map.Top + 34 + ((i * 61) % 211) / 210f * (map.Height - 68);
            canvas.DrawCircle(x, y, 1.8f, obstaclePaint);
        }

        var active = new SKRect(map.Left + map.Width * 0.34f, map.Top + map.Height * 0.34f, map.Left + map.Width * 0.66f, map.Top + map.Height * 0.66f);
        canvas.DrawRect(active, activePaint);
        int solverCrossCount = Math.Min(12, Math.Max(0, snapshot.ObstacleDiagnostics.SolverActiveStaticObstacleCount));
        for (int i = 0; i < solverCrossCount; i++)
        {
            float t = solverCrossCount == 1 ? 0.5f : i / (float)(solverCrossCount - 1);
            float x = active.Left + 14f + ((i * 31) % Math.Max(1f, active.Width - 28f));
            float y = active.Top + 14f + (t * Math.Max(1f, active.Height - 28f));
            canvas.DrawLine(x - 5f, y - 5f, x + 5f, y + 5f, solverCrossPaint);
            canvas.DrawLine(x - 5f, y + 5f, x + 5f, y - 5f, solverCrossPaint);
        }
        var card = new SKRect(map.Left + 8, map.Bottom - 76, map.Right - 8, map.Bottom - 8);
        canvas.DrawRect(card, cardFill);
        canvas.DrawText($"planned={snapshot.StaticObstacleWorldDiagnostics.PlannedWorldObstacleCount} authored={snapshot.ObstacleDiagnostics.AuthoredStaticObstacleCount} baked={snapshot.ObstacleDiagnostics.BakedStaticObstacleCount} loaded={snapshot.ObstacleDiagnostics.LoadedStaticObstacleCount}", card.Left + 8, card.Top + 18, smallPaint);
        canvas.DrawText($"solverActive={snapshot.ObstacleDiagnostics.SolverActiveStaticObstacleCount}/{snapshot.ObstacleDiagnostics.SolverStaticObstacleCapacity} activation={snapshot.StaticObstacleWorldDiagnostics.RuntimeActivationStrategy}", card.Left + 8, card.Top + 38, smallPaint);
        canvas.DrawText("Visual dots are sampled buckets; summary/report preserve the exact 40k chain.", card.Left + 8, card.Top + 58, smallPaint);
    }

    private static void DrawMassNavigationRuntimeBakeToolCompactOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map)
    {
        using var lanePaint = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.3f, StrokeCap = SKStrokeCap.Round };
        using var boxFill = new SKPaint { Color = new SKColor(18, 42, 54), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var sourceFill = new SKPaint { Color = new SKColor(34, 76, 96), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var passFill = new SKPaint { Color = new SKColor(62, 150, 92), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var blockedFill = new SKPaint { Color = new SKColor(128, 56, 64), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { Color = new SKColor(90, 150, 180), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 9.5f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(178, 198, 212), IsAntialias = true, TextSize = 8.6f };

        canvas.DrawText("Bake tool: source -> LogicHeightmap -> Recast .ntil -> validator outputs", map.Left + 8, map.Top + 15, textPaint);
        string[] sources = { ".vtxm vertex", ".vhtm visual", ".lhtm logic" };
        for (int i = 0; i < sources.Length; i++)
        {
            float y = map.Top + 34 + i * 45f;
            DrawToolBox(sources[i], map.Left + 10, y, 76, 24, sourceFill);
            DrawToolBox("LogicHeightmap", map.Left + 106, y, 96, 24, boxFill);
            DrawToolBox(".ntil", map.Left + 224, y, 50, 24, boxFill);
            DrawToolBox("64/64", map.Left + 292, y, 48, 24, passFill);
            canvas.DrawLine(map.Left + 86, y + 12, map.Left + 106, y + 12, lanePaint);
            canvas.DrawLine(map.Left + 202, y + 12, map.Left + 224, y + 12, lanePaint);
            canvas.DrawLine(map.Left + 274, y + 12, map.Left + 292, y + 12, lanePaint);
        }

        string[] outputs = { "coverage", "tile detail", "path-only", "HPA", "layer", "result JSON" };
        for (int i = 0; i < outputs.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            DrawToolBox(outputs[i], map.Left + 10 + col * 104f, map.Bottom - 72 + row * 26f, 92, 20, i < 5 ? passFill : sourceFill);
        }

        DrawToolBox("PASS: source lanes + query outputs + manifest hashes", map.Left + 10, map.Bottom - 20, map.Width - 20, 16, passFill);
        canvas.DrawText($"active-window navmesh={snapshot.NavMeshBake.BakedChunks}/{snapshot.NavMeshBake.TotalChunks}", map.Left + 10, map.Top + 181, smallPaint);

        void DrawToolBox(string label, float x, float y, float width, float height, SKPaint fill)
        {
            var rect = new SKRect(x, y, x + width, y + height);
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, stroke);
            canvas.DrawText(FitMassNavigationText(label, textPaint, width - 8), x + 4, y + height * 0.66f, textPaint);
        }
    }

    private static void DrawMassNavigationRuntimeWaypointCompactOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map)
    {
        using var pathOld = new SKPaint { Color = new SKColor(120, 136, 148, 110), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var pathNew = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.3f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var waypointLine = new SKPaint { Color = new SKColor(255, 226, 116), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, PathEffect = SKPathEffect.CreateDash(new[] { 7f, 5f }, 0f) };
        using var waypointFill = new SKPaint { Color = new SKColor(255, 226, 116), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var movedFill = new SKPaint { Color = new SKColor(255, 174, 88), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var pathPointFill = new SKPaint { Color = new SKColor(8, 12, 18), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var pathPointStroke = new SKPaint { Color = new SKColor(142, 245, 204), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 9.5f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(178, 198, 212), IsAntialias = true, TextSize = 8.8f };

        Vector2[] oldWorld = BuildMassNavigationPathPreviewPoints(snapshot);
        Vector2 start = oldWorld[0];
        Vector2 goal = oldWorld[^1];
        Vector2 oldMiddle = (start + goal) * 0.5f + new Vector2(0f, 8_000f);
        Vector2 newMiddle = (start + goal) * 0.5f + new Vector2(-16_000f, 23_000f);
        Vector2 center = (start + goal) * 0.5f + new Vector2(-2_500f, 7_000f);
        float half = MathF.Max(32_000f, Vector2.Distance(start, goal) * 0.95f);
        DrawMassNavigationLocalChunkGrid(canvas, snapshot, map, center, half, new SKPaint { Color = new SKColor(72, 112, 132, 100), Style = SKPaintStyle.Stroke, StrokeWidth = 1f });
        DrawMassNavigationLocalPolyline(canvas, center, half, map, pathOld, oldWorld);
        Vector2[] newWorld = oldWorld.Select((point, index) =>
        {
            if (index == 0 || index == oldWorld.Length - 1)
            {
                return point;
            }

            float t = index / (float)(oldWorld.Length - 1);
            return point + new Vector2(-7_000f * MathF.Sin(t * MathF.PI), 18_000f * MathF.Sin(t * MathF.PI));
        }).ToArray();
        DrawMassNavigationLocalPolyline(canvas, center, half, map, pathNew, newWorld);
        DrawMassNavigationLocalPolyline(canvas, center, half, map, waypointLine, start, newMiddle, goal);
        foreach (Vector2 point in newWorld.Where((_, index) => index % 3 == 0))
        {
            SKPoint screen = ToMassNavigationLocalScreen(point, center, half, map);
            canvas.DrawCircle(screen.X, screen.Y, 3f, pathPointFill);
            canvas.DrawCircle(screen.X, screen.Y, 3f, pathPointStroke);
        }

        DrawWaypoint(start, "A", waypointFill);
        DrawWaypoint(oldMiddle, "old B", waypointFill);
        DrawWaypoint(newMiddle, "moved B", movedFill);
        DrawWaypoint(goal, "C", waypointFill);
        canvas.DrawText("yellow=editable waypoints; cyan=current immutable pathpoints; gray=old invalidated result", map.Left + 8, map.Bottom - 8, smallPaint);
        canvas.DrawText($"waypoints={snapshot.WaypointPathDiagnostics.WaypointCount} pathpoints={snapshot.WaypointPathDiagnostics.PathPointCount} editable={snapshot.WaypointPathDiagnostics.WaypointsEditable} immutable={snapshot.WaypointPathDiagnostics.PathPointsImmutable}", map.Left + 8, map.Top + 15, textPaint);

        void DrawWaypoint(Vector2 world, string label, SKPaint fill)
        {
            SKPoint point = ToMassNavigationLocalScreen(world, center, half, map);
            canvas.DrawCircle(point.X, point.Y, 5.4f, fill);
            canvas.DrawText(label, point.X + 6, point.Y - 4, textPaint);
        }
    }

    private static void DrawMassNavigationRuntimeTimingOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map)
    {
        using var barFill = new SKPaint { Color = new SKColor(30, 58, 74), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var simFill = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var presFill = new SKPaint { Color = new SKColor(80, 172, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var debugFill = new SKPaint { Color = new SKColor(255, 212, 96), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { Color = new SKColor(235, 245, 250), IsAntialias = true, TextSize = 10f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(176, 196, 210), IsAntialias = true, TextSize = 9.4f };
        using var cardFill = new SKPaint { Color = new SKColor(12, 28, 38, 230), IsAntialias = true, Style = SKPaintStyle.Fill };

        canvas.DrawText("Timing scope and debug budget are the Raylib production gate for this showcase", map.Left + 10, map.Top + 18, textPaint);
        float x = map.Left + 28;
        float y = map.Top + 56;
        DrawBar("simulation", snapshot.SimulationMs, simFill, y);
        DrawBar("presentation", snapshot.PresentationMs, presFill, y + 42);
        DrawBar("massNav", snapshot.MassNavigationMs, debugFill, y + 84);
        DrawBar("overlay draw", snapshot.DebugVisualDiagnostics.ScreenOverlayDrawMs, debugFill, y + 126);
        var card = new SKRect(map.Left + 8, map.Bottom - 76, map.Right - 8, map.Bottom - 8);
        canvas.DrawRect(card, cardFill);
        canvas.DrawText($"frame={snapshot.FrameMs:F2}ms overlayItems={snapshot.DebugVisualDiagnostics.ScreenOverlayItems} evidenceItems={snapshot.DebugVisualDiagnostics.EvidenceOverlayItems}", card.Left + 8, card.Top + 18, smallPaint);
        canvas.DrawText("rendererScope=raylib_framebuffer_micro_benchmark; loaded summary data=true", card.Left + 8, card.Top + 38, smallPaint);
        canvas.DrawText("Gate: p95<=10ms, p99<=12.5ms, overlay draw<=0.5ms, runtime overlay writes=0.", card.Left + 8, card.Top + 58, smallPaint);

        void DrawBar(string label, float valueMs, SKPaint fill, float top)
        {
            float width = MathF.Min(map.Width - 140f, MathF.Max(2f, valueMs * 20f));
            canvas.DrawText($"{label}: {valueMs:F2}ms", x, top + 13, smallPaint);
            canvas.DrawRect(new SKRect(x + 104, top, map.Right - 24, top + 16), barFill);
            canvas.DrawRect(new SKRect(x + 104, top, x + 104 + width, top + 16), fill);
        }
    }

    private static void DrawMassNavigationLocalChunkGrid(
        SKCanvas canvas,
        MassNavigationSnapshot snapshot,
        SKRect map,
        Vector2 center,
        float halfExtent,
        SKPaint paint)
    {
        int chunkWidth = Math.Max(1, snapshot.MacroChunkSizeXCm);
        int chunkHeight = Math.Max(1, snapshot.MacroChunkSizeYCm);
        float minX = center.X - halfExtent;
        float maxX = center.X + halfExtent;
        float minY = center.Y - halfExtent;
        float maxY = center.Y + halfExtent;
        int firstX = (int)MathF.Floor((minX + snapshot.WorldWidthCm * 0.5f) / chunkWidth);
        int lastX = (int)MathF.Ceiling((maxX + snapshot.WorldWidthCm * 0.5f) / chunkWidth);
        int firstY = (int)MathF.Floor((minY + snapshot.WorldHeightCm * 0.5f) / chunkHeight);
        int lastY = (int)MathF.Ceiling((maxY + snapshot.WorldHeightCm * 0.5f) / chunkHeight);

        for (int cx = firstX; cx <= lastX; cx++)
        {
            float worldX = cx * chunkWidth - snapshot.WorldWidthCm * 0.5f;
            SKPoint a = ToMassNavigationLocalScreen(new Vector2(worldX, minY), center, halfExtent, map);
            SKPoint b = ToMassNavigationLocalScreen(new Vector2(worldX, maxY), center, halfExtent, map);
            canvas.DrawLine(a, b, paint);
        }

        for (int cy = firstY; cy <= lastY; cy++)
        {
            float worldY = cy * chunkHeight - snapshot.WorldHeightCm * 0.5f;
            SKPoint a = ToMassNavigationLocalScreen(new Vector2(minX, worldY), center, halfExtent, map);
            SKPoint b = ToMassNavigationLocalScreen(new Vector2(maxX, worldY), center, halfExtent, map);
            canvas.DrawLine(a, b, paint);
        }
    }

    private static void DrawMassNavigationPortalBar(SKCanvas canvas, SKPoint previous, SKPoint current, SKPoint next, float halfWidth, SKPaint paint)
    {
        float dx = next.X - previous.X;
        float dy = next.Y - previous.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.001f)
        {
            return;
        }

        float nx = -dy / length;
        float ny = dx / length;
        canvas.DrawLine(current.X - nx * halfWidth, current.Y - ny * halfWidth, current.X + nx * halfWidth, current.Y + ny * halfWidth, paint);
    }

    private static void DrawMassNavigationWaypoint(SKCanvas canvas, SKPoint point, string label, SKPaint fill, SKPaint stroke, SKPaint text)
    {
        canvas.DrawCircle(point.X, point.Y, 7f, fill);
        canvas.DrawCircle(point.X, point.Y, 7f, stroke);
        canvas.DrawText(label, point.X + 9f, point.Y - 7f, text);
    }

    private static void DrawMassNavigationOverlayTag(SKCanvas canvas, string label, SKPoint anchor, SKColor color, SKPaint textPaint)
    {
        using var fill = new SKPaint { Color = new SKColor(8, 16, 22, 225), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f };
        float width = MathF.Max(72f, textPaint.MeasureText(label) + 14f);
        var rect = new SKRect(anchor.X + 8f, anchor.Y - 24f, anchor.X + 8f + width, anchor.Y - 5f);
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
        canvas.DrawText(label, rect.Left + 7f, rect.Bottom - 5f, textPaint);
    }

    private static IReadOnlyList<(int X, int Y)> BuildMassNavigationHpaRouteChunks(MassNavigationSnapshot snapshot)
    {
        var route = new List<(int X, int Y)>();
        string signature = snapshot.HpaGraphDiagnostics.RouteSignature ?? string.Empty;
        foreach (string step in signature.Split("->", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string chunkPart = step.Split(':')[0];
            string[] parts = chunkPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                continue;
            }

            if (route.Count == 0 || route[^1].X != x || route[^1].Y != y)
            {
                route.Add((x, y));
            }
        }

        if (route.Count >= 2)
        {
            return route;
        }

        int startX = snapshot.HpaMacroDiagnostics.StartMacroChunkX;
        int startY = snapshot.HpaMacroDiagnostics.StartMacroChunkY;
        int goalX = snapshot.HpaMacroDiagnostics.GoalMacroChunkX;
        int goalY = snapshot.HpaMacroDiagnostics.GoalMacroChunkY;
        int currentX = startX;
        int currentY = startY;
        route.Clear();
        route.Add((currentX, currentY));
        while (currentX != goalX || currentY != goalY)
        {
            if (currentX != goalX)
            {
                currentX += Math.Sign(goalX - currentX);
            }
            else if (currentY != goalY)
            {
                currentY += Math.Sign(goalY - currentY);
            }

            route.Add((currentX, currentY));
            if (route.Count > 64)
            {
                break;
            }
        }

        return route.Count >= 2 ? route : new[] { (startX, startY), (goalX, goalY) };
    }

    private static string BuildMassNavigationHpaRouteChunkManifest(MassNavigationSnapshot snapshot)
    {
        IReadOnlyList<(int X, int Y)> route = BuildMassNavigationHpaRouteChunks(snapshot);
        if (route.Count == 0)
        {
            return "not_available";
        }

        string manifest = string.Join(
            " -> ",
            route.Take(12).Select((chunk, index) => $"{index + 1:00}:{chunk.X},{chunk.Y}"));
        return route.Count > 12 ? manifest + " ..." : manifest;
    }

    private static SKRect ResolveMassNavigationHpaCell(int chunkX, int chunkY, int minX, int minY, int columns, int rows, SKRect grid)
    {
        int localX = Math.Clamp(chunkX - minX, 0, Math.Max(0, columns - 1));
        int localY = Math.Clamp(chunkY - minY, 0, Math.Max(0, rows - 1));
        float cellWidth = grid.Width / Math.Max(1, columns);
        float cellHeight = grid.Height / Math.Max(1, rows);
        float left = grid.Left + localX * cellWidth;
        float top = grid.Top + localY * cellHeight;
        return new SKRect(left + 1f, top + 1f, left + cellWidth - 1f, top + cellHeight - 1f);
    }

    private static SKPoint ResolveMassNavigationHpaCellCenter(int chunkX, int chunkY, int minX, int minY, int columns, int rows, SKRect grid)
    {
        SKRect cell = ResolveMassNavigationHpaCell(chunkX, chunkY, minX, minY, columns, rows, grid);
        return new SKPoint((cell.Left + cell.Right) * 0.5f, (cell.Top + cell.Bottom) * 0.5f);
    }

    private static void DrawMassNavigationLegendLine(SKCanvas canvas, float x, float y, SKColor color, string label)
    {
        using var line = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round };
        using var text = new SKPaint { Color = new SKColor(176, 196, 210), IsAntialias = true, TextSize = 9.4f };
        canvas.DrawLine(x, y - 3f, x + 20f, y - 3f, line);
        canvas.DrawText(label, x + 25f, y, text);
    }

    private static void DrawMassNavigationScreenPolyline(SKCanvas canvas, SKPaint paint, params SKPoint[] points)
    {
        if (points.Length < 2)
        {
            return;
        }

        using var path = new SKPath();
        path.MoveTo(points[0]);
        for (int i = 1; i < points.Length; i++)
        {
            path.LineTo(points[i]);
        }

        canvas.DrawPath(path, paint);
    }

    private static string BuildMassNavigationKeyframeSubtitle(string mode)
    {
        return mode switch
        {
            "path" => "Path preview: preset endpoints highlight a route without submitting a unit order.",
            "strategy" => "Strategy inspector: the same query is compared across road graph, navmesh and hybrid routes.",
            "layer" => "Layer/cost inspector: ground, water, air and mountain profiles resolve different costs.",
            "hpa" => "Large-world inspector: macro chunks, portals and active-window HPA evidence are visible.",
            "navmesh_gap" => "NavMesh streaming gate: active-window tiles exist and full-world not-loaded count remains explicit.",
            "bake_tool" => "Bake validator composite: each source is normalized to LogicHeightmap and checked by Raylib screenshots.",
            "waypoint_before" => "Waypoint authoring: editable plan points can be seeded from a path preview.",
            "waypoint_after" => "Waypoint authoring: changing the plan invalidates old pathpoints and regenerates the route.",
            "allocation" => "Order reuse: same or nearby orders should share route buckets instead of recomputing per unit.",
            "full" => "Mass order: a 10k-unit selection receives allocated target slots and a shared flow command.",
            "obstacles" => "Obstacle chain: authored, baked, loaded and solver-active counts stay separated.",
            "fps" => "Frame timing: Raylib framebuffer benchmark checks the machine FPS/debug budget.",
            _ => "Showcase keyframe: read the visible action, expected output, and production gate separately."
        };
    }

    private static string BuildMassNavigationKeyframeOverlayTitle(string mode)
    {
        return mode switch
        {
            "path" => "Path Preview",
            "strategy" => "Strategy Routes",
            "layer" => "Layer Cost Query",
            "hpa" => "HPA Route Sample",
            "navmesh_gap" => "NavMesh Tile Gate",
            "bake_tool" => "Bake Validator",
            "waypoint_before" => "Waypoint Before",
            "waypoint_after" => "Waypoint After",
            "allocation" => "Reuse + Slots",
            "full" => "10k Target Slots",
            "obstacles" => "Obstacle Buckets",
            "fps" => "Timing Scope",
            _ => "Scenario Overlay"
        };
    }

    private static string BuildMassNavigationKeyframeOverlaySubtitle(string mode)
    {
        return mode switch
        {
            "path" => "Yellow points are preset route endpoints; green is the preview path.",
            "strategy" => "Green/yellow route lines show alternative strategy outputs for one query.",
            "layer" => "Same endpoints, different movement layers and area-cost profiles.",
            "hpa" => "Numbered route cells show the crossed chunks; yellow strokes mark portal crossings.",
            "navmesh_gap" => "Green is the active-window tile set; gray/red keeps not-loaded world tiles explicit.",
            "bake_tool" => "Each lane ends in validator screenshots and result JSON.",
            "waypoint_before" => "Yellow handles are editable waypoints; green dots are immutable pathpoints.",
            "waypoint_after" => "Moved waypoint creates a new plan; old pathpoints are faded and regenerated.",
            "allocation" => "Destination cloud shows sampled target slots for repeated orders.",
            "full" => "Slot cloud is sampled visually; JSON/report preserve exact 10k slot count.",
            "obstacles" => "Yellow dots are sampled world buckets; bright green crosses are the solver-active subset.",
            "fps" => "The screenshot anchors timing numbers to a renderer scope.",
            _ => "Spatial context for this keyframe."
        };
    }

    private static IReadOnlyList<string> BuildMassNavigationKeyframePlayerLines(string mode, string status, IReadOnlyList<string> lines)
    {
        string first = lines.Count > 0 ? lines[0] : "Inspect the highlighted route and counters.";
        string second = lines.Count > 1 ? lines[1] : "Expected result is visible in the overlay and evidence fields.";
        string gate = status.Contains("BLOCKED", StringComparison.Ordinal)
            ? "Machine evidence is blocked for this keyframe; inspect summary.json for the failed check."
            : "Machine evidence for this keyframe is present; human-operated UAT signoff is still separate.";

        return mode switch
        {
            "path" => new[] { "Player input: click Path Preview; this uses the showcased start and goal.", "Expected output: cyan corridor/pathpoints appear, yellow waypoints stay editable, and units do not move.", "Pass signal: NoOrderSubmitted=true, order_delta=0, pathpoints and portals are visible.", second },
            "strategy" => new[] { "Tool input: click Strategy for one start/goal query.", "Expected output: Road, NavMesh and Hybrid candidate routes are colored separately.", "Pass signal: every profile row reports selected strategy, graph/mesh provenance, route id and touched tiles.", gate },
            "layer" => new[] { "Tool input: click Layer/Cost and compare ground, water, air and mountain profiles.", "Expected output: layer regions, NoFly/high-cost zones, blocked mask and cost rows are visible together.", "Pass signal: Infantry/LargeVehicle/Mountain/Naval/Air rows expose layer, area costs and active-window mesh status.", gate },
            "hpa" => new[] { "Tool input: click World/HPA for the 256x256 macro world.", "Expected output: numbered route chunks, start/goal chunks, portal crossings and active window are visible on the image.", "Pass signal: route chunks and portal counts are non-zero.", gate },
            "navmesh_gap" => new[] { "Tool input: click NavMesh View or inspect large-world tile coverage.", "Expected output: active-window loaded tiles and full-world notLoaded count are both visible.", "Pass signal: active-window tiles are queryable and failed/missing/dirty are zero.", gate },
            "bake_tool" => new[] { "Mod input: choose vtxm, vhtm or lhtm bake source.", "Expected output: every source flows into LogicHeightmap, .ntil tiles, validator screenshots and result JSON.", "Pass signal: the validator composite links bake, path, HPA, layer and result artifacts.", gate },
            "waypoint_before" => new[] { "Designer input: copy a path preview into editable route waypoints.", "Expected output: waypoint handles are editable while pathpoints remain query output.", first, second },
            "waypoint_after" => new[] { "Designer input: move one waypoint in the authored plan.", "Expected output: old pathpoints are invalidated and regenerated from the new plan.", first, second },
            "allocation" => new[] { "Player input: select a reuse squad, right-click the same destination twice, right-click a nearby destination, then select 10k and right-click one destination.", "Expected output: route cache hit, reuse scope, goal bucket and sampled target-slot cloud are visible.", "Pass signal: reused route id, same/near scope, 10k logical slots and bounded visual sampling.", second },
            "full" => new[] { "Player input: box-select the whole army and click one destination.", "Expected output: 10k units receive allocated slots and a shared flow command.", "Pass signal: commanded=10000, movement/flow is recorded, and FPS budget is checked separately.", gate },
            "obstacles" => new[] { "Tool input: load the 40k static obstacle world asset.", "Expected output: planned, authored, baked, loaded and solver-active counts are separate.", "Pass signal: yellow buckets and bright green solver-active subset are visually distinct.", gate },
            "fps" => new[] { "Tool input: compare debug-off and debug-on frames.", "Expected output: overlay cost and production FPS budget are measured.", first, gate },
            _ => new[] { first, second, gate }
        };
    }

    private static IReadOnlyList<string> BuildMassNavigationKeyframeEvidenceLines(MassNavigationSnapshot snapshot, string mode, string status, IReadOnlyList<string> lines)
    {
        var result = new List<string>
        {
            $"Status={status}; snapshot={snapshot.Step}; world={snapshot.WorldWidthCm / 100000f:F1}km x {snapshot.WorldHeightCm / 100000f:F1}km.",
            $"Agents selected/commanded/moving/settled={snapshot.SelectedCount}/{snapshot.CommandedAgents}/{snapshot.MovingAgents}/{snapshot.SettledAgents}.",
        };

        switch (mode)
        {
            case "path":
                result.Add($"Path-only status={snapshot.PathOnlyQuery.Status}; noOrder={snapshot.PathOnlyQuery.NoOrderSubmitted}; waypoints/pathpoints={snapshot.PathOnlyQuery.WaypointCount}/{snapshot.PathOnlyQuery.PathPointCount}.");
                result.Add($"Route provenance={snapshot.PathOnlyQuery.RouteProvenance}; touchedTiles={snapshot.PathOnlyQuery.TouchedTileCount}; portals={snapshot.PathOnlyQuery.CorridorPortalCount}.");
                break;
            case "strategy":
            case "layer":
                result.Add($"Strategy rows={snapshot.StrategySwitchDiagnostics.Count}; layers/profiles/areaCosts={snapshot.NavMeshLayerCount}/{snapshot.NavMeshProfileCount}/{snapshot.NavMeshAreaCostCount}.");
                result.Add($"Selected strategies={string.Join(", ", snapshot.StrategySwitchDiagnostics.Take(5).Select(item => item.AgentTypeId + ":" + item.SelectedStrategy))}.");
                break;
            case "hpa":
                result.Add($"Macro={snapshot.MacroChunkColumns}x{snapshot.MacroChunkRows}; expected edges={snapshot.ExpectedMacroAdjacencyEdgeCount}; route portals={snapshot.HpaMacroDiagnostics.SamplePortalCount}.");
                result.Add($"Route chunks: {BuildMassNavigationHpaRouteChunkManifest(snapshot)}.");
                result.Add($"Active-window graph tiles/nodes/edges={snapshot.HpaGraphDiagnostics.LoadedTileCount}/{snapshot.HpaGraphDiagnostics.GraphNodeCount}/{snapshot.HpaGraphDiagnostics.GraphEdgeCount}; route portals={snapshot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount}.");
                break;
            case "navmesh_gap":
                result.Add($"Large-world NavMesh tiles baked/notLoaded/total={snapshot.NavMeshBake.BakedChunks}/{snapshot.NavMeshBake.NotLoadedChunks}/{snapshot.NavMeshBake.TotalChunks}; failed={snapshot.NavMeshBake.FailedChunks}.");
                result.Add("NavMesh visual contract: walkable triangles, blocked/high-cost source, portal clearance, agent radius and mesh-link status must be visible in the guided view.");
                result.Add($"Active-window graph tiles={snapshot.HpaGraphDiagnostics.LoadedTileCount}/{snapshot.HpaGraphDiagnostics.ActiveWindowChunkCount}; streaming loaded/notLoaded counts stay explicit.");
                break;
            case "bake_tool":
                result.Add("Bake sources: vtxm, vhtm and lhtm all normalize to LogicHeightmap before .ntil bake.");
                result.Add("Validator outputs: 001 coverage, 002 tile detail, 003 path-only, 004 HPA, 005 layer editor, plus nav-bake-raylib-result.json.");
                break;
            case "waypoint_before":
            case "waypoint_after":
                result.Add($"Waypoint/pathpoint contract: waypoints={snapshot.WaypointPathDiagnostics.WaypointCount}, pathpoints={snapshot.WaypointPathDiagnostics.PathPointCount}, editable={snapshot.WaypointPathDiagnostics.WaypointsEditable}, immutable={snapshot.WaypointPathDiagnostics.PathPointsImmutable}.");
                result.Add($"Source={snapshot.WaypointPathDiagnostics.Source}; pathpointsCanSeedWaypoints={snapshot.WaypointPathDiagnostics.PathPointsCanSeedWaypoints}.");
                break;
            case "allocation":
                result.Add($"Reuse hit={snapshot.OrderReuse.CacheHit}; routeId={snapshot.OrderReuse.ReusedRouteId}; same/near={snapshot.OrderReuse.SamePointReuseCount}/{snapshot.OrderReuse.NearPointReuseCount}; fanout={snapshot.OrderReuse.FanoutCount}.");
                result.Add($"Target slots selected/slots/reachable/blocked={snapshot.TargetAllocation.SelectedCount}/{snapshot.TargetAllocation.SlotCount}/{snapshot.TargetAllocation.ReachableSlotCount}/{snapshot.TargetAllocation.BlockedSlotCount}.");
                break;
            case "full":
                result.Add($"Target slots={snapshot.TargetAllocation.SlotCount}; reachable={snapshot.TargetAllocation.ReachableSlotCount}; fallback={snapshot.TargetAllocation.FallbackSlotCount}; flow={snapshot.FlowEnabled}.");
                result.Add($"Frame={snapshot.FrameMs:F2}ms; MassNav={snapshot.MassNavigationMs:F2}ms; minimap dropped={snapshot.MinimapDroppedTotal}.");
                break;
            case "obstacles":
                result.Add($"Obstacles target/authored/baked/loaded/solver={snapshot.ObstacleDiagnostics.TargetStaticObstacleCount}/{snapshot.ObstacleDiagnostics.AuthoredStaticObstacleCount}/{snapshot.ObstacleDiagnostics.BakedStaticObstacleCount}/{snapshot.ObstacleDiagnostics.LoadedStaticObstacleCount}/{snapshot.ObstacleDiagnostics.SolverActiveStaticObstacleCount}.");
                result.Add($"Runtime activation={snapshot.StaticObstacleWorldDiagnostics.RuntimeActivationStrategy}; macro coverage={snapshot.StaticObstacleWorldDiagnostics.MacroChunkCoverageCount}.");
                break;
            case "fps":
                result.Add($"Frame={snapshot.FrameMs:F2}ms; simulation/presentation/massNav={snapshot.SimulationMs:F2}/{snapshot.PresentationMs:F2}/{snapshot.MassNavigationMs:F2}ms.");
                result.Add("Production FPS gate is evaluated by Raylib framebuffer p95/p99/overlay thresholds.");
                break;
            default:
                result.Add($"Macro={snapshot.MacroChunkColumns}x{snapshot.MacroChunkRows}; NavMesh baked/notLoaded/total={snapshot.NavMeshBake.BakedChunks}/{snapshot.NavMeshBake.NotLoadedChunks}/{snapshot.NavMeshBake.TotalChunks}.");
                result.Add($"Scenario spawn/reset/rejects={snapshot.ScenarioSpawnCount}/{snapshot.SceneResetCount}/{snapshot.CommandRejectsTotal}.");
                break;
        }

        foreach (string line in lines.Skip(2).Take(4))
        {
            result.Add(line);
        }

        return result;
    }

    private static Vector2 ResolveMassNavigationOverlayCenter(MassNavigationSnapshot snapshot, string mode)
    {
        if (mode is "allocation" or "full")
        {
            return snapshot.TargetAllocation.HasAllocation
                ? snapshot.TargetAllocation.DestinationWorldCm
                : new Vector2(1_050_000, -780_000);
        }

        if (mode is "path" or "strategy" or "layer" or "hpa")
        {
            Vector2 start = snapshot.PathOnlyQuery.StartWorldCm == Vector2.Zero ? new Vector2(-2_200_000, -1_600_000) : snapshot.PathOnlyQuery.StartWorldCm;
            Vector2 goal = snapshot.PathOnlyQuery.GoalWorldCm == Vector2.Zero ? new Vector2(2_200_000, 1_600_000) : snapshot.PathOnlyQuery.GoalWorldCm;
            return (start + goal) * 0.5f;
        }

        return snapshot.FlowWorkAreaCenterCm;
    }

    private static float ResolveMassNavigationOverlayHalfExtent(MassNavigationSnapshot snapshot, string mode)
    {
        if (mode is "allocation" or "full")
        {
            int slots = Math.Max(1, snapshot.TargetAllocation.SlotCount);
            int cols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(slots)));
            return MathF.Max(16_000f, cols * 520f);
        }

        if (mode is "path" or "strategy" or "layer" or "hpa")
        {
            Vector2 start = snapshot.PathOnlyQuery.StartWorldCm == Vector2.Zero ? new Vector2(-2_200_000, -1_600_000) : snapshot.PathOnlyQuery.StartWorldCm;
            Vector2 goal = snapshot.PathOnlyQuery.GoalWorldCm == Vector2.Zero ? new Vector2(2_200_000, 1_600_000) : snapshot.PathOnlyQuery.GoalWorldCm;
            return MathF.Max(50_000f, Vector2.Distance(start, goal) * 0.62f);
        }

        return MathF.Max(25_000f, MathF.Max(snapshot.FlowWorkAreaWidthCm, snapshot.FlowWorkAreaHeightCm) * 0.65f);
    }

    private static IReadOnlyList<string> BuildMassNavigationKeyframeMachineFieldHints(string mode)
    {
        return mode switch
        {
            "path" => new[] { "summary.path_only_query.PreviewMode/InputContract/RoutePreviewState", "summary.path_only_query.NoOrderSubmitted/order_delta", "summary.path_only_query.PathPointContract/WaypointContract", "summary.waypoint_path_diagnostics.*" },
            "strategy" => new[] { "summary.strategy_switch_diagnostics[]", "summary.layer_cost_query_matrix[]", "summary.use_case_statuses[]" },
            "layer" => new[] { "summary.layer_cost_diagnostics[]", "summary.layer_cost_query_matrix[]", "configs/Navigation/navmesh.json" },
            "hpa" => new[] { "summary.hpa_macro_diagnostics.*", "summary.navmesh_*_tiles", "screens/004_bake_hpa_overlay.png" },
            "navmesh_gap" => new[] { "summary.navmesh_baked_tiles/not_loaded/total", "summary.hpa_graph_diagnostics.ActiveWindow*", "summary.production_gate_failed_checks[]" },
            "bake_tool" => new[] { "navmesh-*-current/screens/nav-bake-raylib-result.json", "navmesh-*-current/screens/001..005.png", "nav-bake-source-manifest.json" },
            "waypoint_before" => new[] { "summary.waypoint_path_diagnostics.*", "summary.path_only_query.PathPointContract", "summary.path_only_query.WaypointContract" },
            "waypoint_after" => new[] { "summary.waypoint_path_diagnostics.PathPointsImmutable", "summary.waypoint_path_diagnostics.PathPointsCanSeedWaypoints", "summary.path_only_query.PathPointCount" },
            "allocation" => new[] { "summary.order_reuse.*", "summary.target_allocation_reuse_probe.*", "summary.screenshot_keyframes[]" },
            "full" => new[] { "summary.target_allocation.*", "summary.movement_proof.*", "summary.frame_timing.*" },
            "obstacles" => new[] { "summary.static_obstacle_world_diagnostics.*", "summary.obstacle_diagnostics.*", "summary.production_gate_failed_checks[]" },
            "fps" => new[] { "summary.frame_timing.renderer_scope", "summary.raylib_frame_benchmark.*", "screens/raylib-frame-benchmark.json" },
            _ => new[] { "summary.use_case_statuses[]", "summary.evidence_manifest[]", "visible-checklist.md" },
        };
    }

    private static double EstimateMassNavigationSampleAdvanceCm(MassNavigationSnapshot start, MassNavigationSnapshot end)
    {
        int count = Math.Min(start.SamplePositions.Count, end.SamplePositions.Count);
        if (count == 0)
        {
            return 0d;
        }

        var distances = new double[count];
        for (int i = 0; i < count; i++)
        {
            distances[i] = Vector2.Distance(start.SamplePositions[i].WorldCm, end.SamplePositions[i].WorldCm);
        }

        Array.Sort(distances);
        return Math.Round(PercentileSorted(distances, 0.5d), 3);
    }


    private static void DrawMassNavigationStatusPill(SKCanvas canvas, string status, float x, float y)
    {
        SKColor color = status switch
        {
            "SMOKE" => new SKColor(62, 166, 101),
            "SCENE_SMOKE" => new SKColor(62, 142, 205),
            "CONFIG_SMOKE" => new SKColor(160, 135, 58),
            "NEEDS_MANUAL_UAT" => new SKColor(188, 132, 55),
            "CONCEPT" => new SKColor(135, 116, 198),
            "MISSING" => new SKColor(196, 84, 84),
            "BLOCKED" => new SKColor(196, 84, 84),
            _ => new SKColor(94, 113, 130)
        };

        using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 12f, FakeBoldText = true };
        canvas.DrawRect(new SKRect(x, y, x + 104, y + 24), fill);
        canvas.DrawText(TruncateForImage(status, 13), x + 8, y + 17, text);
    }

    private static string TruncateForImage(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value ?? string.Empty;
        }

        return value[..Math.Max(0, maxChars - 3)] + "...";
    }

    private static float DrawMassNavigationWrappedText(
        SKCanvas canvas,
        string value,
        SKRect rect,
        SKPaint paint,
        float lineHeight,
        int maxLines = 12)
    {
        float y = rect.Top;
        int written = 0;
        foreach (string line in WrapMassNavigationText(value, paint, rect.Width))
        {
            if (written >= maxLines || y > rect.Bottom)
            {
                break;
            }

            canvas.DrawText(line, rect.Left, y, paint);
            y += lineHeight;
            written++;
        }

        return y;
    }

    private static float DrawMassNavigationBulletLines(
        SKCanvas canvas,
        IEnumerable<string> lines,
        SKRect rect,
        SKPaint paint,
        float lineHeight,
        int maxLines = 12)
    {
        float y = rect.Top;
        int written = 0;
        foreach (string line in lines)
        {
            bool firstWrappedLine = true;
            foreach (string wrapped in WrapMassNavigationText(line, paint, rect.Width - 18f))
            {
                if (written >= maxLines || y > rect.Bottom)
                {
                    return y;
                }

                canvas.DrawText(firstWrappedLine ? "-" : " ", rect.Left, y, paint);
                canvas.DrawText(wrapped, rect.Left + 18f, y, paint);
                y += lineHeight;
                written++;
                firstWrappedLine = false;
            }
        }

        return y;
    }

    private static IReadOnlyList<string> WrapMassNavigationText(string value, SKPaint paint, float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        string current = words[0];
        for (int i = 1; i < words.Length; i++)
        {
            string candidate = current + " " + words[i];
            if (paint.MeasureText(candidate) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            lines.Add(FitMassNavigationText(current, paint, maxWidth));
            current = words[i];
        }

        lines.Add(FitMassNavigationText(current, paint, maxWidth));
        return lines;
    }

    private static string FitMassNavigationText(string value, SKPaint paint, float maxWidth)
    {
        if (paint.MeasureText(value) <= maxWidth)
        {
            return value;
        }

        string ellipsis = "...";
        int maxChars = value.Length;
        while (maxChars > 1 && paint.MeasureText(value[..maxChars] + ellipsis) > maxWidth)
        {
            maxChars--;
        }

        return value[..Math.Max(1, maxChars)] + ellipsis;
    }

    private static void DrawMassNavigationPanelTitle(SKCanvas canvas, string title, string subtitle, SKRect rect)
    {
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 18f, FakeBoldText = true };
        using var subtitlePaint = new SKPaint { Color = new SKColor(166, 187, 202), IsAntialias = true, TextSize = 13f };
        canvas.DrawText(title, rect.Left + 16, rect.Top + 28, titlePaint);
        DrawMassNavigationWrappedText(
            canvas,
            subtitle,
            new SKRect(rect.Left + 16, rect.Top + 50, rect.Right - 16, rect.Top + 76),
            subtitlePaint,
            17f,
            2);
    }

    private static void WriteMassNavigationSnapshotImage(MassNavigationSnapshot snapshot, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(MassNavigationImageWidth, MassNavigationImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(8, 12, 18));

        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 24f, FakeBoldText = true };
        using var subtitlePaint = new SKPaint { Color = new SKColor(190, 205, 216), IsAntialias = true, TextSize = 16f };
        using var smallPaint = new SKPaint { Color = new SKColor(160, 178, 192), IsAntialias = true, TextSize = 13f };
        using var panelFill = new SKPaint { Color = new SKColor(10, 20, 29), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelStroke = new SKPaint { Color = new SKColor(70, 103, 122), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };

        var overviewRect = new SKRect(36, 120, 636, 720);
        var zoomRect = new SKRect(670, 120, 1038, 488);
        var cardRect = new SKRect(1070, 120, 1548, 488);
        var dataRect = new SKRect(670, 520, 1548, 842);

        canvas.DrawText($"MassNavigation UAT | {BuildMassNavigationStepTitle(snapshot.Step)}", 36, 44, titlePaint);
        canvas.DrawText(BuildMassNavigationStepPlainLanguage(snapshot), 36, 72, subtitlePaint);
        DrawMassNavigationVerdictPill(canvas, BuildMassNavigationStepVerdict(snapshot), 36, 88);

        canvas.DrawRect(overviewRect, panelFill);
        canvas.DrawRect(overviewRect, panelStroke);
        DrawMassNavigationWorldOverview(canvas, snapshot, overviewRect);

        canvas.DrawRect(zoomRect, panelFill);
        canvas.DrawRect(zoomRect, panelStroke);
        if (HasMassNavigationRuntimeGuideOverlay(snapshot))
        {
            DrawMassNavigationRuntimeGuideZoom(canvas, snapshot, zoomRect);
        }
        else
        {
            DrawMassNavigationLocalZoom(canvas, snapshot, zoomRect);
        }

        canvas.DrawRect(cardRect, panelFill);
        canvas.DrawRect(cardRect, panelStroke);
        DrawMassNavigationStepCard(canvas, snapshot, cardRect);

        canvas.DrawRect(dataRect, panelFill);
        canvas.DrawRect(dataRect, panelStroke);

        if (HasMassNavigationRuntimeGuideOverlay(snapshot))
        {
            DrawMassNavigationRuntimeGuideDataPanel(canvas, snapshot, dataRect);
        }
        else if (string.Equals(snapshot.Step, "004_bake_hpa_overlay", StringComparison.Ordinal))
        {
            DrawMassNavigationBakeHpaDataPanel(canvas, snapshot, dataRect);
        }
        else if (string.Equals(snapshot.Step, "005_path_strategy_inspector", StringComparison.Ordinal))
        {
            DrawMassNavigationPathStrategyDataPanel(canvas, snapshot, dataRect);
        }
        else if (string.Equals(snapshot.Step, "006_order_reuse_target_allocation", StringComparison.Ordinal))
        {
            DrawMassNavigationOrderReuseDataPanel(canvas, snapshot, dataRect);
        }
        else if (string.Equals(snapshot.Step, "007_10k_commanded_flow_probe", StringComparison.Ordinal))
        {
            DrawMassNavigationFullCommandDataPanel(canvas, snapshot, dataRect);
        }
        else
        {
            DrawMassNavigationBakeSummaryDataPanel(canvas, snapshot, dataRect);
        }

        canvas.DrawText("Performance is shown only as supporting evidence. First read action, map relation, and verdict.", 670, 872, smallPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static string BuildMassNavigationStepTitle(string step)
    {
        return step switch
        {
            "000_boot" => "Boot 64km World",
            "001_selection_order" => "Select Army And Issue Order",
            "002_remote_minimap_jump" => "Jump To Remote Hot Zone",
            "003_return_original_area" => "Return To Original Area",
            "004_bake_hpa_overlay" => "Bake And HPA Overlay",
            "005_path_strategy_inspector" => "Path Strategy Inspector",
            "006_order_reuse_target_allocation" => "Order Reuse And Allocation",
            "006a_runtime_u1_visual_heightmap_bake" => "U1 VisualHeightmap Bake",
            "006b_runtime_u2_logic_heightmap_bake" => "U2 LogicHeightmap Unification",
            "006c_runtime_u3_layer_area_editor" => "U3 Layer Area Editor",
            "006d_runtime_u4_path_only" => "U4 Path-Only Query",
            "006e_runtime_u5_world_hpa" => "U5 World HPA Route",
            "006f_runtime_u6_strategy_switch" => "U6 Strategy Switch",
            "006g_runtime_u7_order_reuse" => "U7 Same/Near Order Reuse",
            "006h_runtime_u8_target_allocation" => "U8 Target Allocation",
            "006i_runtime_u9_layer_costs" => "U9 Layer Cost Profiles",
            "006j_runtime_u10_waypoint_authoring" => "U10 Waypoint Authoring",
            "006k_runtime_u11_large_world" => "U11 Large World Window",
            "006l_runtime_u12_10k_flow" => "U12 10k Flow",
            "006m_runtime_u13_static_obstacles" => "U13 40k Static Obstacles",
            "006n_runtime_u14_fps_scope" => "U14 FPS Scope",
            "006o_runtime_u15_debug_budget" => "U15 Debug Visual Budget",
            "006p_runtime_u16_bake_tool" => "U16 Bake Tool Query",
            "008_acceptance_gate_matrix" => "Acceptance Gate Matrix",
            "009_raylib_frame_benchmark" => "Raylib Frame Benchmark",
            "007_10k_commanded_flow_probe" => "10k Commanded Flow Probe",
            _ => step
        };
    }

    private static string BuildMassNavigationStepPlainLanguage(MassNavigationSnapshot snapshot)
    {
        return snapshot.Step switch
        {
            "000_boot" => "Open the 64km world and verify the navigation showcase binds data without claiming production readiness.",
            "001_selection_order" => "Box-select the army and click a destination; units should receive one shared move order.",
            "002_remote_minimap_jump" => "Use the minimap to jump to a far hot zone; the world marker should move across the overview map.",
            "003_return_original_area" => "Jump back to the original hot zone; the camera should return while the scenario is not respawned.",
            "004_bake_hpa_overlay" => "Inspect active-window NavMesh and HPA graph evidence; streamed-out tiles stay explicit.",
            "005_path_strategy_inspector" => "Compare Road, NavMesh and Hybrid route choices for the same query.",
            "006_order_reuse_target_allocation" => "Repeat same/near orders and verify route reuse plus target-slot allocation.",
            "006a_runtime_u1_visual_heightmap_bake" => "Click U1 VHTM and verify visual terrain becomes LogicHeightmap, NavMesh tile and query data.",
            "006b_runtime_u2_logic_heightmap_bake" => "Click U2 Logic and verify every terrain source converges into the same LogicHeightmap bake contract.",
            "006c_runtime_u3_layer_area_editor" => "Click U3 Areas and verify mountains, rivers, high-cost and blocked masks are visible before bake trust.",
            "006d_runtime_u4_path_only" => "Click Path Preview and verify a highlighted route appears without submitting a unit order.",
            "006e_runtime_u5_world_hpa" => "Click World/HPA and verify the crossed chunks are numbered, highlighted and tied to portals.",
            "006f_runtime_u6_strategy_switch" => "Click Strategy and compare road graph, NavMesh and hybrid results for the same query.",
            "006g_runtime_u7_order_reuse" => "Select Reuse Squad, then right-click the same destination twice and a nearby destination once; verify the same route bucket is reused for near-identical commands.",
            "006h_runtime_u8_target_allocation" => "Select 10k Army, then right-click one destination and verify it expands into reachable formation slots.",
            "006i_runtime_u9_layer_costs" => "Click Layer/Cost and verify air, water, mountain and ground profiles use different layers and costs.",
            "006j_runtime_u10_waypoint_authoring" => "Click Waypoint Edit and verify editable waypoints are separate from immutable pathpoints.",
            "006k_runtime_u11_large_world" => "Click U11 World and verify 64km, 256x256 chunks, active window and not-loaded production gap.",
            "006l_runtime_u12_10k_flow" => "Select 10k Army, then right-click a destination and verify 10k command, slot allocation and flow smoke stay visually readable.",
            "006m_runtime_u13_static_obstacles" => "Click U13 Obstacles and verify authored, baked, loaded and solver-active obstacle counts are separate.",
            "006n_runtime_u14_fps_scope" => "Click U14 FPS and verify timing scope is visible without claiming production FPS.",
            "006o_runtime_u15_debug_budget" => "Click U15 Debug and verify debug visuals are sampled, bounded and measured.",
            "006p_runtime_u16_bake_tool" => "Click U16 BakeTool and verify the bake workbench explains source, LogicHeightmap, .ntil and validator outputs.",
            "007_10k_commanded_flow_probe" => "Submit a 10k-unit order and verify flow movement and allocation smoke.",
            _ => "Inspect this MassNavigation acceptance step."
        };
    }

    private static string BuildMassNavigationStepVerdict(MassNavigationSnapshot snapshot)
    {
        return snapshot.Step switch
        {
            "003_return_original_area" when snapshot.ScenarioSpawnCount == 1 && snapshot.SceneResetCount == 0 => "SMOKE OK",
            "004_bake_hpa_overlay" => "MACHINE OK",
            "005_path_strategy_inspector" => "SMOKE OK",
            "006_order_reuse_target_allocation" => snapshot.OrderReuse.CacheHit ? "SMOKE OK" : "CHECK",
            string runtimeStep when runtimeStep.StartsWith("006", StringComparison.Ordinal) &&
                runtimeStep.Contains("_runtime_", StringComparison.Ordinal) => "PLAYABLE GUIDE",
            "007_10k_commanded_flow_probe" when snapshot.CommandedAgents >= 10000 => "MACHINE OK",
            _ => "SMOKE EVIDENCE"
        };
    }

    private static void DrawMassNavigationVerdictPill(SKCanvas canvas, string verdict, float x, float y)
    {
        SKColor color = verdict.Contains("BLOCKED", StringComparison.Ordinal)
            ? new SKColor(169, 96, 54)
            : verdict.Contains("OK", StringComparison.Ordinal)
                ? new SKColor(50, 143, 86)
                : new SKColor(80, 104, 128);
        using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 13f, FakeBoldText = true };
        canvas.DrawRect(new SKRect(x, y, x + 220, y + 24), fill);
        canvas.DrawText(verdict, x + 10, y + 17, text);
    }

    private static void DrawMassNavigationWorldOverview(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect rect)
    {
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 18f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(186, 204, 218), IsAntialias = true, TextSize = 13f };
        using var gridPaint = new SKPaint { Color = new SKColor(60, 85, 100), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var worldPaint = new SKPaint { Color = new SKColor(18, 34, 46), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var worldStroke = new SKPaint { Color = new SKColor(75, 130, 162), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        using var hotZoneStroke = new SKPaint { Color = new SKColor(255, 214, 102), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f };
        using var cameraPaint = new SKPaint { Color = new SKColor(255, 96, 96), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f };
        using var activePaint = new SKPaint { Color = new SKColor(103, 224, 145), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f };
        using var flowPaint = new SKPaint { Color = new SKColor(80, 190, 255), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f };

        canvas.DrawText("World Overview", rect.Left + 16, rect.Top + 28, labelPaint);
        canvas.DrawText("64km x 64km map, hot zones, current camera and active data window", rect.Left + 16, rect.Top + 50, smallPaint);
        var map = new SKRect(rect.Left + 28, rect.Top + 70, rect.Right - 28, rect.Bottom - 48);
        canvas.DrawRect(map, worldPaint);
        canvas.DrawRect(map, worldStroke);
        for (int i = 1; i < 8; i++)
        {
            float x = map.Left + map.Width * i / 8f;
            float y = map.Top + map.Height * i / 8f;
            canvas.DrawLine(x, map.Top, x, map.Bottom, gridPaint);
            canvas.DrawLine(map.Left, y, map.Right, y, gridPaint);
        }

        foreach (MassNavigationHotZoneSample zone in snapshot.HotZones)
        {
            DrawMassNavigationWorldRect(canvas, snapshot, map, zone.CenterCm, zone.WidthCm, zone.HeightCm, hotZoneStroke);
            SKPoint point = ToMassNavigationScreen(zone.CenterCm, snapshot, map);
            canvas.DrawCircle(point.X, point.Y, 4.5f, zone.Id == snapshot.ActiveHotZoneId ? activePaint : hotZoneStroke);
            canvas.DrawText(TruncateForImage(zone.Label, 18), point.X + 8, point.Y - 6, smallPaint);
        }

        DrawMassNavigationWorldRect(canvas, snapshot, map, snapshot.FlowWorkAreaCenterCm, snapshot.FlowWorkAreaWidthCm, snapshot.FlowWorkAreaHeightCm, flowPaint);
        DrawMassNavigationWorldRect(canvas, snapshot, map, snapshot.SolverWindowCenterCm, snapshot.SolverWindowWidthCm, snapshot.SolverWindowHeightCm, activePaint);
        DrawCrosshair(canvas, ToMassNavigationScreen(snapshot.CameraTargetCm, snapshot, map), 12f, cameraPaint);
        canvas.DrawText($"Camera {FormatPoint(snapshot.CameraTargetCm)}", rect.Left + 28, rect.Bottom - 24, smallPaint);
    }

    private static void DrawMassNavigationLocalZoom(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect rect)
    {
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 18f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(186, 204, 218), IsAntialias = true, TextSize = 13f };
        using var borderPaint = new SKPaint { Color = new SKColor(80, 130, 160), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
        using var cellPaint = new SKPaint { Color = new SKColor(32, 48, 58), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var solverPaint = new SKPaint { Color = new SKColor(103, 224, 145), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f };
        using var flowPaint = new SKPaint { Color = new SKColor(80, 190, 255), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var cameraPaint = new SKPaint { Color = new SKColor(255, 96, 96), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f };

        canvas.DrawText("Local Data Window", rect.Left + 16, rect.Top + 28, labelPaint);
        canvas.DrawText("Solver box, flow work area and sampled units", rect.Left + 16, rect.Top + 50, smallPaint);
        var map = new SKRect(rect.Left + 22, rect.Top + 70, rect.Right - 22, rect.Bottom - 44);
        canvas.DrawRect(map, borderPaint);
        for (int i = 1; i < 6; i++)
        {
            float x = map.Left + map.Width * i / 6f;
            float y = map.Top + map.Height * i / 6f;
            canvas.DrawLine(x, map.Top, x, map.Bottom, cellPaint);
            canvas.DrawLine(map.Left, y, map.Right, y, cellPaint);
        }

        Vector2 center = snapshot.FlowWorkAreaCenterCm;
        float half = MathF.Max(snapshot.FlowWorkAreaWidthCm, snapshot.FlowWorkAreaHeightCm) * 0.65f;
        if (half <= 0) half = 25000f;
        DrawMassNavigationLocalRect(canvas, snapshot.FlowWorkAreaCenterCm, snapshot.FlowWorkAreaWidthCm, snapshot.FlowWorkAreaHeightCm, center, half, map, flowPaint);
        DrawMassNavigationLocalRect(canvas, snapshot.SolverWindowCenterCm, snapshot.SolverWindowWidthCm, snapshot.SolverWindowHeightCm, center, half, map, solverPaint);
        DrawCrosshair(canvas, ToMassNavigationLocalScreen(snapshot.CameraTargetCm, center, half, map), 9f, cameraPaint);

        int stride = Math.Max(1, snapshot.SamplePositions.Count / 160);
        for (int i = 0; i < snapshot.SamplePositions.Count; i += stride)
        {
            MassNavigationAgentSample sample = snapshot.SamplePositions[i];
            SKPoint point = ToMassNavigationLocalScreen(sample.WorldCm, center, half, map);
            if (!map.Contains(point.X, point.Y))
            {
                continue;
            }

            using var teamPaint = new SKPaint { Color = ResolveMassNavigationTeamColor(sample.TeamId), IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawCircle(point.X, point.Y, 3.2f, teamPaint);
        }

        canvas.DrawText($"Loaded chunks={snapshot.LoadedChunkCount}  Flow={snapshot.FlowEnabled}", rect.Left + 22, rect.Bottom - 20, smallPaint);
    }

    private static void DrawMassNavigationRuntimeGuideZoom(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect rect)
    {
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 18f, FakeBoldText = true };
        using var smallPaint = new SKPaint { Color = new SKColor(186, 204, 218), IsAntialias = true, TextSize = 12f };
        using var borderPaint = new SKPaint { Color = new SKColor(72, 118, 150), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f };
        using var fillPaint = new SKPaint { Color = new SKColor(16, 30, 42), IsAntialias = true, Style = SKPaintStyle.Fill };

        string mode = ResolveMassNavigationRuntimeGuideMode(snapshot.Step);
        canvas.DrawText("Debug Presentation", rect.Left + 16, rect.Top + 28, labelPaint);
        canvas.DrawText(BuildMassNavigationRuntimeGuideZoomSubtitle(mode), rect.Left + 16, rect.Top + 50, smallPaint);
        var map = new SKRect(rect.Left + 22, rect.Top + 74, rect.Right - 22, rect.Bottom - 30);
        canvas.DrawRect(map, fillPaint);
        canvas.DrawRect(map, borderPaint);

        canvas.Save();
        canvas.ClipRect(map);
        if (mode == "navmesh_workbench")
        {
            DrawMassNavigationNavMeshWorkbenchOverlay(canvas, snapshot, map);
        }
        else if (mode == "path")
        {
            Vector2 center = ResolveMassNavigationOverlayCenter(snapshot, mode);
            float half = ResolveMassNavigationOverlayHalfExtent(snapshot, mode);
            DrawMassNavigationPathOnlyShowcaseOverlay(canvas, snapshot, map, center, half);
        }
        else if (mode == "hpa")
        {
            DrawMassNavigationHpaShowcaseOverlay(canvas, snapshot, map);
        }
        else if (mode == "strategy")
        {
            Vector2 center = ResolveMassNavigationOverlayCenter(snapshot, mode);
            float half = ResolveMassNavigationOverlayHalfExtent(snapshot, mode);
            DrawMassNavigationStrategyCompareShowcaseOverlay(canvas, snapshot, map, center, half);
        }
        else if (mode == "layer")
        {
            DrawMassNavigationLayerAreaShowcaseOverlay(canvas, snapshot, map);
        }
        else if (mode == "navmesh_gap")
        {
            DrawMassNavigationNavMeshGapShowcaseOverlay(canvas, snapshot, map);
        }
        else if (mode == "bake_tool")
        {
            DrawMassNavigationRuntimeBakeToolCompactOverlay(canvas, snapshot, map);
        }
        else if (mode == "waypoint_after")
        {
            DrawMassNavigationRuntimeWaypointCompactOverlay(canvas, snapshot, map);
        }
        else if (mode == "allocation" || mode == "full")
        {
            DrawMassNavigationRuntimeSlotOverlay(canvas, snapshot, map, mode == "full");
        }
        else if (mode == "obstacles")
        {
            DrawMassNavigationRuntimeObstacleOverlay(canvas, snapshot, map);
        }
        else
        {
            DrawMassNavigationRuntimeTimingOverlay(canvas, snapshot, map);
        }

        canvas.Restore();
    }

    private static void DrawMassNavigationStepCard(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect rect)
    {
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 19f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(210, 224, 235), IsAntialias = true, TextSize = 14f };
        using var mutedPaint = new SKPaint { Color = new SKColor(150, 170, 184), IsAntialias = true, TextSize = 13f };
        canvas.DrawText("What This Step Proves", rect.Left + 16, rect.Top + 30, titlePaint);

        IReadOnlyList<string> lines = BuildMassNavigationStepCardLines(snapshot);
        float y = rect.Top + 64;
        foreach (string line in lines)
        {
            canvas.DrawText(TruncateForImage(line, 58), rect.Left + 16, y, linePaint);
            y += 26;
        }

        canvas.DrawText("Supporting counters", rect.Left + 16, y + 18, titlePaint);
        y += 50;
        canvas.DrawText($"Agents selected/commanded/moving: {snapshot.SelectedCount}/{snapshot.CommandedAgents}/{snapshot.MovingAgents}", rect.Left + 16, y, mutedPaint);
        y += 24;
        canvas.DrawText($"Scenario spawn/reset/rejects: {snapshot.ScenarioSpawnCount}/{snapshot.SceneResetCount}/{snapshot.CommandRejectsTotal}", rect.Left + 16, y, mutedPaint);
        y += 24;
        canvas.DrawText($"Frame {snapshot.FrameMs:F2}ms, MassNav {snapshot.MassNavigationMs:F2}ms, minimap dropped {snapshot.MinimapDroppedTotal}", rect.Left + 16, y, mutedPaint);
    }

    private static void DrawMassNavigationRuntimeGuideDataPanel(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect panel)
    {
        using var titlePaint = new SKPaint { Color = new SKColor(248, 251, 255), IsAntialias = true, TextSize = 20f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(210, 224, 235), IsAntialias = true, TextSize = 14f };
        using var mutedPaint = new SKPaint { Color = new SKColor(160, 178, 192), IsAntialias = true, TextSize = 12.5f };
        using var passPaint = new SKPaint { Color = new SKColor(92, 224, 136), IsAntialias = true, TextSize = 13.5f, FakeBoldText = true };
        using var gatePaint = new SKPaint { Color = new SKColor(255, 190, 110), IsAntialias = true, TextSize = 13.5f, FakeBoldText = true };
        using var chipFill = new SKPaint { Color = new SKColor(18, 42, 54), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var chipStroke = new SKPaint { Color = new SKColor(74, 128, 156), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f };

        canvas.DrawText("Playable Runtime Guide", panel.Left + 16, panel.Top + 30, titlePaint);
        string useCaseId = ResolveMassNavigationRuntimeUseCaseId(snapshot.Step);
        string mode = ResolveMassNavigationRuntimeGuideMode(snapshot.Step);
        canvas.DrawRect(new SKRect(panel.Right - 164, panel.Top + 14, panel.Right - 18, panel.Top + 42), chipFill);
        canvas.DrawRect(new SKRect(panel.Right - 164, panel.Top + 14, panel.Right - 18, panel.Top + 42), chipStroke);
        canvas.DrawText($"{useCaseId} / {mode}", panel.Right - 154, panel.Top + 33, mutedPaint);

        IReadOnlyList<string> guideLines = BuildMassNavigationRuntimeGuidePanelLines(snapshot);
        DrawMassNavigationBulletLines(
            canvas,
            guideLines,
            new SKRect(panel.Left + 18, panel.Top + 62, panel.Right - 18, panel.Top + 206),
            linePaint,
            20f,
            8);

        canvas.DrawText("Pass Signal", panel.Left + 18, panel.Top + 236, passPaint);
        DrawMassNavigationBulletLines(
            canvas,
            BuildMassNavigationRuntimePassSignalLines(snapshot),
            new SKRect(panel.Left + 18, panel.Top + 258, panel.Left + 420, panel.Bottom - 26),
            mutedPaint,
            18f,
            6);

        canvas.DrawText("Production Gate", panel.Left + 456, panel.Top + 236, gatePaint);
        DrawMassNavigationBulletLines(
            canvas,
            BuildMassNavigationRuntimeGateLines(snapshot),
            new SKRect(panel.Left + 456, panel.Top + 258, panel.Right - 18, panel.Bottom - 26),
            mutedPaint,
            18f,
            6);
    }

    private static IReadOnlyList<string> BuildMassNavigationRuntimeGuidePanelLines(MassNavigationSnapshot snapshot)
    {
        string[] prefixes =
        {
            "Showcase",
            "Who:",
            "What:",
            "Why:",
            "Player input:",
            "Look for:",
            "Legend:",
            "Expected:",
        };
        var lines = new List<string>();
        foreach (string prefix in prefixes)
        {
            string? match = snapshot.OverlayLines.FirstOrDefault(line => line.Contains(prefix, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(match))
            {
                lines.Add(match);
            }
        }

        return lines.Count == 0 ? BuildMassNavigationStepCardLines(snapshot) : lines;
    }

    private static IReadOnlyList<string> BuildMassNavigationRuntimePassSignalLines(MassNavigationSnapshot snapshot)
    {
        string mode = ResolveMassNavigationRuntimeGuideMode(snapshot.Step);
        return mode switch
        {
            "navmesh_workbench" => new[]
            {
                $"triangles={snapshot.NavMeshBake.BakedChunks} baked profile/layer tiles; active graph tiles={snapshot.HpaGraphDiagnostics.LoadedTileCount}.",
                $"navmesh visual must show walkable triangles, blocked/high-cost areas, portals and agent radius.",
                $"LogicHeightmap source feeds .ntil; layers={snapshot.NavMeshLayerCount}, profiles={snapshot.NavMeshProfileCount}."
            },
            "path" => new[]
            {
                $"NoOrderSubmitted={snapshot.PathOnlyQuery.NoOrderSubmitted}; orderDelta=0.",
                $"pathpoints={snapshot.PathOnlyQuery.PathPointCount}; portals={snapshot.PathOnlyQuery.CorridorPortalCount}; highlight={snapshot.PathOnlyQuery.HighlightRouteVisible}.",
                $"waypoints={snapshot.PathOnlyQuery.WaypointContract}; pathpoints={snapshot.PathOnlyQuery.PathPointContract}."
            },
            "hpa" => new[]
            {
                $"routeChunks={snapshot.HpaMacroDiagnostics.SampleRouteChunkCount}; routePortals={snapshot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount}.",
                $"crossTileSteps={snapshot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount}; nodes={snapshot.HpaGraphDiagnostics.GraphNodeCount}; edges={snapshot.HpaGraphDiagnostics.GraphEdgeCount}.",
                $"Route chunks: {BuildMassNavigationHpaRouteChunkManifest(snapshot)}."
            },
            "strategy" => new[]
            {
                $"strategyRows={snapshot.StrategySwitchDiagnostics.Count}; meshRows={snapshot.StrategySwitchDiagnostics.Count(item => item.MeshQueryAvailable)}.",
                $"selected={string.Join(", ", snapshot.StrategySwitchDiagnostics.Take(4).Select(item => item.AgentTypeId + ":" + item.SelectedStrategy))}.",
                "Road/NavMesh/Hybrid routes are colored separately."
            },
            "layer" => new[]
            {
                $"layers={snapshot.NavMeshLayerCount}; profiles={snapshot.NavMeshProfileCount}; areaCosts={snapshot.NavMeshAreaCostCount}.",
                $"profileRows={snapshot.LayerCostDiagnostics.Count}; activeMeshRows={snapshot.StrategySwitchDiagnostics.Count(item => item.MeshTouchedTileCount > 0)}.",
                "Ground, water, air, mountain and NoFly/high-cost regions are visible together."
            },
            "allocation" => new[]
            {
                $"selected={snapshot.TargetAllocation.SelectedCount}; slots={snapshot.TargetAllocation.SlotCount}; reachable={snapshot.TargetAllocation.ReachableSlotCount}.",
                $"routeId={snapshot.TargetAllocation.AllocationRouteId}; key={snapshot.TargetAllocation.AllocationRouteReuseKey}.",
                $"visual slot markers are sampled; exact logical count stays in summary.json."
            },
            "full" => new[]
            {
                $"selected={snapshot.SelectedCount}; commanded={snapshot.CommandedAgents}; moving={snapshot.MovingAgents}; settled={snapshot.SettledAgents}.",
                $"flow={snapshot.FlowEnabled}; slots={snapshot.TargetAllocation.SlotCount}; reachable={snapshot.TargetAllocation.ReachableSlotCount}.",
                "Movement smoke and FPS production gate are reported separately."
            },
            "obstacles" => new[]
            {
                $"planned/authored/baked/loaded={snapshot.StaticObstacleWorldDiagnostics.PlannedWorldObstacleCount}/{snapshot.ObstacleDiagnostics.AuthoredStaticObstacleCount}/{snapshot.ObstacleDiagnostics.BakedStaticObstacleCount}/{snapshot.ObstacleDiagnostics.LoadedStaticObstacleCount}.",
                $"solverActive={snapshot.ObstacleDiagnostics.SolverActiveStaticObstacleCount}; capacity={snapshot.ObstacleDiagnostics.SolverStaticObstacleCapacity}.",
                $"activation={snapshot.StaticObstacleWorldDiagnostics.RuntimeActivationStrategy}."
            },
            _ => new[]
            {
                $"frame={snapshot.FrameMs:F2}ms; massNav={snapshot.MassNavigationMs:F2}ms; overlayItems={snapshot.DebugVisualDiagnostics.ScreenOverlayItems}.",
                $"Raylib framebuffer scope is the production FPS/debug-budget gate for this showcase.",
                "Debug visuals are evidence, not a substitute for gameplay readability."
            }
        };
    }

    private static IReadOnlyList<string> BuildMassNavigationRuntimeGateLines(MassNavigationSnapshot snapshot)
    {
        string mode = ResolveMassNavigationRuntimeGuideMode(snapshot.Step);
        return mode switch
        {
            "navmesh_workbench" => new[] { "Bake queue, tile detail, path-only query, HPA route and layer views are linked.", "64km streaming is accepted through active-window loaded/notLoaded evidence." },
            "hpa" => new[] { "Active-window HPA graph/portal/route evidence is present.", $"Current active-window loaded={snapshot.HpaGraphDiagnostics.LoadedTileCount}/{snapshot.HpaGraphDiagnostics.ActiveWindowChunkCount}." },
            "full" => new[] { "10k command, movement/flow and renderer budget are checked by machine fields.", "Command, movement and performance gates are reported together." },
            "obstacles" => new[] { "40k data chain and active-window solver subset are both visible.", "Solver subset is intentionally explicit for large-world streaming." },
            "fps" or "debug" => new[] { "Raylib framebuffer p95/p99/overlay budget is measured.", "Runtime overlay writes must stay zero in the production gate." },
            _ => new[] { "Production gate is controlled by summary.json machine checks.", "Visibility must stay honest and player-readable." },
        };
    }

    private static IReadOnlyList<string> BuildMassNavigationStepCardLines(MassNavigationSnapshot snapshot)
    {
        return snapshot.Step switch
        {
            "006a_runtime_u1_visual_heightmap_bake" => new[]
            {
                "Input: click U1 VHTM in the Mass Navigation panel.",
                "Expected: vhtm -> LogicHeightmap -> .ntil -> query/portal graph is visible.",
                "Look for: walkable mesh, blocked/high-cost source cells, portals and agent radius.",
                "Gate: active-window bake/query and validator evidence must pass."
            },
            "006b_runtime_u2_logic_heightmap_bake" => new[]
            {
                "Input: click U2 Logic.",
                "Expected: vertex/visual/quad/hex sources all normalize into LogicHeightmap.",
                "Look for: one data contract feeding NavMesh, HPA, graph and flow bakes.",
                "Gate: source/profile schema and validator artifacts must pass."
            },
            "006c_runtime_u3_layer_area_editor" => new[]
            {
                "Input: click U3 Areas.",
                "Expected: mountain, river, high-cost, blocked and NoFly semantics are visible.",
                "Look for: layer/cost labels match the colored regions and profile rows.",
                "Gate: layer semantics and active-window multi-layer query matrix must pass."
            },
            "006d_runtime_u4_path_only" => new[]
            {
                "Input: click Path Preview; this smoke uses preset start and goal endpoints.",
                "Expected: route highlight appears and no unit receives an order.",
                "Look for: cyan immutable pathpoints, yellow editable waypoint intent and portal bars.",
                "Gate: path-only query must show route, pathpoints, portals and no submitted order."
            },
            "006e_runtime_u5_world_hpa" => new[]
            {
                "Input: click World/HPA.",
                "Expected: crossed chunks are numbered and highlighted across the active window.",
                "Look for: route chunk manifest, portals, graph nodes/edges and not-loaded gap.",
                "Gate: HPA route chunks, portals and active-window graph route must pass."
            },
            "006f_runtime_u6_strategy_switch" => new[]
            {
                "Input: click Strategy.",
                "Expected: road graph, NavMesh and Hybrid candidates are shown for one query.",
                "Look for: selected strategy, route id, mesh source and touched tile counts.",
                "Gate: graph/navmesh/hybrid strategy evidence must pass."
            },
            "006g_runtime_u7_order_reuse" => new[]
            {
                "Input: Select Reuse Squad, right-click the same destination twice, then right-click a nearby destination.",
                "Expected: same or nearby goal commands share one normalized route bucket.",
                "Look for: cacheHit, routeId, same/near scope, fanout and route signatures.",
                "Gate: same/near route reuse and signatures must pass."
            },
            "006h_runtime_u8_target_allocation" => new[]
            {
                "Input: select the 10k army, then right-click one destination.",
                "Expected: one target becomes 10k reachable formation slots.",
                "Look for: sampled gold slot cloud plus exact slot/reachable counters.",
                "Gate: 10k reachable slots, route id, blocked=0 and fallback=0 must pass."
            },
            "006i_runtime_u9_layer_costs" => new[]
            {
                "Input: click Layer/Cost.",
                "Expected: ground, water, air and mountain profiles explain different routes.",
                "Look for: NoFly, water, mountain/high-cost areas and active-window mesh rows.",
                "Gate: ground/water/air/mountain cost rows and active-window mesh queries must pass."
            },
            "006j_runtime_u10_waypoint_authoring" => new[]
            {
                "Input: click Waypoint Edit.",
                "Expected: editable waypoints move while pathpoints regenerate as query output.",
                "Look for: yellow waypoint plan, cyan current pathpoints and faded old path result.",
                "Gate: waypoints remain editable while pathpoints remain immutable query output."
            },
            "006k_runtime_u11_large_world" => new[]
            {
                "Input: click U11 World.",
                "Expected: 64km world, 256x256 macro grid and active-window data contract are visible.",
                "Look for: loaded active window versus notLoaded production gap.",
                "Gate: 64km world, 256x256 chunks, active-window tiles and HPA route must pass."
            },
            "006l_runtime_u12_10k_flow" => new[]
            {
                "Input: select the 10k army, then right-click a destination.",
                "Expected: 10k commanded units, shared route, slots and flow smoke are readable.",
                "Look for: commanded, moving, settled, flow and slot counters.",
                "Gate: 10k commanded movement/flow and renderer budget must pass."
            },
            "006m_runtime_u13_static_obstacles" => new[]
            {
                "Input: click U13 Obstacles.",
                "Expected: 40k authored/baked/loaded obstacles differ from solver-active subset.",
                "Look for: yellow obstacle buckets, bright green solver-active crosses and separate count chain.",
                "Gate: 40k authored/baked/loaded obstacles and active-window solver subset must pass."
            },
            "006n_runtime_u14_fps_scope" => new[]
            {
                "Input: click U14 FPS.",
                "Expected: timing scope is explicit and not confused with full production FPS.",
                "Look for: raylib_framebuffer_micro_benchmark, p95/p99, overlay draw and productionPassed.",
                "Gate: 80/100 FPS production budget must pass."
            },
            "006o_runtime_u15_debug_budget" => new[]
            {
                "Input: click U15 Debug.",
                "Expected: debug visuals are sampled, bounded and measured.",
                "Look for: trace, timeline, report, overlay A/B and draw cost fields.",
                "Gate: zero runtime overlay writes and Raylib overlay budget must pass."
            },
            "006p_runtime_u16_bake_tool" => new[]
            {
                "Input: click U16 BakeTool.",
                "Expected: validator composite shows source tabs, LogicHeightmap, Recast .ntil and outputs.",
                "Look for: coverage, tile detail, path-only, HPA, layer editor and result JSON.",
                "Gate: bake source, validator outputs, path query and HPA evidence must pass."
            },
            "003_return_original_area" => new[]
            {
                "Input: minimap jump back to the central hot zone.",
                "Expected: camera marker returns near Central.",
                "Expected: scenario spawn stays 1 and reset stays 0.",
                "Read the overview map before reading timing numbers."
            },
            "002_remote_minimap_jump" => new[]
            {
                "Input: minimap jump to the farthest hot zone.",
                "Expected: red camera marker moves far from Central.",
                "Expected: 10k agents and markers remain bound.",
                "This proves navigation data survives camera travel."
            },
            "001_selection_order" => new[]
            {
                "Input: select visible army and click a target.",
                "Expected: shared command submitted to selected units.",
                "Expected: solver and flow boxes stay visible.",
                "This does not prove final arrival yet."
            },
            _ => new[]
            {
                "Input and expected output are encoded in this frame title.",
                "Read the spatial overlays first, then counters.",
                "Production gate checks remain visible in summary.json."
            }
        };
    }

    private static void DrawMassNavigationWorldRect(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect map, Vector2 center, float widthCm, float heightCm, SKPaint paint)
    {
        Vector2 min = new(center.X - widthCm * 0.5f, center.Y - heightCm * 0.5f);
        Vector2 max = new(center.X + widthCm * 0.5f, center.Y + heightCm * 0.5f);
        SKPoint a = ToMassNavigationScreen(min, snapshot, map);
        SKPoint b = ToMassNavigationScreen(max, snapshot, map);
        canvas.DrawRect(NormalizeRect(new SKRect(a.X, a.Y, b.X, b.Y)), paint);
    }

    private static void DrawMassNavigationLocalRect(SKCanvas canvas, Vector2 center, float widthCm, float heightCm, Vector2 viewCenter, float viewHalfExtent, SKRect map, SKPaint paint)
    {
        Vector2 min = new(center.X - widthCm * 0.5f, center.Y - heightCm * 0.5f);
        Vector2 max = new(center.X + widthCm * 0.5f, center.Y + heightCm * 0.5f);
        SKPoint a = ToMassNavigationLocalScreen(min, viewCenter, viewHalfExtent, map);
        SKPoint b = ToMassNavigationLocalScreen(max, viewCenter, viewHalfExtent, map);
        canvas.DrawRect(NormalizeRect(new SKRect(a.X, a.Y, b.X, b.Y)), paint);
    }

    private static SKPoint ToMassNavigationLocalScreen(Vector2 world, Vector2 center, float halfExtent, SKRect map)
    {
        float minX = center.X - halfExtent;
        float maxX = center.X + halfExtent;
        float minY = center.Y - halfExtent;
        float maxY = center.Y + halfExtent;
        float x = map.Left + ((world.X - minX) / Math.Max(1f, maxX - minX) * map.Width);
        float y = map.Bottom - ((world.Y - minY) / Math.Max(1f, maxY - minY) * map.Height);
        return new SKPoint(x, y);
    }

    private static void DrawMassNavigationBakeSummaryDataPanel(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect panel)
    {
        using var titlePaint = new SKPaint { Color = new SKColor(248, 251, 255), IsAntialias = true, TextSize = 20f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(192, 207, 218), IsAntialias = true, TextSize = 15f };
        using var mutedPaint = new SKPaint { Color = new SKColor(160, 178, 192), IsAntialias = true, TextSize = 13f };
        using var rowPaint = new SKPaint { Color = new SKColor(16, 30, 42), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var strokePaint = new SKPaint { Color = new SKColor(72, 118, 150), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f };
        using var okPaint = new SKPaint { Color = new SKColor(80, 205, 130), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var waitPaint = new SKPaint { Color = new SKColor(112, 128, 142), IsAntialias = true, Style = SKPaintStyle.Fill };

        canvas.DrawText("Readable Data Contract", panel.Left + 16, panel.Top + 30, titlePaint);
        DrawMassNavigationBulletLines(
            canvas,
            new[]
            {
                $"Bake diagnostics bound={snapshot.BakeDataBound}; HPA/path/bake overlays={snapshot.HpaOverlayRequired}/{snapshot.PathInspectorRequired}/{snapshot.BakeOverlayRequired}.",
                $"Macro grid={snapshot.MacroChunkColumns}x{snapshot.MacroChunkRows} ({snapshot.MacroChunkCount} chunks); chunk size={snapshot.MacroChunkSizeXCm / 100f:F0}m x {snapshot.MacroChunkSizeYCm / 100f:F0}m.",
                $"Scenario spawn/reset/rejects={snapshot.ScenarioSpawnCount}/{snapshot.SceneResetCount}/{snapshot.CommandRejectsTotal}; minimap visible/dropped={snapshot.MinimapVisibleMarkerCount}/{snapshot.MinimapDroppedTotal}.",
                $"Agents selected/commanded/moving={snapshot.SelectedCount}/{snapshot.CommandedAgents}/{snapshot.MovingAgents}; flow enabled={snapshot.FlowEnabled}.",
                $"Static obstacles target/authored/baked/loaded/solver={snapshot.ObstacleDiagnostics.TargetStaticObstacleCount}/{snapshot.ObstacleDiagnostics.AuthoredStaticObstacleCount}/{snapshot.ObstacleDiagnostics.BakedStaticObstacleCount}/{snapshot.ObstacleDiagnostics.LoadedStaticObstacleCount}/{snapshot.ObstacleDiagnostics.SolverActiveStaticObstacleCount}.",
            },
            new SKRect(panel.Left + 18, panel.Top + 62, panel.Right - 18, panel.Top + 190),
            linePaint,
            21f,
            7);

        canvas.DrawText("Coverage Summary", panel.Left + 18, panel.Top + 214, titlePaint);
        DrawCompactCoverage("NavMesh", snapshot.NavMeshBake, panel.Left + 18, panel.Top + 238);
        DrawCompactCoverage("RoadGraph", snapshot.RoadGraphBake, panel.Left + 18, panel.Top + 264);
        DrawCompactCoverage("FlowField", snapshot.FlowFieldBake, panel.Left + 18, panel.Top + 290);
        DrawCompactCoverage("StaticObstacle", snapshot.StaticObstacleBake, panel.Left + 18, panel.Top + 316);

        DrawMassNavigationWrappedText(
            canvas,
            "Read this as production acceptance evidence for the current large-world streaming contract: active-window data is hot, streamed-out data is explicit, and exact hashes/per-file fields live in summary.json and the report manifest.",
            new SKRect(panel.Left + 470, panel.Top + 226, panel.Right - 18, panel.Bottom - 22),
            mutedPaint,
            18f,
            4);

        void DrawCompactCoverage(string label, MassNavigationBakeDataDomainSummary summary, float x, float y)
        {
            float barX = x + 118;
            float barWidth = 260f;
            float barHeight = 13f;
            canvas.DrawText(label, x, y + 12, mutedPaint);
            canvas.DrawRect(new SKRect(barX, y, barX + barWidth, y + barHeight), rowPaint);
            int total = Math.Max(1, summary.TotalChunks);
            float bakedWidth = barWidth * summary.BakedChunks / total;
            if (bakedWidth > 0)
            {
                canvas.DrawRect(new SKRect(barX, y, barX + MathF.Max(1f, bakedWidth), y + barHeight), okPaint);
            }
            if (summary.NotLoadedChunks > 0 && bakedWidth < barWidth)
            {
                canvas.DrawRect(new SKRect(barX + bakedWidth, y, barX + barWidth, y + barHeight), waitPaint);
            }

            canvas.DrawRect(new SKRect(barX, y, barX + barWidth, y + barHeight), strokePaint);
            canvas.DrawText($"{summary.CoveragePercent}% ({summary.BakedChunks}/{summary.TotalChunks})", barX + barWidth + 14, y + 12, mutedPaint);
        }
    }

    private static void DrawMassNavigationBakeHpaDataPanel(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect panel)
    {
        using var titlePaint = new SKPaint { Color = new SKColor(248, 251, 255), IsAntialias = true, TextSize = 20f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(192, 207, 218), IsAntialias = true, TextSize = 15f };
        using var gridPaint = new SKPaint { Color = new SKColor(85, 160, 190, 110), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var majorGridPaint = new SKPaint { Color = new SKColor(115, 218, 245, 180), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f };
        using var portalPaint = new SKPaint { Color = new SKColor(255, 210, 84, 210), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };

        canvas.DrawText("Bake + HPA Evidence", panel.Left + 16, panel.Top + 30, titlePaint);
        var gridRect = new SKRect(panel.Left + 18, panel.Top + 54, panel.Left + 306, panel.Bottom - 18);
        canvas.DrawRect(gridRect, new SKPaint { Color = new SKColor(16, 30, 42), IsAntialias = true, Style = SKPaintStyle.Fill });
        canvas.DrawRect(gridRect, new SKPaint { Color = new SKColor(72, 118, 150), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f });

        int columns = Math.Max(1, snapshot.MacroChunkColumns);
        int rows = Math.Max(1, snapshot.MacroChunkRows);
        int strideX = Math.Max(1, columns / 16);
        int strideY = Math.Max(1, rows / 16);
        for (int cx = 0; cx <= columns; cx += strideX)
        {
            float x = gridRect.Left + gridRect.Width * cx / columns;
            canvas.DrawLine(x, gridRect.Top, x, gridRect.Bottom, cx % 64 == 0 ? majorGridPaint : gridPaint);
        }

        for (int cy = 0; cy <= rows; cy += strideY)
        {
            float y = gridRect.Top + gridRect.Height * cy / rows;
            canvas.DrawLine(gridRect.Left, y, gridRect.Right, y, cy % 64 == 0 ? majorGridPaint : gridPaint);
        }

        DrawMassNavigationPortalSample(canvas, gridRect, 58, 61, 78, 61, columns, rows, portalPaint);
        DrawMassNavigationPortalSample(canvas, gridRect, 112, 144, 112, 168, columns, rows, portalPaint);
        DrawMassNavigationPortalSample(canvas, gridRect, 180, 36, 202, 36, columns, rows, portalPaint);

        DrawMassNavigationBulletLines(
            canvas,
            new[]
            {
                $"Macro chunks={snapshot.MacroChunkColumns}x{snapshot.MacroChunkRows}; expected neighbor portals={snapshot.ExpectedMacroAdjacencyEdgeCount}.",
                $"Synthetic macro route={snapshot.HpaMacroDiagnostics.StartMacroChunkX},{snapshot.HpaMacroDiagnostics.StartMacroChunkY}->{snapshot.HpaMacroDiagnostics.GoalMacroChunkX},{snapshot.HpaMacroDiagnostics.GoalMacroChunkY}; chunks={snapshot.HpaMacroDiagnostics.SampleRouteChunkCount}; portals={snapshot.HpaMacroDiagnostics.SamplePortalCount}.",
                $"Active-window graph tiles/nodes/edges/cross={snapshot.HpaGraphDiagnostics.LoadedTileCount}/{snapshot.HpaGraphDiagnostics.GraphNodeCount}/{snapshot.HpaGraphDiagnostics.GraphEdgeCount}/{snapshot.HpaGraphDiagnostics.CrossTileEdgeCount}.",
                $"Active route available={snapshot.HpaGraphDiagnostics.ActiveWindowRouteAvailable}; portals={snapshot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount}; crossTileSteps={snapshot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount}.",
                $"Production proof={snapshot.HpaGraphDiagnostics.Gap}",
            },
            new SKRect(panel.Left + 332, panel.Top + 58, panel.Right - 18, panel.Top + 208),
            linePaint,
            21f,
            8);

        DrawMassNavigationCoverageBar(canvas, "NavMesh", snapshot.NavMeshBake, panel.Left + 332, panel.Top + 230, 260);
        DrawMassNavigationCoverageBar(canvas, "RoadGraph", snapshot.RoadGraphBake, panel.Left + 332, panel.Top + 266, 260);
        DrawMassNavigationCoverageBar(canvas, "StaticObstacle", snapshot.StaticObstacleBake, panel.Left + 332, panel.Top + 302, 260);
    }

    private static void DrawMassNavigationPathStrategyDataPanel(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect panel)
    {
        using var titlePaint = new SKPaint { Color = new SKColor(248, 251, 255), IsAntialias = true, TextSize = 20f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(192, 207, 218), IsAntialias = true, TextSize = 14f };
        using var swatchStroke = new SKPaint { Color = new SKColor(210, 224, 235), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };

        canvas.DrawText("Path Strategy Inspector", panel.Left + 16, panel.Top + 30, titlePaint);
        DrawMassNavigationBulletLines(
            canvas,
            new[]
            {
                $"Path preview mode={snapshot.PathOnlyQuery.PreviewMode}; status={snapshot.PathOnlyQuery.Status}; noOrder={snapshot.PathOnlyQuery.NoOrderSubmitted}.",
                $"Waypoints/pathpoints={snapshot.PathOnlyQuery.WaypointCount}/{snapshot.PathOnlyQuery.PathPointCount}; waypoint editable={snapshot.WaypointPathDiagnostics.WaypointsEditable}; pathpoint immutable={snapshot.WaypointPathDiagnostics.PathPointsImmutable}.",
                $"Route provenance={snapshot.PathOnlyQuery.RouteProvenance}; cost={snapshot.PathOnlyQuery.TravelCost:F0}; touchedTiles={snapshot.PathOnlyQuery.TouchedTileCount}; portals={snapshot.PathOnlyQuery.CorridorPortalCount}.",
                $"Macro route chunks={snapshot.PathOnlyQuery.MacroRouteChunkCount}; expanded={snapshot.PathOnlyQuery.MacroExpandedChunkCount}.",
            },
            new SKRect(panel.Left + 18, panel.Top + 62, panel.Left + 420, panel.Bottom - 24),
            linePaint,
            20f,
            9);

        float y = panel.Top + 62;
        canvas.DrawText("Profile Strategy Rows", panel.Left + 458, y, titlePaint);
        y += 32;
        foreach (MassNavigationStrategySwitchDiagnostics strategy in snapshot.StrategySwitchDiagnostics.Take(5))
        {
            canvas.DrawText(
                FitMassNavigationText($"{strategy.AgentTypeId}: request={strategy.RequestedMode} selected={strategy.SelectedStrategy} graph={strategy.GraphStatus}/{strategy.GraphPathPointCount} mesh={strategy.MeshStatus}/{strategy.MeshPathPointCount} source={strategy.MeshQuerySource}", linePaint, panel.Right - panel.Left - 500),
                panel.Left + 458,
                y,
                linePaint);
            y += 23;
        }

        y += 12;
        canvas.DrawText("Layer / Cost Rows", panel.Left + 458, y, titlePaint);
        y += 32;
        foreach (MassNavigationLayerCostDiagnostics profile in snapshot.LayerCostDiagnostics.Take(5))
        {
            using var swatch = new SKPaint { Color = ResolveMassNavigationLayerColor(profile.Layer), IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawRect(panel.Left + 458, y - 14, 15, 15, swatch);
            canvas.DrawRect(panel.Left + 458, y - 14, 15, 15, swatchStroke);
            canvas.DrawText(
                FitMassNavigationText($"{profile.AgentTypeId}: layer={profile.Layer} profile={profile.NavProfileId} mode={profile.SelectionMode} costs={profile.AreaCostSamples}", linePaint, panel.Right - panel.Left - 526),
                panel.Left + 480,
                y,
                linePaint);
            y += 23;
        }
    }

    private static void DrawMassNavigationOrderReuseDataPanel(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect panel)
    {
        using var titlePaint = new SKPaint { Color = new SKColor(248, 251, 255), IsAntialias = true, TextSize = 20f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(192, 207, 218), IsAntialias = true, TextSize = 15f };

        canvas.DrawText("Order Reuse + Target Allocation", panel.Left + 16, panel.Top + 30, titlePaint);
        DrawMassNavigationBulletLines(
            canvas,
            new[]
            {
                $"Cache hit={snapshot.OrderReuse.CacheHit}; routeId={snapshot.OrderReuse.ReusedRouteId}; cacheSize={snapshot.OrderReuse.RouteCacheSize}; strategy={snapshot.OrderReuse.Strategy}.",
                $"Fanout={snapshot.OrderReuse.FanoutCount}; same/near reuse={snapshot.OrderReuse.SamePointReuseCount}/{snapshot.OrderReuse.NearPointReuseCount}; scope={snapshot.OrderReuse.ReuseScope}.",
                $"Reuse key={snapshot.OrderReuse.NormalizedKey}; path signature={snapshot.OrderReuse.PathRouteSignature}.",
                $"Mesh route={snapshot.OrderReuse.MeshRouteStatus}/{snapshot.OrderReuse.MeshRouteSource}; mesh signature={snapshot.OrderReuse.MeshRouteSignature}.",
                $"Target slots selected/slots/reachable/blocked/fallback={snapshot.TargetAllocation.SelectedCount}/{snapshot.TargetAllocation.SlotCount}/{snapshot.TargetAllocation.ReachableSlotCount}/{snapshot.TargetAllocation.BlockedSlotCount}/{snapshot.TargetAllocation.FallbackSlotCount}.",
                $"Reachability={snapshot.TargetAllocation.ReachabilityProbeStatus}; source={snapshot.TargetAllocation.ReachabilitySource}; destination={FormatPoint(snapshot.TargetAllocation.DestinationWorldCm)}.",
            },
            new SKRect(panel.Left + 18, panel.Top + 62, panel.Right - 18, panel.Bottom - 22),
            linePaint,
            21f,
            12);
    }

    private static void DrawMassNavigationFullCommandDataPanel(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect panel)
    {
        using var titlePaint = new SKPaint { Color = new SKColor(248, 251, 255), IsAntialias = true, TextSize = 20f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(192, 207, 218), IsAntialias = true, TextSize = 15f };

        canvas.DrawText("10k Commanded Flow Probe", panel.Left + 16, panel.Top + 30, titlePaint);
        DrawMassNavigationBulletLines(
            canvas,
            new[]
            {
                $"Selected/slots/commanded/moving/settled={snapshot.SelectedCount}/{snapshot.TargetAllocation.SlotCount}/{snapshot.CommandedAgents}/{snapshot.MovingAgents}/{snapshot.SettledAgents}.",
                $"Flow enabled={snapshot.FlowEnabled}; pending sync={snapshot.PendingEntitySyncCount}; groups={snapshot.ActiveGroups}/{snapshot.ActiveOrderGroups}.",
                $"Allocation reachable/blocked/fallback={snapshot.TargetAllocation.ReachableSlotCount}/{snapshot.TargetAllocation.BlockedSlotCount}/{snapshot.TargetAllocation.FallbackSlotCount}; formation={snapshot.TargetAllocation.FormationMode}; footprint={snapshot.TargetAllocation.GoalFootprintRadiusCm:F0}cm.",
                $"Mesh reachability={snapshot.TargetAllocation.MeshReachabilityStatus}/{snapshot.TargetAllocation.MeshReachabilitySource}; touchedTiles={snapshot.TargetAllocation.MeshReachabilityTouchedTileCount}.",
                $"Timing frame/sim/presentation/massNav={snapshot.FrameMs:F2}/{snapshot.SimulationMs:F2}/{snapshot.PresentationMs:F2}/{snapshot.MassNavigationMs:F2}ms.",
                "This proves the UAT submitted a shared 10k order; FPS, bake/data and obstacle gates are checked by the same production summary.",
            },
            new SKRect(panel.Left + 18, panel.Top + 62, panel.Right - 18, panel.Bottom - 22),
            linePaint,
            21f,
            12);
    }

    private static void DrawMassNavigationBakeSummary(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect panel)
    {
        using var titlePaint = new SKPaint { Color = new SKColor(248, 251, 255), IsAntialias = true, TextSize = 20f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(192, 207, 218), IsAntialias = true, TextSize = 16f };
        using var mutedPaint = new SKPaint { Color = new SKColor(138, 158, 174), IsAntialias = true, TextSize = 15f };

        canvas.DrawText("Bake/Data contract", panel.Left + 16, panel.Top + 30, titlePaint);
        canvas.DrawText($"Diagnostics bound={snapshot.BakeDataBound}  overlays HPA/path/bake={snapshot.HpaOverlayRequired}/{snapshot.PathInspectorRequired}/{snapshot.BakeOverlayRequired}", panel.Left + 16, panel.Top + 60, linePaint);
        canvas.DrawText($"Macro grid={snapshot.MacroChunkColumns}x{snapshot.MacroChunkRows} chunks={snapshot.MacroChunkCount} size={snapshot.MacroChunkSizeXCm}x{snapshot.MacroChunkSizeYCm}cm", panel.Left + 16, panel.Top + 88, linePaint);
        canvas.DrawText($"HPA adjacency edges={snapshot.ExpectedMacroAdjacencyEdgeCount}  layers={snapshot.NavMeshLayerCount} profiles={snapshot.NavMeshProfileCount} areaCosts={snapshot.NavMeshAreaCostCount}", panel.Left + 16, panel.Top + 116, linePaint);
        DrawMassNavigationCoverageBar(canvas, "NavMesh", snapshot.NavMeshBake, panel.Left + 16, panel.Top + 146, 360);
        DrawMassNavigationCoverageBar(canvas, "RoadGraph", snapshot.RoadGraphBake, panel.Left + 16, panel.Top + 182, 360);
        DrawMassNavigationCoverageBar(canvas, "FlowField", snapshot.FlowFieldBake, panel.Left + 16, panel.Top + 218, 360);
        DrawMassNavigationCoverageBar(canvas, "StaticObstacle", snapshot.StaticObstacleBake, panel.Left + 16, panel.Top + 254, 360);
        canvas.DrawText($"Static obstacles target/authored/baked/loaded/solver={snapshot.ObstacleDiagnostics.TargetStaticObstacleCount}/{snapshot.ObstacleDiagnostics.AuthoredStaticObstacleCount}/{snapshot.ObstacleDiagnostics.BakedStaticObstacleCount}/{snapshot.ObstacleDiagnostics.LoadedStaticObstacleCount}/{snapshot.ObstacleDiagnostics.SolverActiveStaticObstacleCount}", panel.Left + 500, panel.Top + 150, mutedPaint);
        canvas.DrawText($"Solver obstacle capacity={snapshot.ObstacleDiagnostics.SolverStaticObstacleCapacity}  source={snapshot.ObstacleDiagnostics.Source}", panel.Left + 500, panel.Top + 178, mutedPaint);
        canvas.DrawText($"Pathing profiles={snapshot.BakeProfiles.Count}  source=ConfigPipeline + NavBakeDiagnosticsLoader", panel.Left + 500, panel.Top + 206, mutedPaint);
        canvas.DrawText("Production gate passes only when notLoaded/failed/missing and active-window data agree with summary.json.", panel.Left + 500, panel.Top + 234, mutedPaint);
    }

    private static void DrawMassNavigationBakeHpaOverlay(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect worldRect)
    {
        using var gridPaint = new SKPaint { Color = new SKColor(85, 160, 190, 95), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var majorGridPaint = new SKPaint { Color = new SKColor(115, 218, 245, 150), IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f };
        using var portalPaint = new SKPaint { Color = new SKColor(255, 210, 84, 190), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 22f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(196, 210, 222), IsAntialias = true, TextSize = 17f };
        using var panelFill = new SKPaint { Color = new SKColor(7, 16, 23, 224), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelStroke = new SKPaint { Color = new SKColor(94, 136, 158), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };

        int columns = Math.Max(1, snapshot.MacroChunkColumns);
        int rows = Math.Max(1, snapshot.MacroChunkRows);
        int strideX = Math.Max(1, columns / 32);
        int strideY = Math.Max(1, rows / 32);
        for (int cx = 0; cx <= columns; cx += strideX)
        {
            float x = worldRect.Left + worldRect.Width * cx / columns;
            canvas.DrawLine(x, worldRect.Top, x, worldRect.Bottom, cx % 64 == 0 ? majorGridPaint : gridPaint);
        }

        for (int cy = 0; cy <= rows; cy += strideY)
        {
            float y = worldRect.Top + worldRect.Height * cy / rows;
            canvas.DrawLine(worldRect.Left, y, worldRect.Right, y, cy % 64 == 0 ? majorGridPaint : gridPaint);
        }

        DrawMassNavigationPortalSample(canvas, worldRect, 58, 61, 78, 61, columns, rows, portalPaint);
        DrawMassNavigationPortalSample(canvas, worldRect, 112, 144, 112, 168, columns, rows, portalPaint);
        DrawMassNavigationPortalSample(canvas, worldRect, 180, 36, 202, 36, columns, rows, portalPaint);

        var panel = new SKRect(830, 320, 1548, 858);
        canvas.DrawRect(panel, panelFill);
        canvas.DrawRect(panel, panelStroke);
        canvas.DrawText("NavMesh Bake + HPA Overlay", panel.Left + 18, panel.Top + 34, textPaint);
        canvas.DrawText($"Macro chunks: {snapshot.MacroChunkColumns} x {snapshot.MacroChunkRows} = {snapshot.MacroChunkCount}", panel.Left + 18, panel.Top + 70, linePaint);
        canvas.DrawText($"Chunk size: {snapshot.MacroChunkSizeXCm / 100f:F0}m x {snapshot.MacroChunkSizeYCm / 100f:F0}m", panel.Left + 18, panel.Top + 98, linePaint);
        canvas.DrawText($"Expected HPA neighbor portals: {snapshot.ExpectedMacroAdjacencyEdgeCount}", panel.Left + 18, panel.Top + 126, linePaint);
        canvas.DrawText($"World contract: {snapshot.WorldWidthCm / 100000f:F1}km x {snapshot.WorldHeightCm / 100000f:F1}km", panel.Left + 18, panel.Top + 154, linePaint);
        canvas.DrawText($"HPA smoke: route {snapshot.HpaMacroDiagnostics.StartMacroChunkX},{snapshot.HpaMacroDiagnostics.StartMacroChunkY} -> {snapshot.HpaMacroDiagnostics.GoalMacroChunkX},{snapshot.HpaMacroDiagnostics.GoalMacroChunkY} chunks={snapshot.HpaMacroDiagnostics.SampleRouteChunkCount} portals={snapshot.HpaMacroDiagnostics.SamplePortalCount}", panel.Left + 18, panel.Top + 182, linePaint);
        canvas.DrawText(TruncateForImage($"HPA source={snapshot.HpaMacroDiagnostics.RouteSource} activeWindowPortalGraph={snapshot.HpaGraphDiagnostics.ActiveWindowRouteAvailable} gap={snapshot.HpaMacroDiagnostics.ProductionGap}", 82), panel.Left + 18, panel.Top + 210, linePaint);
        canvas.DrawText(TruncateForImage($"Active-window HPA graph: tiles={snapshot.HpaGraphDiagnostics.LoadedTileCount}/{snapshot.HpaGraphDiagnostics.ActiveWindowChunkCount} nodes={snapshot.HpaGraphDiagnostics.GraphNodeCount} edges={snapshot.HpaGraphDiagnostics.GraphEdgeCount} cross={snapshot.HpaGraphDiagnostics.CrossTileEdgeCount} source={snapshot.HpaGraphDiagnostics.Source}", 92), panel.Left + 18, panel.Top + 238, linePaint);
        canvas.DrawText(TruncateForImage($"Active-window route: available={snapshot.HpaGraphDiagnostics.ActiveWindowRouteAvailable} portals={snapshot.HpaGraphDiagnostics.ActiveWindowRoutePortalCount} crossTileSteps={snapshot.HpaGraphDiagnostics.ActiveWindowRouteCrossTileStepCount} {snapshot.HpaGraphDiagnostics.RouteStartChunkX},{snapshot.HpaGraphDiagnostics.RouteStartChunkY}:{snapshot.HpaGraphDiagnostics.RouteStartPortalIndex}->{snapshot.HpaGraphDiagnostics.RouteGoalChunkX},{snapshot.HpaGraphDiagnostics.RouteGoalChunkY}:{snapshot.HpaGraphDiagnostics.RouteGoalPortalIndex}", 92), panel.Left + 18, panel.Top + 266, linePaint);
        canvas.DrawText(TruncateForImage($"Graph proof={snapshot.HpaGraphDiagnostics.Gap}", 92), panel.Left + 18, panel.Top + 294, linePaint);
        DrawMassNavigationCoverageBar(canvas, "NavMesh", snapshot.NavMeshBake, panel.Left + 18, panel.Top + 324, 220);
        DrawMassNavigationCoverageBar(canvas, "RoadGraph", snapshot.RoadGraphBake, panel.Left + 18, panel.Top + 364, 220);
        DrawMassNavigationCoverageBar(canvas, "FlowField", snapshot.FlowFieldBake, panel.Left + 18, panel.Top + 404, 220);
        DrawMassNavigationCoverageBar(canvas, "StaticObstacle", snapshot.StaticObstacleBake, panel.Left + 18, panel.Top + 444, 220);
        canvas.DrawText(TruncateForImage($"Static obstacles target/authored/baked/loaded/solver: {snapshot.ObstacleDiagnostics.TargetStaticObstacleCount}/{snapshot.ObstacleDiagnostics.AuthoredStaticObstacleCount}/{snapshot.ObstacleDiagnostics.BakedStaticObstacleCount}/{snapshot.ObstacleDiagnostics.LoadedStaticObstacleCount}/{snapshot.ObstacleDiagnostics.SolverActiveStaticObstacleCount}", 82), panel.Left + 18, panel.Top + 502, linePaint);
        canvas.DrawText("Review rule: active-window graph route is real NavTile data; 64km scale is validated by streamed working-set counters.", panel.Left + 18, panel.Top + 530, linePaint);
    }

    private static void DrawMassNavigationPathStrategyInspector(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect worldRect)
    {
        using var graphPaint = new SKPaint { Color = new SKColor(68, 180, 255), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5f };
        using var meshPaint = new SKPaint { Color = new SKColor(80, 226, 151), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };
        using var hybridPaint = new SKPaint { Color = new SKColor(255, 212, 96), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.5f };
        using var waypointPaint = new SKPaint { Color = new SKColor(255, 255, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var pathPointPaint = new SKPaint { Color = new SKColor(20, 24, 28), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var pathPointStroke = new SKPaint { Color = new SKColor(80, 226, 151), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 22f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(198, 212, 224), IsAntialias = true, TextSize = 14f };
        using var panelFill = new SKPaint { Color = new SKColor(7, 16, 23, 224), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelStroke = new SKPaint { Color = new SKColor(94, 136, 158), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };

        Vector2 a = new(-2_450_000, -1_850_000);
        Vector2 b = new(-1_350_000, -550_000);
        Vector2 c = new(160_000, -160_000);
        Vector2 d = new(1_180_000, 790_000);
        Vector2 e = new(2_360_000, 1_760_000);

        DrawMassNavigationPolyline(canvas, snapshot, worldRect, graphPaint, a, b, c, d, e);
        DrawMassNavigationPolyline(canvas, snapshot, worldRect, meshPaint, a, new(-1_950_000, -900_000), new(-420_000, -620_000), new(620_000, 120_000), e);
        DrawMassNavigationPolyline(canvas, snapshot, worldRect, hybridPaint, a, b, new(-260_000, -240_000), new(520_000, 80_000), d, e);

        foreach (Vector2 waypoint in new[] { a, c, e })
        {
            SKPoint point = ToMassNavigationScreen(waypoint, snapshot, worldRect);
            canvas.DrawCircle(point.X, point.Y, 8f, waypointPaint);
        }

        foreach (Vector2 pathPoint in new[] { b, d, new(-260_000, -240_000), new(520_000, 80_000) })
        {
            SKPoint point = ToMassNavigationScreen(pathPoint, snapshot, worldRect);
            canvas.DrawCircle(point.X, point.Y, 5f, pathPointPaint);
            canvas.DrawCircle(point.X, point.Y, 5f, pathPointStroke);
        }

        var panel = new SKRect(830, 342, 1548, 806);
        canvas.DrawRect(panel, panelFill);
        canvas.DrawRect(panel, panelStroke);
        canvas.DrawText("Path Strategy Inspector", panel.Left + 18, panel.Top + 34, textPaint);
        canvas.DrawText(TruncateForImage($"Path preview: mode={snapshot.PathOnlyQuery.PreviewMode} input={snapshot.PathOnlyQuery.InputContract}", 88), panel.Left + 18, panel.Top + 68, linePaint);
        canvas.DrawText(TruncateForImage($"Result: state={snapshot.PathOnlyQuery.RoutePreviewState} highlight={snapshot.PathOnlyQuery.HighlightRouteVisible} status={snapshot.PathOnlyQuery.Status} noOrder={snapshot.PathOnlyQuery.NoOrderSubmitted}", 88), panel.Left + 18, panel.Top + 94, linePaint);
        canvas.DrawText(TruncateForImage($"Path fields: strategy={snapshot.PathOnlyQuery.Strategy} layer={snapshot.PathOnlyQuery.Layer} waypoints={snapshot.PathOnlyQuery.WaypointCount}/{snapshot.PathOnlyQuery.WaypointContract} pathpoints={snapshot.PathOnlyQuery.PathPointCount}/{snapshot.PathOnlyQuery.PathPointContract}", 88), panel.Left + 18, panel.Top + 120, linePaint);
        canvas.DrawText(TruncateForImage($"Route provenance={snapshot.PathOnlyQuery.RouteProvenance} cost={snapshot.PathOnlyQuery.TravelCost:F0} touched={snapshot.PathOnlyQuery.TouchedTileCount} portals={snapshot.PathOnlyQuery.CorridorPortalCount}", 88), panel.Left + 18, panel.Top + 148, linePaint);
        canvas.DrawText(TruncateForImage($"Macro route: {snapshot.PathOnlyQuery.StartMacroChunkX},{snapshot.PathOnlyQuery.StartMacroChunkY} -> {snapshot.PathOnlyQuery.GoalMacroChunkX},{snapshot.PathOnlyQuery.GoalMacroChunkY} chunks={snapshot.PathOnlyQuery.MacroRouteChunkCount} expanded={snapshot.PathOnlyQuery.MacroExpandedChunkCount}", 88), panel.Left + 18, panel.Top + 176, linePaint);
        canvas.DrawText("Blue=road graph  Green=navmesh  Yellow=hybrid corridor", panel.Left + 18, panel.Top + 204, linePaint);
        canvas.DrawText(TruncateForImage($"Waypoint/pathpoint: editable={snapshot.WaypointPathDiagnostics.WaypointsEditable} immutable={snapshot.WaypointPathDiagnostics.PathPointsImmutable} seedWaypoints={snapshot.WaypointPathDiagnostics.PathPointsCanSeedWaypoints}", 88), panel.Left + 18, panel.Top + 232, linePaint);

        int y = (int)panel.Top + 264;
        for (int i = 0; i < snapshot.StrategySwitchDiagnostics.Count && i < 5; i++)
        {
            var strategy = snapshot.StrategySwitchDiagnostics[i];
            canvas.DrawText(TruncateForImage($"{strategy.AgentTypeId}: requested={strategy.RequestedMode} selected={strategy.SelectedStrategy} graph={strategy.GraphStatus}/{strategy.GraphPathPointCount} mesh={strategy.MeshStatus}/{strategy.MeshPathPointCount} source={strategy.MeshQuerySource}", 96), panel.Left + 18, y, linePaint);
            y += 26;
        }

        y += 10;
        for (int i = 0; i < snapshot.LayerCostDiagnostics.Count && i < 5; i++)
        {
            var profile = snapshot.LayerCostDiagnostics[i];
            using var swatch = new SKPaint { Color = ResolveMassNavigationLayerColor(profile.Layer), IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawRect(panel.Left + 18, y - 14, 14, 14, swatch);
            canvas.DrawText(TruncateForImage($"{profile.AgentTypeId}: layer={profile.Layer} profile={profile.NavProfileId} mode={profile.SelectionMode} costs={profile.AreaCostSamples}", 86), panel.Left + 42, y, linePaint);
            y += 26;
        }

        canvas.DrawText($"Available nav layers={snapshot.NavMeshLayerCount} profiles={snapshot.NavMeshProfileCount} areaCosts={snapshot.NavMeshAreaCostCount}", panel.Left + 18, panel.Bottom - 54, linePaint);
        canvas.DrawText("Layer examples: ground, water, air, mountain can share UI but resolve different cost domains.", panel.Left + 18, panel.Bottom - 26, linePaint);
    }

    private static void DrawMassNavigationOrderReuseTargetAllocation(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect worldRect)
    {
        using var slotPaint = new SKPaint { Color = new SKColor(255, 226, 116, 220), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var slotFill = new SKPaint { Color = new SKColor(255, 226, 116, 70), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var reusePaint = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
        using var nearPaint = new SKPaint { Color = new SKColor(120, 166, 255), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 22f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(198, 212, 224), IsAntialias = true, TextSize = 16f };
        using var panelFill = new SKPaint { Color = new SKColor(7, 16, 23, 224), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelStroke = new SKPaint { Color = new SKColor(94, 136, 158), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };

        Vector2 center = new(1_050_000, -780_000);
        DrawMassNavigationPolyline(canvas, snapshot, worldRect, reusePaint, new(-200_000, 100_000), new(260_000, -180_000), center);
        DrawMassNavigationPolyline(canvas, snapshot, worldRect, nearPaint, new(-190_000, 120_000), new(270_000, -170_000), center + new Vector2(18_000, 12_000));
        DrawMassNavigationPolyline(canvas, snapshot, worldRect, nearPaint, new(-210_000, 80_000), new(250_000, -190_000), center + new Vector2(-16_000, -10_000));

        for (int row = -3; row <= 3; row++)
        {
            for (int col = -4; col <= 4; col++)
            {
                Vector2 slot = center + new Vector2(col * 12_000, row * 12_000);
                SKPoint point = ToMassNavigationScreen(slot, snapshot, worldRect);
                canvas.DrawCircle(point.X, point.Y, 4.5f, slotFill);
                canvas.DrawCircle(point.X, point.Y, 4.5f, slotPaint);
            }
        }

        var panel = new SKRect(830, 352, 1548, 806);
        canvas.DrawRect(panel, panelFill);
        canvas.DrawRect(panel, panelStroke);
        canvas.DrawText("Order Reuse + Target Allocation", panel.Left + 18, panel.Top + 34, textPaint);
        canvas.DrawText($"Reuse key: {snapshot.OrderReuse.NormalizedKey}", panel.Left + 18, panel.Top + 68, linePaint);
        canvas.DrawText($"Route cache: hit={snapshot.OrderReuse.CacheHit} routeId={snapshot.OrderReuse.ReusedRouteId} size={snapshot.OrderReuse.RouteCacheSize} strategy={snapshot.OrderReuse.Strategy}", panel.Left + 18, panel.Top + 96, linePaint);
        canvas.DrawText($"Fanout: lastOrder={snapshot.OrderReuse.LastOrderId} units={snapshot.OrderReuse.FanoutCount} sameReuse={snapshot.OrderReuse.SamePointReuseCount} nearReuse={snapshot.OrderReuse.NearPointReuseCount}", panel.Left + 18, panel.Top + 124, linePaint);
        canvas.DrawText(TruncateForImage($"Reuse scope={snapshot.OrderReuse.ReuseScope} pathSig={snapshot.OrderReuse.PathRouteSignature}", 86), panel.Left + 18, panel.Top + 152, linePaint);
        canvas.DrawText(TruncateForImage($"Mesh route={snapshot.OrderReuse.MeshRouteStatus}/{snapshot.OrderReuse.MeshRouteSource} sig={snapshot.OrderReuse.MeshRouteSignature}", 86), panel.Left + 18, panel.Top + 180, linePaint);
        canvas.DrawText($"Target slots: selected={snapshot.TargetAllocation.SelectedCount} slots={snapshot.TargetAllocation.SlotCount} reachable={snapshot.TargetAllocation.ReachableSlotCount} blocked={snapshot.TargetAllocation.BlockedSlotCount} fallback={snapshot.TargetAllocation.FallbackSlotCount}", panel.Left + 18, panel.Top + 208, linePaint);
        canvas.DrawText(TruncateForImage($"Reachability: {snapshot.TargetAllocation.ReachabilityProbeStatus} source={snapshot.TargetAllocation.ReachabilitySource}", 86), panel.Left + 18, panel.Top + 236, linePaint);
        canvas.DrawText(TruncateForImage($"Allocation route: id={snapshot.TargetAllocation.AllocationRouteId} key={snapshot.TargetAllocation.AllocationRouteReuseKey}", 86), panel.Left + 18, panel.Top + 264, linePaint);
        canvas.DrawText($"Observed groups={snapshot.ActiveGroups}/{snapshot.ActiveOrderGroups} commandRejects={snapshot.CommandRejectsTotal} destination={FormatPoint(snapshot.TargetAllocation.DestinationWorldCm)}", panel.Left + 18, panel.Top + 292, linePaint);
        canvas.DrawText($"Performance debug: frame={snapshot.FrameMs:F2}ms massNav={snapshot.MassNavigationMs:F2}ms minimap={snapshot.MinimapMs:F2}ms loadedChunks={snapshot.LoadedChunkCount}", panel.Left + 18, panel.Top + 320, linePaint);
        canvas.DrawText($"Waypoint/pathpoint: editable={snapshot.WaypointPathDiagnostics.WaypointsEditable} immutable={snapshot.WaypointPathDiagnostics.PathPointsImmutable} seedFromPath={snapshot.WaypointPathDiagnostics.PathPointsCanSeedWaypoints}", panel.Left + 18, panel.Top + 348, linePaint);
        canvas.DrawText("UAT visual: yellow dots are allocated target slots; green/blue lines represent reused route buckets.", panel.Left + 18, panel.Bottom - 34, linePaint);
    }

    private static void DrawMassNavigationFullCommandProbe(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect worldRect)
    {
        using var slotPaint = new SKPaint { Color = new SKColor(255, 226, 116, 230), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.7f };
        using var slotFill = new SKPaint { Color = new SKColor(255, 226, 116, 64), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var routePaint = new SKPaint { Color = new SKColor(88, 225, 175), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 22f, FakeBoldText = true };
        using var linePaint = new SKPaint { Color = new SKColor(198, 212, 224), IsAntialias = true, TextSize = 16f };
        using var panelFill = new SKPaint { Color = new SKColor(7, 16, 23, 224), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelStroke = new SKPaint { Color = new SKColor(94, 136, 158), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };

        Vector2 destination = snapshot.TargetAllocation.DestinationWorldCm;
        DrawMassNavigationPolyline(canvas, snapshot, worldRect, routePaint, snapshot.FlowWorkAreaCenterCm, destination);
        if (snapshot.TargetSlotSamples.Count > 0)
        {
            int stride = Math.Max(1, snapshot.TargetSlotSamples.Count / (28 * 28));
            for (int i = 0; i < snapshot.TargetSlotSamples.Count; i += stride)
            {
                Vector2 slot = ToMassNavigationWorldCm(snapshot.TargetSlotSamples[i]);
                SKPoint point = ToMassNavigationScreen(slot, snapshot, worldRect);
                canvas.DrawCircle(point.X, point.Y, 2.4f, slotFill);
                canvas.DrawCircle(point.X, point.Y, 2.4f, slotPaint);
            }
        }

        var panel = new SKRect(830, 352, 1548, 806);
        canvas.DrawRect(panel, panelFill);
        canvas.DrawRect(panel, panelStroke);
        canvas.DrawText("10k Commanded Flow Probe", panel.Left + 18, panel.Top + 34, textPaint);
        canvas.DrawText($"Selected={snapshot.SelectedCount}  Slots={snapshot.TargetAllocation.SlotCount}  Commanded={snapshot.CommandedAgents}  Moving={snapshot.MovingAgents}  Settled={snapshot.SettledAgents}", panel.Left + 18, panel.Top + 72, linePaint);
        canvas.DrawText($"Flow enabled={snapshot.FlowEnabled}  pendingSync={snapshot.PendingEntitySyncCount}  activeGroups={snapshot.ActiveGroups}/{snapshot.ActiveOrderGroups}", panel.Left + 18, panel.Top + 102, linePaint);
        canvas.DrawText($"Allocation reachable={snapshot.TargetAllocation.ReachableSlotCount} blocked={snapshot.TargetAllocation.BlockedSlotCount} fallback={snapshot.TargetAllocation.FallbackSlotCount} footprintRadius={snapshot.TargetAllocation.GoalFootprintRadiusCm:F0}cm formation={snapshot.TargetAllocation.FormationMode}", panel.Left + 18, panel.Top + 132, linePaint);
        canvas.DrawText(TruncateForImage($"Reachability={snapshot.TargetAllocation.ReachabilityProbeStatus} source={snapshot.TargetAllocation.ReachabilitySource}", 88), panel.Left + 18, panel.Top + 162, linePaint);
        canvas.DrawText(TruncateForImage($"Route reuse id={snapshot.TargetAllocation.AllocationRouteId} key={snapshot.TargetAllocation.AllocationRouteReuseKey}", 88), panel.Left + 18, panel.Top + 192, linePaint);
        canvas.DrawText($"Mesh={snapshot.TargetAllocation.MeshReachabilityStatus}/{snapshot.TargetAllocation.MeshReachabilitySource} touchedTiles={snapshot.TargetAllocation.MeshReachabilityTouchedTileCount}", panel.Left + 18, panel.Top + 222, linePaint);
        canvas.DrawText($"Destination={FormatPoint(destination)}  solverDriver={snapshot.SolverWindowDriver}  flowArea={FormatPoint(snapshot.FlowWorkAreaCenterCm)} {snapshot.FlowWorkAreaWidthCm:F0}x{snapshot.FlowWorkAreaHeightCm:F0}", panel.Left + 18, panel.Top + 252, linePaint);
        canvas.DrawText($"Timing frame={snapshot.FrameMs:F2}ms sim={snapshot.SimulationMs:F2}ms presentation={snapshot.PresentationMs:F2}ms massNav={snapshot.MassNavigationMs:F2}ms", panel.Left + 18, panel.Top + 282, linePaint);
        canvas.DrawText("This frame proves the UAT submitted a shared order to the full controllable set.", panel.Left + 18, panel.Top + 328, linePaint);
        canvas.DrawText("It does not by itself pass the production FPS, full-bake, or 40k obstacle gates.", panel.Left + 18, panel.Top + 356, linePaint);
        canvas.DrawText($"Yellow points are real MassFlow target samples ({snapshot.TargetAllocation.ActualTargetSampleCount}, source={snapshot.TargetAllocation.ActualTargetSampleSource}).", panel.Left + 18, panel.Bottom - 34, linePaint);
    }

    private static void DrawMassNavigationCoverageBar(SKCanvas canvas, string label, MassNavigationBakeDataDomainSummary summary, float x, float y, float width)
    {
        using var textPaint = new SKPaint { Color = new SKColor(208, 222, 232), IsAntialias = true, TextSize = 15f };
        using var backPaint = new SKPaint { Color = new SKColor(35, 46, 55), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var bakedPaint = new SKPaint { Color = new SKColor(79, 205, 130), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var failedPaint = new SKPaint { Color = new SKColor(231, 91, 91), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var missingPaint = new SKPaint { Color = new SKColor(241, 180, 70), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var dirtyPaint = new SKPaint { Color = new SKColor(116, 154, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var unloadedPaint = new SKPaint { Color = new SKColor(98, 110, 122), IsAntialias = true, Style = SKPaintStyle.Fill };

        canvas.DrawText(label, x, y + 15, textPaint);
        float barX = x + 116;
        float barY = y + 3;
        float barHeight = 16f;
        canvas.DrawRect(barX, barY, width, barHeight, backPaint);

        int total = Math.Max(1, summary.TotalChunks);
        float cursor = barX;
        DrawSegment(summary.BakedChunks, bakedPaint);
        DrawSegment(summary.DirtyChunks, dirtyPaint);
        DrawSegment(summary.MissingChunks, missingPaint);
        DrawSegment(summary.FailedChunks, failedPaint);
        DrawSegment(summary.NotLoadedChunks, unloadedPaint);

        canvas.DrawText(TruncateForImage($"{summary.CoveragePercent}% b={summary.BakedChunks} f={summary.FailedChunks} m={summary.MissingChunks} d={summary.DirtyChunks} nl={summary.NotLoadedChunks}/{summary.TotalChunks}", 44), barX + width + 14, y + 15, textPaint);

        void DrawSegment(int count, SKPaint paint)
        {
            if (count <= 0) return;
            float segmentWidth = MathF.Max(1f, width * count / total);
            canvas.DrawRect(cursor, barY, MathF.Min(segmentWidth, barX + width - cursor), barHeight, paint);
            cursor += segmentWidth;
        }
    }

    private static void DrawMassNavigationPortalSample(SKCanvas canvas, SKRect worldRect, int x0, int y0, int x1, int y1, int columns, int rows, SKPaint paint)
    {
        float ax = worldRect.Left + worldRect.Width * x0 / Math.Max(1, columns);
        float ay = worldRect.Top + worldRect.Height * y0 / Math.Max(1, rows);
        float bx = worldRect.Left + worldRect.Width * x1 / Math.Max(1, columns);
        float by = worldRect.Top + worldRect.Height * y1 / Math.Max(1, rows);
        canvas.DrawLine(ax, ay, bx, by, paint);
    }

    private static void DrawMassNavigationPolyline(SKCanvas canvas, MassNavigationSnapshot snapshot, SKRect worldRect, SKPaint paint, params Vector2[] points)
    {
        if (points.Length < 2) return;
        using var path = new SKPath();
        SKPoint start = ToMassNavigationScreen(points[0], snapshot, worldRect);
        path.MoveTo(start);
        for (int i = 1; i < points.Length; i++)
        {
            SKPoint p = ToMassNavigationScreen(points[i], snapshot, worldRect);
            path.LineTo(p);
        }

        canvas.DrawPath(path, paint);
    }

    private static void DrawMassNavigationLocalPolyline(SKCanvas canvas, Vector2 center, float halfExtent, SKRect rect, SKPaint paint, params Vector2[] points)
    {
        if (points.Length < 2) return;
        using var path = new SKPath();
        SKPoint start = ToMassNavigationLocalScreen(points[0], center, halfExtent, rect);
        path.MoveTo(start);
        for (int i = 1; i < points.Length; i++)
        {
            SKPoint point = ToMassNavigationLocalScreen(points[i], center, halfExtent, rect);
            path.LineTo(point);
        }

        canvas.DrawPath(path, paint);
    }

    private static SKColor ResolveMassNavigationLayerColor(int layer)
    {
        return layer switch
        {
            0 => new SKColor(80, 226, 151),
            1 => new SKColor(68, 180, 255),
            2 => new SKColor(210, 235, 255),
            3 => new SKColor(255, 190, 91),
            _ => new SKColor(192, 207, 218),
        };
    }

    private static void DrawMassNavigationRect(SKCanvas canvas, Vector2 center, float widthCm, float heightCm, MassNavigationSnapshot snapshot, SKRect worldRect, SKPaint paint)
    {
        Vector2 min = new(center.X - widthCm * 0.5f, center.Y - heightCm * 0.5f);
        Vector2 max = new(center.X + widthCm * 0.5f, center.Y + heightCm * 0.5f);
        SKPoint a = ToMassNavigationScreen(min, snapshot, worldRect);
        SKPoint b = ToMassNavigationScreen(max, snapshot, worldRect);
        SKRect rect = NormalizeRect(new SKRect(a.X, a.Y, b.X, b.Y));
        canvas.DrawRect(rect, paint);
    }

    private static SKPoint ToMassNavigationScreen(Vector2 world, MassNavigationSnapshot snapshot, SKRect worldRect)
    {
        float minX = snapshot.WorldWidthCm * -0.5f;
        float maxX = snapshot.WorldWidthCm * 0.5f;
        float minY = snapshot.WorldHeightCm * -0.5f;
        float maxY = snapshot.WorldHeightCm * 0.5f;
        float x = worldRect.Left + ((world.X - minX) / Math.Max(1f, maxX - minX) * worldRect.Width);
        float y = worldRect.Bottom - ((world.Y - minY) / Math.Max(1f, maxY - minY) * worldRect.Height);
        return new SKPoint(x, y);
    }

    private static Vector2 ToMassNavigationWorldCm(MassNavigationTargetSlotSample sample)
    {
        return new Vector2(sample.Xcm, sample.Ycm);
    }

    private static SKColor ResolveMassNavigationTeamColor(int teamId)
    {
        return teamId switch
        {
            1 => new SKColor(80, 150, 255),
            2 => new SKColor(255, 82, 74),
            3 => new SKColor(255, 198, 62),
            4 => new SKColor(88, 235, 120),
            _ => new SKColor(210, 220, 230),
        };
    }

    private static LauncherRecordingResult RecordNavigation2DTimedAvoidance(LauncherRecordingRequest request)
    {
        string screensDir = Path.Combine(request.OutputDirectory, "screens");
        Directory.CreateDirectory(screensDir);

        var timeline = new List<AvoidanceSnapshot>();
        var captureFrames = new List<CaptureFrame>();
        var frameTimesMs = new List<double>();

        using var runtime = CreateRuntime(request.Plan, request.BootstrapPath);
        if (!string.Equals(runtime.Config.StartupMapId, "nav2d_playground", StringComparison.OrdinalIgnoreCase))
        {
            runtime.Engine.LoadMap("nav2d_playground");
        }

        var navRuntime = runtime.Engine.GetService(CoreServiceKeys.Navigation2DRuntime)
            ?? throw new InvalidOperationException("Navigation2DRuntime is missing.");
        var overlay = runtime.Engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("ScreenOverlayBuffer is missing.");

        Navigation2DPlaygroundState.CurrentScenarioIndex = 0;
        Navigation2DPlaygroundState.AgentsPerTeam = NavAcceptanceAgentsPerTeam;
        RespawnNavigationPlaygroundScenario(runtime.Engine, scenarioIndex: 0, agentsPerTeam: NavAcceptanceAgentsPerTeam);
        Tick(runtime, 2, frameTimesMs);

        if (!string.Equals(runtime.Engine.GetService(Navigation2DPlaygroundKeys.ScenarioName), "Pass Through", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Navigation2D playground did not land on the expected Pass Through scenario.");
        }

        AssertNavigationOverlay(overlay);
        CaptureNavigationSnapshot(runtime.Engine, navRuntime, screensDir, frameTimesMs, timeline, captureFrames, tick: 0, step: "000_start", captureImage: true);

        for (int tick = 1; tick <= NavFinalTick; tick++)
        {
            Tick(runtime, 1, frameTimesMs);
            if (tick % NavTraceStrideTicks == 0)
            {
                bool captureImage = tick % NavCaptureStrideTicks == 0 || tick == NavFinalTick;
                string step = captureImage ? $"{tick:000}_t{tick:000}" : $"{tick:000}_sample";
                CaptureNavigationSnapshot(runtime.Engine, navRuntime, screensDir, frameTimesMs, timeline, captureFrames, tick, step, captureImage);
            }
        }

        WriteTimelineSheet("Navigation2D timed avoidance timeline", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        AvoidanceAcceptanceResult acceptance = EvaluateNavigationAcceptance(timeline);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildNavigationBattleReport(request, timeline, captureFrames, frameTimesMs, acceptance));
        File.WriteAllText(tracePath, BuildNavigationTraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildNavigationPathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildNavigationVisibleChecklist(captureFrames));
        File.WriteAllText(summaryPath, BuildNavigationSummaryJson(request, acceptance));

        if (!acceptance.Success)
        {
            throw new InvalidOperationException(acceptance.FailureSummary);
        }

        return new LauncherRecordingResult(
            request.OutputDirectory,
            battleReportPath,
            tracePath,
            pathPath,
            summaryPath,
            visibleChecklistPath,
            captureFrames.Select(frame => Path.Combine(screensDir, frame.FileName)).Append(Path.Combine(screensDir, "timeline.png")).ToList(),
            acceptance.NormalizedSignature);
    }

    private static void RespawnNavigationPlaygroundScenario(GameEngine engine, int scenarioIndex, int agentsPerTeam)
    {
        GameConfig? gameConfig = engine.GetService(CoreServiceKeys.GameConfig);
        var playgroundConfig = Navigation2DPlaygroundScenarioSpawner.GetPlaygroundConfig(gameConfig);
        Navigation2DPlaygroundState.CurrentScenarioIndex = Navigation2DPlaygroundScenarioSpawner.ClampScenarioIndex(playgroundConfig, scenarioIndex);
        Navigation2DPlaygroundState.AgentsPerTeam = agentsPerTeam;
        engine.World.Destroy(in NavScenarioEntitiesQuery);
        engine.World.Destroy(in NavFlowGoalQuery);
        var scenario = Navigation2DPlaygroundScenarioSpawner.GetScenario(playgroundConfig, Navigation2DPlaygroundState.CurrentScenarioIndex);
        var summary = Navigation2DPlaygroundScenarioSpawner.SpawnScenario(engine.World, scenario, agentsPerTeam);
        Navigation2DPlaygroundControlSystem.PublishScenarioServices(engine, playgroundConfig, summary, agentsPerTeam, Navigation2DPlaygroundState.CurrentScenarioIndex);
    }

    private static void CaptureNavigationSnapshot(
        GameEngine engine,
        Navigation2DRuntime navRuntime,
        string screensDir,
        IReadOnlyList<double> frameTimesMs,
        List<AvoidanceSnapshot> timeline,
        List<CaptureFrame> captureFrames,
        int tick,
        string step,
        bool captureImage)
    {
        AvoidanceSnapshot snapshot = SampleNavigationSnapshot(engine, navRuntime, tick, step, frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d);
        timeline.Add(snapshot);
        if (!captureImage)
        {
            return;
        }

        string fileName = $"{step}.png";
        WriteNavigationSnapshotImage(snapshot, Path.Combine(screensDir, fileName));
        captureFrames.Add(new CaptureFrame(snapshot.Tick, step, fileName, snapshot.CenterCount, snapshot.CenterStoppedAgents, snapshot.Team0CrossedFraction, snapshot.Team1CrossedFraction));
    }

    private static AvoidanceSnapshot SampleNavigationSnapshot(GameEngine engine, Navigation2DRuntime navRuntime, int tick, string step, double tickMs)
    {
        var team0 = new List<Vector2>();
        var team1 = new List<Vector2>();
        var blockers = new List<Vector2>();
        int movingAgents = 0;
        int centerCount = 0;
        int centerMovingAgents = 0;
        int centerStoppedAgents = 0;

        foreach (ref var chunk in engine.World.Query(in NavDynamicAgentsQuery))
        {
            var positions = chunk.GetSpan<Position2D>();
            var velocities = chunk.GetSpan<Velocity2D>();
            var teams = chunk.GetSpan<NavPlaygroundTeam>();
            foreach (int entityIndex in chunk)
            {
                Vector2 position = positions[entityIndex].Value.ToVector2();
                if (teams[entityIndex].Id == 0)
                {
                    team0.Add(position);
                }
                else if (teams[entityIndex].Id == 1)
                {
                    team1.Add(position);
                }

                bool isMoving = velocities[entityIndex].Linear.ToVector2().LengthSquared() > NavMovingSpeedSquaredThreshold;
                if (isMoving)
                {
                    movingAgents++;
                }

                if (MathF.Abs(position.X) <= NavCenterHalfWidthCm && MathF.Abs(position.Y) <= NavCenterHalfHeightCm)
                {
                    centerCount++;
                    if (isMoving)
                    {
                        centerMovingAgents++;
                    }
                    else
                    {
                        centerStoppedAgents++;
                    }
                }
            }
        }

        foreach (ref var chunk in engine.World.Query(in NavBlockerQuery))
        {
            var positions = chunk.GetSpan<Position2D>();
            foreach (int entityIndex in chunk)
            {
                blockers.Add(positions[entityIndex].Value.ToVector2());
            }
        }

        return new AvoidanceSnapshot(
            Tick: tick,
            Step: step,
            ScenarioName: engine.GetService(Navigation2DPlaygroundKeys.ScenarioName) ?? "Unknown",
            AgentsPerTeam: engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam),
            LiveAgents: engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal),
            FlowEnabled: navRuntime.FlowEnabled,
            FlowDebugEnabled: navRuntime.FlowDebugEnabled,
            TickMs: tickMs,
            Team0Positions: team0,
            Team1Positions: team1,
            BlockerPositions: blockers,
            Team0MedianPrimary: Median(team0.Select(point => point.X).ToArray()),
            Team1MedianPrimary: Median(team1.Select(point => point.X).ToArray()),
            Team0CrossedFraction: Fraction(team0, point => point.X > 0f),
            Team1CrossedFraction: Fraction(team1, point => point.X < 0f),
            CenterCount: centerCount,
            CenterMovingAgents: centerMovingAgents,
            CenterStoppedAgents: centerStoppedAgents,
            MovingAgents: movingAgents,
            FlowActiveTiles: navRuntime.FlowCount > 0 ? navRuntime.Flows.Sum(flow => flow.ActiveTileCount) : 0,
            FlowFrontierProcessed: navRuntime.FlowCount > 0 ? navRuntime.Flows.Sum(flow => flow.InstrumentedFrontierProcessedFrame) : 0,
            FlowBudgetClamped: navRuntime.FlowCount > 0 && navRuntime.Flows.Any(flow => flow.InstrumentedWindowBudgetClampedFrame),
            FlowWorldClamped: navRuntime.FlowCount > 0 && navRuntime.Flows.Any(flow => flow.InstrumentedWindowWorldClampedFrame));
    }

    private static void WriteNavigationSnapshotImage(AvoidanceSnapshot snapshot, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavImageWidth, NavImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(12, 16, 24));

        using var fillCenter = new SKPaint { Color = new SKColor(50, 90, 130, 48), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var strokeCenter = new SKPaint { Color = new SKColor(80, 180, 255, 140), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var axisPaint = new SKPaint { Color = new SKColor(90, 100, 120), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        using var team0Paint = new SKPaint { Color = new SKColor(64, 220, 110), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var team1Paint = new SKPaint { Color = new SKColor(255, 88, 88), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var blockerPaint = new SKPaint { Color = new SKColor(90, 150, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 24f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(180, 190, 205), IsAntialias = true, TextSize = 18f };

        SKRect centerRect = ToScreenRect(-NavCenterHalfWidthCm, -NavCenterHalfHeightCm, NavCenterHalfWidthCm, NavCenterHalfHeightCm);
        canvas.DrawRect(centerRect, fillCenter);
        canvas.DrawRect(centerRect, strokeCenter);
        canvas.DrawLine(ToNavigationScreen(new Vector2(NavWorldMinX, 0f)), ToNavigationScreen(new Vector2(NavWorldMaxX, 0f)), axisPaint);
        canvas.DrawLine(ToNavigationScreen(new Vector2(0f, NavWorldMinY)), ToNavigationScreen(new Vector2(0f, NavWorldMaxY)), axisPaint);

        foreach (Vector2 blocker in snapshot.BlockerPositions)
        {
            DrawNavigationAgent(canvas, blockerPaint, blocker, radiusPx: 6f);
        }

        foreach (Vector2 agent in snapshot.Team0Positions)
        {
            DrawNavigationAgent(canvas, team0Paint, agent, radiusPx: 3.8f);
        }

        foreach (Vector2 agent in snapshot.Team1Positions)
        {
            DrawNavigationAgent(canvas, team1Paint, agent, radiusPx: 3.8f);
        }

        canvas.DrawText($"Navigation2D Timed Avoidance | {snapshot.Step} | tick={snapshot.Tick}", 24, 34, textPaint);
        canvas.DrawText($"Scenario={snapshot.ScenarioName}  Agents/team={snapshot.AgentsPerTeam}  Live={snapshot.LiveAgents}", 24, 66, minorTextPaint);
        canvas.DrawText($"MedianX T0={snapshot.Team0MedianPrimary:F0}  T1={snapshot.Team1MedianPrimary:F0}  Crossed T0={snapshot.Team0CrossedFraction:P0}  T1={snapshot.Team1CrossedFraction:P0}", 24, 94, minorTextPaint);
        canvas.DrawText($"CenterCount={snapshot.CenterCount}  CenterMove={snapshot.CenterMovingAgents}  CenterStop={snapshot.CenterStoppedAgents}  MovingAgents={snapshot.MovingAgents}", 24, 122, minorTextPaint);
        canvas.DrawText($"FlowActiveTiles={snapshot.FlowActiveTiles}  Frontier={snapshot.FlowFrontierProcessed}", 24, 150, minorTextPaint);
        canvas.DrawText($"BudgetClamp={snapshot.FlowBudgetClamped}  WorldClamp={snapshot.FlowWorldClamped}  Tick={snapshot.TickMs:F3}ms", 24, 178, minorTextPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static AvoidanceAcceptanceResult EvaluateNavigationAcceptance(IReadOnlyList<AvoidanceSnapshot> timeline)
    {
        var failures = new List<string>();
        AvoidanceSnapshot start = timeline.First(snapshot => snapshot.Tick == 0);
        AvoidanceSnapshot mid = timeline.First(snapshot => snapshot.Tick == NavFinalTick / 2);
        AvoidanceSnapshot final = timeline.First(snapshot => snapshot.Tick == NavFinalTick);
        AvoidanceSnapshot peak = timeline.OrderByDescending(snapshot => snapshot.CenterCount).First();

        float team0MidAdvance = mid.Team0MedianPrimary - start.Team0MedianPrimary;
        float team1MidAdvance = start.Team1MedianPrimary - mid.Team1MedianPrimary;
        float team0FinalAdvance = final.Team0MedianPrimary - start.Team0MedianPrimary;
        float team1FinalAdvance = start.Team1MedianPrimary - final.Team1MedianPrimary;
        float finalCenterFraction = final.LiveAgents == 0 ? 0f : final.CenterCount / (float)final.LiveAgents;
        float finalCenterStoppedFraction = final.LiveAgents == 0 ? 0f : final.CenterStoppedAgents / (float)final.LiveAgents;
        bool densePeakObserved = peak.CenterCount >= Math.Max(16, (int)Math.Ceiling(final.LiveAgents * NavFinalCenterFractionLimit));
        bool centerRelieved = !densePeakObserved || final.CenterCount <= Math.Max((int)Math.Ceiling(peak.CenterCount * 0.75f), 8);

        AddAcceptanceCheck(start.Team0MedianPrimary < -3000f, $"Team 0 should spawn well left of center, but median X was {start.Team0MedianPrimary:F0}.", failures);
        AddAcceptanceCheck(start.Team1MedianPrimary > 3000f, $"Team 1 should spawn well right of center, but median X was {start.Team1MedianPrimary:F0}.", failures);
        AddAcceptanceCheck(team0MidAdvance > NavMidProgressMinimumCm, $"Team 0 median only advanced {team0MidAdvance:F0}cm by midpoint.", failures);
        AddAcceptanceCheck(team1MidAdvance > NavMidProgressMinimumCm, $"Team 1 median only advanced {team1MidAdvance:F0}cm by midpoint.", failures);
        AddAcceptanceCheck(team0FinalAdvance > NavFinalProgressMinimumCm, $"Team 0 median only advanced {team0FinalAdvance:F0}cm by timeout.", failures);
        AddAcceptanceCheck(team1FinalAdvance > NavFinalProgressMinimumCm, $"Team 1 median only advanced {team1FinalAdvance:F0}cm by timeout.", failures);
        AddAcceptanceCheck(finalCenterFraction < NavFinalCenterFractionLimit, $"Center box still contains {final.CenterCount}/{final.LiveAgents} agents at timeout ({finalCenterFraction:P0}).", failures);
        AddAcceptanceCheck(finalCenterStoppedFraction < NavFinalCenterStoppedFractionLimit, $"Center box still contains {final.CenterStoppedAgents}/{final.LiveAgents} stationary agents at timeout ({finalCenterStoppedFraction:P0}).", failures);
        AddAcceptanceCheck(final.MovingAgents > (int)Math.Ceiling(final.LiveAgents * NavMovingAgentsFractionLimit), $"Only {final.MovingAgents}/{final.LiveAgents} agents are still moving at timeout.", failures);
        AddAcceptanceCheck(centerRelieved, $"Center occupancy peaked at {peak.CenterCount} on tick {peak.Tick} and only fell to {final.CenterCount} by timeout.", failures);

        string normalizedSignature = string.Join("|", new[]
        {
            "navigation2d_playground_timed_avoidance",
            $"mid:{MathF.Round(team0MidAdvance):F0}/{MathF.Round(team1MidAdvance):F0}",
            $"final:{MathF.Round(team0FinalAdvance):F0}/{MathF.Round(team1FinalAdvance):F0}",
            $"center:{final.CenterCount}/{final.LiveAgents}",
            $"stopped:{final.CenterStoppedAgents}",
            $"peak:{peak.CenterCount}@{peak.Tick}"
        });

        string verdict = failures.Count == 0
            ? $"Timed avoidance passes: median advance is {team0FinalAdvance:F0}/{team1FinalAdvance:F0}cm and timeout center occupancy is {final.CenterCount}/{final.LiveAgents} with {final.CenterStoppedAgents} stationary."
            : "Timed avoidance fails: timeout still looks jammed by the configured progress and decongestion checks.";
        string failureSummary = failures.Count == 0 ? verdict : string.Join(Environment.NewLine, failures);

        return new AvoidanceAcceptanceResult(
            Success: failures.Count == 0,
            Verdict: verdict,
            FailureSummary: failureSummary,
            FailedChecks: failures,
            Team0MidAdvanceCm: team0MidAdvance,
            Team1MidAdvanceCm: team1MidAdvance,
            Team0FinalAdvanceCm: team0FinalAdvance,
            Team1FinalAdvanceCm: team1FinalAdvance,
            FinalCenterFraction: finalCenterFraction,
            FinalCenterStoppedFraction: finalCenterStoppedFraction,
            PeakCenterCount: peak.CenterCount,
            PeakCenterTick: peak.Tick,
            FinalCenterCount: final.CenterCount,
            FinalCenterStoppedAgents: final.CenterStoppedAgents,
            FinalLiveAgents: final.LiveAgents,
            NormalizedSignature: normalizedSignature);
    }

    private static string BuildNavigationBattleReport(
        LauncherRecordingRequest request,
        IReadOnlyList<AvoidanceSnapshot> timeline,
        IReadOnlyList<CaptureFrame> captureFrames,
        IReadOnlyList<double> frameTimesMs,
        AvoidanceAcceptanceResult acceptance)
    {
        AvoidanceSnapshot final = timeline[^1];
        double medianTickMs = Median(frameTimesMs.ToArray());
        double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
        string evidenceImages = string.Join(", ", captureFrames.Select(frame => $"`screens/{frame.FileName}`").Append("`screens/timeline.png`"));

        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: navigation2d-playground-timed-avoidance");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: verify the launcher-started Navigation2D playground actually decongests over time instead of timing out as a stationary knot in the center.");
        sb.AppendLine("- Gameplay domain: real launcher bootstrap, real adapter camera and culling services, real Navigation2D playground scenario state.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none");
        sb.AppendLine("- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Launch command: `{request.CommandText}`");
        sb.AppendLine($"- Scenario: `{timeline[0].ScenarioName}`");
        sb.AppendLine($"- Agents per team: `{NavAcceptanceAgentsPerTeam}`");
        sb.AppendLine($"- Clock profile: fixed `1/60s`, timeout tick `{NavFinalTick}`");
        sb.AppendLine($"- Evidence images: {evidenceImages}");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Boot the real playable Navigation2D playground through the unified launcher bootstrap.");
        sb.AppendLine("2. Force the Pass Through scenario and deterministic agent count through the existing playground state.");
        sb.AppendLine("3. Simulate until timeout while sampling crowd progress every 30 ticks and capturing timeline frames every 120 ticks.");
        sb.AppendLine("4. Fail if timeout still looks like a dense stationary center jam.");
        sb.AppendLine();
        sb.AppendLine("## Expected Outcomes");
        sb.AppendLine("- Primary success condition: both teams measurably advance through the conflict zone and timeout no longer shows a dense stationary center jam.");
        sb.AppendLine("- Failure branch condition: timeout arrives with weak median progress, excessive center occupancy, or too many stationary agents trapped in the center box.");
        sb.AppendLine("- Key metrics: team median X progress, center occupancy, stopped center agents, moving agent count, crossed fractions.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (AvoidanceSnapshot snapshot in timeline.Where(item => item.Tick == 0 || item.Tick % NavCaptureStrideTicks == 0 || item.Tick == NavFinalTick))
        {
            sb.AppendLine($"- [T+{snapshot.Tick:000}] {snapshot.Step} | MedianX T0={snapshot.Team0MedianPrimary:F0} T1={snapshot.Team1MedianPrimary:F0} | Crossed T0={snapshot.Team0CrossedFraction:P0} T1={snapshot.Team1CrossedFraction:P0} | Center={snapshot.CenterCount} move={snapshot.CenterMovingAgents} stop={snapshot.CenterStoppedAgents} | Moving={snapshot.MovingAgents} | Tick={snapshot.TickMs:F3}ms");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        foreach (string failedCheck in acceptance.FailedChecks)
        {
            sb.AppendLine($"- failed-check: {failedCheck}");
        }

        sb.AppendLine($"- reason: median advance reached `{acceptance.Team0FinalAdvanceCm:F0}` / `{acceptance.Team1FinalAdvanceCm:F0}` cm; timeout center box held `{final.CenterCount}` of `{final.LiveAgents}` agents with `{final.CenterStoppedAgents}` stationary; peak center occupancy was `{acceptance.PeakCenterCount}` at tick `{acceptance.PeakCenterTick}`.");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- trace samples: `{timeline.Count}`");
        sb.AppendLine($"- screenshot captures: `{captureFrames.Count}`");
        sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
        sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
        sb.AppendLine($"- normalized signature: `{acceptance.NormalizedSignature}`");
        sb.AppendLine("- reusable wiring: `launcher.runtime.json`, `Navigation2DPlaygroundState`, `Navigation2DRuntime`, `ScreenOverlayBuffer`, `PlayerInputHandler`");
        return sb.ToString();
    }

    private static string BuildNavigationTraceJsonl(string adapterId, IReadOnlyList<AvoidanceSnapshot> timeline)
    {
        var lines = new List<string>(timeline.Count);
        for (int index = 0; index < timeline.Count; index++)
        {
            AvoidanceSnapshot snapshot = timeline[index];
            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"nav2d-{adapterId}-{index + 1:000}",
                tick = snapshot.Tick,
                step = snapshot.Step,
                scenario = snapshot.ScenarioName,
                agents_per_team = snapshot.AgentsPerTeam,
                live_agents = snapshot.LiveAgents,
                team0_median_x = Math.Round(snapshot.Team0MedianPrimary, 2),
                team1_median_x = Math.Round(snapshot.Team1MedianPrimary, 2),
                team0_crossed_fraction = Math.Round(snapshot.Team0CrossedFraction, 4),
                team1_crossed_fraction = Math.Round(snapshot.Team1CrossedFraction, 4),
                center_count = snapshot.CenterCount,
                center_moving_agents = snapshot.CenterMovingAgents,
                center_stopped_agents = snapshot.CenterStoppedAgents,
                moving_agents = snapshot.MovingAgents,
                flow_active_tiles = snapshot.FlowActiveTiles,
                flow_frontier_processed = snapshot.FlowFrontierProcessed,
                flow_budget_clamped = snapshot.FlowBudgetClamped,
                flow_world_clamped = snapshot.FlowWorldClamped,
                tick_ms = Math.Round(snapshot.TickMs, 4),
                status = "done"
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildNavigationPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Boot launcher runtime for Navigation2D playground] --> B[Force PassThrough + deterministic agents per team]",
            "    B --> C[Run timed simulation to timeout]",
            "    C --> D[Capture multi-frame timeline + trace metrics]",
            "    D --> E{Median advance strong and timeout center jam low?}",
            "    E -->|yes| F[Write battle-report + trace + path + PNG timeline]",
            "    E -->|no| X[Fail acceptance: timeout still looks jammed]"
        }) + Environment.NewLine;
    }

    private static string BuildNavigationVisibleChecklist(IReadOnlyList<CaptureFrame> frames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: navigation2d-playground-timed-avoidance");
        sb.AppendLine();
        sb.AppendLine("- Review the PNG sequence chronologically; each later frame should show stronger approach through the conflict zone without a stationary knot surviving at timeout.");
        sb.AppendLine("- Timeout is acceptable only when the center box is not densely packed and the agents inside it are still moving.");
        sb.AppendLine("- `screens/timeline.png` is the compact strip for side-by-side adapter review.");
        sb.AppendLine();
        foreach (CaptureFrame frame in frames)
        {
            sb.AppendLine($"- `{frame.FileName}`: center={frame.CenterCount}, centerStopped={frame.CenterStoppedAgents}, crossed={frame.Team0CrossedFraction:P0}/{frame.Team1CrossedFraction:P0}");
        }

        return sb.ToString();
    }

    private static string BuildNavigationSummaryJson(LauncherRecordingRequest request, AvoidanceAcceptanceResult acceptance)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = "navigation2d_playground_timed_avoidance",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            team0_mid_advance_cm = Math.Round(acceptance.Team0MidAdvanceCm, 2),
            team1_mid_advance_cm = Math.Round(acceptance.Team1MidAdvanceCm, 2),
            team0_final_advance_cm = Math.Round(acceptance.Team0FinalAdvanceCm, 2),
            team1_final_advance_cm = Math.Round(acceptance.Team1FinalAdvanceCm, 2),
            final_center_fraction = Math.Round(acceptance.FinalCenterFraction, 4),
            final_center_stopped_fraction = Math.Round(acceptance.FinalCenterStoppedFraction, 4),
            final_center_count = acceptance.FinalCenterCount,
            final_center_stopped_agents = acceptance.FinalCenterStoppedAgents,
            final_live_agents = acceptance.FinalLiveAgents,
            peak_center_count = acceptance.PeakCenterCount,
            peak_center_tick = acceptance.PeakCenterTick,
            normalized_signature = acceptance.NormalizedSignature
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void AssertNavigationOverlay(ScreenOverlayBuffer overlay)
    {
        string dump = string.Join(" || ", ExtractOverlayText(overlay));
        if (!dump.Contains("Navigation2D Playground", StringComparison.Ordinal) ||
            !dump.Contains("FlowEnabled=", StringComparison.Ordinal) ||
            !dump.Contains("CacheLookups=", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Navigation overlay lines are incomplete: {dump}");
        }
    }

    private static List<string> ExtractOverlayText(ScreenOverlayBuffer? overlay)
    {
        var lines = new List<string>();
        if (overlay == null)
        {
            return lines;
        }

        foreach (ScreenOverlayItem item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            string? text = overlay.GetString(item.StringId);
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text);
            }
        }

        return lines;
    }

    private static void AddAcceptanceCheck(bool passed, string failure, List<string> failures)
    {
        if (!passed)
        {
            failures.Add(failure);
        }
    }

    private static void DrawWorldGrid(SKCanvas canvas, float minX, float maxX, float minY, float maxY, SKPaint paint, int width, int height)
    {
        const int spacing = 1000;
        int startX = (int)MathF.Floor(minX / spacing) * spacing;
        int endX = (int)MathF.Ceiling(maxX / spacing) * spacing;
        int startY = (int)MathF.Floor(minY / spacing) * spacing;
        int endY = (int)MathF.Ceiling(maxY / spacing) * spacing;

        for (int x = startX; x <= endX; x += spacing)
        {
            SKPoint from = ToScreen(new Vector2(x, minY), minX, maxX, minY, maxY, width, height);
            SKPoint to = ToScreen(new Vector2(x, maxY), minX, maxX, minY, maxY, width, height);
            canvas.DrawLine(from, to, paint);
        }

        for (int y = startY; y <= endY; y += spacing)
        {
            SKPoint from = ToScreen(new Vector2(minX, y), minX, maxX, minY, maxY, width, height);
            SKPoint to = ToScreen(new Vector2(maxX, y), minX, maxX, minY, maxY, width, height);
            canvas.DrawLine(from, to, paint);
        }
    }

    private static SKPaint ResolveEntityPaint(string entityName, SKPaint heroPaint, SKPaint scoutPaint, SKPaint captainPaint, SKPaint dummyPaint, SKPaint genericPaint)
    {
        return entityName switch
        {
            var name when string.Equals(name, CameraAcceptanceIds.HeroName, StringComparison.OrdinalIgnoreCase) => heroPaint,
            var name when string.Equals(name, CameraAcceptanceIds.ScoutName, StringComparison.OrdinalIgnoreCase) => scoutPaint,
            var name when string.Equals(name, CameraAcceptanceIds.CaptainName, StringComparison.OrdinalIgnoreCase) => captainPaint,
            "Dummy" => dummyPaint,
            _ => genericPaint
        };
    }

    private static void DrawCrosshair(SKCanvas canvas, SKPoint point, float radius, SKPaint paint)
    {
        canvas.DrawCircle(point.X, point.Y, radius, paint);
        canvas.DrawLine(point.X - radius - 6f, point.Y, point.X + radius + 6f, point.Y, paint);
        canvas.DrawLine(point.X, point.Y - radius - 6f, point.X, point.Y + radius + 6f, paint);
    }

    private static void WriteTimelineSheet(string title, IReadOnlyList<CaptureFrame> frames, string screensDir, string outputPath)
    {
        if (frames.Count == 0)
        {
            return;
        }

        const int thumbWidth = 800;
        const int thumbHeight = 450;
        int columns = 2;
        int rows = (int)Math.Ceiling(frames.Count / (double)columns);

        using var surface = SKSurface.Create(new SKImageInfo(columns * thumbWidth, rows * thumbHeight + 60));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(8, 10, 16));
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28f };
        canvas.DrawText(title, 20, 36, titlePaint);

        for (int index = 0; index < frames.Count; index++)
        {
            string sourcePath = Path.Combine(screensDir, frames[index].FileName);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            using SKBitmap bitmap = SKBitmap.Decode(sourcePath);
            int col = index % columns;
            int row = index / columns;
            SKRect dest = new(col * thumbWidth, row * thumbHeight + 60, (col + 1) * thumbWidth, (row + 1) * thumbHeight + 60);
            canvas.DrawBitmap(bitmap, dest);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static SKPoint ToScreen(Vector2 world, float minX, float maxX, float minY, float maxY, int width, int height)
    {
        float safeWidth = Math.Max(1f, maxX - minX);
        float safeHeight = Math.Max(1f, maxY - minY);
        float x = (world.X - minX) / safeWidth * width;
        float y = (world.Y - minY) / safeHeight * height;
        return new SKPoint(x, height - y);
    }

    private static SKPoint ToNavigationScreen(Vector2 world)
    {
        float x = (world.X - NavWorldMinX) / (NavWorldMaxX - NavWorldMinX) * NavImageWidth;
        float y = (world.Y - NavWorldMinY) / (NavWorldMaxY - NavWorldMinY) * NavImageHeight;
        return new SKPoint(x, NavImageHeight - y);
    }

    private static SKRect ToScreenRect(float minX, float minY, float maxX, float maxY)
    {
        SKPoint a = ToNavigationScreen(new Vector2(minX, minY));
        SKPoint b = ToNavigationScreen(new Vector2(maxX, maxY));
        return SKRect.Create(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }

    private static void DrawNavigationAgent(SKCanvas canvas, SKPaint paint, Vector2 world, float radiusPx)
    {
        SKPoint point = ToNavigationScreen(world);
        canvas.DrawCircle(point.X, point.Y, radiusPx, paint);
    }

    private static float Fraction(IReadOnlyList<Vector2> values, Func<Vector2, bool> predicate)
    {
        if (values.Count == 0)
        {
            return 0f;
        }

        int count = 0;
        for (int index = 0; index < values.Count; index++)
        {
            if (predicate(values[index]))
            {
                count++;
            }
        }

        return count / (float)values.Count;
    }

    private static float Median(float[] values)
    {
        if (values.Length == 0)
        {
            return 0f;
        }

        Array.Sort(values);
        int middle = values.Length / 2;
        return (values.Length & 1) != 0 ? values[middle] : (values[middle - 1] + values[middle]) * 0.5f;
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0d;
        }

        Array.Sort(values);
        int middle = values.Length / 2;
        return (values.Length & 1) != 0 ? values[middle] : (values[middle - 1] + values[middle]) * 0.5d;
    }

    private static string FormatPoint(Vector2 point)
    {
        return $"{point.X.ToString("F0", CultureInfo.InvariantCulture)},{point.Y.ToString("F0", CultureInfo.InvariantCulture)}";
    }

    private enum EvidenceScenario
    {
        None,
        CameraAcceptanceProjectionClick,
        RoadNetworkShowcaseCommandAndChunking,
        ChunkStreamingShowcaseCameraWindows,
        Navigation2DPlaygroundTimedAvoidance,
        MassNavigationLargeWorld
    }

    private sealed class RecordingRuntime : IDisposable
    {
        public RecordingRuntime(string adapterId, GameEngine engine, GameConfig config, ScriptedInputBackend inputBackend, IScreenProjector screenProjector, CameraPresenter cameraPresenter, RenderCameraDebugState renderCameraDebug, PresentationFrameSetupSystem? presentationFrameSetup, WorldHudToScreenSystem? hudProjection)
        {
            AdapterId = adapterId;
            Engine = engine;
            Config = config;
            InputBackend = inputBackend;
            ScreenProjector = screenProjector;
            CameraPresenter = cameraPresenter;
            RenderCameraDebug = renderCameraDebug;
            PresentationFrameSetup = presentationFrameSetup;
            HudProjection = hudProjection;
        }

        public string AdapterId { get; }
        public GameEngine Engine { get; }
        public GameConfig Config { get; }
        public ScriptedInputBackend InputBackend { get; }
        public IScreenProjector ScreenProjector { get; }
        public CameraPresenter CameraPresenter { get; }
        public RenderCameraDebugState RenderCameraDebug { get; }
        public PresentationFrameSetupSystem? PresentationFrameSetup { get; }
        public WorldHudToScreenSystem? HudProjection { get; }

        public Vector2 ProjectWorldCm(Vector2 worldCm)
        {
            return ProjectWorldCm(worldCm, yMeters: 0f);
        }

        public Vector2 ProjectWorldCm(Vector2 worldCm, float yMeters)
        {
            var world = new WorldCmInt2((int)MathF.Round(worldCm.X), (int)MathF.Round(worldCm.Y));
            return ScreenProjector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(world, yMeters));
        }

        public bool TryProjectNamedEntity(string targetName, out Vector2 screenPosition)
        {
            Vector2 projected = default;
            bool found = false;
            Engine.World.Query(in RoadNamedVisualEntityQuery, (Entity entity, ref Name name, ref VisualTransform transform) =>
            {
                if (found || !string.Equals(name.Value, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                projected = ScreenProjector.WorldToScreen(transform.Position);
                found = true;
            });
            screenPosition = projected;
            return found;
        }

        public void Dispose()
        {
            try
            {
                Engine.Stop();
            }
            catch
            {
            }

            Engine.Dispose();
        }
    }

    private sealed class ScriptedInputBackend : IInputBackend
    {
        private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
        private Vector2 _mousePosition;
        private float _mouseWheel;

        public void SetButton(string path, bool isDown) => _buttons[path] = isDown;
        public void SetMousePosition(Vector2 position) => _mousePosition = position;
        public void SetMouseWheel(float value) => _mouseWheel = value;
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool isDown) && isDown;
        public Vector2 GetMousePosition() => _mousePosition;
        public float GetMouseWheel() => _mouseWheel;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }

    private readonly record struct CameraSnapshot(
        int Tick,
        string Step,
        double TickMs,
        string ActiveMapId,
        string ActiveCameraId,
        Vector2 CameraTargetCm,
        float CameraDistanceCm,
        bool CameraIsFollowing,
        Vector2? ClickTargetWorldCm,
        IReadOnlyDictionary<string, Vector2> NamedEntities,
        IReadOnlyList<Vector2> DummyPositions,
        bool CueMarkerPresent,
        Vector2 CueMarkerWorldCm,
        IReadOnlyList<string> OverlayLines)
    {
        public int DummyCount => DummyPositions.Count;
    }

    private sealed record CameraAcceptanceResult(
        bool Success,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> FailedChecks,
        int StartDummyCount,
        int AfterClickDummyCount,
        Vector2 SpawnedDummyWorldCm,
        bool CueMarkerVisibleAfterClick,
        bool CueMarkerVisibleMidCapture,
        bool CueMarkerVisibleFinalCapture,
        int FinalTick,
        string NormalizedSignature);

    private readonly record struct RoadSplineCapture(
        int StableId,
        Vector3 P0,
        Vector3 P1,
        Vector3 P2,
        Vector3 P3,
        float Width);

    private readonly record struct RoadSnapshot(
        int Tick,
        string Step,
        double TickMs,
        string ActiveMapId,
        Vector2 CameraTargetCm,
        int LoadedChunkCount,
        int LoadedNodeCount,
        int ChunkSizeCm,
        IReadOnlyList<long> ActiveChunkKeys,
        string ActiveChunkSignature,
        int RoadSplineCount,
        IReadOnlyDictionary<string, Vector2> NamedEntities,
        IReadOnlyList<string> SelectedNames,
        bool CueMarkerPresent,
        Vector2 CueMarkerWorldCm,
        string ControlledActorName,
        Vector2 ControlledActorWorldCm,
        Vector2 BlueVanguardWorldCm,
        Vector2 BlueNorthWorldCm,
        Vector2 BlueSouthWorldCm,
        string StatusLine,
        IReadOnlyList<string> OverlayLines,
        IReadOnlyList<RoadSplineCapture> Splines);

    private sealed record RoadAcceptanceResult(
        bool Success,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> FailedChecks,
        IReadOnlyList<string> SelectedNames,
        string ControlledActorName,
        string AcceptedStatus,
        Vector2 StartControlledActorWorldCm,
        Vector2 FinalControlledActorWorldCm,
        string StartChunkSignature,
        string FinalChunkSignature,
        bool CueMarkerVisible,
        string NormalizedSignature);

    private readonly record struct ChunkSnapshot(
        int Tick,
        string Step,
        double TickMs,
        string ActiveMapId,
        Vector2 CameraTargetCm,
        int LoadedChunkCount,
        int LoadedNodeCount,
        int ChunkSizeCm,
        IReadOnlyList<long> ActiveChunkKeys,
        string ActiveChunkSignature,
        int RoadSplineCount,
        string StatusLine,
        IReadOnlyList<string> OverlayLines,
        IReadOnlyList<RoadSplineCapture> Splines);

    private sealed record ChunkAcceptanceResult(
        bool Success,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> FailedChecks,
        string StartChunkSignature,
        string FarChunkSignature,
        string ResetChunkSignature,
        string NormalizedSignature);

    private readonly record struct MassNavigationAgentSample(
        int TeamId,
        Vector2 WorldCm);

    private readonly record struct MassNavigationWorldBoundaryDiagnostics(
        bool Available,
        string Source,
        int WorldMinXCm,
        int WorldMinYCm,
        int WorldMaxXCm,
        int WorldMaxYCm,
        bool CameraInBounds,
        bool MinimapBoundaryClickInBounds,
        bool MinimapBoundaryClickClamped,
        bool GroundPickingInsideAccepted,
        bool GroundPickingOutsideClamped,
        string BoundaryClickResult,
        string GroundPickingResult,
        WorldCmInt2 InsideProbeWorldCm,
        WorldCmInt2 OutsideProbeWorldCm,
        WorldCmInt2 OutsideClampedWorldCm);

    private readonly record struct MassNavigationSnapshot(
        int Tick,
        string Step,
        double TickMs,
        string ActiveMapId,
        Vector2 CameraTargetCm,
        int WorldWidthCm,
        int WorldHeightCm,
        Vector2 SolverWindowCenterCm,
        float SolverWindowWidthCm,
        float SolverWindowHeightCm,
        string SolverWindowDriver,
        Vector2 FlowWorkAreaCenterCm,
        float FlowWorkAreaWidthCm,
        float FlowWorkAreaHeightCm,
        string FlowWorkAreaReason,
        int LoadedChunkCount,
        MassNavigationWorldBoundaryDiagnostics WorldBoundaryDiagnostics,
        string ActiveHotZoneId,
        IReadOnlyList<MassNavigationHotZoneSample> HotZones,
        int TeamCount,
        IReadOnlyList<int> TeamIds,
        IReadOnlyDictionary<int, int> TeamCounts,
        int AgentCount,
        int EcsAgentCount,
        int ControllableCount,
        int CommandedAgents,
        int MovingAgents,
        int SettledAgents,
        int PendingEntitySyncCount,
        bool FlowEnabled,
        int SolverActiveStaticObstacleCount,
        int SolverStaticObstacleCapacity,
        int BlockerCount,
        int HotspotMarkerCount,
        int PerformerPayloadCount,
        int PerformerActiveCount,
        bool MinimapVisible,
        string MinimapPreset,
        int MinimapMarkerCount,
        int MinimapVisibleMarkerCount,
        int MinimapBufferCount,
        int MinimapDroppedTotal,
        Vector2 MinimapCenterCm,
        float MinimapHalfExtentCm,
        Vector2 MinimapCameraTargetCm,
        int SelectedCount,
        int ActiveGroups,
        int ActiveOrderGroups,
        int ScenarioSpawnCount,
        int SceneResetCount,
        int CommandCountFrame,
        int SolverWindowMovesTotal,
        int CameraBudgetUpdatesTotal,
        int CommandRejectsTotal,
        float FrameMs,
        float SimulationMs,
        float PresentationMs,
        float PerformerMs,
        float MinimapMs,
        float MassNavigationMs,
        bool BakeDataBound,
        int MacroChunkColumns,
        int MacroChunkRows,
        int MacroChunkCount,
        int MacroChunkSizeXCm,
        int MacroChunkSizeYCm,
        int ExpectedMacroAdjacencyEdgeCount,
        MassNavigationBakeDataDomainSummary NavMeshBake,
        MassNavigationBakeDataDomainSummary RoadGraphBake,
        MassNavigationBakeDataDomainSummary FlowFieldBake,
        MassNavigationBakeDataDomainSummary StaticObstacleBake,
        int NavMeshLayerCount,
        int NavMeshProfileCount,
        int NavMeshAreaCostCount,
        int AuthoredStaticObstacleCount,
        int TargetStaticObstacleCount,
        bool HpaOverlayRequired,
        bool PathInspectorRequired,
        bool BakeOverlayRequired,
        IReadOnlyList<MassNavigationBakeDataProfileSummary> BakeProfiles,
        MassNavigationPathOnlyQueryDiagnostics PathOnlyQuery,
        MassNavigationOrderReuseDiagnostics OrderReuse,
        MassNavigationTargetAllocationDiagnostics TargetAllocation,
        IReadOnlyList<MassNavigationTargetSlotSample> TargetSlotSamples,
            MassNavigationObstacleDiagnostics ObstacleDiagnostics,
            MassNavigationStaticObstacleWorldDiagnostics StaticObstacleWorldDiagnostics,
        MassNavigationHpaMacroDiagnostics HpaMacroDiagnostics,
        MassNavigationHpaGraphAssetDiagnostics HpaGraphDiagnostics,
        IReadOnlyList<MassNavigationLayerCostDiagnostics> LayerCostDiagnostics,
        IReadOnlyList<MassNavigationStrategySwitchDiagnostics> StrategySwitchDiagnostics,
        MassNavigationWaypointPathDiagnostics WaypointPathDiagnostics,
        MassNavigationDebugVisualDiagnostics DebugVisualDiagnostics,
        IReadOnlyList<MassNavigationAgentSample> SamplePositions,
        IReadOnlyList<string> OverlayLines);

    private readonly record struct MassNavigationHotZoneSample(
        string Id,
        string Label,
        Vector2 CenterCm,
        int WidthCm,
        int HeightCm);

    private readonly record struct MassNavigationDebugVisualDiagnostics(
        bool Available,
        float ScreenOverlayBuildMs,
        float ScreenOverlayDrawMs,
        float ScreenOverlayPaintMs,
        float ScreenOverlayCompositeMs,
        float ScreenOverlayFinalDrawMs,
        int ScreenOverlayItems,
        int ScreenOverlayRebuiltLanes,
        int ScreenOverlayDirtyLanes,
        int EvidenceOverlayItems,
        int TextLayoutCacheCount,
        float DebugDrawRenderMs,
        float NativeDiagnosticHudMs,
        int DebugDrawCommands,
        int VisibleEntities,
        bool FpsMeasured,
        string Source);

    private sealed record MassNavigationRaylibFrameBenchmark(
        bool Available,
        bool SmokePassed,
        bool ProductionPassed,
        bool MicroBenchmarkProductionThresholdPassed,
        string RendererScope,
        bool FullGameRendererLoadedDataMeasured,
        string ScreenshotPath,
        string JsonPath,
        MassNavigationRaylibFramePass DebugOff,
        MassNavigationRaylibFramePass DebugOn,
        double FpsDeltaPercent,
        double OverlayP95DeltaMs,
        double OverlayDrawMs,
        int AgentDrawCount,
        int ObstacleTargetCount,
        int ObstacleBucketCount,
        string Notes)
    {
        public static MassNavigationRaylibFrameBenchmark Unavailable(string notes)
        {
            return new MassNavigationRaylibFrameBenchmark(
                Available: false,
                SmokePassed: false,
                ProductionPassed: false,
                MicroBenchmarkProductionThresholdPassed: false,
                RendererScope: MassNavigationRendererScope,
                FullGameRendererLoadedDataMeasured: false,
                ScreenshotPath: string.Empty,
                JsonPath: string.Empty,
                DebugOff: MassNavigationRaylibFramePass.Empty(debugOverlay: false),
                DebugOn: MassNavigationRaylibFramePass.Empty(debugOverlay: true),
                FpsDeltaPercent: 0d,
                OverlayP95DeltaMs: 0d,
                OverlayDrawMs: 0d,
                AgentDrawCount: 0,
                ObstacleTargetCount: 0,
                ObstacleBucketCount: 0,
                Notes: notes);
        }
    }

    private readonly record struct MassNavigationRaylibFramePass(
        bool DebugOverlay,
        int FrameCount,
        double P50Ms,
        double P95Ms,
        double P99Ms,
        double MaxMs,
        double FpsP50,
        double FpsP95,
        double FpsP99,
        double OverlayP95Ms,
        double OverlayDrawMs,
        double ScreenshotMs)
    {
        public static MassNavigationRaylibFramePass Empty(bool debugOverlay)
        {
            return new MassNavigationRaylibFramePass(
                DebugOverlay: debugOverlay,
                FrameCount: 0,
                P50Ms: 0d,
                P95Ms: 0d,
                P99Ms: 0d,
                MaxMs: 0d,
                FpsP50: 0d,
                FpsP95: 0d,
                FpsP99: 0d,
                OverlayP95Ms: 0d,
                OverlayDrawMs: 0d,
                ScreenshotMs: 0d);
        }
    }

    private sealed record MassNavigationAcceptanceResult(
        bool Success,
        bool SceneSmokeSuccess,
        bool ProductionGateSuccess,
        bool MachineProductionEvidenceSuccess,
        bool ManualUatAccepted,
        string ManualUatEvidencePath,
        string ManualUatBlocker,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> SceneSmokeFailedChecks,
        IReadOnlyList<string> ProductionGateFailedChecks,
        IReadOnlyList<string> FailedChecks,
        string NormalizedSignature);

    private sealed record MassNavigationManualUatSignoff(
        bool Accepted,
        string EvidencePath,
        string Blocker);

    private sealed record MassNavigationUseCaseStatus(
        [property: JsonPropertyName("id")]
        string Id,
        [property: JsonPropertyName("name")]
        string Name,
        [property: JsonPropertyName("showcase_status")]
        string ShowcaseStatus,
        [property: JsonPropertyName("production_status")]
        string ProductionStatus,
        [property: JsonPropertyName("evidence")]
        string Evidence,
        [property: JsonPropertyName("acceptance_proof")]
        string AcceptanceProof,
        [property: JsonPropertyName("player_story_status")]
        string PlayerStoryStatus,
        [property: JsonPropertyName("player_visible_evidence_files")]
        IReadOnlyList<string> PlayerVisibleEvidenceFiles);

    private sealed record MassNavigationLayerQueryMatrixRow(
        [property: JsonPropertyName("agent_type_id")]
        string AgentTypeId,
        [property: JsonPropertyName("nav_profile_id")]
        string NavProfileId,
        [property: JsonPropertyName("layer")]
        int Layer,
        [property: JsonPropertyName("selection_mode")]
        string SelectionMode,
        [property: JsonPropertyName("area_cost_samples")]
        string AreaCostSamples,
        [property: JsonPropertyName("graph_rule_summary")]
        string GraphRuleSummary,
        [property: JsonPropertyName("forbidden_tag_summary")]
        string ForbiddenTagSummary,
        [property: JsonPropertyName("requested_mode")]
        string RequestedMode,
        [property: JsonPropertyName("selected_strategy")]
        string SelectedStrategy,
        [property: JsonPropertyName("graph_status")]
        string GraphStatus,
        [property: JsonPropertyName("mesh_status")]
        string MeshStatus,
        [property: JsonPropertyName("mesh_query_source")]
        string MeshQuerySource,
        [property: JsonPropertyName("mesh_touched_tile_count")]
        int MeshTouchedTileCount,
        [property: JsonPropertyName("cost_breakdown")]
        string CostBreakdown,
        [property: JsonPropertyName("acceptance_proof")]
        string AcceptanceProof);

    private readonly record struct FrameTimingStats(
        double P50Ms,
        double P95Ms,
        double P99Ms,
        double MaxMs,
        int FrameCount);

    private readonly record struct AvoidanceSnapshot(
        int Tick,
        string Step,
        string ScenarioName,
        int AgentsPerTeam,
        int LiveAgents,
        bool FlowEnabled,
        bool FlowDebugEnabled,
        double TickMs,
        IReadOnlyList<Vector2> Team0Positions,
        IReadOnlyList<Vector2> Team1Positions,
        IReadOnlyList<Vector2> BlockerPositions,
        float Team0MedianPrimary,
        float Team1MedianPrimary,
        float Team0CrossedFraction,
        float Team1CrossedFraction,
        int CenterCount,
        int CenterMovingAgents,
        int CenterStoppedAgents,
        int MovingAgents,
        int FlowActiveTiles,
        int FlowFrontierProcessed,
        bool FlowBudgetClamped,
        bool FlowWorldClamped);

    private readonly record struct CaptureFrame(
        int Tick,
        string Step,
        string FileName,
        int CenterCount,
        int CenterStoppedAgents,
        float Team0CrossedFraction,
        float Team1CrossedFraction);

    private sealed record AvoidanceAcceptanceResult(
        bool Success,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> FailedChecks,
        float Team0MidAdvanceCm,
        float Team1MidAdvanceCm,
        float Team0FinalAdvanceCm,
        float Team1FinalAdvanceCm,
        float FinalCenterFraction,
        float FinalCenterStoppedFraction,
        int PeakCenterCount,
        int PeakCenterTick,
        int FinalCenterCount,
        int FinalCenterStoppedAgents,
        int FinalLiveAgents,
        string NormalizedSignature);
}
