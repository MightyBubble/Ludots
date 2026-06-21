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
using Ludots.Core.MassCrowd.Runtime;

namespace Ludots.Core.MassCrowd.Systems;

internal sealed class MassNavigationLocalCommandInputSystem : ISystem<float>
{
    private static readonly QueryDescription AuthoredPlayerOwnerQuery = new QueryDescription().WithAll<PlayerOwner>();

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
        if (!MassNavigationIds.IsCurrentNavigationMap(_engine))
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
            throw new InvalidOperationException("MassCrowd runtime requires OrderBufferSystem for selection move commands.");
        }

        return orderBufferSystem;
    }

    private Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry ResolveOrderTypeRegistry()
    {
        if (_engine.GetService(CoreServiceKeys.OrderTypeRegistry) is not Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry registry)
        {
            throw new InvalidOperationException($"MassCrowd runtime requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}'.");
        }

        return registry;
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
            throw new InvalidOperationException("MassCrowd runtime LocalPlayerEntity must author PlayerOwner.");
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
            0 => throw new InvalidOperationException("MassCrowd runtime requires LocalPlayerEntity or exactly one authored PlayerOwner before submitting move orders."),
            _ => throw new InvalidOperationException("MassCrowd runtime found multiple PlayerOwner entities before LocalPlayerEntity was resolved; author one local player or bind CoreServiceKeys.LocalPlayerEntity explicitly.")
        };
    }
}
