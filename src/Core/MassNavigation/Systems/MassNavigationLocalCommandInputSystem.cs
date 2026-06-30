using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationLocalCommandInputSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;

    public MassNavigationLocalCommandInputSystem(GameEngine engine, MassNavigationSimulationRuntime simulation)
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

        _simulation.ObserveCommandTick();

        if (_engine.GetService(CoreServiceKeys.UiCaptured) ||
            _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
            _engine.GlobalContext,
            nameof(MassNavigationLocalCommandInputSystem));
        if (!input.PressedThisFrame(bindings.CommandActionId) ||
            !AuthoritativeGroundPointerHelper.TryRead(input, out WorldCmInt2 worldCm))
        {
            return;
        }

        EnqueueMoveCommand(new Vector2(worldCm.X, worldCm.Y));
    }

    private void EnqueueMoveCommand(Vector2 centerCm)
    {
        _simulation.SubmitMoveCommand(
            _engine.World,
            _engine.GlobalContext,
            ResolveOrderBufferSystem(),
            ResolveOrderTypeRegistry(),
            centerCm,
            ResolveLocalPlayerId());
    }

    internal void SubmitMoveCommandForTests(Vector2 centerCm)
    {
        EnqueueMoveCommand(centerCm);
    }

    private OrderBufferSystem ResolveOrderBufferSystem()
    {
        if (_engine.GetService(CoreServiceKeys.OrderBufferSystem) is not OrderBufferSystem orderBufferSystem)
        {
            throw new InvalidOperationException("MassNavigation runtime requires OrderBufferSystem for selection move commands.");
        }

        return orderBufferSystem;
    }

    private Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry ResolveOrderTypeRegistry()
    {
        if (_engine.GetService(CoreServiceKeys.OrderTypeRegistry) is not Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry registry)
        {
            throw new InvalidOperationException($"MassNavigation runtime requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}'.");
        }

        return registry;
    }

    private int ResolveLocalPlayerId()
    {
        if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object? playerIdObj) &&
            playerIdObj is int playerId &&
            playerId > 0)
        {
            return playerId;
        }

        if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
            localObj is Entity local &&
            _engine.World.IsAlive(local) &&
            _engine.World.TryGet(local, out PlayerOwner owner))
        {
            return owner.PlayerId;
        }

        throw new InvalidOperationException(
            "MassNavigation runtime requires map launch context to publish LocalPlayerId before submitting move orders.");
    }
}
