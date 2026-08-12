using System.Numerics;
using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;

/// <summary>
/// Readable stage using the same DebugDraw primitives as the working ability sandbox
/// (circles/lines only — avoid box path that segfaults under software GL in this VM).
/// </summary>
internal sealed class LiveSkillWorkbenchVignettePresentationSystem : ISystem<float>
{
    private readonly LiveSkillWorkbenchVignetteRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;

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
        GraphShowcaseStagePresenter.Clear(_debugDraw);

        // Ground lane
        _debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(-8f, -2.2f),
            B = new Vector2(8f, -2.2f),
            Thickness = 0.1f,
            Color = DebugDrawColor.Gray
        });

        var mageColor = _runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.HealMage && _runtime.FlashFrames > 0
            ? DebugDrawColor.Green
            : GraphShowcaseStagePresenter.CasterColor;
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.MageX, _runtime.MageY, 0.85f, mageColor, 0.22f);

        var dummyColor = _runtime.FlashFrames > 0 && _runtime.ProjectileT < 0f
            ? DebugDrawColor.White
            : GraphShowcaseStagePresenter.EnemyColor;
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.DummyX, _runtime.DummyY, 0.95f, dummyColor, 0.22f);

        // HP as concentric rings (outer=full, inner=current)
        DrawHpRings(_runtime.MageX, _runtime.MageY, _runtime.MageHp01, DebugDrawColor.Green);
        DrawHpRings(_runtime.DummyX, _runtime.DummyY, _runtime.DummyHp01, DebugDrawColor.Red);

        if (_runtime.ProjectileT >= 0f)
        {
            _runtime.GetProjectilePos(out float px, out float py);
            var color = _runtime.ProjectileFrost ? DebugDrawColor.Cyan : DebugDrawColor.Yellow;
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, px, py, _runtime.ProjectileFrost ? 0.35f : 0.45f, color, 0.18f);
            GraphShowcaseStagePresenter.DrawAggroLine(
                _debugDraw, _runtime.MageX, _runtime.MageY, _runtime.DummyX, _runtime.DummyY);
        }

        if (_runtime.ProjectileFrost && _runtime.FlashFrames > 0)
        {
            GraphShowcaseStagePresenter.DrawTriggerRing(_debugDraw, _runtime.DummyX, _runtime.DummyY, 1.8f, armed: true);
        }

        // Effect-chain pips as small circles
        for (int i = 0; i < 4; i++)
        {
            bool on = i < _runtime.ChainLit;
            GraphShowcaseStagePresenter.DrawActor(
                _debugDraw,
                -3.3f + i * 2.2f,
                -5.2f,
                on ? 0.35f : 0.22f,
                on ? DebugDrawColor.Yellow : DebugDrawColor.Gray,
                on ? 0.18f : 0.08f);
        }

        // Hot-apply / banner marker above
        bool hot = _runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.HotApplyBanner;
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw,
            0f,
            6.5f,
            hot ? 0.7f : 0.4f,
            hot ? DebugDrawColor.Yellow : DebugDrawColor.Cyan,
            hot ? 0.22f : 0.1f);

        // Beat index dots
        int beatIndex = (int)_runtime.CurrentBeat;
        for (int i = 0; i < 6; i++)
        {
            GraphShowcaseStagePresenter.DrawActor(
                _debugDraw,
                -7.2f + i * 0.7f,
                8.0f,
                0.18f,
                i == beatIndex ? DebugDrawColor.Green : DebugDrawColor.Gray,
                i == beatIndex ? 0.14f : 0.06f);
        }
    }

    private void DrawHpRings(float x, float y, float fill01, DebugDrawColor color)
    {
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, x, y + 1.55f, 0.55f, DebugDrawColor.Gray, 0.06f);
        float r = 0.2f + 0.35f * Math.Clamp(fill01, 0f, 1f);
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, x, y + 1.55f, r, color, 0.14f);
    }
}
