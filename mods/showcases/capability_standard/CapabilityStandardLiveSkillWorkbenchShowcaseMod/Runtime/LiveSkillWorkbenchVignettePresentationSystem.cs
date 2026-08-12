using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;

internal sealed class LiveSkillWorkbenchVignettePresentationSystem : ISystem<float>
{
    private readonly LiveSkillWorkbenchVignetteRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private int _frames;

    public LiveSkillWorkbenchVignettePresentationSystem(
        LiveSkillWorkbenchVignetteRuntime runtime,
        DebugDrawCommandBuffer debugDraw)
    {
        _runtime = runtime;
        _debugDraw = debugDraw;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _frames++;
        // Skip first few presentation frames while host finishes GPU warmup.
        if (_frames < 8)
        {
            GraphShowcaseStagePresenter.Clear(_debugDraw);
            return;
        }

        GraphShowcaseStagePresenter.Clear(_debugDraw);

        var mageColor = _runtime.FlashFrames > 0 && _runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.HealMage
            ? DebugDrawColor.Green
            : GraphShowcaseStagePresenter.CasterColor;
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.MageX, _runtime.MageY, 0.7f, mageColor, 0.2f);

        var dummyColor = _runtime.FlashFrames > 0 && _runtime.ProjectileT < 0f
            ? DebugDrawColor.White
            : GraphShowcaseStagePresenter.EnemyColor;
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.DummyX, _runtime.DummyY, 0.55f, dummyColor);

        if (_runtime.ProjectileT >= 0f)
        {
            _runtime.GetProjectilePos(out float px, out float py);
            var color = _runtime.ProjectileFrost ? DebugDrawColor.Cyan : DebugDrawColor.Yellow;
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, px, py, 0.35f, color);
            GraphShowcaseStagePresenter.DrawAggroLine(
                _debugDraw, _runtime.MageX, _runtime.MageY, _runtime.DummyX, _runtime.DummyY);
        }

        for (int i = 0; i < 4; i++)
        {
            bool on = i < _runtime.ChainLit;
            GraphShowcaseStagePresenter.DrawActor(
                _debugDraw,
                -3f + i * 2f,
                -4.5f,
                on ? 0.3f : 0.18f,
                on ? DebugDrawColor.Yellow : DebugDrawColor.Gray);
        }

        if (_runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.HotApplyBanner)
        {
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, 0f, 5.5f, 0.5f, DebugDrawColor.Yellow, 0.2f);
        }
    }
}
