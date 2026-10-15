using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Ludots.Adapter.Web.Services;
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
            int targetFps = setup.Config.TargetFps <= 0 ? 30 : setup.Config.TargetFps;
            double targetFrameMs = 1000.0 / targetFps;

            WebHostFrameDriver driver = WebHostFrameDriver.Start(setup);
            Log.Info(in LogChannel, $"Web host loop started (target {targetFps} fps, map={setup.Config.StartupMapId})");

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
                        driver.Frame(dt);
                        if (nowMs - lastDiagMs > 5000)
                        {
                            lastDiagMs = nowMs;
                            driver.LogDiagnostics();
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
                driver.Stop();
                Log.Info(in LogChannel, "Web host loop stopped.");
            }
        }
    }

    /// <summary>
    /// One Web host session: composes presentation services and loads the startup map, then advances one host
    /// frame per <see cref="Frame"/>. <see cref="WebHostLoop"/> paces it in real time;
    /// headless tests step it directly.
    /// </summary>
    public sealed class WebHostFrameDriver
    {
        private static readonly LogChannel LogChannel = Log.RegisterChannel("WebHostLoop");

        private readonly WebHostSetup _setup;
        private readonly GameEngine _engine;
        private readonly WebViewController _viewController;
        private readonly PresentationFrameSetupSystem? _presentationFrameSetup;
        private readonly CoreScreenProjector _screenProjector;
        private readonly CoreScreenRayProvider _screenRayProvider;
        private readonly CameraPresenter _cameraPresenter;
        private readonly CameraCullingSystem _cullingSystem;
        private readonly RenderCameraDebugState _renderCameraDebug;
        private readonly WorldHudToScreenSystem? _hudProjection;
        private readonly PresentationExtractor _extractor;

        private WebHostFrameDriver(WebHostSetup setup)
        {
            _setup = setup;
            _engine = setup.Engine;
            var engine = _engine;
            _viewController = setup.ViewController;
            var viewController = _viewController;
            var cameraAdapter = setup.CameraAdapter;
            _presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);
            var presentationFrameSetup = _presentationFrameSetup;

            _screenProjector = new CoreScreenProjector(ClientLocalSeatAccess.ResolveAuthorityCamera(engine), viewController);
            _screenRayProvider = new CoreScreenRayProvider(ClientLocalSeatAccess.ResolveAuthorityCamera(engine), viewController);
            engine.SetService(CoreServiceKeys.ViewController, (IViewController)viewController);
            engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)_screenProjector);
            engine.SetService(
                CoreServiceKeys.ScreenRayProvider,
                (IScreenRayProvider)new Ludots.Core.Client.PresentBindingScreenRayProvider(
                    engine,
                    _screenRayProvider,
                    () => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f));

            _cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter);
            _screenProjector.BindPresenter(_cameraPresenter);
            _screenRayProvider.BindPresenter(_cameraPresenter);
            _screenProjector.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);
            _screenRayProvider.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);

            _cullingSystem = new CameraCullingSystem(
                engine.World,
                ClientLocalSeatAccess.ResolveAuthorityCamera(engine),
                engine.SpatialQueries,
                viewController,
                cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            if (engine.GetService(CoreServiceKeys.PresenterEntityRuntime) is PresenterEntityRuntime presenterInstances)
            {
                _cullingSystem = new CameraCullingSystem(
                    engine.World,
                    ClientLocalSeatAccess.ResolveAuthorityCamera(engine),
                    engine.SpatialQueries,
                    viewController,
                    presenters: presenterInstances,
                    cullingConfig: engine.MergedConfig.Presentation.CameraCulling);
            }
            _cullingSystem.DisarmPresentBindingCulling();
            engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(_cullingSystem);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, _cullingSystem.DebugState);

            _renderCameraDebug = new RenderCameraDebugState();
            engine.SetService(CoreServiceKeys.RenderCameraDebugState, _renderCameraDebug);

            engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));

            if (engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer) is WorldHudBatchBuffer worldHud &&
                engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer) is ScreenHudBatchBuffer screenHud)
            {
                var worldHudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
                _hudProjection = new WorldHudToScreenSystem(
                    engine.World,
                    worldHud,
                    worldHudStrings,
                    _screenProjector,
                    viewController,
                    screenHud,
                    heightmapProvider: () => engine.GetService(CoreServiceKeys.ContinuousHeightmap));
            }

            ValidateRequiredContext(engine);

            _extractor = new PresentationExtractor(engine, cameraAdapter, setup.UiBridge);
        }

        public CameraPresenter CameraPresenter => _cameraPresenter;

        public static WebHostFrameDriver Start(WebHostSetup setup)
        {
            ArgumentNullException.ThrowIfNull(setup);
            var driver = new WebHostFrameDriver(setup);
            driver.StartSession();
            return driver;
        }

        private void StartSession()
        {
            var engine = _engine;
            engine.Start();
            if (string.IsNullOrWhiteSpace(_setup.Config.StartupMapId))
            {
                throw new InvalidOperationException("Invalid launcher bootstrap: 'StartupMapId' cannot be empty.");
            }

            engine.LoadStartupMap();
            Ludots.Core.Client.PresentBindingPresentation.TryEnsurePresentBindings(
                engine,
                _screenProjector,
                _screenRayProvider,
                _viewController.Fov,
                _viewController,
                _cullingSystem);
            engine.SetService(CoreServiceKeys.UiCaptured, false);

            BuildAndSendMeshMap(engine, _setup.Transport);

            _setup.LoopStatus.MarkStarted();
        }

        public void Frame(float dt)
        {
            var engine = _engine;
            bool uiCaptured = _setup.UiBridge.Update(dt);
            engine.SetService(CoreServiceKeys.UiCaptured, uiCaptured);
            Ludots.Core.Client.PresentBindingPresentation.TryEnsurePresentBindings(
                engine,
                _screenProjector,
                _screenRayProvider,
                _viewController.Fov,
                _viewController,
                _cullingSystem);
            Ludots.Core.Client.PresentBindingPresentation.TryArmPresentBindingCullingPasses(
                engine,
                _viewController.Fov,
                _viewController,
                _cullingSystem);
            engine.Tick(dt);

            float cameraAlpha = _presentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
            if (!Ludots.Core.Client.PresentBindingPresentation.TryDrivePresentBindings(
                    engine,
                    _cameraPresenter,
                    _screenProjector,
                    _screenRayProvider,
                    cameraAlpha,
                    _viewController.Fov,
                    drawBinding: null,
                    cameraDebug: _renderCameraDebug,
                    hostView: _viewController,
                    culling: _cullingSystem))
            {
                if (engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats) &&
                    seats != null &&
                    seats.Count > 0)
                {
                    throw new InvalidOperationException(
                        "ClientLocalSeatRegistry is published but PresentBinding pipeline failed to sync.");
                }

                _cameraPresenter.Update(
                    ClientLocalSeatAccess.ResolveFirstPresentBindingCamera(engine),
                    cameraAlpha,
                    _renderCameraDebug);
            }
            _hudProjection?.Update(dt);

            if (_setup.Transport.HasClients)
            {
                var (data, len) = _extractor.CaptureFrame();
                _setup.Transport.BroadcastFrame(data.AsSpan(0, len));
            }
        }

        public void LogDiagnostics()
        {
            var engine = _engine;
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
            Log.Info(in LogChannel, $"[Diag] Primitives={primCount} VisibleSkinned={visibleSkinnedCount} DebugLines={ddLines} Clients={_setup.Transport.ClientCount} Tick={engine.GameSession?.CurrentTick ?? 0}");
        }

        public void Stop()
        {
            if (!_setup.LoopStatus.IsFaulted)
            {
                _setup.LoopStatus.MarkStopped();
            }

            _engine.Stop();
            _setup.Transport.Dispose();
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
