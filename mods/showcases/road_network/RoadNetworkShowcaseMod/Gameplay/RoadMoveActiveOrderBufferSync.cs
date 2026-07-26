using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal static class RoadMoveActiveOrderBufferSync
    {
        public static bool TryReplaceActive(World world, Entity entity, in Order order)
        {
            if (!world.IsAlive(entity) ||
                !world.Has<OrderBuffer>(entity))
            {
                OrderSpatialPayloadOps.Release(world, in order);
                return false;
            }

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(entity);
            if (!buffer.HasActive)
            {
                OrderSpatialPayloadOps.Release(world, in order);
                return false;
            }

            OrderSpatialPayloadOps.Release(world, in buffer.ActiveOrder.Order);
            buffer.ActiveOrder.Order = order;
            return true;
        }
    }
}
