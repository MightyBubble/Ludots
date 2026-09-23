using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace Ludots.Adapter.WebGpu;

internal sealed class WebGpuPresentationHost
{
    private readonly GameEngine _engine;
    private readonly WebGpuViewController _view;
    private readonly CameraPresenter _cameraPresenter;
    private readonly PresentationFrameSetupSystem? _presentationFrameSetup;

    private WebGpuPresentationHost(
        GameEngine engine,
        WebGpuViewController view,
        CameraPresenter cameraPresenter,
        PresentationFrameSetupSystem? presentationFrameSetup)
    {
        _engine = engine;
        _view = view;
        _cameraPresenter = cameraPresenter;
        _presentationFrameSetup = presentationFrameSetup;
    }

    public WebGpuCameraAdapter Camera { get; private init; } = null!;

    public static WebGpuPresentationHost Install(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var view = new WebGpuViewController();
        var camera = new WebGpuCameraAdapter();
        var timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
        var cameraPresenter = new CameraPresenter(engine.SpatialCoords, camera, timings);
        var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, view);
        var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, view);
        var presentationFrameSetup = engine.GetService(CoreServiceKeys.PresentationFrameSetup);

        screenProjector.BindPresenter(cameraPresenter);
        screenRayProvider.BindPresenter(cameraPresenter);
        screenProjector.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);
        screenRayProvider.BindPresentationAlphaProvider(() => presentationFrameSetup?.GetInterpolationAlpha() ?? 1f);

        engine.SetService(CoreServiceKeys.ViewController, view);
        engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
        engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);

        var culling = new CameraCullingSystem(
            engine.World,
            engine.GameSession.Camera,
            engine.SpatialQueries,
            view,
            engine.MergedConfig.Presentation.CameraCulling,
            performers: engine.GetService(CoreServiceKeys.PerformerEntityRuntime),
            timingDiagnostics: timings);
        engine.InsertPresentationSystemBefore<PresentationEntityLifecycleSystem>(culling);
        engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);

        return new WebGpuPresentationHost(
            engine,
            view,
            cameraPresenter,
            presentationFrameSetup)
        {
            Camera = camera,
        };
    }

    public void Update(uint viewportWidth, uint viewportHeight, float deltaSeconds)
    {
        SetViewport(viewportWidth, viewportHeight);
        float alpha = _presentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
        _cameraPresenter.Update(_engine.GameSession.Camera, alpha);
    }

    public void SetViewport(uint viewportWidth, uint viewportHeight)
    {
        _view.SetResolution(viewportWidth, viewportHeight);
    }
}

internal sealed class WebGpuViewController : IViewController
{
    public Vector2 Resolution { get; private set; } = new(1280f, 720f);

    public float Fov { get; private set; } = 60f;

    public float AspectRatio => Resolution.X / Resolution.Y;

    public void SetResolution(uint width, uint height)
    {
        Resolution = new Vector2(Math.Max(1u, width), Math.Max(1u, height));
    }
}

public sealed class WebGpuCameraAdapter : ICameraAdapter
{
    public CameraRenderState3D State { get; private set; }

    public void UpdateCamera(in CameraRenderState3D state)
    {
        State = state;
    }
}
