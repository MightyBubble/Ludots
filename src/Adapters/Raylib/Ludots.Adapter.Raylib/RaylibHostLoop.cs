using System;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Adapter.Raylib.Services;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Components;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.Presentation.Skia;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using SkiaSharp;

namespace Ludots.Adapter.Raylib
{
    internal static class RaylibHostLoop
    {
        private const uint FlagWindowResizable = 4;
        private static readonly ServiceKey<IRaylibBenchmarkRenderer> RaylibBenchmarkRendererKey = new("Platform.RaylibBenchmarkRenderer");
        private static bool _uiPointerCaptured;
        private static bool _emptyBufferWarned;

        public static void Run(RaylibHostSetup setup)
        {
            var engine = setup.Engine;
            var config = setup.Config;
            var uiRoot = setup.UiRoot;
            var skiaRenderer = setup.Renderer;
            var presentationTiming = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);

            int screenWidth = config.WindowWidth <= 0 ? 1280 : config.WindowWidth;
            int screenHeight = config.WindowHeight <= 0 ? 720 : config.WindowHeight;
            string title = string.IsNullOrWhiteSpace(config.WindowTitle) ? "Ludots Engine" : config.WindowTitle;
            // targetFps = 0 表示不锁帧，< 0 使用默认 60
            int targetFps = config.TargetFps == 0 ? 0 : (config.TargetFps < 0 ? 60 : config.TargetFps);
            bool windowOpened = false;
            bool windowResizable = config.WindowResizable || config.WindowStartMaximized;

            var terrainRenderer = new RaylibTerrainRenderer
            {
                HeightScale = 2.0f,
                VisibleRadius = 900f,
                SimplifiedCliffRadius = 350f,
                LightPosition = new Vector3(50f, 200f, 100f),
                Ambient = 0.8f,
                LightIntensity = 1.0f
            };

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

                screenWidth = Math.Max(1, Rl.GetScreenWidth());
                screenHeight = Math.Max(1, Rl.GetScreenHeight());
                config.WindowWidth = screenWidth;
                config.WindowHeight = screenHeight;

                using var compositeRenderer = new RaylibSkiaRenderer(screenWidth, screenHeight);
                using var underlayLayer = new SkiaRasterLayer();
                using var uiLayer = new SkiaRasterLayer();
                using var overlayLayer = new SkiaRasterLayer();
                using var overlaySkiaRenderer = new SkiaOverlayRenderer();
                underlayLayer.Resize(screenWidth, screenHeight);
                uiLayer.Resize(screenWidth, screenHeight);
                overlayLayer.Resize(screenWidth, screenHeight);
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

                var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, viewController);
                var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, viewController);
                screenProjector.BindPresenter(cameraPresenter);
                screenRayProvider.BindPresenter(cameraPresenter);
                engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)screenProjector);
                engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)screenRayProvider);

                var performerInstances = engine.GetService(CoreServiceKeys.PerformerEntityRuntime);
                var cullingSystem = new CameraCullingSystem(
                    engine.World,
                    engine.GameSession.Camera,
                    engine.SpatialQueries,
                    viewController,
                    loadedChunks: null,
                    performers: performerInstances,
                    timingDiagnostics: presentationTiming);
                engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(cullingSystem);
                engine.SetService(CoreServiceKeys.CameraCullingDebugState, cullingSystem.DebugState);

                var renderCameraDebug = new RenderCameraDebugState();
                engine.SetService(CoreServiceKeys.RenderCameraDebugState, renderCameraDebug);

                engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));
                var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);

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
                    hudProjection = new WorldHudToScreenSystem(engine.World, worldHud, worldHudStrings, screenProjector, viewController, screenHud, presentationTiming);
                    overlaySceneBuilder = new PresentationOverlaySceneBuilder(screenHud, worldHudStrings, textCatalog, localeSelection, screenOverlayBuffer);
                    overlayScene = new PresentationOverlayScene(screenHud.Capacity + ScreenOverlayBuffer.MaxItems);
                }

                ValidateRequiredContextBeforeLoop(engine);

                var debugDrawRenderer = new RaylibDebugDrawRenderer { PlaneY = 0.35f };
                PresentationMaterialRegistry? materials = engine.GetService(CoreServiceKeys.PresentationMaterialRegistry);
                using var primitiveRenderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Instanced, engine.VFS, materials);
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
                engine.LoadMap(config.StartupMapId);

                int lastW = screenWidth;
                int lastH = screenHeight;
                bool underlayHadContent = false;
                bool overlayHadContent = false;
                bool uiHadContent = false;
                bool compositeHadContent = false;
                int underlayLayerVersion = -1;
                int topOverlayLayerVersion = -1;
                var underlayPacer = new PresentationOverlayLanePacer(PresentationOverlayLayer.UnderUi);
                string? screenshotPath = Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_PATH");
                string? diagnosticPath = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_DIAGNOSTIC_PATH");
                string? screenshotTargetPath = string.IsNullOrWhiteSpace(screenshotPath)
                    ? null
                    : Path.GetFullPath(screenshotPath);
                string? screenshotFileName = string.IsNullOrWhiteSpace(screenshotTargetPath)
                    ? null
                    : Path.GetFileName(screenshotTargetPath);
                string? screenshotWorkingPath = string.IsNullOrWhiteSpace(screenshotFileName)
                    ? null
                    : Path.Combine(Environment.CurrentDirectory, screenshotFileName);
                bool screenshotPending = !string.IsNullOrWhiteSpace(screenshotFileName);
                int screenshotFrame = int.TryParse(Environment.GetEnvironmentVariable("LUDOTS_TAKE_SCREENSHOT_FRAME"), out int parsedScreenshotFrame)
                    ? Math.Max(1, parsedScreenshotFrame)
                    : 60;
                int frameIndex = 0;

                while (!Rl.WindowShouldClose())
                {
                    try
                    {
                        int w = Math.Max(1, Rl.GetScreenWidth());
                        int h = Math.Max(1, Rl.GetScreenHeight());
                        if (w != lastW || h != lastH)
                        {
                            lastW = w;
                            lastH = h;
                            config.WindowWidth = w;
                            config.WindowHeight = h;
                            compositeRenderer.Resize(w, h);
                            underlayLayer.Resize(w, h);
                            uiLayer.Resize(w, h);
                            overlayLayer.Resize(w, h);
                            uiRoot.Resize(w, h);
                        }

                        float dt = Rl.GetFrameTime();
                        var renderDebug = ResolveRenderDebugState(engine);
                        bool drawTerrain = renderDebug.DrawTerrain;
                        bool drawPrimitives = renderDebug.DrawPrimitives;
                        bool drawDebugDraw = renderDebug.DrawDebugDraw;
                        bool drawSkiaUi = renderDebug.DrawSkiaUi;

                        double uiInputMs = 0d;
                        bool uiCaptured = false;
                        if (drawSkiaUi)
                        {
                            long uiInputStart = Stopwatch.GetTimestamp();
                            uiCaptured = UpdateInput(uiRoot);
                            uiInputMs = ElapsedMs(uiInputStart);
                        }

                        presentationTiming?.ObserveUiInput(uiInputMs);
                        engine.SetService(CoreServiceKeys.UiCaptured, uiCaptured);

                        engine.Tick(dt);

                        float cameraAlpha = presentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
                        cameraPresenter.Update(engine.GameSession.Camera, cameraAlpha, renderCameraDebug);
                        hudProjection?.Update(dt);
                        benchmarkRenderer?.PrepareFrame(presentationTiming, lastW, lastH);
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

                        string? activeMapId = engine.CurrentMapSession?.MapId.Value;
                        bool uxPrototypeMapActive = string.Equals(activeMapId, "ux_prototype_battle", StringComparison.OrdinalIgnoreCase);

                        Rl.BeginDrawing();
                        Rl.ClearBackground(uxPrototypeMapActive
                            ? new Raylib_cs.Color(6, 10, 16, 255)
                            : new Raylib_cs.Color(0, 0, 0, 255));

                        var activeCamera = cameraAdapter.Camera;
                        Rl.BeginMode3D(activeCamera);

                        if (drawDebugDraw && !uxPrototypeMapActive)
                        {
                            DrawInfiniteGrid(activeCamera.target, 300, 1.0f, 10);

                            var target = activeCamera.target;
                            Rl.DrawLine3D(target, target + new Vector3(2.0f, 0, 0), Color.RED);
                            Rl.DrawLine3D(target, target + new Vector3(0, 0, 2.0f), Color.BLUE);
                            Rl.DrawLine3D(target, target + new Vector3(0, 2.0f, 0), Color.GREEN);
                        }

                        // 锚定到 target，网格以观察点为中心；halfCount 越大边界越远
                        if (drawTerrain)
                        {
                            long terrainStart = Stopwatch.GetTimestamp();
                            terrainRenderer.Render(engine.VertexMap, activeCamera);
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

                        bool benchmarkDrew = false;
                        if (benchmarkRenderer != null)
                        {
                            benchmarkDrew = benchmarkRenderer.Draw(activeCamera);
                        }

                        if (!benchmarkDrew &&
                            drawPrimitives &&
                            engine.TryGetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, out PrimitiveDrawBuffer draw) &&
                            engine.TryGetService(CoreServiceKeys.PresentationMeshAssetRegistry, out MeshAssetRegistry meshes))
                        {
                            if (!_emptyBufferWarned && draw.GetSpan().Length == 0)
                            {
                                System.Diagnostics.Debug.WriteLine("[RaylibHostLoop] PrimitiveDrawBuffer is empty on first render frame — no Marker3D performers emitting?");
                                _emptyBufferWarned = true;
                            }
                            long primitiveStart = Stopwatch.GetTimestamp();
                            PrimitiveDrawBuffer? snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer);
                            SkinnedVisualBatchBuffer? skinnedBatch = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
                            engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap? visualHeightmap);
                            primitiveRenderer.Draw(draw, activeCamera, snapshot, skinnedBatch, meshes, renderDebug.AcceptanceScaleMultiplier, visualHeightmap);
                            presentationTiming?.ObservePrimitiveRender(
                                ElapsedMs(primitiveStart),
                                primitiveRenderer.LastInstancedInstances,
                                primitiveRenderer.LastInstancedBatches);
                        }
                        else
                        {
                            presentationTiming?.ObservePrimitiveRender(0d, 0, 0);
                        }

                        // Draw ground overlays (range circles, cones, etc.)
                        if (engine.TryGetService(CoreServiceKeys.GroundOverlayBuffer, out GroundOverlayBuffer overlays) &&
                            overlays.Count > 0)
                        {
                            DrawGroundOverlays(overlays);
                        }

                        if (engine.GlobalContext.TryGetValue(CoreServiceKeys.RoadSplineBuffer.Name, out var splineObj) &&
                            splineObj is RoadSplineBuffer roadSplines && roadSplines.Count > 0)
                        {
                            DrawRoadSplines(roadSplines);
                        }

                        if (drawDebugDraw &&
                            engine.TryGetService(CoreServiceKeys.DebugDrawCommandBuffer, out DebugDrawCommandBuffer dd))
                        {
                            debugDrawRenderer.Draw(dd);
                        }

                        Rl.EndMode3D();

                        long overlayStart = Stopwatch.GetTimestamp();
                        overlaySkiaRenderer.ResetFrameStats();
                        double overlayPaintMs = 0d;
                        double overlayCompositeMs = 0d;
                        double overlayUploadMs = 0d;
                        double overlayFinalDrawMs = 0d;

                        bool hasUnderlay = overlayScene != null && overlayScene.ContainsLayer(PresentationOverlayLayer.UnderUi);
                        bool hasTopOverlay = overlayScene != null && overlayScene.ContainsLayer(PresentationOverlayLayer.TopMost);
                        bool hasUiLayer = drawSkiaUi && uiRoot.Scene != null;

                        int currentUnderlayVersion = overlayScene?.GetLayerVersion(PresentationOverlayLayer.UnderUi) ?? 0;
                        int currentTopOverlayVersion = overlayScene?.GetLayerVersion(PresentationOverlayLayer.TopMost) ?? 0;
                        bool refreshUnderlay = overlayScene != null && (hasUnderlay || underlayHadContent) &&
                            (currentUnderlayVersion != underlayLayerVersion || hasUnderlay != underlayHadContent);
                        bool underlayCanvasChanged = false;
                        if (refreshUnderlay)
                        {
                            long underlayRenderStart = Stopwatch.GetTimestamp();
                            PresentationOverlayLanePacer.LaneRefreshPlan underlayPlan = default;
                            if (hasUnderlay)
                            {
                                underlayPlan = underlayPacer.BuildPlan(overlayScene!);
                            }

                            if (!hasUnderlay || underlayPlan.HasAnyRefresh)
                            {
                                underlayLayer.Clear();
                                if (hasUnderlay)
                                {
                                    overlaySkiaRenderer.Render(overlayScene!, underlayLayer.Canvas,
                                        PresentationOverlayLayer.UnderUi, underlayPlan);
                                    underlayLayer.SetHasContent(true);
                                }
                                underlayCanvasChanged = true;
                            }

                            if (hasUnderlay)
                                underlayPacer.MarkPresented(overlayScene!, underlayPlan);
                            else
                                underlayPacer.Reset();

                            underlayHadContent = hasUnderlay;
                            underlayLayerVersion = currentUnderlayVersion;
                            overlayPaintMs += ElapsedMs(underlayRenderStart);
                        }

                        bool refreshUiLayer = hasUiLayer != uiHadContent || (drawSkiaUi && uiRoot.IsDirty);
                        if (refreshUiLayer)
                        {
                            long uiRenderStart = Stopwatch.GetTimestamp();
                            uiLayer.Clear();
                            if (hasUiLayer)
                            {
                                skiaRenderer.SetCanvas(uiLayer.Canvas);
                                uiRoot.Render();
                                uiLayer.SetHasContent(true);
                            }
                            double uiRenderMs = ElapsedMs(uiRenderStart);
                            presentationTiming?.ObserveUiRender(uiRenderMs);
                            overlayPaintMs += uiRenderMs;
                            uiHadContent = hasUiLayer;
                        }
                        else
                        {
                            presentationTiming?.ObserveUiRender(0d);
                        }

                        bool refreshTopOverlay = overlayScene != null && (hasTopOverlay || overlayHadContent) &&
                            (currentTopOverlayVersion != topOverlayLayerVersion || hasTopOverlay != overlayHadContent);
                        if (refreshTopOverlay)
                        {
                            long topOverlayRenderStart = Stopwatch.GetTimestamp();
                            overlayLayer.Clear();
                            if (hasTopOverlay)
                            {
                                overlaySkiaRenderer.Render(overlayScene!, overlayLayer.Canvas, PresentationOverlayLayer.TopMost);
                                overlayLayer.SetHasContent(true);
                            }
                            overlayPaintMs += ElapsedMs(topOverlayRenderStart);
                            overlayHadContent = hasTopOverlay;
                            topOverlayLayerVersion = currentTopOverlayVersion;
                        }

                        bool hasCompositeContent = hasUnderlay || hasUiLayer || hasTopOverlay;
                        bool refreshComposite = underlayCanvasChanged || refreshUiLayer || refreshTopOverlay
                            || hasCompositeContent != compositeHadContent;
                        if (refreshComposite)
                        {
                            long compositeStart = Stopwatch.GetTimestamp();
                            compositeRenderer.Canvas.Clear(SKColors.Transparent);
                            if (hasUnderlay)
                            {
                                underlayLayer.DrawTo(compositeRenderer.Canvas);
                            }

                            if (hasUiLayer)
                            {
                                uiLayer.DrawTo(compositeRenderer.Canvas);
                            }

                            if (hasTopOverlay)
                            {
                                overlayLayer.DrawTo(compositeRenderer.Canvas);
                            }
                            overlayCompositeMs = ElapsedMs(compositeStart);

                            long uiUploadStart = Stopwatch.GetTimestamp();
                            compositeRenderer.UpdateTexture();
                            overlayUploadMs = hasCompositeContent ? ElapsedMs(uiUploadStart) : 0d;
                            presentationTiming?.ObserveUiUpload(overlayUploadMs);
                            compositeHadContent = hasCompositeContent;
                        }
                        else
                        {
                            presentationTiming?.ObserveUiUpload(0d);
                        }

                        if (hasCompositeContent || compositeHadContent)
                        {
                            long finalDrawStart = Stopwatch.GetTimestamp();
                            compositeRenderer.Draw();
                            overlayFinalDrawMs = ElapsedMs(finalDrawStart);
                        }

                        presentationTiming?.ObserveCompositeSkip(!refreshComposite);
                        screenOverlayBuffer?.Clear();
                        presentationTiming?.ObserveScreenOverlayDraw(
                            ElapsedMs(overlayStart),
                            overlayPaintMs,
                            overlayCompositeMs,
                            overlayUploadMs,
                            overlayFinalDrawMs,
                            overlaySkiaRenderer.RebuiltLaneCountLastFrame,
                            overlaySkiaRenderer.CachedTextLayoutCount);

                        Rl.EndDrawing();

                        frameIndex++;
                        if (screenshotPending && frameIndex >= screenshotFrame)
                        {
                            string fullScreenshotPath = screenshotTargetPath!;
                            string? screenshotDirectory = Path.GetDirectoryName(fullScreenshotPath);
                            if (!string.IsNullOrWhiteSpace(screenshotDirectory))
                            {
                                Directory.CreateDirectory(screenshotDirectory);
                            }

                            AppendRaylibDiagnostic(
                                diagnosticPath,
                                $"screenshot frame={frameIndex} cameraPos=({activeCamera.position.X:F2},{activeCamera.position.Y:F2},{activeCamera.position.Z:F2}) cameraTarget=({activeCamera.target.X:F2},{activeCamera.target.Y:F2},{activeCamera.target.Z:F2})");
                            AppendRaylibDiagnostic(diagnosticPath, primitiveRenderer.BuildVisualKindDiagnosticSummary());
                            AppendRaylibDiagnostic(diagnosticPath, BuildInputSelectionDiagnostic(engine));

                            Rl.TakeScreenshot(screenshotFileName!);
                            if (!string.IsNullOrWhiteSpace(screenshotWorkingPath) &&
                                !string.Equals(screenshotWorkingPath, fullScreenshotPath, StringComparison.OrdinalIgnoreCase) &&
                                File.Exists(screenshotWorkingPath))
                            {
                                File.Copy(screenshotWorkingPath, fullScreenshotPath, overwrite: true);
                                File.Delete(screenshotWorkingPath);
                            }

                            screenshotPending = false;
                            Log.Info(in LogChannels.Engine, $"Captured runtime screenshot: {fullScreenshotPath}");
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
                if (windowOpened) Rl.CloseWindow();
                terrainRenderer.Dispose();
                engine.Stop();
            }
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

        private static bool UpdateInput(UIRoot uiRoot)
        {
            var mousePos = Rl.GetMousePosition();
            UiNode? hitNode = uiRoot.Scene?.HitTest(mousePos.X, mousePos.Y);
            bool hitInteractiveUi = IsInteractiveUiNode(hitNode);

            if (_uiPointerCaptured || hitInteractiveUi)
            {
                uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Move, X = mousePos.X, Y = mousePos.Y });
            }

            if (Rl.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON))
            {
                if (hitInteractiveUi &&
                    uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Down, X = mousePos.X, Y = mousePos.Y }))
                {
                    _uiPointerCaptured = true;
                }
            }

            if (Rl.IsMouseButtonReleased(MouseButton.MOUSE_LEFT_BUTTON))
            {
                if (_uiPointerCaptured)
                {
                    uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Up, X = mousePos.X, Y = mousePos.Y });
                    _uiPointerCaptured = false;
                }
            }

            return _uiPointerCaptured;
        }

        private static bool IsInteractiveUiNode(UiNode? node)
        {
            for (UiNode? current = node; current != null; current = current.Parent)
            {
                if (current.ActionHandles.Count > 0)
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
            if (engine.TryGetService(CoreServiceKeys.HoveredEntity, out Entity hovered) &&
                hovered != Entity.Null)
            {
                hoveredSummary = $"hovered={DescribeEntity(engine, hovered)}";
            }

            string selectedSummary = "selected=(none)";
            if (SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected) &&
                selected != Entity.Null)
            {
                selectedSummary = $"selected={DescribeEntity(engine, selected)}";
            }

            bool uiCaptured = engine.TryGetService(CoreServiceKeys.UiCaptured, out bool captured) &&
                captured;

            string dragSummary = "drag=inactive";
            if (engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity localPlayer) &&
                engine.World.IsAlive(localPlayer) &&
                engine.World.Has<SelectionDragState>(localPlayer))
            {
                ref SelectionDragState drag = ref engine.World.Get<SelectionDragState>(localPlayer);
                dragSummary = drag.Active
                    ? $"drag=active({drag.StartScreen.X:0.##},{drag.StartScreen.Y:0.##})->({drag.CurrentScreen.X:0.##},{drag.CurrentScreen.Y:0.##})"
                    : "drag=idle";
            }

            string targetSummary = BuildSelectionTargetSummary(engine);
            return $"windowFocused={Rl.IsWindowFocused()} {pointerSummary} {authPointerSummary} {liveSelectSummary} {authSelectSummary} {liveCommandSummary} {authCommandSummary} {hoveredSummary} {selectedSummary} uiCaptured={uiCaptured} {dragSummary} {targetSummary}";
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
                        screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0f));
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

        private static void DrawGroundOverlays(GroundOverlayBuffer overlays)
        {
            var span = overlays.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                switch (item.Shape)
                {
                    case GroundOverlayShape.Circle:
                        DrawGroundCircle(in item);
                        break;
                    case GroundOverlayShape.Cone:
                        DrawGroundCone(in item);
                        break;
                    case GroundOverlayShape.Ring:
                        DrawGroundRing(in item);
                        break;
                    case GroundOverlayShape.Line:
                        DrawGroundLine(in item);
                        break;
                }
            }
        }

        private static void DrawRoadSplines(RoadSplineBuffer splines)
        {
            ReadOnlySpan<float> p0x = splines.P0X;
            ReadOnlySpan<float> p0y = splines.P0Y;
            ReadOnlySpan<float> p0z = splines.P0Z;
            ReadOnlySpan<float> p1x = splines.P1X;
            ReadOnlySpan<float> p1y = splines.P1Y;
            ReadOnlySpan<float> p1z = splines.P1Z;
            ReadOnlySpan<float> p2x = splines.P2X;
            ReadOnlySpan<float> p2y = splines.P2Y;
            ReadOnlySpan<float> p2z = splines.P2Z;
            ReadOnlySpan<float> p3x = splines.P3X;
            ReadOnlySpan<float> p3y = splines.P3Y;
            ReadOnlySpan<float> p3z = splines.P3Z;
            ReadOnlySpan<float> width = splines.Width;
            ReadOnlySpan<float> borderWidth = splines.BorderWidth;
            ReadOnlySpan<float> fillR = splines.FillR;
            ReadOnlySpan<float> fillG = splines.FillG;
            ReadOnlySpan<float> fillB = splines.FillB;
            ReadOnlySpan<float> fillA = splines.FillA;
            ReadOnlySpan<float> borderR = splines.BorderR;
            ReadOnlySpan<float> borderG = splines.BorderG;
            ReadOnlySpan<float> borderB = splines.BorderB;
            ReadOnlySpan<float> borderA = splines.BorderA;

            for (int i = 0; i < splines.Count; i++)
            {
                Vector3 p0 = new(p0x[i], p0y[i], p0z[i]);
                Vector3 p1 = new(p1x[i], p1y[i], p1z[i]);
                Vector3 p2 = new(p2x[i], p2y[i], p2z[i]);
                Vector3 p3 = new(p3x[i], p3y[i], p3z[i]);
                float drawWidth = MathF.Max(0.02f, width[i]);
                float drawBorder = MathF.Max(0.01f, borderWidth[i]);
                var fill = ToRaylibColor(new Vector4(fillR[i], fillG[i], fillB[i], fillA[i]));
                var border = ToRaylibColor(new Vector4(borderR[i], borderG[i], borderB[i], borderA[i]));
                DrawRoadSplineRibbon(p0, p1, p2, p3, drawWidth, fill, border, drawBorder);
            }
        }

        private static void DrawRoadSplineRibbon(
            in Vector3 p0,
            in Vector3 p1,
            in Vector3 p2,
            in Vector3 p3,
            float width,
            Color fill,
            Color border,
            float borderWidth)
        {
            const int samples = 20;
            Span<Vector3> points = stackalloc Vector3[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                points[i] = EvaluateCubicBezier(p0, p1, p2, p3, t);
            }

            if (fill.a > 0)
            {
                int lanes = Math.Max(1, (int)MathF.Ceiling(width / 0.08f));
                for (int lane = 0; lane < lanes; lane++)
                {
                    float alpha = lanes == 1 ? 0f : lane / (float)(lanes - 1);
                    float offset = (alpha - 0.5f) * width;
                    DrawOffsetPolyline(points, offset, fill);
                }
            }

            if (border.a > 0)
            {
                float edgeOffset = (width * 0.5f) + borderWidth;
                DrawOffsetPolyline(points, edgeOffset, border);
                DrawOffsetPolyline(points, -edgeOffset, border);
            }
        }

        private static void DrawOffsetPolyline(ReadOnlySpan<Vector3> points, float offset, Color color)
        {
            if (points.Length < 2)
            {
                return;
            }

            Vector3 previous = OffsetPoint(points, 0, offset);
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 current = OffsetPoint(points, i, offset);
                Rl.DrawLine3D(previous, current, color);
                previous = current;
            }
        }

        private static Vector3 OffsetPoint(ReadOnlySpan<Vector3> points, int index, float offset)
        {
            Vector3 current = points[index];
            Vector3 forward = index == points.Length - 1
                ? current - points[index - 1]
                : points[index + 1] - current;
            Vector2 lateral = new(-forward.Z, forward.X);
            float length = lateral.Length();
            if (length <= 0.0001f)
            {
                return current;
            }

            lateral /= length;
            return new Vector3(current.X + lateral.X * offset, current.Y, current.Z + lateral.Y * offset);
        }

        private static Vector3 EvaluateCubicBezier(in Vector3 p0, in Vector3 p1, in Vector3 p2, in Vector3 p3, float t)
        {
            float oneMinusT = 1f - t;
            float a = oneMinusT * oneMinusT * oneMinusT;
            float b = 3f * oneMinusT * oneMinusT * t;
            float c = 3f * oneMinusT * t * t;
            float d = t * t * t;
            return (p0 * a) + (p1 * b) + (p2 * c) + (p3 * d);
        }

        private static void DrawGroundCircle(in GroundOverlayItem item)
        {
            const int segments = 48;
            float step = MathF.PI * 2f / segments;
            var center = item.Center;

            // Draw fill as multiple concentric rings (approximation since Raylib has no DrawTriangle3D)
            if (item.FillColor.W > 0.01f)
            {
                var fillColor = ToRaylibColor(item.FillColor);
                const int fillRings = 4;
                for (int r = 1; r <= fillRings; r++)
                {
                    float radius = item.Radius * r / fillRings;
                    for (int s = 0; s < segments; s++)
                    {
                        float a0 = s * step;
                        float a1 = (s + 1) * step;
                        var p0 = new Vector3(center.X + MathF.Cos(a0) * radius, center.Y, center.Z + MathF.Sin(a0) * radius);
                        var p1 = new Vector3(center.X + MathF.Cos(a1) * radius, center.Y, center.Z + MathF.Sin(a1) * radius);
                        Rl.DrawLine3D(p0, p1, fillColor);
                    }
                }
            }

            // Draw border as line loop (outermost ring, thicker appearance via slight offset)
            if (item.BorderColor.W > 0.01f && item.BorderWidth > 0f)
            {
                var border = ToRaylibColor(item.BorderColor);
                for (int s = 0; s < segments; s++)
                {
                    float a0 = s * step;
                    float a1 = (s + 1) * step;
                    var p0 = new Vector3(center.X + MathF.Cos(a0) * item.Radius, center.Y, center.Z + MathF.Sin(a0) * item.Radius);
                    var p1 = new Vector3(center.X + MathF.Cos(a1) * item.Radius, center.Y, center.Z + MathF.Sin(a1) * item.Radius);
                    Rl.DrawLine3D(p0, p1, border);
                }
            }
        }

        private static void DrawGroundRing(in GroundOverlayItem item)
        {
            const int segments = 48;
            float innerRadius = Math.Clamp(item.InnerRadius, 0f, item.Radius);
            float outerRadius = MathF.Max(item.Radius, innerRadius);
            var center = item.Center;

            if (item.FillColor.W > 0.01f && outerRadius > innerRadius)
            {
                var fillColor = ToRaylibColor(item.FillColor);
                const int bands = 6;
                for (int band = 0; band < bands; band++)
                {
                    float radius = innerRadius + (outerRadius - innerRadius) * (band + 0.5f) / bands;
                    DrawGroundArcLoop(center, radius, 0f, MathF.PI * 2f, segments, fillColor);
                }
            }

            if (item.BorderColor.W > 0.01f && item.BorderWidth > 0f)
            {
                var border = ToRaylibColor(item.BorderColor);
                DrawGroundArcLoop(center, outerRadius, 0f, MathF.PI * 2f, segments, border);
                if (innerRadius > 0.001f)
                {
                    DrawGroundArcLoop(center, innerRadius, 0f, MathF.PI * 2f, segments, border);
                }
            }
        }

        private static void DrawGroundCone(in GroundOverlayItem item)
        {
            const int segments = 24;
            float radius = MathF.Max(item.Radius, 0f);
            float start = item.Rotation - item.Angle;
            float end = item.Rotation + item.Angle;
            var center = item.Center;

            if (radius <= 0f)
            {
                return;
            }

            if (item.FillColor.W > 0.01f)
            {
                var fillColor = ToRaylibColor(item.FillColor);
                const int bands = 6;
                for (int band = 1; band <= bands; band++)
                {
                    float ringRadius = radius * band / bands;
                    DrawGroundArcLoop(center, ringRadius, start, end, segments, fillColor);
                }
            }

            if (item.BorderColor.W > 0.01f && item.BorderWidth > 0f)
            {
                var border = ToRaylibColor(item.BorderColor);
                DrawGroundArcLoop(center, radius, start, end, segments, border);
                var left = new Vector3(center.X + MathF.Cos(start) * radius, center.Y, center.Z + MathF.Sin(start) * radius);
                var right = new Vector3(center.X + MathF.Cos(end) * radius, center.Y, center.Z + MathF.Sin(end) * radius);
                Rl.DrawLine3D(center, left, border);
                Rl.DrawLine3D(center, right, border);
            }
        }

        private static void DrawGroundLine(in GroundOverlayItem item)
        {
            float length = item.Length > 0f ? item.Length : item.Radius;
            if (length <= 0f)
            {
                return;
            }

            float dx = MathF.Cos(item.Rotation) * length;
            float dz = MathF.Sin(item.Rotation) * length;
            var a = item.Center;
            var b = new Vector3(a.X + dx, a.Y, a.Z + dz);
            float halfWidth = MathF.Max(0f, item.Width) * 0.5f;
            var normal = new Vector3(-MathF.Sin(item.Rotation), 0f, MathF.Cos(item.Rotation));

            if (item.FillColor.W > 0.01f)
            {
                var fill = ToRaylibColor(item.FillColor);
                int stripes = halfWidth > 0.001f ? Math.Clamp((int)MathF.Ceiling(halfWidth / 0.12f), 1, 8) : 1;
                for (int stripe = -stripes; stripe <= stripes; stripe++)
                {
                    float offset = stripes == 0 ? 0f : halfWidth * stripe / Math.Max(stripes, 1);
                    var delta = normal * offset;
                    Rl.DrawLine3D(a + delta, b + delta, fill);
                }
            }

            if (item.BorderColor.W > 0.01f)
            {
                var border = ToRaylibColor(item.BorderColor);
                Rl.DrawLine3D(a, b, border);
                if (halfWidth > 0.001f)
                {
                    var delta = normal * halfWidth;
                    Rl.DrawLine3D(a + delta, b + delta, border);
                    Rl.DrawLine3D(a - delta, b - delta, border);
                }
            }
        }

        private static void DrawGroundArcLoop(Vector3 center, float radius, float startAngle, float endAngle, int segments, Color color)
        {
            if (segments <= 0 || radius <= 0f)
            {
                return;
            }

            float step = (endAngle - startAngle) / segments;
            for (int s = 0; s < segments; s++)
            {
                float a0 = startAngle + s * step;
                float a1 = startAngle + (s + 1) * step;
                var p0 = new Vector3(center.X + MathF.Cos(a0) * radius, center.Y, center.Z + MathF.Sin(a0) * radius);
                var p1 = new Vector3(center.X + MathF.Cos(a1) * radius, center.Y, center.Z + MathF.Sin(a1) * radius);
                Rl.DrawLine3D(p0, p1, color);
            }
        }

        private static Color ToRaylibColor(Vector4 c) => RaylibColorUtil.ToRaylibColor(in c);
    }
}
