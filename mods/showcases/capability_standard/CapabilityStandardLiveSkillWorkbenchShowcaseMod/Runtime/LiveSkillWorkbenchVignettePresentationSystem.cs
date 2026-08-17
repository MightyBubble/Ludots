using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;

/// <summary>
/// Real Showcase Mod presentation (DebugDraw only — ScreenOverlay crashes this host under llvmpipe).
/// </summary>
internal sealed class LiveSkillWorkbenchVignettePresentationSystem : ISystem<float>
{
    private readonly LiveSkillWorkbenchVignetteRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private int _frames;

    public LiveSkillWorkbenchVignettePresentationSystem(
        GameEngine engine,
        LiveSkillWorkbenchVignetteRuntime runtime,
        DebugDrawCommandBuffer debugDraw)
    {
        _ = engine;
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
        GraphShowcaseStagePresenter.Clear(_debugDraw);
        if (_frames < 8)
        {
            return;
        }

        var mageColor = _runtime.FlashFrames > 0 && _runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.HealMage
            ? DebugDrawColor.Green
            : GraphShowcaseStagePresenter.CasterColor;
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.MageX, _runtime.MageY, 0.7f, mageColor, 0.2f);

        var dummyColor = _runtime.FlashFrames > 0 && _runtime.ProjectileT < 0f
            ? DebugDrawColor.White
            : GraphShowcaseStagePresenter.EnemyColor;
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.DummyX, _runtime.DummyY, 0.55f, dummyColor);

        DrawHpRing(_runtime.MageX, _runtime.MageY + 1.4f, _runtime.MageHp01, DebugDrawColor.Green);
        DrawHpRing(_runtime.DummyX, _runtime.DummyY + 1.4f, _runtime.DummyHp01, DebugDrawColor.Red);

        if (_runtime.ProjectileT >= 0f)
        {
            _runtime.GetProjectilePos(out float px, out float py);
            var color = _runtime.ProjectileFrost ? DebugDrawColor.Cyan : DebugDrawColor.Yellow;
            // Projectile radius scales with production ExecuteSlice ReturnInt (35 vs 70).
            float radius = 0.22f + 0.006f * System.Math.Clamp(_runtime.LastReturnInt, 0, 100);
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, px, py, radius, color);
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
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, 0f, 5.5f, 0.55f, DebugDrawColor.Yellow, 0.22f);
        }
    }

    private void DrawHpRing(float x, float y, float fill01, DebugDrawColor color)
    {
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, x, y, 0.45f, DebugDrawColor.Gray, 0.06f);
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, x, y, 0.18f + 0.27f * System.Math.Clamp(fill01, 0f, 1f), color, 0.14f);
    }
}
