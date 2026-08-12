using System.Numerics;
using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardLiveSkillWorkbenchShowcaseMod.Runtime;

/// <summary>
/// World circles + screen text that states player action / feedback in plain language.
/// </summary>
internal sealed class LiveSkillWorkbenchVignettePresentationSystem : ISystem<float>
{
    private static readonly Vector4 TitleColor = new(0.96f, 0.98f, 1f, 1f);
    private static readonly Vector4 ActionColor = new(1f, 0.86f, 0.42f, 1f);
    private static readonly Vector4 FeedbackColor = new(0.55f, 0.95f, 0.70f, 1f);
    private static readonly Vector4 HintColor = new(0.78f, 0.84f, 0.90f, 1f);

    private readonly LiveSkillWorkbenchVignetteRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer? _overlay;
    private int _frames;

    public LiveSkillWorkbenchVignettePresentationSystem(
        GameEngine engine,
        LiveSkillWorkbenchVignetteRuntime runtime,
        DebugDrawCommandBuffer debugDraw)
    {
        _runtime = runtime;
        _debugDraw = debugDraw;
        _overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _frames++;
        GraphShowcaseStagePresenter.Clear(_debugDraw);
        if (_frames < 4)
        {
            return;
        }

        DrawWorld();
        DrawHud();
    }

    private void DrawWorld()
    {
        var mageColor = _runtime.FlashFrames > 0 && _runtime.CurrentBeat == LiveSkillWorkbenchVignetteRuntime.Beat.HealMage
            ? DebugDrawColor.Green
            : GraphShowcaseStagePresenter.CasterColor;
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.MageX, _runtime.MageY, 0.7f, mageColor, 0.2f);

        var dummyColor = _runtime.FlashFrames > 0 && _runtime.ProjectileT < 0f
            ? DebugDrawColor.White
            : GraphShowcaseStagePresenter.EnemyColor;
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, _runtime.DummyX, _runtime.DummyY, 0.55f, dummyColor);

        // HP rings above heads (outer gray = full, colored = current)
        DrawHpRing(_runtime.MageX, _runtime.MageY + 1.4f, _runtime.MageHp01, DebugDrawColor.Green);
        DrawHpRing(_runtime.DummyX, _runtime.DummyY + 1.4f, _runtime.DummyHp01, DebugDrawColor.Red);

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
            GraphShowcaseStagePresenter.DrawActor(_debugDraw, 0f, 5.5f, 0.55f, DebugDrawColor.Yellow, 0.22f);
        }
    }

    private void DrawHpRing(float x, float y, float fill01, DebugDrawColor color)
    {
        GraphShowcaseStagePresenter.DrawActor(_debugDraw, x, y, 0.45f, DebugDrawColor.Gray, 0.06f);
        GraphShowcaseStagePresenter.DrawActor(
            _debugDraw, x, y, 0.18f + 0.27f * System.Math.Clamp(fill01, 0f, 1f), color, 0.14f);
    }

    private void DrawHud()
    {
        if (_overlay == null)
        {
            return;
        }

        _overlay.Clear();
        _overlay.AddText(24, 20, "Hot-edit / Hot-apply acceptance (editor -> runtime)", 22, TitleColor, 61001, 1);
        _overlay.AddText(24, 54, $"EDITOR: {_runtime.EditorAction}", 17, ActionColor, 61002, StringHash(_runtime.EditorAction));
        _overlay.AddText(24, 82, $"RUNTIME: {_runtime.RuntimeResult}", 17, FeedbackColor, 61003, StringHash(_runtime.RuntimeResult));
        _overlay.AddText(
            24,
            112,
            $"mageHP={_runtime.MageHp01:P0}  dummyHP={_runtime.DummyHp01:P0}  chain={_runtime.ChainLit}/4  beat={_runtime.CurrentBeat}",
            15,
            HintColor,
            61004,
            StringHash(_runtime.Metrics.Detail ?? string.Empty));
        _overlay.AddText(
            24,
            140,
            "Proof target: LiveGasEditPipeline Stage/Classify/Commit — NOT player controls",
            14,
            HintColor,
            61005,
            1);
    }

    private static int StringHash(string value)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < value.Length; i++)
            {
                hash = hash * 31 + value[i];
            }

            return hash == 0 ? 1 : hash;
        }
    }
}
