using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardAbilityGraphSandboxMod.Runtime;

internal sealed class AbilityGraphSandboxPresentationSystem : ISystem<float>
{
    private readonly AbilityGraphSandboxRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public AbilityGraphSandboxPresentationSystem(
        AbilityGraphSandboxRuntime runtime,
        DebugDrawCommandBuffer debugDraw,
        ScreenOverlayBuffer overlay)
    {
        _runtime = runtime;
        _debugDraw = debugDraw;
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        GraphShowcaseStagePresenter.Clear(_debugDraw);
        GraphShowcaseStagePresenter.DrawTriggerRing(_debugDraw, _runtime.CasterX, _runtime.CasterY, 8f, armed: true);
        int hit = _runtime.LastHit;
        if (hit >= 0 && hit < _runtime.TargetCount)
        {
            GraphShowcaseStagePresenter.DrawAggroLine(
                _debugDraw,
                _runtime.CasterX,
                _runtime.CasterY,
                _runtime.TargetX[hit],
                _runtime.TargetY[hit]);
        }

        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, "能力图沙盘", _runtime.Metrics.Detail);
    }
}
