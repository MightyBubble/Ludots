using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardAttachmentMountDismountMod.Runtime;

internal sealed class AttachmentMountPresentationSystem : ISystem<float>
{
    private readonly AttachmentMountDemoState _state;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public AttachmentMountPresentationSystem(
        AttachmentMountDemoState state,
        DebugDrawCommandBuffer debugDraw,
        ScreenOverlayBuffer overlay)
    {
        _state = state;
        _debugDraw = debugDraw;
        _overlay = overlay;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        GraphShowcaseStagePresenter.Clear(_debugDraw);
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw,
            _state.CarrierXCm / 100f,
            0f,
            1.2f,
            DebugDrawColor.Cyan,
            0.14f);
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw,
            _state.RiderXCm / 100f,
            _state.RiderYCm / 100f,
            0.55f,
            _state.RiderAttached ? DebugDrawColor.Yellow : DebugDrawColor.Green,
            0.12f);
        GraphShowcaseStagePresenter.DrawPlayerCaption(
            _overlay,
            "乘员上下车",
            _state.Caption);
    }
}
