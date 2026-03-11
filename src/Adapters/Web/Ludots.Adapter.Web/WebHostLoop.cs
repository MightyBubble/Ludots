using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Ludots.Adapter.Web.Streaming;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Web
{
    public static class WebHostLoop
    {
        private static readonly LogChannel LogChannel = Log.RegisterChannel("WebHostLoop");

        public static void Run(WebHostSetup setup, CancellationToken ct)
        {
            var engine = setup.Engine;
            var config = setup.Config;

            int targetFps = config.TargetFps <= 0 ? 30 : config.TargetFps;
            double targetFrameMs = 1000.0 / targetFps;

            var viewController = setup.ViewController;
            var cameraAdapter = setup.CameraAdapter;
            var screenRayProvider = setup.ScreenRayProvider;

            var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, viewController);
            engine.SetService(CoreServiceKeys.ViewController, (IViewController)viewController);
            engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)screenRayProvider);

            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter);
            screenProjector.BindPresenter(cameraPresenter);

            var cullingSystem = new CameraCullingSystem(
                engine.World, engine.GameSession.Camera, engine.SpatialQueries, viewController);
            engine.RegisterPresentationSystem(cullingSystem);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, cullingSystem.DebugState);

            var renderCameraDebug = new RenderCameraDebugState();
            engine.SetService(CoreServiceKeys.RenderCameraDebugState, renderCameraDebug);

            engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));
            var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);

            WorldHudToScreenSystem hudProjection = null;
            if (engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer) is WorldHudBatchBuffer worldHud &&
                engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer) is ScreenHudBatchBuffer screenHud)
            {
                var worldHudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
                hudProjection = new WorldHudToScreenSystem(
                    engine.World, worldHud, worldHudStrings, screenProjector, viewController, screenHud);
            }

            ValidateRequiredContext(engine);

            var uiSystem = (Services.WebUiSystem)engine.GetService(CoreServiceKeys.UISystem)!;
            var extractor = new PresentationExtractor(engine, cameraAdapter, uiSystem);

            engine.Start();
            if (string.IsNullOrWhiteSpace(config.StartupMapId))
                throw new InvalidOperationException("Invalid launcher bootstrap: 'StartupMapId' cannot be empty.");
            engine.LoadMap(config.StartupMapId);

            BuildAndSendMeshMap(engine, setup.Transport);

            Log.Info(in LogChannel, $"Web host loop started (target {targetFps} fps, map={config.StartupMapId})");

            var sw = Stopwatch.StartNew();
            long lastTickMs = 0;
            long lastDiagMs = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    long nowMs = sw.ElapsedMilliseconds;
                    double elapsedMs = nowMs - lastTickMs;

                    if (elapsedMs < targetFrameMs)
                    {
                        int sleepMs = (int)(targetFrameMs - elapsedMs);
                        if (sleepMs > 1)
                            Thread.Sleep(sleepMs - 1);
                        continue;
                    }

                    lastTickMs = nowMs;
                    float dt = (float)(elapsedMs / 1000.0);
                    dt = Math.Clamp(dt, 0.001f, 0.1f);

                    try
                    {
                        engine.SetService(CoreServiceKeys.UiCaptured, false);
                        engine.Tick(dt);
                        float cameraAlpha = presentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
                        cameraPresenter.Update(engine.GameSession!.Camera, cameraAlpha, renderCameraDebug);
                        hudProjection?.Update(dt);

                        if (setup.Transport.HasClients)
                        {
                            var (data, len) = extractor.CaptureFrame();
                            setup.Transport.BroadcastFrame(data.AsSpan(0, len));
                        }

                        if (nowMs - lastDiagMs > 5000)
                        {
                            lastDiagMs = nowMs;
                            var primBuf = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
                            int primCount = primBuf?.Count ?? 0;
                            var ddBuf = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer);
                            int ddLines = ddBuf?.Lines.Count ?? 0;
                            Log.Info(in LogChannel, $"[Diag] Primitives={primCount} DebugLines={ddLines} Clients={setup.Transport.ClientCount} Tick={engine.GameSession?.CurrentTick ?? 0}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(in LogChannel, $"Unhandled exception in game loop: {ex}");
                    }
                }
            }
            finally
            {
                engine.Stop();
                setup.Transport.Dispose();
                Log.Info(in LogChannel, "Web host loop stopped.");
            }
        }

        private static void BuildAndSendMeshMap(GameEngine engine, WebTransportLayer transport)
        {
            var meshRegistry = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
            if (meshRegistry == null) return;

            var map = new Dictionary<int, string>();
            for (int id = 1; id < 4096; id++)
            {
                string name = meshRegistry.GetName(id);
                if (string.IsNullOrEmpty(name)) break;
                map[id] = name;
            }

            if (map.Count > 0)
                transport.SetMeshMap(map);
        }

        private static void ValidateRequiredContext(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.ScreenProjector) == null)
                throw new InvalidOperationException($"GlobalContext missing: {CoreServiceKeys.ScreenProjector}");
            if (engine.GetService(CoreServiceKeys.ScreenRayProvider) == null)
                throw new InvalidOperationException($"GlobalContext missing: {CoreServiceKeys.ScreenRayProvider}");
        }
    }
}
