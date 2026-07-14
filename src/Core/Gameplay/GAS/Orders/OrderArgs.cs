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
}
