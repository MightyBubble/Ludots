using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Orders;

public sealed class MassNavigationMoveOrderSourceSystem : ISystem<float>
{
    private static readonly QueryDescription AuthoredPlayerOwnerQuery = new QueryDescription().WithAll<PlayerOwner>();

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
            _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
            _engine.GlobalContext,
            nameof(MassNavigationMoveOrderSourceSystem));
        if (!input.PressedThisFrame(bindings.CommandActionId) ||
            !AuthoritativeGroundPointerHelper.TryRead(input, out WorldCmInt2 worldCm))
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
        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity local ||
            !_engine.World.IsAlive(local))
        {
            local = ResolveSingleAuthoredPlayerOwner();
            _engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = local;
        }

        if (!_engine.World.TryGet(local, out PlayerOwner owner))
        {
            throw new InvalidOperationException("MassNavigation move order source LocalPlayerEntity must author PlayerOwner.");
        }

        return owner.PlayerId;
    }

    private Entity ResolveSingleAuthoredPlayerOwner()
    {
        Entity resolved = Entity.Null;
        int count = 0;
        _engine.World.Query(in AuthoredPlayerOwnerQuery, (Entity entity, ref PlayerOwner _) =>
        {
            resolved = entity;
            count++;
        });

        return count switch
        {
            1 => resolved,
            0 => throw new InvalidOperationException("MassNavigation move order source requires LocalPlayerEntity or exactly one authored PlayerOwner before submitting move orders."),
            _ => throw new InvalidOperationException("MassNavigation move order source found multiple PlayerOwner entities before LocalPlayerEntity was resolved; author one local player or bind CoreServiceKeys.LocalPlayerEntity explicitly.")
        };
    }
}
