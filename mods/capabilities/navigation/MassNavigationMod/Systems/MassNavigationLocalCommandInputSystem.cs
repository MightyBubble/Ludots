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
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationLocalCommandInputSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavigationSimulationRuntime _simulation;
    private int _moveOrderTypeId;

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
        if (!_simulation.ContainsWorldPoint(centerCm.X, centerCm.Y))
        {
            _simulation.RejectCommandOutsideWorld(centerCm.X, centerCm.Y);
            return;
        }

        ReadOnlySpan<Entity> selected = _simulation.SelectedEntities;
        if (selected.Length <= 0)
        {
            _simulation.RejectCommandWithoutSelection(centerCm.X, centerCm.Y);
            return;
        }

        int playerId = ResolveLocalPlayerId();
        if (!CanSubmitSelectionMoveOrders(selected, playerId))
        {
            _simulation.RejectCommandUnauthorizedSelection(centerCm.X, centerCm.Y);
            return;
        }

        SubmitSelectionMoveOrders(selected, centerCm, playerId);
    }

    internal void SubmitMoveCommandForTests(Vector2 centerCm)
    {
        EnqueueMoveCommand(centerCm);
    }

    private void SubmitSelectionMoveOrders(ReadOnlySpan<Entity> selected, Vector2 centerCm, int playerId)
    {
        if (_engine.GetService(CoreServiceKeys.OrderBufferSystem) is not OrderBufferSystem orderBufferSystem)
        {
            throw new InvalidOperationException("MassNavigationMod requires OrderBufferSystem for selection move commands.");
        }

        int moveOrderTypeId = ResolveMoveOrderType();
        if (!SelectionContextRuntime.TryGetCurrentContainer(_engine.World, _engine.GlobalContext, out Entity selectionContainer))
        {
            throw new InvalidOperationException("MassNavigationMod requires a current selection container before submitting move orders.");
        }

        int sharedOrderId = _simulation.AllocateSharedOrderId();
        int submitted = 0;
        float rotationRadians = _simulation.NavGroupRuntime.SelectedRotationRadians;
        for (int i = 0; i < selected.Length; i++)
        {
            Entity actor = selected[i];
            if (!_engine.World.IsAlive(actor))
            {
                continue;
            }

            var order = new Order
            {
                OrderId = sharedOrderId,
                OrderTypeId = moveOrderTypeId,
                PlayerId = playerId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = MassNavigationMoveOrderArgs.Encode(
                    centerCm,
                    _simulation.FormationMode,
                    rotationRadians,
                    selectionContainer)
            };

            OrderSubmitResult result = orderBufferSystem.SubmitOrder(actor, in order);
            if (IsAcceptedOrderSubmit(result))
            {
                submitted++;
            }
        }

        if (submitted <= 0)
        {
            _simulation.RejectCommandOrderSubmit(centerCm.X, centerCm.Y);
            return;
        }

        _simulation.FocusCommandTarget(centerCm, selected);
        _simulation.MarkCommandApply();
    }

    private static bool IsAcceptedOrderSubmit(OrderSubmitResult result)
    {
        return result == OrderSubmitResult.Activated ||
               result == OrderSubmitResult.Queued;
    }

    private bool CanSubmitSelectionMoveOrders(ReadOnlySpan<Entity> selected, int localPlayerId)
    {
        int liveCommandableActors = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            Entity actor = selected[i];
            if (!_engine.World.IsAlive(actor))
            {
                continue;
            }

            if (!CanLocalPlayerCommand(actor, localPlayerId))
            {
                return false;
            }

            liveCommandableActors++;
        }

        return liveCommandableActors > 0;
    }

    private bool CanLocalPlayerCommand(Entity actor, int localPlayerId)
    {
        return _engine.World.TryGet(actor, out PlayerOwner owner) &&
               owner.PlayerId == localPlayerId;
    }

    private int ResolveMoveOrderType()
    {
        if (_moveOrderTypeId > 0)
        {
            return _moveOrderTypeId;
        }

        if (_engine.GetService(CoreServiceKeys.OrderTypeRegistry) is not Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry registry ||
            !registry.TryGetId(MassNavigationOrderKeys.Move, out _moveOrderTypeId))
        {
            throw new InvalidOperationException($"MassNavigationMod requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}'.");
        }

        return _moveOrderTypeId;
    }

    private int ResolveLocalPlayerId()
    {
        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity local ||
            !_engine.World.IsAlive(local))
        {
            throw new InvalidOperationException("MassNavigationMod requires LocalPlayerEntity before submitting move orders.");
        }

        if (!_engine.World.TryGet(local, out PlayerOwner owner))
        {
            throw new InvalidOperationException("MassNavigationMod LocalPlayerEntity must author PlayerOwner.");
        }

        return owner.PlayerId;
    }
}

