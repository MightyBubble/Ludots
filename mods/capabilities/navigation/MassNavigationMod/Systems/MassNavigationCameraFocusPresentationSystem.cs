using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationCameraFocusPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationCameraFocusPresentationSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        float alpha = _engine.GetService(CoreServiceKeys.PresentationFrameSetup)?.GetInterpolationAlpha() ?? 1f;
        CameraStateSnapshot camera = _engine.GameSession.Camera.GetInterpolatedState(alpha);
        if (_engine.GetService(CoreServiceKeys.ViewController) is not IViewController view)
        {
            _simulation.ObserveCameraFocus(camera.TargetCm);
            return;
        }

        var extent = CameraViewportUtil.ComputeViewportExtent(
            camera.DistanceCm,
            camera.FovYDeg,
            camera.Pitch,
            view.AspectRatio);
        _simulation.ObserveCameraFocus(camera.TargetCm, extent.widthCm, extent.heightCm);
    }
}
