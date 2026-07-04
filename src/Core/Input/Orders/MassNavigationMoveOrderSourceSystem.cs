using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.MassNavigation;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Orders;

public sealed class MassNavigationMoveOrderSourceSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly OrderQueue _orderQueue;

    public MassNavigationMoveOrderSourceSystem(GameEngine engine, OrderQueue orderQueue)
    {
        _engine = engine;
        _orderQueue = orderQueue;
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

        if (_engine.GetService(MassNavigationKeys.SimulationRuntime) is not MassNavigationSimulationRuntime simulation)
        {
            return;
        }

        simulation.ObserveCommandTick();

        if (_engine.GetService(CoreServiceKeys.UiCaptured) ||
            _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader)
        {
            return;
        }

        if (!CommandInteractionSemanticRuntime.TryConsumeGroundMoveCommand(_engine.GlobalContext, out WorldCmInt2 worldCm))
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.OrderTypeRegistry) is not OrderTypeRegistry orderTypeRegistry)
        {
            throw new InvalidOperationException(
                $"MassNavigation move order source requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}'.");
        }

        MassNavigationMoveOrderSubmitter.SubmitViaOrderQueue(
            simulation,
            _engine.World,
            _engine.GlobalContext,
            _orderQueue,
            orderTypeRegistry,
            new Vector2(worldCm.X, worldCm.Y),
            ResolveLocalPlayerId());
    }

    private int ResolveLocalPlayerId()
    {
        Entity local = MassNavigationPrimarySelectionViewBootstrapSystem.RequireLocalSelectionOwner(_engine);
        return _engine.World.Get<PlayerOwner>(local).PlayerId;
    }
}
