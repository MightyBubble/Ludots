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
