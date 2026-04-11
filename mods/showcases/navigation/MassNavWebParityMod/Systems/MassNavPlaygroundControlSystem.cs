using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Input;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavWebParityControlSystem : ISystem<float>
{
    private const float RotationSpeedRadiansPerSecond = 2.5f;

    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavWebParityControlSystem(GameEngine engine, MassNavSimulationRuntime simulation)
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
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        _simulation.ObserveControlTick();

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(MassNavWebParityInputActions.ResetScene))
            {
                _simulation.RequestSceneReset();
            }

            float deltaRadians = 0f;
            if (input.IsDown(MassNavWebParityInputActions.RotateLeft))
            {
                deltaRadians -= RotationSpeedRadiansPerSecond * dt;
            }

            if (input.IsDown(MassNavWebParityInputActions.RotateRight))
            {
                deltaRadians += RotationSpeedRadiansPerSecond * dt;
            }

            if (MathF.Abs(deltaRadians) > 1e-5f)
            {
                _simulation.Commands.EnqueueSelectionRotate(_simulation.SelectedEntities, deltaRadians);
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
        _simulation.Commands.Reset();
        _simulation.NavGroupRuntime.Reset();
        _simulation.AgentState.DestroyTracked(_engine.World);
        MassNavScenarioBootstrap.SpawnDefaultScenario(_engine.World, _simulation);
        MassNavWebParityRuntime.RequestTacticalCameraReset(_engine);
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
