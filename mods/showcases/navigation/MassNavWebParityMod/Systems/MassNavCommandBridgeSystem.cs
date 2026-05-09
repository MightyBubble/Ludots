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
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal sealed class MassNavCommandBridgeSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly MassNavSimulationRuntime _simulation;
    private int _moveOrderTypeId;

    public MassNavCommandBridgeSystem(GameEngine engine, MassNavSimulationRuntime simulation)
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
        if (!MassNavWebParityIds.IsCurrentPlaygroundMap(_engine))
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
            nameof(MassNavCommandBridgeSystem));
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

        _simulation.FocusCommandTarget(centerCm, selected);
        SubmitSelectionMoveOrders(selected, centerCm);
    }

    internal void SubmitMoveCommandForTests(Vector2 centerCm)
    {
        EnqueueMoveCommand(centerCm);
    }

    private void SubmitSelectionMoveOrders(ReadOnlySpan<Entity> selected, Vector2 centerCm)
    {
        if (_engine.GetService(CoreServiceKeys.OrderBufferSystem) is not OrderBufferSystem orderBufferSystem)
        {
            throw new InvalidOperationException("MassNavWebParityMod requires OrderBufferSystem for selection move commands.");
        }

        int moveOrderTypeId = ResolveMoveOrderType();
        int playerId = ResolveLocalPlayerId();
        SelectionContextRuntime.TryGetCurrentContainer(_engine.World, _engine.GlobalContext, out Entity selectionContainer);
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
                Args = new OrderArgs
                {
                    I0 = (int)_simulation.FormationMode,
                    F0 = rotationRadians,
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(centerCm.X, 0f, centerCm.Y),
                    },
                    Selection = new OrderSelectionReference
                    {
                        Container = selectionContainer
                    }
                }
            };

            if (orderBufferSystem.SubmitOrder(actor, in order) != OrderSubmitResult.InvalidEntity)
            {
                submitted++;
            }
        }

        if (submitted <= 0)
        {
            return;
        }

        _simulation.MarkCommandApply();
        _simulation.MarkStructuralChange();
    }

    private int ResolveMoveOrderType()
    {
        if (_moveOrderTypeId > 0)
        {
            return _moveOrderTypeId;
        }

        if (_engine.GetService(CoreServiceKeys.OrderTypeRegistry) is not Ludots.Core.Gameplay.GAS.Orders.OrderTypeRegistry registry ||
            !registry.TryGetId(MassNavOrderKeys.Move, out _moveOrderTypeId))
        {
            throw new InvalidOperationException($"MassNavWebParityMod requires GAS/order_types.json to define '{MassNavOrderKeys.Move}'.");
        }

        return _moveOrderTypeId;
    }

    private int ResolveLocalPlayerId()
    {
        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity local ||
            !_engine.World.IsAlive(local))
        {
            throw new InvalidOperationException("MassNavWebParityMod requires LocalPlayerEntity before submitting move orders.");
        }

        if (!_engine.World.TryGet(local, out PlayerOwner owner))
        {
            throw new InvalidOperationException("MassNavWebParityMod LocalPlayerEntity must author PlayerOwner.");
        }

        return owner.PlayerId;
    }
}
