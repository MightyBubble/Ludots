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
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map.Board;
using Ludots.Core.Input.Runtime;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
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
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Launcher.Backend;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using Raylib_cs;
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

    private static readonly QueryDescription MassNavigationAgentQuery = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, WorldPositionCm>();

    private static readonly QueryDescription OrderableMassNavigationAgentQuery = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex, Team, OrderBuffer, WorldPositionCm, SelectionSelectableTag, PresentationOwnerHasPerformerPayload>();

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
    private const int MassNavigationImageWidth = 1600;
    private const int MassNavigationImageHeight = 900;
    private const int MassNavigationInitialSettleTicks = 20;
    private const int MassNavigationCommandSettleTicks = 90;
    private const int MassNavigationRemoteSettleTicks = 20;
    private const int MassNavigationReturnSettleTicks = 20;
    private const int MassNavigationSelectionSampleCount = 128;
    private const int MassNavigationAvoidanceFrameIntervalTicks = 4;
    private const int MassNavigationAvoidanceExtraOrderTicks = 210;
    private const int MassNavigationAvoidanceCrossingTicks = 420;
    private const int MassNavigationAvoidanceCrowdSettleTicks = 1800;
    private const int MassNavigationAvoidanceImageWidth = 1280;
    private const int MassNavigationAvoidanceImageHeight = 720;
    private const float MassNavigationAvoidanceZoomWidthCm = 4000f;
    private const float MassNavigationAvoidanceZoomAspectWidth = 16f;
    private const float MassNavigationAvoidanceZoomAspectHeight = 9f;
    private const float MassNavigationAvoidanceCrossingScale = 0.2f;
    private const float MassNavigationAvoidanceCrowdSettleFraction = 0.8f;
    private const float MassNavigationAvoidanceDeepOverlapRatio = 0.10f;
    private const float MassNavigationAvoidanceFinalMaxPenetrationRatio = 0.10f;
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

        if (plan.RootModIds.Any(id =>
                string.Equals(id, "MassNavigationMod", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "CapabilityStandardMassNavigationLargeWorld10kMod", StringComparison.OrdinalIgnoreCase)))
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
        var uiSurfaceHost = new UiSurfaceHost(uiRoot, textMeasurer, imageSizeProvider);
        uiRoot.Resize(DefaultWidth, DefaultHeight);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiSurfaceHost, (object)uiSurfaceHost);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)textMeasurer);
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)imageSizeProvider);
        engine.SetService(CoreServiceKeys.UISystem, (Ludots.Core.UI.IUiSystem)new MarkupUiSystem(uiSurfaceHost));

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

        engine.LoadStartupMap();
        return new RecordingRuntime(plan.AdapterId, engine, config, inputBackend, screenProjector, cameraPresenter, renderCameraDebug, presentationFrameSetup, hudProjection);
    }

    private static void ApplyRaylibHostAssets(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshAssets))
        {
            throw new InvalidOperationException("Raylib evidence recording requires PresentationMeshAssetRegistry before host asset binding.");
        }

        if (!engine.TryGetService(CoreServiceKeys.PresentationMaterialRegistry, out PresentationMaterialRegistry materialAssets))
        {
            throw new InvalidOperationException("Raylib evidence recording requires PresentationMaterialRegistry before host asset binding.");
        }

        new PresentationHostAssetConfigLoader(engine.ConfigPipeline, meshAssets, materialAssets)
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
        var uiSurfaceHost = new UiSurfaceHost(uiRoot, textMeasurer, imageSizeProvider);
        uiRoot.Resize(DefaultWidth, DefaultHeight);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiSurfaceHost, (object)uiSurfaceHost);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)textMeasurer);
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)imageSizeProvider);
        engine.SetService(CoreServiceKeys.UISystem, (Ludots.Core.UI.IUiSystem)new MarkupUiSystem(uiSurfaceHost));

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

        engine.LoadStartupMap();
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
        sb.AppendLine("- reusable wiring: `launcher.runtime.json`, `PlayerInputHandler`, `CurrentSelectionApplySystem`, `InputOrderMappingSystem`, `AutoPathService`, `RoadSplineBuffer`, `LoadedChunksSource`");
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
        if (!MassNavigationIds.IsCurrentNavigationMap(runtime.Engine))
        {
            if (string.IsNullOrWhiteSpace(runtime.Config.StartupMapId))
            {
                throw new InvalidOperationException("MassNavigation UAT requires a configured startup map.");
            }

            runtime.Engine.LoadStartupMap();
        }

        PresentationTimingDiagnostics timings = runtime.Engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
            ?? throw new InvalidOperationException("MassNavigation UAT requires PresentationTimingDiagnostics.");
        timings.SystemBreakdownEnabled = true;

        Tick(runtime, MassNavigationInitialSettleTicks, frameTimesMs);
        MassNavigationSimulationRuntime simulation = runtime.Engine.GetService(MassNavigationKeys.SimulationRuntime)
            ?? throw new InvalidOperationException("MassNavigation UAT requires MassNavigationSimulationRuntime.");
        WaitForMassNavigationScenario(runtime, simulation, frameTimesMs, maxTicks: 240);
        MassNavigationSolverRuntimeConfigSnapshot solverSnapshot = simulation.CaptureSolverRuntimeConfig();
        var avoidanceScratch = new MassNavigationAvoidanceScratch(
            simulation.Config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity,
            solverSnapshot.MaxObstacleCount);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, 0, "000_boot", captureImage: true);

        Entity[] selected = SelectFirstOrderableMassNavigationAgents(runtime.Engine, simulation, MassNavigationSelectionSampleCount);
        Tick(runtime, 3, frameTimesMs);
        // The flow work area follows command focus, so capture the pre-command center now:
        // the later crossing order mirrors the first target through this fixed point.
        Vector2 initialWorkAreaCenter = new(simulation.FlowWorkAreaCenterXCm, simulation.FlowWorkAreaCenterYCm);
        Vector2 commandTarget = new(
            initialWorkAreaCenter.X + (simulation.SolverWindowWidthCm * 0.34f),
            initialWorkAreaCenter.Y + (simulation.SolverWindowHeightCm * 0.18f));
        SubmitMassNavigationMoveOrder(runtime.Engine, simulation, selected, commandTarget);
        string avoidanceDir = Path.Combine(screensDir, "avoidance");
        Directory.CreateDirectory(avoidanceDir);
        var avoidanceMetrics = new List<MassNavigationAvoidanceFrameMetrics>();
        CaptureMassNavigationAvoidanceSequence(runtime, simulation, avoidanceScratch, commandTarget, avoidanceDir, frameTimesMs, avoidanceMetrics, MassNavigationCommandSettleTicks);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, MassNavigationCommandSettleTicks, "001_selection_order", captureImage: true);
        CaptureMassNavigationAvoidanceSequence(runtime, simulation, avoidanceScratch, commandTarget, avoidanceDir, frameTimesMs, avoidanceMetrics, MassNavigationAvoidanceExtraOrderTicks);

        WaitForMassNavigationCrowdSettle(runtime, simulation, frameTimesMs, MassNavigationAvoidanceCrowdSettleFraction, MassNavigationAvoidanceCrowdSettleTicks);
        // March the commanded group back across the settled central crowd. The crossing target
        // mirrors the first order target through the pre-command work-area center, scaled down so
        // the destination-following solver window keeps the whole march inside the active play
        // area (hard resolve is intentionally skipped outside it).
        Vector2 crossingTarget = initialWorkAreaCenter - ((commandTarget - initialWorkAreaCenter) * MassNavigationAvoidanceCrossingScale);
        SubmitMassNavigationMoveOrder(runtime.Engine, simulation, selected, crossingTarget);
        CaptureMassNavigationAvoidanceSequence(runtime, simulation, avoidanceScratch, crossingTarget, avoidanceDir, frameTimesMs, avoidanceMetrics, MassNavigationAvoidanceCrossingTicks);
        WriteMassNavigationAvoidanceMetrics(Path.Combine(request.OutputDirectory, "avoidance-metrics.jsonl"), avoidanceMetrics);

        Vector2 originalCameraTarget = runtime.Engine.GameSession.Camera.State.TargetCm;
        MassNavigationHotZoneConfig remoteZone = ResolveRemoteHotZone(simulation);
        MinimapRuntime minimap = runtime.Engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException("MassNavigation UAT requires core MinimapRuntime.");
        minimap.JumpCameraTo(runtime.Engine, new Vector2(remoteZone.CenterXCm, remoteZone.CenterYCm));
        Tick(runtime, MassNavigationRemoteSettleTicks, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, MassNavigationCommandSettleTicks + MassNavigationRemoteSettleTicks, "002_remote_minimap_jump", captureImage: true);

        minimap.JumpCameraTo(runtime.Engine, originalCameraTarget);
        Tick(runtime, MassNavigationReturnSettleTicks, frameTimesMs);
        CaptureMassNavigationSnapshot(runtime, simulation, screensDir, frameTimesMs, timeline, captureFrames, MassNavigationCommandSettleTicks + MassNavigationRemoteSettleTicks + MassNavigationReturnSettleTicks, "003_return_original_area", captureImage: true);

        WriteTimelineSheet("MassNavigation performer + minimap large-world UAT", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        MassNavigationAcceptanceResult acceptance = EvaluateMassNavigationAcceptance(timeline, simulation, avoidanceMetrics);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildMassNavigationBattleReport(request, timeline, captureFrames, frameTimesMs, avoidanceMetrics, acceptance));
        File.WriteAllText(tracePath, BuildMassNavigationTraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildMassNavigationPathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildMassNavigationVisibleChecklist(captureFrames));
        File.WriteAllText(summaryPath, BuildMassNavigationSummaryJson(request, acceptance, timeline, avoidanceMetrics));

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
        int expectedAgents = ExpectedMassNavigationScenarioAgentCount(simulation);
        int expectedBlockers = simulation.NavigationObstacleCount;
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
            $"MassNavigation scenario did not finish core-authored MassNavigation binding: agents={simulation.AgentState.TotalAgents}/{expectedAgents}, blockers={simulation.AgentState.BlockerCount}/{expectedBlockers}, markers={simulation.AgentState.WorldMarkerCount}/{expectedMarkers}.");
    }

    private static int ExpectedMassNavigationScenarioAgentCount(MassNavigationSimulationRuntime simulation)
    {
        return checked(simulation.AgentsPerTeam * simulation.TeamCount);
    }

    private static Entity[] SelectFirstOrderableMassNavigationAgents(GameEngine engine, MassNavigationSimulationRuntime simulation, int requestedCount)
    {
        SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new InvalidOperationException("MassNavigation UAT requires SelectionRuntime.");

        Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (owner == Entity.Null || !engine.World.IsAlive(owner))
        {
            throw new InvalidOperationException("MassNavigation UAT requires a live LocalPlayerEntity.");
        }

        int count = Math.Min(requestedCount, simulation.AgentState.ControllableAgentSlotCount);
        if (count <= 0)
        {
            throw new InvalidOperationException("MassNavigation UAT found no OrderBuffer-backed MassNavigation agents.");
        }

        var selected = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            selected[i] = simulation.AgentState.ControllableAgentSlots[i];
        }

        if (!selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, selected))
        {
            throw new InvalidOperationException("MassNavigation UAT failed to write SelectionRuntime LivePrimary selection.");
        }

        return selected;
    }

    private static void SubmitMassNavigationMoveOrder(GameEngine engine, MassNavigationSimulationRuntime simulation, ReadOnlySpan<Entity> selected, Vector2 targetCm)
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

        int sharedOrderId = simulation.AllocateSharedOrderId();
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

    private static void WaitForMassNavigationCrowdSettle(
        RecordingRuntime runtime,
        MassNavigationSimulationRuntime simulation,
        List<double> frameTimesMs,
        float minSettledFraction,
        int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            int unitCount = simulation.NavigationAgentCount;
            if (unitCount > 0 && simulation.NavigationSettledAgentCount >= unitCount * minSettledFraction)
            {
                return;
            }

            Tick(runtime, 1, frameTimesMs);
        }
    }

    private static void CaptureMassNavigationAvoidanceSequence(
        RecordingRuntime runtime,
        MassNavigationSimulationRuntime simulation,
        MassNavigationAvoidanceScratch scratch,
        Vector2 commandTargetCm,
        string framesDir,
        List<double> frameTimesMs,
        List<MassNavigationAvoidanceFrameMetrics> metrics,
        int tickCount)
    {
        for (int i = 0; i < tickCount; i++)
        {
            Tick(runtime, 1, frameTimesMs);
            if (i % MassNavigationAvoidanceFrameIntervalTicks != 0)
            {
                continue;
            }

            CaptureMassNavigationAvoidanceZoomFrame(simulation, scratch, commandTargetCm, framesDir, metrics);
        }
    }

    private static void CaptureMassNavigationAvoidanceZoomFrame(
        MassNavigationSimulationRuntime simulation,
        MassNavigationAvoidanceScratch scratch,
        Vector2 commandTargetCm,
        string framesDir,
        List<MassNavigationAvoidanceFrameMetrics> metrics)
    {
        MassNavigationAvoidanceSnapshot snapshot = scratch.Capture(simulation);
        if (snapshot.UnitCount <= 0)
        {
            return;
        }

        ReadOnlySpan<MassNavigationAvoidanceAgentSnapshot> agents = scratch.Agents(snapshot.UnitCount);
        ReadOnlySpan<MassNavigationObstacleSnapshot> obstacles = scratch.Obstacles(snapshot.ObstacleCount);
        float centroidX = 0f;
        float centroidY = 0f;
        int selectedCount = 0;
        foreach (MassNavigationAvoidanceAgentSnapshot agent in agents)
        {
            if (!agent.Selected)
            {
                continue;
            }

            centroidX += agent.WorldXCm;
            centroidY += agent.WorldYCm;
            selectedCount++;
        }

        if (selectedCount <= 0)
        {
            return;
        }

        centroidX /= selectedCount;
        centroidY /= selectedCount;

        float zoomWidthCm = MassNavigationAvoidanceZoomWidthCm;
        float zoomHeightCm = zoomWidthCm * MassNavigationAvoidanceZoomAspectHeight / MassNavigationAvoidanceZoomAspectWidth;
        float minXCm = centroidX - (zoomWidthCm * 0.5f);
        float maxXCm = centroidX + (zoomWidthCm * 0.5f);
        float minYCm = centroidY - (zoomHeightCm * 0.5f);
        float maxYCm = centroidY + (zoomHeightCm * 0.5f);

        float pxPerCm = MassNavigationAvoidanceImageWidth / zoomWidthCm;

        Span<MassNavigationAvoidanceAgentSnapshot> visibleAgents = scratch.VisibleAgents(snapshot.UnitCount);
        Span<MassNavigationAvoidanceAgentSnapshot> playAreaAgents = scratch.PlayAreaAgents(snapshot.UnitCount);
        int visibleAgentCount = 0;
        int playAreaAgentCount = 0;
        foreach (MassNavigationAvoidanceAgentSnapshot agent in agents)
        {
            if (agent.WorldXCm >= minXCm && agent.WorldXCm <= maxXCm && agent.WorldYCm >= minYCm && agent.WorldYCm <= maxYCm)
            {
                visibleAgents[visibleAgentCount++] = agent;
                if (agent.InsidePlayArea)
                {
                    playAreaAgents[playAreaAgentCount++] = agent;
                }
            }
        }

        // Hard resolve intentionally skips agents outside the solver play area
        // (IsInsideTacticalField), so overlap metrics only cover play-area agents;
        // outside-play-area agents are drawn dimmed so frame review preserves that distinction.
        float maxPenetrationCm = 0f;
        float maxPenetrationRatio = 0f;
        int deepOverlapPairs = 0;
        for (int a = 0; a < playAreaAgentCount; a++)
        {
            MassNavigationAvoidanceAgentSnapshot first = playAreaAgents[a];
            for (int b = a + 1; b < playAreaAgentCount; b++)
            {
                MassNavigationAvoidanceAgentSnapshot second = playAreaAgents[b];
                float dx = first.LocalXCm - second.LocalXCm;
                float dy = first.LocalYCm - second.LocalYCm;
                float minDistance = first.BodyRadiusCm + second.BodyRadiusCm;
                float distanceSq = (dx * dx) + (dy * dy);
                if (distanceSq >= minDistance * minDistance)
                {
                    continue;
                }

                float penetration = minDistance - MathF.Sqrt(distanceSq);
                float ratio = penetration / minDistance;
                maxPenetrationCm = MathF.Max(maxPenetrationCm, penetration);
                maxPenetrationRatio = MathF.Max(maxPenetrationRatio, ratio);
                if (ratio > MassNavigationAvoidanceDeepOverlapRatio)
                {
                    deepOverlapPairs++;
                }
            }
        }

        int settledCount = 0;
        int heavyCount = 0;
        int selectedVisibleCount = 0;
        for (int a = 0; a < playAreaAgentCount; a++)
        {
            MassNavigationAvoidanceAgentSnapshot agent = playAreaAgents[a];
            if (agent.Settled)
            {
                settledCount++;
            }

            if (agent.HeavyProfile)
            {
                heavyCount++;
            }

            if (agent.Selected)
            {
                selectedVisibleCount++;
            }
        }

        int frameIndex = metrics.Count;
        metrics.Add(new MassNavigationAvoidanceFrameMetrics(
            frameIndex,
            centroidX,
            centroidY,
            snapshot.UnitCount,
            visibleAgentCount,
            playAreaAgentCount,
            selectedVisibleCount,
            heavyCount,
            settledCount,
            maxPenetrationCm,
            maxPenetrationRatio,
            deepOverlapPairs));

        using var surface = SKSurface.Create(new SKImageInfo(MassNavigationAvoidanceImageWidth, MassNavigationAvoidanceImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(8, 12, 18));

        using var obstaclePaint = new SKPaint { Color = new SKColor(120, 128, 138, 210), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var heavyRingPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var targetPaint = new SKPaint { Color = new SKColor(255, 92, 92), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var minorTextPaint = new SKPaint { Color = new SKColor(190, 205, 216), IsAntialias = true };
        using var textFont = new SKFont { Size = 22f };
        using var minorTextFont = new SKFont { Size = 17f };

        float ToScreenX(float worldXCm) => (worldXCm - minXCm) * pxPerCm;
        float ToScreenY(float worldYCm) => MassNavigationAvoidanceImageHeight - ((worldYCm - minYCm) * pxPerCm);

        foreach (MassNavigationObstacleSnapshot obstacle in obstacles)
        {
            if (obstacle.WorldXCm + obstacle.RadiusCm < minXCm ||
                obstacle.WorldXCm - obstacle.RadiusCm > maxXCm ||
                obstacle.WorldYCm + obstacle.RadiusCm < minYCm ||
                obstacle.WorldYCm - obstacle.RadiusCm > maxYCm)
            {
                continue;
            }

            canvas.DrawCircle(ToScreenX(obstacle.WorldXCm), ToScreenY(obstacle.WorldYCm), obstacle.RadiusCm * pxPerCm, obstaclePaint);
        }

        foreach (MassNavigationAvoidanceAgentSnapshot agent in visibleAgents[..visibleAgentCount])
        {
            float radiusPx = MathF.Max(1.5f, agent.BodyRadiusCm * pxPerCm);
            SKColor teamColor = ResolveMassNavigationTeamColor(agent.TeamId);
            using var agentPaint = new SKPaint { Color = teamColor.WithAlpha(agent.InsidePlayArea ? (byte)220 : (byte)70), IsAntialias = true, Style = SKPaintStyle.Fill };
            float screenX = ToScreenX(agent.WorldXCm);
            float screenY = ToScreenY(agent.WorldYCm);
            canvas.DrawCircle(screenX, screenY, radiusPx, agentPaint);
            if (agent.InsidePlayArea && agent.HeavyProfile)
            {
                canvas.DrawCircle(screenX, screenY, radiusPx + 1.5f, heavyRingPaint);
            }
        }

        if (commandTargetCm.X >= minXCm && commandTargetCm.X <= maxXCm && commandTargetCm.Y >= minYCm && commandTargetCm.Y <= maxYCm)
        {
            DrawCrosshair(canvas, new SKPoint(ToScreenX(commandTargetCm.X), ToScreenY(commandTargetCm.Y)), 14f, targetPaint);
        }

        canvas.DrawText($"MassNavigation avoidance zoom | frame={frameIndex:D4} | window {zoomWidthCm:F0}x{zoomHeightCm:F0} cm @ ({centroidX:F0}, {centroidY:F0})", 24, 34, SKTextAlign.Left, textFont, textPaint);
        canvas.DrawText($"Agents={visibleAgentCount}/{snapshot.UnitCount} playArea={playAreaAgentCount} selected={selectedVisibleCount} heavyProfile(ringed)={heavyCount} settled={settledCount}", 24, 62, SKTextAlign.Left, minorTextFont, minorTextPaint);
        canvas.DrawText($"maxPenetration={maxPenetrationCm:F1}cm ({maxPenetrationRatio:P1} of pair radius) deepOverlapPairs(>{MassNavigationAvoidanceDeepOverlapRatio:P0})={deepOverlapPairs}", 24, 86, SKTextAlign.Left, minorTextFont, minorTextPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(Path.Combine(framesDir, $"frame_{frameIndex:D4}.png"), FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteMassNavigationAvoidanceMetrics(string path, IReadOnlyList<MassNavigationAvoidanceFrameMetrics> metrics)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < metrics.Count; i++)
        {
            sb.AppendLine(JsonSerializer.Serialize(metrics[i]));
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static MassNavigationAvoidanceSummary SummarizeMassNavigationAvoidance(IReadOnlyList<MassNavigationAvoidanceFrameMetrics> metrics)
    {
        if (metrics.Count == 0)
        {
            return new MassNavigationAvoidanceSummary(
                FrameCount: 0,
                MaxVisibleAgentCount: 0,
                MaxPlayAreaAgentCount: 0,
                MaxSelectedVisibleAgentCount: 0,
                MaxHeavyAgentCount: 0,
                PeakDeepOverlapPairCount: 0,
                FinalDeepOverlapPairCount: 0,
                PeakMaxPenetrationRatio: 0f,
                FinalMaxPenetrationRatio: 0f);
        }

        MassNavigationAvoidanceFrameMetrics final = metrics[^1];
        int maxVisible = 0;
        int maxPlayArea = 0;
        int maxSelected = 0;
        int maxHeavy = 0;
        int peakDeepOverlap = 0;
        float peakPenetrationRatio = 0f;
        for (int i = 0; i < metrics.Count; i++)
        {
            MassNavigationAvoidanceFrameMetrics metric = metrics[i];
            maxVisible = Math.Max(maxVisible, metric.VisibleAgentCount);
            maxPlayArea = Math.Max(maxPlayArea, metric.PlayAreaAgentCount);
            maxSelected = Math.Max(maxSelected, metric.SelectedVisibleAgentCount);
            maxHeavy = Math.Max(maxHeavy, metric.HeavyAgentCount);
            peakDeepOverlap = Math.Max(peakDeepOverlap, metric.DeepOverlapPairCount);
            peakPenetrationRatio = MathF.Max(peakPenetrationRatio, metric.MaxPenetrationRatio);
        }

        return new MassNavigationAvoidanceSummary(
            FrameCount: metrics.Count,
            MaxVisibleAgentCount: maxVisible,
            MaxPlayAreaAgentCount: maxPlayArea,
            MaxSelectedVisibleAgentCount: maxSelected,
            MaxHeavyAgentCount: maxHeavy,
            PeakDeepOverlapPairCount: peakDeepOverlap,
            FinalDeepOverlapPairCount: final.DeepOverlapPairCount,
            PeakMaxPenetrationRatio: peakPenetrationRatio,
            FinalMaxPenetrationRatio: final.MaxPenetrationRatio);
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
        engine.World.Query(in OrderableMassNavigationAgentQuery, (Entity entity, ref MassNavigationAgentIndex agentIndex, ref Team team, ref WorldPositionCm position, ref PresentationOwnerHasPerformerPayload payload) =>
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
            ActiveHotZoneId: simulation.ActiveHotZoneId,
            TeamCount: simulation.TeamCount,
            TeamIds: configuredTeamIds,
            TeamCounts: teamCounts,
            AgentCount: simulation.AgentState.TotalAgents,
            EcsAgentCount: ecsAgentCount,
            ControllableCount: simulation.AgentState.ControllableAgentCount,
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
            FocusBudgetUpdatesTotal: simulation.FocusBudgetUpdatesTotal,
            CommandRejectsTotal: simulation.CommandRejectsTotal,
            FrameMs: timings.LastTotalTickMs,
            SimulationMs: timings.LastSimulationMs,
            PresentationMs: timings.LastPresentationMs,
            PerformerMs: timings.LastPerformerEmitMs + timings.LastPerformerBehaviorMs + timings.LastPerformerEntityTransformSyncMs,
            MinimapMs: timings.LastPerformerMinimapMarkerMs + timings.LastMinimapProjectionMs,
            MassNavigationMs: simulation.SelectionSyncMs + simulation.FormationTargetMs + simulation.FlowFieldRebuildMs + simulation.StepPrepMs + simulation.LocalSteeringMs + simulation.HardResolveMs + simulation.SimStepMs + simulation.EntitySyncMs,
            SamplePositions: samplePositions);
    }

    private static MassNavigationAcceptanceResult EvaluateMassNavigationAcceptance(
        IReadOnlyList<MassNavigationSnapshot> timeline,
        MassNavigationSimulationRuntime simulation,
        IReadOnlyList<MassNavigationAvoidanceFrameMetrics> avoidanceMetrics)
    {
        var failures = new List<string>();
        MassNavigationSnapshot boot = timeline.First(snapshot => snapshot.Step == "000_boot");
        MassNavigationSnapshot afterOrder = timeline.First(snapshot => snapshot.Step == "001_selection_order");
        MassNavigationSnapshot remote = timeline.First(snapshot => snapshot.Step == "002_remote_minimap_jump");
        MassNavigationSnapshot returned = timeline.First(snapshot => snapshot.Step == "003_return_original_area");
        string expectedMapId = simulation.Config.MapId;

        AddAcceptanceCheck(boot.ActiveMapId == expectedMapId, $"Expected MassNavigation map '{expectedMapId}', got '{boot.ActiveMapId}'.", failures);
        AddAcceptanceCheck(boot.WorldWidthCm == 6_400_000 && boot.WorldHeightCm == 6_400_000, $"Expected 64km x 64km config, got {boot.WorldWidthCm}x{boot.WorldHeightCm} cm.", failures);
        AddAcceptanceCheck(boot.TeamCount >= 4, $"Expected at least 4 configured teams, got {boot.TeamCount}.", failures);
        int expectedAgentCount = ExpectedMassNavigationScenarioAgentCount(simulation);
        AddAcceptanceCheck(boot.AgentCount == expectedAgentCount, $"Agent state count mismatch: {boot.AgentCount} vs configured {expectedAgentCount}.", failures);
        AddAcceptanceCheck(boot.EcsAgentCount == boot.AgentCount, $"ECS controllable agent count mismatch: {boot.EcsAgentCount} vs runtime {boot.AgentCount}.", failures);
        AddAcceptanceCheck(boot.BlockerCount == simulation.NavigationObstacleCount, $"Blocker count mismatch: {boot.BlockerCount} vs solver {simulation.NavigationObstacleCount}.", failures);
        AddAcceptanceCheck(boot.HotspotMarkerCount == simulation.HotZones.Length, $"Hotspot marker count mismatch: {boot.HotspotMarkerCount} vs config {simulation.HotZones.Length}.", failures);
        AddAcceptanceCheck(boot.PerformerPayloadCount >= boot.AgentCount + boot.BlockerCount + boot.HotspotMarkerCount, $"Performer payloads missing: {boot.PerformerPayloadCount} for {boot.AgentCount + boot.BlockerCount + boot.HotspotMarkerCount} MassNavigation owners.", failures);
        AddAcceptanceCheck(boot.PerformerActiveCount >= boot.AgentCount + boot.BlockerCount + boot.HotspotMarkerCount, $"Performer active count too low: {boot.PerformerActiveCount}.", failures);
        AddAcceptanceCheck(boot.MinimapVisible, "Core minimap should be visible.", failures);
        AddAcceptanceCheck(string.Equals(boot.MinimapPreset, MinimapPreset.RtsFullMap.ToString(), StringComparison.Ordinal), $"Expected core minimap RtsFullMap preset, got {boot.MinimapPreset}.", failures);
        AddAcceptanceCheck(boot.MinimapBufferCount >= boot.AgentCount + boot.BlockerCount + boot.HotspotMarkerCount, $"Minimap marker buffer too low: {boot.MinimapBufferCount}.", failures);
        AddAcceptanceCheck(boot.MinimapDroppedTotal == 0, $"Minimap markers dropped: {boot.MinimapDroppedTotal}.", failures);
        AddAcceptanceCheck(afterOrder.SelectedCount > 0, "SelectionRuntime LivePrimary selection was not observed by MassNavigation.", failures);
        AddAcceptanceCheck(afterOrder.ActiveOrderGroups > 0 || afterOrder.ActiveGroups > 0, "massNavigationMove order did not create an active NavGroup.", failures);
        AddAcceptanceCheck(afterOrder.CommandRejectsTotal == 0, $"MassNavigation rejected commands unexpectedly: {afterOrder.CommandRejectsTotal}.", failures);
        AddAcceptanceCheck(Vector2.Distance(remote.CameraTargetCm, boot.CameraTargetCm) > 500_000f, $"Remote minimap jump did not move the camera far enough: boot={FormatPoint(boot.CameraTargetCm)} remote={FormatPoint(remote.CameraTargetCm)}.", failures);
        AddAcceptanceCheck(returned.AgentCount == boot.AgentCount, $"Returning to original area changed agent count: {boot.AgentCount} -> {returned.AgentCount}.", failures);
        AddAcceptanceCheck(returned.ScenarioSpawnCount == boot.ScenarioSpawnCount, $"Returning to original area re-ran scenario spawn: {boot.ScenarioSpawnCount} -> {returned.ScenarioSpawnCount}.", failures);
        AddAcceptanceCheck(returned.SceneResetCount == boot.SceneResetCount, $"Returning to original area reset the scene: {boot.SceneResetCount} -> {returned.SceneResetCount}.", failures);

        MassNavigationAvoidanceSummary avoidance = SummarizeMassNavigationAvoidance(avoidanceMetrics);
        AddAcceptanceCheck(avoidance.FrameCount > 0, "MassNavigation avoidance evidence captured zero metric frames.", failures);
        AddAcceptanceCheck(avoidance.MaxVisibleAgentCount > 0, "MassNavigation avoidance evidence never observed visible agents.", failures);
        AddAcceptanceCheck(avoidance.MaxSelectedVisibleAgentCount > 0, "MassNavigation avoidance evidence never observed selected agents in the zoom window.", failures);
        AddAcceptanceCheck(avoidance.MaxHeavyAgentCount > 0, "MassNavigation avoidance evidence never observed heavy-profile agents in the solver play area.", failures);
        AddAcceptanceCheck(avoidance.MaxPlayAreaAgentCount > 1, "MassNavigation avoidance evidence did not observe enough play-area agents to validate overlap resolution.", failures);
        AddAcceptanceCheck(avoidance.FinalDeepOverlapPairCount == 0, $"MassNavigation avoidance ended with {avoidance.FinalDeepOverlapPairCount} deep overlap pairs.", failures);
        AddAcceptanceCheck(
            avoidance.FinalMaxPenetrationRatio <= MassNavigationAvoidanceFinalMaxPenetrationRatio,
            $"MassNavigation avoidance final penetration ratio {avoidance.FinalMaxPenetrationRatio:P2} exceeded {MassNavigationAvoidanceFinalMaxPenetrationRatio:P2}.",
            failures);
        if (avoidance.FrameCount >= 2 && avoidance.PeakDeepOverlapPairCount > 0)
        {
            AddAcceptanceCheck(
                avoidance.FinalDeepOverlapPairCount < avoidance.PeakDeepOverlapPairCount,
                $"MassNavigation avoidance deep overlaps did not reduce from peak {avoidance.PeakDeepOverlapPairCount} to final {avoidance.FinalDeepOverlapPairCount}.",
                failures);
        }

        string normalizedSignature = string.Join("|", new[]
        {
            "mass_navigation_large_world",
            $"agents:{boot.AgentCount}",
            $"teams:{boot.TeamCount}",
            $"performers:{boot.PerformerActiveCount}",
            $"markers:{boot.MinimapBufferCount}/{boot.MinimapDroppedTotal}",
            $"remote:{MathF.Round(remote.CameraTargetCm.X):F0},{MathF.Round(remote.CameraTargetCm.Y):F0}",
            $"spawns:{boot.ScenarioSpawnCount}->{returned.ScenarioSpawnCount}",
            $"resets:{boot.SceneResetCount}->{returned.SceneResetCount}",
            $"avoidance:{avoidance.FrameCount}/{avoidance.MaxVisibleAgentCount}/{avoidance.MaxHeavyAgentCount}/{avoidance.FinalDeepOverlapPairCount}/{avoidance.FinalMaxPenetrationRatio:0.0000}"
        });

        string verdict = failures.Count == 0
            ? $"MassNavigation passes large-world performer/minimap/avoidance UAT with {boot.AgentCount} agents, {boot.PerformerActiveCount} performers, {boot.MinimapBufferCount} minimap markers and {avoidance.FrameCount} avoidance frames."
            : "MassNavigation large-world performer/minimap UAT failed.";

        return new MassNavigationAcceptanceResult(
            Success: failures.Count == 0,
            Verdict: verdict,
            FailureSummary: failures.Count == 0 ? verdict : string.Join(Environment.NewLine, failures),
            FailedChecks: failures,
            NormalizedSignature: normalizedSignature);
    }

    private static string BuildMassNavigationBattleReport(
        LauncherRecordingRequest request,
        IReadOnlyList<MassNavigationSnapshot> timeline,
        IReadOnlyList<CaptureFrame> captureFrames,
        IReadOnlyList<double> frameTimesMs,
        IReadOnlyList<MassNavigationAvoidanceFrameMetrics> avoidanceMetrics,
        MassNavigationAcceptanceResult acceptance)
    {
        MassNavigationSnapshot boot = timeline[0];
        MassNavigationSnapshot final = timeline[^1];
        MassNavigationAvoidanceSummary avoidance = SummarizeMassNavigationAvoidance(avoidanceMetrics);
        double medianTickMs = Median(frameTimesMs.ToArray());
        double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
        string evidenceImages = string.Join(", ", captureFrames.Select(frame => $"`screens/{frame.FileName}`").Append("`screens/timeline.png`"));

        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: mass-navigation-large-world");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: verify MassNavigation is the MassNavigationFlow SSOT and runs through performer + core minimap on a 64km RTS map.");
        sb.AppendLine("- Gameplay domain: real launcher bootstrap, component-authored MassNavigation binding, SelectionRuntime, OrderBuffer, performer runtime and core MinimapRuntime.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Map: `mods/capabilities/navigation/MassNavigationMod/assets/Maps/mass_navigation.json`");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Launch command: `{request.CommandText}`");
        sb.AppendLine($"- Evidence images: {evidenceImages}");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Boot the real MassNavigation launcher preset and wait for core MassNavigation runtime binding to settle.");
        sb.AppendLine("2. Write LivePrimary selection through SelectionRuntime and submit a `massNavigationMove` order through OrderBufferSystem.");
        sb.AppendLine("3. Jump the core minimap camera to a remote 64km hot-zone landmark, then jump back to the original area.");
        sb.AppendLine("4. Fail if units are recreated/reset, performer payloads are missing, minimap markers drop, or core minimap is not the visible RTS full-map preset.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (MassNavigationSnapshot snapshot in timeline)
        {
            sb.AppendLine($"- [{snapshot.Step}] camera={FormatPoint(snapshot.CameraTargetCm)} agents={snapshot.AgentCount} teams={snapshot.TeamCount} selected={snapshot.SelectedCount} groups={snapshot.ActiveGroups}/{snapshot.ActiveOrderGroups} performers={snapshot.PerformerActiveCount} minimap={snapshot.MinimapVisibleMarkerCount}/{snapshot.MinimapMarkerCount} loadedChunks={snapshot.LoadedChunkCount} frame={snapshot.FrameMs:F3}ms sim={snapshot.SimulationMs:F3}ms pres={snapshot.PresentationMs:F3}ms mass_navigation={snapshot.MassNavigationMs:F3}ms");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        foreach (string failedCheck in acceptance.FailedChecks)
        {
            sb.AppendLine($"- failed-check: {failedCheck}");
        }

        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- world: `{boot.WorldWidthCm} x {boot.WorldHeightCm}` cm");
        sb.AppendLine($"- agents: `{boot.AgentCount}`");
        sb.AppendLine($"- blockers: `{boot.BlockerCount}`");
        sb.AppendLine($"- hotspot markers: `{boot.HotspotMarkerCount}`");
        sb.AppendLine($"- performer active at boot: `{boot.PerformerActiveCount}`");
        sb.AppendLine($"- minimap markers at boot: `{boot.MinimapBufferCount}` droppedTotal=`{boot.MinimapDroppedTotal}`");
        sb.AppendLine($"- scenario spawn count boot/final: `{boot.ScenarioSpawnCount}` / `{final.ScenarioSpawnCount}`");
        sb.AppendLine($"- scene reset count boot/final: `{boot.SceneResetCount}` / `{final.SceneResetCount}`");
        sb.AppendLine($"- avoidance frames: `{avoidance.FrameCount}`");
        sb.AppendLine($"- avoidance max visible/play-area/selected/heavy-profile agents: `{avoidance.MaxVisibleAgentCount}` / `{avoidance.MaxPlayAreaAgentCount}` / `{avoidance.MaxSelectedVisibleAgentCount}` / `{avoidance.MaxHeavyAgentCount}`");
        sb.AppendLine($"- avoidance peak/final deep overlap pairs: `{avoidance.PeakDeepOverlapPairCount}` / `{avoidance.FinalDeepOverlapPairCount}`");
        sb.AppendLine($"- avoidance peak/final max penetration ratio: `{avoidance.PeakMaxPenetrationRatio:P2}` / `{avoidance.FinalMaxPenetrationRatio:P2}`");
        sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
        sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
        sb.AppendLine($"- normalized signature: `{acceptance.NormalizedSignature}`");
        sb.AppendLine("- reusable wiring: `RuntimeEntitySpawnQueue`, `RuntimeEntitySpawnSystem`, `SystemGroup.RuntimeEntityBinding`, `SelectionRuntime`, `OrderBufferSystem`, `PerformerEntityRuntime`, `MinimapRuntime`, `PresentationTimingDiagnostics`");
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
                solver_window_driver = snapshot.SolverWindowDriver,
                flow_work_area_reason = snapshot.FlowWorkAreaReason,
                scenario_spawns = snapshot.ScenarioSpawnCount,
                scene_resets = snapshot.SceneResetCount,
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
            "    A[Boot mass_navigation launcher] --> B[Run core MassNavigation runtime binding]",
            "    B --> C[Verify performer owners and minimap markers]",
            "    C --> D[Write LivePrimary selection]",
            "    D --> E[Submit massNavigationMove through OrderBuffer]",
            "    E --> F[Jump core minimap camera to remote 64km coordinate]",
            "    F --> G[Jump back to original area]",
            "    G --> H{No respawn, reset, marker drop, or old minimap path?}",
            "    H -->|yes| I[Write battle-report + trace + path + PNG timeline]",
            "    H -->|no| X[Fail MassNavigation UAT]"
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
        IReadOnlyList<MassNavigationAvoidanceFrameMetrics> avoidanceMetrics)
    {
        MassNavigationSnapshot boot = timeline[0];
        MassNavigationSnapshot final = timeline[^1];
        MassNavigationAvoidanceSummary avoidance = SummarizeMassNavigationAvoidance(avoidanceMetrics);
        return JsonSerializer.Serialize(new
        {
            scenario = "mass_navigation_large_world",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            success = acceptance.Success,
            normalized_signature = acceptance.NormalizedSignature,
            world_width_cm = boot.WorldWidthCm,
            world_height_cm = boot.WorldHeightCm,
            agent_count = boot.AgentCount,
            team_count = boot.TeamCount,
            blocker_count = boot.BlockerCount,
            hotspot_marker_count = boot.HotspotMarkerCount,
            performer_active_count = boot.PerformerActiveCount,
            minimap_marker_count = boot.MinimapBufferCount,
            minimap_dropped_total = boot.MinimapDroppedTotal,
            boot_scenario_spawn_count = boot.ScenarioSpawnCount,
            final_scenario_spawn_count = final.ScenarioSpawnCount,
            boot_scene_reset_count = boot.SceneResetCount,
            final_scene_reset_count = final.SceneResetCount,
            avoidance_frame_count = avoidance.FrameCount,
            avoidance_max_visible_agent_count = avoidance.MaxVisibleAgentCount,
            avoidance_max_play_area_agent_count = avoidance.MaxPlayAreaAgentCount,
            avoidance_max_selected_visible_agent_count = avoidance.MaxSelectedVisibleAgentCount,
            avoidance_max_heavy_agent_count = avoidance.MaxHeavyAgentCount,
            avoidance_peak_deep_overlap_pair_count = avoidance.PeakDeepOverlapPairCount,
            avoidance_final_deep_overlap_pair_count = avoidance.FinalDeepOverlapPairCount,
            avoidance_peak_max_penetration_ratio = avoidance.PeakMaxPenetrationRatio,
            avoidance_final_max_penetration_ratio = avoidance.FinalMaxPenetrationRatio,
            failed_checks = acceptance.FailedChecks
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void WriteMassNavigationSnapshotImage(MassNavigationSnapshot snapshot, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(MassNavigationImageWidth, MassNavigationImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(8, 12, 18));

        using var worldFillPaint = new SKPaint { Color = new SKColor(16, 30, 42), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var worldStrokePaint = new SKPaint { Color = new SKColor(72, 118, 150), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var solverPaint = new SKPaint { Color = new SKColor(255, 210, 84, 190), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
        using var flowPaint = new SKPaint { Color = new SKColor(80, 190, 255, 150), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var cameraPaint = new SKPaint { Color = new SKColor(255, 92, 92), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 24f };
        using var minorTextPaint = new SKPaint { Color = new SKColor(190, 205, 216), IsAntialias = true, TextSize = 18f };

        const int mapX = 36;
        const int mapY = 96;
        const int mapSize = 760;
        var worldRect = new SKRect(mapX, mapY, mapX + mapSize, mapY + mapSize);
        canvas.DrawRect(worldRect, worldFillPaint);
        canvas.DrawRect(worldRect, worldStrokePaint);

        DrawMassNavigationRect(canvas, snapshot.FlowWorkAreaCenterCm, snapshot.FlowWorkAreaWidthCm, snapshot.FlowWorkAreaHeightCm, snapshot, worldRect, flowPaint);
        DrawMassNavigationRect(canvas, snapshot.SolverWindowCenterCm, snapshot.SolverWindowWidthCm, snapshot.SolverWindowHeightCm, snapshot, worldRect, solverPaint);
        DrawCrosshair(canvas, ToMassNavigationScreen(snapshot.CameraTargetCm, snapshot, worldRect), 12f, cameraPaint);

        for (int i = 0; i < snapshot.SamplePositions.Count; i++)
        {
            MassNavigationAgentSample sample = snapshot.SamplePositions[i];
            using var teamPaint = new SKPaint { Color = ResolveMassNavigationTeamColor(sample.TeamId), IsAntialias = true, Style = SKPaintStyle.Fill };
            SKPoint point = ToMassNavigationScreen(sample.WorldCm, snapshot, worldRect);
            canvas.DrawCircle(point.X, point.Y, 2.5f, teamPaint);
        }

        canvas.DrawText($"MassNavigation Large World | {snapshot.Step} | tick={snapshot.Tick}", 36, 38, textPaint);
        canvas.DrawText($"World={snapshot.WorldWidthCm / 100000f:F1}km x {snapshot.WorldHeightCm / 100000f:F1}km  Camera={FormatPoint(snapshot.CameraTargetCm)}", 36, 68, minorTextPaint);
        canvas.DrawText($"Agents={snapshot.AgentCount} ECS={snapshot.EcsAgentCount} Teams={snapshot.TeamCount} Selected={snapshot.SelectedCount} Groups={snapshot.ActiveGroups}/{snapshot.ActiveOrderGroups}", 830, 130, minorTextPaint);
        canvas.DrawText($"Performers={snapshot.PerformerActiveCount} Payloads={snapshot.PerformerPayloadCount} Blockers={snapshot.BlockerCount} Hotspots={snapshot.HotspotMarkerCount}", 830, 160, minorTextPaint);
        canvas.DrawText($"Minimap visible={snapshot.MinimapVisible} preset={snapshot.MinimapPreset} markers={snapshot.MinimapVisibleMarkerCount}/{snapshot.MinimapMarkerCount} buffer={snapshot.MinimapBufferCount} dropped={snapshot.MinimapDroppedTotal}", 830, 190, minorTextPaint);
        canvas.DrawText($"Solver center={FormatPoint(snapshot.SolverWindowCenterCm)} driver={snapshot.SolverWindowDriver}", 830, 220, minorTextPaint);
        canvas.DrawText($"Flow area={FormatPoint(snapshot.FlowWorkAreaCenterCm)} {snapshot.FlowWorkAreaWidthCm:F0}x{snapshot.FlowWorkAreaHeightCm:F0} reason={snapshot.FlowWorkAreaReason}", 830, 250, minorTextPaint);
        canvas.DrawText($"LoadedChunks={snapshot.LoadedChunkCount} SpawnCount={snapshot.ScenarioSpawnCount} ResetCount={snapshot.SceneResetCount} Rejects={snapshot.CommandRejectsTotal}", 830, 280, minorTextPaint);
        canvas.DrawText($"Timing frame={snapshot.FrameMs:F3}ms sim={snapshot.SimulationMs:F3}ms pres={snapshot.PresentationMs:F3}ms performer={snapshot.PerformerMs:F3}ms minimap={snapshot.MinimapMs:F3}ms mass_navigation={snapshot.MassNavigationMs:F3}ms", 830, 310, minorTextPaint);

        int teamY = 354;
        foreach ((int teamId, int count) in snapshot.TeamCounts.OrderBy(pair => pair.Key))
        {
            using var teamPaint = new SKPaint { Color = ResolveMassNavigationTeamColor(teamId), IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawCircle(842, teamY - 6, 7f, teamPaint);
            canvas.DrawText($"Team {teamId}: {count}", 858, teamY, minorTextPaint);
            teamY += 26;
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
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

    private readonly record struct MassNavigationAvoidanceFrameMetrics(
        int FrameIndex,
        float WindowCenterXCm,
        float WindowCenterYCm,
        int UnitCount,
        int VisibleAgentCount,
        int PlayAreaAgentCount,
        int SelectedVisibleAgentCount,
        int HeavyAgentCount,
        int SettledAgentCount,
        float MaxPenetrationCm,
        float MaxPenetrationRatio,
        int DeepOverlapPairCount);

    private readonly record struct MassNavigationAvoidanceSummary(
        int FrameCount,
        int MaxVisibleAgentCount,
        int MaxPlayAreaAgentCount,
        int MaxSelectedVisibleAgentCount,
        int MaxHeavyAgentCount,
        int PeakDeepOverlapPairCount,
        int FinalDeepOverlapPairCount,
        float PeakMaxPenetrationRatio,
        float FinalMaxPenetrationRatio);

    private sealed class MassNavigationAvoidanceScratch
    {
        private readonly MassNavigationAvoidanceAgentSnapshot[] _agents;
        private readonly MassNavigationObstacleSnapshot[] _obstacles;
        private readonly MassNavigationAvoidanceAgentSnapshot[] _visibleAgents;
        private readonly MassNavigationAvoidanceAgentSnapshot[] _playAreaAgents;

        public MassNavigationAvoidanceScratch(int agentCapacity, int obstacleCapacity)
        {
            _agents = new MassNavigationAvoidanceAgentSnapshot[agentCapacity];
            _obstacles = new MassNavigationObstacleSnapshot[obstacleCapacity];
            _visibleAgents = new MassNavigationAvoidanceAgentSnapshot[agentCapacity];
            _playAreaAgents = new MassNavigationAvoidanceAgentSnapshot[agentCapacity];
        }

        public MassNavigationAvoidanceSnapshot Capture(MassNavigationSimulationRuntime simulation)
        {
            return simulation.CaptureAvoidanceSnapshot(_agents, _obstacles);
        }

        public ReadOnlySpan<MassNavigationAvoidanceAgentSnapshot> Agents(int count)
        {
            return _agents.AsSpan(0, count);
        }

        public ReadOnlySpan<MassNavigationObstacleSnapshot> Obstacles(int count)
        {
            return _obstacles.AsSpan(0, count);
        }

        public Span<MassNavigationAvoidanceAgentSnapshot> VisibleAgents(int count)
        {
            return _visibleAgents.AsSpan(0, count);
        }

        public Span<MassNavigationAvoidanceAgentSnapshot> PlayAreaAgents(int count)
        {
            return _playAreaAgents.AsSpan(0, count);
        }
    }

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
        string ActiveHotZoneId,
        int TeamCount,
        IReadOnlyList<int> TeamIds,
        IReadOnlyDictionary<int, int> TeamCounts,
        int AgentCount,
        int EcsAgentCount,
        int ControllableCount,
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
        int FocusBudgetUpdatesTotal,
        int CommandRejectsTotal,
        float FrameMs,
        float SimulationMs,
        float PresentationMs,
        float PerformerMs,
        float MinimapMs,
        float MassNavigationMs,
        IReadOnlyList<MassNavigationAgentSample> SamplePositions);

    private sealed record MassNavigationAcceptanceResult(
        bool Success,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> FailedChecks,
        string NormalizedSignature);

    private readonly record struct CaptureFrame(
        int Tick,
        string Step,
        string FileName,
        int CenterCount,
        int CenterStoppedAgents,
        float Team0CrossedFraction,
        float Team1CrossedFraction);
}
