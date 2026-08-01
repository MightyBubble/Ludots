using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
 
namespace Ludots.Core.Gameplay.GAS.Orders
{
    public enum OrderSpatialKind : byte
    {
        None = 0,
        WorldCm = 1,
        Grid = 2,
        Hex = 3,
        Abstract = 4
    }
 
    public enum OrderCollectionMode : byte
    {
        None = 0,
        Single = 1,
        List = 2,
        Set = 3
    }
 
    public struct OrderSpatial
    {
        public const int MaxPoints = 64;
        public const int MaxInlinePoints = 2;
 
        public OrderSpatialKind Kind;
        public OrderCollectionMode Mode;
 
        public Vector3 WorldCm;
        public int A0;
        public int A1;
        public int A2;
 
        public int PointCount;
        public Vector3 Point0WorldCm;
        public Vector3 Point1WorldCm;
        public OrderSpatialPayloadHandle Payload;

        public byte HasDestinationWorldCm;
        public Vector3 DestinationWorldCm;
 
        public void AddInlinePointWorldCm(int x, int y, int z)
        {
            Vector3 point = new(x, y, z);
            if (PointCount == 0)
            {
                Point0WorldCm = point;
            }
            else if (PointCount == 1)
            {
                Point1WorldCm = point;
            }
            else
            {
                throw new System.InvalidOperationException(
                    $"ORDER.SPATIAL.ERR.InlineCapacity: list has more than {MaxInlinePoints} points; author OrderSpatialPayloadBuffer for long paths.");
            }

            PointCount++;
        }
    }
 
    public struct OrderArgs
    {
        public static OrderArgs CreateSingleWorldCm(Vector3 worldCm)
        {
            return new OrderArgs
            {
                Spatial = new OrderSpatial
                {
                    Kind = OrderSpatialKind.WorldCm,
                    Mode = OrderCollectionMode.Single,
                    WorldCm = worldCm
                }
            };
        }

        public int I0;
        public int I1;
        public int I2;
        public int I3;
 
        public float F0;
        public float F1;
        public float F2;
        public float F3;

        public OrderSpatial Spatial;
    }

    public enum OrderIntArgSlot : byte
    {
        I0 = 0,
        I1 = 1,
        I2 = 2,
        I3 = 3
    }

    public static class OrderBuilder
    {
        public static Order Create(
            int orderTypeId,
            int playerId,
            Entity actor,
            Entity target,
            Entity targetContext,
            OrderSubmitMode submitMode,
            int submitStep)
        {
            if (orderTypeId <= 0)
            {
                throw new System.InvalidOperationException(
                    $"ORDER.BUILDER.ERR.InvalidOrderTypeId: orderTypeId={orderTypeId}.");
            }

            return new Order
            {
                OrderTypeId = orderTypeId,
                PlayerId = playerId,
                Actor = actor,
                Target = target,
                TargetContext = targetContext,
                Args = default,
                SubmitStep = submitStep,
                SubmitMode = submitMode
            };
        }

        public static void SetAbilitySlot(ref Order order, int abilitySlotIndex)
        {
            if (abilitySlotIndex < 0)
            {
                throw new System.InvalidOperationException(
                    $"ORDER.BUILDER.ERR.InvalidAbilitySlot: orderTypeId={order.OrderTypeId}, slot={abilitySlotIndex}.");
            }

            order.Args.I0 = abilitySlotIndex;
        }

        public static void SetIntArg(ref Order order, OrderIntArgSlot slot, int value)
        {
            switch (slot)
            {
                case OrderIntArgSlot.I0:
                    order.Args.I0 = value;
                    break;
                case OrderIntArgSlot.I1:
                    order.Args.I1 = value;
                    break;
                case OrderIntArgSlot.I2:
                    order.Args.I2 = value;
                    break;
                case OrderIntArgSlot.I3:
                    order.Args.I3 = value;
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(slot), slot, "Unknown order integer argument slot.");
            }
        }

        public static void SetSingleWorldCm(ref Order order, Vector3 worldCm)
        {
            order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            order.Args.Spatial.Mode = OrderCollectionMode.Single;
            order.Args.Spatial.WorldCm = worldCm;
        }
    }
}
