using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
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
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Map;
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

        private const uint FlagWindowResizable = 4;
        private static readonly ServiceKey<IRaylibBenchmarkRenderer> RaylibBenchmarkRendererKey = new("Platform.RaylibBenchmarkRenderer");
        private static bool _emptyBufferWarned;
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
            RaylibFrameRenderer? frameRenderer = null;

            var terrainRenderer = new RaylibTerrainRenderer
            {
                HeightScale = 2.0f,
                VisibleRadius = 900f,
                SimplifiedCliffRadius = 350f,
            };
            var visualHeightmapRenderer = new RaylibVisualHeightmapRenderer(engine.VFS)
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
                engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)screenRayProvider);
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
                using var fieldRenderPresenter = new RaylibFieldRenderPresenter();
                Ludots.Core.Presentation.Navigation.NavMeshPresentationBuffer navMeshPresentationBuffer =
                    engine.GetService(CoreServiceKeys.NavMeshPresentationBuffer)
                        ?? throw new InvalidOperationException("Raylib host requires the Core NavMeshPresentationBuffer service.");
                using var navMeshPresentationRenderer = new RaylibNavMeshPresentationRenderer(navMeshPresentationBuffer.TileCapacity);
                PresentationMaterialRegistry? materials = engine.GetService(CoreServiceKeys.PresentationMaterialRegistry);
                RaylibPrimitiveRenderMode primitiveMode = ResolvePrimitiveRenderMode();
                using var primitiveRenderer = new RaylibPrimitiveRenderer(primitiveMode, engine.VFS, materials, Ludots.Core.Presentation.Assets.AnimationChannelRegistry.Register);
                primitiveRenderer.BindReceiverMeshProjector(
                    new MapLaneReceiverMeshProjector(engine, visualHeightmapRenderer, terrainRenderer, primitiveRenderer.StaticMeshReceiverProjector));
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
                visualHeightmapRenderer.LoadAlbedoDescriptors(PresentationCatalogMerge.MergeEntries(
                    engine.ConfigCatalog, engine.ConfigPipeline, engine.ConfigConflictReport, RaylibVisualHeightmapRenderer.DefaultAlbedoRelativePath));
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
                Ludots.Core.Client.PresentBindingPresentation.TryEnsureSolePresentBindingPipeline(
                    engine,
                    screenProjector,
                    screenRayProvider,
                    viewController.Fov,
                    viewController,
                    cullingSystem);

                frameRenderer = new RaylibFrameRenderer(
                    engine,
                    uiRoot,
                    skiaRenderer,
                    overlayCompositor,
                    browserLayerRenderer,
                    environmentRenderer,
                    skyEnvironment,
                    waterPass,
                    frameLighting,
                    terrainRenderer,
                    visualHeightmapRenderer,
                    fieldRenderPresenter,
                    navMeshPresentationRenderer,
                    navMeshPresentationBuffer,
                    primitiveRenderer,
                    debugDrawRenderer,
                    benchmarkRenderer,
                    globalFieldVisualBuffer,
                    screenOverlayBuffer,
                    presentationTiming);

                int lastW = screenWidth;
                int lastH = screenHeight;
                string? diagnosticPath = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_DIAGNOSTIC_PATH");
                RaylibScreenshotEvidenceRecorder? screenshotRecorder = RaylibScreenshotEvidenceRecorder.TryCreateFromEnvironment();
                int autoExitFrame = int.TryParse(Environment.GetEnvironmentVariable("LUDOTS_AUTO_EXIT_FRAME"), out int parsedAutoExitFrame)
                    ? Math.Max(0, parsedAutoExitFrame)
                    : 0;
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
                var inputRouter = new RaylibHostInputRouter();
            SyntheticUiPlayback syntheticUiPlayback = RaylibHostInputRouter.ReadSyntheticUiPlayback();
                int frameIndex = 0;
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
                        if (!ReadEnvBoolOrDefault("LUDOTS_RAYLIB_SHADOW", defaultValue: true))
                        {
                            renderDebug.DrawShadows = false;
                        }

                        bool activeMapRequestsDeepBackground = ActiveMapHasTag(engine, MapTags.RaylibDeepBackground);
                        bool activeMapHidesDebugGuides = ActiveMapHasTag(engine, MapTags.RaylibHideDebugGuides);
                        IBenchmarkSceneController? benchmarkController = engine.GetService(CoreServiceKeys.BenchmarkSceneController);
                        bool cleanPerformanceMode = IsCleanPerformanceScene(benchmarkController);
                        bool hostDiagnosticUiSuppressed = benchmarkController is { IsActive: true, SuppressHostDiagnosticUi: true };
                        bool hostDebugGuidesSuppressed =
                            activeMapHidesDebugGuides ||
                            benchmarkController is { IsActive: true, SuppressHostDebugGuides: true };
                        bool drawTerrain = renderDebug.DrawTerrain && !cleanPerformanceMode;
                        bool drawVisualHeightmap = renderDebug.DrawTerrain;
                        bool hasVisualHeightmap = engine.TryGetService(
                            CoreServiceKeys.VisualHeightmap,
                            out IVisualHeightmap? visualHeightmapForFrame) &&
                            visualHeightmapForFrame is IVisualHeightmapRenderSource;
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
                            UiInputFrameResult uiInput = inputRouter.UpdateInput(uiRoot, syntheticUiPlayback, frameIndex, diagnosticPath, syntheticInput);
                            uiCaptured = uiInput.PointerCaptured;
                            uiWheelCaptured = uiInput.WheelCaptured;
                            uiInputHandled = uiInput.Handled;
                            uiInputMs = ElapsedMs(uiInputStart);
                        }

                        presentationTiming?.ObserveUiInput(uiInputMs);
                        engine.SetService(
                            CoreServiceKeys.UiCaptured,
                            RaylibHostInputRouter.ShouldCaptureWorldPointer(
                                uiCaptured,
                                uiWheelCaptured,
                                uiInputHandled));
                        engine.SetService(CoreServiceKeys.UiWheelCaptured, uiWheelCaptured);
                        presentationTiming?.ObserveHostPreTick(ElapsedMs(preTickStart));

                        Ludots.Core.Client.PresentBindingPresentation.TryEnsureSolePresentBindingPipeline(
                            engine,
                            screenProjector,
                            screenRayProvider,
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
                            engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? coreVisualHeightmap)
                                ? coreVisualHeightmap
                                : null);
                        long postTickStart = Stopwatch.GetTimestamp();
                        if (autoOrbitDegPerSecond != 0f)
                        {
                            var authorityCamera = Ludots.Core.Client.ClientLocalSeatAccess.ResolveAuthorityCamera(engine);
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
                        if (!Ludots.Core.Client.PresentBindingPresentation.TrySyncSolePresentPipeline(
                                engine,
                                cameraPresenter,
                                screenProjector,
                                screenRayProvider,
                                cameraAlpha,
                                viewController.Fov,
                                renderCameraDebug,
                                viewController,
                                cullingSystem))
                        {
                            if (engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats) &&
                                seats != null &&
                                seats.Count > 0)
                            {
                                throw new InvalidOperationException(
                                    "ClientLocalSeatRegistry is published but PresentBinding pipeline failed to sync.");
                            }

                            cameraPresenter.Update(ClientLocalSeatAccess.ResolveAuthorityCamera(engine), cameraAlpha, renderCameraDebug);
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

                        var activeCamera = cameraAdapter.Camera;
                        var renderFrame = new RaylibRenderFrame(
                            ActiveCamera: activeCamera,
                            ActiveCameraState: cameraPresenter.SmoothedRenderState,
                            RenderDebug: renderDebug,
                            OverlayScene: overlayScene,
                            Width: lastW,
                            Height: lastH,
                            TimeSeconds: runtimeStopwatch.Elapsed.TotalSeconds,
                            DeltaSeconds: dt,
                            ActiveMapRequestsDeepBackground: activeMapRequestsDeepBackground,
                            HostDebugGuidesSuppressed: hostDebugGuidesSuppressed,
                            DrawTerrain: drawTerrain,
                            DrawVisualHeightmap: drawVisualHeightmap,
                            HasVisualHeightmap: hasVisualHeightmap,
                            DrawPrimitives: drawPrimitives,
                            DrawDebugDraw: drawDebugDraw,
                            DrawFieldOverlays: drawFieldOverlays,
                            DrawSkiaUi: drawSkiaUi,
                            DrawNavMeshOverlay: drawNavMeshOverlay,
                            CleanPerformanceMode: cleanPerformanceMode,
                            HostDiagnosticUiSuppressed: hostDiagnosticUiSuppressed,
                            EmptyBufferWarned: _emptyBufferWarned);
                        _emptyBufferWarned = frameRenderer.RenderFrame(renderFrame).EmptyBufferWarned;
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
                            RaylibDiagnosticHud.Draw(engine, presentationTiming);
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

                        if (screenshotRecorder != null &&
                            screenshotRecorder.ShouldCapture(frameIndex, runtimeStopwatch.ElapsedMilliseconds))
                        {
                            double screenshotElapsedMs = screenshotRecorder.CaptureFrame(
                                frameIndex,
                                lastW,
                                lastH,
                                writeDiagnostics: () =>
                                {
                                    AppendRaylibDiagnostic(
                                        diagnosticPath,
                                        $"screenshot frame={frameIndex} cameraPos=({activeCamera.position.X:F2},{activeCamera.position.Y:F2},{activeCamera.position.Z:F2}) cameraTarget=({activeCamera.target.X:F2},{activeCamera.target.Y:F2},{activeCamera.target.Z:F2})");
                                    AppendRaylibDiagnostic(diagnosticPath, BuildTimingDiagnostic(engine, presentationTiming, overlayScene));
                                    AppendRaylibDiagnostic(diagnosticPath, primitiveRenderer.BuildVisualKindDiagnosticSummary());
                                    if (engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshesForDiagnostics))
                                    {
                                        AppendRaylibDiagnostic(diagnosticPath, primitiveRenderer.BuildPrimitiveLaneDiagnosticSummary(meshesForDiagnostics));
                                    }

                                    AppendRaylibDiagnostic(diagnosticPath, BuildInputSelectionDiagnostic(engine));
                                });
                            presentationTiming?.ObserveScreenshot(screenshotElapsedMs);
                        }

                        if (autoExitFrame > 0 && frameIndex >= autoExitFrame && screenshotRecorder?.Pending != true)
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
                frameRenderer?.Dispose();
                soundConsumer?.Dispose();
                if (windowOpened) Rl.CloseWindow();
                terrainRenderer.Dispose();
                visualHeightmapRenderer.Dispose();
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
        internal static void AppendRaylibDiagnostic(string? diagnosticPath, string message)
        {
            RaylibAdapterEnv.AppendDiagnostic(diagnosticPath, message);
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

        internal static bool ReadEnvBoolOrDefault(string key, bool defaultValue)
        {
            return RaylibAdapterEnv.ReadEnvBoolOrDefault(key, defaultValue);
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


        internal static int ReadEnvIntOrDefault(string key, int defaultValue)
        {
            return RaylibAdapterEnv.ReadEnvIntOrDefault(key, defaultValue);
        }

        internal static float ReadEnvFloatOrDefault(string key, float defaultValue)
        {
            return RaylibAdapterEnv.ReadEnvFloatOrDefault(key, defaultValue);
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




        private static double ElapsedMs(long startTicks)
        {
            return (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
        }
    /// <summary>
    /// 按聚焦地图的车道组合 Decal 接收面：地形接收面（vhtm 渲染源优先，其次 VertexMap）承担 stamp 高度拟合；
    /// 单件静态网格接收面与地形接收面同时重画相交网格——贴花可同时落在地面与道具/建筑上。
    /// Fit 永远只走地形车道（静态网格无高度采样，authored Y 不得存活）；无任何地形车道时抛错，
    /// Decal 没有可退化的占位接收面。
    /// </summary>
    internal sealed class MapLaneReceiverMeshProjector : Ludots.Raylib.Render.IRaylibReceiverMeshProjector
    {
        private readonly GameEngine _engine;
        private readonly Ludots.Raylib.Render.IRaylibReceiverMeshProjector _visualHeightmapRenderer;
        private readonly Ludots.Raylib.Render.IRaylibReceiverMeshProjector _terrainRenderer;
        private readonly Ludots.Raylib.Render.IRaylibReceiverMeshProjector _staticMeshReceiverProjector;

        public MapLaneReceiverMeshProjector(
            GameEngine engine,
            Ludots.Raylib.Render.IRaylibReceiverMeshProjector visualHeightmapRenderer,
            Ludots.Raylib.Render.IRaylibReceiverMeshProjector terrainRenderer,
            Ludots.Raylib.Render.IRaylibReceiverMeshProjector staticMeshReceiverProjector)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _visualHeightmapRenderer = visualHeightmapRenderer ?? throw new ArgumentNullException(nameof(visualHeightmapRenderer));
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
            if (_engine.TryGetService(CoreServiceKeys.VisualHeightmap, out Ludots.Platform.Abstractions.IVisualHeightmap? heightmap) &&
                heightmap is Ludots.Platform.Abstractions.IVisualHeightmapRenderSource)
            {
                return _visualHeightmapRenderer;
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
