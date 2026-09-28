using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.GAS.Input
{
    /// <summary>
    /// Common interface for request types that carry a RequestId.
    /// Used by the generic RingBuffer.
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
}
