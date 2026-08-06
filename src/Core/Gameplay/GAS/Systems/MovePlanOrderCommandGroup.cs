using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Systems;

internal static class MovePlanOrderCommandGroup
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ResolveToken(in Order order)
    {
        if (order.AdmissionBatchId > 0)
        {
            if (order.AdmissionBatchSize <= 1)
            {
                throw new InvalidOperationException(
                    $"Move order {order.OrderId} has admission batch {order.AdmissionBatchId} with invalid size {order.AdmissionBatchSize}.");
            }

            return order.AdmissionBatchId;
        }

        if (order.AdmissionBatchSize != 0)
        {
            throw new InvalidOperationException(
                $"Move order {order.OrderId} has admission batch size {order.AdmissionBatchSize} without a batch id.");
        }

        return order.OrderId;
    }
}
