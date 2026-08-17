using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.Platform.Abstractions;

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
        var navAgent = new Vector2(state.NavPositionXCm * 0.01f, state.NavPositionYCm * 0.01f);
        var hero = new Vector2(state.HeroLocalPositionXCm * 0.01f, state.HeroLocalPositionYCm * 0.01f);
        var target = new Vector2(state.NavTargetXCm * 0.01f, state.NavTargetYCm * 0.01f);
        var enemy = new Vector2(state.EnemyPositionXCm * 0.01f, state.EnemyPositionYCm * 0.01f);
        var physics = new Vector2(state.PhysicsPositionXCm * 0.01f, state.PhysicsPositionYCm * 0.01f);
        var physicsVelocity = new Vector2(state.PhysicsVelocityXCm * 0.01f, state.PhysicsVelocityYCm * 0.01f);
        var gasBeat = new Vector2((state.EnemyPositionXCm - 760f) * 0.01f, (state.EnemyPositionYCm - 860f) * 0.01f);

        _debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = navAgent,
            B = target,
            Thickness = 2f,
            Color = state.SkillIndicatorPauseActive || state.HeroLocalBurstActive
                ? new DebugDrawColor(80, 220, 150)
                : DebugDrawColor.Gray
        });
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = navAgent,
            Radius = 0.42f,
            Thickness = 2f,
            Color = new DebugDrawColor(80, 220, 150)
        });
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = enemy,
            Radius = 0.55f,
            Thickness = 2f,
            Color = DebugDrawColor.Yellow
        });

        float gasPulse01 = (state.GasStep % 24) / 24f;
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = gasBeat,
            Radius = 0.35f + (gasPulse01 * 0.45f),
            Thickness = 2f,
            Color = state.GasPaused ? DebugDrawColor.Gray : new DebugDrawColor(183, 148, 255)
        });
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = gasBeat,
            Radius = 0.18f,
            Thickness = 2f,
            Color = new DebugDrawColor(183, 148, 255)
        });

        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = physics,
            Radius = 0.35f,
            Thickness = 2f,
            Color = state.HeroLocalBurstActive || state.SimulationPaused
                ? new DebugDrawColor(150, 133, 94)
                : new DebugDrawColor(230, 195, 90)
        });
        if (physicsVelocity.LengthSquared() > 0.0001f)
        {
            Vector2 direction = Vector2.Normalize(physicsVelocity) * 0.85f;
            _debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = physics,
                B = physics + direction,
                Thickness = 2f,
                Color = new DebugDrawColor(230, 195, 90)
            });
        }

        if (state.HeroLocalBurstActive)
        {
            _debugDraw.Lines.Add(new DebugDrawLine2D
            {
                A = hero,
                B = enemy,
                Thickness = 3f,
                Color = state.HeroLocalBurstPausedBySystem ? DebugDrawColor.Gray : new DebugDrawColor(255, 226, 138)
            });
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = hero,
                Radius = 0.48f,
                Thickness = 3f,
                Color = state.HeroLocalBurstPausedBySystem ? DebugDrawColor.Gray : DebugDrawColor.Cyan
            });

            int slashCount = Math.Min(3, Math.Max(1, state.HeroComboHitCount));
            for (int i = 0; i < slashCount; i++)
            {
                float angle = (state.HeroLocalClockSeconds * 9f) + (i * 2.1f);
                Vector2 offset = new Vector2(MathF.Cos(angle) * 0.9f, MathF.Sin(angle) * 0.9f);
                _debugDraw.Lines.Add(new DebugDrawLine2D
                {
                    A = enemy - offset,
                    B = enemy + offset,
                    Thickness = 3f,
                    Color = new DebugDrawColor(255, 112, 112)
                });
            }
        }

        if (state.SkillIndicatorPauseActive)
        {
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = enemy,
                Radius = 1.45f,
                Thickness = 2f,
                Color = new DebugDrawColor(72, 182, 255)
            });
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = enemy,
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
                Center = enemy,
                Radius = 0.8f + (age01 * 2.4f),
                Thickness = 3f,
                Color = new DebugDrawColor(255, 210, 92)
            });
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = enemy,
                Radius = 0.35f + (age01 * 1.2f),
                Thickness = 2f,
                Color = new DebugDrawColor(255, 95, 95)
            });
        }
    }
}
