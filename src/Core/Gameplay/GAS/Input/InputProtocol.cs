using Arch.Core;

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
        public int Count;
        public fixed int EntityIds[64];
        public fixed int WorldIds[64];
        public fixed int Versions[64];

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

