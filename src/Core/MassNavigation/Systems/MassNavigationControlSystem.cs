using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Core.MassNavigation.Input;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationControlSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationRuntimeBinding _binding;
    private MassNavigationSimulationRuntime Simulation => _binding.RequireCurrent();

    public MassNavigationControlSystem(GameEngine engine, MassNavigationRuntimeBinding binding)
    {
        _engine = engine;
        _binding = binding;
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

        Simulation.Telemetry.ObserveControlTick();

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(MassNavigationInputActions.ResetScene))
            {
                Simulation.RequestSceneReset();
            }
        }

        if (Simulation.ConsumeSceneResetRequest())
        {
            ResetRuntimeState();
            _engine.GetService(MassNavigationKeys.SceneController)?.PopulateScene(_engine, Simulation);
            Simulation.MarkSceneResetExecuted();
            Simulation.MarkStructuralChange();
        }
    }

    private void ResetRuntimeState()
    {
        RemovePendingScenarioSpawns();
        Simulation.ResetRuntimeState(_engine.World);
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

}
