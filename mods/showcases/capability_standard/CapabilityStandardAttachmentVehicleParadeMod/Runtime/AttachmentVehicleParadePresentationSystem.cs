using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardAttachmentVehicleParadeMod.Runtime;

internal sealed class AttachmentVehicleParadePresentationSystem : ISystem<float>
{
    private readonly AttachmentVehicleParadeDemoState _state;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public AttachmentVehicleParadePresentationSystem(
        AttachmentVehicleParadeDemoState state,
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
        float chassisX = _state.ChassisXCm / 100f;
        float barrelX = _state.BarrelXCm / 100f;
        float barrelY = _state.BarrelYCm / 100f;
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, chassisX, 0f, 1.1f, DebugDrawColor.Cyan, 0.14f);
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, chassisX, 0f, 0.55f, DebugDrawColor.Yellow, 0.1f);
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, barrelX, barrelY, 0.35f, DebugDrawColor.Red, 0.1f);
        GraphShowcaseStagePresenter.DrawPlayerCaption(
            _overlay,
            "装甲阅兵",
            _state.Caption);
    }
}
