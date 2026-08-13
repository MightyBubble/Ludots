using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardGraphOpsRelMod.Runtime;

internal sealed class GraphOpsRelPresentationSystem : ISystem<float>
{
    private readonly GraphOpsRelRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public GraphOpsRelPresentationSystem(
        GraphOpsRelRuntime runtime,
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
        for (int i = 0; i < _runtime.FriendSlotCount; i++)
        {
            if (!_runtime.IsFriendLinked(i))
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawAggroLine(
                _debugDraw,
                _runtime.PlayerX,
                _runtime.PlayerY,
                _runtime.FriendX[i],
                _runtime.FriendY[i]);
        }

        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, _runtime.Phase, _runtime.Metrics.Detail);
    }
}
