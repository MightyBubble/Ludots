using System;
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
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
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

        public static void Run(RaylibHostSetup setup)
        {
            var engine = setup.Engine;
            var config = setup.Config;
            var uiRoot = setup.UiRoot;

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

                using var uiRenderer = new RaylibSkiaRenderer(screenWidth, screenHeight);
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
                var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter);

                var viewController = new RaylibViewController(cameraAdapter);
                engine.GlobalContext[CoreServiceKeys.ViewController.Name] = viewController;

                var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, viewController);
                screenProjector.BindPresenter(cameraPresenter);
                var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, viewController);
                screenRayProvider.BindPresenter(cameraPresenter);
                engine.GlobalContext[CoreServiceKeys.ScreenProjector.Name] = screenProjector;
                engine.GlobalContext[CoreServiceKeys.ScreenRayProvider.Name] = screenRayProvider;

                var cullingSystem = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, viewController);
                engine.RegisterPresentationSystem(cullingSystem);
                engine.GlobalContext[CoreServiceKeys.CameraCullingDebugState.Name] = cullingSystem.DebugState;

                var renderCameraDebug = new RenderCameraDebugState();
                engine.GlobalContext[CoreServiceKeys.RenderCameraDebugState.Name] = renderCameraDebug;

                engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));

                if (engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationWorldHudBuffer.Name, out var whObj) && whObj is WorldHudBatchBuffer worldHud &&
                    engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationScreenHudBuffer.Name, out var shObj) && shObj is ScreenHudBatchBuffer screenHud)
                {
                    engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationWorldHudStrings.Name, out var strObj);
                    var worldHudStrings = strObj as Ludots.Core.Presentation.Config.WorldHudStringTable;
                    engine.RegisterPresentationSystem(new WorldHudToScreenSystem(engine.World, worldHud, worldHudStrings, screenProjector, viewController, screenHud));
                }

                ValidateRequiredContextBeforeLoop(engine);

                engine.Start();
                if (string.IsNullOrWhiteSpace(config.StartupMapId))
                {
                    throw new InvalidOperationException("Invalid game.json: 'StartupMapId' cannot be empty.");
                }
                engine.LoadMap(config.StartupMapId);
                cameraPresenter.Update(engine.GameSession.Camera.State, 0f, renderCameraDebug);

                var debugDrawRenderer = new RaylibDebugDrawRenderer { PlaneY = 0.35f };
                using var primitiveRenderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate, engine.VFS);

                int lastW = screenWidth;
                int lastH = screenHeight;

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
                            uiRenderer.Resize(w, h);
                            uiRoot.Resize(w, h);
                        }

                        float dt = Rl.GetFrameTime();
                        var renderDebug = ResolveRenderDebugState(engine);
                        bool drawTerrain = renderDebug.DrawTerrain;
                        bool drawPrimitives = renderDebug.DrawPrimitives;
                        bool drawDebugDraw = renderDebug.DrawDebugDraw;
                        bool drawSkiaUi = renderDebug.DrawSkiaUi;

                        bool uiCaptured = drawSkiaUi && UpdateInput(uiRoot);
                        engine.GlobalContext[CoreServiceKeys.UiCaptured.Name] = uiCaptured;
                        engine.Tick(dt);
                        uiRoot.Update(dt);

                        cameraPresenter.Update(engine.GameSession.Camera.State, dt, renderCameraDebug);

                        Rl.BeginDrawing();
                        Rl.ClearBackground(new Raylib_cs.Color(0, 0, 0, 255));

                        var activeCamera = cameraAdapter.Camera;
                        Rl.BeginMode3D(activeCamera);

                        // 锚定到 target，网格以观察点为中心；halfCount 越大边界越远
                        DrawInfiniteGrid(activeCamera.target, 300, 1.0f, 10);

                        var t = activeCamera.target;
                        Rl.DrawLine3D(t, t + new Vector3(2.0f, 0, 0), Color.RED);
                        Rl.DrawLine3D(t, t + new Vector3(0, 0, 2.0f), Color.BLUE);
                        Rl.DrawLine3D(t, t + new Vector3(0, 2.0f, 0), Color.GREEN);

                        if (drawTerrain)
                        {
                            terrainRenderer.Render(engine.VertexMap, activeCamera);
                        }

                        if (drawPrimitives &&
                            engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationPrimitiveDrawBuffer.Name, out var drawObj) &&
                            engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationMeshAssetRegistry.Name, out var meshObj) &&
                            drawObj is PrimitiveDrawBuffer draw &&
                            meshObj is MeshAssetRegistry meshes)
                        {
                            primitiveRenderer.Draw(draw, meshes, renderDebug.AcceptanceScaleMultiplier);
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

                        DrawScreenHud(engine);

                        if (drawSkiaUi && uiRoot.IsDirty)
                        {
                            uiRenderer.Canvas.Clear(SKColors.Transparent);
                            uiRoot.Render(uiRenderer.Canvas);
                            uiRenderer.UpdateTexture();
                        }
                        if (drawSkiaUi)
                        {
                            uiRenderer.Draw();
                        }

                        var res = viewController.Resolution;
                        DrawScreenOverlays(engine, (int)res.X, (int)res.Y);

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

        private static void DrawScreenHud(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationScreenHudBuffer.Name, out var hudObj)) return;
            engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationWorldHudStrings.Name, out var strObj);
            if (hudObj is not ScreenHudBatchBuffer hud) return;
            var strings = strObj as Ludots.Core.Presentation.Config.WorldHudStringTable;

            var span = hud.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                int ix = (int)item.ScreenX;
                int iy = (int)item.ScreenY;
                int iw = (int)item.Width;
                int ih = (int)item.Height;

                if (item.Kind == WorldHudItemKind.Bar)
                {
                    var bg = ToRaylibColor(item.Color0);
                    var fg = ToRaylibColor(item.Color1);

                    Rl.DrawRectangle(ix, iy, iw, ih, bg);
                    int fw = (int)(iw * item.Value0);
                    Rl.DrawRectangle(ix, iy, fw, ih, fg);
                    Rl.DrawRectangleLines(ix, iy, iw, ih, new Color(0, 0, 0, 255));
                    continue;
                }

                if (item.Kind == WorldHudItemKind.Text)
                {
                    int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                    var col = ToRaylibColor(item.Color0);

                    string? text = null;
                    if (item.Id0 != 0 && strings != null)
                    {
                        text = strings.TryGet(item.Id0);
                    }
                    else
                    {
                        var mode = (Ludots.Core.Presentation.Hud.WorldHudValueMode)item.Id1;
                        if (mode == Ludots.Core.Presentation.Hud.WorldHudValueMode.AttributeCurrentOverBase)
                        {
                            text = $"{(int)item.Value0}/{(int)item.Value1}";
                        }
                        else if (mode == Ludots.Core.Presentation.Hud.WorldHudValueMode.AttributeCurrent)
                        {
                            text = $"{(int)item.Value0}";
                        }
                        else if (mode == Ludots.Core.Presentation.Hud.WorldHudValueMode.Constant)
                        {
                            text = $"{item.Value0}";
                        }
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        Rl.DrawText(text, ix, iy, fontSize, col);
                    }
                }
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
                    case GroundOverlayShape.Ring:
                        DrawGroundCircle(in item); // ring uses same path for now
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

        private static void DrawGroundLine(in GroundOverlayItem item)
        {
            float dx = MathF.Cos(item.Rotation) * item.Length;
            float dz = MathF.Sin(item.Rotation) * item.Length;
            var a = item.Center;
            var b = new Vector3(a.X + dx, a.Y, a.Z + dz);
            Rl.DrawLine3D(a, b, ToRaylibColor(item.BorderColor));
        }

        private static Color ToRaylibColor(Vector4 c) => RaylibColorUtil.ToRaylibColor(in c);

        private static void DrawScreenOverlays(GameEngine engine, int screenWidth, int screenHeight)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var bufferObj)) return;
            if (bufferObj is not ScreenOverlayBuffer buffer) return;

            const int maxOverlayDim = 4096;

            var span = buffer.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                switch (item.Kind)
                {
                    case ScreenOverlayItemKind.Text:
                    {
                        string? text = buffer.GetString(item.StringId);
                        if (!string.IsNullOrEmpty(text))
                        {
                            int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                            Rl.DrawText(text, item.X, item.Y, fontSize, ToRaylibColor(item.Color));
                        }
                        break;
                    }
                    case ScreenOverlayItemKind.Rect:
                    {
                        if (item.Width <= 0 || item.Height <= 0 || item.Width > maxOverlayDim || item.Height > maxOverlayDim) break;
                        Rl.DrawRectangle(item.X, item.Y, item.Width, item.Height, ToRaylibColor(item.BackgroundColor));
                        if (item.Color.W > 0.01f)
                        {
                            Rl.DrawRectangleLines(item.X, item.Y, item.Width, item.Height, ToRaylibColor(item.Color));
                        }
                        break;
                    }
                }
            }

            buffer.Clear();
        }
    }
}
