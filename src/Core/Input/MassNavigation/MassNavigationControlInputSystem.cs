using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Input;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.MassNavigation;

public sealed class MassNavigationControlInputSystem : ISystem<float>
{
    private readonly GameEngine _engine;

    public MassNavigationControlInputSystem(GameEngine engine)
    {
        _engine = engine;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine) ||
            _engine.GetService(CoreServiceKeys.UiCaptured) ||
            _engine.GetService(MassNavigationKeys.SimulationRuntime) is not MassNavigationSimulationRuntime simulation ||
            _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        if (input.PressedThisFrame(MassNavigationInputActions.ResetScene))
        {
            simulation.RequestSceneReset();
            return;
        }

        float deltaRadians = 0f;
        if (input.IsDown(MassNavigationInputActions.RotateLeft))
        {
            deltaRadians -= simulation.Config.Semantics.Group.FormationRotationSpeedRadiansPerSecond * dt;
        }

        if (input.IsDown(MassNavigationInputActions.RotateRight))
        {
            deltaRadians += simulation.Config.Semantics.Group.FormationRotationSpeedRadiansPerSecond * dt;
        }

        if (MathF.Abs(deltaRadians) <= simulation.Config.Semantics.Group.FormationRotationEpsilonRadians)
        {
            return;
        }

        simulation.RotateSelectedFormation(
            _engine.World,
            _engine.GlobalContext,
            deltaRadians,
            ResolveLocalPlayerId());
    }

    private int ResolveLocalPlayerId()
    {
        Entity local = MassNavigationPrimarySelectionViewBootstrapSystem.RequireLocalSelectionOwner(_engine);
        return _engine.World.Get<Ludots.Core.Gameplay.Components.PlayerOwner>(local).PlayerId;
    }
}
