using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using MassNavigationMod.Input;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationControlSystem : ISystem<float>
{
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
                deltaRadians -= _simulation.Config.Semantics.Group.FormationRotationSpeedRadiansPerSecond * dt;
            }

            if (input.IsDown(MassNavigationInputActions.RotateRight))
            {
                deltaRadians += _simulation.Config.Semantics.Group.FormationRotationSpeedRadiansPerSecond * dt;
            }

            if (MathF.Abs(deltaRadians) > _simulation.Config.Semantics.Group.FormationRotationEpsilonRadians)
            {
                _simulation.Commands.EnqueueSelectionRotate(_simulation.SelectedEntities, deltaRadians);
            }
        }

        if (_simulation.ConsumeSceneResetRequest())
        {
            if (_simulation.Config.ScenarioRuntime.AutoSpawnConfiguredScenario)
            {
                ResetConfiguredScenario();
            }
            else
            {
                ResetRuntimeState();
                _simulation.MarkSceneResetExecuted();
                _simulation.MarkStructuralChange();
            }
        }
        else
        {
            _simulation.NavGroupRuntime.RefreshSelectedRotation(_simulation.AgentState, _simulation.SelectedEntities);
        }
    }

    private void ResetConfiguredScenario()
    {
        ResetRuntimeState();
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

    private void ResetRuntimeState()
    {
        ClearSelection();
        RemovePendingScenarioSpawns();
        _simulation.ResetRuntimeState(_engine.World);
    }

    private void RemovePendingScenarioSpawns()
    {
        RuntimeEntitySpawnQueue spawnQueue = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("MassNavigationMod requires RuntimeEntitySpawnQueue.");
        RuntimeEntitySpawnReceiptChannelRegistry channels = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
            ?? throw new InvalidOperationException("MassNavigationMod requires RuntimeEntitySpawnReceiptChannelRegistry.");
        int receiptChannelId = channels.Register(MassNavigationIds.RuntimeSpawnReceiptChannelKey);
        spawnQueue.RemoveForReceiptChannel(receiptChannelId);
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

