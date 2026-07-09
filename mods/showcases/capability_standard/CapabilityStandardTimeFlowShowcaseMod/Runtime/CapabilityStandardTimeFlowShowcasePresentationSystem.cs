using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace CapabilityStandardTimeFlowShowcaseMod.Runtime;

internal sealed class CapabilityStandardTimeFlowShowcasePresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CapabilityStandardTimeFlowShowcaseRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly CapabilityStandardTimeFlowShowcasePanelController _panel;

    public CapabilityStandardTimeFlowShowcasePresentationSystem(
        GameEngine engine,
        CapabilityStandardTimeFlowShowcaseRuntime runtime,
        DebugDrawCommandBuffer debugDraw)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _debugDraw = debugDraw ?? throw new ArgumentNullException(nameof(debugDraw));
        _panel = new CapabilityStandardTimeFlowShowcasePanelController(runtime);
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (_engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        if (!_runtime.IsActive)
        {
            _panel.ClearIfOwned(root);
            return;
        }

        CapabilityStandardTimeFlowShowcasePanelState state = _runtime.CapturePanelState(_engine);
        _panel.MountOrSync(root, _engine, in state);
        DrawPlayerScene(state);
    }

    private void DrawPlayerScene(in CapabilityStandardTimeFlowShowcasePanelState state)
    {
        var hero = new Vector2(state.NavPositionXCm * 0.01f, state.NavPositionYCm * 0.01f);
        var target = new Vector2(state.NavTargetXCm * 0.01f, state.NavTargetYCm * 0.01f);
        var physics = new Vector2(state.PhysicsPositionXCm * 0.01f, state.PhysicsPositionYCm * 0.01f);
        _debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = hero,
            B = target,
            Thickness = 2f,
            Color = state.SkillIndicatorPauseActive ? DebugDrawColor.Cyan : DebugDrawColor.Gray
        });
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = hero,
            Radius = 0.45f,
            Thickness = 2f,
            Color = DebugDrawColor.Cyan
        });
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = target,
            Radius = 0.55f,
            Thickness = 2f,
            Color = DebugDrawColor.Yellow
        });
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = physics,
            Radius = 0.35f,
            Thickness = 2f,
            Color = new DebugDrawColor(230, 195, 90)
        });

        if (state.SkillIndicatorPauseActive)
        {
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = target,
                Radius = 1.45f,
                Thickness = 2f,
                Color = new DebugDrawColor(72, 182, 255)
            });
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = target,
                Radius = 2.05f,
                Thickness = 2f,
                Color = new DebugDrawColor(120, 226, 255)
            });
        }

        if (state.HeroSkillCastAgeSteps < 90)
        {
            float age01 = Math.Clamp(state.HeroSkillCastAgeSteps / 90f, 0f, 1f);
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = target,
                Radius = 0.8f + (age01 * 2.4f),
                Thickness = 3f,
                Color = new DebugDrawColor(255, 210, 92)
            });
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = target,
                Radius = 0.35f + (age01 * 1.2f),
                Thickness = 2f,
                Color = new DebugDrawColor(255, 95, 95)
            });
        }
    }
}
