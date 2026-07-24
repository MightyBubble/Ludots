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
    /// Explicit outcomes for committing local prediction after a payload was accepted for send.
    /// </summary>
    public enum FixedInputPayloadCommitStatus : byte
    {
        /// <summary>Local prediction for the exact sent payload was committed.</summary>
        Committed = 0,
        /// <summary>
        /// Prediction commit failed. The clock must not invent prediction and must treat the step as failed.
        /// </summary>
        Failed = 1,
    }

    /// <summary>
    /// Allocation-free fixed-input payload source. Implementations write into a caller-owned buffer
    /// and must not allocate, grow collections, or perform structural ECS changes on the hot path.
    /// Sample prepares bytes; commit of local prediction is allowed only after enqueue and send pulse
    /// both accepted the exact payload.
    /// </summary>
    public interface IFixedInputPayloadSource
    {
        /// <summary>
        /// Writes exactly one configured fixed-size payload for <paramref name="targetTick"/> into
        /// <paramref name="destination"/>. <paramref name="destination"/>.Length is the sole payload
        /// size contract for this sample. The source observes the exact selected tick so recording
        /// and replay remain deterministic. Must not commit local prediction.
        /// </summary>
        FixedInputPayloadSampleStatus TrySample(uint targetTick, Span<byte> destination);

        /// <summary>
        /// Commits local prediction for the exact payload bytes that were accepted by the outbox and
        /// by transport or its bounded fixed send queue. Called only after both enqueue and pulse
        /// succeed. Multi-step catch-up invokes this once per emitted target tick, in order, with
        /// that tick's exact sent bytes.
        /// </summary>
        FixedInputPayloadCommitStatus TryCommit(uint targetTick, ReadOnlySpan<byte> sentPayload);
    }
}
