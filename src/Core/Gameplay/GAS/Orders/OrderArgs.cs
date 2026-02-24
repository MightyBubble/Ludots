using System.Numerics;
using Arch.Core;
 
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
 
    public unsafe struct OrderSpatial
    {
        public const int MaxPoints = 64;
 
        public OrderSpatialKind Kind;
        public OrderCollectionMode Mode;
 
        public Vector3 WorldCm;
        public int A0;
        public int A1;
        public int A2;
 
        public int PointCount;
        public fixed int PointX[MaxPoints];
        public fixed int PointY[MaxPoints];
        public fixed int PointZ[MaxPoints];
 
        public void AddPointWorldCm(int x, int y, int z)
        {
            if (PointCount >= MaxPoints) return;
            fixed (int* px = PointX) px[PointCount] = x;
            fixed (int* py = PointY) py[PointCount] = y;
            fixed (int* pz = PointZ) pz[PointCount] = z;
            PointCount++;
        }
    }
 
    public unsafe struct OrderEntitySelection
    {
        public const int MaxEntities = 64;
 
        public int Count;
        public fixed int EntityIds[MaxEntities];
        public fixed int WorldIds[MaxEntities];
        public fixed int Versions[MaxEntities];
 
        public Entity GetEntity(int index)
        {
            if ((uint)index >= (uint)Count) return default;

            int id;
            fixed (int* ids = EntityIds) id = ids[index];
            int worldId;
            fixed (int* wids = WorldIds) worldId = wids[index];
            int version;
            fixed (int* vs = Versions) version = vs[index];
            return Ludots.Core.Gameplay.GAS.EntityUtil.Reconstruct(id, worldId, version);
        }
 
        public void Add(Entity entity)
        {
            if (Count >= MaxEntities) return;
            fixed (int* ids = EntityIds) ids[Count] = entity.Id;
            fixed (int* wids = WorldIds) wids[Count] = entity.WorldId;
            fixed (int* vs = Versions) vs[Count] = entity.Version;
            Count++;
        }
    }
 
    public struct OrderArgs
    {
        public int I0;
        public int I1;
        public int I2;
        public int I3;
 
        public float F0;
        public float F1;
        public float F2;
        public float F3;
 
        public OrderSpatial Spatial;
        public OrderEntitySelection Entities;
    }
}
