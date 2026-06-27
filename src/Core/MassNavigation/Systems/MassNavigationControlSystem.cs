using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using Ludots.Core.MassNavigation.Input;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

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
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine))
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
                _simulation.RotateSelectedFormation(_engine.World, deltaRadians, ResolveLocalPlayerId());
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
            _simulation.NavGroupRuntime.RefreshSelectedRotation(_engine.World, _simulation.AgentState, _simulation.SelectedEntities);
        }
    }

    private void ResetConfiguredScenario()
    {
        ResetRuntimeState();
        MassNavigationScenarioBootstrap.SpawnConfiguredScenario(
            _engine,
            _simulation,
            _engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new InvalidOperationException("MassNavigation runtime requires TeamEntityLookup."));

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
            ?? throw new InvalidOperationException("MassNavigation runtime requires RuntimeEntitySpawnQueue.");
        if (_engine.CurrentMapSession != null)
        {
            spawnQueue.RemoveForMap(_engine.CurrentMapSession.MapId);
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

    private int ResolveLocalPlayerId()
    {
        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Arch.Core.Entity local ||
            !_engine.World.IsAlive(local))
        {
            throw new InvalidOperationException("MassNavigation runtime requires LocalPlayerEntity before rotating formations.");
        }

        if (!_engine.World.TryGet(local, out Ludots.Core.Gameplay.Components.PlayerOwner owner))
        {
            throw new InvalidOperationException("MassNavigation runtime LocalPlayerEntity must author PlayerOwner before rotating formations.");
        }

        return owner.PlayerId;
    }
}
