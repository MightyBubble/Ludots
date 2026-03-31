using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using CameraAcceptanceMod;
using Ludots.Adapter.Raylib.Services;
using Ludots.Adapter.Web.Services;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map.Board;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Launcher.Backend;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Skia;
using Navigation2DPlaygroundMod;
using Navigation2DPlaygroundMod.Systems;
using Raylib_cs;
using SkiaSharp;
using TerrainBenchmarkMod;

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

    private const float DeltaTime = 1f / 60f;
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;
    private const int CameraImageWidth = 1600;
    private const int CameraImageHeight = 900;
    private const int RoadImageWidth = 1600;
    private const int RoadImageHeight = 900;
    private const int NavImageWidth = 1600;
    private const int NavImageHeight = 900;
    private const int NavMeshImageWidth = 1600;
    private const int NavMeshImageHeight = 900;
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
    private const int NavMeshAcceptanceChunkX = -1;
    private const int NavMeshAcceptanceChunkY = -1;
    private const int NavMeshAcceptanceCandidateCount = 20;
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
    private static readonly Vector2[] RoadCommandPickOffsetsPx =
    [
        Vector2.Zero,
        new Vector2(-180f, 0f),
        new Vector2(180f, 0f),
        new Vector2(0f, -120f),
        new Vector2(0f, 120f),
        new Vector2(-260f, -120f),
        new Vector2(260f, -120f),
        new Vector2(-260f, 120f),
        new Vector2(260f, 120f),
        new Vector2(-340f, 0f),
        new Vector2(340f, 0f),
        new Vector2(0f, -220f),
        new Vector2(0f, 220f)
    ];
    private static readonly Vector2[] RoadFallbackGroundScreenFractions =
    [
        new Vector2(0.50f, 0.32f),
        new Vector2(0.42f, 0.32f),
        new Vector2(0.58f, 0.32f),
        new Vector2(0.35f, 0.32f),
        new Vector2(0.65f, 0.32f),
        new Vector2(0.50f, 0.50f),
        new Vector2(0.40f, 0.50f),
        new Vector2(0.60f, 0.50f),
        new Vector2(0.50f, 0.40f),
        new Vector2(0.50f, 0.60f),
        new Vector2(0.25f, 0.25f),
        new Vector2(0.50f, 0.25f),
        new Vector2(0.75f, 0.25f),
        new Vector2(0.25f, 0.75f),
        new Vector2(0.50f, 0.75f),
        new Vector2(0.75f, 0.75f)
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
            EvidenceScenario.NavMeshBakeTerrainVisualization => Task.FromResult(RecordNavMeshBakeTerrainVisualization(request)),
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

        if (plan.RootModIds.Any(id => string.Equals(id, "RoadNetworkShowcaseMod", StringComparison.OrdinalIgnoreCase)))
        {
            return EvidenceScenario.RoadNetworkShowcaseCommandAndChunking;
        }

        if (plan.RootModIds.Any(id => string.Equals(id, "ChunkStreamingShowcaseMod", StringComparison.OrdinalIgnoreCase)))
        {
            return EvidenceScenario.ChunkStreamingShowcaseCameraWindows;
        }

        if (plan.RootModIds.Any(id => string.Equals(id, "TerrainBenchmarkMod", StringComparison.OrdinalIgnoreCase)))
        {
            return EvidenceScenario.NavMeshBakeTerrainVisualization;
        }

        return EvidenceScenario.None;
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

        var skiaRenderer = new SkiaUiRenderer();
        var textMeasurer = new SkiaTextMeasurer();
        var imageSizeProvider = new SkiaImageSizeProvider();
        var uiRoot = new UIRoot(skiaRenderer);
        uiRoot.Resize(DefaultWidth, DefaultHeight);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UISystem, (Ludots.Core.UI.IUiSystem)new MarkupUiSystem(uiRoot, textMeasurer, imageSizeProvider));
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)textMeasurer);
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)imageSizeProvider);

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

        var cullingSystem = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, viewController);
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
        engine.SetService(CoreServiceKeys.UISystem, (Ludots.Core.UI.IUiSystem)new MarkupUiSystem(uiRoot, textMeasurer, imageSizeProvider));
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)textMeasurer);
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)imageSizeProvider);

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

        var cullingSystem = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, viewController);
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
            runtime.Engine.SetService(CoreServiceKeys.UiCaptured, false);
            runtime.Engine.Tick(DeltaTime);
            float alpha = runtime.PresentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
            runtime.CameraPresenter.Update(runtime.Engine.GameSession.Camera, alpha, runtime.RenderCameraDebug);
            runtime.HudProjection?.Update(DeltaTime);
            frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
        }
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
        double medianTickMs = Median(frameTimesMs.ToArray());
        double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
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
        sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
        sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
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

        RoadCommandTarget roadCommandTarget = ResolveRoadCommandTarget(runtime, frameTimesMs, RoadCommandWorldCm);
        Vector2 commandScreen = roadCommandTarget.ScreenPosition;
        ClickSecondary(runtime, commandScreen, frameTimesMs);
        AdvanceUntilRoadStatus(runtime, frameTimesMs, roadCommandTarget.WorldCm, maxFrames: 36);
        CaptureRoadSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "002_command_accepted", roadCommandTarget.WorldCm);

        AdvanceUntilRoadMovement(runtime, frameTimesMs, roadCommandTarget.WorldCm, timeline[2].ControlledActorName, timeline[2].ControlledActorWorldCm, maxFrames: 420);
        CaptureRoadSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "003_column_advancing", roadCommandTarget.WorldCm);

        ApplyCameraTarget(runtime, RoadChunkShiftTargetCm, frameTimesMs, settleTicks: 4);
        AdvanceUntilRoadChunkShift(runtime, frameTimesMs, roadCommandTarget.WorldCm, timeline[0].ActiveChunkSignature, maxFrames: 48);
        CaptureRoadSnapshot(runtime, screensDir, frameTimesMs, timeline, captureFrames, "004_chunk_shifted", roadCommandTarget.WorldCm);

        WriteTimelineSheet("Road network showcase timeline", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        RoadAcceptanceResult acceptance = EvaluateRoadAcceptance(timeline, roadCommandTarget.WorldCm);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildRoadBattleReport(request, timeline, captureFrames, frameTimesMs, acceptance, roadCommandTarget.WorldCm));
        File.WriteAllText(tracePath, BuildRoadTraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildRoadPathMermaid(roadCommandTarget.WorldCm));
        File.WriteAllText(visibleChecklistPath, BuildRoadVisibleChecklist(timeline, roadCommandTarget.WorldCm));
        File.WriteAllText(summaryPath, BuildRoadSummaryJson(request, acceptance, roadCommandTarget.WorldCm));

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

    private static RoadCommandTarget ResolveRoadCommandTarget(RecordingRuntime runtime, List<double> frameTimesMs, Vector2 preferredWorldCm)
    {
        if (TryFindGroundScreenNearWorld(runtime, preferredWorldCm, out RoadCommandTarget directTarget))
        {
            return directTarget;
        }

        ApplyCameraTarget(runtime, preferredWorldCm, frameTimesMs, settleTicks: 6);
        if (TryFindGroundScreenNearWorld(runtime, preferredWorldCm, out RoadCommandTarget focusedTarget))
        {
            return focusedTarget;
        }

        if (TryFindAnyVisibleGroundScreen(runtime, preferredWorldCm, out RoadCommandTarget fallbackTarget))
        {
            return fallbackTarget;
        }

        throw new InvalidOperationException($"Road showcase recorder could not resolve any visible ground screen point near {FormatPoint(preferredWorldCm)}.");
    }

    private static bool TryFindGroundScreenNearWorld(RecordingRuntime runtime, Vector2 preferredWorldCm, out RoadCommandTarget target)
    {
        target = default;
        Vector2 projected = runtime.ProjectWorldCm(preferredWorldCm);
        bool found = false;
        float bestDistanceSq = float.MaxValue;
        Vector2 bestScreen = default;
        Vector2 bestWorld = default;

        foreach (Vector2 offset in RoadCommandPickOffsetsPx)
        {
            Vector2 candidateScreen = projected + offset;
            if (!runtime.TryResolveGroundWorldCm(candidateScreen, out Vector2 candidateWorld))
            {
                continue;
            }

            float distanceSq = Vector2.DistanceSquared(candidateWorld, preferredWorldCm);
            if (distanceSq >= bestDistanceSq)
            {
                continue;
            }

            found = true;
            bestDistanceSq = distanceSq;
            bestScreen = candidateScreen;
            bestWorld = candidateWorld;
        }

        if (!found)
        {
            return false;
        }

        target = new RoadCommandTarget(bestScreen, bestWorld);
        return true;
    }

    private static bool TryFindAnyVisibleGroundScreen(RecordingRuntime runtime, Vector2 preferredWorldCm, out RoadCommandTarget target)
    {
        target = default;
        Vector2 resolution = runtime.Resolution;
        bool found = false;
        float bestDistanceSq = float.MaxValue;
        Vector2 bestScreen = default;
        Vector2 bestWorld = default;
        foreach (Vector2 fraction in RoadFallbackGroundScreenFractions)
        {
            Vector2 candidateScreen = new(resolution.X * fraction.X, resolution.Y * fraction.Y);
            if (!runtime.TryResolveGroundWorldCm(candidateScreen, out Vector2 candidateWorld))
            {
                continue;
            }

            float distanceSq = Vector2.DistanceSquared(candidateWorld, preferredWorldCm);
            if (distanceSq >= bestDistanceSq)
            {
                continue;
            }

            found = true;
            bestDistanceSq = distanceSq;
            bestScreen = candidateScreen;
            bestWorld = candidateWorld;
        }

        if (!found)
        {
            return false;
        }

        target = new RoadCommandTarget(bestScreen, bestWorld);
        return true;
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
        for (int index = 0; index < selectedEntities.Length; index++)
        {
            Entity entity = selectedEntities[index];
            if (runtime.Engine.World.Has<Name>(entity))
            {
                selectedNames.Add(runtime.Engine.World.Get<Name>(entity).Value);
            }
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
            CommandTargetWorldCm: commandTargetWorldCm,
            StatusLine: statusLine,
            OverlayLines: overlayLines,
            Splines: splines);
    }

    private static void AdvanceUntilRoadStatus(RecordingRuntime runtime, List<double> frameTimesMs, Vector2 commandTargetWorldCm, int maxFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            Tick(runtime, 1, frameTimesMs);
            RoadSnapshot snapshot = SampleRoadSnapshot(runtime, "probe_status", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, commandTargetWorldCm);
            if (IsAcceptedRoadStatus(snapshot.StatusLine))
            {
                return;
            }
        }
    }

    private static void AdvanceUntilRoadMovement(RecordingRuntime runtime, List<double> frameTimesMs, Vector2 commandTargetWorldCm, string controlledActorName, Vector2 startWorldCm, int maxFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            Tick(runtime, 1, frameTimesMs);
            RoadSnapshot snapshot = SampleRoadSnapshot(runtime, "probe_move", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, commandTargetWorldCm);
            Vector2 currentWorldCm = string.Equals(snapshot.ControlledActorName, controlledActorName, StringComparison.OrdinalIgnoreCase)
                ? snapshot.ControlledActorWorldCm
                : ResolveNamedPosition(snapshot.NamedEntities, controlledActorName);
            if (currentWorldCm.X - startWorldCm.X >= RoadMovementMinimumCm)
            {
                return;
            }
        }
    }

    private static void AdvanceUntilRoadChunkShift(RecordingRuntime runtime, List<double> frameTimesMs, Vector2 commandTargetWorldCm, string startChunkSignature, int maxFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            Tick(runtime, 1, frameTimesMs);
            RoadSnapshot snapshot = SampleRoadSnapshot(runtime, "probe_chunks", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d, commandTargetWorldCm);
            if (!string.Equals(snapshot.ActiveChunkSignature, startChunkSignature, StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    private static RoadAcceptanceResult EvaluateRoadAcceptance(IReadOnlyList<RoadSnapshot> timeline, Vector2 commandTargetWorldCm)
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
            $"Right-click should emit a visible cue marker near {FormatPoint(commandTargetWorldCm)}.", failures);
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
            $"command:{FormatPoint(commandTargetWorldCm)}",
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
        RoadAcceptanceResult acceptance,
        Vector2 commandTargetWorldCm)
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
        sb.AppendLine($"- Command target: `{FormatPoint(commandTargetWorldCm)}`");
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
        sb.AppendLine("- reusable wiring: `launcher.runtime.json`, `PlayerInputHandler`, `EntityClickSelectSystem`, `InputOrderMappingSystem`, `AutoPathService`, `RoadSplineBuffer`, `LoadedChunksSource`");
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

    private static string BuildRoadPathMermaid(Vector2 commandTargetWorldCm)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Boot launcher runtime for RoadNetworkShowcaseMod] --> B[Settle tactical camera and chunk window]",
            "    B --> C[Project Blue Vanguard visual pivot and inject left-click]",
            "    C --> D{Selection contains Blue Vanguard?}",
            $"    D -->|yes| E[Resolve visible ground near {FormatPoint(commandTargetWorldCm)} and inject right-click]",
            "    E --> F{HUD shows an accepted route selection and cue marker is visible?}",
            "    F -->|yes| G[Advance simulation until the controlled blue column moves east]",
            "    G --> H[Apply east camera target and wait for loaded chunk signature to change]",
            "    H --> I[Write battle-report + trace + path + PNG timeline]",
            "    D -->|no| X[Fail acceptance: selection bridge diverged]",
            "    F -->|no| Y[Fail acceptance: road command still invalid or marker missing]"
        }) + Environment.NewLine;
    }

    private static string BuildRoadVisibleChecklist(IReadOnlyList<RoadSnapshot> timeline, Vector2 commandTargetWorldCm)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: road-network-showcase-command-and-chunking");
        sb.AppendLine();
        sb.AppendLine("- `000_start` should show the initial central loaded chunk window and visible road splines.");
        sb.AppendLine("- `001_selected` should highlight Blue Vanguard as selected.");
        sb.AppendLine($"- `002_command_accepted` should show a cue marker near `{FormatPoint(commandTargetWorldCm)}` and a valid accepted route HUD status instead of `error 2`.");
        sb.AppendLine("- `003_column_advancing` should show the controlled blue column shifted east along the road.");
        sb.AppendLine("- `004_chunk_shifted` should show the camera moved east and a different loaded chunk window.");
        sb.AppendLine();
        foreach (RoadSnapshot snapshot in timeline)
        {
            sb.AppendLine($"- `{snapshot.Step}.png`: status=`{snapshot.StatusLine}` selected=`{string.Join(", ", snapshot.SelectedNames)}` chunks={snapshot.LoadedChunkCount} roads={snapshot.RoadSplineCount} cue={(snapshot.CueMarkerPresent ? "visible" : "hidden")}");
        }

        return sb.ToString();
    }

    private static string BuildRoadSummaryJson(LauncherRecordingRequest request, RoadAcceptanceResult acceptance, Vector2 commandTargetWorldCm)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = "road_network_showcase_command_and_chunking",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            command_target = new
            {
                x = Math.Round(commandTargetWorldCm.X, 2),
                y = Math.Round(commandTargetWorldCm.Y, 2)
            },
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
        if (snapshot.CommandTargetWorldCm.HasValue)
        {
            DrawCrosshair(canvas, ToScreen(snapshot.CommandTargetWorldCm.Value, RoadWorldMinX, RoadWorldMaxX, RoadWorldMinY, RoadWorldMaxY, RoadImageWidth, RoadImageHeight), 14f, roadPaint);
        }
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

    private static LauncherRecordingResult RecordNavigation2DTimedAvoidance(LauncherRecordingRequest request)
    {
        NavigationAcceptanceSpec spec = ResolveNavigationAcceptanceSpec(request.OutputDirectory);
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

        Navigation2DPlaygroundState.CurrentScenarioIndex = spec.ScenarioIndex;
        Navigation2DPlaygroundState.AgentsPerTeam = spec.AgentsPerTeam;
        RespawnNavigationPlaygroundScenario(runtime.Engine, scenarioIndex: spec.ScenarioIndex, agentsPerTeam: spec.AgentsPerTeam);
        Tick(runtime, 2, frameTimesMs);

        if (!string.Equals(runtime.Engine.GetService(Navigation2DPlaygroundKeys.ScenarioName), spec.ScenarioName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Navigation2D playground did not land on the expected scenario '{spec.ScenarioName}'.");
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

        WriteTimelineSheet($"{spec.ScenarioName} timed avoidance timeline", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        AvoidanceAcceptanceResult acceptance = EvaluateNavigationAcceptance(timeline, spec);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildNavigationBattleReport(request, spec, timeline, captureFrames, frameTimesMs, acceptance));
        File.WriteAllText(tracePath, BuildNavigationTraceJsonl(request.Plan.AdapterId, spec, timeline));
        File.WriteAllText(pathPath, BuildNavigationPathMermaid(spec));
        File.WriteAllText(visibleChecklistPath, BuildNavigationVisibleChecklist(spec, timeline, captureFrames));
        File.WriteAllText(summaryPath, BuildNavigationSummaryJson(request, spec, acceptance));

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

    private static NavigationAcceptanceSpec ResolveNavigationAcceptanceSpec(string outputDirectory)
    {
        string normalized = (outputDirectory ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
        if (normalized.Contains("bottleneck"))
        {
            return new NavigationAcceptanceSpec(
                Key: "navigation2d-bottleneck-obstacle",
                ScenarioName: "Bottleneck",
                ScenarioIndex: 2,
                AgentsPerTeam: NavAcceptanceAgentsPerTeam,
                Team0Direction: 1,
                Team1Direction: -1,
                MinimumBlockers: 8,
                FinalCenterFractionLimit: 0.80f,
                FinalCenterStoppedFractionLimit: 0.30f,
                MovingAgentsFractionLimit: 0.60f,
                PeakReliefFraction: 0.95f,
                Team0FinalCrossedFractionLimit: 0.35f,
                Team1FinalCrossedFractionLimit: 0.25f);
        }

        if (normalized.Contains("lane_merge"))
        {
            return new NavigationAcceptanceSpec(
                Key: "navigation2d-lane-merge-hybrid",
                ScenarioName: "Lane Merge",
                ScenarioIndex: 3,
                AgentsPerTeam: NavAcceptanceAgentsPerTeam,
                Team0Direction: 1,
                Team1Direction: 1,
                MinimumBlockers: 0,
                FinalCenterFractionLimit: NavFinalCenterFractionLimit,
                FinalCenterStoppedFractionLimit: NavFinalCenterStoppedFractionLimit,
                MovingAgentsFractionLimit: NavMovingAgentsFractionLimit,
                PeakReliefFraction: 0.75f,
                Team0FinalCrossedFractionLimit: 0f,
                Team1FinalCrossedFractionLimit: 0f);
        }

        return new NavigationAcceptanceSpec(
            Key: "navigation2d-pass-through-collision",
            ScenarioName: "Pass Through",
            ScenarioIndex: 0,
            AgentsPerTeam: NavAcceptanceAgentsPerTeam,
            Team0Direction: 1,
            Team1Direction: -1,
            MinimumBlockers: 0,
            FinalCenterFractionLimit: NavFinalCenterFractionLimit,
            FinalCenterStoppedFractionLimit: NavFinalCenterStoppedFractionLimit,
            MovingAgentsFractionLimit: NavMovingAgentsFractionLimit,
            PeakReliefFraction: 0.75f,
            Team0FinalCrossedFractionLimit: 0f,
            Team1FinalCrossedFractionLimit: 0f);
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

    private static AvoidanceAcceptanceResult EvaluateNavigationAcceptance(IReadOnlyList<AvoidanceSnapshot> timeline, NavigationAcceptanceSpec spec)
    {
        var failures = new List<string>();
        AvoidanceSnapshot start = timeline.First(snapshot => snapshot.Tick == 0);
        AvoidanceSnapshot mid = timeline.First(snapshot => snapshot.Tick == NavFinalTick / 2);
        AvoidanceSnapshot final = timeline.First(snapshot => snapshot.Tick == NavFinalTick);
        AvoidanceSnapshot peak = timeline.OrderByDescending(snapshot => snapshot.CenterCount).First();

        float team0MidAdvance = spec.Team0Direction > 0
            ? mid.Team0MedianPrimary - start.Team0MedianPrimary
            : start.Team0MedianPrimary - mid.Team0MedianPrimary;
        float team1MidAdvance = spec.Team1Direction > 0
            ? mid.Team1MedianPrimary - start.Team1MedianPrimary
            : start.Team1MedianPrimary - mid.Team1MedianPrimary;
        float team0FinalAdvance = spec.Team0Direction > 0
            ? final.Team0MedianPrimary - start.Team0MedianPrimary
            : start.Team0MedianPrimary - final.Team0MedianPrimary;
        float team1FinalAdvance = spec.Team1Direction > 0
            ? final.Team1MedianPrimary - start.Team1MedianPrimary
            : start.Team1MedianPrimary - final.Team1MedianPrimary;
        float team0FinalDirectionalFraction = GetDirectionalFraction(final.Team0Positions, spec.Team0Direction);
        float team1FinalDirectionalFraction = GetDirectionalFraction(final.Team1Positions, spec.Team1Direction);
        float finalCenterFraction = final.LiveAgents == 0 ? 0f : final.CenterCount / (float)final.LiveAgents;
        float finalCenterStoppedFraction = final.LiveAgents == 0 ? 0f : final.CenterStoppedAgents / (float)final.LiveAgents;
        bool densePeakObserved = peak.CenterCount >= Math.Max(16, (int)Math.Ceiling(final.LiveAgents * spec.FinalCenterFractionLimit));
        bool centerRelieved = !densePeakObserved || final.CenterCount <= Math.Max((int)Math.Ceiling(peak.CenterCount * spec.PeakReliefFraction), 8);

        AddAcceptanceCheck(
            spec.Team0Direction > 0 ? start.Team0MedianPrimary < -3000f : start.Team0MedianPrimary > 3000f,
            $"Team 0 should spawn on the opposite side of travel, but median primary axis was {start.Team0MedianPrimary:F0}.",
            failures);
        AddAcceptanceCheck(
            spec.Team1Direction > 0 ? start.Team1MedianPrimary < -3000f : start.Team1MedianPrimary > 3000f,
            $"Team 1 should spawn on the opposite side of travel, but median primary axis was {start.Team1MedianPrimary:F0}.",
            failures);
        AddAcceptanceCheck(team0MidAdvance > NavMidProgressMinimumCm, $"Team 0 median only advanced {team0MidAdvance:F0}cm by midpoint.", failures);
        AddAcceptanceCheck(team1MidAdvance > NavMidProgressMinimumCm, $"Team 1 median only advanced {team1MidAdvance:F0}cm by midpoint.", failures);
        AddAcceptanceCheck(team0FinalAdvance > NavFinalProgressMinimumCm, $"Team 0 median only advanced {team0FinalAdvance:F0}cm by timeout.", failures);
        AddAcceptanceCheck(team1FinalAdvance > NavFinalProgressMinimumCm, $"Team 1 median only advanced {team1FinalAdvance:F0}cm by timeout.", failures);
        if (spec.Team0FinalCrossedFractionLimit > 0f)
        {
            AddAcceptanceCheck(team0FinalDirectionalFraction >= spec.Team0FinalCrossedFractionLimit, $"Team 0 only crossed {team0FinalDirectionalFraction:P0} of the crowd by timeout.", failures);
        }

        if (spec.Team1FinalCrossedFractionLimit > 0f)
        {
            AddAcceptanceCheck(team1FinalDirectionalFraction >= spec.Team1FinalCrossedFractionLimit, $"Team 1 only crossed {team1FinalDirectionalFraction:P0} of the crowd by timeout.", failures);
        }

        AddAcceptanceCheck(final.BlockerPositions.Count >= spec.MinimumBlockers, $"Expected at least {spec.MinimumBlockers} blockers, but only saw {final.BlockerPositions.Count}.", failures);
        AddAcceptanceCheck(finalCenterFraction < spec.FinalCenterFractionLimit, $"Center box still contains {final.CenterCount}/{final.LiveAgents} agents at timeout ({finalCenterFraction:P0}).", failures);
        AddAcceptanceCheck(finalCenterStoppedFraction < spec.FinalCenterStoppedFractionLimit, $"Center box still contains {final.CenterStoppedAgents}/{final.LiveAgents} stationary agents at timeout ({finalCenterStoppedFraction:P0}).", failures);
        AddAcceptanceCheck(final.MovingAgents > (int)Math.Ceiling(final.LiveAgents * spec.MovingAgentsFractionLimit), $"Only {final.MovingAgents}/{final.LiveAgents} agents are still moving at timeout.", failures);
        AddAcceptanceCheck(centerRelieved, $"Center occupancy peaked at {peak.CenterCount} on tick {peak.Tick} and only fell to {final.CenterCount} by timeout.", failures);

        string normalizedSignature = string.Join("|", new[]
        {
            spec.Key,
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
        NavigationAcceptanceSpec spec,
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
        sb.AppendLine($"# Scenario Card: {spec.Key}");
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
        sb.AppendLine($"- Scenario: `{spec.ScenarioName}`");
        sb.AppendLine($"- Agents per team: `{spec.AgentsPerTeam}`");
        sb.AppendLine($"- Clock profile: fixed `1/60s`, timeout tick `{NavFinalTick}`");
        sb.AppendLine($"- Evidence images: {evidenceImages}");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Boot the real playable Navigation2D playground through the unified launcher bootstrap.");
        sb.AppendLine($"2. Force the `{spec.ScenarioName}` scenario and deterministic agent count through the existing playground state.");
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
            float team0Directional = GetDirectionalFraction(snapshot.Team0Positions, spec.Team0Direction);
            float team1Directional = GetDirectionalFraction(snapshot.Team1Positions, spec.Team1Direction);
            sb.AppendLine($"- [T+{snapshot.Tick:000}] {snapshot.Step} | MedianX T0={snapshot.Team0MedianPrimary:F0} T1={snapshot.Team1MedianPrimary:F0} | DirectionalCross T0={team0Directional:P0} T1={team1Directional:P0} | Center={snapshot.CenterCount} move={snapshot.CenterMovingAgents} stop={snapshot.CenterStoppedAgents} | Moving={snapshot.MovingAgents} | Tick={snapshot.TickMs:F3}ms");
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

    private static string BuildNavigationTraceJsonl(string adapterId, NavigationAcceptanceSpec spec, IReadOnlyList<AvoidanceSnapshot> timeline)
    {
        var lines = new List<string>(timeline.Count);
        for (int index = 0; index < timeline.Count; index++)
        {
            AvoidanceSnapshot snapshot = timeline[index];
            float team0Directional = GetDirectionalFraction(snapshot.Team0Positions, spec.Team0Direction);
            float team1Directional = GetDirectionalFraction(snapshot.Team1Positions, spec.Team1Direction);
            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"nav2d-{adapterId}-{index + 1:000}",
                tick = snapshot.Tick,
                step = snapshot.Step,
                scenario = spec.ScenarioName,
                scenario_key = spec.Key,
                agents_per_team = snapshot.AgentsPerTeam,
                live_agents = snapshot.LiveAgents,
                team0_median_x = Math.Round(snapshot.Team0MedianPrimary, 2),
                team1_median_x = Math.Round(snapshot.Team1MedianPrimary, 2),
                team0_crossed_fraction = Math.Round(team0Directional, 4),
                team1_crossed_fraction = Math.Round(team1Directional, 4),
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

    private static string BuildNavigationPathMermaid(NavigationAcceptanceSpec spec)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            $"    A[Boot launcher runtime for Navigation2D playground] --> B[Force {spec.ScenarioName} + deterministic agents per team]",
            "    B --> C[Run timed simulation to timeout]",
            "    C --> D[Capture multi-frame timeline + trace metrics]",
            "    D --> E{Median advance strong and timeout center jam low?}",
            "    E -->|yes| F[Write battle-report + trace + path + PNG timeline]",
            "    E -->|no| X[Fail acceptance: timeout still looks jammed]"
        }) + Environment.NewLine;
    }

    private static string BuildNavigationVisibleChecklist(NavigationAcceptanceSpec spec, IReadOnlyList<AvoidanceSnapshot> timeline, IReadOnlyList<CaptureFrame> frames)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Visible Checklist: {spec.Key}");
        sb.AppendLine();
        sb.AppendLine("- Review the PNG sequence chronologically; each later frame should show stronger approach through the conflict zone without a stationary knot surviving at timeout.");
        sb.AppendLine("- Timeout is acceptable only when the center box is not densely packed and the agents inside it are still moving.");
        sb.AppendLine("- `screens/timeline.png` is the compact strip for side-by-side adapter review.");
        sb.AppendLine();
        foreach (CaptureFrame frame in frames)
        {
            AvoidanceSnapshot snapshot = timeline.First(item => item.Tick == frame.Tick);
            float team0Directional = GetDirectionalFraction(snapshot.Team0Positions, spec.Team0Direction);
            float team1Directional = GetDirectionalFraction(snapshot.Team1Positions, spec.Team1Direction);
            sb.AppendLine($"- `{frame.FileName}`: center={frame.CenterCount}, centerStopped={frame.CenterStoppedAgents}, crossed={team0Directional:P0}/{team1Directional:P0}");
        }

        return sb.ToString();
    }

    private static string BuildNavigationSummaryJson(LauncherRecordingRequest request, NavigationAcceptanceSpec spec, AvoidanceAcceptanceResult acceptance)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = spec.Key,
            scenario_name = spec.ScenarioName,
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

    private static float GetDirectionalFraction(IReadOnlyList<Vector2> positions, int direction)
    {
        return direction >= 0
            ? Fraction(positions, point => point.X > 0f)
            : Fraction(positions, point => point.X < 0f);
    }

    private static LauncherRecordingResult RecordNavMeshBakeTerrainVisualization(LauncherRecordingRequest request)
    {
        string screensDir = Path.Combine(request.OutputDirectory, "screens");
        Directory.CreateDirectory(screensDir);

        string mapPath = EnsureTerrainBenchmarkMapPath(request);
        if (!File.Exists(mapPath))
        {
            throw new FileNotFoundException($"Terrain benchmark vertex map is missing: {mapPath}");
        }

        VertexMap map;
        using (FileStream stream = File.OpenRead(mapPath))
        {
            map = VertexMapBinary.Read(stream);
        }

        var buildConfig = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
        NavMeshAcceptanceChunk chunk = ResolveNavMeshAcceptanceChunk(map, buildConfig);
        var context = new BakePipelineContext();
        BakePipelineResult result = BakePipeline.Execute(map, chunk.ChunkX, chunk.ChunkY, tileVersion: 1, buildConfig, context);
        NavMeshTriangleAudit audit = BuildNavMeshTriangleAudit(map, chunk, context.WalkMask, buildConfig);

        int startC = chunk.StartC;
        int startR = chunk.StartR;
        int walkableTriangleCount = context.WalkMask.Walkable?.Length > 0 ? context.WalkMask.WalkableTriangleCount : result.Artifact.WalkableTriangleCount;
        int ringCount = context.ContourRings?.Count ?? 0;
        int polygonCount = context.PolygonSet.Polygons?.Length ?? 0;
        int polygonHoleCount = context.PolygonSet.Polygons?.Sum(polygon => polygon.Holes.Length) ?? 0;
        int triVertexCount = context.TriMesh.Vertices?.Length ?? result.Artifact.VertexCount;
        int triTriangleCount = context.TriMesh.Triangles?.Length > 0 ? context.TriMesh.TriangleCount : result.Artifact.TriangleCount;
        int portalCount = result.Tile?.Portals.Length ?? result.Artifact.PortalCount;
        bool usedGridFallback = context.Logs.Any(log => log.Contains("Grid mesh fallback", StringComparison.Ordinal));

        var failedChecks = new List<string>();
        AddAcceptanceCheck(walkableTriangleCount > 0, "Walk mask did not produce any walkable triangles.", failedChecks);
        AddAcceptanceCheck(ringCount > 0, "Contour extraction did not produce any rings.", failedChecks);
        AddAcceptanceCheck(polygonCount > 0, "Polygon processing did not produce any polygons.", failedChecks);
        AddAcceptanceCheck(triTriangleCount > 0, "Triangulation did not produce any triangles.", failedChecks);
        AddAcceptanceCheck(result.Success, $"Bake pipeline failed at stage {result.Artifact.Stage}: {result.Artifact.Message}", failedChecks);
        AddAcceptanceCheck(audit.BlockedTriangleCount > 0, "Cause audit did not find any blocked triangles inside the chosen showcase chunk.", failedChecks);
        AddAcceptanceCheck(portalCount > 0, "Final nav tile did not expose any border portals for runtime traversal proof.", failedChecks);

        string normalizedSignature = string.Join("|", new[]
        {
            "navmesh-bake-terrain-visualization",
            $"chunk:{chunk.ChunkX},{chunk.ChunkY}",
            $"walk:{walkableTriangleCount}",
            $"water:{audit.WaterBlockedTriangleCount}",
            $"cliff:{audit.CliffBlockedTriangleCount}",
            $"rings:{ringCount}",
            $"polys:{polygonCount}",
            $"holes:{polygonHoleCount}",
            $"verts:{triVertexCount}",
            $"tris:{triTriangleCount}",
            $"portals:{portalCount}",
            $"fallback:{(usedGridFallback ? 1 : 0)}"
        });

        NavMeshRuntimeProof runtimeProof = BuildNavMeshRuntimeProof(map, chunk, buildConfig, result.Tile, audit);
        AddAcceptanceCheck(runtimeProof.SameTilePathSuccess, "Runtime proof could not find a same-tile path across the baked movement surface.", failedChecks);
        AddAcceptanceCheck(runtimeProof.CrossPortalPathSuccess, "Runtime proof could not traverse across a baked border portal into a neighboring tile.", failedChecks);
        AddAcceptanceCheck(runtimeProof.BlockedSampleRejected, "Runtime proof did not reject a blocked sample outside the baked movement surface.", failedChecks);

        var acceptance = new NavMeshBakeAcceptanceResult(
            Success: failedChecks.Count == 0,
            Verdict: failedChecks.Count == 0
                ? $"NavMesh bake acceptance selected shoreline chunk ({chunk.ChunkX},{chunk.ChunkY}) with visible cutouts, {triTriangleCount} triangles, and {portalCount} portals."
                : "NavMesh bake visualization failed one or more structural checks.",
            FailureSummary: failedChecks.Count == 0 ? "NavMesh bake visualization checks passed." : string.Join(Environment.NewLine, failedChecks),
            FailedChecks: failedChecks,
            WalkableTriangleCount: walkableTriangleCount,
            RingCount: ringCount,
            PolygonCount: polygonCount,
            PolygonHoleCount: polygonHoleCount,
            VertexCount: triVertexCount,
            TriangleCount: triTriangleCount,
            PortalCount: portalCount,
            WaterBlockedTriangleCount: audit.WaterBlockedTriangleCount,
            HardBlockedTriangleCount: audit.HardBlockedTriangleCount,
            CliffBlockedTriangleCount: audit.CliffBlockedTriangleCount,
            StraightenedTriangleCount: audit.StraightenedTriangleCount,
            UsedGridFallback: usedGridFallback,
            SelectionReason: chunk.SelectionReason,
            NormalizedSignature: normalizedSignature);

        var stageFrames = new List<NavMeshStageFrame>
        {
            new("000_map_overview.png", "overview", "Selected showcase chunk inside the terrain benchmark, with auto-pick reason and pass card."),
            new("010_chunk_terrain.png", "terrain", "Chosen terrain slice showing shoreline, blocked markup, and height variation."),
            new("020_block_causes.png", "blocked_causes", "Per-triangle audit showing why triangles are removed: water, hard-block, cliff, or cliff straightening."),
            new("030_walk_mask.png", "walk_mask", "Final walkable domain after all terrain rules are applied."),
            new("040_contours.png", "contours", "Extracted outer and hole contours outlining valid movement ground."),
            new("050_polygons.png", "polygons", "Processed polygons and hole assignment before triangulation."),
            new("060_trimesh.png", "trimesh", "Final movement surface triangles generated from the processed polygons."),
            new("070_runtime_queries.png", "runtime", "Runtime proof: same-tile path, cross-portal traversal, and blocked-point rejection.")
        };

        WriteNavMeshOverviewImage(map, chunk, Path.Combine(screensDir, stageFrames[0].FileName));
        WriteNavMeshChunkTerrainImage(map, chunk, audit, Path.Combine(screensDir, stageFrames[1].FileName));
        WriteNavMeshCauseAuditImage(audit, chunk, Path.Combine(screensDir, stageFrames[2].FileName));
        WriteNavMeshWalkMaskImage(context.WalkMask, audit, chunk, Path.Combine(screensDir, stageFrames[3].FileName));
        WriteNavMeshContoursImage(context.WalkMask, context.ContourRings, audit, chunk, Path.Combine(screensDir, stageFrames[4].FileName));
        WriteNavMeshPolygonsImage(context.PolygonSet, audit, chunk, Path.Combine(screensDir, stageFrames[5].FileName));
        WriteNavMeshTriMeshImage(context.WalkMask, context.TriMesh, audit, chunk, Path.Combine(screensDir, stageFrames[6].FileName));
        WriteNavMeshRuntimeProofImage(result.Tile, runtimeProof, Path.Combine(screensDir, stageFrames[7].FileName));

        if (result.Tile != null)
        {
            stageFrames.Add(new NavMeshStageFrame("080_nav_tile.png", "nav_tile", "Final nav tile with labeled border portals and triangle coverage."));
            WriteNavMeshTileImage(result.Tile, Path.Combine(screensDir, stageFrames[^1].FileName));
        }

        WriteNavMeshGallerySheet("NavMesh Bake Visualization", stageFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");
        string artifactReportPath = Path.Combine(request.OutputDirectory, "artifact-report.txt");

        File.WriteAllText(battleReportPath, BuildNavMeshBakeBattleReport(request, mapPath, chunk, buildConfig, acceptance, runtimeProof, stageFrames));
        File.WriteAllText(tracePath, BuildNavMeshBakeTraceJsonl(acceptance, result, context));
        File.WriteAllText(pathPath, BuildNavMeshBakePathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildNavMeshBakeVisibleChecklist(stageFrames));
        File.WriteAllText(summaryPath, BuildNavMeshBakeSummaryJson(request, mapPath, chunk, acceptance, runtimeProof));
        File.WriteAllText(artifactReportPath, BakeArtifactBuilder.CreateFromContext(
            context,
            new NavTileId(chunk.ChunkX, chunk.ChunkY, 0),
            1,
            result.Artifact.Stage,
            result.Artifact.ErrorCode,
            result.Artifact.Message,
            portalCount).GenerateReport());

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
            stageFrames.Select(frame => Path.Combine(screensDir, frame.FileName)).Append(Path.Combine(screensDir, "timeline.png")).ToList(),
            acceptance.NormalizedSignature);
    }

    private static string ResolveTerrainBenchmarkRootPath(LauncherRecordingRequest request)
    {
        LauncherPlannedMod? terrainMod = request.Plan.Mods.FirstOrDefault(mod => string.Equals(mod.Id, "TerrainBenchmarkMod", StringComparison.OrdinalIgnoreCase));
        return terrainMod?.RootPath ?? Path.Combine(request.RepoRoot, "mods", "TerrainBenchmarkMod");
    }

    private static string EnsureTerrainBenchmarkMapPath(LauncherRecordingRequest request)
    {
        string terrainRoot = ResolveTerrainBenchmarkRootPath(request);
        string mapPath = Path.Combine(terrainRoot, "assets", "Data", "Maps", "terrain_bench.vtxm");
        TerrainBenchmarkMapGenerator.EnsureGenerated(mapPath);
        return mapPath;
    }

    private static NavMeshAcceptanceChunk ResolveNavMeshAcceptanceChunk(VertexMap map, NavBuildConfig buildConfig)
    {
        if (NavMeshAcceptanceChunkX >= 0 && NavMeshAcceptanceChunkX < map.WidthInChunks &&
            NavMeshAcceptanceChunkY >= 0 && NavMeshAcceptanceChunkY < map.HeightInChunks)
        {
            NavMeshChunkHeuristics forced = AnalyzeNavMeshChunk(map, NavMeshAcceptanceChunkX, NavMeshAcceptanceChunkY);
            return TryEvaluateNavMeshAcceptanceChunk(map, buildConfig, forced, out NavMeshAcceptanceChunk forcedChunk)
                ? forcedChunk with { SelectionReason = $"Manual acceptance chunk override {forced.ChunkX},{forced.ChunkY}." }
                : CreateFallbackNavMeshAcceptanceChunk(map, forced, $"Manual acceptance chunk override {forced.ChunkX},{forced.ChunkY} could not produce a richer showcase, but was kept as requested.");
        }

        var candidates = new List<NavMeshChunkHeuristics>(map.WidthInChunks * map.HeightInChunks);
        for (int chunkY = 0; chunkY < map.HeightInChunks; chunkY++)
        {
            for (int chunkX = 0; chunkX < map.WidthInChunks; chunkX++)
            {
                NavMeshChunkHeuristics heuristics = AnalyzeNavMeshChunk(map, chunkX, chunkY);
                if (heuristics.WaterFraction <= 0d || heuristics.WaterFraction >= 0.98d)
                {
                    continue;
                }

                if (heuristics.HeightMax - heuristics.HeightMin < 2 && heuristics.ShorelineTransitions < 24)
                {
                    continue;
                }

                candidates.Add(heuristics);
            }
        }

        foreach (NavMeshChunkHeuristics heuristics in candidates.OrderByDescending(item => item.HeuristicScore).Take(NavMeshAcceptanceCandidateCount))
        {
            if (TryEvaluateNavMeshAcceptanceChunk(map, buildConfig, heuristics, out NavMeshAcceptanceChunk selected))
            {
                return selected;
            }
        }

        NavMeshChunkHeuristics fallback = AnalyzeNavMeshChunk(map, map.WidthInChunks / 2, map.HeightInChunks / 2);
        return CreateFallbackNavMeshAcceptanceChunk(map, fallback, "Auto search did not find a richer shoreline chunk, so the recorder fell back to the center tile.");
    }

    private static NavMeshChunkHeuristics AnalyzeNavMeshChunk(VertexMap map, int chunkX, int chunkY)
    {
        int startC = chunkX * VertexChunk.ChunkSize;
        int startR = chunkY * VertexChunk.ChunkSize;
        int waterCells = 0;
        int blockedCells = 0;
        int shorelineTransitions = 0;
        int minHeight = byte.MaxValue;
        int maxHeight = byte.MinValue;
        var previousWetRow = new bool[VertexChunk.ChunkSize];

        for (int localR = 0; localR < VertexChunk.ChunkSize; localR++)
        {
            var wetRow = new bool[VertexChunk.ChunkSize];
            for (int localC = 0; localC < VertexChunk.ChunkSize; localC++)
            {
                int globalC = startC + localC;
                int globalR = startR + localR;
                byte height = map.GetHeight(globalC, globalR);
                byte water = map.GetWaterHeight(globalC, globalR);
                bool isWet = water > height;
                wetRow[localC] = isWet;
                if (isWet)
                {
                    waterCells++;
                }

                if (map.IsBlocked(globalC, globalR))
                {
                    blockedCells++;
                }

                minHeight = Math.Min(minHeight, height);
                maxHeight = Math.Max(maxHeight, height);

                if (localC > 0 && wetRow[localC - 1] != isWet)
                {
                    shorelineTransitions++;
                }

                if (localR > 0 && previousWetRow[localC] != isWet)
                {
                    shorelineTransitions++;
                }
            }

            previousWetRow = wetRow;
        }

        double totalCells = VertexChunk.ChunkSize * VertexChunk.ChunkSize;
        double waterFraction = waterCells / totalCells;
        double blockedFraction = blockedCells / totalCells;
        int heightRange = maxHeight - minHeight;
        double shorelineScore = Math.Min(1d, shorelineTransitions / 128d);
        double waterMixScore = 1d - Math.Min(1d, Math.Abs(waterFraction - 0.25d) / 0.25d);
        double blockedMixScore = blockedFraction <= 0d ? 0d : 1d - Math.Min(1d, Math.Abs(blockedFraction - 0.08d) / 0.08d);
        double heightScore = Math.Min(1d, heightRange / 4d);
        double heuristicScore = waterMixScore * 40d + blockedMixScore * 8d + heightScore * 20d + shorelineScore * 28d;

        return new NavMeshChunkHeuristics(
            chunkX,
            chunkY,
            waterFraction,
            blockedFraction,
            minHeight,
            maxHeight,
            shorelineTransitions,
            heuristicScore);
    }

    private static bool TryEvaluateNavMeshAcceptanceChunk(
        VertexMap map,
        NavBuildConfig buildConfig,
        NavMeshChunkHeuristics heuristics,
        out NavMeshAcceptanceChunk chunk)
    {
        var context = new BakePipelineContext();
        BakePipelineResult result = BakePipeline.Execute(map, heuristics.ChunkX, heuristics.ChunkY, tileVersion: 1, buildConfig, context);
        if (!result.Success || result.Tile == null)
        {
            chunk = default!;
            return false;
        }

        var evaluatedChunk = new NavMeshAcceptanceChunk(
            heuristics.ChunkX,
            heuristics.ChunkY,
            heuristics.ChunkX * VertexChunk.ChunkSize,
            heuristics.ChunkY * VertexChunk.ChunkSize,
            "",
            heuristics.WaterFraction,
            heuristics.BlockedFraction,
            heuristics.HeightMin,
            heuristics.HeightMax,
            heuristics.ShorelineTransitions,
            heuristics.HeuristicScore,
            0d);

        NavMeshTriangleAudit audit = BuildNavMeshTriangleAudit(map, evaluatedChunk, context.WalkMask, buildConfig);
        int ringCount = context.ContourRings?.Count ?? 0;
        int polygonCount = context.PolygonSet.Polygons?.Length ?? 0;
        int polygonHoleCount = context.PolygonSet.Polygons?.Sum(polygon => polygon.Holes.Length) ?? 0;
        int triangleCount = context.TriMesh.TriangleCount;
        int portalCount = result.Tile.Portals.Length;
        bool usedGridFallback = context.Logs.Any(log => log.Contains("Grid mesh fallback", StringComparison.Ordinal));
        double blockedFraction = audit.BlockedTriangleCount / (double)(VertexChunk.ChunkSize * VertexChunk.ChunkSize * 2);
        double walkableFraction = context.WalkMask.WalkableTriangleCount / (double)(VertexChunk.ChunkSize * VertexChunk.ChunkSize * 2);
        double walkableBalance = 1d - Math.Min(1d, Math.Abs(walkableFraction - 0.55d) / 0.55d);
        double evidenceScore =
            heuristics.HeuristicScore +
            walkableBalance * 18d +
            Math.Min(18d, ringCount * 8d) +
            Math.Min(18d, polygonCount * 10d) +
            Math.Min(12d, polygonHoleCount * 12d) +
            Math.Min(40d, triangleCount * 1.5d) +
            Math.Min(18d, portalCount * 8d) +
            Math.Min(16d, blockedFraction * 32d) -
            (usedGridFallback ? 8d : 0d);

        string selectionReason =
            $"Auto-picked chunk {heuristics.ChunkX},{heuristics.ChunkY}: water={heuristics.WaterFraction:P0}, blockedVertices={heuristics.BlockedFraction:P0}, shorelineTransitions={heuristics.ShorelineTransitions}, polygons={polygonCount}, holes={polygonHoleCount}, triangles={triangleCount}, portals={portalCount}.";

        chunk = evaluatedChunk with
        {
            EvidenceScore = evidenceScore,
            SelectionReason = selectionReason
        };

        return context.WalkMask.WalkableTriangleCount > 0 &&
               audit.BlockedTriangleCount > 0 &&
               ringCount > 0 &&
               polygonCount > 0 &&
               triangleCount >= 6 &&
               portalCount > 0;
    }

    private static NavMeshAcceptanceChunk CreateFallbackNavMeshAcceptanceChunk(VertexMap map, NavMeshChunkHeuristics heuristics, string reason)
    {
        return new NavMeshAcceptanceChunk(
            heuristics.ChunkX,
            heuristics.ChunkY,
            heuristics.ChunkX * VertexChunk.ChunkSize,
            heuristics.ChunkY * VertexChunk.ChunkSize,
            reason,
            heuristics.WaterFraction,
            heuristics.BlockedFraction,
            heuristics.HeightMin,
            heuristics.HeightMax,
            heuristics.ShorelineTransitions,
            heuristics.HeuristicScore,
            heuristics.HeuristicScore);
    }

    private static string BuildNavMeshBakeBattleReport(
        LauncherRecordingRequest request,
        string mapPath,
        NavMeshAcceptanceChunk chunk,
        NavBuildConfig buildConfig,
        NavMeshBakeAcceptanceResult acceptance,
        NavMeshRuntimeProof runtimeProof,
        IReadOnlyList<NavMeshStageFrame> stageFrames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: navmesh-bake-terrain-visualization");
        sb.AppendLine();
        sb.AppendLine("## Plain-Language Pass Card");
        sb.AppendLine("- Pass if land stays walkable, water and cliffs are visibly cut out, the outline matches the final mesh, border openings become portals, and the runtime query crosses a portal while rejecting a blocked sample.");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: verify that movement space matches the terrain in an obvious way, instead of trusting a black-box navmesh export.");
        sb.AppendLine("- Engine-user goal: inspect the same shoreline chunk across terrain, cause audit, contours, polygon cleanup, triangulation, portals, and runtime path queries.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Root mod: `TerrainBenchmarkMod`");
        sb.AppendLine($"- Input map: `{mapPath}`");
        sb.AppendLine($"- Chunk: `{chunk.ChunkX},{chunk.ChunkY}`");
        sb.AppendLine($"- Auto-pick reason: {chunk.SelectionReason}");
        sb.AppendLine($"- Build config: heightScale=`{buildConfig.HeightScaleMeters:F1}`, minWalkableUpDot=`{buildConfig.MinWalkableUpDot:F1}`, cliffThreshold=`{buildConfig.CliffHeightThreshold}`");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Load the generated terrain benchmark vertex map from the clean worktree.");
        sb.AppendLine("2. Auto-select the most legible shoreline chunk instead of hard-pinning the center tile.");
        sb.AppendLine("3. Export terrain, blocked-cause audit, final walk mask, contours, polygons, triangulated mesh, runtime query proof, and final portalized tile.");
        sb.AppendLine("4. Fail if any structural bake stage collapses, if blocked reasons are not visible, or if runtime queries do not prove the baked result is usable.");
        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        foreach (string failedCheck in acceptance.FailedChecks)
        {
            sb.AppendLine($"- failed-check: {failedCheck}");
        }

        sb.AppendLine($"- walkable triangles: `{acceptance.WalkableTriangleCount}`");
        sb.AppendLine($"- blocked by water: `{acceptance.WaterBlockedTriangleCount}`");
        sb.AppendLine($"- blocked by hard obstacle: `{acceptance.HardBlockedTriangleCount}`");
        sb.AppendLine($"- blocked by cliff: `{acceptance.CliffBlockedTriangleCount}`");
        sb.AppendLine($"- blocked by straightening: `{acceptance.StraightenedTriangleCount}`");
        sb.AppendLine($"- contour rings: `{acceptance.RingCount}`");
        sb.AppendLine($"- polygons: `{acceptance.PolygonCount}`");
        sb.AppendLine($"- polygon holes: `{acceptance.PolygonHoleCount}`");
        sb.AppendLine($"- mesh vertices: `{acceptance.VertexCount}`");
        sb.AppendLine($"- mesh triangles: `{acceptance.TriangleCount}`");
        sb.AppendLine($"- border portals: `{acceptance.PortalCount}`");
        sb.AppendLine($"- used grid fallback: `{acceptance.UsedGridFallback}`");
        sb.AppendLine($"- runtime same-tile path: `{runtimeProof.SameTilePathSuccess}` with `{runtimeProof.SameTilePathPointCount}` points");
        sb.AppendLine($"- runtime cross-portal path: `{runtimeProof.CrossPortalPathSuccess}` with `{runtimeProof.CrossPortalPathPointCount}` points");
        sb.AppendLine($"- blocked sample rejected: `{runtimeProof.BlockedSampleRejected}`");
        sb.AppendLine($"- normalized signature: `{acceptance.NormalizedSignature}`");
        sb.AppendLine();
        sb.AppendLine("## Visual Evidence");
        foreach (NavMeshStageFrame frame in stageFrames)
        {
            sb.AppendLine($"- `screens/{frame.FileName}`: {frame.Caption}");
        }

        sb.AppendLine("- `screens/timeline.png`: compact stage gallery for review.");
        return sb.ToString();
    }

    private static string BuildNavMeshBakeTraceJsonl(NavMeshBakeAcceptanceResult acceptance, BakePipelineResult result, BakePipelineContext context)
    {
        var entries = new[]
        {
            new { step = "walk_mask", metric = acceptance.WalkableTriangleCount, status = context.WalkMask.Walkable?.Length > 0 ? "done" : "missing" },
            new { step = "contours", metric = acceptance.RingCount, status = context.ContourRings?.Count > 0 ? "done" : "missing" },
            new { step = "polygons", metric = acceptance.PolygonCount, status = context.PolygonSet.Polygons?.Length > 0 ? "done" : "missing" },
            new { step = "triangulate", metric = acceptance.TriangleCount, status = context.TriMesh.Triangles?.Length > 0 ? "done" : "missing" },
            new { step = "serialize", metric = acceptance.PortalCount, status = result.Success ? "done" : "failed" }
        };

        var lines = new List<string>(entries.Length);
        for (int index = 0; index < entries.Length; index++)
        {
            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"navmesh-bake-{index + 1:000}",
                step = entries[index].step,
                metric = entries[index].metric,
                stage = result.Artifact.Stage.ToString(),
                error_code = result.Artifact.ErrorCode.ToString(),
                status = entries[index].status
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildNavMeshBakePathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Load TerrainBenchmark vertex map] --> B[Scan chunks and auto-pick the most legible shoreline tile]",
            "    B --> C[Audit blocked causes: water, hard block, cliff, straightening]",
            "    C --> D[Build final walk mask and extract contour rings]",
            "    D --> E[Process polygons and holes]",
            "    E --> F[Triangulate movement surface and build final nav tile]",
            "    F --> G[Run runtime proof queries: same tile, cross portal, blocked rejection]",
            "    G --> H[Export screenshots, report, summary, and video]"
        }) + Environment.NewLine;
    }

    private static string BuildNavMeshBakeVisibleChecklist(IReadOnlyList<NavMeshStageFrame> stageFrames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: navmesh-bake-terrain-visualization");
        sb.AppendLine();
        sb.AppendLine("- `000_map_overview.png` should explain in plain language why this chunk was chosen, not just highlight the center tile.");
        sb.AppendLine("- `010_chunk_terrain.png` should make the shoreline and height bands visually obvious.");
        sb.AppendLine("- `020_block_causes.png` should let a reviewer distinguish water, hard obstacles, cliff rejection, and straightened cutouts at a glance.");
        sb.AppendLine("- `030_walk_mask.png` should show a believable movement footprint instead of a full green rectangle.");
        sb.AppendLine("- `040_contours.png` should show bright closed loops that match the visible movement footprint.");
        sb.AppendLine("- `050_polygons.png` should prove polygon cleanup and hole assignment before triangulation.");
        sb.AppendLine("- `060_trimesh.png` should show a non-trivial final movement surface, with enough triangles to inspect.");
        sb.AppendLine("- `070_runtime_queries.png` should prove three things: a same-tile path, a portal-crossing path, and blocked-point rejection.");
        sb.AppendLine("- `080_nav_tile.png` should show the final tile portals lining up with the visible boundary.");
        sb.AppendLine();
        foreach (NavMeshStageFrame frame in stageFrames)
        {
            sb.AppendLine($"- `{frame.FileName}`: {frame.Caption}");
        }

        sb.AppendLine("- `timeline.png`: review all stages side by side.");
        return sb.ToString();
    }

    private static string BuildNavMeshBakeSummaryJson(
        LauncherRecordingRequest request,
        string mapPath,
        NavMeshAcceptanceChunk chunk,
        NavMeshBakeAcceptanceResult acceptance,
        NavMeshRuntimeProof runtimeProof)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = "navmesh-bake-terrain-visualization",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            input_map = mapPath,
            chunk_x = chunk.ChunkX,
            chunk_y = chunk.ChunkY,
            selection_reason = chunk.SelectionReason,
            water_fraction = Math.Round(chunk.WaterFraction, 4),
            blocked_vertex_fraction = Math.Round(chunk.BlockedFraction, 4),
            shoreline_transitions = chunk.ShorelineTransitions,
            walkable_triangles = acceptance.WalkableTriangleCount,
            water_blocked_triangles = acceptance.WaterBlockedTriangleCount,
            hard_blocked_triangles = acceptance.HardBlockedTriangleCount,
            cliff_blocked_triangles = acceptance.CliffBlockedTriangleCount,
            straightened_triangles = acceptance.StraightenedTriangleCount,
            contour_rings = acceptance.RingCount,
            polygons = acceptance.PolygonCount,
            polygon_holes = acceptance.PolygonHoleCount,
            mesh_vertices = acceptance.VertexCount,
            mesh_triangles = acceptance.TriangleCount,
            border_portals = acceptance.PortalCount,
            used_grid_fallback = acceptance.UsedGridFallback,
            runtime_same_tile_path = runtimeProof.SameTilePathSuccess,
            runtime_cross_portal_path = runtimeProof.CrossPortalPathSuccess,
            runtime_blocked_rejected = runtimeProof.BlockedSampleRejected,
            normalized_signature = acceptance.NormalizedSignature
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static NavMeshTriangleAudit BuildNavMeshTriangleAudit(VertexMap map, NavMeshAcceptanceChunk chunk, TriWalkMask walkMask, NavBuildConfig config)
    {
        var causes = new NavMeshTriangleCause[VertexChunk.ChunkSize * VertexChunk.ChunkSize * 2];
        int walkable = 0;
        int water = 0;
        int hardBlocked = 0;
        int cliff = 0;
        int straightened = 0;
        Vector2 blockedSampleLocalCm = default;
        bool blockedSampleFound = false;

        for (int localR = 0; localR < VertexChunk.ChunkSize; localR++)
        {
            for (int localC = 0; localC < VertexChunk.ChunkSize; localC++)
            {
                int globalC = chunk.StartC + localC;
                int globalR = chunk.StartR + localR;
                bool isOddRow = (globalR & 1) == 1;

                for (int triIndex = 0; triIndex < 2; triIndex++)
                {
                    int arrayIndex = (localR * VertexChunk.ChunkSize + localC) * 2 + triIndex;
                    WalkMaskBuilder.GetTriangleVertexOffsets(localC, localR, triIndex, isOddRow, out var va, out var vb, out var vc);
                    int aC = chunk.StartC + localC + va.dc;
                    int aR = chunk.StartR + localR + va.dr;
                    int bC = chunk.StartC + localC + vb.dc;
                    int bR = chunk.StartR + localR + vb.dr;
                    int cC = chunk.StartC + localC + vc.dc;
                    int cR = chunk.StartR + localR + vc.dr;

                    byte aH = map.GetHeight(aC, aR);
                    byte bH = map.GetHeight(bC, bR);
                    byte cH = map.GetHeight(cC, cR);
                    byte aW = map.GetWaterHeight(aC, aR);
                    byte bW = map.GetWaterHeight(bC, bR);
                    byte cW = map.GetWaterHeight(cC, cR);
                    bool aBlocked = map.IsBlocked(aC, aR);
                    bool bBlocked = map.IsBlocked(bC, bR);
                    bool cBlocked = map.IsBlocked(cC, cR);

                    NavMeshTriangleCause cause;
                    if (walkMask.IsWalkable(localC, localR, triIndex))
                    {
                        cause = NavMeshTriangleCause.Walkable;
                        walkable++;
                    }
                    else if (aBlocked || bBlocked || cBlocked)
                    {
                        cause = NavMeshTriangleCause.HardBlocked;
                        hardBlocked++;
                    }
                    else if (aW > aH || bW > bH || cW > cH)
                    {
                        cause = NavMeshTriangleCause.Water;
                        water++;
                    }
                    else if (Math.Max(aH, Math.Max(bH, cH)) - Math.Min(aH, Math.Min(bH, cH)) > config.CliffHeightThreshold)
                    {
                        cause = NavMeshTriangleCause.Cliff;
                        cliff++;
                    }
                    else
                    {
                        cause = NavMeshTriangleCause.Straightened;
                        straightened++;
                    }

                    if (!blockedSampleFound && cause != NavMeshTriangleCause.Walkable)
                    {
                        blockedSampleLocalCm = AverageTriangleLocalCm(chunk.StartC, chunk.StartR, aC, aR, bC, bR, cC, cR);
                        blockedSampleFound = true;
                    }

                    causes[arrayIndex] = cause;
                }
            }
        }

        return new NavMeshTriangleAudit(
            causes,
            walkable,
            water,
            hardBlocked,
            cliff,
            straightened,
            blockedSampleLocalCm,
            blockedSampleFound);
    }

    private static NavMeshRuntimeProof BuildNavMeshRuntimeProof(VertexMap map, NavMeshAcceptanceChunk chunk, NavBuildConfig buildConfig, NavTile? selectedTile, NavMeshTriangleAudit audit)
    {
        if (selectedTile == null)
        {
            return new NavMeshRuntimeProof(false, 0, Array.Empty<Vector2>(), Vector2.Zero, Vector2.Zero, false, 0, Array.Empty<Vector2>(), null, Vector2.Zero, Vector2.Zero, audit.BlockedSampleFound && !TryPointInsideNavTile(selectedTile, audit.BlockedSampleLocalCm), audit.BlockedSampleLocalCm);
        }

        Vector2[] centroids = GetTriangleCentroids(selectedTile);
        Vector2 sameStartLocal = Vector2.Zero;
        Vector2 sameGoalLocal = Vector2.Zero;
        if (centroids.Length > 1)
        {
            FindFarthestPair(centroids, out sameStartLocal, out sameGoalLocal);
        }

        bool sameTilePathSuccess = false;
        Vector2[] sameTilePathLocal = Array.Empty<Vector2>();
        if (centroids.Length > 1)
        {
            var sameTileStore = CreateNavQueryService([selectedTile]);
            NavPathResult sameTilePath = sameTileStore.TryFindPath(
                selectedTile.OriginXcm + (int)MathF.Round(sameStartLocal.X),
                selectedTile.OriginZcm + (int)MathF.Round(sameStartLocal.Y),
                selectedTile.OriginXcm + (int)MathF.Round(sameGoalLocal.X),
                selectedTile.OriginZcm + (int)MathF.Round(sameGoalLocal.Y));

            if (sameTilePath.Status == NavPathStatus.Ok)
            {
                sameTilePathSuccess = true;
                sameTilePathLocal = sameTilePath.PathXcm.Zip(sameTilePath.PathZcm, (x, z) => new Vector2(x - selectedTile.OriginXcm, z - selectedTile.OriginZcm)).ToArray();
            }
        }

        bool crossPortalPathSuccess = false;
        Vector2[] crossPortalPathWorld = Array.Empty<Vector2>();
        NavTile? neighborTile = null;
        Vector2 crossPortalStartWorld = Vector2.Zero;
        Vector2 crossPortalGoalWorld = Vector2.Zero;
        if (selectedTile.Portals.Length > 0 && centroids.Length > 0)
        {
            foreach (NavBorderPortal chosenPortal in selectedTile.Portals
                         .OrderByDescending(portal => GetPortalLengthSquared(portal)))
            {
                NavTileId neighborTileId = ResolveNeighborTileId(selectedTile.TileId, chosenPortal.Side);
                if (neighborTileId.ChunkX < 0 ||
                    neighborTileId.ChunkY < 0 ||
                    neighborTileId.ChunkX >= map.WidthInChunks ||
                    neighborTileId.ChunkY >= map.HeightInChunks)
                {
                    continue;
                }

                var neighborContext = new BakePipelineContext();
                BakePipelineResult neighborBake = BakePipeline.Execute(map, neighborTileId.ChunkX, neighborTileId.ChunkY, tileVersion: 1, buildConfig, neighborContext);
                if (!neighborBake.Success || neighborBake.Tile == null)
                {
                    continue;
                }

                NavTile candidateNeighborTile = neighborBake.Tile;
                Vector2[] neighborCentroids = GetTriangleCentroids(candidateNeighborTile);
                if (neighborCentroids.Length == 0)
                {
                    continue;
                }

                Vector2 portalMidWorld = GetPortalMidWorld(selectedTile, chosenPortal);
                Vector2 startLocal = centroids
                    .OrderByDescending(point => Vector2.DistanceSquared(point + new Vector2(selectedTile.OriginXcm, selectedTile.OriginZcm), portalMidWorld))
                    .First();
                Vector2 goalLocal = neighborCentroids
                    .OrderByDescending(point => Vector2.DistanceSquared(point + new Vector2(candidateNeighborTile.OriginXcm, candidateNeighborTile.OriginZcm), portalMidWorld))
                    .First();

                Vector2 candidateStartWorld = startLocal + new Vector2(selectedTile.OriginXcm, selectedTile.OriginZcm);
                Vector2 candidateGoalWorld = goalLocal + new Vector2(candidateNeighborTile.OriginXcm, candidateNeighborTile.OriginZcm);
                var crossTileQuery = CreateNavQueryService([selectedTile, candidateNeighborTile]);
                NavPathResult crossTilePath = crossTileQuery.TryFindPath(
                    (int)MathF.Round(candidateStartWorld.X),
                    (int)MathF.Round(candidateStartWorld.Y),
                    (int)MathF.Round(candidateGoalWorld.X),
                    (int)MathF.Round(candidateGoalWorld.Y));

                if (neighborTile == null)
                {
                    neighborTile = candidateNeighborTile;
                    crossPortalStartWorld = candidateStartWorld;
                    crossPortalGoalWorld = candidateGoalWorld;
                }

                if (crossTilePath.Status != NavPathStatus.Ok)
                {
                    continue;
                }

                Vector2[] candidatePathWorld = crossTilePath.PathXcm
                    .Zip(crossTilePath.PathZcm, (x, z) => new Vector2(x, z))
                    .ToArray();
                bool touchesPrimaryTile = candidatePathWorld.Any(point => TryPointInsideNavTile(selectedTile, point - new Vector2(selectedTile.OriginXcm, selectedTile.OriginZcm)));
                bool touchesNeighborTile = candidatePathWorld.Any(point => TryPointInsideNavTile(candidateNeighborTile, point - new Vector2(candidateNeighborTile.OriginXcm, candidateNeighborTile.OriginZcm)));
                if (!touchesPrimaryTile || !touchesNeighborTile)
                {
                    continue;
                }

                neighborTile = candidateNeighborTile;
                crossPortalStartWorld = candidateStartWorld;
                crossPortalGoalWorld = candidateGoalWorld;
                crossPortalPathWorld = candidatePathWorld;
                crossPortalPathSuccess = candidatePathWorld.Length >= 2;
                break;
            }
        }

        bool blockedSampleRejected = audit.BlockedSampleFound && !TryPointInsideNavTile(selectedTile, audit.BlockedSampleLocalCm);
        return new NavMeshRuntimeProof(
            sameTilePathSuccess,
            sameTilePathLocal.Length,
            sameTilePathLocal,
            sameStartLocal,
            sameGoalLocal,
            crossPortalPathSuccess,
            crossPortalPathWorld.Length,
            crossPortalPathWorld,
            neighborTile,
            crossPortalStartWorld,
            crossPortalGoalWorld,
            blockedSampleRejected,
            audit.BlockedSampleLocalCm);
    }

    private static void WriteNavMeshOverviewImage(VertexMap map, NavMeshAcceptanceChunk chunk, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(11, 15, 22));

        float margin = 90f;
        float cellWidth = (NavMeshImageWidth - margin * 2f) / Math.Max(1, map.WidthInChunks);
        float cellHeight = (NavMeshImageHeight - margin * 2f) / Math.Max(1, map.HeightInChunks);

        for (int mapChunkY = 0; mapChunkY < map.HeightInChunks; mapChunkY++)
        {
            for (int mapChunkX = 0; mapChunkX < map.WidthInChunks; mapChunkX++)
            {
                int sampleC = mapChunkX * VertexChunk.ChunkSize + VertexChunk.ChunkSize / 2;
                int sampleR = mapChunkY * VertexChunk.ChunkSize + VertexChunk.ChunkSize / 2;
                byte height = map.GetHeight(sampleC, sampleR);
                byte water = map.GetWaterHeight(sampleC, sampleR);
                using var fillPaint = new SKPaint { Color = ResolveTerrainColor(height, water, blocked: false), Style = SKPaintStyle.Fill, IsAntialias = true };
                canvas.DrawRect(new SKRect(
                    margin + mapChunkX * cellWidth,
                    margin + mapChunkY * cellHeight,
                    margin + (mapChunkX + 1) * cellWidth,
                    margin + (mapChunkY + 1) * cellHeight), fillPaint);
            }
        }

        using var fillHighlight = new SKPaint { Color = new SKColor(255, 196, 92, 60), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var outlineHighlight = new SKPaint { Color = new SKColor(255, 196, 92), Style = SKPaintStyle.Stroke, StrokeWidth = 5f, IsAntialias = true };
        var selectedRect = new SKRect(
            margin + chunk.ChunkX * cellWidth,
            margin + chunk.ChunkY * cellHeight,
            margin + (chunk.ChunkX + 1) * cellWidth,
            margin + (chunk.ChunkY + 1) * cellHeight);
        canvas.DrawRect(selectedRect, fillHighlight);
        canvas.DrawRect(selectedRect, outlineHighlight);

        DrawNavMeshStageHeader(canvas, "Chosen Shoreline Chunk", "Pass if this highlighted chunk obviously mixes land, cutouts, and border exits.", $"Chunk={chunk.ChunkX},{chunk.ChunkY}  water={chunk.WaterFraction:P0}  blockedVertices={chunk.BlockedFraction:P0}  shorelineTransitions={chunk.ShorelineTransitions}");
        DrawNavMeshCallout(canvas, new SKRect(1050f, 120f, 1540f, 320f), chunk.SelectionReason);
        SaveNavMeshSurface(surface, path);
    }

    private static void WriteNavMeshChunkTerrainImage(VertexMap map, NavMeshAcceptanceChunk chunk, NavMeshTriangleAudit audit, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshGrid(canvas, chunk.StartC, chunk.StartR);

        for (int localR = 0; localR < VertexChunk.ChunkSize; localR++)
        {
            for (int localC = 0; localC < VertexChunk.ChunkSize; localC++)
            {
                int globalC = chunk.StartC + localC;
                int globalR = chunk.StartR + localR;
                byte height = map.GetHeight(globalC, globalR);
                byte water = map.GetWaterHeight(globalC, globalR);
                bool blocked = map.IsBlocked(globalC, globalR);
                using var fillPaint = new SKPaint { Color = ResolveTerrainColor(height, water, blocked), Style = SKPaintStyle.Fill, IsAntialias = true };
                canvas.DrawRect(ToNavMeshGridRect(globalC, globalR, globalC + 1f, globalR + 1f, chunk.StartC, chunk.StartR), fillPaint);
            }
        }

        DrawNavMeshStageHeader(canvas, "Terrain Slice", "Player view: water and blocked terrain should already explain where movement gets cut out.", $"Height={chunk.HeightMin}-{chunk.HeightMax}  walkable={audit.WalkableTriangleCount}  blocked={audit.BlockedTriangleCount}");
        DrawNavMeshLegend(canvas, 1120f, 118f, [
            ("Land / high ground", new SKColor(112, 150, 72)),
            ("Submerged water", new SKColor(48, 110, 180)),
            ("Hard-block markup", new SKColor(176, 72, 72))
        ]);
        SaveNavMeshSurface(surface, path);
    }

    private static void WriteNavMeshCauseAuditImage(NavMeshTriangleAudit audit, NavMeshAcceptanceChunk chunk, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshGrid(canvas, chunk.StartC, chunk.StartR);
        DrawNavMeshAuditTriangles(canvas, audit, chunk.StartC, chunk.StartR);
        DrawNavMeshStageHeader(canvas, "Why Triangles Are Removed", "Pass if each cutout can be explained by one visible cause color.", $"water={audit.WaterBlockedTriangleCount}  hard={audit.HardBlockedTriangleCount}  cliff={audit.CliffBlockedTriangleCount}  straightened={audit.StraightenedTriangleCount}");
        DrawNavMeshLegend(canvas, 1040f, 118f, [
            ("Walkable", ResolveAuditColor(NavMeshTriangleCause.Walkable)),
            ("Blocked by water", ResolveAuditColor(NavMeshTriangleCause.Water)),
            ("Blocked by obstacle", ResolveAuditColor(NavMeshTriangleCause.HardBlocked)),
            ("Blocked by cliff", ResolveAuditColor(NavMeshTriangleCause.Cliff)),
            ("Straightened edge", ResolveAuditColor(NavMeshTriangleCause.Straightened))
        ]);
        SaveNavMeshSurface(surface, path);
    }

    private static void WriteNavMeshWalkMaskImage(TriWalkMask walkMask, NavMeshTriangleAudit audit, NavMeshAcceptanceChunk chunk, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshGrid(canvas, chunk.StartC, chunk.StartR);
        using var walkablePaint = new SKPaint { Color = new SKColor(88, 220, 136, 196), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var blockedPaint = new SKPaint { Color = new SKColor(116, 52, 60, 148), Style = SKPaintStyle.Fill, IsAntialias = true };

        for (int localR = 0; localR < VertexChunk.ChunkSize; localR++)
        {
            for (int localC = 0; localC < VertexChunk.ChunkSize; localC++)
            {
                int globalR = chunk.StartR + localR;
                bool isOddRow = (globalR & 1) == 1;
                DrawWalkMaskTriangle(canvas, localC, localR, 0, isOddRow, walkMask.IsWalkable(localC, localR, 0) ? walkablePaint : blockedPaint, chunk.StartC, chunk.StartR);
                DrawWalkMaskTriangle(canvas, localC, localR, 1, isOddRow, walkMask.IsWalkable(localC, localR, 1) ? walkablePaint : blockedPaint, chunk.StartC, chunk.StartR);
            }
        }

        DrawNavMeshStageHeader(canvas, "Where Units May Walk", "Pass if the footprint follows the shoreline and cliff cuts instead of filling the chunk.", $"walkable={walkMask.WalkableTriangleCount}  blocked={audit.BlockedTriangleCount}");
        SaveNavMeshSurface(surface, path);
    }

    private static void WriteNavMeshContoursImage(TriWalkMask walkMask, List<IntRing>? contourRings, NavMeshTriangleAudit audit, NavMeshAcceptanceChunk chunk, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshGrid(canvas, chunk.StartC, chunk.StartR);
        DrawNavMeshAuditTriangles(canvas, audit, chunk.StartC, chunk.StartR, 0.25f);
        using var outerPaint = new SKPaint { Color = new SKColor(94, 230, 162), Style = SKPaintStyle.Stroke, StrokeWidth = 5f, IsAntialias = true };
        using var holePaint = new SKPaint { Color = new SKColor(255, 175, 94), Style = SKPaintStyle.Stroke, StrokeWidth = 5f, IsAntialias = true };

        if (contourRings != null)
        {
            foreach (IntRing ring in contourRings)
            {
                if (ring.Points.Length == 0)
                {
                    continue;
                }

                using var ringPath = new SKPath();
                ringPath.MoveTo(ToNavMeshGridScreen(ring.Points[0].X, ring.Points[0].Y, chunk.StartC, chunk.StartR));
                for (int index = 1; index < ring.Points.Length; index++)
                {
                    ringPath.LineTo(ToNavMeshGridScreen(ring.Points[index].X, ring.Points[index].Y, chunk.StartC, chunk.StartR));
                }

                ringPath.Close();
                canvas.DrawPath(ringPath, ring.IsOuter ? outerPaint : holePaint);
            }
        }

        DrawNavMeshStageHeader(canvas, "Outline Of Valid Ground", "Pass if the bright contour loops hug the same footprint shown in the walk mask.", $"rings={contourRings?.Count ?? 0}");
        SaveNavMeshSurface(surface, path);
    }

    private static void WriteNavMeshPolygonsImage(ValidPolygonSet polygonSet, NavMeshTriangleAudit audit, NavMeshAcceptanceChunk chunk, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshGrid(canvas, chunk.StartC, chunk.StartR);
        DrawNavMeshAuditTriangles(canvas, audit, chunk.StartC, chunk.StartR, 0.18f);
        using var outerFill = new SKPaint { Color = new SKColor(84, 156, 255, 72), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var outerStroke = new SKPaint { Color = new SKColor(196, 226, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 4f, IsAntialias = true };
        using var holeStroke = new SKPaint { Color = new SKColor(255, 198, 92), Style = SKPaintStyle.Stroke, StrokeWidth = 4f, IsAntialias = true };

        for (int polygonIndex = 0; polygonIndex < polygonSet.Polygons.Length; polygonIndex++)
        {
            Polygon polygon = polygonSet.Polygons[polygonIndex];
            using var polygonPath = new SKPath();
            if (polygon.Outer.Length > 0)
            {
                polygonPath.MoveTo(ToNavMeshGridScreen(polygon.Outer[0].X, polygon.Outer[0].Y, chunk.StartC, chunk.StartR));
                for (int index = 1; index < polygon.Outer.Length; index++)
                {
                    polygonPath.LineTo(ToNavMeshGridScreen(polygon.Outer[index].X, polygon.Outer[index].Y, chunk.StartC, chunk.StartR));
                }

                polygonPath.Close();
                canvas.DrawPath(polygonPath, outerFill);
                canvas.DrawPath(polygonPath, outerStroke);
            }

            foreach (IntPoint[] hole in polygon.Holes)
            {
                if (hole.Length == 0)
                {
                    continue;
                }

                using var holePath = new SKPath();
                holePath.MoveTo(ToNavMeshGridScreen(hole[0].X, hole[0].Y, chunk.StartC, chunk.StartR));
                for (int index = 1; index < hole.Length; index++)
                {
                    holePath.LineTo(ToNavMeshGridScreen(hole[index].X, hole[index].Y, chunk.StartC, chunk.StartR));
                }

                holePath.Close();
                canvas.DrawPath(holePath, holeStroke);
            }
        }

        DrawNavMeshStageHeader(canvas, "Processed Polygons", "Engine-user view: this stage must prove contour cleanup and hole assignment before triangulation.", $"polygons={polygonSet.Polygons.Length}  warnings={polygonSet.Warnings.Length}");
        SaveNavMeshSurface(surface, path);
    }

    private static void WriteNavMeshTriMeshImage(TriWalkMask walkMask, TriMesh triMesh, NavMeshTriangleAudit audit, NavMeshAcceptanceChunk chunk, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshGrid(canvas, chunk.StartC, chunk.StartR);
        DrawNavMeshAuditTriangles(canvas, audit, chunk.StartC, chunk.StartR, 0.16f);
        using var fillPaint = new SKPaint { Color = new SKColor(84, 156, 255, 70), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var strokePaint = new SKPaint { Color = new SKColor(214, 232, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 3.2f, IsAntialias = true };
        using var vertexPaint = new SKPaint { Color = new SKColor(255, 235, 164), Style = SKPaintStyle.Fill, IsAntialias = true };

        if (triMesh.Triangles?.Length > 0 && triMesh.Vertices?.Length > 0)
        {
            for (int triangleIndex = 0; triangleIndex < triMesh.TriangleCount; triangleIndex++)
            {
                int i0 = triMesh.Triangles[triangleIndex * 3 + 0];
                int i1 = triMesh.Triangles[triangleIndex * 3 + 1];
                int i2 = triMesh.Triangles[triangleIndex * 3 + 2];
                using var trianglePath = new SKPath();
                trianglePath.MoveTo(ToNavMeshGridScreen(triMesh.Vertices[i0].X, triMesh.Vertices[i0].Y, chunk.StartC, chunk.StartR));
                trianglePath.LineTo(ToNavMeshGridScreen(triMesh.Vertices[i1].X, triMesh.Vertices[i1].Y, chunk.StartC, chunk.StartR));
                trianglePath.LineTo(ToNavMeshGridScreen(triMesh.Vertices[i2].X, triMesh.Vertices[i2].Y, chunk.StartC, chunk.StartR));
                trianglePath.Close();
                canvas.DrawPath(trianglePath, fillPaint);
                canvas.DrawPath(trianglePath, strokePaint);
            }

            foreach (Vector2 vertex in triMesh.Vertices)
            {
                SKPoint point = ToNavMeshGridScreen(vertex.X, vertex.Y, chunk.StartC, chunk.StartR);
                canvas.DrawCircle(point.X, point.Y, 3f, vertexPaint);
            }
        }

        DrawNavMeshStageHeader(canvas, "Final Movement Surface", "Pass if the final triangles visibly match the contour and are dense enough to inspect.", $"vertices={triMesh.Vertices?.Length ?? 0}  triangles={triMesh.TriangleCount}");
        SaveNavMeshSurface(surface, path);
    }

    private static void WriteNavMeshRuntimeProofImage(NavTile tile, NavMeshRuntimeProof runtimeProof, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshStageHeader(canvas, "Runtime Query Proof", "Pass if one query stays inside the tile, one crosses a portal, and a blocked sample is rejected.", $"sameTile={runtimeProof.SameTilePathSuccess}  crossPortal={runtimeProof.CrossPortalPathSuccess}  blockedRejected={runtimeProof.BlockedSampleRejected}");

        SKRect leftRect = new(40f, 118f, 770f, 860f);
        SKRect rightRect = new(830f, 118f, 1560f, 860f);
        DrawNavMeshPanel(canvas, leftRect, "Same-tile path + blocked sample");
        DrawTileMeshPanel(canvas, leftRect, tile, runtimeProof.SameTilePathLocal, runtimeProof.SameTileStartLocal, runtimeProof.SameTileGoalLocal, runtimeProof.BlockedSampleLocalCm, runtimeProof.BlockedSampleRejected);

        DrawNavMeshPanel(canvas, rightRect, runtimeProof.NeighborTile != null ? "Portal crossing to neighbor tile" : "Portal crossing to neighbor tile (unavailable)");
        if (runtimeProof.NeighborTile != null)
        {
            DrawWorldTilesPanel(canvas, rightRect, tile, runtimeProof.NeighborTile, runtimeProof.CrossPortalPathWorld, runtimeProof.CrossPortalStartWorld, runtimeProof.CrossPortalGoalWorld);
        }
        else
        {
            DrawNavMeshCallout(canvas, new SKRect(rightRect.Left + 28f, rightRect.Top + 80f, rightRect.Right - 28f, rightRect.Top + 220f), "No neighboring baked tile was available for the portal traversal proof.");
        }

        SaveNavMeshSurface(surface, path);
    }

    private static void WriteNavMeshOverviewImage(VertexMap map, int chunkX, int chunkY, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(11, 15, 22));

        float margin = 90f;
        float cellWidth = (NavMeshImageWidth - margin * 2f) / Math.Max(1, map.WidthInChunks);
        float cellHeight = (NavMeshImageHeight - margin * 2f) / Math.Max(1, map.HeightInChunks);
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 24f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(186, 194, 208), IsAntialias = true, TextSize = 18f };
        using var highlightPaint = new SKPaint { Color = new SKColor(255, 196, 92), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };

        for (int mapChunkY = 0; mapChunkY < map.HeightInChunks; mapChunkY++)
        {
            for (int mapChunkX = 0; mapChunkX < map.WidthInChunks; mapChunkX++)
            {
                int sampleC = mapChunkX * VertexChunk.ChunkSize + VertexChunk.ChunkSize / 2;
                int sampleR = mapChunkY * VertexChunk.ChunkSize + VertexChunk.ChunkSize / 2;
                byte height = map.GetHeight(sampleC, sampleR);
                byte water = map.GetWaterHeight(sampleC, sampleR);
                using var fillPaint = new SKPaint { Color = ResolveTerrainColor(height, water, blocked: false), Style = SKPaintStyle.Fill, IsAntialias = true };
                var rect = new SKRect(
                    margin + mapChunkX * cellWidth,
                    margin + mapChunkY * cellHeight,
                    margin + (mapChunkX + 1) * cellWidth,
                    margin + (mapChunkY + 1) * cellHeight);
                canvas.DrawRect(rect, fillPaint);
            }
        }

        var selectedRect = new SKRect(
            margin + chunkX * cellWidth,
            margin + chunkY * cellHeight,
            margin + (chunkX + 1) * cellWidth,
            margin + (chunkY + 1) * cellHeight);
        canvas.DrawRect(selectedRect, highlightPaint);
        canvas.DrawText("Terrain Benchmark | NavMesh acceptance chunk", 28, 38, labelPaint);
        canvas.DrawText($"Chunk={chunkX},{chunkY}  Map={map.WidthInChunks}x{map.HeightInChunks} chunks", 28, 68, minorTextPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteNavMeshChunkTerrainImage(VertexMap map, int chunkX, int chunkY, int startC, int startR, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshGrid(canvas, startC, startR);

        for (int localR = 0; localR < VertexChunk.ChunkSize; localR++)
        {
            for (int localC = 0; localC < VertexChunk.ChunkSize; localC++)
            {
                int globalC = startC + localC;
                int globalR = startR + localR;
                byte height = map.GetHeight(globalC, globalR);
                byte water = map.GetWaterHeight(globalC, globalR);
                bool blocked = map.IsBlocked(globalC, globalR);
                using var fillPaint = new SKPaint { Color = ResolveTerrainColor(height, water, blocked), Style = SKPaintStyle.Fill, IsAntialias = true };
                canvas.DrawRect(ToNavMeshGridRect(globalC, globalR, globalC + 1f, globalR + 1f, startC, startR), fillPaint);
            }
        }

        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 24f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(186, 194, 208), IsAntialias = true, TextSize = 18f };
        canvas.DrawText("Chunk Terrain Detail", 28, 38, labelPaint);
        canvas.DrawText($"Chunk={chunkX},{chunkY}  GlobalCellOrigin={startC},{startR}", 28, 68, minorTextPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteNavMeshWalkMaskImage(TriWalkMask walkMask, int startC, int startR, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshGrid(canvas, startC, startR);
        using var walkablePaint = new SKPaint { Color = new SKColor(88, 220, 136, 180), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var blockedPaint = new SKPaint { Color = new SKColor(128, 54, 60, 160), Style = SKPaintStyle.Fill, IsAntialias = true };

        bool hasData = walkMask.Walkable?.Length > 0;
        if (hasData)
        {
            for (int localR = 0; localR < VertexChunk.ChunkSize; localR++)
            {
                for (int localC = 0; localC < VertexChunk.ChunkSize; localC++)
                {
                    int globalR = startR + localR;
                    bool isOddRow = (globalR & 1) == 1;
                    DrawWalkMaskTriangle(canvas, localC, localR, triIndex: 0, isOddRow, walkMask.IsWalkable(localC, localR, 0) ? walkablePaint : blockedPaint, startC, startR);
                    DrawWalkMaskTriangle(canvas, localC, localR, triIndex: 1, isOddRow, walkMask.IsWalkable(localC, localR, 1) ? walkablePaint : blockedPaint, startC, startR);
                }
            }
        }

        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 24f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(186, 194, 208), IsAntialias = true, TextSize = 18f };
        canvas.DrawText("Walk Mask", 28, 38, labelPaint);
        canvas.DrawText($"WalkableTriangles={(hasData ? walkMask.WalkableTriangleCount : 0)}", 28, 68, minorTextPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteNavMeshContoursImage(TriWalkMask walkMask, List<IntRing>? contourRings, int startC, int startR, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshGrid(canvas, startC, startR);
        using var outerPaint = new SKPaint { Color = new SKColor(94, 230, 162), Style = SKPaintStyle.Stroke, StrokeWidth = 3f, IsAntialias = true };
        using var holePaint = new SKPaint { Color = new SKColor(255, 149, 94), Style = SKPaintStyle.Stroke, StrokeWidth = 3f, IsAntialias = true };

        if (walkMask.Walkable?.Length > 0)
        {
            using var backgroundPaint = new SKPaint { Color = new SKColor(72, 110, 92, 42), Style = SKPaintStyle.Fill, IsAntialias = true };
            for (int localR = 0; localR < VertexChunk.ChunkSize; localR++)
            {
                for (int localC = 0; localC < VertexChunk.ChunkSize; localC++)
                {
                    int globalR = startR + localR;
                    bool isOddRow = (globalR & 1) == 1;
                    if (walkMask.IsWalkable(localC, localR, 0))
                    {
                        DrawWalkMaskTriangle(canvas, localC, localR, 0, isOddRow, backgroundPaint, startC, startR);
                    }

                    if (walkMask.IsWalkable(localC, localR, 1))
                    {
                        DrawWalkMaskTriangle(canvas, localC, localR, 1, isOddRow, backgroundPaint, startC, startR);
                    }
                }
            }
        }

        if (contourRings != null)
        {
            foreach (IntRing ring in contourRings)
            {
                if (ring.Points.Length == 0)
                {
                    continue;
                }

                using var ringPath = new SKPath();
                SKPoint start = ToNavMeshGridScreen(ring.Points[0].X, ring.Points[0].Y, startC, startR);
                ringPath.MoveTo(start);
                for (int index = 1; index < ring.Points.Length; index++)
                {
                    ringPath.LineTo(ToNavMeshGridScreen(ring.Points[index].X, ring.Points[index].Y, startC, startR));
                }

                ringPath.Close();
                canvas.DrawPath(ringPath, ring.IsOuter ? outerPaint : holePaint);
            }
        }

        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 24f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(186, 194, 208), IsAntialias = true, TextSize = 18f };
        canvas.DrawText("Contour Extraction", 28, 38, labelPaint);
        canvas.DrawText($"Rings={contourRings?.Count ?? 0}", 28, 68, minorTextPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteNavMeshTriMeshImage(TriWalkMask walkMask, TriMesh triMesh, int startC, int startR, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        DrawNavMeshGrid(canvas, startC, startR);
        using var fillPaint = new SKPaint { Color = new SKColor(84, 156, 255, 68), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var strokePaint = new SKPaint { Color = new SKColor(196, 222, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true };
        using var vertexPaint = new SKPaint { Color = new SKColor(255, 235, 164), Style = SKPaintStyle.Fill, IsAntialias = true };

        if (triMesh.Triangles?.Length > 0 && triMesh.Vertices?.Length > 0)
        {
            for (int triangleIndex = 0; triangleIndex < triMesh.TriangleCount; triangleIndex++)
            {
                int i0 = triMesh.Triangles[triangleIndex * 3 + 0];
                int i1 = triMesh.Triangles[triangleIndex * 3 + 1];
                int i2 = triMesh.Triangles[triangleIndex * 3 + 2];
                using var trianglePath = new SKPath();
                trianglePath.MoveTo(ToNavMeshGridScreen(triMesh.Vertices[i0].X, triMesh.Vertices[i0].Y, startC, startR));
                trianglePath.LineTo(ToNavMeshGridScreen(triMesh.Vertices[i1].X, triMesh.Vertices[i1].Y, startC, startR));
                trianglePath.LineTo(ToNavMeshGridScreen(triMesh.Vertices[i2].X, triMesh.Vertices[i2].Y, startC, startR));
                trianglePath.Close();
                canvas.DrawPath(trianglePath, fillPaint);
                canvas.DrawPath(trianglePath, strokePaint);
            }

            foreach (Vector2 vertex in triMesh.Vertices)
            {
                SKPoint point = ToNavMeshGridScreen(vertex.X, vertex.Y, startC, startR);
                canvas.DrawCircle(point.X, point.Y, 2.4f, vertexPaint);
            }
        }

        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 24f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(186, 194, 208), IsAntialias = true, TextSize = 18f };
        canvas.DrawText("Triangulated Mesh", 28, 38, labelPaint);
        canvas.DrawText($"Vertices={triMesh.Vertices?.Length ?? 0}  Triangles={triMesh.Triangles?.Length / 3 ?? 0}", 28, 68, minorTextPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteNavMeshTileImage(NavTile tile, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(NavMeshImageWidth, NavMeshImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 20));
        using var fillPaint = new SKPaint { Color = new SKColor(88, 196, 255, 52), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var strokePaint = new SKPaint { Color = new SKColor(196, 226, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true };
        using var portalPaint = new SKPaint { Color = new SKColor(255, 198, 92), Style = SKPaintStyle.Stroke, StrokeWidth = 6f, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 24f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(186, 194, 208), IsAntialias = true, TextSize = 18f };

        int minX = tile.VertexXcm.Min();
        int maxX = tile.VertexXcm.Max();
        int minZ = tile.VertexZcm.Min();
        int maxZ = tile.VertexZcm.Max();

        for (int triangleIndex = 0; triangleIndex < tile.TriangleCount; triangleIndex++)
        {
            int a = tile.TriA[triangleIndex];
            int b = tile.TriB[triangleIndex];
            int c = tile.TriC[triangleIndex];
            using var trianglePath = new SKPath();
            trianglePath.MoveTo(ToNavTileScreen(tile.VertexXcm[a], tile.VertexZcm[a], minX, maxX, minZ, maxZ));
            trianglePath.LineTo(ToNavTileScreen(tile.VertexXcm[b], tile.VertexZcm[b], minX, maxX, minZ, maxZ));
            trianglePath.LineTo(ToNavTileScreen(tile.VertexXcm[c], tile.VertexZcm[c], minX, maxX, minZ, maxZ));
            trianglePath.Close();
            canvas.DrawPath(trianglePath, fillPaint);
            canvas.DrawPath(trianglePath, strokePaint);
        }

        foreach (NavBorderPortal portal in tile.Portals)
        {
            SKPoint left = ToNavTileScreen(portal.LeftXcm, portal.LeftZcm, minX, maxX, minZ, maxZ);
            SKPoint right = ToNavTileScreen(portal.RightXcm, portal.RightZcm, minX, maxX, minZ, maxZ);
            canvas.DrawLine(left, right, portalPaint);
        }

        canvas.DrawText("Final Nav Tile", 28, 38, labelPaint);
        canvas.DrawText($"Vertices={tile.VertexCount}  Triangles={tile.TriangleCount}  Portals={tile.Portals.Length}", 28, 68, minorTextPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static Vector2 AverageTriangleLocalCm(int startC, int startR, int aC, int aR, int bC, int bR, int cC, int cR)
    {
        Vector2 a = ToTileLocalPointCm(startC, startR, aC, aR);
        Vector2 b = ToTileLocalPointCm(startC, startR, bC, bR);
        Vector2 c = ToTileLocalPointCm(startC, startR, cC, cR);
        return (a + b + c) / 3f;
    }

    private static Vector2 ToTileLocalPointCm(int startC, int startR, int globalC, int globalR)
    {
        float originXm = startC * HexCoordinates.HexWidth;
        float originZm = startR * HexCoordinates.RowSpacing;
        float worldXm = HexCoordinates.HexWidth * (globalC + 0.5f * (globalR & 1)) - originXm;
        float worldZm = HexCoordinates.RowSpacing * globalR - originZm;
        return new Vector2(worldXm * 100f, worldZm * 100f);
    }

    private static NavTileId ResolveNeighborTileId(NavTileId source, NavPortalSide side)
    {
        return side switch
        {
            NavPortalSide.West => new NavTileId(source.ChunkX - 1, source.ChunkY, source.Layer),
            NavPortalSide.East => new NavTileId(source.ChunkX + 1, source.ChunkY, source.Layer),
            NavPortalSide.North => new NavTileId(source.ChunkX, source.ChunkY - 1, source.Layer),
            NavPortalSide.South => new NavTileId(source.ChunkX, source.ChunkY + 1, source.Layer),
            _ => source
        };
    }

    private static Vector2 GetPortalMidWorld(NavTile tile, NavBorderPortal portal)
    {
        return new Vector2(
            (portal.LeftXcm + portal.RightXcm) * 0.5f + tile.OriginXcm,
            (portal.LeftZcm + portal.RightZcm) * 0.5f + tile.OriginZcm);
    }

    private static float GetPortalLengthSquared(NavBorderPortal portal)
    {
        float dx = portal.RightXcm - portal.LeftXcm;
        float dz = portal.RightZcm - portal.LeftZcm;
        return dx * dx + dz * dz;
    }

    private static NavQueryService CreateNavQueryService(IReadOnlyList<NavTile> tiles)
    {
        var tileBytes = new Dictionary<NavTileId, byte[]>(tiles.Count);
        foreach (NavTile tile in tiles)
        {
            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, tile);
            tileBytes[tile.TileId] = ms.ToArray();
        }

        var store = new NavTileStore(id => new MemoryStream(tileBytes[id], writable: false));
        return new NavQueryService(store);
    }

    private static bool TryPointInsideNavTile(NavTile tile, Vector2 localPoint)
    {
        for (int triangleIndex = 0; triangleIndex < tile.TriangleCount; triangleIndex++)
        {
            int a = tile.TriA[triangleIndex];
            int b = tile.TriB[triangleIndex];
            int c = tile.TriC[triangleIndex];
            Vector2 va = new(tile.VertexXcm[a], tile.VertexZcm[a]);
            Vector2 vb = new(tile.VertexXcm[b], tile.VertexZcm[b]);
            Vector2 vc = new(tile.VertexXcm[c], tile.VertexZcm[c]);
            if (IsPointInsideTriangle(localPoint, va, vb, vc))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s1 = Cross2D(b - a, p - a);
        float s2 = Cross2D(c - b, p - b);
        float s3 = Cross2D(a - c, p - c);
        bool hasNegative = s1 < 0f || s2 < 0f || s3 < 0f;
        bool hasPositive = s1 > 0f || s2 > 0f || s3 > 0f;
        return !(hasNegative && hasPositive);
    }

    private static float Cross2D(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static Vector2[] GetTriangleCentroids(NavTile tile)
    {
        var centroids = new Vector2[tile.TriangleCount];
        for (int triangleIndex = 0; triangleIndex < tile.TriangleCount; triangleIndex++)
        {
            int a = tile.TriA[triangleIndex];
            int b = tile.TriB[triangleIndex];
            int c = tile.TriC[triangleIndex];
            centroids[triangleIndex] = new Vector2(
                (tile.VertexXcm[a] + tile.VertexXcm[b] + tile.VertexXcm[c]) / 3f,
                (tile.VertexZcm[a] + tile.VertexZcm[b] + tile.VertexZcm[c]) / 3f);
        }

        return centroids;
    }

    private static void FindFarthestPair(IReadOnlyList<Vector2> points, out Vector2 first, out Vector2 second)
    {
        first = points[0];
        second = points[^1];
        float bestDistanceSq = -1f;
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                float distanceSq = Vector2.DistanceSquared(points[i], points[j]);
                if (distanceSq > bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    first = points[i];
                    second = points[j];
                }
            }
        }
    }

    private static void DrawNavMeshStageHeader(SKCanvas canvas, string title, string subtitle, string metrics)
    {
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28f };
        using var subtitlePaint = new SKPaint { Color = new SKColor(214, 222, 232), IsAntialias = true, TextSize = 18f };
        using var metricsPaint = new SKPaint { Color = new SKColor(255, 213, 122), IsAntialias = true, TextSize = 17f };
        canvas.DrawText(title, 28f, 42f, titlePaint);
        canvas.DrawText(subtitle, 28f, 72f, subtitlePaint);
        canvas.DrawText(metrics, 28f, 98f, metricsPaint);
    }

    private static void DrawNavMeshCallout(SKCanvas canvas, SKRect rect, string text)
    {
        using var fillPaint = new SKPaint { Color = new SKColor(14, 21, 32, 238), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var strokePaint = new SKPaint { Color = new SKColor(84, 156, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true };
        using var textPaint = new SKPaint { Color = new SKColor(230, 236, 242), IsAntialias = true, TextSize = 17f };
        canvas.DrawRoundRect(rect, 18f, 18f, fillPaint);
        canvas.DrawRoundRect(rect, 18f, 18f, strokePaint);
        DrawWrappedText(canvas, text, rect.Left + 18f, rect.Top + 28f, rect.Width - 36f, textPaint, 24f);
    }

    private static void DrawWrappedText(SKCanvas canvas, string text, float x, float y, float maxWidth, SKPaint paint, float lineHeight)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        float cursorY = y;
        foreach (string word in words)
        {
            string candidate = line.Length == 0 ? word : $"{line} {word}";
            if (paint.MeasureText(candidate) > maxWidth && line.Length > 0)
            {
                canvas.DrawText(line.ToString(), x, cursorY, paint);
                line.Clear();
                line.Append(word);
                cursorY += lineHeight;
            }
            else
            {
                line.Clear();
                line.Append(candidate);
            }
        }

        if (line.Length > 0)
        {
            canvas.DrawText(line.ToString(), x, cursorY, paint);
        }
    }

    private static void DrawNavMeshLegend(SKCanvas canvas, float x, float y, (string Label, SKColor Color)[] items)
    {
        using var textPaint = new SKPaint { Color = new SKColor(230, 236, 242), IsAntialias = true, TextSize = 16f };
        using var strokePaint = new SKPaint { Color = new SKColor(72, 84, 98), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        float boxHeight = items.Length * 30f + 26f;
        float boxWidth = 430f;
        var rect = new SKRect(x, y, x + boxWidth, y + boxHeight);
        DrawNavMeshCallout(canvas, rect, "");
        canvas.DrawRoundRect(rect, 18f, 18f, strokePaint);
        using var fillPaint = new SKPaint { Color = new SKColor(16, 23, 35, 236), Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(rect, 18f, 18f, fillPaint);
        for (int index = 0; index < items.Length; index++)
        {
            using var swatchPaint = new SKPaint { Color = items[index].Color, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(x + 18f, y + 16f + index * 30f, x + 38f, y + 34f + index * 30f), 5f, 5f, swatchPaint);
            canvas.DrawText(items[index].Label, x + 52f, y + 31f + index * 30f, textPaint);
        }
    }

    private static void DrawNavMeshAuditTriangles(SKCanvas canvas, NavMeshTriangleAudit audit, int startC, int startR, float alphaScale = 1f)
    {
        for (int localR = 0; localR < VertexChunk.ChunkSize; localR++)
        {
            for (int localC = 0; localC < VertexChunk.ChunkSize; localC++)
            {
                int globalR = startR + localR;
                bool isOddRow = (globalR & 1) == 1;
                for (int triIndex = 0; triIndex < 2; triIndex++)
                {
                    using var fillPaint = new SKPaint { Color = ApplyAlpha(ResolveAuditColor(audit.GetCause(localC, localR, triIndex)), alphaScale), Style = SKPaintStyle.Fill, IsAntialias = true };
                    DrawWalkMaskTriangle(canvas, localC, localR, triIndex, isOddRow, fillPaint, startC, startR);
                }
            }
        }
    }

    private static SKColor ResolveAuditColor(NavMeshTriangleCause cause)
    {
        return cause switch
        {
            NavMeshTriangleCause.Walkable => new SKColor(88, 220, 136, 220),
            NavMeshTriangleCause.Water => new SKColor(74, 156, 255, 220),
            NavMeshTriangleCause.HardBlocked => new SKColor(220, 92, 92, 220),
            NavMeshTriangleCause.Cliff => new SKColor(255, 176, 94, 220),
            NavMeshTriangleCause.Straightened => new SKColor(184, 132, 255, 220),
            _ => new SKColor(94, 104, 120, 220)
        };
    }

    private static SKColor ApplyAlpha(SKColor color, float alphaScale)
    {
        return color.WithAlpha((byte)Math.Clamp(color.Alpha * alphaScale, 0f, 255f));
    }

    private static void DrawNavMeshPanel(SKCanvas canvas, SKRect rect, string title)
    {
        using var fillPaint = new SKPaint { Color = new SKColor(14, 21, 32, 224), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var strokePaint = new SKPaint { Color = new SKColor(68, 82, 100), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true };
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 20f };
        canvas.DrawRoundRect(rect, 18f, 18f, fillPaint);
        canvas.DrawRoundRect(rect, 18f, 18f, strokePaint);
        canvas.DrawText(title, rect.Left + 20f, rect.Top + 34f, titlePaint);
    }

    private static void DrawTileMeshPanel(SKCanvas canvas, SKRect rect, NavTile tile, IReadOnlyList<Vector2> pathLocal, Vector2 startLocal, Vector2 goalLocal, Vector2 blockedSampleLocal, bool blockedRejected)
    {
        float minX = tile.VertexXcm.Min();
        float maxX = tile.VertexXcm.Max();
        float minZ = tile.VertexZcm.Min();
        float maxZ = tile.VertexZcm.Max();
        using var fillPaint = new SKPaint { Color = new SKColor(84, 156, 255, 52), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var strokePaint = new SKPaint { Color = new SKColor(196, 226, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true };
        using var pathPaint = new SKPaint { Color = new SKColor(88, 220, 136), Style = SKPaintStyle.Stroke, StrokeWidth = 4f, IsAntialias = true };
        using var rejectedPaint = new SKPaint { Color = new SKColor(255, 92, 92), Style = SKPaintStyle.Stroke, StrokeWidth = 4f, IsAntialias = true };
        using var pointPaint = new SKPaint { Color = new SKColor(255, 213, 122), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var textPaint = new SKPaint { Color = new SKColor(214, 222, 232), IsAntialias = true, TextSize = 16f };

        foreach ((SKPoint a, SKPoint b, SKPoint c) in EnumerateTileTriangles(tile, rect, minX, maxX, minZ, maxZ, useWorld: false))
        {
            using var trianglePath = new SKPath();
            trianglePath.MoveTo(a);
            trianglePath.LineTo(b);
            trianglePath.LineTo(c);
            trianglePath.Close();
            canvas.DrawPath(trianglePath, fillPaint);
            canvas.DrawPath(trianglePath, strokePaint);
        }

        DrawPolyline(canvas, pathLocal.Select(point => ToRectPoint(rect, minX, maxX, minZ, maxZ, point.X, point.Y)).ToArray(), pathPaint);
        SKPoint startPoint = ToRectPoint(rect, minX, maxX, minZ, maxZ, startLocal.X, startLocal.Y);
        SKPoint goalPoint = ToRectPoint(rect, minX, maxX, minZ, maxZ, goalLocal.X, goalLocal.Y);
        canvas.DrawCircle(startPoint, 5f, pointPaint);
        canvas.DrawCircle(goalPoint, 5f, pointPaint);
        SKPoint blockedPoint = ToRectPoint(rect, minX, maxX, minZ, maxZ, blockedSampleLocal.X, blockedSampleLocal.Y);
        canvas.DrawLine(blockedPoint.X - 8f, blockedPoint.Y - 8f, blockedPoint.X + 8f, blockedPoint.Y + 8f, rejectedPaint);
        canvas.DrawLine(blockedPoint.X - 8f, blockedPoint.Y + 8f, blockedPoint.X + 8f, blockedPoint.Y - 8f, rejectedPaint);
        canvas.DrawText($"Blocked sample rejected={blockedRejected}", rect.Left + 20f, rect.Bottom - 18f, textPaint);
    }

    private static void DrawWorldTilesPanel(SKCanvas canvas, SKRect rect, NavTile primaryTile, NavTile neighborTile, IReadOnlyList<Vector2> pathWorld, Vector2 startWorld, Vector2 goalWorld)
    {
        float minX = Math.Min(primaryTile.VertexXcm.Min() + primaryTile.OriginXcm, neighborTile.VertexXcm.Min() + neighborTile.OriginXcm);
        float maxX = Math.Max(primaryTile.VertexXcm.Max() + primaryTile.OriginXcm, neighborTile.VertexXcm.Max() + neighborTile.OriginXcm);
        float minZ = Math.Min(primaryTile.VertexZcm.Min() + primaryTile.OriginZcm, neighborTile.VertexZcm.Min() + neighborTile.OriginZcm);
        float maxZ = Math.Max(primaryTile.VertexZcm.Max() + primaryTile.OriginZcm, neighborTile.VertexZcm.Max() + neighborTile.OriginZcm);
        using var primaryFill = new SKPaint { Color = new SKColor(84, 156, 255, 56), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var neighborFill = new SKPaint { Color = new SKColor(88, 220, 136, 52), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var strokePaint = new SKPaint { Color = new SKColor(214, 232, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true };
        using var pathPaint = new SKPaint { Color = new SKColor(255, 213, 122), Style = SKPaintStyle.Stroke, StrokeWidth = 4f, IsAntialias = true };
        using var pointPaint = new SKPaint { Color = new SKColor(255, 213, 122), Style = SKPaintStyle.Fill, IsAntialias = true };

        DrawWorldTile(canvas, rect, primaryTile, minX, maxX, minZ, maxZ, primaryFill, strokePaint);
        DrawWorldTile(canvas, rect, neighborTile, minX, maxX, minZ, maxZ, neighborFill, strokePaint);
        DrawPolyline(canvas, pathWorld.Select(point => ToRectPoint(rect, minX, maxX, minZ, maxZ, point.X, point.Y)).ToArray(), pathPaint);
        canvas.DrawCircle(ToRectPoint(rect, minX, maxX, minZ, maxZ, startWorld.X, startWorld.Y), 5f, pointPaint);
        canvas.DrawCircle(ToRectPoint(rect, minX, maxX, minZ, maxZ, goalWorld.X, goalWorld.Y), 5f, pointPaint);
    }

    private static IEnumerable<(SKPoint A, SKPoint B, SKPoint C)> EnumerateTileTriangles(NavTile tile, SKRect rect, float minX, float maxX, float minZ, float maxZ, bool useWorld)
    {
        for (int triangleIndex = 0; triangleIndex < tile.TriangleCount; triangleIndex++)
        {
            int a = tile.TriA[triangleIndex];
            int b = tile.TriB[triangleIndex];
            int c = tile.TriC[triangleIndex];
            float offsetX = useWorld ? tile.OriginXcm : 0f;
            float offsetZ = useWorld ? tile.OriginZcm : 0f;
            yield return (
                ToRectPoint(rect, minX, maxX, minZ, maxZ, tile.VertexXcm[a] + offsetX, tile.VertexZcm[a] + offsetZ),
                ToRectPoint(rect, minX, maxX, minZ, maxZ, tile.VertexXcm[b] + offsetX, tile.VertexZcm[b] + offsetZ),
                ToRectPoint(rect, minX, maxX, minZ, maxZ, tile.VertexXcm[c] + offsetX, tile.VertexZcm[c] + offsetZ));
        }
    }

    private static void DrawWorldTile(SKCanvas canvas, SKRect rect, NavTile tile, float minX, float maxX, float minZ, float maxZ, SKPaint fillPaint, SKPaint strokePaint)
    {
        foreach ((SKPoint a, SKPoint b, SKPoint c) in EnumerateTileTriangles(tile, rect, minX, maxX, minZ, maxZ, useWorld: true))
        {
            using var trianglePath = new SKPath();
            trianglePath.MoveTo(a);
            trianglePath.LineTo(b);
            trianglePath.LineTo(c);
            trianglePath.Close();
            canvas.DrawPath(trianglePath, fillPaint);
            canvas.DrawPath(trianglePath, strokePaint);
        }
    }

    private static void DrawPolyline(SKCanvas canvas, IReadOnlyList<SKPoint> points, SKPaint paint)
    {
        if (points.Count < 2)
        {
            return;
        }

        using var path = new SKPath();
        path.MoveTo(points[0]);
        for (int index = 1; index < points.Count; index++)
        {
            path.LineTo(points[index]);
        }

        canvas.DrawPath(path, paint);
    }

    private static SKPoint ToRectPoint(SKRect rect, float minX, float maxX, float minZ, float maxZ, float x, float z)
    {
        float padding = 28f;
        float safeWidth = Math.Max(1f, maxX - minX);
        float safeHeight = Math.Max(1f, maxZ - minZ);
        float px = rect.Left + padding + ((x - minX) / safeWidth) * (rect.Width - padding * 2f);
        float py = rect.Bottom - padding - ((z - minZ) / safeHeight) * (rect.Height - padding * 2f);
        return new SKPoint(px, py);
    }

    private static void SaveNavMeshSurface(SKSurface surface, string path)
    {
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteNavMeshGallerySheet(string title, IReadOnlyList<NavMeshStageFrame> frames, string screensDir, string outputPath)
    {
        if (frames.Count == 0)
        {
            return;
        }

        const int thumbWidth = 760;
        const int thumbHeight = 428;
        const int headerHeight = 72;
        const int labelHeight = 36;
        int columns = 2;
        int rows = (int)Math.Ceiling(frames.Count / (double)columns);

        using var surface = SKSurface.Create(new SKImageInfo(columns * thumbWidth, rows * (thumbHeight + labelHeight) + headerHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(8, 10, 16));
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28f };
        using var labelPaint = new SKPaint { Color = new SKColor(196, 204, 216), IsAntialias = true, TextSize = 18f };
        canvas.DrawText(title, 20, 38, titlePaint);

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
            float x = col * thumbWidth;
            float y = headerHeight + row * (thumbHeight + labelHeight);
            canvas.DrawBitmap(bitmap, new SKRect(x, y, x + thumbWidth, y + thumbHeight));
            canvas.DrawText($"{frames[index].Step}: {frames[index].Caption}", x + 12f, y + thumbHeight + 24f, labelPaint);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void DrawNavMeshGrid(SKCanvas canvas, int startC, int startR)
    {
        using var gridPaint = new SKPaint { Color = new SKColor(38, 48, 64), Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        for (int offset = 0; offset <= VertexChunk.ChunkSize; offset += 8)
        {
            SKPoint top = ToNavMeshGridScreen(startC + offset, startR, startC, startR);
            SKPoint bottom = ToNavMeshGridScreen(startC + offset, startR + VertexChunk.ChunkSize, startC, startR);
            canvas.DrawLine(top, bottom, gridPaint);
            SKPoint left = ToNavMeshGridScreen(startC, startR + offset, startC, startR);
            SKPoint right = ToNavMeshGridScreen(startC + VertexChunk.ChunkSize, startR + offset, startC, startR);
            canvas.DrawLine(left, right, gridPaint);
        }
    }

    private static void DrawWalkMaskTriangle(SKCanvas canvas, int localC, int localR, int triIndex, bool isOddRow, SKPaint paint, int startC, int startR)
    {
        float globalC = startC + localC;
        float globalR = startR + localR;
        SKPoint p00 = ToNavMeshGridScreen(globalC, globalR, startC, startR);
        SKPoint p10 = ToNavMeshGridScreen(globalC + 1f, globalR, startC, startR);
        SKPoint p01 = ToNavMeshGridScreen(globalC, globalR + 1f, startC, startR);
        SKPoint p11 = ToNavMeshGridScreen(globalC + 1f, globalR + 1f, startC, startR);

        using var path = new SKPath();
        if (!isOddRow)
        {
            if (triIndex == 0)
            {
                path.MoveTo(p00);
                path.LineTo(p10);
                path.LineTo(p01);
            }
            else
            {
                path.MoveTo(p10);
                path.LineTo(p11);
                path.LineTo(p01);
            }
        }
        else
        {
            if (triIndex == 0)
            {
                path.MoveTo(p00);
                path.LineTo(p10);
                path.LineTo(p11);
            }
            else
            {
                path.MoveTo(p00);
                path.LineTo(p11);
                path.LineTo(p01);
            }
        }

        path.Close();
        canvas.DrawPath(path, paint);
    }

    private static SKPoint ToNavMeshGridScreen(float globalC, float globalR, int startC, int startR)
    {
        const float padding = 84f;
        float usableWidth = NavMeshImageWidth - padding * 2f;
        float usableHeight = NavMeshImageHeight - padding * 2f;
        float x = padding + ((globalC - startC) / VertexChunk.ChunkSize) * usableWidth;
        float y = padding + ((globalR - startR) / VertexChunk.ChunkSize) * usableHeight;
        return new SKPoint(x, NavMeshImageHeight - y);
    }

    private static SKRect ToNavMeshGridRect(float minC, float minR, float maxC, float maxR, int startC, int startR)
    {
        SKPoint a = ToNavMeshGridScreen(minC, minR, startC, startR);
        SKPoint b = ToNavMeshGridScreen(maxC, maxR, startC, startR);
        return SKRect.Create(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }

    private static SKPoint ToNavTileScreen(int xcm, int zcm, int minX, int maxX, int minZ, int maxZ)
    {
        const float padding = 84f;
        float safeWidth = Math.Max(1f, maxX - minX);
        float safeHeight = Math.Max(1f, maxZ - minZ);
        float x = padding + ((xcm - minX) / safeWidth) * (NavMeshImageWidth - padding * 2f);
        float y = padding + ((zcm - minZ) / safeHeight) * (NavMeshImageHeight - padding * 2f);
        return new SKPoint(x, NavMeshImageHeight - y);
    }

    private static SKColor ResolveTerrainColor(byte height, byte water, bool blocked)
    {
        if (blocked)
        {
            return new SKColor(176, 72, 72);
        }

        if (water > height)
        {
            byte depth = (byte)Math.Clamp((water - height) * 18 + 96, 0, 255);
            return new SKColor(48, 110, depth);
        }

        byte r = (byte)Math.Clamp(48 + height * 10, 0, 255);
        byte g = (byte)Math.Clamp(82 + height * 9, 0, 255);
        byte b = (byte)Math.Clamp(42 + height * 4, 0, 255);
        return new SKColor(r, g, b);
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
        NavMeshBakeTerrainVisualization
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
        public Vector2 Resolution => new(DefaultWidth, DefaultHeight);

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

        public bool TryResolveGroundWorldCm(Vector2 screenPosition, out Vector2 worldCm)
        {
            worldCm = default;
            if (!AuthoritativeGroundPointerHelper.TryResolveFromScreen(Engine.GlobalContext, screenPosition, out WorldCmInt2 resolved))
            {
                return false;
            }

            worldCm = new Vector2(resolved.X, resolved.Y);
            return true;
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

    private sealed class ScriptedInputBackend : IInputBackend, IFrameSynchronizedInputBackend
    {
        private readonly Dictionary<string, bool> _pendingButtons = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _frameButtons = new(StringComparer.Ordinal);
        private Vector2 _pendingMousePosition;
        private Vector2 _frameMousePosition;
        private float _pendingMouseWheel;
        private float _frameMouseWheel;
        private bool _frameInitialized;

        public void SetButton(string path, bool isDown) => _pendingButtons[path] = isDown;
        public void SetMousePosition(Vector2 position) => _pendingMousePosition = position;
        public void SetMouseWheel(float value) => _pendingMouseWheel = value;
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _frameButtons.TryGetValue(devicePath, out bool isDown) && isDown;
        public Vector2 GetMousePosition() => _frameInitialized ? _frameMousePosition : _pendingMousePosition;
        public float GetMouseWheel() => _frameInitialized ? _frameMouseWheel : _pendingMouseWheel;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;

        public void AdvanceFrameInput()
        {
            _frameButtons.Clear();
            foreach (KeyValuePair<string, bool> pair in _pendingButtons)
            {
                _frameButtons[pair.Key] = pair.Value;
            }

            _frameMousePosition = _pendingMousePosition;
            _frameMouseWheel = _pendingMouseWheel;
            _frameInitialized = true;
        }
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
        Vector2? CommandTargetWorldCm,
        string StatusLine,
        IReadOnlyList<string> OverlayLines,
        IReadOnlyList<RoadSplineCapture> Splines);

    private readonly record struct RoadCommandTarget(
        Vector2 ScreenPosition,
        Vector2 WorldCm);

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

    private sealed record NavigationAcceptanceSpec(
        string Key,
        string ScenarioName,
        int ScenarioIndex,
        int AgentsPerTeam,
        int Team0Direction,
        int Team1Direction,
        int MinimumBlockers,
        float FinalCenterFractionLimit,
        float FinalCenterStoppedFractionLimit,
        float MovingAgentsFractionLimit,
        float PeakReliefFraction,
        float Team0FinalCrossedFractionLimit,
        float Team1FinalCrossedFractionLimit);

    private readonly record struct NavMeshStageFrame(
        string FileName,
        string Step,
        string Caption);

    private enum NavMeshTriangleCause : byte
    {
        Walkable = 0,
        Water = 1,
        HardBlocked = 2,
        Cliff = 3,
        Straightened = 4
    }

    private sealed record NavMeshChunkHeuristics(
        int ChunkX,
        int ChunkY,
        double WaterFraction,
        double BlockedFraction,
        int HeightMin,
        int HeightMax,
        int ShorelineTransitions,
        double HeuristicScore);

    private sealed record NavMeshAcceptanceChunk(
        int ChunkX,
        int ChunkY,
        int StartC,
        int StartR,
        string SelectionReason,
        double WaterFraction,
        double BlockedFraction,
        int HeightMin,
        int HeightMax,
        int ShorelineTransitions,
        double HeuristicScore,
        double EvidenceScore);

    private sealed record NavMeshTriangleAudit(
        NavMeshTriangleCause[] Causes,
        int WalkableTriangleCount,
        int WaterBlockedTriangleCount,
        int HardBlockedTriangleCount,
        int CliffBlockedTriangleCount,
        int StraightenedTriangleCount,
        Vector2 BlockedSampleLocalCm,
        bool BlockedSampleFound)
    {
        public int BlockedTriangleCount => WaterBlockedTriangleCount + HardBlockedTriangleCount + CliffBlockedTriangleCount + StraightenedTriangleCount;

        public NavMeshTriangleCause GetCause(int localC, int localR, int triIndex)
        {
            return Causes[(localR * VertexChunk.ChunkSize + localC) * 2 + triIndex];
        }
    }

    private sealed record NavMeshRuntimeProof(
        bool SameTilePathSuccess,
        int SameTilePathPointCount,
        IReadOnlyList<Vector2> SameTilePathLocal,
        Vector2 SameTileStartLocal,
        Vector2 SameTileGoalLocal,
        bool CrossPortalPathSuccess,
        int CrossPortalPathPointCount,
        IReadOnlyList<Vector2> CrossPortalPathWorld,
        NavTile? NeighborTile,
        Vector2 CrossPortalStartWorld,
        Vector2 CrossPortalGoalWorld,
        bool BlockedSampleRejected,
        Vector2 BlockedSampleLocalCm);

    private sealed record NavMeshBakeAcceptanceResult(
        bool Success,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> FailedChecks,
        int WalkableTriangleCount,
        int RingCount,
        int PolygonCount,
        int PolygonHoleCount,
        int VertexCount,
        int TriangleCount,
        int PortalCount,
        int WaterBlockedTriangleCount,
        int HardBlockedTriangleCount,
        int CliffBlockedTriangleCount,
        int StraightenedTriangleCount,
        bool UsedGridFallback,
        string SelectionReason,
        string NormalizedSignature);
}
