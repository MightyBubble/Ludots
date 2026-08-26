using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardAttachmentSettlementMod.Runtime;

internal sealed class AttachmentSettlementPresentationSystem : ISystem<float>
{
    private readonly AttachmentSettlementDemoState _state;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public AttachmentSettlementPresentationSystem(
        AttachmentSettlementDemoState state,
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
            _debugDraw, _state.HallXCm / 100f, _state.HallYCm / 100f, 1.4f, DebugDrawColor.White, 0.14f);
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _state.AnnexXCm / 100f, _state.AnnexYCm / 100f, 0.9f, DebugDrawColor.Cyan, 0.12f);
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, _state.TowerXCm / 100f, _state.TowerYCm / 100f, 0.7f, DebugDrawColor.Yellow, 0.12f);
        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, "哨所静物", _state.Caption);
    }
}
