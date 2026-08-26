using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardAttachmentSettlementMod.Runtime;

internal sealed class AttachmentSettlementPresentationSystem : ISystem<float>
{
    private readonly AttachmentSettlementDemoState _state;
    private readonly ScreenOverlayBuffer _overlay;

    public AttachmentSettlementPresentationSystem(
        AttachmentSettlementDemoState state,
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
        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, "哨所静物", _state.Caption);
    }
}
