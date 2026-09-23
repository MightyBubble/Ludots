using System;
using System.Numerics;
using Ludots.Adapter.WebGpu.Services;
using Ludots.Client.WebGpu.Input;
using Ludots.Client.WebGpu.Rendering;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.MassNavigation.Presentation;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using WgpuColor = Silk.NET.WebGPU.Color;

namespace Ludots.Adapter.WebGpu
{
    public static class WebGpuHostLoop
    {
        private static readonly LogChannel LogChannel = Log.RegisterChannel("WebGpuHostLoop");
        private static readonly WebGpuInstanceDraw[] InstanceScratch = new WebGpuInstanceDraw[WebGpuWorldRenderer.MaxInstances];
        private static readonly MassNavigationPresentationProxyItem[] MassNavigationProxyScratch =
            new MassNavigationPresentationProxyItem[WebGpuWorldRenderer.MaxInstances];

        private static bool _uiPointerCaptured;
        private static PointerButton? _uiCapturedPointerButton;
        private static bool _hasLastUiPointerMove;
        private static float _lastUiPointerMoveX;
        private static float _lastUiPointerMoveY;
        private static bool _emptyBufferWarned;

        public static bool ShouldCaptureWorldPointer(
            bool pointerCaptured,
            bool wheelCaptured,
            bool inputHandled)
        {
            return pointerCaptured || wheelCaptured || inputHandled;
        }

        public static void Run(WebGpuHostSetup setup)
        {
            ResetInputState();

            GameEngine engine = setup.Engine;
            var config = setup.Config;
            UIRoot uiRoot = setup.UiRoot;
            WebGpuCameraAdapter cameraAdapter = setup.CameraAdapter;

            int screenWidth = config.WindowWidth <= 0 ? 1280 : config.WindowWidth;
            int screenHeight = config.WindowHeight <= 0 ? 720 : config.WindowHeight;
            string title = string.IsNullOrWhiteSpace(config.WindowTitle) ? "Ludots WebGPU" : config.WindowTitle;
            int targetFps = config.TargetFps == 0 ? 0 : (config.TargetFps < 0 ? 60 : config.TargetFps);

            var options = WindowOptions.Default;
            options.API = GraphicsAPI.None;
            options.Size = new Vector2D<int>(screenWidth, screenHeight);
            options.Title = title;
            options.IsVisible = true;
            options.ShouldSwapAutomatically = false;
            options.IsContextControlDisabled = true;
            options.FramesPerSecond = targetFps;
            options.UpdatesPerSecond = targetFps;
            options.WindowBorder = config.WindowResizable ? WindowBorder.Resizable : WindowBorder.Fixed;

            IWindow window = Window.Create(options);
            WebGpuRuntime? runtime = null;
            WebGpuInputBackend? inputBackend = null;
            WebGpuUiRasterLayer? uiRasterLayer = null;
            WebGpuViewController? viewController = null;
            CameraPresenter? cameraPresenter = null;
            WorldHudToScreenSystem? hudProjection = null;
            PresentationOverlaySceneBuilder? overlaySceneBuilder = null;
            PresentationOverlayScene? overlayScene = null;
            ScreenOverlayBuffer? screenOverlayBuffer = null;
            RenderCameraDebugState? renderCameraDebug = null;
            var presentationTiming = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);
            long lastDiagMs = 0;
            int frameIndex = 0;
            int autoExitFrame = ReadPositiveIntEnvironment("LUDOTS_WEBGPU_AUTO_EXIT_FRAME");
            bool loopStarted = false;

            void OnLoad()
            {
                runtime = new WebGpuRuntime(window);
                runtime.Initialize();
                uiRasterLayer = new WebGpuUiRasterLayer(setup.SkiaRenderer);

                inputBackend = new WebGpuInputBackend(window);
                inputBackend.Attach();
                WebGpuHostComposer.RegisterInputRuntime(engine, config, inputBackend);

                viewController = new WebGpuViewController(cameraAdapter, window.Size.X, window.Size.Y);
                engine.SetService(CoreServiceKeys.ViewController, (IViewController)viewController);
                uiRoot.Resize(window.Size.X, window.Size.Y);

                cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, presentationTiming);
                var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, viewController);
                var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, viewController);
                screenProjector.BindPresenter(cameraPresenter);
                screenRayProvider.BindPresenter(cameraPresenter);
                screenProjector.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);
                screenRayProvider.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);
                engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)screenProjector);
                engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)screenRayProvider);

                var cullingFocusOverride = new CameraCullingFocusOverride();
                engine.SetService(CoreServiceKeys.CameraCullingFocusOverride, cullingFocusOverride);

                PerformerEntityRuntime? performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime);
                var cullingSystem = new CameraCullingSystem(
                    engine.World,
                    engine.GameSession.Camera,
                    engine.SpatialQueries,
                    viewController,
                    loadedChunks: null,
                    focusOverride: cullingFocusOverride,
                    performers: performers,
                    timingDiagnostics: presentationTiming,
                    cullingConfig: config.Presentation.CameraCulling);
                engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(cullingSystem);
                engine.SetService(CoreServiceKeys.CameraCullingDebugState, cullingSystem.DebugState);

                renderCameraDebug = new RenderCameraDebugState();
                engine.SetService(CoreServiceKeys.RenderCameraDebugState, renderCameraDebug);
                engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));

                WorldHudBatchBuffer worldHud = RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer);
                ScreenHudBatchBuffer screenHud = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
                WorldHudStringTable worldHudStrings = RequireService(engine, CoreServiceKeys.PresentationWorldHudStrings);
                PresentationTextCatalog textCatalog = RequireService(engine, CoreServiceKeys.PresentationTextCatalog);
                PresentationTextLocaleSelection localeSelection = RequireService(engine, CoreServiceKeys.PresentationTextLocaleSelection);
                screenOverlayBuffer = RequireService(engine, CoreServiceKeys.ScreenOverlayBuffer);
                MinimapScreenMarkerBuffer minimapScreenMarkers = RequireService(engine, CoreServiceKeys.MinimapScreenMarkerBuffer);
                hudProjection = new WorldHudToScreenSystem(
                    engine.World,
                    worldHud,
                    worldHudStrings,
                    screenProjector,
                    viewController,
                    screenHud,
                    presentationTiming,
                    engine.GetService(CoreServiceKeys.CameraCullingDebugState));
                overlaySceneBuilder = new PresentationOverlaySceneBuilder(
                    screenHud,
                    worldHudStrings,
                    textCatalog,
                    localeSelection,
                    screenOverlayBuffer,
                    minimapScreenMarkers);
                overlayScene = new PresentationOverlayScene(screenHud.Capacity + ScreenOverlayBuffer.MaxItems + minimapScreenMarkers.Capacity);

                WebGpuHostComposer.ValidateRequiredContextBeforeLoop(engine);

                engine.Start();
                if (string.IsNullOrWhiteSpace(config.StartupMapId))
                {
                    throw new InvalidOperationException("Invalid launcher bootstrap: 'StartupMapId' cannot be empty.");
                }

                engine.LoadStartupMap();
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                loopStarted = true;
                Log.Info(in LogChannel, $"WebGPU host loop started ({window.Size.X}x{window.Size.Y}, map={config.StartupMapId})");
                Log.Info(in LogChannel, "UI pass status: retained UiScene is composited through Skia raster upload into the WebGPU frame.");
            }

            void OnUpdate(double delta)
            {
                if (!loopStarted || runtime == null || inputBackend == null || viewController == null || cameraPresenter == null)
                {
                    return;
                }

                float dt = (float)Math.Clamp(delta, 0.001, 0.1);
                inputBackend.BeginFrame();

                Vector2 mouse = inputBackend.GetMousePosition();
                float wheel = inputBackend.GetMouseWheel();
                bool leftDown = inputBackend.GetButton("<Mouse>/leftButton");
                bool rightDown = inputBackend.GetButton("<Mouse>/rightButton");
                bool middleDown = inputBackend.GetButton("<Mouse>/middleButton");

                UiInputFrameResult uiInput = RouteUiInput(
                    uiRoot,
                    mouse.X,
                    mouse.Y,
                    wheel,
                    leftDown,
                    rightDown,
                    middleDown);
                bool uiCaptured = ShouldCaptureWorldPointer(uiInput.PointerCaptured, uiInput.WheelCaptured, uiInput.Handled);
                engine.SetService(CoreServiceKeys.UiCaptured, uiCaptured);

                uiRoot.Update(dt);
                engine.Tick(dt);

                float cameraAlpha = presentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
                cameraPresenter.Update(
                    engine.GameSession!.Camera,
                    cameraAlpha,
                    renderCameraDebug ?? throw new InvalidOperationException("RenderCameraDebugState was not initialized before update."));
                if (hudProjection == null)
                {
                    throw new InvalidOperationException("WebGPU HUD projection was not initialized before frame update.");
                }

                hudProjection.Update(dt);
                if (overlaySceneBuilder == null || overlayScene == null)
                {
                    throw new InvalidOperationException("WebGPU overlay scene builder was not initialized before frame update.");
                }

                overlaySceneBuilder.Build(overlayScene);

                long nowMs = Environment.TickCount64;
                if (nowMs - lastDiagMs > 2000)
                {
                    lastDiagMs = nowMs;
                    int primCount = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)?.Count ?? 0;
                    Log.Info(
                        in LogChannel,
                        $"[Diag] frame={frameIndex} tick={engine.GameSession?.CurrentTick ?? 0} primitives={primCount} uiCaptured={uiCaptured} camera=({cameraAdapter.CurrentState.Position.X:F1},{cameraAdapter.CurrentState.Position.Y:F1},{cameraAdapter.CurrentState.Position.Z:F1})");
                }
            }

            void OnRender(double _)
            {
                if (!loopStarted || runtime == null || viewController == null || cameraPresenter == null)
                {
                    return;
                }

                frameIndex++;
                int instanceCount = CollectPrimitiveInstances(engine, InstanceScratch, out bool hadEmptyBuffer);
                MassNavigationPresentationProxyStats massNavigationProxy = AppendMassNavigationProxyInstances(
                    engine,
                    InstanceScratch,
                    instanceCount);
                instanceCount += massNavigationProxy.Written;
                int referenceCount = AppendReferenceInstances(
                    cameraPresenter.SmoothedRenderState,
                    InstanceScratch,
                    instanceCount);
                instanceCount += referenceCount;
                if (hadEmptyBuffer && !_emptyBufferWarned)
                {
                    _emptyBufferWarned = true;
                    Log.Info(
                        in LogChannel,
                        massNavigationProxy.Written > 0
                            ? "PresentationPrimitiveDrawBuffer is empty; WebGPU is drawing adapter-observable MassNavigation proxies."
                            : "PresentationPrimitiveDrawBuffer is empty on first render frames - clear pass still runs with explicit empty-world diagnostic.");
                }

                Matrix4x4 viewProjection = BuildViewProjection(
                    cameraPresenter.SmoothedRenderState,
                    viewController.AspectRatio);
                var clear = new WgpuColor
                {
                    R = 0.08,
                    G = 0.10,
                    B = 0.14,
                    A = 1.0
                };

                // Tint slightly when we have content so the player can tell the pass is alive.
                if (instanceCount > 0)
                {
                    clear.G = 0.14;
                }

                ReadOnlySpan<byte> uiPixels = ReadOnlySpan<byte>.Empty;
                int uiWidth = 0;
                int uiHeight = 0;
                int uiBytesPerRow = 0;
                bool hasUi = false;
                WebGpuUiRasterStats uiStats = default;
                if (uiRasterLayer == null)
                {
                    throw new InvalidOperationException("WebGPU UI raster layer was not initialized before frame render.");
                }

                hasUi = uiRasterLayer.TryRender(overlayScene, uiRoot, window.Size.X, window.Size.Y, out uiStats);
                if (hasUi)
                {
                    uiPixels = uiRasterLayer.UploadBytes;
                    uiWidth = uiRasterLayer.Width;
                    uiHeight = uiRasterLayer.Height;
                    uiBytesPerRow = uiRasterLayer.BytesPerRow;
                }
                screenOverlayBuffer?.Clear();

                runtime.PresentClearWorldAndUi(
                    in clear,
                    InstanceScratch.AsSpan(0, instanceCount),
                    new Matrix4x4Gpu(viewProjection),
                    uiPixels,
                    uiWidth,
                    uiHeight,
                    uiBytesPerRow,
                    hasUi,
                    out string? frameDiagnostic);

                if (frameIndex == 1 || frameIndex % 120 == 0)
                {
                    Log.Info(
                        in LogChannel,
                        $"[Render] frame={frameIndex} referenceInstances={referenceCount} massNavigationAgents={massNavigationProxy.AgentCount} massNavigationMarkers={massNavigationProxy.MarkerCount} massNavigationProxy={massNavigationProxy.Written} massNavigationDropped={massNavigationProxy.Dropped} uiOverlayItems={uiStats.OverlayItemCount} uiUnder={uiStats.UnderUiItemCount} uiTop={uiStats.TopMostItemCount} uiMinimapMarkers={uiStats.MinimapMarkerCount} uiOverlayDropped={uiStats.Dropped} {frameDiagnostic}");
                }

                if (autoExitFrame > 0 && frameIndex >= autoExitFrame)
                {
                    Log.Info(in LogChannel, $"WebGPU auto exit reached frame={frameIndex}.");
                    window.Close();
                    Environment.Exit(0);
                }
            }

            void OnResize(Vector2D<int> size)
            {
                if (size.X <= 0 || size.Y <= 0)
                {
                    return;
                }

                runtime?.Resize(size);
                uiRasterLayer?.Resize(size.X, size.Y);
                viewController?.SetResolution(size.X, size.Y);
                uiRoot.Resize(size.X, size.Y);
                config.WindowWidth = size.X;
                config.WindowHeight = size.Y;
            }

            void OnClosing()
            {
                try
                {
                    engine.Stop();
                }
                finally
                {
                    uiRasterLayer?.Dispose();
                    uiRasterLayer = null;
                    inputBackend?.DisposeInput();
                    runtime?.Dispose();
                    runtime = null;
                }
            }

            window.Load += OnLoad;
            window.Update += OnUpdate;
            window.Render += OnRender;
            window.FramebufferResize += OnResize;
            window.Closing += OnClosing;

            window.Run();
        }

        internal static int CollectPrimitiveInstances(
            GameEngine engine,
            WebGpuInstanceDraw[] destination,
            out bool hadEmptyBuffer)
        {
            hadEmptyBuffer = false;
            var buffer = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            if (buffer == null)
            {
                hadEmptyBuffer = true;
                return 0;
            }

            var span = buffer.GetSpan();
            int written = 0;
            for (int i = 0; i < span.Length && written < destination.Length; i++)
            {
                ref readonly PrimitiveDrawItem item = ref span[i];
                if (item.Visibility != VisualVisibility.Visible)
                {
                    continue;
                }

                destination[written++] = new WebGpuInstanceDraw
                {
                    Position = item.Position,
                    Scale = SanitizeScale(item.Scale),
                    Color = item.Color.W <= 0f
                        ? new Vector4(0.85f, 0.85f, 0.9f, 1f)
                        : item.Color
                };
            }

            if (written == 0)
            {
                hadEmptyBuffer = true;
            }

            return written;
        }

        private static Vector3 SanitizeScale(Vector3 scale)
        {
            float x = MathF.Abs(scale.X) < 1e-4f ? 1f : scale.X;
            float y = MathF.Abs(scale.Y) < 1e-4f ? 1f : scale.Y;
            float z = MathF.Abs(scale.Z) < 1e-4f ? 1f : scale.Z;
            return new Vector3(x, y, z);
        }

        private static MassNavigationPresentationProxyStats AppendMassNavigationProxyInstances(
            GameEngine engine,
            WebGpuInstanceDraw[] destination,
            int startIndex)
        {
            int capacity = Math.Max(0, Math.Min(destination.Length - startIndex, MassNavigationProxyScratch.Length));
            MassNavigationPresentationProxyStats stats = MassNavigationPresentationProxyCollector.Collect(
                engine.World,
                MassNavigationProxyScratch.AsSpan(0, capacity));
            for (int i = 0; i < stats.Written; i++)
            {
                MassNavigationPresentationProxyItem item = MassNavigationProxyScratch[i];
                destination[startIndex + i] = new WebGpuInstanceDraw
                {
                    Position = item.Position,
                    Scale = item.Scale,
                    Color = item.Color
                };
            }

            return stats;
        }

        private static int AppendReferenceInstances(
            in CameraRenderState3D camera,
            WebGpuInstanceDraw[] destination,
            int startIndex)
        {
            if (destination.Length - startIndex < 3 ||
                !float.IsFinite(camera.Target.X) ||
                !float.IsFinite(camera.Target.Y) ||
                !float.IsFinite(camera.Target.Z))
            {
                return 0;
            }

            Vector3 target = camera.Target;
            destination[startIndex] = new WebGpuInstanceDraw
            {
                Position = target + new Vector3(1.25f, 0.15f, 0f),
                Scale = new Vector3(2.5f, 0.3f, 0.3f),
                Color = new Vector4(1f, 0.16f, 0.12f, 1f)
            };
            destination[startIndex + 1] = new WebGpuInstanceDraw
            {
                Position = target + new Vector3(0f, 1.25f, 0f),
                Scale = new Vector3(0.3f, 2.5f, 0.3f),
                Color = new Vector4(0.1f, 0.9f, 0.25f, 1f)
            };
            destination[startIndex + 2] = new WebGpuInstanceDraw
            {
                Position = target + new Vector3(0f, 0.15f, 1.25f),
                Scale = new Vector3(0.3f, 0.3f, 2.5f),
                Color = new Vector4(0.18f, 0.45f, 1f, 1f)
            };

            return 3;
        }

        private static Matrix4x4 BuildViewProjection(in CameraRenderState3D camera, float aspect)
        {
            float safeAspect = aspect <= 1e-4f ? 1f : aspect;
            Matrix4x4 view = Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up);
            CameraClipPlanes clipPlanes = CameraViewportUtil.ResolveClipPlanes(in camera);
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
                camera.FovYDeg * (MathF.PI / 180f),
                safeAspect,
                clipPlanes.NearMeters,
                clipPlanes.FarMeters);
            return view * projection;
        }

        private readonly record struct UiInputFrameResult(bool Handled, bool PointerCaptured, bool WheelCaptured);

        private static UiInputFrameResult RouteUiInput(
            UIRoot uiRoot,
            float mouseX,
            float mouseY,
            float mouseWheel,
            bool leftDown,
            bool rightDown,
            bool middleDown)
        {
            bool uiInputHandled = false;
            bool uiWheelCaptured = false;
            Vector2 mousePos = new Vector2(mouseX, mouseY);

            UiNode? hitNode = _uiPointerCaptured ? null : uiRoot.Scene?.HitTest(mousePos.X, mousePos.Y);
            bool hitInteractiveUi = !_uiPointerCaptured && IsInteractiveUiNode(hitNode);

            bool leftPressed = leftDown && !_leftWasDown;
            bool rightPressed = rightDown && !_rightWasDown;
            bool middlePressed = middleDown && !_middleWasDown;
            bool leftReleased = !leftDown && _leftWasDown;
            bool rightReleased = !rightDown && _rightWasDown;
            bool middleReleased = !middleDown && _middleWasDown;
            _leftWasDown = leftDown;
            _rightWasDown = rightDown;
            _middleWasDown = middleDown;

            if (_uiPointerCaptured)
            {
                if ((_uiCapturedPointerButton == PointerButton.Left && leftReleased) ||
                    (_uiCapturedPointerButton == PointerButton.Right && rightReleased) ||
                    (_uiCapturedPointerButton == PointerButton.Middle && middleReleased))
                {
                    uiInputHandled |= uiRoot.HandleInput(new PointerEvent
                    {
                        DeviceType = InputDeviceType.Mouse,
                        PointerId = 0,
                        Action = PointerAction.Up,
                        Button = _uiCapturedPointerButton ?? PointerButton.Left,
                        X = mousePos.X,
                        Y = mousePos.Y
                    });
                    _uiPointerCaptured = false;
                    _uiCapturedPointerButton = null;
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
            if (shouldRouteMouseDownToUi)
            {
                if (TryRouteMouseDown(uiRoot, mousePos, leftPressed, PointerButton.Left, ref uiInputHandled) ||
                    TryRouteMouseDown(uiRoot, mousePos, rightPressed, PointerButton.Right, ref uiInputHandled) ||
                    TryRouteMouseDown(uiRoot, mousePos, middlePressed, PointerButton.Middle, ref uiInputHandled))
                {
                    // capture updated inside TryRouteMouseDown
                }
            }

            return new UiInputFrameResult(Handled: uiInputHandled, PointerCaptured: _uiPointerCaptured, WheelCaptured: uiWheelCaptured);
        }

        private static bool _leftWasDown;
        private static bool _rightWasDown;
        private static bool _middleWasDown;

        private static bool TryRouteMouseDown(
            UIRoot uiRoot,
            Vector2 mousePos,
            bool pressed,
            PointerButton button,
            ref bool uiInputHandled)
        {
            if (!pressed)
            {
                return false;
            }

            bool handled = uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Down,
                Button = button,
                X = mousePos.X,
                Y = mousePos.Y
            });
            uiInputHandled |= handled;
            if (handled)
            {
                _uiPointerCaptured = true;
                _uiCapturedPointerButton = button;
            }

            return handled;
        }

        private static bool ShouldForwardUiPointerMove(float x, float y)
        {
            if (!_hasLastUiPointerMove ||
                Math.Abs(x - _lastUiPointerMoveX) > 0.01f ||
                Math.Abs(y - _lastUiPointerMoveY) > 0.01f)
            {
                _hasLastUiPointerMove = true;
                _lastUiPointerMoveX = x;
                _lastUiPointerMoveY = y;
                return true;
            }

            return false;
        }

        private static void ResetInputState()
        {
            _uiPointerCaptured = false;
            _uiCapturedPointerButton = null;
            _hasLastUiPointerMove = false;
            _lastUiPointerMoveX = 0f;
            _lastUiPointerMoveY = 0f;
            _emptyBufferWarned = false;
            _leftWasDown = false;
            _rightWasDown = false;
            _middleWasDown = false;
        }

        private static bool IsInteractiveUiNode(UiNode? node)
        {
            for (UiNode? current = node; current != null; current = current.Parent)
            {
                if (current.ActionHandles.Count > 0)
                {
                    return true;
                }

                if (current.CanvasContent is IUiCanvasInputSink)
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

        private static T RequireService<T>(GameEngine engine, ServiceKey<T> key)
        {
            if (engine.TryGetService(key, out T service))
            {
                return service;
            }

            throw new InvalidOperationException(
                $"WebGPU host requires Core service '{key.Name}' before the render loop starts.");
        }

        private static int ReadPositiveIntEnvironment(string key)
        {
            string? value = Environment.GetEnvironmentVariable(key);
            if (int.TryParse(value, out int parsed) && parsed > 0)
            {
                return parsed;
            }

            return 0;
        }
    }
}
