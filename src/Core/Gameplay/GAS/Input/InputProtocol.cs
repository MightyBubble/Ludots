using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.GAS.Input
{
    /// <summary>
    /// Common interface for request/response types that carry a RequestId.
    /// Used by generic RingBuffer and SwapRemoveBuffer.
    /// </summary>
    public interface IHasRequestId
    {
        int RequestId { get; set; }
    }

    public struct InputRequest : IHasRequestId
    {
        public int RequestId { get; set; }
        public int RequestTagId;
        public Entity Source;
        public Entity Context;
        public int PayloadA;
        public int PayloadB;
    }

    public struct InputResponse : IHasRequestId
    {
        public int RequestId { get; set; }
        public int ResponseTagId;
        public Entity Source;
        public Entity Target;
        public Entity TargetContext;
        public int PayloadA;
        public int PayloadB;
    }

    public struct SelectionRequest : IHasRequestId
    {
        public int RequestId { get; set; }
        public int RequestTagId;
        public Entity Origin;
        public Entity TargetContext;
        public int PayloadA;
        public int PayloadB;
    }

    public unsafe struct SelectionResponse : IHasRequestId
    {
        public int RequestId { get; set; }
        public int ResponseTagId;
        public Entity TargetContext;
        public int WorldCmX;
        public int WorldCmY;
        public byte HasWorldPoint;
        public int Count;
        public fixed int EntityIds[64];
        public fixed int WorldIds[64];
        public fixed int Versions[64];

        public void SetEntity(int index, Entity entity)
        {
            if ((uint)index >= 64u)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index), index, "Selection response entity index must be within [0, 63].");
            }

            fixed (int* ids = EntityIds)
            fixed (int* worldIds = WorldIds)
            fixed (int* versions = Versions)
            {
                ids[index] = entity.Id;
                worldIds[index] = entity.WorldId;
                versions[index] = entity.Version;
            }
        }

        public void SetWorldPoint(in WorldCmInt2 worldCm)
        {
            WorldCmX = worldCm.X;
            WorldCmY = worldCm.Y;
            HasWorldPoint = 1;
        }

        public bool TryGetWorldPoint(out WorldCmInt2 worldCm)
        {
            if (HasWorldPoint == 0)
            {
                worldCm = default;
                return false;
            }

            worldCm = new WorldCmInt2(WorldCmX, WorldCmY);
            return true;
        }

        public Entity GetEntity(int index)
        {
            int id;
            fixed (int* ids = EntityIds) id = ids[index];
            int worldId;
            fixed (int* wids = WorldIds) worldId = wids[index];
            int version;
            fixed (int* vs = Versions) version = vs[index];
            return EntityUtil.Reconstruct(id, worldId, version);
        }
    }
}
