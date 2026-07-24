using System;

namespace Ludots.Core.Networking.FixedInput
{
    /// <summary>
    /// Explicit outcomes for sampling one fixed-input payload.
    /// </summary>
    public enum FixedInputPayloadSampleStatus : byte
    {
        /// <summary>Exactly <c>destination.Length</c> bytes were written.</summary>
        Sampled = 0,
        /// <summary>Source could not produce a valid payload; the clock must not enqueue or advance the tick.</summary>
        Failed = 1,
    }

    /// <summary>
    /// Allocation-free fixed-input payload source. Implementations write into a caller-owned buffer
    /// and must not allocate, grow collections, or perform structural ECS changes on the hot path.
    /// </summary>
    public interface IFixedInputPayloadSource
    {
        /// <summary>
        /// Writes exactly one configured fixed-size payload for <paramref name="targetTick"/> into
        /// <paramref name="destination"/>. <paramref name="destination"/>.Length is the sole payload
        /// size contract for this sample. The source observes the exact selected tick so recording
        /// and replay remain deterministic.
        /// </summary>
        FixedInputPayloadSampleStatus TrySample(uint targetTick, Span<byte> destination);
    }
}
