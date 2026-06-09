using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using MassNavigationMod.Input;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationControlSystem : ISystem<float>
{
    private const float RotationSpeedRadiansPerSecond = 2.5f;

    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationControlSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
    {
        _engine = engine;
        _simulation = simulation;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
        {
            return;
        }

        _simulation.ObserveControlTick();

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(MassNavigationInputActions.ResetScene))
            {
                _simulation.RequestSceneReset();
            }

            float deltaRadians = 0f;
            if (input.IsDown(MassNavigationInputActions.RotateLeft))
            {
                deltaRadians -= RotationSpeedRadiansPerSecond * dt;
            }

            if (input.IsDown(MassNavigationInputActions.RotateRight))
            {
                deltaRadians += RotationSpeedRadiansPerSecond * dt;
            }

            if (MathF.Abs(deltaRadians) > 1e-5f)
            {
                _simulation.NavGroupRuntime.RotateSelected(
                    _simulation.AgentState,
                    _simulation.SelectedEntities,
                    deltaRadians);
            }
        }

        if (_simulation.ConsumeSceneResetRequest())
        {
            ResetScenario();
        }
        else
        {
            _simulation.NavGroupRuntime.RefreshSelectedRotation(_simulation.AgentState, _simulation.SelectedEntities);
        }
    }

    private void ResetScenario()
    {
        ClearSelection();
        _simulation.ClearSelection();
        _simulation.NavGroupRuntime.Reset();
        _simulation.AgentState.DestroyTracked(_engine.World);
        MassNavigationScenarioBootstrap.SpawnDefaultScenario(
            _engine,
            _simulation,
            _engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new InvalidOperationException("MassNavigationMod requires TeamEntityLookup."));
        MassNavigationRuntime.RequestTacticalCameraReset(_engine);
        MassNavigationRuntime.RequestMinimapStrategicWorldView(_engine);

        _simulation.MarkSceneResetExecuted();
        _simulation.MarkStructuralChange();
    }

    private void ClearSelection()
    {
        if (!SelectionContextRuntime.TryGetRuntime(_engine.GlobalContext, out SelectionRuntime selection))
        {
            return;
        }

        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localPlayerObj) ||
            localPlayerObj is not Arch.Core.Entity owner ||
            !_engine.World.IsAlive(owner))
        {
            return;
        }

        selection.ClearSelection(owner, SelectionSetKeys.LivePrimary);
        selection.ClearSelection(owner, SelectionSetKeys.FormationPrimary);
        selection.ClearSelection(owner, SelectionSetKeys.CommandPreview);
        selection.ClearSelection(owner, SelectionSetKeys.CommandSnapshot);
    }
}

