using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Input.Selection;

namespace Ludots.Core.MassNavigation.Runtime;

public static class MassNavigationMoveOrderSubmitter
{
    public static MassNavigationMoveCommandResult SubmitViaOrderBuffer(
        MassNavigationSimulationRuntime simulation,
        World world,
        Dictionary<string, object> globals,
        OrderBufferSystem orderBufferSystem,
        OrderTypeRegistry orderTypeRegistry,
        Vector2 centerCm,
        int playerId)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(globals);
        ArgumentNullException.ThrowIfNull(orderBufferSystem);
        ArgumentNullException.ThrowIfNull(orderTypeRegistry);

        return SubmitCore(
            simulation,
            world,
            globals,
            orderBufferSystem,
            incomingOrders: null,
            orderTypeRegistry,
            centerCm,
            playerId);
    }

    public static MassNavigationMoveCommandResult SubmitViaOrderQueue(
        MassNavigationSimulationRuntime simulation,
        World world,
        Dictionary<string, object> globals,
        OrderQueue orderQueue,
        OrderTypeRegistry orderTypeRegistry,
        Vector2 centerCm,
        int playerId)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(globals);
        ArgumentNullException.ThrowIfNull(orderQueue);
        ArgumentNullException.ThrowIfNull(orderTypeRegistry);

        return SubmitCore(
            simulation,
            world,
            globals,
            orderBufferSystem: null,
            orderQueue,
            orderTypeRegistry,
            centerCm,
            playerId);
    }

    private static MassNavigationMoveCommandResult SubmitCore(
        MassNavigationSimulationRuntime simulation,
        World world,
        Dictionary<string, object> globals,
        OrderBufferSystem? orderBufferSystem,
        OrderQueue? incomingOrders,
        OrderTypeRegistry orderTypeRegistry,
        Vector2 centerCm,
        int playerId)
    {
        if (orderBufferSystem == null && incomingOrders == null)
        {
            throw new InvalidOperationException("MassNavigation move order submitter requires OrderBufferSystem or OrderQueue.");
        }

        if (!simulation.ContainsWorldPoint(centerCm.X, centerCm.Y))
        {
            simulation.RejectCommandOutsideWorld(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.OutsideWorld;
        }

        if (!SelectionContextRuntime.TryGetCurrentContainer(world, globals, out Entity selectionContainer))
        {
            throw new InvalidOperationException("MassNavigation runtime requires a current selection container before submitting move orders.");
        }

        int selectedCount = MassNavigationSelectionAccess.GetCurrentCount(world, globals);
        if (selectedCount <= 0)
        {
            simulation.RejectCommandWithoutSelection(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.EmptySelection;
        }

        Span<Entity> selectionScratch = simulation.EnsureSelectionScratch(selectedCount);
        int written = MassNavigationSelectionAccess.CopyCurrentSelection(world, globals, simulation, selectionScratch);
        ReadOnlySpan<Entity> selected = selectionScratch[..written];
        if (written <= 0)
        {
            simulation.RejectCommandWithoutSelection(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.EmptySelection;
        }

        if (!CanSubmitSelectionMoveOrders(world, selected, playerId))
        {
            simulation.RejectCommandUnauthorizedSelection(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.UnauthorizedSelection;
        }

        if (!orderTypeRegistry.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
        {
            throw new InvalidOperationException($"MassNavigation runtime requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}'.");
        }

        int sharedOrderId = simulation.AllocateSharedOrderId();
        float rotationRadians = simulation.NavGroupRuntime.SelectedRotationRadians;
        int submitted = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            Entity actor = selected[i];
            if (!world.IsAlive(actor))
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
                    simulation.FormationMode,
                    rotationRadians,
                    selectionContainer)
            };

            if (incomingOrders != null)
            {
                if (incomingOrders.TryEnqueue(in order))
                {
                    submitted++;
                }

                continue;
            }

            OrderSubmitResult result = orderBufferSystem!.SubmitOrder(actor, in order);
            if (IsAcceptedOrderSubmit(result))
            {
                submitted++;
            }
        }

        if (submitted <= 0)
        {
            simulation.RejectCommandOrderSubmit(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.OrderSubmitRejected;
        }

        simulation.FocusCommandTarget(centerCm, selected);
        simulation.MarkCommandApply();
        return MassNavigationMoveCommandResult.Submitted;
    }

    internal static bool CanSubmitSelectionMoveOrders(World world, ReadOnlySpan<Entity> selected, int localPlayerId)
    {
        int liveCommandableActors = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            Entity actor = selected[i];
            if (!world.IsAlive(actor))
            {
                continue;
            }

            if (!world.TryGet(actor, out PlayerOwner owner) ||
                owner.PlayerId != localPlayerId)
            {
                return false;
            }

            liveCommandableActors++;
        }

        return liveCommandableActors > 0;
    }

    private static bool IsAcceptedOrderSubmit(OrderSubmitResult result)
    {
        return result == OrderSubmitResult.Activated ||
               result == OrderSubmitResult.Queued;
    }
}
