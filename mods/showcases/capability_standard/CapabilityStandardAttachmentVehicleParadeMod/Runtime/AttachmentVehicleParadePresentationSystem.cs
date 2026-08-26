using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardAttachmentVehicleParadeMod.Runtime;

internal sealed class AttachmentVehicleParadePresentationSystem : ISystem<float>
{
    private readonly AttachmentVehicleParadeDemoState _state;
    private readonly ScreenOverlayBuffer _overlay;

    public AttachmentVehicleParadePresentationSystem(
        AttachmentVehicleParadeDemoState state,
        ScreenOverlayBuffer overlay)
    {
        _state = state;
        _overlay = overlay;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        GraphShowcaseStagePresenter.DrawPlayerCaption(
            _overlay,
            "装甲阅兵",
            _state.Caption);
    }
}
