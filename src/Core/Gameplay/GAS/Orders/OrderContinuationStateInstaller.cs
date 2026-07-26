using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public static class OrderContinuationStateInstaller
    {
        public const string MissingStateError = "GAS.ORDER_CONTINUATION.ERR.MissingState";

        public static void EnsureInstalled(World world, Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException($"{MissingStateError}: entity={entity.Id}, state=dead.");
            }

            if (!world.Has<OrderContinuationBuffer>(entity))
            {
                world.Add(entity, new OrderContinuationBuffer());
            }
        }

        public static void RequireInstalled(World world, Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException($"{MissingStateError}: entity={entity.Id}, state=dead.");
            }

            if (!world.Has<OrderContinuationBuffer>(entity))
            {
                throw new InvalidOperationException($"{MissingStateError}: entity={entity.Id}.");
            }
        }
    }
}
