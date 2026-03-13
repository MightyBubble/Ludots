using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using CameraAcceptanceMod;
using InteractionShowcaseMod;
using Ludots.Adapter.Raylib.Services;
using Ludots.Adapter.Web.Services;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
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
using Navigation2DPlaygroundMod;
using Navigation2DPlaygroundMod.Systems;
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
    private const int InteractionImageWidth = 1600;
    private const int InteractionImageHeight = 900;
    private const int NavImageWidth = 1600;
    private const int NavImageHeight = 900;
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

    public static Task<LauncherRecordingResult> RecordAsync(LauncherRecordingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(request.OutputDirectory);
        return InferScenario(request.Plan) switch
        {
            EvidenceScenario.CameraAcceptanceProjectionClick => Task.FromResult(RecordCameraAcceptanceProjection(request)),
            EvidenceScenario.InteractionShowcase => Task.FromResult(RecordInteractionShowcase(request)),
            EvidenceScenario.Navigation2DPlaygroundTimedAvoidance => Task.FromResult(RecordNavigation2DTimedAvoidance(request)),
            _ => throw new InvalidOperationException($"No recording scenario is registered for root mods: {string.Join(", ", request.Plan.RootModIds)}")
        };
    }

    private static EvidenceScenario InferScenario(LauncherLaunchPlan plan)
    {
        if (plan.RootModIds.Any(id => string.Equals(id, "CameraAcceptanceMod", StringComparison.OrdinalIgnoreCase)))
        {
            return EvidenceScenario.CameraAcceptanceProjectionClick;
        }

        if (plan.RootModIds.Any(id => string.Equals(id, InteractionShowcaseIds.ModId, StringComparison.OrdinalIgnoreCase)))
        {
            return EvidenceScenario.InteractionShowcase;
        }

        if (plan.RootModIds.Any(id => string.Equals(id, "Navigation2DPlaygroundMod", StringComparison.OrdinalIgnoreCase)))
        {
            return EvidenceScenario.Navigation2DPlaygroundTimedAvoidance;
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

        var uiRoot = new UIRoot();
        uiRoot.Resize(DefaultWidth, DefaultHeight);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UISystem, (Ludots.Core.UI.IUiSystem)new MarkupUiSystem(uiRoot));

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
        screenProjector.BindPresenter(cameraPresenter);

        engine.SetService(CoreServiceKeys.ViewController, viewController);
        engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)screenProjector);
        engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)new RaylibScreenRayProvider(cameraAdapter, DefaultWidth, DefaultHeight));

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

        var uiRoot = new UIRoot();
        uiRoot.Resize(DefaultWidth, DefaultHeight);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UISystem, (Ludots.Core.UI.IUiSystem)new MarkupUiSystem(uiRoot));

        var inputBackend = new ScriptedInputBackend();
        var inputHandler = new PlayerInputHandler(inputBackend, new InputConfigPipelineLoader(engine.ConfigPipeline).Load());
        PushStartupInputContexts(config, inputHandler);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)inputBackend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);

        var viewController = new WebViewController();
        viewController.SetResolution(DefaultWidth, DefaultHeight);
        var cameraAdapter = new WebCameraAdapter();
        var screenRayProvider = new WebScreenRayProvider(cameraAdapter, viewController);
        var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, viewController);
        var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter);
        screenProjector.BindPresenter(cameraPresenter);

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

        var overlayLines = ExtractOverlayText(runtime.Engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) as ScreenOverlayBuffer, clearAfterRead: true);
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

    private static LauncherRecordingResult RecordInteractionShowcase(LauncherRecordingRequest request)
    {
        using var runtime = CreateRuntime(request.Plan, request.BootstrapPath);
        if (!InteractionShowcaseIds.IsShowcaseMap(runtime.Config.StartupMapId))
        {
            throw new InvalidOperationException($"Interaction showcase recorder does not recognize startup map '{runtime.Config.StartupMapId}'.");
        }

        if (string.Equals(runtime.Config.StartupMapId, InteractionShowcaseIds.C1HostileUnitDamageMapId, StringComparison.OrdinalIgnoreCase))
        {
            return RecordInteractionC1HostileUnitDamage(request, runtime);
        }

        if (string.Equals(runtime.Config.StartupMapId, InteractionShowcaseIds.C2FriendlyUnitHealMapId, StringComparison.OrdinalIgnoreCase))
        {
            return RecordInteractionC2FriendlyUnitHeal(request, runtime);
        }

        if (string.Equals(runtime.Config.StartupMapId, InteractionShowcaseIds.C3AnyUnitConditionalMapId, StringComparison.OrdinalIgnoreCase))
        {
            return RecordInteractionC3AnyUnitConditional(request, runtime);
        }

        return RecordInteractionB1SelfBuff(request, runtime);
    }

    private static LauncherRecordingResult RecordInteractionB1SelfBuff(LauncherRecordingRequest request, RecordingRuntime runtime)
    {
        string screensDir = Path.Combine(request.OutputDirectory, "screens");
        Directory.CreateDirectory(screensDir);

        var frameTimesMs = new List<double>();
        var timeline = new List<InteractionSnapshot>();
        var captureFrames = new List<InteractionCaptureFrame>();

        InteractionSnapshot start = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.ActiveScenarioId, InteractionShowcaseIds.B1SelfBuffScenarioId, StringComparison.OrdinalIgnoreCase) && !snapshot.CastSubmitted,
            maxFrames: 180,
            expectation: "showcase warmup",
            sampleCurrentFrameFirst: true);
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, start, "000_start");

        InteractionSnapshot submitted = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => snapshot.CastSubmitted && string.Equals(snapshot.Stage, "order_submitted", StringComparison.OrdinalIgnoreCase),
            maxFrames: 180,
            expectation: "cast submission");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, submitted, "001_order_submitted");

        InteractionSnapshot active = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "buff_active", StringComparison.OrdinalIgnoreCase) &&
                snapshot.AttackDamage >= 149.999f &&
                snapshot.EmpoweredCount > 0 &&
                snapshot.EffectiveEmpoweredTag,
            maxFrames: 180,
            expectation: "buff activation");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, active, "002_buff_active");

        InteractionSnapshot expired = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => snapshot.Tick > active.Tick &&
                snapshot.BuffExpired &&
                snapshot.AttackDamage <= 100.001f &&
                snapshot.EmpoweredCount == 0 &&
                !snapshot.EffectiveEmpoweredTag,
            maxFrames: 900,
            expectation: "buff expiry");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, expired, "003_buff_expired");

        InteractionSnapshot silencedBlocked = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "silenced_blocked", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.LastCastFailReason, "BlockedByTag", StringComparison.Ordinal),
            maxFrames: 180,
            expectation: "silenced blocked branch");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, silencedBlocked, "004_silenced_blocked");

        InteractionSnapshot insufficientManaBlocked = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "insufficient_mana_blocked", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.LastCastFailReason, "InsufficientResource", StringComparison.Ordinal),
            maxFrames: 180,
            expectation: "insufficient mana branch");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, insufficientManaBlocked, "005_insufficient_mana");

        WriteTimelineSheet("Interaction showcase B1 self buff timeline", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        InteractionAcceptanceResult acceptance = EvaluateInteractionAcceptance(timeline);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildInteractionBattleReport(request, timeline, captureFrames, frameTimesMs, acceptance));
        File.WriteAllText(tracePath, BuildInteractionTraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildInteractionPathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildInteractionVisibleChecklist(captureFrames));
        File.WriteAllText(summaryPath, BuildInteractionSummaryJson(request, acceptance));

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

    private static LauncherRecordingResult RecordInteractionC1HostileUnitDamage(LauncherRecordingRequest request, RecordingRuntime runtime)
    {
        string screensDir = Path.Combine(request.OutputDirectory, "screens");
        Directory.CreateDirectory(screensDir);

        var frameTimesMs = new List<double>();
        var timeline = new List<InteractionSnapshot>();
        var captureFrames = new List<InteractionCaptureFrame>();

        InteractionSnapshot start = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.ActiveScenarioId, InteractionShowcaseIds.C1HostileUnitDamageScenarioId, StringComparison.OrdinalIgnoreCase) && !snapshot.CastSubmitted,
            maxFrames: 180,
            expectation: "showcase warmup",
            sampleCurrentFrameFirst: true);
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, start, "000_start");

        InteractionSnapshot submitted = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => snapshot.CastSubmitted &&
                string.Equals(snapshot.Stage, "order_submitted", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C1PrimaryTargetName, StringComparison.OrdinalIgnoreCase),
            maxFrames: 180,
            expectation: "cast submission");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, submitted, "001_order_submitted");

        InteractionSnapshot damageApplied = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "damage_applied", StringComparison.OrdinalIgnoreCase) &&
                snapshot.DamageApplied &&
                snapshot.PrimaryTargetHealth <= 300.001f &&
                snapshot.DamageAmount >= 299.999f &&
                snapshot.FinalDamage >= 199.999f,
            maxFrames: 180,
            expectation: "damage application");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, damageApplied, "002_damage_applied");

        InteractionSnapshot invalidTargetBlocked = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "invalid_target_blocked", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.LastCastFailReason, "InvalidTarget", StringComparison.Ordinal) &&
                string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C1InvalidTargetName, StringComparison.OrdinalIgnoreCase),
            maxFrames: 180,
            expectation: "invalid target branch");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, invalidTargetBlocked, "003_invalid_target_blocked");

        InteractionSnapshot outOfRangeBlocked = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "out_of_range_blocked", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.LastCastFailReason, "OutOfRange", StringComparison.Ordinal) &&
                string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C1FarTargetName, StringComparison.OrdinalIgnoreCase),
            maxFrames: 180,
            expectation: "out-of-range branch");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, outOfRangeBlocked, "004_out_of_range_blocked");

        WriteTimelineSheet("Interaction showcase C1 hostile unit damage timeline", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        C1InteractionAcceptanceResult acceptance = EvaluateInteractionC1Acceptance(timeline);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildInteractionC1BattleReport(request, timeline, captureFrames, frameTimesMs, acceptance));
        File.WriteAllText(tracePath, BuildInteractionC1TraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildInteractionC1PathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildInteractionC1VisibleChecklist(captureFrames));
        File.WriteAllText(summaryPath, BuildInteractionC1SummaryJson(request, acceptance));

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

    private static LauncherRecordingResult RecordInteractionC2FriendlyUnitHeal(LauncherRecordingRequest request, RecordingRuntime runtime)
    {
        string screensDir = Path.Combine(request.OutputDirectory, "screens");
        Directory.CreateDirectory(screensDir);

        var frameTimesMs = new List<double>();
        var timeline = new List<InteractionSnapshot>();
        var captureFrames = new List<InteractionCaptureFrame>();

        InteractionSnapshot start = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.ActiveScenarioId, InteractionShowcaseIds.C2FriendlyUnitHealScenarioId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.Stage, "warmup", StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(snapshot.Mana - 100f) <= 0.001f &&
                Math.Abs(snapshot.C2AllyTargetHealth - 200f) <= 0.001f &&
                Math.Abs(snapshot.C2HostileTargetHealth - 400f) <= 0.001f &&
                Math.Abs(snapshot.C2DeadAllyTargetHealth) <= 0.001f,
            maxFrames: 180,
            expectation: "showcase warmup",
            sampleCurrentFrameFirst: true);
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, start, "000_start");

        InteractionSnapshot submitted = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => snapshot.CastSubmitted &&
                string.Equals(snapshot.Stage, "order_submitted", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C2AllyTargetName, StringComparison.OrdinalIgnoreCase),
            maxFrames: 180,
            expectation: "cast submission");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, submitted, "001_order_submitted");

        InteractionSnapshot healApplied = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "heal_applied", StringComparison.OrdinalIgnoreCase) &&
                snapshot.C2HealApplied &&
                snapshot.C2AllyTargetHealth >= 349.999f &&
                snapshot.C2HealAmount >= 149.999f,
            maxFrames: 180,
            expectation: "heal application");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, healApplied, "002_heal_applied");

        InteractionSnapshot hostileTargetBlocked = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "hostile_target_blocked", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.LastCastFailReason, "InvalidTarget", StringComparison.Ordinal) &&
                string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C2HostileTargetName, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(snapshot.C2HostileTargetHealth - 400f) <= 0.001f,
            maxFrames: 180,
            expectation: "hostile target blocked branch");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, hostileTargetBlocked, "003_hostile_target_blocked");

        InteractionSnapshot deadAllyBlocked = AdvanceUntilInteraction(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "dead_ally_blocked", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.LastCastFailReason, "InvalidTarget", StringComparison.Ordinal) &&
                string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C2DeadAllyTargetName, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(snapshot.C2DeadAllyTargetHealth) <= 0.001f,
            maxFrames: 180,
            expectation: "dead ally blocked branch");
        CaptureInteractionSnapshot(screensDir, timeline, captureFrames, deadAllyBlocked, "004_dead_ally_blocked");

        WriteTimelineSheet("Interaction showcase C2 friendly unit heal timeline", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        C2InteractionAcceptanceResult acceptance = EvaluateInteractionC2Acceptance(timeline);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildInteractionC2BattleReport(request, timeline, captureFrames, frameTimesMs, acceptance));
        File.WriteAllText(tracePath, BuildInteractionC2TraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildInteractionC2PathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildInteractionC2VisibleChecklist(captureFrames));
        File.WriteAllText(summaryPath, BuildInteractionC2SummaryJson(request, acceptance));

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

    private static LauncherRecordingResult RecordInteractionC3AnyUnitConditional(LauncherRecordingRequest request, RecordingRuntime runtime)
    {
        string screensDir = Path.Combine(request.OutputDirectory, "screens");
        Directory.CreateDirectory(screensDir);

        var frameTimesMs = new List<double>();
        var timeline = new List<C3InteractionSnapshot>();
        var captureFrames = new List<C3InteractionCaptureFrame>();

        C3InteractionSnapshot start = AdvanceUntilInteractionC3(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.ActiveScenarioId, InteractionShowcaseIds.C3AnyUnitConditionalScenarioId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.Stage, "warmup", StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(snapshot.Mana - 100f) <= 0.001f &&
                Math.Abs(snapshot.HostileMoveSpeed - 200f) <= 0.001f &&
                Math.Abs(snapshot.FriendlyMoveSpeed - 180f) <= 0.001f &&
                !snapshot.HostilePolymorphActive &&
                !snapshot.FriendlyHasteActive,
            maxFrames: 180,
            expectation: "showcase warmup",
            sampleCurrentFrameFirst: true);
        CaptureInteractionC3Snapshot(screensDir, timeline, captureFrames, start, "000_start");

        C3InteractionSnapshot hostileSubmitted = AdvanceUntilInteractionC3(
            runtime,
            frameTimesMs,
            snapshot => snapshot.CastSubmitted &&
                string.Equals(snapshot.Stage, "hostile_order_submitted", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C3HostileTargetName, StringComparison.OrdinalIgnoreCase),
            maxFrames: 180,
            expectation: "hostile branch cast submission");
        CaptureInteractionC3Snapshot(screensDir, timeline, captureFrames, hostileSubmitted, "001_hostile_order_submitted");

        C3InteractionSnapshot hostileApplied = AdvanceUntilInteractionC3(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "hostile_polymorph_applied", StringComparison.OrdinalIgnoreCase) &&
                snapshot.HostilePolymorphApplied &&
                snapshot.HostilePolymorphActive &&
                snapshot.HostilePolymorphCount > 0 &&
                snapshot.HostileMoveSpeed <= 80.001f &&
                !snapshot.FriendlyHasteActive,
            maxFrames: 180,
            expectation: "hostile polymorph branch");
        CaptureInteractionC3Snapshot(screensDir, timeline, captureFrames, hostileApplied, "002_hostile_polymorph_applied");

        C3InteractionSnapshot friendlySubmitted = AdvanceUntilInteractionC3(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "friendly_order_submitted", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C3FriendlyTargetName, StringComparison.OrdinalIgnoreCase),
            maxFrames: 180,
            expectation: "friendly branch cast submission");
        CaptureInteractionC3Snapshot(screensDir, timeline, captureFrames, friendlySubmitted, "003_friendly_order_submitted");

        C3InteractionSnapshot friendlyApplied = AdvanceUntilInteractionC3(
            runtime,
            frameTimesMs,
            snapshot => string.Equals(snapshot.Stage, "friendly_haste_applied", StringComparison.OrdinalIgnoreCase) &&
                snapshot.FriendlyHasteApplied &&
                snapshot.FriendlyHasteActive &&
                snapshot.FriendlyHasteCount > 0 &&
                snapshot.FriendlyMoveSpeed >= 259.999f &&
                snapshot.HostilePolymorphActive,
            maxFrames: 180,
            expectation: "friendly haste branch");
        CaptureInteractionC3Snapshot(screensDir, timeline, captureFrames, friendlyApplied, "004_friendly_haste_applied");

        WriteTimelineSheet("Interaction showcase C3 any unit conditional timeline", captureFrames, screensDir, Path.Combine(screensDir, "timeline.png"));

        C3InteractionAcceptanceResult acceptance = EvaluateInteractionC3Acceptance(timeline);
        string battleReportPath = Path.Combine(request.OutputDirectory, "battle-report.md");
        string tracePath = Path.Combine(request.OutputDirectory, "trace.jsonl");
        string pathPath = Path.Combine(request.OutputDirectory, "path.mmd");
        string visibleChecklistPath = Path.Combine(request.OutputDirectory, "visible-checklist.md");
        string summaryPath = Path.Combine(request.OutputDirectory, "summary.json");

        File.WriteAllText(battleReportPath, BuildInteractionC3BattleReport(request, timeline, captureFrames, frameTimesMs, acceptance));
        File.WriteAllText(tracePath, BuildInteractionC3TraceJsonl(request.Plan.AdapterId, timeline));
        File.WriteAllText(pathPath, BuildInteractionC3PathMermaid());
        File.WriteAllText(visibleChecklistPath, BuildInteractionC3VisibleChecklist(captureFrames));
        File.WriteAllText(summaryPath, BuildInteractionC3SummaryJson(request, acceptance));

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

    private static C3InteractionSnapshot AdvanceUntilInteractionC3(
        RecordingRuntime runtime,
        List<double> frameTimesMs,
        Func<C3InteractionSnapshot, bool> predicate,
        int maxFrames,
        string expectation,
        bool sampleCurrentFrameFirst = false)
    {
        if (sampleCurrentFrameFirst)
        {
            C3InteractionSnapshot snapshot = SampleInteractionC3Snapshot(runtime, "probe", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d);
            if (predicate(snapshot))
            {
                return snapshot;
            }
        }

        for (int frame = 1; frame <= maxFrames; frame++)
        {
            Tick(runtime, 1, frameTimesMs);
            C3InteractionSnapshot snapshot = SampleInteractionC3Snapshot(runtime, "probe", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d);
            if (predicate(snapshot))
            {
                return snapshot;
            }
        }

        throw new InvalidOperationException($"Interaction showcase did not reach '{expectation}' within {maxFrames} frames.");
    }

    private static void CaptureInteractionC3Snapshot(
        string screensDir,
        List<C3InteractionSnapshot> timeline,
        List<C3InteractionCaptureFrame> captureFrames,
        C3InteractionSnapshot snapshot,
        string step)
    {
        C3InteractionSnapshot captured = snapshot with { Step = step };
        timeline.Add(captured);
        string fileName = $"{step}.png";
        string outputPath = Path.Combine(screensDir, fileName);
        WriteInteractionC3SnapshotImage(captured, outputPath);
        captureFrames.Add(new C3InteractionCaptureFrame(
            captured.Tick,
            step,
            fileName,
            captured.ActiveScenarioId,
            captured.Stage,
            captured.Mana,
            captured.HostileMoveSpeed,
            captured.FriendlyMoveSpeed,
            captured.HostilePolymorphActive,
            captured.FriendlyHasteActive,
            captured.LastCastFailReason,
            captured.LastAttemptTargetName));
    }

    private static C3InteractionSnapshot SampleInteractionC3Snapshot(RecordingRuntime runtime, string step, double tickMs)
    {
        var namedEntities = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        Entity hero = Entity.Null;

        runtime.Engine.World.Query(in CameraNamedEntityQuery, (Entity entity, ref Name name, ref WorldPositionCm position) =>
        {
            string entityName = name.Value;
            if (!namedEntities.ContainsKey(entityName))
            {
                namedEntities[entityName] = position.Value.ToVector2();
            }

            if (hero == Entity.Null && string.Equals(entityName, InteractionShowcaseIds.HeroName, StringComparison.OrdinalIgnoreCase))
            {
                hero = entity;
            }
        });

        float mana = 0f;
        if (hero != Entity.Null && runtime.Engine.World.IsAlive(hero))
        {
            int manaAttributeId = AttributeRegistry.Register("Mana");
            if (runtime.Engine.World.Has<AttributeBuffer>(hero))
            {
                ref var attributes = ref runtime.Engine.World.Get<AttributeBuffer>(hero);
                mana = attributes.GetCurrent(manaAttributeId);
            }
        }

        return new C3InteractionSnapshot(
            Tick: runtime.Engine.GameSession.CurrentTick,
            Step: step,
            TickMs: tickMs,
            ActiveMapId: runtime.Engine.CurrentMapSession?.MapId.ToString() ?? runtime.Config.StartupMapId,
            ActiveScenarioId: ReadGlobalString(runtime.Engine, InteractionShowcaseRuntimeKeys.ActiveScenarioId, "(unset)"),
            ScriptTick: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.ScriptTick, 0),
            Stage: ReadGlobalString(runtime.Engine, InteractionShowcaseRuntimeKeys.Stage, "not_started"),
            HeroPresent: hero != Entity.Null && runtime.Engine.World.IsAlive(hero),
            Mana: mana,
            HostileMoveSpeed: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.C3HostileTargetMoveSpeed, 0f),
            FriendlyMoveSpeed: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.C3FriendlyTargetMoveSpeed, 0f),
            HostilePolymorphActive: ReadGlobalBool(runtime.Engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphActive),
            HostilePolymorphCount: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphCount, 0),
            HostilePolymorphApplied: ReadGlobalBool(runtime.Engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphApplied),
            HostilePolymorphAppliedTick: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphAppliedTick, -1),
            FriendlyHasteActive: ReadGlobalBool(runtime.Engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteActive),
            FriendlyHasteCount: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteCount, 0),
            FriendlyHasteApplied: ReadGlobalBool(runtime.Engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteApplied),
            FriendlyHasteAppliedTick: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteAppliedTick, -1),
            CastSubmitted: ReadGlobalBool(runtime.Engine, InteractionShowcaseRuntimeKeys.CastSubmitted),
            CastSubmittedTick: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.CastSubmittedTick, -1),
            LastAttemptTargetName: ReadGlobalString(runtime.Engine, InteractionShowcaseRuntimeKeys.LastAttemptTargetName, string.Empty),
            LastCastFailReason: ReadGlobalString(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty),
            NamedEntities: namedEntities,
            OverlayLines: ExtractOverlayText(runtime.Engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) as ScreenOverlayBuffer, clearAfterRead: true));
    }

    private static C3InteractionAcceptanceResult EvaluateInteractionC3Acceptance(IReadOnlyList<C3InteractionSnapshot> timeline)
    {
        C3InteractionSnapshot start = timeline[0];
        C3InteractionSnapshot hostileSubmitted = timeline[1];
        C3InteractionSnapshot hostileApplied = timeline[2];
        C3InteractionSnapshot friendlySubmitted = timeline[3];
        C3InteractionSnapshot friendlyApplied = timeline[4];

        var failures = new List<string>();
        AddAcceptanceCheck(string.Equals(start.ActiveScenarioId, InteractionShowcaseIds.C3AnyUnitConditionalScenarioId, StringComparison.OrdinalIgnoreCase),
            $"Expected active scenario '{InteractionShowcaseIds.C3AnyUnitConditionalScenarioId}', but saw '{start.ActiveScenarioId}'.", failures);
        AddAcceptanceCheck(string.Equals(start.ActiveMapId, InteractionShowcaseIds.C3AnyUnitConditionalMapId, StringComparison.OrdinalIgnoreCase),
            $"Expected active map '{InteractionShowcaseIds.C3AnyUnitConditionalMapId}', but saw '{start.ActiveMapId}'.", failures);
        AddAcceptanceCheck(start.HeroPresent,
            "Hero should be present in the C3 showcase map.", failures);
        AddAcceptanceCheck(Math.Abs(start.Mana - 100f) <= 0.001f &&
                Math.Abs(start.HostileMoveSpeed - 200f) <= 0.001f &&
                Math.Abs(start.FriendlyMoveSpeed - 180f) <= 0.001f &&
                !start.HostilePolymorphActive &&
                !start.FriendlyHasteActive,
            $"C3 should start at Mana=100, hostileMS=200, friendlyMS=180 with no active tags, but saw mana={start.Mana:F1} hostileMS={start.HostileMoveSpeed:F1} friendlyMS={start.FriendlyMoveSpeed:F1} hostileActive={start.HostilePolymorphActive} friendlyActive={start.FriendlyHasteActive}.",
            failures);
        AddAcceptanceCheck(hostileSubmitted.CastSubmitted &&
                string.Equals(hostileSubmitted.LastAttemptTargetName, InteractionShowcaseIds.C3HostileTargetName, StringComparison.OrdinalIgnoreCase),
            $"Autoplay should submit the first C3 cast at '{InteractionShowcaseIds.C3HostileTargetName}', but saw castSubmitted={hostileSubmitted.CastSubmitted} target='{hostileSubmitted.LastAttemptTargetName}'.", failures);
        AddAcceptanceCheck(hostileApplied.HostilePolymorphApplied &&
                hostileApplied.HostilePolymorphActive &&
                hostileApplied.HostilePolymorphCount == 1 &&
                Math.Abs(hostileApplied.HostileMoveSpeed - 80f) <= 0.001f,
            $"Hostile branch should set MoveSpeed=80 with Status.Polymorphed, but capture saw moveSpeed={hostileApplied.HostileMoveSpeed:F3} active={hostileApplied.HostilePolymorphActive} count={hostileApplied.HostilePolymorphCount}.", failures);
        AddAcceptanceCheck(!hostileApplied.FriendlyHasteActive &&
                hostileApplied.FriendlyHasteCount == 0 &&
                Math.Abs(hostileApplied.FriendlyMoveSpeed - 180f) <= 0.001f,
            $"Hostile branch must not apply friendly haste, but capture saw friendlyMS={hostileApplied.FriendlyMoveSpeed:F3} active={hostileApplied.FriendlyHasteActive} count={hostileApplied.FriendlyHasteCount}.", failures);
        AddAcceptanceCheck(string.Equals(friendlySubmitted.LastAttemptTargetName, InteractionShowcaseIds.C3FriendlyTargetName, StringComparison.OrdinalIgnoreCase),
            $"Second cast should target '{InteractionShowcaseIds.C3FriendlyTargetName}', but capture saw '{friendlySubmitted.LastAttemptTargetName}'.", failures);
        AddAcceptanceCheck(friendlyApplied.FriendlyHasteApplied &&
                friendlyApplied.FriendlyHasteActive &&
                friendlyApplied.FriendlyHasteCount == 1 &&
                Math.Abs(friendlyApplied.FriendlyMoveSpeed - 260f) <= 0.001f,
            $"Friendly branch should set MoveSpeed=260 with Status.Hasted, but capture saw moveSpeed={friendlyApplied.FriendlyMoveSpeed:F3} active={friendlyApplied.FriendlyHasteActive} count={friendlyApplied.FriendlyHasteCount}.", failures);
        AddAcceptanceCheck(friendlyApplied.HostilePolymorphActive &&
                friendlyApplied.HostilePolymorphCount == 1 &&
                Math.Abs(friendlyApplied.HostileMoveSpeed - 80f) <= 0.001f,
            $"Friendly branch must not remove hostile polymorph during the capture window, but capture saw hostileMS={friendlyApplied.HostileMoveSpeed:F3} active={friendlyApplied.HostilePolymorphActive} count={friendlyApplied.HostilePolymorphCount}.", failures);

        string normalizedSignature = string.Join("|", new[]
        {
            "interaction_c3_any_unit_conditional",
            $"hostile_submitted:{hostileSubmitted.Tick}:{hostileSubmitted.LastAttemptTargetName}",
            $"hostile_applied:{(hostileApplied.HostilePolymorphAppliedTick >= 0 ? hostileApplied.HostilePolymorphAppliedTick : hostileApplied.Tick)}:{MathF.Round(hostileApplied.HostileMoveSpeed):F0}:{hostileApplied.HostilePolymorphCount}",
            $"friendly_submitted:{friendlySubmitted.Tick}:{friendlySubmitted.LastAttemptTargetName}",
            $"friendly_applied:{(friendlyApplied.FriendlyHasteAppliedTick >= 0 ? friendlyApplied.FriendlyHasteAppliedTick : friendlyApplied.Tick)}:{MathF.Round(friendlyApplied.FriendlyMoveSpeed):F0}:{friendlyApplied.FriendlyHasteCount}"
        });

        int hostileEffectTick = hostileApplied.HostilePolymorphAppliedTick >= 0
            ? hostileApplied.HostilePolymorphAppliedTick
            : hostileApplied.Tick;
        int friendlyEffectTick = friendlyApplied.FriendlyHasteAppliedTick >= 0
            ? friendlyApplied.FriendlyHasteAppliedTick
            : friendlyApplied.Tick;
        string verdict = failures.Count == 0
            ? $"C3 any unit conditional passes: the same ability polymorphs the hostile target at tick {hostileEffectTick}, then hastes the friendly target at tick {friendlyEffectTick} without cross-applying the wrong branch."
            : "C3 any unit conditional fails: ability routing, relation-filtered branches, or visual checkpoints diverged from the scenario contract.";
        string failureSummary = failures.Count == 0 ? verdict : string.Join(Environment.NewLine, failures);

        return new C3InteractionAcceptanceResult(
            Success: failures.Count == 0,
            Verdict: verdict,
            FailureSummary: failureSummary,
            FailedChecks: failures,
            StartMana: start.Mana,
            StartHostileMoveSpeed: start.HostileMoveSpeed,
            StartFriendlyMoveSpeed: start.FriendlyMoveSpeed,
            HostileMoveSpeed: hostileApplied.HostileMoveSpeed,
            FriendlyMoveSpeed: friendlyApplied.FriendlyMoveSpeed,
            HostilePolymorphCount: hostileApplied.HostilePolymorphCount,
            FriendlyHasteCount: friendlyApplied.FriendlyHasteCount,
            SubmittedHostileTick: hostileSubmitted.Tick,
            HostileCaptureTick: hostileApplied.Tick,
            HostileAppliedTick: hostileEffectTick,
            SubmittedFriendlyTick: friendlySubmitted.Tick,
            FriendlyCaptureTick: friendlyApplied.Tick,
            FriendlyAppliedTick: friendlyEffectTick,
            NormalizedSignature: normalizedSignature);
    }

    private static string BuildInteractionC3BattleReport(
        LauncherRecordingRequest request,
        IReadOnlyList<C3InteractionSnapshot> timeline,
        IReadOnlyList<C3InteractionCaptureFrame> captureFrames,
        IReadOnlyList<double> frameTimesMs,
        C3InteractionAcceptanceResult acceptance)
    {
        double medianTickMs = Median(frameTimesMs.ToArray());
        double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
        string evidenceImages = string.Join(", ", captureFrames.Select(frame => $"`screens/{frame.FileName}`").Append("`screens/timeline.png`"));

        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: interaction-c3-any-unit-conditional");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: use one explicit-target skill on different unit relations and prove that hostile/friendly targets receive different reusable GAS effects.");
        sb.AppendLine("- Gameplay domain: launcher-started real mod bootstrap, real `OrderQueue` submission, real `Ability.Interaction.C3AnyUnitConditional` execution with two search-wrapper effects at `tick=0`.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none");
        sb.AppendLine("- Map: `mods/InteractionShowcaseMod/assets/Maps/interaction_c3_any_unit_conditional.json`");
        sb.AppendLine("- Clock profile: fixed `1/60s`");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Launch command: `{request.CommandText}`");
        sb.AppendLine($"- Evidence images: {evidenceImages}");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Boot the launcher runtime with `InteractionShowcaseMod` rooted to the C3 showcase map.");
        sb.AppendLine($"2. Let autoplay warm up and submit slot `0` against `{InteractionShowcaseIds.C3HostileTargetName}`.");
        sb.AppendLine($"3. Capture the hostile branch after `Effect.Interaction.C3HostileConditionalSearch` fans out and dispatches `Effect.Interaction.C3HostilePolymorph`, reducing target MoveSpeed to `80`.");
        sb.AppendLine($"4. Submit the same slot `0` against `{InteractionShowcaseIds.C3FriendlyTargetName}`.");
        sb.AppendLine($"5. Capture the friendly branch after `Effect.Interaction.C3FriendlyConditionalSearch` fans out and dispatches `Effect.Interaction.C3FriendlyHaste`, raising target MoveSpeed to `260`.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (C3InteractionSnapshot snapshot in timeline)
        {
            string overlay = snapshot.OverlayLines.Count == 0 ? "(none)" : snapshot.OverlayLines[0];
            sb.AppendLine($"- [T+{snapshot.Tick:000}] InteractionShowcase.{snapshot.Step} -> stage={snapshot.Stage} | mana={snapshot.Mana:F1} | hostileMS={snapshot.HostileMoveSpeed:F1} | hostilePoly={snapshot.HostilePolymorphActive}/{snapshot.HostilePolymorphCount} | hostileEffectTick={snapshot.HostilePolymorphAppliedTick} | friendlyMS={snapshot.FriendlyMoveSpeed:F1} | friendlyHaste={snapshot.FriendlyHasteActive}/{snapshot.FriendlyHasteCount} | friendlyEffectTick={snapshot.FriendlyHasteAppliedTick} | target={FormatInteractionDisplayValue(snapshot.LastAttemptTargetName, "-")} | overlay={overlay}");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        foreach (string failedCheck in acceptance.FailedChecks)
        {
            sb.AppendLine($"- failed-check: {failedCheck}");
        }

        sb.AppendLine("- runtime note: both successful branches are native GAS execution through search-wrapper fan-out; there is no showcase-local relation guard deciding hostile vs friendly.");
        sb.AppendLine("- implementation note: this delivery replaces the earlier doc's non-existent graph conditional branch with a reuse-first `Search + targetDispatch payload` pattern.");
        sb.AppendLine("- timing note: `002_hostile_polymorph_applied` is the first visual capture that shows the completed hostile branch, but the overlay/runtime key still reports the actual hostile effect application at tick `6`; `summary.json` stores both capture tick and applied tick.");
        sb.AppendLine("- tech-debt: `TD-2026-03-13-C3-DirectExplicitTargetRelationFilterGap` -> `artifacts/techdebt/2026-03-13-c3-direct-explicit-target-relation-filter-gap.md`");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- screenshot captures: `{captureFrames.Count}`");
        sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
        sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
        sb.AppendLine($"- hostile submitted tick: `{acceptance.SubmittedHostileTick}`");
        sb.AppendLine($"- hostile capture tick: `{acceptance.HostileCaptureTick}`");
        sb.AppendLine($"- hostile applied tick: `{acceptance.HostileAppliedTick}`");
        sb.AppendLine($"- friendly submitted tick: `{acceptance.SubmittedFriendlyTick}`");
        sb.AppendLine($"- friendly capture tick: `{acceptance.FriendlyCaptureTick}`");
        sb.AppendLine($"- friendly applied tick: `{acceptance.FriendlyAppliedTick}`");
        sb.AppendLine($"- normalized signature: `{acceptance.NormalizedSignature}`");
        sb.AppendLine("- reusable wiring: `GameBootstrapper`, `ConfigPipeline`, `OrderQueue`, `AbilityExecSystem`, `EffectProcessingLoopSystem`, `EffectPresetType.Buff`, `targetFilter.relationFilter`, `ScreenOverlayBuffer`");
        return sb.ToString();
    }

    private static string BuildInteractionC3TraceJsonl(string adapterId, IReadOnlyList<C3InteractionSnapshot> timeline)
    {
        var lines = new List<string>(timeline.Count);
        for (int index = 0; index < timeline.Count; index++)
        {
            C3InteractionSnapshot snapshot = timeline[index];
            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"interaction-c3-{adapterId}-{index + 1:000}",
                tick = snapshot.Tick,
                step = snapshot.Step,
                script_tick = snapshot.ScriptTick,
                stage = snapshot.Stage,
                cast_submitted = snapshot.CastSubmitted,
                mana = Math.Round(snapshot.Mana, 2),
                hostile_target_move_speed = Math.Round(snapshot.HostileMoveSpeed, 2),
                hostile_polymorph_active = snapshot.HostilePolymorphActive,
                hostile_polymorph_count = snapshot.HostilePolymorphCount,
                hostile_polymorph_applied = snapshot.HostilePolymorphApplied,
                hostile_polymorph_applied_tick = snapshot.HostilePolymorphAppliedTick,
                friendly_target_move_speed = Math.Round(snapshot.FriendlyMoveSpeed, 2),
                friendly_haste_active = snapshot.FriendlyHasteActive,
                friendly_haste_count = snapshot.FriendlyHasteCount,
                friendly_haste_applied = snapshot.FriendlyHasteApplied,
                friendly_haste_applied_tick = snapshot.FriendlyHasteAppliedTick,
                last_attempt_target_name = snapshot.LastAttemptTargetName,
                last_cast_fail_reason = snapshot.LastCastFailReason,
                status = "done"
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildInteractionC3PathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart LR",
            "    A[Boot launcher runtime for InteractionShowcaseMod] --> B[Autoplay warmup on interaction_c3_any_unit_conditional]",
            "    B --> C[OrderQueue submits slot 0 on C3EnemyPrimary]",
            "    C --> D[Ability emits C3HostileConditionalSearch + C3FriendlyConditionalSearch]",
            "    D --> E[Hostile search fan-out passes, friendly search skips]",
            "    E --> F[Enemy MoveSpeed 200 -> 80 and Status.Polymorphed]",
            "    F --> G[OrderQueue submits same slot 0 on C3AllyPrimary]",
            "    G --> H[Ability emits same two search wrappers again]",
            "    H --> I[Friendly search fan-out passes, hostile search skips]",
            "    I --> J[Ally MoveSpeed 180 -> 260 and Status.Hasted]",
            "    J --> K[Write battle-report + trace + path + PNG timeline]"
        }) + Environment.NewLine;
    }

    private static string BuildInteractionC3VisibleChecklist(IReadOnlyList<C3InteractionCaptureFrame> frames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: interaction-c3-any-unit-conditional");
        sb.AppendLine();
        sb.AppendLine("- `000_start` should show the hero plus two explicit targets, with hostile MoveSpeed `200` and friendly MoveSpeed `180`.");
        sb.AppendLine("- `001_hostile_order_submitted` should highlight `C3EnemyPrimary` as the selected target.");
        sb.AppendLine("- `002_hostile_polymorph_applied` should show a hostile-only debuff cue and the hostile MoveSpeed reduced to `80`.");
        sb.AppendLine("- `003_friendly_order_submitted` should highlight `C3AllyPrimary` while the hostile target remains polymorphed.");
        sb.AppendLine("- `004_friendly_haste_applied` should show a friendly-only buff cue and the friendly MoveSpeed raised to `260`.");
        sb.AppendLine("- same ability note: the five frames together must prove relation-dependent branching from one skill, not two separate skills.");
        sb.AppendLine("- workaround note: launcher evidence should match the documented `Search + targetDispatch payload` pattern, not a direct explicit-target `relationFilter` claim.");
        sb.AppendLine("- visual note: in `003_friendly_order_submitted` and `004_friendly_haste_applied`, the hostile `POLYMORPH` floating label rises in screen space between the two targets and can look visually close to `C3AllyPrimary`; approve branch isolation by the overlay `PolyActive/FriendlyMS/HasteActive` values, not by label proximity alone.");
        sb.AppendLine("- timing note: `002_hostile_polymorph_applied` is captured at the first visual checkpoint that shows the completed hostile state; the same frame overlay still reports the actual hostile apply tick separately as `HostileTick=6`.");
        sb.AppendLine("- `screens/timeline.png` gives the five-frame strip for quick approval and Claude review.");
        sb.AppendLine();
        foreach (C3InteractionCaptureFrame frame in frames)
        {
            sb.AppendLine($"- `{frame.FileName}`: stage={frame.Stage}, hostileMS={frame.HostileMoveSpeed:F1}, hostilePoly={frame.HostilePolymorphActive}, friendlyMS={frame.FriendlyMoveSpeed:F1}, friendlyHaste={frame.FriendlyHasteActive}, fail={FormatInteractionDisplayValue(frame.LastCastFailReason, "None")}, target={FormatInteractionDisplayValue(frame.LastAttemptTargetName, "-")}");
        }

        return sb.ToString();
    }

    private static string BuildInteractionC3SummaryJson(LauncherRecordingRequest request, C3InteractionAcceptanceResult acceptance)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = "interaction_c3_any_unit_conditional",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            submitted_hostile_tick = acceptance.SubmittedHostileTick,
            hostile_capture_tick = acceptance.HostileCaptureTick,
            hostile_applied_tick = acceptance.HostileAppliedTick,
            submitted_friendly_tick = acceptance.SubmittedFriendlyTick,
            friendly_capture_tick = acceptance.FriendlyCaptureTick,
            friendly_applied_tick = acceptance.FriendlyAppliedTick,
            start_mana = Math.Round(acceptance.StartMana, 2),
            start_hostile_move_speed = Math.Round(acceptance.StartHostileMoveSpeed, 2),
            start_friendly_move_speed = Math.Round(acceptance.StartFriendlyMoveSpeed, 2),
            hostile_move_speed = Math.Round(acceptance.HostileMoveSpeed, 2),
            friendly_move_speed = Math.Round(acceptance.FriendlyMoveSpeed, 2),
            hostile_polymorph_count = acceptance.HostilePolymorphCount,
            friendly_haste_count = acceptance.FriendlyHasteCount,
            hostile_target_name = InteractionShowcaseIds.C3HostileTargetName,
            friendly_target_name = InteractionShowcaseIds.C3FriendlyTargetName,
            validation_scope = new
            {
                hostile_branch = "native_gas_search_wrapper_relation_filter_hostile",
                friendly_branch = "native_gas_search_wrapper_relation_filter_friendly",
                direct_explicit_target_relation_filter = "gap_detected_workaround_in_use",
                core_changes = "none"
            },
            tech_debt_id = "TD-2026-03-13-C3-DirectExplicitTargetRelationFilterGap",
            mana_cost_configured = false,
            normalized_signature = acceptance.NormalizedSignature
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static InteractionSnapshot AdvanceUntilInteraction(
        RecordingRuntime runtime,
        List<double> frameTimesMs,
        Func<InteractionSnapshot, bool> predicate,
        int maxFrames,
        string expectation,
        bool sampleCurrentFrameFirst = false)
    {
        if (sampleCurrentFrameFirst)
        {
            InteractionSnapshot snapshot = SampleInteractionSnapshot(runtime, "probe", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d);
            if (predicate(snapshot))
            {
                return snapshot;
            }
        }

        for (int frame = 1; frame <= maxFrames; frame++)
        {
            Tick(runtime, 1, frameTimesMs);
            InteractionSnapshot snapshot = SampleInteractionSnapshot(runtime, "probe", frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d);
            if (predicate(snapshot))
            {
                return snapshot;
            }
        }

        throw new InvalidOperationException($"Interaction showcase did not reach '{expectation}' within {maxFrames} frames.");
    }

    private static void CaptureInteractionSnapshot(
        string screensDir,
        List<InteractionSnapshot> timeline,
        List<InteractionCaptureFrame> captureFrames,
        InteractionSnapshot snapshot,
        string step)
    {
        InteractionSnapshot captured = snapshot with { Step = step };
        timeline.Add(captured);
        string fileName = $"{step}.png";
        string outputPath = Path.Combine(screensDir, fileName);
        WriteInteractionSnapshotImage(captured, outputPath);
        captureFrames.Add(new InteractionCaptureFrame(
            captured.Tick,
            step,
            fileName,
            captured.ActiveScenarioId,
            captured.Stage,
            captured.AttackDamage,
            captured.Mana,
            captured.EmpoweredCount,
            captured.LastCastFailReason,
            captured.PrimaryTargetHealth,
            captured.InvalidTargetHealth,
            captured.FarTargetHealth,
            captured.DamageAmount,
            captured.FinalDamage,
            captured.DamageApplied,
            captured.LastAttemptTargetName,
            captured.C2AllyTargetHealth,
            captured.C2HostileTargetHealth,
            captured.C2DeadAllyTargetHealth,
            captured.C2HealAmount,
            captured.C2HealApplied,
            captured.C2HealAppliedTick));
    }

    private static InteractionSnapshot SampleInteractionSnapshot(RecordingRuntime runtime, string step, double tickMs)
    {
        var namedEntities = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        Entity hero = Entity.Null;

        runtime.Engine.World.Query(in CameraNamedEntityQuery, (Entity entity, ref Name name, ref WorldPositionCm position) =>
        {
            string entityName = name.Value;
            if (!namedEntities.ContainsKey(entityName))
            {
                namedEntities[entityName] = position.Value.ToVector2();
            }

            if (hero == Entity.Null && string.Equals(entityName, InteractionShowcaseIds.HeroName, StringComparison.OrdinalIgnoreCase))
            {
                hero = entity;
            }
        });

        float attackDamage = 0f;
        float mana = 0f;
        bool effectiveEmpoweredTag = false;
        int empoweredCount = 0;
        if (hero != Entity.Null && runtime.Engine.World.IsAlive(hero))
        {
            int attackDamageAttributeId = AttributeRegistry.Register("AttackDamage");
            int manaAttributeId = AttributeRegistry.Register("Mana");
            if (runtime.Engine.World.Has<AttributeBuffer>(hero))
            {
                ref var attributes = ref runtime.Engine.World.Get<AttributeBuffer>(hero);
                attackDamage = attributes.GetCurrent(attackDamageAttributeId);
                mana = attributes.GetCurrent(manaAttributeId);
            }

            int empoweredTagId = TagRegistry.Register("Status.Empowered");
            if (runtime.Engine.World.Has<GameplayTagContainer>(hero) && runtime.Engine.GetService(CoreServiceKeys.TagOps) is TagOps tagOps)
            {
                ref var tags = ref runtime.Engine.World.Get<GameplayTagContainer>(hero);
                effectiveEmpoweredTag = tagOps.HasTag(ref tags, empoweredTagId, TagSense.Effective);
            }

            if (runtime.Engine.World.Has<TagCountContainer>(hero))
            {
                empoweredCount = runtime.Engine.World.Get<TagCountContainer>(hero).GetCount(empoweredTagId);
            }
        }

        return new InteractionSnapshot(
            Tick: runtime.Engine.GameSession.CurrentTick,
            Step: step,
            TickMs: tickMs,
            ActiveMapId: runtime.Engine.CurrentMapSession?.MapId.ToString() ?? runtime.Config.StartupMapId,
            ActiveScenarioId: ReadGlobalString(runtime.Engine, InteractionShowcaseRuntimeKeys.ActiveScenarioId, "(unset)"),
            ScriptTick: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.ScriptTick, 0),
            Stage: ReadGlobalString(runtime.Engine, InteractionShowcaseRuntimeKeys.Stage, "not_started"),
            HeroPresent: hero != Entity.Null && runtime.Engine.World.IsAlive(hero),
            AttackDamage: attackDamage,
            Mana: mana,
            HeroBaseDamage: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.HeroBaseDamage, 0f),
            PrimaryTargetHealth: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.PrimaryTargetHealth, 0f),
            PrimaryTargetArmor: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.PrimaryTargetArmor, 0f),
            InvalidTargetHealth: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.InvalidTargetHealth, 0f),
            FarTargetHealth: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.FarTargetHealth, 0f),
            DamageAmount: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.DamageAmount, 0f),
            FinalDamage: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.FinalDamage, 0f),
            DamageApplied: ReadGlobalBool(runtime.Engine, InteractionShowcaseRuntimeKeys.DamageApplied),
            DamageAppliedTick: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.DamageAppliedTick, -1),
            C2AllyTargetHealth: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.C2AllyTargetHealth, 0f),
            C2HostileTargetHealth: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.C2HostileTargetHealth, 0f),
            C2DeadAllyTargetHealth: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.C2DeadAllyTargetHealth, 0f),
            C2HealAmount: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.C2HealAmount, 0f),
            C2HealApplied: ReadGlobalBool(runtime.Engine, InteractionShowcaseRuntimeKeys.C2HealApplied),
            C2HealAppliedTick: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.C2HealAppliedTick, -1),
            LastAttemptTargetName: ReadGlobalString(runtime.Engine, InteractionShowcaseRuntimeKeys.LastAttemptTargetName, string.Empty),
            EffectiveEmpoweredTag: effectiveEmpoweredTag,
            EmpoweredCount: empoweredCount,
            CastSubmitted: ReadGlobalBool(runtime.Engine, InteractionShowcaseRuntimeKeys.CastSubmitted),
            CastSubmittedTick: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.CastSubmittedTick, -1),
            BuffObserved: ReadGlobalBool(runtime.Engine, InteractionShowcaseRuntimeKeys.BuffObserved),
            BuffExpired: ReadGlobalBool(runtime.Engine, InteractionShowcaseRuntimeKeys.BuffExpired),
            LastCastFailReason: ReadGlobalString(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty),
            LastCastFailTick: ReadGlobalInt(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailTick, -1),
            LastCastFailAttribute: ReadGlobalString(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailAttribute, string.Empty),
            LastCastFailDelta: ReadGlobalFloat(runtime.Engine, InteractionShowcaseRuntimeKeys.LastCastFailDelta, 0f),
            NamedEntities: namedEntities,
            OverlayLines: ExtractOverlayText(runtime.Engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) as ScreenOverlayBuffer, clearAfterRead: true));
    }

    private static InteractionAcceptanceResult EvaluateInteractionAcceptance(IReadOnlyList<InteractionSnapshot> timeline)
    {
        InteractionSnapshot start = timeline[0];
        InteractionSnapshot submitted = timeline[1];
        InteractionSnapshot active = timeline[2];
        InteractionSnapshot expired = timeline[3];
        InteractionSnapshot silencedBlocked = timeline[4];
        InteractionSnapshot insufficientManaBlocked = timeline[5];

        var failures = new List<string>();
        AddAcceptanceCheck(string.Equals(start.ActiveScenarioId, InteractionShowcaseIds.B1SelfBuffScenarioId, StringComparison.OrdinalIgnoreCase),
            $"Expected active scenario '{InteractionShowcaseIds.B1SelfBuffScenarioId}', but saw '{start.ActiveScenarioId}'.", failures);
        AddAcceptanceCheck(string.Equals(start.ActiveMapId, InteractionShowcaseIds.B1SelfBuffMapId, StringComparison.OrdinalIgnoreCase),
            $"Expected active map '{InteractionShowcaseIds.B1SelfBuffMapId}', but saw '{start.ActiveMapId}'.", failures);
        AddAcceptanceCheck(start.HeroPresent,
            "Hero should be present in the showcase map.", failures);
        AddAcceptanceCheck(Math.Abs(start.AttackDamage - 100f) <= 0.001f &&
                Math.Abs(start.Mana - 100f) <= 0.001f &&
                start.EmpoweredCount == 0 &&
                !start.EffectiveEmpoweredTag,
            $"Hero should start at AttackDamage=100, Mana=100 with no empowered state, but attackDamage={start.AttackDamage:F1} mana={start.Mana:F1} empoweredCount={start.EmpoweredCount} effectiveTag={start.EffectiveEmpoweredTag}.", failures);
        AddAcceptanceCheck(submitted.CastSubmitted,
            "Autoplay should submit the self-cast order.", failures);
        AddAcceptanceCheck(active.AttackDamage >= 149.999f && active.AttackDamage <= 150.001f,
            $"B1 self buff should raise AttackDamage to 150, but active capture saw {active.AttackDamage:F3}.", failures);
        AddAcceptanceCheck(active.EmpoweredCount == 1,
            $"B1 self buff should set Status.Empowered count to 1, but active capture saw {active.EmpoweredCount}.", failures);
        AddAcceptanceCheck(active.EffectiveEmpoweredTag,
            "B1 self buff should set effective Status.Empowered while active.", failures);
        AddAcceptanceCheck(expired.BuffExpired,
            "Expiry capture should report BuffExpired=true.", failures);
        AddAcceptanceCheck(Math.Abs(expired.AttackDamage - 100f) <= 0.001f &&
                expired.EmpoweredCount == 0 &&
                !expired.EffectiveEmpoweredTag,
            $"Expiry capture should return to baseline, but attackDamage={expired.AttackDamage:F1} empoweredCount={expired.EmpoweredCount} effectiveTag={expired.EffectiveEmpoweredTag}.", failures);
        AddAcceptanceCheck(string.Equals(silencedBlocked.LastCastFailReason, "BlockedByTag", StringComparison.Ordinal),
            $"Silenced capture should report BlockedByTag, but saw '{silencedBlocked.LastCastFailReason}'.", failures);
        AddAcceptanceCheck(Math.Abs(silencedBlocked.AttackDamage - 100f) <= 0.001f && silencedBlocked.EmpoweredCount == 0,
            $"Silenced capture should not apply the buff, but attackDamage={silencedBlocked.AttackDamage:F1} empoweredCount={silencedBlocked.EmpoweredCount}.", failures);
        AddAcceptanceCheck(string.Equals(insufficientManaBlocked.LastCastFailReason, "InsufficientResource", StringComparison.Ordinal),
            $"Insufficient mana capture should report InsufficientResource, but saw '{insufficientManaBlocked.LastCastFailReason}'.", failures);
        AddAcceptanceCheck(string.Equals(insufficientManaBlocked.LastCastFailAttribute, "Mana", StringComparison.Ordinal) &&
                Math.Abs(insufficientManaBlocked.LastCastFailDelta - 50f) <= 0.001f,
            $"Insufficient mana capture should report Mana delta 50, but attr='{insufficientManaBlocked.LastCastFailAttribute}' delta={insufficientManaBlocked.LastCastFailDelta:F1}.", failures);
        AddAcceptanceCheck(Math.Abs(insufficientManaBlocked.Mana) <= 0.001f,
            $"Insufficient mana capture should run at Mana=0, but saw {insufficientManaBlocked.Mana:F1}.", failures);

        string normalizedSignature = string.Join("|", new[]
        {
            "interaction_b1_self_buff",
            $"submitted:{submitted.Tick}",
            $"active:{active.Tick}:{MathF.Round(active.AttackDamage):F0}:{active.EmpoweredCount}:{(active.EffectiveEmpoweredTag ? 1 : 0)}",
            $"expired:{expired.Tick}:{MathF.Round(expired.AttackDamage):F0}:{expired.EmpoweredCount}:{(expired.EffectiveEmpoweredTag ? 1 : 0)}",
            $"silenced:{silencedBlocked.Tick}:{silencedBlocked.LastCastFailReason}",
            $"insufficient:{insufficientManaBlocked.Tick}:{insufficientManaBlocked.LastCastFailReason}:{insufficientManaBlocked.LastCastFailAttribute}:{MathF.Round(insufficientManaBlocked.LastCastFailDelta):F0}"
        });

        string verdict = failures.Count == 0
            ? $"B1 self buff passes: order submits at tick {submitted.Tick}, AttackDamage reaches {active.AttackDamage:F0} with effective Status.Empowered, expires at tick {expired.Tick}, then silenced and insufficient-mana branches both fail for the expected reasons."
            : "B1 self buff fails: autoplay order, buff application, expiry cleanup, or guard branches diverged from the scenario contract.";
        string failureSummary = failures.Count == 0 ? verdict : string.Join(Environment.NewLine, failures);

        return new InteractionAcceptanceResult(
            Success: failures.Count == 0,
            Verdict: verdict,
            FailureSummary: failureSummary,
            FailedChecks: failures,
            StartAttackDamage: start.AttackDamage,
            ActiveAttackDamage: active.AttackDamage,
            ExpiredAttackDamage: expired.AttackDamage,
            StartMana: start.Mana,
            InsufficientMana: insufficientManaBlocked.Mana,
            ActiveEmpoweredCount: active.EmpoweredCount,
            ExpiredEmpoweredCount: expired.EmpoweredCount,
            SilencedFailReason: silencedBlocked.LastCastFailReason,
            InsufficientManaFailReason: insufficientManaBlocked.LastCastFailReason,
            SubmittedTick: submitted.Tick,
            ActiveTick: active.Tick,
            ExpiredTick: expired.Tick,
            SilencedBlockedTick: silencedBlocked.Tick,
            InsufficientManaBlockedTick: insufficientManaBlocked.Tick,
            NormalizedSignature: normalizedSignature);
    }

    private static string BuildInteractionBattleReport(
        LauncherRecordingRequest request,
        IReadOnlyList<InteractionSnapshot> timeline,
        IReadOnlyList<InteractionCaptureFrame> captureFrames,
        IReadOnlyList<double> frameTimesMs,
        InteractionAcceptanceResult acceptance)
    {
        double medianTickMs = Median(frameTimesMs.ToArray());
        double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
        string evidenceImages = string.Join(", ", captureFrames.Select(frame => $"`screens/{frame.FileName}`").Append("`screens/timeline.png`"));

        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: interaction-b1-self-buff");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: press a self-buff skill and verify the caster gains a temporary AttackDamage buff that cleanly expires, then validate silenced and insufficient-mana guard branches.");
        sb.AppendLine("- Gameplay domain: launcher-started real mod bootstrap, real `OrderQueue` submission, real `Ability.Interaction.B1SelfBuff` / `Effect.Interaction.B1SelfBuffBuff` execution.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none");
        sb.AppendLine("- Map: `mods/InteractionShowcaseMod/assets/Maps/interaction_b1_self_buff.json`");
        sb.AppendLine("- Clock profile: fixed `1/60s`");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Launch command: `{request.CommandText}`");
        sb.AppendLine($"- Evidence images: {evidenceImages}");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Boot the launcher runtime with `InteractionShowcaseMod` as the root selection.");
        sb.AppendLine("2. Let the autoplay system warm up until it submits slot `0` through `OrderQueue`.");
        sb.AppendLine("3. Capture the start, order-submitted, buff-active, buff-expired, silenced-blocked, and insufficient-mana frames.");
        sb.AppendLine();
        sb.AppendLine("## Expected Outcomes");
        sb.AppendLine("- Primary success condition: AttackDamage moves `100 -> 150 -> 100` and `Status.Empowered` moves `0 -> 1 -> 0` with effective tag truth matching the granted count.");
        sb.AppendLine("- Failure branch conditions: silenced cast fails with `BlockedByTag`; no-mana cast fails with `InsufficientResource` on `Mana` delta `50`.");
        sb.AppendLine("- Key metrics: attack damage, mana, empowered count, effective tag bit, fail reason metadata, autoplay stage, capture ticks.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (InteractionSnapshot snapshot in timeline)
        {
            string overlay = snapshot.OverlayLines.Count == 0 ? "(none)" : snapshot.OverlayLines[0];
            sb.AppendLine($"- [T+{snapshot.Tick:000}] InteractionShowcase.{snapshot.Step} -> stage={snapshot.Stage} | attackDamage={snapshot.AttackDamage:F1} | mana={snapshot.Mana:F1} | empoweredCount={snapshot.EmpoweredCount} | effectiveTag={(snapshot.EffectiveEmpoweredTag ? "On" : "Off")} | fail={snapshot.LastCastFailReason} | overlay={overlay}");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        foreach (string failedCheck in acceptance.FailedChecks)
        {
            sb.AppendLine($"- failed-check: {failedCheck}");
        }

        sb.AppendLine("- runtime note: launcher evidence validates both granted tag count and effective tag state for `Status.Empowered`.");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- screenshot captures: `{captureFrames.Count}`");
        sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
        sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
        sb.AppendLine($"- submitted tick: `{acceptance.SubmittedTick}`");
        sb.AppendLine($"- active tick: `{acceptance.ActiveTick}`");
        sb.AppendLine($"- expired tick: `{acceptance.ExpiredTick}`");
        sb.AppendLine($"- silenced blocked tick: `{acceptance.SilencedBlockedTick}`");
        sb.AppendLine($"- insufficient mana tick: `{acceptance.InsufficientManaBlockedTick}`");
        sb.AppendLine($"- normalized signature: `{acceptance.NormalizedSignature}`");
        sb.AppendLine("- reusable wiring: `GameBootstrapper`, `ConfigPipeline`, `OrderQueue`, `AbilityExecSystem`, `EffectProcessingLoopSystem`, `AbilityActivationPreconditionEvaluator`, `ScreenOverlayBuffer`");
        return sb.ToString();
    }

    private static string BuildInteractionTraceJsonl(string adapterId, IReadOnlyList<InteractionSnapshot> timeline)
    {
        var lines = new List<string>(timeline.Count);
        for (int index = 0; index < timeline.Count; index++)
        {
            InteractionSnapshot snapshot = timeline[index];
            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"interaction-{adapterId}-{index + 1:000}",
                tick = snapshot.Tick,
                step = snapshot.Step,
                stage = snapshot.Stage,
                attack_damage = Math.Round(snapshot.AttackDamage, 2),
                mana = Math.Round(snapshot.Mana, 2),
                empowered_count = snapshot.EmpoweredCount,
                effective_tag = snapshot.EffectiveEmpoweredTag,
                cast_submitted = snapshot.CastSubmitted,
                buff_expired = snapshot.BuffExpired,
                last_cast_fail_reason = snapshot.LastCastFailReason,
                last_cast_fail_attribute = snapshot.LastCastFailAttribute,
                last_cast_fail_delta = Math.Round(snapshot.LastCastFailDelta, 2),
                status = "done"
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildInteractionPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Boot launcher runtime for InteractionShowcaseMod] --> B[Autoplay warmup on interaction_b1_self_buff]",
            "    B --> C{OrderQueue submits slot 0 self-cast?}",
            "    C -->|yes| D[Ability.Interaction.B1SelfBuff emits Effect.Interaction.B1SelfBuffBuff]",
            "    D --> E{AttackDamage = 150 and Status.Empowered effective=true?}",
            "    E -->|yes| F[Capture buff-active frame]",
            "    F --> G{Buff expires and AttackDamage returns to 100?}",
            "    G -->|yes| H{Silenced retry fails with BlockedByTag?}",
            "    H -->|yes| I{Mana=0 retry fails with InsufficientResource?}",
            "    I -->|yes| J[Write battle-report + trace + path + PNG timeline]",
            "    C -->|no| X[Fail acceptance: autoplay/order pipeline diverged]",
            "    E -->|no| Y[Fail acceptance: buff apply path diverged]",
            "    G -->|no| Z[Fail acceptance: buff lifetime/cleanup diverged]",
            "    H -->|no| Q[Fail acceptance: silenced branch diverged]",
            "    I -->|no| R[Fail acceptance: mana precondition branch diverged]"
        }) + Environment.NewLine;
    }

    private static string BuildInteractionVisibleChecklist(IReadOnlyList<InteractionCaptureFrame> frames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: interaction-b1-self-buff");
        sb.AppendLine();
        sb.AppendLine("- `000_start` should show the hero idle at AttackDamage `100`, Mana `100`, with no empowered aura.");
        sb.AppendLine("- `001_order_submitted` should show the overlay in `order_submitted` stage.");
        sb.AppendLine("- `002_buff_active` should show the hero highlighted as buffed, with AttackDamage `150` and empowered count `1`.");
        sb.AppendLine("- `003_buff_expired` should remove the buff aura and restore AttackDamage and empowered count to baseline.");
        sb.AppendLine("- `004_silenced_blocked` should show no buff aura and a `BlockedByTag` failure in the overlay.");
        sb.AppendLine("- `005_insufficient_mana` should show Mana `0` and an `InsufficientResource` failure for `Mana` delta `50`.");
        sb.AppendLine("- visual scope note: this showcase uses the world-space aura plus debug overlay as the acceptance surface; it does not render a production skill bar or buff HUD icon.");
        sb.AppendLine("- resource scope note: this showcase validates the mana precondition gate; it does not deduct Mana on successful cast.");
        sb.AppendLine("- `screens/timeline.png` gives the six-frame strip for quick approval and Claude review.");
        sb.AppendLine();
        foreach (InteractionCaptureFrame frame in frames)
        {
            sb.AppendLine($"- `{frame.FileName}`: stage={frame.Stage}, attackDamage={frame.AttackDamage:F1}, mana={frame.Mana:F1}, empoweredCount={frame.EmpoweredCount}, fail={FormatInteractionDisplayValue(frame.LastCastFailReason, "None")}");
        }

        return sb.ToString();
    }

    private static string BuildInteractionSummaryJson(LauncherRecordingRequest request, InteractionAcceptanceResult acceptance)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = "interaction_b1_self_buff",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            submitted_tick = acceptance.SubmittedTick,
            active_tick = acceptance.ActiveTick,
            expired_tick = acceptance.ExpiredTick,
            silenced_blocked_tick = acceptance.SilencedBlockedTick,
            insufficient_mana_tick = acceptance.InsufficientManaBlockedTick,
            active_attack_damage = Math.Round(acceptance.ActiveAttackDamage, 2),
            expired_attack_damage = Math.Round(acceptance.ExpiredAttackDamage, 2),
            start_mana = Math.Round(acceptance.StartMana, 2),
            insufficient_mana = Math.Round(acceptance.InsufficientMana, 2),
            active_empowered_count = acceptance.ActiveEmpoweredCount,
            expired_empowered_count = acceptance.ExpiredEmpoweredCount,
            silenced_fail_reason = acceptance.SilencedFailReason,
            insufficient_mana_fail_reason = acceptance.InsufficientManaFailReason,
            normalized_signature = acceptance.NormalizedSignature
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static C1InteractionAcceptanceResult EvaluateInteractionC1Acceptance(IReadOnlyList<InteractionSnapshot> timeline)
    {
        InteractionSnapshot start = timeline[0];
        InteractionSnapshot submitted = timeline[1];
        InteractionSnapshot damageApplied = timeline[2];
        InteractionSnapshot invalidTargetBlocked = timeline[3];
        InteractionSnapshot outOfRangeBlocked = timeline[4];

        var failures = new List<string>();
        AddAcceptanceCheck(string.Equals(start.ActiveScenarioId, InteractionShowcaseIds.C1HostileUnitDamageScenarioId, StringComparison.OrdinalIgnoreCase),
            $"Expected active scenario '{InteractionShowcaseIds.C1HostileUnitDamageScenarioId}', but saw '{start.ActiveScenarioId}'.", failures);
        AddAcceptanceCheck(string.Equals(start.ActiveMapId, InteractionShowcaseIds.C1HostileUnitDamageMapId, StringComparison.OrdinalIgnoreCase),
            $"Expected active map '{InteractionShowcaseIds.C1HostileUnitDamageMapId}', but saw '{start.ActiveMapId}'.", failures);
        AddAcceptanceCheck(start.HeroPresent,
            "Hero should be present in the C1 showcase map.", failures);
        AddAcceptanceCheck(Math.Abs(start.HeroBaseDamage - 200f) <= 0.001f &&
                Math.Abs(start.Mana - 100f) <= 0.001f &&
                Math.Abs(start.PrimaryTargetHealth - 500f) <= 0.001f &&
                Math.Abs(start.PrimaryTargetArmor - 50f) <= 0.001f &&
                Math.Abs(start.InvalidTargetHealth) <= 0.001f &&
                Math.Abs(start.FarTargetHealth - 500f) <= 0.001f,
            $"C1 should start at BaseDamage=200, Mana=100, PrimaryHP=500, PrimaryArmor=50, InvalidHP=0, FarHP=500, but saw baseDamage={start.HeroBaseDamage:F1} mana={start.Mana:F1} primaryHP={start.PrimaryTargetHealth:F1} armor={start.PrimaryTargetArmor:F1} invalidHP={start.InvalidTargetHealth:F1} farHP={start.FarTargetHealth:F1}.",
            failures);
        AddAcceptanceCheck(submitted.CastSubmitted &&
                string.Equals(submitted.LastAttemptTargetName, InteractionShowcaseIds.C1PrimaryTargetName, StringComparison.OrdinalIgnoreCase),
            $"Autoplay should submit the hostile cast at '{InteractionShowcaseIds.C1PrimaryTargetName}', but saw castSubmitted={submitted.CastSubmitted} target='{submitted.LastAttemptTargetName}'.", failures);
        AddAcceptanceCheck(damageApplied.DamageApplied,
            "Damage capture should report DamageApplied=true.", failures);
        AddAcceptanceCheck(Math.Abs(damageApplied.PrimaryTargetHealth - 300f) <= 0.001f,
            $"Primary target HP should drop to 300, but capture saw {damageApplied.PrimaryTargetHealth:F3}.", failures);
        AddAcceptanceCheck(Math.Abs(damageApplied.DamageAmount - 300f) <= 0.001f,
            $"DamageAmount should be 300, but capture saw {damageApplied.DamageAmount:F3}.", failures);
        AddAcceptanceCheck(Math.Abs(damageApplied.FinalDamage - 200f) <= 0.001f,
            $"FinalDamage should be 200, but capture saw {damageApplied.FinalDamage:F3}.", failures);
        AddAcceptanceCheck(string.Equals(damageApplied.LastAttemptTargetName, InteractionShowcaseIds.C1PrimaryTargetName, StringComparison.OrdinalIgnoreCase),
            $"Damage capture should still reference primary target '{InteractionShowcaseIds.C1PrimaryTargetName}', but saw '{damageApplied.LastAttemptTargetName}'.", failures);
        AddAcceptanceCheck(string.Equals(invalidTargetBlocked.LastCastFailReason, "InvalidTarget", StringComparison.Ordinal) &&
                string.Equals(invalidTargetBlocked.LastAttemptTargetName, InteractionShowcaseIds.C1InvalidTargetName, StringComparison.OrdinalIgnoreCase),
            $"Invalid-target capture should report InvalidTarget on '{InteractionShowcaseIds.C1InvalidTargetName}', but saw fail='{invalidTargetBlocked.LastCastFailReason}' target='{invalidTargetBlocked.LastAttemptTargetName}'.", failures);
        AddAcceptanceCheck(Math.Abs(invalidTargetBlocked.InvalidTargetHealth) <= 0.001f,
            $"Invalid target should stay at 0 HP, but capture saw {invalidTargetBlocked.InvalidTargetHealth:F3}.", failures);
        AddAcceptanceCheck(Math.Abs(invalidTargetBlocked.PrimaryTargetHealth - 300f) <= 0.001f,
            $"Invalid-target retry should not change primary HP, but capture saw {invalidTargetBlocked.PrimaryTargetHealth:F3}.", failures);
        AddAcceptanceCheck(string.Equals(outOfRangeBlocked.LastCastFailReason, "OutOfRange", StringComparison.Ordinal) &&
                string.Equals(outOfRangeBlocked.LastAttemptTargetName, InteractionShowcaseIds.C1FarTargetName, StringComparison.OrdinalIgnoreCase),
            $"Out-of-range capture should report OutOfRange on '{InteractionShowcaseIds.C1FarTargetName}', but saw fail='{outOfRangeBlocked.LastCastFailReason}' target='{outOfRangeBlocked.LastAttemptTargetName}'.", failures);
        AddAcceptanceCheck(Math.Abs(outOfRangeBlocked.FarTargetHealth - 500f) <= 0.001f,
            $"Far target should stay at 500 HP, but capture saw {outOfRangeBlocked.FarTargetHealth:F3}.", failures);

        string normalizedSignature = string.Join("|", new[]
        {
            "interaction_c1_hostile_unit_damage",
            $"submitted:{submitted.Tick}:{submitted.LastAttemptTargetName}",
            $"damage:{damageApplied.Tick}:{MathF.Round(damageApplied.DamageAmount):F0}:{MathF.Round(damageApplied.FinalDamage):F0}:{MathF.Round(damageApplied.PrimaryTargetHealth):F0}",
            $"invalid:{invalidTargetBlocked.Tick}:{invalidTargetBlocked.LastCastFailReason}",
            $"range:{outOfRangeBlocked.Tick}:{outOfRangeBlocked.LastCastFailReason}"
        });

        string verdict = failures.Count == 0
            ? $"C1 hostile unit damage passes: order submits at tick {submitted.Tick}, target blackboard records DamageAmount={damageApplied.DamageAmount:F0} and FinalDamage={damageApplied.FinalDamage:F0}, primary HP drops to {damageApplied.PrimaryTargetHealth:F0}, then invalid-target and out-of-range retries fail for the expected reasons."
            : "C1 hostile unit damage fails: hostile target damage, mitigation math, or guard branches diverged from the scenario contract.";
        string failureSummary = failures.Count == 0 ? verdict : string.Join(Environment.NewLine, failures);

        return new C1InteractionAcceptanceResult(
            Success: failures.Count == 0,
            Verdict: verdict,
            FailureSummary: failureSummary,
            FailedChecks: failures,
            StartHeroBaseDamage: start.HeroBaseDamage,
            StartPrimaryTargetHealth: start.PrimaryTargetHealth,
            DamageAmount: damageApplied.DamageAmount,
            FinalDamage: damageApplied.FinalDamage,
            DamageAppliedPrimaryTargetHealth: damageApplied.PrimaryTargetHealth,
            InvalidTargetHealth: invalidTargetBlocked.InvalidTargetHealth,
            FarTargetHealth: outOfRangeBlocked.FarTargetHealth,
            InvalidTargetFailReason: invalidTargetBlocked.LastCastFailReason,
            OutOfRangeFailReason: outOfRangeBlocked.LastCastFailReason,
            SubmittedTick: submitted.Tick,
            DamageAppliedTick: damageApplied.Tick,
            InvalidTargetBlockedTick: invalidTargetBlocked.Tick,
            OutOfRangeBlockedTick: outOfRangeBlocked.Tick,
            NormalizedSignature: normalizedSignature);
    }

    private static string BuildInteractionC1BattleReport(
        LauncherRecordingRequest request,
        IReadOnlyList<InteractionSnapshot> timeline,
        IReadOnlyList<InteractionCaptureFrame> captureFrames,
        IReadOnlyList<double> frameTimesMs,
        C1InteractionAcceptanceResult acceptance)
    {
        double medianTickMs = Median(frameTimesMs.ToArray());
        double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
        string evidenceImages = string.Join(", ", captureFrames.Select(frame => $"`screens/{frame.FileName}`").Append("`screens/timeline.png`"));

        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: interaction-c1-hostile-unit-damage");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: smart-cast a hostile unit damage skill and verify the real GAS graph pipeline computes DamageAmount, mitigates by Armor, and applies final Health loss on the chosen target.");
        sb.AppendLine("- Gameplay domain: launcher-started real mod bootstrap, real `OrderQueue` submission, real `Ability.Interaction.C1HostileUnitDamage` / `Effect.Interaction.C1HostileUnitDamage` execution.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none");
        sb.AppendLine("- Map: `mods/InteractionShowcaseMod/assets/Maps/interaction_c1_hostile_unit_damage.json`");
        sb.AppendLine("- Clock profile: fixed `1/60s`");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Launch command: `{request.CommandText}`");
        sb.AppendLine($"- Evidence images: {evidenceImages}");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Boot the launcher runtime with `InteractionShowcaseMod` rooted to the C1 showcase map.");
        sb.AppendLine($"2. Let autoplay warm up and submit slot `0` against `{InteractionShowcaseIds.C1PrimaryTargetName}`.");
        sb.AppendLine("3. Capture the hostile hit frame after `DamageAmount` and `FinalDamage` are written to the target blackboard and Health drops.");
        sb.AppendLine($"4. Retry against `{InteractionShowcaseIds.C1InvalidTargetName}` and `{InteractionShowcaseIds.C1FarTargetName}` to prove the local validation branches.");
        sb.AppendLine();
        sb.AppendLine("## Expected Outcomes");
        sb.AppendLine("- Primary success condition: hero `BaseDamage=200` and coeff `1.5` produce `DamageAmount=300`, Armor `50` reduces that to `FinalDamage=200`, and primary target Health moves `500 -> 300`.");
        sb.AppendLine("- Failure branch conditions: the invalid target retry fails with `InvalidTarget`; the far target retry fails with `OutOfRange`; neither retry changes Health.");
        sb.AppendLine("- Key metrics: primary target Health/Armor, target blackboard `DamageAmount`/`FinalDamage`, autoplay stage, fail reasons, capture ticks.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (InteractionSnapshot snapshot in timeline)
        {
            string overlay = snapshot.OverlayLines.Count == 0 ? "(none)" : snapshot.OverlayLines[0];
            sb.AppendLine($"- [T+{snapshot.Tick:000}] InteractionShowcase.{snapshot.Step} -> stage={snapshot.Stage} | baseDamage={snapshot.HeroBaseDamage:F1} | primaryHP={snapshot.PrimaryTargetHealth:F1} | damageAmount={snapshot.DamageAmount:F1} | finalDamage={snapshot.FinalDamage:F1} | fail={snapshot.LastCastFailReason} | target={snapshot.LastAttemptTargetName} | overlay={overlay}");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        foreach (string failedCheck in acceptance.FailedChecks)
        {
            sb.AppendLine($"- failed-check: {failedCheck}");
        }

        sb.AppendLine("- runtime note: current graph runtime exposes source/target context registers, so this showcase writes `Interaction.C1.DamageAmount` and `Interaction.C1.FinalDamage` to the target blackboard instead of an effect entity blackboard.");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- screenshot captures: `{captureFrames.Count}`");
        sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
        sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
        sb.AppendLine($"- submitted tick: `{acceptance.SubmittedTick}`");
        sb.AppendLine($"- damage applied tick: `{acceptance.DamageAppliedTick}`");
        sb.AppendLine($"- invalid target blocked tick: `{acceptance.InvalidTargetBlockedTick}`");
        sb.AppendLine($"- out of range blocked tick: `{acceptance.OutOfRangeBlockedTick}`");
        sb.AppendLine($"- normalized signature: `{acceptance.NormalizedSignature}`");
        sb.AppendLine("- reusable wiring: `GameBootstrapper`, `ConfigPipeline`, `OrderQueue`, `AbilityExecSystem`, `EffectProcessingLoopSystem`, `Graph.Interaction.C1.CalculateDamage`, `Graph.Interaction.C1.ApplyMitigatedDamage`, `ScreenOverlayBuffer`");
        return sb.ToString();
    }

    private static string BuildInteractionC1TraceJsonl(string adapterId, IReadOnlyList<InteractionSnapshot> timeline)
    {
        var lines = new List<string>(timeline.Count);
        for (int index = 0; index < timeline.Count; index++)
        {
            InteractionSnapshot snapshot = timeline[index];
            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"interaction-c1-{adapterId}-{index + 1:000}",
                tick = snapshot.Tick,
                step = snapshot.Step,
                stage = snapshot.Stage,
                hero_base_damage = Math.Round(snapshot.HeroBaseDamage, 2),
                mana = Math.Round(snapshot.Mana, 2),
                primary_target_health = Math.Round(snapshot.PrimaryTargetHealth, 2),
                primary_target_armor = Math.Round(snapshot.PrimaryTargetArmor, 2),
                invalid_target_health = Math.Round(snapshot.InvalidTargetHealth, 2),
                far_target_health = Math.Round(snapshot.FarTargetHealth, 2),
                damage_amount = Math.Round(snapshot.DamageAmount, 2),
                final_damage = Math.Round(snapshot.FinalDamage, 2),
                damage_applied = snapshot.DamageApplied,
                damage_applied_tick = snapshot.DamageAppliedTick,
                last_attempt_target_name = snapshot.LastAttemptTargetName,
                last_cast_fail_reason = snapshot.LastCastFailReason,
                cast_submitted = snapshot.CastSubmitted,
                status = "done"
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildInteractionC1PathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Boot launcher runtime for InteractionShowcaseMod] --> B[Autoplay warmup on interaction_c1_hostile_unit_damage]",
            "    B --> C{OrderQueue submits slot 0 hostile cast on C1EnemyPrimary?}",
            "    C -->|yes| D[Graph.Interaction.C1.CalculateDamage writes target blackboard DamageAmount=300]",
            "    D --> E[Graph.Interaction.C1.ApplyMitigatedDamage reads Armor=50 and writes FinalDamage=200]",
            "    E --> F{Primary target HP = 300?}",
            "    F -->|yes| G{Retry on dead target fails with InvalidTarget?}",
            "    G -->|yes| H{Retry on far target fails with OutOfRange?}",
            "    H -->|yes| I[Write battle-report + trace + path + PNG timeline]",
            "    C -->|no| X[Fail acceptance: autoplay/order pipeline diverged]",
            "    F -->|no| Y[Fail acceptance: damage or mitigation graph diverged]",
            "    G -->|no| Z[Fail acceptance: invalid-target guard diverged]",
            "    H -->|no| Q[Fail acceptance: range guard diverged]"
        }) + Environment.NewLine;
    }

    private static string BuildInteractionC1VisibleChecklist(IReadOnlyList<InteractionCaptureFrame> frames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: interaction-c1-hostile-unit-damage");
        sb.AppendLine();
        sb.AppendLine("- `000_start` should show the hero at `BaseDamage=200`, the primary target at `HP=500`, the invalid target already dead, and the far target outside the cast range.");
        sb.AppendLine("- `001_order_submitted` should still point at `C1EnemyPrimary` with `CastSubmitted=true` and no Health loss yet.");
        sb.AppendLine("- `002_damage_applied` should show the primary target Health bar at `300`, plus a floating `-200` damage label over the target.");
        sb.AppendLine("- `003_invalid_target_blocked` should keep the invalid target unchanged and show `InvalidTarget` in the overlay.");
        sb.AppendLine("- `004_out_of_range_blocked` should keep the far target unchanged and show `OutOfRange` in the overlay.");
        sb.AppendLine("- visual note: the earlier `-200` damage pop can still remain visible on later blocked frames because this showcase keeps the previous combat label alive during the short capture window.");
        sb.AppendLine("- `screens/timeline.png` gives the five-frame strip for quick approval and Claude review.");
        sb.AppendLine();
        foreach (InteractionCaptureFrame frame in frames)
        {
            sb.AppendLine($"- `{frame.FileName}`: stage={frame.Stage}, primaryHP={frame.PrimaryTargetHealth:F1}, invalidHP={frame.InvalidTargetHealth:F1}, farHP={frame.FarTargetHealth:F1}, damage={frame.DamageAmount:F1}/{frame.FinalDamage:F1}, damageApplied={frame.DamageApplied}, fail={FormatInteractionDisplayValue(frame.LastCastFailReason, "None")}, target={FormatInteractionDisplayValue(frame.LastAttemptTargetName, "-")}");
        }

        return sb.ToString();
    }

    private static string BuildInteractionC1SummaryJson(LauncherRecordingRequest request, C1InteractionAcceptanceResult acceptance)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = "interaction_c1_hostile_unit_damage",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            submitted_tick = acceptance.SubmittedTick,
            damage_applied_tick = acceptance.DamageAppliedTick,
            invalid_target_blocked_tick = acceptance.InvalidTargetBlockedTick,
            out_of_range_blocked_tick = acceptance.OutOfRangeBlockedTick,
            start_hero_base_damage = Math.Round(acceptance.StartHeroBaseDamage, 2),
            start_primary_target_health = Math.Round(acceptance.StartPrimaryTargetHealth, 2),
            damage_amount = Math.Round(acceptance.DamageAmount, 2),
            final_damage = Math.Round(acceptance.FinalDamage, 2),
            primary_target_health_after_damage = Math.Round(acceptance.DamageAppliedPrimaryTargetHealth, 2),
            invalid_target_health = Math.Round(acceptance.InvalidTargetHealth, 2),
            far_target_health = Math.Round(acceptance.FarTargetHealth, 2),
            invalid_target_fail_reason = acceptance.InvalidTargetFailReason,
            out_of_range_fail_reason = acceptance.OutOfRangeFailReason,
            normalized_signature = acceptance.NormalizedSignature
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static C2InteractionAcceptanceResult EvaluateInteractionC2Acceptance(IReadOnlyList<InteractionSnapshot> timeline)
    {
        InteractionSnapshot start = timeline[0];
        InteractionSnapshot submitted = timeline[1];
        InteractionSnapshot healApplied = timeline[2];
        InteractionSnapshot hostileTargetBlocked = timeline[3];
        InteractionSnapshot deadAllyBlocked = timeline[4];

        var failures = new List<string>();
        AddAcceptanceCheck(string.Equals(start.ActiveScenarioId, InteractionShowcaseIds.C2FriendlyUnitHealScenarioId, StringComparison.OrdinalIgnoreCase),
            $"Expected active scenario '{InteractionShowcaseIds.C2FriendlyUnitHealScenarioId}', but saw '{start.ActiveScenarioId}'.", failures);
        AddAcceptanceCheck(string.Equals(start.ActiveMapId, InteractionShowcaseIds.C2FriendlyUnitHealMapId, StringComparison.OrdinalIgnoreCase),
            $"Expected active map '{InteractionShowcaseIds.C2FriendlyUnitHealMapId}', but saw '{start.ActiveMapId}'.", failures);
        AddAcceptanceCheck(start.HeroPresent,
            "Hero should be present in the C2 showcase map.", failures);
        AddAcceptanceCheck(Math.Abs(start.Mana - 100f) <= 0.001f &&
                Math.Abs(start.C2AllyTargetHealth - 200f) <= 0.001f &&
                Math.Abs(start.C2HostileTargetHealth - 400f) <= 0.001f &&
                Math.Abs(start.C2DeadAllyTargetHealth) <= 0.001f,
            $"C2 should start at Mana=100, AllyHP=200, HostileHP=400, DeadAllyHP=0, but saw mana={start.Mana:F1} allyHP={start.C2AllyTargetHealth:F1} hostileHP={start.C2HostileTargetHealth:F1} deadAllyHP={start.C2DeadAllyTargetHealth:F1}.",
            failures);
        AddAcceptanceCheck(submitted.CastSubmitted &&
                string.Equals(submitted.LastAttemptTargetName, InteractionShowcaseIds.C2AllyTargetName, StringComparison.OrdinalIgnoreCase),
            $"Autoplay should submit the friendly heal at '{InteractionShowcaseIds.C2AllyTargetName}', but saw castSubmitted={submitted.CastSubmitted} target='{submitted.LastAttemptTargetName}'.", failures);
        AddAcceptanceCheck(healApplied.C2HealApplied,
            "Heal capture should report HealApplied=true.", failures);
        AddAcceptanceCheck(Math.Abs(healApplied.C2AllyTargetHealth - 350f) <= 0.001f,
            $"Ally target HP should rise to 350, but capture saw {healApplied.C2AllyTargetHealth:F3}.", failures);
        AddAcceptanceCheck(Math.Abs(healApplied.C2HealAmount - 150f) <= 0.001f,
            $"HealAmount should be 150, but capture saw {healApplied.C2HealAmount:F3}.", failures);
        AddAcceptanceCheck(string.Equals(healApplied.LastAttemptTargetName, InteractionShowcaseIds.C2AllyTargetName, StringComparison.OrdinalIgnoreCase),
            $"Heal capture should still reference ally target '{InteractionShowcaseIds.C2AllyTargetName}', but saw '{healApplied.LastAttemptTargetName}'.", failures);
        AddAcceptanceCheck(string.Equals(hostileTargetBlocked.LastCastFailReason, "InvalidTarget", StringComparison.Ordinal) &&
                string.Equals(hostileTargetBlocked.LastAttemptTargetName, InteractionShowcaseIds.C2HostileTargetName, StringComparison.OrdinalIgnoreCase),
            $"Hostile-target capture should report InvalidTarget on '{InteractionShowcaseIds.C2HostileTargetName}', but saw fail='{hostileTargetBlocked.LastCastFailReason}' target='{hostileTargetBlocked.LastAttemptTargetName}'.", failures);
        AddAcceptanceCheck(Math.Abs(hostileTargetBlocked.C2HostileTargetHealth - 400f) <= 0.001f,
            $"Hostile target should stay at 400 HP, but capture saw {hostileTargetBlocked.C2HostileTargetHealth:F3}.", failures);
        AddAcceptanceCheck(Math.Abs(hostileTargetBlocked.C2AllyTargetHealth - 350f) <= 0.001f,
            $"Hostile-target retry should not change ally HP, but capture saw {hostileTargetBlocked.C2AllyTargetHealth:F3}.", failures);
        AddAcceptanceCheck(string.Equals(deadAllyBlocked.LastCastFailReason, "InvalidTarget", StringComparison.Ordinal) &&
                string.Equals(deadAllyBlocked.LastAttemptTargetName, InteractionShowcaseIds.C2DeadAllyTargetName, StringComparison.OrdinalIgnoreCase),
            $"Dead-ally capture should report InvalidTarget on '{InteractionShowcaseIds.C2DeadAllyTargetName}', but saw fail='{deadAllyBlocked.LastCastFailReason}' target='{deadAllyBlocked.LastAttemptTargetName}'.", failures);
        AddAcceptanceCheck(Math.Abs(deadAllyBlocked.C2DeadAllyTargetHealth) <= 0.001f,
            $"Dead ally should stay at 0 HP, but capture saw {deadAllyBlocked.C2DeadAllyTargetHealth:F3}.", failures);
        AddAcceptanceCheck(Math.Abs(deadAllyBlocked.C2AllyTargetHealth - 350f) <= 0.001f,
            $"Dead-ally retry should not change healed ally HP, but capture saw {deadAllyBlocked.C2AllyTargetHealth:F3}.", failures);

        string normalizedSignature = string.Join("|", new[]
        {
            "interaction_c2_friendly_unit_heal",
            $"submitted:{submitted.Tick}:{submitted.LastAttemptTargetName}",
            $"heal:{healApplied.Tick}:{MathF.Round(healApplied.C2HealAmount):F0}:{MathF.Round(healApplied.C2AllyTargetHealth):F0}",
            $"hostile:{hostileTargetBlocked.Tick}:{hostileTargetBlocked.LastCastFailReason}",
            $"dead:{deadAllyBlocked.Tick}:{deadAllyBlocked.LastCastFailReason}"
        });

        string verdict = failures.Count == 0
            ? $"C2 friendly unit heal passes: order submits at tick {submitted.Tick}, ally HP rises to {healApplied.C2AllyTargetHealth:F0} with HealAmount={healApplied.C2HealAmount:F0}, then hostile-target and dead-ally retries both fail with InvalidTarget."
            : "C2 friendly unit heal fails: autoplay order, heal application, or invalid-target guard branches diverged from the scenario contract.";
        string failureSummary = failures.Count == 0 ? verdict : string.Join(Environment.NewLine, failures);

        return new C2InteractionAcceptanceResult(
            Success: failures.Count == 0,
            Verdict: verdict,
            FailureSummary: failureSummary,
            FailedChecks: failures,
            StartMana: start.Mana,
            StartAllyTargetHealth: start.C2AllyTargetHealth,
            StartHostileTargetHealth: start.C2HostileTargetHealth,
            StartDeadAllyTargetHealth: start.C2DeadAllyTargetHealth,
            HealAmount: healApplied.C2HealAmount,
            HealedAllyTargetHealth: healApplied.C2AllyTargetHealth,
            HostileTargetHealth: hostileTargetBlocked.C2HostileTargetHealth,
            DeadAllyTargetHealth: deadAllyBlocked.C2DeadAllyTargetHealth,
            HostileTargetFailReason: hostileTargetBlocked.LastCastFailReason,
            DeadAllyFailReason: deadAllyBlocked.LastCastFailReason,
            SubmittedTick: submitted.Tick,
            HealAppliedTick: healApplied.Tick,
            HostileTargetBlockedTick: hostileTargetBlocked.Tick,
            DeadAllyBlockedTick: deadAllyBlocked.Tick,
            NormalizedSignature: normalizedSignature);
    }

    private static string BuildInteractionC2BattleReport(
        LauncherRecordingRequest request,
        IReadOnlyList<InteractionSnapshot> timeline,
        IReadOnlyList<InteractionCaptureFrame> captureFrames,
        IReadOnlyList<double> frameTimesMs,
        C2InteractionAcceptanceResult acceptance)
    {
        double medianTickMs = Median(frameTimesMs.ToArray());
        double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
        string evidenceImages = string.Join(", ", captureFrames.Select(frame => $"`screens/{frame.FileName}`").Append("`screens/timeline.png`"));

        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: interaction-c2-friendly-unit-heal");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: cast a friendly-target heal on a wounded ally and prove the reusable GAS heal preset restores Health only on a valid allied unit.");
        sb.AppendLine("- Gameplay domain: launcher-started real mod bootstrap, real `OrderQueue` submission, real `Ability.Interaction.C2FriendlyUnitHeal` / `Effect.Interaction.C2FriendlyUnitHeal` execution.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Seed: none");
        sb.AppendLine("- Map: `mods/InteractionShowcaseMod/assets/Maps/interaction_c2_friendly_unit_heal.json`");
        sb.AppendLine("- Clock profile: fixed `1/60s`");
        sb.AppendLine($"- Adapter: `{request.Plan.AdapterId}`");
        sb.AppendLine($"- Launch command: `{request.CommandText}`");
        sb.AppendLine($"- Evidence images: {evidenceImages}");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Boot the launcher runtime with `InteractionShowcaseMod` rooted to the C2 showcase map.");
        sb.AppendLine($"2. Let autoplay warm up and submit slot `0` against `{InteractionShowcaseIds.C2AllyTargetName}`.");
        sb.AppendLine("3. Capture the heal-applied frame after the built-in `Heal` preset restores `+150 Health` to the ally.");
        sb.AppendLine($"4. Retry against `{InteractionShowcaseIds.C2HostileTargetName}` and `{InteractionShowcaseIds.C2DeadAllyTargetName}` to prove the showcase-local invalid-target guards.");
        sb.AppendLine();
        sb.AppendLine("## Expected Outcomes");
        sb.AppendLine("- Primary success condition: hero mana remains `100`, wounded ally Health moves `200 -> 350`, and the recorder captures `HealAmount=150`.");
        sb.AppendLine("- Failure branch conditions: the hostile retry fails with `InvalidTarget`; the dead-ally retry fails with `InvalidTarget`; neither retry changes any target Health.");
        sb.AppendLine("- Key metrics: ally/hostile/dead-ally Health, heal amount, autoplay stage, fail reasons, capture ticks.");
        sb.AppendLine("- Resource scope: current showcase does not configure mana cost, so hero mana stays at `100` for the whole capture.");
        sb.AppendLine("- Trace artifact split: root `trace.jsonl` is the per-tick headless acceptance stream; `visual/trace.jsonl` is the five-checkpoint launcher capture with the same core fields plus recorder metadata.");
        sb.AppendLine($"- Timing semantics: `heal_applied` is captured on tick `{acceptance.HealAppliedTick}` because recorder/autoplay label the first checkpoint that publishes the completed heal state. Read it as a presentation/observation tick, not a precise modifier-apply timestamp.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (InteractionSnapshot snapshot in timeline)
        {
            string overlay = snapshot.OverlayLines.Count == 0 ? "(none)" : snapshot.OverlayLines[0];
            sb.AppendLine($"- [T+{snapshot.Tick:000}] InteractionShowcase.{snapshot.Step} -> stage={snapshot.Stage} | mana={snapshot.Mana:F1} | allyHP={snapshot.C2AllyTargetHealth:F1} | hostileHP={snapshot.C2HostileTargetHealth:F1} | deadAllyHP={snapshot.C2DeadAllyTargetHealth:F1} | healAmount={snapshot.C2HealAmount:F1} | fail={snapshot.LastCastFailReason} | target={snapshot.LastAttemptTargetName} | overlay={overlay}");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
        sb.AppendLine($"- verdict: {acceptance.Verdict}");
        foreach (string failedCheck in acceptance.FailedChecks)
        {
            sb.AppendLine($"- failed-check: {failedCheck}");
        }

        sb.AppendLine("- runtime note: the positive branch is native GAS preset execution; both negative branches are currently showcase-local validation before `OrderQueue` submission.");
        sb.AppendLine("- validation-scope note: hostile-target rejection is still showcase-local before enqueue; although the effect config carries `relationFilter = Friendly`, the direct explicit-target gap tracked in `TD-2026-03-13-C3-DirectExplicitTargetRelationFilterGap` means this path is not treated as a proven native hostile fence.");
        sb.AppendLine("- validation-scope note: dead-ally rejection has no native alive-status fence in evidence; if the showcase-local alive gate were bypassed, the same `Friendly + Heal` config would revive the dead ally (`Health 0 -> 150`).");
        sb.AppendLine("- tech-debt: `TD-2026-03-13-C3-DirectExplicitTargetRelationFilterGap` -> `artifacts/techdebt/2026-03-13-c3-direct-explicit-target-relation-filter-gap.md`");
        sb.AppendLine("- tech-debt: `TD-2026-03-13-C2-DeadAllyAliveFenceGap` -> `artifacts/techdebt/2026-03-13-c2-dead-ally-alive-fence-gap.md`");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- screenshot captures: `{captureFrames.Count}`");
        sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
        sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
        sb.AppendLine($"- submitted tick: `{acceptance.SubmittedTick}`");
        sb.AppendLine($"- heal applied tick: `{acceptance.HealAppliedTick}`");
        sb.AppendLine($"- hostile target blocked tick: `{acceptance.HostileTargetBlockedTick}`");
        sb.AppendLine($"- dead ally blocked tick: `{acceptance.DeadAllyBlockedTick}`");
        sb.AppendLine($"- normalized signature: `{acceptance.NormalizedSignature}`");
        sb.AppendLine("- reusable wiring: `GameBootstrapper`, `ConfigPipeline`, `OrderQueue`, `AbilityExecSystem`, `EffectProcessingLoopSystem`, `EffectPresetType.Heal`, `ScreenOverlayBuffer`");
        return sb.ToString();
    }

    private static string BuildInteractionC2TraceJsonl(string adapterId, IReadOnlyList<InteractionSnapshot> timeline)
    {
        var lines = new List<string>(timeline.Count);
        for (int index = 0; index < timeline.Count; index++)
        {
            InteractionSnapshot snapshot = timeline[index];
            bool currentAttemptEnqueued =
                string.Equals(snapshot.Stage, "order_submitted", StringComparison.OrdinalIgnoreCase) &&
                snapshot.CastSubmitted &&
                string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C2AllyTargetName, StringComparison.OrdinalIgnoreCase) &&
                !snapshot.C2HealApplied &&
                snapshot.C2AllyTargetHealth <= 200.001f;
            bool blockedLocallyBeforeEnqueue =
                (string.Equals(snapshot.Stage, "hostile_target_blocked", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(snapshot.Stage, "dead_ally_blocked", StringComparison.OrdinalIgnoreCase)) &&
                snapshot.LastCastFailTick == snapshot.Tick;

            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"interaction-c2-{adapterId}-{index + 1:000}",
                tick = snapshot.Tick,
                step = snapshot.Step,
                script_tick = snapshot.ScriptTick,
                stage = snapshot.Stage,
                run_has_submitted_cast = snapshot.CastSubmitted,
                current_attempt_enqueued = currentAttemptEnqueued,
                blocked_locally_before_enqueue = blockedLocallyBeforeEnqueue,
                mana = Math.Round(snapshot.Mana, 2),
                ally_target_health = Math.Round(snapshot.C2AllyTargetHealth, 2),
                hostile_target_health = Math.Round(snapshot.C2HostileTargetHealth, 2),
                dead_ally_target_health = Math.Round(snapshot.C2DeadAllyTargetHealth, 2),
                heal_amount = Math.Round(snapshot.C2HealAmount, 2),
                heal_applied = snapshot.C2HealApplied,
                heal_applied_tick = snapshot.C2HealAppliedTick,
                last_attempt_target_name = snapshot.LastAttemptTargetName,
                last_cast_fail_reason = snapshot.LastCastFailReason,
                status = "done"
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildInteractionC2PathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart LR",
            "    A[Boot launcher runtime for InteractionShowcaseMod] --> B[Autoplay warmup on interaction_c2_friendly_unit_heal]",
            "    B --> C{OrderQueue submits slot 0 heal on C2AllyPrimary?}",
            "    C -->|yes| D[Effect.Interaction.C2FriendlyUnitHeal runs built-in Heal preset]",
            "    D --> E{Ally HP = 350 and HealAmount = 150?}",
            "    E -->|yes| F{Autoplay blocks enqueue on hostile target with InvalidTarget?}",
            "    F -->|yes| G{Autoplay blocks enqueue on dead ally with InvalidTarget?}",
            "    G -->|yes| H[Write battle-report + trace + path + PNG timeline]",
            "    C -->|no| X[Fail acceptance: autoplay/order pipeline diverged]",
            "    E -->|no| Y[Fail acceptance: heal preset or target state diverged]",
            "    F -->|no| Z[Fail acceptance: hostile-target local guard diverged]",
            "    G -->|no| Q[Fail acceptance: dead-ally local guard diverged]"
        }) + Environment.NewLine;
    }

    private static string BuildInteractionC2VisibleChecklist(IReadOnlyList<InteractionCaptureFrame> frames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: interaction-c2-friendly-unit-heal");
        sb.AppendLine();
        sb.AppendLine("- `000_start` should show the hero with Mana `100`, the ally target wounded at `HP=200`, the hostile target at `HP=400`, and the dead ally crossed out at `HP=0`.");
        sb.AppendLine("- `001_order_submitted` should highlight `C2AllyPrimary` as the selected target with no Health changes yet.");
        sb.AppendLine("- `002_heal_applied` should show the ally Health bar at `350` with a floating `+150` heal label.");
        sb.AppendLine("- `003_hostile_target_blocked` should keep the hostile target unchanged and show `InvalidTarget` in the overlay.");
        sb.AppendLine("- `004_dead_ally_blocked` should keep the dead ally unchanged, retain the cross marker, and show `InvalidTarget` in the overlay.");
        sb.AppendLine("- local-validation scope note: both negative branches are blocked by showcase autoplay before enqueue.");
        sb.AppendLine("- branch note: hostile-target rejection is still blocked locally before enqueue; although the effect config still carries `relationFilter = Friendly`, `TD-2026-03-13-C3-DirectExplicitTargetRelationFilterGap` means this direct explicit-target path is not treated as a proven native hostile fence.");
        sb.AppendLine("- branch note: dead-ally rejection has no native alive-status fence in current evidence.");
        sb.AppendLine("- checklist note: the per-frame `fail=` / `target=` lines below print raw GlobalContext values; the rendered overlay normalizes empty values to `LastResult=None` and `Target=-`.");
        sb.AppendLine("- tech-debt: hostile relation-filter uncertainty is tracked under `TD-2026-03-13-C3-DirectExplicitTargetRelationFilterGap`.");
        sb.AppendLine("- tech-debt: `TD-2026-03-13-C2-DeadAllyAliveFenceGap` keeps the dead-ally path in showcase-local isolation until Core exposes a native alive gate.");
        sb.AppendLine("- `screens/timeline.png` gives the five-frame strip for quick approval and Claude review.");
        sb.AppendLine();
        foreach (InteractionCaptureFrame frame in frames)
        {
            sb.AppendLine($"- `{frame.FileName}`: stage={frame.Stage}, allyHP={frame.C2AllyTargetHealth:F1}, hostileHP={frame.C2HostileTargetHealth:F1}, deadAllyHP={frame.C2DeadAllyTargetHealth:F1}, heal={frame.C2HealAmount:F1}, healApplied={frame.C2HealApplied}, fail={FormatInteractionDisplayValue(frame.LastCastFailReason, "None")}, target={FormatInteractionDisplayValue(frame.LastAttemptTargetName, "-")}");
        }

        return sb.ToString();
    }

    private static string BuildInteractionC2SummaryJson(LauncherRecordingRequest request, C2InteractionAcceptanceResult acceptance)
    {
        return JsonSerializer.Serialize(new
        {
            scenario = "interaction_c2_friendly_unit_heal",
            adapter = request.Plan.AdapterId,
            selectors = request.Plan.Selectors,
            root_mods = request.Plan.RootModIds,
            submitted_tick = acceptance.SubmittedTick,
            heal_applied_tick = acceptance.HealAppliedTick,
            hostile_target_blocked_tick = acceptance.HostileTargetBlockedTick,
            dead_ally_blocked_tick = acceptance.DeadAllyBlockedTick,
            start_mana = Math.Round(acceptance.StartMana, 2),
            start_ally_target_health = Math.Round(acceptance.StartAllyTargetHealth, 2),
            start_hostile_target_health = Math.Round(acceptance.StartHostileTargetHealth, 2),
            start_dead_ally_target_health = Math.Round(acceptance.StartDeadAllyTargetHealth, 2),
            heal_amount = Math.Round(acceptance.HealAmount, 2),
            healed_ally_target_health = Math.Round(acceptance.HealedAllyTargetHealth, 2),
            hostile_target_health = Math.Round(acceptance.HostileTargetHealth, 2),
            dead_ally_target_health = Math.Round(acceptance.DeadAllyTargetHealth, 2),
            hostile_target_fail_reason = acceptance.HostileTargetFailReason,
            dead_ally_fail_reason = acceptance.DeadAllyFailReason,
            mana_cost_configured = false,
            heal_applied_tick_semantics = "first_autoplay_observation_after_health_change",
            validation_scope = new
            {
                positive_branch = "native_gas_orderqueue_to_heal_preset",
                hostile_branch = "showcase_local_guard_only_direct_explicit_target_relation_filter_gap_tracked",
                dead_ally_branch = "showcase_local_alive_guard_only_no_native_alive_fence_proven"
            },
            hostile_relation_filter_tech_debt = new
            {
                id = "TD-2026-03-13-C3-DirectExplicitTargetRelationFilterGap",
                scope = "Cross-layer",
                applies_to = "direct_explicit_target_relation_filter_on_c2_hostile_retry"
            },
            tech_debt = new
            {
                id = "TD-2026-03-13-C2-DeadAllyAliveFenceGap",
                severity = "P1",
                scope = "Cross-layer",
                fuse_mode = "isolation",
                branch_reason_code = "dead_ally_native_alive_fence_missing"
            },
            normalized_signature = acceptance.NormalizedSignature
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string FormatInteractionDisplayValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static void WriteInteractionSnapshotImage(InteractionSnapshot snapshot, string path)
    {
        if (string.Equals(snapshot.ActiveScenarioId, InteractionShowcaseIds.C1HostileUnitDamageScenarioId, StringComparison.OrdinalIgnoreCase))
        {
            WriteInteractionC1SnapshotImage(snapshot, path);
            return;
        }

        if (string.Equals(snapshot.ActiveScenarioId, InteractionShowcaseIds.C2FriendlyUnitHealScenarioId, StringComparison.OrdinalIgnoreCase))
        {
            WriteInteractionC2SnapshotImage(snapshot, path);
            return;
        }

        WriteInteractionB1SnapshotImage(snapshot, path);
    }

    private static void WriteInteractionB1SnapshotImage(InteractionSnapshot snapshot, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(InteractionImageWidth, InteractionImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 24));

        var worldPoints = snapshot.NamedEntities.Values.ToList();
        if (worldPoints.Count == 0)
        {
            worldPoints.Add(Vector2.Zero);
        }

        float minX = worldPoints.Min(point => point.X) - 1400f;
        float maxX = worldPoints.Max(point => point.X) + 1400f;
        float minY = worldPoints.Min(point => point.Y) - 1400f;
        float maxY = worldPoints.Max(point => point.Y) + 1400f;

        using var gridPaint = new SKPaint { Color = new SKColor(34, 46, 66), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28f };
        using var bodyPaint = new SKPaint { Color = new SKColor(197, 208, 224), IsAntialias = true, TextSize = 18f };
        using var heroPaint = new SKPaint { Color = new SKColor(88, 214, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var enemyPaint = new SKPaint { Color = new SKColor(255, 132, 132), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var auraPaint = new SKPaint { Color = new SKColor(255, 214, 90), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5f };
        using var neutralPaint = new SKPaint { Color = new SKColor(172, 182, 204), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelFill = new SKPaint { Color = new SKColor(12, 18, 31, 222), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelBorder = new SKPaint { Color = new SKColor(82, 132, 210), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };

        DrawWorldGrid(canvas, minX, maxX, minY, maxY, gridPaint, InteractionImageWidth, InteractionImageHeight);

        foreach ((string name, Vector2 position) in snapshot.NamedEntities.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            SKPoint point = ToScreen(position, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            SKPaint fill = string.Equals(name, InteractionShowcaseIds.HeroName, StringComparison.OrdinalIgnoreCase)
                ? heroPaint
                : string.Equals(name, InteractionShowcaseIds.DummyName, StringComparison.OrdinalIgnoreCase)
                    ? enemyPaint
                    : neutralPaint;
            float radius = string.Equals(name, InteractionShowcaseIds.HeroName, StringComparison.OrdinalIgnoreCase) ? 13f : 11f;
            canvas.DrawCircle(point.X, point.Y, radius, fill);
            if (string.Equals(name, InteractionShowcaseIds.HeroName, StringComparison.OrdinalIgnoreCase) && snapshot.EmpoweredCount > 0)
            {
                canvas.DrawCircle(point.X, point.Y, 24f, auraPaint);
            }

            canvas.DrawText(name, point.X + 16f, point.Y - 12f, bodyPaint);
        }

        SKRect panel = SKRect.Create(22, 22, 760, 206);
        canvas.DrawRect(panel, panelFill);
        canvas.DrawRect(panel, panelBorder);
        canvas.DrawText($"Interaction Showcase | B1 Self Buff | {snapshot.Step}", 40, 58, titlePaint);
        canvas.DrawText($"Map={snapshot.ActiveMapId}  Scenario={snapshot.ActiveScenarioId}", 40, 92, bodyPaint);
        canvas.DrawText($"Stage={snapshot.Stage}  ScriptTick={snapshot.ScriptTick}  Tick={snapshot.Tick}", 40, 122, bodyPaint);
        canvas.DrawText($"AttackDamage={snapshot.AttackDamage:F1}  Mana={snapshot.Mana:F1}  EmpoweredCount={snapshot.EmpoweredCount}  EffectiveTag={(snapshot.EffectiveEmpoweredTag ? "true" : "false")}", 40, 152, bodyPaint);
        canvas.DrawText($"LastFail={snapshot.LastCastFailReason}  Attribute={snapshot.LastCastFailAttribute}  Delta={snapshot.LastCastFailDelta:F1}", 40, 182, bodyPaint);
        canvas.DrawText($"CastSubmitted={(snapshot.CastSubmitted ? "true" : "false")}  BuffExpired={(snapshot.BuffExpired ? "true" : "false")}  TickMs={snapshot.TickMs:F3}", 40, 212, bodyPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteInteractionC1SnapshotImage(InteractionSnapshot snapshot, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(InteractionImageWidth, InteractionImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 24));

        var worldPoints = snapshot.NamedEntities.Values.ToList();
        if (worldPoints.Count == 0)
        {
            worldPoints.Add(Vector2.Zero);
        }

        float minX = worldPoints.Min(point => point.X) - 900f;
        float maxX = worldPoints.Max(point => point.X) + 900f;
        float minY = worldPoints.Min(point => point.Y) - 1000f;
        float maxY = worldPoints.Max(point => point.Y) + 1000f;

        using var gridPaint = new SKPaint { Color = new SKColor(34, 46, 66), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28f };
        using var bodyPaint = new SKPaint { Color = new SKColor(197, 208, 224), IsAntialias = true, TextSize = 18f };
        using var heroPaint = new SKPaint { Color = new SKColor(88, 214, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var primaryPaint = new SKPaint { Color = new SKColor(255, 110, 110), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var invalidPaint = new SKPaint { Color = new SKColor(140, 144, 160), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var farPaint = new SKPaint { Color = new SKColor(255, 198, 96), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var ringPaint = new SKPaint { Color = new SKColor(255, 214, 90), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };
        using var rangePaint = new SKPaint { Color = new SKColor(120, 180, 255, 160), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var damagePaint = new SKPaint { Color = new SKColor(255, 92, 92), IsAntialias = true, TextSize = 34f };
        using var panelFill = new SKPaint { Color = new SKColor(12, 18, 31, 224), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelBorder = new SKPaint { Color = new SKColor(82, 132, 210), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var hpBackPaint = new SKPaint { Color = new SKColor(36, 44, 58), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var hpFrontPaint = new SKPaint { Color = new SKColor(86, 214, 116), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var hpMissingPaint = new SKPaint { Color = new SKColor(166, 78, 78), IsAntialias = true, Style = SKPaintStyle.Fill };

        DrawWorldGrid(canvas, minX, maxX, minY, maxY, gridPaint, InteractionImageWidth, InteractionImageHeight);

        if (snapshot.NamedEntities.TryGetValue(InteractionShowcaseIds.HeroName, out Vector2 heroPosition) &&
            snapshot.NamedEntities.TryGetValue(snapshot.LastAttemptTargetName, out Vector2 targetPosition) &&
            !string.IsNullOrWhiteSpace(snapshot.LastAttemptTargetName))
        {
            SKPoint heroPoint = ToScreen(heroPosition, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            SKPoint targetPoint = ToScreen(targetPosition, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            canvas.DrawLine(heroPoint, targetPoint, rangePaint);
        }

        foreach ((string name, Vector2 position) in snapshot.NamedEntities.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            SKPoint point = ToScreen(position, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            bool isHero = string.Equals(name, InteractionShowcaseIds.HeroName, StringComparison.OrdinalIgnoreCase);
            bool isPrimary = string.Equals(name, InteractionShowcaseIds.C1PrimaryTargetName, StringComparison.OrdinalIgnoreCase);
            bool isInvalid = string.Equals(name, InteractionShowcaseIds.C1InvalidTargetName, StringComparison.OrdinalIgnoreCase);
            bool isFar = string.Equals(name, InteractionShowcaseIds.C1FarTargetName, StringComparison.OrdinalIgnoreCase);

            SKPaint fill = isHero
                ? heroPaint
                : isPrimary
                    ? primaryPaint
                    : isInvalid
                        ? invalidPaint
                        : isFar
                            ? farPaint
                            : primaryPaint;

            float radius = isHero ? 13f : 11f;
            canvas.DrawCircle(point.X, point.Y, radius, fill);
            if (string.Equals(name, snapshot.LastAttemptTargetName, StringComparison.OrdinalIgnoreCase))
            {
                canvas.DrawCircle(point.X, point.Y, radius + 10f, ringPaint);
            }

            canvas.DrawText(name, point.X + 16f, point.Y - 12f, bodyPaint);
            if (isPrimary)
            {
                DrawInteractionHealthBar(canvas, point, snapshot.PrimaryTargetHealth, 500f, hpBackPaint, hpMissingPaint, hpFrontPaint, bodyPaint);
            }
            else if (isInvalid)
            {
                DrawInteractionHealthBar(canvas, point, snapshot.InvalidTargetHealth, 500f, hpBackPaint, hpMissingPaint, hpFrontPaint, bodyPaint);
                canvas.DrawLine(point.X - 16f, point.Y - 16f, point.X + 16f, point.Y + 16f, ringPaint);
                canvas.DrawLine(point.X - 16f, point.Y + 16f, point.X + 16f, point.Y - 16f, ringPaint);
            }
            else if (isFar)
            {
                DrawInteractionHealthBar(canvas, point, snapshot.FarTargetHealth, 500f, hpBackPaint, hpMissingPaint, hpFrontPaint, bodyPaint);
            }
        }

        if (snapshot.DamageApplied &&
            snapshot.NamedEntities.TryGetValue(InteractionShowcaseIds.C1PrimaryTargetName, out Vector2 primaryPosition))
        {
            SKPoint point = ToScreen(primaryPosition, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            canvas.DrawText($"-{MathF.Round(snapshot.FinalDamage):F0}", point.X - 28f, point.Y - 44f, damagePaint);
        }

        SKRect panel = SKRect.Create(22, 22, 920, 236);
        canvas.DrawRect(panel, panelFill);
        canvas.DrawRect(panel, panelBorder);

        string failReason = string.IsNullOrWhiteSpace(snapshot.LastCastFailReason) ? "None" : snapshot.LastCastFailReason;
        string lastTarget = string.IsNullOrWhiteSpace(snapshot.LastAttemptTargetName) ? "-" : snapshot.LastAttemptTargetName;

        canvas.DrawText($"Interaction Showcase | C1 Hostile Unit Damage | {snapshot.Step}", 40, 58, titlePaint);
        canvas.DrawText($"Map={snapshot.ActiveMapId}  Scenario={snapshot.ActiveScenarioId}", 40, 92, bodyPaint);
        canvas.DrawText($"Stage={snapshot.Stage}  ScriptTick={snapshot.ScriptTick}  Tick={snapshot.Tick}  TickMs={snapshot.TickMs:F3}", 40, 122, bodyPaint);
        canvas.DrawText($"HeroBaseDamage={snapshot.HeroBaseDamage:F1}  Mana={snapshot.Mana:F1}  CastSubmitted={(snapshot.CastSubmitted ? "true" : "false")}  DamageApplied={(snapshot.DamageApplied ? "true" : "false")}", 40, 152, bodyPaint);
        canvas.DrawText($"PrimaryHP={snapshot.PrimaryTargetHealth:F1}  Armor={snapshot.PrimaryTargetArmor:F1}  DamageAmount={snapshot.DamageAmount:F1}  FinalDamage={snapshot.FinalDamage:F1}", 40, 182, bodyPaint);
        canvas.DrawText($"InvalidHP={snapshot.InvalidTargetHealth:F1}  FarHP={snapshot.FarTargetHealth:F1}  LastFail={failReason}  Target={lastTarget}", 40, 212, bodyPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteInteractionC2SnapshotImage(InteractionSnapshot snapshot, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(InteractionImageWidth, InteractionImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 24));

        var worldPoints = snapshot.NamedEntities.Values.ToList();
        if (worldPoints.Count == 0)
        {
            worldPoints.Add(Vector2.Zero);
        }

        float minX = worldPoints.Min(point => point.X) - 900f;
        float maxX = worldPoints.Max(point => point.X) + 900f;
        float minY = worldPoints.Min(point => point.Y) - 1000f;
        float maxY = worldPoints.Max(point => point.Y) + 1000f;

        using var gridPaint = new SKPaint { Color = new SKColor(34, 46, 66), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28f };
        using var bodyPaint = new SKPaint { Color = new SKColor(197, 208, 224), IsAntialias = true, TextSize = 18f };
        using var heroPaint = new SKPaint { Color = new SKColor(88, 214, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var allyPaint = new SKPaint { Color = new SKColor(90, 214, 130), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var hostilePaint = new SKPaint { Color = new SKColor(255, 116, 116), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var deadPaint = new SKPaint { Color = new SKColor(150, 156, 170), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var ringPaint = new SKPaint { Color = new SKColor(255, 214, 90), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };
        using var tetherPaint = new SKPaint { Color = new SKColor(120, 180, 255, 160), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var healPaint = new SKPaint { Color = new SKColor(110, 255, 154), IsAntialias = true, TextSize = 34f };
        using var panelFill = new SKPaint { Color = new SKColor(12, 18, 31, 224), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelBorder = new SKPaint { Color = new SKColor(82, 132, 210), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var hpBackPaint = new SKPaint { Color = new SKColor(36, 44, 58), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var hpFrontPaint = new SKPaint { Color = new SKColor(86, 214, 116), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var hpMissingPaint = new SKPaint { Color = new SKColor(166, 78, 78), IsAntialias = true, Style = SKPaintStyle.Fill };

        DrawWorldGrid(canvas, minX, maxX, minY, maxY, gridPaint, InteractionImageWidth, InteractionImageHeight);

        if (snapshot.NamedEntities.TryGetValue(InteractionShowcaseIds.HeroName, out Vector2 heroPosition) &&
            snapshot.NamedEntities.TryGetValue(snapshot.LastAttemptTargetName, out Vector2 targetPosition) &&
            !string.IsNullOrWhiteSpace(snapshot.LastAttemptTargetName))
        {
            SKPoint heroPoint = ToScreen(heroPosition, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            SKPoint targetPoint = ToScreen(targetPosition, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            canvas.DrawLine(heroPoint, targetPoint, tetherPaint);
        }

        foreach ((string name, Vector2 position) in snapshot.NamedEntities.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            SKPoint point = ToScreen(position, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            bool isHero = string.Equals(name, InteractionShowcaseIds.HeroName, StringComparison.OrdinalIgnoreCase);
            bool isAlly = string.Equals(name, InteractionShowcaseIds.C2AllyTargetName, StringComparison.OrdinalIgnoreCase);
            bool isHostile = string.Equals(name, InteractionShowcaseIds.C2HostileTargetName, StringComparison.OrdinalIgnoreCase);
            bool isDeadAlly = string.Equals(name, InteractionShowcaseIds.C2DeadAllyTargetName, StringComparison.OrdinalIgnoreCase);

            SKPaint fill = isHero
                ? heroPaint
                : isAlly
                    ? allyPaint
                    : isHostile
                        ? hostilePaint
                        : isDeadAlly
                            ? deadPaint
                            : allyPaint;

            float radius = isHero ? 13f : 11f;
            canvas.DrawCircle(point.X, point.Y, radius, fill);
            if (string.Equals(name, snapshot.LastAttemptTargetName, StringComparison.OrdinalIgnoreCase))
            {
                canvas.DrawCircle(point.X, point.Y, radius + 10f, ringPaint);
            }

            canvas.DrawText(name, point.X + 16f, point.Y - 12f, bodyPaint);
            if (isAlly)
            {
                DrawInteractionHealthBar(canvas, point, snapshot.C2AllyTargetHealth, 500f, hpBackPaint, hpMissingPaint, hpFrontPaint, bodyPaint);
            }
            else if (isHostile)
            {
                DrawInteractionHealthBar(canvas, point, snapshot.C2HostileTargetHealth, 400f, hpBackPaint, hpMissingPaint, hpFrontPaint, bodyPaint);
            }
            else if (isDeadAlly)
            {
                DrawInteractionHealthBar(canvas, point, snapshot.C2DeadAllyTargetHealth, 500f, hpBackPaint, hpMissingPaint, hpFrontPaint, bodyPaint);
                canvas.DrawLine(point.X - 16f, point.Y - 16f, point.X + 16f, point.Y + 16f, ringPaint);
                canvas.DrawLine(point.X - 16f, point.Y + 16f, point.X + 16f, point.Y - 16f, ringPaint);
            }
        }

        if (string.Equals(snapshot.Stage, "heal_applied", StringComparison.OrdinalIgnoreCase) &&
            snapshot.NamedEntities.TryGetValue(InteractionShowcaseIds.C2AllyTargetName, out Vector2 allyPosition))
        {
            SKPoint point = ToScreen(allyPosition, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            string healText = $"+{MathF.Round(snapshot.C2HealAmount):F0}";
            float textWidth = healPaint.MeasureText(healText);
            canvas.DrawText(healText, point.X - (textWidth / 2f), point.Y - 56f, healPaint);
        }

        SKRect panel = SKRect.Create(22, 22, 980, 236);
        canvas.DrawRect(panel, panelFill);
        canvas.DrawRect(panel, panelBorder);

        string failReason = string.IsNullOrWhiteSpace(snapshot.LastCastFailReason) ? "None" : snapshot.LastCastFailReason;
        string lastTarget = string.IsNullOrWhiteSpace(snapshot.LastAttemptTargetName) ? "-" : snapshot.LastAttemptTargetName;

        canvas.DrawText($"Interaction Showcase | C2 Friendly Unit Heal | {snapshot.Step}", 40, 58, titlePaint);
        canvas.DrawText($"Map={snapshot.ActiveMapId}  Scenario={snapshot.ActiveScenarioId}", 40, 92, bodyPaint);
        canvas.DrawText($"Stage={snapshot.Stage}  ScriptTick={snapshot.ScriptTick}  Tick={snapshot.Tick}  TickMs={snapshot.TickMs:F3}", 40, 122, bodyPaint);
        canvas.DrawText($"HeroMana={snapshot.Mana:F1}  CastSubmitted={(snapshot.CastSubmitted ? "true" : "false")}  HealApplied={(snapshot.C2HealApplied ? "true" : "false")}  HealTick={snapshot.C2HealAppliedTick}", 40, 152, bodyPaint);
        canvas.DrawText($"AllyHP={snapshot.C2AllyTargetHealth:F1}  HostileHP={snapshot.C2HostileTargetHealth:F1}  DeadAllyHP={snapshot.C2DeadAllyTargetHealth:F1}  HealAmount={snapshot.C2HealAmount:F1}", 40, 182, bodyPaint);
        canvas.DrawText($"LastFail={failReason}  Target={lastTarget}", 40, 212, bodyPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static void WriteInteractionC3SnapshotImage(C3InteractionSnapshot snapshot, string path)
    {
        using var surface = SKSurface.Create(new SKImageInfo(InteractionImageWidth, InteractionImageHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 14, 24));

        var worldPoints = snapshot.NamedEntities.Values.ToList();
        if (worldPoints.Count == 0)
        {
            worldPoints.Add(Vector2.Zero);
        }

        float minX = worldPoints.Min(point => point.X) - 900f;
        float maxX = worldPoints.Max(point => point.X) + 900f;
        float minY = worldPoints.Min(point => point.Y) - 1000f;
        float maxY = worldPoints.Max(point => point.Y) + 1000f;

        using var gridPaint = new SKPaint { Color = new SKColor(34, 46, 66), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28f };
        using var bodyPaint = new SKPaint { Color = new SKColor(197, 208, 224), IsAntialias = true, TextSize = 18f };
        using var heroPaint = new SKPaint { Color = new SKColor(88, 214, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var hostilePaint = new SKPaint { Color = new SKColor(255, 116, 116), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var friendlyPaint = new SKPaint { Color = new SKColor(90, 214, 130), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var ringPaint = new SKPaint { Color = new SKColor(255, 214, 90), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };
        using var tetherPaint = new SKPaint { Color = new SKColor(120, 180, 255, 160), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        using var hostileTagPaint = new SKPaint { Color = new SKColor(255, 190, 110), IsAntialias = true, TextSize = 28f };
        using var friendlyTagPaint = new SKPaint { Color = new SKColor(110, 255, 154), IsAntialias = true, TextSize = 28f };
        using var statPaint = new SKPaint { Color = new SKColor(214, 224, 240), IsAntialias = true, TextSize = 20f };
        using var panelFill = new SKPaint { Color = new SKColor(12, 18, 31, 224), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var panelBorder = new SKPaint { Color = new SKColor(82, 132, 210), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };

        DrawWorldGrid(canvas, minX, maxX, minY, maxY, gridPaint, InteractionImageWidth, InteractionImageHeight);

        if (snapshot.NamedEntities.TryGetValue(InteractionShowcaseIds.HeroName, out Vector2 heroPosition) &&
            snapshot.NamedEntities.TryGetValue(snapshot.LastAttemptTargetName, out Vector2 targetPosition) &&
            !string.IsNullOrWhiteSpace(snapshot.LastAttemptTargetName))
        {
            SKPoint heroPoint = ToScreen(heroPosition, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            SKPoint targetPoint = ToScreen(targetPosition, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            canvas.DrawLine(heroPoint, targetPoint, tetherPaint);
        }

        foreach ((string name, Vector2 position) in snapshot.NamedEntities.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            SKPoint point = ToScreen(position, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            bool isHero = string.Equals(name, InteractionShowcaseIds.HeroName, StringComparison.OrdinalIgnoreCase);
            bool isHostile = string.Equals(name, InteractionShowcaseIds.C3HostileTargetName, StringComparison.OrdinalIgnoreCase);
            bool isFriendly = string.Equals(name, InteractionShowcaseIds.C3FriendlyTargetName, StringComparison.OrdinalIgnoreCase);

            SKPaint fill = isHero
                ? heroPaint
                : isHostile
                    ? hostilePaint
                    : friendlyPaint;

            float radius = isHero ? 13f : 11f;
            canvas.DrawCircle(point.X, point.Y, radius, fill);
            if (string.Equals(name, snapshot.LastAttemptTargetName, StringComparison.OrdinalIgnoreCase))
            {
                canvas.DrawCircle(point.X, point.Y, radius + 10f, ringPaint);
            }

            canvas.DrawText(name, point.X + 16f, point.Y - 14f, bodyPaint);
            if (isHostile)
            {
                canvas.DrawText($"MS {snapshot.HostileMoveSpeed:F0}", point.X - 28f, point.Y + 42f, statPaint);
                if (snapshot.HostilePolymorphActive)
                {
                    canvas.DrawText("POLYMORPH", point.X - 74f, point.Y - 46f, hostileTagPaint);
                }
            }
            else if (isFriendly)
            {
                canvas.DrawText($"MS {snapshot.FriendlyMoveSpeed:F0}", point.X - 24f, point.Y + 42f, statPaint);
                if (snapshot.FriendlyHasteActive)
                {
                    canvas.DrawText("HASTE", point.X - 42f, point.Y - 46f, friendlyTagPaint);
                }
            }
        }

        if (string.Equals(snapshot.Stage, "hostile_polymorph_applied", StringComparison.OrdinalIgnoreCase) &&
            snapshot.NamedEntities.TryGetValue(InteractionShowcaseIds.C3HostileTargetName, out Vector2 hostilePosition))
        {
            SKPoint point = ToScreen(hostilePosition, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            canvas.DrawText("-120 MS", point.X - 56f, point.Y - 78f, hostileTagPaint);
        }

        if (string.Equals(snapshot.Stage, "friendly_haste_applied", StringComparison.OrdinalIgnoreCase) &&
            snapshot.NamedEntities.TryGetValue(InteractionShowcaseIds.C3FriendlyTargetName, out Vector2 friendlyPosition))
        {
            SKPoint point = ToScreen(friendlyPosition, minX, maxX, minY, maxY, InteractionImageWidth, InteractionImageHeight);
            canvas.DrawText("+80 MS", point.X - 40f, point.Y - 78f, friendlyTagPaint);
        }

        SKRect panel = SKRect.Create(22, 22, 980, 236);
        canvas.DrawRect(panel, panelFill);
        canvas.DrawRect(panel, panelBorder);

        string failReason = string.IsNullOrWhiteSpace(snapshot.LastCastFailReason) ? "None" : snapshot.LastCastFailReason;
        string lastTarget = string.IsNullOrWhiteSpace(snapshot.LastAttemptTargetName) ? "-" : snapshot.LastAttemptTargetName;

        canvas.DrawText($"Interaction Showcase | C3 Any Unit Conditional | {snapshot.Step}", 40, 58, titlePaint);
        canvas.DrawText($"Map={snapshot.ActiveMapId}  Scenario={snapshot.ActiveScenarioId}", 40, 92, bodyPaint);
        canvas.DrawText($"Stage={snapshot.Stage}  ScriptTick={snapshot.ScriptTick}  Tick={snapshot.Tick}  TickMs={snapshot.TickMs:F3}", 40, 122, bodyPaint);
        canvas.DrawText($"HeroMana={snapshot.Mana:F1}  CastSubmitted={(snapshot.CastSubmitted ? "true" : "false")}  HostileTick={snapshot.HostilePolymorphAppliedTick}  FriendlyTick={snapshot.FriendlyHasteAppliedTick}", 40, 152, bodyPaint);
        canvas.DrawText($"HostileMS={snapshot.HostileMoveSpeed:F1}  PolyActive={snapshot.HostilePolymorphActive}/{snapshot.HostilePolymorphCount}  FriendlyMS={snapshot.FriendlyMoveSpeed:F1}  HasteActive={snapshot.FriendlyHasteActive}/{snapshot.FriendlyHasteCount}", 40, 182, bodyPaint);
        canvas.DrawText($"LastFail={failReason}  Target={lastTarget}", 40, 212, bodyPaint);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(stream);
    }

    private static string ReadGlobalString(GameEngine engine, string key, string fallback)
    {
        return engine.GlobalContext.TryGetValue(key, out object? value) && value is string text
            ? text
            : fallback;
    }

    private static int ReadGlobalInt(GameEngine engine, string key, int fallback)
    {
        return engine.GlobalContext.TryGetValue(key, out object? value) && value is int number
            ? number
            : fallback;
    }

    private static float ReadGlobalFloat(GameEngine engine, string key, float fallback)
    {
        return engine.GlobalContext.TryGetValue(key, out object? value) && value is float number
            ? number
            : fallback;
    }

    private static bool ReadGlobalBool(GameEngine engine, string key)
    {
        return engine.GlobalContext.TryGetValue(key, out object? value) && value is bool flag && flag;
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
        string dump = string.Join(" || ", ExtractOverlayText(overlay, clearAfterRead: true));
        if (!dump.Contains("Navigation2D Playground", StringComparison.Ordinal) ||
            !dump.Contains("FlowEnabled=", StringComparison.Ordinal) ||
            !dump.Contains("CacheLookups=", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Navigation overlay lines are incomplete: {dump}");
        }
    }

    private static List<string> ExtractOverlayText(ScreenOverlayBuffer? overlay, bool clearAfterRead = false)
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

        if (clearAfterRead)
        {
            overlay.Clear();
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

    private static void DrawInteractionHealthBar(
        SKCanvas canvas,
        SKPoint center,
        float current,
        float max,
        SKPaint backgroundPaint,
        SKPaint missingPaint,
        SKPaint filledPaint,
        SKPaint textPaint)
    {
        const float width = 110f;
        const float height = 10f;
        float ratio = max <= 0f ? 0f : Math.Clamp(current / max, 0f, 1f);
        SKRect bar = SKRect.Create(center.X - width / 2f, center.Y + 18f, width, height);
        canvas.DrawRect(bar, backgroundPaint);
        canvas.DrawRect(bar, missingPaint);
        if (ratio > 0f)
        {
            canvas.DrawRect(SKRect.Create(bar.Left, bar.Top, width * ratio, height), filledPaint);
        }

        canvas.DrawText($"HP {current:F0}/{max:F0}", center.X - width / 2f, center.Y + 46f, textPaint);
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

    private static void WriteTimelineSheet(string title, IReadOnlyList<InteractionCaptureFrame> frames, string screensDir, string outputPath)
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

    private static void WriteTimelineSheet(string title, IReadOnlyList<C3InteractionCaptureFrame> frames, string screensDir, string outputPath)
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
        InteractionShowcase,
        Navigation2DPlaygroundTimedAvoidance
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
            var world = new WorldCmInt2((int)MathF.Round(worldCm.X), (int)MathF.Round(worldCm.Y));
            return ScreenProjector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(world, yMeters: 0f));
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

    private readonly record struct InteractionSnapshot(
        int Tick,
        string Step,
        double TickMs,
        string ActiveMapId,
        string ActiveScenarioId,
        int ScriptTick,
        string Stage,
        bool HeroPresent,
        float AttackDamage,
        float Mana,
        float HeroBaseDamage,
        float PrimaryTargetHealth,
        float PrimaryTargetArmor,
        float InvalidTargetHealth,
        float FarTargetHealth,
        float DamageAmount,
        float FinalDamage,
        bool DamageApplied,
        int DamageAppliedTick,
        float C2AllyTargetHealth,
        float C2HostileTargetHealth,
        float C2DeadAllyTargetHealth,
        float C2HealAmount,
        bool C2HealApplied,
        int C2HealAppliedTick,
        string LastAttemptTargetName,
        bool EffectiveEmpoweredTag,
        int EmpoweredCount,
        bool CastSubmitted,
        int CastSubmittedTick,
        bool BuffObserved,
        bool BuffExpired,
        string LastCastFailReason,
        int LastCastFailTick,
        string LastCastFailAttribute,
        float LastCastFailDelta,
        IReadOnlyDictionary<string, Vector2> NamedEntities,
        IReadOnlyList<string> OverlayLines);

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

    private readonly record struct InteractionCaptureFrame(
        int Tick,
        string Step,
        string FileName,
        string ScenarioId,
        string Stage,
        float AttackDamage,
        float Mana,
        int EmpoweredCount,
        string LastCastFailReason,
        float PrimaryTargetHealth,
        float InvalidTargetHealth,
        float FarTargetHealth,
        float DamageAmount,
        float FinalDamage,
        bool DamageApplied,
        string LastAttemptTargetName,
        float C2AllyTargetHealth,
        float C2HostileTargetHealth,
        float C2DeadAllyTargetHealth,
        float C2HealAmount,
        bool C2HealApplied,
        int C2HealAppliedTick);

    private sealed record InteractionAcceptanceResult(
        bool Success,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> FailedChecks,
        float StartAttackDamage,
        float ActiveAttackDamage,
        float ExpiredAttackDamage,
        float StartMana,
        float InsufficientMana,
        int ActiveEmpoweredCount,
        int ExpiredEmpoweredCount,
        string SilencedFailReason,
        string InsufficientManaFailReason,
        int SubmittedTick,
        int ActiveTick,
        int ExpiredTick,
        int SilencedBlockedTick,
        int InsufficientManaBlockedTick,
        string NormalizedSignature);

    private sealed record C1InteractionAcceptanceResult(
        bool Success,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> FailedChecks,
        float StartHeroBaseDamage,
        float StartPrimaryTargetHealth,
        float DamageAmount,
        float FinalDamage,
        float DamageAppliedPrimaryTargetHealth,
        float InvalidTargetHealth,
        float FarTargetHealth,
        string InvalidTargetFailReason,
        string OutOfRangeFailReason,
        int SubmittedTick,
        int DamageAppliedTick,
        int InvalidTargetBlockedTick,
        int OutOfRangeBlockedTick,
        string NormalizedSignature);

    private sealed record C2InteractionAcceptanceResult(
        bool Success,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> FailedChecks,
        float StartMana,
        float StartAllyTargetHealth,
        float StartHostileTargetHealth,
        float StartDeadAllyTargetHealth,
        float HealAmount,
        float HealedAllyTargetHealth,
        float HostileTargetHealth,
        float DeadAllyTargetHealth,
        string HostileTargetFailReason,
        string DeadAllyFailReason,
        int SubmittedTick,
        int HealAppliedTick,
        int HostileTargetBlockedTick,
        int DeadAllyBlockedTick,
        string NormalizedSignature);

    private readonly record struct C3InteractionSnapshot(
        int Tick,
        string Step,
        double TickMs,
        string ActiveMapId,
        string ActiveScenarioId,
        int ScriptTick,
        string Stage,
        bool HeroPresent,
        float Mana,
        float HostileMoveSpeed,
        float FriendlyMoveSpeed,
        bool HostilePolymorphActive,
        int HostilePolymorphCount,
        bool HostilePolymorphApplied,
        int HostilePolymorphAppliedTick,
        bool FriendlyHasteActive,
        int FriendlyHasteCount,
        bool FriendlyHasteApplied,
        int FriendlyHasteAppliedTick,
        bool CastSubmitted,
        int CastSubmittedTick,
        string LastAttemptTargetName,
        string LastCastFailReason,
        IReadOnlyDictionary<string, Vector2> NamedEntities,
        IReadOnlyList<string> OverlayLines);

    private readonly record struct C3InteractionCaptureFrame(
        int Tick,
        string Step,
        string FileName,
        string ScenarioId,
        string Stage,
        float Mana,
        float HostileMoveSpeed,
        float FriendlyMoveSpeed,
        bool HostilePolymorphActive,
        bool FriendlyHasteActive,
        string LastCastFailReason,
        string LastAttemptTargetName);

    private sealed record C3InteractionAcceptanceResult(
        bool Success,
        string Verdict,
        string FailureSummary,
        IReadOnlyList<string> FailedChecks,
        float StartMana,
        float StartHostileMoveSpeed,
        float StartFriendlyMoveSpeed,
        float HostileMoveSpeed,
        float FriendlyMoveSpeed,
        int HostilePolymorphCount,
        int FriendlyHasteCount,
        int SubmittedHostileTick,
        int HostileCaptureTick,
        int HostileAppliedTick,
        int SubmittedFriendlyTick,
        int FriendlyCaptureTick,
        int FriendlyAppliedTick,
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
}
