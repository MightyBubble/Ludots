using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.EntityView;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.MassNavigation;

public static class MassNavigationMoveOrderSubmitter
{
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

        if (!simulation.ContainsWorldPoint(centerCm.X, centerCm.Y))
        {
            simulation.RejectCommandOutsideWorld(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.OutsideWorld;
        }

        EntityViewRuntimeConfig config = RequireEntityViewConfig(globals);
        if (!EntityViewRuntime.TryGetCommandSourceHandle(world, globals, config, out Entity collectionOwner, out EntityCollectionHandle collectionHandle))
        {
            simulation.RejectCommandWithoutSelection(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.EmptySelection;
        }

        int selectedCount = EntityViewRuntime.GetCommandSourceCount(world, globals, config);
        if (selectedCount <= 0)
        {
            simulation.RejectCommandWithoutSelection(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.EmptySelection;
        }

        Span<Entity> selectionScratch = simulation.EnsureSelectionScratch(selectedCount);
        int written = EntityViewRuntime.CopyCommandSourceEntities(world, globals, config, selectionScratch);
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
                    collectionOwner,
                    collectionHandle)
            };

            if (orderQueue.TryEnqueue(in order))
            {
                submitted++;
            }
        }

        if (submitted <= 0)
        {
            simulation.RejectCommandOrderSubmit(centerCm.X, centerCm.Y);
            return MassNavigationMoveCommandResult.OrderSubmitRejected;
        }

        simulation.StagePendingMoveOrderAcceptance(
            centerCm,
            selected,
            sharedOrderId,
            moveOrderTypeId);
        return MassNavigationMoveCommandResult.Submitted;
    }

    public static bool CanSubmitSelectionMoveOrders(World world, ReadOnlySpan<Entity> selected, int localPlayerId)
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

    private static EntityViewRuntimeConfig RequireEntityViewConfig(Dictionary<string, object> globals)
    {
        if (globals.TryGetValue(CoreServiceKeys.EntityViewConfig.Name, out object? configObj) &&
            configObj is EntityViewRuntimeConfig config)
        {
            return config;
        }

        throw new InvalidOperationException("MassNavigation move order submit requires EntityViewConfig.");
    }
}
