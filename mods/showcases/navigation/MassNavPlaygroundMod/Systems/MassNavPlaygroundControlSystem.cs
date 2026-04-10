using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Scripting;
using MassNavPlaygroundMod.Input;
using MassNavPlaygroundMod.Runtime;

namespace MassNavPlaygroundMod.Systems;

internal sealed class MassNavPlaygroundControlSystem : ISystem<float>
{
    private const float RotationSpeedRadiansPerSecond = 2.5f;

    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;

    public MassNavPlaygroundControlSystem(GameEngine engine, MassNavSimulationRuntime simulation)
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
        if (!MassNavPlaygroundIds.IsCurrentPlaygroundMap(_engine))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(MassNavPlaygroundInputActions.ResetScene))
            {
                _simulation.RequestSceneReset();
            }

            float deltaRadians = 0f;
            if (input.IsDown(MassNavPlaygroundInputActions.RotateLeft))
            {
                deltaRadians -= RotationSpeedRadiansPerSecond * dt;
            }

            if (input.IsDown(MassNavPlaygroundInputActions.RotateRight))
            {
                deltaRadians += RotationSpeedRadiansPerSecond * dt;
            }

            if (MathF.Abs(deltaRadians) > 1e-5f)
            {
                _simulation.FormationRuntime.RotateSelected(_simulation.AgentState, _simulation.SelectedEntities, deltaRadians);
            }
        }

        ApplyFlowTuning();

        if (_simulation.ConsumeSceneResetRequest())
        {
            ResetScenario();
        }
        else
        {
            _simulation.FormationRuntime.RefreshSelectedRotation(_simulation.AgentState, _simulation.SelectedEntities);
        }
    }

    private void ApplyFlowTuning()
    {
        if (_engine.GetService(CoreServiceKeys.Navigation2DRuntime) is not Navigation2DRuntime navRuntime)
        {
            return;
        }

        navRuntime.FlowEnabled = _simulation.FlowTuning.Enabled;
        navRuntime.FlowIterationsPerTick = _simulation.FlowTuning.IterationsPerStep;
        navRuntime.FlowStepIntervalTicks = _simulation.FlowTuning.StepIntervalTicks;
        navRuntime.FlowCrowdStampIntervalTicks = _simulation.FlowTuning.CrowdStampIntervalTicks;
        navRuntime.FlowObstacleStampIntervalTicks = _simulation.FlowTuning.ObstacleStampIntervalTicks;
    }

    private void ResetScenario()
    {
        ClearSelection();
        _simulation.ClearSelection();
        _simulation.FormationRuntime.Reset();
        _simulation.AgentState.DestroyTracked(_engine.World);

        if (_engine.GetService(CoreServiceKeys.Navigation2DRuntime) is Navigation2DRuntime navRuntime)
        {
            navRuntime.ResetFlowState();
        }

        MassNavScenarioBootstrap.SpawnDefaultScenario(_engine.World, _simulation.AgentState, _simulation.AgentsPerTeam);
        MassNavPlaygroundRuntime.RequestTacticalCameraReset(_engine);
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
