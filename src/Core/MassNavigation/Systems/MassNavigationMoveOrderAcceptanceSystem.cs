using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationMoveOrderAcceptanceSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationMoveOrderAcceptanceSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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

        OrderTypeRegistry orderTypeRegistry = _engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("MassNavigation move order acceptance requires OrderTypeRegistry.");
        _simulation.ReconcilePendingMoveOrderAcceptance(_engine.World, orderTypeRegistry);
    }
}
