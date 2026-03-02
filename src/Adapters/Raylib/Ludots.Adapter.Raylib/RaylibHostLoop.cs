using System;
using System.Numerics;
using Ludots.Adapter.Raylib.Services;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Components;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Presentation.Camera;
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
            string title = string.IsNullOrWhiteSpace(config.WindowTitle) ? "Ludots Engine (Raylib)" : config.WindowTitle;
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
                Rl.SetExitKey(0); // Disable ESC closing the window (used for game input)
                Rl.SetTargetFPS(targetFps);

                using var uiRenderer = new RaylibSkiaRenderer(screenWidth, screenHeight);
                uiRoot.Resize(screenWidth, screenHeight);

                bool drawTerrain = true;
                bool drawPrimitives = true;
                bool drawDebugDraw = true;
                bool drawSkiaUi = true;
                int navAgentDeltaPerTeam = 0;
                bool navAgentReset = false;

                var initialCamera = new Camera3D
                {
                    position = new Vector3(10.0f, 10.0f, 10.0f),
                    target = new Vector3(0.0f, 0.0f, 0.0f),
                    up = new Vector3(0.0f, 1.0f, 0.0f),
                    fovy = 45.0f,
                    projection = CameraProjection.CAMERA_PERSPECTIVE
                };

                var cameraAdapter = new RaylibCameraAdapter(initialCamera);
                var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter);

                IScreenProjector screenProjector = new RaylibScreenProjector(cameraAdapter);
                engine.GlobalContext[ContextKeys.ScreenProjector] = screenProjector;
                engine.GlobalContext[ContextKeys.ScreenRayProvider] = new RaylibScreenRayProvider(cameraAdapter);

                ValidateRequiredContextBeforeLoop(engine);

                var viewController = new RaylibViewController(cameraAdapter);
                var cullingSystem = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, viewController);
                engine.RegisterPresentationSystem(cullingSystem);

                engine.Start();
                if (string.IsNullOrWhiteSpace(config.StartupMapId))
                {
                    throw new InvalidOperationException("Invalid game.json: 'StartupMapId' cannot be empty.");
                }
                engine.LoadMap(config.StartupMapId);

                var debugDrawRenderer = new RaylibDebugDrawRenderer { PlaneY = 0.35f };
                using var primitiveRenderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Instanced);

                while (!Rl.WindowShouldClose())
                {
                    try
                    {
                        float dt = Rl.GetFrameTime();

                        navAgentDeltaPerTeam = 0;
                        navAgentReset = false;
                        Navigation2DRuntime? navRuntime = null;
                        if (engine.GlobalContext.TryGetValue(ContextKeys.Navigation2DRuntime, out var navObj) && navObj is Navigation2DRuntime nr) navRuntime = nr;
                        ApplyOverlayToggles(ref drawTerrain, ref drawPrimitives, ref drawDebugDraw, ref drawSkiaUi, ref navAgentDeltaPerTeam, ref navAgentReset, navRuntime);
                        if (navAgentDeltaPerTeam != 0)
                        {
                            engine.GlobalContext[ContextKeys.Navigation2DPlayground_AgentDeltaPerTeam] = navAgentDeltaPerTeam;
                        }
                        if (navAgentReset)
                        {
                            engine.GlobalContext[ContextKeys.Navigation2DPlayground_ResetScenario] = true;
                        }

                        bool uiCaptured = drawSkiaUi && UpdateInput(uiRoot);
                        engine.GlobalContext[ContextKeys.UiCaptured] = uiCaptured;
                        engine.Tick(dt);

                        cameraPresenter.Update(engine.GameSession.Camera.State, dt);

                        Rl.BeginDrawing();
                        Rl.ClearBackground(new Raylib_cs.Color(0, 0, 0, 255));

                        var activeCamera = cameraAdapter.Camera;
                        Rl.BeginMode3D(activeCamera);

                        DrawInfiniteGrid(activeCamera.position, 120, 1.0f, 10);

                        var t = activeCamera.target;
                        Rl.DrawLine3D(t, t + new Vector3(2.0f, 0, 0), Color.RED);
                        Rl.DrawLine3D(t, t + new Vector3(0, 0, 2.0f), Color.BLUE);
                        Rl.DrawLine3D(t, t + new Vector3(0, 2.0f, 0), Color.GREEN);

                        if (drawTerrain)
                        {
                            terrainRenderer.Render(engine.VertexMap, activeCamera);
                        }

                        if (drawPrimitives &&
                            engine.GlobalContext.TryGetValue(ContextKeys.PresentationPrimitiveDrawBuffer, out var drawObj) &&
                            engine.GlobalContext.TryGetValue(ContextKeys.PresentationMeshAssetRegistry, out var meshObj) &&
                            drawObj is PrimitiveDrawBuffer draw &&
                            meshObj is MeshAssetRegistry meshes)
                        {
                            primitiveRenderer.Draw(draw, meshes);
                        }

                        // Draw ground overlays (range circles, cones, etc.)
                        if (engine.GlobalContext.TryGetValue(ContextKeys.GroundOverlayBuffer, out var goObj) &&
                            goObj is GroundOverlayBuffer overlays && overlays.Count > 0)
                        {
                            DrawGroundOverlays(overlays);
                        }

                        if (drawDebugDraw &&
                            engine.GlobalContext.TryGetValue(ContextKeys.DebugDrawCommandBuffer, out var ddObj) &&
                            ddObj is DebugDrawCommandBuffer dd)
                        {
                            debugDrawRenderer.Draw(dd);
                        }

                        Rl.EndMode3D();

                        if (engine.GlobalContext.TryGetValue(ContextKeys.PresentationPrimitiveDrawBuffer, out var drawObj2) &&
                            drawObj2 is PrimitiveDrawBuffer draw2)
                        {
                            Rl.DrawText($"Primitives: {draw2.Count} (Dropped: {draw2.DroppedSinceClear})", 10, 10, 20, Raylib_cs.Color.YELLOW);
                        }

                        int wpCount = 0;
                        var wpQueryDesc = new Arch.Core.QueryDescription().WithAll<WorldPositionCm>();
                        var wpQuery = engine.World.Query(in wpQueryDesc);
                        foreach (var chunk in wpQuery) wpCount += chunk.Count;
                        Rl.DrawText($"WorldPositionCm Entities: {wpCount}", 10, 34, 20, Raylib_cs.Color.YELLOW);

                        RenderWorldHud(engine, screenProjector);

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

                        Rl.DrawFPS(screenWidth - 100, 10);
                        Rl.DrawText($"Scale | Grid=1.00m | HexWidth={HexCoordinates.HexWidth:F3}m | RowSpacing={HexCoordinates.RowSpacing:F3}m | HeightScale={terrainRenderer.HeightScale:F2}", 10, screenHeight - 35, 20, Raylib_cs.Color.WHITE);
                        DrawOverlay(drawTerrain, drawPrimitives, drawDebugDraw, drawSkiaUi, engine, primitiveRenderer);
                        DrawInteractionModeHud(engine, screenWidth, screenHeight);

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
            ValidateKey<IScreenProjector>(engine, ContextKeys.ScreenProjector);
            ValidateKey<IScreenRayProvider>(engine, ContextKeys.ScreenRayProvider);
        }

        private static void ValidateKey<T>(GameEngine engine, string key)
        {
            if (!engine.GlobalContext.TryGetValue(key, out var obj) || obj is not T)
            {
                throw new InvalidOperationException($"GlobalContext missing or invalid: {key} expected {typeof(T).FullName}");
            }
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

        private static void RenderWorldHud(GameEngine engine, IScreenProjector projector)
        {
            if (!engine.GlobalContext.TryGetValue(ContextKeys.PresentationWorldHudBuffer, out var hudObj)) return;
            engine.GlobalContext.TryGetValue(ContextKeys.PresentationWorldHudStrings, out var strObj);
            if (hudObj is not WorldHudBatchBuffer hud) return;
            var strings = strObj as Ludots.Core.Presentation.Config.WorldHudStringTable;

            var span = hud.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                Vector2 screen = projector.WorldToScreen(item.WorldPosition);
                float x = screen.X - item.Width * 0.5f;
                float y = screen.Y;

                int ix = (int)x;
                int iy = (int)y;
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

        private static Color ToRaylibColor(Vector4 c)
        {
            byte r = (byte)(Clamp01(c.X) * 255f);
            byte g = (byte)(Clamp01(c.Y) * 255f);
            byte b = (byte)(Clamp01(c.Z) * 255f);
            byte a = (byte)(Clamp01(c.W) * 255f);
            return new Color(r, g, b, a);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static void ApplyOverlayToggles(ref bool drawTerrain, ref bool drawPrimitives, ref bool drawDebugDraw, ref bool drawSkiaUi, ref int navAgentDeltaPerTeam, ref bool navAgentReset, Navigation2DRuntime? navRuntime)
        {
            var mousePos = Rl.GetMousePosition();
            bool clicked = Rl.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON);

            int x = 10;
            int y = 60;
            int w = 190;
            int h = 22;
            int gap = 6;

            if (clicked)
            {
                if (Hit(mousePos, x, y + (h + gap) * 0, w, h)) drawTerrain = !drawTerrain;
                else if (Hit(mousePos, x, y + (h + gap) * 1, w, h)) drawPrimitives = !drawPrimitives;
                else if (Hit(mousePos, x, y + (h + gap) * 2, w, h)) drawDebugDraw = !drawDebugDraw;
                else if (Hit(mousePos, x, y + (h + gap) * 3, w, h)) drawSkiaUi = !drawSkiaUi;
                else if (Hit(mousePos, x, y + (h + gap) * 4, w, h) && navRuntime != null) navRuntime.FlowEnabled = !navRuntime.FlowEnabled;
                else if (Hit(mousePos, x, y + (h + gap) * 5, w, h) && navRuntime != null) navRuntime.FlowDebugEnabled = !navRuntime.FlowDebugEnabled;
                else if (Hit(mousePos, x, y + (h + gap) * 6, w, h) && navRuntime != null) navRuntime.FlowDebugMode = (navRuntime.FlowDebugMode + 1) % 3;
                else if (Hit(mousePos, x, y + (h + gap) * 7, w, h)) navAgentDeltaPerTeam = 500;
                else if (Hit(mousePos, x, y + (h + gap) * 8, w, h)) navAgentDeltaPerTeam = -500;
                else if (Hit(mousePos, x, y + (h + gap) * 9, w, h)) navAgentReset = true;
                else if (Hit(mousePos, x, y + (h + gap) * 10, w, h) && navRuntime != null) navRuntime.FlowIterationsPerTick = Math.Clamp(navRuntime.FlowIterationsPerTick + 512, 0, 131072);
                else if (Hit(mousePos, x, y + (h + gap) * 11, w, h) && navRuntime != null) navRuntime.FlowIterationsPerTick = Math.Clamp(navRuntime.FlowIterationsPerTick - 512, 0, 131072);
            }
        }

        private static void DrawOverlay(bool drawTerrain, bool drawPrimitives, bool drawDebugDraw, bool drawSkiaUi, GameEngine engine, RaylibPrimitiveRenderer primitiveRenderer)
        {
            int x = 10;
            int y = 60;
            int w = 190;
            int h = 22;
            int gap = 6;

            DrawToggle(x, y + (h + gap) * 0, w, h, "Terrain", drawTerrain);
            DrawToggle(x, y + (h + gap) * 1, w, h, "Primitives", drawPrimitives);
            DrawToggle(x, y + (h + gap) * 2, w, h, "DebugDraw", drawDebugDraw);
            DrawToggle(x, y + (h + gap) * 3, w, h, "SkiaUI", drawSkiaUi);

            bool flowEnabled = false;
            bool flowDebug = false;
            int flowDebugMode = 0;
            int flowIters = 0;
            if (engine.GlobalContext.TryGetValue(ContextKeys.Navigation2DRuntime, out var navObj) && navObj is Navigation2DRuntime nr)
            {
                flowEnabled = nr.FlowEnabled;
                flowDebug = nr.FlowDebugEnabled;
                flowDebugMode = nr.FlowDebugMode;
                flowIters = nr.FlowIterationsPerTick;
            }

            DrawToggle(x, y + (h + gap) * 4, w, h, "FlowField", flowEnabled);
            DrawToggle(x, y + (h + gap) * 5, w, h, "FlowDebug", flowDebug);
            DrawButton(x, y + (h + gap) * 6, w, h, flowDebugMode switch { 1 => "DbgMode: Flow0", 2 => "DbgMode: Flow1", _ => "DbgMode: Both" });
            DrawButton(x, y + (h + gap) * 7, w, h, "Agents +500/team");
            DrawButton(x, y + (h + gap) * 8, w, h, "Agents -500/team");
            DrawButton(x, y + (h + gap) * 9, w, h, "Reset positions");
            DrawButton(x, y + (h + gap) * 10, w, h, $"FlowIter +512 ({flowIters})");
            DrawButton(x, y + (h + gap) * 11, w, h, $"FlowIter -512 ({flowIters})");

            int agentsPerTeam = 0;
            int liveTotal = 0;
            if (engine.GlobalContext.TryGetValue(ContextKeys.Navigation2DPlayground_AgentsPerTeam, out var aptObj) && aptObj is int apt) agentsPerTeam = apt;
            if (engine.GlobalContext.TryGetValue(ContextKeys.Navigation2DPlayground_LiveAgentsTotal, out var ltObj) && ltObj is int lt) liveTotal = lt;

            Rl.DrawText($"Agents/team: {agentsPerTeam}", x + 4, y + (h + gap) * 12 + 0, 16, new Color(220, 220, 220, 220));
            Rl.DrawText($"Live agents: {liveTotal}", x + 4, y + (h + gap) * 12 + 18, 16, new Color(220, 220, 220, 220));
            Rl.DrawText($"Instanced: {primitiveRenderer.LastInstancedInstances} ({primitiveRenderer.LastInstancedBatches} batches)", x + 4, y + (h + gap) * 12 + 36, 16, new Color(220, 220, 220, 220));

            int ddLines = 0;
            if (engine.GlobalContext.TryGetValue(ContextKeys.DebugDrawCommandBuffer, out var ddObj) && ddObj is DebugDrawCommandBuffer dd)
            {
                ddLines = dd.Lines.Count;
            }
            Rl.DrawText($"DebugLines: {ddLines}", x + 4, y + (h + gap) * 12 + 54, 16, new Color(220, 220, 220, 220));

            int flowDbgLines = 0;
            if (engine.GlobalContext.TryGetValue(ContextKeys.Navigation2DPlayground_FlowDebugLines, out var fdlObj) && fdlObj is int fdl) flowDbgLines = fdl;
            Rl.DrawText($"FlowDbgLines: {flowDbgLines}", x + 4, y + (h + gap) * 12 + 72, 16, new Color(220, 220, 220, 220));

            // Audit playground counters (render only when present)
            int auditGlobal = engine.GlobalContext.TryGetValue("Audit.GlobalMapLoadedCount", out var agObj) && agObj is int ag ? ag : -1;
            int auditScoped = engine.GlobalContext.TryGetValue("Audit.ScopedMapLoadedCount", out var asObj) && asObj is int asc ? asc : -1;
            int auditNamed = engine.GlobalContext.TryGetValue("Audit.NamedDecoratorCount", out var anObj) && anObj is int an ? an : -1;
            int auditAnchor = engine.GlobalContext.TryGetValue("Audit.AnchorDecoratorCount", out var aaObj) && aaObj is int aa ? aa : -1;
            int auditFactory = engine.GlobalContext.TryGetValue("Audit.FactoryActivationCount", out var afObj) && afObj is int af ? af : -1;

            if (auditGlobal >= 0 || auditScoped >= 0 || auditNamed >= 0 || auditAnchor >= 0 || auditFactory >= 0)
            {
                int auditYBase = y + (h + gap) * 12 + 96;
                Rl.DrawText($"Audit Global: {Math.Max(auditGlobal, 0)}", x + 4, auditYBase, 16, new Color(180, 240, 180, 230));
                Rl.DrawText($"Audit Scoped: {Math.Max(auditScoped, 0)}", x + 4, auditYBase + 18, 16, new Color(180, 240, 180, 230));
                Rl.DrawText($"Audit Named: {Math.Max(auditNamed, 0)}", x + 4, auditYBase + 36, 16, new Color(180, 240, 180, 230));
                Rl.DrawText($"Audit Anchor: {Math.Max(auditAnchor, 0)}", x + 4, auditYBase + 54, 16, new Color(180, 240, 180, 230));
                Rl.DrawText($"Audit Factory: {Math.Max(auditFactory, 0)}", x + 4, auditYBase + 72, 16, new Color(180, 240, 180, 230));
            }
        }

        private static void DrawToggle(int x, int y, int w, int h, string label, bool enabled)
        {
            var col = enabled ? new Color(0, 180, 0, 220) : new Color(140, 0, 0, 220);
            Rl.DrawRectangle(x, y, w, h, new Color(0, 0, 0, 160));
            Rl.DrawRectangleLines(x, y, w, h, col);
            Rl.DrawText($"{label}: {(enabled ? "ON" : "OFF")}", x + 8, y + 3, 16, col);
        }

        private static void DrawButton(int x, int y, int w, int h, string label)
        {
            var col = new Color(90, 150, 220, 220);
            Rl.DrawRectangle(x, y, w, h, new Color(0, 0, 0, 160));
            Rl.DrawRectangleLines(x, y, w, h, col);
            Rl.DrawText(label, x + 8, y + 3, 16, col);
        }

        private static bool Hit(Vector2 p, int x, int y, int w, int h)
        {
            return p.X >= x && p.X <= x + w && p.Y >= y && p.Y <= y + h;
        }

        private static void DrawInteractionModeHud(GameEngine engine, int screenWidth, int screenHeight)
        {
            int x = screenWidth - 360;
            int y = 40;

            string mode = "N/A";
            if (engine.GlobalContext.TryGetValue("MobaDemo.InteractionMode", out var modeObj) && modeObj is string mStr)
                mode = mStr;

            bool isAiming = false;
            if (engine.GlobalContext.TryGetValue("MobaDemo.IsAiming", out var aimObj) && aimObj is bool a)
                isAiming = a;

            string aimAction = "";
            if (engine.GlobalContext.TryGetValue("MobaDemo.AimingAction", out var aaObj) && aaObj is string aa)
                aimAction = aa;

            Rl.DrawRectangle(x - 6, y - 6, 356, 130, new Color(0, 0, 0, 180));
            Rl.DrawRectangleLines(x - 6, y - 6, 356, 130, new Color(0, 200, 255, 200));

            Rl.DrawText($"Mode: {mode}", x, y, 20, new Color(0, 255, 200, 255));
            y += 24;
            Rl.DrawText("[F1] WoW  [F2] LoL  [F3] DotA  [F4] LoL+", x, y, 14, new Color(180, 180, 180, 255));
            y += 20;
            Rl.DrawText("[Q] Damage  [W] Heal  [E] AoE  [R] Zone", x, y, 14, new Color(200, 200, 100, 255));
            y += 20;

            if (isAiming && !string.IsNullOrEmpty(aimAction))
            {
                Rl.DrawText($">>> AIMING: {aimAction} (click to confirm) <<<", x, y, 16, new Color(255, 100, 50, 255));
            }
            else
            {
                Rl.DrawText("Click entity to select, then use skills", x, y, 14, new Color(140, 140, 140, 255));
            }
            y += 20;
            Rl.DrawText("[RightClick] Move  [S] Stop  [LClick] Select", x, y, 14, new Color(140, 140, 140, 255));
        }
    }
}
