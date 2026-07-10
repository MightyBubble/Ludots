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
        public Entity Target;
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

}
