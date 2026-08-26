using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardAttachmentMountDismountMod.Runtime;

internal sealed class AttachmentMountPresentationSystem : ISystem<float>
{
    private readonly AttachmentMountDemoState _state;
    private readonly ScreenOverlayBuffer _overlay;

    public AttachmentMountPresentationSystem(
        AttachmentMountDemoState state,
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
            "乘员上下车",
            _state.Caption);
    }
}
