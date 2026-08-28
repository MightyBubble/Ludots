using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Ludots.Adapter.Web.Streaming;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.Core.Client;

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
            var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);

            var screenProjector = new CoreScreenProjector(ClientLocalSeatAccess.ResolveAuthorityCamera(engine), viewController);
            var screenRayProvider = new CoreScreenRayProvider(ClientLocalSeatAccess.ResolveAuthorityCamera(engine), viewController);
            engine.SetService(CoreServiceKeys.ViewController, (IViewController)viewController);
            engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)screenProjector);
            engine.SetService(
                CoreServiceKeys.ScreenRayProvider,
                (IScreenRayProvider)new Ludots.Core.Client.PresentBindingScreenRayProvider(
                    engine,
                    screenRayProvider,
                    () => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f));

            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            screenProjector.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);
            screenRayProvider.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);

            var cullingSystem = new CameraCullingSystem(
                engine.World,
                ClientLocalSeatAccess.ResolveAuthorityCamera(engine),
                engine.SpatialQueries,
                viewController,
                cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            if (engine.GetService(CoreServiceKeys.PresenterEntityRuntime) is PresenterEntityRuntime presenterInstances)
            {
                cullingSystem = new CameraCullingSystem(
                    engine.World,
                    ClientLocalSeatAccess.ResolveAuthorityCamera(engine),
                    engine.SpatialQueries,
                    viewController,
                    presenters: presenterInstances,
                    cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            }
            cullingSystem.DisarmPresentBindingCulling();
            engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(cullingSystem);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, cullingSystem.DebugState);

            var renderCameraDebug = new RenderCameraDebugState();
            engine.SetService(CoreServiceKeys.RenderCameraDebugState, renderCameraDebug);

            engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));

            WorldHudToScreenSystem? hudProjection = null;
            if (engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer) is WorldHudBatchBuffer worldHud &&
                engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer) is ScreenHudBatchBuffer screenHud)
            {
                var worldHudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
                hudProjection = new WorldHudToScreenSystem(
                    engine.World, worldHud, worldHudStrings, screenProjector, viewController, screenHud);
            }

            ValidateRequiredContext(engine);

            var extractor = new PresentationExtractor(engine, cameraAdapter, setup.UiBridge);

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
            engine.SetService(CoreServiceKeys.UiCaptured, false);

            BuildAndSendMeshMap(engine, setup.Transport);

            setup.LoopStatus.MarkStarted();
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
                        {
                            Thread.Sleep(sleepMs - 1);
                        }

                        continue;
                    }

                    lastTickMs = nowMs;
                    float dt = (float)(elapsedMs / 1000.0);
                    dt = Math.Clamp(dt, 0.001f, 0.1f);

                    try
                    {
                        bool uiCaptured = setup.UiBridge.Update(dt);
                        engine.SetService(CoreServiceKeys.UiCaptured, uiCaptured);
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
                        engine.Tick(dt);

                        float cameraAlpha = presentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
                        if (!Ludots.Core.Client.PresentBindingPresentation.TryDrivePresentBindings(
                                engine,
                                cameraPresenter,
                                screenProjector,
                                screenRayProvider,
                                cameraAlpha,
                                viewController.Fov,
                                drawBinding: null,
                                cameraDebug: renderCameraDebug,
                                hostView: viewController,
                                culling: cullingSystem))
                        {
                            if (engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out Ludots.Core.Client.ClientLocalSeatRegistry? seats) &&
                                seats != null &&
                                seats.Count > 0)
                            {
                                throw new InvalidOperationException(
                                    "ClientLocalSeatRegistry is published but PresentBinding pipeline failed to sync.");
                            }

                            cameraPresenter.Update(
                                ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine),
                                cameraAlpha,
                                renderCameraDebug);
                        }
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
                            var skinnedBuf = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
                            int visibleSkinnedCount = 0;
                            if (skinnedBuf != null)
                            {
                                var skinnedSpan = skinnedBuf.GetSpan();
                                for (int i = 0; i < skinnedSpan.Length; i++)
                                {
                                    if (skinnedSpan[i].Visibility == VisualVisibility.Visible)
                                    {
                                        visibleSkinnedCount++;
                                    }
                                }
                            }
                            var ddBuf = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer);
                            int ddLines = ddBuf?.Lines.Count ?? 0;
                            Log.Info(in LogChannel, $"[Diag] Primitives={primCount} VisibleSkinned={visibleSkinnedCount} DebugLines={ddLines} Clients={setup.Transport.ClientCount} Tick={engine.GameSession?.CurrentTick ?? 0}");
                        }
                    }
                    catch (Exception ex)
                    {
                        setup.LoopStatus.MarkFaulted(ex);
                        Log.Error(in LogChannel, $"Unhandled exception in game loop: {ex}");
                        throw;
                    }
                }
            }
            finally
            {
                if (!setup.LoopStatus.IsFaulted)
                {
                    setup.LoopStatus.MarkStopped();
                }

                engine.Stop();
                setup.Transport.Dispose();
                Log.Info(in LogChannel, "Web host loop stopped.");
            }
        }

        private static void BuildAndSendMeshMap(GameEngine engine, WebTransportLayer transport)
        {
            var meshRegistry = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
            if (meshRegistry == null)
            {
                return;
            }

            var map = new Dictionary<int, string>();
            for (int id = 1; id < 4096; id++)
            {
                string name = meshRegistry.GetName(id);
                if (string.IsNullOrEmpty(name))
                {
                    break;
                }

                map[id] = name;
            }

            if (map.Count > 0)
            {
                transport.SetMeshMap(map);
            }
        }

        private static void ValidateRequiredContext(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.ScreenProjector) == null)
            {
                throw new InvalidOperationException($"GlobalContext missing: {CoreServiceKeys.ScreenProjector}");
            }

            if (engine.GetService(CoreServiceKeys.ScreenRayProvider) == null)
            {
                throw new InvalidOperationException($"GlobalContext missing: {CoreServiceKeys.ScreenRayProvider}");
            }
        }
    }
}
