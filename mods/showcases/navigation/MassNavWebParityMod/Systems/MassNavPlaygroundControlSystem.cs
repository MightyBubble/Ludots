using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Input;
using MassNavWebParityMod.Runtime;
using MinimapControlMod;
using MinimapControlMod.Runtime;

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
        ConsumeMinimapWorldClick();

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
        MassNavScenarioBootstrap.SpawnDefaultScenario(
            _engine.World,
            _simulation,
            _engine.GetService(CoreServiceKeys.TeamEntityLookup));
        MassNavWebParityRuntime.RequestTacticalCameraReset(_engine);
        if (_engine.GetService(MinimapControlServiceKeys.Runtime) is { } minimap)
        {
            MassNavWebParityRuntime.SyncMinimapKnownContacts(_simulation, minimap);
            MassNavWebParityRuntime.RequestMinimapStrategicWorldView(_engine);
        }

        _simulation.MarkSceneResetExecuted();
        _simulation.MarkStructuralChange();
    }

    private void ConsumeMinimapWorldClick()
    {
        if (!_engine.GlobalContext.Remove(MinimapControlServiceKeys.WorldClickRequest.Name, out object? requestObj) ||
            requestObj is not MinimapWorldClickRequest request)
        {
            return;
        }

        var targetCm = new System.Numerics.Vector2(request.WorldXcm, request.WorldYcm);
        if (!_simulation.ContainsWorldPoint(targetCm.X, targetCm.Y))
        {
            _simulation.RejectCommandOutsideWorld(targetCm.X, targetCm.Y);
            return;
        }

        _simulation.ObserveCameraFocus(targetCm);
        MassNavWebParityRuntime.RequestCameraJump(_engine, targetCm, 18_000f);
        if (_engine.GetService(MinimapControlServiceKeys.Runtime) is { } runtime)
        {
            MassNavWebParityRuntime.SyncMinimapKnownContacts(_simulation, runtime);
            runtime.SetAbsoluteWorldOverview(true);
        }
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
