using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal static class RoadMoveActiveOrderResolver
    {
        public static bool TryResolve(World world, Entity entity, int roadMoveFollowOrderTypeId, out Order activeOrder)
        {
            activeOrder = default;
            if (roadMoveFollowOrderTypeId <= 0 ||
                !world.IsAlive(entity) ||
                !world.Has<OrderBuffer>(entity))
            {
                return false;
            }

            ref readonly OrderBuffer buffer = ref world.Get<OrderBuffer>(entity);
            if (!buffer.HasActive ||
                buffer.ActiveOrder.Order.OrderTypeId != roadMoveFollowOrderTypeId)
            {
                return false;
            }

            activeOrder = buffer.ActiveOrder.Order;
            return true;
        }

        public static bool OwnsRuntime(in Order activeOrder, in RoadMoveOrderRuntime runtime)
        {
            return runtime.ActiveOrderId == activeOrder.OrderId;
        }
    }
}
