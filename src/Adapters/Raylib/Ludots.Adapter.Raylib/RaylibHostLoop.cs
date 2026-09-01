using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Arch.Core;
using Ludots.Adapter.Raylib.Services;
using Ludots.Client.Raylib.Rendering;
using Ludots.Client.Raylib.Input;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Map;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Platform.Abstractions;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Core.Vision;
using Ludots.Platform.Abstractions;
using Ludots.Presentation.Skia;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using SkiaSharp;
using Ludots.Raylib.Render;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Adapter.Raylib
{
    internal static class RaylibHostLoop
    {
        private static VertexMapTerrainChunkMeshSource? _terrainSource;

        private const uint FlagWindowResizable = 4;
        private static readonly ServiceKey<IRaylibBenchmarkRenderer> RaylibBenchmarkRendererKey = new("Platform.RaylibBenchmarkRenderer");
        private static bool _uiPointerCaptured;
        private static PointerButton? _uiCapturedPointerButton;
        private static bool _hasLastUiPointerMove;
        private static float _lastUiPointerMoveX;
        private static float _lastUiPointerMoveY;
        private static bool _emptyBufferWarned;
        private static readonly MouseButton[] MouseButtonsInPriorityOrder =
        {
            MouseButton.MOUSE_LEFT_BUTTON,
            MouseButton.MOUSE_RIGHT_BUTTON,
            MouseButton.MOUSE_MIDDLE_BUTTON
        };
        private static readonly KeyboardKey[] BrowserForwardedKeys =
        {
            KeyboardKey.KEY_ENTER,
            KeyboardKey.KEY_TAB,
            KeyboardKey.KEY_BACKSPACE,
            KeyboardKey.KEY_DELETE,
            KeyboardKey.KEY_ESCAPE,
            KeyboardKey.KEY_LEFT,
            KeyboardKey.KEY_RIGHT,
            KeyboardKey.KEY_UP,
            KeyboardKey.KEY_DOWN,
            KeyboardKey.KEY_HOME,
            KeyboardKey.KEY_END,
            KeyboardKey.KEY_PAGE_UP,
            KeyboardKey.KEY_PAGE_DOWN,
            KeyboardKey.KEY_SPACE,
            KeyboardKey.KEY_A,
            KeyboardKey.KEY_B,
            KeyboardKey.KEY_C,
            KeyboardKey.KEY_D,
            KeyboardKey.KEY_E,
            KeyboardKey.KEY_F,
            KeyboardKey.KEY_G,
            KeyboardKey.KEY_H,
            KeyboardKey.KEY_I,
            KeyboardKey.KEY_J,
            KeyboardKey.KEY_K,
            KeyboardKey.KEY_L,
            KeyboardKey.KEY_M,
            KeyboardKey.KEY_N,
            KeyboardKey.KEY_O,
            KeyboardKey.KEY_P,
            KeyboardKey.KEY_Q,
            KeyboardKey.KEY_R,
            KeyboardKey.KEY_S,
            KeyboardKey.KEY_T,
            KeyboardKey.KEY_U,
            KeyboardKey.KEY_V,
            KeyboardKey.KEY_W,
            KeyboardKey.KEY_X,
            KeyboardKey.KEY_Y,
            KeyboardKey.KEY_Z,
            KeyboardKey.KEY_ZERO,
            KeyboardKey.KEY_ONE,
            KeyboardKey.KEY_TWO,
            KeyboardKey.KEY_THREE,
            KeyboardKey.KEY_FOUR,
            KeyboardKey.KEY_FIVE,
            KeyboardKey.KEY_SIX,
            KeyboardKey.KEY_SEVEN,
            KeyboardKey.KEY_EIGHT,
            KeyboardKey.KEY_NINE
        };

        private sealed class SyntheticUiPlayback
        {
            public bool Enabled { get; init; }

            public int StartFrame { get; init; }

            public int EndFrame { get; init; }

            public float StartX { get; init; }

            public float StartY { get; init; }

            public float EndX { get; init; }

            public float EndY { get; init; }

            public int ScrollFrame { get; init; }

            public float ScrollDeltaY { get; init; }

            public int KeyFrame { get; init; }

            public string Key { get; init; } = string.Empty;

            public string KeyText { get; init; } = string.Empty;
        }

        private readonly record struct UiInputFrameResult(bool Handled, bool PointerCaptured, bool WheelCaptured);

        /// <summary>
        /// One driven present binding captured at drive time with the concrete camera pose the
        /// presenter produced for it; the draw phase renders exactly one viewport per entry.
        /// </summary>
        private readonly record struct ViewportDrawFrame(
            Ludots.Core.Client.PresentBindingDrawFrame Frame,
            Camera3D Camera,
            CameraRenderState3D CameraState);

        internal static bool ShouldCaptureWorldPointer(
            bool pointerCaptured,
            bool wheelCaptured,
            bool inputHandled)
        {
            return pointerCaptured || wheelCaptured || inputHandled;
        }

        public static void Run(RaylibHostSetup setup)
        {
            Ludots.Raylib.Render.RenderDiagnostics.InfoSink = static message =>
                Ludots.Core.Diagnostics.Log.Info(in Ludots.Core.Diagnostics.LogChannels.Presentation, message);
            Ludots.Raylib.Render.RenderDiagnostics.WarnSink = static message =>
                Ludots.Core.Diagnostics.Log.Warn(in Ludots.Core.Diagnostics.LogChannels.Presentation, message);

            var engine = setup.Engine;
            var config = setup.Config;
            var uiRoot = setup.UiRoot;
            var skiaRenderer = setup.Renderer;
            var presentationTiming = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            engine.TryGetService(CoreServiceKeys.SyntheticInput, out SyntheticInputDevice? syntheticInput);
            engine.TryGetService(CoreServiceKeys.HostFrameCapture, out IHostFrameCapture? frameCapture);
            engine.TryGetService(CoreServiceKeys.SoundRequestBuffer, out SoundRequestBuffer? soundRequests);
            ClientLocalSeatDeviceBinding seatDeviceBinding = engine.GetService(CoreServiceKeys.ClientLocalSeatDeviceBinding)
                ?? throw new InvalidOperationException("ClientLocalSeatDeviceBinding missing.");
            var deviceWatcher = new RaylibInputDeviceWatcher();
            deviceWatcher.DeviceChanged += seatDeviceBinding.HandleDeviceChange;
            engine.SetService(CoreServiceKeys.InputDeviceWatcher, deviceWatcher);

            int screenWidth = config.WindowWidth <= 0 ? 1280 : config.WindowWidth;
            int screenHeight = config.WindowHeight <= 0 ? 720 : config.WindowHeight;
            string title = string.IsNullOrWhiteSpace(config.WindowTitle) ? "Ludots Engine" : config.WindowTitle;
            // targetFps = 0 leaves VSync/FPS uncapped; values below 0 use the host default.
            int targetFps = config.TargetFps == 0 ? 0 : (config.TargetFps < 0 ? 60 : config.TargetFps);
            bool windowOpened = false;
            bool windowResizable = config.WindowResizable || config.WindowStartMaximized;
            RaylibSoundConsumer? soundConsumer = null;

            var terrainRenderer = new RaylibTerrainRenderer
            {
                HeightScale = 2.0f,
                VisibleRadius = 900f,
                SimplifiedCliffRadius = 350f,
            };
            _terrainSource = new VertexMapTerrainChunkMeshSource(engine.VertexMap);
            var continuousHeightmapRenderer = new RaylibContinuousHeightmapRenderer(engine.VFS)
            {
                VisibleRadiusCm = 140_000f,
            };
            var frameLighting = RaylibFrameLighting.LoadFromDefaultPath(dayPhase01: ResolveInitialDayPhase01());
            RaylibRenderEnvironmentConfig renderEnvironmentConfig = RaylibRenderEnvironmentConfig.CreateDefault();

            try
            {
                if (windowResizable)
                {
                    Rl.SetConfigFlags(FlagWindowResizable);
                }

                Rl.InitWindow(screenWidth, screenHeight, title);
                windowOpened = true;
                if (config.WindowStartMaximized)
                {
                    Rl.MaximizeWindow();
                }

                Rl.SetExitKey(0);
                Rl.SetTargetFPS(targetFps);
                IntPtr nativeWindowHandle = Rl.GetWindowHandle();

                soundConsumer = CreateSoundConsumer(engine, soundRequests);

                screenWidth = Math.Max(1, Rl.GetScreenWidth());
                screenHeight = Math.Max(1, Rl.GetScreenHeight());
                config.WindowWidth = screenWidth;
                config.WindowHeight = screenHeight;

                using var overlayCompositor = new RaylibOverlayCompositor(screenWidth, screenHeight);
                using var browserLayerRenderer = new RaylibBrowserLayerRenderer();
                using var environmentRenderer = new RaylibRenderEnvironmentRenderer(renderEnvironmentConfig);
                var windowRepaintGuard = new RaylibWindowRepaintGuard();
                uiRoot.Resize(screenWidth, screenHeight);

                var initialCamera = new Camera3D
                {
                    position = new Vector3(10.0f, 10.0f, 10.0f),
                    target = new Vector3(0.0f, 0.0f, 0.0f),
                    up = new Vector3(0.0f, 1.0f, 0.0f),
                    fovy = 60.0f,
                    projection = CameraProjection.CAMERA_PERSPECTIVE
                };

                var cameraAdapter = new RaylibCameraAdapter(initialCamera);
                var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, presentationTiming);

                var viewController = new RaylibViewController(cameraAdapter);
                engine.SetService(CoreServiceKeys.ViewController, (IViewController)viewController);
                var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);

                var screenProjector = new CoreScreenProjector(ClientLocalSeatAccess.ResolveAuthorityCamera(engine), viewController);
                var screenRayProvider = new CoreScreenRayProvider(ClientLocalSeatAccess.ResolveAuthorityCamera(engine), viewController);
                screenProjector.BindPresenter(cameraPresenter);
                screenRayProvider.BindPresenter(cameraPresenter);
                screenProjector.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);
                screenRayProvider.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);
                engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)screenProjector);
                engine.SetService(
                    CoreServiceKeys.ScreenRayProvider,
                    (IScreenRayProvider)new PresentBindingScreenRayProvider(
                        engine,
                        screenRayProvider,
                        () => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f));
                var cullingFocusOverride = new CameraCullingFocusOverride();
                engine.SetService(CoreServiceKeys.CameraCullingFocusOverride, cullingFocusOverride);

                var presenterInstances = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);
                var cullingSystem = new CameraCullingSystem(
                    engine.World,
                    ClientLocalSeatAccess.ResolveAuthorityCamera(engine),
                    engine.SpatialQueries,
                    viewController,
                    loadedChunks: null,
                    focusOverride: cullingFocusOverride,
                    presenters: presenterInstances,
                    timingDiagnostics: presentationTiming,
                    cullingConfig: config.Presentation.CameraCulling);
                cullingSystem.DisarmPresentBindingCulling();
                engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(cullingSystem);
                engine.SetService(CoreServiceKeys.CameraCullingDebugState, cullingSystem.DebugState);

                var renderCameraDebug = new RenderCameraDebugState();
                engine.SetService(CoreServiceKeys.RenderCameraDebugState, renderCameraDebug);

                engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));

                WorldHudToScreenSystem? hudProjection = null;
                PresentationOverlaySceneBuilder? overlaySceneBuilder = null;
                PresentationOverlayScene? overlayScene = null;
                ScreenOverlayBuffer? screenOverlayBuffer = null;
                if (engine.TryGetService(CoreServiceKeys.PresentationWorldHudBuffer, out WorldHudBatchBuffer worldHud) &&
                    engine.TryGetService(CoreServiceKeys.PresentationScreenHudBuffer, out ScreenHudBatchBuffer screenHud))
                {
                    WorldHudStringTable? worldHudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
                    PresentationTextCatalog? textCatalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog);
                    PresentationTextLocaleSelection? localeSelection = engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection);
                    screenOverlayBuffer = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
                    MinimapScreenMarkerBuffer? minimapScreenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer);
                    CameraCullingDebugState? cullingDebug = engine.GetService(CoreServiceKeys.CameraCullingDebugState);
                    hudProjection = new WorldHudToScreenSystem(engine.World, worldHud, worldHudStrings, screenProjector, viewController, screenHud, presentationTiming, cullingDebug);
                    overlaySceneBuilder = new PresentationOverlaySceneBuilder(screenHud, worldHudStrings, textCatalog, localeSelection, screenOverlayBuffer, minimapScreenMarkers);
                    overlayScene = new PresentationOverlayScene(screenHud.Capacity + ScreenOverlayBuffer.MaxItems + (minimapScreenMarkers?.Capacity ?? 0));
                }

                ValidateRequiredContextBeforeLoop(engine);

                var debugDrawRenderer = new RaylibDebugDrawRenderer { PlaneY = 0.35f };
                GlobalFieldVisualBuffer? globalFieldVisualBuffer = engine.GetService(CoreServiceKeys.GlobalFieldVisualBuffer);
                var fogFieldProjector = new FogGlobalFieldVisualProjector();
                var discreteFieldProjector = new FieldDiscreteVisualProjector(
                    RaylibFieldRenderPresenter.ResolveDiscreteOwnershipColorVector);
                using var fieldRenderPresenter = new RaylibFieldRenderPresenter();
                Ludots.Core.Presentation.Navigation.NavMeshPresentationBuffer navMeshPresentationBuffer =
                    engine.GetService(CoreServiceKeys.NavMeshPresentationBuffer)
                        ?? throw new InvalidOperationException("Raylib host requires the Core NavMeshPresentationBuffer service.");
                using var navMeshPresentationRenderer = new RaylibNavMeshPresentationRenderer(navMeshPresentationBuffer.TileCapacity);
                PresentationMaterialRegistry? materials = engine.GetService(CoreServiceKeys.PresentationMaterialRegistry);
                RaylibPrimitiveRenderMode primitiveMode = ResolvePrimitiveRenderMode();
                using var primitiveRenderer = new RaylibPrimitiveRenderer(primitiveMode, engine.VFS, materials, Ludots.Core.Presentation.Assets.AnimationChannelRegistry.Register);
                primitiveRenderer.BindReceiverMeshProjector(
                    new MapLaneReceiverMeshProjector(engine, continuousHeightmapRenderer, terrainRenderer, primitiveRenderer.StaticMeshReceiverProjector));
                primitiveRenderer.BindInstancedBatchLaneSource(setup.InstancedBatchLaneStore);
                engine.SetService(
                    CoreServiceKeys.BoneTransformProvider,
                    (Core.Presentation.Presenters.IBoneTransformProvider)new RaylibBoneTransformProvider(
                        engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
                            ?? throw new InvalidOperationException("Raylib host requires the Core PresentationSkinnedVisualBatchBuffer service."),
                        engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                            ?? throw new InvalidOperationException("Raylib host requires the Core PresenterDefinitionRegistry service."),
                        engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
                            ?? throw new InvalidOperationException("Raylib host requires the Core PresentationMeshAssetRegistry service."),
                        (meshAssetId, descriptor) => primitiveRenderer.GpuSkinnedModelCache.GetOrLoad(meshAssetId, in descriptor)));
                using var skyEnvironment = new RaylibSkyEnvironment(engine.VFS);
                skyEnvironment.LoadDescriptors(PresentationCatalogMerge.MergeEntries(
                    engine.ConfigCatalog, engine.ConfigPipeline, engine.ConfigConflictReport, RaylibSkyEnvironment.DefaultRelativePath));
                using var waterPass = new RaylibWaterPass(engine.VFS);
                waterPass.LoadDescriptors(PresentationCatalogMerge.MergeEntries(
                    engine.ConfigCatalog, engine.ConfigPipeline, engine.ConfigConflictReport, RaylibWaterPass.DefaultRelativePath));
                continuousHeightmapRenderer.LoadAlbedoDescriptors(PresentationCatalogMerge.MergeEntries(
                    engine.ConfigCatalog, engine.ConfigPipeline, engine.ConfigConflictReport, RaylibContinuousHeightmapRenderer.DefaultAlbedoRelativePath));
                GlobalPresentationEventBuffer? globalPresentationEvents = engine.GetService(CoreServiceKeys.GlobalPresentationEventBuffer);
                skyEnvironment.SetPhaseSourceRequirement(requiredWhenActive: true);
                skyEnvironment.ApplyDayPhase(frameLighting.DayPhase01);
                if (globalPresentationEvents != null)
                {
                    engine.InsertPresentationSystemBefore<GlobalPresentationEventProjectionSystem>(
                        new RaylibSkyDayNightLatchSystem(engine.World, globalPresentationEvents, skyEnvironment));
                }

                RaylibBenchmarkRenderService? benchmarkRenderer = null;
                if (engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry benchmarkMeshes))
                {
                    ScreenHudBatchBuffer benchmarkHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                        ?? throw new InvalidOperationException("Raylib benchmark rendering requires PresentationScreenHudBuffer.");
                    ScreenOverlayBuffer benchmarkOverlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                        ?? throw new InvalidOperationException("Raylib benchmark rendering requires ScreenOverlayBuffer.");
                    benchmarkRenderer = new RaylibBenchmarkRenderService(primitiveRenderer, benchmarkMeshes, benchmarkHud, benchmarkOverlay);
                    engine.SetService(RaylibBenchmarkRendererKey, (IRaylibBenchmarkRenderer)benchmarkRenderer);
                }

                engine.Start();
                if (string.IsNullOrWhiteSpace(config.StartupMapId))
                {
                    throw new InvalidOperationException("Invalid launcher bootstrap: 'StartupMapId' cannot be empty.");
                }
                engine.LoadStartupMap();
                Ludots.Core.Client.PresentBindingPresentation.TryEnsurePresentBindings(
                    engine,
                    screenProjector,
                    screenRayProvider,
                    viewController.Fov,
                    viewController,
                    cullingSystem);

                int lastW = screenWidth;
                int lastH = screenHeight;
                string? screenshotPath = Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_PATH");
                string? diagnosticPath = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_DIAGNOSTIC_PATH");
                string? screenshotTargetPath = string.IsNullOrWhiteSpace(screenshotPath)
                    ? null
                    : Path.GetFullPath(screenshotPath);
                string? screenshotFileName = string.IsNullOrWhiteSpace(screenshotTargetPath)
                    ? null
                    : Path.GetFileName(screenshotTargetPath);
                string? rawScreenshotMilestones = Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_MILESTONES");
                string? rawScreenshotFrame = Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_FRAME");
                string? rawScreenshotFrames = Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_FRAMES");
                bool milestoneScreenshotMode = RaylibPresentationCaptureSequence.ValidateCaptureMode(
                    rawScreenshotMilestones,
                    rawScreenshotFrame,
                    rawScreenshotFrames);

                RaylibPresentationCaptureSequence? milestoneCaptureSequence = null;
                if (milestoneScreenshotMode)
                {
                    if (!engine.TryGetService(
                            CoreServiceKeys.PresentationCaptureMilestoneSource,
                            out IPresentationCaptureMilestoneSource milestoneSource) ||
                        milestoneSource == null)
                    {
                        throw new InvalidOperationException(
                            "Milestone screenshot capture requires the PresentationCaptureMilestoneSource service.");
                    }

                    milestoneCaptureSequence = RaylibPresentationCaptureSequence.Create(
                        milestoneSource,
                        screenshotTargetPath ?? string.Empty,
                        rawScreenshotMilestones!);
                }

                int[] screenshotFrames = milestoneScreenshotMode
                    ? Array.Empty<int>()
                    : ReadEnvFrameList("LUDOTS_TAKE_SCREENSHOT_FRAMES");
                int screenshotSequenceIndex = 0;
                bool screenshotSequenceEnabled = screenshotFrames.Length > 0;
                bool screenshotPending = milestoneCaptureSequence?.HasPending ??
                                         (!string.IsNullOrWhiteSpace(screenshotFileName) &&
                                          (!screenshotSequenceEnabled || screenshotFrames.Length > 0));
                int screenshotFrame = milestoneScreenshotMode
                    ? 0
                    : screenshotSequenceEnabled
                    ? screenshotFrames[0]
                    : int.TryParse(rawScreenshotFrame, out int parsedScreenshotFrame)
                    ? Math.Max(1, parsedScreenshotFrame)
                    : 60;
                int autoExitFrame = int.TryParse(Environment.GetEnvironmentVariable("LUDOTS_AUTO_EXIT_FRAME"), out int parsedAutoExitFrame)
                    ? Math.Max(0, parsedAutoExitFrame)
                    : 0;
                int minRuntimeMsBeforeScreenshot = ReadEnvIntOrDefault("LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT", 0);
                int timingLogIntervalFrames = int.TryParse(Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES"), out int parsedTimingLogIntervalFrames)
                    ? Math.Max(0, parsedTimingLogIntervalFrames)
                    : 0;
                bool timingSystemBreakdownEnabled = timingLogIntervalFrames > 0 ||
                    ReadEnvBoolOrDefault("LUDOTS_RAYLIB_TIMING_SYSTEM_BREAKDOWN", defaultValue: false);
                if (presentationTiming != null)
                {
                    presentationTiming.SystemBreakdownEnabled = timingSystemBreakdownEnabled;
                }
                bool lightweightDiagnosticHudEnabled = ReadEnvBoolOrDefault(
                    "LUDOTS_RAYLIB_LIGHTWEIGHT_DIAGNOSTIC_HUD",
                    defaultValue: false);
                float autoOrbitDegPerSecond = float.TryParse(Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_AUTO_ORBIT_DEG_PER_SEC"), out float parsedAutoOrbitDegPerSecond)
                    ? parsedAutoOrbitDegPerSecond
                    : 0f;
                SyntheticUiPlayback syntheticUiPlayback = ReadSyntheticUiPlayback();
                var presentFrames = new List<ViewportDrawFrame>(4);
                int frameIndex = 0;
                string? navWalkabilityOverlayMapId = null;
                Stopwatch runtimeStopwatch = Stopwatch.StartNew();
                long previousLoopEnd = Stopwatch.GetTimestamp();

                while (true)
                {
                    long windowPollStart = Stopwatch.GetTimestamp();
                    bool shouldClose = Rl.WindowShouldClose();
                    double windowPollMs = ElapsedMs(windowPollStart);
                    if (shouldClose)
                    {
                        break;
                    }

                    try
                    {
                        long wallFrameStart = Stopwatch.GetTimestamp();
                        presentationTiming?.ObserveHostLoopGap((wallFrameStart - previousLoopEnd) * 1000d / Stopwatch.Frequency);
                        presentationTiming?.ObserveWindowPoll(windowPollMs);
                        long preTickStart = wallFrameStart;
                        int w = Math.Max(1, Rl.GetScreenWidth());
                        int h = Math.Max(1, Rl.GetScreenHeight());
                        windowRepaintGuard.ObserveWindowRect(nativeWindowHandle, Rl.GetWindowPosition(), w, h);
                        if (w != lastW || h != lastH)
                        {
                            lastW = w;
                            lastH = h;
                            config.WindowWidth = w;
                            config.WindowHeight = h;
                            overlayCompositor.Resize(w, h);
                            uiRoot.Resize(w, h);
                        }

                        float dt = Rl.GetFrameTime();
                        presentationTiming?.ObserveFrame(dt * 1000d);
                        var renderDebug = ResolveRenderDebugState(engine);
                        bool activeMapRequestsDeepBackground = ActiveMapHasTag(engine, MapTags.RaylibDeepBackground);
                        bool activeMapHidesDebugGuides = ActiveMapHasTag(engine, MapTags.RaylibHideDebugGuides);
                        IBenchmarkSceneController? benchmarkController = engine.GetService(CoreServiceKeys.BenchmarkSceneController);
                        bool cleanPerformanceMode = IsCleanPerformanceScene(benchmarkController);
                        bool hostDiagnosticUiSuppressed = benchmarkController is { IsActive: true, SuppressHostDiagnosticUi: true };
                        bool hostDebugGuidesSuppressed =
                            activeMapHidesDebugGuides ||
                            benchmarkController is { IsActive: true, SuppressHostDebugGuides: true };
                        bool drawTerrain = renderDebug.DrawTerrain && !cleanPerformanceMode;
                        bool drawContinuousHeightmap = renderDebug.DrawTerrain;
                        bool hasContinuousHeightmap = engine.TryGetService(
                            CoreServiceKeys.ContinuousHeightmap,
                            out IContinuousHeightmap? continuousHeightmapForFrame) &&
                            continuousHeightmapForFrame is IContinuousHeightmapRenderSource;
                        TerrainPresentationBindingConfig? terrainPresentation =
                            engine.CurrentMapSession?.MapConfig?.TerrainPresentation;
                        ResolvedTerrainPresentation? resolvedTerrainPresentation =
                            engine.CurrentMapSession?.TerrainPresentation;
                        if (terrainPresentation != null && resolvedTerrainPresentation == null)
                        {
                            throw new InvalidOperationException(
                                "The active map declares terrain presentation, but Core did not resolve its terrain source.");
                        }
                        if (terrainPresentation?.Source == TerrainPresentationSource.ContinuousHeightmap && !hasContinuousHeightmap)
                        {
                            throw new InvalidOperationException(
                                "The active map explicitly requires ContinuousHeightmap terrain presentation, but no renderable continuous heightmap is bound.");
                        }
                        if (resolvedTerrainPresentation?.Source == TerrainPresentationSource.BoardTerrain)
                        {
                            drawContinuousHeightmap = false;
                        }
                        VertexMap? boardTerrain = resolvedTerrainPresentation?.Source == TerrainPresentationSource.BoardTerrain
                            ? resolvedTerrainPresentation.BoardTerrain
                            : engine.VertexMap;
                        bool drawPrimitives = renderDebug.DrawPrimitives;
                        bool drawDebugDraw = renderDebug.DrawDebugDraw && !cleanPerformanceMode;
                        bool drawFieldOverlays = renderDebug.DrawFieldOverlays && !cleanPerformanceMode;
                        bool drawSkiaUi = renderDebug.DrawSkiaUi;

                        bool drawNavMeshOverlay = renderDebug.DrawNavMesh &&
                            navMeshPresentationBuffer.TileCount > 0 &&
                            engine.TryGetService(
                                CoreServiceKeys.NavMeshPresentationState,
                                out Ludots.Core.Presentation.Navigation.NavMeshPresentationState? navMeshFrameState) &&
                            navMeshFrameState is { Enabled: true };

                        double uiInputMs = 0d;
                        bool uiCaptured = false;
                        bool uiWheelCaptured = false;
                        bool uiInputHandled = false;
                        syntheticInput?.AdvanceFrame();
                        deviceWatcher.Poll();
                        if (drawSkiaUi)
                        {
                            long uiInputStart = Stopwatch.GetTimestamp();
                            UiInputFrameResult uiInput = UpdateInput(uiRoot, syntheticUiPlayback, frameIndex, diagnosticPath, syntheticInput);
                            uiCaptured = uiInput.PointerCaptured;
                            uiWheelCaptured = uiInput.WheelCaptured;
                            uiInputHandled = uiInput.Handled;
                            uiInputMs = ElapsedMs(uiInputStart);
                        }

                        presentationTiming?.ObserveUiInput(uiInputMs);
                        engine.SetService(
                            CoreServiceKeys.UiCaptured,
                            ShouldCaptureWorldPointer(
                                uiCaptured,
                                uiWheelCaptured,
                                uiInputHandled));
                        engine.SetService(CoreServiceKeys.UiWheelCaptured, uiWheelCaptured);
                        presentationTiming?.ObserveHostPreTick(ElapsedMs(preTickStart));

                        Ludots.Core.Client.PresentBindingPresentation.TryEnsurePresentBindings(
                            engine,
                            screenProjector,
                            screenRayProvider,
                            viewController.Fov,
                            viewController,
                            cullingSystem);
                        Ludots.Core.Client.PresentBindingPresentation.TryArmPresentBindingCullingPasses(
                            engine,
                            viewController.Fov,
                            viewController,
                            cullingSystem);

                        engine.SetService(CoreServiceKeys.HostFrameIndex, frameIndex);
                        engine.Tick(dt);
                        // Typed instanced batch requests live from tick end until the next tick's
                        // buffer clear; consuming them here hands resident lanes to this frame's draw.
                        // Grounding always samples the Core-owned visual heightmap service; the
                        // adapter never substitutes its own ground height truth.
                        setup.InstancedBatchLaneStore.ApplyRequests(
                            engine.GetService(CoreServiceKeys.InstancedBatchRequestBuffer).GetSpan(),
                            engine.GetService(CoreServiceKeys.InstancedBatchAssetRegistry),
                            engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap? coreContinuousHeightmap)
                                ? coreContinuousHeightmap
                                : null);
                        long postTickStart = Stopwatch.GetTimestamp();
                        if (autoOrbitDegPerSecond != 0f)
                        {
                            var authorityCamera = Ludots.Core.Client.ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine);
                            CameraState cameraState = authorityCamera.State;
                            authorityCamera.ApplyPose(new CameraPoseRequest
                            {
                                Yaw = WorldPlane2D.NormalizeDegreesPositive(cameraState.Yaw + (autoOrbitDegPerSecond * dt))
                            });
                        }

                        if (soundConsumer != null && soundRequests is { Count: > 0 })
                        {
                            soundConsumer.Consume(soundRequests.GetSpan(), cameraAdapter.Camera.position);
                        }

                        float cameraAlpha = presentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
                        presentFrames.Clear();
                        if (!Ludots.Core.Client.PresentBindingPresentation.TryDrivePresentBindings(
                                engine,
                                cameraPresenter,
                                screenProjector,
                                screenRayProvider,
                                cameraAlpha,
                                viewController.Fov,
                                drawBinding: (in Ludots.Core.Client.PresentBindingDrawFrame frame) =>
                                {
                                    presentFrames.Add(
                                        new ViewportDrawFrame(frame, cameraAdapter.Camera, cameraPresenter.SmoothedRenderState));
                                },
                                cameraDebug: renderCameraDebug,
                                hostView: viewController,
                                culling: cullingSystem))
                        {
                            if (engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats) &&
                                seats != null &&
                                seats.Count > 0)
                            {
                                throw new InvalidOperationException(
                                    "ClientLocalSeatRegistry is published but PresentBinding pipeline failed to sync.");
                            }

                            cameraPresenter.Update(ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine), cameraAlpha, renderCameraDebug);
                        }
                        hudProjection?.Update(dt);
                        benchmarkRenderer?.PrepareFrame(
                            presentationTiming,
                            lastW,
                            lastH,
                            suppressControlPanel: hostDiagnosticUiSuppressed);
                        if (globalFieldVisualBuffer != null)
                        {
                            globalFieldVisualBuffer.BeginFrame();
                            if (engine.TryGetService(CoreServiceKeys.VisionFogFieldStore, out FogFieldStore fogFieldsForProjection))
                            {
                                fogFieldProjector.Project(fogFieldsForProjection, globalFieldVisualBuffer);
                            }

                            MapSession? fieldSession = engine.CurrentMapSession;
                            if (fieldSession?.Fields != null)
                            {
                                FieldDiscreteVisualMapMode mapMode = ResolveDiscreteFieldMapMode(fieldSession);
                                discreteFieldProjector.Project(
                                    ResolveFieldScopeKeyId(fieldSession.MapId),
                                    fieldSession.Fields,
                                    fieldSession.RegionGroups,
                                    in mapMode,
                                    globalFieldVisualBuffer);
                            }
                        }

                        if (overlaySceneBuilder != null && overlayScene != null)
                        {
                            long overlayBuildStart = Stopwatch.GetTimestamp();
                            overlaySceneBuilder.Build(overlayScene);
                            presentationTiming?.ObserveScreenOverlayBuild(
                                ElapsedMs(overlayBuildStart),
                                overlayScene.DirtyLaneCount,
                                overlayScene.Count);
                        }
                        else
                        {
                            presentationTiming?.ObserveScreenOverlayBuild(0d, 0, 0);
                        }
                        presentationTiming?.ObserveHostPostTick(ElapsedMs(postTickStart));

                        long beginDrawingStart = Stopwatch.GetTimestamp();
                        Rl.BeginDrawing();
                        presentationTiming?.ObserveBeginDrawing(ElapsedMs(beginDrawingStart));
                        Restore3DDepthState();
                        string? activeMapId = engine.CurrentMapSession?.MapId.Value;
                        skyEnvironment.EnsureActiveForMap(activeMapId);
                        waterPass.EnsureActiveForMap(activeMapId);
                        continuousHeightmapRenderer.EnsureAlbedoActiveForMap(activeMapId);
                        ConfigureNavWalkabilityOverlay(
                            engine.CurrentMapSession,
                            engine.VFS,
                            continuousHeightmapRenderer,
                            renderDebug.DrawNavWalkabilityTexture,
                            ref navWalkabilityOverlayMapId);
                        Color frameClearColor = skyEnvironment.IsActive
                            ? skyEnvironment.ResolveClearColor()
                            : (activeMapRequestsDeepBackground
                                ? new Raylib_cs.Color(6, 10, 16, 255)
                                : new Raylib_cs.Color(0, 0, 0, 255));

                        var activeCamera = cameraAdapter.Camera;
                        CameraRenderState3D activeCameraState = cameraPresenter.SmoothedRenderState;
                        float windowAspect = MathF.Max(0.001f, lastW / (float)Math.Max(1, lastH));

                        if (skyEnvironment.HasDayPhase)
                        {
                            frameLighting.SetDayPhase(skyEnvironment.DayPhase01);
                        }
                        else
                        {
                            frameLighting.Evaluate();
                        }

                        terrainRenderer.ApplyFrameLighting(frameLighting);
                        continuousHeightmapRenderer.ApplyFrameLighting(frameLighting);
                        primitiveRenderer.DrawSurfaceWireBoxes = drawDebugDraw;

                        bool waterOnContinuousHeightmap = waterPass.IsActive &&
                                                      drawTerrain &&
                                                      drawContinuousHeightmap &&
                                                      hasContinuousHeightmap;
                        bool waterOnVertexMap = waterPass.IsActive &&
                                                drawTerrain &&
                                                !waterOnContinuousHeightmap &&
                                                engine.VertexMap != null;
                        bool waterFboEnabled = waterOnContinuousHeightmap || waterOnVertexMap;
                        bool postProcessWorldFrame = !waterFboEnabled;
                        if (postProcessWorldFrame)
                        {
                            environmentRenderer.BeginWorldFrame(lastW, lastH, frameClearColor);
                        }
                        else
                        {
                            Rl.ClearBackground(frameClearColor);
                        }
                        if (waterFboEnabled)
                        {
                            waterPass.EnsureRenderTargets(lastW, lastH);
                            waterPass.Advance(dt);

                            Camera3D reflectionCamera = waterPass.BuildReflectionCamera(in activeCamera);
                            waterPass.BeginReflectionPass(frameClearColor);
                            Restore3DDepthState();
                            BeginCoreMode3D(reflectionCamera, in activeCameraState, windowAspect);
                            Restore3DDepthState();
                            if (skyEnvironment.IsActive)
                            {
                                skyEnvironment.Draw(in reflectionCamera, in activeCameraState);
                                Restore3DDepthState();
                            }

                            if (waterOnContinuousHeightmap &&
                                engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap? vhReflect) &&
                                vhReflect is IContinuousHeightmapRenderSource reflectSource)
                            {
                                continuousHeightmapRenderer.AbsoluteColorSeaLevelCm = waterPass.WaterPlaneY * 100f;
                                continuousHeightmapRenderer.AbsoluteColorPeakSpanCm = reflectSource.RenderProfile.AbsoluteColorPeakSpanCm;
                                continuousHeightmapRenderer.DisplayHeightScale = reflectSource.RenderProfile.DisplayHeightScale;
                                continuousHeightmapRenderer.Render(reflectSource, reflectionCamera);
                            }
                            else
                            {
                                terrainRenderer.RenderTerrainOnly(TerrainSourceFor(boardTerrain), reflectionCamera);
                            }

                            EndCoreMode3D();
                            waterPass.EndPass();

                            waterPass.BeginRefractionPass(frameClearColor);
                            Restore3DDepthState();
                            BeginCoreMode3D(activeCamera, in activeCameraState, windowAspect);
                            Restore3DDepthState();
                            if (skyEnvironment.IsActive)
                            {
                                skyEnvironment.Draw(in activeCamera, in activeCameraState);
                                Restore3DDepthState();
                            }

                            if (waterOnContinuousHeightmap &&
                                engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap? vhRefract) &&
                                vhRefract is IContinuousHeightmapRenderSource refractSource)
                            {
                                continuousHeightmapRenderer.AbsoluteColorSeaLevelCm = waterPass.WaterPlaneY * 100f;
                                continuousHeightmapRenderer.AbsoluteColorPeakSpanCm = refractSource.RenderProfile.AbsoluteColorPeakSpanCm;
                                continuousHeightmapRenderer.DisplayHeightScale = refractSource.RenderProfile.DisplayHeightScale;
                                continuousHeightmapRenderer.Render(refractSource, activeCamera);
                            }
                            else
                            {
                                terrainRenderer.RenderTerrainOnly(TerrainSourceFor(boardTerrain), activeCamera);
                            }

                            EndCoreMode3D();
                            waterPass.EndPass();
                        }

                        long mode3DStart = Stopwatch.GetTimestamp();
                        bool multiViewport = presentFrames.Count > 1;
                        if (presentFrames.Count == 0)
                        {
                            // No-seat fallback frame: one fullscreen viewport on the current adapter camera.
                            presentFrames.Add(new ViewportDrawFrame(default, activeCamera, activeCameraState));
                        }

                        PresentationFrameReceiptBuffer? frameReceipts =
                            engine.GetService(CoreServiceKeys.PresentationFrameReceiptBuffer);
                        if (frameReceipts != null)
                        {
                            IScreenProjector frameProjector =
                                engine.GetService(CoreServiceKeys.ScreenProjector)
                                ?? throw new InvalidOperationException(
                                    "Presentation frame receipts require the platform-neutral screen projector.");
                            if (frameProjector is not IProjectionSnapshotProvider frameProjectionProvider ||
                                !frameProjectionProvider.TryGetProjectionSnapshot(out ProjectionSnapshot frameProjection))
                            {
                                throw new InvalidOperationException(
                                    "Presentation frame receipts require a valid projection snapshot.");
                            }
                            frameReceipts.BeginFrame(
                                in frameProjection,
                                frameProjectionProvider.ProjectionRevision);
                        }

                        for (int viewportIndex = 0; viewportIndex < presentFrames.Count; viewportIndex++)
                        {
                            ViewportDrawFrame viewport = presentFrames[viewportIndex];
                            Camera3D viewportCamera = viewport.Camera;
                            CameraRenderState3D viewportCameraState = viewport.CameraState;
                            float viewportAspect = windowAspect;
                            primitiveRenderer.ApplyFrameLighting(frameLighting, viewportCamera.position);
                            if (multiViewport)
                            {
                                Vector4 viewportRect = viewport.Frame.Binding.NormalizedScreenRect;
                                int scissorX = (int)MathF.Floor(viewportRect.X * lastW);
                                int scissorY = (int)MathF.Floor(viewportRect.Y * lastH);
                                int scissorW = Math.Max(1, (int)MathF.Ceiling(viewportRect.Z * lastW));
                                int scissorH = Math.Max(1, (int)MathF.Ceiling(viewportRect.W * lastH));
                                Rl.BeginScissorMode(scissorX, scissorY, scissorW, scissorH);
                                viewportAspect = MathF.Max(0.001f, scissorW / (float)scissorH);
                            }

                            Restore3DDepthState();
                            BeginCoreMode3D(viewportCamera, in viewportCameraState, viewportAspect);
                            Restore3DDepthState();

                            if (skyEnvironment.IsActive)
                            {
                                skyEnvironment.Draw(in viewportCamera, in viewportCameraState);
                                Restore3DDepthState();
                            }

                            if (drawDebugDraw &&
                                !(drawContinuousHeightmap && hasContinuousHeightmap) &&
                                !hostDebugGuidesSuppressed)
                            {
                                DrawInfiniteGrid(viewportCamera.target, 300, 1.0f, 10);

                                var target = viewportCamera.target;
                                Rl.DrawLine3D(target, target + new Vector3(2.0f, 0, 0), Color.RED);
                                Rl.DrawLine3D(target, target + new Vector3(0, 0, 2.0f), Color.BLUE);
                                Rl.DrawLine3D(target, target + new Vector3(0, 2.0f, 0), Color.GREEN);
                            }

                            if (drawContinuousHeightmap &&
                                engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap? continuousHeightmapForTerrain) &&
                                continuousHeightmapForTerrain is IContinuousHeightmapRenderSource visualTerrainSource)
                            {
                                long terrainStart = Stopwatch.GetTimestamp();
                                // Absolute elevation tint keeps continental land readable even when the
                                // reflective water pass is off; water FBO still overrides sea from the plane.
                                continuousHeightmapRenderer.AbsoluteColorPeakSpanCm =
                                    visualTerrainSource.RenderProfile.AbsoluteColorPeakSpanCm;
                                continuousHeightmapRenderer.DisplayHeightScale =
                                    visualTerrainSource.RenderProfile.DisplayHeightScale;
                                continuousHeightmapRenderer.AbsoluteColorSeaLevelCm = waterOnContinuousHeightmap
                                    ? waterPass.WaterPlaneY * 100f
                                    : visualTerrainSource.RenderProfile.SeaLevelCm;

                                continuousHeightmapRenderer.Render(visualTerrainSource, viewportCamera);

                                if (waterOnContinuousHeightmap)
                                {
                                    terrainRenderer.EnsureWaterShadersReady();
                                    terrainRenderer.BindReflectiveWater(waterPass);
                                    // Half-extent covers the island board (~1.28km); plane follows camera target XZ.
                                    terrainRenderer.DrawReflectiveOceanPlane(
                                        waterPass.WaterPlaneY,
                                        halfExtentMeters: 900f,
                                        in viewportCamera);
                                }
                                else
                                {
                                    terrainRenderer.ClearReflectiveWater();
                                }

                                presentationTiming?.ObserveTerrain(
                                    ElapsedMs(terrainStart),
                                    continuousHeightmapRenderer.ChunkBuildMsLastFrame,
                                    continuousHeightmapRenderer.DrawnChunkCountLastFrame,
                                    continuousHeightmapRenderer.BuiltChunkCountLastFrame);
                            }
                            else if (drawTerrain)
                            {
                                long terrainStart = Stopwatch.GetTimestamp();
                                if (waterOnVertexMap)
                                {
                                    terrainRenderer.BindReflectiveWater(waterPass);
                                }
                                else
                                {
                                    terrainRenderer.ClearReflectiveWater();
                                }

                                terrainRenderer.Render(TerrainSourceFor(boardTerrain), viewportCamera);
                                presentationTiming?.ObserveTerrain(
                                    ElapsedMs(terrainStart),
                                    terrainRenderer.ChunkBuildMsLastFrame,
                                    terrainRenderer.DrawnChunkCountLastFrame,
                                    terrainRenderer.BuiltChunkCountLastFrame);
                            }
                            else
                            {
                                presentationTiming?.ObserveTerrain(0d, 0d, 0, 0);
                            }

                            if (drawNavMeshOverlay)
                            {
                                navMeshPresentationRenderer.Draw(navMeshPresentationBuffer);
                                if (viewportIndex == 0)
                                {
                                    // Window-anchored overlay text is composited once for the whole frame.
                                    screenOverlayBuffer?.AddText(
                                        10,
                                        40,
                                        navMeshPresentationBuffer.FormatMetadataLine(),
                                        14,
                                        new Vector4(1f, 0.92f, 0.5f, 1f));
                                }
                            }

                            if (engine.TryGetService(
                                    CoreServiceKeys.ContinuousHeightmap,
                                    out IContinuousHeightmap? fieldHeightSampleSource))
                            {
                                fieldRenderPresenter.HeightSampleSource = fieldHeightSampleSource;
                                // Match RaylibContinuousHeightmapRenderer mesh Y (heightCm * 0.01f) — that path
                                // does not multiply DisplayHeightScale, so neither does the drape.
                                fieldRenderPresenter.HeightSampleDisplayScale = 1f;
                            }
                            else
                            {
                                fieldRenderPresenter.HeightSampleSource = null;
                                fieldRenderPresenter.HeightSampleDisplayScale = 1f;
                            }

                            if (drawFieldOverlays && globalFieldVisualBuffer != null)
                            {
                                long fieldRenderStart = Stopwatch.GetTimestamp();
                                fieldRenderPresenter.Draw(globalFieldVisualBuffer);
                                presentationTiming?.ObserveGlobalFieldRender(
                                    ElapsedMs(fieldRenderStart),
                                    fieldRenderPresenter.LastFieldTextureCount,
                                    fieldRenderPresenter.LastDirtyUploadCount,
                                    fieldRenderPresenter.LastDirtyUploadArea,
                                    fieldRenderPresenter.LastDrawCount);
                            }
                            else
                            {
                                presentationTiming?.ObserveGlobalFieldRender(0d, 0, 0, 0, 0);
                            }

                            // Benchmark ISM bridge and performer primitive/skinned lanes are independent.
                            // Drawing the benchmark scene must not skip GpuSkinnedInstance / host material / VFX.
                            if (benchmarkRenderer != null)
                            {
                                _ = benchmarkRenderer.Draw(viewportCamera);
                            }

                            if (drawPrimitives &&
                                engine.TryGetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, out PrimitiveDrawBuffer draw) &&
                                engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshes))
                            {
                                if (!_emptyBufferWarned && draw.GetSpan().Length == 0)
                                {
                                    System.Diagnostics.Debug.WriteLine("[RaylibHostLoop] PrimitiveDrawBuffer is empty on first render frame; no Marker3D presenters emitting?");
                                    _emptyBufferWarned = true;
                                }
                                long primitiveStart = Stopwatch.GetTimestamp();
                                PrimitiveDrawBuffer? snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer);
                                SkinnedVisualBatchBuffer? skinnedBatch = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
                                engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap? continuousHeightmap);
                                if (continuousHeightmap != null)
                                {
                                    continuousHeightmapRenderer.BindStampHeightSampleSource(continuousHeightmap);
                                    terrainRenderer.BindStampHeightSampleSource(continuousHeightmap);
                                }

                                primitiveRenderer.Draw(
                                    draw,
                                    viewportCamera,
                                    snapshot,
                                    skinnedBatch,
                                    meshes,
                                    renderDebug.AcceptanceScaleMultiplier,
                                    continuousHeightmap,
                                    frameReceipts,
                                    runtimeStopwatch.Elapsed.TotalSeconds);
                                presentationTiming?.ObservePrimitiveRender(
                                    ElapsedMs(primitiveStart),
                                    primitiveRenderer.LastInstancedInstances,
                                    primitiveRenderer.LastInstancedBatches,
                                    primitiveRenderer.LastInstancedMatrixBuildMs,
                                    primitiveRenderer.LastInstancedMeshDrawMs,
                                    primitiveRenderer.LastInstancedMatrixCacheHits,
                                    primitiveRenderer.LastInstancedMatrixCacheMisses,
                                    primitiveRenderer.LastPersistentSyncMs,
                                    primitiveRenderer.LastPersistentBucketDrawMs,
                                    primitiveRenderer.LastImmediateDrawMs,
                                    primitiveRenderer.LastImmediateSkippedCount,
                                    skinnedBatch?.Count ?? 0,
                                    primitiveRenderer.LastGpuSkinnedInstances,
                                    primitiveRenderer.LastGpuSkinnedBatches,
                                    primitiveRenderer.LastGpuSkinnedMatrixBuildMs,
                                    primitiveRenderer.LastGpuSkinnedMeshDrawMs);
                            }
                            else
                            {
                                presentationTiming?.ObservePrimitiveRender(0d, 0, 0);
                            }

                            // Draw ground overlays (range circles, cones, etc.)
                            if (!cleanPerformanceMode &&
                                engine.TryGetService(CoreServiceKeys.GroundOverlayBuffer, out GroundOverlayBuffer overlays) &&
                                overlays.Count > 0)
                            {
                                long groundOverlayStart = Stopwatch.GetTimestamp();
                                RaylibWorldOverlayRenderer.DrawGroundOverlays(overlays);
                                presentationTiming?.ObserveGroundOverlayRender(ElapsedMs(groundOverlayStart), overlays.Count);
                            }
                            else
                            {
                                presentationTiming?.ObserveGroundOverlayRender(0d, 0);
                            }

                            if (!cleanPerformanceMode &&
                                engine.GlobalContext.TryGetValue(CoreServiceKeys.SplineRibbonBuffer.Name, out var splineObj) &&
                                splineObj is SplineRibbonBuffer splineRibbons && splineRibbons.Count > 0)
                            {
                                long splineRibbonStart = Stopwatch.GetTimestamp();
                                RaylibWorldOverlayRenderer.DrawSplineRibbons(splineRibbons);
                                presentationTiming?.ObserveSplineRibbonRender(ElapsedMs(splineRibbonStart), splineRibbons.Count);
                            }
                            else
                            {
                                presentationTiming?.ObserveSplineRibbonRender(0d, 0);
                            }

                            if (drawDebugDraw &&
                                engine.TryGetService(CoreServiceKeys.DebugDrawCommandBuffer, out DebugDrawCommandBuffer dd))
                            {
                                long debugDrawStart = Stopwatch.GetTimestamp();
                                debugDrawRenderer.Draw(dd);
                                presentationTiming?.ObserveDebugDrawRender(
                                    ElapsedMs(debugDrawStart),
                                    dd.Lines.Count + dd.Circles.Count + dd.Boxes.Count);
                            }
                            else
                            {
                                presentationTiming?.ObserveDebugDrawRender(0d, 0);
                            }

                            EndCoreMode3D();
                            if (multiViewport)
                            {
                                Rl.EndScissorMode();
                            }
                        }

                        presentationTiming?.ObserveMode3D(ElapsedMs(mode3DStart));
                        if (postProcessWorldFrame)
                        {
                            environmentRenderer.EndWorldFrame(runtimeStopwatch.Elapsed.TotalSeconds);
                        }

                        if (drawSkiaUi)
                        {
                            browserLayerRenderer.Render(uiRoot.Scene, lastW, lastH);
                        }

                        long overlayStart = Stopwatch.GetTimestamp();
                        OverlayCompositeResult overlayResult = overlayCompositor.Render(
                            overlayScene,
                            uiRoot,
                            skiaRenderer,
                            drawSkiaUi,
                            hostDiagnosticUiSuppressed);
                        presentationTiming?.ObserveUiRender(overlayResult.UiRenderMs);
                        presentationTiming?.ObserveUiUpload(overlayResult.UploadMs);
                        presentationTiming?.ObserveCompositeSkip(!overlayResult.RefreshComposite);
                        screenOverlayBuffer?.Clear();
                        presentationTiming?.ObserveScreenOverlayDraw(
                            ElapsedMs(overlayStart),
                            overlayResult.PaintMs,
                            overlayResult.CompositeMs,
                            overlayResult.UploadMs,
                            overlayResult.FinalDrawMs,
                            overlayCompositor.OverlayRenderer.RebuiltLaneCountLastFrame,
                            overlayCompositor.OverlayRenderer.CachedTextLayoutCount);
                        if (timingLogIntervalFrames > 0 && frameIndex % timingLogIntervalFrames == 0)
                        {
                            SkiaOverlayRenderer overlaySkiaRenderer = overlayCompositor.OverlayRenderer;
                            AppendRaylibDiagnostic(
                                diagnosticPath,
                                $"overlay-lanes backend=skia underBar={overlaySkiaRenderer.LastUnderUiBarMs:F2} underText={overlaySkiaRenderer.LastUnderUiTextMs:F2} barBuild={overlaySkiaRenderer.LastBarBatchBuildMs:F2} barDraw={overlaySkiaRenderer.LastBarBatchDrawMs:F2} barBuckets={overlaySkiaRenderer.LastBarBatchBucketCount} barCache={overlaySkiaRenderer.LastBarSpriteCacheHits}/{overlaySkiaRenderer.LastBarSpriteCacheMisses}/clear{overlaySkiaRenderer.LastBarSpriteCacheClears}/size{overlaySkiaRenderer.BarSpriteCacheCount} textBuild={overlaySkiaRenderer.LastTextBatchBuildMs:F2} textDraw={overlaySkiaRenderer.LastTextBatchDrawMs:F2} textBuckets={overlaySkiaRenderer.LastTextSpriteBatchBucketCount} markerBuild={overlaySkiaRenderer.LastMinimapMarkerBatchBuildMs:F2} markerDraw={overlaySkiaRenderer.LastMinimapMarkerBatchDrawMs:F2} markerBuckets={overlaySkiaRenderer.LastMinimapMarkerBatchBucketCount}/{overlaySkiaRenderer.LastMinimapMarkerOrientationBatchBucketCount} markerSpriteCache={overlaySkiaRenderer.LastMinimapMarkerSpriteCacheHits}/{overlaySkiaRenderer.LastMinimapMarkerSpriteCacheMisses}/clear{overlaySkiaRenderer.LastMinimapMarkerSpriteCacheClears}/size{overlaySkiaRenderer.MarkerSpriteCacheCount} textSpriteCache={overlaySkiaRenderer.LastTextSpriteCacheHits}/{overlaySkiaRenderer.LastTextSpriteCacheMisses}/clear{overlaySkiaRenderer.LastTextSpriteCacheClears}/size{overlaySkiaRenderer.TextSpriteCacheCount} textLayout={overlaySkiaRenderer.LastTextLayoutCacheHits}/{overlaySkiaRenderer.LastTextLayoutCacheMisses}/clear{overlaySkiaRenderer.LastTextLayoutCacheClears}/size{overlaySkiaRenderer.CachedTextLayoutCount}");
                        }

                        bool drawLightweightDiagnosticHud = lightweightDiagnosticHudEnabled;
                        if (drawLightweightDiagnosticHud)
                        {
                            long nativeDiagnosticStart = Stopwatch.GetTimestamp();
                            DrawLightweightDiagnosticHud(engine, presentationTiming);
                            presentationTiming?.ObserveNativeDiagnosticHud(ElapsedMs(nativeDiagnosticStart));
                        }
                        else
                        {
                            presentationTiming?.ObserveNativeDiagnosticHud(0d);
                        }

                        long endDrawingStart = Stopwatch.GetTimestamp();
                        Rl.EndDrawing();
                        windowRepaintGuard.AfterPresent();
                        if (frameCapture is RaylibFrameCaptureService frameCaptureDriver)
                        {
                            frameCaptureDriver.OnFramePresented();
                        }
                        presentationTiming?.ObserveEndDrawing(ElapsedMs(endDrawingStart));
                        presentationTiming?.ObserveWallFrame(ElapsedMs(wallFrameStart));
                        previousLoopEnd = Stopwatch.GetTimestamp();

                        frameIndex++;
                        if (timingLogIntervalFrames > 0 && frameIndex % timingLogIntervalFrames == 0)
                        {
                            AppendRaylibDiagnostic(diagnosticPath, $"sample frame={frameIndex}");
                            AppendRaylibDiagnostic(diagnosticPath, BuildTimingDiagnostic(engine, presentationTiming, overlayScene));
                        }

                        if (milestoneCaptureSequence != null &&
                            milestoneCaptureSequence.TryPrepareCapture(
                                frameIndex,
                                out RaylibPresentationCaptureRequest milestoneCapture))
                        {
                            string? screenshotDirectory = Path.GetDirectoryName(milestoneCapture.Path);
                            if (!string.IsNullOrWhiteSpace(screenshotDirectory))
                            {
                                Directory.CreateDirectory(screenshotDirectory);
                            }

                            AppendRaylibDiagnostic(
                                diagnosticPath,
                                $"screenshot milestone={milestoneCapture.Milestone} milestoneOrder={milestoneCapture.MilestoneOrder} milestoneRevision={milestoneCapture.MilestoneRevision} frame={milestoneCapture.HostFrame} cameraPos=({activeCamera.position.X:F2},{activeCamera.position.Y:F2},{activeCamera.position.Z:F2}) cameraTarget=({activeCamera.target.X:F2},{activeCamera.target.Y:F2},{activeCamera.target.Z:F2})");
                            AppendRaylibDiagnostic(diagnosticPath, BuildTimingDiagnostic(engine, presentationTiming, overlayScene));
                            AppendRaylibDiagnostic(diagnosticPath, primitiveRenderer.BuildVisualKindDiagnosticSummary());
                            if (engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry milestoneMeshesForDiagnostics))
                            {
                                AppendRaylibDiagnostic(diagnosticPath, primitiveRenderer.BuildPrimitiveLaneDiagnosticSummary(milestoneMeshesForDiagnostics));
                            }
                            AppendPresentationFrameReceiptDiagnostics(engine, diagnosticPath);
                            AppendRaylibDiagnostic(diagnosticPath, BuildInputSelectionDiagnostic(engine));

                            long screenshotStart = Stopwatch.GetTimestamp();
                            TakeScreenshotAtomic(milestoneCapture.Path);
                            string evidencePath = BuildPresentationEvidencePath(milestoneCapture.Path);
                            WritePresentationCaptureEvidenceAtomic(
                                engine,
                                in milestoneCapture,
                                in activeCamera,
                                evidencePath);
                            presentationTiming?.ObserveScreenshot(ElapsedMs(screenshotStart));
                            milestoneCaptureSequence.CompleteCapture(in milestoneCapture);
                            screenshotPending = milestoneCaptureSequence.HasPending;
                            AppendRaylibDiagnostic(
                                diagnosticPath,
                                $"screenshot-complete milestone={milestoneCapture.Milestone} milestoneOrder={milestoneCapture.MilestoneOrder} milestoneRevision={milestoneCapture.MilestoneRevision} frame={milestoneCapture.HostFrame} file={Path.GetFileName(milestoneCapture.Path)} evidence={Path.GetFileName(evidencePath)}");
                            Log.Info(in LogChannels.Engine, $"Captured runtime milestone screenshot: {milestoneCapture.Path}");
                        }
                        else if (milestoneCaptureSequence == null && screenshotPending && frameIndex >= screenshotFrame &&
                            runtimeStopwatch.ElapsedMilliseconds >= minRuntimeMsBeforeScreenshot)
                        {
                            string fullScreenshotPath = screenshotSequenceEnabled
                                ? BuildSequencedScreenshotPath(screenshotTargetPath!, screenshotSequenceIndex, screenshotFrame)
                                : screenshotTargetPath!;

                            AppendRaylibDiagnostic(
                                diagnosticPath,
                                $"screenshot frame={frameIndex} cameraPos=({activeCamera.position.X:F2},{activeCamera.position.Y:F2},{activeCamera.position.Z:F2}) cameraTarget=({activeCamera.target.X:F2},{activeCamera.target.Y:F2},{activeCamera.target.Z:F2})");
                            AppendRaylibDiagnostic(diagnosticPath, BuildTimingDiagnostic(engine, presentationTiming, overlayScene));
                            AppendRaylibDiagnostic(diagnosticPath, primitiveRenderer.BuildVisualKindDiagnosticSummary());
                            if (engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshesForDiagnostics))
                            {
                                AppendRaylibDiagnostic(diagnosticPath, primitiveRenderer.BuildPrimitiveLaneDiagnosticSummary(meshesForDiagnostics));
                            }
                            AppendPresentationFrameReceiptDiagnostics(engine, diagnosticPath);

                            AppendRaylibDiagnostic(diagnosticPath, BuildInputSelectionDiagnostic(engine));

                            long screenshotStart = Stopwatch.GetTimestamp();
                            RaylibFramebufferCapture.WriteFramebufferPng(fullScreenshotPath);
                            ValidateRuntimeScreenshotEvidence(
                                fullScreenshotPath,
                                Math.Max(1, Rl.GetRenderWidth()),
                                Math.Max(1, Rl.GetRenderHeight()));
                            presentationTiming?.ObserveScreenshot(ElapsedMs(screenshotStart));

                            if (screenshotSequenceEnabled)
                            {
                                screenshotSequenceIndex++;
                                screenshotPending = screenshotSequenceIndex < screenshotFrames.Length;
                                if (screenshotPending)
                                {
                                    screenshotFrame = screenshotFrames[screenshotSequenceIndex];
                                }
                            }
                            else
                            {
                                screenshotPending = false;
                            }
                            Log.Info(in LogChannels.Engine, $"Captured runtime screenshot: {fullScreenshotPath}");
                        }

                        if (autoExitFrame > 0 && frameIndex >= autoExitFrame && !screenshotPending)
                        {
                            AppendRaylibDiagnostic(diagnosticPath, $"auto-exit frame={frameIndex}");
                            AppendRaylibDiagnostic(diagnosticPath, BuildTimingDiagnostic(engine, presentationTiming, overlayScene));
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(in LogChannels.Engine, $"Unhandled exception in game loop: {ex}");
                        break;
                    }
                }
            }
            finally
            {
                soundConsumer?.Dispose();
                if (windowOpened) Rl.CloseWindow();
                terrainRenderer.Dispose();
                continuousHeightmapRenderer.Dispose();
                engine.Dispose();
            }
        }

        private static RaylibSoundConsumer? CreateSoundConsumer(GameEngine engine, SoundRequestBuffer? soundRequests)
        {
            if (soundRequests == null)
            {
                return null;
            }

            MeshAssetRegistry? soundAssets = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
            if (soundAssets == null)
            {
                return null;
            }

            var attenuation = new RaylibSoundAttenuationConfig
            {
                ReferenceDistanceMeters = ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SOUND_ATTEN_REF_METERS", 5f),
                MaxDistanceMeters = ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SOUND_ATTEN_MAX_METERS", 45f),
            }.Validate();
            var consumer = new RaylibSoundConsumer(soundAssets, engine.VFS, attenuation);
            consumer.InitializeDevice();
            return consumer;
        }

        private static unsafe void BeginCoreMode3D(in Camera3D camera, in CameraRenderState3D cameraState, float viewportAspect)
        {
            Rl.rlDrawRenderBatchActive();
            Rl.rlMatrixMode((int)RlMatrixMode.RL_PROJECTION);
            Rl.rlPushMatrix();
            Rl.rlLoadIdentity();

            CameraClipPlanes clipPlanes = CameraViewportUtil.ResolveClipPlanes(in cameraState);
            float aspect = MathF.Max(0.001f, viewportAspect);
            if (camera.projection == CameraProjection.CAMERA_ORTHOGRAPHIC)
            {
                double top = camera.fovy / 2.0;
                double right = top * aspect;
                Rl.rlOrtho(-right, right, -top, top, clipPlanes.NearMeters, clipPlanes.FarMeters);
            }
            else
            {
                double top = clipPlanes.NearMeters * Math.Tan(WorldPlane2D.DegToRadValue(camera.fovy) * 0.5);
                double right = top * aspect;
                Rl.rlFrustum(-right, right, -top, top, clipPlanes.NearMeters, clipPlanes.FarMeters);
            }

            Rl.rlMatrixMode((int)RlMatrixMode.RL_MODELVIEW);
            Rl.rlLoadIdentity();
            Matrix4x4 view = Matrix4x4.CreateLookAt(camera.position, camera.target, camera.up);
            RaylibMatrix raylibView = RaylibMatrix.FromSystemNumerics(in view);
            MultMatrix(in raylibView);
            Rl.rlEnableDepthTest();
        }

        private static unsafe void MultMatrix(in RaylibMatrix matrix)
        {
            float* values = stackalloc float[16]
            {
                matrix.m0, matrix.m1, matrix.m2, matrix.m3,
                matrix.m4, matrix.m5, matrix.m6, matrix.m7,
                matrix.m8, matrix.m9, matrix.m10, matrix.m11,
                matrix.m12, matrix.m13, matrix.m14, matrix.m15
            };
            Rl.rlMultMatrixf(values);
        }

        private static void EndCoreMode3D()
        {
            Rl.rlDrawRenderBatchActive();
            Rl.rlMatrixMode((int)RlMatrixMode.RL_PROJECTION);
            Rl.rlPopMatrix();
            Rl.rlMatrixMode((int)RlMatrixMode.RL_MODELVIEW);
            Rl.rlLoadIdentity();
            Rl.rlDisableDepthTest();
        }

        private static void AppendRaylibDiagnostic(string? diagnosticPath, string message)
        {
            if (string.IsNullOrWhiteSpace(diagnosticPath))
            {
                return;
            }

            string fullPath = Path.GetFullPath(diagnosticPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(fullPath, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
        }

        private static void AppendPresentationFrameReceiptDiagnostics(GameEngine engine, string? diagnosticPath)
        {
            PresentationFrameReceiptBuffer receipts = engine.GetService(CoreServiceKeys.PresentationFrameReceiptBuffer)
                ?? throw new InvalidOperationException("Presentation frame receipt diagnostics require the receipt buffer service.");
            IScreenProjector projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("Presentation frame receipt diagnostics require the screen projector service.");
            if (projector is not IProjectionSnapshotProvider projectionProvider)
            {
                throw new InvalidOperationException(
                    $"Screen projector '{projector.GetType().FullName}' does not provide projection snapshots required by presentation receipt diagnostics.");
            }
            if (!projectionProvider.TryGetProjectionSnapshot(out ProjectionSnapshot projection))
            {
                throw new InvalidOperationException("Presentation frame receipt diagnostics could not resolve a valid projection snapshot.");
            }

            PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("Presentation frame receipt diagnostics require the performer definition registry.");
            PresentationOnscreenStateReceipt worldState = receipts.BuildOnscreenStateReceipt(in projection);
            AppendRaylibDiagnostic(
                diagnosticPath,
                $"world-visual-receipt onscreen={worldState.SubmissionCount} stateSha256={worldState.StateSha256}");
            PresentationTemplateReceiptSummary[] summaries = receipts.BuildTemplateSummaries(in projection);
            for (int i = 0; i < summaries.Length; i++)
            {
                ref readonly PresentationTemplateReceiptSummary summary = ref summaries[i];
                string template = definitions.GetName(summary.TemplateId);
                if (string.IsNullOrWhiteSpace(template))
                {
                    throw new InvalidOperationException(
                        $"Presentation receipt references performer template id {summary.TemplateId} without a registered name.");
                }

                PresentationOnscreenStateReceipt templateState = receipts.BuildOnscreenStateReceipt(
                    in projection,
                    summary.TemplateId);
                AppendRaylibDiagnostic(
                    diagnosticPath,
                    $"presentation-receipt template={template} templateId={summary.TemplateId} submitted={summary.SubmittedCount} onscreen={summary.OnscreenCount} minShortEdgePx={summary.MinimumShortEdgePx:F2} minAreaPx2={summary.MinimumAreaPx2:F2} stateSha256={templateState.StateSha256}");
            }
        }

        private static void WritePresentationCaptureEvidenceAtomic(
            GameEngine engine,
            in RaylibPresentationCaptureRequest capture,
            in Camera3D camera,
            string evidencePath)
        {
            PresentationFrameReceiptBuffer receipts = engine.GetService(CoreServiceKeys.PresentationFrameReceiptBuffer)
                ?? throw new InvalidOperationException("Presentation capture evidence requires the receipt buffer service.");
            IScreenProjector projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("Presentation capture evidence requires the screen projector service.");
            if (projector is not IProjectionSnapshotProvider projectionProvider ||
                !projectionProvider.TryGetProjectionSnapshot(out ProjectionSnapshot projection))
            {
                throw new InvalidOperationException("Presentation capture evidence requires a valid projection snapshot.");
            }
            PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("Presentation capture evidence requires the performer definition registry.");
            PresentationOnscreenInstanceReceipt[] receiptsByInstance =
                receipts.BuildOnscreenInstanceReceipts(in projection);
            var instances = new PresentationCaptureInstanceEvidence[receiptsByInstance.Length];
            for (int i = 0; i < receiptsByInstance.Length; i++)
            {
                ref readonly PresentationOnscreenInstanceReceipt receipt = ref receiptsByInstance[i];
                string template = definitions.GetName(receipt.TemplateId);
                if (string.IsNullOrWhiteSpace(template))
                {
                    throw new InvalidOperationException(
                        $"Presentation capture evidence references performer template id {receipt.TemplateId} without a registered name.");
                }

                instances[i] = new PresentationCaptureInstanceEvidence
                {
                    OwnerStableId = receipt.OwnerStableId,
                    VisualStableId = receipt.VisualStableId,
                    TemplateId = receipt.TemplateId,
                    Template = template,
                    WorldXCm = receipt.WorldXCm,
                    WorldYCm = receipt.WorldYCm,
                    ScreenLeftPx = receipt.ScreenLeftPx,
                    ScreenTopPx = receipt.ScreenTopPx,
                    ScreenRightPx = receipt.ScreenRightPx,
                    ScreenBottomPx = receipt.ScreenBottomPx,
                    ShortEdgePx = receipt.ShortEdgePx,
                    AreaPx2 = receipt.AreaPx2,
                };
            }

            var document = new PresentationCaptureEvidenceDocument
            {
                Milestone = capture.Milestone,
                MilestoneOrder = capture.MilestoneOrder,
                MilestoneRevision = capture.MilestoneRevision,
                HostFrame = capture.HostFrame,
                CameraTargetXCm = checked((int)MathF.Round(camera.target.X * 100f)),
                CameraTargetYCm = checked((int)MathF.Round(camera.target.Z * 100f)),
                ViewportWidthPx = checked((int)MathF.Round(projection.Resolution.X)),
                ViewportHeightPx = checked((int)MathF.Round(projection.Resolution.Y)),
                Instances = instances,
            };
            WriteJsonAtomic(evidencePath, document);
        }

        private static bool IsCleanPerformanceScene(IBenchmarkSceneController? benchmarkController)
        {
            return benchmarkController is { IsActive: true, IsCleanPerformanceScene: true };
        }

        private static bool ActiveMapHasTag(GameEngine engine, MapTag tag)
        {
            MapSession? session = engine.CurrentMapSession;
            IReadOnlyList<string>? tags = session?.MapConfig?.Tags;
            if (tags == null || tags.Count == 0)
            {
                return false;
            }

            string required = tag.Name;
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], required, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReadEnvBoolOrDefault(string key, bool defaultValue)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            return raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        /// Instanced DrawMeshInstanced can crash on some software GL stacks when Material.maps is null.
        /// Prefer explicit env; otherwise auto-select Immediate when the host declares software GL.
        private static RaylibPrimitiveRenderMode ResolvePrimitiveRenderMode()
        {
            string? configured = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_PRIMITIVE_RENDER_MODE");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (configured.Equals("immediate", StringComparison.OrdinalIgnoreCase) ||
                    configured.Equals("0", StringComparison.Ordinal))
                {
                    Log.Warn(in LogChannels.Presentation, "Primitive render mode forced Immediate by LUDOTS_RAYLIB_PRIMITIVE_RENDER_MODE.");
                    return RaylibPrimitiveRenderMode.Immediate;
                }

                if (configured.Equals("instanced", StringComparison.OrdinalIgnoreCase) ||
                    configured.Equals("1", StringComparison.Ordinal))
                {
                    return RaylibPrimitiveRenderMode.Instanced;
                }

                throw new InvalidOperationException(
                    "LUDOTS_RAYLIB_PRIMITIVE_RENDER_MODE must be 'immediate' or 'instanced'.");
            }

            if (ReadEnvBoolOrDefault("LUDOTS_RAYLIB_FORCE_IMMEDIATE_PRIMITIVES", defaultValue: false))
            {
                Log.Warn(in LogChannels.Presentation, "Primitive render mode forced Immediate by LUDOTS_RAYLIB_FORCE_IMMEDIATE_PRIMITIVES.");
                return RaylibPrimitiveRenderMode.Immediate;
            }

            string? galliumDriver = Environment.GetEnvironmentVariable("GALLIUM_DRIVER");
            string? glRenderer = TryReadActiveGlRenderer();
            bool softwareGl =
                ReadEnvBoolOrDefault("LIBGL_ALWAYS_SOFTWARE", defaultValue: false) ||
                string.Equals(galliumDriver, "llvmpipe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(galliumDriver, "softpipe", StringComparison.OrdinalIgnoreCase) ||
                ContainsSoftwareGlRendererToken(glRenderer);

            if (softwareGl)
            {
                Log.Warn(
                    in LogChannels.Presentation,
                    $"Primitive render mode auto Immediate (software GL). DrawMeshInstanced is unsafe on this host. renderer='{glRenderer ?? "<unknown>"}'.");
                return RaylibPrimitiveRenderMode.Immediate;
            }

            return RaylibPrimitiveRenderMode.Instanced;
        }

        private static bool ContainsSoftwareGlRendererToken(string? renderer)
        {
            if (string.IsNullOrWhiteSpace(renderer))
            {
                return false;
            }

            return renderer.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase) ||
                   renderer.Contains("softpipe", StringComparison.OrdinalIgnoreCase) ||
                   renderer.Contains("swrast", StringComparison.OrdinalIgnoreCase) ||
                   renderer.Contains("Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryReadActiveGlRenderer()
        {
            const int GlRenderer = 0x1F01;
            try
            {
                string libName = OperatingSystem.IsWindows() ? "opengl32" : "libGL.so.1";
                if (!NativeLibrary.TryLoad(libName, out IntPtr libHandle))
                {
                    return null;
                }

                // Do not NativeLibrary.Free: Raylib already owns the process GL mapping;
                // releasing here can unload shared GL while the GLFW context is live.
                if (!NativeLibrary.TryGetExport(libHandle, "glGetString", out IntPtr glGetStringPtr) ||
                    glGetStringPtr == IntPtr.Zero)
                {
                    return null;
                }

                var glGetString = Marshal.GetDelegateForFunctionPointer<GlGetStringDelegate>(glGetStringPtr);
                IntPtr rendererPtr = glGetString(GlRenderer);
                return rendererPtr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(rendererPtr);
            }
            catch (Exception ex)
            {
                Log.Warn(
                    in LogChannels.Presentation,
                    $"Unable to read GL_RENDERER for software-GL detection: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GlGetStringDelegate(int name);

        private static int[] ReadEnvFrameList(string key)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<int>();
            }

            string[] parts = raw.Split(
                new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var frames = new List<int>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int frame))
                {
                    frames.Add(Math.Max(1, frame));
                }
            }

            return frames.ToArray();
        }

        private static string BuildSequencedScreenshotPath(string targetPath, int sequenceIndex, int frame)
        {
            string directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
            string extension = Path.GetExtension(targetPath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            string fileName = Path.GetFileNameWithoutExtension(targetPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "screenshot";
            }

            string sequencedFileName = $"{fileName}_{sequenceIndex + 1:000}_f{frame:0000}{extension}";
            return string.IsNullOrWhiteSpace(directory)
                ? Path.GetFullPath(sequencedFileName)
                : Path.Combine(directory, sequencedFileName);
        }

        private static string BuildPresentationEvidencePath(string screenshotPath)
        {
            string fullScreenshotPath = Path.GetFullPath(screenshotPath);
            return Path.ChangeExtension(fullScreenshotPath, ".evidence.json");
        }

        private static void WriteJsonAtomic(
            string targetPath,
            PresentationCaptureEvidenceDocument document)
        {
            string fullTargetPath = Path.GetFullPath(targetPath);
            string directory = Path.GetDirectoryName(fullTargetPath)
                ?? throw new InvalidOperationException("Presentation evidence target path has no directory.");
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullTargetPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            try
            {
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                    document,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        WriteIndented = true,
                    });
                File.WriteAllBytes(temporaryPath, payload);
                if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
                {
                    throw new IOException(
                        $"Presentation evidence writer did not produce a non-empty temporary file '{temporaryPath}'.");
                }

                File.Move(temporaryPath, fullTargetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void TakeScreenshotAtomic(string targetPath)
        {
            string fullTargetPath = Path.GetFullPath(targetPath);
            string directory = Path.GetDirectoryName(fullTargetPath)
                ?? throw new InvalidOperationException("Screenshot target path has no directory.");
            Directory.CreateDirectory(directory);
            string extension = Path.GetExtension(fullTargetPath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            string temporaryFileName =
                $".{Path.GetFileNameWithoutExtension(fullTargetPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp{extension}";
            string workingTemporaryPath = Path.Combine(Environment.CurrentDirectory, temporaryFileName);
            string targetTemporaryPath = Path.Combine(directory, temporaryFileName);
            try
            {
                Rl.TakeScreenshot(temporaryFileName);
                if (!File.Exists(workingTemporaryPath) || new FileInfo(workingTemporaryPath).Length == 0)
                {
                    throw new IOException(
                        $"Raylib did not write a non-empty screenshot temporary file '{workingTemporaryPath}'.");
                }

                if (!string.Equals(workingTemporaryPath, targetTemporaryPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(workingTemporaryPath, targetTemporaryPath, overwrite: true);
                    if (!File.Exists(targetTemporaryPath) || new FileInfo(targetTemporaryPath).Length == 0)
                    {
                        throw new IOException(
                            $"Screenshot staging did not produce a non-empty temporary file '{targetTemporaryPath}'.");
                    }
                }

                File.Move(targetTemporaryPath, fullTargetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(workingTemporaryPath))
                {
                    File.Delete(workingTemporaryPath);
                }
                if (!string.Equals(workingTemporaryPath, targetTemporaryPath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(targetTemporaryPath))
                {
                    File.Delete(targetTemporaryPath);
                }
            }
        }

        private static SyntheticUiPlayback ReadSyntheticUiPlayback()
        {
            bool enabled = ReadEnvBoolOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_PLAYBACK", defaultValue: false);
            int startFrame = ReadEnvIntOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_START_FRAME", 180);
            int endFrame = ReadEnvIntOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_END_FRAME", 260);
            if (endFrame <= startFrame)
            {
                endFrame = startFrame + 1;
            }

            return new SyntheticUiPlayback
            {
                Enabled = enabled,
                StartFrame = startFrame,
                EndFrame = endFrame,
                StartX = ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_START_X", 190f),
                StartY = ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_START_Y", 205f),
                EndX = ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_END_X", 310f),
                EndY = ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_END_Y", 270f),
                ScrollFrame = ReadEnvIntOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_SCROLL_FRAME", -1),
                ScrollDeltaY = ReadEnvFloatOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_SCROLL_DELTA_Y", 0f),
                KeyFrame = ReadEnvIntOrDefault("LUDOTS_RAYLIB_SYNTHETIC_UI_KEY_FRAME", -1),
                Key = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_SYNTHETIC_UI_KEY") ?? string.Empty,
                KeyText = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_SYNTHETIC_UI_KEY_TEXT") ?? string.Empty
            };
        }

        private static int ReadEnvIntOrDefault(string key, int defaultValue)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(key), out int value)
                ? value
                : defaultValue;
        }

        internal static float ReadEnvFloatOrDefault(string key, float defaultValue)
        {
            return float.TryParse(Environment.GetEnvironmentVariable(key), out float value)
                ? value
                : defaultValue;
        }

        private static void DrawLightweightDiagnosticHud(GameEngine engine, PresentationTimingDiagnostics? timing)
        {
            if (timing == null)
            {
                return;
            }

            ScreenHudBatchBuffer? screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            WorldHudBatchBuffer? worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            Ludots.Core.Gameplay.GAS.EffectRequestQueue? effectRequests = engine.GetService(CoreServiceKeys.EffectRequestQueue);
            float frameMs = timing.LastWallFrameMs > 0.001f
                ? timing.LastWallFrameMs
                : (timing.WallFrameMs > 0.001f ? timing.WallFrameMs : timing.LastFrameMs);
            float fps = frameMs > 0.001f ? 1000f / frameMs : 0f;
            string line1 = $"FPS {FormatFixed(fps, 4, 0)}  FRAME {FormatFixed(frameMs, 5, 1)}MS  TICK {FormatFixed(timing.LastTotalTickMs, 5, 1)}MS";
            string line2 = $"ISM {FormatFixed(timing.PrimitiveInstancesLastFrame, 6)}  FIELD {FormatFixed(timing.GlobalFieldTexturesLastFrame, 4)}/{FormatFixed(timing.GlobalFieldDirtyUploadsLastFrame, 4)}  3D {FormatFixed(timing.LastMode3DMs, 5, 1)}MS";
            string line3 = $"HUD {FormatFixed(timing.WorldHudProjectedLastFrame, 6)}/{FormatFixed(worldHud?.Count ?? 0, 6)}  BAR {FormatFixed(screenHud?.BarCount ?? 0, 6)}  TEXT {FormatFixed(screenHud?.TextCount ?? 0, 6)}";
            string line4 = $"SKIA {FormatFixed(timing.LastScreenOverlayPaintMs, 5, 1)}MS  EMIT {FormatFixed(timing.LastPresenterEmitMs, 5, 1)}MS  BEHAV {FormatFixed(timing.LastPresenterBehaviorMs, 5, 1)}MS";
            string line5 = $"FXQ {FormatFixed(effectRequests?.Count ?? 0, 6)}  OVF {FormatFixed(effectRequests?.OverflowCount ?? 0, 6)}  AVL {FormatFixed(effectRequests?.AvailableCapacity ?? 0, 6)}";

            const int x = 10;
            const int y = 10;
            const int fontSize = 20;
            const int lineHeight = 25;
            const int panelWidth = 720;
            const int panelHeight = 137;
            var background = new Color(0, 0, 0, 238);
            var border = new Color(80, 255, 150, 255);
            Rl.DrawRectangle(x - 8, y - 8, panelWidth, panelHeight, background);
            Rl.DrawRectangleLines(x - 8, y - 8, panelWidth, panelHeight, border);
            DrawDiagnosticText(line1, x, y, fontSize, new Color(215, 255, 220, 255));
            DrawDiagnosticText(line2, x, y + lineHeight, fontSize, new Color(220, 240, 255, 255));
            DrawDiagnosticText(line3, x, y + lineHeight * 2, fontSize, new Color(255, 245, 185, 255));
            DrawDiagnosticText(line4, x, y + lineHeight * 3, fontSize, new Color(245, 210, 255, 255));
            DrawDiagnosticText(line5, x, y + lineHeight * 4, fontSize, new Color(255, 215, 180, 255));
        }

        private static string FormatFixed(float value, int width, int decimals)
        {
            string text = decimals <= 0 ? value.ToString("F0") : value.ToString($"F{decimals}");
            return text.Length >= width ? text.Substring(text.Length - width, width) : text.PadLeft(width);
        }

        private static string FormatFixed(double value, int width, int decimals)
        {
            string text = decimals <= 0 ? value.ToString("F0") : value.ToString($"F{decimals}");
            return text.Length >= width ? text.Substring(text.Length - width, width) : text.PadLeft(width);
        }

        private static string FormatFixed(int value, int width)
        {
            string text = value.ToString();
            return text.Length >= width ? text.Substring(text.Length - width, width) : text.PadLeft(width);
        }

        private static void DrawDiagnosticText(string text, int x, int y, int fontSize, Color color)
        {
            _ = fontSize;
            DrawBitmapText(text, x + 2, y + 2, 2, new Color(0, 0, 0, 255));
            DrawBitmapText(text, x, y, 2, color);
        }

        private static void DrawBitmapText(string text, int x, int y, int scale, Color color)
        {
            int cursor = x;
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToUpperInvariant(text[i]);
                if (c == ' ')
                {
                    cursor += 4 * scale;
                    continue;
                }

                ulong glyph = GetDiagnosticGlyph(c);
                for (int row = 0; row < 7; row++)
                {
                    int bits = (int)((glyph >> ((6 - row) * 5)) & 0b11111UL);
                    for (int col = 0; col < 5; col++)
                    {
                        if ((bits & (1 << (4 - col))) == 0)
                        {
                            continue;
                        }

                        Rl.DrawRectangle(cursor + col * scale, y + row * scale, scale, scale, color);
                    }
                }

                cursor += 6 * scale;
            }
        }

        private static ulong PackDiagnosticGlyph(int r0, int r1, int r2, int r3, int r4, int r5, int r6)
        {
            return (((ulong)r0 & 0b11111UL) << 30) |
                   (((ulong)r1 & 0b11111UL) << 25) |
                   (((ulong)r2 & 0b11111UL) << 20) |
                   (((ulong)r3 & 0b11111UL) << 15) |
                   (((ulong)r4 & 0b11111UL) << 10) |
                   (((ulong)r5 & 0b11111UL) << 5) |
                   ((ulong)r6 & 0b11111UL);
        }

        private static ulong GetDiagnosticGlyph(char c)
        {
            return c switch
            {
                'A' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
                'B' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110),
                'C' => PackDiagnosticGlyph(0b01111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b01111),
                'D' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110),
                'E' => PackDiagnosticGlyph(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111),
                'F' => PackDiagnosticGlyph(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000),
                'G' => PackDiagnosticGlyph(0b01111, 0b10000, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111),
                'H' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
                'I' => PackDiagnosticGlyph(0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111),
                'K' => PackDiagnosticGlyph(0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001),
                'L' => PackDiagnosticGlyph(0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111),
                'M' => PackDiagnosticGlyph(0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001),
                'N' => PackDiagnosticGlyph(0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001),
                'O' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
                'P' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000),
                'R' => PackDiagnosticGlyph(0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001),
                'S' => PackDiagnosticGlyph(0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110),
                'T' => PackDiagnosticGlyph(0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100),
                'U' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
                'V' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b01010, 0b00100),
                'W' => PackDiagnosticGlyph(0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010),
                'X' => PackDiagnosticGlyph(0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001),
                'Y' => PackDiagnosticGlyph(0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100),
                '0' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110),
                '1' => PackDiagnosticGlyph(0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
                '2' => PackDiagnosticGlyph(0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111),
                '3' => PackDiagnosticGlyph(0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110),
                '4' => PackDiagnosticGlyph(0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010),
                '5' => PackDiagnosticGlyph(0b11111, 0b10000, 0b10000, 0b11110, 0b00001, 0b00001, 0b11110),
                '6' => PackDiagnosticGlyph(0b01110, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110),
                '7' => PackDiagnosticGlyph(0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000),
                '8' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110),
                '9' => PackDiagnosticGlyph(0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b01110),
                '.' => PackDiagnosticGlyph(0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b01100),
                '/' => PackDiagnosticGlyph(0b00001, 0b00010, 0b00010, 0b00100, 0b01000, 0b01000, 0b10000),
                '-' => PackDiagnosticGlyph(0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000),
                ':' => PackDiagnosticGlyph(0b00000, 0b01100, 0b01100, 0b00000, 0b01100, 0b01100, 0b00000),
                '|' => PackDiagnosticGlyph(0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100),
                _ => PackDiagnosticGlyph(0b11111, 0b10001, 0b00001, 0b00110, 0b00100, 0b00000, 0b00100),
            };
        }

        private static void Restore3DDepthState()
        {
            Rl.rlEnableDepthTest();
            Rl.rlEnableDepthMask();
            Rl.rlEnableBackfaceCulling();
        }

        private static string BuildTimingDiagnostic(
            GameEngine engine,
            PresentationTimingDiagnostics? timing,
            PresentationOverlayScene? overlayScene)
        {
            if (timing == null)
            {
                return "timing unavailable";
            }

            WorldHudBatchBuffer? worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            ScreenHudBatchBuffer? screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            PrimitiveDrawBuffer? primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            PresenterEntityRuntime? presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);
            GroundOverlayBuffer? groundOverlay = engine.GetService(CoreServiceKeys.GroundOverlayBuffer);
            SplineRibbonBuffer? splineRibbon = engine.GetService(CoreServiceKeys.SplineRibbonBuffer);
            DebugDrawCommandBuffer? debugDraw = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer);
            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            IBenchmarkSceneController? benchmarkController = engine.GetService(CoreServiceKeys.BenchmarkSceneController);
            bool cleanPerformanceMode = IsCleanPerformanceScene(benchmarkController);
            float frameMs = timing.LastWallFrameMs > 0.001f
                ? timing.LastWallFrameMs
                : (timing.WallFrameMs > 0.001f ? timing.WallFrameMs : timing.LastFrameMs);
            float fps = frameMs > 0.001f ? 1000f / frameMs : 0f;
            int rawDebugDrawCount = debugDraw == null ? 0 : debugDraw.Lines.Count + debugDraw.Circles.Count + debugDraw.Boxes.Count;
            string presenterDefs = presenters?.BuildActiveDefinitionSummary(8) ?? string.Empty;
            return $"timing frame={frameMs:F2}ms fps={fps:F1} cleanPerf={(cleanPerformanceMode ? 1 : 0)} visibleEntities={timing.VisibleEntitiesLastFrame} presenterActive={presenters?.ActiveCount ?? 0} presenterDefs={presenterDefs} primitiveRaw={primitives?.Count ?? 0} primitiveStaticRaw={primitives?.StaticMeshLaneItemCount ?? 0} skinnedRaw={timing.SkinnedRawLastFrame} gpuSkinned={timing.GpuSkinnedInstancesLastFrame}/{timing.GpuSkinnedBatchesLastFrame} gpuSkinBuild={timing.LastGpuSkinnedMatrixBuildMs:F2} gpuSkinDraw={timing.LastGpuSkinnedMeshDrawMs:F2} gap={timing.LastHostLoopGapMs:F2} poll={timing.LastWindowPollMs:F2} pre={timing.LastHostPreTickMs:F2} tick={timing.LastTotalTickMs:F2} post={timing.LastHostPostTickMs:F2} begin={timing.LastBeginDrawingMs:F2} sim={timing.LastSimulationMs:F2} simTop1={timing.LastSimulationTopSystem1Name}:{timing.LastSimulationTopSystem1Ms:F2} simTop2={timing.LastSimulationTopSystem2Name}:{timing.LastSimulationTopSystem2Ms:F2} simTop3={timing.LastSimulationTopSystem3Name}:{timing.LastSimulationTopSystem3Ms:F2} presentation={timing.LastPresentationMs:F2} presTop1={timing.LastPresentationTopSystem1Name}:{timing.LastPresentationTopSystem1Ms:F2} presTop2={timing.LastPresentationTopSystem2Name}:{timing.LastPresentationTopSystem2Ms:F2} presTop3={timing.LastPresentationTopSystem3Name}:{timing.LastPresentationTopSystem3Ms:F2} behavior={timing.LastPresenterBehaviorMs:F2} behaviorBoot={timing.PresenterBootstrapCountLastFrame} behaviorOwner={timing.PresenterOwnerChangesLastFrame} behaviorAttr={timing.PresenterOwnerAttributeChangesLastFrame} behaviorTag={timing.PresenterOwnerTagChangesLastFrame} behaviorTick={timing.PresenterTickDrivenCountLastFrame} animator={timing.LastPresenterAnimatorMs:F2} transformSync={timing.LastPresenterEntityTransformSyncMs:F2} minimapCollect={timing.LastPresenterMinimapMarkerMs:F2} minimapMarkers={timing.PresenterMinimapMarkersLastFrame}/{timing.PresenterMinimapDroppedLastFrame} minimapProject={timing.LastMinimapProjectionMs:F2} minimapScreen={timing.MinimapScreenMarkersLastFrame}/{timing.MinimapScreenMarkersDroppedLastFrame} heightSync={timing.LastTerrainHeightSyncMs:F2} heightSamples={timing.TerrainHeightSamplesLastFrame} requestFlush={timing.LastPresentationRequestFlushMs:F2} spawnBatch={timing.LastRuntimeSpawnBatchPrepareMs:F2}/{timing.LastRuntimeSpawnWorldCreateMs:F2}/{timing.LastRuntimeSpawnFillBatchMs:F2}/{timing.LastRuntimeSpawnPostSpawnMs:F2} spawnPerf={timing.LastRuntimeSpawnPresenterBatchMs:F2}/{timing.LastRuntimeSpawnPresenterCreateMs:F2}/{timing.LastRuntimeSpawnPresenterBootstrapMarkMs:F2} spawnPerfParts={timing.LastRuntimeSpawnPresenterCreateSetupMs:F2}/{timing.LastRuntimeSpawnPresenterWorldCreateMs:F2}/{timing.LastRuntimeSpawnPresenterComponentFillMs:F2}/{timing.LastRuntimeSpawnPresenterIndexWriteMs:F2}/{timing.LastRuntimeSpawnPresenterOwnerPayloadMs:F2}/{timing.LastRuntimeSpawnPresenterPostCreateMs:F2} spawnPerfChildParts={timing.LastRuntimeSpawnPresenterChildSetupMs:F2}/{timing.LastRuntimeSpawnPresenterChildWorldCreateMs:F2}/{timing.LastRuntimeSpawnPresenterChildComponentFillMs:F2}/{timing.LastRuntimeSpawnPresenterChildIndexWriteMs:F2}/{timing.LastRuntimeSpawnPresenterChildStableIdMs:F2} cull={timing.LastCameraCullingMs:F2} cullSpatial={timing.LastCameraCullingSpatialQueryMs:F2} cullStatic={timing.LastCameraCullingStaticProcessMs:F2} cullDyn={timing.LastCameraCullingDynamicProcessMs:F2} cullEntity={timing.LastCameraCullingEntityProcessMs:F2} cullSync={timing.LastCameraCullingPresenterSyncMs:F2} hudProj={timing.LastWorldHudProjectionMs:F2} hudRaw={timing.WorldHudItemsLastProjection} hudProjected={timing.WorldHudProjectedLastFrame} hudDensitySkip={timing.WorldHudDensitySkippedLastFrame} mode3D={timing.LastMode3DMs:F2} terrain={timing.LastTerrainRenderMs:F2} terrainChunks={timing.TerrainChunksDrawnLastFrame}/{timing.TerrainChunksBuiltLastFrame} field={timing.LastGlobalFieldRenderMs:F2} fieldCount={timing.GlobalFieldTexturesLastFrame} fieldDirty={timing.GlobalFieldDirtyUploadsLastFrame} fieldArea={timing.GlobalFieldDirtyUploadAreaLastFrame} fieldDraws={timing.GlobalFieldDrawsLastFrame} primitive={timing.LastPrimitiveRenderMs:F2} primSync={timing.LastPrimitivePersistentSyncMs:F2} primBucket={timing.LastPrimitivePersistentBucketDrawMs:F2} primImmediate={timing.LastPrimitiveImmediateDrawMs:F2} primImmediateSkip={timing.PrimitiveImmediateSkippedLastFrame} primBuild={timing.LastPrimitiveMatrixBuildMs:F2} primDraw={timing.LastPrimitiveMeshDrawMs:F2} primInstances={timing.PrimitiveInstancesLastFrame} primBatches={timing.PrimitiveBatchesLastFrame} primCache={timing.PrimitiveMatrixCacheHitsLastFrame}/{timing.PrimitiveMatrixCacheMissesLastFrame} ground={timing.LastGroundOverlayRenderMs:F2} groundCount={timing.GroundOverlaysLastFrame} groundRaw={groundOverlay?.Count ?? 0} spline={timing.LastSplineRibbonRenderMs:F2} splineCount={timing.SplineRibbonsLastFrame} splineRaw={splineRibbon?.Count ?? 0} debugDraw={timing.LastDebugDrawRenderMs:F2} debugDrawCount={timing.DebugDrawCommandsLastFrame} debugDrawRaw={rawDebugDrawCount} overlay={timing.LastScreenOverlayDrawMs:F2} overlayBuild={timing.LastScreenOverlayBuildMs:F2} overlayDirtyLanes={timing.ScreenOverlayDirtyLanesLastFrame} overlayItems={timing.ScreenOverlayItemsLastFrame} overlayRebuilt={timing.ScreenOverlayRebuiltLanesLastFrame} overlayPaint={timing.LastScreenOverlayPaintMs:F2} overlayComposite={timing.LastScreenOverlayCompositeMs:F2} uiRender={timing.LastUiRenderMs:F2} uiUpload={timing.LastUiUploadMs:F2} overlayFinal={timing.LastScreenOverlayFinalDrawMs:F2} nativeDiag={timing.LastNativeDiagnosticHudMs:F2} emit={timing.LastPresenterEmitMs:F2} emitDirty={timing.LastPresenterEmitDirtyProcessMs:F2} emitDirtyCount={timing.PresenterEmitDirtyCountLastFrame} emitRetained={timing.LastPresenterEmitRetainedProcessMs:F2} emitRetainedCount={timing.PresenterEmitRetainedCountLastFrame} emitRetainedDirectPath={timing.PresenterEmitRetainedDirectHitsLastFrame}/{timing.PresenterEmitRetainedFullPathLastFrame}/{timing.PresenterEmitRetainedDirectMissesLastFrame} endDraw={timing.LastEndDrawingMs:F2} screenshot={timing.LastScreenshotMs:F2} worldHud={worldHud?.Count ?? 0} screenBars={screenHud?.BarCount ?? 0} screenText={screenHud?.TextCount ?? 0} worldHudDrops={worldHud?.DroppedTotal ?? 0} screenHudDrops={screenHud?.DroppedTotal ?? 0} overlaySceneDrops={overlayScene?.DroppedTotal ?? 0}";
        }

        private static void ValidateRequiredContextBeforeLoop(GameEngine engine)
        {
            ValidateKey(engine, CoreServiceKeys.ScreenProjector);
            ValidateKey(engine, CoreServiceKeys.ScreenRayProvider);
            ValidateKey(engine, CoreServiceKeys.RenderDebugState);
        }

        private static void ValidateKey<T>(GameEngine engine, ServiceKey<T> key)
        {
            if (!engine.TryGetService(key, out _))
            {
                throw new InvalidOperationException($"Required service missing or invalid: {key.Name} expected {typeof(T).FullName}");
            }
        }

        private static RenderDebugState ResolveRenderDebugState(GameEngine engine)
        {
            if (engine.TryGetService(CoreServiceKeys.RenderDebugState, out RenderDebugState state))
            {
                return state;
            }

            throw new InvalidOperationException($"Required service missing or invalid: {CoreServiceKeys.RenderDebugState.Name} expected {typeof(RenderDebugState).FullName}");
        }

        internal static void ConfigureNavWalkabilityOverlay(
            MapSession? session,
            IRenderAssetPathResolver assetPaths,
            RaylibContinuousHeightmapRenderer renderer,
            bool enabled,
            ref string? configuredMapId)
        {
            if (!enabled)
            {
                renderer.ClearNavWalkabilityOverlay();
                configuredMapId = null;
                return;
            }

            MapConfig mapConfig = session?.MapConfig
                ?? throw new InvalidOperationException(
                    "DrawNavWalkabilityTexture requires an active map session.");
            if (renderer.NavWalkabilityOverlayActive &&
                string.Equals(configuredMapId, mapConfig.Id, StringComparison.Ordinal))
            {
                return;
            }

            NavWalkabilityOverlayDescriptor descriptor =
                NavWalkabilityOverlayDescriptorResolver.ResolveOrThrow(mapConfig, assetPaths);
            renderer.SetNavWalkabilityOverlay(
                descriptor.TextureUri,
                descriptor.BoundsCm,
                enabled: true);
            configuredMapId = mapConfig.Id;
        }

        private static UiInputFrameResult UpdateInput(UIRoot uiRoot, SyntheticUiPlayback syntheticUiPlayback, int frameIndex, string? diagnosticPath, SyntheticInputDevice? syntheticInput)
        {
            if (syntheticUiPlayback.Enabled &&
                HandleSyntheticUiPlayback(uiRoot, syntheticUiPlayback, frameIndex, diagnosticPath) is { Handled: true } syntheticResult)
            {
                ForwardKeyboardInput(uiRoot, syntheticInput);
                return syntheticResult;
            }

            var mousePos = syntheticInput is { HasPointerOverride: true } ? syntheticInput.PointerPosition : Rl.GetMousePosition();
            bool windowFocused = Rl.IsWindowFocused() || syntheticInput is { HasPointerOverride: true };
            float mouseWheel = Rl.GetMouseWheelMove() + (syntheticInput?.WheelDeltaThisFrame ?? 0f);
            UiNode? hitNode = _uiPointerCaptured ? null : uiRoot.Scene?.HitTest(mousePos.X, mousePos.Y);
            bool hitInteractiveUi = !_uiPointerCaptured && IsInteractiveUiNode(hitNode);
            bool uiWheelCaptured = false;
            bool uiInputHandled = false;

            if (_uiPointerCaptured)
            {
                bool capturedButtonDown = _uiCapturedPointerButton.HasValue &&
                    (Rl.IsMouseButtonDown(ToMouseButton(_uiCapturedPointerButton.Value)) ||
                     (syntheticInput?.IsButtonDown(RaylibInputBackend.ToSyntheticButton(ToMouseButton(_uiCapturedPointerButton.Value))) ?? false));
                bool capturedButtonReleased = _uiCapturedPointerButton.HasValue &&
                    (Rl.IsMouseButtonReleased(ToMouseButton(_uiCapturedPointerButton.Value)) ||
                     (syntheticInput?.WasButtonReleasedThisFrame(RaylibInputBackend.ToSyntheticButton(ToMouseButton(_uiCapturedPointerButton.Value))) ?? false));

                if (!windowFocused || (!_uiCapturedPointerButton.HasValue && !capturedButtonDown && !capturedButtonReleased) || capturedButtonReleased)
                {
                    if (windowFocused && _uiCapturedPointerButton.HasValue && capturedButtonReleased)
                    {
                        uiInputHandled |= uiRoot.HandleInput(new PointerEvent
                        {
                            DeviceType = InputDeviceType.Mouse,
                            PointerId = 0,
                            Action = PointerAction.Up,
                            Button = _uiCapturedPointerButton.Value,
                            X = mousePos.X,
                            Y = mousePos.Y
                        });
                    }

                    _uiPointerCaptured = false;
                    _uiCapturedPointerButton = null;
                    ResetUiPointerMoveCache();
                }
            }

            if ((_uiPointerCaptured || hitInteractiveUi) && ShouldForwardUiPointerMove(mousePos.X, mousePos.Y))
            {
                uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Move,
                    Button = _uiCapturedPointerButton,
                    X = mousePos.X,
                    Y = mousePos.Y
                });
            }

            if ((_uiPointerCaptured || hitInteractiveUi) && Math.Abs(mouseWheel) > float.Epsilon)
            {
                uiWheelCaptured = uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Scroll,
                    X = mousePos.X,
                    Y = mousePos.Y,
                    DeltaX = 0f,
                    DeltaY = -mouseWheel * 120f
                });
            }

            bool shouldRouteMouseDownToUi = hitInteractiveUi || uiRoot.HasFocusedCanvas || _uiPointerCaptured;
            foreach (MouseButton mouseButton in MouseButtonsInPriorityOrder)
            {
                bool syntheticPressed = syntheticInput?.WasButtonPressedThisFrame(RaylibInputBackend.ToSyntheticButton(mouseButton)) ?? false;
                if (!Rl.IsMouseButtonPressed(mouseButton) && !syntheticPressed)
                {
                    continue;
                }

                PointerButton pointerButton = ToPointerButton(mouseButton);
                if (!shouldRouteMouseDownToUi)
                {
                    continue;
                }

                bool handled = uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Down,
                    Button = pointerButton,
                    X = mousePos.X,
                    Y = mousePos.Y
                });

                uiInputHandled |= handled;
                if (handled)
                {
                    _uiPointerCaptured = true;
                    _uiCapturedPointerButton = pointerButton;
                    ResetUiPointerMoveCache();
                }
            }

            // Same-frame synthetic releases (e.g. Click) arrive after the capture
            // check above; without this the UI capture would latch forever.
            if (_uiPointerCaptured && _uiCapturedPointerButton.HasValue &&
                (syntheticInput?.WasButtonReleasedThisFrame(RaylibInputBackend.ToSyntheticButton(ToMouseButton(_uiCapturedPointerButton.Value))) ?? false))
            {
                uiInputHandled |= uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Up,
                    Button = _uiCapturedPointerButton.Value,
                    X = mousePos.X,
                    Y = mousePos.Y
                });
                _uiPointerCaptured = false;
                _uiCapturedPointerButton = null;
                ResetUiPointerMoveCache();
            }

            ForwardKeyboardInput(uiRoot, syntheticInput);
            return new UiInputFrameResult(Handled: uiInputHandled, PointerCaptured: _uiPointerCaptured, WheelCaptured: uiWheelCaptured);
        }

        private static bool ShouldForwardUiPointerMove(float x, float y)
        {
            if (!_hasLastUiPointerMove ||
                Math.Abs(_lastUiPointerMoveX - x) > 0.01f ||
                Math.Abs(_lastUiPointerMoveY - y) > 0.01f)
            {
                _hasLastUiPointerMove = true;
                _lastUiPointerMoveX = x;
                _lastUiPointerMoveY = y;
                return true;
            }

            return false;
        }

        private static void ResetUiPointerMoveCache()
        {
            _hasLastUiPointerMove = false;
            _lastUiPointerMoveX = 0f;
            _lastUiPointerMoveY = 0f;
        }

        private static MouseButton ToMouseButton(PointerButton button)
        {
            return button switch
            {
                PointerButton.Left => MouseButton.MOUSE_LEFT_BUTTON,
                PointerButton.Middle => MouseButton.MOUSE_MIDDLE_BUTTON,
                PointerButton.Right => MouseButton.MOUSE_RIGHT_BUTTON,
                _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported pointer button.")
            };
        }

        private static PointerButton ToPointerButton(MouseButton button)
        {
            return button switch
            {
                MouseButton.MOUSE_LEFT_BUTTON => PointerButton.Left,
                MouseButton.MOUSE_MIDDLE_BUTTON => PointerButton.Middle,
                MouseButton.MOUSE_RIGHT_BUTTON => PointerButton.Right,
                _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
            };
        }

        private static void ForwardKeyboardInput(UIRoot uiRoot, SyntheticInputDevice? syntheticInput = null)
        {
            if (!uiRoot.HasFocusedCanvas)
            {
                DrainCharQueue();
                return;
            }

            int modifiers = ReadBrowserInputModifiers();
            foreach (KeyboardKey key in BrowserForwardedKeys)
            {
                if (Rl.IsKeyPressed(key))
                {
                    uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Down,
                        Key = MapKeyboardKey(key),
                        Code = key.ToString(),
                        Modifiers = modifiers
                    });
                }

                if (Rl.IsKeyReleased(key))
                {
                    uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Up,
                        Key = MapKeyboardKey(key),
                        Code = key.ToString(),
                        Modifiers = modifiers
                    });
                }
            }

            if (syntheticInput != null)
            {
                foreach (string key in syntheticInput.KeysDownSnapshotPressedThisFrame())
                {
                    uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Down,
                        Key = key,
                        Code = key,
                        Modifiers = modifiers
                    });
                }

                foreach (string key in syntheticInput.KeysReleasedThisFrameSnapshot())
                {
                    uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Up,
                        Key = key,
                        Code = key,
                        Modifiers = modifiers
                    });
                }
            }

            while (true)
            {
                int codePoint = Rl.GetCharPressed();
                if (codePoint == 0)
                {
                    break;
                }

                string text = char.ConvertFromUtf32(codePoint);
                uiRoot.HandleInput(new KeyboardEvent
                {
                    DeviceType = InputDeviceType.Keyboard,
                    Action = KeyboardAction.Character,
                    Key = text,
                    Text = text,
                    Modifiers = modifiers
                });
            }

            if (syntheticInput != null)
            {
                foreach (char c in syntheticInput.CharsThisFrame)
                {
                    string text = c.ToString();
                    uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Character,
                        Key = text,
                        Text = text,
                        Modifiers = modifiers
                    });
                }
            }
        }

        private static void DrainCharQueue()
        {
            while (Rl.GetCharPressed() != 0)
            {
            }
        }

        private static int ReadBrowserInputModifiers()
        {
            BrowserInputModifiers modifiers = BrowserInputModifiers.None;
            if (Rl.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) || Rl.IsKeyDown(KeyboardKey.KEY_RIGHT_SHIFT))
            {
                modifiers |= BrowserInputModifiers.Shift;
            }

            if (Rl.IsKeyDown(KeyboardKey.KEY_LEFT_CONTROL) || Rl.IsKeyDown(KeyboardKey.KEY_RIGHT_CONTROL))
            {
                modifiers |= BrowserInputModifiers.Control;
            }

            if (Rl.IsKeyDown(KeyboardKey.KEY_LEFT_ALT) || Rl.IsKeyDown(KeyboardKey.KEY_RIGHT_ALT))
            {
                modifiers |= BrowserInputModifiers.Alt;
            }

            if (Rl.IsKeyDown(KeyboardKey.KEY_LEFT_SUPER) || Rl.IsKeyDown(KeyboardKey.KEY_RIGHT_SUPER))
            {
                modifiers |= BrowserInputModifiers.Meta;
            }

            return (int)modifiers;
        }

        private static string MapKeyboardKey(KeyboardKey key)
        {
            return key switch
            {
                KeyboardKey.KEY_ENTER => "Enter",
                KeyboardKey.KEY_TAB => "Tab",
                KeyboardKey.KEY_BACKSPACE => "Backspace",
                KeyboardKey.KEY_DELETE => "Delete",
                KeyboardKey.KEY_ESCAPE => "Escape",
                KeyboardKey.KEY_LEFT => "ArrowLeft",
                KeyboardKey.KEY_RIGHT => "ArrowRight",
                KeyboardKey.KEY_UP => "ArrowUp",
                KeyboardKey.KEY_DOWN => "ArrowDown",
                KeyboardKey.KEY_HOME => "Home",
                KeyboardKey.KEY_END => "End",
                KeyboardKey.KEY_PAGE_UP => "PageUp",
                KeyboardKey.KEY_PAGE_DOWN => "PageDown",
                KeyboardKey.KEY_SPACE => "Space",
                KeyboardKey.KEY_ZERO => "0",
                KeyboardKey.KEY_ONE => "1",
                KeyboardKey.KEY_TWO => "2",
                KeyboardKey.KEY_THREE => "3",
                KeyboardKey.KEY_FOUR => "4",
                KeyboardKey.KEY_FIVE => "5",
                KeyboardKey.KEY_SIX => "6",
                KeyboardKey.KEY_SEVEN => "7",
                KeyboardKey.KEY_EIGHT => "8",
                KeyboardKey.KEY_NINE => "9",
                >= KeyboardKey.KEY_A and <= KeyboardKey.KEY_Z => key.ToString()[4..],
                _ => key.ToString()
            };
        }

        private static UiInputFrameResult HandleSyntheticUiPlayback(UIRoot uiRoot, SyntheticUiPlayback playback, int frameIndex, string? diagnosticPath)
        {
            if (frameIndex < playback.StartFrame)
            {
                return default;
            }

            if (frameIndex == playback.StartFrame)
            {
                uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Move,
                    X = playback.StartX,
                    Y = playback.StartY
                });
                _uiPointerCaptured = uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Down,
                    Button = PointerButton.Left,
                    X = playback.StartX,
                    Y = playback.StartY
                });
                AppendRaylibDiagnostic(diagnosticPath, $"synthetic-ui down frame={frameIndex} x={playback.StartX:F1} y={playback.StartY:F1} captured={_uiPointerCaptured}");
                return new UiInputFrameResult(Handled: true, PointerCaptured: _uiPointerCaptured, WheelCaptured: false);
            }

            if (frameIndex > playback.StartFrame && frameIndex < playback.EndFrame)
            {
                (float x, float y) = InterpolateSyntheticPointer(playback, frameIndex);
                uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Move,
                    Button = PointerButton.Left,
                    X = x,
                    Y = y
                });

                bool uiWheelCaptured = false;
                if (playback.ScrollFrame == frameIndex && Math.Abs(playback.ScrollDeltaY) > float.Epsilon)
                {
                    uiWheelCaptured = uiRoot.HandleInput(new PointerEvent
                    {
                        DeviceType = InputDeviceType.Mouse,
                        PointerId = 0,
                        Action = PointerAction.Scroll,
                        X = x,
                        Y = y,
                        DeltaX = 0f,
                        DeltaY = playback.ScrollDeltaY
                    });
                    AppendRaylibDiagnostic(diagnosticPath, $"synthetic-ui scroll frame={frameIndex} x={x:F1} y={y:F1} deltaY={playback.ScrollDeltaY:F1}");
                }

                if (playback.KeyFrame == frameIndex)
                {
                    if (!string.IsNullOrWhiteSpace(playback.Key))
                    {
                        RaylibSyntheticKeyboardInput.SendKeyStroke(uiRoot, playback.Key);
                    }

                    if (!string.IsNullOrEmpty(playback.KeyText))
                    {
                        RaylibSyntheticKeyboardInput.SendTextInput(uiRoot, playback.KeyText);
                    }

                    AppendRaylibDiagnostic(diagnosticPath, $"synthetic-ui key frame={frameIndex} key={playback.Key} textLength={playback.KeyText.Length}");
                }

                return new UiInputFrameResult(Handled: true, PointerCaptured: _uiPointerCaptured, WheelCaptured: uiWheelCaptured);
            }

            if (frameIndex == playback.EndFrame)
            {
                uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Move,
                    Button = PointerButton.Left,
                    X = playback.EndX,
                    Y = playback.EndY
                });
                uiRoot.HandleInput(new PointerEvent
                {
                    DeviceType = InputDeviceType.Mouse,
                    PointerId = 0,
                    Action = PointerAction.Up,
                    Button = PointerButton.Left,
                    X = playback.EndX,
                    Y = playback.EndY
                });
                AppendRaylibDiagnostic(diagnosticPath, $"synthetic-ui up frame={frameIndex} x={playback.EndX:F1} y={playback.EndY:F1}");
                _uiPointerCaptured = false;
                return new UiInputFrameResult(Handled: true, PointerCaptured: false, WheelCaptured: false);
            }

            return default;
        }

        private static (float X, float Y) InterpolateSyntheticPointer(SyntheticUiPlayback playback, int frameIndex)
        {
            int moveFrames = Math.Max(1, playback.EndFrame - playback.StartFrame);
            float progress = Math.Clamp((frameIndex - playback.StartFrame) / (float)moveFrames, 0f, 1f);
            float x = playback.StartX + ((playback.EndX - playback.StartX) * progress);
            float y = playback.StartY + ((playback.EndY - playback.StartY) * progress);
            return (x, y);
        }

        private static bool IsInteractiveUiNode(UiNode? node)
        {
            for (UiNode? current = node; current != null; current = current.Parent)
            {
                if (current.ActionHandles.Count > 0)
                {
                    return true;
                }

                if (current.CanvasContent is Ludots.UI.Runtime.IUiCanvasInputSink)
                {
                    return true;
                }

                if (current.Style.Overflow == UiOverflow.Scroll)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildInputSelectionDiagnostic(GameEngine engine)
        {
            string pointerSummary = "pointer=(n/a)";
            string liveSelectSummary = "liveSelect=missing";
            string liveCommandSummary = "liveCommand=missing";
            if (engine.TryGetService(CoreServiceKeys.InputHandler, out PlayerInputHandler input))
            {
                Vector2 pointer = input.ReadAction<Vector2>("PointerPos");
                pointerSummary = $"pointer=({pointer.X:0.##},{pointer.Y:0.##})";
                liveSelectSummary = BuildActionStateSummary(input, "Select", "liveSelect");
                liveCommandSummary = BuildActionStateSummary(input, "Command", "liveCommand");
            }

            string authSelectSummary = "authSelect=missing";
            string authCommandSummary = "authCommand=missing";
            string authPointerSummary = "authPointer=(n/a)";
            if (engine.TryGetService(CoreServiceKeys.AuthoritativeInput, out IInputActionReader authoritativeInput))
            {
                Vector2 authoritativePointer = authoritativeInput.ReadAction<Vector2>("PointerPos");
                authPointerSummary = $"authPointer=({authoritativePointer.X:0.##},{authoritativePointer.Y:0.##})";
                authSelectSummary = BuildActionStateSummary(authoritativeInput, "Select", "authSelect");
                authCommandSummary = BuildActionStateSummary(authoritativeInput, "Command", "authCommand");
            }

            string hoveredSummary = "hovered=(none)";
            if (TryGetLocalEntityCollectionStore(engine, out Entity debugOwner, out EntityCollectionStore debugCollections) &&
                EntityCollectionContextRuntime.TryGetHovered(engine.World, debugCollections, debugOwner, out Entity hovered) &&
                hovered != Entity.Null)
            {
                hoveredSummary = $"hovered={DescribeEntity(engine, hovered)}";
            }

            string selectedSummary = "commandSource=(none)";
            if (TryGetLocalEntityCollectionStore(engine, out debugOwner, out debugCollections) &&
                EntityCollectionContextRuntime.TryGetPrimary(
                    engine.World,
                    debugCollections,
                    debugOwner,
                    EntityCollectionKeys.CommandSource,
                    out Entity commandSource) &&
                commandSource != Entity.Null)
            {
                selectedSummary = $"commandSource={DescribeEntity(engine, commandSource)}";
            }

            bool uiCaptured = engine.TryGetService(CoreServiceKeys.UiCaptured, out bool captured) &&
                captured;

            string dragSummary = "drag=inactive";
            if (ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity localPlayer) &&
                engine.World.IsAlive(localPlayer) &&
                engine.World.Has<CommandSourceDragState>(localPlayer))
            {
                ref CommandSourceDragState drag = ref engine.World.Get<CommandSourceDragState>(localPlayer);
                dragSummary = drag.Active
                    ? $"drag=active({drag.StartScreen.X:0.##},{drag.StartScreen.Y:0.##})->({drag.CurrentScreen.X:0.##},{drag.CurrentScreen.Y:0.##})"
                    : "drag=idle";
            }

            string targetSummary = BuildSelectionTargetSummary(engine);
            return $"windowFocused={Rl.IsWindowFocused()} {pointerSummary} {authPointerSummary} {liveSelectSummary} {authSelectSummary} {liveCommandSummary} {authCommandSummary} {hoveredSummary} {selectedSummary} uiCaptured={uiCaptured} {dragSummary} {targetSummary}";
        }

        private static bool TryGetLocalEntityCollectionStore(
            GameEngine engine,
            out Entity owner,
            out EntityCollectionStore collections)
        {
            collections = default!;
            return ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out owner) &&
                   engine.World.IsAlive(owner) &&
                   engine.TryGetService(CoreServiceKeys.EntityCollectionStore, out collections) &&
                   collections != null;
        }

        private static string BuildActionStateSummary(IInputActionReader input, string actionId, string label)
        {
            return $"{label}[down={input.IsDown(actionId)},pressed={input.PressedThisFrame(actionId)},released={input.ReleasedThisFrame(actionId)}]";
        }

        private static string DescribeEntity(GameEngine engine, Entity entity)
        {
            if (!engine.World.IsAlive(entity))
            {
                return $"Entity#{entity.Id}(dead)";
            }

            if (engine.World.TryGet(entity, out Name name) && !string.IsNullOrWhiteSpace(name.Value))
            {
                return $"{name.Value}#{entity.Id}";
            }

            return $"Entity#{entity.Id}";
        }

        private static string BuildSelectionTargetSummary(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
            {
                return "targets=(projector-missing)";
            }

            string[] names =
            {
                "Blue City",
                "Blue Barracks",
                "Blue Stable",
                "Blue Workshop",
                "Worker A",
                "Soldier A",
                "Catapult A"
            };

            var parts = new System.Collections.Generic.List<string>(names.Length);
            int count = 0;
            for (int i = 0; i < names.Length; i++)
            {
                if (TryFindEntityByName(engine, names[i], out Entity entity))
                {
                    Vector2 screen;
                    string source;
                    if (engine.World.TryGet(entity, out VisualTransform transform))
                    {
                        screen = projector.WorldToScreen(transform.Position);
                        source = "vt";
                    }
                    else if (engine.World.TryGet(entity, out WorldPositionCm position))
                    {
                        screen = projector.WorldToScreen(WorldUnitsFix64.WorldCmToVisualMeters(position.Value, yMeters: 0f));
                        source = "world";
                    }
                    else
                    {
                        continue;
                    }

                    parts.Add($"{names[i]}@({screen.X:0},{screen.Y:0})[{source}]");
                    count++;
                }
            }

            return count == 0
                ? "targets=(none)"
                : $"targets={string.Join("; ", parts)}";
        }

        private static bool TryFindEntityByName(GameEngine engine, string entityName, out Entity entity)
        {
            Entity found = Entity.Null;
            var query = new Arch.Core.QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (Entity candidate, ref Name name) =>
            {
                if (found == Entity.Null &&
                    string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    found = candidate;
                }
            });

            entity = found;
            return found != Entity.Null;
        }

        private static float ResolveInitialDayPhase01()
        {
            string? raw = Environment.GetEnvironmentVariable("LUDOTS_DAY_PHASE");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return 0.42f;
            }

            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float phase) ||
                float.IsNaN(phase) ||
                float.IsInfinity(phase))
            {
                throw new InvalidOperationException(
                    $"LUDOTS_DAY_PHASE must be a finite float in [0,1] (got '{raw}').");
            }

            return phase - MathF.Floor(phase);
        }

        internal static void ValidateRuntimeScreenshotEvidence(string screenshotPath, int expectedWidth, int expectedHeight)
        {
            if (string.IsNullOrWhiteSpace(screenshotPath))
            {
                throw new ArgumentException("Raylib screenshot evidence path cannot be null or whitespace.", nameof(screenshotPath));
            }

            if (expectedWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedWidth));
            }

            if (expectedHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedHeight));
            }

            string fullPath = Path.GetFullPath(screenshotPath);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"Raylib screenshot evidence was not written: {fullPath}");
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length < 24)
            {
                throw new InvalidOperationException($"Raylib screenshot evidence is too small to be a valid PNG: {fullPath} length={fileInfo.Length}.");
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Raylib screenshot evidence must be a PNG so dimensions can be verified: {fullPath}");
            }

            using var bitmap = SKBitmap.Decode(fullPath);
            if (bitmap == null)
            {
                throw new InvalidOperationException($"Raylib screenshot evidence is not a decodable PNG image: {fullPath}");
            }

            int actualWidth = bitmap.Width;
            int actualHeight = bitmap.Height;
            if (actualWidth != expectedWidth || actualHeight != expectedHeight)
            {
                throw new InvalidOperationException(
                    $"Raylib screenshot evidence dimensions mismatch: {fullPath} actual={actualWidth}x{actualHeight} expected={expectedWidth}x{expectedHeight}.");
            }

            if (IsVisuallyFlat(bitmap))
            {
                throw new InvalidOperationException($"Raylib screenshot evidence is visually flat and cannot prove a rendered scene: {fullPath}");
            }
        }

        private static bool IsVisuallyFlat(SKBitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            if (width <= 0 || height <= 0)
            {
                return true;
            }

            SKColor first = bitmap.GetPixel(0, 0);
            int stepX = Math.Max(1, width / 16);
            int stepY = Math.Max(1, height / 16);
            for (int y = 0; y < height; y += stepY)
            {
                for (int x = 0; x < width; x += stepX)
                {
                    if (ColorDistance(bitmap.GetPixel(x, y), first) > 6)
                    {
                        return false;
                    }
                }
            }

            return ColorDistance(bitmap.GetPixel(width - 1, height - 1), first) <= 6;
        }

        private static int ColorDistance(SKColor a, SKColor b)
        {
            return Math.Abs(a.Red - b.Red) +
                Math.Abs(a.Green - b.Green) +
                Math.Abs(a.Blue - b.Blue) +
                Math.Abs(a.Alpha - b.Alpha);
        }

        private static FieldDiscreteVisualMapMode ResolveDiscreteFieldMapMode(MapSession session)
        {
            if (session.Variables == null || !session.Variables.Contains("mapmode"))
            {
                return FieldDiscreteVisualMapMode.Leaf;
            }

            int mapmode = session.Variables.ReadInt("mapmode");
            return mapmode switch
            {
                0 => FieldDiscreteVisualMapMode.Leaf,
                > 0 => FieldDiscreteVisualMapMode.AncestorDepth(mapmode),
                _ => throw new InvalidOperationException(
                    $"Map '{session.MapId.Value}' has invalid negative mapmode {mapmode}."),
            };
        }

        private static int ResolveFieldScopeKeyId(MapId mapId)
        {
            uint hash = 2166136261u;
            string value = mapId.Value;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }

            int resolved = (int)(hash & 0x7FFFFFFF);
            return resolved == 0 ? 1 : resolved;
        }

        private static double ElapsedMs(long startTicks)
        {
            return (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
        }

        private static void DrawInfiniteGrid(Vector3 anchor, int halfCount, float spacing, int majorEvery)
        {
            float y = -0.05f;
            float extent = halfCount * spacing;

            float minX = anchor.X - extent;
            float minZ = anchor.Z - extent;

            float startX = MathF.Floor(minX / spacing) * spacing;
            float startZ = MathF.Floor(minZ / spacing) * spacing;

            float endX = startX + 2f * extent;
            float endZ = startZ + 2f * extent;

            var minor = new Color(80, 80, 80, 255);
            var major = new Color(130, 130, 130, 255);

            int lineCount = halfCount * 2;
            for (int i = 0; i <= lineCount; i++)
            {
                float x = startX + i * spacing;
                float z = startZ + i * spacing;

                int xi = (int)MathF.Round(x / spacing);
                int zi = (int)MathF.Round(z / spacing);

                var xCol = majorEvery > 0 && (xi % majorEvery) == 0 ? major : minor;
                var zCol = majorEvery > 0 && (zi % majorEvery) == 0 ? major : minor;

                Rl.DrawLine3D(new Vector3(x, y, startZ), new Vector3(x, y, endZ), xCol);
                Rl.DrawLine3D(new Vector3(startX, y, z), new Vector3(endX, y, z), zCol);
            }
        }


    private static Ludots.Platform.Abstractions.ITerrainChunkMeshSource TerrainSourceFor(Ludots.Core.Map.Hex.VertexMap? map)
    {
        _terrainSource ??= new VertexMapTerrainChunkMeshSource(null);
        if (!ReferenceEquals(_terrainSource.Map, map))
        {
            _terrainSource = new VertexMapTerrainChunkMeshSource(map);
        }
        return _terrainSource;
    }

    /// <summary>
    /// 按聚焦地图的车道组合 Decal 接收面：地形接收面（.height 渲染源优先，其次 VertexMap）承担 stamp 高度拟合；
    /// 单件静态网格接收面与地形接收面同时重画相交网格——贴花可同时落在地面与道具/建筑上。
    /// Fit 永远只走地形车道（静态网格无高度采样，authored Y 不得存活）；无任何地形车道时抛错，
    /// Decal 没有可退化的占位接收面。
    /// </summary>
    internal sealed class MapLaneReceiverMeshProjector : Ludots.Raylib.Render.IRaylibReceiverMeshProjector
    {
        private readonly GameEngine _engine;
        private readonly Ludots.Raylib.Render.IRaylibReceiverMeshProjector _continuousHeightmapRenderer;
        private readonly Ludots.Raylib.Render.IRaylibReceiverMeshProjector _terrainRenderer;
        private readonly Ludots.Raylib.Render.IRaylibReceiverMeshProjector _staticMeshReceiverProjector;

        public MapLaneReceiverMeshProjector(
            GameEngine engine,
            Ludots.Raylib.Render.IRaylibReceiverMeshProjector continuousHeightmapRenderer,
            Ludots.Raylib.Render.IRaylibReceiverMeshProjector terrainRenderer,
            Ludots.Raylib.Render.IRaylibReceiverMeshProjector staticMeshReceiverProjector)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _continuousHeightmapRenderer = continuousHeightmapRenderer ?? throw new ArgumentNullException(nameof(continuousHeightmapRenderer));
            _terrainRenderer = terrainRenderer ?? throw new ArgumentNullException(nameof(terrainRenderer));
            _staticMeshReceiverProjector = staticMeshReceiverProjector ?? throw new ArgumentNullException(nameof(staticMeshReceiverProjector));
        }

        public int DrawMeshesOverlappingAabbMeters(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            Raylib_cs.Material material)
        {
            int drawn = 0;
            Ludots.Raylib.Render.IRaylibReceiverMeshProjector? terrain = TryResolveTerrainReceiver();
            if (terrain != null)
            {
                drawn += terrain.DrawMeshesOverlappingAabbMeters(minX, minY, minZ, maxX, maxY, maxZ, material);
            }

            drawn += _staticMeshReceiverProjector.DrawMeshesOverlappingAabbMeters(minX, minY, minZ, maxX, maxY, maxZ, material);
            return drawn;
        }

        public System.Numerics.Vector3 FitYawedStampProjectorCenter(
            in System.Numerics.Vector3 stampCenter,
            float yawRad,
            in System.Numerics.Vector2 stampSizeMeters,
            int stableId)
        {
            Ludots.Raylib.Render.IRaylibReceiverMeshProjector? terrain = TryResolveTerrainReceiver();
            if (terrain == null)
            {
                throw new InvalidOperationException(
                    $"Projected Decal stableId={stableId} stamp fit requires a height-sampling terrain receiver; focused map '{_engine.CurrentMapSession?.MapId.Value ?? "<none>"}' exposes neither a visual heightmap nor a VertexMap, and the static mesh receiver cannot fit stamp height.");
            }

            return terrain.FitYawedStampProjectorCenter(in stampCenter, yawRad, in stampSizeMeters, stableId);
        }

        private Ludots.Raylib.Render.IRaylibReceiverMeshProjector? TryResolveTerrainReceiver()
        {
            if (_engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out Ludots.Platform.Abstractions.IContinuousHeightmap? heightmap) &&
                heightmap is Ludots.Platform.Abstractions.IContinuousHeightmapRenderSource)
            {
                return _continuousHeightmapRenderer;
            }

            if (_engine.VertexMap != null)
            {
                return _terrainRenderer;
            }

            return null;
        }
    }
    }
}
