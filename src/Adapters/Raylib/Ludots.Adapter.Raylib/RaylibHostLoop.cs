using System;
using System.Diagnostics;
using System.Numerics;
using Ludots.Adapter.Raylib.Services;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Components;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.Presentation.Skia;
using Ludots.UI;
using Ludots.UI.Input;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using SkiaSharp;

namespace Ludots.Adapter.Raylib
{
    internal static class RaylibHostLoop
    {
        private static bool _uiPointerCaptured;
        private static bool _emptyBufferWarned;

        public static void Run(RaylibHostSetup setup)
        {
            var engine = setup.Engine;
            var config = setup.Config;
            var uiRoot = setup.UiRoot;
            var presentationTiming = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);

            int screenWidth = config.WindowWidth <= 0 ? 1280 : config.WindowWidth;
            int screenHeight = config.WindowHeight <= 0 ? 720 : config.WindowHeight;
            string title = string.IsNullOrWhiteSpace(config.WindowTitle) ? "Ludots Engine" : config.WindowTitle;
            // targetFps = 0 表示不锁帧，< 0 使用默认 60
            int targetFps = config.TargetFps == 0 ? 0 : (config.TargetFps < 0 ? 60 : config.TargetFps);
            bool windowOpened = false;

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
                Rl.InitWindow(screenWidth, screenHeight, title);
                windowOpened = true;
                Rl.SetExitKey(0);
                Rl.SetTargetFPS(targetFps);

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
                engine.GlobalContext[CoreServiceKeys.ViewController.Name] = viewController;

                var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, viewController);
                var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, viewController);
                screenProjector.BindPresenter(cameraPresenter);
                screenRayProvider.BindPresenter(cameraPresenter);
                engine.GlobalContext[CoreServiceKeys.ScreenProjector.Name] = screenProjector;
                engine.GlobalContext[CoreServiceKeys.ScreenRayProvider.Name] = screenRayProvider;

                var cullingSystem = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, viewController, presentationTiming);
                engine.RegisterPresentationSystem(cullingSystem);
                engine.GlobalContext[CoreServiceKeys.CameraCullingDebugState.Name] = cullingSystem.DebugState;

                var renderCameraDebug = new RenderCameraDebugState();
                engine.GlobalContext[CoreServiceKeys.RenderCameraDebugState.Name] = renderCameraDebug;

                engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));
                var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);

                WorldHudToScreenSystem? hudProjection = null;
                PresentationOverlaySceneBuilder? overlaySceneBuilder = null;
                PresentationOverlayScene? overlayScene = null;
                ScreenOverlayBuffer? screenOverlayBuffer = null;
                if (engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationWorldHudBuffer.Name, out var whObj) && whObj is WorldHudBatchBuffer worldHud &&
                    engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationScreenHudBuffer.Name, out var shObj) && shObj is ScreenHudBatchBuffer screenHud)
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

                engine.Start();
                if (string.IsNullOrWhiteSpace(config.StartupMapId))
                {
                    throw new InvalidOperationException("Invalid launcher bootstrap: 'StartupMapId' cannot be empty.");
                }
                engine.LoadMap(config.StartupMapId);

                var debugDrawRenderer = new RaylibDebugDrawRenderer { PlaneY = 0.35f };
                using var primitiveRenderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Instanced, engine.VFS);

                int lastW = screenWidth;
                int lastH = screenHeight;
                bool underlayHadContent = false;
                bool overlayHadContent = false;
                bool uiHadContent = false;
                bool compositeHadContent = false;
                int underlayLayerVersion = -1;
                int topOverlayLayerVersion = -1;

                while (!Rl.WindowShouldClose())
                {
                    try
                    {
                        int w = Rl.GetScreenWidth();
                        int h = Rl.GetScreenHeight();
                        if (w != lastW || h != lastH)
                        {
                            lastW = w;
                            lastH = h;
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
                        engine.GlobalContext[CoreServiceKeys.UiCaptured.Name] = uiCaptured;
                        engine.Tick(dt);

                        float cameraAlpha = presentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
                        cameraPresenter.Update(engine.GameSession.Camera, cameraAlpha, renderCameraDebug);
                        hudProjection?.Update(dt);
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

                        Rl.BeginDrawing();
                        Rl.ClearBackground(new Raylib_cs.Color(0, 0, 0, 255));

                        var activeCamera = cameraAdapter.Camera;
                        Rl.BeginMode3D(activeCamera);

                        DrawInfiniteGrid(activeCamera.target, 300, 1.0f, 10);

                        var target = activeCamera.target;
                        Rl.DrawLine3D(target, target + new Vector3(2.0f, 0, 0), Color.RED);
                        Rl.DrawLine3D(target, target + new Vector3(0, 0, 2.0f), Color.BLUE);
                        Rl.DrawLine3D(target, target + new Vector3(0, 2.0f, 0), Color.GREEN);

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

                        if (drawPrimitives &&
                            engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationPrimitiveDrawBuffer.Name, out var drawObj) &&
                            engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationMeshAssetRegistry.Name, out var meshObj) &&
                            drawObj is PrimitiveDrawBuffer draw &&
                            meshObj is MeshAssetRegistry meshes)
                        {
                            if (!_emptyBufferWarned && draw.GetSpan().Length == 0)
                            {
                                System.Diagnostics.Debug.WriteLine("[RaylibHostLoop] PrimitiveDrawBuffer is empty on first render frame — no Marker3D performers emitting?");
                                _emptyBufferWarned = true;
                            }
                            long primitiveStart = Stopwatch.GetTimestamp();
                            primitiveRenderer.Draw(draw, meshes, renderDebug.AcceptanceScaleMultiplier);
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
                        if (engine.GlobalContext.TryGetValue(CoreServiceKeys.GroundOverlayBuffer.Name, out var goObj) &&
                            goObj is GroundOverlayBuffer overlays && overlays.Count > 0)
                        {
                            DrawGroundOverlays(overlays);
                        }

                        if (drawDebugDraw &&
                            engine.GlobalContext.TryGetValue(CoreServiceKeys.DebugDrawCommandBuffer.Name, out var ddObj) &&
                            ddObj is DebugDrawCommandBuffer dd)
                        {
                            debugDrawRenderer.Draw(dd);
                        }

                        Rl.EndMode3D();

                        long overlayStart = Stopwatch.GetTimestamp();
                        overlaySkiaRenderer.ResetFrameStats();

                        bool hasUnderlay = overlayScene != null && overlayScene.ContainsLayer(PresentationOverlayLayer.UnderUi);
                        bool hasTopOverlay = overlayScene != null && overlayScene.ContainsLayer(PresentationOverlayLayer.TopMost);
                        bool hasUiLayer = drawSkiaUi && uiRoot.Scene != null;

                        int currentUnderlayVersion = overlayScene?.GetLayerVersion(PresentationOverlayLayer.UnderUi) ?? 0;
                        int currentTopOverlayVersion = overlayScene?.GetLayerVersion(PresentationOverlayLayer.TopMost) ?? 0;
                        bool refreshUnderlay = overlayScene != null && (hasUnderlay || underlayHadContent) &&
                            (currentUnderlayVersion != underlayLayerVersion || hasUnderlay != underlayHadContent);
                        if (refreshUnderlay)
                        {
                            underlayLayer.Clear();
                            if (hasUnderlay)
                            {
                                overlaySkiaRenderer.Render(overlayScene!, underlayLayer.Canvas, PresentationOverlayLayer.UnderUi);
                                underlayLayer.SetHasContent(true);
                            }

                            underlayHadContent = hasUnderlay;
                            underlayLayerVersion = currentUnderlayVersion;
                        }

                        bool refreshUiLayer = hasUiLayer != uiHadContent || (drawSkiaUi && uiRoot.IsDirty);
                        if (refreshUiLayer)
                        {
                            long uiRenderStart = Stopwatch.GetTimestamp();
                            uiLayer.Clear();
                            if (hasUiLayer)
                            {
                                uiRoot.Render(uiLayer.Canvas);
                                uiLayer.SetHasContent(true);
                            }
                            presentationTiming?.ObserveUiRender(ElapsedMs(uiRenderStart));
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
                            overlayLayer.Clear();
                            if (hasTopOverlay)
                            {
                                overlaySkiaRenderer.Render(overlayScene!, overlayLayer.Canvas, PresentationOverlayLayer.TopMost);
                                overlayLayer.SetHasContent(true);
                            }
                            overlayHadContent = hasTopOverlay;
                            topOverlayLayerVersion = currentTopOverlayVersion;
                        }

                        bool hasCompositeContent = hasUnderlay || hasUiLayer || hasTopOverlay;
                        bool refreshComposite = refreshUnderlay || refreshUiLayer || refreshTopOverlay || hasCompositeContent != compositeHadContent;
                        if (refreshComposite)
                        {
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

                            long uiUploadStart = Stopwatch.GetTimestamp();
                            compositeRenderer.UpdateTexture();
                            presentationTiming?.ObserveUiUpload(hasCompositeContent ? ElapsedMs(uiUploadStart) : 0d);
                            compositeHadContent = hasCompositeContent;
                        }
                        else
                        {
                            presentationTiming?.ObserveUiUpload(0d);
                        }

                        if (hasCompositeContent || compositeHadContent)
                        {
                            compositeRenderer.Draw();
                        }

                        screenOverlayBuffer?.Clear();
                        presentationTiming?.ObserveScreenOverlayDraw(
                            ElapsedMs(overlayStart),
                            overlaySkiaRenderer.RebuiltLaneCountLastFrame,
                            overlaySkiaRenderer.CachedTextLayoutCount);

                        Rl.EndDrawing();
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

        private static void ValidateRequiredContextBeforeLoop(GameEngine engine)
        {
            ValidateKey<IScreenProjector>(engine, CoreServiceKeys.ScreenProjector.Name);
            ValidateKey<IScreenRayProvider>(engine, CoreServiceKeys.ScreenRayProvider.Name);
            ValidateKey<RenderDebugState>(engine, CoreServiceKeys.RenderDebugState.Name);
        }

        private static void ValidateKey<T>(GameEngine engine, string key)
        {
            if (!engine.GlobalContext.TryGetValue(key, out var obj) || obj is not T)
            {
                throw new InvalidOperationException($"GlobalContext missing or invalid: {key} expected {typeof(T).FullName}");
            }
        }

        private static RenderDebugState ResolveRenderDebugState(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.RenderDebugState.Name, out var obj) &&
                obj is RenderDebugState state)
            {
                return state;
            }

            throw new InvalidOperationException($"GlobalContext missing or invalid: {CoreServiceKeys.RenderDebugState.Name} expected {typeof(RenderDebugState).FullName}");
        }

        private static bool UpdateInput(UIRoot uiRoot)
        {
            var mousePos = Rl.GetMousePosition();

            uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Move, X = mousePos.X, Y = mousePos.Y });

            if (Rl.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON))
            {
                if (uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Down, X = mousePos.X, Y = mousePos.Y }))
                {
                    _uiPointerCaptured = true;
                }
            }

            if (Rl.IsMouseButtonReleased(MouseButton.MOUSE_LEFT_BUTTON))
            {
                uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Up, X = mousePos.X, Y = mousePos.Y });
                _uiPointerCaptured = false;
            }

            return _uiPointerCaptured;
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
